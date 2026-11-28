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

    private static readonly HashSet<ContentDb> Applied = new(ReferenceEqualityComparer.Instance); // before Classic (static init order)

    /// <summary>
    /// Content whose first main levels have every run element (campfire, shops, miniboss lair, clock minibosses, forks, the
    /// portal's own tile pool, normal awakening, mini-games from level 2), as before the level unlocks (GDD §16). The run
    /// mechanic tests were written against these boards; LevelUnlockRunContentTests covers the real early levels.
    /// </summary>
    public static readonly ContentDb Classic = ClassicBoards(new ContentDb());

    /// <summary>Restores the classic boards on main levels 1–12 of <paramref name="c"/> (never ContentDb.Default). Idempotent.</summary>
    public static ContentDb ClassicBoards(ContentDb c)
    {
        if (ReferenceEquals(c, ContentDb.Default)) return c;
        lock (Applied)
        {
            if (!Applied.Add(c)) return c; // once per content: shared contents are read by parallel tests
            ApplyClassic(c);
        }
        return c;
    }


    private static void ApplyClassic(ContentDb c)
    {
        for (int n = 1; n <= 12; n++)
        {
            var l = c.MainLevel(n)!;
            if (n <= c.Campaign.ChapterLevels) l.TilePoolId = "pool_" + c.CampaignBiome(n)!.Family; // the full pool of its biome (level 1: not the tutorial pool)
            l.Modifiers.AwakeningPct = 0;
            l.BranchChance = new LevelDef().BranchChance;
            l.ClockMinibosses = true;
            l.SignatureChoice = true;
            foreach (var (at, id) in new[] { (0.22, "campfire"), (0.42, "shop"), (0.68, "miniboss"), (0.88, "shop") })
                if (!l.FixedTiles.Any(f => f.TileId == id && Math.Abs(f.At - at) < 1e-9)) l.FixedTiles.Add(new FixedTile(at, id));
            l.FixedTiles.Sort((a, b) => a.At.CompareTo(b.At));
        }
        // the classic ease of levels 1 and 2 (the tutorial levels are eased a bit more now, without shops and Mythics)
        void Ease(int n, double hpWas, double atkWas)
        {
            var m = c.MainLevel(n)!.Modifiers;
            double hpNow = c.Campaign.EarlyHpPct(n), atkNow = c.Campaign.EarlyAtkPct(n);
            if (Math.Abs(hpNow - hpWas) < 1e-9 && Math.Abs(atkNow - atkWas) < 1e-9) return;
            m.EnemyHpPct = Math.Round(((1 + m.EnemyHpPct / 100) * (1 + hpWas / 100) / (1 + hpNow / 100) - 1) * 100, 2);
            m.EnemyAtkPct = Math.Round(((1 + m.EnemyAtkPct / 100) * (1 + atkWas / 100) / (1 + atkNow / 100) - 1) * 100, 2);
            c.Campaign.EarlyLevelEnemyHpPct[n - 1] = hpWas;
            c.Campaign.EarlyLevelEnemyAtkPct[n - 1] = atkWas;
        }
        Ease(1, -60, -40);
        Ease(2, -55, -40);
        c.BoardTiles["campfire"].MinMainLevel = 0;
        c.MinigameTuning.MinMainLevel = Math.Min(c.MinigameTuning.MinMainLevel, 2);
    }

    public static GameEngine Engine(WormSetup? worm = null, ContentDb? content = null)
    {
        worm ??= new WormSetup();
        var e = new GameEngine(ClassicBoards(content ?? Classic)) { Cloner = TestHelpers.Clone };
        e.RegisterLoadoutContributor((s, c, now, l) =>
        {
            // Controlled worm: replace whatever Progression/City contributed (registered after every system).
            l.WormStats = new StatBlock();
            l.MetaSkillIds.Clear();
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

    public static GameState StartRun(GameEngine e, string level = "main_1", long seed = 42, RunMode mode = RunMode.Normal)
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
            var r = Exec(e, ref s, new RunAutoStep { Farming = true, FarmShops = true });
            Assert.True(r.Ok, r.Error + " in phase " + s.Run.Active?.Phase);
        }
        Assert.Fail("run did not finish within " + maxSteps + " steps");
        return maxSteps;
    }

    public static CommandContext Ctx(GameEngine e, GameState s) =>
        new() { State = s, Content = e.Content, Engine = e, NowMs = TestHelpers.T0 };

    /// <summary>Replace the board with a straight line of tiles (ids); its last tile is the open end (the rules extend the path from there).</summary>
    public static void LineBoard(ActiveRun run, params string[] tiles)
    {
        run.Tiles.Clear();
        var all = new List<string> { "start" };
        all.AddRange(tiles);
        for (int i = 0; i < all.Count; i++)
        {
            var t = new RunTile { Id = i, TileId = all[i], X = i };
            if (i + 1 < all.Count) t.Next.Add(i + 1);
            run.Tiles.Add(t);
        }
        run.BoardEnd = all.Count - 1;
        run.BossGateTile = -1;
        run.Position = 0;
        run.Trail.Clear();
        run.Trail.Add(0);
    }

    /// <summary>
    /// Adds a side arm of <paramref name="length"/> empty tiles from tile <paramref name="from"/> (a fork: its second way)
    /// that rejoins the board at tile <paramref name="join"/>; returns the arm's first tile id.
    /// </summary>
    public static int SideArm(ActiveRun run, int from, int join, int length, string tileId = "empty")
    {
        int first = run.Tiles.Count, prev = from;
        for (int i = 0; i < length; i++)
        {
            var t = new RunTile { Id = run.Tiles.Count, TileId = tileId, X = from + 1 + i, Y = 1 };
            run.Tiles.Add(t);
            run.Tiles[prev].Next.Add(t.Id);
            prev = t.Id;
        }
        run.Tiles[prev].Next.Add(join);
        return first;
    }

    public static SkillDef Skill(string id, params SkillTrigger[] triggers)
    {
        var s = new SkillDef { Id = id, Name = id, Source = SkillSource.Building, Draftable = false };
        s.Triggers.AddRange(triggers);
        return s;
    }
}
