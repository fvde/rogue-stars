using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

public class EndlessCampaignTests
{
    /// <summary>Biome family strength factor of main portal <paramref name="index"/> (base offset × late offset).</summary>
    internal static double FamilyFactor(ContentDb c, int index)
    {
        var k = c.Campaign;
        double f = 1 + k.FamilyStrengthPct[index % k.FamilyStrengthPct.Count] / 100.0;
        if (index >= k.LateFamilyFromPortal) f *= 1 + k.LateFamilyStrengthPct[index % k.LateFamilyStrengthPct.Count] / 100.0;
        return f;
    }

    [Fact]
    public void MainLevels_ContinuePastTheNamedPortals_OnTheSameRisingCurve()
    {
        var c = new ContentDb();
        var ids = new HashSet<string>();
        var names = new HashSet<string>();
        double prev = 0;
        for (int n = 1; n <= 3000; n++)
        {
            var l = c.MainLevel(n);
            Assert.NotNull(l);
            Assert.Equal(n, l.Number);
            Assert.Equal(n, RunSystem.MainLevelNumber(c, l.Id));
            Assert.True(ids.Add(l.Id), l.Id);
            Assert.Same(c.Portal(l.PortalId), c.MainPortal((n - 1) / c.Campaign.LevelsPerPortal));
            if (l.Index == 1) Assert.True(names.Add(c.Portal(l.PortalId).Name), c.Portal(l.PortalId).Name);
            double eff = c.ThreatMultiplier(l.Threat) * (1 + l.Modifiers.EnemyHpPct / 100.0) / FamilyFactor(c, (n - 1) / c.Campaign.LevelsPerPortal);
            Assert.True(eff > prev, "level " + n);
            prev = eff;
            Assert.NotNull(c.RunLootTable(l.LootTableId));
            Assert.True(c.RunBosses.ContainsKey(l.BossId), l.BossId);
            Assert.True(c.TilePools.ContainsKey(l.TilePoolId));
        }
        // Materialized content stays the named 50 portals; everything else lives in a bounded cache.
        Assert.Equal(c.Campaign.Portals, c.Portals.Values.Count(p => p.Kind == PortalKind.Main));
        Assert.Equal("ember_wastes_1", c.MainLevelId(1));
        Assert.Equal("main_51_1", c.MainLevelId(501));
        Assert.Null(c.RunLevel("main_51_11"));
        Assert.Null(c.RunLevel("main_5_1")); // a named portal's index is not a generated id
        Assert.Null(c.Portal("main_0"));
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
        // Bosses change variant every few portals, cycling through every variant.
        var bosses = Enumerable.Range(50, 4 * a.Campaign.BossVariants * a.Campaign.BossVariantEveryPortals).Select(i => a.MainPortalBossId(i)).Distinct().Count();
        Assert.Equal(4 * a.Campaign.BossVariants, bosses);
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
    public void MilestoneChests_EveryTenLevels_BiggerPortalBonusEveryFifty()
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

        // The next portal (level 511 = portal 52, level 1) opens; replaying 510 pays no second chest.
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
}
