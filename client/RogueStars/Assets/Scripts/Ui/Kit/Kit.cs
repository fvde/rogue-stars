using System;
using System.Collections.Generic;
using RogueStars.Client.World;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.UiKit
{
    public enum KitTone { Card, Raised, Well }

    /// <summary>Button skin colours (the sprite slot is <c>button_&lt;name&gt;</c>, falling back to the shared <c>button</c>).</summary>
    public enum KitButtonColor { Primary, Secondary, Positive, Danger, Premium, Accent, Gold }

    /// <summary>One entry of the <see cref="Kit.BottomTabBar"/>.</summary>
    public sealed class KitTab
    {
        public string Id, Label, Badge;
        public Sprite Icon;
        public bool Active;
        public Action OnClick;
    }

    /// <summary>
    /// The UI kit: the reusable menu pieces every redesigned screen is composed from. Procedural look today, optional
    /// sprite per piece (<see cref="KitArt"/>, docs/design/ui-kit.md). Built on <see cref="Ui"/> / <see cref="UiShapes"/> /
    /// <see cref="Theme"/>, never parallel to them.
    /// </summary>
    public static class Kit
    {
        // ---- palette of the kit (menu chrome: hub-matching plum for navigation, warm browns for cards) ----------------
        public static readonly Color NavDark = new Color32(0x2A, 0x26, 0x3E, 255);
        public static readonly Color NavBar = new Color32(0x1A, 0x16, 0x2A, 255);
        public static readonly Color NavActive = new Color32(0x7C, 0x5C, 0xE0, 255);
        public static readonly Color Ink = new Color32(0x1E, 0x16, 0x24, 255);
        public static readonly Color DotRed = new Color32(0xE5, 0x30, 0x3A, 255);

        public static Color ButtonColor(KitButtonColor c)
        {
            switch (c)
            {
                case KitButtonColor.Primary: return Theme.Accent;
                case KitButtonColor.Positive: return Theme.Good;
                case KitButtonColor.Danger: return Theme.Bad;
                case KitButtonColor.Premium: return Theme.Premium;
                case KitButtonColor.Accent: return Theme.Purple;
                case KitButtonColor.Gold: return Theme.Gold;
                default: return Theme.Button;
            }
        }

        public static Color ToneColor(KitTone t) => t == KitTone.Raised ? Theme.Header : t == KitTone.Well ? Theme.PanelAlt : Theme.Panel;

        // ---- surfaces -------------------------------------------------------------------------------------------

        /// <summary>
        /// The shared look of every kit surface: the panel sprite slot (else the rounded rectangle), a soft top-to-bottom
        /// gradient and a drop shadow. No extra child objects (layout and tests see the same hierarchy).
        /// </summary>
        public static Image Decorate(Image img, float radius, string piece = "panel", bool shadow = true, float lighten = 1.14f, float darken = 0.86f)
        {
            if (img == null) return null;
            KitArt.Skin(img, piece, radius);
            var g = img.GetComponent<KitGradient>();
            if (g == null) g = img.gameObject.AddComponent<KitGradient>();
            g.Set(new Color(lighten, lighten, lighten, 1), new Color(darken, darken, darken, 1));
            if (shadow && img.GetComponent<Shadow>() == null)
            {
                var sh = img.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0, 0, 0, 0.32f);
                sh.effectDistance = new Vector2(0, -5);
            }
            return img;
        }

        /// <summary>A rounded kit surface (free-positioned; no layout): panel / card frame.</summary>
        public static Image Surface(Transform parent, Color fill, float radius = Theme.RadiusCard, Color? border = null, string piece = "panel", string name = "Surface")
        {
            var img = Ui.Panel(parent, fill, name);
            Decorate(img, radius, piece);
            if (border.HasValue) UiShapes.Outline(img.rectTransform, border.Value, radius, 4);
            return img;
        }

        /// <summary>Fill and rim of a <see cref="Plate"/>: near-opaque dark plum, a light rim (reads on light and dark art).</summary>
        public static readonly Color PlateFill = new Color32(0x14, 0x10, 0x22, 236);
        public static readonly Color PlateRim = new Color(1f, 0.93f, 0.78f, 0.7f);

        /// <summary>
        /// A solid backing plate for HUD pieces floating over art (battle backgrounds, the board): the near-opaque dark
        /// <see cref="PlateFill"/>, a light rim and a drop shadow, so whatever sits on it reads on every background (sprite slot
        /// <c>plate</c>). Free-positioned, no layout, never takes input.
        /// </summary>
        public static Image Plate(Transform parent, float radius = 24, string name = "Plate")
        {
            var img = Ui.Panel(parent, PlateFill, name);
            img.raycastTarget = false;
            var le = img.GetComponent<LayoutElement>() ?? img.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            Decorate(img, radius, "plate", true, 1.1f, 0.9f);
            var sh = img.GetComponent<Shadow>();
            if (sh != null) { sh.effectColor = new Color(0, 0, 0, 0.55f); sh.effectDistance = new Vector2(0, -6); }
            UiShapes.Outline(img.rectTransform, PlateRim, radius, 3);
            return img;
        }

        /// <summary>Makes a text read on any background: a dark outline and a drop shadow (HUD labels over art).</summary>
        public static Text Legible(Text text)
        {
            if (text == null) return null;
            if (text.GetComponent<Outline>() == null)
            {
                var o = text.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0.06f, 0.04f, 0.1f, 0.95f);
                o.effectDistance = new Vector2(2, -2);
            }
            return text;
        }

        /// <summary>A card: padded column on the tone's surface (panel, raised panel, inset well).</summary>
        public static RectTransform Card(Transform parent, KitTone tone = KitTone.Card, int padding = 24, float spacing = 10, string name = "Card")
        {
            var rt = Ui.Card(parent, ToneColor(tone), tone == KitTone.Raised ? Theme.Border : (Color?)null, Theme.RadiusCard, padding, spacing, 4, name);
            if (tone != KitTone.Card) KitArt.Skin(rt.GetComponent<Image>(), tone == KitTone.Raised ? "panel_raised" : "panel_well", Theme.RadiusCard);
            return rt;
        }

        /// <summary>A rarity-coloured tappable card (part tile, alien card): gradient in the rarity colour, light rim, gem top-left.</summary>
        public static Button RarityCard(Transform parent, string name, Color rarity, Action onClick, out RectTransform content,
            float radius = 30, int padding = 16, float spacing = 4, bool highlight = false, Color? rim = null, float strength = 0.3f)
        {
            var fill = Color.Lerp(Theme.Panel, rarity, strength);
            var edge = rim ?? (highlight ? Theme.Good : Color.Lerp(rarity, Color.white, 0.25f));
            var btn = Ui.CardButton(parent, name, fill, edge, onClick, out content, radius, padding, spacing);
            var img = content.GetComponent<Image>();
            var piece = KitArt.Get("card_frame") != null ? "card_frame" : "panel";
            KitArt.Skin(img, piece, radius);
            Gem(content, rarity, 26);
            return btn;
        }

        /// <summary>The rarity gem: a small faceted dot in the rarity colour in the top-left corner of <paramref name="card"/>.</summary>
        public static Image Gem(Transform card, Color rarity, float size = 26, string name = "Gem")
        {
            var g = Ui.Dot(card, rarity, size, name);
            var s = KitArt.Get("gem");
            if (s != null) g.sprite = s;
            var le = g.GetComponent<LayoutElement>();
            if (le == null) le = g.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            var rt = g.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(10, -10);
            var shine = Ui.Dot(rt, new Color(1, 1, 1, 0.55f), size * 0.38f, "Shine");
            var srt = shine.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.34f, 0.68f);
            srt.sizeDelta = new Vector2(size * 0.38f, size * 0.38f);
            srt.anchoredPosition = Vector2.zero;
            shine.raycastTarget = false;
            UiShapes.Outline(rt, Theme.Shade(rarity, 0.45f), size / 2, 3);
            return g;
        }

        // ---- text -----------------------------------------------------------------------------------------------

        /// <summary>Chunky display text with a dark outline (titles, numbers on art); size is exact (no theme scaling).</summary>
        public static Text Title(Transform parent, string text, int size, Color? color = null, TextAnchor align = TextAnchor.MiddleCenter, string name = "Label")
        {
            var t = Ui.Display(parent, text, size, color ?? Color.white, align, name);
            t.fontSize = size;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            float d = Mathf.Max(2, size * 0.07f);
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = Ink; o.effectDistance = new Vector2(d, -d);
            var o2 = t.gameObject.AddComponent<Outline>();
            o2.effectColor = Ink; o2.effectDistance = new Vector2(d * 0.7f, d * 0.7f);
            return t;
        }

        // ---- buttons --------------------------------------------------------------------------------------------

        /// <summary>A kit button in one of the skin colours (chunky face, lip, pressed/disabled look from <see cref="UiButtonLook"/>).</summary>
        public static Button Button(Transform parent, string text, Action onClick, KitButtonColor color = KitButtonColor.Primary, float width = -1,
            float height = Theme.ButtonHeight, bool interactable = true, int fontSize = Theme.FontNormal)
        {
            var b = Ui.Button(parent, text, onClick, ButtonColor(color), width, height, interactable, fontSize);
            var skin = KitArt.Get("button_" + color.ToString().ToLowerInvariant());
            if (skin != null)
            {
                var face = b.targetGraphic as Image;
                if (face != null) { face.sprite = skin; face.type = Image.Type.Sliced; face.pixelsPerUnitMultiplier = 1; }
            }
            return b;
        }

        /// <summary>The screen header's back button (an arrow plate; <c>icon_back</c> replaces the glyph).</summary>
        public static Button BackButton(Transform parent, string label, Action onBack)
        {
            var b = Button(parent, label, onBack, KitButtonColor.Secondary, 104, 104, true, Theme.FontTitle);
            b.name = "BackButton";
            var icon = KitArt.Get("icon_back");
            if (icon != null)
            {
                var txt = b.GetComponentInChildren<Text>();
                if (txt != null) txt.text = "";
                var img = Ui.Panel(b.transform, Color.white, "Icon");
                img.sprite = icon; img.preserveAspect = true; img.raycastTarget = false;
                Ui.Stretch(img.rectTransform, 22, 22, 22, 22);
            }
            return b;
        }

        // ---- navigation -----------------------------------------------------------------------------------------

        /// <summary>
        /// Chip tabs (sub-tabs, filters, shop sections): flexible width, the active chip gold, labels that never wrap
        /// (<see cref="KitFit"/>), an optional "!" dot per chip. Buttons are named <c>Tab:&lt;label&gt;</c>.
        /// </summary>
        public static UiTabBar TabChips(Transform parent, IList<string> labels, int selected, Action<int> onSelect, float height = 84, IList<bool> dots = null)
        {
            var bar = Ui.TabBar(parent, labels, selected, onSelect, height);
            bar.Active = Theme.Gold; bar.Inactive = Theme.Button;
            bar.Select(selected);
            var row = bar.Buttons.Count > 0 ? bar.Buttons[0].transform.parent : null;
            if (row != null)
            {
                var img = row.GetComponent<Image>();
                if (img != null) img.color = new Color(0, 0, 0, 0.25f);
            }
            for (int i = 0; i < bar.Buttons.Count; i++)
            {
                bar.Buttons[i].name = "Tab:" + labels[i];
                if (dots != null && i < dots.Count && dots[i]) NotifyDot(bar.Buttons[i].transform);
            }
            return bar;
        }

        /// <summary>
        /// The main tab bar along the bottom of a screen: equal tabs on a dark plum bar, the active one raised, taller and
        /// purple with a bigger label, icons from <see cref="KitTab.Icon"/> (else the label's initial), "!" badges. Returns the
        /// bar (anchored to the bottom of <paramref name="parent"/>, <paramref name="height"/> tall; the active tab rises above it).
        /// </summary>
        public static RectTransform BottomTabBar(Transform parent, IList<KitTab> tabs, float height = 150, float raise = 40)
        {
            var bar = Ui.Rect(parent, "MainTabs");
            bar.anchorMin = Vector2.zero; bar.anchorMax = new Vector2(1, 0); bar.pivot = new Vector2(0.5f, 0);
            bar.sizeDelta = new Vector2(0, height); bar.anchoredPosition = Vector2.zero;
            var bg = Ui.Panel(bar, NavBar, "Bar");
            KitArt.Skin(bg, "bottom_bar", 0);
            if (bg.sprite == UiShapes.Fill) { bg.sprite = null; bg.type = Image.Type.Simple; }
            Ui.Stretch(bg.rectTransform);
            bg.raycastTarget = true;
            int n = Mathf.Max(1, tabs.Count);
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                var rt = Ui.Rect(bar, "Tab:" + t.Id);
                rt.anchorMin = new Vector2(i / (float)n, 0); rt.anchorMax = new Vector2((i + 1f) / n, 1);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = new Vector2(5, 6); rt.offsetMax = new Vector2(-5, t.Active ? raise : -6);
                var face = Ui.Panel(rt, t.Active ? NavActive : NavDark, "Face");
                Decorate(face, 24, t.Active ? "bottom_tab_active" : "bottom_tab", false, 1.18f, 0.84f);
                Ui.Stretch(face.rectTransform);
                UiShapes.Outline(rt, t.Active ? Color.Lerp(NavActive, Color.white, 0.45f) : Theme.Shade(NavDark, 0.55f), 24, 4);
                var btn = rt.gameObject.AddComponent<Button>();
                btn.targetGraphic = face;
                var cb = btn.colors; cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); cb.fadeDuration = 0; btn.colors = cb;
                if (t.OnClick != null) { var click = t.OnClick; btn.onClick.AddListener(() => click()); }
                var icon = Ui.Panel(rt, Color.white, "Icon");
                icon.raycastTarget = false;
                if (t.Icon != null)
                {
                    icon.sprite = t.Icon; icon.preserveAspect = true;
                    Ui.Stretch(icon.rectTransform, 26, 14, 26, t.Active ? 66 : 54);
                }
                else
                {
                    icon.color = new Color(0, 0, 0, 0);
                    var g = Title(rt, string.IsNullOrEmpty(t.Label) ? "" : t.Label.Substring(0, 1), 54, Color.white, TextAnchor.MiddleCenter, "Glyph");
                    Ui.Stretch(g.rectTransform, 0, 8, 0, 50);
                }
                var l = Title(rt, t.Label, t.Active ? 32 : 26, t.Active ? Color.white : new Color32(0xD8, 0xD2, 0xF0, 255), TextAnchor.LowerCenter, "Label");
                Ui.Stretch(l.rectTransform, 0, 0, 0, 8);
                KitFit.On(l, 18);
                if (!string.IsNullOrEmpty(t.Badge)) NotifyDot(rt, t.Badge);
            }
            return bar;
        }

        /// <summary>The screen header's title in the kit's chunky outlined type.</summary>
        public static Text HeaderTitle(Transform parent, string text, TextAnchor align) => Title(parent, text, Theme.FontTitle, Theme.Text, align, "Title");

        // ---- headers --------------------------------------------------------------------------------------------

        /// <summary>
        /// Ribbon section header: a coloured plaque with swallow-tail ends and chunky text (<c>ribbon</c> sprite slot).
        /// Named "Ribbon:" + text.
        /// </summary>
        public static RectTransform Ribbon(Transform parent, string text, Color? color = null, float height = 88)
        {
            var c = color ?? new Color32(0x8A, 0x5A, 0x3C, 255);
            var root = Ui.Rect(parent, "Ribbon:" + text);
            Ui.Size(root, -1, height, 1);
            var le = root.GetComponent<LayoutElement>();
            float tail = height * 0.62f;
            for (int side = 0; side < 2; side++)
            {
                var tl = Ui.Panel(root, Theme.Shade(c, 0.62f), side == 0 ? "TailL" : "TailR");
                Decorate(tl, 14, "ribbon_tail", false);
                var rt = tl.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(side, 0.5f);
                rt.pivot = new Vector2(side, 0.5f);
                rt.sizeDelta = new Vector2(tail + 30, height * 0.72f);
                rt.anchoredPosition = new Vector2(0, -height * 0.1f);
                tl.raycastTarget = false;
            }
            var plaque = Ui.Panel(root, c, "Plaque");
            Decorate(plaque, 18, "ribbon", true, 1.22f, 0.82f);
            Ui.Stretch(plaque.rectTransform, tail, 0, tail, height * 0.12f);
            plaque.raycastTarget = false;
            UiShapes.Outline(plaque.rectTransform, Theme.Shade(c, 0.5f), 18, 4);
            var t = Title(plaque.transform, text, Mathf.RoundToInt(height * 0.46f), new Color32(0xFF, 0xF1, 0xDD, 255), TextAnchor.MiddleCenter, "Text");
            Ui.Stretch(t.rectTransform, 10, 0, 10, 4);
            KitFit.On(t, 20);
            return root;
        }

        // ---- chips and badges -----------------------------------------------------------------------------------

        /// <summary>Currency chip: icon slot (the currency's icon art) and a chunky amount on a dark pill. Tappable when <paramref name="onTap"/> is set.</summary>
        public static RectTransform CurrencyChip(Transform parent, string currencyId, string text, Action onTap = null, string name = null, float height = 60)
        {
            var rt = Ui.Rect(parent, name ?? "CurrencyChip:" + currencyId);
            var img = rt.gameObject.AddComponent<Image>();
            // kit v2's currency_pill is the same dark navy pill (outline baked in, untinted)
            if (!KitV2.Skin(img, "currency_pill"))
            {
                img.color = new Color32(0x16, 0x12, 0x2A, 255);
                Decorate(img, height / 2, "chip", false, 1.1f, 0.9f);
                UiShapes.Outline(rt, Theme.Shade(NavActive, 0.5f), height / 2, 3);
            }
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(10, Mathf.RoundToInt(height * 0.4f), 4, 4);
            h.spacing = 8; h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            var icon = Ui.Panel(rt, Color.white, "Icon");
            icon.sprite = KitV2.CurrencyIcon(currencyId); icon.preserveAspect = true; icon.raycastTarget = false;
            Ui.Size(icon, height - 12, height - 12, 0, 0);
            var t = Title(rt, text, Mathf.RoundToInt(height * 0.48f), Color.white, TextAnchor.MiddleLeft, "Amount");
            Ui.Size(t, -1, height - 8, 0, 0);
            if (onTap != null)
            {
                img.raycastTarget = true;
                var b = rt.gameObject.AddComponent<Button>();
                b.targetGraphic = img;
                b.onClick.AddListener(() => onTap());
            }
            else img.raycastTarget = false;
            Ui.Size(rt, -1, height, 0, 0);
            return rt;
        }

        /// <summary>Level badge: a small dark pill with chunky text ("Lv 12", "3/10").</summary>
        public static RectTransform LevelBadge(Transform parent, string text, Color? color = null, float height = 38, string name = "LevelBadge")
        {
            var rt = Ui.Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.07f, 0.05f, 0.1f, 0.82f);
            Decorate(img, height / 2, "badge_level", false, 1.1f, 0.9f);
            img.raycastTarget = false;
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(Mathf.RoundToInt(height * 0.35f), Mathf.RoundToInt(height * 0.35f), 0, 0);
            h.childAlignment = TextAnchor.MiddleCenter; h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            var t = Title(rt, text, Mathf.RoundToInt(height * 0.58f), color ?? Color.white, TextAnchor.MiddleCenter, "Text");
            Ui.Size(t, -1, height, 0, 0);
            Ui.Size(rt, -1, height, 0, 0);
            return rt;
        }

        /// <summary>Red notification dot with a white "!" (or a count) in the top-right corner of <paramref name="host"/>; never takes layout or raycasts.</summary>
        public static RectTransform NotifyDot(Transform host, string text = "!", float size = 40, string name = "NotifyDot")
        {
            var d = Ui.Dot(host, DotRed, size, name);
            d.raycastTarget = false;
            // kit v2's badge_red is the same red dot, fully coloured with its white ring and navy outline baked in
            var v2 = KitArt.Get("badge_red");
            var s = v2 ?? KitArt.Get("badge_dot");
            if (s != null) d.sprite = s;
            if (v2 != null) { d.color = Color.white; d.preserveAspect = true; }
            var le = d.GetComponent<LayoutElement>();
            if (le == null) le = d.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            var rt = d.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(6, 6);
            if (v2 == null) UiShapes.Outline(rt, Color.white, size / 2, 3);
            var t = Title(rt, text, Mathf.RoundToInt(size * 0.62f), Color.white, TextAnchor.MiddleCenter, "Text");
            Ui.Stretch(t.rectTransform);
            return rt;
        }

        /// <summary>A round coloured badge in the top-right corner of a card (level-up arrow, check), outside its layout.</summary>
        public static RectTransform CornerBadge(Transform card, string text, Color color, string name, float size = 52)
        {
            var b = Ui.Dot(card, color, size, name);
            var le = b.GetComponent<LayoutElement>();
            if (le == null) le = b.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            var rt = b.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(-8, -8);
            var t = Ui.Label(b.transform, text, 26, Theme.OnAccent, TextAnchor.MiddleCenter, "BadgeText");
            t.fontStyle = FontStyle.Bold;
            Ui.Stretch(t.rectTransform);
            UiShapes.Outline(rt, Theme.Shade(color, 0.55f), size / 2, 3);
            return rt;
        }

        /// <summary>Small status pill (EQUIPPED, LOCKED, MAXED).</summary>
        public static RectTransform Pill(Transform parent, string text, Color? fill, Color textColor, string name = "Pill", float height = 36) =>
            Ui.Chip(parent, text, fill, textColor, 18, height, null, name);

        // ---- progress -------------------------------------------------------------------------------------------

        /// <summary>Progress pill: dark track, gradient fill, chunky label centred on it (<c>pill_track</c>/<c>pill_fill</c> slots).</summary>
        public static UiBar ProgressPill(Transform parent, float value, Color fill, string label = null, float height = 36) => Ui.Bar(parent, value, fill, label, height);

        /// <summary>
        /// A row of stat pills (icon disc + value): the stat bar under the worm and the aliens' bonus row. Entries are
        /// (key, colour, value); the icon slot is <c>stat_&lt;key&gt;</c> (else the key's abbreviation on a coloured disc).
        /// </summary>
        public static RectTransform StatPills(Transform parent, IList<(string key, string abbr, Color color, string value)> stats, string name = "StatPills")
        {
            var row = Ui.Row(parent, 64, 14, null, 0, name);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            foreach (var st in stats)
            {
                var pill = Ui.Rect(row, "Stat:" + st.key);
                Ui.Size(pill, -1, 56, 1, 0);
                var track = Ui.Panel(pill, new Color(0.05f, 0.03f, 0.08f, 0.78f), "Track");
                // kit v2's stat_bar is the same dark value pill the icon overlaps (untinted), else the tinted v1 slot
                if (!KitV2.Skin(track, "stat_bar")) Decorate(track, 28, "stat_pill", false, 1.1f, 0.9f);
                track.raycastTarget = false;
                Ui.Stretch(track.rectTransform, 28, 8, 0, 8);
                var disc = Ui.Dot(pill, st.color, 56, "Icon");
                var icon = KitArt.Get("stat_" + st.key);
                if (icon != null) { disc.sprite = icon; disc.color = Color.white; }
                var drt = disc.rectTransform;
                drt.anchorMin = drt.anchorMax = new Vector2(0, 0.5f); drt.pivot = new Vector2(0, 0.5f);
                drt.sizeDelta = new Vector2(56, 56); drt.anchoredPosition = Vector2.zero;
                UiShapes.Outline(drt, Theme.Shade(st.color, 0.5f), 28, 3);
                if (icon == null)
                {
                    var a = Title(disc.transform, st.abbr, 18, Color.white, TextAnchor.MiddleCenter, "Abbr");
                    Ui.Stretch(a.rectTransform);
                }
                var v = Title(pill, st.value, 30, Color.white, TextAnchor.MiddleCenter, "Value");
                Ui.Stretch(v.rectTransform, 56, 0, 4, 0);
                KitFit.On(v, 16);
            }
            return row;
        }

        // ---- nodes, slots, backdrops, lines ---------------------------------------------------------------------

        /// <summary>
        /// Hex node (skill tree): a rounded hexagon in <paramref name="color"/> with a rim, a glyph on top and "x/y" under it.
        /// Locked nodes are greyed; maxed nodes get a gold rim. Free-positioned; the caller adds a Button when needed.
        /// </summary>
        public static RectTransform HexNode(Transform parent, Color color, string glyph, string level, float size, bool locked, bool maxed, string name = "Node")
        {
            var root = Ui.Rect(parent, name);
            root.sizeDelta = new Vector2(size, size);
            var fillColor = locked ? new Color(0.28f, 0.26f, 0.34f, 1) : color;
            var fill = Ui.Panel(root, fillColor, "Hex");
            var s = KitArt.Get("node_hex");
            fill.sprite = s != null ? s : UiShapes.Hex;
            fill.preserveAspect = true;
            var g = fill.gameObject.AddComponent<KitGradient>();
            g.Set(new Color(1.18f, 1.18f, 1.18f, 1), new Color(0.8f, 0.8f, 0.8f, 1));
            Ui.Stretch(fill.rectTransform);
            var ring = Ui.Panel(root, maxed ? Theme.Gold : Theme.Shade(fillColor, 0.45f), "Rim");
            var rs = KitArt.Get("node_hex_ring");
            ring.sprite = rs != null ? rs : UiShapes.HexRing;
            ring.preserveAspect = true; ring.raycastTarget = false;
            Ui.Stretch(ring.rectTransform);
            var gt = Title(root, glyph ?? "", Mathf.RoundToInt(size * 0.34f), locked ? new Color(1, 1, 1, 0.5f) : Color.white, TextAnchor.MiddleCenter, "Glyph");
            Ui.Stretch(gt.rectTransform, 0, size * 0.12f, 0, size * 0.3f);
            if (!string.IsNullOrEmpty(level))
            {
                var lt = Title(root, level, Mathf.RoundToInt(size * 0.2f), Color.white, TextAnchor.MiddleCenter, "Level");
                Ui.Stretch(lt.rectTransform, 0, size * 0.62f, 0, size * 0.1f);
                KitFit.On(lt, 12);
            }
            return root;
        }

        /// <summary>
        /// Reward slot: a square tile with an icon, an amount badge and a state (open, ready = gold glow rim, claimed = dim
        /// with a green check, locked = grey). Named <paramref name="name"/>.
        /// </summary>
        public static RectTransform RewardSlot(Transform parent, Sprite icon, string amount, float size, string state = "open", Color? rim = null, string name = "RewardSlot")
        {
            var root = Ui.Rect(parent, name);
            Ui.Size(root, size, size, 0, 0);
            bool ready = state == "ready", claimed = state == "claimed", locked = state == "locked";
            var bg = Ui.Panel(root, locked ? Theme.Locked : Theme.Tint(rim ?? Theme.Gold, 0.18f), "Slot");
            Decorate(bg, size * 0.2f, "slot_reward");
            Ui.Stretch(bg.rectTransform);
            UiShapes.Outline(root, ready ? Theme.Gold : rim ?? Theme.Border, size * 0.2f, ready ? 6 : 4);
            if (icon != null)
            {
                var img = Ui.Panel(root, claimed || locked ? new Color(1, 1, 1, 0.45f) : Color.white, "Icon");
                img.sprite = icon; img.preserveAspect = true; img.raycastTarget = false;
                Ui.Stretch(img.rectTransform, size * 0.14f, size * 0.1f, size * 0.14f, size * 0.26f);
            }
            if (!string.IsNullOrEmpty(amount))
            {
                var a = Title(root, amount, Mathf.RoundToInt(size * 0.2f), Color.white, TextAnchor.LowerCenter, "Amount");
                Ui.Stretch(a.rectTransform, 2, 0, 2, 4);
                KitFit.On(a, 12);
            }
            if (claimed)
            {
                var c = Title(root, "✓", Mathf.RoundToInt(size * 0.5f), Theme.Good, TextAnchor.MiddleCenter, "Check");
                Ui.Stretch(c.rectTransform);
            }
            return root;
        }

        /// <summary>A stretched backdrop behind a screen or a stage (<paramref name="piece"/> slot, else a vertical gradient).</summary>
        public static Image Backdrop(Transform parent, string piece, Color top, Color bottom, string name = "Backdrop")
        {
            var img = Ui.Panel(parent, Color.white, name);
            var s = KitArt.Get(piece);
            if (s != null) { img.sprite = s; img.type = Image.Type.Simple; img.preserveAspect = false; }
            var g = img.gameObject.AddComponent<KitGradient>();
            g.Set(s != null ? Color.white : top, s != null ? Color.white : bottom);
            if (s == null) img.color = Color.white;
            img.raycastTarget = false;
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            Ui.Stretch(img.rectTransform);
            img.transform.SetAsFirstSibling();
            return img;
        }

        /// <summary>A straight connector between two points of <paramref name="area"/> (anchored bottom-left); skill-tree and progress-track lines.</summary>
        public static Image Line(RectTransform area, Vector2 a, Vector2 b, float thickness, Color color, string name = "Line")
        {
            var img = Ui.Panel(area, color, name);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var d = b - a;
            rt.sizeDelta = new Vector2(d.magnitude, thickness);
            rt.anchoredPosition = (a + b) / 2;
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            return img;
        }
    }
}
