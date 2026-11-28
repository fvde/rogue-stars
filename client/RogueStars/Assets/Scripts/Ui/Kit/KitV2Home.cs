using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// The home pieces of UI kit v2 (board "New · home", tools/art/kit_layouts.json screen "home"): the board nav shared by
    /// every main tab, the avatar frame, the season bar, the level hex, the side rails with their timer chips, the quick band,
    /// the big START button and the edge tabs. Each loads its kit sprite by id and otherwise draws the board's fallback.
    /// </summary>
    public static partial class KitV2
    {
        /// <summary>Height of the board nav (px at 1080) and of the top bar.</summary>
        public const float NavDesign = 237, TopBarDesign = 237;

        /// <summary>Horizontal distance between the nav tiles' centres (board: 117, 328, 540, 752, 963).</summary>
        public const float NavPitch = 211.5f;

        public static readonly Color RailFill = new Color32(0x1D, 0x1B, 0x33, 0x8C);
        public static readonly Color QuickBandFill = new Color32(0x1D, 0x1B, 0x33, 0x73);
        public static readonly (Color top, Color bottom) Gold = (Theme.Hex(0xFFD46B), Theme.Hex(0xE8902A));
        public static readonly (Color top, Color bottom) HexGold = (Theme.Hex(0xF7B52A), Theme.Hex(0xD8862A));
        public static readonly (Color top, Color bottom) Start = (Theme.Hex(0xFFF0A0), Theme.Hex(0xF7B52A));
        public static readonly Color AvatarWindow = Theme.Hex(0x5B4B9B);

        // ---- nav ------------------------------------------------------------------------------------------------

        /// <summary>
        /// The board's bottom nav: a navy band, the inactive tabs as dark icon tiles, the active one raised in purple with its
        /// label, red badges ("!", a count, or a wider "NEW" / "▲ UP" pill). A bar along the bottom of <paramref name="root"/>
        /// whose design (1080 × <see cref="NavDesign"/>) <paramref name="fit"/> scales to the width.
        /// </summary>
        public static RectTransform Nav(RectTransform root, IList<KitTab> tabs, out KitDesignFit fit)
        {
            var bar = Ui.Rect(root, "MainTabs");
            bar.anchorMin = Vector2.zero; bar.anchorMax = new Vector2(1, 0); bar.pivot = new Vector2(0.5f, 0);
            bar.sizeDelta = new Vector2(0, NavDesign); bar.anchoredPosition = Vector2.zero;
            var design = Ui.Rect(bar, "Nav");
            design.anchorMin = design.anchorMax = design.pivot = new Vector2(0, 1);
            var band = Box(design, "nav_bar", 0, 0, 1080, NavDesign + 40, Navy, 1, 0, null, null, "Bar");
            band.raycastTarget = true; // taps on the band never reach the body under it
            Box(design, "nav_edge", 0, 0, 1080, 8, Ink, 1, 0, null, null, "Edge");
            int n = tabs.Count;
            for (int i = 0; i < n; i++)
            {
                var t = tabs[i];
                float cx = 540 + (i - (n - 1) / 2f) * NavPitch;
                RectTransform tile;
                if (t.Active)
                {
                    tile = Box(design, "nav_tile_active", cx - 93.5f, -16, 187, 224, Purple.top, 26.7f, 6.7f, Ink, Purple.bottom, "Tab:" + t.Id).rectTransform;
                    Icon(tile, t.Icon, 19, 13, 149, 149, "Icon");
                    var l = Text(tile, t.Label, 6, 160, 175, 48, 40, Color.white, TextAnchor.MiddleCenter, "Label");
                    KitFit.On(l, 22);
                }
                else
                {
                    tile = Box(design, "nav_tile", cx - 88, 34, 176, 176, NavTile, 26.7f, 6.7f, Ink, null, "Tab:" + t.Id).rectTransform;
                    Icon(tile, t.Icon, 21, 18, 134, 134, "Icon");
                }
                if (!string.IsNullOrEmpty(t.Badge)) NavBadge(tile, t.Badge);
                var click = t.OnClick;
                Tap(tile, () => click?.Invoke());
            }
            fit = bar.gameObject.AddComponent<KitDesignFit>();
            fit.Target = design; fit.DesignHeight = NavDesign;
            fit.Apply();
            return bar;
        }

        /// <summary>A tile's badge at its top-right corner: the round red badge for "!" and short counts, a red pill for words.</summary>
        private static void NavBadge(RectTransform tile, string badge)
        {
            float w = tile.sizeDelta.x;
            if (badge.Length <= 2)
            {
                var b = BadgeRed(tile, w - 46, -10, badge, 64, "Badge");
                b.Find("Text").name = "BadgeText";
                return;
            }
            var pill = Box(tile, "badge_red_pill", w - 112, -14, 124, 56, BadgeRedFill, 28, 4.5f, Color.white, null, "Badge");
            UiShapes.Outline(pill.rectTransform, Ink, 28, 2.5f, "Rim");
            var t = Text(pill.rectTransform, badge, 6, 0, 112, 56, 30, Color.white, TextAnchor.MiddleCenter, "BadgeText");
            KitFit.On(t, 18);
        }

        // ---- top bar ------------------------------------------------------------------------------------------------

        /// <summary>The navy top bar band with its dark bottom edge (board 0,0 1080×237).</summary>
        public static void TopBand(Transform design)
        {
            Box(design, "topbar", 0, 0, 1080, TopBarDesign, Navy, 1, 0, null, null, "Bar");
            Box(design, "topbar_edge", 0, TopBarDesign - 8, 1080, 8, Ink, 1, 0, null, null, "Edge");
        }

        /// <summary>
        /// The avatar: a purple window with the portrait inside the gold frame (board window 128 at (x+14, y+14), frame 155 at
        /// (x, y)). Returns the frame rect (the level badge hangs off it).
        /// </summary>
        public static RectTransform AvatarFrame(Transform parent, float x, float y, Sprite portrait, out Image portraitImage)
        {
            var holder = Place(parent, "AvatarFrame", x, y, 155, 155);
            bool art = KitArt.Get("avatar_frame") != null;
            // fallback: the gold gradient square (navy rim) with the purple window on top; art: the window under the frame sprite
            if (!art) Box(holder, "avatar_gold", 0, 0, 155, 155, Gold.top, 32, 6.7f, Ink, Gold.bottom, "Gold");
            var window = Box(holder, "avatar_window", 14, 14, 128, 128, AvatarWindow, 21.3f, 0, null, null, "Avatar");
            portraitImage = Icon(window.rectTransform, portrait, 4, 4, 120, 120, "Portrait");
            if (art) Box(holder, "avatar_frame", 0, 0, 155, 155, Color.clear, 1, 0, null, null, "Frame");
            else UiShapes.Outline(window.rectTransform, Ink, 21.3f, 4, "Inner");
            return holder;
        }

        // ---- season bar --------------------------------------------------------------------------------------------

        /// <summary>
        /// The season bar (board: bar 219,323 643×101, fill inset 7, hex 187,312 112) at <paramref name="x"/>,
        /// <paramref name="y"/> = the bar's top-left: a navy-rimmed track, the gold fill to <paramref name="progress"/>, the
        /// level hex with <paramref name="level"/>, the centred text and the chest. Returns the bar rect.
        /// </summary>
        public static RectTransform SeasonBar(Transform parent, float x, float y, float progress, string level, string text, Sprite chest, string name = "SeasonBar")
        {
            const float w = 643, h = 101;
            var holder = Place(parent, name, x, y, w, h);
            Box(holder, "season_bar_track", 7, 7, w - 14, h - 14, BarFill, 16, 0, null, null, "Track");
            var fill = Box(holder, "season_bar_fill", 7, 7, Mathf.Max(32, (w - 14) * Mathf.Clamp01(progress)), h - 14, Yellow.top, 16, 0, null, Yellow.bottom, "Fill");
            fill.gameObject.SetActive(progress > 0.001f);
            Box(holder, "season_bar", 0, 0, w, h, Color.clear, 21.3f, 6.7f, Ink, null, "Rim");
            var t = Text(holder, text, 105, 28, 420, 45, 40, Color.white, TextAnchor.MiddleCenter, "SeasonProgress");
            KitFit.On(t, 24);
            if (chest != null) Icon(holder, chest, 530, -1, 101, 102, "Reward");
            LevelHex(holder, -32, -11, 112, level, "Tier");
            return holder;
        }

        /// <summary>The level hexagon (flat top, gold gradient, navy rim) with its number.</summary>
        public static RectTransform LevelHex(Transform parent, float x, float y, float size, string level, string name = "LevelHex")
        {
            var holder = Place(parent, name, x, y, size, size);
            var s = KitArt.Get("level_hex");
            if (s != null)
            {
                var img = holder.gameObject.AddComponent<Image>();
                img.raycastTarget = false; img.sprite = s; img.preserveAspect = true;
            }
            else
            {
                Image Hex(string n, float inset, Color c)
                {
                    var rt = Ui.Rect(holder, n);
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    float side = size - 2 * inset;
                    rt.sizeDelta = new Vector2(side * UiShapes.HexAspect * 1.155f, side * 1.04f);
                    rt.localRotation = Quaternion.Euler(0, 0, 90); // pointy-top sprite → flat top, points left and right
                    var img = rt.gameObject.AddComponent<Image>();
                    img.raycastTarget = false; img.sprite = UiShapes.Hex; img.color = c;
                    return img;
                }
                Hex("Rim", 0, Ink);
                var face = Hex("Face", 9, Color.white);
                // the gradient runs top to bottom on screen: the hex is rotated, so it runs along its local x
                face.gameObject.AddComponent<KitHGradient>().Set(HexGold.top, HexGold.bottom);
            }
            var t = Text(holder, level, 8, 26, size - 16, 56, 42.7f, Color.white, TextAnchor.MiddleCenter, "TierText");
            KitFit.On(t, 22);
            return holder;
        }

        // ---- side rails --------------------------------------------------------------------------------------------

        /// <summary>One entry of a side rail.</summary>
        public sealed class RailEntry
        {
            public string Name, Label, Badge;
            public Sprite Icon;
            public Func<string> Timer;
            public Action OnClick;
        }

        /// <summary>Board metrics of a rail entry: icon 123, label line 31 overlapping the icon by 12, timer chip 32 + 2, gap 16.</summary>
        public const float RailWidth = 181, RailIcon = 123, RailLine = 31, RailPad = 21, RailGap = 16;

        /// <summary>Lines of a rail label ("Starter Pack" → 2: the board breaks two-word labels).</summary>
        public static string RailLabel(string label) =>
            string.IsNullOrEmpty(label) ? "" : label.IndexOf('\n') >= 0 ? label : label.Length > 7 && label.IndexOf(' ') > 0 ? label.Replace(' ', '\n') : label;

        /// <summary>Height of a rail entry in board px.</summary>
        public static float RailEntryHeight(RailEntry e) =>
            RailIcon - 12 + RailLine * RailLabel(e.Label).Split('\n').Length + (e.Timer != null ? 2 + 32 : 0);

        /// <summary>Height of a whole rail (pads and gaps included).</summary>
        public static float RailHeight(IList<RailEntry> entries)
        {
            if (entries.Count == 0) return 0;
            float h = 2 * RailPad + (entries.Count - 1) * RailGap;
            foreach (var e in entries) h += RailEntryHeight(e);
            return h;
        }

        /// <summary>
        /// A translucent rail at (x, y) with its entries top to bottom: icon, outlined label, red badge (top-right) and timer
        /// chip. Returns the rail; every entry is a button named <see cref="RailEntry.Name"/>; <paramref name="live"/> registers
        /// the timer texts (recomputed every tick).
        /// </summary>
        public static RectTransform SideRail(Transform parent, string name, float x, float y, IList<RailEntry> entries, Action<Text, Func<string>> live, bool badgeOutside)
        {
            float h = RailHeight(entries);
            var rail = Place(parent, name, x, y, RailWidth, h);
            var railBg = Box(rail, "nav_tile", 0, 0, RailWidth, h, RailFill, 32, 0, null, null, "Rail");
            if (railBg.sprite == KitArt.Get("nav_tile") && railBg.sprite != null) railBg.color = new Color(1, 1, 1, 0.75f); // owner: the rail is the nav tile, stretched, 75%
            float cy = RailPad;
            foreach (var e in entries)
            {
                float eh = RailEntryHeight(e);
                var hit = Place(rail, e.Name, 0, cy - 8, RailWidth, eh + 16);
                Icon(hit, e.Icon, 29, 8, RailIcon, RailIcon, "Icon");
                string label = RailLabel(e.Label);
                int lines = label.Split('\n').Length;
                var l = Text(hit, label, -6, 8 + RailIcon - 12, RailWidth + 12, RailLine * lines, 29.3f, Color.white, TextAnchor.MiddleCenter, "Label");
                l.lineSpacing = 0.82f;
                if (e.Timer != null)
                {
                    var chip = Box(hit, "timer_chip", 34, 8 + RailIcon - 12 + RailLine * lines + 2, 112, 32, Ink, 18.7f, 0, null, null, "Timer").rectTransform;
                    var tt = Text(chip, "", 0, 0, 112, 32, 26.7f, Color.white, TextAnchor.MiddleCenter, "TimerText", false);
                    live?.Invoke(tt, e.Timer);
                    tt.text = e.Timer();
                }
                if (!string.IsNullOrEmpty(e.Badge)) BadgeRed(hit, badgeOutside ? 126 : 112, -12, e.Badge, 64, "Badge").Find("Text").name = "CountText";
                var click = e.OnClick;
                Tap(hit, () => click?.Invoke());
                cy += eh + RailGap;
            }
            return rail;
        }

        // ---- quick band, START, edge tabs ----------------------------------------------------------------------------

        /// <summary>One shortcut on the quick band.</summary>
        public sealed class QuickEntry
        {
            public string Name, Label;
            public Sprite Icon;
            public Action OnClick;
        }

        /// <summary>Distance between the quick band's slots (board: 116, 354, 592, 830 for 133 px icons).</summary>
        public const float QuickPitch = 238, QuickBandHeight = 171;

        /// <summary>The translucent quick band (board 64,1659 952×171 for four) with its shortcuts, centred on 540.</summary>
        public static RectTransform QuickBand(Transform parent, float y, IList<QuickEntry> entries)
        {
            int n = entries.Count;
            float w = n * QuickPitch;
            var band = Place(parent, "QuickBand", 540 - w / 2, y, w, QuickBandHeight);
            Box(band, "quick_band", 0, 0, w, QuickBandHeight, QuickBandFill, 37.3f, 0, null, null, "Band");
            for (int i = 0; i < n; i++)
            {
                var e = entries[i];
                var hit = Place(band, e.Name, i * QuickPitch, 0, QuickPitch, QuickBandHeight);
                Icon(hit, e.Icon, (QuickPitch - 133) / 2, 26, 133, 133, "Icon");
                var l = Text(hit, e.Label, 4, 133, QuickPitch - 8, 37, 32, Color.white, TextAnchor.MiddleCenter, "Label");
                KitFit.On(l, 20);
                var click = e.OnClick;
                Tap(hit, () => click?.Invoke());
            }
            return band;
        }

        /// <summary>
        /// The big yellow START (board 272,1867 533×208): navy shadow 11 px under it, the glossy yellow face with a navy rim and
        /// the darker lip along the bottom. Returns the button rect (children: Face).
        /// </summary>
        public static RectTransform StartButton(Transform parent, float x, float y, float w, float h, string name)
        {
            var holder = Place(parent, name, x, y, w, h + 11);
            var s = KitArt.Get("start_button");
            if (s != null)
            {
                var img = Place(holder, "Face", 0, 0, w, h).gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                Skin(img, "start_button");
                return holder;
            }
            Box(holder, "start_shadow", 0, 11, w, h, Ink, 32, 0, null, null, "Shadow");
            var face = Box(holder, "start_face", 0, 0, w, h, Start.top, 32, 8, Ink, Start.bottom, "Face");
            var lip = Place(face.rectTransform, "Lip", 8, h - 8 - 22, w - 16, 22);
            var li = lip.gameObject.AddComponent<Image>();
            li.raycastTarget = false; UiShapes.Round(li, 11); li.color = new Color32(0xC8, 0x78, 0x14, 0x66);
            var gloss = Place(face.rectTransform, "Gloss", 22, 12, w - 44, h * 0.3f);
            var gi = gloss.gameObject.AddComponent<Image>();
            gi.raycastTarget = false; UiShapes.Round(gi, 18); gi.color = new Color(1, 1, 1, 0.28f);
            return holder;
        }

        /// <summary>
        /// An edge tab flush with the screen edge (board: Guild 0,1893 192×181 on the left, Modes 888,1893 on the right): the
        /// outer corners square (drawn past the edge), the inner ones rounded, icon over its label.
        /// </summary>
        public static RectTransform EdgeTab(Transform parent, bool left, float y, Sprite icon, string label, string name, Action onClick)
        {
            const float w = 192, h = 181;
            float x = left ? 0 : 1080 - w;
            var holder = Place(parent, name, x, y, w, h);
            var s = KitArt.Get(left ? "edge_tab_left" : "edge_tab_right");
            var bgRt = Place(holder, "Bg", s != null ? 0 : left ? -40 : 0, 0, s != null ? w : w + 40, h);
            var bg = bgRt.gameObject.AddComponent<Image>();
            bg.raycastTarget = false;
            if (s != null) Skin(bg, left ? "edge_tab_left" : "edge_tab_right");
            else Fallback(bg, NavTile, 32, 6.7f, Ink);
            Icon(holder, icon, (w - 90) / 2 + (left ? -3 : 3), 28, 90, 82, "Icon");
            var l = Text(holder, label, 6, 112, w - 12, 44, 34.7f, Color.white, TextAnchor.MiddleCenter, "Label");
            KitFit.On(l, 22);
            Tap(holder, onClick);
            return holder;
        }
    }

    /// <summary>A horizontal colour gradient (left → right in local space) multiplied onto a graphic: rotated pieces (the level hex).</summary>
    public sealed class KitHGradient : UnityEngine.UI.BaseMeshEffect
    {
        public Color Left = Color.white, Right = Color.gray;

        public void Set(Color left, Color right)
        {
            Left = left; Right = right;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var r = ((RectTransform)transform).rect;
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float t = r.width > 0.01f ? Mathf.InverseLerp(r.xMax, r.xMin, v.position.x) : 0;
                var g = Color.Lerp(Left, Right, t);
                var c = (Color)v.color;
                v.color = new Color(c.r * g.r, c.g * g.g, c.b * g.b, c.a * g.a);
                vh.SetUIVertex(v, i);
            }
        }
    }
}
