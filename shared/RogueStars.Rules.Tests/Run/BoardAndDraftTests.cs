using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// GDD §3.1: the board is a long path that never repeats, generated ahead of the worm in segments (playtest 0.1.86: the
/// looping board felt weird and the same fork came back three times in one run).
/// </summary>
public class BoardTests
{
    public static IEnumerable<object[]> Levels() =>
        new[] { "main_1", "main_10", "main_15", "main_29", "the_burrow_3", "dark_side_7" }.Select(l => new object[] { l });

    [Theory]
    [MemberData(nameof(Levels))]
    public void Generation_Invariants(string levelId)
    {
        var c = ContentDb.Default;
        var level = c.RunLevel(levelId)!;
        var boss = c.RunBosses[level.BossId];
        for (long seed = 1; seed <= 30; seed++)
        {
            var b = RunBoard.Generate(c, level, new Rng((ulong)seed));
            Assert.Equal(c.RunTuning.InitialSegments, b.Segments);
            var reach = RunBoard.Reachable(b.Tiles, 0);
            Assert.Equal(b.Tiles.Count, reach.Count);                              // connected from the start
            Assert.All(b.Tiles, t => Assert.True(c.BoardTiles.ContainsKey(t.TileId), t.TileId));
            Assert.All(b.Tiles, t => Assert.All(t.Next, n => Assert.True(n > t.Id, "edges only point forward: no tile is reachable twice")));
            Assert.All(b.Tiles.Where(t => t.Id != b.EndTile), t => Assert.NotEmpty(t.Next));
            Assert.Empty(b.Tiles[b.EndTile].Next);                                  // the open end: the next segment starts here
            Assert.All(b.Tiles, t => Assert.Contains(b.EndTile, RunBoard.Reachable(b.Tiles, t.Id))); // no dead ends
            Assert.DoesNotContain(b.Tiles, t => t.TileId == "boss_gate");           // the arena opens when the boss wakes
            foreach (var f in level.FixedTiles.GroupBy(f => f.TileId))
                Assert.True(b.Tiles.Count(t => t.TileId == f.Key) >= b.Segments * f.Count(), f.Key);
            Assert.Equal(boss.BossTileCount * b.Segments, b.Tiles.Count(t => boss.BossTileIds.Contains(t.TileId)));
            Assert.True(b.Tiles.Count(t => t.TileId == "miniboss") <= b.Segments); // "once" tiles: once per segment
            Assert.True(b.MainPath.Count >= level.BoardLength * b.Segments);
        }
    }

