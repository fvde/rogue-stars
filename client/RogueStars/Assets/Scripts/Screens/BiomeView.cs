using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Biome identity on screen (docs/design/biomes): the biome reward chip ("×2 Ore"), the synergy weather chips ("Heat +25%"
    /// green, "Chill −50%" red), the "NEW BIOME" chip on a biome's first visit, the "+25% here" / "−50% here" badge on skill cards
    /// whose synergy the level's weather touches, and the level-start weather banner. Code-drawn pills only (Ui.Chip), no art.
    /// </summary>
    public static class BiomeView
    {
        public static readonly Color BoostColor = Theme.Good, WeakColor = Theme.Bad, RewardColor = Theme.Gold, NewColor = Theme.AccentLight;

        /// <summary>"×2 Ore" for a main level with a biome reward, else null.</summary>
        public static string RewardText(ContentDb c, LevelDef level) =>
            level?.BiomeResource == null ? null : "×" + Fmt.Num(c.Campaign.BiomeRewardMult) + " " + Fmt.CurrencyName(level.BiomeResource, c);

        public static string BoostText(BattleWeather w) => w == null ? null : SkillText.WeatherName(w.Boost) + " +" + Fmt.Num(w.BoostPct) + "%";
        public static string WeakText(BattleWeather w) => w == null ? null : SkillText.WeatherName(w.Weak) + " −" + Fmt.Num(w.WeakPct) + "%";

        /// <summary>"Shock +25% · Alien −25%" with the boost green and the weakening red (rich text).</summary>
        public static string WeatherRich(BattleWeather w) => w == null ? null :
            "<color=#" + ColorUtility.ToHtmlStringRGB(BoostColor) + ">" + BoostText(w) + "</color> · <color=#" + ColorUtility.ToHtmlStringRGB(WeakColor) + ">" + WeakText(w) + "</color>";

        /// <summary>The weather of the run's level (main campaign Normal / Overcharged only, like RunSystem's battles), else null.</summary>
        public static BattleWeather RunWeather(ContentDb c, ActiveRun run)
        {
            if (run == null || (run.Mode != RunMode.Normal && run.Mode != RunMode.Overcharged)) return null;
            return c.Weather(c.RunLevel(run.LevelId));
        }

        /// <summary>The weather badge of a skill of <paramref name="family"/> under <paramref name="w"/>: "+25% here", "−50% here" or null.</summary>
        public static string Badge(BattleWeather w, SkillFamily family)
        {
            if (w == null) return null;
            var f = family == SkillFamily.Crit ? SkillFamily.None : family;
            if (f == w.Boost) return "+" + Fmt.Num(w.BoostPct) + "% here";
            if (f == w.Weak) return "−" + Fmt.Num(w.WeakPct) + "% here";
            return null;
        }

        /// <summary>Whether a badge text is the boost (green) rather than the weakening (red).</summary>
        public static bool IsBoost(string badge) => badge != null && badge.StartsWith("+");

        /// <summary>
        /// The chips of a main level: [NEW BIOME] [×2 Ore] [Heat +25%] [Chill −50%] (weather only where the level has it). Returns the
        /// chip texts with their colours (pure: the portal map, the level card and tests share it).
        /// </summary>
        public static List<KeyValuePair<string, Color>> Chips(ContentDb c, LevelDef level, bool withNew = true)
        {
            var list = new List<KeyValuePair<string, Color>>();
            if (level == null || string.IsNullOrEmpty(level.Biome)) return list;
            if (withNew && level.BiomeDebut) list.Add(new KeyValuePair<string, Color>("NEW BIOME", NewColor));
            var reward = RewardText(c, level);
            if (reward != null) list.Add(new KeyValuePair<string, Color>(reward, RewardColor));
            var w = c.Weather(level);
            if (w != null)
            {
                list.Add(new KeyValuePair<string, Color>(BoostText(w), BoostColor));
                list.Add(new KeyValuePair<string, Color>(WeakText(w), WeakColor));
            }
            return list;
        }

        /// <summary>One code-drawn chip (dark fill, coloured text and rim) sized to its text.</summary>
        public static RectTransform Chip(Transform parent, string text, Color color, int fontSize = 22, float height = 42, string name = null)
        {
            // sized by its parent row (HorizontalLayoutGroup with childControlWidth: the chip's preferred width)
            return Ui.Chip(parent, text, new Color(0.08f, 0.05f, 0.12f, 0.85f), color, fontSize, height, color, name ?? "BiomeChip:" + text);
        }

        /// <summary>A row of the level's chips (Ui.ChipRow, named "BiomeChips"); null when the level has none.</summary>
        public static RectTransform ChipRow(Transform parent, ContentDb c, LevelDef level, int fontSize = 22, float height = 42, bool withNew = true)
        {
            var chips = Chips(c, level, withNew);
            if (chips.Count == 0) return null;
            var row = Ui.ChipRow(parent, height, 8);
            row.name = "BiomeChips";
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.childForceExpandWidth = false;
            h.childControlWidth = true;
            foreach (var kv in chips) Chip(row, kv.Key, kv.Value, fontSize, height);
            return row;
        }

        /// <summary>The weather badge on a skill card (top right corner of its panel), named "WeatherBadge".</summary>
        public static RectTransform CardBadge(RectTransform card, float s, string badge)
        {
            if (card == null || badge == null) return null;
            var color = IsBoost(badge) ? BoostColor : WeakColor;
            var chip = Ui.Chip(card, badge, color, Color.white, (int)(24 * s), 42 * s, SliceKit.Ink, "WeatherBadge");
            chip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            chip.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            chip.anchorMin = chip.anchorMax = new Vector2(1, 0);
            chip.pivot = new Vector2(1, 0);
            chip.sizeDelta = new Vector2(0, 42 * s);
            chip.anchoredPosition = new Vector2(-30 * s, 18 * s);
            return chip;
        }

        /// <summary>
        /// The level-start weather banner (shown with the signature pick, from the level weather starts): the biome's name with
        /// "NEW BIOME" on its first visit, and the two weather chips. Named "WeatherBanner"; null without weather.
        /// </summary>
        public static RectTransform Banner(RectTransform root, float s, ContentDb c, LevelDef level, float y)
        {
            var w = c.Weather(level);
            if (w == null) return null;
            var band = Ui.RoundPanel(root, new Color(0.08f, 0.05f, 0.14f, 0.9f), 26 * s, "WeatherBanner");
            band.raycastTarget = false;
            var rt = band.rectTransform;
            SliceKit.Place(rt, s, 540, y, 940, 132);
            UiShapes.Outline(rt, SliceKit.Ink, 26 * s, 4);
            string head = level.Name.ToUpperInvariant() + (level.BiomeDebut ? " · NEW BIOME" : "");
            var title = SliceKit.Chunky(rt, head, (int)(40 * s), level.BiomeDebut ? NewColor : Color.white, TextAnchor.MiddleCenter, "WeatherTitle");
            title.rectTransform.anchorMin = new Vector2(0, 1); title.rectTransform.anchorMax = new Vector2(1, 1); title.rectTransform.pivot = new Vector2(0.5f, 1);
            title.rectTransform.sizeDelta = new Vector2(0, 58 * s); title.rectTransform.anchoredPosition = new Vector2(0, -6 * s);
            var row = Ui.Rect(rt, "WeatherChips");
            row.anchorMin = new Vector2(0, 0); row.anchorMax = new Vector2(1, 0); row.pivot = new Vector2(0.5f, 0);
            row.sizeDelta = new Vector2(0, 50 * s); row.anchoredPosition = new Vector2(0, 12 * s);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter; h.spacing = 14 * s;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            Chip(row, BoostText(w), BoostColor, (int)(28 * s), 50 * s, "WeatherBoost");
            Chip(row, WeakText(w), WeakColor, (int)(28 * s), 50 * s, "WeatherWeak");
            var reward = RewardText(c, level);
            if (reward != null) Chip(row, reward, RewardColor, (int)(28 * s), 50 * s, "BiomeReward");
            return rt;
        }

        /// <summary>"Ore 250 (×2 biome)": the run's biome-doubled reward, summed over its lines; null when none.</summary>
        public static string ResultLine(ContentDb c, RunSummary sum)
        {
            if (sum?.Rewards == null) return null;
            string id = null;
            long total = 0;
            foreach (var r in sum.Rewards)
                if (r.Biome && r.Reward != null) { id = r.Reward.Id; total += r.Reward.Amount; }
            if (id == null) return null;
            return Fmt.CurrencyName(id, c) + " " + Fmt.Num(total) + " (×" + Fmt.Num(c.Campaign.BiomeRewardMult) + " biome)";
        }
    }
}
