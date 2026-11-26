using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Onboarding v2 hub (design canvas HubDay1 / Main / Unlock): the day-1 hub shows only PLAY, the Planet tab, the teaser,
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
        public void Day1_NewAccount_HubShowsOnlyPlayPlanetTeaserStaminaAndProfile()
        {
            var hub = Hub;
            var buttons = ButtonNames(hub.Root);
            Assert.That(buttons, Does.Contain("PlayButton"));
            Assert.That(buttons, Does.Contain("Tab:planet"));
            Assert.That(S.Mail.Inbox, Is.Not.Empty, "the welcome mail waits until after the first run");
            var others = buttons.Where(n => n != "PlayButton" && n != "Tab:planet" && n != "TapTarget").ToList();
            Assert.That(others, Is.Empty, "nothing else on the day-1 hub, not even greyed out: " + string.Join(", ", others));
            Assert.That(Named(hub.Root, "TeaserText").text, Is.EqualTo("1 more level to the next feature"));
            Assert.That(Named(hub.Root, "Figure"), Is.Null);

            // Rewards follow use: a brand-new account holds stamina and Diamonds (stamina refills) only, and shows just those.
            var chips = _f.Canvas.GetComponentsInChildren<Text>(true).Where(t => t.name.StartsWith("Chip:")).Select(t => t.name).ToList();
            Assert.That(chips, Is.EqualTo(new[] { "Chip:" + Currencies.Stamina, "Chip:" + Currencies.Diamonds }));
            var bar = _f.App.Bar;
            Assert.That(_f.Canvas.GetComponentsInChildren<Button>(true).Any(b => b.name == "ProfileChip"), Is.True);
            Assert.That(bar, Is.Not.Null);
            Assert.That(HubLayout.Reachable(S, C, _f.Service.NowMs), Is.EquivalentTo(new[] { "portals", "settings" }));
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

            Assert.That(Tabs(), Is.EqualTo(new[] { "Tab:worm", "Tab:planet", "Tab:signals" }));
            Assert.That(Named(Hub.Root, "BadgeText").text, Is.EqualTo("NEW"));
            Assert.That(Named(Hub.Root, "GoalText").text, Does.Contain("Equip your starter parts"));
            Assert.That(Hub.ShowPendingUnlock(), Is.False, "nothing pending any more");
        }

        [Test]
        public void TabsAndSideIcons_AppearAsFeaturesUnlock()
        {
            Assert.That(Tabs(), Is.EqualTo(new[] { "Tab:planet" }));
            Unlock(Features.Parts, Features.Quests);
            Assert.That(Tabs(), Is.EqualTo(new[] { "Tab:worm", "Tab:planet", "Tab:signals" }));
            Assert.That(ButtonNames(Hub.Root), Does.Contain("Side:quests"));
            Unlock(Features.BiomassFarm);
            Assert.That(Tabs(), Is.EqualTo(new[] { "Tab:worm", "Tab:city", "Tab:planet", "Tab:signals" }));
            Assert.That(ButtonNames(Hub.Root), Does.Not.Contain("Side:shop"));
            Unlock(Features.WormLevel, Features.PartPulls, Features.Shop);
            Assert.That(Tabs(), Is.EqualTo(new[] { "Tab:worm", "Tab:city", "Tab:planet", "Tab:signals" }));
            Assert.That(ButtonNames(Hub.Root), Does.Contain("Side:shop"));
            Assert.That(ButtonNames(Hub.Root), Does.Not.Contain("Side:more"), "nothing else unlocked yet");
            Unlock(Features.Laboratory);
            Assert.That(ButtonNames(Hub.Root), Does.Contain("Side:more"));
            Unlock(Features.Arena, Features.SeasonPass);
            Assert.That(Tabs(), Is.EqualTo(new[] { "Tab:worm", "Tab:city", "Tab:planet", "Tab:signals", "Tab:arena" }));
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
        public void EveryUnlockedScreen_IsReachableFromTheHub()
        {
            Unlock(Features.Parts, Features.Quests, Features.BiomassFarm, Features.Laboratory, Features.Aliens);
            var reachable = HubLayout.Reachable(S, C, _f.Service.NowMs);
            foreach (var e in ScreenCatalog.All)
                if (e.FeatureId == null || Features.IsUnlocked(S, C, e.FeatureId))
                    Assert.That(reachable, Does.Contain(e.Id), e.Id + " unlocked but not reachable");
            Assert.That(reachable, Does.Not.Contain("arena"));

            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
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
            Unlock(Features.Parts, Features.RunShop, Features.WormLevel, Features.Quests, Features.RunMiniboss, Features.RunTwoDice); // Daily Goals unlock at level 3
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
                Features.Quests, Features.RunCampfire, Features.PartPulls, Features.RunSignatures, Features.Shop, Features.AlienPulls,
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
    }
}
