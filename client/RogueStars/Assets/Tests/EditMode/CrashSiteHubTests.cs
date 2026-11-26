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
    /// "The crash site comes first" (owner, 2026-11-25): from the Aliens unlock the hub's planet shows the crash site (fireplace, saucer,
    /// the Launch Pod) with the squad walking around it; the Camp tab and the props open the City screen in camp mode (the pod's seats), the full City from level 15.
    /// </summary>
    public sealed class CrashSiteHubTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

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

        private static bool Has(Component root, string name) => root.GetComponentsInChildren<RectTransform>(true).Any(t => t.name == name);
        private static Button Btn(Component root, string name) => root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        /// <summary>Main level <paramref name="level"/> cleared, every moment up to it seen (the settler records the unlocks).</summary>
        private void ReachLevel(int level)
        {
            S.Counters[Requirements.MainLevelCounter] = level;
            _f.Service.Refresh(_ => { });
            foreach (var id in S.Onboarding.Unlocked.Keys) S.Onboarding.Celebrated.Add(id);
        }

        [Test]
        public void BeforeTheAliens_NoCrashSite()
        {
            ReachLevel(5);
            var hub = Hub;
            Assert.That(HubScreen.CrashSiteVisible(S), Is.False);
            Assert.That(Has(hub.Root, "CrashSite"), Is.False);
            Assert.That(hub.PodButton, Is.Null);
        }

        [Test]
        public void AfterTheAliens_TheHubShowsTheCrashSiteProps_AndTheCampIcon()
        {
            ReachLevel(6);
            Assert.That(S.Onboarding.Unlocked.ContainsKey(Features.Aliens), Is.True);
            Assert.That(Features.IsUnlocked(S, C, Features.BiomassFarm), Is.False);
            var hub = Hub;
            foreach (var n in new[] { "CrashSite", "CrashSite:Saucer", "CrashSite:Fire", "CrashSite:Pod" })
                Assert.That(Has(hub.Root, n), Is.True, n);
            Assert.That(Has(hub.Root, "PodBadge"), Is.False, "no planet-integrated pod card any more");
            Assert.That(hub.Squad, Is.Not.Null, "the squad walks around the crash site");
            Assert.That(hub.Squad.Count, Is.EqualTo(1));
            // one entry for the camp: the tab "Camp" with the saucer art
            var tab = Btn(hub.Root, "Tab:city");
            Assert.That(tab, Is.Not.Null, "the camp tab shows from level 6");
            Assert.That(tab.GetComponentsInChildren<Text>(true).Any(t => t.name == "Label" && t.text == "Camp"), Is.True);
            Assert.That(HubLayout.Tabs(S, C).First(e => e.Id == "city").Icon, Is.EqualTo(HubLayout.CampIcon));
        }

        [Test]
        public void TheTab_AndTheCrashSiteProps_OpenTheCityScreenInCampMode()
        {
            ReachLevel(6);
            var hub = Hub;
            Btn(hub.Root, "Tab:city").onClick.Invoke();
            var city = _f.App.Router.Current as CityScreen;
            Assert.That(city, Is.Not.Null);
            Assert.That(city.CampMode, Is.True);
            _f.App.Router.Pop();
            hub = Hub;
            hub.PodButton.onClick.Invoke();
            Assert.That(_f.App.Router.Current, Is.InstanceOf<CityScreen>());
            Assert.That(((CityScreen)_f.App.Router.Current).CampMode, Is.True);
        }

        [Test]
        public void CampMode_ShowsNoDockActions_NoDigs_NoEdits_OnlyTheNote()
        {
            ReachLevel(6);
            _f.App.Open("city");
            var city = (CityScreen)_f.App.Router.Current;
            city.ForceTablet = true; // even a tablet gets no build rail
            city.Rebuild();
            foreach (var n in new[] { "Dock", "Dock:Build", "Dock:Workers", "Dock:Output", "CollectAll", "Rail" })
                Assert.That(Has(city.Root, n), Is.False, n);
            Assert.That(city.Root.GetComponentsInChildren<Text>(true).First(t => t.name == "CampNoteText").text, Is.EqualTo("The camp grows into a city at level 15"));
            city.EnterEdit();
            Assert.That(city.EditMode, Is.False);
            city.OpenDig("patch_a");
            Assert.That(city.DigPatch, Is.Null);
            Assert.That(Has(city.Root, "DigPopup"), Is.False);
        }

        [Test]
        public void CampMode_ThePodCard_ShowsOnlyTheSeats_AndSeatingWorks()
        {
            ReachLevel(6);
            _f.Service.DevGrant(new List<Reward> { new Reward(RewardKinds.Alien, "alien_zib", 1), Reward.Cur(Currencies.Biomass, 10_000) });
            _f.App.Open("city");
            var city = (CityScreen)_f.App.Router.Current;
            string uid = CitySystem.LaunchPod(S).Uid;
            city.Select(uid);
            var card = city.Root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "BuildingCard");
            Assert.That(card.GetComponentsInChildren<Text>(true).First(t => t.name == "Name").text, Is.EqualTo("Launch Pod"));
            Assert.That(Btn(card, "Worker:alien_blorp"), Is.Not.Null);
            foreach (var n in new[] { "Upgrade", "StarUp", "Move", "Store", "UpgradeRow", "Actions", "LevelLine", "Output" })
                Assert.That(Has(card, n), Is.False, n);
            // free the seat, seat Zib
            Btn(card, "Worker:alien_blorp").onClick.Invoke();
            Assert.That(AlienSystem.ActiveSquad(S, C), Is.Empty);
            card = city.Root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "BuildingCard");
            Btn(card, "Pick:alien_zib").onClick.Invoke();
            Assert.That(AlienSystem.ActiveSquad(S, C), Is.EqualTo(new[] { "alien_zib" }));
            Assert.That(CitySystem.LaunchPod(S).TimerId, Is.Null);
        }

        [Test]
        public void AtTheCity_TheSameEntryShowsTheCityIcon_AndEverythingUnlocks()
        {
            ReachLevel(6);
            Assert.That(HubLayout.Tabs(S, C).First(e => e.Id == "city").Icon, Is.EqualTo(HubLayout.CampIcon));
            ReachLevel(15);
            Assert.That(Features.IsUnlocked(S, C, Features.BiomassFarm), Is.True);
            var hub = Hub;
            var entry = HubLayout.Tabs(S, C).First(e => e.Id == "city");
            Assert.That(entry.Icon, Is.EqualTo("tab_city"));
            Assert.That(entry.Label, Is.EqualTo("City"));
            Assert.That(Btn(hub.Root, "Tab:city").GetComponentsInChildren<Text>(true).Any(t => t.name == "Label" && t.text == "City"), Is.True);
            Btn(hub.Root, "Tab:city").onClick.Invoke();
            var city = (CityScreen)_f.App.Router.Current;
            Assert.That(city.CampMode, Is.False);
            foreach (var n in new[] { "Dock:Build", "Dock:Workers", "Dock:Output", "CollectAll" })
                Assert.That(Has(city.Root, n), Is.True, n);
            city.Select(CitySystem.LaunchPod(S).Uid);
            var card = city.Root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "BuildingCard");
            foreach (var n in new[] { "Upgrade", "Move", "Store" })
                Assert.That(Has(card, n), Is.True, n);
        }

        [Test]
        public void AliensMoment_IntroducesTheLaunchPod_InOneMoment()
        {
            S.Counters[Requirements.MainLevelCounter] = 5;
            _f.Service.Refresh(_ => { });
            foreach (var id in S.Onboarding.Unlocked.Keys) S.Onboarding.Celebrated.Add(id);
            S.Counters[Requirements.MainLevelCounter] = 6;
            _f.Service.Refresh(_ => { });
            Assert.That(Hub.ShowPendingUnlock(), Is.True);
            var moment = (UnlockScreen)_f.App.Router.Current;
            Assert.That(moment.FeatureIds, Is.EqualTo(new[] { Features.Aliens, Features.AlienSquad }));
            Assert.That(moment.PageCount, Is.EqualTo(C.Feature(Features.Aliens).Intro.Count + C.Feature(Features.AlienSquad).Intro.Count));
            moment.ShowPage(moment.PageCount - 2);
            Assert.That(moment.Root.GetComponentsInChildren<Text>(true).First(t => t.name == "StepText").text, Does.Contain("join your runs"));
            moment.ShowPage(moment.PageCount - 1);
            moment.Claim();
            Assert.That(S.Onboarding.Celebrated, Does.Contain(Features.AlienSquad));
            Assert.That(_f.App.Router.Current, Is.InstanceOf<AliensScreen>());
        }
    }
}
