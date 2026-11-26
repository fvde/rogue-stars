using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Meta.MetaTestKit;

namespace RogueStars.Rules.Tests.Meta;

/// <summary>GDD §16 level unlocks: every feature unlocks at a main level cleared, never by calendar day.</summary>
public class OnboardingTests
{
    /// <summary>T0 is 08:00 UTC: account day <paramref name="day"/> (1 = creation day) at 09:00.</summary>
    private static long OnDay(int day) => Day0 + (day - 1) * D + 9 * H;

    /// <summary>Runs one harmless command (the onboarding settler runs first) and returns the unlocked ids.</summary>
    private static List<string> Tick(GameEngine e, ref GameState s, long t) =>
        Ok(e, ref s, new LobbyPopupShown { PopupId = "tick" + t }, t).Events.Where(ev => ev.Type == "feature_unlocked").Select(ev => ev.Args[0]).ToList();

    /// <summary>
    /// The level-unlock table: feature → main level cleared. The Signals come with Parts (1), Aliens (6) and the City (15) (owner,
    /// 2026-11-25: same feature ids); building features unlock with their blueprint instead (<see cref="BuildingFeatures"/>).
    /// </summary>
    public static readonly (string id, int level)[] Schedule =
    {
        (Features.Parts, 1), (Features.RunShop, 2), (Features.WormLevel, 3), (Features.RunMiniboss, 4), (Features.RunTwoDice, 5),
        (Features.Aliens, 6), (Features.AlienSquad, 6), (Features.AutoRoll, 7), (Features.RunCampfire, 9),
        (Features.RunSignatures, 11), (Features.Shop, 12), (Features.RunMinigames, 14), (Features.BiomassFarm, 15),
        (Features.Heroes, 18), (Features.PackageIce, 24), (Features.Mounts, 26), (Features.Runes, 28), (Features.Funds, 30),
        (Features.Arena, 32), (Features.SeasonPass, 36), (Features.PackageLightning, 38), (Features.Overcharged, 40),
        (Features.Guilds, 42), (Features.Invasions, 44), (Features.Burrow, 46), (Features.PackageCounter, 48), (Features.Forge, 50),
        (Features.DarkSide, 53), (Features.PackageAlien, 56), (Features.PackageTime, 60),
    };

    /// <summary>
    /// Building features: unlocked the moment the blueprint is owned (after the City), never by level. The Launch Pod is not one any more:
    /// it comes with the Aliens unlock on the crash site (owner, 2026-11-25), one unlock moment with the Aliens (<see cref="Grouped"/>).
    /// </summary>
    public static readonly (string id, string blueprint)[] BuildingFeatures =
    {
        (Features.Laboratory, ContentDb.LaboratoryId), (Features.GrainFarm, "bp_grain_farm"),
        (Features.OreMine, "bp_ore_mine"), (Features.Breeding, "bp_breeding_pen"),
    };

    /// <summary>Features that unlock in another feature's moment (member → group): the Launch Pod with the Aliens.</summary>
    public static readonly Dictionary<string, string> Grouped = new() { [Features.AlienSquad] = Features.Aliens };

    /// <summary>The unlock moments in schedule order: each group's ids (the Aliens moment = Aliens + Launch Pod).</summary>
    private static List<(string[] ids, int level)> Moments() =>
        Schedule.Where(x => !Grouped.ContainsKey(x.id)).Select(x => x.level).Distinct()
            .Select(level => (Schedule.Where(y => y.level == level).Select(y => y.id).ToArray(), level)).ToList();

    private static List<FeatureDef> LevelFeatures(ContentDb c) => c.FeatureOrder.Where(f => Features.UnlockLevel(f) > 0).ToList();

