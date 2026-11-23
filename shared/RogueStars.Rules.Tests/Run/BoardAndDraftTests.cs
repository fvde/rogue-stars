using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

public class BoardTests
{
    public static IEnumerable<object[]> Levels() =>
        new[] { "ember_wastes_1", "ember_wastes_10", "glacier_rift_5", "static_jungle_9", "the_burrow_3", "dark_side_7" }.Select(l => new object[] { l });

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
            var reach = RunBoard.Reachable(b.Tiles, 0);
            Assert.Equal(b.Tiles.Count, reach.Count);                              // connected from the start
            Assert.Contains(b.GateTile, reach);                                     // boss gate reachable
            Assert.Single(b.Tiles, t => t.TileId == "boss_gate");
            Assert.All(b.Tiles, t => Assert.True(c.BoardTiles.ContainsKey(t.TileId), t.TileId));
            Assert.All(b.Tiles.Where(t => t.Id != b.GateTile), t => Assert.NotEmpty(t.Next));
            // every tile can still reach the gate (no dead ends / trapped loops)
            Assert.All(b.Tiles, t => Assert.Contains(b.GateTile, RunBoard.Reachable(b.Tiles, t.Id)));
            foreach (var f in level.FixedTiles.Select(f => f.TileId).Distinct())
                Assert.Contains(b.Tiles, t => t.TileId == f);
            Assert.Equal(boss.BossTileCount, b.Tiles.Count(t => boss.BossTileIds.Contains(t.TileId)));
            Assert.True(b.Tiles.Count(t => t.TileId == "miniboss") <= 1);         // "once" tiles
            Assert.True(b.MainPath.Count >= level.BoardLength);
        }
    }

    [Fact]
    public void Generation_HasBranchesAndLoops_AcrossSeeds()
    {
        var level = ContentDb.Default.RunLevel("ember_wastes_5")!;
        int branches = 0, loops = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var b = RunBoard.Generate(ContentDb.Default, level, new Rng(seed));
            branches += b.Tiles.Count(t => t.Next.Count > 1);
            loops += b.Tiles.Count(t => t.Next.Any(n => n < t.Id && b.Tiles[n].Y == 0 && t.Y == -1));
        }
        Assert.True(branches > 20);
        Assert.True(loops > 5);
    }

    [Fact]
    public void SameSeed_SameBoard()
    {
        var level = ContentDb.Default.RunLevel("glacier_rift_3")!;
        var a = RunBoard.Generate(ContentDb.Default, level, new Rng(7));
        var b = RunBoard.Generate(ContentDb.Default, level, new Rng(7));
        Assert.Equal(a.Tiles.Select(t => t.TileId + string.Join(",", t.Next)), b.Tiles.Select(t => t.TileId + string.Join(",", t.Next)));
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
        Assert.False(RunDraft.IsEligible(c, empty, c.Skill("fire_ember_2")));       // chain needs base
        Assert.False(RunDraft.IsEligible(c, empty, c.Skill("fire_flare")));         // family gate 2
        Assert.True(RunDraft.IsEligible(c, empty, c.Skill("fire_ember")));
        Assert.False(RunDraft.IsEligible(c, empty, c.Skill("lab_second_wind")));    // lab skill not toggled on
        Assert.False(RunDraft.IsEligible(c, empty, c.Skill("enemy_rend")));         // not draftable

        var two = Run("fire_ember", "fire_kindle");
        Assert.True(RunDraft.IsEligible(c, two, c.Skill("fire_ember_2")));
        Assert.True(RunDraft.IsEligible(c, two, c.Skill("fire_flare")));
        Assert.False(RunDraft.IsEligible(c, two, c.Skill("fire_ember")));           // owned

        var four = Run("fire_ember", "fire_kindle", "fire_flare", "ice_shard");
        Assert.False(RunDraft.IsEligible(c, four, c.Skill("fire_phoenix")));        // requires fire_inferno + family 4
        four.Skills.Add("fire_inferno");
        Assert.True(RunDraft.IsEligible(c, four, c.Skill("fire_phoenix")));

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
    public void DraftPick_ReplacesUpgradedSkill_AndTracksPity()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.Skills.Add("fire_ember");
        run.Phase = RunPhase.Draft;
        run.PendingDrafts = 1;
        run.DraftOffer.AddRange(new[] { "fire_ember_2", "gen_guard", "crit_keen" });
        Assert.False(RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = "fire_inferno" }).Ok);
        Assert.True(RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = "fire_ember_2" }).Ok);
        run = s.Run.Active!;
        Assert.Contains("fire_ember_2", run.Skills);
        Assert.DoesNotContain("fire_ember", run.Skills);
        Assert.Contains("fire_ember", run.ReplacedSkills);
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
            if (f != SkillFamily.None) Assert.True(run.Count(s => s.Family == f) >= 5, f.ToString());
        foreach (Rarity r in Enum.GetValues(typeof(Rarity))) Assert.Contains(run, s => s.Rarity == r);
        Assert.Contains(run, s => s.UpgradesFrom != null);
        Assert.Contains(run, s => s.Requires.Count > 0);
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
        Assert.Equal(c.Campaign.Portals, c.Portals.Values.Count(p => p.Kind == PortalKind.Main));
        Assert.All(c.Portals.Values.Where(p => p.Kind == PortalKind.Main), p => Assert.Equal(c.Campaign.LevelsPerPortal, p.LevelIds.Count));
        Assert.Equal(c.Campaign.Portals, c.Portals.Values.Where(p => p.Kind == PortalKind.Main).Select(p => p.Name).Distinct().Count());
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
        // loot feeds every system: currencies, parts, runes, blueprints, aliens, tokens
        var kinds = c.RunLootTables["loot_p1"].Entries.Select(e => e.Kind).ToHashSet();
        Assert.Superset(new HashSet<string> { RewardKinds.Currency, RewardKinds.Part, RewardKinds.Rune, RewardKinds.Blueprint, RewardKinds.Alien }, kinds);
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
            double family = c.Campaign.FamilyStrengthPct[(n - 1) / c.Campaign.LevelsPerPortal % c.Campaign.FamilyStrengthPct.Count];
            double eff = c.ThreatMultiplier(l.Threat) * (1 + l.Modifiers.EnemyHpPct / 100.0) / (1 + family / 100.0);
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
