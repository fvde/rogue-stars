using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Meta.MetaTestKit;

namespace RogueStars.Rules.Tests.Meta;

/// <summary>
/// Daily Quests are there from level 1 (owner, 2026-11-26), so every quest must make sense the moment it is offered: it is hidden until
/// the feature that makes it possible, the early set alone fills both point tracks, and its reward has a use soon.
/// </summary>
public class QuestAvailabilityTests
{
    /// <summary>A new player who has cleared main levels up to <paramref name="level"/> (unlocks recorded by the onboarding settler).</summary>
    private static (GameEngine e, GameState s) At(int level, bool adsDisabled = false)
    {
        var e = Engine();
        e.Content.Shop.AdsDisabled = adsDisabled;
        var s = Player(e);
        s.Counters[Requirements.MainLevelCounter] = level;
        Ok(e, ref s, new LobbyPopupShown { PopupId = "tick" }, TestHelpers.T0);
        return (e, s);
    }

    private static List<QuestDef> Visible(GameEngine e, GameState s, QuestKind kind) => QuestSystem.Visible(s, e.Content, kind, TestHelpers.T0);

    /// <summary>
    /// The early set: what needs no shop purchase, ad, Arena, City or later feature. The shop and ad quests show from the start (the
    /// Shop's offers need no unlock, owner 2026-11-27) but stay extras: points no chest needs.
    /// </summary>
    private static bool Early(QuestDef q) => !q.NeedsAds && !ShopExtras.Contains(q.Id) && q.Feature != Features.Arena && !LateFeatures.Contains(q.Feature ?? "");

    private static readonly HashSet<string> ShopExtras = new() { "d_shop", "d_ad" };

