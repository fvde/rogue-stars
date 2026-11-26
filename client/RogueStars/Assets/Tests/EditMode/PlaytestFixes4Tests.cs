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
    /// Playtest fixes 4: the one cycling speed button (x0.5 / x1 / x2 / x3) in battle and on the board, the worm turning to its
    /// target (battle) and its movement (board), the hub pass label and the pause cog.
    /// </summary>
    public sealed class PlaytestFixes4Tests
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

        private static BattleResult Battle(params (int actor, int target, BattleAction action)[] steps)
        {
            var b = new BattleResult { Winner = BattleSide.Player, Rounds = 2 };
            b.Fighters.Add(new BattleFighterInfo { Index = 0, Name = "Worm", Role = CombatRole.Worm, Side = BattleSide.Player, Targetable = true, MaxHp = 1000, StartHp = 1000, EndHp = 1000, Alive = true });
            b.Fighters.Add(new BattleFighterInfo { Index = 1, Name = "Buddy", Id = "x", Role = CombatRole.Alien, Side = BattleSide.Player, Targetable = true, MaxHp = 500, StartHp = 500, EndHp = 500, Alive = true });
            b.Fighters.Add(new BattleFighterInfo { Index = 2, Name = "Hound", Role = CombatRole.Enemy, Side = BattleSide.Enemy, Targetable = true, MaxHp = 500, StartHp = 500, EndHp = 0, Alive = false });
            foreach (var (actor, target, action) in steps)
                b.Log.Add(new BattleLogEntry { Round = 1, Actor = actor, Target = target, Action = action, Amount = 10, TargetHp = 400 });
            return b;
        }

        // ---- speed ----------------------------------------------------------------------------------

        [Test]
        public void PlaySpeed_CyclesHalfOneTwoThree_AndSkipsThreeWithoutTheRequirement()
        {
            Assert.That(PlaySpeed.Options, Is.EqualTo(new[] { 0.5f, 1f, 2f, 3f }));
            float s = 0.5f; var seen = new List<float>();
            for (int i = 0; i < 5; i++) { s = PlaySpeed.Next(s, true); seen.Add(s); }
            Assert.That(seen, Is.EqualTo(new[] { 1f, 2f, 3f, 0.5f, 1f }));
            s = 0.5f; seen.Clear();
            for (int i = 0; i < 4; i++) { s = PlaySpeed.Next(s, false); seen.Add(s); }
            Assert.That(seen, Is.EqualTo(new[] { 1f, 2f, 0.5f, 1f }), "x3 is skipped without the pass");
            Assert.That(PlaySpeed.Next(3f, false), Is.EqualTo(0.5f), "a stored x3 without the pass moves on to x0.5");
            Assert.That(PlaySpeed.Valid(3f, false), Is.EqualTo(2f));
            Assert.That(PlaySpeed.Valid(7f, true), Is.EqualTo(1f));
            Assert.That(new[] { 0.5f, 1f, 2f, 3f }.Select(PlaySpeed.Label), Is.EqualTo(new[] { "x0.5", "x1", "x2", "x3" }));
        }

        [Test]
        public void Battle_FollowsTheOneSpeedSetting_AndCyclesThroughIt()
        {
            // playtest 0.1.308: the battle has no speed button of its own; it plays at the board's speed (PlaySpeed.Current)
            var screen = new BattlePlaybackScreen(Battle((0, 2, BattleAction.Basic)));
            _f.App.Router.Push(screen);
            Assert.That(Find(screen.Root, "SpeedChip"), Is.Null);
            bool passUser = ShopSystem.HasBattleSpeed3(S, C, _f.Clock.NowMs);
            var seen = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                seen.Add(screen.SpeedLabel);
                Assert.That(screen.SpeedLabel, Is.EqualTo(PlaySpeed.Label(screen.Speed)));
                Assert.That(screen.Speed, Is.EqualTo(PlaySpeed.Allowed(passUser)), "the battle follows the shared setting");
                screen.CycleSpeed();
            }
            Assert.That(seen.All(l => l == "x0.5" || l == "x1" || l == "x2" || l == "x3"));
            Assert.That(seen.Contains("x3"), Is.EqualTo(passUser), "x3 only with the pass requirement");
        }

        [Test]
        public void Battle_HalfSpeed_PlaysTwiceAsSlow_WithoutStallingTheLog()
        {
            var battle = Battle((0, 2, BattleAction.Basic), (2, 0, BattleAction.Basic), (0, 2, BattleAction.Basic), (0, 1, BattleAction.Skill));
            var screen = new BattlePlaybackScreen(battle);
            _f.App.Router.Push(screen);
            while (screen.Speed != 1f) screen.CycleSpeed();
            float step1 = screen.StepSeconds, fire1 = screen.FireSeconds;
            while (screen.Speed != 0.5f) screen.CycleSpeed();
            Assert.That(screen.StepSeconds, Is.EqualTo(step1 * 2).Within(1e-4f), "a log step takes twice as long");
            Assert.That(screen.FireSeconds, Is.EqualTo(fire1 * 2).Within(1e-4f), "a skill fire too: same step-to-fire ratio as x1");
            // frames of exactly one step each advance the playback one entry at a time and it ends
            int frames = 0;
            while (!screen.Finished && frames++ < 100) screen.Frame(screen.StepSeconds);
            Assert.That(screen.Finished, Is.True);
            Assert.That(frames, Is.EqualTo(battle.Log.Count));
            for (int i = 0; i < 6; i++) screen.Frame(1f); // the fire queue drains at x0.5 too
            Assert.That(screen.FireQueue.Busy, Is.False);
            while (screen.Speed != 1f) screen.CycleSpeed();
        }

        [Test]
        public void Board_SpeedChip_CyclesThroughHalfSpeed_AndShowsIt()
        {
            StartRun();
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            var seen = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                var chip = Find(screen.Root, "SpeedChip");
                Assert.That(Find(chip, "SpeedText").GetComponent<Text>().text, Is.EqualTo(PlaySpeed.Label(RunScreen.BoardSpeed)));
                seen.Add(PlaySpeed.Label(RunScreen.BoardSpeed));
                chip.GetComponent<Button>().onClick.Invoke();
            }
            bool pass = ShopSystem.HasBattleSpeed3(S, C, _f.Clock.NowMs);
            Assert.That(seen, Is.EqualTo(pass ? new[] { "x1", "x2", "x3", "x0.5" } : new[] { "x1", "x2", "x0.5", "x1" }), "x3 with the monthly pass (one setting with battles)");
            while (RunScreen.BoardSpeed != 1f) Find(screen.Root, "SpeedChip").GetComponent<Button>().onClick.Invoke();
            Assert.That(RunScreen.BoardSpeed, Is.EqualTo(1f), "and back to x1");
        }

        [Test]
        public void Board_HalfSpeed_RollAndHopsStillComplete()
        {
            StartRun();
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            RunScreen.BoardSpeed = 0.5f;
            int rolls = S.Run.Active.Rolls;
            Assert.That(_f.Service.ExecuteNow(new RunRoll()).Ok, Is.True);
            screen.Rebuild();
            Assert.That(S.Run.Active == null || S.Run.Active.Rolls > rolls, Is.True);
            Assert.That(screen.Animating, Is.False, "sequences run to the end at x0.5 (EditMode completes them at once)");
        }

        // ---- facing ---------------------------------------------------------------------------------

        [Test]
        public void WormFacing_TheArtFacesLeft_SoItIsMirroredWhileLookingRight()
        {
            Assert.That(WormFacing.Flip(-80, 300, false), Is.True, "enemy on the right: mirrored");
            Assert.That(WormFacing.Flip(300, -80, true), Is.False, "target on the left: as drawn");
            Assert.That(WormFacing.Flip(10, 10.001f, true), Is.True, "no horizontal movement keeps the facing");
            Assert.That(WormFacing.Flip(10, 10f, false), Is.False);
            Assert.That(WormFacing.ScaleX(true), Is.EqualTo(-1f));
            Assert.That(WormFacing.ScaleX(false), Is.EqualTo(1f));
        }

        [Test]
        public void Battle_TheWormFacesItsEnemies_AndTurnsToATargetOnItsOtherSide()
        {
            Assert.That(ArtCatalog.Hero(), Is.Not.Null, "the hero art is imported");
            var screen = new BattlePlaybackScreen(Battle((0, 2, BattleAction.Basic), (0, 1, BattleAction.Skill), (0, 2, BattleAction.Basic)));
            _f.App.Router.Push(screen);
            var worm = (RectTransform)Find(screen.Root, "Fighter:0").Find("Sprite");
            Assert.That(screen.WormFlipped, Is.True, "enemies stand on the right: the left-facing art is mirrored from the start");
            Assert.That(worm.localScale.x, Is.EqualTo(-1f));
            Assert.That(worm.anchoredPosition, Is.EqualTo(new Vector2(0, BattlePlaybackScreen.SpriteBase)), "ground contact untouched");
            screen.PlaySteps(1);
            Assert.That(screen.WormFlipped, Is.True, "attacking the enemy on the right");
            screen.PlaySteps(1);
            Assert.That(screen.WormFlipped, Is.False, "a target on its left (a sidekick): it turns that way");
            Assert.That(worm.localScale.x, Is.EqualTo(1f));
            screen.PlaySteps(1);
            Assert.That(screen.WormFlipped, Is.True, "and back to the enemy");
            Assert.That(worm.anchoredPosition, Is.EqualTo(new Vector2(0, BattlePlaybackScreen.SpriteBase)));
        }

        [Test]
        public void Battle_WormOnItsMount_TurnsWithTheMountUnderIt()
        {
            var b = Battle((0, 2, BattleAction.Basic));
            b.Fighters.Add(new BattleFighterInfo { Index = 3, Name = "Skiff", Id = "skiff", Role = CombatRole.Mount, Side = BattleSide.Player, Targetable = false, MaxHp = 1, StartHp = 1, EndHp = 1, Alive = true });
            var screen = new BattlePlaybackScreen(b);
            _f.App.Router.Push(screen);
            var worm = Find(screen.Root, "Fighter:0").Find("Sprite");
            Assert.That(worm.Find("Mount"), Is.Not.Null, "the mount is a child of the worm's sprite, so it is mirrored with it");
            Assert.That(screen.WormFlipped, Is.True);
        }

        [Test]
        public void Board_TheWormLooksWhereItMoves_WithoutLeavingItsTile()
        {
            StartRun();
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            var world = screen.World;
            Assert.That(world.TokenFlip, Is.False, "starts as drawn");
            var sr = Find(world.Token, "Worm").GetComponentInChildren<SpriteRenderer>();
            var before = sr.transform.localPosition;
            world.FaceToken(0, 1);
            Assert.That(world.TokenFlip, Is.True);
            Assert.That(sr.flipX, Is.True);
            Assert.That(Mathf.Abs(sr.transform.localPosition.y - before.y), Is.LessThan(1e-4f), "feet stay on the ground");
            world.FaceToken(1, 1);
            Assert.That(world.TokenFlip, Is.True, "a vertical move keeps the facing");
            world.FaceToken(1, 0);
            Assert.That(world.TokenFlip, Is.False);
            Assert.That((sr.transform.localPosition - before).magnitude, Is.LessThan(1e-4f), "and back exactly");
        }

        [Test]
        public void Board_AfterARoll_TheWormFacesItsLastHorizontalHop()
        {
            StartRun();
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            int seen = S.Run.Active.Trail.Count;
            Assert.That(_f.Service.ExecuteNow(new RunRoll()).Ok, Is.True);
            screen.Rebuild();
            var run = S.Run.Active;
            bool expect = false;
            for (int i = Mathf.Max(1, seen); i < run.Trail.Count; i++)
                expect = WormFacing.Flip(WorldBoard.TopOf(run.Tiles[run.Trail[i - 1]]).x, WorldBoard.TopOf(run.Tiles[run.Trail[i]]).x, expect);
            Assert.That(screen.World.TokenFlip, Is.EqualTo(expect));
            Assert.That(screen.World.TokenFlip, Is.EqualTo(expect), "kept across the rebuild");
        }

        // ---- hub and HUD ----------------------------------------------------------------------------

        [Test]
        public void Hub_PassIcon_ReadsPass_WithASmallLevelBadge_NotT0()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.App.Router.Reset(new HubScreen());
            var hub = (HubScreen)_f.App.Router.Current;
            hub.Rebuild();
            var side = Find(hub.Root, "Side:pass");
            Assert.That(side, Is.Not.Null);
            Assert.That(side.Find("Label").GetComponent<Text>().text, Is.EqualTo("Pass"));
            var tier = side.Find("Timer").GetComponentInChildren<Text>(true).text;
            Assert.That(tier, Is.EqualTo("Lv " + ShopSystem.SeasonLevel(S, C)));
            Assert.That(string.Join("|", side.GetComponentsInChildren<Text>(true).Select(t => t.text)), Does.Not.Contain("T0"));
        }

        [Test]
        public void RunHud_PauseCog_HasADarkHubOverItsWhiteHole_AndIsLargerThanBefore()
        {
            StartRun();
            _f.App.Router.Push(new RunScreen());
            var screen = _f.App.Router.Current;
            var cog = Find(screen.Root, "PauseButton");
            Assert.That(cog, Is.Not.Null);
            var face = cog.Find("Face").GetComponent<Image>();
            Assert.That(face.sprite, Is.SameAs(HudArt.Icon("cog")));
            Assert.That(face.color, Is.EqualTo(SliceHud.CogTint), "tinted grey, no longer pure white");
            Assert.That(face.transform.Find("CogHub"), Is.Not.Null, "the art's opaque white hole is covered");
            Assert.That(SliceHud.CogSize, Is.GreaterThan(72f));
        }

        private void StartRun()
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
        }
    }
}
