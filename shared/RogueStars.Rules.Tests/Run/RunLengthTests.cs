using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using Xunit.Abstractions;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// GDD §3.3: the board is a circuit and the boss's awakening meter is the run's clock — a run lasts ~30 rolls (27–33 on
/// average for every level band, one and two dice) before skills shift it, with ~8–12 drafts; account loot comes from at
/// most a few risky opportunities per run; every event / chest / campfire choice and shop visit has a structured outcome.
/// </summary>
public class RunLengthTests(ITestOutputHelper output)
{
    private const int Seeds = 40;

    /// <summary>Main levels 1, 3, 8 (two dice), 25, 100, 500 and Dark Side 10: early, mid and late.</summary>
    public static IEnumerable<object[]> Levels() => new[]
    {
        new object[] { 1, RunMode.Normal }, new object[] { 3, RunMode.Normal }, new object[] { 8, RunMode.Normal },
        new object[] { 25, RunMode.Normal }, new object[] { 100, RunMode.Normal }, new object[] { 500, RunMode.Normal },
        new object[] { 10, RunMode.DarkSide },
    };

    private static string LevelId(ContentDb c, int n, RunMode mode) => mode == RunMode.DarkSide ? c.DarkSideLevel(n).Id : c.MainLevel(n).Id;

    /// <summary>Plays one run with an unbeatable worm (no deaths: pure board pacing). Risk-taker: always takes option 0 of events.</summary>
    private static RunSummary Play(GameEngine e, string levelId, RunMode mode, long seed, bool riskTaker, out int bossRoll) =>
        Play(e, levelId, mode, seed, riskTaker, out bossRoll, out _);

    private static RunSummary Play(GameEngine e, string levelId, RunMode mode, long seed, bool riskTaker, out int bossRoll, out int drafts)
    {
        drafts = 0;
        var s = TestHelpers.NewPlayer(e, seed);
        s.Flags.Add(Features.DevUnlockAllFlag);
        var start = new RunStart { LevelId = levelId, Mode = mode };
        Assert.True(RunTestKit.Exec(e, ref s, start).Ok || ForceStart(e, ref s, levelId, mode));
        bossRoll = -1;
        for (int i = 0; i < 3000 && s.Run.Active != null; i++)
        {
            var a = s.Run.Active;
            if (a.BossFought && bossRoll < 0) bossRoll = a.Rolls;
            GameCommand cmd = new RunAutoStep { Farming = true };
            if (riskTaker && a.Phase == RunPhase.Event)
            {
                var ev = e.Content.RunEvents[a.EventId];
                if (a.Coins >= ev.Options[0].CoinCost && a.Shards >= ev.Options[0].ShardCost) cmd = new RunChooseEventOption { Index = 0 };
            }
            var res = RunTestKit.Exec(e, ref s, cmd);
            Assert.True(res.Ok);
            drafts += res.Events.Count(ev => ev.Type == "run_draft");
        }
        Assert.Null(s.Run.Active);
        if (bossRoll < 0) bossRoll = s.Run.LastSummary.Rolls;
        return s.Run.LastSummary;
    }

    private static bool ForceStart(GameEngine e, ref GameState s, string levelId, RunMode mode)
    {
        // Levels far ahead: record the previous level as cleared (progression is not under test).
        var c = e.Content;
        var level = c.RunLevel(levelId);
        if (mode == RunMode.DarkSide) s.Run.Levels[c.DarkSideLevel(level.Number - 1).Id] = new LevelProgress { Cleared = true };
        else
        {
            s.Run.Levels[c.MainLevelId(level.Number - 1)] = new LevelProgress { Cleared = true };
            s.Counters[Requirements.MainLevelCounter] = level.Number - 1;
        }
        s.Wallet.Add(Currencies.Stamina, 100);
        return RunTestKit.Exec(e, ref s, new RunStart { LevelId = levelId, Mode = mode }).Ok;
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void RunLength_AveragesAbout30Rolls_With8To12Drafts(int n, RunMode mode)
    {
        var e = RunTestKit.Engine(new RunTestKit.WormSetup { Hp = 1e12, Atk = 1e10, Def = 1e9, Speed = 1000 });
        string id = LevelId(e.Content, n, mode);
        var rolls = new List<int>();
        var drafts = new List<int>();
        for (int seed = 1; seed <= Seeds; seed++)
        {
            var sum = Play(e, id, mode, seed, false, out int bossRoll, out int picked);
            Assert.True(sum.Won);
            rolls.Add(bossRoll);
            drafts.Add(picked);
            Assert.True(sum.Laps >= 1 || e.Content.RunLevel(id).Dice == 1, "two-dice runs lap the circuit");
        }
        double avg = rolls.Average(), avgDrafts = drafts.Average();
        output.WriteLine($"{id} ({e.Content.RunLevel(id).Dice} dice): rolls avg {avg:0.0} min {rolls.Min()} max {rolls.Max()}, drafts avg {avgDrafts:0.0}");
        Assert.InRange(avg, 27, 33);
        Assert.True(rolls.Max() <= e.Content.RunLevel(id).MaxRolls);
        Assert.InRange(avgDrafts, 8, 12);
    }

    [Fact]
    public void SlowerAwakening_MakesRunsLonger_UpToTheRollLimit()
    {
        var c = new ContentDb();
        c.AddSkill(RunTestKit.Skill("b_slow", new SkillTrigger { When = TriggerKind.OnRunStart, Effect = EffectKind.SlowAwakening, Value = 20 }));
        var worm = RunTestKit.Strong();
        worm.Meta.Add("b_slow");
        var slow = RunTestKit.Engine(worm, c);
        var normal = RunTestKit.Engine(RunTestKit.Strong(), c);
        double a = 0, b = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            Play(normal, "ember_wastes_1", RunMode.Normal, seed, false, out int r1);
            Play(slow, "ember_wastes_1", RunMode.Normal, seed, false, out int r2);
            a += r1; b += r2;
        }
        output.WriteLine($"rolls: normal {a / 20:0.0}, 20% slower awakening {b / 20:0.0}");
        Assert.True(b / 20 >= a / 20 + 4, "a 20% slower meter adds ~6 rolls");
    }