    private static readonly HashSet<string> LateFeatures = new()
    {
        Features.BiomassFarm, Features.Breeding, Features.Invasions, Features.Aliens, Features.Heroes, Features.Mounts, Features.Guilds,
    };

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(14)]
    public void NewPlayer_EarlyDailyQuests_FillTheWholeDailyTrack_WithoutAdsArenaOrTheShop(int level)
    {
        var (e, s) = At(level, adsDisabled: true);
        var daily = Visible(e, s, QuestKind.Daily);
        Assert.DoesNotContain(daily, q => q.NeedsAds); // no ads: the ad quest is hidden even with the Shop open
        long points = daily.Where(Early).Sum(q => q.Points);
        long top = e.Content.PointChests.Where(c => c.Track == PointTrack.Daily).Max(c => c.Points);
        Assert.True(points >= top, $"level {level}: {points} early daily points for a chest at {top}");
        Assert.Equal(100, points); // exactly: nothing is wasted on points no chest needs
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(14)]
    public void NewPlayer_EarlyWeeklyQuests_FillTheWholeWeeklyTrack_BeforeTheCity(int level)
    {
        var (e, s) = At(level, adsDisabled: true);
        var weekly = Visible(e, s, QuestKind.Weekly);
        Assert.All(weekly, q => Assert.True(Early(q), q.Id)); // nothing City-or-later is offered yet
        long top = e.Content.PointChests.Where(c => c.Track == PointTrack.Weekly).Max(c => c.Points);
        Assert.Equal(top, weekly.Sum(q => q.Points));
    }

    [Fact]
    public void LateQuests_AreExtraPoints_NeverNeededForAChest()
    {
        var (e, s) = At(60);
        foreach (var kind in new[] { QuestKind.Daily, QuestKind.Weekly })
        {
            var all = Visible(e, s, kind);
            long early = all.Where(Early).Sum(q => q.Points);
            long top = e.Content.PointChests.Where(c => c.Track == (kind == QuestKind.Daily ? PointTrack.Daily : PointTrack.Weekly)).Max(c => c.Points);
            Assert.Equal(top, early);
            Assert.True(all.Sum(q => q.Points) > early, kind + ": later quests add points");
        }
    }

    [Fact]
    public void DailyQuests_AtLevel1_TheEarlySetPlusTheShopExtras()
    {
        var (e, s) = At(1);
        Assert.Equal(new[] { "d_ad", "d_battles", "d_login", "d_runs", "d_shop", "d_skills", "d_stamina" },Visible(e, s, QuestKind.Daily).Select(q => q.Id).ToArray());
        Assert.Equal(new[] { "w_bosses", "w_daily", "w_runs" }, Visible(e, s, QuestKind.Weekly).Select(q => q.Id).ToArray());
        Assert.Equal(new[] { "np_runs_3" }, Visible(e, s, QuestKind.NewPlayer).Select(q => q.Id).ToArray()); // day 1 of the event, no worm level yet
    }

    /// <summary>Counters that only move once a feature exists: the quest reading one must be gated by that feature (or a later one).</summary>
    private static readonly (string counter, string feature)[] CounterNeeds =
    {
        ("arena_fights", Features.Arena), ("arena_wins", Features.Arena),
        ("invasion_battles", Features.Invasions), ("invasion_top_contributor", Features.Invasions), ("pulls_blueprint", Features.Blueprints),
        ("city_building_upgrades", Features.BiomassFarm), ("breeds_collected", Features.Breeding), ("pulls_part", Features.PartPulls),
        ("worm_level_max", Features.WormLevel),
    };

    private static long Level(ContentDb c, string featureId)
    {
        if (featureId == null) return 0;
        long l = Features.UnlockLevel(c.Feature(featureId)!);
        return l > 0 ? l : 1000; // a building feature: after the City and its blueprint
    }

    [Fact]
    public void EveryQuest_ReadingAFeatureCounter_IsHiddenUntilThatFeature()
    {
        var c = ContentDb.Default;
        foreach (var q in c.Quests.Values)
        {
            foreach (var (counter, feature) in CounterNeeds.Where(n => n.counter == q.CounterKey))
                Assert.True(q.Feature != null && Level(c, q.Feature) >= Level(c, feature), $"{q.Id} counts {counter} but is gated by {q.Feature ?? "nothing"}, not {feature}");
        }
        // The one that could not be completed when offered at level 8. The shop and ad quests need no feature: the Shop's offers are
        // there from the start (owner, 2026-11-27).
        Assert.Equal(Features.BiomassFarm, c.Quest("w_buildings")!.Feature);
        Assert.Null(c.Quest("d_shop")!.Feature);
        Assert.Null(c.Quest("d_ad")!.Feature);
        Assert.True(c.Quest("d_ad")!.NeedsAds);
        Assert.Null(c.Quest("np_buildings_5"));
    }

    [Fact]
    public void EveryQuest_IsHiddenAndUnclaimable_BeforeItsFeature()
    {
        foreach (var level in new[] { 1, 2, 5, 11, 14 })
        {
            var (e, s) = At(level);
            foreach (var q in e.Content.Quests.Values.Where(q => q.Feature != null && !Features.IsUnlocked(s, e.Content, q.Feature)))
            {
                foreach (var kind in new[] { QuestKind.Daily, QuestKind.Weekly, QuestKind.Achievement, QuestKind.NewPlayer })
                    Assert.DoesNotContain(Visible(e, s, kind), v => v.Id == q.Id);
                s.Counters[q.CounterKey] = q.Target; // even a met target cannot be claimed while hidden
                Assert.False(QuestSystem.CanClaim(s, e.Content, q, TestHelpers.T0 + 3 * D), q.Id);
            }
        }
    }

    [Fact]
    public void ShopQuests_AreThereFromTheStart_AndTheAdQuestOnlyWhileAdsAreOn()
    {
        var (e, s) = At(1); // the Shop's offers need no unlock (owner, 2026-11-27)
        var ids = Visible(e, s, QuestKind.Daily).Select(q => q.Id).ToList();
        Assert.Contains("d_shop", ids);
        Assert.Contains("d_ad", ids);
        (e, s) = At(1, adsDisabled: true);
        ids = Visible(e, s, QuestKind.Daily).Select(q => q.Id).ToList();
        Assert.Contains("d_shop", ids);
        Assert.DoesNotContain("d_ad", ids);
        s.Counters["ads_watched"] = 1;
        Assert.False(QuestSystem.CanClaim(s, e.Content, e.Content.Quest("d_ad")!, TestHelpers.T0));
    }

    [Fact]
    public void BuildingQuests_AppearWithTheCity()
    {
        var (e, s) = At(14);
        Assert.DoesNotContain(Visible(e, s, QuestKind.Weekly), q => q.Id == "w_buildings" || q.Id == "w_blueprints");
        (e, s) = At(15);
        Assert.Contains(Visible(e, s, QuestKind.Weekly), q => q.Id == "w_buildings");
        Assert.Contains(Visible(e, s, QuestKind.Weekly), q => q.Id == "w_blueprints");
    }

    [Fact]
    public void PartPullQuest_IsFree_TheDailyOneIsGone_AndTheNewPlayerOneUsesTheFreePull()
    {
        var c = ContentDb.Default;
        Assert.Null(c.Quest("d_pull")); // a Part pull costs tokens or Diamonds: replaced by picking run skills
        Assert.Equal("run_skills_drafted", c.Quest("d_skills")!.CounterKey);
        // np_pulls_10 (day 3): the Parts unlock's free 10× pull is exactly 10 pulls, no token needed
        var e = Engine(); var s = Player(e);
        s.Counters[Requirements.MainLevelCounter] = 1;
        Ok(e, ref s, new LobbyPopupShown { PopupId = "tick" }, TestHelpers.T0);
        Ok(e, ref s, new FeatureCelebrate { FeatureId = Features.Parts }, TestHelpers.T0);
        Assert.Equal(1, s.Gacha.FreePulls(GachaType.Part));
        long tokens = Bal(s, Currencies.PartTokens);
        Ok(e, ref s, new GachaPull { PullType = GachaType.Part, Count = 10, Payment = GachaPayment.Free }, TestHelpers.T0);
        Assert.Equal(tokens, Bal(s, Currencies.PartTokens));
        Assert.True(QuestSystem.IsComplete(s, c.Quest("np_pulls_10")!));
    }

    // ---- rewards: points only, the chests pay ------------------------------------------------------------------------

    [Fact]
    public void DailyAndWeeklyQuests_PayNoRewardOfTheirOwn_OnlyPoints()
    {
        var c = ContentDb.Default;
        foreach (var q in c.Quests.Values.Where(q => q.Kind == QuestKind.Daily || q.Kind == QuestKind.Weekly))
        {
            Assert.Empty(q.Rewards);
            Assert.True(q.Points > 0 && q.Points % 5 == 0, q.Id);
        }
        // claiming one only moves the bar
        var (e, s) = At(1);
        long dia = Bal(s, Currencies.Diamonds), stamina = Bal(s, Currencies.Stamina);
        s.Count("runs_completed", 3);
        Ok(e, ref s, new QuestClaim { QuestId = "d_runs" }, TestHelpers.T0);
        Assert.Equal(dia, Bal(s, Currencies.Diamonds));
        Assert.Equal(stamina, Bal(s, Currencies.Stamina));
        Assert.Equal(25, s.Quests.DailyPoints);
    }

    private static long Total(IEnumerable<PointChestDef> chests, string currency) =>
        chests.SelectMany(c => c.Rewards).Where(r => r.Id == currency).Sum(r => r.Amount);

    [Fact]
    public void Chests_FiveSteps_RoundNumbers_AndTheAgreedContents()
    {
        var c = ContentDb.Default;
        var daily = c.PointChests.Where(x => x.Track == PointTrack.Daily).ToList();
        var weekly = c.PointChests.Where(x => x.Track == PointTrack.Weekly).ToList();
        Assert.Equal(new long[] { 20, 40, 60, 80, 100 }, daily.Select(x => x.Points).ToArray());
        Assert.Equal(new long[] { 100, 200, 300, 400, 500 }, weekly.Select(x => x.Points).ToArray());
        foreach (var r in c.PointChests.SelectMany(x => x.Rewards)) Assert.True(r.Amount % 5 == 0 || r.Amount == 1, r.Id + " " + r.Amount);
        Assert.Equal(100, Total(daily, Currencies.Diamonds));
        Assert.Equal(500, Total(weekly, Currencies.Diamonds));
        foreach (var cur in new[] { Currencies.Stamina, Currencies.Biomass, Currencies.Ore, Currencies.GearCores })
            Assert.True(Total(daily, cur) > 0, "daily " + cur);
        Assert.True(Total(weekly, Currencies.Ore) > Total(daily, Currencies.Ore));
        Assert.True(Total(weekly, Currencies.Biomass) > Total(daily, Currencies.Biomass));
        Assert.True(Total(weekly, Currencies.GearCores) > 0);
        Assert.Equal(1, Total(weekly, ProgressionCurrencies.PartAwakenCores));
        Assert.Contains(weekly[4].Rewards, r => r.Id == ProgressionCurrencies.PartAwakenCores); // the single crystal is in the top chest
        Assert.Equal(new[] { 0, 0, 0, 0, 3 }, daily.Select(x => x.RandomTokens).ToArray());
        Assert.Equal(new[] { 0, 0, 0, 5, 0 }, weekly.Select(x => x.TokensEachType).ToArray());
        // the first daily chest pays only what a level 1 player can use at once (Gear Cores come later)
        Assert.DoesNotContain(daily[0].Rewards, r => r.Id == Currencies.GearCores);
        Assert.All(daily[0].Rewards, r => Assert.True(UseLevel(c, r) <= 1, r.Id));
    }

    private static long UseLevel(ContentDb c, Reward r)
    {
        var use = c.RewardUse(r);
        if (use == null || use.UsedWhen.Items.Count == 0) return 0;
        return use.UsedWhen.Items.Min(i => i.Kind == RequirementKind.Feature ? Level(c, i.Key) : 1000);
    }

    private static List<string> Tokens(GameEngine e, ref GameState s, PointTrack track, long points)
    {
        s.Quests.ChestsClaimed.Clear();
        if (track == PointTrack.Daily) s.Quests.DailyPoints = 100; else s.Quests.WeeklyPoints = 500;
        Ok(e, ref s, new QuestClaimChest { Track = track, Points = points }, TestHelpers.T0);
        return s.Quests.LastChestTokens.ToList();
    }

    [Theory]
    [InlineData(0, new[] { "part_tokens" })] // before anything is unlocked: Part tokens only
    [InlineData(1, new[] { "part_tokens" })]
    [InlineData(6, new[] { "part_tokens", "alien_tokens" })]
    [InlineData(15, new[] { "part_tokens", "alien_tokens", "blueprint_tokens" })]
    public void DailyTopChest_ThreeRandomTokens_OnlyOfUnlockedSignals(int level, string[] allowed)
    {
        var (e, s) = At(level);
        long before = allowed.Sum(t => Bal(s, t) + RewardGate.Stashed(s, t));
        var picked = Tokens(e, ref s, PointTrack.Daily, 100);
        Assert.Equal(3, picked.Count);
        Assert.All(picked, t => Assert.Contains(t, allowed));
        Assert.Equal(before + 3, allowed.Sum(t => Bal(s, t) + RewardGate.Stashed(s, t))); // granted (or stashed before the first Signal), none of a locked type
        foreach (var locked in new[] { "part_tokens", "alien_tokens", "blueprint_tokens" }.Except(allowed))
            Assert.Equal(0, Bal(s, locked));
    }

    [Fact]
    public void DailyTopChest_TokenDraw_IsDeterministic_AndMixesTypesWhenAllAreUnlocked()
    {
        var results = new HashSet<string>();
        for (int run = 0; run < 2; run++)
        {
            var (e, s) = At(15);
            results.Add(string.Join(",", Tokens(e, ref s, PointTrack.Daily, 100)));
        }
        Assert.Single(results); // same player, same day: the same three
        var kinds = new HashSet<string>();
        for (int seed = 1; seed <= 40; seed++)
        {
            var e = Engine(); var s = e.NewPlayer("p" + seed, TestHelpers.T0, seed);
            s.Counters[Requirements.MainLevelCounter] = 15;
            Ok(e, ref s, new LobbyPopupShown { PopupId = "tick" }, TestHelpers.T0);
            foreach (var t in Tokens(e, ref s, PointTrack.Daily, 100)) kinds.Add(t);
        }
        Assert.Equal(3, kinds.Count);
    }

    [Fact]
    public void WeeklyChest_FiveTokensOfEachUnlockedType_AndTheAwakeningCoreInTheTop()
    {
        foreach (var (level, types) in new[] { (1, 1), (6, 2), (15, 3) })
        {
            var (e, s) = At(level);
            long p = Bal(s, Currencies.PartTokens), a = Bal(s, Currencies.AlienTokens), b = Bal(s, Currencies.BlueprintTokens);
            s.Quests.WeeklyPoints = 500;
            Ok(e, ref s, new QuestClaimChest { Track = PointTrack.Weekly, Points = 400 }, TestHelpers.T0);
            Assert.Equal(p + 5, Bal(s, Currencies.PartTokens));
            Assert.Equal(a + (types >= 2 ? 5 : 0), Bal(s, Currencies.AlienTokens));
            Assert.Equal(b + (types >= 3 ? 5 : 0), Bal(s, Currencies.BlueprintTokens));
            Assert.Empty(s.Quests.LastChestTokens);
            Ok(e, ref s, new QuestClaimChest { Track = PointTrack.Weekly, Points = 500 }, TestHelpers.T0);
            Assert.Equal(0, Bal(s, ProgressionCurrencies.PartAwakenCores)); // no Mythic part yet: waits in the stash
            Assert.Equal(1, RewardGate.Stashed(s, ProgressionCurrencies.PartAwakenCores));
        }
    }

    [Fact]
    public void QuestAndChestRewards_AreUsableSoon_NotHeldBackForLong()
    {
        var c = ContentDb.Default;
        const long Soon = 15; // the City: the longest the early game holds a reward (about a week for Casual F2P, capped stashes)
        // Gear Cores and the awakening core wait for a part that needs them (stash, released later): fine in the later chests.
        bool Held(Reward r) => r.Id != Currencies.GearCores && r.Id != ProgressionCurrencies.PartAwakenCores && UseLevel(c, r) > Soon;
        foreach (var chest in c.PointChests)
            foreach (var r in chest.Rewards)
                Assert.False(Held(r), $"{chest.Track} chest {chest.Points} pays {r.Id}: no use before level {UseLevel(c, r)}");
        foreach (var q in c.Quests.Values.Where(q => q.Kind == QuestKind.NewPlayer || q.Kind == QuestKind.Achievement))
            foreach (var r in q.Rewards)
                Assert.False(Held(r) && Level(c, q.Feature) < UseLevel(c, r), $"{q.Id} pays {r.Id}: no use before level {UseLevel(c, r)}");
    }

    // ---- achievements: linear, repeating milestones -------------------------------------------------------------------

    [Fact]
    public void Achievements_RepeatEveryN_WithTheSameReward_ForEver()
    {
        var c = ContentDb.Default;
        var ach = c.Quests.Values.Where(q => q.Kind == QuestKind.Achievement).ToList();
        Assert.All(ach, q => { Assert.True(q.Every > 0 && q.Every == q.Target, q.Id); Assert.Single(q.Rewards); });
        Assert.Equal(100, c.Quest("a_enemies")!.Every); Assert.Equal("run_enemies_defeated", c.Quest("a_enemies")!.CounterKey);
        Assert.Equal(10, c.Quest("a_bosses")!.Every); Assert.Equal("run_bosses_killed", c.Quest("a_bosses")!.CounterKey);
        Assert.Equal(10, c.Quest("a_minigames")!.Every); Assert.Equal("run_minigames_played", c.Quest("a_minigames")!.CounterKey);
        foreach (var (id, cur) in new[] { ("a_pulls_part", Currencies.PartTokens), ("a_pulls_alien", Currencies.AlienTokens), ("a_pulls_blueprint", Currencies.BlueprintTokens) })
        {
            var q = c.Quest(id)!;
            Assert.Equal(100, q.Every);
            Assert.Equal(10, q.Rewards[0].Amount);
            Assert.Equal(cur, q.Rewards[0].Id);
        }
        var (e, s) = At(1);
        var bosses = c.Quest("a_bosses")!;
        s.Counters["run_bosses_killed"] = 9;
        Assert.False(QuestSystem.CanClaim(s, e.Content, bosses, TestHelpers.T0));
        Assert.Equal(10, QuestSystem.NextMilestone(s, bosses));
        s.Counters["run_bosses_killed"] = 10;
        long dia = Bal(s, Currencies.Diamonds);
        Ok(e, ref s, new QuestClaim { QuestId = "a_bosses" }, TestHelpers.T0);
        Assert.Equal(dia + 20, Bal(s, Currencies.Diamonds));
        Fail(e, ref s, new QuestClaim { QuestId = "a_bosses" }, TestHelpers.T0);
        Assert.Equal(20, QuestSystem.NextMilestone(s, bosses));
        Assert.Equal(0, QuestSystem.Progress(s, bosses));
        // the next milestone is the same again, and skipped ones pay together
        s.Counters["run_bosses_killed"] = 40;
        Assert.Equal(3, QuestSystem.Pending(s, bosses));
        dia = Bal(s, Currencies.Diamonds);
        Ok(e, ref s, new QuestClaimAll(), TestHelpers.T0);
        Assert.Equal(dia + 60, Bal(s, Currencies.Diamonds));
        Assert.Equal(50, QuestSystem.NextMilestone(s, bosses));
        Assert.Equal(4, s.Counter("achievements_claimed"));
    }

    [Fact]
    public void PullAchievement_PaysTenFreeTickets_PerHundredPulls_OfThatType()
    {
        var (e, s) = At(15);
        long a = Bal(s, Currencies.AlienTokens);
        s.Counters["pulls_alien"] = 200;
        Ok(e, ref s, new QuestClaim { QuestId = "a_pulls_alien" }, TestHelpers.T0);
        Assert.Equal(a + 20, Bal(s, Currencies.AlienTokens));
        // hidden until the Signal exists
        var (e2, s2) = At(1);
        Assert.DoesNotContain(Visible(e2, s2, QuestKind.Achievement), q => q.Id == "a_pulls_alien" || q.Id == "a_pulls_blueprint");
        Assert.Contains(Visible(e2, s2, QuestKind.Achievement), q => q.Id == "a_pulls_part");
    }

    [Fact]
    public void NewPlayerEvent_EveryDayHasOneQuest_AllReachableByTheirFeatures()
    {
        var c = ContentDb.Default;
        var np = c.Quests.Values.Where(q => q.Kind == QuestKind.NewPlayer).OrderBy(q => q.Day).ToList();
        Assert.Equal(Enumerable.Range(1, c.Retention.NewPlayerEventDays), np.Select(q => q.Day));
        foreach (var q in np) Assert.True(q.Target > 0 && q.Rewards.Count > 0, q.Id);
    }
}
