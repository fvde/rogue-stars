using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Progression.ProgressionKit;

namespace RogueStars.Rules.Tests.Progression;

/// <summary>
/// The pull system (docs/design/pull-system.md): flat published rates (Mythic 1.2%, Legendary 3%), a fixed visible pity (Mythic at
/// 100, Legendary at 40), no soft or hidden pity anywhere, batches of 50 shards, one star table to 10★ for heroes, mounts, aliens and
/// blueprints, and the save migration.
/// </summary>
public class PullSystemTests
{
    private static IEnumerable<(string Name, GachaTypeDef Def)> AllPullTypes()
    {
        foreach (var kv in C.GachaConfig.Types) yield return (kv.Key.ToString(), kv.Value);
        foreach (var kv in C.SummonEvents.Types) yield return ("summon " + kv.Key, kv.Value.Gacha);
    }

    // ---- rates and pity ----------------------------------------------------------------------------------------

    [Fact]
    public void Rates_AreFlat_Mythic1Point2_Legendary3_OverManySeeds()
    {
        foreach (var (name, def) in AllPullTypes())
        {
            Assert.Equal(1.2, def.Tiers.Single(t => t.Tier == Rarity.Mythic).Weight / def.Tiers.Sum(t => t.Weight) * 100, 6);
            if (def.LegendaryPity > 0) Assert.Equal(3.0, def.Tiers.Single(t => t.Tier == Rarity.Legendary).Weight / def.Tiers.Sum(t => t.Weight) * 100, 6);
            // Fresh counters before every roll: what the published rate promises, over 20 seeds × 20 000 rolls.
            long n = 0, mythic = 0, legendary = 0;
            for (long seed = 1; seed <= 20; seed++)
            {
                var rng = Rng.FromSeed(seed * 7919);
                for (int i = 0; i < 20_000; i++)
                {
                    var tier = GachaSystem.RollTier(def, new GachaPityState(), rng, false).Tier;
                    n++;
                    if (tier == Rarity.Mythic) mythic++;
                    if (tier == Rarity.Legendary) legendary++;
                }
            }
            Assert.InRange(mythic * 100.0 / n, 1.1, 1.3);
            if (def.LegendaryPity > 0) Assert.InRange(legendary * 100.0 / n, 2.85, 3.15);
            else Assert.Equal(0, legendary);
        }
    }

    [Fact]
    public void Pity_IsExact_TheHundredthPullWithoutAMythic_TheFortiethWithoutALegendary()
    {
        foreach (var (name, def) in AllPullTypes())
        {
            Assert.Equal(100, def.MythicPity);
            // Live counters over many seeds: the longest gap is exactly the pity, never more; the mean is (1 − 0.988^100) / 0.012 ≈ 58.
            int maxGap = 0, maxLegGap = 0; long drops = 0, pulls = 0;
            for (long seed = 1; seed <= 30; seed++)
            {
                var rng = Rng.FromSeed(seed);
                var p = new GachaPityState();
                int gap = 0, leg = 0;
                for (int i = 0; i < 6000; i++)
                {
                    var tier = GachaSystem.RollTier(def, p, rng, false).Tier;
                    pulls++; gap++; leg++;
                    if (tier == Rarity.Mythic) { maxGap = Math.Max(maxGap, gap); gap = 0; drops++; }
                    if (tier >= Rarity.Legendary) { maxLegGap = Math.Max(maxLegGap, leg); leg = 0; }
                    Assert.Equal(gap, p.SinceMythic);
                    Assert.True(p.SinceMythic < def.MythicPity, name);
                }
            }
            Assert.Equal(def.MythicPity, maxGap);
            Assert.InRange(pulls / (double)drops, 55, 61.5);
            if (def.LegendaryPity > 0) Assert.Equal(def.LegendaryPity, maxLegGap);
        }
        // Deterministic: at 99 the next pull is the Mythic; at 98 it is not forced.
        var sig = C.GachaConfig.Types[GachaType.Alien];
        for (long seed = 1; seed <= 200; seed++)
        {
            var at99 = new GachaPityState { SinceMythic = 99 };
            Assert.Equal(Rarity.Mythic, GachaSystem.RollTier(sig, at99, Rng.FromSeed(seed), false).Tier);
            var at39 = new GachaPityState { SinceLegendary = 39 };
            Assert.True(GachaSystem.RollTier(sig, at39, Rng.FromSeed(seed), false).Tier >= Rarity.Legendary);
        }
        int natural98 = 0;
        for (long seed = 1; seed <= 2000; seed++)
            if (GachaSystem.RollTier(sig, new GachaPityState { SinceMythic = 98, SinceLegendary = 38 }, Rng.FromSeed(seed), false).Tier >= Rarity.Legendary) natural98++;
        Assert.InRange(natural98 / 2000.0, 0.03, 0.06); // 4.2% Legendary+: nothing ramps up before the pity
    }

