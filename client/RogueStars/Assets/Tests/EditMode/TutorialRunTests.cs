using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
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
    /// GDD §16 tutorial run (main level 1): coach marks with a hand pointer for rolling, picking a skill on level-up and
    /// beating the boss, nothing else; "Skip tips" turns them off; later runs have none. The first run starts with the
    /// signature pick like every run (playtest 0.1.325) and has no auto roll yet (a level unlock).
    /// </summary>
    public sealed class TutorialRunTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; }
        [TearDown] public void TearDown() { RunScreen.InstantOutcomes = false; _f.Dispose(); }

        private static Transform FindNamed(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        private ActiveRun StartTutorial()
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            return S.Run.Active;
        }

        private RunScreen Open()
        {
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            screen.Frame(0.1f);
            return screen;
        }

        [Test]
        public void FirstRun_CoachMarks_Roll_ThenPick_ThenBoss()
        {
            var run = StartTutorial();
            Assert.That(CoachMarks.IsTutorialRun(S, C), Is.True);
            var screen = Open();
            Assert.That(screen.CoachMark, Is.EqualTo(CoachMarks.Roll));
            var coach = FindNamed(_f.App.CoachLayer, "Coach:" + CoachMarks.Roll);
            Assert.That(coach, Is.Not.Null);
            Assert.That(FindNamed(coach, "Hand").gameObject.activeSelf, Is.True, "the hand points at the dice");
            Assert.That(FindNamed(coach, "CoachText").GetComponent<Text>().text, Is.EqualTo("Tap the dice to roll!"));
            Assert.That(coach.GetComponentsInChildren<Graphic>(true).Where(g => g.raycastTarget).All(g => g.GetComponentInParent<Button>() != null), Is.True,
                "the coach layer never blocks the board");

            // a level-up: the draft opens, the hand points at a skill card
            _f.App.Router.Pop();
            run.Phase = RunPhase.Draft; run.MythicDraft = false;
            run.DraftOffer.Clear(); run.DraftOffer.AddRange(new[] { "crit_keen", "gen_power", "gen_guard" });
            screen = Open();
            screen.Frame(0.1f);
            Assert.That(FindNamed(_f.App.ModalLayer, "Button:Pick"), Is.Not.Null, "the draft is open");
            Assert.That(screen.CoachMark, Is.EqualTo(CoachMarks.Pick));
            Assert.That(FindNamed(_f.App.CoachLayer, "CoachText").GetComponent<Text>().text, Is.EqualTo("Level up! Pick a skill."));

            // the boss
            _f.App.Router.Pop();
            run.Phase = RunPhase.Encounter; run.EncounterKind = "boss";
            screen = Open();
            Assert.That(screen.CoachMark, Is.EqualTo(CoachMarks.Boss));
            Assert.That(FindNamed(_f.App.CoachLayer, "CoachText").GetComponent<Text>().text, Is.EqualTo("The boss is here. Tap to fight!"));

            // only one mark at a time, and none once the screen is left
            Assert.That(_f.App.CoachLayer.childCount, Is.EqualTo(1));
            _f.App.Router.Pop();
            Assert.That(_f.App.CoachLayer.childCount, Is.EqualTo(0));
        }

        [Test]
        public void SkipTips_TurnsTheMarksOff_ForThisRun()
        {
            StartTutorial();
            var screen = Open();
            Assert.That(screen.CoachMark, Is.EqualTo(CoachMarks.Roll));
            FindNamed(_f.App.CoachLayer, "CoachSkip").GetComponent<Button>().onClick.Invoke();
            screen.Frame(0.1f);
            Assert.That(screen.CoachMark, Is.Null);
            Assert.That(_f.App.CoachLayer.childCount, Is.EqualTo(0));
        }

        [Test]
        public void LaterRuns_HaveNoCoachMarks()
        {
            S.Counters[Requirements.MainLevelCounter] = 1;
            S.Run.Levels[C.MainLevelId(1)] = new LevelProgress { Cleared = true, Clears = 1 };
            StartTutorial(); // a replay of level 1
            Assert.That(CoachMarks.IsTutorialRun(S, C), Is.False);
            var screen = Open();
            Assert.That(screen.CoachMark, Is.Null);
        }

        /// <summary>
        /// Level 1 from the client: the run starts and the signature pick shows on its board before the first roll (playtest
        /// 0.1.325: every run, the first one too; run UI glitches #24: on the run map), its first card picks.
        /// </summary>
        private void StartFirstRun()
        {
            var level = C.MainLevel(1);
            SignaturePick.StartRun(_f.App, new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }, _ => { });
            Assert.That(S.Run.Active, Is.Not.Null);
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Signature), "the first roll waits for the signature pick");
            var screen = Open();
            Assert.That(screen.CoachMark, Is.Null, "no roll coach mark over the pick");
            var card = _f.App.ModalLayer.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name.StartsWith("Button:Signature:"));
            Assert.That(card, Is.Not.Null, "three signature cards");
            card.onClick.Invoke();
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.AwaitRoll));
            _f.App.Router.Pop();
        }

        [Test]
        public void FirstRun_PicksItsSignature_NoAutoRoll_UntilItsUnlock()
        {
            StartFirstRun();
            Assert.That(RunSystem.Signatures(S.Run.Active).Count, Is.EqualTo(1));
            var screen = Open();
            var roll = FindNamed(screen.Root, "RollButton");
            Assert.That(roll.GetComponentInChildren<Text>().text, Is.EqualTo("Tap to roll"));
            roll.GetComponent<HoldButton>().OnHold();
            Assert.That(screen.Auto, Is.False, "Auto Roll is a level unlock");
            Assert.That(_f.App.Feedback.LastTip, Is.EqualTo("Auto Roll unlocks at level 7"), "owner playtest 0.1.224: a hold before the unlock explains it");

            S.Onboarding.Unlocked[Features.AutoRoll] = _f.Service.NowMs;
            screen.Rebuild();
            roll = FindNamed(screen.Root, "RollButton");
            Assert.That(roll.GetComponentInChildren<Text>().text, Is.EqualTo("Hold for Auto"));
            roll.GetComponent<HoldButton>().OnHold();
            Assert.That(screen.Auto, Is.True);
        }

        /// <summary>
        /// Owner playtest 0.1.224: from level 7 a real hold on the dice cap starts auto — the press reaches the cap (no
        /// raycast target of the screen lies over its middle), the handler is found from the cap upwards, and 0.5 s held fires.
        /// </summary>
        [Test]
        public void HoldOnDiceCap_StartsAuto_OnceUnlocked()
        {
            StartFirstRun();
            S.Onboarding.Unlocked[Features.AutoRoll] = _f.Service.NowMs;
            var screen = Open();
            var cap = (RectTransform)FindNamed(screen.Root, "Cap");
            Assert.That(cap, Is.Not.Null);
            Canvas.ForceUpdateCanvases();
            Vector2 at = RectTransformUtility.WorldToScreenPoint(null, cap.TransformPoint(cap.rect.center + new Vector2(0, cap.rect.height * 0.1f)));
            // the topmost raycast target under the press (the last drawn of the canvas' targets containing the point)
            // (hierarchy order = draw order on the one canvas)
            Graphic top = null;
            foreach (var g in _f.Canvas.GetComponentsInChildren<Graphic>())
                if (g.raycastTarget && g.isActiveAndEnabled &&RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, at, null)) top = g;
            Assert.That(top, Is.Not.Null);
            Assert.That(top.transform.IsChildOf(FindNamed(screen.Root, "RollButton")), Is.True, "the press lands on the dice, not on " + top.name);
            var handler = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerDownHandler>(top.gameObject);
            var hold = handler.GetComponent<HoldButton>();
            Assert.That(hold, Is.Not.Null, "the press reaches the hold handler");
            var e = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = at };
            hold.OnPointerDown(e);
            hold.Advance(0.3f);
            Assert.That(screen.Auto, Is.False, "not yet a hold");
            hold.Advance(0.3f);
            Assert.That(screen.Auto, Is.True, "held 0.6 s: auto on");
        }
    }
}
