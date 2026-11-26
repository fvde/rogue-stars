using RogueStars.Rules.Content;
using RogueStars.Rules.Content.Quests;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using Xunit.Abstractions;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Run Quests (docs/design/run-quests/recipe.md): encounter rate and pity, at most one encounter per run, the blocking
/// semantics of battles and sacrifices, every sacrifice type, the account coin bonus on every coin gain, chain completion
/// and the epilogue scenes.
/// </summary>
public class RunQuestTests(ITestOutputHelper output)
{
    private const string Q = QuestMothwick.Id;

    private static GameEngine Engine(RunTestKit.WormSetup? worm = null) => RunTestKit.Engine(worm ?? RunTestKit.Strong());

    /// <summary>Starts a run and puts the quest tile right ahead (a straight line), then rolls into it: phase Quest.</summary>
    private static void Meet(GameEngine e, ref GameState s, Action<ActiveRun>? setup = null, int coinBonus = -1)
    {
        if (s.Run.Active == null) Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1" }).Ok);
        var run = s.Run.Active!;
        run.Quest = new RunQuestRun
        {
            QuestId = Q, CoinBonus = coinBonus >= 0 ? coinBonus : RunSystem.QuestCoinBonus(s), BonusName = "Starglint",
            Placed = true,
        };
        var p = RunSystem.QuestProgressOf(s, Q);
        if (p != null && p.Completed)
        {
            var q = e.Content.RunQuest(Q)!;
            run.Quest.Epilogue = true;
            run.Quest.EpilogueId = q.Epilogues[p.EpiloguesSeen % q.Epilogues.Count].Id;
        }
        RunTestKit.LineBoard(run, "quest", "empty", "empty", "empty", "empty", "empty", "empty", "empty", "empty", "empty", "empty", "empty", "empty");
        run.Phase = RunPhase.AwaitRoll;
        run.StepsLeft = 0;
        setup?.Invoke(run);
        var r = RunTestKit.Exec(e, ref s, new RunRoll());
        Assert.True(r.Ok, r.Error);
        Assert.Equal(RunPhase.Quest, s.Run.Active!.Phase);
    }

    private static QuestNodeDef Node(GameEngine e, GameState s) => RunSystem.QuestCurrentNode(e.Content, s, e.Content.RunQuest(Q)!)!;

    private static int Option(QuestNodeDef n, QuestOptionAction a) => n.Options.FindIndex(o => o.Action == a);

    /// <summary>Moves the chain to a node (the journal is not filled: test setup only).</summary>
    private static void At(GameState s, string node)
    {
        if (!s.RunQuests.Quests.TryGetValue(Q, out var p)) s.RunQuests.Quests[Q] = p = new QuestProgress();
        p.Node = node;
    }

    private static string SkillOfTier(ContentDb c, Rarity tier, int skip = 0) =>
        c.Skills.Values.Where(x => x.Draftable && !x.Signature && RunTuning.RunTier(x.Rarity) == tier).OrderBy(x => x.Id, StringComparer.Ordinal).Skip(skip).First().Id;

    // ---- encounter rate ------------------------------------------------------------------------------------------------

