using System.Collections.Generic;
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
    public sealed class RunAndProgressionScreenTests
    {
        private AppFixture _f;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private LevelDef FirstLevel()
        {
            PortalDef first = null;
            foreach (var p in _f.Service.Content.Portals.Values)
                if (p.Kind == PortalKind.Main && (first == null || p.Order < first.Order)) first = p;
            return _f.Service.Content.RunLevel(first.LevelIds[0]);
        }

        private void StartFirstRun()
        {
            var level = FirstLevel();
            var r = _f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(r.Ok, Is.True, r.Error);
        }

        /// <summary>Drive the current run with RunAutoStep, closing battle playbacks as they appear.</summary>
        private int AutoToEnd(int max = 3000)
        {
            int steps = 0;
            while (_f.Service.State.Run.Active != null && steps < max)
            {
                var r = _f.Service.ExecuteNow(new RunAutoStep());
                Assert.That(r.Ok, Is.True, r.Error);
                steps++;
                CloseBattles();
            }
            CloseBattles();
            return steps;
        }

        private int CloseBattles()
        {
            int n = 0;
            while (_f.App.Router.Current is BattlePlaybackScreen b)
            {
                b.PlayAll();
                Assert.That(b.Finished, Is.True);
                _f.App.Router.Pop();
                n++;
            }
            return n;
        }

        [Test]
        public void NewPlayer_CanPlayFirstRunEndToEnd_ThroughTheRunScreen()
        {
            _f.App.Open("portals");
            var level = FirstLevel();
            var portals = _f.App.Router.Current;
            bool found = false;
            foreach (var b in portals.Root.GetComponentsInChildren<Button>(true))
                if (b.name == "Level:" + level.Id) { found = b.interactable; break; }
            Assert.That(found, Is.True, "first level is open for a new player");

            StartFirstRun();
            _f.App.Router.Push(new RunScreen());
            var run = _f.App.Router.Current as RunScreen;
            Assert.That(run, Is.Not.Null);
            Assert.That(run.Root.GetComponentsInChildren<Image>(true).Length, Is.GreaterThan(20), "board drawn");

            int steps = AutoToEnd();
            Assert.That(_f.Service.State.Run.Active, Is.Null, "run finished in " + steps + " steps");
            Assert.That(_f.Service.State.Run.LastSummary, Is.Not.Null);
            // the run screen moves on to the result screen
            for (int i = 0; i < 3 && !(_f.App.Router.Current is RunResultScreen); i++) _f.App.Router.Current.Frame(0.1f);
            Assert.That(_f.App.Router.Current, Is.InstanceOf<RunResultScreen>());
            Assert.That(_f.Service.State.Counter("run_rolls"), Is.GreaterThan(0));
            // tutorial steps advance automatically from what the player did
            Assert.That(_f.Service.State.Onboarding.TutorialDone, Does.Contain("first_roll"));
        }

        [Test]
        public void RunScreen_ShowsDecisionModalsForEveryPhaseSeen()
        {
            StartFirstRun();
            _f.App.Router.Push(new RunScreen());
            var phases = new HashSet<RunPhase>();
            for (int i = 0; i < 3000 && _f.Service.State.Run.Active != null; i++)
            {
                var active = _f.Service.State.Run.Active;
                phases.Add(active.Phase);
                if (active.Phase != RunPhase.AwaitRoll && _f.App.Router.Current is RunScreen)
                    Assert.That(_f.App.ModalLayer.childCount, Is.GreaterThan(0), "decision modal for " + active.Phase);
                _f.Service.ExecuteNow(new RunAutoStep());
                CloseBattles();
            }
            Assert.That(phases, Does.Contain(RunPhase.AwaitRoll));
        }

        [Test]
        public void BoardLayout_TilesNeverOverlap()
        {
            var content = _f.Service.Content;
            foreach (var level in content.RunLevels.Values)
            {
                if (content.Portal(level.PortalId)?.Kind != PortalKind.Main) continue;
                for (int seed = 1; seed <= 5; seed++)
                {
                    var board = RunBoard.Generate(content, level, Rng.FromSeed(seed * 977 + level.Index));
                    var seen = new Dictionary<Vector2, int>();
                    foreach (var t in board.Tiles)
                    {
                        var c = RunScreen.TileCenter(t);
                        Assert.That(seen.ContainsKey(c), Is.False, level.Id + " tiles " + t.Id + " and " + (seen.ContainsKey(c) ? seen[c] : -1) + " overlap");
                        seen[c] = t.Id;
                        Assert.That(c.x, Is.InRange(0f, RunScreen.Columns * RunScreen.Cell + 60));
                    }
                }
            }
        }

        [Test]
        public void BossMarker_NeverOverlapsATile()
        {
            var content = _f.Service.Content;
            foreach (var level in content.RunLevels.Values)
            {
                if (content.Portal(level.PortalId)?.Kind != PortalKind.Main) continue;
                for (int seed = 1; seed <= 5; seed++)
                {
                    var board = RunBoard.Generate(content, level, Rng.FromSeed(seed * 977 + level.Index));
                    var size = new Vector2(RunScreen.Columns * RunScreen.Cell + 60, RunScreen.BoardHeight(board.Tiles));
                    var p = RunScreen.BossMarkerCenter(board.Tiles, board.GateTile, size);
                    var marker = new Rect(p - RunScreen.BossMarkerSize / 2, RunScreen.BossMarkerSize);
                    Assert.That(marker.xMin >= 0 && marker.yMin >= 0 && marker.xMax <= size.x && marker.yMax <= size.y, Is.True, level.Id + " marker on the board");
                    foreach (var t in board.Tiles)
                        Assert.That(RunScreen.TileRect(t, t.Id == board.GateTile).Overlaps(marker), Is.False, level.Id + " seed " + seed + ": marker overlaps tile " + t.Id);
                }
            }
        }

        [Test]
        public void RunScreen_ToastsUseAReservedStripAboveTheActionBar_AtMostTwo()
        {
            StartFirstRun();
            _f.App.Router.Push(new RunScreen());
            var run = (RunScreen)_f.App.Router.Current;
            _f.App.Update(0.01f);
            for (int i = 0; i < 5; i++) _f.App.Toasts.Info("Toast " + i);
            Assert.That(_f.App.Toasts.Count, Is.EqualTo(ToastCenter.MaxVisible));
            Assert.That(_f.App.Toasts.LastText, Is.EqualTo("Toast 4"));
            // The strip starts just above the fixed action bar and the scrolling body (board) ends above the strip.
            Assert.That(_f.App.Toasts.Stack.anchoredPosition.y, Is.EqualTo(run.FooterHeight + 4).Within(0.01f));
            Assert.That(run.BodyFrame.offsetMin.y, Is.GreaterThanOrEqualTo(run.FooterHeight + ToastCenter.StripHeight));
            Assert.That(run.Root.Find("Footer").GetComponentsInChildren<Button>(true).Length, Is.GreaterThanOrEqualTo(4), "ROLL/Auto/Decision/Abandon in the bar");
            // Short-lived.
            _f.App.Update(ToastCenter.Lifetime + 0.1f);
            Assert.That(_f.App.Toasts.Count, Is.EqualTo(0));
            // Other screens: back to the default inset.
            _f.App.Router.Pop();
            _f.App.Update(0.01f);
            Assert.That(_f.App.Toasts.Stack.anchoredPosition.y, Is.EqualTo(ToastCenter.DefaultBottomInset).Within(0.01f));
        }

        [Test]
        public void EventToasts_ShowDisplayNames_NeverInternalIds()
        {
            _f.Service.DevGrantStarterKit();
            var state = _f.Service.State;
            string uid = null;
            foreach (var kv in state.Parts.Items) { uid = kv.Key; break; }
            Assert.That(uid, Is.Not.Null);
            var content = _f.Service.Content;
            var text = EventText.Describe(new GameEvent { Type = "part_equipped", Args = new List<string> { "LeftArm", uid } }, content, state);
            Assert.That(text, Does.Not.Contain(uid));
            Assert.That(text, Does.Contain(PartsScreen.PartName(content, state.Parts.Items[uid])));
            Assert.That(text, Does.Contain("left arm"));
            foreach (var type in new[] { "part_merged", "part_awakened", "run_rewards_doubled", "run_completed", "mail", "some_new_event" })
            {
                var t = EventText.Describe(new GameEvent { Type = type, Args = new List<string> { uid, "2", "x" } }, content, state);
                if (t != null) Assert.That(t, Does.Not.Contain(uid), type);
            }
        }

        [Test]
        public void RunResult_DoubleRewards_WatchesAnAdAndDoublesOnce()
        {
            _f.Service.DevGrantStarterKit();
            for (int attempt = 0; attempt < 6 && !(_f.Service.State.Run.LastSummary?.Won ?? false); attempt++)
            {
                StartFirstRun();
                AutoToEnd();
            }
            var summary = _f.Service.State.Run.LastSummary;
            Assert.That(summary.Won, Is.True, "a run of the first level is won");
            _f.App.Router.Reset(new HubScreen());
            _f.App.Router.Push(new RunResultScreen());
            Button btn = null;
            foreach (var b in _f.App.Router.Current.Root.GetComponentsInChildren<Button>(true)) if (b.name == "DoubleRewards") btn = b;
            Assert.That(btn, Is.Not.Null, "double rewards button");
            Assert.That(btn.interactable, Is.True);
            long bio = _f.Service.State.Wallet.Get(Currencies.Biomass);
            btn.onClick.Invoke();
            Assert.That(_f.Service.State.Run.LastSummary.Doubled, Is.True);
            Assert.That(_f.Service.State.Wallet.Get(Currencies.Biomass), Is.GreaterThan(bio));
            _f.App.Router.Current.Rebuild();
            foreach (var b in _f.App.Router.Current.Root.GetComponentsInChildren<Button>(true)) Assert.That(b.name, Is.Not.EqualTo("DoubleRewards"));
        }

        [Test]
        public void BattlePlayback_EndsWithFightersAtTheirFinalHp()
        {
            StartFirstRun();
            BattleResult battle = null;
            for (int i = 0; i < 3000 && battle == null && _f.Service.State.Run.Active != null; i++)
            {
                _f.Service.ExecuteNow(new RunAutoStep());
                battle = _f.Service.State.Run.Active?.LastBattle ?? _f.Service.State.Run.LastSummary?.LastBattle;
            }
            Assert.That(battle, Is.Not.Null, "a battle happened");
            _f.App.Router.Reset(new HubScreen());
            var screen = new BattlePlaybackScreen(battle);
            _f.App.Router.Push(screen);
            screen.PlaySteps(3);
            screen.PlayAll();
            Assert.That(screen.Finished, Is.True);
            string expected = battle.PlayerWon() ? "VICTORY" : battle.EndReason == BattleEndReason.RoundCap ? "TIME UP" : "DEFEAT";
            bool bannerOk = false;
            foreach (var t in screen.Root.GetComponentsInChildren<Text>(true)) if (t.text == expected) bannerOk = true;
            Assert.That(bannerOk, Is.True);
            var fighter = battle.Fighters[0];
            bool hpOk = false;
            foreach (var t in screen.Root.GetComponentsInChildren<Text>(true)) if (t.text == Fmt.Num(System.Math.Max(0, fighter.EndHp)) + " / " + Fmt.Num(fighter.MaxHp)) hpOk = true;
            Assert.That(hpOk || !fighter.Alive, Is.True, "first fighter shows its end HP");
        }

        [Test]
        public void BattlePlayback_RageAndShieldBarsFollowTheLog()
        {
            var setup = new BattleSetup { RoundCap = 6 };
            var lo = new CombatLoadout { MountId = "skiff" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 5000)).Add(new StatMod(StatId.Atk, 50)).Add(new StatMod(StatId.Speed, 100));
            lo.MountStats.Add(new StatMod(StatId.Atk, 30)).Add(new StatMod(StatId.Hp, 200));
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, _f.Service.Content));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(_f.Service.Content, "cinder_hound", 3));
            var battle = BattleSimulator.Simulate(setup, _f.Service.Content, new Rng(7));
            Assert.That(battle.Log.Exists(e => e.Action == BattleAction.Rage), Is.True, "a rage attack happened");
            _f.App.Router.Reset(new HubScreen());
            var screen = new BattlePlaybackScreen(battle);
            _f.App.Router.Push(screen);
            int worm = battle.Fighters.FindIndex(f => f.Role == CombatRole.Worm);
            Assert.That(screen.TryGetBars(worm, out _, out double shield0, out _), Is.True);
            Assert.That(shield0, Is.EqualTo(battle.Fighters[worm].StartShield), "mount shield shown before the first entry");
            for (int i = 0; i < battle.Log.Count; i++)
            {
                screen.PlaySteps(1);
                var e = battle.Log[i];
                if (e.Actor >= 0 && e.Actor != e.Target)
                {
                    screen.TryGetBars(e.Actor, out _, out double s, out double r);
                    Assert.That(r, Is.EqualTo(e.ActorRage), "actor rage at entry " + i);
                    Assert.That(s, Is.EqualTo(e.ActorShield), "actor shield at entry " + i);
                }
                if (e.Target >= 0)
                {
                    screen.TryGetBars(e.Target, out double hp, out double s, out double r);
                    Assert.That(hp, Is.EqualTo(e.TargetHp), "target HP at entry " + i);
                    Assert.That(r, Is.EqualTo(e.TargetRage), "target rage at entry " + i);
                    Assert.That(s, Is.EqualTo(e.TargetShield), "target shield at entry " + i);
                }
            }
            Assert.That(screen.Finished, Is.True);
        }

        [Test]
        public void Signals_PullShowsRevealCards()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.Service.DevGrantStarterKit();
            _f.App.Open("signals");
            var screen = (SignalsScreen)_f.App.Router.Current;
            var r = _f.Service.ExecuteNow(new GachaPull { PullType = GachaType.Part, Count = 10, Payment = GachaPayment.Tokens });
            Assert.That(r.Ok, Is.True, r.Error);
            var modal = screen.Reveal((GachaPullResult)r.Payload);
            int cards = 0;
            foreach (Transform t in modal.Body.GetChild(0)) cards++;
            Assert.That(cards, Is.EqualTo(10));
        }

        [Test]
        public void Parts_MergeFinderPicksValidMaterials()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.Service.DevGrantStarterKit();
            _f.App.Open("parts");
            var screen = (PartsScreen)_f.App.Router.Current;
            int merged = 0;
            foreach (var uid in PartsSystem.SortedPartUids(_f.Service.State))
            {
                var p = PartsSystem.Get(_f.Service.State, uid);
                if (p == null) continue;
                var mats = screen.FindMergeMaterials(p, out _);
                if (mats == null) continue;
                var cmd = new PartMerge { TargetUid = uid };
                cmd.MaterialUids.AddRange(mats);
                var r = _f.Service.ExecuteNow(cmd);
                Assert.That(r.Ok, Is.True, r.Error);
                merged++;
                break;
            }
            Assert.That(merged, Is.EqualTo(1), "three common copies merge");
        }

        [Test]
        public void ProgressionScreens_ActionsWork()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.Service.DevGrantStarterKit();
            foreach (var id in new[] { "worm", "parts", "heroes", "mounts", "laboratory", "signals", "portals" })
            {
                _f.App.Open(id);
                var screen = _f.App.Router.Current;
                Assert.That(screen.Root.GetComponentsInChildren<Button>(true).Length, Is.GreaterThan(2), id);
                _f.App.Router.Pop();
            }
            // Equip via the worm slot modal and level up the worm.
            _f.App.Open("worm");
            Button slot = null;
            foreach (var b in _f.App.Router.Current.Root.GetComponentsInChildren<Button>(true)) if (b.name == "Slot:LeftArm") slot = b;
            Assert.That(slot, Is.Not.Null);
            slot.onClick.Invoke();
            Assert.That(_f.App.ModalLayer.childCount, Is.EqualTo(1));
            long before = _f.Service.State.Worm.Level;
            Assert.That(_f.Service.ExecuteNow(new WormLevelUp { Levels = 1 }).Ok, Is.True);
            Assert.That(_f.Service.State.Worm.Level, Is.EqualTo(before + 1));
            // Hero unlock + equip from shards.
            string hero = null;
            foreach (var h in _f.Service.Content.HeroConfig.Defs.Keys) if (_f.Service.State.Wallet.Get(Currencies.HeroShards(h)) > 0) hero = h;
            Assert.That(_f.Service.ExecuteNow(new HeroUnlock { HeroId = hero }).Ok, Is.True);
            Assert.That(_f.Service.ExecuteNow(new HeroEquip { HeroId = hero }).Ok, Is.True);
        }
    }
}