    [Fact]
    public void NoHiddenOrSoftPity_TheMythicChanceIsFlatAtEveryCounter()
    {
        var def = C.GachaConfig.Types[GachaType.Blueprint];
        // The Mythic chance at counters 0, 50 and 98 (and on a Legendary-pity pull) is the same 1.2%.
        foreach (var (sm, sl) in new[] { (0, 0), (50, 10), (98, 20), (60, 39) })
        {
            int m = 0; const int n = 60_000;
            var rng = Rng.FromSeed(sm * 1000 + sl);
            for (int i = 0; i < n; i++)
                if (GachaSystem.RollTier(def, new GachaPityState { SinceMythic = sm, SinceLegendary = sl }, rng, false).Tier == Rarity.Mythic) m++;
            Assert.InRange(m * 100.0 / n, 1.05, 1.35);
        }
        // The content carries only the fixed, visible values: no soft-pity fields remain.
        var fields = typeof(GachaTypeDef).GetFields().Select(f => f.Name).ToList();
        Assert.DoesNotContain(fields, f => f.Contains("Soft") || f.Contains("Hidden") || f.Contains("Hard"));
        Assert.Contains(nameof(GachaTypeDef.MythicPity), fields);
        Assert.Contains(nameof(GachaTypeDef.LegendaryPity), fields);
    }

    [Fact]
    public void TenPullRule_IsVisible_AndOnlyUpgradesBelowEpic()
    {
        var def = C.GachaConfig.Types[GachaType.Part];
        Assert.True(def.TenPullGuarantee);
        Assert.Equal(Rarity.Epic, GachaSystem.GetOdds(C, GachaType.Part, null, TestHelpers.T0).TenPullMinTier);
        int m = 0; const int n = 60_000;
        var rng = Rng.FromSeed(3);
        for (int i = 0; i < n; i++)
        {
            var t = GachaSystem.RollTier(def, new GachaPityState(), rng, true).Tier;
            Assert.True(t >= Rarity.Epic);
            if (t == Rarity.Mythic) m++;
        }
        Assert.InRange(m * 100.0 / n, 1.05, 1.35); // the forced pull keeps the flat Mythic chance
        foreach (var t in C.SummonEvents.Types.Values) Assert.False(t.Gacha.TenPullGuarantee);
    }

    [Fact]
    public void PullResult_ShowsTheVisibleCounters()
    {
        var e = Engine(cloner: false); var s = Player(e);
        SetBalance(s, Currencies.Diamonds, 1_000_000);
        var r = (GachaPullResult)Ok(e, ref s, new GachaPull { PullType = GachaType.Alien, Count = 10, Payment = GachaPayment.Diamonds }).Payload;
        var p = GachaSystem.Pity(s, C, GachaType.Alien);
        Assert.Equal((100, 40), (r.Pity.MythicPity, r.Pity.LegendaryPity));
        Assert.Equal((p.Mythic, p.Legendary), (r.Pity.Mythic, r.Pity.Legendary));
        Assert.Equal(s.Gacha.Pity[GachaType.Alien].SinceMythic, p.Mythic);
    }

    // ---- batches and the star table -------------------------------------------------------------------------

    [Fact]
    public void StarTable_50ToUnlock_50_50_100x4_150x3_1000To10Stars()
    {
        var t = C.ShardStars;
        Assert.Equal(new long[] { 0, 50, 50, 50, 100, 100, 100, 100, 150, 150, 150 }, t.StepCosts);
        Assert.Equal(50, t.UnlockShards);
        Assert.Equal(50, t.BatchShards);
        Assert.Equal(1000, t.TotalTo(10));
        Assert.All(t.StepCosts, c => Assert.Equal(0, c % t.BatchShards)); // always whole batches
        // Every Mythic item type goes to 10★ on it.
        Assert.Equal(10, C.HeroConfig.MaxStars);
        Assert.Equal(10, C.MountConfig.MaxStars);
        Assert.Equal(10, AlienSystem.MaxStars);
        Assert.Equal(10, CitySystem.MaxStars);
        var o = new OwnedAlien { Stars = 3 }; var b = new OwnedBlueprint { Stars = 7 };
        Assert.Equal(100, AlienSystem.StarCost(C, o));
        Assert.Equal(150, CitySystem.StarCost(C, b));
        Assert.Equal(1000 - 150, t.Remaining(3));
    }

