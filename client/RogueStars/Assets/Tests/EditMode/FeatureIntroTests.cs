using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// GDD §16 feature intros: every feature's unlock screen shows its panels (art with a drawn fallback, one line, an
    /// animation) and ends on the guided action; every feature screen's "?" replays the intro (a topic list when the screen
    /// has several), without claiming anything; locked features have no "?".
    /// </summary>
    public sealed class FeatureIntroTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private void Unlock(params string[] ids) { foreach (var id in ids) S.Onboarding.Unlocked[id] = _f.Service.NowMs; }

        private static Button Btn(Component root, string name) =>
            root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        private static Text Named(Component root, string name) =>
            root.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == name);

        [Test]
        public void EveryIntroArtKey_HasADrawnFallback()
        {
            foreach (var f in C.FeatureOrder)
            {
                Assert.That(IntroArt.HasFallback(f.Art), Is.True, f.Id + " art " + f.Art);
                foreach (var step in f.Intro) Assert.That(IntroArt.HasFallback(step.Art), Is.True, f.Id + " panel art " + step.Art);
            }
            Assert.That(IntroArt.Hand(), Is.Not.Null);
        }

        [Test]
        public void EveryFeature_UnlockScreen_ShowsEachPanel_ThenTheGuidedAction()
        {
            foreach (var f in C.FeatureOrder)
            {
                Unlock(f.Id);
                var screen = new UnlockScreen(new List<string> { f.Id });
                _f.App.Router.Push(screen);
                Assert.That(screen.PageCount, Is.EqualTo(f.Intro.Count), f.Id);
                for (int p = 0; p < screen.PageCount; p++)
                {
                    screen.ShowPage(p);
                    Assert.That(Named(screen.Root, "FeatureName").text, Is.EqualTo(f.Name));
                    Assert.That(Named(screen.Root, "StepText").text, Is.EqualTo(f.Intro[p].Text), f.Id + " panel " + p);
                    var art = screen.Root.GetComponentsInChildren<Image>(true).First(i => i.name == "IntroArt");
                    Assert.That(art.sprite, Is.Not.Null, f.Id + " panel " + p + " art");
                    var anim = screen.Root.GetComponentInChildren<IntroAnimator>(true);
                    Assert.That(anim.Kind, Is.EqualTo(f.Intro[p].Anim));
                    if (anim.Kind == IntroAnim.Tap) Assert.That(anim.Hand, Is.Not.Null, "the hand taps");
                    if (anim.Kind == IntroAnim.Count) Assert.That(anim.Counter, Is.Not.Null);
                    if (anim.Kind == IntroAnim.Fill) Assert.That(anim.Fill, Is.Not.Null);
                    anim.Apply(anim.Kind, 0.7f); // a mid-animation pose never throws
                    if (p < screen.PageCount - 1)
                    {
                        Assert.That(screen.Action, Is.Null);
                        Assert.That(Btn(screen.Root, "IntroNext"), Is.Not.Null);
                    }
                }
                Assert.That(screen.OnLastPage, Is.True);
                Assert.That(screen.Action, Is.Not.Null, f.Id + ": the guided action");
                Assert.That(screen.Action.GetComponentInChildren<Text>().text, Is.EqualTo(HubLayout.UnlockAction(f.Id).Label));
                Assert.That(ScreenCatalog.Find(HubLayout.UnlockAction(f.Id).ScreenId), Is.Not.Null, f.Id + ": the action opens a screen");
                _f.App.Router.Pop();
            }
        }

        [Test]
        public void EveryFeature_HasAScreenWhoseHelpReplaysIt()
        {
            var covered = new HashSet<string>();
            foreach (var e in ScreenCatalog.All)
            {
                var screen = e.Create();
                if (screen.IntroFeatures != null) covered.UnionWith(screen.IntroFeatures);
            }
            foreach (var f in C.FeatureOrder) Assert.That(covered, Does.Contain(f.Id), f.Id + " has no screen with a \"?\"");
        }

        [Test]
        public void HelpButton_ReplaysTheIntro_WithoutClaiming_AndReturns()
        {
            Unlock(Features.Laboratory);
            _f.App.Open("laboratory");
            var lab = _f.App.Router.Current;
            Assert.That(lab.HelpButton, Is.Not.Null);
            Assert.That(lab.HelpButton.gameObject.activeSelf, Is.True);
            lab.HelpButton.onClick.Invoke();
            var replay = _f.App.Router.Current as UnlockScreen;
            Assert.That(replay, Is.Not.Null);
            Assert.That(replay.IsReplay, Is.True);
            Assert.That(replay.FeatureIds, Is.EqualTo(new[] { Features.Laboratory }));
            Assert.That(Named(replay.Root, "Kicker").text, Is.EqualTo("HOW IT WORKS"));
            Assert.That(Named(replay.Root, "StepText").text, Is.EqualTo(C.Feature(Features.Laboratory).Intro[0].Text));
            Btn(replay.Root, "IntroSkip").onClick.Invoke();
            Assert.That(replay.Action, Is.Null, "nothing to claim in a replay");
            Assert.That(Named(replay.Root, "Description"), Is.Null);
            long dia = S.Wallet.Get(Currencies.Diamonds);
            Btn(replay.Root, "IntroDone").onClick.Invoke();
            Assert.That(_f.App.Router.Current, Is.SameAs(lab));
            Assert.That(S.Onboarding.Celebrated, Does.Not.Contain(Features.Laboratory));
            Assert.That(S.Wallet.Get(Currencies.Diamonds), Is.EqualTo(dia));
        }

        [Test]
        public void HelpButton_OnAScreenOfSeveralFeatures_OpensATopicList()
        {
            Unlock(Features.Parts, Features.WormLevel);
            _f.App.Open("worm");
            var worm = _f.App.Router.Current;
            worm.HelpButton.onClick.Invoke();
            var topics = _f.App.ModalLayer.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Intro:")).Select(b => b.name).ToList();
            Assert.That(topics, Is.EqualTo(new[] { "Intro:" + Features.Parts, "Intro:" + Features.WormLevel }));
            Btn(_f.App.ModalLayer, "Intro:" + Features.WormLevel).onClick.Invoke();
            var replay = (UnlockScreen)_f.App.Router.Current;
            Assert.That(replay.FeatureIds, Is.EqualTo(new[] { Features.WormLevel }));
            Assert.That(_f.App.ModalLayer.GetComponentsInChildren<Button>(true).Any(b => b.name.StartsWith("Intro:")), Is.False, "the list closed");
        }

        [Test]
        public void HelpButton_IsHidden_UntilAFeatureOfTheScreenUnlocks()
        {
            _f.App.Open("portals");
            var portals = _f.App.Router.Current;
            Assert.That(portals.HelpButton, Is.Not.Null);
            Assert.That(portals.HelpButton.gameObject.activeSelf, Is.False, "level 0: no run feature to explain yet");
            Unlock(Features.RunShop);
            portals.Rebuild();
            Assert.That(portals.HelpButton.gameObject.activeSelf, Is.True);
            Assert.That(portals.UnlockedIntroFeatures(), Is.EqualTo(new[] { Features.RunShop }));
        }
    }
}
