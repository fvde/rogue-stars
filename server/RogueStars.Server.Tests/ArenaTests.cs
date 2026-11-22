using RogueStars.Rules.Core;
using RogueStars.Server.Arena;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Tests;

public class ArenaTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Groups_HoldAtMost150_ThenOpenANewGroup()
    {
        _ = _app.CreateClient();
        var arena = _app.Service<ArenaService>();
        var groups = new Dictionary<string, int>();
        for (int i = 0; i < 151; i++)
        {
            var entry = await arena.EnsureEntryAsync("p" + i, new CombatLoadout { PlayerId = "p" + i, DisplayName = "P" + i }, default);
            groups[entry.GroupId!] = groups.GetValueOrDefault(entry.GroupId!) + 1;
        }
        Assert.Equal(2, groups.Count);
        Assert.Equal(150, groups["2026-W47_L0_0"]);
        Assert.Equal(1, groups["2026-W47_L0_1"]);

        // Re-entering the same week keeps the assignment.
        var again = await arena.EnsureEntryAsync("p3", new CombatLoadout { PlayerId = "p3" }, default);
        Assert.Equal("2026-W47_L0_0", again.GroupId);
        var docs = _app.Service<IDocStore>();
        Assert.Equal(150, (await docs.GetAsync<ArenaGroup>(Collections.ArenaGroups, "2026-W47_L0_0"))!.Value.Members.Count);
    }

    [Fact]
    public void OpponentPicker_ChoosesClosestInPoints_NeverSelf()
    {
        var members = new List<ArenaMember>
        {
            new() { PlayerId = "me", Points = 100 },
            new() { PlayerId = "far", Points = 900 },
            new() { PlayerId = "near", Points = 120 },
            new() { PlayerId = "mid", Points = 300 },
        };
        for (int seed = 0; seed < 20; seed++)
            Assert.Equal("near", OpponentPicker.Pick(members, "me", 100, 1, Rng.FromSeed(seed))!.PlayerId);
        var two = OpponentPicker.Candidates(members, "me", 100, 2).Select(m => m.PlayerId).ToList();
        Assert.Equal(new[] { "near", "mid" }, two);
        Assert.Null(OpponentPicker.Pick(new List<ArenaMember> { members[0] }, "me", 100, 3, Rng.FromSeed(1)));
    }

    [Fact]
    public async Task Fight_UpdatesPoints_Leaderboard_AndRunsFightHook()
    {
        var a = await _app.PlayerAsync("a", "Alpha");
        var b = await _app.PlayerAsync("b", "Beta");

        // b fights first: alone in its group, falls back to any snapshot -> none besides itself yet.
        var lonely = await b.PostAsync("/v1/arena/fight");
        Assert.Equal(409, lonely.Status);
        Assert.Equal("no_opponents", lonely.Body.GetProperty("error").GetString());

        var (status, fight) = await a.PostAsync("/v1/arena/fight");
        Assert.Equal(200, status);
        Assert.True(fight.GetProperty("won").GetBoolean());
        Assert.Equal("dev_b", fight.GetProperty("opponent").GetProperty("playerId").GetString());
        // Points come from the rules (ArenaSystem, Elo from StartPoints 1000 vs an equal opponent: +20).
        Assert.Equal(1020, fight.GetProperty("points").GetInt64());
        Assert.Equal(20, fight.GetProperty("pointsDelta").GetInt64());
        Assert.Equal(1020, (await a.StateAsync()).GetProperty("arena").GetProperty("points").GetInt64());
        // Real rules hooks: ArenaStartFight consumed a ticket, ArenaFightResult recorded the win.
        Assert.Equal(1, await a.CounterAsync("arena_fights"));
        Assert.Equal(1, await a.CounterAsync("arena_wins"));
        Assert.Equal(RogueStars.Rules.Content.ContentDb.Default.Arena.StartTickets - 1, await a.BalanceAsync(Currencies.ArenaTickets));
        Assert.Equal(1, (await a.StateAsync()).GetProperty("arena").GetProperty("weekWins").GetInt32());

        var (_, board) = await a.GetAsync("/v1/arena/leaderboard");
        Assert.Equal(1, board.GetProperty("myRank").GetInt32());
        Assert.Equal(1020, board.GetProperty("entries")[0].GetProperty("points").GetInt64());
        Assert.Equal(150, board.GetProperty("groupSize").GetInt32());
        var entries = board.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal("dev_a", entries[0].GetProperty("playerId").GetString());

        var (_, opps) = await a.GetAsync("/v1/arena/opponents");
        Assert.Equal("dev_b", opps.GetProperty("opponents")[0].GetProperty("playerId").GetString());

        // Explicit opponent must be in the group.
        Assert.Equal(400, (await a.PostAsync("/v1/arena/fight", new { opponentId = "dev_nobody" })).Status);
    }

    [Fact]
    public async Task Fight_RejectedByRulesHook_DoesNotChangePoints()
    {
        _app.ConfigureEngine = e => e.RegisterNewPlayer(ctx =>
        {
            if (ctx.State.PlayerId == "dev_blocked") ctx.State.Wallet.Balances[Currencies.ArenaTickets] = 0;
        });
        var other = await _app.PlayerAsync("other");
        await other.PostAsync("/v1/arena/snapshot");
        var p = await _app.PlayerAsync("blocked");
        var (status, body) = await p.PostAsync("/v1/arena/fight");
        Assert.Equal(409, status);
        Assert.Equal("no arena tickets", body.GetProperty("error").GetString());
        var (_, board) = await p.GetAsync("/v1/arena/leaderboard");
        Assert.Equal(RogueStars.Rules.Content.ContentDb.Default.Arena.StartPoints, board.GetProperty("entries")[0].GetProperty("points").GetInt64());
        Assert.Equal(0, await p.CounterAsync("arena_fights"));
    }

    [Fact]
    public async Task Fight_LockedArenaFeature_IsRejected()
    {
        _app.DevUnlockAll = false;
        var other = await _app.PlayerAsync("other");
        await other.PostAsync("/v1/arena/snapshot");
        var p = await _app.PlayerAsync("newbie");
        var (status, body) = await p.PostAsync("/v1/arena/fight");
        Assert.Equal(409, status);
        Assert.Equal("feature locked", body.GetProperty("error").GetString());
        Assert.Equal(RogueStars.Rules.Content.ContentDb.Default.Arena.StartTickets, await p.BalanceAsync(Currencies.ArenaTickets));
    }

    [Fact]
    public async Task WeeklyJob_MailsRankRewards_PromotesAndIsIdempotent()
    {
        _app.Settings["RogueStars:Arena:PromoteTop"] = "1";
        _app.Settings["RogueStars:Arena:DemoteBottom"] = "1";
        var a = await _app.PlayerAsync("a");
        var b = await _app.PlayerAsync("b");
        var c = await _app.PlayerAsync("c");
        await b.PostAsync("/v1/arena/snapshot");
        await c.PostAsync("/v1/arena/snapshot");
        await a.PostAsync("/v1/arena/fight");
        await a.PostAsync("/v1/arena/fight");
        await b.PostAsync("/v1/arena/fight");
        await c.PostAsync("/v1/arena/fight");

        _app.Time.Advance(TimeSpan.FromDays(5)); // Monday of W48
        var jobs = _app.JobClient();
        var resp = await jobs.PostAsync("/internal/jobs/weekly-arena", null);
        Assert.Equal(200, (int)resp.StatusCode);
        var body = await PlayerClient.Read(resp);
        Assert.Equal("2026-W47", body.GetProperty("result").GetProperty("week").GetString());
        Assert.Equal(3, body.GetProperty("result").GetProperty("players").GetInt32());

        // Real MailPush: the rank reward sits in the inbox until claimed.
        var mails = await a.MailsAsync("Arena week 2026-W47");
        Assert.Single(mails);
        var reward = mails[0].GetProperty("rewards")[0];
        Assert.Equal(Currencies.Diamonds, reward.GetProperty("id").GetString());
        Assert.Equal(500, reward.GetProperty("amount").GetInt64());
        Assert.Single(await c.MailsAsync("Arena week 2026-W47"));
        long diamonds = await a.BalanceAsync(Currencies.Diamonds);
        var (_, claim) = await a.CommandAsync("MailClaim", new { mailId = mails[0].GetProperty("id").GetString() });
        Assert.True(claim.GetProperty("ok").GetBoolean());
        Assert.Equal(diamonds + 500, await a.BalanceAsync(Currencies.Diamonds));

        var docs = _app.Service<IDocStore>();
        Assert.Equal(1, (await docs.GetAsync<ArenaPlayer>(Collections.ArenaPlayers, "dev_a"))!.Value.League);

        // Re-running does not mail twice.
        await jobs.PostAsync("/internal/jobs/weekly-arena?week=2026-W47", null);
        Assert.Single(await a.MailsAsync("Arena week 2026-W47"));
        Assert.Single(await c.MailsAsync("Arena week 2026-W47"));

        // Next week the promoted player is placed in league 1.
        await b.PostAsync("/v1/arena/snapshot");
        var (_, fight) = await a.PostAsync("/v1/arena/fight");
        var (_, board) = await a.GetAsync("/v1/arena/leaderboard");
        Assert.Equal(1, board.GetProperty("league").GetInt32());
        Assert.StartsWith("2026-W48_L1", board.GetProperty("groupId").GetString());
    }

    [Fact]
    public async Task WeeklyJob_QueuesMail_WhenMailCommandMissing_AndDailyJobDelivers()
    {
        _app.DisableHook("MailDeliver");
        var a = await _app.PlayerAsync("a");
        var b = await _app.PlayerAsync("b");
        await b.PostAsync("/v1/arena/snapshot");
        await a.PostAsync("/v1/arena/fight");
        _app.Time.Advance(TimeSpan.FromDays(7));
        await _app.JobClient().PostAsync("/internal/jobs/weekly-arena", null);
        var outbox = await _app.Service<IDocStore>().GetAsync<RogueStars.Server.Commands.MailOutbox>(Collections.MailOutbox, "dev_a");
        Assert.Single(outbox!.Value.Items);

        // Mail command becomes available (e.g. after a deploy): daily job flushes the outbox.
        Assert.Empty(await a.MailsAsync("Arena week 2026-W47"));
        _app.Service<Microsoft.Extensions.Options.IOptions<RogueStars.Server.Infrastructure.ServerOptions>>().Value.Commands.MailDeliver = "MailPush";
        var resp = await _app.JobClient().PostAsync("/internal/jobs/daily-reset", null);
        Assert.Equal(200, (int)resp.StatusCode);
        Assert.Single(await a.MailsAsync("Arena week 2026-W47"));
        Assert.Null(await _app.Service<IDocStore>().GetAsync<RogueStars.Server.Commands.MailOutbox>(Collections.MailOutbox, "dev_a"));
    }
}
