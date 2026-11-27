using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Progression.ProgressionKit;

namespace RogueStars.Rules.Tests.Progression;

public class HeroTests
{
    private const string H = "hero_star_knight";

    [Fact]
    public void Content_NineHeroes_KitsAreNonDraftableHeroSkills()
    {
        Assert.Equal(9, C.HeroConfig.Defs.Count);
        foreach (var d in C.HeroConfig.Defs.Values)
            foreach (var t in d.Traits)
            {
                var sk = C.Skill(t.SkillId)!;
                Assert.Equal(SkillSource.Hero, sk.Source);
                Assert.False(sk.Draftable);
            }
    }

    [Fact]
    public void Stars1To10_OnTheSharedShardTable()
    {
        var e = Engine(); var s = Player(e);
        var costs = C.ShardStars.StepCosts;
        Grant(e, s, new Reward(RewardKinds.HeroShards, H, costs[1] - 1));
        Fail(e, ref s, new HeroUnlock { HeroId = H });
        Grant(e, s, new Reward(RewardKinds.HeroShards, H, 1));
        Ok(e, ref s, new HeroUnlock { HeroId = H });
        Assert.Equal(H, s.Heroes.Equipped);
        Fail(e, ref s, new HeroUnlock { HeroId = H });
        for (int star = 2; star <= 10; star++)
        {
            Assert.True(costs[star] >= costs[star - 1], "costs never fall");
            Assert.Equal(costs[star], HeroSystem.NextStarCost(s, C, H));
            Grant(e, s, new Reward(RewardKinds.HeroShards, H, costs[star] - 1));
            Fail(e, ref s, new HeroStarUp { HeroId = H });
            Grant(e, s, new Reward(RewardKinds.HeroShards, H, 1));
            Ok(e, ref s, new HeroStarUp { HeroId = H });
            Assert.Equal(star, s.Heroes.Owned[H].Stars);
            Assert.Equal(0, s.Wallet.Get(Currencies.HeroShards(H)));
        }
        Assert.Contains("max", Fail(e, ref s, new HeroStarUp { HeroId = H }));
        Assert.Equal(1000, HeroSystem.TotalShardsToStar(C, 10));
        // Duplicates beyond max stars still become shards (value for later content).
        Grant(e, s, new Reward(RewardKinds.HeroShards, H, 5));
        Assert.Equal(5, s.Wallet.Get(Currencies.HeroShards(H)));
    }

    [Fact]
    public void Kit_ChangesAtStars3_5_7_10()
    {
        string trait = H + "_trait", ab = H + "_ability", ab3 = H + "_ability_3", c5 = H + "_c5", c7 = H + "_c7", c10 = H + "_c10";
        Assert.Equal(new[] { trait, ab }, HeroSystem.TraitSkills(C, H, 1));
        Assert.Equal(new[] { trait, ab }, HeroSystem.TraitSkills(C, H, 2));
        Assert.Equal(new[] { trait, ab3 }, HeroSystem.TraitSkills(C, H, 3));
        Assert.Equal(new[] { trait, ab3, c5 }, HeroSystem.TraitSkills(C, H, 5));
        Assert.Equal(new[] { trait, ab3, c5, c7 }, HeroSystem.TraitSkills(C, H, 7));
        Assert.Equal(new[] { trait, ab3, c5, c7 }, HeroSystem.TraitSkills(C, H, 9));
        Assert.Equal(new[] { trait, ab3, c5, c7, c10 }, HeroSystem.TraitSkills(C, H, 10));
        Assert.Equal(new[] { "Trait", "Ability" }, HeroSystem.KitAt(C, H, 2).Select(k => k.Slot));
        Assert.Equal("Starlight", HeroSystem.NextKit(C, H, 3)!.Name);
        Assert.Null(HeroSystem.NextKit(C, H, 10));
    }

    [Fact]
    public void LevelCap_GrowsWithStars_AndStatsReachLoadout()
    {
        var e = Engine(); var s = Player(e);
        Grant(e, s, new Reward(RewardKinds.HeroShards, H, 50));
        Ok(e, ref s, new HeroUnlock { HeroId = H });
        SetBalance(s, Currencies.HeroXp, 10_000_000);
        int cap = C.HeroConfig.MaxLevel(1);
        Ok(e, ref s, new HeroLevelUp { HeroId = H, Levels = cap - 1 });
        Assert.Contains("cap", Fail(e, ref s, new HeroLevelUp { HeroId = H }));

        var withHero = e.BuildLoadout(s, TestHelpers.T0);
        Assert.Equal(H, withHero.HeroId);
        Assert.Contains(H + "_trait", withHero.MetaSkillIds);
        Ok(e, ref s, new HeroEquip { HeroId = "" });
        var l = e.BuildLoadout(s, TestHelpers.T0);
        Assert.Null(l.HeroId);
        Assert.True(withHero.WormStats.Value(StatId.Hp) > l.WormStats.Value(StatId.Hp));
        // Collection bonus applies even when the hero is not equipped.
        Assert.Equal(0.3, l.WormStats.Percent[StatId.Hp], 6);
    }
}

