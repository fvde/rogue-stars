using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.Minigames;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Run mini-games (GDD §3.6): every game reaches every tier with scripted inputs on a fixed seed within 30 s of
    /// simulated time, draws its shapes, and the host plays a GAME tile end to end on the run screen (start → intro →
    /// play → medal → RunMinigameFinish → result screen; skip; skill roll → draft).
    /// </summary>
    public sealed class MinigameTests
    {
        private const int Seed = 7919;

        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (var id in ContentDb.Default.Minigames.Select(m => m.Id))
                foreach (MinigameTier tier in Enum.GetValues(typeof(MinigameTier)))
                    yield return new TestCaseData(id, tier).SetName("Minigame_" + id + "_" + tier);
        }

        [TestCaseSource(nameof(Cases))]
        public void ScriptedInputs_ReachTheTier_Within30Seconds(string id, MinigameTier aim)
        {
            var game = MinigameCatalog.Create(id, Seed);
            var shapes = new List<Shape>();
            int steps = 0;
            while (!game.Over && steps < 60 * 35)
            {
                game.Bot(aim);
                game.Step();
                if (steps % 20 == 0) { game.Draw(shapes); Assert.That(shapes.Count, Is.GreaterThan(5), id + " draws its scene"); }
                steps++;
            }
            Assert.That(game.Over, Is.True, id + " ends by itself");
            Assert.That(game.Time, Is.LessThanOrEqualTo(30.01f), id + " takes at most 30 s");
            Assert.That(game.Tier, Is.EqualTo(aim), id + " score " + game.Score + " (" + game.Status + ")");
        }

        [Test]
        public void Catalog_CoversEveryRulesGame_WithOrderedThresholds()
        {
            foreach (var def in ContentDb.Default.Minigames)
            {
                Assert.That(MinigameCatalog.Has(def.Id), Is.True, def.Id);
                var g = MinigameCatalog.Create(def.Id, 1);
                var t = g.Thresholds;
                Assert.That(t.Length, Is.EqualTo(3));
                Assert.That(t[0] > 0 && t[0] < t[1] && t[1] < t[2], Is.True, def.Id + " thresholds rise");
                Assert.That(g.TimeLimit, Is.InRange(15f, 30f), def.Id);
                Assert.That(g.HintTarget.X, Is.InRange(0f, MinigameLogic.Width));
                Assert.That(g.HintTarget.Y, Is.InRange(0f, MinigameLogic.Height));
            }
            Assert.That(MinigameCatalog.Ids.Count(), Is.EqualTo(14));
        }

        [Test]
        public void SameSeed_PlaysTheSameGame()
        {
            foreach (var id in MinigameCatalog.Ids)
            {
                var a = MinigameCatalog.Create(id, 42);
                var b = MinigameCatalog.Create(id, 42);
                for (int i = 0; i < 600; i++) { a.Bot(MinigameTier.Silver); a.Step(); b.Bot(MinigameTier.Silver); b.Step(); }
                Assert.That(a.Score, Is.EqualTo(b.Score), id);
                Assert.That(a.Status, Is.EqualTo(b.Status), id);
            }
        }

        // ---- host on the run screen ----------------------------------------------------------------

        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp]
        public void SetUp()
        {
            RunScreen.InstantOutcomes = true;
            BattlePlaybackScreen.Instant = true;
            MinigameHost.Instant = true;
        }

        [TearDown]
        public void TearDown()
        {
            RunScreen.InstantOutcomes = false;
            BattlePlaybackScreen.Instant = false;
            MinigameHost.Instant = false;
            MinigameHost.AutoTier = null;
            MinigameHost.Autoplay = null;
            _f?.Dispose();
            _f = null;
        }

        /// <summary>A fresh save with a run whose next roll lands on a GAME tile, then the run screen.</summary>
        private RunScreen OnGameTile(MinigameReward? reward = null)
        {
            _f = new AppFixture();
            if (reward.HasValue) C.MinigameTuning.SkillRollChancePct = reward == MinigameReward.SkillRoll ? 100 : 0;
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            var run = S.Run.Active;
            run.Tiles.Clear();
            for (int i = 0; i < 12; i++)
            {
                var t = new RunTile { Id = i, TileId = i == 0 ? "start" : i == 11 ? "boss_gate" : ContentDb.MinigameTileId, X = i, Fixed = true };
                t.Next.Add(i == 11 ? 1 : i + 1);
                run.Tiles.Add(t);
            }
            run.BossGateTile = 11;
            run.Position = 0; run.Trail.Clear(); run.Trail.Add(0);
            Assert.That(_f.Service.ExecuteNow(new RunRoll()).Ok, Is.True);
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Minigame));
            Assert.That(S.Run.Active.Position, Is.EqualTo(1), "a GAME tile stops the worm (force stop)");
            _f.App.Router.Push(new RunScreen());
            return (RunScreen)_f.App.Router.Current;
        }

        private MinigameHost Host() => _f.App.ModalLayer.GetComponentInChildren<MinigameHost>(true);

        [Test]
        public void GameTile_OpensTheHost_PlayGold_ThenTheResultScreen()
        {
            var screen = OnGameTile(MinigameReward.Coins);
            var host = Host();
            Assert.That(host, Is.Not.Null, "the GAME tile opens the mini-game");
            Assert.That(S.Run.Active.Minigame.Started, Is.True, "the host started it (rules picked game, seed, reward)");
            Assert.That(host.GameId, Is.EqualTo(S.Run.Active.Minigame.GameId));
            var def = C.Minigame(host.GameId);
            string texts = string.Join(" | ", host.GetComponentsInChildren<Text>(true).Select(t => t.text));
            StringAssert.Contains(def.Name, texts);
            StringAssert.Contains(def.Instruction, texts);
            StringAssert.Contains("RUN COINS", texts);
            Assert.That(host.Phase, Is.EqualTo(MinigameHost.Stage.Playing), "Instant: straight into play");
            long coins = S.Run.Active.Coins;

            Assert.That(host.PlayToEnd(MinigameTier.Gold), Is.EqualTo(MinigameTier.Gold));
            Assert.That(host.Renderer.Drawn, Is.GreaterThan(5));
            host.Advance(0.1f); // medal → finish
            Assert.That(host.SentTier, Is.EqualTo(MinigameTier.Gold));
            Assert.That(Host(), Is.Null, "the host closed");
            var run = S.Run.Active;
            Assert.That(run.Phase, Is.EqualTo(RunPhase.AwaitRoll));
            Assert.That(run.Minigame.LastTier, Is.EqualTo(MinigameTier.Gold));
            Assert.That(run.Coins, Is.EqualTo(coins + RunSystem.MinigameCoins(C, run, C.MinigameTuning.For(MinigameTier.Gold))));
            screen.Rebuild();
            // event outcome rules: the host's medal was the reveal; the coins fly in under it, no result sheet
            Assert.That(screen.OutcomeOpen, Is.False, "no sheet after the medal");
            Assert.That(screen.LastPopups.Any(p => p.EndsWith(" coins")), Is.True, string.Join(", ", screen.LastPopups));
        }

        [Test]
        public void Skip_FinishesAsMiss_WithConsolationCoins()
        {
            MinigameHost.Instant = false;
            OnGameTile(MinigameReward.Coins);
            var host = Host();
            Assert.That(host.Phase, Is.EqualTo(MinigameHost.Stage.Intro), "intro: instruction, hint and PLAY");
            var skip = host.GetComponentsInChildren<Button>(true).First(b => b.name == "Button:Skip");
            long coins = S.Run.Active.Coins;
            skip.onClick.Invoke();
            Assert.That(S.Run.Active.Minigame.LastTier, Is.EqualTo(MinigameTier.Miss));
            Assert.That(S.Run.Active.Coins, Is.EqualTo(coins + C.MinigameTuning.For(MinigameTier.Miss).Coins));
            Assert.That(Host(), Is.Null);
        }

        [Test]
        public void Intro_Countdown_Play_TimesOutWithinTheLimit()
        {
            MinigameHost.Instant = false;
            OnGameTile();
            var host = Host();
            var play = host.GetComponentsInChildren<Button>(true).First(b => b.name == "Button:PLAY");
            host.Advance(0.8f); // the hint animates
            play.onClick.Invoke();
            Assert.That(host.Phase, Is.EqualTo(MinigameHost.Stage.Countdown));
            host.Advance(MinigameHost.CountdownSeconds + 0.01f);
            Assert.That(host.Phase, Is.EqualTo(MinigameHost.Stage.Playing));
            for (int i = 0; i < 60 * 31 && host.Phase == MinigameHost.Stage.Playing; i++) host.Advance(MinigameLogic.Dt); // nobody plays
            Assert.That(host.Phase, Is.EqualTo(MinigameHost.Stage.Medal), "the game ends by itself");
            host.Advance(MinigameHost.MedalSeconds + 0.01f);
            Assert.That(S.Run.Active.Minigame.Finished, Is.EqualTo(1));
        }

        [Test]
        public void SkillRoll_Silver_OpensTheDraft_Directly()
        {
            var screen = OnGameTile(MinigameReward.SkillRoll);
            MinigameHost.AutoTier = MinigameTier.Silver;
            Host().Modal.Close();
            screen.OnShow();
            screen.Rebuild(); // reopened: the started game is kept, AutoTier finishes it
            var run = S.Run.Active;
            Assert.That(run.Minigame.LastTier, Is.EqualTo(MinigameTier.Silver));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Draft));
            Assert.That(run.LastOutcome.Lines.Exists(l => l.Kind == OutcomeKind.SkillRoll), Is.True);
            Assert.That(RunScreen.OutcomeLineText(run.LastOutcome.Lines[0], C), Does.StartWith("Skill roll: pick 1 of 3"));
        }

        [Test]
        public void Board_LabelsGameTiles()
        {
            Assert.That(Theme.TileLabel(TileCategory.Minigame), Is.EqualTo("GAME"));
        }
    }
}
