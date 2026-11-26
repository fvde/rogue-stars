using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using Xunit.Abstractions;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Owner playtest feedback (run rules): forks never bypass a miniboss and are never short; every tile shows what it did;
/// the first shop is affordable; skill levels grow the skill's own numbers; one battle gives at most one level-up; no shop
/// summary; double rewards double what they say; HUD stats with every run modification.
/// </summary>
public class PlaytestRulesTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Levels() =>
        new[] { "ember_wastes_1", "ember_wastes_8", "glacier_rift_5", "static_jungle_9", "the_burrow_3", "dark_side_7" }.Select(l => new object[] { l });

    private static Dictionary<int, int> Incoming(List<RunTile> tiles)
    {
        var inc = new Dictionary<int, int>();
        foreach (var t in tiles) foreach (var n in t.Next) inc[n] = inc.GetValueOrDefault(n) + 1;
        return inc;
    }

    /// <summary>True if the open end can be reached from the start without stepping on <paramref name="avoid"/>.</summary>
    private static bool EndReachableAvoiding(List<RunTile> tiles, int end, int avoid)
    {
        var seen = new HashSet<int> { 0 };
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            int id = stack.Pop();
            if (id == end) return true;
            foreach (var n in tiles[id].Next) if (n != avoid && seen.Add(n)) stack.Push(n);
        }
        return false;
    }

    // ---- 1. branching and layout ----------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Levels))]
    public void Forks_NeverBypassAMinibossOrShop_AndBothArmsHaveAtLeast8Tiles(string levelId)
    {
        var c = RunTestKit.Classic; // classic boards: forks from level 1
        var level = c.RunLevel(levelId)!;
        int forks = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var rng = new Rng(seed);
            var b = RunBoard.Generate(c, level, rng);
            RunBoard.AppendSegment(c, level, b, rng);
            var inc = Incoming(b.Tiles);
            foreach (var t in b.Tiles.Where(t => t.Next.Count > 1))
            {
                forks++;
                foreach (var start in t.Next)
                {
                    int len = 0, cur = start;
                    while (inc.GetValueOrDefault(cur) < 2) { len++; Assert.Single(b.Tiles[cur].Next); cur = b.Tiles[cur].Next[0]; }
                    Assert.True(len >= c.RunTuning.BranchMinArm, $"{levelId} seed {seed}: arm of {len} tiles from tile {t.Id}");
                }
            }
            // every path passes every miniboss lair, shop and campfire: removing one cuts the start off the open end
            foreach (var t in b.Tiles.Where(t => t.Fixed && t.TileId is "miniboss" or "shop" or "campfire"))
                Assert.False(EndReachableAvoiding(b.Tiles, b.EndTile, t.Id), $"{levelId} seed {seed}: a fork bypasses {t.TileId} at tile {t.Id}");
        }
        Assert.True(forks > 10, levelId + ": forks " + forks);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void Layout_EveryTileHasAGridCell_NeighboursAdjacent_NoOverlap(string levelId)
    {
        // the iso grid layout (GridLayoutTests has the full set): path neighbours one cell apart, no shared cells, inside the band
        var c = ContentDb.Default;
        var level = c.RunLevel(levelId)!;
        for (ulong seed = 1; seed <= 25; seed++)
        {
            var rng = new Rng(seed);
            var b = RunBoard.Generate(c, level, rng);
            RunBoard.AppendSegment(c, level, b, rng);
            var tiles = b.Tiles;
            var seen = new HashSet<(int, int)>();
            foreach (var t in tiles)
            {
                Assert.True(t.Segment >= 0);
                Assert.True(seen.Add((RunBoard.CellX(t), RunBoard.CellY(t))), $"{levelId} seed {seed}: tile {t.Id} overlaps");
                foreach (var n in t.Next)
                    Assert.Equal(1, Math.Abs(RunBoard.CellX(tiles[n]) - RunBoard.CellX(t)) + Math.Abs(RunBoard.CellY(tiles[n]) - RunBoard.CellY(t)));
                Assert.InRange(RunBoard.Lateral(RunBoard.CellX(t), RunBoard.CellY(t)), -c.RunTuning.GridBand, c.RunTuning.GridBand);
            }
        }
    }

    [Fact]
    public void Layout_SegmentsUseVariedShapes()
    {
        var c = RunTestKit.Classic;
        Assert.True(c.RunTuning.BoardLayouts.Select(l => l.Kind).Distinct().Count() >= 5);
        var level = c.RunLevel("ember_wastes_5")!;
        // the shapes differ: how often the path turns varies across seeds (straights, zig-zags, hairpins, fork loops)
        var turnRates = new HashSet<int>();
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var b = RunBoard.Generate(c, level, new Rng(seed));
            var seg = b.Tiles.Where(t => t.Segment == 0).ToList();
            int turns = seg.Count(t => t.Next.Count > 0 && b.Tiles[t.Next[0]].Dir != t.Dir);
            turnRates.Add((int)Math.Round(10.0 * turns / seg.Count));
            Assert.True(seg.Max(t => t.PosX + t.PosY) > seg.Min(t => t.PosX + t.PosY) + 8, "the path climbs");
        }
        Assert.True(turnRates.Count >= 3, "turn rates: " + string.Join(",", turnRates));
    }

    [Fact]
    public void BossArena_IsPlacedAheadOfTheWorm()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.MinibossesSpawned = e.Content.RunTuning.MinibossAtMeter.Count;
        run.Awakening = 99.9;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        run = s.Run.Active!;
        Assert.Equal(RunPhase.Encounter, run.Phase);
        var here = run.Tiles[run.Position];
        var arena = run.Tiles[run.BossGateTile];
        double d = Math.Sqrt(Math.Pow(arena.PosX - here.PosX, 2) + Math.Pow(arena.PosY - here.PosY, 2));
        Assert.InRange(d, 1, 1.6);
    }

    // ---- 2. every tile shows what it did ---------------------------------------------------------------------------

    [Fact]
    public void EveryTileKind_ProducesAVisibleOutcome()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var c = e.Content;
        foreach (var def in c.BoardTiles.Values.Where(d => d.Category != TileCategory.BossGate && d.Category != TileCategory.Start))
        {
            var s = RunTestKit.StartRun(e);
            var run = s.Run.Active!;
            run.MinibossesSpawned = c.RunTuning.MinibossAtMeter.Count;
            RunTestKit.LineBoard(run, Enumerable.Repeat(def.Id, 14).ToArray());
            int seq = run.OutcomeSeq;
            var r = RunTestKit.Exec(e, ref s, new RunRoll());
            Assert.True(r.Ok, def.Id + ": " + r.Error);
            run = s.Run.Active!;
            var landed = run.TileOutcomes.LastOrDefault(o => o.Tile == run.Position && o.Source != "pass");
            bool decision = run.Phase is RunPhase.Event or RunPhase.Shop or RunPhase.Minigame || run.OutcomeSeq != seq;
            Assert.True(landed != null || decision, def.Id + " resolved silently");
            if (landed != null)
            {
                Assert.NotEmpty(landed.Lines);
                Assert.Equal(def.Id, landed.TileId);
                Assert.True(landed.Seq > 0);
                output.WriteLine($"{def.Id}: {landed.Source} " + string.Join(", ", landed.Lines.Select(l => l.Kind + (l.Id != null ? ":" + l.Id : "") + " " + l.Amount)));
            }
            else output.WriteLine($"{def.Id}: decision {run.Phase} / outcome {run.LastOutcome?.Source}");
            if (def.Category == TileCategory.Empty) Assert.Equal(OutcomeKind.Empty, landed!.Lines[0].Kind);
            if (def.Category is TileCategory.Enemy or TileCategory.Elite or TileCategory.Miniboss)
            {
                Assert.Equal("battle", landed!.Source);
                Assert.Contains(landed.Lines, l => l.Kind == OutcomeKind.Battle && l.Id == "won");
            }
            if (def.Id == "hazard_vortex") Assert.Contains(landed!.Lines, l => l.Kind == OutcomeKind.StepPenalty && l.Amount == 2);
        }
    }

    [Fact]
    public void NegativeTiles_AlwaysCostThePlayer_NeverAGain()
    {
        // playtest 0.1.308: a red tile showed a gain. Every curse / trap (TileValence.Negative) lands as a loss only.
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var c = e.Content;
        var negatives = c.BoardTiles.Values.Where(d => d.Valence == TileValence.Negative).ToList();
        Assert.Contains(negatives, d => d.Category == TileCategory.StatDebuff);
        Assert.Contains(negatives, d => d.Category == TileCategory.Hazard);
        foreach (var def in negatives)
        {
            Assert.True(def.Category is TileCategory.StatDebuff or TileCategory.Hazard, def.Id + " is " + def.Category);
            if (def.Category == TileCategory.StatDebuff) Assert.True(def.Value < 0, def.Id + " lowers its stat");
            var s = RunTestKit.StartRun(e);
            var run = s.Run.Active!;
            run.MinibossesSpawned = c.RunTuning.MinibossAtMeter.Count;
            run.Coins = 200;
            RunTestKit.LineBoard(run, Enumerable.Repeat(def.Id, 14).ToArray());
            Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok, def.Id);
            run = s.Run.Active!;
            var landed = run.TileOutcomes.LastOrDefault(o => o.Tile == run.Position && o.Source != "pass");
            Assert.NotNull(landed);
            output.WriteLine($"{def.Id}: " + string.Join(", ", landed!.Lines.Select(l => l.Kind + " " + l.Amount + (l.Good ? " good" : " bad"))));
            Assert.NotEmpty(landed.Lines);
            Assert.All(landed.Lines, l => Assert.False(l.Good, def.Id + ": " + l.Kind + " " + l.Amount + " shows as a gain"));
            Assert.All(landed.Lines, l => Assert.True(l.Amount <= 0 || l.Kind == OutcomeKind.StepPenalty, def.Id + ": " + l.Kind + " " + l.Amount));
        }
    }

    [Fact]
    public void Pickups_OnPass_AndAvoidedHazards_AreReported()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.MinibossesSpawned = e.Content.RunTuning.MinibossAtMeter.Count;
        RunTestKit.LineBoard(run, Enumerable.Repeat("shard", 14).ToArray());
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        run = s.Run.Active!;
        int steps = run.LastDice.Sum();
        Assert.Equal(steps - 1, run.TileOutcomes.Count(o => o.Source == "pass" && o.Lines.Any(l => l.Kind == OutcomeKind.Shards && l.Amount == 1)));
        Assert.Equal(run.TileOutcomes.Select(o => o.Seq).OrderBy(x => x), run.TileOutcomes.Select(o => o.Seq)); // in order

        // a boon keeps a hazard away: shown as Avoided, not silent
        RunTestKit.LineBoard(run, Enumerable.Repeat("hazard_spikes", 14).ToArray());
        run.SkipNegativeEvents = 1;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        run = s.Run.Active!;
        Assert.Contains(run.TileOutcomes, o => o.Tile == run.Position && o.Lines.Any(l => l.Kind == OutcomeKind.Avoided));
    }

    // ---- 3. start coins ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("ember_wastes_1")]
    [InlineData("glacier_rift_4")]
    public void FirstShop_AlwaysAllowsAPurchase(string levelId)
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var c = e.Content;
        var level = c.RunLevel(levelId)!;
        var table = c.RunShopTables[level.ShopTableId];
        long cheapest = table.SkillPrices.Values.Min();
        Assert.Equal(cheapest + c.RunTuning.StartCoinsMargin, RunSystem.StartCoins(c, level));
        for (long seed = 1; seed <= 30; seed++)
        {
            var s = TestHelpers.NewPlayer(e, seed);
            if (level.Number > 1) { s.Run.Levels[c.MainLevelId(level.Number - 1)] = new LevelProgress { Cleared = true }; s.Counters[Requirements.MainLevelCounter] = level.Number - 1; }
            Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = levelId }).Ok);
            Assert.True(s.Run.Active!.Coins >= RunSystem.StartCoins(c, level));
            for (int i = 0; i < 400 && s.Run.Active != null && s.Run.Active.Phase != RunPhase.Shop; i++)
                Assert.True(RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true }).Ok);
            var run = s.Run.Active;
            Assert.NotNull(run);
            Assert.Equal(1, run!.ShopsVisited);
            var skills = run.Shop.Where(o => o.Kind == ShopOfferKind.Skill).ToList();
            Assert.Contains(skills, o => o.Price == cheapest);                       // the first shop leads with a cheap skill
            Assert.True(run.Coins >= cheapest, $"seed {seed}: {run.Coins} coins at the first shop");
        }
    }

    // ---- 4. skill levels grow the skill's own numbers ------------------------------------------------------------

    [Fact]
    public void SpikedHide_LevelUp_GrowsItsReflectDamage_NotGenericStats()
    {
        var c = ContentDb.Default;
        var hide = c.Skill("counter_spiked")!;
        Assert.Empty(hide.Stats);                                                     // trigger-only skill
        var lv3 = SkillLeveling.At(c, hide, 3);
        Assert.Empty(lv3.Stats);                                                      // no +ATK / +HP package added
        Assert.Equal(30, hide.Triggers[0].Value);
        Assert.Equal(80, lv3.Triggers[0].Value, 6);                                   // the ladder: ×1, ×5/3, ×8/3
        Assert.Equal("Spiked Hide Lv 3", SkillText.Title(hide, 3));
        Assert.Equal("Spiked Hide Lv 3/3", SkillText.Title(c, hide, 3));
        Assert.Contains(SkillText.Effects(c, hide, 3), l => l.Contains("reflect 80%"));
        Assert.Contains(SkillText.Effects(c, hide, 1), l => l.Contains("reflect 30%"));
        Assert.Contains("reflect 30% → 50%", SkillText.UpgradeFull(c, hide, 1, 2));

        // in a run: the worm's ATK / HP do not move when Spiked Hide levels up, the fighter uses the leveled skill
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.Skills.Add("counter_spiked");
        var before = RunSystem.WormStats(c, run);
        for (int i = 0; i < 2; i++)
        {
            run = s.Run.Active!;
            run.Phase = RunPhase.Draft; run.MythicDraft = false; run.DraftOffer.Clear(); run.DraftOffer.Add("counter_spiked"); run.PendingDrafts = 1;
            Assert.True(RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = "counter_spiked" }).Ok);
        }
        run = s.Run.Active!;
        Assert.Equal(3, RunDraft.Level(run, "counter_spiked"));
        var after = RunSystem.WormStats(c, run);
        Assert.Equal(before.Atk, after.Atk, 6);
        Assert.Equal(before.MaxHp, after.MaxHp, 6);
        var spec = RunSystem.WormSpec(c, run);
        Assert.Equal(80, spec.Skill(c, "counter_spiked").Triggers[0].Value, 6);
    }

    [Fact]
    public void SkillLevels_ScaleValues_Chances_Stacks_Durations()
    {
        var c = ContentDb.Default;
        // the ladder: Common ×1, ×5/3, ×8/3 (15 → 25 → 40); Legendary ×1, ×8/5 (25 → 40)
        Assert.Equal(5 / 3.0, SkillLeveling.Factor(c, Rarity.Common, 2), 6);
        Assert.Equal(8 / 3.0, SkillLeveling.Factor(c, Rarity.Common, 3), 6);
        Assert.Equal(1.6, SkillLeveling.Factor(c, Rarity.Legendary, 2), 6);
        // trigger-only status skill: the chance grows (stacks stay while the chance is below 100%)
        var ember = c.Skill("fire_ember")!;                                           // On crit: 30% chance Heat ×3
        var e3 = SkillLeveling.At(c, ember, 3);
        Assert.Equal(80, e3.Triggers[0].Chance, 6);
        Assert.Equal(3, e3.Triggers[0].Stacks);
        // Grow = Chance: a damage proc grows its chance, the damage stays (Ice Shard: 30% → 80% chance of a 50% shard)
        var shard = c.Skill("ice_shard")!;
        var s3 = SkillLeveling.At(c, shard, 3);
        Assert.Equal(50, s3.Triggers[0].Value, 6);
        Assert.Equal(80, s3.Triggers[0].Chance, 6);
        // Grow = Fixed: Frost Armor's chill stays one stack while its shield grows
        var armor = SkillLeveling.At(c, c.Skill("ice_armor")!, 3);
        Assert.Equal(16, armor.Triggers[0].Value, 6);
        Assert.Equal(1, armor.Triggers[1].Stacks);
        // past 100% the chance is capped and the stacks grow instead
        var hot = new SkillDef { Id = "x", Name = "X" };
        hot.Triggers.Add(new SkillTrigger { When = TriggerKind.OnHit, Effect = EffectKind.ApplyStatus, Status = StatusId.Heat, Chance = 80 });
        var hot2 = SkillLeveling.At(c, hot, 2); var hot3 = SkillLeveling.At(c, hot, 3);
        Assert.Equal(100, hot2.Triggers[0].Chance, 6);
        Assert.Equal(1, hot2.Triggers[0].Stacks);
        Assert.Equal(100, hot3.Triggers[0].Chance, 6);
        Assert.Equal(2, hot3.Triggers[0].Stacks);                                      // 80% × 8/3 ≈ 2.1 stacks
        // a sure status (100%) grows its stacks at every level: Critical Mass's Exposed ×25 → ×40 (Legendary)
        Assert.Equal(40, SkillLeveling.At(c, c.Skill("poison_critical")!, 2).Triggers[0].Stacks);
        // a sure single-stack status (Freeze, Stun, Charm) lasts longer instead
        var sure = new SkillDef { Id = "sure", Name = "Sure", Rarity = Rarity.Legendary };
        sure.Triggers.Add(new SkillTrigger { When = TriggerKind.OnRageAttack, Effect = EffectKind.ApplyStatus, Status = StatusId.Freeze, Target = TargetKind.AllEnemies });
        var nova = SkillLeveling.At(c, sure, 2).Triggers[0];
        Assert.Equal(100, nova.Chance, 6);
        Assert.Equal(2, nova.Duration);
        // CC pass: lab Frost Nova's freeze stays (Fixed), its damage grows (100% → 160% ATK at research V)
        var nova5 = SkillLeveling.At(c, c.Skill("lab_frost_nova_5")!, 2);
        Assert.Equal((0, 160.0), (nova5.Triggers[0].Duration, nova5.Triggers[1].Value));
        // CC pass: linear Step growth (Knockout research V: 14% → 21% → 28% stun)
        Assert.Equal(new double[] { 14, 21, 28 }, new[] { 1, 2, 3 }.Select(lv => SkillLeveling.At(c, c.Skill("lab_retaliation_5")!, lv).Triggers[0].Chance));
        // buff: the value grows (Battle Rhythm: +2.5% → +4% ATK per combo)
        Assert.Equal(4, SkillLeveling.At(c, c.Skill("combo_rhythm")!, 2).Triggers[0].Stat.Percent, 6);
        // a drone summon grows its power (Mothership Link: 30% → 50% → 80% of ATK); sidekick actions are a count (Awaken: Command 1 → 2 → 3)
        var link = c.Skill("alien_rally")!;
        Assert.Equal(50, SkillLeveling.At(c, link, 2).Triggers[0].Value, 6);
        Assert.Equal(80, SkillLeveling.At(c, link, 3).Triggers[0].Value, 6);
        // passive stat skill: its own package grows
        Assert.Equal(40, SkillLeveling.At(c, c.Skill("poison_glands")!, 3).Stats[0].Flat, 6);
        // the content def is never mutated
        Assert.Equal(30, c.Skill("counter_spiked")!.Triggers[0].Value);
        // the run clock skill grows too now (Calm Mind: the boss awakens 9% → 15% → 24% slower)
        Assert.Equal(24, SkillLeveling.At(c, c.Skill("gen_calm_mind")!, 3).Triggers[0].Value, 6);
        var run = new ActiveRun();
        run.Skills.Add("gen_calm_mind");
        Assert.True(RunDraft.CanLevelUp(c, run, c.Skill("gen_calm_mind")));
        // Mythics never level (skill redesign: some are pure rule changes without a number)
        Assert.All(c.Skills.Values.Where(x => x.Draftable && x.Rarity != Rarity.Mythic && (x.Source == SkillSource.Run || x.Source == SkillSource.Laboratory)),
            x => Assert.True(SkillLeveling.Grows(c, x), x.Id + " does not grow"));
    }

    [Fact]
    public void RunStartBoon_LevelUp_AddsTheDifference()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.Phase = RunPhase.Draft; run.DraftOffer.Clear(); run.DraftOffer.Add("gen_calm_mind"); run.PendingDrafts = 1;
        Assert.True(RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = "gen_calm_mind" }).Ok);
        Assert.Equal(9, s.Run.Active!.AwakeningReductionPct, 6);
        run = s.Run.Active!;
        run.Phase = RunPhase.Draft; run.DraftOffer.Clear(); run.DraftOffer.Add("gen_calm_mind"); run.PendingDrafts = 1;
        Assert.True(RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = "gen_calm_mind" }).Ok);
        Assert.Equal(15, s.Run.Active!.AwakeningReductionPct, 6);                    // Lv 2/3: 9 → 15
    }

    [Fact]
    public void RunSkills_HaveThreeFixedTiers_WithLevelCaps_AndTierPrices()
    {
        var c = ContentDb.Default;
        var pool = c.Skills.Values.Where(s => s.Draftable && (s.Source == SkillSource.Run || s.Source == SkillSource.Laboratory)).ToList();
        Assert.True(pool.Count > 60);
        Assert.All(pool, s => Assert.True(s.Rarity is Rarity.Common or Rarity.Legendary or Rarity.Mythic, s.Id + ": " + s.Rarity));
        Assert.All(pool, s => Assert.Null(s.UpgradesFrom));                           // no upgrade chains
        foreach (var r in new[] { Rarity.Common, Rarity.Legendary, Rarity.Mythic }) Assert.Contains(pool, s => s.Rarity == r);
        Assert.Equal(3, c.RunTuning.MaxSkillLevelOf(Rarity.Common));
        Assert.Equal(2, c.RunTuning.MaxSkillLevelOf(Rarity.Legendary));
        Assert.Equal(1, c.RunTuning.MaxSkillLevelOf(Rarity.Mythic));
        // lab research levels raise the numbers, never the tier
        Assert.Equal(c.Skill("lab_second_wind")!.Rarity, c.Skill("lab_second_wind_5")!.Rarity);
        foreach (var t in c.RunShopTables.Values)
        {
            Assert.Equal(2 * t.SkillPrices[Rarity.Common], t.SkillPrices[Rarity.Legendary]);
            Assert.Equal(3 * t.SkillPrices[Rarity.Common], t.SkillPrices[Rarity.Mythic]);
            Assert.Equal(3, t.SkillPrices.Count);
        }
        Assert.Equal(new[] { Rarity.Common, Rarity.Legendary, Rarity.Mythic }, c.RunTuning.DraftRarityWeights.Keys.OrderBy(k => k));
        foreach (var m in c.MinigameTuning.Rewards.Where(r => r.Draft))
            Assert.Equal(new[] { Rarity.Common, Rarity.Legendary, Rarity.Mythic }, m.DraftRarityWeights.Keys.OrderBy(k => k));
        // Mythics never level: an owned Mythic is never offered again (draft, shop or miniboss choice)
        var run = new ActiveRun();
        var mythic = pool.First(s => s.Rarity == Rarity.Mythic && s.RequiresFamilyCount == 0 && s.Requires.Count == 0 && s.Source == SkillSource.Run);
        run.Skills.Add(mythic.Id);
        Assert.False(RunDraft.IsEligible(c, run, mythic));
        Assert.False(RunDraft.IsMythicEligible(c, run, mythic));
        Assert.Equal(mythic.Name, SkillText.Title(c, mythic, 1));
    }

    // ---- 5. XP: one level-up per battle --------------------------------------------------------------------------

    [Fact]
    public void BigBattle_GivesOneLevelUp_TheCarryPaysOnePerRoll()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var c = e.Content;
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.MinibossesSpawned = c.RunTuning.MinibossAtMeter.Count;
        RunTestKit.LineBoard(run, Enumerable.Repeat("elite", 14).ToArray());       // an elite fight: 18+ XP, need 3 at level 1
        var r = RunTestKit.Exec(e, ref s, new RunRoll());
        Assert.True(r.Ok);
        Assert.Equal(1, r.Events.Count(x => x.Type == "run_level_up"));
        run = s.Run.Active!;
        Assert.Equal(2, run.Level);
        Assert.Equal(RunPhase.Draft, run.Phase);
        Assert.Equal(1, run.PendingDrafts);
        // the carry is kept (up to two more levels' worth) and paid one level per roll
        Assert.Equal(RunSystem.XpNeeded(c, 2) + RunSystem.XpNeeded(c, 3) - 1, run.Xp);
        Assert.True(RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = run.DraftOffer[0] }).Ok);
        run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 14).ToArray());
        r = RunTestKit.Exec(e, ref s, new RunRoll());
        Assert.Equal(1, r.Events.Count(x => x.Type == "run_level_up"));
        Assert.Equal(3, s.Run.Active!.Level);
        var info = RunSystem.LevelInfo(c, s.Run.Active);
        Assert.Equal(3, info.Level);
        Assert.Equal(RunSystem.XpNeeded(c, 3), info.XpNeeded);
        Assert.Equal(s.Run.Active.Xp, info.Xp);
    }

    [Fact]
    public void FullRuns_NeverLevelUpTwiceInOneCommand()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        for (long seed = 1; seed <= 10; seed++)
        {
            var s = RunTestKit.StartRun(e, "ember_wastes_1", seed);
            for (int i = 0; i < 800 && s.Run.Active != null; i++)
            {
                var r = RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true, FarmShops = true });
                Assert.True(r.Ok);
                Assert.True(r.Events.Count(x => x.Type == "run_level_up") <= 1, $"seed {seed}: several level-ups in one step");
            }
            Assert.InRange(s.Run.LastSummary.Level, 6, 16); // 16: both miniboss lairs always stop the worm and pay their fight XP (0.1.308)
        }
    }

    // ---- 7. double rewards ---------------------------------------------------------------------------------------

    [Fact]
    public void DoubleRewards_DoublesEveryDoublableLine_AndTheSummaryShowsIt()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var c = e.Content;
        var s = TestHelpers.NewPlayer(e, 5);
        // level 10: its first clear pays the milestone chest (not doublable)
        s.Run.Levels[c.MainLevelId(9)] = new LevelProgress { Cleared = true };
        s.Counters[Requirements.MainLevelCounter] = 9;
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = c.MainLevelId(10) }).Ok);
        s.Run.Active!.PendingLoot.Add(Reward.Cur(Currencies.Grain, 77));
        s.Run.Active.PendingLoot.Add(new Reward(RewardKinds.Part, "*", 1, 1));
        RunTestKit.AutoToEnd(e, ref s);
        var sum = s.Run.LastSummary;
        Assert.True(sum.Won);
        Assert.True(sum.BiomassBonus);                                                // the daily Biomass surge applied
        Assert.Contains(sum.Rewards, l => l.Source == RunRewardLine.RunLoot && l.Reward.Id == Currencies.Grain && l.Doublable);
        Assert.Contains(sum.Rewards, l => l.Source == RunRewardLine.Completion && l.Doublable);
        Assert.Contains(sum.Rewards, l => l.Source == RunRewardLine.CompletionRoll && l.Doublable);
        Assert.Contains(sum.Rewards, l => l.Source == RunRewardLine.Milestone && !l.Doublable);
        Assert.Contains(sum.Rewards, l => l.Source == RunRewardLine.Feat && !l.Doublable);
        Assert.Equal(sum.Rewards.Count(l => l.Doublable), sum.Doublable.Count);
        var grantedBefore = Totals(sum.Granted);
        int partsBefore = s.Parts.Items.Count;
        var wallet = new Dictionary<string, long>(s.Wallet.Balances);

        Assert.True(RunTestKit.Exec(e, ref s, new RunDoubleRewards { AdToken = "ad" }).Ok);
        sum = s.Run.LastSummary;
        Assert.True(sum.Doubled);
        Assert.All(sum.Rewards, l => Assert.Equal(l.Doublable, l.Doubled));
        Assert.Equal(sum.Doublable.Count, sum.DoubledGranted.Count);
        // the wallet got every doublable currency line once more (surged Biomass included)
        foreach (var g in sum.Rewards.Where(l => l.Doubled && l.Reward.Kind == RewardKinds.Currency).GroupBy(l => l.Reward.Id))
            Assert.Equal(wallet.GetValueOrDefault(g.Key) + g.Sum(l => l.Reward.Amount), s.Wallet.Get(g.Key));
        Assert.True(s.Parts.Items.Count > partsBefore);                         // the item drop came again
        // the summary totals show the doubled amounts: doublable lines twice, milestone / feats once
        var grantedAfter = Totals(sum.Granted);
        foreach (var id in grantedAfter.Keys)
        {
            long single = sum.Rewards.Where(l => l.Reward.Kind == RewardKinds.Currency && l.Reward.Id == id).Sum(l => l.Reward.Amount);
            long doubled = sum.Rewards.Where(l => l.Doubled && l.Reward.Kind == RewardKinds.Currency && l.Reward.Id == id).Sum(l => l.Reward.Amount);
            Assert.Equal(single + doubled, grantedAfter[id]);
            Assert.Equal(grantedBefore.GetValueOrDefault(id) + doubled, grantedAfter[id]);
        }
        Assert.Equal(sum.Granted.Count(r => r.Kind != RewardKinds.Currency),
            sum.Rewards.Count(l => l.Reward.Kind != RewardKinds.Currency) + sum.Rewards.Count(l => l.Doubled && l.Reward.Kind != RewardKinds.Currency));
        output.WriteLine(string.Join("\n", sum.Rewards.Select(l => $"{l.Source}: {l.Reward} doublable={l.Doublable}")));
    }

    private static Dictionary<string, long> Totals(List<Reward> rewards) =>
        rewards.Where(r => r.Kind == RewardKinds.Currency).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

    // ---- 8. HUD stats -------------------------------------------------------------------------------------------------

    [Fact]
    public void WormStats_IncludeSkillsLevelsPerksAndCurses()
    {
        var e = RunTestKit.Engine();
        var c = e.Content;
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        var w0 = RunSystem.WormStats(c, run);
        Assert.Equal(1000, w0.MaxHp, 6);
        Assert.Equal(100, w0.Atk, 6);
        Assert.Equal(20, w0.Def, 6);
        Assert.Equal(run.WormHp, w0.Hp, 6);

        run.RunStats.Add(new StatMod(StatId.Atk, 0, 8));                             // shop perk
        run.RunStats.Add(new StatMod(StatId.Hp, 0, -10));                            // curse
        run.Skills.Add("gen_guard");                                                 // +15% DEF
        run.SkillLevels["gen_guard"] = 3;                                            // Lv 3: +40% DEF
        var w = RunSystem.WormStats(c, run);
        Assert.Equal(108, w.Atk, 6);
        Assert.Equal(900, w.MaxHp, 6);
        Assert.Equal(20 * 1.4, w.Def, 6);
        Assert.Equal(w.Atk, w.Stats.Value(StatId.Atk), 6);
        run.WormHp = 450;
        Assert.Equal(450, RunSystem.WormStats(c, run).Hp, 6);
    }
}