public class MountTests
{
    private const string M = "mount_magma_barge";

    [Fact]
    public void Unlock_WithShards_LikeHeroes()
    {
        var e = Engine(); var s = Player(e);
        var cfg = C.MountConfig;
        SetBalance(s, Currencies.MountShards(M), C.ShardStars.UnlockShards - 1);
        Assert.Contains("not enough", Fail(e, ref s, new MountUnlock { MountId = M }));
        SetBalance(s, Currencies.MountShards(M), C.ShardStars.UnlockShards);
        Ok(e, ref s, new MountUnlock { MountId = M });
        Assert.Equal(1, s.Mounts.Owned[M].Stars);
        Assert.Equal(M, s.Mounts.Active);
        Assert.Equal(0, s.Wallet.Get(Currencies.MountShards(M)));
        Assert.Contains("already", Fail(e, ref s, new MountUnlock { MountId = M }));
    }

    [Fact]
    public void Granter_ABatchIs50Shards_TheFirstUnlocks_At10StarsOverflow()
    {
        var e = Engine(); var s = Player(e);
        var cfg = C.MountConfig;
        Grant(e, s, new Reward(RewardKinds.Mount, M, 3));
        Assert.Equal(1, s.Mounts.Owned[M].Stars); // the first batch unlocks, two more are 100 shards (no automatic stars)
        Assert.Equal(M, s.Mounts.Active);
        Assert.Equal(100, s.Wallet.Get(Currencies.MountShards(M)));
        Assert.Equal("shards", MountSystem.CopyEffect(s, C, M));
        s.Mounts.Owned[M].Stars = cfg.MaxStars;
        Assert.Equal("overflow", MountSystem.CopyEffect(s, C, M));
        Grant(e, s, new Reward(RewardKinds.Mount, M, 1));
        Assert.Equal(100, s.Wallet.Get(Currencies.MountShards(M)));
        Assert.Equal(cfg.CopyOverflow.Amount, s.Wallet.Get(cfg.CopyOverflow.Id));
    }

    [Fact]
    public void Stars1To10_SharedTable_ShardsOnly_KitStepsAt1_3_5_8_10()
    {
        var e = Engine(); var s = Player(e);
        Grant(e, s, new Reward(RewardKinds.Mount, M, 1));
        SetBalance(s, Currencies.MountShards(M), 100_000);
        string P(int k) => M + "_passive_" + k;
        string A(int k) => M + "_active_" + k;
        Assert.Equal(new[] { P(1), A(1) }, MountSystem.WeaponSkills(C, M, 1));
        for (int star = 2; star <= 10; star++)
        {
            long before = s.Wallet.Get(Currencies.MountShards(M));
            var walletBefore = new Dictionary<string, long>(s.Wallet.Balances);
            Ok(e, ref s, new MountStarUp { MountId = M });
            Assert.Equal(before - C.ShardStars.StepCosts[star], s.Wallet.Get(Currencies.MountShards(M)));
            // Shards only: no other balance changes on a star-up (stars 6–10 included).
            foreach (var kv in walletBefore)
                if (kv.Key != Currencies.MountShards(M)) Assert.Equal(kv.Value, s.Wallet.Get(kv.Key));
        }
        Assert.Contains("max stars", Fail(e, ref s, new MountStarUp { MountId = M }));
        Assert.Equal(new[] { P(1), A(1) }, MountSystem.WeaponSkills(C, M, 2));
        Assert.Equal(new[] { P(2), A(2) }, MountSystem.WeaponSkills(C, M, 4));
        Assert.Equal(new[] { P(3), A(3) }, MountSystem.WeaponSkills(C, M, 7));
        Assert.Equal(new[] { P(4), A(4) }, MountSystem.WeaponSkills(C, M, 8));
        Assert.Equal(new[] { P(5), A(5) }, MountSystem.WeaponSkills(C, M, 10));
        var l = e.BuildLoadout(s, TestHelpers.T0);
        Assert.Equal(M, l.MountId);
        Assert.Contains(P(5), l.MetaSkillIds);
        Assert.Contains(A(5), l.MetaSkillIds);
        // no mount stats: 5 in-between steps (2★, 4★, 6★, 7★, 9★) × 5% mount damage, nothing else
        Assert.Equal(0, l.MountStats.Value(StatId.Atk), 6);
        Assert.Equal(0, l.MountStats.Value(StatId.Hp), 6);
        Assert.Equal(25, l.MountStats.Value(StatId.MountPower), 6);
        Assert.Equal(SkillSource.Mount, C.Skill(A(5))!.Source);
        Assert.Equal("10★", C.MountConfig.Defs[M].Kit.Last().StepLabel);
    }

