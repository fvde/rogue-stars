using System;
using System.Collections.Generic;
using RogueStars.Client.UiKit;
using UnityEngine;
using static RogueStars.Client.World.IsoPaint;

namespace RogueStars.Client.World
{
    /// <summary>
    /// Art of the feature intros (unlock screens, "?" replays) and of the tutorial's coach marks: ArtCatalog.Intro
    /// (<c>Art/Intro/&lt;key&gt;</c>) first, else an existing drawn sprite that fits the key (HUD, hub and run icons, props), else
    /// a drawn badge. Drop a PNG named after the key into <c>Assets/Art/Resources/Art/Intro/</c> to replace a fallback.
    /// </summary>
    public static class IntroArt
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static readonly Color AlienGreen = new Color32(0x7C, 0xD8, 0x5A, 255);

        /// <summary>The fallback of every intro art key (FeatureDef.Art and FeatureIntroStep.Art in ContentDb.Onboarding).</summary>
        private static readonly Dictionary<string, Func<Sprite>> Fallbacks = new Dictionary<string, Func<Sprite>>
        {
            // worm, parts, levels
            { "parts", Worm }, { "parts_slots", Worm }, { "part_equip", () => HudArt.Icon("shield") }, { "part_rare", () => ScreenArt.Prop("chest_signature") },
            { "ore", () => HudArt.Icon("cog") }, { "worm_level", Worm }, { "worm_up", () => ScreenArt.Icon("max_hp") }, { "biomass", () => ScreenArt.Icon("blessing") },
            { "talents", () => HudArt.Icon("exp") }, { "worm_move", Worm },
            // run content
            { "run_shop", () => ScreenArt.Character("shopkeeper") }, { "shop_tile", () => ScreenArt.Character("shopkeeper") }, { "coins", () => ScreenArt.Icon("coins_big") },
            { "shop_buy", () => ScreenArt.Icon("sale_tag") }, { "run_miniboss", () => HudArt.Icon("skull") }, { "miniboss", () => HudArt.Icon("skull") },
            { "chest_mythic", () => ScreenArt.Prop("chest_mythic") }, { "mythic", () => ScreenArt.Prop("chest_mythic") },
            { "run_two_dice", () => HudArt.Icon("dice") }, { "dice", () => HudArt.Icon("dice") }, { "fork", () => ScreenArt.HubIcon("map") },
            { "auto_roll", () => HudArt.DiceButton(true) }, { "dice_hold", () => HudArt.DiceButton(true) }, { "auto_pause", () => HudArt.Icon("speed") },
            { "tap_stop", () => HudArt.DiceButton(false) }, { "run_campfire", () => ScreenArt.Prop("campfire_big") }, { "campfire", () => ScreenArt.Prop("campfire_big") },
            { "campfire_choice", () => ScreenArt.Character("camp_cook") }, { "run_signatures", () => ScreenArt.Prop("chest_signature") },
            { "signature", () => ScreenArt.Prop("chest_signature") }, { "signature_pick", () => HudArt.Icon("book") }, { "signature_awake", () => ScreenArt.Icon("upgrade") },
            { "run_minigames", () => ScreenArt.HubIcon("challenges") }, { "game_tile", () => ScreenArt.HubIcon("challenges") }, { "minigame", () => ScreenArt.HubIcon("challenges") },
            { "medal", () => ScreenArt.Result("crown") },
            // hub systems
            { "quests", () => ScreenArt.HubIcon("quests") }, { "goals", () => ScreenArt.HubIcon("quests") }, { "points", () => ScreenArt.HubIcon("chapter_rewards") },
            { "chest", () => ScreenArt.Prop("chest_common") }, { "calendar", () => ScreenArt.HubIcon("mail") },
            { "biomass_farm", () => ScreenArt.HubIcon("tab_city") }, { "city", () => ScreenArt.HubIcon("tab_city") }, { "farm_build", () => ScreenArt.HubIcon("patrol") },
            { "part_pulls", () => ScreenArt.HubIcon("shop") }, { "signal", () => ScreenArt.HubIcon("shop") }, { "token", () => HudArt.Icon("coin") },
            { "merge", () => ScreenArt.Icon("upgrade") }, { "shop", () => ScreenArt.HubIcon("shop") }, { "gift", () => ScreenArt.HubIcon("offer") },
            { "laboratory", () => HudArt.Icon("book") }, { "lab", () => HudArt.Icon("book") }, { "data", () => HudArt.Icon("exp") }, { "package", () => ScreenArt.Prop("chest_signature") },
            { "aliens", Alien }, { "alien", Alien }, { "alien_squad", Alien }, { "alien_fight", () => HudArt.Drone() }, { "grain", () => ScreenArt.Icon("blessing") },
            { "housing", () => ScreenArt.HubIcon("tab_city") }, { "grain_farm", () => ScreenArt.HubIcon("patrol") },
            { "heroes", () => ScreenArt.HubIcon("starter") }, { "hero_event", () => ScreenArt.HubIcon("starter") }, { "hero_pull", () => ScreenArt.HubIcon("tab_signals") },
            { "hero", Worm }, { "alien_pulls", () => ScreenArt.HubIcon("shop") }, { "alien_signal", () => ScreenArt.HubIcon("shop") }, { "alien_token", () => HudArt.Icon("coin") },
            { "blueprints", () => ScreenArt.HubIcon("tab_signals") }, { "blueprint_signal", () => ScreenArt.HubIcon("shop") }, { "building", () => ScreenArt.HubIcon("tab_city") },
            { "ore_mine", () => ScreenArt.HubIcon("patrol") },
            { "mounts", () => HudArt.Mount() }, { "mount_event", () => HudArt.Mount() }, { "mount", () => HudArt.Mount() }, { "fuel", () => HudArt.Icon("speed") },
            { "runes", () => ScreenArt.Icon("blessing") }, { "rune", () => ScreenArt.Icon("blessing") }, { "rune_stats", () => ScreenArt.Icon("upgrade") },
            { "funds", () => ScreenArt.HubIcon("offer") }, { "funds_claim", () => ScreenArt.Icon("coins_big") },
            { "arena", () => HudArt.Arena() }, { "arena_medal", () => ScreenArt.Result("crown") }, { "arena_shop", () => ScreenArt.HubIcon("shop") },
            { "breeding", Alien }, { "genes", () => ScreenArt.Icon("upgrade") }, { "season_pass", () => ScreenArt.HubIcon("pass") },
            { "pass_points", () => ScreenArt.HubIcon("pass") }, { "pass_track", () => ScreenArt.HubIcon("chapter_rewards") },
            { "overcharged", () => ScreenArt.HubIcon("challenges") }, { "overcharged_reward", () => ScreenArt.Prop("chest_mythic") },
            { "guilds", () => ScreenArt.Result("banner") }, { "guild", () => ScreenArt.Result("banner") }, { "guild_shop", () => ScreenArt.HubIcon("shop") },
            { "invasions", () => ScreenArt.HubIcon("invasion") }, { "invasion", () => ScreenArt.HubIcon("invasion") }, { "observatory", () => ScreenArt.HubIcon("observatory") },
            { "invasion_reward", () => ScreenArt.Prop("chest_common") }, { "burrow", () => ScreenArt.HubIcon("map") }, { "burrow_cash", () => ScreenArt.Icon("coins_big") },
            { "forge", () => ScreenArt.Icon("upgrade") }, { "forge_slot", () => HudArt.Icon("cog") },
            { "dark_side", () => ScreenArt.HubIcon("challenges") }, { "dark_rules", () => ScreenArt.Icon("curse") },
            { "package_ice", Package }, { "ice", Package }, { "ice_skill", () => ScreenArt.Icon("blessing") },
            { "package_lightning", Package }, { "lightning", Package }, { "lightning_skill", () => HudArt.Icon("speed") },
            { "package_counter", Package }, { "counter", Package }, { "counter_skill", () => HudArt.Icon("shield") },
            { "package_alien", Package }, { "alien_package", Package }, { "package_time", Package }, { "time", Package }, { "time_skill", () => HudArt.Icon("speed") },
        };

