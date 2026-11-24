using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Server.Tests;

/// <summary>
/// The pull system through the authoritative command endpoint (docs/design/pull-system.md): pulls report the visible pity
/// counters, blueprint / alien results are 50-shard batches, and the Shard Reactor's jobs are rule-checked, persisted and collected.
/// </summary>
public class PullSystemServerTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    private static bool Ok((int Status, System.Text.Json.JsonElement Body) r) => r.Status == 200 && r.Body.GetProperty("ok").GetBoolean();

    [Fact]
    public async Task SignalPull_ReportsTheVisibleCounters_AndBatchesOf50()
    {
        var p = await _app.PlayerAsync("puller");
        Assert.True(Ok(await p.CommandAsync("TestReactorSetup")));
        var r = await p.CommandAsync("GachaPull", new { pullType = 2, count = 10, payment = 1 }); // Alien, Diamonds
        Assert.True(Ok(r), r.Body.ToString());
        var pity = r.Body.GetProperty("payload").GetProperty("pity");
        Assert.Equal(100, pity.GetProperty("mythicPity").GetInt32());
        Assert.Equal(40, pity.GetProperty("legendaryPity").GetInt32());
        var state = await p.StateAsync();
        var counters = state.GetProperty("gacha").GetProperty("pity").GetProperty("Alien");
        Assert.Equal(pity.GetProperty("mythic").GetInt32(), counters.GetProperty("sinceMythic").GetInt32());
        foreach (var it in r.Body.GetProperty("payload").GetProperty("items").EnumerateArray())
            if (it.GetProperty("kind").GetString() == RewardKinds.Alien) Assert.Equal(50, it.GetProperty("shards").GetInt64());
        Assert.Equal(1, state.GetProperty("gacha").GetProperty("migrated").GetInt32());
    }

    [Fact]
    public async Task ShardReactor_StartIsValidated_TheJobPersists_SpeedUpAndCollect()
    {
        var p = await _app.PlayerAsync("reactor");
        Assert.True(Ok(await p.CommandAsync("TestReactorSetup")));
        // same rarity: refused, nothing paid
        var bad = await p.CommandAsync("ReactorStart", new { kind = 0, sourceId = "alien_blorp", targetId = "alien_mossling", units = 1 });
        Assert.False(bad.Body.GetProperty("ok").GetBoolean());
        Assert.Contains("higher rarity", bad.Body.GetProperty("error").GetString());
        Assert.Equal(5_000, await p.BalanceAsync(Currencies.AlienShards("alien_blorp")));

        var start = await p.CommandAsync("ReactorStart", new { kind = 0, sourceId = "alien_blorp", targetId = "alien_nebulisk", units = 1 });
        Assert.True(Ok(start), start.Body.ToString());
        Assert.Equal(5_000 - 2_560, await p.BalanceAsync(Currencies.AlienShards("alien_blorp")));
        // one working alien = one job
        var busy = await p.CommandAsync("ReactorStart", new { kind = 0, sourceId = "alien_blorp", targetId = "alien_nebulisk", units = 1 });
        Assert.Contains("busy", busy.Body.GetProperty("error").GetString());
        Assert.Contains("not finished", (await p.CommandAsync("ReactorCollect", new { jobId = start.Body.GetProperty("payload").GetString() })).Body.GetProperty("error").GetString());

        var state = await p.StateAsync();
        var job = state.GetProperty("reactor").GetProperty("jobs")[0];
        Assert.Equal("alien_nebulisk", job.GetProperty("targetId").GetString());
        Assert.True(Ok(await p.CommandAsync("TimerSpeedUp", new { timerId = job.GetProperty("timerId").GetString(), method = 0 })));
        var collect = await p.CommandAsync("ReactorCollect", new { });
        Assert.True(Ok(collect), collect.Body.ToString());
        Assert.Equal(10, collect.Body.GetProperty("payload").GetInt64());
        Assert.Equal(10, await p.BalanceAsync(Currencies.AlienShards("alien_nebulisk")));
        Assert.Equal(10, await p.CounterAsync("reactor_shards_out"));
    }
}