    [Fact]
    public void Content_FollowsTheLevelSchedule_OneFeaturePerLevel_NoDayGates()
    {
        var c = ContentDb.Default;
        Assert.Equal(Schedule.Length + BuildingFeatures.Length, c.FeatureOrder.Count);
        var levelFeatures = LevelFeatures(c);
        Assert.Equal(Schedule.Length, levelFeatures.Count);
        foreach (var (id, bp) in BuildingFeatures)
        {
            var f = c.Feature(id)!;
            Assert.Equal(0, Features.UnlockLevel(f));
            Assert.Contains(f.Requires.Items, r => r.Kind == RequirementKind.Feature && r.Key == Features.BiomassFarm);
            Assert.Contains(f.Requires.Items, r => r.Kind == RequirementKind.Blueprint && r.Key == bp);
            Assert.DoesNotContain(f.UnlockGrants, g => g.Kind == RewardKinds.Blueprint); // the blueprint is the trigger, not a gift
        }
        for (int i = 0; i < Schedule.Length; i++)
        {
            var (id, level) = Schedule[i];
            var f = levelFeatures[i];
            Assert.Equal(id, f.Id);
            Assert.Equal(Grouped.TryGetValue(id, out var g) ? g : id, f.GroupId); // every feature is its own unlock (own screen) but the Launch Pod
            Assert.False(string.IsNullOrEmpty(f.Description), id);
            var req = Assert.Single(f.Requires.Items);
            Assert.Equal(RequirementKind.MainLevel, req.Kind);
            Assert.Equal(level, req.Target);
        }
        Assert.DoesNotContain(c.FeatureOrder, f => f.Requires.Items.Any(r => r.Kind == RequirementKind.AccountAgeDays));
        Assert.Equal(c.FeatureOrder.Count - Grouped.Count, Features.Groups(c).Count);
    }

    [Fact]
    public void DailyQuests_AreNoFeature_NoUnlockMoment_NoIntro_NoGift()
    {
        var e = Engine(); var s = Player(e);
        Assert.Null(e.Content.Feature("quests")); // simply there: no feature record, no intro, no teaser entry, no unlock grant
        Assert.DoesNotContain(e.Content.FeatureOrder, f => f.Name.Contains("Quest") || f.Name.Contains("Goal"));
        Assert.Empty(Tick(e, ref s, TestHelpers.T0));
        s.Counters[Requirements.MainLevelCounter] = 1;
        Assert.Equal(new[] { Features.Parts }, Tick(e, ref s, TestHelpers.T0 + D)); // level 1: Parts only, one moment
        Assert.Equal(new[] { Features.Parts }, OnboardingSystem.PendingCelebrations(s, e.Content));
        Assert.Equal(new[] { Features.RunShop }, Features.Next(s, e.Content, TestHelpers.T0 + D)!.Ids);
    }

    [Fact]
    public void Schedule_IsSpacedOut_OnePerLevelEarly_WiderLater()
    {
        var levels = LevelFeatures(ContentDb.Default).Where(f => f.GroupId == f.Id).Select(f => (int)Features.UnlockLevel(f)).ToList();
        Assert.Equal(1, levels[0]); // the first unlock right after the tutorial run
        for (int i = 1; i < levels.Count; i++)
        {
            int gap = levels[i] - levels[i - 1];
            Assert.True(gap >= 1, "two moments at level " + levels[i]);
            // Was one per level to 20 / two to 50: the Signals now come with Parts / Aliens / the City and the buildings with their
            // blueprints (owner, 2026-11-25), which leaves level gaps up to 6 (Heroes 18 → Ice package 24).
            int max = levels[i] <= 24 ? 6 : 4;
            Assert.True(gap <= max, "gap of " + gap + " levels before level " + levels[i]);
        }
    }

