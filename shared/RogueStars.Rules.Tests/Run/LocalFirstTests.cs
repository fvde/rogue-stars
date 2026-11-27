using System.Text.Json;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Local-first commands and offline battles (docs/server.md): the state hash both sides compare, the battle resolver the
/// server uses for claimed results, and the cross-runtime battle determinism probe.
/// </summary>
public class LocalFirstTests
{
    /// <summary>The golden fingerprint shared with the Unity EditMode suite (client runtime).</summary>
    public static string GoldenPath => Path.Combine(RepoRoot(), "shared", "RogueStars.Rules.Tests", "Golden", "battle-determinism.txt");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RogueStars.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static GameState PlayedState(out GameEngine e)
    {
        e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        for (int i = 0; i < 40 && s.Run.Active != null && s.Run.Active.BattleCount < 2; i++)
            Assert.True(RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true }).Ok);
        return s;
    }

    [Fact]
    public void StateHash_SurvivesJsonRoundTrips_AndSeesEveryChange()
    {
        var s = PlayedState(out _);
        string h = StateHash.Hex(s);
        Assert.Equal(h, StateHash.Hex(TestHelpers.Clone(s)));
        // wire format (camelCase, web defaults) as the server sends it
        var wire = new JsonSerializerOptions(JsonSerializerDefaults.Web) { IncludeFields = true };
        Assert.Equal(h, StateHash.Hex(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(s, wire), wire)!));

        var changed = TestHelpers.Clone(s);
        changed.Wallet.Add(Currencies.Ore, 1);
        Assert.NotEqual(h, StateHash.Hex(changed));
        var rng = TestHelpers.Clone(s);
        rng.Rng.NextULong();
        Assert.NotEqual(h, StateHash.Hex(rng));
    }

    [Fact]
    public void StateHash_IgnoresDictionaryOrder_NullVsEmpty_AndTheLocalBattleLog()
    {
        var a = new GameState();
        var b = new GameState();
        a.Counters["x"] = 1; a.Counters["y"] = 2;
        b.Counters["y"] = 2; b.Counters["x"] = 1;
        Assert.Equal(StateHash.Of(a), StateHash.Of(b));
        b.Flags = null!;
        Assert.Equal(StateHash.Of(a), StateHash.Of(b));

        var s = PlayedState(out _);
        Assert.NotNull(s.Run.Active!.LastBattle);
        Assert.Empty(s.Run.Active.LastBattle!.Log); // the state keeps no log
        Assert.NotEmpty(s.Run.Active.LastBattleForPlayback().Log); // the device that fought it keeps it for playback
        Assert.Equal(StateHash.Hex(s), StateHash.Hex(TestHelpers.Clone(s))); // and it is not part of the state
    }

    [Fact]
    public void ServerTakingTheClientsClaim_EndsInTheSameState_AsSimulating()
    {
        var e = RunTestKit.Engine();
        var start = RunTestKit.StartRun(e);
        var client = TestHelpers.Clone(start);
        var server = TestHelpers.Clone(start);
        int battles = 0, claimsUsed = 0;
        for (int i = 0; i < 60 && client.Run.Active != null && battles < 3; i++)
        {
            var cmd = new RunAutoStep { Farming = true };
            var local = e.Execute(ref client, cmd, TestHelpers.T0);
            Assert.True(local.Ok, local.Error);
            var claims = local.Battles.Select(b => b.Result.Summary()).ToList();
            battles += claims.Count;
            // the server takes the claims (no re-simulation)
            var remote = e.Execute(ref server, cmd, TestHelpers.T0, (ctx, setup, rng, kind, index) =>
            {
                claimsUsed++;
                return claims[index];
            });
            Assert.True(remote.Ok, remote.Error);
            Assert.Equal(StateHash.Hex(client), StateHash.Hex(server));
        }
        Assert.True(battles >= 1 && claimsUsed == battles);
    }

    [Fact]
    public void BattleTiers_StandardEliteBoss()
    {
        Assert.Equal(BattleTier.Boss, FoughtBattle.TierOf("boss"));
        Assert.Equal(BattleTier.Elite, FoughtBattle.TierOf("elite"));
        Assert.Equal(BattleTier.Elite, FoughtBattle.TierOf("miniboss"));
        Assert.Equal(BattleTier.Elite, FoughtBattle.TierOf("ambush"));
        Assert.Equal(BattleTier.Standard, FoughtBattle.TierOf("enemy"));
        Assert.Equal(BattleTier.Standard, FoughtBattle.TierOf("event"));
        Assert.Equal(BattleTier.Standard, FoughtBattle.TierOf("quest"));
    }

    [Fact]
    public void Battles_AreDeterministic_AndMatchTheGoldenShared_WithTheClientSuite()
    {
        var c = ContentDb.Default;
        ulong a = BattleDeterminism.Fingerprint(c);
        Assert.Equal(a, BattleDeterminism.Fingerprint(new ContentDb())); // fresh content, same result
        // the probe exercises real fights: both winners, several rounds, logs
        var results = BattleDeterminism.Scenarios(c).Select(s => BattleSimulator.Simulate(s.Key, c, Rng.FromSeed(s.Value))).ToList();
        Assert.Contains(results, r => r.PlayerWon());
        Assert.Contains(results, r => !r.PlayerWon());
        Assert.All(results, r => Assert.NotEmpty(r.Log));
        // same inputs after a JSON round trip of the setup (what a server re-simulation sees) → identical log
        foreach (var s in BattleDeterminism.Scenarios(c).Take(8))
            Assert.Equal(StateHash.Hex(BattleSimulator.Simulate(s.Key, c, Rng.FromSeed(s.Value))),
                StateHash.Hex(BattleSimulator.Simulate(TestHelpers.Clone(s.Key), c, Rng.FromSeed(s.Value))));

        string golden = File.ReadAllText(GoldenPath).Trim();
        Assert.True(golden == a.ToString("x16"),
            "battle determinism golden changed: " + a.ToString("x16") + " (was " + golden + "). If content or battle rules changed on purpose, " +
            "write the new value to " + GoldenPath + " (the Unity EditMode suite checks the client runtime against it).");
    }
}
