using System.Collections.Generic;
using RogueStars.Client.UiKit;
using UnityEngine;
using static RogueStars.Client.World.IsoPaint;

namespace RogueStars.Client.World
{
    /// <summary>
    /// HUD icons of the vertical slice (ArtCatalog <c>Art/Slice/Hud/&lt;id&gt;</c> first, else drawn in code): heart, sword,
    /// shield, coin, exp, cog, skull, dice, book, speed, reroll, pedestal, plus the big dice button face
    /// (<see cref="DiceButton"/>).
    /// </summary>
    public static class HudArt
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static void ClearCache() => Cache.Clear();

        public static readonly string[] Ids = { "dice", "pedestal", "heart", "sword", "shield", "coin", "exp", "cog", "skull", "book", "speed", "reroll", "boss_rail_top" };

        public static Sprite Icon(string id)
        {
            var art = ArtCatalog.SliceHud(id);
            if (art != null) return art;
            if (Cache.TryGetValue(id, out var s) && s != null) return s;
            s = Draw(id);
            Cache[id] = s;
            return s;
        }

        private static readonly Color Ink = IsoArt.Ink;

        private static Sprite Draw(string id)
        {
            const int n = 128;
            var p = new IsoPaint(n, n);
            float c = n / 2f;
            switch (id)
            {
                case "heart":
                {
                    var red = (Color)new Color32(0xF0, 0x3E, 0x4E, 255);
                    var pts = new List<Vector2>();
                    for (int i = 0; i <= 40; i++)
                    {
                        float t = i / 40f * Mathf.PI * 2;
                        float x = 16 * Mathf.Pow(Mathf.Sin(t), 3);
                        float y = 13 * Mathf.Cos(t) - 5 * Mathf.Cos(2 * t) - 2 * Mathf.Cos(3 * t) - Mathf.Cos(4 * t);
                        pts.Add(V(c + x * 3.4f, c + 6 + y * 3.4f));
                    }
                    p.Shape(pts, IsoPaint.Radial(Shade(red, 0.35f), Shade(red, -0.2f), c - 18, c + 24, 70), Ink, 7);
                    p.Ellipse(c - 22, c + 26, 9, 6, new Color(1, 1, 1, 0.75f));
                    break;
                }
                case "sword":
                {
                    var blade = (Color)new Color32(0xDC, 0xEE, 0xFF, 255);
                    p.Shape(new[] { V(30, 40), V(42, 30), V(112, 100), V(118, 118), V(100, 112) }, VGrad(Shade(blade, -0.2f), Shade(blade, 0.2f), 30, 118), Ink, 6);
                    p.Line(V(40, 44), V(108, 110), 3, new Color(1, 1, 1, 0.9f));
                    p.Line(V(18, 50), V(50, 18), 16, Ink); p.Line(V(18, 50), V(50, 18), 9, new Color32(0xFF, 0xC4, 0x3C, 255));
                    p.Line(V(14, 14), V(34, 34), 16, Ink); p.Line(V(14, 14), V(34, 34), 9, new Color32(0x8A, 0x5A, 0xE0, 255));
                    p.Circle(12, 12, 9, Ink); p.Circle(12, 12, 5, new Color32(0xFF, 0xC4, 0x3C, 255));
                    break;
                }
                case "shield":
                {
                    var blue = (Color)new Color32(0x3E, 0x86, 0xF0, 255);
                    var pts = new[] { V(c, 120), V(112, 100), V(108, 50), V(c, 8), V(20, 50), V(16, 100) };
                    p.Shape(pts, VGrad(Shade(blue, -0.25f), Shade(blue, 0.25f), 8, 120), Ink, 7);
                    p.Poly(new[] { V(c, 110), V(c, 18), V(28, 54), V(26, 96) }, new Color(1, 1, 1, 0.22f));
                    break;
                }
                case "coin":
                {
                    var silver = (Color)new Color32(0x9C, 0xC8, 0xF0, 255);
                    p.Blob(c, c, 54, 54, IsoPaint.Radial(Shade(silver, 0.35f), Shade(silver, -0.25f), c - 18, c + 18, 80), Ink, 7);
                    p.Blob(c, c, 34, 34, Shade(silver, -0.12f), Shade(silver, -0.45f), 5);
                    p.Ellipse(c - 16, c + 20, 10, 7, new Color(1, 1, 1, 0.7f));
                    break;
                }
                case "exp":
                {
                    var green = (Color)new Color32(0x5C, 0xD0, 0x6A, 255);
                    p.Shape(Star(c, c, 58, 6, 1, 0), IsoPaint.Radial(Shade(green, 0.3f), Shade(green, -0.25f), c - 14, c + 20, 80), Ink, 7);
                    p.Shape(Star(c, c, 40, 6, 1, 0), Shade(green, -0.15f), Shade(green, -0.4f), 3);
                    break;
                }
                case "cog":
                {
                    var grey = (Color)new Color32(0xC8, 0xCE, 0xE0, 255);
                    var pts = new List<Vector2>();
                    for (int i = 0; i < 8; i++)
                    {
                        float a0 = i / 8f * Mathf.PI * 2;
                        foreach (var (da, r) in new[] { (-0.2f, 42f), (-0.13f, 58f), (0.13f, 58f), (0.2f, 42f) })
                            pts.Add(V(c + Mathf.Cos(a0 + da) * r, c + Mathf.Sin(a0 + da) * r));
                    }
                    p.Shape(pts, IsoPaint.Radial(Shade(grey, 0.3f), Shade(grey, -0.25f), c - 14, c + 14, 80), Ink, 6);
                    p.Blob(c, c, 18, 18, new Color32(0x3A, 0x44, 0x6A, 255), Ink, 5);
                    break;
                }
                case "skull":
                case "boss_rail_top":
                {
                    var red = id == "skull" ? (Color)new Color32(0xF2, 0xEC, 0xE0, 255) : (Color)new Color32(0xE8, 0x40, 0x48, 255);
                    p.Shape(RoundRect(c - 22, 14, 44, 30, 9), Shade(red, -0.15f), Ink, 6);
                    p.Blob(c, 70, 50, 44, IsoPaint.Radial(Shade(red, 0.3f), Shade(red, -0.2f), c - 14, 84, 70), Ink, 7);
                    p.Ellipse(c - 19, 66, 13, 14, Ink); p.Ellipse(c + 19, 66, 13, 14, Ink);
                    p.Ellipse(c - 18, 65, 5, 5, new Color32(0xFF, 0x50, 0x40, 255)); p.Ellipse(c + 18, 65, 5, 5, new Color32(0xFF, 0x50, 0x40, 255));
                    if (id != "skull")
                        foreach (int s in new[] { -1, 1 })
                            p.Shape(new[] { V(c + s * 30, 100), V(c + s * 50, 104), V(c + s * 60, 126), V(c + s * 40, 112) }, new Color32(0x7B, 0x2B, 0x3A, 255), Ink, 5);
                    break;
                }
                case "dice":
                {
                    DrawDie(p, 14, 26, 70, -12, new Color32(0xF6, 0xF2, 0xEC, 255), 3);
                    DrawDie(p, 52, 46, 64, 14, new Color32(0xF6, 0xF2, 0xEC, 255), 5);
                    break;
                }
                case "book":
                {
                    var red = (Color)new Color32(0xB8, 0x3C, 0x6E, 255);
                    p.Shape(new[] { V(30, 10), V(104, 22), V(104, 120), V(30, 108) }, new Color32(0xF2, 0xE6, 0xD0, 255), Ink, 6);
                    p.Shape(new[] { V(20, 14), V(92, 24), V(92, 122), V(20, 112) }, VGrad(Shade(red, -0.2f), Shade(red, 0.2f), 14, 122), Ink, 6);
                    p.Line(V(28, 18), V(28, 110), 5, Shade(red, -0.35f));
                    p.Shape(Star(58, 68, 22, 4, 0.45f, 90), new Color32(0xFF, 0xD0, 0x48, 255), Ink, 4);
                    break;
                }
                case "speed":
                {
                    var gold = (Color)new Color32(0xF6, 0xE6, 0xB8, 255);
                    foreach (float x0 in new[] { 18f, 58f })
                        p.Shape(new[] { V(x0, 20), V(x0 + 26, 20), V(x0 + 56, c), V(x0 + 26, 108), V(x0, 108), V(x0 + 30, c) }, gold, Ink, 6);
                    break;
                }
                case "reroll":
                {
                    var col = (Color)new Color32(0xF6, 0xF2, 0xFF, 255);
                    var arc = new List<Vector2>();
                    for (int i = 0; i <= 16; i++) { float a = Mathf.Lerp(30, 300, i / 16f) * Mathf.Deg2Rad; arc.Add(V(c + Mathf.Cos(a) * 40, c + Mathf.Sin(a) * 40)); }
                    p.Stroke(arc, 20, Ink, false);
                    p.Stroke(arc, 11, col, false);
                    p.Shape(new[] { V(c + 34, c + 2), V(c + 62, c + 34), V(c + 20, c + 44) }, col, Ink, 5);
                    break;
                }
                case "pedestal":
                    return Pedestal();
                default:
                    p.Blob(c, c, 50, 50, Color.gray, Ink, 6);
                    break;
            }
            return p.ToSprite("rs_hud_" + id, V(c, c));
        }