    [Fact]
    public void Order_SystemsComeBeforeWhatDependsOnThem()
    {
        var c = ContentDb.Default;
        int L(string id) => (int)Features.UnlockLevel(c.Feature(id)!);
        void Before(string first, params string[] later)
        {
            foreach (var l in later) Assert.True(L(first) < L(l), first + " must unlock before " + l);
        }
        Before(Features.Parts, Features.Runes, Features.Forge, Features.WormLevel);
        Before(Features.WormLevel, Features.BiomassFarm); // the farm's Biomass has a use the moment it is built
        Before(Features.Aliens, Features.BiomassFarm); // aliens staff the buildings
        Assert.Equal(Features.Aliens, c.Feature(Features.AlienSquad)!.GroupId); // the Launch Pod (squad) comes with the Aliens
        Assert.Equal(Features.Parts, Features.PartPulls); // the Signals come with their system's unlock
        Assert.Equal(Features.Aliens, Features.AlienPulls);
        Assert.Equal(Features.BiomassFarm, Features.Blueprints);
        // building features (Launch Pod, Laboratory, farms, Ore Mine, Breeding Pen) need the City and their blueprint
        foreach (var (id, _) in BuildingFeatures)
            Assert.Contains(c.Feature(id)!.Requires.Items, r => r.Kind == RequirementKind.Feature && r.Key == Features.BiomassFarm);
        Before(Features.BiomassFarm, Features.PackageIce); // research packages after the Laboratory can exist
        Before(Features.PackageIce, Features.PackageLightning);
        Before(Features.PackageLightning, Features.PackageCounter);
        Before(Features.PackageCounter, Features.PackageAlien);
        Before(Features.PackageAlien, Features.PackageTime);
        Before(Features.Aliens, Features.PackageAlien);
        Before(Features.Guilds, Features.Invasions);
        Before(Features.PartPulls, Features.AlienPulls, Features.Blueprints);
        Before(Features.RunShop, Features.Shop);
        // Lab packages: each package node needs its feature; the Combo package comes with the Laboratory.
        Assert.Null(c.LabNode("pkg_combo")!.RequiresFeature);
        foreach (var (node, feature) in new[] { ("pkg_ice", Features.PackageIce), ("pkg_lightning", Features.PackageLightning),
                     ("pkg_counter", Features.PackageCounter), ("pkg_alien", Features.PackageAlien), ("pkg_time", Features.PackageTime) })
            Assert.Equal(feature, c.LabNode(node)!.RequiresFeature);
    }

    [Fact]
    public void RewardsFollowUnlocks_EveryGrantAndGiftIsUsableTheMomentItArrives()
    {
        var c = ContentDb.Default;
        var e = new GameEngine(c);
        var s = e.NewPlayer("p", TestHelpers.T0, 1);
        foreach (var f in c.FeatureOrder)
        {
            s.Onboarding.Unlocked[f.Id] = TestHelpers.T0; // everything up to and including f
            s.Counters[Requirements.MainLevelCounter] = Features.UnlockLevel(f);
            foreach (var r in f.UnlockGrants.Concat(f.UnlockRewards))
                Assert.True(RewardGate.HasUse(s, c, r, TestHelpers.T0), f.Id + " grants " + r.Kind + ":" + r.Id + " before it has a use");
        }
    }

    [Fact]
    public void EveryFeature_HasAnIntro_OfTwoToFourShortPanels()
    {
        foreach (var f in ContentDb.Default.FeatureOrder)
        {
            Assert.InRange(f.Intro.Count, 2, 4);
            Assert.False(string.IsNullOrEmpty(f.Art), f.Id);
            foreach (var step in f.Intro)
            {
                Assert.False(string.IsNullOrWhiteSpace(step.Text), f.Id);
                Assert.True(step.Text.Length <= 52, f.Id + ": \"" + step.Text + "\" is too long for a panel");
                Assert.EndsWith(".", step.Text);
                Assert.False(string.IsNullOrEmpty(step.Art), f.Id);
                Assert.Contains(step.Anim, IntroAnim.All);
            }
            // a few kinds of motion per screen, not the same one on every panel
            if (f.Intro.Count >= 3) Assert.True(f.Intro.Select(x => x.Anim).Distinct().Count() >= 2, f.Id);
        }
    }

    [Fact]
    public void NewPlayer_NothingUnlocked_NextIsParts_AtLevel1()
    {
        var e = Engine(); var s = Player(e);
        Assert.Equal(e.Content.Onboarding.Version, s.Onboarding.Version);
        Assert.False(Features.IsUnlocked(s, e.Content, Features.Parts));
        Assert.True(Features.IsUnlocked(s, e.Content, "not_a_gated_feature"));
        var next = Features.Next(s, e.Content, TestHelpers.T0)!;
        Assert.Equal(new[] { Features.Parts }, next.Ids);
        Assert.Equal(1, next.Level);
        Assert.Equal(0, next.CurrentLevel);
        Assert.Equal(1, next.LevelsToGo);
        Assert.Equal("clear level 1", next.Text);
        Assert.Equal(e.Content.Feature(Features.Parts)!.Description, next.Description);
        Assert.Equal(e.Content.FeatureDefs.Count, Features.List(s, e.Content).Count);
    }

