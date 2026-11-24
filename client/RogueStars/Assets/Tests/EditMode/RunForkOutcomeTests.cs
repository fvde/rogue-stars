using System.Collections.Generic;
using NUnit.Framework;
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
    /// Run screen: the zoomed board with A / B landing tiles to tap at a fork (no text list), the "Roll n/N" clock, and the
    /// result screen of event / chest / campfire / shop outcomes (chance roll, gained/lost lines, auto passes it).
    /// </summary>
    public sealed class RunForkOutcomeTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; }
        [TearDown] public void TearDown() { RunScreen.InstantOutcomes = false; _f.Dispose(); }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static string Texts(Transform root)
        {
            var parts = new List<string>();
            foreach (var t in root.GetComponentsInChildren<Text>(true)) if (t.gameObject.activeInHierarchy) parts.Add(t.text);
            return string.Join(" | ", parts);
        }

        private ActiveRun StartRun()
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            return S.Run.Active;
        }

        /// <summary>start → 1 → 2 (fork) → A: 3 → 4 → 5 → 6 → 9 / B: 7 → 8 (shop) → 9 → 10 gate.</summary>
        private static void ForkBoard(ActiveRun run)
        {
            run.Tiles.Clear();
            string[] ids = { "start", "empty", "empty", "empty", "empty", "empty", "empty", "empty", "shop", "empty", "boss_gate" };
            for (int i = 0; i < ids.Length; i++) run.Tiles.Add(new RunTile { Id = i, TileId = ids[i], X = i, Y = i >= 7 && i <= 8 ? 1 : 0 });
            void Link(int a, int b) => run.Tiles[a].Next.Add(b);
            Link(0, 1); Link(1, 2); Link(2, 3); Link(2, 7); Link(3, 4); Link(4, 5); Link(5, 6); Link(6, 9); Link(7, 8); Link(8, 9); Link(9, 10);
            run.Tiles[7].X = 3; run.Tiles[8].X = 4;
            run.BossGateTile = -1; // the path ends at 10 (drawn as an arena tile; the arena itself is not open)
            run.BoardEnd = 10;
            run.Trail.Clear(); run.Trail.AddRange(new[] { 0, 1, 2 });
            run.Position = 2;
            run.Phase = RunPhase.ChooseBranch;
            run.BranchOptions.Clear(); run.BranchOptions.AddRange(new[] { 3, 7 });
            run.StepsLeft = 4;
        }

        [Test]
        public void BranchLanding_FollowsTheStepsLeft_AndStopsAtShops()
        {
            var run = StartRun();
            ForkBoard(run);
            Assert.That(RunScreen.BranchLanding(run, 0, C), Is.EqualTo(6), "A: 4 steps = 3, 4, 5, 6");
            Assert.That(RunScreen.BranchLanding(run, 1, C), Is.EqualTo(8), "B: the shop forces a stop");
            Assert.That(RunScreen.BranchPath(run, 0, C), Is.EqualTo(new List<int> { 3, 4, 5, 6 }));
            run.StepsLeft = 1;
            Assert.That(RunScreen.BranchLanding(run, 0, C), Is.EqualTo(3));
        }

        [Test]
        public void Fork_IsPickedOnTheBoard_AtTheHighlightedLandingTiles()
        {
            var run = StartRun();
            ForkBoard(run);
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            Assert.That(_f.App.ModalLayer.childCount, Is.EqualTo(0), "no text list modal at a fork");
            var a = (RectTransform)FindNamed(screen.Root, "BranchPick:A");
            var b = (RectTransform)FindNamed(screen.Root, "BranchPick:B");
            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.Not.Null);
            Assert.That(Vector2.Distance(a.anchoredPosition, screen.OverlayPoint(WorldBoard.TopOf(run.Tiles[6]) + new Vector2(0, 0.2f))), Is.LessThan(0.5f), "A sits on its landing tile");
            Assert.That(Vector2.Distance(b.anchoredPosition, screen.OverlayPoint(WorldBoard.TopOf(run.Tiles[8]) + new Vector2(0, 0.2f))), Is.LessThan(0.5f), "B sits on the shop it stops at");
            Assert.That(a.sizeDelta.x, Is.GreaterThanOrEqualTo(150f), "a thumb-sized target");
            Assert.That(Texts(a), Does.Contain("A · LAND"));
            Assert.That(FindNamed(screen.Root, "BranchOption:B"), Is.Not.Null, "the option tile is tappable too");
            Assert.That(FindNamed(screen.World.Root.transform, "BranchPath"), Is.Not.Null, "the walked path is highlighted");
            Assert.That(Texts(FindNamed(screen.Root, "RollButton")), Does.Contain("Pick a path"));

            b.GetComponent<Button>().onClick.Invoke();
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Shop), "path B taken, stopped at the shop");
            Assert.That(S.Run.Active.Position, Is.EqualTo(8));
        }

        [Test]
        public void Board_IsZoomedIn_AndShowsTheRollClock()
        {
            // a tile's top face is at least 15% of the screen width (big, thumb-sized tap targets)
            Assert.That(Iso.TileW / WorldBoard.ViewWidth, Is.GreaterThanOrEqualTo(0.15f));
            var run = StartRun();
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            int est = RunSystem.BossRollEstimate(C, run);
            Assert.That(est, Is.InRange(25, 35));
            Assert.That(screen.Subtitle, Does.StartWith("Roll 0/" + est));                 // no laps: the path never repeats
            Assert.That(Texts(FindNamed(screen.Root, "RollCounter")), Does.Contain("0/" + est));
            Assert.That(Texts(FindNamed(screen.Root, "BossClock")), Does.Contain("BOSS"));
        }

        private RunScreen EventOnScreen(string eventId)
        {
            var run = StartRun();
            var tiles = new List<string>();
            for (int i = 0; i < 20; i++) tiles.Add("empty");
            run.Tiles.Clear();
            for (int i = 0; i <= tiles.Count; i++) { var t = new RunTile { Id = i, TileId = i == 0 ? "start" : "empty", X = i }; if (i < tiles.Count) t.Next.Add(i + 1); run.Tiles.Add(t); }
            run.BossGateTile = -1;
            run.Phase = RunPhase.Event; run.EventId = eventId; run.EventTile = 1;
            _f.App.Router.Push(new RunScreen());
            return (RunScreen)_f.App.Router.Current;
        }

        [Test]
        public void ChanceEvent_ShowsTheChanceRoll_AndAResultScreen()
        {
            var screen = EventOnScreen("abandoned_cache");
            Button search = null;
            foreach (var b in _f.App.ModalLayer.GetComponentsInChildren<Button>(true)) if (b.name.StartsWith("Button:Search it")) search = b;
            Assert.That(search, Is.Not.Null, "event option");
            Assert.That(Texts(_f.App.ModalLayer), Does.Contain("account loot"), "the risk is spelled out");
            search.onClick.Invoke();
            if (!screen.OutcomeOpen) screen.Rebuild();
            Assert.That(screen.OutcomeOpen, Is.True, "result screen after the choice");
            var o = S.Run.Active.LastOutcome;
            var sheet = FindNamed(_f.App.ModalLayer, "Modal:Outcome");
            Assert.That(sheet, Is.Not.Null);
            string texts = Texts(sheet);
            Assert.That(FindNamed(sheet, "ChanceMeter"), Is.Not.Null, "chance indicator");
            Assert.That(texts, Does.Contain("65% chance"));
            Assert.That(texts, Does.Contain(o.Success ? "SUCCESS!" : "NO LUCK"));
            Assert.That(texts, Does.Contain("EVENT RESULT"));
            foreach (var l in o.Lines) Assert.That(texts, Does.Contain(RunScreen.OutcomeLineText(l, C)), l.Kind.ToString());
            if (o.Success) Assert.That(texts, Does.Contain("account loot, paid when you beat the boss"));
            else Assert.That(texts, Does.Contain("−5% max HP for the run"));
            Assert.That(FindNamed(sheet, "Icon"), Is.Not.Null, "icons per line");

            FindNamed(sheet, "OutcomeContinue").GetComponent<Button>().onClick.Invoke();
            Assert.That(screen.OutcomeOpen, Is.False);
            screen.Rebuild();
            Assert.That(screen.OutcomeOpen, Is.False, "shown once");
        }

        [Test]
        public void Campfire_And_GuardedChest_ResultsListWhatWasGranted()
        {
            var screen = EventOnScreen("treasure_pod");
            S.Run.Active.WormHp = 1e9; // the guardian fight is not under test
            Assert.That(_f.Service.ExecuteNow(new RunChooseEventOption { Index = 0 }).Ok, Is.True);
            while (_f.App.Router.Current is BattlePlaybackScreen bp) { bp.PlayAll(); _f.App.Router.Pop(); }
            screen.Rebuild();
            Assert.That(screen.OutcomeOpen, Is.True);
            string texts = Texts(FindNamed(_f.App.ModalLayer, "Modal:Outcome"));
            Assert.That(texts, Does.Contain("CHEST RESULT"));
            Assert.That(texts, Does.Contain("Fight"));
            Assert.That(FindNamed(_f.App.ModalLayer, "ChanceMeter"), Is.Null, "no chance roll without chance");
        }

        [Test]
        public void AutoMode_PassesTheResultScreen_AfterAShortLook()
        {
            var screen = EventOnScreen("campfire");
            screen.StartAuto();
            Assert.That(screen.AutoPaused, Is.True, "a campfire is a player decision");
            S.Run.Active.WormHp = S.Run.Active.WormMaxHp / 2;
            Assert.That(_f.Service.ExecuteNow(new RunChooseEventOption { Index = 0 }).Ok, Is.True);
            screen.Rebuild();
            Assert.That(screen.OutcomeOpen, Is.True);
            Assert.That(Texts(FindNamed(_f.App.ModalLayer, "Modal:Outcome")), Does.Contain("CAMPFIRE RESULT"));
            long rolls = S.Counter("run_rolls");
            screen.Frame(RunScreen.AutoOutcomeSeconds / 2);
            Assert.That(screen.OutcomeOpen, Is.True, "shown for a moment");
            Assert.That(S.Counter("run_rolls"), Is.EqualTo(rolls), "auto waits while the result is shown");
            screen.Frame(RunScreen.AutoOutcomeSeconds);
            Assert.That(screen.OutcomeOpen, Is.False, "auto passes the result");
            screen.Frame(1f);
            Assert.That(S.Counter("run_rolls"), Is.EqualTo(rolls + 1), "and rolls on");
        }

        [Test]
        public void OutcomeLines_ReadAsGainsAndLosses()
        {
            Assert.That(RunScreen.OutcomeLineText(new OutcomeLine(OutcomeKind.Coins, null, -25, false), C), Is.EqualTo("−25 coins"));
            Assert.That(RunScreen.OutcomeLineText(new OutcomeLine(OutcomeKind.Stat, "Atk", 8, true), C), Is.EqualTo("+8% ATK for the run"));
            Assert.That(RunScreen.OutcomeLineText(new OutcomeLine(OutcomeKind.AwakenRate, null, 15, false), C), Is.EqualTo("Boss awakens 15% faster for the rest of the run"));
            Assert.That(RunScreen.OutcomeLineText(new OutcomeLine(OutcomeKind.Battle, "won", 1, true), C), Is.EqualTo("Fight won"));
            var loot = new OutcomeLine(OutcomeKind.AccountLoot, Currencies.Biomass, 120, true) { Reward = Reward.Cur(Currencies.Biomass, 120) };
            Assert.That(RunScreen.OutcomeLineText(loot, C), Does.EndWith("account loot, paid when you beat the boss"));
        }
    }
}
