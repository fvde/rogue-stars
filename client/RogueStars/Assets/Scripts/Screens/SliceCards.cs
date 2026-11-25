using System;
using System.Text.RegularExpressions;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using UnityEngine;
using UnityEngine.UI;
using static RogueStars.Client.World.IsoPaint;

namespace RogueStars.Client.Screens
{
    /// <summary>What one skill card shows (level-up draft, mythic choice, shop perks).</summary>
    public sealed class SkillCardSpec
    {
        public string Id;
        public string Tier;
        public Color TierColor;
        public SkillFamily Family;
        public Sprite IconArt;
        public string Title;
        public string Body;
        /// <summary>A chip on the name ribbon ("LEVEL UP", "SYNERGY", "UPGRADE"), its name and colour; null = none.</summary>
        public string Chip, ChipName;
        public Color ChipColor;
        public bool Dim;
        /// <summary>The skill tooltip the card's "?" (and, on a card that does not pick on tap, its tile) opens; null = none / onInfo.</summary>
        public SkillTip? Tip;
        public ContentDb TipContent;
    }

    /// <summary>
    /// The card family of the vertical slice (reference 2): a full-screen dimmed overlay over the board, the hero large at
    /// the top, a ribbon title ("LEVEL UP!" gold / "MYTHIC!" red / "SHOP" blue), a white subtitle, cards with a tier tag, a
    /// framed icon tile, the name on a ribbon and a cream panel whose keywords are coloured (numbers green, statuses and
    /// stats blue), a "?" button, and a purple reroll button.
    /// </summary>
    public static class SliceCards
    {
        public static readonly Color Cream = new Color32(0xF2, 0xE6, 0xD6, 255);
        public static readonly Color CreamEdge = new Color32(0xC8, 0xBC, 0xAE, 255);
        public static readonly Color NameRibbon = new Color32(0xB4, 0xA3, 0x90, 255);
        public static readonly Color BodyInk = new Color32(0x2A, 0x20, 0x2E, 255);
        public static readonly Color KeyGreen = new Color32(0x3E, 0xB0, 0x48, 255);
        public static readonly Color KeyBlue = new Color32(0x2A, 0x5E, 0xD8, 255);
        public static readonly Color Purple = new Color32(0xB8, 0x78, 0xF2, 255);

        private static readonly Regex Numbers = new Regex(@"([+\-−]?\d+(?:[.,]\d+)?\s?(?:%|×|x\b)?)", RegexOptions.Compiled);
        private static readonly Regex Keys = new Regex(@"\b(Heat|Supernova|Radiation|Burn|Freeze|Frozen|Chill|Chilled|Poison|Bleed|Stun|Stuns|Shock|Shield|Heal|HP|ATK|DEF|Crit|Crits|Combo|Counter|Dodge|Rage|Lifesteal|Thorns|SPD|Speed|Exposed|Stagger|Delay|Delayed|Charm|Arc|Arcs|Echo|Echoes|Signature)\b", RegexOptions.Compiled);
        /// <summary>Warning colour for a skill's costs (trade-offs: "−15% max HP", "You take +20% damage").</summary>
        public static readonly Color CostRed = new Color32(0xD2, 0x3C, 0x46, 255);

        /// <summary>
        /// Rich text with the keywords coloured: numbers (and %) green, statuses / stats blue; a cost marked by SkillText
        /// (CostStart … CostEnd) is all in the warning colour (pure; tested).
        /// </summary>
        public static string Keywords(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("→", "»"); // the display font has no arrow; » keeps the card in the chunky font
            var sb = new System.Text.StringBuilder();
            string r = ColorUtility.ToHtmlStringRGB(CostRed);
            int i = 0;
            while (i < text.Length)
            {
                int s = text.IndexOf(SkillText.CostStart, i);
                if (s < 0) { sb.Append(Plain(text.Substring(i))); break; }
                sb.Append(Plain(text.Substring(i, s - i)));
                int e = text.IndexOf(SkillText.CostEnd, s + 1);
                if (e < 0) e = text.Length;
                sb.Append("<color=#").Append(r).Append('>').Append(text, s + 1, e - s - 1).Append("</color>");
                i = e + 1;
            }
            return sb.ToString();
        }

