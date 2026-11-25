using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using UnityEngine;

namespace RogueStars.Client.Tests
{
    public sealed class RouterTests
    {
        private sealed class ProbeScreen : UiScreen
        {
            private readonly string _name;
            public int Builds, Shows, Hides;
            public ProbeScreen(string name) { _name = name; }
            public override string Title => _name;
            protected override void Build(RectTransform body) { Builds++; Ui.Label(body, _name); }
            public override void OnShow() => Shows++;
            public override void OnHide() => Hides++;
        }

        private AppFixture _f;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        [Test]
        public void Start_ShowsHubAsRoot()
        {
            Assert.That(_f.App.Router.Depth, Is.EqualTo(1));
            Assert.That(_f.App.Router.Current, Is.InstanceOf<HubScreen>());
            Assert.That(_f.App.Router.Pop(), Is.False, "root is never popped");
        }

        [Test]
        public void PushPop_ManagesStackAndVisibility()
        {
            var router = _f.App.Router;
            var hub = router.Current;
            var a = new ProbeScreen("A");
            var b = new ProbeScreen("B");
            router.Push(a);
            router.Push(b);
            Assert.That(router.Depth, Is.EqualTo(3));
            Assert.That(router.Current, Is.SameAs(b));
            Assert.That(a.Root.gameObject.activeSelf, Is.False);
            Assert.That(hub.Root.gameObject.activeSelf, Is.False);
            Assert.That(a.Hides, Is.EqualTo(1));

            Assert.That(router.Pop(), Is.True);
            Assert.That(router.Current, Is.SameAs(a));
            Assert.That(b.IsCreated, Is.False, "popped screen is destroyed");
            Assert.That(a.Root.gameObject.activeSelf, Is.True);
            Assert.That(a.Shows, Is.EqualTo(2));
            Assert.That(a.Builds, Is.GreaterThanOrEqualTo(2), "returning screen rebuilds from current state");

            Assert.That(_f.App.HandleBack(), Is.True);
            Assert.That(router.Current, Is.SameAs(hub));
        }

        [Test]
        public void StateChange_RebuildsCurrentScreen()
        {
            var probe = new ProbeScreen("P");
            _f.App.Router.Push(probe);
            int before = probe.Builds;
            _f.Service.ExecuteNow(new RogueStars.Rules.Systems.LoginClaim());
            Assert.That(probe.Builds, Is.EqualTo(before + 1));
        }

        [Test]
        public void Reset_DestroysWholeStack()
        {
            var a = new ProbeScreen("A");
            _f.App.Router.Push(a);
            _f.App.Router.Reset(new ProbeScreen("Root"));
            Assert.That(_f.App.Router.Depth, Is.EqualTo(1));
            Assert.That(a.IsCreated, Is.False);
        }

        [Test]
        public void BackClosesModalBeforePopping()
        {
            _f.App.Router.Push(new ProbeScreen("A"));
            Ui.Modal(_f.App.ModalLayer, "Hello", null);
            Assert.That(_f.App.ModalLayer.childCount, Is.EqualTo(1));
            Assert.That(_f.App.HandleBack(), Is.True);
            Assert.That(_f.App.ModalLayer.childCount, Is.EqualTo(0));
            Assert.That(_f.App.Router.Depth, Is.EqualTo(2));
        }

        [Test]
        public void Events_BecomeFeedback_NoToastLayer()
        {
            // G1: rewards fly into the currency bar; there is no toast layer any more
            int flights = FeedbackFx.FlightsStarted;
            _f.App.Session.Run(new RogueStars.Rules.Systems.LoginClaim());
            Assert.That(FeedbackFx.FlightsStarted, Is.GreaterThan(flights), "the login reward flies into the bar");
            Assert.That(_f.App.Root.Find("ToastLayer"), Is.Null);
            Assert.That(_f.App.Root.Find("FeedbackLayer"), Is.Not.Null);
        }
    }
}
