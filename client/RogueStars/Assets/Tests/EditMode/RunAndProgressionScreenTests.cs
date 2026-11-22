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