        private static string Plain(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string g = ColorUtility.ToHtmlStringRGB(KeyGreen), b = ColorUtility.ToHtmlStringRGB(KeyBlue);
            var withKeys = Keys.Replace(text, m => "\u0001" + m.Value + "\u0004");
            var withNums = Numbers.Replace(withKeys, m => "<color=#" + g + ">" + m.Value + "</color>");
            return withNums.Replace("\u0001", "<color=#" + b + ">").Replace("\u0004", "</color>");
        }

        /// <summary>Card type (reference 2 at 1080 px): the description and the name on the ribbon.</summary>
        public const int BodySize = 46, TitleSize = 58;

        private static Sprite _gold, _red, _blue;

        /// <summary>The ribbon banner (gold, red for a mythic choice, blue for the shop) with folded ends.</summary>
        public static Sprite Ribbon(Color c)
        {
            int w = 1000, h = 170;
            var p = new IsoPaint(w, h);
            var ink = IsoArt.Ink;
            var dark = Shade(c, -0.3f);
            // folded tails
            foreach (bool right in new[] { false, true })
            {
                float X(float x) => right ? w - x : x;
                p.Shape(new[] { V(X(130), 40), V(X(4), 40), V(X(46), 82), V(X(4), 124), V(X(130), 124) }, dark, ink, 5);
            }
            p.Shape(new[] { V(70, 28), V(w - 70, 28), V(w - 90, 150), V(90, 150) }, VGrad(Shade(c, -0.12f), Shade(c, 0.18f), 28, 150), ink, 6);
            p.Line(V(96, 136), V(w - 96, 136), 6, new Color(1, 1, 1, 0.35f));
            p.Line(V(90, 40), V(w - 90, 40), 5, Shade(c, -0.25f));
            return p.ToSprite("rs_ribbon", V(w / 2f, h / 2f));
        }

        public static Sprite GoldRibbon => _gold != null ? _gold : (_gold = Ribbon(new Color32(0xE6, 0xB4, 0x30, 255)));
        public static Sprite RedRibbon => _red != null ? _red : (_red = Ribbon(new Color32(0xE0, 0x46, 0x50, 255)));
        public static Sprite BlueRibbon => _blue != null ? _blue : (_blue = Ribbon(new Color32(0x4C, 0x9C, 0xE8, 255)));

        /// <summary>A full-screen dim overlay in <paramref name="layer"/> (the board stays visible behind it).</summary>
        public static RectTransform Overlay(Transform layer, string name, out UiModal modal, float alpha = 0.8f)
        {
            var dim = Ui.Panel(layer, new Color(0.03f, 0.05f, 0.08f, alpha), "Modal:" + name);
            Ui.Stretch(dim.rectTransform);
            modal = new UiModal(dim.gameObject);
            modal.Body = dim.rectTransform;
            return dim.rectTransform;
        }

        /// <summary>The header: the hero large, the ribbon with its title and the subtitle (reference px, top-anchored).</summary>
        public static void Header(RectTransform root, float s, Sprite ribbon, string title, string titleName, string subtitle, float heroY = 520, float ribbonY = 760)
        {
            var hero = ArtCatalog.Hero();
            if (hero != null && heroY > 0)
            {
                var h = Ui.Panel(root, Color.white, "Hero");
                h.sprite = hero;
                h.preserveAspect = true;
                h.raycastTarget = false;
                SliceKit.Place(h.rectTransform, s, 540, heroY, 440, 440);
            }
            var rib = Ui.Panel(root, Color.white, "Ribbon");
            rib.sprite = ribbon;
            rib.preserveAspect = true;
            rib.raycastTarget = false;
            SliceKit.Place(rib.rectTransform, s, 540, ribbonY, 1010, 172);
            var t = SliceKit.Chunky(rib.transform, title, (int)(76 * s), Color.white, TextAnchor.MiddleCenter, titleName);
            Ui.Stretch(t.rectTransform, 0, 0, 0, 14 * s);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var sub = SliceKit.Chunky(root, subtitle, (int)(46 * s), Color.white, TextAnchor.MiddleCenter, "Subtitle");
                SliceKit.Place(sub.rectTransform, s, 540, ribbonY + 105, 1000, 60);
            }
        }

