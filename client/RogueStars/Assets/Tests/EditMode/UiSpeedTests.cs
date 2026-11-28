using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
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

        // ---- step 3: in-place updates --------------------------------------------------------------------------

        private sealed class KeyedScreen : UiScreen
        {
            public readonly List<string> Items = new List<string> { "a", "b", "c" };
            public readonly Dictionary<string, int> Values = new Dictionary<string, int> { { "a", 1 }, { "b", 2 }, { "c", 3 }, { "d", 4 } };
            public int Total;
            protected override bool InPlace => true;
            public override string Title => "Keyed";
            protected override void Build(RectTransform body)
            {
                foreach (var id in Items)
                {
                    var i = id;
                    Part(body, i, K(Values[i]), b => Ui.Label(b, i + "=" + Values[i]).name = "Item:" + i);
                }
                Part(body, "total", "", b => Live(Ui.Label(b, ""), () => "total " + Total).name = "Total");
            }
            public Transform Body => BodyFrame.GetComponentsInChildren<Transform>(true).First(t => t.name == "Item:a").parent;
        }

        private static GameObject Child(UiScreen s, string name) =>
            s.Root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)?.gameObject;

        [Test]
        public void InPlace_RebuildsOnlyThePartWhoseKeyChanged_KeepsOrder_DropsRemovedParts()
        {
            var screen = new KeyedScreen();
            _f.App.Router.Push(screen);
            var a = Child(screen, "Item:a"); var b = Child(screen, "Item:b"); var c = Child(screen, "Item:c");
            int built = screen.PartBuilds;

            screen.Refresh();
            Assert.That(screen.PartBuilds, Is.EqualTo(built), "nothing changed: nothing rebuilt");
            Assert.That(Child(screen, "Item:b"), Is.SameAs(b));

            screen.Values["b"] = 20;
            screen.Refresh();
            Assert.That(screen.PartBuilds, Is.EqualTo(built + 1), "only b rebuilt");
            Assert.That(Child(screen, "Item:a"), Is.SameAs(a));
            Assert.That(Child(screen, "Item:c"), Is.SameAs(c));
            var b2 = Child(screen, "Item:b");
            Assert.That(b2, Is.Not.SameAs(b));
            Assert.That(b2.GetComponent<Text>().text, Is.EqualTo("b=20"));

            screen.Items.Clear(); screen.Items.AddRange(new[] { "c", "d", "a" });
            screen.Refresh();
            var body = screen.Body;
            var order = new List<string>();
            for (int i = 0; i < body.childCount; i++) order.Add(body.GetChild(i).name);
            Assert.That(order, Is.EqualTo(new[] { "Item:c", "Item:d", "Item:a", "Total" }), "order follows the Build calls; b is gone");
            Assert.That(Child(screen, "Item:a"), Is.SameAs(a), "kept across the reorder");
            Assert.That(Child(screen, "Item:b"), Is.Null);
        }

        [Test]
        public void InPlace_LiveTextOfAKeptPart_UpdatesWithoutNewGameObjects()
        {
            var screen = new KeyedScreen();
            _f.App.Router.Push(screen);
            var total = Child(screen, "Total");
            screen.Total = 42;
            _f.App.Session.NotifyChanged();
            Assert.That(Child(screen, "Total"), Is.SameAs(total));
            Assert.That(total.GetComponent<Text>().text, Is.EqualTo("total 42"));
        }

        [Test]
        public void ResourceBar_BalanceChange_UpdatesTheChipInPlace()
        {
            _f.Service.DevGrantStarterKit();
            var bar = _f.App.Bar;
            bar.Rebuild();
            var chip = _f.App.Root.GetComponentsInChildren<Text>(true).First(t => t.name == "Chip:" + Currencies.Diamonds);
            int builds = bar.Builds;
            _f.Service.State.Wallet.Add(Currencies.Diamonds, 123);
            _f.App.Session.NotifyChanged();
            var after = _f.App.Root.GetComponentsInChildren<Text>(true).First(t => t.name == "Chip:" + Currencies.Diamonds);
            Assert.That(after, Is.SameAs(chip), "same chip, not a rebuilt bar");
            Assert.That(bar.Builds, Is.EqualTo(builds));
            Assert.That(after.text, Is.EqualTo(Fmt.Num(_f.Service.State.Wallet.Get(Currencies.Diamonds))));
        }

        [Test]
        public void PartsScreen_StateChange_KeepsUnchangedTiles_AndRebuildsTheChangedOne()
        {
            _f.Service.DevGrantStarterKit();
            _f.App.Router.Push(new PartsScreen());
            var screen = (PartsScreen)_f.App.Router.Current;
            Assert.That(screen.ShownParts.Count, Is.GreaterThanOrEqualTo(2));
            string first = screen.ShownParts[0], second = screen.ShownParts[1];
            var tile1 = Child(screen, "Part:" + first);
            var tile2 = Child(screen, "Part:" + second);
            int goCount = screen.Root.GetComponentsInChildren<Transform>(true).Length;

            // a refresh with nothing changed (periodic refresh, NotifyChanged): every GameObject stays
            int built = screen.PartBuilds;
            _f.App.Session.NotifyChanged();
            Assert.That(screen.PartBuilds, Is.EqualTo(built));
            Assert.That(Child(screen, "Part:" + first), Is.SameAs(tile1));
            Assert.That(screen.Root.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(goCount));
            Assert.That(screen.BuildCount, Is.EqualTo(1), "never fully rebuilt");

            // Ore changes (no tile shows it): the tiles stay
            _f.Service.State.Wallet.Add(Currencies.Ore, 777);
            _f.App.Session.NotifyChanged();
            Assert.That(Child(screen, "Part:" + first), Is.SameAs(tile1));

            // one part changes: only its tile is rebuilt
            _f.Service.State.Parts.Items[first].Level += 1;
            _f.App.Session.NotifyChanged();
            Assert.That(Child(screen, "Part:" + first), Is.Not.SameAs(tile1), "the changed part's tile is new");
            Assert.That(Child(screen, "Part:" + second), Is.SameAs(tile2), "the other tiles stay");
            Assert.That(screen.BuildCount, Is.EqualTo(1));
        }

        [Test]
        public void EveryConvertedScreen_RefreshesWithoutRebuildingAnything()
        {
            _f.Service.DevGrantStarterKit();
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            foreach (var id in new[] { "worm", "parts", "laboratory", "passes", "portals", "heroes", "mounts", "aliens", "city", "shop" })
            {
                _f.App.OpenTab(null);
                _f.App.Open(id);
                var screen = _f.App.Router.Current;
                Assert.That(screen, Is.Not.InstanceOf<HubScreen>(), id);
                int built = screen.PartBuilds, gos = screen.Root.GetComponentsInChildren<Transform>(true).Length;
                _f.App.Session.NotifyChanged();
                Assert.That(screen.BuildCount, Is.EqualTo(1), id + ": refreshed in place, not rebuilt");
                Assert.That(screen.PartBuilds, Is.EqualTo(built), id + ": no part changed");
                Assert.That(screen.Root.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(gos), id);
            }
            _f.App.OpenTab(null);
            var hub = _f.App.Router.Current;
            int hubBuilt = hub.PartBuilds;
            _f.App.Session.NotifyChanged();
            Assert.That(hub.PartBuilds, Is.EqualTo(hubBuilt), "hub: no part changed");
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
