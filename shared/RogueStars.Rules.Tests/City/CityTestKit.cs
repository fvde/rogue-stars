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

    /// <summary>New player with the starting wallet's city resources removed (exact cost math).</summary>
    public static GameState Broke(GameEngine e, long seed = 42)
    {
        var s = TestHelpers.NewPlayer(e, seed);
        foreach (var c in new[] { Currencies.Ore, Currencies.Biomass, Currencies.Grain, Currencies.Diamonds })
            s.Wallet.Balances.Remove(c);
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

    /// <summary>
    /// Grant (if needed), place a new copy at the planner's spot (CityPlanner: paves streets when needed) and finish construction.
    /// Returns the completion time; <paramref name="uid"/> is the new copy.
    /// </summary>
    public static long BuildActive(GameEngine e, ref GameState s, string bp, long now, out string uid)
    {
        if (CitySystem.Owned(s, bp) == null) GrantBlueprint(e, s, bp);
        var plan = CityPlanner.Find(s, e.Content, bp);
        Assert.NotNull(plan);
        var r = CityPlanner.Apply(e, ref s, plan, bp, null, now);
        Assert.True(r.Ok, r.Error);
        uid = (string)r.Payload;
        var t = TimerSystem.Get(s, CitySystem.Building(s, uid).TimerId);
        long done = t?.EndsMs ?? now;
        e.Settle(s, done);
        Assert.True(CitySystem.IsBuilt(CitySystem.Building(s, uid)));
        return done;
    }

    public static long BuildActive(GameEngine e, ref GameState s, string bp, long now) => BuildActive(e, ref s, bp, now, out _);

    /// <summary>Grant (if needed), place a new copy at exactly (x, y) (no streets paved) and finish construction. Returns the completion time.</summary>
    public static long BuildAt(GameEngine e, ref GameState s, string bp, int x, int y, long now, out string uid, bool rotated = false)
    {
        if (CitySystem.Owned(s, bp) == null) GrantBlueprint(e, s, bp);
        uid = (string)Ok(e, ref s, new CityPlace { BlueprintId = bp, X = x, Y = y, Rotated = rotated }, now).Payload;
        var t = TimerSystem.Get(s, CitySystem.Building(s, uid).TimerId);
        long done = t?.EndsMs ?? now;
        e.Settle(s, done);
        Assert.True(CitySystem.IsBuilt(CitySystem.Building(s, uid)));
        return done;
    }

    /// <summary>Cell index of (x, y).</summary>
    public static int Cell(GameEngine e, int x, int y) => CityLayout.Cell(e.Content, x, y);

    /// <summary>Paves streets (one stroke) at the given (x, y) pairs.</summary>
    public static void Streets(GameEngine e, ref GameState s, long now, params (int x, int y)[] cells)
    {
        var cmd = new CityStreets();
        foreach (var (x, y) in cells) cmd.Adds.Add(Cell(e, x, y));
        Ok(e, ref s, cmd, now);
    }

    /// <summary>The first copy of a blueprint.</summary>
    public static CityBuilding B(GameState s, string bp) => CitySystem.Copies(s, bp).Count > 0 ? CitySystem.Copies(s, bp)[0] : null;

    /// <summary>Grants an alien (if needed) and puts it to work in a building (it then runs).</summary>
    public static void Staff(GameEngine e, ref GameState s, string uid, string alien, long now)
    {
        if (AlienSystem.Owned(s, alien) == null) GrantAlien(e, s, alien);
        Ok(e, ref s, new AlienAssign { AlienTypeId = alien, BuildingUid = uid }, now);
    }

    /// <summary>Make an alien's genes neutral and traits empty for exact math.</summary>
    public static OwnedAlien Neutral(GameState s, string type)
    {
        var a = AlienSystem.Owned(s, type);
        a.Genes = new AlienGenes();
        a.Traits.Clear();
        return a;
    }

    /// <summary>Dig every patch (test convenience: the whole land).</summary>
    public static void UnlockAll(GameEngine e, GameState s)
    {
        foreach (var p in e.Content.CityPatches)
            if (!s.City.DugPatches.Contains(p.Id)) s.City.DugPatches.Add(p.Id);
        CityLayout.Invalidate(s);
    }
}