    [Fact]
    public void EachFeature_UnlocksTheMomentItsLevelIsCleared_InOrder()
    {
        var e = Engine(); var s = Player(e);
        long t = TestHelpers.T0;
        foreach (var (ids, level) in Moments())
        {
            s.Counters[Requirements.MainLevelCounter] = level - 1;
            Tick(e, ref s, t);
            Assert.False(Features.IsUnlocked(s, e.Content, ids[0]), ids[0] + " before level " + level);
            Assert.Equal("clear level " + level, Features.Next(s, e.Content, t)!.Text);
            s.Counters[Requirements.MainLevelCounter] = level;
            Assert.Equal(ids, Tick(e, ref s, t)); // same day, no daily limit: one moment per level (Aliens + Launch Pod together)
            t += D; // the tick command is a lobby popup (capped per day)
        }
        Assert.Null(Features.Next(s, e.Content, t));
    }

    [Fact]
    public void NoCalendarGate_ABingePlayerGetsEverythingOnDay1_AReturningPlayerAllAtOnce()
    {
        var e = Engine(); var s = Player(e);
        s.Counters[Requirements.MainLevelCounter] = 60;
        var day1 = Tick(e, ref s, TestHelpers.T0);
        Assert.Equal(Schedule.Select(x => x.id), day1); // in order, all on the creation day
        Assert.All(Schedule, f => Assert.True(Features.IsUnlocked(s, e.Content, f.id)));
        Assert.All(BuildingFeatures, f => Assert.False(Features.IsUnlocked(s, e.Content, f.id))); // no blueprint pulled yet

        // A player who comes back after weeks catches up at once, not one feature per day.
        var t2 = Player(e);
        Tick(e, ref t2, TestHelpers.T0);
        t2.Counters[Requirements.MainLevelCounter] = 20;
        var back = Tick(e, ref t2, OnDay(30));
        Assert.Equal(Schedule.Where(x => x.level <= 20).Select(x => x.id), back);
        Assert.Equal(Features.PackageIce, Features.Next(t2, e.Content, OnDay(30))!.Ids[0]); // building features are never teased
    }

    [Fact]
    public void FeatureRequire_FollowsRecordedUnlocks()
    {
        var e = Engine(); var s = Player(e);
        s.Wallet.Add(Currencies.Diamonds, 10_000);
        var pull = new GachaPull { PullType = GachaType.Part, Payment = GachaPayment.Diamonds };
        Assert.Equal("feature locked", Fail(e, ref s, pull, TestHelpers.T0));
        s.Counters[Requirements.MainLevelCounter] = 1;
        Ok(e, ref s, pull, TestHelpers.T0); // the settler records Parts (and its Part Signal) before the pull
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
        // The account-age kind still works for other content (offers, events); feature gates never use it.
        var age = new RequirementDef(RequirementKind.AccountAgeDays, null, 2);
        Assert.False(Requirements.IsMet(s, e.Content, age, TestHelpers.T0 + D));
        Assert.True(Requirements.IsMet(s, e.Content, age, Day0 + 2 * D)); // reset days, not 48 h
        Assert.Equal("Unlocks on day 3", Requirements.Describe(e.Content, age));
    }

    [Fact]
    public void Unlock_IsDetected_Sticky_AndCelebratedOnceWithGift()
    {
        var e = Engine(); var s = Player(e);
        s.Counters[Requirements.MainLevelCounter] = 15;
        var r = Ok(e, ref s, new LobbyPopupShown { PopupId = "x" }, TestHelpers.T0);
        Assert.Contains(r.Events, ev => ev.Type == "feature_unlocked" && ev.Args[0] == Features.Aliens);
        Assert.Contains(r.Events, ev => ev.Type == "feature_unlocked" && ev.Args[0] == Features.AlienSquad); // one moment with the Aliens
        Assert.NotNull(AlienSystem.Owned(s, "alien_blorp")); // unlock grant
        Assert.True(CitySystem.IsBuilt(CitySystem.LaunchPod(s))); // the crash site's Launch Pod
        Assert.Contains(Features.Aliens, OnboardingSystem.PendingCelebrations(s, e.Content));
        Assert.Contains(Features.RunShop, OnboardingSystem.PendingCelebrations(s, e.Content)); // a new player sees every intro

        long t = TestHelpers.T0;
        long dia = Bal(s, Currencies.Diamonds);
        Ok(e, ref s, new FeatureCelebrate { FeatureId = Features.Aliens }, t);
        Assert.Equal(dia + 50, Bal(s, Currencies.Diamonds));
        Fail(e, ref s, new FeatureCelebrate { FeatureId = Features.Aliens }, t);
        Fail(e, ref s, new FeatureCelebrate { FeatureId = Features.Breeding }, t);

        s.Counters[Requirements.MainLevelCounter] = 0; // sticky: never re-locks
        Tick(e, ref s, t);
        Assert.True(Features.IsUnlocked(s, e.Content, Features.Aliens));
    }

