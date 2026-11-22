using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Progression.ProgressionKit;

namespace RogueStars.Rules.Tests.Progression;

public class WormTests
{
    [Fact]
    public void LevelCurve_IsMonotonic_AndBuiltForYears()
    {
        var w = C.WormConfig;
        Assert.True(w.MaxLevel >= 400);
        long total = 0;
        for (int l = 2; l < w.MaxLevel; l++) { Assert.True(w.LevelCosts[l] > w.LevelCosts[l - 1]); total += w.LevelCosts[l]; }
        Assert.True(total > 1_000_000_000, "total biomass to max level should be enormous");
    }

    [Fact]
    public void LevelUp_SpendsBiomass_AndGrowsBaseStats()
    {
        var e = Engine(); var s = Player(e);
        var before = WormSystem.WormBaseStats(s, C);
        long cost = C.WormConfig.LevelCosts[1] + C.WormConfig.LevelCosts[2] + C.WormConfig.LevelCosts[3];
        SetBalance(s, Currencies.Biomass, cost);
        Ok(e, ref s, new WormLevelUp { Levels = 3 });
        Assert.Equal(4, s.Worm.Level);
        Assert.Equal(0, s.Wallet.Get(Currencies.Biomass));
        var after = WormSystem.WormBaseStats(s, C);
        Assert.Equal(before.Value(StatId.Hp) + 3 * 25, after.Value(StatId.Hp), 6);
        Assert.Equal(before.Value(StatId.Atk) + 3 * 5, after.Value(StatId.Atk), 6);
        Fail(e, ref s, new WormLevelUp { Levels = 1 }); // broke
        Assert.Equal(4, s.Worm.Level);
    }

    [Fact]
    public void Milestone_EveryNLevels_AddsPercent()
    {
        var s = new GameState();
        s.Worm.Level = 24;
        double hp24 = WormSystem.WormBaseStats(s, C).Value(StatId.Hp);
        s.Worm.Level = 25;
        double hp25 = WormSystem.WormBaseStats(s, C).Value(StatId.Hp);
        double flat25 = C.WormConfig.BaseStats.First(m => m.Stat == StatId.Hp).Flat + 24 * 25;
        Assert.Equal(flat25 * 1.03, hp25, 6);
        Assert.True(hp25 > hp24 + 25);
    }

    [Fact]
    public void TalentRoad_CapsRanks_TierUpGrantsPerk()
    {
        var e = Engine(); var s = Player(e);
        SetBalance(s, Currencies.Biomass, 10_000_000);
        var tier = WormSystem.CurrentTier(s, C)!;
        s.Worm.Level = tier.RequiredWormLevel;
        Assert.Contains("maxed", Fail(e, ref s, new WormTalentTierUp()));
        foreach (var b in tier.Buttons)
        {
            Ok(e, ref s, new WormTalentUpgrade { Button = b.Key, Ranks = b.MaxRank });
            Assert.Contains("cap", Fail(e, ref s, new WormTalentUpgrade { Button = b.Key }));
        }
        var hpBefore = WormSystem.WormBaseStats(s, C).Value(StatId.Hp);
        Ok(e, ref s, new WormTalentTierUp());
        Assert.Equal(1, s.Worm.TalentTiersDone);
        Assert.Equal(hpBefore, WormSystem.WormBaseStats(s, C).Value(StatId.Hp), 6); // completed tier keeps its stats
        // Tier 0 perk = stamina cap, applied through the economy regen-bonus provider.
        Assert.Equal(TalentPerkKinds.StaminaCap, tier.Perk.Kind);
        e.Settle(s, TestHelpers.T0 + 1000);
        Assert.Equal(60 + (long)tier.Perk.Value, EconomySystem.RegenCap(s, C, Currencies.Stamina));
    }

    [Fact]
    public void TalentTier_RequiresWormLevel()
    {
        var e = Engine(); var s = Player(e);
        SetBalance(s, Currencies.Biomass, 10_000_000);
        s.Worm.TalentTiersDone = 5;
        var tier = WormSystem.CurrentTier(s, C)!;
        Assert.True(tier.RequiredWormLevel > s.Worm.Level);
        Assert.Contains("level", Fail(e, ref s, new WormTalentUpgrade { Button = "hp" }));
    }

    [Fact]
    public void TalentPerks_DraftRerolls_ReachLoadout()
    {
        var e = Engine(); var s = Player(e);
        s.Worm.TalentTiersDone = 10; // tiers 0..9 done: tier 4 grants a draft reroll
        Assert.Equal(1, WormSystem.PerkTotal(s, C, TalentPerkKinds.DraftRerolls));
        Assert.True(WormSystem.PerkTotal(s, C, TalentPerkKinds.RunCoinsPercent) > 0);
        Assert.Equal(1, e.BuildLoadout(s, TestHelpers.T0).DraftRerolls);
    }
}
