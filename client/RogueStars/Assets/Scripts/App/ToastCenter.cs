using System.Collections.Generic;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.App
{
    /// <summary>Human-readable text for GameEvents (null = not worth a toast). Never shows internal ids.</summary>
    public static class EventText
    {
        public static string Describe(GameEvent e, ContentDb c, GameState state = null)
        {
            var a = e.Args;
            string A(int i) => i < a.Count ? a[i] : "";
            string Bp(int i) => c?.Blueprint(A(i))?.Name ?? "Blueprint";
            string Al(int i) => c?.AlienType(A(i))?.Name ?? "Alien";
            string Hero(int i) => c != null && c.HeroConfig.Defs.TryGetValue(A(i), out var h) ? h.Name : "Hero";
            string Mount(int i) => c != null && c.MountConfig.Defs.TryGetValue(A(i), out var m) ? m.Name : "Mount";
            string Part(int i) => Fmt.PartName(c, state, A(i));
            switch (e.Type)
            {
                case "reward":
                    long.TryParse(A(2), out var amount);
                    return "+" + Fmt.Reward(new Reward(A(0), A(1), amount), c);
                case "timer_completed":
                case "timer_sped_up":
                case "timer_started":
                case "city_priority":
                case "squad_set":
                case "ad_watched":
                case "gacha_pull":
                case "mail_expired":
                case "run_negative_skipped":
                    return null;
                case "city_collected": return "+" + A(2) + " " + Fmt.CurrencyName(A(1), c);
                case "building_placed": return Bp(0) + " placed";
                case "building_built": return Bp(0) + " built!";
                case "building_upgraded": return Bp(0) + " reached level " + A(1);
                case "building_moved": return Bp(0) + " moved";
                case "building_demolished": return Bp(0) + " stored";
                case "blueprint_new": return "New blueprint: " + Bp(0);
                case "blueprint_star_up": return Bp(0) + " → " + A(1) + "★";
                case "blueprint_shards": return "+" + A(1) + " " + Bp(0) + " shards";
                case "area_unlocked": return "City area unlocked";
                case "alien_new": return "New alien: " + Al(0);
                case "alien_shards": return "+" + A(1) + " " + Al(0) + " shards";
                case "alien_level_up": return Al(0) + " reached level " + A(1);
                case "alien_star_up": return Al(0) + " → " + A(1) + "★";
                case "alien_assigned": return Al(0) + " assigned to " + Bp(1);
                case "alien_unassigned": return Al(0) + " unassigned";
                case "breed_started": return "Breeding started";
                case "breed_born": return "A baby " + Al(1) + " hatched!";
                case "breed_replaced": return Al(0) + " replaced by its child";
                case "breed_discarded": return Al(0) + " child released";
                case "hero_unlocked": return "New hero: " + Hero(0) + "!";
                case "hero_star": return Hero(0) + " → " + A(1) + "★";
                case "mount_unlocked": return "New mount: " + Mount(0) + "!";
                case "mount_star": return Mount(0) + " → " + A(1) + "★";
                case "mount_awaken": return Mount(0) + " awakened (" + A(1) + ")";
                case "part_equipped": return Part(1) + " equipped (" + Fmt.SplitWords(A(0)) + ")";
                case "part_merged": return Part(0) + " merged to " + (int.TryParse(A(1), out var rar) && c != null ? c.PartsConfig.RarityDef(rar).Name : "the next rarity");
                case "part_awakened": return Part(0) + " awakened (" + A(1) + ")";
                case "worm_level": return "Worm level " + A(0);
                case "worm_talent_tier": return "Talent tier " + A(0) + " complete";
                case "feature_unlocked": return "Unlocked: " + (c?.Feature(A(0))?.Name ?? Fmt.Humanize(A(0))) + "!";
                case "tutorial_step": return "Goal done: " + (c != null && c.TutorialSteps.TryGetValue(A(0), out var ts) ? ts.Name : "tutorial step");
                case "quest_claimed": return "Quest complete: " + (c?.Quest(A(0))?.Name ?? "quest");
                case "purchase": return "Purchased " + (c?.Product(A(0))?.Name ?? "item");
                case "stamina_refilled": return "+" + A(1) + " Stamina";
                case "mail": return "New mail";
                case "returning_player": return "Welcome back!";
                case "guild_left": return "You left the guild";
                case "arena_result": return A(0) == "win" ? "Arena win (" + SignedPoints(A(1)) + ")" : "Arena loss (" + SignedPoints(A(1)) + ")";
                case "arena_week_end": return "Arena week over: rank " + A(0);
                case "planetary_event": return "Planetary event: " + (c?.PlanetaryEvent(A(0))?.Name ?? "a new event");
                case "invasion_outcome":
                    return (c?.Invasion(A(0))?.Name ?? "The invasion") + (A(2) == "repelled" ? " repelled!" : " overran the planet");
                case "run_level_up": return "Run level " + A(0) + "!";
                case "run_skill_gained": return "Skill: " + (c?.Skill(A(0))?.Name ?? "new skill");
                case "run_completed": return "Run complete!";
                case "run_failed": return "Run failed — pending loot lost";
                case "run_revived": return "Revived!";
                case "run_rewards_doubled": return "Run rewards doubled!";
                case "run_burrow_descend": return "Burrow floor " + A(0);
                case "boss_awaken": return "The boss stirs…";
                case "lab_collected": return (c != null && c.LabResearch.TryGetValue(A(0), out var lr) ? lr.Name : "Research") + " level " + A(1) + " researched";
                // high-frequency run plumbing is shown on the board instead
                case "run_roll": case "run_land": case "run_battle": case "run_draft": case "run_event": case "run_shop":
                case "run_started": case "run_draft_pick": case "run_shop_buy": case "run_event_outcome": case "run_boss_fight":
                case "lab_research": case "run_defeated":
                    return null;
                // Unknown events: the humanized type only (args are ids).
                default: return Fmt.Humanize(e.Type);
            }
        }

        private static string SignedPoints(string delta) =>
            long.TryParse(delta, out var d) ? (d >= 0 ? "+" : "−") + System.Math.Abs(d) + " points" : "points";
    }

    /// <summary>
    /// Auto-fading notifications for game events, info and errors: at most two at a time, short-lived, in a strip just
    /// above the current screen's bottom action bar (<see cref="UiScreen.ToastBottomInset"/>), never taking input.
    /// Screens with a board (Run) reserve that strip so toasts never cover it.
    /// </summary>
    public sealed class ToastCenter
    {
        public const int MaxVisible = 2;
        public const float Lifetime = 1.8f;
        /// <summary>Height of the strip toasts may use (two one-line toasts).</summary>
        public const float StripHeight = 132;
        /// <summary>Default distance from the bottom of the safe area (screens without an action bar).</summary>
        public const float DefaultBottomInset = 40;

        private readonly RectTransform _stack;
        private readonly List<KeyValuePair<GameObject, float>> _toasts = new List<KeyValuePair<GameObject, float>>();

        public int Count => _toasts.Count;
        /// <summary>Last shown text (tests, debugging).</summary>
        public string LastText { get; private set; }
        /// <summary>The toast stack (tests: placement checks).</summary>
        public RectTransform Stack => _stack;

        public ToastCenter(RectTransform layer)
        {
            _stack = Ui.Column(layer, 8, 0, null, "Toasts");
            _stack.anchorMin = new Vector2(0.06f, 0); _stack.anchorMax = new Vector2(0.94f, 0); _stack.pivot = new Vector2(0.5f, 0);
            _stack.anchoredPosition = new Vector2(0, DefaultBottomInset);
            _stack.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.LowerCenter;
            _stack.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>Distance of the toast strip from the bottom of the safe area (set by the router for the top screen).</summary>
        public void SetBottomInset(float inset) => _stack.anchoredPosition = new Vector2(0, Mathf.Max(0, inset));

        public void Show(string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            LastText = text;
            while (_toasts.Count >= MaxVisible) Remove(0, false); // evicted at once: a fading toast would still take a slot
            var panel = Ui.Panel(_stack, new Color(color.r * 0.35f, color.g * 0.35f, color.b * 0.35f, 0.95f), "Toast");
            panel.raycastTarget = false;
            var col = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(16, 16, 6, 6);
            col.childControlHeight = true; col.childControlWidth = true; col.childForceExpandHeight = true;
            col.childAlignment = TextAnchor.MiddleCenter;
            var label = Ui.Label(panel.transform, text, Theme.FontSmall, Color.Lerp(color, Color.white, 0.6f), TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            panel.gameObject.AddComponent<LayoutElement>().preferredHeight = (StripHeight - 8) / MaxVisible;
            UiTween.PopIn(panel, 0.15f);
            _toasts.Add(new KeyValuePair<GameObject, float>(panel.gameObject, Lifetime));
        }

        public void Info(string text) => Show(text, Theme.Accent);
        public void Error(string text) => Show(text, Theme.Bad);
        public void Event(GameEvent e, ContentDb c, GameState state = null) => Show(EventText.Describe(e, c, state), e.Type == "reward" ? Theme.Good : Theme.Premium);

        public void Tick(float dt)
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                float left = _toasts[i].Value - dt;
                if (left <= 0 || _toasts[i].Key == null) Remove(i);
                else _toasts[i] = new KeyValuePair<GameObject, float>(_toasts[i].Key, left);
            }
        }

        private void Remove(int i, bool fade = true)
        {
            var go = _toasts[i].Key;
            _toasts.RemoveAt(i);
            if (go == null) return;
            if (fade && Application.isPlaying)
            {
                // Leave the stack right away (keeping its place on screen) so the stack can shrink while this one fades.
                go.transform.SetParent(_stack.parent, true);
                UiTween.FadeOut(go.transform, 0.25f);
            }
            else Ui.DestroyGo(go);
        }
    }
}
