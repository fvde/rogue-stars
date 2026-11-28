using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

public class EndlessCampaignTests
{
    /// <summary>Biome family strength factor of main level <paramref name="n"/> (base offset × late offset).</summary>
    internal static double FamilyFactor(ContentDb c, int n)
    {
        var k = c.Campaign;
        int family = ContentDb.FamilyIndex(c.CampaignBiome(n).Family);
        double f = 1 + k.FamilyStrengthPct[family % k.FamilyStrengthPct.Count] / 100.0;
        if (k.ChapterOf(n) >= k.LateFamilyFromChapter) f *= 1 + k.LateFamilyStrengthPct[family % k.LateFamilyStrengthPct.Count] / 100.0;
        return f;
    }

    [Fact]
    public void MainLevels_AreOnePortalEach_OnTheSameRisingCurve()
    {
        var c = new ContentDb();
        var ids = new HashSet<string>();
        double prev = 0;
        for (int n = 1; n <= 3000; n++)
        {
            var l = c.MainLevel(n);
            Assert.NotNull(l);
            Assert.Equal(n, l.Number);
            Assert.Equal(n, RunSystem.MainLevelNumber(c, l.Id));
            Assert.True(ids.Add(l.Id), l.Id);
            // every level is its own portal, named after its biome
            var portal = c.Portal(l.PortalId);
            Assert.Same(portal, c.MainPortal(n - 1));
            Assert.Equal(new[] { l.Id }, portal.LevelIds);
            Assert.Equal(PortalKind.Main, portal.Kind);
            Assert.Equal(n - 1, portal.Order);
            var biome = c.CampaignBiome(n);
            Assert.Equal(biome.Id, l.Biome);
            Assert.Equal(biome.Name, l.Name);
            Assert.Equal(biome.Name, portal.Name);
            double eff = c.ThreatMultiplier(l.Threat) * (1 + l.Modifiers.EnemyHpPct / 100.0) / FamilyFactor(c, n);
            Assert.True(eff > prev, "level " + n);
            prev = eff;
            Assert.NotNull(c.RunLootTable(l.LootTableId));
            Assert.True(c.RunBosses.ContainsKey(l.BossId), l.BossId);
            Assert.True(c.TilePools.ContainsKey(l.TilePoolId));
        }
        // Materialized content stays the first 500 levels; everything else lives in a bounded cache.
        Assert.Equal(c.Campaign.MaterializedLevels, c.Portals.Values.Count(p => p.Kind == PortalKind.Main));
        Assert.Equal("main_1", c.MainLevelId(1));
        Assert.Equal("main_501", c.MainLevelId(501));
        Assert.Equal("main_501", c.MainLevel(501).PortalId);
        Assert.Null(c.RunLevel("main_51_1")); // the old portal-of-10 ids are gone
        Assert.Null(c.RunLevel("crystal_reef_5"));
        Assert.Null(c.RunLevel("main_0"));
        Assert.Null(c.RunLevel("main_07"));
        Assert.Null(c.Portal("main_0"));
    }

    [Fact]
    public void BiomeOrder_NeverRepeatsABiomeOrFamilyTwiceInARow_UsesEveryBiome_AndIsDeterministic()
    {
        var c = new ContentDb();
        var other = new ContentDb { GeneratedCacheLevels = 10 };
        var k = c.Campaign;
        int b = k.Biomes.Count;
        Assert.Equal(b, k.Biomes.Select(x => x.Id).Distinct().Count());
        Assert.Equal(0, c.CampaignBiomeIndex(1)); // the tutorial run's ember pool
        Assert.Equal("ember", c.CampaignBiome(1).Family);
        var used = new HashSet<int>();
        for (int n = 1; n <= 1000; n++)
        {
            int i = c.CampaignBiomeIndex(n);
            Assert.InRange(i, 0, b - 1);
            used.Add(i);
            Assert.Equal(i, other.CampaignBiomeIndex(n)); // the same for every player / instance
            Assert.Equal(k.Biomes[i].Id, c.MainLevel(n).Biome);
            if (n > 1)
            {
                int p = c.CampaignBiomeIndex(n - 1);
                Assert.NotEqual(p, i);
                Assert.NotEqual(k.Biomes[p].Family, k.Biomes[i].Family); // and never the same enemy roster twice in a row
            }
            // every block of B levels shows every biome once
            if (n % b == 0)
                Assert.Equal(b, Enumerable.Range(n - b + 1, b).Select(c.CampaignBiomeIndex).Distinct().Count());
        }
        Assert.Equal(b, used.Count);
        // deep in the endless campaign too (pure function of the level number, no history)
        for (int n = 999_000; n <= 999_200; n++) Assert.NotEqual(c.CampaignBiomeIndex(n - 1), c.CampaignBiomeIndex(n));
        // not just a fixed cycle: the order of consecutive blocks differs
        var blocks = Enumerable.Range(0, 20).Select(blk => string.Join(",", Enumerable.Range(blk * b + 1, b).Select(c.CampaignBiomeIndex))).Distinct().Count();
        Assert.True(blocks >= 15, blocks + " distinct blocks");
        // pinned: the order is stable across versions while the biome list and seed stay
        Assert.Equal(ExpectedFirst30, Enumerable.Range(1, 30).Select(n => c.CampaignBiome(n).Id).ToArray());
    }

