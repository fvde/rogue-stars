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
    /// the Launch Pod) with the squad walking around it; the pod opens its building card, where the squad is managed before the City.
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
        public void AfterTheAliens_TheHubShowsTheCrashSite_PodAndSquad_CityStillLocked()
        {
            ReachLevel(6);
            Assert.That(S.Onboarding.Unlocked.ContainsKey(Features.Aliens), Is.True);
            Assert.That(Features.IsUnlocked(S, C, Features.BiomassFarm), Is.False);
            var hub = Hub;
            foreach (var n in new[] { "CrashSite", "CrashSite:Saucer", "CrashSite:Fire", "CrashSite:Pod", "PodBadge" })
                Assert.That(Has(hub.Root, n), Is.True, n);
            Assert.That(hub.PodButton, Is.Not.Null);
            Assert.That(hub.Squad, Is.Not.Null, "the squad walks around the crash site");
            Assert.That(hub.Squad.Count, Is.EqualTo(1));
            Assert.That(hub.Root.GetComponentsInChildren<Text>(true).First(t => t.name == "PodBadgeText").text, Is.EqualTo("Squad 1/1"));
            Assert.That(hub.Root.GetComponentsInChildren<Button>(true).Any(b => b.name == "Tab:city"), Is.False, "the City stays locked");
        }

        [Test]
        public void ThePod_OpensItsCard_SeatsBoonLevelUpStarUp()
        {
            ReachLevel(6);
            var hub = Hub;
            hub.PodButton.onClick.Invoke();
            Assert.That(hub.PodCard, Is.Not.Null);
            Assert.That(hub.PodCard.IsOpen, Is.True);
            var card = hub.PodCard.Body;
            Assert.That(card.GetComponentsInChildren<Text>(true).First(t => t.name == "Name").text, Is.EqualTo("Launch Pod"));
            Assert.That(Btn(card, "Seat:alien_blorp"), Is.Not.Null);
            Assert.That(Has(card, "Boon:" + BoonKind.AlienDamagePct), Is.True);
            Assert.That(Btn(card, "Upgrade"), Is.Not.Null);
            Assert.That(card.GetComponentsInChildren<Text>(true).First(t => t.name == "CostType").text, Does.Contain("Biomass"));
            Assert.That(Btn(card, "StarUp"), Is.Not.Null);
            Assert.That(card.GetComponentsInChildren<Text>(true).First(t => t.name == "CityNote").text, Does.Contain("level 15"));
            Btn(card, "CloseCard").onClick.Invoke();
            Assert.That(hub.PodCard, Is.Null);
        }

        [Test]
        public void SquadAssignment_WorksFromThePodCard_BeforeTheCity()
        {
            ReachLevel(6);
            _f.Service.DevGrant(new List<Reward> { new Reward(RewardKinds.Alien, "alien_zib", 1), Reward.Cur(Currencies.Biomass, 10_000) });
            var hub = Hub;
            hub.OpenPodCard();
            // free the seat: Blorp leaves the squad
            Btn(hub.PodCard.Body, "Seat:alien_blorp").onClick.Invoke();
            Assert.That(AlienSystem.ActiveSquad(S, C), Is.Empty);
            Assert.That(hub.PodCard.IsOpen, Is.True, "the card stays open and refreshes");
            Assert.That(Has(hub.PodCard.Body, "EmptySeat"), Is.True);
            // seat Zib
            var pick = Btn(hub.PodCard.Body, "Pick:alien_zib");
            Assert.That(pick.interactable, Is.True);
            pick.onClick.Invoke();
            Assert.That(AlienSystem.ActiveSquad(S, C), Is.EqualTo(new[] { "alien_zib" }));
            Assert.That(Btn(hub.PodCard.Body, "Seat:alien_zib"), Is.Not.Null);
            Assert.That(Btn(hub.PodCard.Body, "Pick:alien_blorp").interactable, Is.False, "seats full");
            Assert.That(_f.Service.Engine.BuildLoadout(S, _f.Service.NowMs).Aliens.Select(a => a.AlienTypeId), Is.EqualTo(new[] { "alien_zib" }), "the squad joins runs");
            // level up with Biomass
            Btn(hub.PodCard.Body, "Upgrade").onClick.Invoke();
            Assert.That(CitySystem.LaunchPod(S).TimerId, Is.Not.Null);
            Assert.That(Features.IsUnlocked(S, C, Features.BiomassFarm), Is.False);
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
