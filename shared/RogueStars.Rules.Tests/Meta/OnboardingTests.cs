using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Meta.MetaTestKit;

namespace RogueStars.Rules.Tests.Meta;

public class OnboardingTests
{
    /// <summary>T0 is 08:00 UTC: account day <paramref name="day"/> (1 = creation day) at 09:00.</summary>
    private static long OnDay(int day) => Day0 + (day - 1) * D + 9 * H;

    /// <summary>Runs one harmless command (the onboarding settler runs first) and returns the unlocked ids.</summary>
    private static List<string> Tick(GameEngine e, ref GameState s, long t) =>
        Ok(e, ref s, new LobbyPopupShown { PopupId = "tick" + t }, t).Events.Where(ev => ev.Type == "feature_unlocked").Select(ev => ev.Args[0]).ToList();

    /// <summary>The approved onboarding v2 schedule: feature → (earliest day, main level, group).</summary>
    public static readonly (string id, int day, int level, string group)[] Schedule =
    {
        (Features.Parts, 1, 1, null), (Features.Quests, 1, 5, null), (Features.BiomassFarm, 2, 8, null), (Features.WormLevel, 3, 6, null),
        (Features.PartPulls, 4, 12, null), (Features.Shop, 4, 12, Features.PartPulls), (Features.Laboratory, 5, 14, null),
        (Features.GrainFarm, 6, 16, null), (Features.Aliens, 7, 18, null), (Features.AlienPulls, 7, 18, Features.Aliens),
        (Features.AlienSquad, 8, 20, null), (Features.Blueprints, 9, 22, null), (Features.Heroes, 10, 24, null),
        (Features.OreMine, 11, 26, null), (Features.Runes, 11, 26, Features.OreMine), (Features.Mounts, 12, 28, null),
        (Features.Funds, 13, 29, null), (Features.Breeding, 14, 30, null), (Features.SeasonPass, 14, 30, Features.Breeding),
        (Features.Arena, 15, 32, null), (Features.Overcharged, 16, 34, null), (Features.Guilds, 17, 36, null),
        (Features.Invasions, 18, 38, null), (Features.Burrow, 19, 40, null), (Features.Forge, 20, 42, null), (Features.DarkSide, 21, 45, null),
    };

    [Fact]
    public void Content_FollowsTheApprovedSchedule_InOrder()
    {
        var c = ContentDb.Default;
        Assert.Equal(Schedule.Length, c.FeatureOrder.Count);
        for (int i = 0; i < Schedule.Length; i++)
        {
            var (id, day, level, group) = Schedule[i];
            var f = c.FeatureOrder[i];
            Assert.Equal(id, f.Id);
            Assert.Equal(group ?? id, f.GroupId);
            Assert.False(string.IsNullOrEmpty(f.Description), id);
            Assert.Equal(level, f.Requires.Items.Single(r => r.Kind == RequirementKind.MainLevel).Target);
            var age = f.Requires.Items.SingleOrDefault(r => r.Kind == RequirementKind.AccountAgeDays);
            Assert.Equal(day, age == null ? 1 : (int)age.Target + 1);
            Assert.Equal(age == null ? 1 : 2, f.Requires.Items.Count);
        }
        // Day 1 unlocks are only Parts and Daily goals; after that at most one group per day.
        var days = Features.Groups(c).Select(g => Schedule.First(x => x.id == g[0].Id).day).ToList();
        Assert.Equal(days.Count(d => d > 1), days.Where(d => d > 1).Distinct().Count());
    }

    [Fact]
    public void NewPlayer_NothingUnlocked_NextIsParts_AtLevel1()
    {
        var e = Engine(); var s = Player(e);
        Assert.False(Features.IsUnlocked(s, e.Content, Features.Parts));
        Assert.True(Features.IsUnlocked(s, e.Content, "not_a_gated_feature"));
        var next = Features.Next(s, e.Content, TestHelpers.T0)!;
        Assert.Equal(new[] { Features.Parts }, next.Ids);
        Assert.Equal(1, next.Level);
        Assert.Equal(0, next.CurrentLevel);
        Assert.Equal(0, next.DaysToWait);
        Assert.Equal("clear level 1", next.Text);
        Assert.Equal(e.Content.FeatureDefs.Count, Features.List(s, e.Content).Count);
    }

