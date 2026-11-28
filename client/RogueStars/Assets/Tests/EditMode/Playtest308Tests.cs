using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Owner playtest 0.1.308: one speed setting for the board and battles (the board's button, no battle button), red tiles
    /// always cost the player, a won battle returns to the board by itself, 3 shop offers, minibosses on the path (bigger
    /// marker, always a "!", two skull pips on the rail, a compact encounter banner) and the battle's "NEXT" strip on a plate.
    /// </summary>
    public sealed class Playtest308Tests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; RunFx.Instant = true; RunScreen.BoardSpeed = 1; }

        [TearDown]
        public void TearDown()
        {
            RunScreen.InstantOutcomes = false; RunFx.Instant = false; RunScreen.BoardSpeed = 1;
            _f.Dispose();
        }

        private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        private ActiveRun StartRun(params string[] tiles)
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            var run = S.Run.Active;
            if (tiles.Length == 0) return run;
            run.Tiles.Clear();
            for (int i = 0; i <= 30; i++)
            {
                var t = new RunTile { Id = i, TileId = i == 0 ? "start" : i - 1 < tiles.Length ? tiles[i - 1] : "empty", X = i };
                if (i < 30) t.Next.Add(i + 1);
                run.Tiles.Add(t);
            }
            run.BoardEnd = 30; run.BossGateTile = -1; run.Position = 0; run.Trail.Clear(); run.Trail.Add(0);
            return run;
        }

        private static BattleResult Battle(bool won)
        {
            var b = new BattleResult { Winner = won ? BattleSide.Player : BattleSide.Enemy, Rounds = 1 };
            b.Fighters.Add(new BattleFighterInfo { Index = 0, Name = "Worm", Role = CombatRole.Worm, Side = BattleSide.Player, Targetable = true, MaxHp = 1000, StartHp = 1000, EndHp = won ? 1000 : 0, Alive = won });
            b.Fighters.Add(new BattleFighterInfo { Index = 1, Name = "Hound", Role = CombatRole.Enemy, Side = BattleSide.Enemy, Targetable = true, MaxHp = 500, StartHp = 500, EndHp = won ? 0 : 500, Alive = !won });
            b.Log.Add(new BattleLogEntry { Round = 1, Actor = won ? 0 : 1, Target = won ? 1 : 0, Action = BattleAction.Basic, Amount = 10, TargetHp = 400 });
            return b;
        }

        // ---- 1. one speed setting ---------------------------------------------------------------------------------

        [Test]
        public void Speed_OneSetting_TheBoardButtonSetsTheBattleSpeedToo_AndTheBattleButtonCyclesTheSameSetting()
        {
            StartRun();
            _f.App.Router.Push(new RunScreen());
            var board = (RunScreen)_f.App.Router.Current;
            var chip = Find(board.Root, "SpeedChip");
            Assert.That(chip, Is.Not.Null, "the board keeps the one speed button");
            while (!Mathf.Approximately(RunScreen.BoardSpeed, 0.5f)) Find(board.Root, "SpeedChip").GetComponent<Button>().onClick.Invoke();
            Assert.That(PlaySpeed.Current, Is.EqualTo(0.5f), "the board button changes THE speed");
            Assert.That(PlayerPrefs.GetFloat(PlaySpeed.PrefKey, -1), Is.EqualTo(0.5f), "kept under one key");

            var battle = new BattlePlaybackScreen(Battle(true), new BattleHudContext { InRun = true });
            _f.App.Router.Push(battle);
            Assert.That(battle.Speed, Is.EqualTo(0.5f), "the battle plays at the board's speed");
            // the battle board's speed button (bottom left) cycles the same shared setting, never a battle-only speed
            var button = Find(battle.Root, "SpeedButton");
            Assert.That(button, Is.Not.Null);
            Assert.That(Find(button, "SpeedText").GetComponent<Text>().text, Is.EqualTo("x0.5"));
            button.GetComponent<Button>().onClick.Invoke();
            Assert.That(PlaySpeed.Current, Is.EqualTo(battle.Speed), "one setting");
            Assert.That(Find(button, "SpeedText").GetComponent<Text>().text, Is.EqualTo(battle.SpeedLabel));
            while (!Mathf.Approximately(battle.Speed, 0.5f)) battle.CycleSpeed();
            float half = battle.StepSeconds;
            _f.App.Router.Pop();
            RunScreen.BoardSpeed = 2f;
            var battle2 = new BattlePlaybackScreen(Battle(true), new BattleHudContext { InRun = true });
            _f.App.Router.Push(battle2);
            Assert.That(battle2.Speed, Is.EqualTo(2f));
            Assert.That(battle2.StepSeconds, Is.EqualTo(half / 4).Within(1e-4f));
            Assert.That(battle2.SpeedLabel, Is.EqualTo("x2"));
        }

        // ---- 3. red tiles cost the player -------------------------------------------------------------------------

        [Test]
        public void RedTiles_AreCursesTrapsAndHarmfulBossTiles_AndNeverShowAGain()
        {
            Assert.That(IsoArt.IsRedFace(IsoArt.FaceOf(TileCategory.StatBuff)), Is.False, "a buff tile is not red");
            Assert.That(IsoArt.IsRedFace(IsoArt.FaceOf(TileCategory.StatDebuff)), Is.True);
            Assert.That(IsoArt.IsRedFace(IsoArt.FaceOf(TileCategory.Hazard)), Is.True);
            var boost = IsoArt.FaceColor(TileFace.Boost);
            Assert.That(boost.g, Is.GreaterThan(boost.r), "buff tiles are green");
            foreach (var f in new[] { TileFace.Curse, TileFace.Hazard, TileFace.Boss })
            {
                var col = IsoArt.FaceColor(f);
                Assert.That(col.r, Is.GreaterThan(col.g).And.GreaterThan(col.b), f + " is red");
            }
            int red = 0;
            foreach (var def in C.BoardTiles.Values)
            {
                var face = IsoArt.FaceOf(def);
                if (!IsoArt.IsRedFace(face) || def.Category == TileCategory.BossGate) continue;
                red++;
                var popups = RunPresentation.FromTileDef(def);
                Assert.That(popups.Any(p => p.Color == Theme.Good), Is.False, def.Id + ": a red tile shows a green gain");
                Assert.That(popups.Any(p => p.Color == Theme.Bad), Is.True, def.Id + ": a red tile shows a red loss");
                Assert.That(Theme.TileAccent(def), Is.Not.EqualTo(Theme.Good), def.Id);
            }
            Assert.That(red, Is.GreaterThanOrEqualTo(6), "curses, traps and the boss's harmful tiles");
            var cooling = C.BoardTiles.Values.First(d => d.Category == TileCategory.BossTile && d.MeterDelta < 0);
            Assert.That(IsoArt.FaceOf(cooling), Is.EqualTo(TileFace.Boost), cooling.Id + " helps: not red");
            Assert.That(Theme.TileAccent(cooling), Is.EqualTo(Theme.Good));
        }

        [Test]
        public void RedTile_Landing_ShowsOnlyRedLosses()
        {
            foreach (var id in new[] { "debuff_atk", "debuff_hp", "hazard_spikes", "hazard_toll" })
            {
                _f.Dispose(); _f = new AppFixture();
                var run = StartRun(Enumerable.Repeat(id, 20).ToArray());
                run.Coins = 200;
                Assert.That(_f.Service.ExecuteNow(new RunRoll()).Ok, Is.True);
                run = S.Run.Active;
                var tile = run.Tiles[run.Position];
                var popups = RunPresentation.Landing(C, run, tile, false, run.LastOutcome);
                Assert.That(popups, Is.Not.Empty, id);
                Assert.That(popups.Any(p => p.Color == Theme.Good || p.Color == Theme.Gold), Is.False, id + ": " + string.Join(", ", popups));
                Assert.That(popups.Any(p => p.Color == Theme.Bad), Is.True, id + ": " + string.Join(", ", popups));
                Assert.That(IsoArt.IsRedFace(IsoArt.FaceOf(C.BoardTiles[id])), Is.True, id + " is drawn red");
            }
        }

        // ---- 4. a won battle returns to the board -----------------------------------------------------------------

        [Test]
        public void WonRunBattle_ReturnsToTheBoardByItself_ALostOneKeepsContinue()
        {
            StartRun();
            _f.App.Router.Push(new RunScreen());
            var board = _f.App.Router.Current;
            var won = new BattlePlaybackScreen(Battle(true), new BattleHudContext { InRun = true });
            _f.App.Router.Push(won);
            won.PlayAll();
            Assert.That(won.AutoClosing, Is.True);
            Assert.That(Find(won.Root, "EndPanel").gameObject.activeSelf, Is.False, "no Continue gate after a win");
            _f.App.Update(BattlePlaybackScreen.WinBeatSeconds * 4);
            Assert.That(_f.App.Router.Current, Is.SameAs(board), "back on the board without a tap");

            var lost = new BattlePlaybackScreen(Battle(false), new BattleHudContext { InRun = true });
            _f.App.Router.Push(lost);
            lost.PlayAll();
            Assert.That(lost.AutoClosing, Is.False);
            Assert.That(Find(lost.Root, "EndPanel").gameObject.activeSelf, Is.True, "a defeat waits for Continue");
            for (int i = 0; i < 5; i++) _f.App.Update(1f);
            Assert.That(_f.App.Router.Current, Is.SameAs(lost));
            lost.Continue();
            Assert.That(_f.App.Router.Current, Is.SameAs(board));
        }

        // ---- 5. shop offers (playtest 0.1.325: 3 skills + the support offers) ---------------------------------------

        [Test]
        public void Shop_ShowsThreeSkillsAndAStatPerk_AndARerollKeepsTheSlots()
        {
            var run = StartRun("shop");
            run.Coins = 5000;
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            Assert.That(_f.Service.ExecuteNow(new RunRoll()).Ok, Is.True);
            screen.Rebuild();
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Shop));
            int Slots() => _f.App.ModalLayer.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("OfferSlot:"));
            Assert.That(S.Run.Active.Shop.Count, Is.EqualTo(4));
            Assert.That(S.Run.Active.Shop.Count(o => o.Kind == ShopOfferKind.Skill), Is.EqualTo(3));
            Assert.That(Slots(), Is.EqualTo(4), "three skills and a stat perk on screen (full HP: no heal)");
            var reroll = _f.App.ModalLayer.GetComponentsInChildren<Button>(true).First(b => b.name.StartsWith("Button:Reroll"));
            reroll.onClick.Invoke();
            Assert.That(S.Run.Active.Shop.Count, Is.EqualTo(4));
            Assert.That(Slots(), Is.EqualTo(4), "a reroll keeps them");
        }

        // ---- 6. minibosses on the path ----------------------------------------------------------------------------

        [Test]
        public void Minibosses_StandOutOnThePath_AndTheRailShowsTheRunsTwo()
        {
            Assert.That(IsoArt.MarkerHeight("miniboss"), Is.GreaterThan(IsoArt.MarkerHeight("elite") * 1.4f), "a big crowned skull");
            Assert.That(IsoArt.Marker("miniboss"), Is.Not.Null);

            var level = C.MainLevel(C.Campaign.MinibossesFromLevel);
            var run = new ActiveRun { LevelId = level.Id, Mode = RunMode.Normal };
            var clock = RunPresentation.BossClock(C, run);
            Assert.That(clock.Markers.Count, Is.EqualTo(2), "two skull pips: the run's two minibosses");
            Assert.That(clock.Markers.All(m => !m.Passed), Is.True);
            run.MinibossesDefeated = 1;
            clock = RunPresentation.BossClock(C, run);
            Assert.That(clock.Markers.Count(m => m.Passed), Is.EqualTo(1));
            Assert.That(RunPresentation.BossClock(C, new ActiveRun { LevelId = C.MainLevel(1).Id }).Markers, Is.Empty, "no minibosses yet on level 1");
        }

        [Test]
        public void MinibossAhead_ShowsItsMarker_WithoutAnExclaim()
        {
            StartRun("shop", "event", "event", "event", "event", "miniboss");
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            Assert.That(screen.World, Is.Not.Null);
            var tile = screen.World.Tiles[6].Go.transform;
            Assert.That(Find(tile, "Exclaim"), Is.Null, "no floating ! markers (owner, 2026-11-26)");
            Assert.That(Find(tile, "Marker:miniboss"), Is.Not.Null);
        }

        [Test]
        public void EncounterBanner_IsACompactStrip()
        {
            Assert.That(GameFeedback.Compact("run_encounter"), Is.True);
            Assert.That(GameFeedback.Compact("worm_level"), Is.False);
            _f.App.Feedback.OnEvents(new List<GameEvent> { new GameEvent { Type = "run_encounter", Args = new List<string> { "boss" } } });
            Assert.That(_f.App.Feedback.LastBanner, Is.EqualTo("BOSS"));
            Assert.That(_f.App.Feedback.LastBannerCompact, Is.True);
            var banner = Find(_f.App.Feedback.Layer, RevealBanner.Name) as RectTransform;
            Assert.That(banner, Is.Not.Null);
            Assert.That(banner.sizeDelta.y, Is.LessThanOrEqualTo(100), "a thin strip, not the big hurray banner");
            _f.App.Update(GameFeedback.CompactBannerLifetime + 0.1f);
            Assert.That(_f.App.Feedback.BannerCount, Is.EqualTo(0), "gone quickly");
        }

        // ---- 8. the "who acts next" strip on a solid plate --------------------------------------------------------

        [Test]
        public void TurnOrder_SitsOnASolidKitPlate()
        {
            var battle = new BattlePlaybackScreen(Battle(true), new BattleHudContext { InRun = true });
            _f.App.Router.Push(battle);
            var plate = Find(battle.Root, "TurnOrderPlate");
            var order = Find(battle.Root, "TurnOrder");
            Assert.That(plate, Is.Not.Null);
            Assert.That(order, Is.Not.Null);
            var img = plate.GetComponent<Image>();
            Assert.That(img.color.a, Is.GreaterThan(0.85f), "a near-opaque backing");
            Assert.That(plate.GetComponent<Shadow>(), Is.Not.Null, "with a drop shadow");
            Assert.That(plate.GetSiblingIndex(), Is.LessThan(order.GetSiblingIndex()), "behind the portraits");
            var pr = (RectTransform)plate; var or = (RectTransform)order;
            Assert.That(pr.rect.width, Is.GreaterThanOrEqualTo(or.rect.width));
            Assert.That(pr.rect.height, Is.GreaterThanOrEqualTo(or.rect.height));
            var caption = order.GetComponentsInChildren<Text>(true).First(t => t.text == "NEXT");
            Assert.That(caption.GetComponent<Outline>(), Is.Not.Null, "the NEXT label is outlined (Kit.Legible)");
        }
    }
}