    [Fact]
    public void LevelWithFuel_CapByStars()
    {
        var e = Engine(); var s = Player(e);
        Grant(e, s, new Reward(RewardKinds.Mount, M, 1));
        SetBalance(s, Currencies.Fuel, 100_000_000);
        int cap = C.MountConfig.MaxLevel(1);
        Ok(e, ref s, new MountLevelUp { MountId = M, Levels = cap - 1 });
        Fail(e, ref s, new MountLevelUp { MountId = M });
        Assert.True(C.MountConfig.MaxLevel(10) > cap);
        // Fuel keeps a sink: +1% mount damage per level above 1
        Assert.Equal(cap - 1, MountSystem.MountStats(C, M, s.Mounts.Owned[M]).Value(StatId.MountPower), 6);
    }
}

public class LoadoutTests
{
    [Fact]
    public void Loadout_CombinesWormPartsHeroMount_AndPowerGrows()
    {
        var e = Engine(); var s = Player(e);
        var bare = e.BuildLoadout(s, TestHelpers.T0);
        Assert.True(bare.Power > 0);
        Assert.Equal(C.WormConfig.BaseStats.First(m => m.Stat == StatId.Hp).Flat, bare.WormStats.Value(StatId.Hp), 6);

        var left = PartsSystem.AddPart(s, C, "myth_arcwright_drill", (int)PartRarity.Mythic);
        var right = PartsSystem.AddPart(s, C, "c_arm_keen", (int)PartRarity.Common);
        var body = PartsSystem.AddPart(s, C, "r_body_guard", (int)PartRarity.Rare);
        Grant(e, s, new Reward(RewardKinds.HeroShards, "hero_chef", 50));
        Grant(e, s, new Reward(RewardKinds.Mount, "mount_scout_saucer", 1));
        Ok(e, ref s, new HeroUnlock { HeroId = "hero_chef" });
        Ok(e, ref s, new PartEquip { Uid = left.Uid, Slot = PartSlot.LeftArm });
        Ok(e, ref s, new PartEquip { Uid = right.Uid, Slot = PartSlot.RightArm });
        Ok(e, ref s, new PartEquip { Uid = body.Uid, Slot = PartSlot.Body });

        var l = e.BuildLoadout(s, TestHelpers.T0);
        Assert.Equal("hero_chef", l.HeroId);
        Assert.Equal("mount_scout_saucer", l.MountId);
        Assert.Contains("hero_chef_trait", l.MetaSkillIds);
        Assert.Contains("mount_scout_saucer_active_1", l.MetaSkillIds);
        Assert.Contains("myth_arcwright_drill_sk3", l.MetaSkillIds);    // Mythic: skills 1-3
        Assert.DoesNotContain("myth_arcwright_drill_sk4", l.MetaSkillIds);
        Assert.DoesNotContain(l.MetaSkillIds, id => id.StartsWith("c_arm_keen", StringComparison.Ordinal)); // Common: none
        Assert.True(l.WormStats.Value(StatId.Atk) > bare.WormStats.Value(StatId.Atk));
        Assert.True(l.WormStats.Value(StatId.Def) > bare.WormStats.Value(StatId.Def));
        Assert.True(l.Power > bare.Power);
        Assert.Equal(WormSystem.ComputePower(l, C), l.Power);
    }

    [Fact]
    public void State_RoundTripsThroughJson()
    {
        var e = Engine(); var s = Player(e);
        var p = PartsSystem.AddPart(s, C, "l_head_guard", (int)PartRarity.Legendary);
        Ok(e, ref s, new PartEquip { Uid = p.Uid, Slot = PartSlot.Head });
        s.Wallet.Add(Currencies.PartTokens, 1); // not in the starting wallet any more (they come with the Part Signal)
        Ok(e, ref s, new GachaPull { PullType = GachaType.Part, Payment = GachaPayment.Tokens });
        var clone = TestHelpers.Clone(s);
        Assert.Equal(p.Uid, clone.Parts.Equipped[PartSlot.Head]);
        Assert.Equal(1, clone.Gacha.Pity[GachaType.Part].TotalPulls);
        Assert.Equal(e.BuildLoadout(s, TestHelpers.T0).Power, e.BuildLoadout(clone, TestHelpers.T0).Power);
    }
}
