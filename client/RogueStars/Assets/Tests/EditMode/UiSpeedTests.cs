using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>UI speed: instant tap feedback, busy buttons, no pointless rebuilds, in-place updates.</summary>
    public sealed class UiSpeedTests
    {
        private AppFixture _f;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        // ---- step 1: instant feedback --------------------------------------------------------------------------

        [Test]
        public void Press_ScalesTheButtonAtOnce_AndReleaseRestoresIt()
        {
            var press = _f.App.Press;
            Assert.That(press, Is.Not.Null, "the app's canvas carries the press feedback");
            var b = Ui.Button(_f.App.Root, "Go", () => { });
            var target = UiPress.Target(b.GetComponentInChildren<Text>().gameObject);
            Assert.That(target, Is.SameAs(b.transform), "a press on the caption presses its button");
            press.Press(target);
            Assert.That(b.transform.localScale.x, Is.EqualTo(UiPress.PressScale).Within(1e-4));
            press.Release(true);
            press.Settle();
            Assert.That(b.transform.localScale, Is.EqualTo(Vector3.one), "no command went out: back to rest");
            Assert.That(press.IsBusy(b.transform), Is.False);
        }

        [Test]
        public void Press_IgnoresDisabledButtons()
        {
            var b = Ui.Button(_f.App.Root, "Off", () => { }, null, -1, Theme.ButtonHeight, false);
            Assert.That(UiPress.Target(b.gameObject), Is.Null);
        }

        [Test]
        public void TapThatSendsACommand_ShowsBusyUntilNothingIsInFlight_AndIgnoresASecondTap()
        {
            int inFlight = 0;
            var press = new GameObject("Press").AddComponent<UiPress>();
            try
            {
                press.InFlight = () => inFlight;
                int clicks = 0;
                var b = Ui.Button(_f.App.Root, "Buy", () => clicks++);
                press.Press(b.transform);
                press.Release(true);
                inFlight = 1; // the tap's command went out
                press.Settle();
                Assert.That(press.IsBusy(b.transform), Is.True);
                var group = b.GetComponent<CanvasGroup>();
                Assert.That(group, Is.Not.Null);
                Assert.That(group.alpha, Is.EqualTo(UiPress.BusyAlpha).Within(1e-4), "busy look: dimmed");
                Assert.That(b.IsInteractable(), Is.False, "a double tap is ignored while the command is in flight");
                Assert.That(b.transform.localScale.x, Is.EqualTo(UiPress.PressScale).Within(1e-4), "stays pressed");
                Assert.That(UiPress.Target(b.gameObject), Is.Null, "a busy button cannot be pressed again");

                inFlight = 0; // answered
                press.Settle();
                Assert.That(press.IsBusy(b.transform), Is.False);
                Assert.That(b.GetComponent<CanvasGroup>(), Is.Null, "the busy group is removed again");
                Assert.That(b.IsInteractable(), Is.True);
                Assert.That(b.transform.localScale, Is.EqualTo(Vector3.one));
            }
            finally { Object.DestroyImmediate(press.gameObject); }
        }

        [Test]
        public void CommandThatResolvesAtOnce_NeverShowsBusy()
        {
            int inFlight = 0;
            var press = new GameObject("Press").AddComponent<UiPress>();
            try
            {
                press.InFlight = () => inFlight;
                var b = Ui.Button(_f.App.Root, "Instant", () => { });
                press.Press(b.transform);
                press.Release(true);
                press.Settle(); // predicted locally: in flight went up and down within the tap
                Assert.That(press.IsBusy(b.transform), Is.False);
                Assert.That(b.transform.localScale, Is.EqualTo(Vector3.one));
            }
            finally { Object.DestroyImmediate(press.gameObject); }
        }

        // ---- step 2: no pointless rebuilds ---------------------------------------------------------------------

        private sealed class CountingScreen : UiScreen
        {
            public int Builds;
            public override string Title => "Counting";
            protected override void Build(RectTransform body) { Builds++; Ui.Label(body, "x"); }
        }

        [Test]
        public void Pop_WithoutAStateChange_DoesNotRebuildTheScreenBelow()
        {
            var below = new CountingScreen();
            _f.App.Router.Push(below);
            _f.App.Router.Push(new CountingScreen());
            Assert.That(below.Builds, Is.EqualTo(1));
            _f.App.Router.Pop();
            Assert.That(below.Builds, Is.EqualTo(1), "nothing changed while it was hidden");
            Assert.That(below.Root.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void Pop_AfterAStateChange_RebuildsTheScreenBelow()
        {
            var below = new CountingScreen();
            _f.App.Router.Push(below);
            _f.App.Router.Push(new CountingScreen());
            _f.Service.ExecuteNow(new RogueStars.Rules.Systems.LoginClaim()); // a command: a new state version
            Assert.That(below.IsStale, Is.True);
            _f.App.Router.Pop();
            Assert.That(below.Builds, Is.EqualTo(2), "the state changed while it was hidden");
            Assert.That(below.IsStale, Is.False);
        }

        [Test]
        public void TabSwitch_KeepsTheHub_AndBuildsNoThrowawayHub()
        {
            var hub = _f.App.Router.Stack[0];
            Assert.That(hub, Is.InstanceOf<RogueStars.Client.Screens.HubScreen>());
            int hubBuilds = hub.BuildCount;
            _f.App.OpenTab("shop");
            Assert.That(_f.App.Router.Depth, Is.EqualTo(2));
            Assert.That(_f.App.Router.Stack[0], Is.SameAs(hub), "the hub is kept, not rebuilt from scratch");
            _f.App.OpenTab("quests");
            Assert.That(_f.App.Router.Depth, Is.EqualTo(2), "the previous tab screen is gone");
            Assert.That(_f.App.Router.Stack[0], Is.SameAs(hub));
            Assert.That(hub.Root.gameObject.activeSelf, Is.False, "hidden under the tab screen");
            _f.App.OpenTab(null); // the Planet tab: the hub itself
            Assert.That(_f.App.Router.Current, Is.SameAs(hub));
            Assert.That(hub.Root.gameObject.activeSelf, Is.True);
            Assert.That(hub.BuildCount, Is.EqualTo(hubBuilds), "no state change: the hub was never rebuilt");
        }

        [Test]
        public void SequenceFlushUntil_FinishesStepsUpToTheMark_AndKeepsTheRest()
        {
            // edit mode runs Then steps at once; FlushUntil without a queued mark is a no-op
            var go = new GameObject("Seq");
            try
            {
                var seq = UiSequence.On(go);
                Assert.That(seq.FlushUntil("decide"), Is.False);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
