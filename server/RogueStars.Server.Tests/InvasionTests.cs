using System.Net.Http.Json;
using RogueStars.Server.Invasions;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Tests;

public class InvasionTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    private async Task CreateInvasion(string id, long hpPerMember)
    {
        long now = _app.Time.GetUtcNow().ToUnixTimeMilliseconds();
        var resp = await _app.JobClient().PostAsJsonAsync("/internal/invasions",
            new { id, name = "Rust Swarm", typeId = "rust_swarm", startsMs = now - 1000, endsMs = now + 3_600_000, hpPerMember });
        Assert.Equal(200, (int)resp.StatusCode);
    }

    [Fact]
    public async Task Players_AreBatchedIntoGroupsOf20()
    {
        await CreateInvasion("inv1", 500);
        var groups = new Dictionary<string, int>();
        for (int i = 0; i < 25; i++)
        {
            var p = await _app.PlayerAsync("p" + i);
            var (s, body) = await p.PostAsync("/v1/invasions/inv1/join");
            Assert.Equal(200, s);
            var gid = body.GetProperty("groupId").GetString()!;
            groups[gid] = groups.GetValueOrDefault(gid) + 1;
        }
        Assert.Equal(20, groups["inv1_0"]);
        Assert.Equal(5, groups["inv1_1"]);
    }

    [Fact]
    public async Task Attack_RecordsDamage_GuildContribution_AndResolveAppliesOutcomes()
    {
        await CreateInvasion("inv2", 1500);
        var players = new List<PlayerClient>();
        for (int i = 0; i < 22; i++) players.Add(await _app.PlayerAsync("q" + i));

        // Group 0 (first 20): only 2 attack -> 2 * 1000 < 20 * 1500 -> overrun.
        // Group 1 (last 2): both attack twice -> 4000 >= 2 * 1500 -> repelled.
        foreach (var p in players) await p.PostAsync("/v1/invasions/inv2/join");
        var guildLeader = players[0];
        var (_, guild) = await guildLeader.PostAsync("/v1/guilds", new { name = "Defenders" });
        string guildId = guild.GetProperty("guild").GetProperty("id").GetString()!;

        var (s, atk) = await players[0].PostAsync("/v1/invasions/inv2/attack");
        Assert.Equal(200, s);
        Assert.Equal(1000, atk.GetProperty("damage").GetInt64());
        Assert.Equal(30_000, atk.GetProperty("groupHp").GetInt64());
        await players[1].PostAsync("/v1/invasions/inv2/attack");
        _app.Battles.Damage = 3000;
        await players[1].PostAsync("/v1/invasions/inv2/attack");
        _app.Battles.Damage = 1000;
        for (int r = 0; r < 2; r++)
        {
            await players[20].PostAsync("/v1/invasions/inv2/attack");
            await players[21].PostAsync("/v1/invasions/inv2/attack");
        }

        var (_, contributions) = await guildLeader.GetAsync($"/v1/guilds/{guildId}/contributions");
        Assert.Equal(1000, contributions.GetProperty("contributions").GetProperty("total").GetInt64());

        // Real InvasionRecordDamage: attempts consumed and contribution recorded in the rules state.
        Assert.Equal(2, await players[1].CounterAsync("invasion_attempts_used"));
        Assert.Equal(4000, await players[1].CounterAsync("invasion_damage"));
        Assert.True(await players[0].CounterAsync("guild_contribution") > 0); // real GuildSetMembership + score

        var (_, status) = await players[20].GetAsync("/v1/invasions/inv2/group");
        Assert.Equal(4000, status.GetProperty("groupDamage").GetInt64());

        // Not ended yet: resolve job does nothing.
        var early = await PlayerClient.Read(await _app.JobClient().PostAsync("/internal/jobs/invasion-resolve", null));
        Assert.Empty(early.GetProperty("result").EnumerateArray());

        _app.Time.Advance(TimeSpan.FromHours(2));
        var resp = await _app.JobClient().PostAsync("/internal/jobs/invasion-resolve", null);
        var report = (await PlayerClient.Read(resp)).GetProperty("result")[0];
        Assert.True(report.GetProperty("resolved").GetBoolean());
        Assert.Equal(22, report.GetProperty("applied").GetInt32());

        // Real LiveOpsApplyOutcome: lasting modifiers of the content invasion (rust_swarm) for the group outcome.
        static List<string> InvMods(List<System.Text.Json.JsonElement> mods) => mods
            .Where(m => m.GetProperty("source").GetString() == "invasion:rust_swarm")
            .Select(m => m.GetProperty("target").GetString() + "x" + m.GetProperty("multiplier").GetDouble().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(new[] { "production:orex0.667" }, InvMods(await players[1].ModifiersAsync()));
        Assert.Equal(1, await players[1].CounterAsync("invasion_top_contributor"));
        Assert.Single(await players[1].MailsAsync("Invasion top contributor"));
        Assert.Equal(new[] { "production:orex1.5", "run_reward:orex1.5" }, InvMods(await players[20].ModifiersAsync()));
        Assert.Equal(1, await players[20].CounterAsync("invasions_repelled"));
        Assert.Single(await players[21].MailsAsync("Invasion top contributor"));
        // Non-attackers get the outcome but no top-contributor mail.
        Assert.Equal(new[] { "production:orex0.667" }, InvMods(await players[10].ModifiersAsync()));
        Assert.Empty(await players[10].MailsAsync("Invasion top contributor"));

        // Idempotent re-run.
        var again = await PlayerClient.Read(await _app.JobClient().PostAsync("/internal/jobs/invasion-resolve", null));
        Assert.Empty(again.GetProperty("result").EnumerateArray());
        Assert.Equal(1, await players[1].CounterAsync("invasion_top_contributor"));
        Assert.Single(InvMods(await players[1].ModifiersAsync()));
        var inv = await _app.Service<IDocStore>().GetAsync<Invasion>(Collections.Invasions, "inv2");
        Assert.True(inv!.Value.Resolved);

        // Attacks after the end are rejected.
        Assert.Equal(409, (await players[0].PostAsync("/v1/invasions/inv2/attack")).Status);
    }

    [Fact]
    public async Task Resolve_WithoutOutcomeCommand_KeepsInvasionPending()
    {
        _app.DisableHook("InvasionOutcome");
        await CreateInvasion("inv3", 10);
        var p = await _app.PlayerAsync("solo");
        await p.PostAsync("/v1/invasions/inv3/attack");
        _app.Time.Advance(TimeSpan.FromHours(2));
        var report = (await PlayerClient.Read(await _app.JobClient().PostAsync("/internal/jobs/invasion-resolve", null))).GetProperty("result")[0];
        Assert.False(report.GetProperty("resolved").GetBoolean());
        Assert.Equal(1, report.GetProperty("pending").GetInt32());
    }

    /// <summary>Moves the clock into the next weekly Gloop Tide occurrence of the rules calendar (Saturday 01:00 UTC).</summary>
    private (string occurrence, string key) EnterWeeklyGloop()
    {
        var content = RogueStars.Rules.Content.ContentDb.Default;
        var ev = content.LiveOpsSchedule.Single(x => x.Id == "weekly_gloop_tide");
        Assert.True(ev.Schedule.TryNext(_app.Time.GetUtcNow().ToUnixTimeMilliseconds(), out long start, out _));
        _app.Time.Now = DateTimeOffset.FromUnixTimeMilliseconds(start + 3_600_000);
        string occ = RogueStars.Rules.Systems.LiveOpsSystem.OccurrenceId(ev.Id, start);
        return (occ, InvasionService.KeyFor(occ));
    }

    [Fact]
    public async Task CalendarOccurrences_AreMaterialisedOnce_KeyedByOccurrence()
    {
        var (occ, key) = EnterWeeklyGloop();
        Assert.Equal("weekly-gloop-tide-" + occ.Split('@')[1], key);
        var p = await _app.PlayerAsync("cal1");
        for (int i = 0; i < 2; i++)
        {
            var (s, body) = await p.GetAsync("/v1/invasions");
            Assert.Equal(200, s);
            var list = body.GetProperty("invasions").EnumerateArray().ToList();
            var inv = Assert.Single(list);
            Assert.Equal(key, inv.GetProperty("id").GetString());
            Assert.Equal(occ, inv.GetProperty("occurrenceId").GetString());
            Assert.Equal("gloop_tide", inv.GetProperty("typeId").GetString());
            Assert.Equal("Gloop Tide", inv.GetProperty("name").GetString());
            Assert.True(inv.GetProperty("scheduled").GetBoolean());
        }
        var job = (await PlayerClient.Read(await _app.JobClient().PostAsync("/internal/jobs/invasion-start", null))).GetProperty("result");
        Assert.Empty(job.GetProperty("created").EnumerateArray());
        Assert.Equal(key, job.GetProperty("active")[0].GetString());

        // Attacks are recorded in the player state under the rules occurrence id; the outcome uses it too.
        var (st, _) = await p.PostAsync($"/v1/invasions/{key}/attack");
        Assert.Equal(200, st);
        var contributions = (await p.StateAsync()).GetProperty("liveOps").GetProperty("contributions");
        Assert.Equal(1000, contributions.GetProperty(occ).GetProperty("damage").GetInt64());
        _app.Time.Advance(TimeSpan.FromDays(2));
        await _app.JobClient().PostAsync("/internal/jobs/invasion-resolve", null);
        var applied = (await p.StateAsync()).GetProperty("liveOps").GetProperty("appliedOutcomes").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains(occ, applied);
    }

    [Fact]
    public async Task InvasionStartJob_CreatesTheInstance_AndExtraInvasionsFollowTheCalendarRules()
    {
        var (_, key) = EnterWeeklyGloop();
        var job = (await PlayerClient.Read(await _app.JobClient().PostAsync("/internal/jobs/invasion-start", null))).GetProperty("result");
        Assert.Equal(key, job.GetProperty("created")[0].GetString());

        long now = _app.Time.GetUtcNow().ToUnixTimeMilliseconds();
        async Task<(int, System.Text.Json.JsonElement)> Create(string type) => await PlayerClient.ReadWithStatus(
            await _app.JobClient().PostAsJsonAsync("/internal/invasions", new { typeId = type, startsMs = now, endsMs = now + 3_600_000 }));
        var (s1, e1) = await Create("gloop_tide");
        Assert.Equal(409, s1);
        Assert.Equal("invasion_type_already_running", e1.GetProperty("error").GetString());
        var (s2, ok) = await Create("void_choir");
        Assert.Equal(200, s2);
        Assert.Equal("server_void_choir@" + now, ok.GetProperty("invasion").GetProperty("occurrenceId").GetString());
        Assert.Equal("server-void-choir-" + now, ok.GetProperty("invasion").GetProperty("id").GetString());
        var (s3, e3) = await Create("prism_horde");
        Assert.Equal(409, s3);
        Assert.Equal("too_many_invasions", e3.GetProperty("error").GetString());

        var p = await _app.PlayerAsync("cal2");
        var (_, body) = await p.GetAsync("/v1/invasions");
        Assert.Equal(2, body.GetProperty("invasions").GetArrayLength());
    }
}
