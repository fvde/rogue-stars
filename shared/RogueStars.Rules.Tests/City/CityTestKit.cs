using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.City;

/// <summary>Helpers for City workstream tests.</summary>
public static class Kit
{
    public const long Min = 60_000, Hour = 60 * Min;

    public static GameState Rich(GameEngine e, long seed = 42)
    {
        var s = TestHelpers.NewPlayer(e, seed);
        foreach (var c in new[] { Currencies.Ore, Currencies.Biomass, Currencies.Grain, Currencies.Diamonds })
            s.Wallet.Add(c, 10_000_000);
        return s;
    }

    public static void Grant(GameEngine e, GameState s, Reward r, long now = TestHelpers.T0)
    {
        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = now };
        ctx.Grant(r);
    }

    public static void GrantBlueprint(GameEngine e, GameState s, string id, long copies = 1) => Grant(e, s, new Reward(RewardKinds.Blueprint, id, copies));
    public static void GrantAlien(GameEngine e, GameState s, string id, long copies = 1) => Grant(e, s, new Reward(RewardKinds.Alien, id, copies));

    public static CommandResult Ok(GameEngine e, ref GameState s, GameCommand c, long now)
    {
        var r = e.Execute(ref s, c, now);
        Assert.True(r.Ok, r.Error);
        return r;
    }

    public static string Fail(GameEngine e, ref GameState s, GameCommand c, long now)
    {
        var r = e.Execute(ref s, c, now);
        Assert.False(r.Ok);
        return r.Error;
    }

    /// <summary>Grant (if needed), place and finish construction. Returns the completion time.</summary>
    public static long BuildActive(GameEngine e, ref GameState s, string bp, int x, int y, long now)
    {
        if (CitySystem.Owned(s, bp) == null) GrantBlueprint(e, s, bp);
        Ok(e, ref s, new CityBuild { BlueprintId = bp, X = x, Y = y }, now);
        var t = TimerSystem.Get(s, CitySystem.Owned(s, bp).TimerId);
        long done = t?.EndsMs ?? now;
        e.Settle(s, done);
        Assert.True(CitySystem.IsActive(CitySystem.Owned(s, bp)));
        return done;
    }

    /// <summary>Make an alien's genes neutral and traits empty for exact math.</summary>
    public static OwnedAlien Neutral(GameState s, string type)
    {
        var a = AlienSystem.Owned(s, type);
        a.Genes = new AlienGenes();
        a.Traits.Clear();
        return a;
    }

    /// <summary>Unlock every city area (test convenience).</summary>
    public static void UnlockAll(GameEngine e, GameState s)
    {
        foreach (var a in e.Content.CityAreas)
            if (!s.City.UnlockedAreas.Contains(a.Id)) s.City.UnlockedAreas.Add(a.Id);
    }
}