    [Fact]
    public void Generation_HasForks_AndSegmentsVaryTheirMix()
    {
        var c = RunTestKit.Classic;
        var level = c.RunLevel("main_5")!;
        int forks = 0;
        var enemyShare = new List<double>();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var rng = new Rng(seed);
            var b = RunBoard.Generate(c, level, rng);
            forks += b.Tiles.Count(t => t.Next.Count > 1);
            int from = b.Tiles.Count;
            RunBoard.AppendSegment(c, level, b, rng);
            var part = b.Tiles.Skip(from).ToList();
            enemyShare.Add(part.Count(t => c.BoardTiles[t.TileId].Category is TileCategory.Enemy or TileCategory.Elite) / (double)part.Count);
        }
        Assert.True(forks > 20);
        Assert.True(enemyShare.Max() - enemyShare.Min() > 0.2, "segment themes change the tile mix");
    }

    [Fact]
    public void SameSeed_SameBoard()
    {
        var level = ContentDb.Default.RunLevel("main_13")!;
        var a = RunBoard.Generate(ContentDb.Default, level, new Rng(7));
        var b = RunBoard.Generate(ContentDb.Default, level, new Rng(7));
        Assert.Equal(a.Tiles.Select(t => t.TileId + string.Join(",", t.Next)), b.Tiles.Select(t => t.TileId + string.Join(",", t.Next)));
    }

    /// <summary>Whole runs (one and two dice, a stalled meter up to the 45-roll limit): no tile and no fork twice, board always ahead.</summary>
    [Theory]
    [InlineData("main_1", false)]
    [InlineData("main_8", false)]
    [InlineData("dark_side_4", false)]
    [InlineData("main_8", true)]
    public void Run_NeverVisitsATileOrForkTwice_AndNeverRunsOutOfBoard(string levelId, bool stalledMeter)
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var c = e.Content;
        for (int seed = 1; seed <= 12; seed++)
        {
            var s = TestHelpers.NewPlayer(e, seed);
            s.Flags.Add(Features.DevUnlockAllFlag);
            var level = c.RunLevel(levelId)!;
            bool dark = level.PortalId == ContentDb.DarkSidePortalId;
            if (level.Number > 1)
            {
                if (dark) s.Run.Levels[c.DarkSideLevel(level.Number - 1).Id] = new LevelProgress { Cleared = true };
                else { s.Run.Levels[c.MainLevelId(level.Number - 1)] = new LevelProgress { Cleared = true }; s.Counters[Requirements.MainLevelCounter] = level.Number - 1; }
            }
            s.Wallet.Add(Currencies.Stamina, 100);
            Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = levelId, Mode = dark ? RunMode.DarkSide : RunMode.Normal }).Ok);
            if (stalledMeter) s.Run.Active!.AwakeningReductionPct = 90;
            var forks = new List<int>();
            int maxRolls = s.Run.Active!.MaxRolls;
            for (int i = 0; i < 4000 && s.Run.Active != null; i++)
            {
                var a = s.Run.Active;
                if (a.Phase == RunPhase.ChooseBranch) forks.Add(a.Position);
                Assert.Equal(a.Trail.Count, a.Trail.Distinct().Count());              // no tile twice
                var r = RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true });
                Assert.True(r.Ok, r.Error);
                var after = s.Run.Active;
                if (after != null && after.BossGateTile < 0 && after.Phase == RunPhase.AwaitRoll)
                    Assert.True(RunBoard.StepsToEnd(after.Tiles, after.Position, after.BoardEnd) > 0, "board ahead");
                if (r.Events.Any(ev => ev.Type == "run_roll") && after != null && after.LastDice.Count > 0 && after.Phase != RunPhase.Encounter)
                    Assert.True(after.Trail.Count > 1);
            }
            Assert.Null(s.Run.Active);
            Assert.Equal(forks.Count, forks.Distinct().Count());                      // no fork twice
            if (stalledMeter) Assert.Equal(maxRolls, s.Run.LastSummary.Rolls);         // walked to the 45-roll limit
        }
    }

    [Fact]
    public void BoardGrowsAhead_FarEnoughFor45RollsOfTwoDice()
    {
        var c = ContentDb.Default;
        var level = c.RunLevel("main_8")!;
        var rng = new Rng(3);
        var board = RunBoard.Generate(c, level, rng);
        var run = new ActiveRun { Tiles = board.Tiles, BoardEnd = board.EndTile, BoardSegments = board.Segments, LevelId = level.Id, Rng = rng };
        var e = new GameEngine(c);
        // walk 45 rolls of double sixes along the spine: the board keeps extending ahead
        int walked = 0;
        for (int roll = 0; roll < 45; roll++)
        {
            Assert.True(RunBoard.StepsToEnd(run.Tiles, run.Position, run.BoardEnd) >= 0);
            for (int step = 0; step < 12; step++)
            {
                if (run.Tiles[run.Position].Next.Count == 0)
                {
                    // the rules extend before every roll; emulate that with a roll-sized look-ahead
                    var b = RunBoard.Wrap(run.Tiles, run.BoardEnd, run.BoardSegments);
                    RunBoard.AppendSegment(e.Content, e.Content.RunLevel(run.LevelId)!, b, run.Rng);
                    run.BoardEnd = b.EndTile; run.BoardSegments = b.Segments;
                }
                run.Position = run.Tiles[run.Position].Next[0];
                walked++;
            }
        }
        Assert.Equal(45 * 12, walked);
        Assert.True(run.Tiles.Count > 45 * 12);
        Assert.All(run.Tiles, t => Assert.All(t.Next, n => Assert.True(n > t.Id)));
    }

    [Fact]
    public void FullMeter_OpensTheBossArenaAhead_TheNextStepWalksInAndFights()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 20).ToArray());
        run.MinibossesSpawned = e.Content.RunTuning.MinibossAtMeter.Count;
        run.Awakening = 99.9;
        var r = RunTestKit.Exec(e, ref s, new RunRoll());
        Assert.True(r.Ok);
        run = s.Run.Active!;
        Assert.Equal(RunPhase.Encounter, run.Phase);
        Assert.Equal(RunDecision.Encounter, RunSystem.Decision(e.Content, run));
        Assert.False(RunSystem.RequiresPlayer(e.Content, run));                        // a clear step: auto walks in
        Assert.Contains(r.Events, ev => ev.Type == "run_encounter" && ev.Args[0] == "boss");
        Assert.Equal("boss_gate", run.Tiles[run.BossGateTile].TileId);
        Assert.Equal(new List<int> { run.BossGateTile }, run.Tiles[run.Position].Next); // the path ends in the arena
        Assert.False(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);                     // no rolling past it
        var f = RunTestKit.Exec(e, ref s, new RunEncounter());
        Assert.True(f.Ok);
        Assert.Contains(f.Events, ev => ev.Type == "run_boss_fight");
        Assert.True(s.Run.LastSummary.Won);
    }

    [Fact]
    public void GravityVortex_ShortensTheNextRoll_NeverPushesBack()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("hazard_vortex", 6).Concat(Enumerable.Repeat("empty", 40)).ToArray());
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        run = s.Run.Active!;
        Assert.Equal(run.LastDice[0], run.Position);                                   // landed where the die said
        Assert.Equal(2, run.StepPenaltyNext);
        int before = run.Position;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        run = s.Run.Active!;
        Assert.Equal(Math.Max(1, run.LastDice[0] - 2), run.Position - before);        // the next roll is shorter, never backwards
        Assert.Equal(run.Trail.Count, run.Trail.Distinct().Count());
    }
}