    [Fact]
    public void EveryItemWinPays50_PullsNeverPaySmallShardAmounts()
    {
        var e = Engine(cloner: false); var s = Player(e);
        SetBalance(s, Currencies.Diamonds, 100_000_000);
        foreach (var type in new[] { GachaType.Alien, GachaType.Blueprint })
            for (int i = 0; i < 40; i++)
            {
                var before = s.Wallet.Balances.Where(kv => kv.Key.StartsWith("shards_")).ToDictionary(kv => kv.Key, kv => kv.Value);
                var r = (GachaPullResult)Ok(e, ref s, new GachaPull { PullType = type, Count = 10, Payment = GachaPayment.Diamonds }).Payload;
                foreach (var it in r.Items.Where(x => x.Kind == RewardKinds.Alien || x.Kind == RewardKinds.Blueprint))
                {
                    Assert.Equal(50, it.Shards);
                    Assert.Contains(it.Note, new[] { "new", "shards" });
                }
                foreach (var kv in s.Wallet.Balances.Where(kv => kv.Key.StartsWith("shards_")))
                    Assert.Equal(0, (kv.Value - before.GetValueOrDefault(kv.Key)) % 50);
            }
        // Events: the Mythic is the featured batch; the filler never pays hero / mount shards.
        foreach (var ev in C.SummonEvents.Types.Values)
            foreach (var tier in ev.Gacha.Tiers)
                foreach (var en in tier.Pool)
                    if (tier.Tier == Rarity.Mythic) Assert.Equal((1, SummonEventsConfig.Featured), (en.Amount, en.Id));
                    else Assert.DoesNotContain("shards", en.Id);
    }

    [Fact]
    public void TheFirstBatchUnlocks_LooseShardsPlusABatchKeepTheRest()
    {
        var e = Engine(); var s = Player(e);
        SetBalance(s, Currencies.HeroShards("hero_chef"), 30); // e.g. from the exchange shop
        Grant(e, s, new Reward(RewardKinds.Hero, "hero_chef", 1));
        Assert.Equal(1, s.Heroes.Owned["hero_chef"].Stars);
        Assert.Equal(30, s.Wallet.Get(Currencies.HeroShards("hero_chef")));
        Grant(e, s, new Reward(RewardKinds.Alien, "alien_nebulisk", 2));
        Assert.Equal(1, AlienSystem.Owned(s, "alien_nebulisk")!.Stars);
        Assert.Equal(50, s.Wallet.Get(Currencies.AlienShards("alien_nebulisk")));
        Grant(e, s, new Reward(RewardKinds.Blueprint, "bp_shard_reactor", 1));
        Assert.Equal(1, CitySystem.Owned(s, "bp_shard_reactor")!.Stars);
        Assert.Equal(0, s.Wallet.Get(Currencies.BlueprintShards("bp_shard_reactor")));
    }

    [Fact]
    public void NoStarterMount_TheMountsUnlockGivesEventAccessAndTickets()
    {
        var grants = C.Feature(Features.Mounts)!.UnlockGrants;
        Assert.DoesNotContain(grants, r => r.Kind == RewardKinds.Mount);
        Assert.Contains(grants, r => r.Id == Currencies.MountTickets && r.Amount == 10);
        Assert.DoesNotContain(C.Feature(Features.Heroes)!.UnlockGrants, r => r.Kind == RewardKinds.Hero || r.Kind == RewardKinds.HeroShards);
    }

    // ---- migration ----------------------------------------------------------------------------------------------

    [Fact]
    public void ConvertShards_KeepsTheShareOfEachStepCovered()
    {
        var n = C.ShardStars.StepCosts;
        // hero 3★ with 50 old shards: 3★→4★ costs 100 old and 100 new → 50; with 160: the step (100) and 60/160 of the next → 100 + 38
        Assert.Equal(50, PullSystemMigration.ConvertShards(PullSystemMigration.LegacyHeroSteps, n, 3, 50));
        Assert.Equal(138, PullSystemMigration.ConvertShards(PullSystemMigration.LegacyHeroSteps, n, 3, 160));
        // not owned, 40 old hero shards (the old unlock) → the new unlock 50
        Assert.Equal(50, PullSystemMigration.ConvertShards(PullSystemMigration.LegacyHeroSteps, n, 0, 40));
        // alien 1★ with 1 old shard: half of the old 1→2 (2) → half of the new (50)
        Assert.Equal(25, PullSystemMigration.ConvertShards(PullSystemMigration.LegacyAlienSteps, n, 1, 1));
        // blueprint at 10★ with 10 spare old shards: the last ratio (150 / 36), rounded up
        Assert.Equal(42, PullSystemMigration.ConvertShards(PullSystemMigration.LegacyBlueprintSteps, n, 10, 10));
        // mount 5★ (old 5★ → A1 costs 100) with 100 → the whole 5★→6★ step (100)
        Assert.Equal(100, PullSystemMigration.ConvertShards(PullSystemMigration.LegacyMountSteps, n, 5, 100));
        Assert.Equal(0, PullSystemMigration.ConvertShards(PullSystemMigration.LegacyMountSteps, n, 5, 0));
    }

