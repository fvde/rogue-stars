using System;
using System.Collections.Generic;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>What a skill tooltip explains: the skill, the level shown, and an optional upgrade (from → to).</summary>
    public struct SkillTip
    {
        public string SkillId;
        /// <summary>The level the text describes (≥ 1).</summary>
        public int Level;
        /// <summary>An offered level-up: the owned level and the offered one (UpgradeTo 0 = none; then the next level's
        /// improvement is shown while the skill can still level).</summary>
        public int UpgradeFrom, UpgradeTo;
        /// <summary>The tier label instead of the skill's run tier ("Signature").</summary>
        public string TierOverride;

        public static SkillTip Of(string skillId, int level) => new SkillTip { SkillId = skillId, Level = Math.Max(1, level) };
    }

    /// <summary>
    /// The one skill tile of the game (owner playtest 0.1.224: "visualize skills with the same boxes everywhere, the art
    /// filling the centre fully, a tap tooltip with the full text"): a thick frame in the skill's tier colour (Common grey,
    /// Legendary orange, Mythic red; signatures their own colour) with an ink edge, the face filled edge to edge by the
    /// skill's icon art (ArtCatalog <c>Art/Skills/&lt;id&gt;</c>, clipped to the face's rounded corners, no glyph) or, while
    /// art is missing, a code-drawn face (tier-tinted slate, shine, the family initial / ★). Used by the battle skill grid,
    /// the draft / mythic / signature / shop / Train cards, the skills book and skill lines of event results. Tapping a
    /// tile where a tap does not already pick opens <see cref="SkillTooltip"/>.
    /// Parts: root "Slot:&lt;id&gt;" (or the given name), "RarityBorder" (tier colour), "Face" (mask), "IconArt" or "Glyph".
    /// </summary>
    public static class SkillTileView
    {
        /// <summary>Frame thickness and corner radius as shares of the tile's size.</summary>
        public const float FrameShare = 0.075f, RadiusShare = 0.16f;

        /// <summary>
        /// Builds a tile of <paramref name="size"/> px (square) under <paramref name="parent"/>; the caller positions the
        /// returned rect (its sizeDelta is set to the size, anchors centred).
        /// </summary>
        public static RectTransform Build(Transform parent, string skillId, Color tierColor, SkillFamily family, float size, string name = null, Sprite art = null)
        {
            var rt = Ui.Rect(parent, name ?? "Slot:" + skillId);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            float r = size * RadiusShare, f = Mathf.Max(3, size * FrameShare);
            var border = Ui.RoundPanel(rt, tierColor, r, "RarityBorder");
            border.raycastTarget = false;
            Ui.Stretch(border.rectTransform);
            UiShapes.Outline(border.rectTransform, IsoArt.Ink, r, Mathf.Max(2, size * 0.022f));
            // a lighter top lip on the frame (the box style of the skills book)
            var lip = Ui.RoundPanel(border.transform, Color.Lerp(tierColor, Color.white, 0.35f), r, "Lip");
            lip.raycastTarget = false;
            Ui.Stretch(lip.rectTransform, f * 0.35f, f * 0.25f, f * 0.35f, size * 0.55f);
            var face = Ui.RoundPanel(rt, Color.Lerp(new Color32(0x3A, 0x3E, 0x5A, 255), tierColor, 0.25f), Mathf.Max(2, r - f * 0.6f), "Face");
            face.raycastTarget = false;
            Ui.Stretch(face.rectTransform, f, f, f, f * 1.15f);
            art = art != null ? art : ArtCatalog.SkillIcon(skillId);
            if (art != null)
            {
                // the art fills the face fully, clipped to its rounded corners
                face.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var img = Ui.Panel(face.transform, Color.white, "IconArt");
                img.sprite = art;
                img.preserveAspect = Mathf.Abs(art.rect.width / Mathf.Max(1, art.rect.height) - 1) > 0.08f; // square art fills the face edge to edge
                img.raycastTarget = false;
                Ui.Stretch(img.rectTransform);
            }
            else
            {
                var shine = Ui.RoundPanel(face.transform, new Color(1, 1, 1, 0.13f), Mathf.Max(2, r - f), "Shine");
                shine.raycastTarget = false;
                Ui.Stretch(shine.rectTransform, size * 0.05f, size * 0.05f, size * 0.05f, size * 0.38f);
                var col = family == SkillFamily.None ? (Color)new Color32(0xFF, 0xE0, 0x5A, 255) : Color.Lerp(Theme.FamilyColor(family), Color.white, 0.3f);
                var glyph = SliceKit.Chunky(face.transform, family == SkillFamily.None ? "★" : SkillText.FamilyName(family).Substring(0, 1), (int)(size * 0.46f), col, TextAnchor.MiddleCenter, "Glyph");
                Ui.Stretch(glyph.rectTransform);
            }
            return rt;
        }

        /// <summary>A tile for a skill of the content (tier colour from its rarity).</summary>
        public static RectTransform Build(Transform parent, ContentDb c, string skillId, float size, string name = null)
        {
            var s = skillId != null ? c?.Skill(skillId) : null;
            return Build(parent, skillId, RunPresentation.SkillColor(s), s?.Family ?? SkillFamily.None, size, name);
        }

        /// <summary>
        /// Makes <paramref name="tile"/> open the skill's tooltip on a tap (an invisible raycast target + a Button; named
        /// "TipButton"). Use only where a tap does not already pick the skill.
        /// </summary>
        public static Button Tappable(RectTransform tile, ContentDb c, Func<SkillTip> tip)
        {
            var hit = tile.GetComponent<Image>();
            if (hit == null) { hit = tile.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0); }
            hit.raycastTarget = true;
            var b = tile.GetComponent<Button>() ?? tile.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => SkillTooltip.Show(tile, c, tip()));
            return b;
        }
    }

    /// <summary>
    /// The skill tooltip: a popover next to the tapped tile with the skill's name, tier and level, its full text
    /// (SkillText.Full at that level) and the upgrade line (an offered level-up, or what the next level adds). A tap
    /// anywhere (also on the popover) closes it. One at a time; lives on the root canvas over everything.
    /// </summary>
    public static class SkillTooltip
    {
        private static GameObject _open;

        public static bool IsOpen => _open != null;
        /// <summary>The open popover (tests).</summary>
        public static RectTransform Current => _open != null ? (RectTransform)_open.transform : null;

        /// <summary>Heading, caption and body lines of the tooltip (pure; tested).</summary>
        public static List<string> Lines(ContentDb c, SkillTip tip)
        {
            var lines = new List<string>();
            var s = c?.Skill(tip.SkillId);
            if (s == null) { lines.Add(tip.SkillId ?? "?"); return lines; }
            int level = Math.Max(1, tip.Level);
            int max = c.RunTuning.MaxSkillLevelOf(s.Rarity);
            lines.Add(SkillText.Title(c, s, level));
            string tier = tip.TierOverride ?? RunTuning.RunTier(s.Rarity).ToString();
            string title = lines[0];
            lines.Add(tier + (s.Family != SkillFamily.None ? " · " + SkillText.FamilyName(s.Family) : "")
                      + (title.Contains(" Lv ") ? "" : " · Lv " + level + "/" + Math.Max(level, max))); // the level once
            lines.Add(SkillText.Full(c, s, level));
            if (tip.UpgradeTo > tip.UpgradeFrom && tip.UpgradeFrom > 0)
                lines.Add("Level " + tip.UpgradeFrom + " → " + tip.UpgradeTo + ": " + SkillText.UpgradeFull(c, s, tip.UpgradeFrom, tip.UpgradeTo));
            else if (tip.TierOverride == null && level < max)
            {
                string up = SkillText.UpgradeFull(c, s, level, level + 1);
                if (!string.IsNullOrEmpty(up)) lines.Add("Next level: " + up);
            }
            return lines;
        }

        public static void Close()
        {
            if (_open != null) Ui.DestroyGo(_open);
            _open = null;
        }

        /// <summary>Opens the tooltip of <paramref name="tip"/> by <paramref name="anchor"/> (above it, or below near the top).</summary>
        public static RectTransform Show(RectTransform anchor, ContentDb c, SkillTip tip)
        {
            Close();
            if (anchor == null) return null;
            var canvas = anchor.GetComponentInParent<Canvas>();
            var root = canvas != null ? (RectTransform)canvas.rootCanvas.transform : (RectTransform)anchor.root;
            // a full-screen catcher: any tap closes
            var layer = Ui.Panel(root, new Color(0, 0, 0, 0.001f), "SkillTooltip");
            Ui.Stretch(layer.rectTransform);
            layer.transform.SetAsLastSibling();
            var catcher = layer.gameObject.AddComponent<Button>();
            catcher.transition = Selectable.Transition.None;
            catcher.onClick.AddListener(Close);
            _open = layer.gameObject;

            var lines = Lines(c, tip);
            float w = Mathf.Min(760, root.rect.width > 100 ? root.rect.width - 40 : 760);
            var panel = Ui.RoundPanel(layer.transform, new Color32(0x22, 0x1C, 0x30, 250), 26, "Popover");
            panel.raycastTarget = false;
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0);
            var s = c?.Skill(tip.SkillId);
            var tierCol = tip.TierOverride != null ? SignaturePick.SignatureColor : RunPresentation.SkillColor(s);
            UiShapes.Outline(prt, tierCol, 26, 5);
            var col = prt.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(30, 30, 24, 26);
            col.spacing = 8;
            col.childControlWidth = col.childControlHeight = true;
            col.childForceExpandWidth = true; col.childForceExpandHeight = false;
            var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            prt.sizeDelta = new Vector2(w, 0);
            for (int i = 0; i < lines.Count; i++)
            {
                Text t;
                if (i == 0) t = SliceKit.Chunky(prt, lines[i], 46, Color.white, TextAnchor.MiddleLeft, "TipTitle");
                else if (i == 1) t = Ui.Label(prt, lines[i], 26, Color.Lerp(tierCol, Color.white, 0.45f), TextAnchor.MiddleLeft, "TipCaption");
                else
                {
                    t = Ui.Label(prt, SliceCards.Keywords(lines[i]).Replace(ColorUtility.ToHtmlStringRGB(SliceCards.KeyBlue), "7FB2FF").Replace(ColorUtility.ToHtmlStringRGB(SliceCards.KeyGreen), "8CEB7A"), 30,
                        i == 2 ? new Color(0.95f, 0.93f, 0.98f) : new Color32(0x9C, 0xF0, 0xB5, 255), TextAnchor.UpperLeft, i == 2 ? "TipText" : "TipUpgrade");
                    t.supportRichText = true;
                }
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.raycastTarget = false;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(prt);
            // above the tile, or below it when there is no room; clamped into the screen
            Vector2 local = root.InverseTransformPoint(anchor.TransformPoint(anchor.rect.center));
            float ah = anchor.rect.height * Mathf.Abs(anchor.lossyScale.y / Mathf.Max(0.0001f, root.lossyScale.y));
            float h = prt.rect.height;
            float half = root.rect.height / 2;
            float y = local.y + ah / 2 + 16;
            if (y + h > half - 20) { prt.pivot = new Vector2(0.5f, 1); y = local.y - ah / 2 - 16; }
            float hw = root.rect.width / 2 - w / 2 - 20;
            prt.anchoredPosition = new Vector2(Mathf.Clamp(local.x, -Mathf.Max(0, hw), Mathf.Max(0, hw)), y);
            UiTween.PopIn(prt, 0.12f);
            return prt;
        }
    }
}