        /// <summary>
        /// One card (reference 2) centred at reference y <paramref name="y"/>: the whole card is the pick button (named
        /// <paramref name="buttonName"/>, content under "Draft:" + id). <paramref name="height"/> 280 = draft, smaller = shop.
        /// Returns the card's root.
        /// </summary>
        public static RectTransform Card(RectTransform root, float s, float y, SkillCardSpec spec, Action onClick, Action onInfo, string buttonName = "Button:Pick", float height = 280, string contentName = null)
        {
            var btn = Ui.Button(root, "", onClick, new Color(1, 1, 1, 0.001f), 1000 * s, height * s, onClick != null, 20);
            btn.name = buttonName;
            var brt = (RectTransform)btn.transform;
            SliceKit.Place(brt, s, 540, y, 1000, height);
            var c = Ui.Rect(brt, contentName ?? "Draft:" + spec.Id);
            Ui.Stretch(c);
            float k = height / 280f;
            // cream panel (behind the icon frame) with a lip
            var lip = Ui.RoundPanel(c, CreamEdge, 26 * s, "Panel");
            lip.raycastTarget = false;
            lip.rectTransform.anchorMin = Vector2.zero; lip.rectTransform.anchorMax = Vector2.one;
            lip.rectTransform.offsetMin = new Vector2(0, 0); lip.rectTransform.offsetMax = new Vector2(0, -52 * s * k);
            var face = Ui.RoundPanel(lip.transform, spec.Dim ? Shade(Cream, -0.25f) : Cream, 24 * s, "Face");
            face.raycastTarget = false;
            Ui.Stretch(face.rectTransform, 4 * s, 4 * s, 4 * s, 12 * s);
            // name ribbon along the top of the panel
            var rib = Ui.RoundPanel(c, NameRibbon, 10 * s, "NameRibbon");
            rib.raycastTarget = false;
            rib.rectTransform.anchorMin = new Vector2(0, 1); rib.rectTransform.anchorMax = new Vector2(1, 1);
            rib.rectTransform.pivot = new Vector2(0.5f, 1);
            rib.rectTransform.offsetMin = new Vector2(240 * s * k, -96 * s * k); rib.rectTransform.offsetMax = new Vector2(-70 * s, -50 * s * k);
            var name = SliceKit.Chunky(c, spec.Title, (int)(TitleSize * s * Mathf.Lerp(0.8f, 1, k)), Color.white, TextAnchor.MiddleLeft, "SkillName");
            name.rectTransform.anchorMin = new Vector2(0, 1); name.rectTransform.anchorMax = new Vector2(1, 1);
            name.rectTransform.pivot = new Vector2(0, 1);
            name.rectTransform.offsetMin = new Vector2(266 * s * k, -96 * s * k); name.rectTransform.offsetMax = new Vector2(-80 * s, -18 * s * k);
            if (!string.IsNullOrEmpty(spec.Chip))
            {
                var chip = Ui.Chip(c, spec.Chip, spec.ChipColor, BodyInk, (int)(22 * s), 38 * s, IsoArt.Ink, spec.ChipName ?? "Chip");
                chip.anchorMin = chip.anchorMax = new Vector2(1, 1);
                chip.pivot = new Vector2(1, 0.5f);
                chip.anchoredPosition = new Vector2(-96 * s, -73 * s * k);
                chip.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                chip.sizeDelta = new Vector2(chip.sizeDelta.x, 40 * s);
            }
            // the description with coloured keywords
            var body = Ui.Display(c, Keywords(spec.Body), (int)(BodySize * s * Mathf.Lerp(0.78f, 1, k)), BodyInk, TextAnchor.UpperLeft, "SkillText");
            body.fontSize = (int)(BodySize * s * Mathf.Lerp(0.78f, 1, k)); // exact, measured against reference 2
            body.supportRichText = true;
            body.raycastTarget = false;
            body.resizeTextForBestFit = true; body.resizeTextMinSize = (int)(22 * s); body.resizeTextMaxSize = body.fontSize;
            body.verticalOverflow = VerticalWrapMode.Truncate; // best fit shrinks long texts into the panel instead of spilling out
            body.rectTransform.anchorMin = Vector2.zero; body.rectTransform.anchorMax = Vector2.one;
            body.rectTransform.offsetMin = new Vector2(266 * s * k, 22 * s); body.rectTransform.offsetMax = new Vector2(-40 * s, -106 * s * k);
            // the framed icon tile with the tier tag above it
            // the shared skill tile (SkillTileView): tier frame, the art filling the face
            float fs = 210 * k;
            var frame = SkillTileView.Build(c, spec.Id, spec.TierColor, spec.Family, fs * s, "Frame", spec.IconArt);
            frame.anchorMin = frame.anchorMax = new Vector2(0, 0.5f);
            frame.pivot = new Vector2(0, 0.5f);
            frame.anchoredPosition = new Vector2(22 * s, -16 * s * k);
            if (spec.Tip.HasValue && spec.TipContent != null)
            {
                var tip = spec.Tip.Value;
                var content = spec.TipContent;
                onInfo = () => SkillTooltip.Show(frame, content, tip); // "?" opens the full text by the tile
                if (onClick == null) SkillTileView.Tappable(frame, content, () => tip); // where a tap does not pick: the tile too
            }
            var tag = Ui.RoundPanel(c, new Color32(0x4A, 0x4E, 0x78, 255), 12 * s, "TierTag");
            tag.raycastTarget = false;
            tag.rectTransform.anchorMin = tag.rectTransform.anchorMax = new Vector2(0, 1);
            tag.rectTransform.pivot = new Vector2(0, 1);
            tag.rectTransform.sizeDelta = new Vector2(fs * s + 16 * s, 52 * s * k);
            tag.rectTransform.anchoredPosition = new Vector2(14 * s, 4 * s);
            UiShapes.Outline(tag.rectTransform, IsoArt.Ink, 12 * s, 3);
            var tt = Ui.Display(tag.transform, spec.Tier, (int)(32 * s * k), Color.Lerp(spec.TierColor, Color.white, 0.55f), TextAnchor.MiddleCenter, "Tier");
            tt.raycastTarget = false;
            Ui.Stretch(tt.rectTransform);
            if (onInfo != null)
            {
                var q = Ui.Button(c, "", onInfo, new Color32(0xE4, 0xE2, 0xEE, 255), 64 * s, 64 * s, true, 20);
                q.name = "InfoButton";
                var qrt = (RectTransform)q.transform;
                qrt.anchorMin = qrt.anchorMax = new Vector2(1, 1);
                qrt.pivot = new Vector2(0.5f, 0.5f);
                qrt.sizeDelta = new Vector2(64 * s, 64 * s);
                qrt.anchoredPosition = new Vector2(-24 * s, -74 * s * k);
                var face2 = qrt.Find("Face");
                if (face2 != null) face2.GetComponent<Image>().sprite = UiShapes.Circle;
                qrt.GetComponent<Image>().color = new Color(0, 0, 0, 0);
                UiShapes.Outline(qrt, IsoArt.Ink, 32 * s, 4);
                var qt = SliceKit.Chunky(qrt, "?", (int)(44 * s), new Color32(0x3A, 0x34, 0x50, 255), TextAnchor.MiddleCenter, "Q");
                foreach (var o in qt.GetComponents<Shadow>()) UnityEngine.Object.DestroyImmediate(o);
                Ui.Stretch(qt.rectTransform);
            }
            return brt;
        }

        private static Color FamilyGlyph(SkillFamily f) => f == SkillFamily.None ? (Color)new Color32(0xFF, 0xE0, 0x5A, 255) : Color.Lerp(Theme.FamilyColor(f), Color.white, 0.35f);

        /// <summary>The purple reroll button (reference px, from the bottom).</summary>
        public static Button Reroll(RectTransform root, float s, string text, Action onClick, bool interactable, float y = 2086, string name = "Button:Reroll")
        {
            var b = Ui.Button(root, "", onClick, Purple, 320 * s, 170 * s, interactable, 20);
            b.name = name;
            SliceKit.Place((RectTransform)b.transform, s, 540, y, 320, 170, true);
            UiShapes.Outline((RectTransform)b.transform, IsoArt.Ink, Theme.RadiusButton, 6);
            var t = SliceKit.Chunky(b.transform, text, (int)(58 * s), Color.white, TextAnchor.MiddleCenter, "RerollText");
            Ui.Stretch(t.rectTransform, 0, 0, 0, 8 * s);
            return b;
        }
    }
}