        private static Sprite Worm() => ArtCatalog.Hero() ?? ScreenArt.HubIcon("tab_worm");
        private static Sprite Alien() => ArtCatalog.Alien("alien_blorp") ?? HudArt.AlienCritter(AlienGreen);
        private static Sprite Package() => ScreenArt.Prop("chest_signature");

        /// <summary>True when the key has a drawn fallback (tests: every intro key of the content has one).</summary>
        public static bool HasFallback(string key) => key != null && Fallbacks.ContainsKey(key);

        /// <summary>The art of an intro key: the generated file if present, else its fallback, else a drawn badge.</summary>
        public static Sprite Sprite(string key)
        {
            var art = ArtCatalog.Intro(key);
            if (art != null) return art;
            if (key != null && Fallbacks.TryGetValue(key, out var draw)) { var s = draw(); if (s != null) return s; }
            return ScreenArt.HubIcon("more");
        }

        /// <summary>The coach-mark / intro hand: a white pointing glove with the slice ink outline (pivot: the fingertip).</summary>
        public static Sprite Hand()
        {
            var art = ArtCatalog.Intro("hand");
            if (art != null) return art;
            if (Cache.TryGetValue("hand", out var s) && s != null) return s;
            const int n = 256;
            var p = new IsoPaint(n, n);
            var glove = (Color)new Color32(0xFA, 0xF6, 0xF0, 255);
            var shade = (Color)new Color32(0xD8, 0xD0, 0xE0, 255);
            var ink = IsoArt.Ink;
            // the palm and three curled fingers, the thumb, the pointing finger (up)
            p.Shape(RoundRect(70, 18, 130, 120, 40), VGrad(shade, glove, 18, 138), ink, 9);
            p.Shape(RoundRect(40, 60, 56, 34, 16), glove, ink, 8);
            p.Shape(RoundRect(92, 110, 46, 132, 22), VGrad(shade, glove, 110, 240), ink, 9);
            p.Shape(RoundRect(150, 104, 44, 30, 14), glove, ink, 6);
            p.Shape(RoundRect(150, 76, 44, 30, 14), glove, ink, 6);
            p.Shape(RoundRect(70, 6, 130, 22, 10), (Color)new Color32(0x5C, 0x9C, 0xF0, 255), ink, 7); // cuff
            s = p.ToSprite("rs_hand", V(115, 240));
            Cache["hand"] = s;
            return s;
        }
    }

    /// <summary>
    /// The looping animation of an intro panel (IntroAnim kinds): the art taps, breathes, slides in, counts, turns, hops or
    /// fills a bar. Play mode only (Update); in edit mode the panel shows its resting pose, so tests stay deterministic.
    /// </summary>
    public sealed class IntroAnimator : MonoBehaviour
    {
        public string Kind;
        public RectTransform Art;
        /// <summary>Optional parts: the hand (tap), the "+N" label (count), the bar fill (fill), the glow (pulse).</summary>
        public RectTransform Hand;
        public UnityEngine.UI.Text Counter;
        public RectTransform Fill;
        public UnityEngine.UI.Image Glow;
        public int CountTo = 100;
        private float _t;
        private Vector2 _artBase, _handBase;

        private void Start()
        {
            if (Art != null) _artBase = Art.anchoredPosition;
            if (Hand != null) _handBase = Hand.anchoredPosition;
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            Apply(Kind, _t);
        }

        /// <summary>The pose at time <paramref name="t"/> seconds (also used by the screenshot showcase).</summary>
        public void Apply(string kind, float t)
        {
            if (Art == null) return;
            switch (kind)
            {
                case "tap":
                {
                    float k = Mathf.Repeat(t, 1.4f) / 1.4f;           // 0..1 per tap
                    float press = k < 0.35f ? k / 0.35f : k < 0.5f ? 1 : Mathf.Max(0, 1 - (k - 0.5f) / 0.3f);
                    if (Hand != null) Hand.anchoredPosition = _handBase + new Vector2(0, 60 * (1 - press));
                    Art.localScale = Vector3.one * (1 - 0.06f * (k >= 0.35f && k < 0.55f ? 1 : 0));
                    break;
                }
                case "pulse":
                {
                    float k = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2 / 1.6f);
                    Art.localScale = Vector3.one * (1 + 0.07f * k);
                    if (Glow != null) { var c = Glow.color; c.a = 0.15f + 0.35f * k; Glow.color = c; }
                    break;
                }
                case "slide":
                {
                    float k = Mathf.Repeat(t, 2.4f);
                    float x = k < 0.6f ? Mathf.SmoothStep(-320, 0, k / 0.6f) : k < 2.0f ? 0 : Mathf.Lerp(0, 40, (k - 2) / 0.4f);
                    Art.anchoredPosition = _artBase + new Vector2(x, 0);
                    var cg = Art.GetComponent<CanvasGroup>();
                    if (cg != null) cg.alpha = k < 0.6f ? k / 0.6f : k > 2.0f ? 1 - (k - 2) / 0.4f : 1;
                    break;
                }
                case "count":
                {
                    float k = Mathf.Repeat(t, 2.2f) / 1.4f;
                    int v = Mathf.RoundToInt(Mathf.Clamp01(k) * CountTo);
                    if (Counter != null)
                    {
                        Counter.text = "+" + v;
                        Counter.rectTransform.anchoredPosition = new Vector2(0, 150 + 40 * Mathf.Clamp01(k));
                    }
                    Art.localScale = Vector3.one * (1 + 0.05f * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI));
                    break;
                }
                case "spin":
                    Art.localRotation = Quaternion.Euler(0, 0, -Mathf.Repeat(t, 2f) / 2f * 360f);
                    break;
                case "bounce":
                {
                    float k = Mathf.Repeat(t, 1.1f) / 1.1f;
                    float h = Mathf.Sin(k * Mathf.PI);
                    Art.anchoredPosition = _artBase + new Vector2(0, 70 * h);
                    Art.localScale = new Vector3(1 + 0.08f * (1 - h) * (1 - h), 1 - 0.08f * (1 - h) * (1 - h), 1);
                    break;
                }
                case "fill":
                    if (Fill != null) Fill.anchorMax = new Vector2(Mathf.Clamp01(Mathf.Repeat(t, 2.2f) / 1.6f), 1);
                    break;
            }
        }
    }
}