        /// <summary>A die seen at an angle (a rounded square with pips) for the dice icons.</summary>
        private static void DrawDie(IsoPaint p, float x, float y, float s, float rotDeg, Color face, int value)
        {
            var c = V(x + s / 2, y + s / 2);
            float a = rotDeg * Mathf.Deg2Rad;
            Vector2 R(Vector2 v) => c + new Vector2(v.x * Mathf.Cos(a) - v.y * Mathf.Sin(a), v.x * Mathf.Sin(a) + v.y * Mathf.Cos(a));
            var sq = RoundRect(-s / 2, -s / 2, s, s, s * 0.22f);
            var pts = new List<Vector2>();
            foreach (var v in sq) pts.Add(R(v));
            var side = new List<Vector2>();
            foreach (var v in sq) side.Add(R(v) + V(0, -s * 0.12f));
            p.Shape(side, Shade(face, -0.35f), Ink, 5);
            p.Shape(pts, IsoPaint.Radial(Color.white, Shade(face, -0.08f), c.x - s * 0.2f, c.y + s * 0.2f, s), Ink, 5);
            foreach (var pip in RunFx.Pips(value)) { var q = R(pip * (s * 0.28f)); p.Circle(q.x, q.y, s * 0.085f, value == 1 ? (Color)new Color32(0xE8, 0x3A, 0x4A, 255) : (Color)new Color32(0xE0, 0x3A, 0x4A, 255)); }
        }

