using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
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
    /// Event outcome rules (owner 2026-11): the run screen opens a result sheet only for news (gambles, random picks, the
    /// story's end); deterministic results fly in as popups, results without effect are a one-line toast, a lost fight goes
    /// straight to the revive with its own text; Rest and the tonic say "HP already full"; boss-wake numbers on the button match
    /// the result.
    /// </summary>
    public sealed class EventOutcomeShowTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; RunFx.Instant = true; }
        [TearDown] public void TearDown() { RunScreen.InstantOutcomes = false; RunFx.Instant = false; _f.Dispose(); }

        private static Transform FindNamed(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        private static string Texts(Transform root) =>
            string.Join(" | ", root.GetComponentsInChildren<Text>(true).Where(t => t.gameObject.activeInHierarchy).Select(t => t.text));

        /// <summary>A run on a straight board with the event open on tile 1 (enemies ahead for the campfire's cost).</summary>
        private RunScreen EventOnScreen(string eventId, System.Action<ActiveRun> setup = null)
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            var run = S.Run.Active;
            run.Tiles.Clear();
            for (int i = 0; i <= 20; i++)
            {
                var t = new RunTile { Id = i, TileId = i == 0 ? "start" : i == 1 ? "event" : i == 2 || i == 4 ? "enemy_1" : "empty", X = i };
                if (i < 20) t.Next.Add(i + 1);
                run.Tiles.Add(t);
            }
            run.BossGateTile = -1; run.BoardEnd = 20;
            run.Position = 1;
            run.Phase = RunPhase.Event; run.EventId = eventId; run.EventTile = 1;
            run.Coins = 500; run.Shards = 5;
            run.WormHp = run.WormMaxHp / 2;
            setup?.Invoke(run);
            _f.App.Router.Push(new RunScreen());
            return (RunScreen)_f.App.Router.Current;
        }

        private static int Opt(ContentDb c, string ev, string text) => c.RunEvents[ev].Options.FindIndex(o => o.Text.StartsWith(text));

        private void Choose(RunScreen screen, string ev, string option)
        {
            Assert.That(_f.Service.ExecuteNow(new RunChooseEventOption { Index = Opt(C, ev, option) }).Ok, Is.True);
            while (_f.App.Router.Current is BattlePlaybackScreen bp) { bp.PlayAll(); _f.App.Router.Pop(); }
            screen.Rebuild();
        }

        [Test]
        public void Gamble_OpensTheSheet_WithTheChanceMeter()
        {
            var screen = EventOnScreen("strange_spores");
            Choose(screen, "strange_spores", "Eat them");
            Assert.That(screen.OutcomeOpen, Is.True);
            Assert.That(FindNamed(_f.App.ModalLayer, "ChanceMeter"), Is.Not.Null);
        }

        [Test]
        public void RandomSkill_OpensTheSheet_LedByTheSkill()
        {
            var screen = EventOnScreen("unstable_reactor");
            Choose(screen, "unstable_reactor", "Absorb the surge");
            Assert.That(screen.OutcomeOpen, Is.True);
            var lines = FindNamed(_f.App.ModalLayer, "Lines");
            var first = lines.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith("Line:"));
            Assert.That(first.name, Is.EqualTo("Line:SkillGained"), "the news first");
            Assert.That(Texts(lines), Does.Contain("Boss +15% ATK"), "the cost the button promised");
        }

        [Test]
        public void Deterministic_Rest_FliesIn_NoSheet_ListsEnemiesStronger()
        {
            var screen = EventOnScreen("campfire");
            Choose(screen, "campfire", "Rest");
            Assert.That(screen.OutcomeOpen, Is.False, "no sheet, no Continue tap");
            Assert.That(screen.LastPopups.Any(p => p.EndsWith(" HP") && p.StartsWith("+")), Is.True, string.Join(", ", screen.LastPopups));
            Assert.That(screen.LastPopups, Does.Contain("Enemies +12% stronger"));
            Assert.That(screen.LastToast, Is.Null);
        }

        [Test]
        public void Nothing_IsAToast_YouMoveOn()
        {
            var screen = EventOnScreen("strange_spores");
            Assert.That(_f.Service.ExecuteNow(new RunChooseEventOption { Index = Opt(C, "strange_spores", "Leave") }).Ok, Is.True);
            if (screen.LastToast == null) screen.Rebuild(); // (a rebuild after the toast would replace the board's overlay)
            Assert.That(screen.OutcomeOpen, Is.False);
            Assert.That(screen.LastToast, Is.EqualTo("You move on."));
            Assert.That(FindNamed(screen.Root, "Toast"), Is.Not.Null, "the toast is on screen");
        }

        [Test]
        public void LostEventFight_GoesStraightToTheRevive_WithTheLossText()
        {
            var screen = EventOnScreen("training_dummy", run => run.WormHp = 1);
            Choose(screen, "training_dummy", "Spar with an elite");
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Defeated));
            Assert.That(screen.OutcomeOpen, Is.False, "no sheet with the win text");
            var revive = FindNamed(_f.App.ModalLayer, "Modal:Defeated!");
            Assert.That(revive, Is.Not.Null, "the revive");
            string texts = Texts(revive);
            Assert.That(texts, Does.Contain("Beaten! Win the retry to claim the reward."));
            Assert.That(texts, Does.Not.Contain("Elite beaten!"));
        }

        [Test]
        public void FullHp_RestAndTonic_SayHpAlreadyFull()
        {
            var screen = EventOnScreen("campfire", run => run.WormHp = run.WormMaxHp);
            Assert.That(Texts(_f.App.ModalLayer), Does.Contain("HP already full"));
            _f.Service.ExecuteNow(new RunAbandon());
            _f.App.Router.Pop();
            EventOnScreen("alien_medic", run => run.WormHp = run.WormMaxHp);
            Assert.That(Texts(_f.App.ModalLayer), Does.Contain("HP already full"));
        }

        [Test]
        public void BossWake_ButtonShowsTheNumberTheResultShows()
        {
            ActiveRun r = null;
            var screen = EventOnScreen("boss_scout", run => { run.AwakeningReductionPct = 40; run.Awakening = 20; r = run; });
            var opt = C.RunEvents["boss_scout"].Options[Opt(C, "boss_scout", "Let it go")];
            string button = RunScenes.EffectLines(opt, C, r).Single(l => l.Value.StartsWith("Boss wakes")).Value;
            Choose(screen, "boss_scout", "Let it go");
            var line = S.Run.Active.LastOutcome.Lines.Single(l => l.Kind == OutcomeKind.Awakening);
            Assert.That(button, Is.EqualTo(RunScreen.OutcomeLineText(line, C)));
            Assert.That(button, Is.Not.EqualTo("Boss wakes 10% sooner"), "scaled by the slower pace");
        }

        [Test]
        public void FlyIn_SkipsTheStoryStep_WhenTheToastCarriesIt()
        {
            var o = new RunOutcome { Show = OutcomeShow.Popups, Toast = "Chapter 5: x" };
            o.Lines.Add(new OutcomeLine(OutcomeKind.Coins, null, -100, false));
            o.Lines.Add(new OutcomeLine(OutcomeKind.QuestStep, "advanced", 5, true));
            Assert.That(RunPresentation.FlyIn(o, C).Select(p => p.Text), Is.EqualTo(new[] { "−100 coins" }));
        }
    }
}
