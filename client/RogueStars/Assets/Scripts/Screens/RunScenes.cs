using System;
using RogueStars.Client.World.Anim;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>Which of the two event layouts an event uses and the art it shows (pure; tested).</summary>
    public enum EventLayoutKind { Scene, Vendor }

    public sealed class EventLook
    {
        public EventLayoutKind Kind;
        /// <summary>Scene: the prop at the right of the stage (ScreenArt.Prop id). Vendor: the NPC (ScreenArt.Character id).</summary>
        public string Art;
        /// <summary>The ribbon title (scene band / vendor ribbon); the event's name unless set.</summary>
        public string Title;
        /// <summary>Scene: the flavour line in the panel. Vendor: the NPC's speech bubble.</summary>
        public string Line;
        /// <summary>The art is a character bust (ScreenArt.Character), also when it stands on the scene layout (3 options).</summary>
        public bool Npc;
    }

    /// <summary>
    /// The screens pass for the run's decisions (references rest / event / event-vendor): a battle-arena-style stage on top
    /// (the worm on its mount with its sidekicks at the left, the event's prop at the right, the run HUD over it), a bottom
    /// panel with a dark title band, a flavour line and option tiles (label above, an icon tile, the outcome below with
    /// gains green and losses red); and the vendor layout (an NPC behind a counter, a speech bubble, two cards with a
    /// blessing and a curse half and a Sign button each). All geometry is in reference pixels of a 1080 × 2400 frame
    /// centred in the overlay (<see cref="Frame"/>), so shorter screens scale it down and keep the layout.
    /// </summary>
    public static class RunScenes
    {
        public const float RefW = 1080, RefH = 2400, SceneBottom = 1250, BandBottom = 1372;

        public static readonly Color Band = new Color32(0x34, 0x2A, 0x50, 255);
        public static readonly Color BandEdge = new Color32(0x1A, 0x14, 0x2A, 255);
        public static readonly Color Paper = new Color32(0xF6, 0xEA, 0xCE, 255);
        public static readonly Color PaperEdge = new Color32(0xE6, 0xCC, 0x98, 255);
        public static readonly Color Ink = new Color32(0x26, 0x20, 0x34, 255);
        public static readonly Color TileFill = new Color32(0x58, 0x48, 0x9C, 255);
        public static readonly Color TileRim = new Color32(0x8C, 0x7C, 0xE0, 255);
        /// <summary>
        /// The one text palette of every choice-event sheet (run UI glitches #17: event, chest, campfire, quest, outcome): body
        /// ink, gains, losses and chances, each dark enough to read on the cream paper (contrast ≥ 4.5 : 1 on <see cref="Paper"/>).
        /// </summary>
        public static readonly Color Gain = new Color32(0x2A, 0x74, 0x1A, 255);
        public static readonly Color Loss = new Color32(0xB4, 0x1E, 0x2A, 255);
        public static readonly Color ChanceInk = new Color32(0x8E, 0x5C, 0x00, 255);
        /// <summary>Text on the purple option tiles / buttons of a choice sheet (quest options): full white.</summary>
        public static readonly Color OnTile = Color.white;
        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        /// <summary>A gain / loss / chance in its colour (rich text) — every choice sheet colours its words through this.</summary>
        public static string Good(string t) => "<color=#" + Hex(Gain) + ">" + t + "</color>";
        public static string Bad(string t) => "<color=#" + Hex(Loss) + ">" + t + "</color>";
        public static readonly Color SignYellow = new Color32(0xF2, 0xC8, 0x4A, 255);
        public static readonly Color VendorPaper = new Color32(0xFC, 0xEF, 0xCA, 255);
        public static readonly Color VendorInk = new Color32(0x4E, 0x46, 0x22, 255);
        public static readonly Color Blessing = new Color32(0x7A, 0xA0, 0x3A, 255);
        public static readonly Color Curse = new Color32(0xA8, 0x34, 0x5A, 255);

        // ---- the layouts of our events --------------------------------------------------------------------------

        /// <summary>
        /// Our events on the two layouts: trades offered by a character (smuggler, black market, cursed relic, unstable
        /// reactor, alien medic) use the vendor layout; everything else is a scene with a prop (pure).
        /// </summary>
        public static EventLook Look(string eventId) => Look(eventId, null);

        /// <summary>
        /// The layout of an event: our own mapping first; else the content's art (<see cref="EventDef.Art"/>, biome and new
        /// shared events): "npc:&lt;id&gt;" stands at the vendor counter (two options) or on the scene stage, "prop:&lt;id&gt;" on
        /// the scene stage. Art without an imported file draws the existing placeholder (ScreenArt: a generic alien bust, the
        /// wreck prop) and picks up the real file once it is imported under that id (pure).
        /// </summary>
        public static EventLook Look(string eventId, EventDef ev)
        {
            var own = OwnLook(eventId);
            if (own != null || ev == null || string.IsNullOrEmpty(ev.Art)) return own ?? new EventLook { Kind = EventLayoutKind.Scene, Art = "event_wreck" };
            int colon = ev.Art.IndexOf(':');
            string kind = colon > 0 ? ev.Art.Substring(0, colon) : "prop", id = colon > 0 ? ev.Art.Substring(colon + 1) : ev.Art;
            if (kind == "npc")
                return new EventLook { Kind = ev.Options.Count == 2 ? EventLayoutKind.Vendor : EventLayoutKind.Scene, Art = id, Npc = true };
            return new EventLook { Kind = EventLayoutKind.Scene, Art = id };
        }

        /// <summary>The event art of the stage: a character bust for an NPC look, else the prop (drawn placeholders until imported).</summary>
        public static Sprite StageArt(EventLook look) => look == null ? null : look.Npc ? ScreenArt.Character(look.Art) : ScreenArt.Prop(look.Art);

        private static EventLook OwnLook(string eventId)
        {
            switch (eventId)
            {
                case "smuggler": return new EventLook { Kind = EventLayoutKind.Vendor, Art = "black_marketeer", Title = "Smuggler", Line = "Psst. Fell off a starship. Totally legit." };
                case "black_market": return new EventLook { Kind = EventLayoutKind.Vendor, Art = "black_marketeer", Title = "Black Market", Line = "No receipts. No refunds. No questions." };
                case "cursed_relic": return new EventLook { Kind = EventLayoutKind.Vendor, Art = "cursed_vendor", Title = "Cursed Vendor", Line = "Sign here. Ignore the small print. And the screaming." };
                case "unstable_reactor": return new EventLook { Kind = EventLayoutKind.Vendor, Art = "cursed_vendor", Title = "Reactor Peddler", Line = "Raw power! Side effects are mostly the boss's problem." };
                case "alien_medic": return new EventLook { Kind = EventLayoutKind.Vendor, Art = "camp_cook", Title = "Alien Medic", Line = "Tonic's fresh. Mostly. Drink before it wriggles." };
                case "campfire": return new EventLook { Kind = EventLayoutKind.Scene, Art = "campfire_big", Title = "A Moment to Rest", Line = "Wiggle-weary…" };
                case "abandoned_cache": return new EventLook { Kind = EventLayoutKind.Scene, Art = "abandoned_cart" };
                case "treasure_pod": return new EventLook { Kind = EventLayoutKind.Scene, Art = "chest_common" };
                case "echoing_shrine": case "shard_altar": case "resonance": return new EventLook { Kind = EventLayoutKind.Scene, Art = "event_shrine" };
                case "rift_echo": case "meteor_shower": case "lost_explorer": case "gravity_well": case "fuel_leak": case "wild_beast": case "training_dummy": case "boss_scout":
                    return new EventLook { Kind = EventLayoutKind.Scene, Art = "event_wreck" };
                default: return null;
            }
        }

        /// <summary>The icon of an option tile: "icon:&lt;ScreenArt icon&gt;", "prop:&lt;prop&gt;" or "hud:&lt;HudArt icon&gt;" (pure).</summary>
        public static string OptionIcon(EventOption o)
        {
            if (o == null) return "hud:speed";
            if (o.Outcomes.Count == 0 || o.Outcomes.TrueForAll(oc => oc.Effects.Count == 0)) return "hud:speed"; // walk away
            if (o.Outcomes.Count > 1) return "hud:dice"; // a gamble
            foreach (var fx in o.Outcomes[0].Effects)
            {
                switch (fx.Kind)
                {
                    case RunEffectKind.HealPct: return "icon:heal";
                    case RunEffectKind.UpgradeSkill: return "icon:skill_book";
                    case RunEffectKind.GainSkill: case RunEffectKind.GainSpecificSkill: case RunEffectKind.GainSignature: return "icon:upgrade";
                    case RunEffectKind.Loot: case RunEffectKind.PendingReward: return "prop:chest_common";
                    case RunEffectKind.Battle: return "hud:sword";
                    case RunEffectKind.Coins: if (fx.Value > 0) return fx.Value >= 100 ? "icon:coins_big" : "icon:coins_small"; break;
                    case RunEffectKind.StatPercent: if (fx.Value > 0) return fx.Stat == StatId.Hp ? "icon:max_hp" : "icon:upgrade"; break;
                    case RunEffectKind.Xp: return "hud:exp";
                    case RunEffectKind.Shards: return "icon:blessing";
                    case RunEffectKind.Meter: if (fx.Value < 0) return "hud:skull"; break;
                    // Rites: the boss / the enemies ahead weakened, the boss slowed
                    case RunEffectKind.BossStatPct: case RunEffectKind.AwakenRatePct: case RunEffectKind.StrengthenEnemies: if (fx.Value < 0) return "hud:skull"; break;
                }
            }
            return "icon:curse";
        }

        public static Sprite IconSprite(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int i = key.IndexOf(':');
            string kind = i > 0 ? key.Substring(0, i) : "icon", id = i > 0 ? key.Substring(i + 1) : key;
            return kind == "prop" ? ScreenArt.Prop(id) : kind == "hud" ? HudArt.Icon(id) : ScreenArt.Icon(id);
        }

        // ---- option text: gains and losses ----------------------------------------------------------------------

        /// <summary>One line per effect of an option (costs first), each marked good or bad (pure; tested).</summary>
        public static List<KeyValuePair<bool, string>> EffectLines(EventOption o, ContentDb c, ActiveRun run = null)
        {
            var lines = new List<KeyValuePair<bool, string>>();
            void Add(bool good, string t) { if (!lines.Exists(l => l.Value == t)) lines.Add(new KeyValuePair<bool, string>(good, t)); }
            if (o == null) return lines;
            if (o.CoinCost > 0) Add(false, "Pay " + Fmt.Num(o.CoinCost) + " coins");
            if (o.ShardCost > 0) Add(false, "Pay " + o.ShardCost + " portal shard" + (o.ShardCost == 1 ? "" : "s"));
            if (o.Offering) Add(false, "Give up 1 " + OfferingName(o.OfferFamily));
            if (o.Outcomes.Count != 1) return lines;
            foreach (var fx in o.Outcomes[0].Effects)
            {
                double v = fx.Value, a = Math.Abs(v);
                switch (fx.Kind)
                {
                    case RunEffectKind.Coins: if (v != 0) Add(v > 0, v > 0 ? "Gain " + Fmt.Num(a) + " coins" : "Lose " + Fmt.Num(a) + " coins"); break;
                    case RunEffectKind.HealPct: Add(true, "Restore " + Fmt.Pct(a) + " HP"); break;
                    case RunEffectKind.DamagePct: Add(false, "Lose " + Fmt.Pct(a) + " HP"); break;
                    case RunEffectKind.StatPercent:
                    {
                        string stat = StatLabel(fx.Stat);
                        Add(v > 0, v > 0 ? "Gain " + Fmt.Pct(a) + " " + stat : "Lose " + Fmt.Pct(a) + " " + stat);
                        break;
                    }
                    case RunEffectKind.GainSkill: Add(true, "Learn a " + (fx.MinRarity > Rarity.Common ? RunTuning.RunTier(fx.MinRarity) + "+ " : "") + "skill"); break;
                    case RunEffectKind.GainSpecificSkill: Add(true, "Learn " + (c?.Skill(fx.Param)?.Name ?? "a skill")); break;
                    case RunEffectKind.GainSignature: Add(true, "Awaken a new signature"); break;
                    case RunEffectKind.UpgradeSkill: Add(true, fx.Param == RunSystem.RandomSkillParam ? "Improve a random skill" : "Improve 1 skill of your choice"); break;
                    case RunEffectKind.LoseRandomSkill: Add(false, "Lose a random skill"); break;
                    case RunEffectKind.Loot: { int nl = Math.Max(1, (int)v); Add(true, "+" + nl + " reward chest" + (nl == 1 ? "" : "s")); break; }
                    case RunEffectKind.PendingReward:
                        if (fx.Param == RunSystem.BiomeChestParam && fx.Reward != null) Add(true, BiomeChestText(fx.Reward.Id, c, run));
                        else Add(true, "+" + (fx.Reward != null && c != null ? Fmt.Reward(fx.Reward, c) : "loot"));
                        break;
                    case RunEffectKind.Meter:
                    {
                        // the number the result will show: gains scale with the boss's pace (RunSystem.EventAwakening)
                        double m = c != null && run != null ? Math.Abs(RunSystem.EventAwakening(c, run, v)) : a;
                        Add(v < 0, "Boss wakes " + Fmt.Whole(m) + "% " + (v < 0 ? "later" : "sooner"));
                        break;
                    }
                    case RunEffectKind.AwakenRatePct: Add(v < 0, "Boss wakes " + Fmt.Pct(a) + (v < 0 ? " slower" : " faster")); break;
                    case RunEffectKind.BossStatPct: Add(v < 0, BossStatText(fx.Param, v)); break;
                    case RunEffectKind.Battle: Add(false, "Fight " + (fx.Param != null && fx.Param.StartsWith("elite") ? "an elite" : "a foe")); break;
                    case RunEffectKind.Xp: Add(true, "Gain " + Fmt.Num(a) + " XP"); break;
                    case RunEffectKind.Shards: Add(true, "Gain " + Fmt.Num(a) + " portal shard" + (a == 1 ? "" : "s")); break;
                    case RunEffectKind.Danger: Add(false, "Danger +" + Fmt.Whole(a)); break;
                    case RunEffectKind.StrengthenEnemies: Add(v < 0, v < 0 ? "Nearby enemies " + Fmt.Pct(a) + " weaker" : "Nearby enemies +" + Fmt.Pct(a) + " stronger"); break;
                }
            }
            return lines;
        }

        /// <summary>"Boss −20% ATK", "Boss −20% HP", "Boss +15% ATK"; both stats (no param): "Boss +15% stronger" (pure).</summary>
        public static string BossStatText(string param, double v)
        {
            string sign = v < 0 ? "−" : "+";
            string stat = param == RunSystem.BossAtkParam ? " ATK" : param == RunSystem.BossHpParam ? " HP" : (v < 0 ? " weaker" : " stronger");
            return "Boss " + (param == null ? (v < 0 ? "" : "+") : sign) + Fmt.Pct(Math.Abs(v)) + stat;
        }

        /// <summary>A stat's label on event options and results: "ATK", "max HP", "Fire damage", "Counter chance" (pure).</summary>
        public static string StatLabel(StatId s)
        {
            switch (s)
            {
                case StatId.Hp: return "max HP";
                case StatId.Atk: return "ATK";
                case StatId.Def: return "DEF";
                case StatId.Speed: return "Speed";
                default: { var n = SkillText.StatName(s); return n.Length > 0 ? char.ToUpperInvariant(n[0]) + n.Substring(1) : n; }
            }
        }

        /// <summary>"Ice skill", "Radiation skill", "skill with no synergy" (Rite Offerings, pure).</summary>
        public static string OfferingName(SkillFamily f) => f == SkillFamily.None ? "skill with no synergy" : SkillText.FamilyName(f) + " skill";

        /// <summary>A biome chest's line: "Ore chest (40–80)" on the run's level (chapter-scaled), "Ore chest" without a run (pure).</summary>
        public static string BiomeChestText(string currency, ContentDb c, ActiveRun run)
        {
            string name = c != null ? Fmt.CurrencyName(currency, c) : currency;
            if (c == null || run == null) return name + " chest";
            var r = RunSystem.BiomeChestRange(c, c.RunLevel(run.LevelId), currency);
            return name + " chest (" + Fmt.Num(r[0]) + "–" + Fmt.Num(r[1]) + ")"; // unsigned: both numbers in the gain colour
        }

        private static readonly Regex Signed = new Regex(@"([+\-−]\s?\d+(?:[.,]\d+)?%?)", RegexOptions.Compiled);
        private static readonly Regex Number = new Regex(@"(\d+(?:[.,]\d+)?%?)", RegexOptions.Compiled);

        /// <summary>
        /// Rich text of option lines: the text in ink, each line's numbers in its colour (gains green, losses red), one line per
        /// row (pure; tested).
        /// </summary>
        public static string Colored(List<KeyValuePair<bool, string>> lines)
        {
            var sb = new StringBuilder();
            foreach (var l in lines)
            {
                if (sb.Length > 0) sb.Append('\n');
                string hex = ColorUtility.ToHtmlStringRGB(l.Key ? Gain : Loss);
                var re = Signed.IsMatch(l.Value) ? Signed : Number;
                sb.Append(re.Replace(l.Value, m => "<color=#" + hex + ">" + m.Value + "</color>"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// An option's outcome text: its effect lines (gains green, losses red), or for a gamble (several outcomes) its own
        /// description with the chances in gold and the rest of its numbers by clause (pure; tested).
        /// </summary>
        public static string OutcomeText(EventOption o, ContentDb c, ActiveRun run = null)
        {
            if (o == null) return "";
            if (o.Outcomes.Count > 1)
            {
                // a gamble: one line per outcome, "65%: +1 reward chest, Boss wakes 8% sooner"
                double total = 0;
                foreach (var oc in o.Outcomes) total += oc.Weight;
                var sb = new StringBuilder();
                string gold = Hex(ChanceInk);
                foreach (var oc in o.Outcomes)
                {
                    var one = new EventOption { Text = o.Text };
                    one.Outcomes.Add(oc);
                    var oneLines = EffectLines(one, c, run);
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append("<color=#").Append(gold).Append('>').Append(total > 0 ? Fmt.Pct(Math.Round(100 * oc.Weight / total)) : "?").Append(":</color> ");
                    var parts = new List<string>();
                    foreach (var l in oneLines) parts.Add(Colored(new List<KeyValuePair<bool, string>> { l }));
                    sb.Append(parts.Count > 0 ? string.Join(", ", parts) : "nothing");
                }
                return sb.ToString();
            }
            var lines = EffectLines(o, c, run);
            if (lines.Count == 0) return "Nothing happens";
            return Colored(lines);
        }

        private static readonly Regex Chance = new Regex(@"(\d+%):", RegexOptions.Compiled);
        private static readonly Regex BadWords = new Regex(@"\b(lose|trapped|attacks|wakes|stronger|fight)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>"60%: unharmed with a shard. 40%: lose 20% HP." → each chance in gold, each clause's numbers by its tone.</summary>
        public static string ColoredRisk(string risk)
        {
            var parts = Chance.Split(risk);
            var sb = new StringBuilder();
            string gold = Hex(ChanceInk);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (i % 2 == 1) { sb.Append("<color=#").Append(gold).Append('>').Append(p).Append(":</color>"); continue; }
                if (string.IsNullOrEmpty(p)) continue;
                bool bad = BadWords.IsMatch(p);
                string hex = ColorUtility.ToHtmlStringRGB(bad ? Loss : Gain);
                sb.Append(Number.Replace(p, m => "<color=#" + hex + ">" + m.Value + "</color>"));
            }
            return sb.ToString().Trim();
        }

        // ---- frame, stage, panel --------------------------------------------------------------------------------

        /// <summary>
        /// The overlay's reference frame: a 1080 × 2400 rect scaled to fit the layer (s = min(w/1080, h/2400)), centred
        /// horizontally and anchored to the top; returns s.
        /// </summary>
        public static RectTransform Frame(RectTransform root, out float s)
        {
            float w = root.rect.width > 100 ? root.rect.width : RefW, h = root.rect.height > 100 ? root.rect.height : RefH;
            s = Mathf.Min(w / RefW, h / RefH);
            var f = Ui.Rect(root, "Frame");
            f.anchorMin = f.anchorMax = new Vector2(0.5f, 1);
            f.pivot = new Vector2(0.5f, 1);
            f.sizeDelta = new Vector2(RefW * s, RefH * s);
            f.anchoredPosition = Vector2.zero;
            return f;
        }

        /// <summary>An opaque full-screen overlay in the modal layer with its reference frame.</summary>
        public static RectTransform Overlay(Transform layer, string name, out UiModal modal, out RectTransform frame, out float s, Color? background = null)
        {
            var dim = Ui.Panel(layer, background ?? new Color32(0x10, 0x0C, 0x1C, 255), "Modal:" + name);
            Ui.Stretch(dim.rectTransform);
            modal = new UiModal(dim.gameObject) { Body = dim.rectTransform };
            Canvas.ForceUpdateCanvases();
            frame = Frame(dim.rectTransform, out s);
            return dim.rectTransform;
        }

        /// <summary>A rect at reference coordinates of the frame (centre x, y from the top, size).</summary>
        public static RectTransform At(RectTransform rt, float s, float x, float y, float w, float h) => SliceKit.Place(rt, s, x, y, w, h);

        /// <summary>Display text at an exact reference size (no Theme.Type scaling), optionally best-fit down to <paramref name="min"/>.</summary>
        public static Text Text(Transform parent, string text, float s, int size, Color color, TextAnchor align, string name, int min = 0)
        {
            var t = Ui.Display(parent, text, size, color, align, name);
            t.fontSize = Mathf.Max(1, (int)(size * s));
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            if (min > 0) { t.resizeTextForBestFit = true; t.resizeTextMaxSize = t.fontSize; t.resizeTextMinSize = Mathf.Max(1, (int)(min * s)); }
            return t;
        }

        /// <summary>
        /// The stage (reference rest / event): the level's battle arena, the worm on its mount left of centre, its sidekicks
        /// down the left edge, the event's prop at the right, the run HUD over it and the skills book bottom-right.
        /// </summary>
        public static SliceHud Stage(RectTransform root, RectTransform frame, float s, ContentDb c, ActiveRun run, Sprite prop, Action onSkills, Action onCog, float propSize = 420)
        {
            SliceKit.Backdrop(root, ArtCatalog.BattleBackground(null), SceneBottom * s, "Stage");
            frame.SetAsLastSibling(); // the frame's contents over the full-width backdrops

            // the hero group: the worm seated on its mount, sidekicks along the left edge
            var mount = Ui.Panel(frame, Color.white, "Mount");
            mount.sprite = ArtCatalog.SliceMount(run?.Loadout?.MountId) ?? HudArt.Mount();
            mount.preserveAspect = true; mount.raycastTarget = false;
            At(mount.rectTransform, s, 380, 840, 560, 280);
            var worm = Ui.Panel(frame, Color.white, "Worm");
            worm.sprite = ArtCatalog.Hero() ?? HudArt.AlienCritter(new Color32(0xF0, 0x8C, 0xB8, 255));
            worm.preserveAspect = true; worm.raycastTarget = false;
            At(worm.rectTransform, s, 380, 640, 400, 400);
            mount.transform.SetAsLastSibling(); // the saucer's rim in front of the worm's tail
            UiBob.On(worm.rectTransform, 6 * s, 1.6f);
            UiCreature.Attach(worm);
            var aliens = run?.Loadout?.Aliens ?? new List<CombatAlien>();
            for (int i = 0; i < aliens.Count && i < 3; i++)
            {
                var a = aliens[i];
                var fam = c?.AlienType(a.AlienTypeId)?.Family ?? SkillFamily.AlienSynergy;
                var img = Ui.Panel(frame, Color.white, "Sidekick:" + a.AlienTypeId);
                img.sprite = ArtCatalog.Alien(a.AlienTypeId) ?? HudArt.AlienCritter(Theme.FamilyColor(fam));
                img.preserveAspect = true; img.raycastTarget = false;
                At(img.rectTransform, s, 78, 620 + i * 165, 160, 160);
                UiCreature.Attach(img, true);
            }
            if (prop != null)
            {
                // the prop's ground shadow: the board's dark soft shadow (a light glow read as a white blob, #12)
                var shadow = Ui.Panel(frame, new Color(0, 0, 0, 0.38f), "PropShadow");
                shadow.sprite = IsoArt.SoftShadow(); shadow.raycastTarget = false;
                At(shadow.rectTransform, s, 810, 640 + propSize * 0.4f, propSize * 0.95f, propSize * 0.26f);
                var p = Ui.Panel(frame, Color.white, "Prop");
                p.sprite = prop; p.preserveAspect = true; p.raycastTarget = false;
                PropFx.Apply(p, PropFx.LookOfSprite(prop)); // the campfire flickers, the shrine glows (shader, UI material)
                At(p.rectTransform, s, 810, 640, propSize, propSize);
            }
            var hud = Hud(root, c, run, onCog);
            if (onSkills != null) SkillsBook(frame, s, 978, 1150, onSkills);
            return hud;
        }

        /// <summary>
        /// The run HUD of a decision sheet (run UI glitches #2): the same SliceHud call, anchors and width as the board's strip
        /// (full overlay width, from its top edge), so the HUD does not jump when a sheet opens or closes; keeps the XP fill.
        /// Built last over <paramref name="root"/>; callers that add layers later call <c>hud.Root.SetAsLastSibling()</c>.
        /// </summary>
        public static SliceHud Hud(RectTransform root, ContentDb c, ActiveRun run, Action onCog)
        {
            Canvas.ForceUpdateCanvases();
            float w = root.rect.width > 100 ? root.rect.width : RefW;
            var hud = SliceHud.Build(root, w, onCog, null, true);
            if (run != null && c != null)
            {
                var st = RunPresentation.Hud(c, run);
                hud.Show(st, st.Level, st.XpNeed > 0 ? (float)st.Xp / st.XpNeed : 0, RunSystem.BuildSize(run));
                if (hud.Coins != null) hud.Coins.text = Fmt.Num(run.Coins);
            }
            return hud;
        }

        /// <summary>
        /// The skills book button with its "Skills" label (reference: bottom-right of the stage / the shop; every run decision
        /// screen has one: it opens the run's skills on top and returns to the same decision). <paramref name="fromBottom"/>:
        /// <paramref name="y"/> is measured on a 2400 px frame anchored to the bottom edge (overlays that fill any aspect).
        /// </summary>
        public static Button SkillsBook(RectTransform frame, float s, float x, float y, Action onClick, bool fromBottom = false)
        {
            var book = Ui.Button(frame, "", onClick, new Color(0, 0, 0, 0), 150 * s, 170 * s, true, 20);
            book.name = "SkillsBook";
            SliceKit.Place((RectTransform)book.transform, s, x, y, 150, 170, fromBottom);
            var le = book.GetComponent<LayoutElement>(); if (le != null) le.ignoreLayout = true;
            var img = Ui.Panel(book.transform, Color.white, "BookIcon");
            img.sprite = ScreenArt.Icon("skill_book"); img.preserveAspect = true; img.raycastTarget = false;
            Ui.Stretch(img.rectTransform, 6 * s, 0, 6 * s, 40 * s);
            var l = SliceKit.Chunky(book.transform, "Skills", (int)(48 * s), Color.white, TextAnchor.MiddleCenter, "SkillsLabel");
            l.rectTransform.anchorMin = new Vector2(0, 0); l.rectTransform.anchorMax = new Vector2(1, 0);
            l.rectTransform.pivot = new Vector2(0.5f, 0); l.rectTransform.sizeDelta = new Vector2(40 * s, 60 * s); l.rectTransform.anchoredPosition = Vector2.zero;
            return book;
        }

        /// <summary>The bottom panel: the dark title band (white chunky title), the paper panel and its flavour line.</summary>
        public static void Panel(RectTransform root, RectTransform frame, float s, string title, string flavour, int flavourSize = 56)
        {
            var band = Ui.Panel(root, Band, "TitleBand");
            band.raycastTarget = false;
            var brt = band.rectTransform;
            brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(0.5f, 1);
            brt.sizeDelta = new Vector2(0, (BandBottom - SceneBottom) * s); brt.anchoredPosition = new Vector2(0, -SceneBottom * s);
            var edge = Ui.Panel(band.transform, BandEdge, "Edge");
            edge.raycastTarget = false;
            edge.rectTransform.anchorMin = new Vector2(0, 1); edge.rectTransform.anchorMax = Vector2.one; edge.rectTransform.pivot = new Vector2(0.5f, 1);
            edge.rectTransform.sizeDelta = new Vector2(0, 8 * s); edge.rectTransform.anchoredPosition = Vector2.zero;
            var paper = Ui.Panel(root, PaperEdge, "Paper");
            paper.raycastTarget = false;
            var prt = paper.rectTransform;
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero; prt.offsetMax = new Vector2(0, -BandBottom * s);
            frame.SetAsLastSibling();
            var inner = Ui.RoundPanel(frame, Paper, 34 * s, "PaperInner");
            inner.raycastTarget = false;
            float innerBottom = Mathf.Min(RefH - 16, (root.rect.height > 100 ? root.rect.height / s : RefH) - 16);
            At(inner.rectTransform, s, RefW / 2, (BandBottom + 18 + innerBottom) / 2, RefW - 40, innerBottom - BandBottom - 18);
            var t = SliceKit.Chunky(frame, title, (int)(84 * s), Color.white, TextAnchor.MiddleCenter, "Title");
            At(t.rectTransform, s, RefW / 2, (SceneBottom + BandBottom) / 2 + 2, 1040, 120);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.resizeTextForBestFit = true; t.resizeTextMaxSize = t.fontSize; t.resizeTextMinSize = (int)(52 * s);
            if (!string.IsNullOrEmpty(flavour))
            {
                var f = Text(frame, flavour, s, flavourSize, Ink, TextAnchor.MiddleCenter, "Flavour", 36);
                At(f.rectTransform, s, RefW / 2, 1495, 940, 190);
            }
        }

        /// <summary>
        /// One option tile (reference rest / event): the label above, a purple framed icon tile (the button, named
        /// <paramref name="name"/>), the outcome text below. Centre x / tile centre y and the tile size in reference px.
        /// </summary>
        public static Button OptionTile(RectTransform frame, float s, float x, float tileY, float size, string label, Sprite icon, string outcome, bool enabled, Action onClick, string name, int labelSize = 56, int outcomeSize = 50, float textWidth = 400)
        {
            var l = Text(frame, label, s, labelSize, Ink, TextAnchor.LowerCenter, "OptionLabel", 32);
            At(l.rectTransform, s, x, tileY - size / 2 - 70, textWidth + 20, 110);
            var btn = Ui.Button(frame, "", onClick, new Color(1, 1, 1, 0.001f), size * s, size * s, enabled, 20);
            btn.name = name;
            var brt = (RectTransform)btn.transform;
            At(brt, s, x, tileY, size, size + 12);
            var shadow = Ui.RoundPanel(brt, new Color(0, 0, 0, 0.25f), 34 * s, "Shadow");
            shadow.raycastTarget = false;
            Ui.Stretch(shadow.rectTransform, 0, 12 * s, 0, 0);
            shadow.rectTransform.anchoredPosition = new Vector2(0, -12 * s);
            var ink = Ui.RoundPanel(brt, SliceKit.Ink, 34 * s, "Ink");
            ink.raycastTarget = false;
            Ui.Stretch(ink.rectTransform, 0, 0, 0, 12 * s);
            var rim = Ui.RoundPanel(ink.transform, enabled ? TileRim : (Color)new Color32(0x9A, 0x96, 0xA6, 255), 30 * s, "Rim");
            rim.raycastTarget = false;
            Ui.Stretch(rim.rectTransform, 7 * s, 7 * s, 7 * s, 7 * s);
            var fill = Ui.RoundPanel(rim.transform, enabled ? TileFill : (Color)new Color32(0x6E, 0x6A, 0x7C, 255), 24 * s, "Fill");
            fill.raycastTarget = false;
            Ui.Stretch(fill.rectTransform, 9 * s, 9 * s, 9 * s, 9 * s);
            var diamond = Ui.Panel(fill.transform, new Color(0, 0, 0, 0.14f), "Diamond");
            diamond.raycastTarget = false;
            diamond.rectTransform.anchorMin = diamond.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            diamond.rectTransform.sizeDelta = new Vector2(size * 0.5f * s, size * 0.5f * s);
            diamond.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            if (icon != null)
            {
                var img = Ui.Panel(fill.transform, enabled ? Color.white : new Color(0.7f, 0.7f, 0.7f, 1), "Icon");
                img.sprite = icon; img.preserveAspect = true; img.raycastTarget = false;
                Ui.Stretch(img.rectTransform, size * 0.16f * s, size * 0.16f * s, size * 0.16f * s, size * 0.16f * s);
            }
            var o = Text(frame, outcome, s, outcomeSize, Ink, TextAnchor.UpperCenter, "OptionOutcome", 30);
            At(o.rectTransform, s, x, tileY + size / 2 + 26 + 130, textWidth, 260);
            return btn;
        }

        /// <summary>The x centres of n option tiles across the panel.</summary>
        public static float[] Columns(int n)
        {
            if (n <= 1) return new[] { 540f };
            if (n == 2) return new[] { 345f, 735f };
            if (n == 3) return new[] { 190f, 540f, 890f };
            var xs = new float[n];
            for (int i = 0; i < n; i++) xs[i] = 135 + i * (810f / (n - 1));
            return xs;
        }

        // ---- vendor ---------------------------------------------------------------------------------------------

        /// <summary>
        /// The vendor layout's backdrop (reference event-vendor): a dark stone wall, the shop counter and the wooden floor
        /// under the cards; the NPC bust behind the counter; the title ribbon and the speech bubble.
        /// </summary>
        public static void VendorStage(RectTransform root, RectTransform frame, float s, string title, string npc, string line)
        {
            var wall = Ui.Panel(root, Color.white, "Wall");
            wall.sprite = HudArt.Gradient(new Color32(0x2A, 0x22, 0x48, 255), new Color32(0x3C, 0x32, 0x66, 255));
            wall.raycastTarget = false;
            Ui.Stretch(wall.rectTransform);
            // stone blocks in rows
            for (int r = 0; r < 7; r++)
                for (int k = -1; k < 6; k++)
                {
                    var b = Ui.RoundPanel(frame, new Color(1, 1, 1, 0.05f), 18 * s, "Brick");
                    b.raycastTarget = false;
                    At(b.rectTransform, s, k * 230 + (r % 2) * 115 + 60, 60 + r * 150, 210, 128);
                }
            var floor = Ui.Panel(root, Color.white, "Floor");
            floor.sprite = HudArt.Gradient(new Color32(0x3A, 0x22, 0x18, 255), new Color32(0x5A, 0x36, 0x24, 255));
            floor.raycastTarget = false;
            floor.rectTransform.anchorMin = Vector2.zero; floor.rectTransform.anchorMax = Vector2.one;
            floor.rectTransform.offsetMin = Vector2.zero; floor.rectTransform.offsetMax = new Vector2(0, -1150 * s);
            frame.SetAsLastSibling();
            var glow = Ui.Panel(frame, new Color(0.95f, 0.75f, 1f, 0.3f), "Glow");
            glow.sprite = ScreenArt.Glow(); glow.raycastTarget = false;
            At(glow.rectTransform, s, 540, 860, 760, 600);
            var bust = Ui.Panel(frame, Color.white, "Npc:" + npc);
            bust.sprite = ScreenArt.Character(npc); bust.preserveAspect = true; bust.raycastTarget = false;
            At(bust.rectTransform, s, 540, 840, 600, 600);
            // E1: a wooden counter with the vendor's wares on it (potions, crates, skull candles)
            var counter = Ui.Panel(frame, Color.white, "Counter");
            counter.sprite = VendorCounter(); counter.preserveAspect = false; counter.raycastTarget = false;
            At(counter.rectTransform, s, 540, 1170, 1120, 170);
            var wares = VendorWares(npc);
            float[] wx = { 150, 300, 905 };
            for (int i = 0; i < wares.Length; i++)
            {
                var w = Ui.Panel(frame, Color.white, "Ware:" + wares[i]);
                w.sprite = VendorWare(wares[i]); w.preserveAspect = true; w.raycastTarget = false;
                SliceKit.Place(w.rectTransform, s, wx[i], 1045, 150, 150);
                w.rectTransform.pivot = new Vector2(0.5f, 0);
                w.rectTransform.anchoredPosition = new Vector2(wx[i] * s, -1092 * s);
            }
            var rib = Ui.Panel(frame, Color.white, "Ribbon");
            rib.sprite = SliceCards.RedRibbon; rib.preserveAspect = true; rib.raycastTarget = false;
            At(rib.rectTransform, s, 540, 370, 1060, 180);
            var t = SliceKit.Chunky(rib.transform, title, (int)(80 * s), Color.white, TextAnchor.MiddleCenter, "Title");
            Ui.Stretch(t.rectTransform, 0, 0, 0, 14 * s);
            Bubble(frame, s, 395, 585, 520, 190, line, true);
        }

        /// <summary>Which wares stand on a vendor's counter (3; pure).</summary>
        public static string[] VendorWares(string npc)
        {
            switch (npc)
            {
                case "cursed_vendor": return new[] { "candle", "potion_purple", "crate" };
                case "camp_cook": return new[] { "potion_green", "potion_red", "crate" };
                default: return new[] { "crate", "potion_purple", "crate" };
            }
        }

        private static readonly Dictionary<string, Sprite> WareCache = new Dictionary<string, Sprite>();

        /// <summary>The vendor's counter: a plank front under a lighter top ledge (placeholder art).</summary>
        public static Sprite VendorCounter()
        {
            if (WareCache.TryGetValue("counter", out var c) && c != null) return c;
            int w = 560, h = 86;
            var p = new IsoPaint(w, h);
            var wood = (Color)new Color32(0x8A, 0x52, 0x2E, 255);
            var ink = IsoArt.Ink;
            p.Shape(IsoPaint.RoundRect(6, 4, w - 12, h - 26, 8), IsoPaint.VGrad(IsoPaint.Shade(wood, -0.35f), IsoPaint.Shade(wood, -0.1f), 4, h - 22), ink, 4);
            for (int i = 1; i < 6; i++) p.Line(IsoPaint.V(i * w / 6f, 8), IsoPaint.V(i * w / 6f, h - 26), 3, IsoPaint.Shade(wood, -0.45f));
            p.Shape(IsoPaint.RoundRect(2, h - 26, w - 4, 22, 6), IsoPaint.VGrad(IsoPaint.Shade(wood, 0.05f), IsoPaint.Shade(wood, 0.3f), h - 26, h - 4), ink, 4);
            c = p.ToSprite("rs_vendor_counter", IsoPaint.V(w / 2f, h / 2f));
            WareCache["counter"] = c;
            return c;
        }

        /// <summary>A ware on the counter: potion (green / red / purple), crate or skull candle (placeholder art, pivot at its foot).</summary>
        public static Sprite VendorWare(string id)
        {
            if (WareCache.TryGetValue(id, out var sp) && sp != null) return sp;
            const int n = 128;
            var p = new IsoPaint(n, n);
            var ink = IsoArt.Ink;
            if (id.StartsWith("potion"))
            {
                var liquid = id.EndsWith("green") ? (Color)new Color32(0x5C, 0xE0, 0x6A, 255) : id.EndsWith("red") ? (Color)new Color32(0xF0, 0x4A, 0x5A, 255) : (Color)new Color32(0xB8, 0x6C, 0xF8, 255);
                p.Shape(IsoPaint.RoundRect(50, 70, 28, 34, 6), new Color(0.85f, 0.9f, 1f, 1f), ink, 5);
                p.Blob(64, 44, 38, 38, IsoPaint.Radial(IsoPaint.Shade(liquid, 0.35f), IsoPaint.Shade(liquid, -0.25f), 52, 56, 46), ink, 6);
                p.Shape(IsoPaint.RoundRect(52, 100, 24, 16, 4), (Color)new Color32(0xB0, 0x80, 0x50, 255), ink, 4);
                p.Ellipse(50, 56, 7, 10, new Color(1, 1, 1, 0.7f));
            }
            else if (id == "crate")
            {
                var wood = (Color)new Color32(0xB8, 0x7A, 0x44, 255);
                p.Shape(IsoPaint.RoundRect(14, 6, 100, 92, 8), IsoPaint.VGrad(IsoPaint.Shade(wood, -0.2f), IsoPaint.Shade(wood, 0.15f), 6, 98), ink, 6);
                p.Line(IsoPaint.V(20, 12), IsoPaint.V(108, 92), 7, IsoPaint.Shade(wood, -0.35f));
                p.Line(IsoPaint.V(14, 52), IsoPaint.V(114, 52), 5, IsoPaint.Shade(wood, -0.35f));
            }
            else
            {
                // skull candle
                p.Blob(64, 38, 40, 34, (Color)new Color32(0xEE, 0xE6, 0xD4, 255), ink, 6);
                p.Circle(50, 40, 9, ink); p.Circle(78, 40, 9, ink);
                p.Shape(IsoPaint.RoundRect(56, 70, 16, 34, 4), (Color)new Color32(0xF4, 0xEC, 0xC8, 255), ink, 4);
                p.Blob(64, 114, 7, 11, (Color)new Color32(0xFF, 0xB8, 0x3C, 255), ink, 3);
            }
            sp = p.ToSprite("rs_ware_" + id, IsoPaint.V(n / 2f, 0));
            WareCache[id] = sp;
            return sp;
        }

        /// <summary>A white speech bubble with a tail toward the speaker (tail at the bottom-right or bottom-left).</summary>
        public static Text Bubble(RectTransform frame, float s, float x, float y, float w, float h, string text, bool tailRight)
        {
            var edge = Ui.RoundPanel(frame, SliceKit.Ink, 30 * s, "Bubble");
            edge.raycastTarget = false;
            At(edge.rectTransform, s, x, y, w, h);
            var tail = Ui.Panel(edge.transform, SliceKit.Ink, "TailInk");
            tail.raycastTarget = false;
            tail.rectTransform.anchorMin = tail.rectTransform.anchorMax = new Vector2(tailRight ? 0.8f : 0.3f, 0);
            tail.rectTransform.sizeDelta = new Vector2(48 * s, 48 * s);
            tail.rectTransform.anchoredPosition = new Vector2(0, -6 * s);
            tail.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var face = Ui.RoundPanel(edge.transform, Color.white, 26 * s, "Face");
            face.raycastTarget = false;
            Ui.Stretch(face.rectTransform, 6 * s, 6 * s, 6 * s, 6 * s);
            var tf = Ui.Panel(edge.transform, Color.white, "Tail");
            tf.raycastTarget = false;
            tf.rectTransform.anchorMin = tf.rectTransform.anchorMax = new Vector2(tailRight ? 0.8f : 0.3f, 0);
            tf.rectTransform.sizeDelta = new Vector2(36 * s, 36 * s);
            tf.rectTransform.anchoredPosition = new Vector2(0, 2 * s);
            tf.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var l = Text(face.transform, text, s, 50, Ink, TextAnchor.MiddleCenter, "BubbleText", 30);
            Ui.Stretch(l.rectTransform, 26 * s, 14 * s, 26 * s, 14 * s);
            return l;
        }

        /// <summary>
        /// One vendor card (reference event-vendor): cream, the blessing emblem and its text on top, a dashed divider, the
        /// curse emblem and its text below; a yellow Sign button under it (named <paramref name="name"/>).
        /// </summary>
        public static Button VendorCard(RectTransform frame, float s, float x, string blessing, string curse, string sign, bool enabled, Action onClick, string name)
        {
            const float top = 1190, bottom = 2170, w = 510;
            var edge = Ui.RoundPanel(frame, SliceKit.Ink, 24 * s, "VendorCard");
            edge.raycastTarget = false;
            At(edge.rectTransform, s, x, (top + bottom) / 2, w, bottom - top);
            var face = Ui.RoundPanel(edge.transform, enabled ? VendorPaper : (Color)new Color32(0xD8, 0xD2, 0xC4, 255), 20 * s, "Face");
            face.raycastTarget = false;
            Ui.Stretch(face.rectTransform, 6 * s, 6 * s, 6 * s, 6 * s);
            var lip = Ui.RoundPanel(face.transform, new Color32(0xE8, 0xD8, 0xA8, 255), 18 * s, "Lip");
            lip.raycastTarget = false;
            Ui.Stretch(lip.rectTransform, 12 * s, 12 * s, 12 * s, 12 * s);
            var inner = Ui.RoundPanel(lip.transform, enabled ? VendorPaper : (Color)new Color32(0xD8, 0xD2, 0xC4, 255), 16 * s, "Inner");
            inner.raycastTarget = false;
            Ui.Stretch(inner.rectTransform, 4 * s, 4 * s, 4 * s, 4 * s);
            float mid = (top + bottom) / 2;
            Emblem(frame, s, x, top + 120, "blessing", "Blessing");
            var bt = Text(frame, blessing, s, 54, VendorInk, TextAnchor.UpperLeft, "BlessingText", 30);
            At(bt.rectTransform, s, x, top + 225 + 120, w - 64, 240);
            for (int i = 0; i < 11; i++)
            {
                var d = Ui.Panel(frame, new Color32(0xE0, 0xCC, 0x98, 255), "Dash");
                d.raycastTarget = false;
                At(d.rectTransform, s, x - w / 2 + 40 + i * 42, mid + 10, 26, 6);
            }
            Emblem(frame, s, x, mid + 130, "curse", "Curse");
            var ct = Text(frame, curse, s, 54, VendorInk, TextAnchor.UpperLeft, "CurseText", 30);
            At(ct.rectTransform, s, x, mid + 235 + 120, w - 64, 240);
            var btn = Ui.Button(frame, "", onClick, SignYellow, 365 * s, 150 * s, enabled, 20);
            btn.name = name;
            At((RectTransform)btn.transform, s, x, 2282, 365, 150);
            UiShapes.Outline((RectTransform)btn.transform, SliceKit.Ink, Theme.RadiusButton, 6);
            var st = SliceKit.Chunky(btn.transform, sign, (int)(74 * s), Color.white, TextAnchor.MiddleCenter, "SignText");
            Ui.Stretch(st.rectTransform, 0, 0, 0, 8 * s);
            return btn;
        }

        private static void Emblem(RectTransform frame, float s, float x, float y, string id, string name)
        {
            var img = Ui.Panel(frame, Color.white, name);
            img.sprite = ScreenArt.Icon(id); img.preserveAspect = true; img.raycastTarget = false;
            At(img.rectTransform, s, x, y, 330, 230);
        }

        /// <summary>The vendor card's two halves from an option: gains (blessing) and costs (curse); a decline reads as a quiet card.</summary>
        public static void Halves(EventOption o, ContentDb c, out string blessing, out string curse, ActiveRun run = null)
        {
            var lines = EffectLines(o, c, run);
            var good = lines.FindAll(l => l.Key);
            var bad = lines.FindAll(l => !l.Key);
            blessing = good.Count > 0 ? Colored(good) : "Nothing changes";
            curse = bad.Count > 0 ? Colored(bad) : good.Count > 0 ? "No strings attached. Suspicious." : "Gain nothing at all.";
        }
    }
}