public class DraftTests
{
    private static ActiveRun Run(params string[] skills)
    {
        var r = new ActiveRun();
        r.Skills.AddRange(skills);
        return r;
    }

    [Fact]
    public void Offer_IsThreeDistinctEligibleSkills()
    {
        var c = ContentDb.Default;
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var run = Run("fire_ember");
            var offer = RunDraft.Generate(c, run, new Rng(seed));
            Assert.Equal(3, offer.Count);
            Assert.Equal(3, offer.Distinct().Count());
            Assert.All(offer, id => Assert.True(RunDraft.IsEligible(c, run, c.Skill(id))));
        }
    }

    [Fact]
    public void Gating_UpgradeChains_FamilyCount_Prerequisites_LabPool()
    {
        var c = ContentDb.Default;
        var empty = Run();
        Assert.Null(c.Skill("fire_ember_2"));                                         // no upgrade chains: one skill, levels
        Assert.False(RunDraft.IsEligible(c, empty, c.Skill("fire_flashpoint")));    // family gate 2
        Assert.True(RunDraft.IsEligible(c, empty, c.Skill("fire_ember")));
        Assert.False(RunDraft.IsEligible(c, empty, c.Skill("lab_second_wind")));    // lab skill not toggled on
        Assert.False(RunDraft.IsEligible(c, empty, c.Skill("enemy_rend")));         // not draftable

        var two = Run("fire_ember", "fire_kindle");
        Assert.True(RunDraft.IsEligible(c, two, c.Skill("fire_flashpoint")));
        Assert.True(RunDraft.IsEligible(c, two, c.Skill("fire_ember")));            // owned Common: offered as a level-up
        two.SkillLevels["fire_ember"] = 3;
        Assert.False(RunDraft.IsEligible(c, two, c.Skill("fire_ember")));           // Lv 3/3

        // Mythics are never in the normal draft: one ungated pool for miniboss choices
        var four = Run("fire_ember", "fire_kindle", "fire_flashpoint", "fire_controlled");
        Assert.False(RunDraft.IsEligible(c, four, c.Skill("fire_heat_siphon")));
        Assert.True(RunDraft.IsMythicEligible(c, empty, c.Skill("fire_heat_siphon")));

        // the synergy lock: a package synergy's run skills only once researched (UnlockedFamilies; empty = an old run: all)
        var locked = Run();
        locked.Loadout.UnlockedFamilies.AddRange(c.RunTuning.StarterSynergies);
        Assert.False(RunDraft.IsEligible(c, locked, c.Skill("ice_shard")));
        Assert.False(RunDraft.IsMythicEligible(c, locked, c.Skill("mythic_winters_grip")));
        Assert.True(RunDraft.IsEligible(c, locked, c.Skill("bleed_serrated")));
        Assert.True(RunDraft.IsEligible(c, locked, c.Skill("crit_keen")));        // generic (the former Crit picks)
        locked.Loadout.UnlockedFamilies.Add(SkillFamily.Ice);
        Assert.True(RunDraft.IsEligible(c, locked, c.Skill("ice_shard")));
        Assert.False(RunDraft.IsEligible(c, locked, c.Skill("sig_ice")));         // signatures are never drafted

        var lab = Run();
        lab.Loadout.DraftPoolSkillIds.Add("lab_second_wind");
        Assert.True(RunDraft.IsEligible(c, lab, c.Skill("lab_second_wind")));
    }

    [Fact]
    public void SynergySlot_FavoursOwnedFamilies()
    {
        var c = ContentDb.Default;
        int withFamily = 0;
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var run = Run("bleed_serrated", "bleed_razor");
            if (RunDraft.Generate(c, run, new Rng(seed)).Any(id => c.Skill(id).Family == SkillFamily.Bleed)) withFamily++;
        }
        Assert.Equal(60, withFamily);
    }

    [Fact]
    public void HiddenPity_GuaranteesLegendary()
    {
        var c = ContentDb.Default;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var run = Run();
            run.DraftsWithoutLegendary = c.RunTuning.LegendaryPity;
            Assert.Contains(RunDraft.Generate(c, run, new Rng(seed)), id => c.Skill(id).Rarity >= Rarity.Legendary);
        }
    }

    [Fact]
    public void DraftPick_LevelsUpAnOwnedSkill_AndTracksPity()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.Skills.Add("fire_ember");
        run.Phase = RunPhase.Draft;
        run.PendingDrafts = 1;
        run.DraftOffer.AddRange(new[] { "fire_ember", "gen_guard", "crit_keen" });
        Assert.False(RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = "fire_inferno" }).Ok);
        Assert.True(RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = "fire_ember" }).Ok);
        run = s.Run.Active!;
        Assert.Single(run.Skills, "fire_ember");
        Assert.Equal(2, RunDraft.Level(run, "fire_ember"));
        Assert.Equal(1, run.DraftsWithoutLegendary);
        Assert.Equal(1, s.Counter("run_skills_drafted"));
    }

    [Fact]
    public void Reroll_ConsumesRerolls()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.Phase = RunPhase.Draft;
        run.PendingDrafts = 1;
        run.DraftOffer.AddRange(new[] { "gen_guard", "crit_keen", "gen_swift" });
        int rerolls = run.DraftRerolls;
        Assert.Equal(ContentDb.Default.RunTuning.DraftRerollsPerRun, rerolls);
        for (int i = 0; i < rerolls; i++) Assert.True(RunTestKit.Exec(e, ref s, new RunDraftReroll()).Ok);
        Assert.False(RunTestKit.Exec(e, ref s, new RunDraftReroll()).Ok);
    }
}

