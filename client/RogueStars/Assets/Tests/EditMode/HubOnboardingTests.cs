using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Onboarding v2 hub (design canvas HubDay1 / Main / Unlock): the day-1 hub shows only PLAY, the Planet and Shop tabs, the teaser,
    /// the stamina and profile chips; tabs and side icons appear as features unlock; every unlocked screen stays reachable;
    /// the full-screen unlock moment claims the gift and opens the feature.
    /// </summary>
    public sealed class HubOnboardingTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;
        private const long Day = GameClock.Day;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private HubScreen Hub
        {
            get
            {
                if (!(_f.App.Router.Current is HubScreen)) _f.App.Router.Reset(new HubScreen());
                var hub = (HubScreen)_f.App.Router.Current;
                hub.Rebuild();
                return hub;
            }
        }

        private static List<string> ButtonNames(Component root) =>
            root.GetComponentsInChildren<Button>(true).Select(b => b.name).ToList();

        private static Text Named(Component root, string name) =>
            root.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == name);

        private List<string> Tabs() => ButtonNames(Hub.Root).Where(n => n.StartsWith("Tab:")).ToList();

        private void Unlock(params string[] ids)
        {
            foreach (var id in ids) S.Onboarding.Unlocked[id] = _f.Service.NowMs;
        }

        private void Celebrated(params string[] ids)
        {
            foreach (var id in ids) S.Onboarding.Celebrated.Add(id);
        }

        /// <summary>Plays level 1 for real (manual roll, then auto steps) until it is cleared.</summary>
        private void ClearLevel1()
        {
            var level = C.MainLevel(1);
            for (int attempt = 0; attempt < 3 && S.Counter(Requirements.MainLevelCounter) < 1; attempt++)
            {
                Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
                for (int i = 0; i < 500 && S.Run.Active != null; i++) _f.Service.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = true });
            }
            Assert.That(S.Counter(Requirements.MainLevelCounter), Is.EqualTo(1), "level 1 cleared");
        }

        [Test]
        public void Day1_NewAccount_HubShowsOnlyPlayPlanetShopTeaserStaminaAndProfile()
        {
            var hub = Hub;
            var buttons = ButtonNames(hub.Root);
            Assert.That(buttons, Does.Contain("PlayButton"));
            Assert.That(buttons, Does.Contain("Tab:planet"));
            Assert.That(buttons, Does.Contain("Tab:shop"), "the Shop's offers need no unlock (owner, 2026-11-27)");
            Assert.That(S.Mail.Inbox, Is.Not.Empty, "the welcome mail waits until after the first run");
            // besides: the timed Offer icon while a timed offer is on sale (it opens the Shop's Offers)
            var others = buttons.Where(n => n != "PlayButton" && n != "Tab:planet" && n != "Tab:shop" && n != "Side:offer" && n != "TapTarget").ToList();
            Assert.That(others, Is.Empty, "nothing else on the day-1 hub, not even greyed out: " + string.Join(", ", others));
            Assert.That(Named(hub.Root, "TeaserText").text, Is.EqualTo("1 more level to the next feature"));
            Assert.That(Named(hub.Root, "Figure"), Is.Null);

            // Rewards follow use: a brand-new account holds stamina and Diamonds (stamina refills) only, and shows just those.
            var chips = _f.Canvas.GetComponentsInChildren<Text>(true).Where(t => t.name.StartsWith("Chip:")).Select(t => t.name).ToList();
            Assert.That(chips, Is.EqualTo(new[] { "Chip:" + Currencies.Stamina, "Chip:" + Currencies.Diamonds }));
            var bar = _f.App.Bar;
            Assert.That(_f.Canvas.GetComponentsInChildren<Button>(true).Any(b => b.name == "ProfileChip"), Is.True);
            Assert.That(bar, Is.Not.Null);
            Assert.That(HubLayout.Reachable(S, C, _f.Service.NowMs), Is.EquivalentTo(new[] { "portals", "settings", "shop", "signals", "passes" }));

            // the Shop opens on Offers: no pull section before Parts
            hub.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "Tab:shop").onClick.Invoke();
            var shop = (ShopScreen)_f.App.Router.Current;
            Assert.That(shop.Section, Is.EqualTo("Offers"));
            Assert.That(ButtonNames(shop.Root).Where(n => n.StartsWith("Tab:")).ToList(),
                Is.EqualTo(new[] { "Tab:Offers", "Tab:Diamonds", "Tab:Passes", "Tab:Items", "Tab:Ads" }));
            Assert.That(C.Feature("shop"), Is.Null, "no unlock record, intro or teaser entry for the offers");
        }

        [Test]
        public void Level1Cleared_UnlockMoment_ClaimsTheGift_OpensTheWorm_ThenTheWormTabShows()
        {
            ClearLevel1();
            var hub = Hub;
            Assert.That(HubLayout.PendingGroup(S, C), Is.EqualTo(new[] { Features.Parts }));
            Assert.That(hub.ShowPendingUnlock(), Is.True);
            var moment = _f.App.Router.Current as UnlockScreen;
            Assert.That(moment, Is.Not.Null, "full-screen unlock moment");
            Assert.That(Named(moment.Root, "FeatureName").text, Is.EqualTo("Parts"));
            // the intro panels first (GDD §16): one line, art and an animation each, NEXT to go on
            Assert.That(moment.PageCount, Is.EqualTo(C.Feature(Features.Parts).Intro.Count));
            Assert.That(Named(moment.Root, "StepText").text, Is.EqualTo(C.Feature(Features.Parts).Intro[0].Text));
            for (int i = 1; i < moment.PageCount; i++)
            {
                moment.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "IntroNext").onClick.Invoke();
                Assert.That(Named(moment.Root, "StepText").text, Is.EqualTo(C.Feature(Features.Parts).Intro[i].Text));
            }
            Assert.That(moment.OnLastPage, Is.True);
            Assert.That(Named(moment.Root, "Description").text, Is.EqualTo(C.Feature(Features.Parts).Description));
            Assert.That(moment.Root.GetComponentsInChildren<Text>(true).Any(t => t.text == "+20 Diamonds"), Is.True, "reward shown");
            var actions = moment.Root.GetComponentsInChildren<Button>(true);
            Assert.That(actions.Length, Is.EqualTo(1), "one guided action");
            Assert.That(actions[0].GetComponentInChildren<Text>().text, Is.EqualTo("EQUIP YOUR PARTS"));

            long dia = S.Wallet.Get(Currencies.Diamonds);
            actions[0].onClick.Invoke();
            Assert.That(S.Onboarding.Celebrated, Does.Contain(Features.Parts));
            Assert.That(S.Wallet.Get(Currencies.Diamonds), Is.EqualTo(dia + 20));
            Assert.That(_f.App.Router.Current, Is.InstanceOf<WormScreen>(), "the action opens the feature");
            Assert.That(_f.App.Router.Pop(), Is.True);
            Assert.That(_f.App.Router.Current, Is.InstanceOf<HubScreen>(), "back returns to the hub, not to the moment");

            Assert.That(Tabs(), Is.EqualTo(new[] { "Tab:worm", "Tab:planet", "Tab:shop" }));
            Assert.That(Named(Hub.Root, "BadgeText").text, Is.EqualTo("NEW"));
            Assert.That(Named(Hub.Root, "GoalText").text, Does.Contain("Equip your starter parts"));
            Assert.That(Hub.ShowPendingUnlock(), Is.False, "nothing pending any more");
        }

        [Test]
        public void DailyQuests_AreNoFeature_NoUnlockMoment_ButTheButtonShowsOnceLevel1IsCleared()
        {
            Assert.That(C.Feature("quests"), Is.Null, "no feature record, intro, gift or teaser entry");
            Assert.That(ButtonNames(Hub.Root), Does.Not.Contain("Side:quests"), "day 1: nothing but the play button before level 1 is cleared");
            ClearLevel1();
            Assert.That(HubLayout.PendingGroup(S, C), Is.EqualTo(new[] { Features.Parts }), "the only moment is Parts");
            Celebrated(Features.Parts);
            var hub = Hub;
            Assert.That(ButtonNames(hub.Root), Does.Contain("Side:quests"));
            var side = hub.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "Side:quests");
            Assert.That(side.GetComponentsInChildren<Text>(true).Any(t => t.text == "Daily Quests"), Is.True, "the hub button label");
            side.onClick.Invoke();
            Assert.That(_f.App.Router.Current, Is.InstanceOf<QuestsScreen>());
            Assert.That(_f.App.Router.Current.Title, Is.EqualTo("Daily Quests"), "the screen title");
            Assert.That(_f.App.Router.Current.IntroFeatures, Is.Null, "no \"?\" intro");
        }

        [Test]
        public void TabsAndSideIcons_AppearAsFeaturesUnlock()
        {
            Assert.That(Tabs(), Is.EqualTo(new[] { "Tab:planet", "Tab:shop" }), "the Shop from the start: its offers need no unlock");
            Unlock(Features.Parts);
            Assert.That(Tabs(), Is.EqualTo(new[] { "Tab:worm", "Tab:planet", "Tab:shop" }));
            Assert.That(ButtonNames(Hub.Root), Does.Contain("Side:quests"));
            Unlock(Features.BiomassFarm);
            Assert.That(Tabs(), Is.EqualTo(new[] { "Tab:worm", "Tab:city", "Tab:planet", "Tab:shop" }));
            Unlock(Features.WormLevel, Features.PartPulls);
            Assert.That(Tabs(), Is.EqualTo(new[] { "Tab:worm", "Tab:city", "Tab:planet", "Tab:shop" }));
            Assert.That(ButtonNames(Hub.Root), Does.Not.Contain("Side:shop"), "one Shop: the bottom tab, no second entry in the right column");
            Assert.That(ButtonNames(Hub.Root), Does.Not.Contain("Side:more"), "nothing else unlocked yet");
            Unlock(Features.Laboratory);
            Assert.That(ButtonNames(Hub.Root), Does.Contain("Side:more"));
            Unlock(Features.Arena, Features.SeasonPass);
            Assert.That(Tabs(), Is.EqualTo(new[] { "Tab:worm", "Tab:city", "Tab:planet", "Tab:shop", "Tab:arena" }));
            Assert.That(ButtonNames(Hub.Root), Does.Contain("Side:pass"));
            Assert.That(ButtonNames(Hub.Root).Any(n => n.StartsWith("Side:observatory") || n.StartsWith("Live:")), Is.False, "live events wait for Invasions");
            Unlock(Features.Invasions);
            Assert.That(ButtonNames(Hub.Root).Any(n => n.StartsWith("Side:observatory") || n.StartsWith("Live:")), Is.True);

            // Currency chips follow the same unlocks.
            var visible = HubLayout.VisibleCurrencies(S, C);
            Assert.That(visible, Does.Contain(Currencies.Biomass).And.Contain(Currencies.Diamonds).And.Contain(Currencies.Data).And.Contain(Currencies.ArenaTickets)
                .And.Contain(Currencies.Ore).And.Contain(Currencies.PartTokens).And.Contain(Currencies.InvasionAttempts));
            Assert.That(visible, Does.Not.Contain(Currencies.Grain).And.Not.Contain(Currencies.Fuel).And.Not.Contain(Currencies.HeroXp)
                .And.Not.Contain(Currencies.AlienTokens).And.Not.Contain(Currencies.GuildMarks));
        }

        [Test]
        public void OneShop_TheTabOpensPullSectionsFirst_EachWithItsFeature_TheOffersAlways()
        {
            Unlock(Features.Parts);
            var tab = HubLayout.Tabs(S, C).Single(t => t.Id == HubLayout.ShopTab);
            Assert.That(tab.Label, Is.EqualTo("Shop"));
            Assert.That(tab.Icon, Is.EqualTo("shop"), "the tab shows the shop stall");
            Assert.That(HubLayout.Tabs(S, C).Any(t => t.Label == "Signals"), Is.False);
            Hub.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "Tab:shop").onClick.Invoke();
            var shop = (ShopScreen)_f.App.Router.Current;
            Assert.That(shop.Title, Is.EqualTo("Shop"));
            Assert.That(shop.Section, Is.EqualTo("Parts"));
            var chips = ButtonNames(shop.Root).Where(n => n.StartsWith("Tab:")).ToList();
            Assert.That(chips, Is.EqualTo(new[] { "Tab:Parts", "Tab:Offers", "Tab:Diamonds", "Tab:Passes", "Tab:Items", "Tab:Ads" }),
                "only the unlocked pull section, the offers always");
            var parts = shop.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "Tab:Parts");
            Assert.That(parts.GetComponentsInChildren<RectTransform>(true).Any(t => t.name == "NotifyDot"), Is.EqualTo(S.Gacha.FreePulls(GachaType.Part) > 0), "a dot while a free 10× waits");

            Unlock(Features.Aliens);
            _f.App.Router.Reset(new HubScreen());
            _f.App.Open("signals"); // the former pull screen's id: an alias of the Shop
            shop = (ShopScreen)_f.App.Router.Current;
            Assert.That(shop.Section, Is.EqualTo("Aliens"), "the first pull section");
            chips = ButtonNames(shop.Root).Where(n => n.StartsWith("Tab:")).ToList();
            Assert.That(chips, Is.EqualTo(new[] { "Tab:Aliens", "Tab:Parts", "Tab:Offers", "Tab:Diamonds", "Tab:Passes", "Tab:Items", "Tab:Ads" }));
            shop.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "Tab:Diamonds").onClick.Invoke();
            Assert.That(shop.Section, Is.EqualTo("Diamonds"));
            Assert.That(shop.Root.GetComponentsInChildren<RectTransform>(true).Any(t => t.name == "Section:DiamondPack"), Is.True);

            // no second shop on the hub: the right column keeps only the timed Offer, which opens the Shop's Offers
            Assert.That(HubLayout.RightColumn(S, C, _f.Service.NowMs).Any(e => e.Id == "shop"), Is.False);
            Assert.That(ButtonNames(Hub.Root), Does.Not.Contain("Side:shop"));
            if (ButtonNames(Hub.Root).Contains("Side:offer"))
            {
                Hub.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "Side:offer").onClick.Invoke();
                Assert.That(((ShopScreen)_f.App.Router.Current).Section, Is.EqualTo("Offers"));
            }
        }

        [Test]
        public void EveryUnlockedScreen_IsReachableFromTheHub()
        {
            Unlock(Features.Parts, Features.BiomassFarm, Features.Laboratory, Features.Aliens);
            var reachable = HubLayout.Reachable(S, C, _f.Service.NowMs);
            foreach (var e in ScreenCatalog.All)
                if ((e.FeatureId == null || Features.IsUnlocked(S, C, e.FeatureId)) && (e.Visible == null || e.Visible(S)))
                    Assert.That(reachable, Does.Contain(e.Id), e.Id + " unlocked but not reachable");
            Assert.That(reachable, Does.Not.Contain("arena"));

            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            S.RunQuests.Encounters = 1; // the journal shows up once a Run Quest character was met
            reachable = HubLayout.Reachable(S, C, _f.Service.NowMs);
            foreach (var e in ScreenCatalog.All) Assert.That(reachable, Does.Contain(e.Id), e.Id + " not reachable with everything unlocked");
            var hub = Hub;
            hub.ShowMore();
            var more = _f.App.ModalLayer.GetComponentsInChildren<Button>(true).Select(b => b.name).ToList();
            foreach (var m in HubLayout.MoreScreens(S, C)) Assert.That(more, Does.Contain("More:" + m.Id));
            _f.App.ModalLayer.GetComponentsInChildren<Button>(true).First(b => b.name == "More:laboratory").onClick.Invoke();
            Assert.That(_f.App.Router.Current, Is.InstanceOf<LaboratoryScreen>());
        }

        [Test]
        public void Teaser_ShowsTheLevelToClear_NeverADay()
        {
            Unlock(Features.Parts, Features.RunShop, Features.WormLevel, Features.RunMiniboss, Features.RunTwoDice);
            S.Counters[Requirements.MainLevelCounter] = 4;
            Assert.That(Named(Hub.Root, "TeaserText").text, Is.EqualTo("2 more levels to the next feature"));
            Assert.That(Named(Hub.Root, "Figure"), Is.Null);
            S.Counters[Requirements.MainLevelCounter] = 6; // met: the next command records it
            Assert.That(Named(Hub.Root, "TeaserText").text, Is.EqualTo("A new feature is unlocking"));
            Assert.That(Named(Hub.Root, "Figure"), Is.Null);
        }

        [Test]
        public void LevelCleared_UnlockMoment_SameDay_OpensTheCity()
        {
            ClearLevel1();
            Celebrated(Features.Parts);
            S.Counters[Requirements.MainLevelCounter] = 15; // no calendar gate: everything up to level 15 (the City) at once
            _f.Service.Refresh(_ => { });
            Assert.That(S.Onboarding.Unlocked.ContainsKey(Features.BiomassFarm), Is.True);
            Celebrated(Features.RunShop, Features.WormLevel, Features.RunMiniboss, Features.RunTwoDice, Features.Aliens, Features.AlienSquad, Features.AutoRoll,
                Features.RunCampfire, Features.PartPulls, Features.RunSignatures, Features.AlienPulls,
                Features.RunMinigames);
            Assert.That(Hub.ShowPendingUnlock(), Is.True);
            var moment = (UnlockScreen)_f.App.Router.Current;
            Assert.That(moment.FeatureIds, Is.EqualTo(new[] { Features.BiomassFarm }));
            Assert.That(Named(moment.Root, "Kicker").text, Is.EqualTo("NEW UNLOCK"));
            Assert.That(moment.Action, Is.Null, "the guided action waits on the last panel");
            moment.ShowPage(moment.PageCount - 1);
            Assert.That(moment.Action.GetComponentInChildren<Text>().text, Is.EqualTo("BUILD THE FARM"));
            Assert.That(Named(moment.Root, "TeaserText").text, Is.EqualTo("3 more levels to the next feature"));
            moment.Claim();
            Assert.That(_f.App.Router.Current, Is.InstanceOf<CityScreen>());
            Assert.That(S.Onboarding.Celebrated, Does.Contain(Features.BiomassFarm));
        }

        [Test]
        public void RunContentUnlock_IntroPanels_NextAndSkip_ThenPlay()
        {
            Unlock(Features.Parts);
            Celebrated(Features.Parts);
            Unlock(Features.RunShop);
            Assert.That(Hub.ShowPendingUnlock(), Is.True);
            var moment = (UnlockScreen)_f.App.Router.Current;
            var intro = C.Feature(Features.RunShop).Intro;
            Assert.That(Named(moment.Root, "Kicker").text, Is.EqualTo("NEW IN YOUR RUNS"));
            Assert.That(Named(moment.Root, "FeatureName").text, Is.EqualTo("Shops"));
            Assert.That(moment.PageCount, Is.EqualTo(intro.Count));
            Assert.That(moment.Root.GetComponentsInChildren<Image>(true).Count(i => i.name.StartsWith("Dot:")), Is.EqualTo(intro.Count));
            Assert.That(moment.Root.GetComponentsInChildren<RectTransform>(true).Any(t => t.name == "IntroArt"), Is.True, "an art slot");
            Assert.That(moment.Root.GetComponentInChildren<RogueStars.Client.World.IntroAnimator>(true).Kind, Is.EqualTo(intro[0].Anim));
            moment.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "IntroNext").onClick.Invoke();
            Assert.That(Named(moment.Root, "StepText").text, Is.EqualTo(intro[1].Text));
            moment.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "IntroNext").onClick.Invoke();
            moment.ShowPage(moment.PageCount - 1);
            Assert.That(moment.OnLastPage, Is.True);
            Assert.That(moment.Action.GetComponentInChildren<Text>().text, Is.EqualTo("PLAY"));
            moment.Claim();
            Assert.That(S.Onboarding.Celebrated, Does.Contain(Features.RunShop));
            Assert.That(_f.App.Router.Current, Is.InstanceOf<PortalsScreen>());
        }

        [Test]
        public void BackingOutOfTheMoment_DismissesItForGood_CountsAsSeen()
        {
            // Q7: Android back on the unlock moment = seen; it never reopens (no snooze, no "NEW: … tap to open" card)
            Unlock(Features.Parts);
            Assert.That(Hub.ShowPendingUnlock(), Is.True);
            long dia = S.Wallet.Get(Currencies.Diamonds);
            Assert.That(_f.App.HandleBack(), Is.True);
            var hub = Hub;
            Assert.That(_f.App.Router.Current, Is.SameAs(hub), "back returns to the hub");
            Assert.That(S.Onboarding.Celebrated, Does.Contain(Features.Parts), "backing out counts as seen");
            Assert.That(S.Wallet.Get(Currencies.Diamonds), Is.EqualTo(dia + 20), "the unlock gift is still granted");
            hub.Frame(0.1f);
            Assert.That(_f.App.Router.Current, Is.SameAs(hub), "not reopened");
            Assert.That(hub.ShowPendingUnlock(), Is.False, "nothing pending any more");
            Assert.That(hub.Root.GetComponentsInChildren<Transform>(true).Any(t => t.name == "NewUnlock"), Is.False, "no snooze card");
        }

        [Test]
        public void SkippingTheMoment_DismissesItForGood_ButTheHelpButtonStillReplaysIt()
        {
            // Q7: SKIP on any panel dismisses the moment permanently (no feature opened); "?" still replays the intro
            Unlock(Features.Parts);
            Assert.That(Hub.ShowPendingUnlock(), Is.True);
            var moment = (UnlockScreen)_f.App.Router.Current;
            Assert.That(moment.PageCount, Is.GreaterThan(1));
            moment.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "IntroSkip").onClick.Invoke();
            Assert.That(_f.App.Router.Current, Is.InstanceOf<HubScreen>(), "skip closes the moment, it does not open the feature");
            Assert.That(S.Onboarding.Celebrated, Does.Contain(Features.Parts));
            Hub.Frame(0.1f);
            Assert.That(_f.App.Router.Current, Is.InstanceOf<HubScreen>(), "not reopened");

            _f.App.Open("worm");
            var worm = _f.App.Router.Current;
            worm.ShowIntro();
            var replay = _f.App.Router.Current as UnlockScreen;
            if (replay == null) // a screen of several features lists its topics first
            {
                _f.App.ModalLayer.GetComponentsInChildren<Button>(true).First(b => b.name == "Intro:" + Features.Parts).onClick.Invoke();
                replay = _f.App.Router.Current as UnlockScreen;
            }
            Assert.That(replay, Is.Not.Null);
            Assert.That(replay.IsReplay, Is.True);
            Assert.That(replay.FeatureIds, Is.EqualTo(new[] { Features.Parts }));
            Assert.That(_f.App.HandleBack(), Is.True);
            Assert.That(_f.App.Router.Current, Is.SameAs(worm), "back closes the replay");
        }

        [Test]
        public void ContinueRun_ReplacesPlay_WhileARunIsActive()
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            var names = ButtonNames(Hub.Root);
            Assert.That(names, Does.Contain("ContinueButton"));
            Assert.That(names, Does.Not.Contain("PlayButton"));
        }

        /// <summary>Lowest and highest world y of a rect.</summary>
        private static (float min, float max) WorldY(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return (Mathf.Min(c[0].y, c[1].y), Mathf.Max(c[0].y, c[1].y));
        }

        [Test]
        public void TabLabels_StayOnScreen_WhenTheFrameChangesSize_SafeAreaInset()
        {
            // the hub lays out in frame units at build time: a later size change (the canvas taking its size, a bottom
            // safe-area inset) rebuilds it on the next frame, so the bottom tabs and their labels end inside the frame
            Unlock(Features.Parts, Features.BiomassFarm, Features.Arena);
            _f.App.Router.Reset(new HubScreen());
            var hub = (HubScreen)_f.App.Router.Current;
            Assert.That(hub.FrameSizeChanged(), Is.False);
            float unit = WorldY(hub.BodyFrame).max - WorldY(hub.BodyFrame).min;
            Ui.Stretch(_f.App.Root, 0, 0, 0, 120); // a bottom inset (gesture bar)
            Assert.That(hub.FrameSizeChanged(), Is.True);
            hub.Frame(0.016f);
            Assert.That(hub.FrameSizeChanged(), Is.False, "rebuilt for the new frame");
            var frame = WorldY(hub.BodyFrame);
            Assert.That(frame.max - frame.min, Is.LessThan(unit));
            var labels = hub.Root.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Tab:"))
                .Select(b => b.GetComponentsInChildren<Text>(true).First(t => t.name == "Label")).ToList();
            Assert.That(labels.Count, Is.EqualTo(5));
            float eps = (frame.max - frame.min) * 0.001f;
            foreach (var l in labels)
                Assert.That(WorldY(l.rectTransform).min, Is.GreaterThanOrEqualTo(frame.min - eps), l.text + " label below the frame");
        }
    }
}
