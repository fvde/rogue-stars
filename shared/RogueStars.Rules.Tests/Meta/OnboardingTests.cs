using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Meta.MetaTestKit;

namespace RogueStars.Rules.Tests.Meta;

public class OnboardingTests
{
    [Fact]
    public void NewPlayer_LockedFeaturesAreVisibleWithRequirementText()
    {
        var e = Engine(); var s = Player(e);
        Assert.False(Features.IsUnlocked(s, e.Content, Features.Breeding));
        var locked = Features.Locked(s, e.Content);
        var breeding = locked.Single(f => f.Id == Features.Breeding);
        Assert.Equal("Clear main level 14 and Unlock Aliens", breeding.RequirementText);
        Assert.Equal(2, breeding.Progress.Count);
        Assert.Equal(e.Content.FeatureDefs.Count, Features.List(s, e.Content).Count);
        Assert.True(Features.IsUnlocked(s, e.Content, "not_a_gated_feature"));
    }

    [Fact]
    public void FeatureOrder_FollowsGdd()
    {
        var c = ContentDb.Default;
        string[] gdd = { Features.Parts, Features.BiomassFarm, Features.WormLevel, Features.Laboratory, Features.GrainFarm, Features.Aliens,
            Features.AlienSquad, Features.Blueprints, Features.OreMine, Features.Heroes, Features.Mounts, Features.Breeding, Features.Arena,
            Features.Guilds, Features.Invasions, Features.Overcharged, Features.Burrow, Features.DarkSide };
        for (int i = 1; i < gdd.Length; i++) Assert.True(c.Feature(gdd[i - 1])!.Order < c.Feature(gdd[i])!.Order, gdd[i]);
        // Every main-level gate along the GDD order is non-decreasing.
        long prev = 0;
        foreach (var id in gdd)
        {
            var main = c.Feature(id)!.Requires.Items.FirstOrDefault(r => r.Kind == RequirementKind.MainLevel);
            if (main == null) continue;
            Assert.True(main.Target >= prev, id); prev = main.Target;
        }
    }

    [Fact]
    public void RequirementEngine_AllAnyCountersFlagsCurrency()
    {
        var e = Engine(); var s = Player(e);
        s.Wallet.Balances.Remove(Currencies.Ore); // starting wallet
        var all = RequirementSet.All(new RequirementDef(RequirementKind.Counter, "runs_completed", 3), new RequirementDef(RequirementKind.Flag, "owns_x", 1));
        var any = RequirementSet.AnyOf(new RequirementDef(RequirementKind.Currency, Currencies.Ore, 100), new RequirementDef(RequirementKind.WormLevel, null, 5));
        Assert.False(Requirements.IsMet(s, e.Content, all));
        s.Count("runs_completed", 3);
        Assert.False(Requirements.IsMet(s, e.Content, all));
        s.Flags.Add("owns_x");
        Assert.True(Requirements.IsMet(s, e.Content, all));
        Assert.False(Requirements.IsMet(s, e.Content, any));
        s.Wallet.Add(Currencies.Ore, 100);
        Assert.True(Requirements.IsMet(s, e.Content, any));
        Assert.True(Requirements.IsMet(s, e.Content, new RequirementSet()));
        var age = new RequirementDef(RequirementKind.AccountAgeDays, null, 2);
        Assert.False(Requirements.IsMet(s, e.Content, age, TestHelpers.T0 + D));
        Assert.True(Requirements.IsMet(s, e.Content, age, TestHelpers.T0 + 2 * D));
    }

    [Fact]
    public void Unlock_IsDetected_Sticky_AndCelebratedOnceWithGift()
    {
        var e = Engine(); var s = Player(e);
        long t = TestHelpers.T0;
        s.Counters[Requirements.MainLevelCounter] = 6;
        var r = Ok(e, ref s, new LobbyPopupShown { PopupId = "x" }, t);
        Assert.Contains(r.Events, ev => ev.Type == "feature_unlocked" && ev.Args[0] == Features.Aliens);
        Assert.Contains(r.Events, ev => ev.Type == "feature_unlocked" && ev.Args[0] == Features.AlienPulls);
        Assert.DoesNotContain(r.Events, ev => ev.Type == "feature_unlocked" && ev.Args[0] == Features.Breeding);
        Assert.Contains(Features.Aliens, OnboardingSystem.PendingCelebrations(s, e.Content));

        long dia = Bal(s, Currencies.Diamonds);
        Ok(e, ref s, new FeatureCelebrate { FeatureId = Features.Aliens }, t);
        Assert.Equal(dia + 50, Bal(s, Currencies.Diamonds));
        Fail(e, ref s, new FeatureCelebrate { FeatureId = Features.Aliens }, t);
        Fail(e, ref s, new FeatureCelebrate { FeatureId = Features.Breeding }, t);

        s.Counters[Requirements.MainLevelCounter] = 0; // sticky: never re-locks
        Assert.True(Features.IsUnlocked(s, e.Content, Features.Aliens));
    }

    [Fact]
    public void AnyOfGate_Forge_UnlocksByWormLevel()
    {
        var e = Engine(); var s = Player(e);
        Assert.False(Features.IsUnlocked(s, e.Content, Features.Forge));
        s.Worm.Level = 25; // gates read the real worm level (provider)
        Assert.True(Features.IsUnlocked(s, e.Content, Features.Forge));
        Assert.False(Features.IsUnlocked(s, e.Content, Features.Burrow)); // all-of: needs main 25 too
    }

    [Fact]
    public void DevUnlockAll_UnlocksEverything()
    {
        var e = Engine(); var s = Player(e);
        UnlockAll(s);
        Assert.All(e.Content.FeatureOrder, f => Assert.True(Features.IsUnlocked(s, e.Content, f.Id)));
    }

    [Fact]
    public void Tutorial_StepsInOrder_SetFlags_GrantOnce()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        Fail(e, ref s, new TutorialAdvance { Step = "first_roll" }, t);
        Ok(e, ref s, new TutorialAdvance { Step = "wake_up" }, t);
        Assert.Contains("tut:wake_up", s.Flags);
        Fail(e, ref s, new TutorialAdvance { Step = "wake_up" }, t);
        Ok(e, ref s, new TutorialAdvance { Step = "first_roll" }, t);
        Ok(e, ref s, new TutorialAdvance { Step = "first_battle" }, t);
        long dia = Bal(s, Currencies.Diamonds);
        Ok(e, ref s, new TutorialAdvance { Step = "first_run" }, t);
        Assert.Equal(dia + 20, Bal(s, Currencies.Diamonds));
        Fail(e, ref s, new TutorialAdvance { Step = "equip_part" }, t); // feature Parts still locked
        s.Counters[Requirements.MainLevelCounter] = 1;
        Ok(e, ref s, new TutorialAdvance { Step = "equip_part" }, t);
        Fail(e, ref s, new TutorialAdvance { Step = "nope" }, t);
    }

    [Fact]
    public void LobbyPopups_CappedPerDay()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        for (int i = 0; i < e.Content.Onboarding.MaxLobbyPopupsPerDay; i++) Ok(e, ref s, new LobbyPopupShown { PopupId = "p" + i }, t);
        Assert.False(OnboardingSystem.CanShowLobbyPopup(s, e.Content, t));
        Fail(e, ref s, new LobbyPopupShown { PopupId = "x" }, t);
        Ok(e, ref s, new LobbyPopupShown { PopupId = "x" }, Day0 + D);
    }
}