public class ContentTests
{
    [Fact]
    public void Skills_CoverFamiliesAndRarities()
    {
        var c = ContentDb.Default;
        var run = c.Skills.Values.Where(s => s.Source == SkillSource.Run && s.Draftable).ToList();
        Assert.True(run.Count >= 70, "run skills: " + run.Count);
        foreach (SkillFamily f in Enum.GetValues(typeof(SkillFamily)))
            if (f != SkillFamily.None && f != SkillFamily.Crit) Assert.True(run.Count(s => s.Family == f) >= 6, f.ToString()); // 2 Common + 2 Legendary + 2–3 Mythics
        Assert.DoesNotContain(run, s => s.Family == SkillFamily.Crit); // Crit is no synergy any more: its picks are generic
        foreach (Rarity r in new[] { Rarity.Common, Rarity.Legendary, Rarity.Mythic }) Assert.Contains(run, s => s.Rarity == r); // the three run tiers
        Assert.DoesNotContain(run, s => s.UpgradesFrom != null);
        Assert.DoesNotContain(run, s => s.Rarity == Rarity.Mythic && (s.Requires.Count > 0 || s.RequiresFamilyCount > 0)); // one ungated Mythic pool
        Assert.Contains(run, s => s.RequiresFamilyCount > 0);
        foreach (var s in c.Skills.Values)
        {
            if (s.UpgradesFrom != null) Assert.NotNull(c.Skill(s.UpgradesFrom));
            foreach (var r in s.Requires) Assert.NotNull(c.Skill(r));
        }
    }

