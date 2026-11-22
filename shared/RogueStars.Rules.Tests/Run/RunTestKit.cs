using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>Helpers for Run workstream tests. Worm stats come from Progression, so tests add their own contributor.</summary>
public static class RunTestKit
{
    public sealed class WormSetup
    {
        public double Hp = 1000, Atk = 100, Def = 20, Speed = 100;
        public List<string> Meta = new();
        public List<StatMod> Extra = new();
    }

    public static GameEngine Engine(WormSetup? worm = null, ContentDb? content = null)
    {
        worm ??= new WormSetup();
        var e = new GameEngine(content ?? ContentDb.Default) { Cloner = TestHelpers.Clone };
        e.RegisterLoadoutContributor((s, c, now, l) =>
        {
            l.WormStats.Add(new StatMod(StatId.Hp, worm.Hp));
            l.WormStats.Add(new StatMod(StatId.Atk, worm.Atk));
            l.WormStats.Add(new StatMod(StatId.Def, worm.Def));
            l.WormStats.Add(new StatMod(StatId.Speed, worm.Speed));
            l.WormStats.AddAll(worm.Extra);
            l.MetaSkillIds.AddRange(worm.Meta);
        });
        return e;
    }

    public static WormSetup Strong() => new() { Hp = 1_000_000, Atk = 100_000, Def = 10_000, Speed = 1000 };
    public static WormSetup Weak() => new() { Hp = 1, Atk = 1, Def = 0, Speed = 1 };

    public static CommandResult Exec(GameEngine e, ref GameState s, GameCommand c, long now = TestHelpers.T0) => e.Execute(ref s, c, now);

    public static GameState StartRun(GameEngine e, string level = "ember_wastes_1", long seed = 42, RunMode mode = RunMode.Normal)
    {
        var s = TestHelpers.NewPlayer(e, seed);
        var r = Exec(e, ref s, new RunStart { LevelId = level, Mode = mode });
        Assert.True(r.Ok, r.Error);
        return s;
    }

    /// <summary>Drive the run with RunAutoStep until it ends; returns the number of steps used.</summary>
    public static int AutoToEnd(GameEngine e, ref GameState s, int maxSteps = 800)
    {
        for (int i = 0; i < maxSteps; i++)
        {
            if (s.Run.Active == null) return i;
            var r = Exec(e, ref s, new RunAutoStep());
            Assert.True(r.Ok, r.Error + " in phase " + s.Run.Active?.Phase);
        }
        Assert.Fail("run did not finish within " + maxSteps + " steps");
        return maxSteps;
    }

    public static CommandContext Ctx(GameEngine e, GameState s) =>
        new() { State = s, Content = e.Content, Engine = e, NowMs = TestHelpers.T0 };

    /// <summary>Replace the board with a straight line of tiles (ids) ending in a boss gate.</summary>
    public static void LineBoard(ActiveRun run, params string[] tiles)
    {
        run.Tiles.Clear();
        var all = new List<string> { "start" };
        all.AddRange(tiles);
        all.Add("boss_gate");
        for (int i = 0; i < all.Count; i++)
        {
            var t = new RunTile { Id = i, TileId = all[i], X = i };
            if (i + 1 < all.Count) t.Next.Add(i + 1);
            run.Tiles.Add(t);
        }
        run.BossGateTile = all.Count - 1;
        run.Position = 0;
        run.Trail.Clear();
        run.Trail.Add(0);
    }

    public static SkillDef Skill(string id, params SkillTrigger[] triggers)
    {
        var s = new SkillDef { Id = id, Name = id, Source = SkillSource.Building, Draftable = false };
        s.Triggers.AddRange(triggers);
        return s;
    }
}
