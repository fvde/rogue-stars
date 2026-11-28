using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// UI kit v2 (docs/design/ui-kit-v2.md): the pieces of the owner-approved screen boards ("New · worm" …), placed in board
    /// pixels (1080 wide, y down from the top-left) inside a design container that <see cref="KitDesignFit"/> scales to the
    /// real width. Every piece loads its kit sprite by id (<see cref="KitArt.Get"/>, <c>Resources/Art/Ui/Kit/&lt;id&gt;</c>,
    /// 9-sliced when the sprite has borders) and otherwise draws the board's code-drawn look: navy outlines, flat fills and
    /// two-colour gradients, white outlined display text (tools/art/kit_layouts.json "fallback" entries).
    /// </summary>
    public static class KitV2
    {
        /// <summary>Width of the boards (1080 px; 405 css px × 2.667).</summary>
        public const float DesignWidth = 1080;

        // ---- board colours ------------------------------------------------------------------------------------------
        public static readonly Color Navy = Theme.Hex(0x2B2847);
        public static readonly Color Ink = Theme.Hex(0x1D1B33);
        public static readonly Color ParchmentFill = Theme.Hex(0xF3E6C8);
        public static readonly Color PatternLine = new Color32(0xDC, 0xC4, 0x96, 0x40);
        public static readonly Color Plus = Theme.Hex(0x5FD35F);
        public static readonly Color BarFill = Theme.Hex(0x3A3550);
        public static readonly Color Band = Theme.Hex(0xEAD9B0);
        public static readonly Color TabInactive = Theme.Hex(0x5B4B9B);
        public static readonly Color GridPanel = Theme.Hex(0x3E2C2A);
        public static readonly Color GridHeader = Theme.Hex(0x2E201F);
        public static readonly Color SlotBadge = Theme.Hex(0x6A4FD0);
        public static readonly Color DiamondGold = Theme.Hex(0xFFC23D);
        public static readonly Color BadgeRedFill = Theme.Hex(0xFF3B3B);
        public static readonly Color PipLock = Theme.Hex(0x5B5578);
        public static readonly Color ListButton = Theme.Hex(0xE8D5A6), ListRim = Theme.Hex(0xB89A60);
        public static readonly Color PowerBadge = Theme.Hex(0x5B4B9B);
        public static readonly Color NavTile = Theme.Hex(0x3A3550);
        public static readonly (Color top, Color bottom) Yellow = (Theme.Hex(0xFFE86B), Theme.Hex(0xF7B52A));
        public static readonly (Color top, Color bottom) Blue = (Theme.Hex(0x7FD6FF), Theme.Hex(0x3C9EE6));
        public static readonly (Color top, Color bottom) Orange = (Theme.Hex(0xF0A25B), Theme.Hex(0xD97A33));
        public static readonly (Color top, Color bottom) Purple = (Theme.Hex(0x8A6CF0), Theme.Hex(0x5B3FCF));
        public static readonly (Color top, Color bottom) Green = (Theme.Hex(0x8FE39A), Theme.Hex(0x2FA84A));
        public static readonly (Color top, Color bottom) Red = (Theme.Hex(0xFF8F8F), Theme.Hex(0xD93B3B));

        /// <summary>Tile id of a part rarity (0–10): Common … Legendary, every Mythic step = <c>tile_mythic</c>.</summary>
        public static string TileId(int partRarity) =>
            partRarity <= 0 ? "tile_common" : partRarity == 1 ? "tile_rare" : partRarity == 2 ? "tile_epic" : partRarity == 3 ? "tile_legendary" : "tile_mythic";

        /// <summary>Gradient of a rarity tile (board fallbacks: grey, green, blue, purple, red).</summary>
        public static (Color top, Color bottom) TileColors(int partRarity)
        {
            switch (TileId(partRarity))
            {
                case "tile_common": return (Theme.Hex(0xE2E6EC), Theme.Hex(0x9AA3AE));
                case "tile_rare": return Green;
                case "tile_epic": return (Theme.Hex(0x8FC6FF), Theme.Hex(0x3A7FE6));
                case "tile_legendary": return (Theme.Hex(0xD6B0FF), Theme.Hex(0x8B4FE0));
                default: return Red;
            }
        }

        /// <summary>Colour of the big rank letter on a tile (board: gold on Common / Mythic, pale tint on the others).</summary>
        public static Color RankColor(int partRarity)
        {
            switch (partRarity)
            {
                case 1: return Theme.Hex(0xE6FFE9);
                case 2: return Theme.Hex(0xE3F0FF);
                case 3: return Theme.Hex(0xF0E0FF);
                default: return Theme.Hex(0xFFE14D);
            }
        }

        // ---- placement ------------------------------------------------------------------------------------------

        /// <summary>A rect at board position (x, y from the parent's top-left, w × h), outside any layout.</summary>
        public static RectTransform Place(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = Ui.Rect(parent, name);
            return At(rt, x, y, w, h);
        }

        /// <summary>Position an existing rect at board coordinates (top-left anchored; ignores layout).</summary>
        public static RectTransform At(RectTransform rt, float x, float y, float w, float h)
        {
            var le = rt.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
            return rt;
        }

        /// <summary>
        /// A design container of <paramref name="designHeight"/> board px under a layout parent: a wrapper that takes the
        /// parent's width and the scaled height, its child (returned) 1080 wide, scaled to fit (<see cref="KitDesignFit"/>).
        /// </summary>
        public static RectTransform DesignBlock(Transform parent, string name, float designHeight, out KitDesignFit fit)
        {
            var wrap = Ui.Rect(parent, name);
            var le = wrap.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = designHeight; le.flexibleWidth = 1;
            var inner = Ui.Rect(wrap, "Design");
            inner.anchorMin = inner.anchorMax = inner.pivot = new Vector2(0, 1);
            inner.anchoredPosition = Vector2.zero;
            inner.sizeDelta = new Vector2(DesignWidth, designHeight);
            fit = wrap.gameObject.AddComponent<KitDesignFit>();
            fit.Target = inner; fit.Layout = le; fit.DesignHeight = designHeight;
            fit.Apply();
            return inner;
        }

        // ---- surfaces -------------------------------------------------------------------------------------------

        /// <summary>The kit sprite of <paramref name="id"/> on <paramref name="img"/> (white, sliced when bordered); false = none.</summary>
        public static bool Skin(Image img, string id)
        {
            var s = KitArt.Get(id);
            if (s == null) return false;
            img.sprite = s;
            img.type = s.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = false;
            img.pixelsPerUnitMultiplier = 1;
            img.color = Color.white;
            return true;
        }

        /// <summary>
        /// A kit piece at board coordinates: its sprite when present, else a rounded box in <paramref name="fill"/> (a vertical
        /// gradient when <paramref name="bottom"/> is set) with a navy <paramref name="border"/>.
        /// </summary>
        public static Image Box(Transform parent, string id, float x, float y, float w, float h, Color fill, float radius,
            float border = 0, Color? borderColor = null, Color? bottom = null, string name = null)
        {
            var rt = Place(parent, name ?? id, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            if (!Skin(img, id)) Fallback(img, fill, radius, border, borderColor, bottom);
            return img;
        }

        /// <summary>The code-drawn look of a kit piece on an existing image.</summary>
        public static void Fallback(Image img, Color fill, float radius, float border = 0, Color? borderColor = null, Color? bottom = null)
        {
            UiShapes.Round(img, Mathf.Max(1, radius));
            if (bottom.HasValue)
            {
                img.color = Color.white;
                var g = img.gameObject.AddComponent<KitGradient>();
                g.Set(fill, bottom.Value);
            }
            else img.color = fill;
            if (border > 0) UiShapes.Outline(img.rectTransform, borderColor ?? Ink, Mathf.Max(1, radius), border);
        }

        /// <summary>A filled circle (badges, the power icon backing) inside rings (outermost first), all code-drawn without the kit sprite.</summary>
        public static Image Circle(Transform parent, string id, float x, float y, float size, Color fill, params (Color color, float width)[] rings)
        {
            var rt = Place(parent, id, x, y, size, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            if (Skin(img, id)) { img.preserveAspect = true; return img; }
            img.sprite = UiShapes.Circle; img.type = Image.Type.Simple;
            if (rings == null || rings.Length == 0) { img.color = fill; return img; }
            img.color = rings[0].color;
            float inset = rings[0].width;
            for (int i = 1; i <= rings.Length; i++)
            {
                var c = i < rings.Length ? rings[i].color : fill;
                var layer = Ui.Rect(rt, i < rings.Length ? "Ring" : "Fill");
                Ui.Stretch(layer, inset, inset, inset, inset);
                var li = layer.gameObject.AddComponent<Image>();
                li.raycastTarget = false; li.sprite = UiShapes.Circle; li.color = c;
                if (i < rings.Length) inset += rings[i].width;
            }
            return img;
        }

        /// <summary>A diamond (rotated rounded square) at board coordinates: copy counts, pip gems.</summary>
        public static Image Diamond(Transform parent, string id, float x, float y, float size, Color fill, float border, Color? tint = null)
        {
            var holder = Place(parent, id, x, y, size, size);
            var s = KitArt.Get(id);
            if (s != null)
            {
                var si = holder.gameObject.AddComponent<Image>();
                si.raycastTarget = false; si.sprite = s; si.preserveAspect = true; si.color = tint ?? Color.white;
                return si;
            }
            float side = size * 0.68f;
            var rt = Ui.Rect(holder, "Shape");
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(side, side);
            rt.localRotation = Quaternion.Euler(0, 0, 45);
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            UiShapes.Round(img, side * 0.14f);
            img.color = tint ?? fill;
            if (border > 0) UiShapes.Outline(rt, Ink, side * 0.14f, border);
            return img;
        }

        /// <summary>White display text with the board's navy outline, placed at board coordinates.</summary>
        public static Text Text(Transform parent, string text, float x, float y, float w, float h, float size, Color? color = null,
            TextAnchor align = TextAnchor.MiddleCenter, string name = "Text", bool outline = true)
        {
            var t = Kit.Title(parent, text, Mathf.RoundToInt(size), color ?? Color.white, align, name);
            At(t.rectTransform, x, y, w, h);
            foreach (var o in t.GetComponents<Outline>())
            {
                if (!outline) { UnityEngine.Object.DestroyImmediate(o); continue; }
                o.effectColor = Ink;
            }
            if (outline)
            {
                var sh = t.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.85f);
                sh.effectDistance = new Vector2(0, -Mathf.Max(2, size * 0.12f));
            }
            return t;
        }

        /// <summary>An icon sprite fitted into a board rect (no raycasts); a null sprite leaves an empty rect.</summary>
        public static Image Icon(Transform parent, Sprite sprite, float x, float y, float w, float h, string name = "Icon", Color? tint = null)
        {
            var rt = Place(parent, name, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.sprite = sprite; img.preserveAspect = true;
            img.color = sprite == null ? Color.clear : tint ?? Color.white;
            return img;
        }

        /// <summary>Make a placed rect tappable (transparent hit area over it).</summary>
        public static Button Tap(RectTransform rt, Action onClick, string name = null)
        {
            var img = rt.GetComponent<Image>();
            if (img == null) { img = rt.gameObject.AddComponent<Image>(); img.color = Color.clear; }
            img.raycastTarget = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            b.colors = colors;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            if (name != null) rt.name = name;
            return b;
        }

        // ---- composite pieces ------------------------------------------------------------------------------------

        /// <summary>The red notification badge ("!" or a count) with a white and a navy ring, centred on (cx, cy).</summary>
        public static RectTransform BadgeRed(Transform parent, float x, float y, string text = "!", float size = 64, string name = "Alert")
        {
            var holder = Place(parent, name, x, y, size, size);
            var s = KitArt.Get("badge_red");
            var img = holder.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            if (s != null) { img.sprite = s; img.preserveAspect = true; }
            else
            {
                img.sprite = UiShapes.Circle; img.color = Ink;
                var white = Ui.Rect(holder, "White"); Ui.Stretch(white, 4, 4, 4, 4);
                var wi = white.gameObject.AddComponent<Image>(); wi.sprite = UiShapes.Circle; wi.color = Color.white; wi.raycastTarget = false;
                var red = Ui.Rect(holder, "Red"); Ui.Stretch(red, 9.3f, 9.3f, 9.3f, 9.3f);
                var ri = red.gameObject.AddComponent<Image>(); ri.sprite = UiShapes.Circle; ri.color = BadgeRedFill; ri.raycastTarget = false;
            }
            Text(holder, text, 4, 4, size - 8, size - 8, text.Length > 1 ? size * 0.4f : size * 0.46f, Color.white, TextAnchor.MiddleCenter, "Text", false).fontStyle = FontStyle.Bold;
            return holder;
        }

        /// <summary>The navy pip strip under a tile: 5 sockets = gems (filled), empty sockets (open) and locks (not yet).</summary>
        public static RectTransform PipStrip(Transform parent, float x, float y, IList<Color?> gems, int open, int total = 5, string name = "Pips")
        {
            const float w = 149, h = 32;
            var strip = Box(parent, "pip_strip", x, y, w, h, BarFill, 16, 0, null, null, name).rectTransform;
            float step = 27, gem = 30;
            float x0 = (w - (total - 1) * step - gem) / 2;
            for (int i = 0; i < total; i++)
            {
                float px = x0 + i * step;
                if (i < gems.Count && gems[i].HasValue)
                {
                    // the coloured gem sprite of this colour (pip_gem_blue / _purple / _gold), else the tinted code-drawn diamond
                    var id = PipGemId(gems[i].Value);
                    if (id != null && KitArt.Get(id) != null) Diamond(strip, id, px + 2, (h - gem) / 2 + 3, gem - 4, gems[i].Value, 0, Color.white).name = "Gem";
                    else Diamond(strip, "pip_gem", px, (h - gem) / 2 + 1, gem, gems[i].Value, 2.7f, gems[i].Value).name = "Gem";
                }
                // empty socket: darker than the code strip, lighter than the kit's near-black pip_strip (else it disappears on it)
                else if (i < open) Diamond(strip, "pip_socket", px + 5, (h - gem) / 2 + 6, gem - 10, KitArt.Get("pip_strip") != null ? Theme.Hex(0x48426A) : Theme.Hex(0x22203A), 2.2f).name = "Socket";
                else
                {
                    var l = Box(strip, "pip_lock", px + 7, 7, 16, 19, PipLock, 5.3f);
                    l.name = "Lock";
                }
            }
            return strip;
        }

        /// <summary>Kit gem sprites by the gem colours the tiles use (<c>PartTileView.GemColor</c>: blue, purple, gold).</summary>
        private static readonly (string id, Color color)[] PipGems =
        {
            ("pip_gem_blue", Theme.Hex(0x5FD3FF)), ("pip_gem_purple", Theme.Hex(0xC084FC)), ("pip_gem_gold", Theme.Hex(0xFFC23D)),
        };

        /// <summary>The kit gem sprite id of a pip colour (the nearest of blue / purple / gold, if close), else null (code-drawn, tinted).</summary>
        public static string PipGemId(Color c)
        {
            string best = null;
            float bestD = 0.06f; // squared RGB distance: only (near) exact matches
            foreach (var g in PipGems)
            {
                float dr = c.r - g.color.r, dg = c.g - g.color.g, db = c.b - g.color.b, d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; best = g.id; }
            }
            return best;
        }

        /// <summary>
        /// The icon of a currency in kit chrome: the kit's <c>icon_&lt;id&gt;</c> when present (stamina, diamond, ore, biomass,
        /// metal), else the game's currency icon.
        /// </summary>
        public static Sprite CurrencyIcon(string c)
        {
            string kit = c == Currencies.Diamonds ? "icon_diamond" : c == Currencies.Ore ? "icon_ore" : c == Currencies.Stamina ? "icon_stamina"
                : c == Currencies.Biomass ? "icon_biomass" : c == Currencies.Metal ? "icon_metal" : null;
            return (kit != null ? KitArt.Get(kit) : null) ?? RogueStars.Client.World.SummonArt.CurrencyIcon(c);
        }

        /// <summary>The navy pill of a currency in the top bar: icon, amount, the green "+" (tap = <paramref name="onPlus"/>).</summary>
        public static RectTransform CurrencyPill(Transform parent, float x, float y, float w, Sprite icon, string amount, Action onPlus, string name)
        {
            var pill = Box(parent, "currency_pill", x, y, w, 69, Ink, 34.7f, 0, null, null, name).rectTransform;
            Icon(pill, icon, 4, 4, 60, 60);
            var t = Text(pill, amount, 66, 14, w - 66 - 62, 40, 34.7f, Color.white, TextAnchor.MiddleCenter, "Amount");
            KitFit.On(t, 20);
            if (KitArt.Get("plus_button") != null)
            {
                // the kit's plus is cut out of the green square: a white backing under it makes it the board's white "+"
                var back = Place(pill, "PlusBack", w - 62 + 10, 8 + 8, 33, 33).gameObject.AddComponent<Image>();
                back.color = Color.white; back.raycastTarget = false;
            }
            var plus = Box(pill, "plus_button", w - 62, 8, 53, 53, Plus, 13.3f, 5.3f, Ink, null, "Plus").rectTransform;
            if (KitArt.Get("plus_button") == null) Text(plus, "+", 0, -2, 53, 53, 40, Color.white, TextAnchor.MiddleCenter, "PlusText", false);
            Tap(pill, onPlus);
            return pill;
        }

        /// <summary>A glossy small button (By Rarity blue, the inventory's yellow action) with outlined white text.</summary>
        public static Button SmallButton(Transform parent, string id, float x, float y, float w, float h, (Color top, Color bottom) colors, string label, Action onClick, string name)
        {
            var img = Box(parent, id, x, y, w, h, colors.top, 21.3f, 6.7f, Ink, colors.bottom, name);
            var t = Text(img.rectTransform, label, 8, 0, w - 16, h, 37.3f, Color.white, TextAnchor.MiddleCenter, "Label");
            KitFit.On(t, 20);
            return Tap(img.rectTransform, onClick);
        }

        // ---- procedural textures ----------------------------------------------------------------------------------

        private static Sprite _parchment;

        /// <summary>The parchment stage fill: <c>parchment_bg</c> (tiled) or a cream tile with the faint diamond lattice.</summary>
        public static Image Parchment(Image img)
        {
            var s = KitArt.Get("parchment_bg");
            img.color = Color.white;
            if (s != null) { img.sprite = s; img.type = Image.Type.Tiled; return img; }
            if (_parchment == null)
            {
                const int n = 118; // one diamond cell every 58.7 board px, lines 5.3 px
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "rs_parchment", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float a = Mathf.Abs(Mathf.Repeat(x + y + 0.5f, n / 2f) - n / 4f);
                        float b = Mathf.Abs(Mathf.Repeat(x - y + 0.5f, n / 2f) - n / 4f);
                        float d = Mathf.Min(n / 4f - a, n / 4f - b) / 1.414f; // distance to the nearest diagonal line
                        float cov = Mathf.Clamp01(2.65f - d);
                        px[y * n + x] = Color.Lerp(ParchmentFill, new Color(PatternLine.r, PatternLine.g, PatternLine.b, 1), cov * PatternLine.a);
                    }
                tex.SetPixels(px);
                tex.Apply(false, true);
                _parchment = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
                _parchment.name = "rs_parchment";
            }
            img.sprite = _parchment; img.type = Image.Type.Tiled; img.pixelsPerUnitMultiplier = 1;
            return img;
        }
    }

    /// <summary>
    /// Scales a 1080-wide board design to its rect's width: the target keeps its design size (board px) with a uniform
    /// local scale; the layout element (if any) takes the scaled height; <see cref="Scaled"/> reports the factor (the
    /// screen moves its body frame under a scaled top bar / nav).
    /// </summary>
    public sealed class KitDesignFit : UIBehaviour
    {
        public RectTransform Target;
        public LayoutElement Layout;
        public float DesignHeight;
        public float Scale { get; private set; } = 1;
        public Action<float> Scaled;

        protected override void OnEnable() { base.OnEnable(); Apply(); }
        protected override void OnRectTransformDimensionsChange() => Apply();

        /// <summary>A new design height (an inventory that grew): resizes the target and the layout height.</summary>
        public void SetDesignHeight(float h)
        {
            if (Mathf.Approximately(h, DesignHeight)) return;
            DesignHeight = h;
            Apply(true);
        }

        public void Apply() => Apply(false);

        private void Apply(bool force)
        {
            if (Target == null) return;
            float w = ((RectTransform)transform).rect.width;
            float k = w > 1 ? w / KitV2.DesignWidth : Scale;
            if (!force && Mathf.Approximately(k, Scale) && Mathf.Approximately(Target.sizeDelta.y, DesignHeight)) return;
            Scale = k;
            Target.localScale = new Vector3(k, k, 1);
            Target.sizeDelta = new Vector2(KitV2.DesignWidth, DesignHeight);
            if (Layout != null && !Mathf.Approximately(Layout.preferredHeight, DesignHeight * k)) Layout.preferredHeight = DesignHeight * k;
            Scaled?.Invoke(k);
        }
    }
}