    [Fact]
    public void RunContent_IsComplete_AndReferencesResolve()
    {
        var c = ContentDb.Default;
        Assert.Equal(c.Campaign.MaterializedLevels, c.Portals.Values.Count(p => p.Kind == PortalKind.Main));
        Assert.All(c.Portals.Values.Where(p => p.Kind == PortalKind.Main), p => Assert.Single(p.LevelIds)); // one portal per level
        Assert.Equal(c.Campaign.Biomes.Count, c.Portals.Values.Where(p => p.Kind == PortalKind.Main).Select(p => p.Name).Distinct().Count());
        Assert.Single(c.Portals.Values, p => p.Kind == PortalKind.Burrow);
        Assert.Single(c.Portals.Values, p => p.Kind == PortalKind.DarkSide);
        Assert.True(c.Enemies.Values.Count(e => !e.Boss) >= 18);
        Assert.True(c.Enemies.Values.Count(e => e.Elite) >= 5);
        Assert.Equal(4 + 4 * c.Campaign.BossVariants, c.RunBosses.Count); // 4 family bosses + variants of the endless portals
        Assert.True(c.RandomRunEvents.Count >= 12);
        Assert.NotNull(c.Regen(Currencies.Stamina));
        Assert.NotNull(c.Regen(Currencies.BurrowKeys));
        foreach (var e in c.Enemies.Values) foreach (var sk in e.SkillIds) Assert.NotNull(c.Skill(sk));
        foreach (var b in c.RunBosses.Values)
        {
            Assert.True(c.Enemies.ContainsKey(b.EnemyId));
            Assert.All(b.BossTileIds, id => Assert.Equal(TileCategory.BossTile, c.BoardTiles[id].Category));
        }
        foreach (var l in c.RunLevels.Values)
        {
            Assert.True(c.TilePools.ContainsKey(l.TilePoolId));
            Assert.True(c.RunLootTables.ContainsKey(l.LootTableId));
            Assert.All(l.EnemyPool.Concat(l.ElitePool).Append(l.MinibossId), id => Assert.True(c.Enemies.ContainsKey(id), id));
            Assert.All(l.FixedTiles, f => Assert.True(c.BoardTiles.ContainsKey(f.TileId)));
        }
        foreach (var p in c.TilePools.Values) Assert.All(p, w => Assert.True(c.BoardTiles.ContainsKey(w.Id), w.Id));
        foreach (var ev in c.RunEvents.Values)
        {
            Assert.InRange(ev.Options.Count, 2, 3);
            Assert.All(ev.Options, o => Assert.False(string.IsNullOrEmpty(o.RiskText)));
            var def = ev.Options[ev.DefaultOption];
            Assert.Equal(0, def.CoinCost + def.ShardCost);
        }
        // loot feeds the systems with currencies, runes and pull tokens; never shards or shard items (owner: shards only from pulls) and
        // never parts (parts v2: Parts pulls, boss first clears and Dark Side clears are the part sources)
        var kinds = c.RunLootTables["loot_p1"].Entries.Select(e => e.Kind).ToHashSet();
        Assert.Superset(new HashSet<string> { RewardKinds.Currency, RewardKinds.Rune }, kinds);
        foreach (var t in c.RunLootTables.Values) Assert.DoesNotContain(t.Entries, e => e.Kind == RewardKinds.Part);
        foreach (var t in c.RunLootTables.Values)
            Assert.DoesNotContain(t.Entries, e => e.Kind == RewardKinds.Blueprint || e.Kind == RewardKinds.Alien || e.Kind == RewardKinds.HeroShards || e.Kind == RewardKinds.Mount);
        Assert.Contains(c.RunLootTables["loot_p1"].Entries, e => e.Id == Currencies.AlienTokens);
        Assert.Contains(c.RunLootTables["loot_p1"].Entries, e => e.Id == Currencies.BlueprintTokens);
    }