    [Fact]
    public void Board_IsACircuit_TheWormNeverRunsOutOfBoard()
    {
        var c = ContentDb.Default;
        foreach (var n in new[] { 1, 6, 30, 120, 600 })
            for (int seed = 1; seed <= 5; seed++)
            {
                var b = RunBoard.Generate(c, c.MainLevel(n), Rng.FromSeed(seed));
                var gate = b.Tiles[b.GateTile];
                Assert.Equal(b.MainPath[1], Assert.Single(gate.Next));                  // past the gate: the next lap
                foreach (var t in b.Tiles) Assert.NotEmpty(t.Next);                       // no dead ends anywhere
                Assert.Contains(b.GateTile, RunBoard.Reachable(b.Tiles, 0));
            }
    }

    [Fact]
    public void Boss_ComesWhenTheMeterIsFull_AfterTheMove()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 20).ToArray());
        double per = RunSystem.AwakeningPerRoll(e.Content, run);
        Assert.Equal(e.Content.RunBosses[run.BossId].AwakenPerRoll, per, 6);
        Assert.Equal((int)Math.Ceiling(100 / per), RunSystem.BossRollEstimate(e.Content, run)); // HUD "Roll 0/28"
        run.Awakening = 100 - per / 2;
        Assert.Equal(1, RunSystem.BossRollEstimate(e.Content, run));
        var r = RunTestKit.Exec(e, ref s, new RunRoll());
        Assert.True(r.Ok);
        Assert.Contains(r.Events, ev => ev.Type == "run_boss_fight");
        Assert.Null(s.Run.Active);                                                       // strong worm beat it
        Assert.True(s.Run.LastSummary.Won);
        Assert.Equal(1, s.Run.LastSummary.Rolls);
    }

    [Fact]
    public void Boss_ComesAtTheRollLimit_EvenWithAStalledMeter()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 20).ToArray());
        run.AwakeningReductionPct = 90;
        run.Rolls = run.MaxRolls - 1;
        Assert.Equal(run.MaxRolls, RunSystem.BossRollEstimate(e.Content, run));
        var r = RunTestKit.Exec(e, ref s, new RunRoll());
        Assert.Contains(r.Events, ev => ev.Type == "run_boss_fight");
    }

    [Fact]
    public void PassingTheGate_StartsTheNextLap()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, "empty", "empty");
        run.StepsLeft = 0;
        var r = RunTestKit.Exec(e, ref s, new RunRoll());
        Assert.True(r.Ok);
        run = s.Run.Active!;
        if (run.LastDice[0] >= 4)
        {
            Assert.Equal(1, run.Laps);
            Assert.Contains(r.Events, ev => ev.Type == "run_lap");
            Assert.NotEqual(run.BossGateTile, run.Position);
        }
    }

    [Fact]
    public void AccountLootOpportunities_AreRare_AtMostThreePerRun()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var c = e.Content;
        int max = 0, runs = 0, offers = 0, drops = 0;
        foreach (var n in new[] { 1, 8, 25, 100 })
            for (int seed = 1; seed <= 25; seed++)
            {
                var sum = Play(e, c.MainLevel(n).Id, RunMode.Normal, seed, true, out _);
                runs++; offers += sum.AccountLootOffers; drops += sum.AccountLootDrops;
                max = Math.Max(max, sum.AccountLootOffers);
            }
        output.WriteLine($"account-loot opportunities per run: avg {(double)offers / runs:0.00}, max {max}; items found by a risk-taker: avg {(double)drops / runs:0.00}");
        Assert.True(max <= c.RunTuning.MaxAccountLootOffersPerRun);
        Assert.InRange((double)offers / runs, 0.75, 3);
        Assert.InRange((double)drops / runs, 0.5, 6);
        // Ordinary tiles never pay account loot: no pool tile is a Loot tile.
        foreach (var pool in c.TilePools.Values)
            foreach (var w in pool)
                Assert.NotEqual(TileCategory.Loot, c.BoardTiles[w.Id].Category);
        // Every account-loot event spells out its risk: a fight, a heavy loss or a lasting effect.
        foreach (var ev in c.RunEvents.Values.Where(x => x.GrantsAccountLoot()))
            foreach (var o in ev.Options.Where(o => o.Outcomes.Any(oc => oc.Effects.Any(fx => fx.Kind is RunEffectKind.Loot or RunEffectKind.PendingReward))))
            {
                Assert.Contains("account loot", o.RiskText);
                bool risky = o.Outcomes.Any(oc => oc.Effects.Any(fx => fx.Kind is RunEffectKind.Battle or RunEffectKind.AwakenRatePct or RunEffectKind.Meter
                    or RunEffectKind.DamagePct or RunEffectKind.BossStatPct || fx.Kind == RunEffectKind.StatPercent && fx.Value < 0));
                Assert.True(risky, ev.Id + ": " + o.Text + " has no risk");
            }
    }

    [Fact]
    public void LootCap_ReplacesLaterLootEvents()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.AccountLootOffers = e.Content.RunTuning.MaxAccountLootOffersPerRun;
        RunTestKit.LineBoard(run, Enumerable.Repeat("treasure", 20).ToArray());
        var r = RunTestKit.Exec(e, ref s, new RunRoll());
        Assert.True(r.Ok);
        Assert.Contains(r.Events, ev => ev.Type == "run_cache_empty");
        Assert.NotEqual(RunPhase.Event, s.Run.Active!.Phase);
    }

    [Fact]
    public void EventChoice_HasAStructuredOutcome_WithChance()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        int lucky = 0, unlucky = 0;
        for (int seed = 1; seed <= 30; seed++)
        {
            var s = RunTestKit.StartRun(e, seed: seed);
            var run = s.Run.Active!;
            RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 30).ToArray());
            run.Phase = RunPhase.Event; run.EventId = "abandoned_cache"; run.EventTile = 1;
            int seq = run.OutcomeSeq;
            Assert.True(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = 0 }).Ok);
            var o = s.Run.Active!.LastOutcome;
            Assert.Equal(seq + 1, s.Run.Active.OutcomeSeq);
            Assert.Equal("event", o.Source);
            Assert.Equal("Abandoned Cache", o.Title);
            Assert.Equal("Search it", o.Choice);
            Assert.True(o.Chance);
            Assert.Equal(65, o.SuccessChancePct, 6);
            if (o.Success)
            {
                lucky++;
                var loot = Assert.Single(o.Lines, l => l.Kind == OutcomeKind.AccountLoot);
                Assert.NotNull(loot.Reward);
                Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Awakening && l.Amount > 0 && !l.Good);
                Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Danger && l.Amount == 1);
            }
            else
            {
                unlucky++;
                Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Hp && l.Amount < 0 && !l.Good);
                Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Stat && l.Id == "Hp" && l.Amount == -5);
            }
        }
        Assert.True(lucky > 0 && unlucky > 0);
    }

    [Fact]
    public void GuardedCache_And_Campfire_And_Shop_ReportWhatWasGranted()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 30).ToArray());

        run.Phase = RunPhase.Event; run.EventId = "treasure_pod"; run.EventTile = 1;
        Assert.True(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = 0 }).Ok);
        var o = s.Run.Active!.LastOutcome;
        Assert.False(o.Chance);
        Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Battle && l.Id == "won");
        Assert.Equal(2, o.Lines.Count(l => l.Kind == OutcomeKind.AccountLoot));

        run = s.Run.Active!;
        run.WormHp = run.WormMaxHp / 2;
        run.Phase = RunPhase.Event; run.EventId = "campfire"; run.EventTile = 2;
        Assert.True(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = 0 }).Ok);
        o = s.Run.Active!.LastOutcome;
        Assert.Equal("campfire", o.Source);
        Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Hp && l.Amount > 0 && l.Good);

        // shop: purchases are summed up when the shop is left
        run = s.Run.Active!;
        run.Coins = 1000;
        var ctx = RunTestKit.Ctx(e, s);
        run.Tiles[3].TileId = "shop";
        run.Position = 2; run.Trail.Add(2);
        run.StepsLeft = 0;
        var buy = (Func<bool>)(() =>
        {
            for (int i = 0; i < 50 && s.Run.Active!.Phase != RunPhase.Shop; i++)
                Assert.True(RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true }).Ok);
            return s.Run.Active!.Phase == RunPhase.Shop;
        });
        Assert.True(buy());
        int seq = s.Run.Active!.OutcomeSeq;
        int skill = s.Run.Active.Shop.FindIndex(x => x.Kind == ShopOfferKind.Skill);
        s.Run.Active.Coins = 1000;
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopBuy { Index = skill }).Ok);
        Assert.Equal(seq, s.Run.Active!.OutcomeSeq);                                        // not before leaving
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopLeave()).Ok);
        o = s.Run.Active!.LastOutcome;
        Assert.Equal("shop", o.Source);
        Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.SkillGained);
        Assert.Contains(o.Lines, l => l.Kind == OutcomeKind.Coins && l.Amount < 0);
    }
}