    private static readonly string[] ExpectedFirst30 =
    {
        "lava", "shipwreck", "cloud_islands", "gas_giant", "alien_jungle", "seabed", "space_station", "toxic_swamp", "gas_giant", "toxic_swamp",
        "lava", "seabed", "shipwreck", "space_station", "cloud_islands", "alien_jungle", "toxic_swamp", "seabed", "shipwreck", "lava",
        "space_station", "gas_giant", "cloud_islands", "alien_jungle", "shipwreck", "lava", "space_station", "seabed", "toxic_swamp", "cloud_islands",
    };

    [Fact]
    public void BiomeOrder_FallsBackToDistinctBiomes_WhenFamiliesCannotAlternate()
    {
        var c = new ContentDb();
        c.Campaign.Biomes = new List<CampaignBiomeDef>
        {
            new("a", "A", "ember"), new("b", "B", "ember"), new("c", "C", "ember"), new("d", "D", "glacier"),
        };
        for (int n = 2; n <= 400; n++) Assert.NotEqual(c.CampaignBiomeIndex(n - 1), c.CampaignBiomeIndex(n));
        Assert.Equal(4, Enumerable.Range(1, 400).Select(c.CampaignBiomeIndex).Distinct().Count());
    }

    [Fact]
    public void GeneratedContent_IsDeterministic_AndSurvivesCacheEviction()
    {
        var a = new ContentDb { GeneratedCacheLevels = 30 };
        var b = new ContentDb();
        var first = a.MainLevel(1234);
        for (int n = 600; n < 700; n++) a.MainLevel(n); // evicts
        var again = a.MainLevel(1234);
        foreach (var l in new[] { again, b.MainLevel(1234) })
        {
            Assert.Equal(first.Id, l.Id);
            Assert.Equal(first.Name, l.Name);
            Assert.Equal(first.Threat, l.Threat);
            Assert.Equal(first.BossId, l.BossId);
            Assert.Equal(first.Modifiers.EnemyHpPct, l.Modifiers.EnemyHpPct);
            Assert.Equal(first.CompletionRewards.Select(r => r.Id + r.Amount), l.CompletionRewards.Select(r => r.Id + r.Amount));
        }
        // Each level's boss is its biome family's; past the endless anchor the variant changes every few chapters,
        // cycling through every variant of every family.
        var k = a.Campaign;
        for (int n = 1; n <= 300; n++) Assert.Equal(new[] { "boss_magma_tyrant", "boss_frost_leviathan", "boss_storm_hive", "boss_void_maw" }[ContentDb.FamilyIndex(a.CampaignBiome(n).Family)], a.MainLevelBossId(n));
        var bosses = Enumerable.Range(k.EndlessFromChapter * k.ChapterLevels + 1, k.BossVariants * k.BossVariantEveryChapters * k.ChapterLevels)
            .Select(a.MainLevelBossId).Distinct().Count();
        Assert.Equal(4 * k.BossVariants, bosses);
    }

    [Fact]
    public void EndlessRewards_GrowSublinearlyToThreat_AndItemDropsStopAtLegendary()
    {
        var c = new ContentDb();
        double alpha = c.Campaign.EndlessRewardExponent;
        Assert.InRange(alpha, 0.1, 0.99);
        double prevScale = c.MainRewardScale(49);
        for (int i = 50; i < 300; i++)
        {
            double s = c.MainRewardScale(i);
            Assert.True(s > prevScale);
            prevScale = s;
        }
        // reward ∝ threat^α past the anchor
        double threatRatio = c.ThreatMultiplierAt(c.Campaign.ThreatAt(1501)) / c.ThreatMultiplierAt(c.Campaign.ThreatAt(491));
        Assert.Equal(Math.Pow(threatRatio, alpha), c.MainRewardScale(150) / c.MainRewardScale(49), 6);
        Assert.True(c.MainRewardScale(150) / c.MainRewardScale(49) < threatRatio);

        Assert.Equal(0, c.DropRarityShift(49));
        Assert.Equal(1, c.DropRarityShift(50));
        foreach (var index in new[] { 49, 50, 80, 120, 2000 })
        {
            var loot = c.RunLootTable("loot_p" + (index + 1));
            Assert.All(loot.Entries.Where(e => e.Kind != RewardKinds.Currency), e => Assert.InRange(e.Rarity, 0, (int)Rarity.Legendary));
        }
        Assert.All(c.RunLootTable("loot_p2001").Entries.Where(e => e.Kind != RewardKinds.Currency), e => Assert.Equal((int)Rarity.Legendary, e.Rarity));
        // Dark Side Data keeps growing past the materialized levels.
        long prev = 0;
        for (int i = 1; i <= 600; i++)
        {
            var l = c.DarkSideLevel(i);
            Assert.Equal("dark_side_" + i, l.Id);
            long data = l.CompletionRewards.Single(r => r.Id == Currencies.Data).Amount;
            Assert.True(data >= prev, "dark " + i);
            prev = data;
        }
    }

