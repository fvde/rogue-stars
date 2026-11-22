using System.Collections.Generic;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.App
{
    /// <summary>Human-readable text for GameEvents (null = not worth a toast).</summary>
    public static class EventText
    {
        public static string Describe(GameEvent e, ContentDb c)
        {
            var a = e.Args;
            string A(int i) => i < a.Count ? a[i] : "";
            string Bp(int i) => c?.Blueprint(A(i))?.Name ?? A(i);
            string Al(int i) => c?.AlienType(A(i))?.Name ?? A(i);
            switch (e.Type)
            {
                case "reward":
                    long.TryParse(A(2), out var amount);
                    return "+" + Fmt.Reward(new Reward(A(0), A(1), amount), c);
                case "timer_completed":
                case "timer_sped_up":
                case "city_priority":
                case "squad_set":
                    return null;
                case "city_collected": return "+" + A(2) + " " + Fmt.CurrencyName(A(1), c);
                case "building_placed": return Bp(0) + " placed";
                case "building_built": return Bp(0) + " built!";
                case "building_upgraded": return Bp(0) + " reached level " + A(1);
                case "building_moved": return Bp(0) + " moved";
                case "building_demolished": return Bp(0) + " stored";
                case "blueprint_new": return "New blueprint: " + Bp(0);
                case "blueprint_star_up": return Bp(0) + " → " + A(1) + "★";
                case "area_unlocked": return "City area unlocked";
                case "alien_new": return "New alien: " + Al(0);
                case "alien_level_up": return Al(0) + " reached level " + A(1);
                case "alien_star_up": return Al(0) + " → " + A(1) + "★";
                case "alien_assigned": return Al(0) + " assigned to " + Bp(1);
                case "breed_started": return "Breeding started";
                case "breed_born": return "A baby " + Al(1) + " hatched!";
                case "breed_replaced": return Al(0) + " replaced by its child";
                case "breed_discarded": return Al(0) + " child released";
                case "feature_unlocked": return "Unlocked: " + (c?.Feature(A(0))?.Name ?? Fmt.Humanize(A(0))) + "!";
                case "tutorial_step": return "Goal done: " + (c != null && c.TutorialSteps.TryGetValue(A(0), out var ts) ? ts.Name : A(0));
                case "run_level_up": return "Run level " + A(0) + "!";
                case "run_skill_gained": return "Skill: " + (c?.Skill(A(0))?.Name ?? A(0));
                case "run_completed": return "Run complete!";
                case "run_failed": return "Run failed — pending loot lost";
                case "run_revived": return "Revived!";
                case "boss_awaken": return "The boss stirs…";
                case "part_awakened": return "Part awakened (" + A(1) + ")";
                case "lab_collected": return "Research complete";
                // high-frequency run plumbing is shown on the board instead
                case "run_roll": case "run_land": case "run_battle": case "run_draft": case "run_event": case "run_shop":
                case "run_started": case "run_draft_pick": case "run_shop_buy": case "run_event_outcome": case "run_boss_fight":
                case "lab_research": case "run_defeated":
                    return null;
                default: return Fmt.Humanize(e.Type) + (a.Count > 0 ? " " + string.Join(" ", a) : "");
            }
        }
    }

    /// <summary>Stacked, auto-fading notifications for game events, info and errors.</summary>
    public sealed class ToastCenter
    {
        private const int MaxVisible = 4;
        private const float Lifetime = 2.6f;

        private readonly RectTransform _stack;
        private readonly List<KeyValuePair<GameObject, float>> _toasts = new List<KeyValuePair<GameObject, float>>();

        public int Count => _toasts.Count;
        /// <summary>Last shown text (tests, debugging).</summary>
        public string LastText { get; private set; }

        public ToastCenter(RectTransform layer)
        {
            _stack = Ui.Column(layer, 10, 0, null, "Toasts");
            // Bottom of the screen, above the main action row; toasts never take input.
            _stack.anchorMin = new Vector2(0.08f, 0); _stack.anchorMax = new Vector2(0.92f, 0); _stack.pivot = new Vector2(0.5f, 0);
            _stack.anchoredPosition = new Vector2(0, 260);
            _stack.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.LowerCenter;
            _stack.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        public void Show(string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            LastText = text;
            if (_toasts.Count >= MaxVisible) Remove(0);
            var panel = Ui.Panel(_stack, new Color(color.r * 0.35f, color.g * 0.35f, color.b * 0.35f, 0.95f), "Toast");
            panel.raycastTarget = false;
            var col = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(20, 20, 10, 10);
            col.childControlHeight = true; col.childControlWidth = true; col.childForceExpandHeight = false;
            Ui.Label(panel.transform, text, Theme.FontSmall + 2, Color.Lerp(color, Color.white, 0.6f), TextAnchor.MiddleCenter);
            UiTween.PopIn(panel, 0.15f);
            _toasts.Add(new KeyValuePair<GameObject, float>(panel.gameObject, Lifetime));
        }

        public void Info(string text) => Show(text, Theme.Accent);
        public void Error(string text) => Show(text, Theme.Bad);
        public void Event(GameEvent e, ContentDb c) => Show(EventText.Describe(e, c), e.Type == "reward" ? Theme.Good : Theme.Premium);

        public void Tick(float dt)
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                float left = _toasts[i].Value - dt;
                if (left <= 0 || _toasts[i].Key == null) Remove(i);
                else _toasts[i] = new KeyValuePair<GameObject, float>(_toasts[i].Key, left);
            }
        }

        private void Remove(int i)
        {
            var go = _toasts[i].Key;
            _toasts.RemoveAt(i);
            if (go != null) UiTween.FadeOut(go.transform, 0.3f);
        }
    }
}