    [Fact]
    public void OldSave_KeepsStars_FoldsAwakening_ConvertsShards_KeepsCounters()
    {
        var e = Engine(); var s = Player(e);
        s.Gacha.Migrated = 0; // a save written before the pull system
        s.Heroes.Owned["hero_chef"] = new HeroEntry { Stars = 4, Level = 20 };
        s.Wallet.Add(Currencies.HeroShards("hero_chef"), 80);          // half of the old 4★→5★ (160)
        s.Mounts.Owned["mount_magma_barge"] = new MountEntry { Stars = 5, Awaken = 2, Level = 30 };
        s.Wallet.Add(Currencies.MountShards("mount_magma_barge"), 100); // half of the old A2→A3 (200)
        s.Aliens.Owned["alien_blorp"] = new OwnedAlien { TypeId = "alien_blorp", Stars = 2 };
        s.Wallet.Add(Currencies.AlienShards("alien_blorp"), 2);         // half of the old 2★→3★ (4)
        s.City.Blueprints["bp_ore_mine"] = new OwnedBlueprint { Id = "bp_ore_mine", Stars = 10 };
        s.Gacha.For(GachaType.Part).SinceMythic = 37;
        s.Gacha.For(GachaType.Part).SinceLegendary = 22;
        s.Summons.LuckFor(SummonEventType.Hero).SinceMythic = 250;     // old Luck max 300
        s.Summons.LuckFor(SummonEventType.Mount).SinceMythic = 40;
        e.Settle(s, TestHelpers.T0 + 1000);

        Assert.Equal(PullSystemMigration.Version, s.Gacha.Migrated);
        Assert.Equal(4, s.Heroes.Owned["hero_chef"].Stars);
        Assert.Equal(20, s.Heroes.Owned["hero_chef"].Level);
        Assert.Equal(50, s.Wallet.Get(Currencies.HeroShards("hero_chef")));       // half of the new 4★→5★ (100)
        Assert.Equal((7, 0, 30), (s.Mounts.Owned["mount_magma_barge"].Stars, s.Mounts.Owned["mount_magma_barge"].Awaken, s.Mounts.Owned["mount_magma_barge"].Level));
        Assert.Equal(75, s.Wallet.Get(Currencies.MountShards("mount_magma_barge"))); // half of the new 7★→8★ (150)
        Assert.Equal(2, s.Aliens.Owned["alien_blorp"].Stars);
        Assert.Equal(25, s.Wallet.Get(Currencies.AlienShards("alien_blorp")));       // half of the new 2★→3★ (50)
        Assert.Equal(10, s.City.Blueprints["bp_ore_mine"].Stars);
        Assert.Equal((37, 22), (s.Gacha.Pity[GachaType.Part].SinceMythic, s.Gacha.Pity[GachaType.Part].SinceLegendary));
        Assert.Equal(99, s.Summons.Luck[SummonEventType.Hero].SinceMythic); // capped: the next pull is the guaranteed one
        Assert.Equal(40, s.Summons.Luck[SummonEventType.Mount].SinceMythic);

        // Once only: a second settle changes nothing.
        var copy = TestHelpers.Clone(s);
        e.Settle(s, TestHelpers.T0 + 2000);
        Assert.Equal(copy.Wallet.Get(Currencies.HeroShards("hero_chef")), s.Wallet.Get(Currencies.HeroShards("hero_chef")));
        Assert.Equal(copy.Mounts.Owned["mount_magma_barge"].Stars, s.Mounts.Owned["mount_magma_barge"].Stars);
    }

    [Fact]
    public void NewSave_NeedsNoMigration()
    {
        var e = Engine(); var s = Player(e);
        Assert.Equal(PullSystemMigration.Version, s.Gacha.Migrated);
    }
}