    [Fact]
    public void MilestoneChests_EveryTenLevels_BiggerBonusChestEveryFifty()
    {
        var c = new ContentDb();
        for (int n = 1; n <= 1200; n++)
        {
            var l = c.MainLevel(n);
            if (n % 10 != 0) { Assert.Empty(l.FirstClearRewards); continue; }
            Assert.Equal(new[] { Currencies.Diamonds, Currencies.PartTokens, Currencies.BlueprintTokens, Currencies.AlienTokens },
                l.FirstClearRewards.Select(r => r.Id));
            var chest = c.Campaign.MilestoneRewards(n - (n % 50 == 0 ? 10 : 0));
            if (n % 50 == 0) Assert.True(l.FirstClearRewards[0].Amount >= 3 * chest[0].Amount);
        }
    }

    [Fact]
    public void Run_PastLevel500_UnlocksInSequence_PaysTheMilestoneOnce()
    {
        var e = RunTestKit.Engine(new RunTestKit.WormSetup { Hp = 1e10, Atk = 1e9, Def = 1e7, Speed = 1000 });
        var c = e.Content;
        var s = TestHelpers.NewPlayer(e, 5);
        s.Wallet.Add(Currencies.Stamina, 1000);
        var l509 = c.MainLevel(509);
        Assert.False(RunSystem.IsLevelUnlocked(s, c, c.MainLevelId(510), RunMode.Normal));
        s.Run.Levels[l509.Id] = new LevelProgress { Cleared = true, Clears = 1 };
        s.Counters[Requirements.MainLevelCounter] = 509;
        Assert.True(RunSystem.IsLevelUnlocked(s, c, c.MainLevelId(510), RunMode.Normal));
        Assert.Equal(c.MainLevelId(510), RunSystem.NextMainLevel(s, c).Id);
        // the hub's PLAY: one helper starts the next level (its own portal, Normal mode)
        var play = RunSystem.NextMainRunStart(s, c)!;
        Assert.Equal((c.MainLevelId(510), c.MainLevelId(510), RunMode.Normal), (play.LevelId, play.PortalId, play.Mode));

        long diamonds = s.Wallet.Get(Currencies.Diamonds), tokens = s.Wallet.Get(Currencies.AlienTokens);
        var chest = c.MainLevel(510).FirstClearRewards;
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = c.MainLevelId(510) }).Ok);
        RunTestKit.AutoToEnd(e, ref s);
        Assert.True(s.Run.LastSummary.Won);
        Assert.Equal(510, s.Counter(Requirements.MainLevelCounter));
        Assert.Equal(1, s.Counter("milestone_chests"));
        Assert.True(s.Wallet.Get(Currencies.AlienTokens) >= tokens + chest.Single(r => r.Id == Currencies.AlienTokens).Amount);
        Assert.True(s.Wallet.Get(Currencies.Diamonds) >= diamonds + chest.Single(r => r.Id == Currencies.Diamonds).Amount);
        // Every listed reward is doublable since playtest 0.1.359: the chest's tokens too.
        long doublableTokens = s.Run.LastSummary.Doublable.Where(r => r.Id == Currencies.AlienTokens).Sum(r => r.Amount);
        Assert.True(doublableTokens >= chest.Single(r => r.Id == Currencies.AlienTokens).Amount);
        Assert.Equal(s.Wallet.Get(Currencies.AlienTokens) - tokens, doublableTokens);

        // The next level (511, its own portal) opens; replaying 510 pays no second chest.
        Assert.True(RunSystem.IsLevelUnlocked(s, c, c.MainLevelId(511), RunMode.Normal));
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = c.MainLevelId(510) }).Ok);
        RunTestKit.AutoToEnd(e, ref s);
        Assert.Equal(1, s.Counter("milestone_chests"));
    }

    [Fact]
    public void Progress_FarBehindTheFrontier_IsFoldedIntoACompactArchive()
    {
        var c = new ContentDb();
        var e = RunTestKit.Engine(RunTestKit.Strong(), c);
        var s = TestHelpers.NewPlayer(e, 9);
        int top = 2000;
        for (int n = 1; n <= top; n++)
        {
            var p = new LevelProgress { Cleared = true, Clears = 2 };
            if (n % 3 == 0) { p.OverchargedCleared = true; p.Feats = new List<bool> { true, true, true }; }
            else p.Feats = new List<bool> { true, n % 2 == 0, false };
            if (n <= 300) { p.OverchargedCleared = true; p.Feats = new List<bool> { true, true, true }; }
            s.Run.Levels[c.MainLevelId(n)] = p;
        }
        for (int i = 1; i <= 400; i++) s.Run.Levels["dark_side_" + i] = new LevelProgress { Cleared = true, Feats = new List<bool> { true, false, i % 2 == 0 } };
        s.Run.Levels["the_burrow_1"] = new LevelProgress { BestFloor = 7 };
        s.Counters[Requirements.MainLevelCounter] = top;
        s.Counters[RunSystem.DarkSideMaxCounter] = 400;

        var before = new Dictionary<int, LevelProgress>();
        for (int n = 1; n <= top; n++) before[n] = s.Run.Levels[c.MainLevelId(n)];
        RunSystem.CompactProgress(s, c);

        int window = c.Campaign.ProgressWindowLevels;
        Assert.InRange(s.Run.Levels.Count, 2 * window, 2 * window + 1);
        Assert.Equal(7, RunSystem.Progress(s, c, "the_burrow_1").BestFloor);
        Assert.Equal(300, s.Run.MainArchive.Full); // the fully done prefix costs nothing
        Assert.Equal(top - window - 300, s.Run.MainArchive.Flags.Length);
        for (int n = 1; n <= top; n++)
        {
            var p = RunSystem.Progress(s, c, c.MainLevelId(n));
            Assert.True(p.Cleared);
            Assert.Equal(before[n].OverchargedCleared, p.OverchargedCleared);
            Assert.Equal(before[n].Feats, p.Feats);
            Assert.True(RunSystem.IsLevelUnlocked(s, c, c.MainLevelId(n), RunMode.Normal));
        }
        Assert.Equal(400 - window, s.Run.DarkArchive.Full + s.Run.DarkArchive.Flags.Length);
        Assert.True(RunSystem.Progress(s, c, "dark_side_2").Feats[2]);
        Assert.False(RunSystem.Progress(s, c, "dark_side_3").Feats[2]);

        // A replayed archived level re-opens with its flags and folds back with the new feat.
        var level7 = c.MainLevelId(307);
        Assert.False(RunSystem.Progress(s, c, level7).Feats[2]);
        s.Counters[Requirements.MainLevelCounter] = top;
        var reopened = RunSystem.Progress(s, c, level7);
        reopened.Feats[2] = true;
        s.Run.Levels[level7] = reopened;
        for (int k = 0; k < 2 * window + 30; k++) s.Run.Levels["filler_" + k] = new LevelProgress();
        RunSystem.CompactProgress(s, c);
        Assert.False(s.Run.Levels.ContainsKey(level7));
        Assert.True(RunSystem.Progress(s, c, level7).Feats[2]);

        // The state round-trips through serialization with the archives.
        var clone = TestHelpers.Clone(s);
        Assert.Equal(s.Run.MainArchive.Flags, clone.Run.MainArchive.Flags);
        Assert.True(RunSystem.Progress(clone, c, level7).Feats[2]);
    }

    [Fact]
    public void Overcharged_FollowsTheLinearSequence_AcrossPortals()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var c = e.Content;
        var s = TestHelpers.NewPlayer(e, 3);
        s.Flags.Add(Features.DevUnlockAllFlag);
        for (int n = 1; n <= 3; n++) s.Run.Levels[c.MainLevelId(n)] = new LevelProgress { Cleared = true, Clears = 1 };
        s.Counters[Requirements.MainLevelCounter] = 3;
        Assert.True(RunSystem.IsLevelUnlocked(s, c, c.MainLevelId(1), RunMode.Overcharged));
        Assert.False(RunSystem.IsLevelUnlocked(s, c, c.MainLevelId(2), RunMode.Overcharged)); // level 1 not overcharged yet
        s.Run.Levels[c.MainLevelId(1)].OverchargedCleared = true;
        Assert.True(RunSystem.IsLevelUnlocked(s, c, c.MainLevelId(2), RunMode.Overcharged));
        Assert.False(RunSystem.IsLevelUnlocked(s, c, c.MainLevelId(4), RunMode.Overcharged)); // not cleared normally
    }
}
