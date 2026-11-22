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
            new { id, name = "Grey Swarm", startsMs = now - 1000, endsMs = now + 3_600_000, hpPerMember });
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

        var s1 = await players[1].StateAsync();
        Assert.Contains("invasion:inv2:overrun", s1.GetProperty("flags").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(1, await players[1].CounterAsync("invasion_rank"));
        Assert.Equal(1, await players[1].CounterAsync("mail_invasion_top"));
        var s20 = await players[20].StateAsync();
        Assert.Contains("invasion:inv2:repelled", s20.GetProperty("flags").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(1, await players[21].CounterAsync("mail_invasion_top"));
        // Non-attackers get the outcome but no top-contributor mail.
        Assert.Contains("invasion:inv2:overrun", (await players[10].StateAsync()).GetProperty("flags").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(0, await players[10].CounterAsync("mail_invasion_top"));

        // Idempotent re-run.
        var again = await PlayerClient.Read(await _app.JobClient().PostAsync("/internal/jobs/invasion-resolve", null));
        Assert.Empty(again.GetProperty("result").EnumerateArray());
        Assert.Equal(1, await players[1].CounterAsync("invasion_rank"));
        var inv = await _app.Service<IDocStore>().GetAsync<Invasion>(Collections.Invasions, "inv2");
        Assert.True(inv!.Value.Resolved);

        // Attacks after the end are rejected.
        Assert.Equal(409, (await players[0].PostAsync("/v1/invasions/inv2/attack")).Status);
    }

    [Fact]
    public async Task Resolve_WithoutOutcomeCommand_KeepsInvasionPending()
    {
        _app.WithHooks = false;
        await CreateInvasion("inv3", 10);
        var p = await _app.PlayerAsync("solo");
        await p.PostAsync("/v1/invasions/inv3/attack");
        _app.Time.Advance(TimeSpan.FromHours(2));
        var report = (await PlayerClient.Read(await _app.JobClient().PostAsync("/internal/jobs/invasion-resolve", null))).GetProperty("result")[0];
        Assert.False(report.GetProperty("resolved").GetBoolean());
        Assert.Equal(1, report.GetProperty("pending").GetInt32());
    }
}
