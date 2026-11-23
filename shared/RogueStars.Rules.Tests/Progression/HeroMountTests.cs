using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Progression.ProgressionKit;

namespace RogueStars.Rules.Tests.Progression;

public class HeroTests
{
    private const string H = "hero_star_knight";

    [Fact]
    public void Content_EightHeroes_TraitsAreNonDraftableHeroSkills()
    {
        Assert.True(C.HeroConfig.Defs.Count >= 8);
        foreach (var d in C.HeroConfig.Defs.Values)
            foreach (var t in d.Traits)
            {
                var sk = C.Skill(t.SkillId)!;
                Assert.Equal(SkillSource.Hero, sk.Source);
                Assert.False(sk.Draftable);
            }
    }

    [Fact]
    public void Stars1To10_CostSteepShardAmounts()
    {
        var e = Engine(); var s = Player(e);
        var costs = C.HeroConfig.StarShardCosts;
        Grant(e, s, new Reward(RewardKinds.HeroShards, H, costs[1] - 1));
        Fail(e, ref s, new HeroUnlock { HeroId = H });
        Grant(e, s, new Reward(RewardKinds.HeroShards, H, 1));
        Ok(e, ref s, new HeroUnlock { HeroId = H });
        Assert.Equal(H, s.Heroes.Equipped);
        Fail(e, ref s, new HeroUnlock { HeroId = H });
        for (int star = 2; star <= 10; star++)
        {
            Assert.True(costs[star] > costs[star - 1] || star == 2, "costs grow");
            Assert.Equal(costs[star], HeroSystem.NextStarCost(s, C, H));
            Grant(e, s, new Reward(RewardKinds.HeroShards, H, costs[star] - 1));
            Fail(e, ref s, new HeroStarUp { HeroId = H });
            Grant(e, s, new Reward(RewardKinds.HeroShards, H, 1));
            Ok(e, ref s, new HeroStarUp { HeroId = H });
            Assert.Equal(star, s.Heroes.Owned[H].Stars);
            Assert.Equal(0, s.Wallet.Get(Currencies.HeroShards(H)));
        }
        Assert.Contains("max", Fail(e, ref s, new HeroStarUp { HeroId = H }));
        Assert.True(HeroSystem.TotalShardsToStar(C, 10) >= 3000);
        // Duplicates beyond max stars still become shards (value for later content).
        Grant(e, s, new Reward(RewardKinds.HeroShards, H, 5));
        Assert.Equal(5, s.Wallet.Get(Currencies.HeroShards(H)));
    }

    [Fact]
    public void Traits_UpgradeAtStars3_5_7_10()
    {
        string a1 = H + "_a1", a2 = H + "_a2", a3 = H + "_a3", b1 = H + "_b1", b2 = H + "_b2";
        Assert.Equal(new[] { a1 }, HeroSystem.TraitSkills(C, H, 1));
        Assert.Equal(new[] { a1 }, HeroSystem.TraitSkills(C, H, 2));
        Assert.Equal(new[] { a2 }, HeroSystem.TraitSkills(C, H, 3));
        Assert.Equal(new[] { a2, b1 }, HeroSystem.TraitSkills(C, H, 5));
        Assert.Equal(new[] { a3, b1 }, HeroSystem.TraitSkills(C, H, 7));
        Assert.Equal(new[] { a3, b1 }, HeroSystem.TraitSkills(C, H, 9));
        Assert.Equal(new[] { a3, b2 }, HeroSystem.TraitSkills(C, H, 10));
        // Meaningful: each upgrade is strictly stronger on its headline trigger and stats.
        double Power(string id) { var sk = C.Skill(id)!; return sk.Triggers.Sum(t => t.Chance + t.Value + t.Stacks) + sk.Stats.Sum(m => m.Flat + m.Percent); }
        Assert.True(Power(a2) > Power(a1) && Power(a3) > Power(a2) && Power(b2) > Power(b1));
        Assert.True(C.Skill(b2)!.Triggers.Count > C.Skill(b1)!.Triggers.Count);
    }