        /// <summary>The stone pedestal under the dice button (pivot: top centre).</summary>
        private static Sprite Pedestal()
        {
            int w = 760, h = 520;
            var p = new IsoPaint(w, h);
            float cx = w / 2f;
            var stone = (Color)new Color32(0xB4, 0xBE, 0xD2, 255);
            float top = h - 20;
            // a big iso block: top diamond, two faces down to the bottom edge
            var T = V(cx, top); var L = V(cx - 360, top - 150); var R = V(cx + 360, top - 150); var B = V(cx, top - 300);
            p.Shape(new[] { L, B, V(B.x, 0), V(L.x, 0) }, VGrad(Shade(stone, -0.35f), Shade(stone, -0.12f), 0, top), Ink, 6);
            p.Shape(new[] { B, R, V(R.x, 0), V(B.x, 0) }, VGrad(Shade(stone, -0.45f), Shade(stone, -0.25f), 0, top), Ink, 6);
            p.Shape(new[] { T, R, B, L }, IsoPaint.Radial(Shade(stone, 0.2f), Shade(stone, -0.05f), cx, top - 110, 380), Ink, 6);
            // block seams
            for (int i = 1; i < 3; i++)
            {
                p.Line(V(L.x + 120 * i, L.y - 50 * i), V(L.x + 120 * i, 0), 4, Shade(stone, -0.5f));
                p.Line(V(R.x - 120 * i, R.y - 50 * i), V(R.x - 120 * i, 0), 4, Shade(stone, -0.6f));
            }
            p.Line(V(L.x, L.y - 140), V(B.x, B.y - 140), 4, Shade(stone, -0.5f));
            p.Line(V(B.x, B.y - 140), V(R.x, R.y - 140), 4, Shade(stone, -0.6f));
            return p.ToSprite("rs_hud_pedestal", V(cx, h));
        }

        /// <summary>
        /// The big 3D dice button face (a red rounded block seen from above at an angle, two dice printed on it): the
        /// ArtCatalog "dice" art is the dice icon, this is the button body (pivot: centre).
        /// </summary>
        public static Sprite DiceButton(bool active)
        {
            string key = "dicebutton:" + active;
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            int w = 640, h = 560;
            var p = new IsoPaint(w, h);
            var red = active ? (Color)new Color32(0xEC, 0x4E, 0x3E, 255) : (Color)new Color32(0x8C, 0x84, 0x9A, 255);
            float cx = w / 2f, cy = h / 2f + 40;
            // a rounded square rotated 45° and squashed (iso), with a thick side
            var top = new List<Vector2>();
            var side = new List<Vector2>();
            foreach (var v in RoundRect(-190, -190, 380, 380, 70, 10))
            {
                float a = 45 * Mathf.Deg2Rad;
                var r = new Vector2(v.x * Mathf.Cos(a) - v.y * Mathf.Sin(a), (v.x * Mathf.Sin(a) + v.y * Mathf.Cos(a)) * 0.74f);
                top.Add(V(cx + r.x, cy + r.y));
                side.Add(V(cx + r.x, cy + r.y - 70));
            }
            // side band: hull of the top and its lowered copy
            for (int i = 0; i < top.Count; i++)
            {
                var a = top[i]; var b = top[(i + 1) % top.Count];
                p.Poly(new[] { a, b, side[(i + 1) % top.Count], side[i] }, Shade(red, -0.38f));
            }
            p.Poly(side, Shade(red, -0.38f));
            p.Stroke(side, 7, Ink);
            p.Poly(top, IsoPaint.Radial(Shade(red, 0.22f), Shade(red, -0.08f), cx - 60, cy + 70, 320));
            p.Stroke(top, 7, Ink);
            // inner bevel
            var inner = new List<Vector2>();
            foreach (var v in top) inner.Add(Vector2.Lerp(v, V(cx, cy), 0.08f));
            p.Stroke(inner, 6, new Color(1, 1, 1, 0.22f));
            var s2 = p.ToSprite("rs_hud_dicebutton_" + active, V(cx, h / 2f));
            Cache[key] = s2;
            return s2;
        }
    }
}
