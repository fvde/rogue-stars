using System;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// Widgets of the planet hub and the unlock moment (design canvas "Main", "HubDay1", "Unlock"): rounded cards and
    /// buttons, the planet disc, floating icon buttons, chips, the PLAY card, the next-unlock teaser and the bottom tabs.
    /// Built on <see cref="Ui"/>; rounded shapes use procedurally generated sprites (no art assets).
    /// </summary>
    public static class HubKit
    {
        // Wireframe palette (canvas HubDay1 / Main / Unlock).
        public static readonly Color Bg = Theme.Hex(0x0E1124);
        public static readonly Color Night = Theme.Hex(0x06070F);
        public static readonly Color Card = Theme.Hex(0x1B2045);
        public static readonly Color CardDeep = Theme.Hex(0x11142B);
        public static readonly Color Ink = Theme.Hex(0xEEF1FF);
        public static readonly Color InkDim = Theme.Hex(0x9AA3D6);
        public static readonly Color InkSoft = Theme.Hex(0xC9CFF5);
        public static readonly Color Cyan = Theme.Hex(0x7FE3FF);
        public static readonly Color CyanDeep = Theme.Hex(0x3BB8E6);
        public static readonly Color CyanInk = Theme.Hex(0x04202B);
        public static readonly Color Orange = Theme.Hex(0xFFB347);
        public static readonly Color OrangeDeep = Theme.Hex(0xFF8A1F);
        public static readonly Color OrangeInk = Theme.Hex(0x2A1300);
        public static readonly Color Gold = Theme.Hex(0xFFC35C);
        public static readonly Color Pink = Theme.Hex(0xF28BB6);
        public static readonly Color PinkInk = Theme.Hex(0x4A1028);
        public static readonly Color TabActive = Theme.Hex(0x2A3170);
        public static readonly Color Dashed = Theme.Hex(0x5A639A);
        public static readonly Color PlanetLight = Theme.Hex(0x3B4A8F);
        public static readonly Color PlanetDark = Theme.Hex(0x1D2554);
        public static readonly Color EventRed = Theme.Hex(0xE5484D);
        public static readonly Color EventRedBg = Theme.Hex(0x3A1414);
        public static readonly Color EventGreen = Theme.Hex(0x7BE08A);
        public static readonly Color EventGreenBg = Theme.Hex(0x16301F);
        public static readonly Color ShopGoldBg = Theme.Hex(0x3B2A10);
        public static readonly Color PassBg = Theme.Hex(0x12303A);
        public static readonly Color MailBorder = Theme.Hex(0x4A5290);

        private static Sprite _round, _circle, _disc;

        /// <summary>Rounded-rectangle sprite (9-sliced, 32 px corners at the canvas' 100 px/unit).</summary>
        public static Sprite Round => _round != null ? _round : (_round = MakeSprite(96, 32, false));

        /// <summary>Filled circle (for avatars, dots).</summary>
        public static Sprite Circle => _circle != null ? _circle : (_circle = MakeSprite(256, 128, false));

        /// <summary>Lit sphere: radial gradient from the upper left (planet, unlock glow).</summary>
        public static Sprite Disc => _disc != null ? _disc : (_disc = MakeSprite(256, 128, true));

        private static Sprite MakeSprite(int size, int radius, bool shaded)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HubKit" + size + (shaded ? "s" : ""), wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[size * size];
            float r = radius;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance outside the rounded rectangle (inset by the radius); circle when radius = size / 2.
                    float cx = Mathf.Clamp(x + 0.5f, r, size - r), cy = Mathf.Clamp(y + 0.5f, r, size - r);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    float shade = 1f;
                    if (shaded)
                    {
                        float lx = (x - size * 0.35f) / size, ly = (y - size * 0.7f) / size;
                        shade = Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(Mathf.Sqrt(lx * lx + ly * ly) * 1.4f));
                    }
                    byte c = (byte)Mathf.RoundToInt(255 * shade);
                    px[y * size + x] = new Color32(c, c, c, (byte)Mathf.RoundToInt(255 * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var border = radius < size / 2 ? new Vector4(radius, radius, radius, radius) : Vector4.zero;
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, border);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        /// <summary>Make an Image rounded (sliced) — panels, buttons, rows.</summary>
        public static Image Rounded(Image img)
        {
            img.sprite = Round;
            img.type = Image.Type.Sliced;
            return img;
        }

        public static Image RoundPanel(Transform parent, Color color, string name = "Panel") => Rounded(Ui.Panel(parent, color, name));

        /// <summary>Rounded button with its own text colour.</summary>
        public static Button RoundButton(Transform parent, string text, Action onClick, Color color, Color textColor, float width = -1, float height = Theme.ButtonHeight, int fontSize = Theme.FontNormal)
        {
            var b = Ui.Button(parent, text, onClick, color, width, height, true, fontSize);
            Rounded((Image)b.targetGraphic);
            var label = b.GetComponentInChildren<Text>();
            label.color = textColor;
            label.fontStyle = FontStyle.Bold;
            return b;
        }

        /// <summary>A layout slot of the given height with a centred, fixed-size child (not stretched by the layout).</summary>
        public static RectTransform Centered(Transform parent, float width, float height, float slotHeight, string name)
        {
            var slot = Ui.Rect(parent, name + "Slot");
            Ui.Size(slot, -1, slotHeight, 1);
            var rt = Ui.Rect(slot, name);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            return rt;
        }

        /// <summary>The planet disc with a glow and the worm on it.</summary>
        public static RectTransform Planet(Transform parent, float diameter, string name = "Planet")
        {
            var holder = Centered(parent, diameter, diameter, diameter + 40, name);
            var glow = Ui.Panel(holder, new Color(0.31f, 0.5f, 1f, 0.1f), "Glow");
            glow.sprite = Circle; glow.raycastTarget = false;
            Ui.Stretch(glow.rectTransform, -26, -26, -26, -26);
            var disc = Ui.Panel(holder, PlanetLight, "Disc");
            disc.sprite = Disc; disc.raycastTarget = false;
            Ui.Stretch(disc.rectTransform);
            var worm = Rounded(Ui.Panel(holder, Pink, "Worm"));
            worm.raycastTarget = false;
            var wrt = worm.rectTransform;
            wrt.anchorMin = wrt.anchorMax = wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(diameter * 0.39f, diameter * 0.155f);
            return holder;
        }

        /// <summary>Small rounded pill with text.</summary>
        public static Text Chip(Transform parent, string text, Color textColor, Color? background = null, float width = -1, float height = 58, string name = "Chip")
        {
            var bg = RoundPanel(parent, background ?? Card, name);
            Ui.Size(bg, width < 0 ? Mathf.Max(120, text.Length * 17 + 48) : width, height, 0);
            var t = Ui.Label(bg.transform, text, Theme.FontSmall, textColor, TextAnchor.MiddleCenter, "Text");
            t.fontStyle = FontStyle.Bold;
            Ui.Stretch(t.rectTransform, 14, 0, 14, 0);
            return t;
        }

        /// <summary>
        /// Floating icon (side columns): a rounded square with a glyph and a border colour, the label below and a small
        /// badge on the square's lower edge (timer, count; hidden while empty). Returns the button; <paramref name="sub"/>
        /// receives the badge text.
        /// </summary>
        public static Button IconButton(Transform parent, string glyph, string label, Color background, Color border, Color textColor, Action onClick, string name, out Text sub)
        {
            var col = Ui.Column(parent, 2, 0, null, name + "Cell");
            Ui.Size(col, 170, 166, 0);
            var lg = col.GetComponent<VerticalLayoutGroup>();
            lg.childAlignment = TextAnchor.UpperCenter; lg.childForceExpandWidth = false;
            var frame = RoundPanel(col, border, name);
            Ui.Size(frame, 124, 124, 0);
            var btn = frame.gameObject.AddComponent<Button>();
            btn.targetGraphic = frame;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var inner = RoundPanel(frame.transform, background, "Face");
            inner.raycastTarget = false;
            Ui.Stretch(inner.rectTransform, 5, 5, 5, 5);
            var g = Ui.Label(inner.transform, glyph, 56, textColor, TextAnchor.MiddleCenter, "Glyph");
            g.fontStyle = FontStyle.Bold;
            Ui.Stretch(g.rectTransform);
            var badge = RoundPanel(frame.transform, Night, "Badge");
            badge.raycastTarget = false;
            var brt = badge.rectTransform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0);
            brt.sizeDelta = new Vector2(150, 36); brt.anchoredPosition = new Vector2(0, -12);
            sub = Ui.Label(badge.transform, "", 22, textColor, TextAnchor.MiddleCenter, "Sub");
            sub.fontStyle = FontStyle.Bold;
            Ui.Stretch(sub.rectTransform, 4, 0, 4, 0);
            badge.gameObject.AddComponent<HideWhenEmpty>().Text = sub;
            var l = Ui.Label(col, label, 26, textColor, TextAnchor.LowerCenter, "Label");
            l.fontStyle = FontStyle.Bold;
            Ui.Size(l, 170, 38, 0);
            return btn;
        }

        /// <summary>The big orange PLAY card: kicker (portal · level), title, right-hand pill (stamina cost).</summary>
        public static Button PlayCard(Transform parent, string kicker, string title, string pill, Action onClick, string name, out Text kickerText)
        {
            var shadow = RoundPanel(parent, Theme.Hex(0xB85A0B), name + "Shadow");
            Ui.Size(shadow, -1, 200, 1);
            var face = RoundPanel(shadow.transform, OrangeDeep, name);
            var frt = face.rectTransform;
            Ui.Stretch(frt, 0, 0, 0, 14);
            var top = RoundPanel(face.transform, Orange, "Light");
            top.raycastTarget = false;
            Ui.Stretch(top.rectTransform, 0, 0, 0, 70);
            var btn = face.gameObject.AddComponent<Button>();
            btn.targetGraphic = face;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var row = Ui.Row(face.transform, -1, 20, null, 0, "Row");
            Ui.Stretch(row, 44, 0, 22, 0);
            var texts = Ui.Column(row, 0, 0, null, "Texts");
            Ui.Size(texts, -1, -1, 1);
            texts.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            kickerText = Ui.Label(texts, kicker, 28, new Color(OrangeInk.r, OrangeInk.g, OrangeInk.b, 0.8f), TextAnchor.MiddleLeft, "Kicker");
            kickerText.fontStyle = FontStyle.Bold;
            var t = Ui.Label(texts, title, 80, OrangeInk, TextAnchor.MiddleLeft, "Title");
            t.fontStyle = FontStyle.Bold;
            if (!string.IsNullOrEmpty(pill))
            {
                var p = RoundPanel(row, new Color(OrangeInk.r, OrangeInk.g, OrangeInk.b, 0.14f), "Pill");
                Ui.Size(p, 230, 140, 0);
                var pt = Ui.Label(p.transform, pill, 32, OrangeInk, TextAnchor.MiddleCenter, "PillText");
                pt.fontStyle = FontStyle.Bold;
                Ui.Stretch(pt.rectTransform, 8, 0, 8, 0);
            }
            return btn;
        }

        /// <summary>The feature's icon as a locked silhouette (or "?" without one) + kicker + text + a progress figure (the next unlock).</summary>
        public static RectTransform Teaser(Transform parent, string kicker, string text, string figure, Color background, string name, Sprite icon = null)
        {
            var bg = RoundPanel(parent, background, name);
            Ui.Size(bg, -1, 150, 1);
            var row = Ui.Row(bg.transform, -1, 28, null, 0, "Row");
            Ui.Stretch(row, 30, 0, 34, 0);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            var q = RoundPanel(row, Dashed, "Silhouette");
            Ui.Size(q, 104, 104, 0);
            var qi = RoundPanel(q.transform, background, "Inner");
            Ui.Stretch(qi.rectTransform, 4, 4, 4, 4);
            if (icon != null)
            {
                // a dark, tinted silhouette of the feature's art with a small lock (H4)
                var si = Ui.Panel(qi.transform, new Color(0.16f, 0.18f, 0.34f, 1f), "Icon");
                si.sprite = icon; si.preserveAspect = true; si.raycastTarget = false;
                Ui.Stretch(si.rectTransform, 8, 8, 8, 8);
                var lk = Ui.Panel(q.transform, Color.white, "Lock");
                lk.sprite = World.SummonArt.Icon("lock"); lk.preserveAspect = true; lk.raycastTarget = false;
                lk.rectTransform.anchorMin = lk.rectTransform.anchorMax = lk.rectTransform.pivot = new Vector2(1, 0);
                lk.rectTransform.sizeDelta = new Vector2(44, 44); lk.rectTransform.anchoredPosition = new Vector2(10, -8);
            }
            else
            {
                var ql = Ui.Label(qi.transform, "?", 48, InkDim, TextAnchor.MiddleCenter, "Q");
                ql.fontStyle = FontStyle.Bold;
                Ui.Stretch(ql.rectTransform);
            }
            var texts = Ui.Column(row, 2, 0, null, "Texts");
            Ui.Size(texts, -1, 120, 1);
            texts.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            if (!string.IsNullOrEmpty(kicker))
            {
                var k = Ui.Label(texts, kicker, 24, InkDim, TextAnchor.MiddleLeft, "Kicker");
                k.fontStyle = FontStyle.Bold;
            }
            var t = Ui.Label(texts, text, 32, Ink, TextAnchor.MiddleLeft, "TeaserText");
            t.fontStyle = FontStyle.Bold;
            if (!string.IsNullOrEmpty(figure))
            {
                var f = Ui.Label(row, figure, 44, Gold, TextAnchor.MiddleRight, "Figure");
                f.fontStyle = FontStyle.Bold;
                Ui.Size(f, 150, 120, 0);
            }
            return (RectTransform)bg.transform;
        }

        /// <summary>One bottom tab: the active one is taller and outlined (the Planet).</summary>
        public static Button Tab(Transform parent, string glyph, string label, bool active, string badge, Action onClick, string name)
        {
            var slot = Ui.Rect(parent, name + "Slot");
            Ui.Size(slot, -1, 180, 1);
            float h = active ? 180 : 150;
            var frame = RoundPanel(slot, active ? Cyan : Card, name);
            var rt = frame.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(0, h); rt.anchoredPosition = Vector2.zero;
            var face = RoundPanel(frame.transform, active ? TabActive : Card, "Face");
            face.raycastTarget = false;
            Ui.Stretch(face.rectTransform, active ? 5 : 0, active ? 5 : 0, active ? 5 : 0, active ? 5 : 0);
            var btn = frame.gameObject.AddComponent<Button>();
            btn.targetGraphic = frame;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var g = Ui.Label(face.transform, glyph, active ? 56 : 48, active ? Cyan : Ink, TextAnchor.MiddleCenter, "Glyph");
            g.fontStyle = FontStyle.Bold;
            Ui.Stretch(g.rectTransform, 0, 10, 0, h * 0.42f);
            var l = Ui.Label(face.transform, label, active ? 30 : 28, Ink, TextAnchor.MiddleCenter, "Label");
            l.fontStyle = FontStyle.Bold;
            Ui.Stretch(l.rectTransform, 0, h * 0.55f, 0, 8);
            if (!string.IsNullOrEmpty(badge))
            {
                var b = RoundPanel(frame.transform, OrangeDeep, "Badge");
                var brt = b.rectTransform;
                brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1, 1);
                brt.sizeDelta = new Vector2(90, 40); brt.anchoredPosition = new Vector2(-10, -10);
                var bt = Ui.Label(b.transform, badge, 22, OrangeInk, TextAnchor.MiddleCenter, "BadgeText");
                bt.fontStyle = FontStyle.Bold;
                Ui.Stretch(bt.rectTransform);
            }
            return btn;
        }
    }

    /// <summary>Hides a badge background while its text is empty (live sub-labels fill in later).</summary>
    public sealed class HideWhenEmpty : MonoBehaviour
    {
        public Text Text;
        private Image _img;

        private void LateUpdate() => Apply();

        public void Apply()
        {
            if (_img == null) _img = GetComponent<Image>();
            if (_img != null && Text != null) _img.enabled = !string.IsNullOrEmpty(Text.text);
        }
    }
}