    [Fact]
    public void LevelCap_GrowsWithStars_AndStatsReachLoadout()
    {
        var e = Engine(); var s = Player(e);
        Grant(e, s, new Reward(RewardKinds.HeroShards, H, 40));
        Ok(e, ref s, new HeroUnlock { HeroId = H });
        SetBalance(s, Currencies.HeroXp, 10_000_000);
        int cap = C.HeroConfig.MaxLevel(1);
        Ok(e, ref s, new HeroLevelUp { HeroId = H, Levels = cap - 1 });
        Assert.Contains("cap", Fail(e, ref s, new HeroLevelUp { HeroId = H }));

        var withHero = e.BuildLoadout(s, TestHelpers.T0);
        Assert.Equal(H, withHero.HeroId);
        Assert.Contains(H + "_a1", withHero.MetaSkillIds);
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
    public void Granter_FirstCopyUnlocks_DuplicatesBecomeShards()
    {
        var e = Engine(); var s = Player(e);
        Grant(e, s, new Reward(RewardKinds.Mount, M, 3));
        Assert.Equal(1, s.Mounts.Owned[M].Stars);
        Assert.Equal(M, s.Mounts.Active);
        Assert.Equal(2 * C.MountConfig.DupeShards, s.Wallet.Get(Currencies.MountShards(M)));
    }

    [Fact]
    public void Stars1To5_ThenAwakening1To5_WithWeaponUpgradesAtMilestones()
    {
        var e = Engine(); var s = Player(e);
        var cfg = C.MountConfig;
        Grant(e, s, new Reward(RewardKinds.Mount, M, 1));
        SetBalance(s, Currencies.MountShards(M), 100_000);
        SetBalance(s, Currencies.MountCores, 100_000);
        string w1 = M + "_w1", w2 = M + "_w2", w3 = M + "_w3", w4 = M + "_w4", s1 = M + "_s1", s2 = M + "_s2";
        Assert.Equal(new[] { w1 }, MountSystem.WeaponSkills(C, M, 1, 0));
        Assert.Contains("max stars", Fail(e, ref s, new MountAwaken { MountId = M }));
        for (int star = 2; star <= 5; star++)
        {
            long before = s.Wallet.Get(Currencies.MountShards(M));
            Ok(e, ref s, new MountStarUp { MountId = M });
            Assert.Equal(before - cfg.StarShardCosts[star], s.Wallet.Get(Currencies.MountShards(M)));
        }
        Fail(e, ref s, new MountStarUp { MountId = M });
        Assert.Equal(new[] { w2 }, MountSystem.WeaponSkills(C, M, 2, 0));
        Assert.Equal(new[] { w3 }, MountSystem.WeaponSkills(C, M, 4, 0));
        Assert.Equal(new[] { w3, s1 }, MountSystem.WeaponSkills(C, M, 5, 0));
        for (int a = 1; a <= 5; a++)
        {
            long cores = s.Wallet.Get(Currencies.MountCores);
            Ok(e, ref s, new MountAwaken { MountId = M });
            Assert.Equal(cores - cfg.AwakenCoreCosts[a], s.Wallet.Get(Currencies.MountCores));
        }
        Assert.Contains("fully", Fail(e, ref s, new MountAwaken { MountId = M }));
        Assert.Equal(new[] { w3, s1 }, MountSystem.WeaponSkills(C, M, 5, 2));
        Assert.Equal(new[] { w3, s2 }, MountSystem.WeaponSkills(C, M, 5, 3));
        Assert.Equal(new[] { w4, s2 }, MountSystem.WeaponSkills(C, M, 5, 5));
        var l = e.BuildLoadout(s, TestHelpers.T0);
        Assert.Equal(M, l.MountId);
        Assert.Contains(w4, l.MetaSkillIds);
        Assert.Contains(s2, l.MetaSkillIds);
        Assert.Equal(30 * (1 + (cfg.StarStatPercent[5] + cfg.AwakenStatPercent[5]) / 100), l.MountStats.Value(StatId.Atk), 6);
        Assert.Equal(SkillSource.Mount, C.Skill(w4)!.Source);
    }

    [Fact]
    public void LevelWithFuel_CapByStarsAndAwakening()
    {
        var e = Engine(); var s = Player(e);
        Grant(e, s, new Reward(RewardKinds.Mount, M, 1));
        SetBalance(s, Currencies.Fuel, 100_000_000);
        int cap = C.MountConfig.MaxLevel(1, 0);
        Ok(e, ref s, new MountLevelUp { MountId = M, Levels = cap - 1 });
        Fail(e, ref s, new MountLevelUp { MountId = M });
        Assert.True(C.MountConfig.MaxLevel(5, 5) > cap);
        double atk = MountSystem.MountStats(C, M, s.Mounts.Owned[M]).Value(StatId.Atk);
        Assert.Equal(30 + 4 * (cap - 1), atk, 6);
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

        var left = PartsSystem.AddPart(s, C, "arm_tesla_whip", (int)PartRarity.Mythic);
        var right = PartsSystem.AddPart(s, C, "arm_drill_lance", (int)PartRarity.Common);
        var body = PartsSystem.AddPart(s, C, "body_plated_shell", (int)PartRarity.Rare);
        Grant(e, s, new Reward(RewardKinds.HeroShards, "hero_chef", 40));
        Grant(e, s, new Reward(RewardKinds.Mount, "mount_scout_saucer", 1));
        Ok(e, ref s, new HeroUnlock { HeroId = "hero_chef" });
        Ok(e, ref s, new PartEquip { Uid = left.Uid, Slot = PartSlot.LeftArm });
        Ok(e, ref s, new PartEquip { Uid = right.Uid, Slot = PartSlot.RightArm });
        Ok(e, ref s, new PartEquip { Uid = body.Uid, Slot = PartSlot.Body });

        var l = e.BuildLoadout(s, TestHelpers.T0);
        Assert.Equal("hero_chef", l.HeroId);
        Assert.Equal("mount_scout_saucer", l.MountId);
        Assert.Equal(new[] { AttackStyle.Whip, AttackStyle.Drill }, l.ArmStyles);
        Assert.Contains("hero_chef_a1", l.MetaSkillIds);
        Assert.Contains("mount_scout_saucer_w1", l.MetaSkillIds);
        Assert.Contains("arm_tesla_whip_sk3", l.MetaSkillIds);    // Mythic: skills 1-3
        Assert.DoesNotContain("arm_tesla_whip_sk4", l.MetaSkillIds);
        Assert.DoesNotContain("arm_drill_lance_sk1", l.MetaSkillIds); // Common: none
        Assert.True(l.WormStats.Value(StatId.Atk) > bare.WormStats.Value(StatId.Atk));
        Assert.True(l.WormStats.Value(StatId.Def) > bare.WormStats.Value(StatId.Def));
        Assert.True(l.MountStats.Value(StatId.Atk) > 0);
        Assert.True(l.Power > bare.Power);
        Assert.Equal(WormSystem.ComputePower(l, C), l.Power);
    }

    [Fact]
    public void State_RoundTripsThroughJson()
    {
        var e = Engine(); var s = Player(e);
        var p = PartsSystem.AddPart(s, C, "head_frost_helm", (int)PartRarity.Legendary);
        Ok(e, ref s, new PartEquip { Uid = p.Uid, Slot = PartSlot.Head });
        s.Wallet.Add(Currencies.PartTokens, 1); // not in the starting wallet any more (they come with the Part Signal)
        Ok(e, ref s, new GachaPull { PullType = GachaType.Part, Payment = GachaPayment.Tokens });
        var clone = TestHelpers.Clone(s);
        Assert.Equal(p.Uid, clone.Parts.Equipped[PartSlot.Head]);
        Assert.Equal(1, clone.Gacha.Pity[GachaType.Part].TotalPulls);
        Assert.Equal(e.BuildLoadout(s, TestHelpers.T0).Power, e.BuildLoadout(clone, TestHelpers.T0).Power);
    }
}
