using System.Collections.Generic;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Core;
using UnityEngine;

namespace RogueStars.Client.Tests
{
    /// <summary>G1: the toast layer is gone; events become animations, banners or nothing (owner-approved map T01–T58).</summary>
    public sealed class FeedbackTests
    {
        private AppFixture _f;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private void Raise(string type, params string[] args) =>
            _f.App.Feedback.OnEvents(new List<GameEvent> { new GameEvent { Type = type, Args = new List<string>(args) } });

        [Test]
        public void Rewards_FlyIntoTheCurrencyBar()
        {
            int before = FeedbackFx.FlightsStarted;
            Raise("reward", RewardKinds.Currency, Currencies.Diamonds, "50");
            Assert.That(FeedbackFx.FlightsStarted, Is.EqualTo(before + 1));
            Assert.That(FeedbackFx.LastFlightTarget, Is.EqualTo("ChipBg:" + Currencies.Diamonds).Or.EqualTo("CurrencyDetails"));
            Assert.That(_f.App.Feedback.Layer.Find("FlyIcon"), Is.Null, "icons are gone once they landed");
        }

        [Test]
        public void StarUps_PopAStar_BannersQueue_UnknownEventsSilent()
        {
            int stars = _f.App.Feedback.StarPops;
            Raise("hero_star", "hero_x", "5");
            Assert.That(_f.App.Feedback.StarPops, Is.EqualTo(stars + 1));
            Raise("worm_level", "12");
            Assert.That(_f.App.Feedback.LastBanner, Is.EqualTo("LEVEL UP!"));
            Assert.That(_f.App.Feedback.Layer.Find(RevealBanner.Name), Is.Not.Null);
            int banners = _f.App.Feedback.BannerCount;
            Raise("some_new_event", "x");
            Raise("alien_assigned", "a", "b");
            Assert.That(_f.App.Feedback.BannerCount, Is.EqualTo(banners));
            Assert.That(_f.App.Feedback.BubbleVisible, Is.False);
        }

        [Test]
        public void Laboratory_ResearchCollected_PopsItsNode()
        {
            var lab = new LaboratoryScreen();
            Assert.That(lab.OnFeedback(new GameEvent { Type = "lab_collected", Args = new List<string> { "x", "1" } }), Is.True);
            Assert.That(lab.OnFeedback(new GameEvent { Type = "reward" }), Is.False);
        }

        [Test]
        public void ErrorBubble_SitsAboveTheTappedButton()
        {
            var screen = _f.App.Router.Current;
            var btn = Ui.Button(screen.Root, "Tap me", null);
            Ui.LastPressed = btn;
            _f.App.Feedback.Error("Not enough coins");
            var bubble = _f.App.Feedback.Layer.Find("ErrorBubble") as RectTransform;
            Assert.That(bubble, Is.Not.Null);
            var b = new Vector3[4]; var t = new Vector3[4];
            bubble.GetWorldCorners(b);
            ((RectTransform)btn.transform).GetWorldCorners(t);
            Assert.That(b[0].y, Is.GreaterThanOrEqualTo(t[1].y - 0.01f), "above the button");
            Ui.LastPressed = null;
        }

        [Test]
        public void ToastTypesAreGone()
        {
            Assert.That(System.Type.GetType("RogueStars.Client.App.ToastCenter, RogueStars.Client"), Is.Null);
            Assert.That(System.Type.GetType("RogueStars.Client.App.EventText, RogueStars.Client"), Is.Null);
        }
    }
}