    [Fact]
    public void Campaign_ReachesEveryFeatureGate_WithARisingThreatCurve()
    {
        var c = ContentDb.Default;
        var mains = c.Portals.Values.Where(p => p.Kind == PortalKind.Main).OrderBy(p => p.Order).SelectMany(p => p.LevelIds).Select(id => c.RunLevel(id)!).ToList();
        // Every MainLevel gate of the unlock order (Dark Side included) is a real level.
        foreach (var f in c.FeatureOrder)
            foreach (var r in f.Requires.Items.Where(r => r.Kind == RequirementKind.MainLevel))
                Assert.True(r.Target <= mains.Count, f.Id + " gated at main level " + r.Target + " of " + mains.Count);
        Assert.True(mains.Count >= 300, "a campaign for years");
        // Effective enemy strength (threat step × fractional bonus, biome family offset removed) rises with every level; stamina stays within the cap.
        double prev = 0;
        for (int n = 1; n <= mains.Count; n++)
        {
            var l = mains[n - 1];
            Assert.Equal(n, RunSystem.MainLevelNumber(c, l.Id));
            double eff = c.ThreatMultiplier(l.Threat) * (1 + l.Modifiers.EnemyHpPct / 100.0) / EndlessCampaignTests.FamilyFactor(c, n);
            Assert.True(eff > prev, "level " + n);
            prev = eff;
            Assert.InRange(l.StaminaCost, 5, c.Campaign.StaminaMax);
            Assert.True(c.RunLootTables.ContainsKey(l.LootTableId));
        }
        // The Dark Side starts just below its gate level.
        var dark1 = c.RunLevel("dark_side_1")!;
        Assert.InRange(dark1.Threat, c.Campaign.ThreatAt(c.Campaign.DarkSideFromLevel) - 8, c.Campaign.ThreatAt(c.Campaign.DarkSideFromLevel));
    }

    [Fact]
    public void ThreatCurve_GrowsForYears()
    {
        var c = ContentDb.Default;
        Assert.Equal(1, c.ThreatMultiplier(0), 6);
        double prev = 0;
        for (int t = 0; t <= 200; t++) { double m = c.ThreatMultiplier(t); Assert.True(m > prev); prev = m; }
        Assert.InRange(c.ThreatMultiplier(29), 15, 30);
        Assert.True(c.ThreatMultiplier(100) > 1000);
    }
}