    [Fact]
    public void DayGate_LevelAloneDoesNotUnlock_UntilTheAccountDay()
    {
        var e = Engine(); var s = Player(e);
        s.Counters[Requirements.MainLevelCounter] = 12;
        var day1 = Tick(e, ref s, TestHelpers.T0);
        Assert.Equal(new[] { Features.Parts, Features.Quests }, day1); // day 1: everything due today, no limit
        Assert.False(Features.IsUnlocked(s, e.Content, Features.BiomassFarm)); // level 8 met, day 2 not reached
        var next = Features.Next(s, e.Content, TestHelpers.T0 + H)!;
        Assert.Equal(Features.BiomassFarm, next.Ids[0]);
        Assert.True(next.LevelMet);
        Assert.Equal(2, next.Day);
        Assert.Equal(1, next.DaysToWait);
        Assert.Equal("unlocks tomorrow", next.Text);

        // Day 2 starts at the daily reset (calendar day, not 24 h after creation): 23:59 on day 1 is still day 1.
        Assert.Empty(Tick(e, ref s, Day0 + D - 60_000));
        Assert.Equal(new[] { Features.BiomassFarm }, Tick(e, ref s, OnDay(2)));
        Assert.Equal(1, Requirements.AccountAgeDays(s, e.Content, OnDay(2)));
    }

    [Fact]
    public void AfterDay1_AtMostOneGroupPerDay_AndGroupsUnlockTogether()
    {
        var e = Engine(); var s = Player(e);
        Tick(e, ref s, TestHelpers.T0);
        s.Counters[Requirements.MainLevelCounter] = 45; // a binge player is far ahead: the day gate paces the unlocks
        Assert.Equal(new[] { Features.Parts, Features.Quests }, Tick(e, ref s, TestHelpers.T0 + H));
        Assert.Equal(new[] { Features.BiomassFarm }, Tick(e, ref s, OnDay(2)));
        Assert.Empty(Tick(e, ref s, OnDay(2) + 5 * H)); // the day's unlock is used
        var waiting = Features.Next(s, e.Content, OnDay(2) + 5 * H)!;
        Assert.Equal(Features.WormLevel, waiting.Ids[0]);
        Assert.Equal("unlocks tomorrow", waiting.Text);
        Assert.Equal(new[] { Features.WormLevel }, Tick(e, ref s, OnDay(3)));
        Assert.Equal(new[] { Features.PartPulls, Features.Shop }, Tick(e, ref s, OnDay(4))); // a pair = one group

        // A returning player who skipped days catches up one group per day, in order, never several at once.
        var unlocked = Tick(e, ref s, OnDay(12));
        Assert.Equal(new[] { Features.Laboratory }, unlocked);
        var next = Features.Next(s, e.Content, OnDay(12))!;
        Assert.Equal(Features.GrainFarm, next.Ids[0]);
        Assert.True(next.WaitingForDailyLimit);
        Assert.Equal("unlocks tomorrow", next.Text);
        Assert.Equal(new[] { Features.GrainFarm }, Tick(e, ref s, OnDay(13)));
        Assert.Equal(new[] { Features.Aliens, Features.AlienPulls }, Tick(e, ref s, OnDay(14)));
        Assert.NotNull(AlienSystem.Owned(s, "alien_blorp")); // unlock grant
    }

    [Fact]
    public void Next_ShowsTheLevelGateAndTheDayGate()
    {
        var e = Engine(); var s = Player(e);
        s.Counters[Requirements.MainLevelCounter] = 6;
        Tick(e, ref s, TestHelpers.T0);
        var n = Features.Next(s, e.Content, TestHelpers.T0)!;
        Assert.Equal(Features.BiomassFarm, n.Ids[0]);
        Assert.Equal(8, n.Level);
        Assert.Equal(6, n.CurrentLevel);
        Assert.False(n.LevelMet);
        Assert.Equal("clear level 8 · unlocks tomorrow", n.Text);
        Assert.Equal(2, n.Progress.Count);

        // A slow player: the level gate is the limit. Worm Level (day 3, level 6) does not wait for the City (level 8).
        Assert.Equal(new[] { Features.WormLevel }, Tick(e, ref s, OnDay(5)));
        n = Features.Next(s, e.Content, OnDay(5))!;
        Assert.Equal("clear level 8 · unlocks tomorrow", n.Text); // today's slot went to Worm Level
        n = Features.Next(s, e.Content, OnDay(6))!;
        Assert.Equal(0, n.DaysToWait);
        Assert.Equal("clear level 8", n.Text);

        // A pair is teased as one entry; far-away day gates count the days.
        var t = Player(e);
        t.Counters[Requirements.MainLevelCounter] = 10;
        t.Onboarding.Unlocked[Features.Parts] = t.Onboarding.Unlocked[Features.Quests] = t.CreatedMs;
        t.Onboarding.Unlocked[Features.BiomassFarm] = t.Onboarding.Unlocked[Features.WormLevel] = t.CreatedMs;
        n = Features.Next(t, e.Content, TestHelpers.T0)!;
        Assert.Equal("Part Signal + Shop", n.Name);
        Assert.Equal("clear level 12 · unlocks in 3 days", n.Text);

        UnlockAll(t);
        Assert.Null(Features.Next(t, e.Content, TestHelpers.T0));
    }