    [Fact]
    public void EncounterRate_AveragesOneInFiveRuns_NeverTwoInARow_NoDroughtOverEight()
    {
        var e = Engine();
        var s = TestHelpers.NewPlayer(e, 7);
        s.Wallet.Add(Currencies.Stamina, 1_000_000);
        var gaps = new List<int>();
        int since = 0, met = 0, runs = 4000, unplaced = 0;
        for (int i = 0; i < runs; i++)
        {
            Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1" }).Ok);
            var run = s.Run.Active!;
            int quests = run.Tiles.Count(t => t.TileId == ContentDb.QuestTileId);
            since++;
            if (run.Quest.QuestId != null)
            {
                // placed on the board once (rarely a crowded segment has no free trunk tile: then the next segment takes it)
                Assert.Equal(run.Quest.Placed ? 1 : 0, quests);
                if (run.Quest.Placed)
                {
                    var tile = run.Tiles[run.Quest.Tile];
                    Assert.True(tile.Fixed);
                    Assert.Equal(e.Content.QuestTuning.Segment, tile.Segment);
                }
                else unplaced++;
                // met: what landing on the tile does to the pity (the run is not played out here)
                s.RunQuests.RunsWithout = 0; s.RunQuests.Encounters++;
                if (met > 0) gaps.Add(since);
                met++; since = 0;
            }
            else Assert.Equal(0, quests);
            Assert.True(RunTestKit.Exec(e, ref s, new RunAbandon()).Ok);
        }
        Assert.True(unplaced <= met / 100, unplaced + " runs placed the tile later");
        double mean = gaps.Average();
        output.WriteLine($"{met} encounters in {runs} runs; mean gap {mean:0.00}, min {gaps.Min()}, max {gaps.Max()}");
        Assert.InRange(mean, 4.6, 5.4);
        Assert.True(gaps.Min() >= 2, "two encounters in a row");
        Assert.True(gaps.Max() <= 8, "a drought of " + gaps.Max() + " runs");
        // the analytic mean of the pity table is 5 runs (±0.1)
        var t = e.Content.QuestTuning.ChanceByRunsWithoutPct;
        double surv = 1, expected = 0;
        for (int k = 0; k < t.Count; k++) { expected += (k + 1) * surv * t[k] / 100; surv *= 1 - t[k] / 100; }
        Assert.InRange(expected, 4.9, 5.1);
        Assert.Equal(100, t[t.Count - 1]);
    }

    [Fact]
    public void FirstEncounter_ComesWithinTwoRuns_OfTheUnlock()
    {
        var e = Engine();
        for (int seed = 1; seed <= 30; seed++)
        {
            var s = TestHelpers.NewPlayer(e, seed);
            Assert.Equal(80, RunSystem.QuestChancePct(e.Content, s));
            Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1" }).Ok);
            bool first = s.Run.Active!.Quest.QuestId != null;
            Assert.True(RunTestKit.Exec(e, ref s, new RunAbandon()).Ok);
            if (first) continue;
            Assert.Equal(100, RunSystem.QuestChancePct(e.Content, s));
            Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1" }).Ok);
            Assert.NotNull(s.Run.Active!.Quest.QuestId);
            RunTestKit.Exec(e, ref s, new RunAbandon());
        }
    }

    [Fact]
    public void NoEncounter_BeforeTheUnlock_OrInTheBurrow()
    {
        var e = Engine();
        var s = TestHelpers.NewPlayer(e, 3);
        s.Flags.Remove(Features.DevUnlockAllFlag);
        Assert.False(RunSystem.QuestEligible(s, e.Content, RunMode.Normal));
        s.Onboarding.Unlocked[Features.RunQuests] = TestHelpers.T0;
        Assert.True(RunSystem.QuestEligible(s, e.Content, RunMode.Normal));
        Assert.True(RunSystem.QuestEligible(s, e.Content, RunMode.DarkSide));
        Assert.False(RunSystem.QuestEligible(s, e.Content, RunMode.Burrow));
        Assert.Equal(20, (int)Features.UnlockLevel(e.Content.Feature(Features.RunQuests)!));
        Assert.Equal(e.Content.QuestTuning.UnlockLevel, (int)Features.UnlockLevel(e.Content.Feature(Features.RunQuests)!));
    }

