using System.Net.Http.Json;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Server.Arena;

namespace RogueStars.Server.Tests;

/// <summary>The production IBattleResolver is the rules BattleSimulator (arena duel, invasion boss pool).</summary>
public class BattleSimulatorResolverTests : IDisposable
{
    private readonly TestApp _app = new() { RealBattles = true };

    public void Dispose() => _app.Dispose();

    private static CombatLoadout Loadout(string id, double hp, double atk) => new()
    {
        PlayerId = id, DisplayName = id,
        WormStats = new StatBlock().AddAll(new[] { new StatMod(StatId.Hp, hp), new StatMod(StatId.Atk, atk), new StatMod(StatId.Def, 10), new StatMod(StatId.Speed, 100) }),
    };

    [Fact]
    public void Resolver_IsTheSimulator_DeterministicPerSeed()
    {
        _ = _app.CreateClient();
        var resolver = _app.Service<IBattleResolver>();
        Assert.IsType<SimulatorBattleResolver>(resolver);

        var strong = Loadout("s", 50_000, 2_000);
        var weak = Loadout("w", 500, 20);
        Assert.True(resolver.ResolveDuel(strong, weak, 7).AttackerWon);
        Assert.False(resolver.ResolveDuel(weak, strong, 7).AttackerWon);
        var a = resolver.ResolveDuel(Loadout("a", 3000, 120), Loadout("b", 3000, 120), 99);
        var b = resolver.ResolveDuel(Loadout("a", 3000, 120), Loadout("b", 3000, 120), 99);
        Assert.Equal(a.AttackerWon, b.AttackerWon);
        Assert.Equal(a.Damage, b.Damage);

        // Invasion: damage is the score, bounded by the remaining HP pool; a tiny pool is depleted (win).
        var hit = resolver.ResolveInvasionAttack(strong, "rust_swarm", 1000, 1_000_000_000, 5);
        Assert.InRange(hit.Damage, 1, 1_000_000_000);
        Assert.False(hit.AttackerWon);
        Assert.Equal(hit.Damage, resolver.ResolveInvasionAttack(strong, "rust_swarm", 1000, 1_000_000_000, 5).Damage);
        var finish = resolver.ResolveInvasionAttack(strong, "rust_swarm", 1000, 10, 5);
        Assert.True(finish.AttackerWon);
        Assert.True(finish.Damage >= 10);
        foreach (var inv in ContentDb.Default.Invasions.Values)
            Assert.True(resolver.ResolveInvasionAttack(weak, inv.Id, 1000, 1_000_000, 1).Damage >= 0);
    }

    [Fact]
    public async Task ArenaAndInvasion_EndToEnd_WithRealBattles()
    {
        var a = await _app.PlayerAsync("a");
        var b = await _app.PlayerAsync("b");
        await b.PostAsync("/v1/arena/snapshot");
        var (status, fight) = await a.PostAsync("/v1/arena/fight");
        Assert.Equal(200, status);
        Assert.Equal(1, await a.CounterAsync("arena_fights"));
        Assert.True(fight.GetProperty("log").GetProperty("rounds").GetInt32() > 0);

        long now = _app.Time.GetUtcNow().ToUnixTimeMilliseconds();
        Assert.Equal(400, (int)(await _app.JobClient().PostAsJsonAsync("/internal/invasions",
            new { id = "realinv", name = "X", typeId = "no_such_type", startsMs = now - 1000, endsMs = now + 3_600_000 })).StatusCode);
        Assert.Equal(200, (int)(await _app.JobClient().PostAsJsonAsync("/internal/invasions",
            new { id = "realinv", name = "Gloop", typeId = "gloop_tide", startsMs = now - 1000, endsMs = now + 3_600_000 })).StatusCode);
        var (s2, atk) = await a.PostAsync("/v1/invasions/realinv/attack");
        Assert.Equal(200, s2);
        long dmg = atk.GetProperty("damage").GetInt64();
        Assert.True(dmg > 0);
        Assert.Equal(dmg, await a.CounterAsync("invasion_damage"));
    }
}