    [Fact]
    public void FeatureRequire_FollowsRecordedUnlocks()
    {
        var e = Engine(); var s = Player(e);
        s.Counters[Requirements.MainLevelCounter] = 26;
        s.Wallet.Add(Currencies.Diamonds, 10_000);
        var pull = new GachaPull { PullType = GachaType.Part, Payment = GachaPayment.Diamonds };
        Assert.Equal("feature locked", Fail(e, ref s, pull, TestHelpers.T0));
        Assert.Equal("feature locked", Fail(e, ref s, pull, OnDay(4))); // days 2 and 3 unlocked other groups first: not yet
        Tick(e, ref s, OnDay(2));
        Tick(e, ref s, OnDay(3));
        Assert.Equal("feature locked", Fail(e, ref s, pull, OnDay(3) + H));
        Ok(e, ref s, pull, OnDay(4));
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
        Assert.True(Requirements.IsMet(s, e.Content, age, Day0 + 2 * D)); // reset days, not 48 h
        Assert.Equal("Unlocks on day 3", Requirements.Describe(e.Content, age));
    }

    [Fact]
    public void Unlock_IsDetected_Sticky_AndCelebratedOnceWithGift()
    {
        var e = Engine(); var s = Player(e);
        s.Counters[Requirements.MainLevelCounter] = 18;
        for (int d = 1; d < 7; d++) Tick(e, ref s, OnDay(d));
        var r = Ok(e, ref s, new LobbyPopupShown { PopupId = "x" }, OnDay(7));
        Assert.Contains(r.Events, ev => ev.Type == "feature_unlocked" && ev.Args[0] == Features.Aliens);
        Assert.Contains(r.Events, ev => ev.Type == "feature_unlocked" && ev.Args[0] == Features.AlienPulls);
        Assert.DoesNotContain(r.Events, ev => ev.Type == "feature_unlocked" && ev.Args[0] == Features.Breeding);
        Assert.Contains(Features.Aliens, OnboardingSystem.PendingCelebrations(s, e.Content));

        long t = OnDay(7);
        long dia = Bal(s, Currencies.Diamonds);
        Ok(e, ref s, new FeatureCelebrate { FeatureId = Features.Aliens }, t);
        Assert.Equal(dia + 50, Bal(s, Currencies.Diamonds));
        Fail(e, ref s, new FeatureCelebrate { FeatureId = Features.Aliens }, t);
        Fail(e, ref s, new FeatureCelebrate { FeatureId = Features.Breeding }, t);

        s.Counters[Requirements.MainLevelCounter] = 0; // sticky: never re-locks
        Assert.True(Features.IsUnlocked(s, e.Content, Features.Aliens));
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
        Ok(e, ref s, new TutorialAdvance { Step = "equip_part" }, t); // the settler records the Parts unlock first
        Fail(e, ref s, new TutorialAdvance { Step = "nope" }, t);
    }

    [Fact]
    public void FirstRun_WithoutParts_IsWonForNearlyEverySeed()
    {
        // Parts unlock after level 1, so the scripted first run is played by a worm without parts (eased level 1).
        var c = ContentDb.Default;
        var l1 = c.MainLevel(1);
        Assert.Equal(c.Campaign.EarlyHpPct(1), l1.Modifiers.EnemyHpPct);
        int wins = 0;
        for (int seed = 1; seed <= 60; seed++)
        {
            var e = new GameEngine(c);
            var s = e.NewPlayer("p", TestHelpers.T0, seed);
            Assert.Empty(s.Parts.Equipped);
            Ok(e, ref s, new RunStart { PortalId = l1.PortalId, LevelId = l1.Id, Mode = RunMode.Normal }, TestHelpers.T0);
            for (int i = 0; i < 500 && s.Run.Active != null; i++) Ok(e, ref s, new RunAutoStep { Farming = true, FarmShops = true }, TestHelpers.T0);
            if (s.Run.LastSummary.Won) wins++;
        }
        Assert.InRange(wins, 58, 60);
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