    [Fact]
    public void ScheduledRun_ReachesTheTile_AndOpensTheEncounter_Once()
    {
        int met = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            var e = Engine();
            var s = TestHelpers.NewPlayer(e, seed);
            s.RunQuests.RunsWithout = 7; s.RunQuests.Encounters = 1; // 100%
            Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1" }).Ok);
            Assert.NotNull(s.Run.Active!.Quest.QuestId);
            int opened = 0;
            for (int i = 0; i < 800 && s.Run.Active != null; i++)
            {
                var r = RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true });
                Assert.True(r.Ok, r.Error);
                opened += r.Events.Count(ev => ev.Type == "run_quest");
            }
            Assert.True(opened <= 1, "two encounters in one run");
            met += opened;
            Assert.Equal(opened, s.RunQuests.Encounters - 1);
        }
        Assert.Equal(12, met); // a strong worm always gets there: the tile is on the trunk and forces a stop
    }

    // ---- blocking semantics --------------------------------------------------------------------------------------------

    [Fact]
    public void Battle_Declined_Or_Lost_KeepsTheNode_WonAdvances_AndALossNeverEndsTheRun()
    {
        // declined
        var e = Engine();
        var s = TestHelpers.NewPlayer(e, 1);
        At(s, "m03");
        Meet(e, ref s);
        Assert.True(RunTestKit.Exec(e, ref s, new RunQuestChoose { Index = Option(Node(e, s), QuestOptionAction.Wait) }).Ok);
        Assert.Equal("m03", RunSystem.QuestProgressOf(s, Q)!.Node);
        Assert.Equal(1, RunSystem.QuestProgressOf(s, Q)!.Waits);
        Assert.Equal(RunPhase.AwaitRoll, s.Run.Active!.Phase);
        Assert.Equal("waits", s.Run.Active.LastOutcome!.Lines.First(l => l.Kind == OutcomeKind.QuestStep).Id);

        // lost (a weak worm): the run goes on with at least 10% HP, the node stays
        var weak = Engine(new RunTestKit.WormSetup { Hp = 100, Atk = 1, Def = 0, Speed = 1 });
        s = TestHelpers.NewPlayer(weak, 2);
        At(s, "m03");
        Meet(weak, ref s);
        var r = RunTestKit.Exec(weak, ref s, new RunQuestChoose { Index = Option(Node(weak, s), QuestOptionAction.Fight) });
        Assert.True(r.Ok, r.Error);
        Assert.Contains(r.Events, ev => ev.Type == "run_battle");
        Assert.NotNull(s.Run.Active);
        Assert.NotEqual(RunPhase.Defeated, s.Run.Active!.Phase);
        Assert.False(s.Run.Active.ReviveUsed);
        Assert.True(s.Run.Active.WormHp >= Math.Round(s.Run.Active.WormMaxHp * 0.1) - 1e-9);
        Assert.Equal("m03", RunSystem.QuestProgressOf(s, Q)!.Node);
        Assert.Contains(s.Run.Active.LastOutcome!.Lines, l => l.Kind == OutcomeKind.Battle && l.Id == "lost");

        // won (a strong worm): advance, journal entry
        s = TestHelpers.NewPlayer(e, 3);
        At(s, "m03");
        Meet(e, ref s);
        r = RunTestKit.Exec(e, ref s, new RunQuestChoose { Index = Option(Node(e, s), QuestOptionAction.Fight) });
        Assert.True(r.Ok, r.Error);
        Assert.Equal("m04", RunSystem.QuestProgressOf(s, Q)!.Node);
        Assert.Equal("m03", RunSystem.QuestProgressOf(s, Q)!.Journal.Last().NodeId);
    }

    [Fact]
    public void QuestBattle_IsAboveTheLevelsElites()
    {
        var e = Engine();
        var s = RunTestKit.StartRun(e, "ember_wastes_1");
        var run = s.Run.Active!;
        var c = e.Content;
        var level = c.RunLevel(run.LevelId)!;
        foreach (var node in c.RunQuest(Q)!.Nodes.Values.Where(n => n.Kind == QuestNodeKind.Battle))
        {
            var enemies = RunSystem.QuestEnemies(c, run, node.Battle!);
            Assert.Equal(1 + node.Battle!.Escorts, enemies.Count);
            Assert.Equal(level.MinibossId, enemies[0].Id);
            // harder than the level's elite fight (an elite at +EliteBonusPct and one ordinary enemy): more HP and ATK in total
            double scale = c.ThreatMultiplier(run.Threat), common = c.RunTuning.BoardEnemyPct, elite = common + c.RunTuning.EliteBonusPct;
            var eliteFight = new List<CombatantSpec>
            {
                BattleSimulator.BuildEnemy(c, level.ElitePool[0], scale, elite + level.Modifiers.EnemyAtkPct, elite + level.Modifiers.EnemyHpPct, level.Modifiers.EnemyDefPct),
                BattleSimulator.BuildEnemy(c, level.EnemyPool[0], scale, common + level.Modifiers.EnemyAtkPct, common + level.Modifiers.EnemyHpPct, level.Modifiers.EnemyDefPct),
            };
            double Sum(List<CombatantSpec> l, StatId stat) => l.Sum(x => BattleSimulator.FinalStats(x, c).Value(stat));
            output.WriteLine($"{node.Id}: quest HP {Sum(enemies, StatId.Hp):0} vs elite fight {Sum(eliteFight, StatId.Hp):0}; " + string.Join(",", enemies.Select(x => x.Id)));
            Assert.True(Sum(enemies, StatId.Hp) > Sum(eliteFight, StatId.Hp) * 1.2, node.Id + " HP");
            Assert.True(BattleSimulator.FinalStats(enemies[0], c).Value(StatId.Atk) > BattleSimulator.FinalStats(eliteFight[0], c).Value(StatId.Atk), node.Id + " ATK");
        }
    }

    [Fact]
    public void Sacrifice_Refused_Or_Unaffordable_KeepsTheNode()
    {
        var e = Engine();
        var s = TestHelpers.NewPlayer(e, 1);
        At(s, "m05a"); // 100 coins
        Meet(e, ref s, run => run.Coins = 99);
        var n = Node(e, s);
        Assert.False(RunSystem.QuestCanPay(e.Content, s.Run.Active, n.Cost!));
        var r = RunTestKit.Exec(e, ref s, new RunQuestChoose { Index = Option(n, QuestOptionAction.Pay) });
        Assert.False(r.Ok);
        Assert.Equal(RunPhase.Quest, s.Run.Active!.Phase); // still deciding
        Assert.True(RunTestKit.Exec(e, ref s, new RunQuestChoose { Index = Option(n, QuestOptionAction.Wait) }).Ok);
        Assert.Equal("m05a", RunSystem.QuestProgressOf(s, Q)!.Node);
        Assert.Equal(99, s.Run.Active!.Coins);
    }

    // ---- every sacrifice type ------------------------------------------------------------------------------------------

    [Fact]
    public void Sacrifice_Coins()
    {
        var e = Engine();
        var s = TestHelpers.NewPlayer(e, 1);
        At(s, "m18a"); // 300 coins
        Meet(e, ref s, run => run.Coins = 350);
        Assert.True(RunTestKit.Exec(e, ref s, new RunQuestChoose { Index = 0 }).Ok);
        Assert.Equal(50, s.Run.Active!.Coins);
        Assert.Equal("m19", RunSystem.QuestProgressOf(s, Q)!.Node);
        Assert.Contains(s.Run.Active.LastOutcome!.Lines, l => l.Kind == OutcomeKind.Coins && l.Amount == -300);
    }

    [Theory]
    [InlineData("m14", Rarity.Legendary)]
    [InlineData("m22", Rarity.Mythic)]
    public void Sacrifice_SkillOfATier_PlayerChoosesWhich(string node, Rarity tier)
    {
        var e = Engine();
        var c = e.Content;
        string a = SkillOfTier(c, tier), b = SkillOfTier(c, tier, 1), common = SkillOfTier(c, Rarity.Common);
        var s = TestHelpers.NewPlayer(e, 1);
        At(s, node);
        Meet(e, ref s, run => { run.Skills.Add(a); run.Skills.Add(b); run.Skills.Add(common); });
        var kind = tier == Rarity.Mythic ? QuestCostKind.MythicSkill : QuestCostKind.LegendarySkill;
        var choices = RunSystem.QuestSkillChoices(c, s.Run.Active, kind);
        Assert.Contains(a, choices); Assert.Contains(b, choices); Assert.DoesNotContain(common, choices);
        Assert.False(RunTestKit.Exec(e, ref s, new RunQuestChoose { Index = 0, SkillId = common }).Ok); // not of the tier
        Assert.True(RunTestKit.Exec(e, ref s, new RunQuestChoose { Index = 0, SkillId = b }).Ok);
        var run = s.Run.Active!;
        Assert.Contains(a, run.Skills); Assert.DoesNotContain(b, run.Skills); Assert.Contains(common, run.Skills);
        Assert.Contains(run.LastOutcome!.Lines, l => l.Kind == OutcomeKind.SkillLost && l.Id == b);
        Assert.NotEqual(node, RunSystem.QuestProgressOf(s, Q)!.Node);
    }

    [Fact]
    public void Sacrifice_SkillOfATier_WithoutOne_CannotBePaid()
    {
        var e = Engine();
        var s = TestHelpers.NewPlayer(e, 1);
        At(s, "m22");
        Meet(e, ref s, run => run.Skills.Add(SkillOfTier(e.Content, Rarity.Legendary)));
        Assert.False(RunSystem.QuestCanPay(e.Content, s.Run.Active, Node(e, s).Cost!));
        Assert.False(RunTestKit.Exec(e, ref s, new RunQuestChoose { Index = 0 }).Ok);
    }

    [Theory]
    [InlineData("m10a", 50)]
    [InlineData("m18b", 80)]
    public void Sacrifice_PercentOfCurrentLife(string node, int pct)
    {
        var e = Engine(new RunTestKit.WormSetup { Hp = 1000, Atk = 100, Def = 20, Speed = 100 });
        var s = TestHelpers.NewPlayer(e, 1);
        At(s, node);
        Meet(e, ref s, run => run.WormHp = 600);
        double before = s.Run.Active!.WormHp;
        Assert.True(RunTestKit.Exec(e, ref s, new RunQuestChoose { Index = 0 }).Ok);
        Assert.Equal(Math.Round(before * (100 - pct) / 100.0), s.Run.Active!.WormHp);
    }

    [Fact]
    public void Sacrifice_ForgetAllRunSkills_KeepsTheSignatures()
    {
        var e = Engine();
        var c = e.Content;
        var s = TestHelpers.NewPlayer(e, 1);
        At(s, "m29a");
        Meet(e, ref s, run =>
        {
            run.Skills.Add(SkillOfTier(c, Rarity.Common)); run.Skills.Add(SkillOfTier(c, Rarity.Legendary)); run.Skills.Add(SkillOfTier(c, Rarity.Mythic));
            run.SkillLevels[SkillOfTier(c, Rarity.Common)] = 2;
        });
        var sigs = RunSystem.Signatures(s.Run.Active!).ToList();
        Assert.NotEmpty(sigs);
        Assert.True(RunTestKit.Exec(e, ref s, new RunQuestChoose { Index = 0 }).Ok);
        var run = s.Run.Active!;
        Assert.Equal(sigs.OrderBy(x => x), run.Skills.OrderBy(x => x));
        Assert.Empty(run.SkillLevels.Keys.Where(k => !sigs.Contains(k)));
        Assert.Equal("m30a", RunSystem.QuestProgressOf(s, Q)!.Node);
        // nothing left to forget: the price cannot be paid again
        Assert.False(RunSystem.QuestCanPay(c, run, new QuestCostDef(QuestCostKind.AllRunSkills)));
    }

    // ---- the account coin bonus ----------------------------------------------------------------------------------------

    [Fact]
    public void CoinBonus_AddsToEveryCoinGain_NotToCosts_AndShowsAsItsOwnLine()
    {
        var e = Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.Quest = new RunQuestRun { CoinBonus = 3, BonusName = "Starglint" };
        RunTestKit.LineBoard(run, "loot", "loot", "loot", "loot", "loot", "loot", "loot", "loot", "loot", "loot", "loot", "loot", "loot");
        long coins = run.Coins;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        run = s.Run.Active!;
        Assert.Equal(coins + 25 + 3, run.Coins); // the landing tile's supply cache: 25 + 3
        Assert.Equal(3, run.Quest.CoinBonusPaid);
        var lines = run.TileOutcomes.Last().Lines;
        Assert.Contains(lines, l => l.Kind == OutcomeKind.Coins && l.Amount == 25);
        Assert.Contains(lines, l => l.Kind == OutcomeKind.CoinBonus && l.Amount == 3 && l.Id == "Starglint");
    }

    [Fact]
    public void CoinBonus_ComesFromTheAccount_AtRunStart_AndABoonAppliesAtOnce()
    {
        var e = Engine();
        var s = TestHelpers.NewPlayer(e, 1);
        s.RunQuests.Quests[Q] = new QuestProgress { Node = "m19", CoinBonus = 1 };
        s.Wallet.Add(Currencies.Stamina, 100);
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1" }).Ok);
        Assert.Equal(1, s.Run.Active!.Quest.CoinBonus);
        Meet(e, ref s);
        Assert.Equal(QuestNodeKind.Boon, Node(e, s).Kind);
        Assert.True(RunTestKit.Exec(e, ref s, new RunQuestChoose { Index = 0 }).Ok);
        Assert.Equal(2, RunSystem.QuestCoinBonus(s));
        Assert.Equal(2, s.Run.Active!.Quest.CoinBonus);
        Assert.Contains(s.Run.Active.LastOutcome!.Lines, l => l.Kind == OutcomeKind.QuestBoon && l.Amount == 2);
        Assert.Equal("Starglint", RunSystem.QuestBonusName(e.Content, s));
    }

    [Fact]
    public void CoinBonus_EveryRunCoinGain_GoesThroughGainCoins()
    {
        // guard: a run-coin increase anywhere in the Run rules must use GainCoins / AddCoins, so the quest bonus is never skipped
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "RogueStars.slnx"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var offenders = new List<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(dir!, "shared", "RogueStars.Rules", "Systems", "Run"), "*.cs"))
        {
            if (Path.GetFileName(f) == "RunSystem.Quests.cs") continue; // GainCoins itself
            var lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
                if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"\.Coins\s*\+=") && !lines[i].Contains("def.Coins; // cost"))
                    offenders.Add(Path.GetFileName(f) + ":" + (i + 1) + ": " + lines[i].Trim());
        }
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    // ---- completion and epilogues --------------------------------------------------------------------------------------

    [Fact]
    public void Chain_CanBeCompleted_ThirtyChapters_ThreeBoons_ThenEpilogues()
    {
        var e = Engine();
        var c = e.Content;
        var quest = c.RunQuest(Q)!;
        var s = TestHelpers.NewPlayer(e, 11);
        s.Wallet.Add(Currencies.Stamina, 1_000_000);
        var rng = new Random(4);
        int encounters = 0;
        while (RunSystem.QuestProgressOf(s, Q)?.Completed != true)
        {
            Assert.True(++encounters < 200);
            Meet(e, ref s, run =>
            {
                run.Coins = 1000;
                run.Skills.Add(SkillOfTier(c, Rarity.Legendary)); run.Skills.Add(SkillOfTier(c, Rarity.Mythic));
            });
            var n = Node(e, s);
            int pick = n.Kind == QuestNodeKind.Story ? rng.Next(n.Options.Count) : n.Options.FindIndex(o => o.Action != QuestOptionAction.Wait);
            var r = RunTestKit.Exec(e, ref s, new RunQuestChoose { Index = pick });
            Assert.True(r.Ok, r.Error);
            if (s.Run.Active != null) RunTestKit.Exec(e, ref s, new RunAbandon());
        }
        var p = RunSystem.QuestProgressOf(s, Q)!;
        Assert.Equal(quest.Chapters, p.Journal.Count);
        Assert.Equal(Enumerable.Range(1, quest.Chapters), p.Journal.Select(j => j.Chapter));
        Assert.All(p.Journal, j => Assert.False(string.IsNullOrEmpty(j.Line)));
        Assert.Equal(3, RunSystem.QuestCoinBonus(s));
        Assert.Null(p.Node);
        Assert.True(s.Counters["run_quests_completed"] == 1);
        Assert.Equal(quest.Chapters + 1, RunSystem.QuestChapter(c, s, quest));

        // epilogues: friendly scenes in rotation, purely positive
        var kinds = new List<QuestEpilogueKind>();
        for (int i = 0; i < quest.Epilogues.Count + 1; i++)
        {
            Meet(e, ref s, run => { run.WormHp = Math.Max(1, run.WormMaxHp / 2); run.Coins = 0; });
            var ep = RunSystem.QuestOpenEpilogue(c, s.Run.Active)!;
            Assert.NotNull(ep);
            Assert.Null(RunSystem.QuestOpenNode(c, s, s.Run.Active));
            var before = s.Run.Active!;
            double hp = before.WormHp, maxHp = before.WormMaxHp; int pending = before.PendingDrafts; int stats = before.RunStats.Count;
            var r = RunTestKit.Exec(e, ref s, new RunQuestChoose { Index = 0 });
            Assert.True(r.Ok, r.Error);
            var run = s.Run.Active!;
            kinds.Add(ep.Kind);
            switch (ep.Kind)
            {
                case QuestEpilogueKind.Coins: Assert.Equal((long)ep.Amount + 3, run.Coins); break; // + Starglint
                case QuestEpilogueKind.HealPct: Assert.True(run.WormHp > hp); break;
                case QuestEpilogueKind.SkillPick: Assert.Equal(RunPhase.Draft, run.Phase); break;
                case QuestEpilogueKind.MaxHpPct: Assert.True(run.WormMaxHp > maxHp && run.RunStats.Count == stats + 1); break;
            }
            Assert.DoesNotContain(run.LastOutcome!.Lines, l => !l.Good);
            RunTestKit.Exec(e, ref s, new RunAbandon());
        }
        Assert.Equal(quest.Epilogues.Select(x => x.Kind).Append(quest.Epilogues[0].Kind), kinds);
        Assert.Equal(3, RunSystem.QuestCoinBonus(s)); // epilogues add no more account power
    }

    [Fact]
    public void CompletedChain_KeepsComingAtTheSameRate_AsEpilogues()
    {
        var e = Engine();
        var s = TestHelpers.NewPlayer(e, 2);
        s.RunQuests.Quests[Q] = new QuestProgress { Completed = true, CoinBonus = 3 };
        s.RunQuests.Encounters = 40;
        s.RunQuests.RunsWithout = 7;
        s.Wallet.Add(Currencies.Stamina, 100);
        Assert.Equal(100, RunSystem.QuestChancePct(e.Content, s));
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1" }).Ok);
        Assert.True(s.Run.Active!.Quest.Epilogue);
        Assert.Equal(e.Content.RunQuest(Q)!.Epilogues[0].Id, s.Run.Active.Quest.EpilogueId);
    }

    [Fact]
    public void Farming_NeverPaysOrFights_ButTellsTheStory()
    {
        var e = Engine();
        var s = TestHelpers.NewPlayer(e, 1);
        At(s, "m05a");
        Meet(e, ref s, run => run.Coins = 500);
        Assert.True(RunSystem.RequiresPlayer(e.Content, s.Run.Active!));
        Assert.False(RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = false }).Ok);
        Assert.True(RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true }).Ok);
        Assert.Equal("m05a", RunSystem.QuestProgressOf(s, Q)!.Node);
        Assert.True(s.Run.Active!.Coins >= 500);
        At(s, "m04");
        s.Run.Active = null;
        Meet(e, ref s);
        Assert.True(RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true }).Ok);
        Assert.Equal("m05a", RunSystem.QuestProgressOf(s, Q)!.Node); // the story node's first option
    }
}
