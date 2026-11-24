using System;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.App
{
    /// <summary>
    /// The tutorial run's coach marks (GDD §16: level 1 teaches rolling, picking a skill on level-up and beating the boss):
    /// a bobbing hand pointer on the thing to tap and a short speech bubble, drawn on the app's coach layer above modals.
    /// The layer never blocks input (only the bubble's "Skip tips" is a button). Which mark shows is a pure function of the
    /// run (<see cref="For"/>), so tests can check it without UI.
    /// </summary>
    public static class CoachMarks
    {
        public const string Roll = "roll", Pick = "pick", Boss = "boss";

        /// <summary>True while the player's first run is on: main level 1 in Normal mode, never cleared yet.</summary>
        public static bool IsTutorialRun(GameState s, ContentDb c)
        {
            var run = s?.Run.Active;
            if (run == null || run.Mode != RunMode.Normal || s.Counter(Requirements.MainLevelCounter) > 0) return false;
            var level = c.RunLevel(run.LevelId);
            return level != null && level.Number == 1 && level.PortalId != ContentDb.DarkSidePortalId;
        }

        /// <summary>The mark for the run's state (null = none): roll while the first rolls wait, pick on a level-up draft, boss on the boss fight.</summary>
        public static string For(GameState s, ContentDb c, bool decisionOpen, bool busy)
        {
            if (!IsTutorialRun(s, c) || busy) return null;
            var run = s.Run.Active;
            switch (run.Phase)
            {
                case RunPhase.Draft: return decisionOpen && !run.MythicDraft ? Pick : null;
                case RunPhase.Encounter: return run.EncounterKind == "boss" ? Boss : null;
                case RunPhase.AwaitRoll: return run.Rolls < 3 && !decisionOpen ? Roll : null;
                default: return null;
            }
        }

        public static string Text(string mark)
        {
            switch (mark)
            {
                case Roll: return "Tap the dice to roll!";
                case Pick: return "Level up! Pick a skill.";
                case Boss: return "The boss is here. Tap to fight!";
                default: return "";
            }
        }

        /// <summary>
        /// Shows a mark on <paramref name="layer"/>: the hand's fingertip on <paramref name="target"/>'s centre (when given), the
        /// bubble above it (below when the target is high on the screen).
        /// </summary>
        public static GameObject Show(RectTransform layer, RectTransform target, string mark, Action onSkip)
        {
            Canvas.ForceUpdateCanvases();
            var root = Ui.Stretch(Ui.Rect(layer, "Coach:" + mark));
            Vector2 at = Vector2.zero;
            if (target != null)
            {
                var world = target.TransformPoint(target.rect.center);
                at = layer.InverseTransformPoint(world);
            }
            float h = layer.rect.height > 10 ? layer.rect.height : 1920;
            bool high = at.y > -h * 0.2f; // a card in the middle: the hand points up at it from below

            var ring = Ui.Panel(root, new Color(1, 0.86f, 0.36f, 0.35f), "Ring");
            ring.sprite = HubKit.Circle; ring.raycastTarget = false;
            Place(ring.rectTransform, at, new Vector2(220, 220));
            UiPulse.On(ring, 0.12f, 1.1f, 0.4f);
            ring.gameObject.SetActive(target != null);

            var hand = Ui.Panel(root, Color.white, "Hand");
            hand.sprite = IntroArt.Hand(); hand.preserveAspect = true; hand.raycastTarget = false;
            var hrt = hand.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
            hrt.pivot = new Vector2(0.45f, 0.95f);
            hrt.sizeDelta = new Vector2(190, 190);
            hrt.anchoredPosition = at + new Vector2(20, -10);
            hrt.localRotation = Quaternion.Euler(0, 0, 180); // the fingertip points up at the target from below
            if (high) { hrt.localRotation = Quaternion.identity; hrt.pivot = new Vector2(0.45f, 0.95f); hrt.anchoredPosition = at + new Vector2(20, 10); }
            UiBob.On(hrt, 18, 0.9f);
            hand.gameObject.SetActive(target != null);

            var bubble = HubKit.RoundPanel(root, new Color(1, 0.97f, 0.9f, 0.98f), "Bubble");
            bubble.raycastTarget = false;
            UiShapes.Outline(bubble.rectTransform, IsoArt.Ink, 28, 5);
            var brt = bubble.rectTransform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(880, 200);
            // low target (the dice): the bubble above the hand; a card in the middle (draft): the bubble at the top, clear of the cards
            float by = target == null ? 300 : high ? h / 2 - 330 : at.y + 400;
            brt.anchoredPosition = new Vector2(0, Mathf.Clamp(by, -h / 2 + 160, h / 2 - 160));
            var text = Ui.Display(bubble.transform, Text(mark), 50, IsoArt.Ink, TextAnchor.MiddleCenter, "CoachText");
            text.raycastTarget = false;
            Ui.Stretch(text.rectTransform, 24, 16, 24, 58); // (left, top, right, bottom): the line, then "Skip tips" under it
            var skip = Ui.Button(bubble.transform, "Skip tips", () => onSkip?.Invoke(), new Color(0, 0, 0, 0), 200, 46, true, 26);
            skip.name = "CoachSkip";
            var srt = (RectTransform)skip.transform;
            var le = skip.GetComponent<LayoutElement>(); if (le != null) le.ignoreLayout = true;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1, 0);
            srt.anchoredPosition = new Vector2(-18, 6);
            var st = skip.GetComponentInChildren<Text>();
            if (st != null) st.color = new Color(0.36f, 0.3f, 0.42f, 1);
            UiTween.PopIn(brt);
            return root.gameObject;
        }

        private static void Place(RectTransform rt, Vector2 at, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = at;
        }
    }
}