    [Fact]
    public void OldSave_KeepsItsUnlocks_AndGetsTheNewRunFeaturesSilently()
    {
        var e = Engine(); var s = Player(e);
        // A save from the day-gated schedule (onboarding v2 before level unlocks): day 3, main level 20, five unlocks.
        s.Onboarding.Version = 0;
        s.Counters[Requirements.MainLevelCounter] = 20;
        long old = TestHelpers.T0 - 2 * D;
        foreach (var id in new[] { Features.Parts, Features.BiomassFarm, Features.WormLevel, Features.Arena })
            s.Onboarding.Unlocked[id] = old; // Arena: as if a dev cheat had opened it early: never re-locked
        s.Onboarding.Celebrated.UnionWith(new[] { Features.Parts, Features.BiomassFarm, Features.WormLevel, Features.Arena });
        long dia = Bal(s, Currencies.Diamonds);

        var unlocked = Tick(e, ref s, TestHelpers.T0);
        Assert.Equal(e.Content.Onboarding.Version, s.Onboarding.Version);
        // kept, with their old unlock times
        foreach (var id in new[] { Features.Parts, Features.BiomassFarm, Features.WormLevel, Features.Arena })
            Assert.Equal(old, s.Onboarding.Unlocked[id]);
        // features the save's content already had (run content, auto roll): recorded and celebrated at once, no unlock moment
        foreach (var id in new[] { Features.RunShop, Features.RunMiniboss, Features.RunTwoDice, Features.AutoRoll, Features.RunCampfire,
                     Features.RunSignatures, Features.RunMinigames })
        {
            Assert.True(Features.IsUnlocked(s, e.Content, id), id);
            Assert.Contains(id, s.Onboarding.Celebrated);
            Assert.DoesNotContain(id, unlocked);
        }
        Assert.False(Features.IsUnlocked(s, e.Content, Features.PackageIce)); // level 24: later, with its own moment
        // features that are new to this player at its level: normal unlock moments, in order
        var expected = Schedule.Where(x => x.level <= 20 && e.Content.Feature(x.id)!.Since == 1 && !new[] { Features.Parts,
            Features.BiomassFarm, Features.WormLevel }.Contains(x.id)).Select(x => x.id).ToList();
        Assert.Equal(expected, unlocked);
        Assert.Equal(expected, OnboardingSystem.PendingCelebrations(s, e.Content));
        Assert.Equal(dia, Bal(s, Currencies.Diamonds)); // run features have no gift; the rest pay on their celebration

        // runs once
        s.Counters[Requirements.MainLevelCounter] = 24;
        Assert.Equal(new[] { Features.PackageIce }, Tick(e, ref s, TestHelpers.T0 + H));
        Assert.Contains(Features.PackageIce, OnboardingSystem.PendingCelebrations(s, e.Content));
    }

    [Fact]
    public void DevUnlockAll_UnlocksEverything()
    {
        var e = Engine(); var s = Player(e);
        UnlockAll(s);
        Assert.All(e.Content.FeatureOrder, f => Assert.True(Features.IsUnlocked(s, e.Content, f.Id)));
        Assert.Null(Features.Next(s, e.Content, TestHelpers.T0));
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
        Ok(e, ref s, new TutorialAdvance { Step = "equip_part" }, t); // the settler records the Parts unlock first
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
