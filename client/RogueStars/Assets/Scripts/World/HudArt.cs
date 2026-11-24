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

        /// <summary>The stone pedestal under the dice button (<see cref="DiceStand"/>: same frame and camera as the cap).</summary>
        public static Sprite Pedestal()
        {
            const string key = "dicestand:pedestal";
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            s = DiceStand.DrawPedestal();
            Cache[key] = s;
            return s;
        }

        /// <summary>
        /// The big dice button: a glossy red key cap (<see cref="DiceStand"/>) drawn in the same frame and through the same
        /// camera as <see cref="Pedestal"/>, so laid over it its base sits exactly on the pedestal's top. The dice printed on
        /// its top are the run screen's DiceView, laid into the top plane (<see cref="DiceStand.TopPlane"/>).
        /// </summary>
        public static Sprite DiceButton(bool active)
        {
            string key = "dicestand:cap:" + active;
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            s = DiceStand.DrawCap(active);
            Cache[key] = s;
            return s;
        }

        /// <summary>
        /// The worm's battle mount (placeholder for ArtCatalog <c>Art/Slice/Mounts/&lt;id&gt;</c>): a hover saucer with an
        /// open seat tub — drawn in front of the worm, whose tail sits inside the tub (pivot: centre).
        /// </summary>
        public static Sprite Mount()
        {
            const string key = "mount";
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            int w = 620, h = 312;
            var p = new IsoPaint(w, h);
            float cx = w / 2f;
            var metal = (Color)new Color32(0x9C, 0xA4, 0xC8, 255);
            var hull = (Color)new Color32(0x6C, 0x64, 0xB0, 255);
            // the hover glow under the saucer
            p.Ellipse(cx, 34, 210, 30, (x, y) => new Color(0.45f, 1f, 0.95f, 0.45f * Mathf.Clamp01(1 - Mathf.Abs(x - cx) / 210f)));
            p.Shape(new[] { V(cx - 120, 104), V(cx + 120, 104), V(cx + 70, 44), V(cx - 70, 44) }, new Color(0.55f, 1f, 0.95f, 0.22f), new Color(0, 0, 0, 0), 0);
            // underside and disc
            p.Blob(cx, 128, 292, 58, VGrad(Shade(hull, -0.45f), Shade(hull, -0.15f), 70, 186), Ink, 7);
            p.Blob(cx, 150, 296, 50, VGrad(Shade(metal, -0.2f), Shade(metal, 0.25f), 100, 200), Ink, 7);
            // rim lights along the front edge
            for (int i = 0; i < 9; i++)
            {
                float t = (i + 0.5f) / 9f;
                float a = Mathf.Lerp(200, 340, t) * Mathf.Deg2Rad;
                var q = V(cx + Mathf.Cos(a) * 262, 150 + Mathf.Sin(a) * 40);
                p.Blob(q.x, q.y, 11, 9, i % 2 == 0 ? (Color)new Color32(0xFF, 0xD8, 0x48, 255) : (Color)new Color32(0x6C, 0xF0, 0xE0, 255), Ink, 3);
            }
            // the seat tub: its front wall from the rim down onto the disc
            var tub = new List<Vector2>();
            for (int i = 0; i <= 16; i++) { float a = Mathf.Lerp(180, 360, i / 16f) * Mathf.Deg2Rad; tub.Add(V(cx + Mathf.Cos(a) * 158, 246 + Mathf.Sin(a) * 34)); }
            for (int i = 0; i <= 16; i++) { float a = Mathf.Lerp(0, -180, i / 16f) * Mathf.Deg2Rad; tub.Add(V(cx + Mathf.Cos(a) * 176, 172 + Mathf.Sin(a) * 40)); }
            p.Shape(tub, (x, y) => Color.Lerp(Shade(hull, 0.25f), Shade(hull, -0.3f), Mathf.Clamp01((x - (cx - 176)) / 352f)), Ink, 7);
            // the rim's front lip and a stripe
            var lip = new List<Vector2>();
            for (int i = 0; i <= 16; i++) { float a = Mathf.Lerp(180, 360, i / 16f) * Mathf.Deg2Rad; lip.Add(V(cx + Mathf.Cos(a) * 158, 246 + Mathf.Sin(a) * 34)); }
            p.Stroke(lip, 18, Ink, false);
            p.Stroke(lip, 10, Shade(metal, 0.3f), false);
            var stripe = new List<Vector2>();
            for (int i = 0; i <= 16; i++) { float a = Mathf.Lerp(185, 355, i / 16f) * Mathf.Deg2Rad; stripe.Add(V(cx + Mathf.Cos(a) * 166, 210 + Mathf.Sin(a) * 36)); }
            p.Stroke(stripe, 7, new Color32(0xFF, 0xC8, 0x48, 255), false);
            // a gloss on the tub
            p.Ellipse(cx - 92, 214, 26, 12, new Color(1, 1, 1, 0.35f));
            var s2 = p.ToSprite("rs_hud_mount", V(cx, h / 2f));
            Cache[key] = s2;
            return s2;
        }

        private static Sprite _arena;

        /// <summary>
        /// The battle arena floor seen 3/4 top-down (placeholder for ArtCatalog slice-battle "battle_meadow"): a sandy clearing
        /// with a darker rock-and-reef band at the back, cracked stone slabs set into the sand, rocks and plants along the
        /// edges, pebbles and shells.
        /// </summary>
        public static Sprite Arena()
        {
            if (_arena != null) return _arena;
            int w = 540, h = 740;
            var p = new IsoPaint(w, h);
            var sand = (Color)new Color32(0xEC, 0xD8, 0x9C, 255);
            var sandDark = (Color)new Color32(0xC8, 0xB0, 0x74, 255);
            p.Poly(new[] { V(0, 0), V(w, 0), V(w, h), V(0, h) }, (x, y) =>
            {
                float n = Mathf.PerlinNoise(x / 80f + 2, y / 80f + 5) * 0.7f + Mathf.PerlinNoise(x / 23f, y / 23f) * 0.3f;
                var c = Color.Lerp(sandDark, sand, Mathf.SmoothStep(0.3f, 0.7f, n));
                // darker towards the edges (a lit clearing)
                float ex = Mathf.Abs(x - w / 2f) / (w / 2f);
                return Shade(c, -0.2f * Mathf.SmoothStep(0.55f, 1f, ex));
            });
            // the far side: a dark reef wall with rock lumps and kelp, darkening to the top
            var reefTop = (Color)new Color32(0x14, 0x30, 0x48, 255);
            var reef = (Color)new Color32(0x2C, 0x5E, 0x70, 255);
            var back = new List<Vector2> { V(0, h), V(w, h) };
            for (int i = 0; i <= 12; i++) { float x = w - i * w / 12f; back.Add(V(x, h - 170 + Mathf.Sin(i * 1.7f) * 18 + (i % 3) * 8)); }
            p.Poly(back, VGrad(reef, reefTop, h - 190, h));
            var rock = (Color)new Color32(0x7C, 0x96, 0xA8, 255);
            for (int i = 0; i < 8; i++)
            {
                float x = 20 + i * 72, y = h - 168 + (i % 3) * 10;
                p.Blob(x, y, 42 + (i % 2) * 10, 26, IsoPaint.Radial(Shade(rock, 0.25f), Shade(rock, -0.35f), x - 14, y + 12, 50), Ink, 3);
            }
            var kelp = (Color)new Color32(0x4E, 0xA8, 0x6E, 255);
            for (int i = 0; i < 10; i++)
            {
                float x0 = 14 + i * 56;
                var pts = new List<Vector2>();
                for (int j = 0; j <= 6; j++) { float t = j / 6f; pts.Add(V(x0 + Mathf.Sin(t * 3 + i) * 10, h - 150 + t * (70 + (i % 3) * 22))); }
                p.Stroke(pts, 9, new Color32(0x1E, 0x46, 0x34, 255), false);
                p.Stroke(pts, 5, kelp, false);
            }
            // cracked stone slabs set into the sand
            var slab = (Color)new Color32(0x9C, 0xB4, 0xC2, 255);
            foreach (var r in new[] { new Vector4(300, 400, 120, 58), new Vector4(420, 320, 86, 44), new Vector4(200, 300, 92, 44), new Vector4(360, 220, 80, 38), new Vector4(130, 450, 70, 34), new Vector4(260, 150, 76, 34) })
            {
                var pts = new[] { V(r.x - r.z, r.y), V(r.x - r.z * 0.2f, r.y + r.w), V(r.x + r.z, r.y + r.w * 0.3f), V(r.x + r.z * 0.3f, r.y - r.w) };
                var sh = new Vector2[4];
                for (int i = 0; i < 4; i++) sh[i] = pts[i] + V(4, -6);
                p.Poly(sh, new Color(0.3f, 0.22f, 0.1f, 0.35f));
                p.Shape(pts, (x, y) => Color.Lerp(Shade(slab, -0.12f), Shade(slab, 0.1f), Mathf.Clamp01((y - (r.y - r.w)) / (2 * r.w))), Shade(slab, -0.45f), 3);
                p.Line(pts[0] + V(4, 2), pts[1] + V(2, -3), 3, Shade(slab, 0.3f));
                p.Line(Vector2.Lerp(pts[0], pts[2], 0.3f), Vector2.Lerp(pts[1], pts[3], 0.6f), 2.5f, Shade(slab, -0.35f));
                p.Line(Vector2.Lerp(pts[0], pts[2], 0.6f), Vector2.Lerp(pts[2], pts[3], 0.5f), 2, Shade(slab, -0.3f));
            }
            // rocks and plants along the left / right edges
            var rnd = new System.Random(3);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            for (int i = 0; i < 7; i++)
            {
                foreach (int sideX in new[] { 0, 1 })
                {
                    float x = sideX == 0 ? R(-10, 40) : R(w - 40, w + 10), y = 40 + i * 78 + R(-10, 10);
                    if (i % 2 == sideX)
                    {
                        float rr = R(22, 36);
                        p.Blob(x, y, rr * 1.3f, rr, IsoPaint.Radial(Shade(rock, 0.3f), Shade(rock, -0.3f), x - rr * 0.4f, y + rr * 0.4f, rr * 1.8f), Ink, 3);
                    }
                    else
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            var pts = new List<Vector2>();
                            float x0 = x + (k - 1) * 9, sway = (k - 1) * 12 + (sideX == 0 ? 8 : -8);
                            for (int j = 0; j <= 6; j++) { float t = j / 6f; pts.Add(V(x0 + Mathf.Sin(t * 2.4f) * sway, y + t * (60 + k * 10))); }
                            p.Stroke(pts, 10, new Color32(0x1E, 0x46, 0x34, 255), false);
                            p.Stroke(pts, 6, k == 1 ? Shade(kelp, 0.2f) : kelp, false);
                        }
                    }
                }
            }
            // pebbles and shells
            for (int i = 0; i < 26; i++)
            {
                float x = R(40, w - 40), y = R(20, h - 200), r = R(3, 8);
                p.Blob(x, y, r * 1.4f, r, IsoPaint.Radial(new Color32(0xB4, 0xBC, 0xC8, 255), new Color32(0x7A, 0x84, 0x96, 255), x - r * 0.4f, y + r * 0.4f, r * 2), Shade(sandDark, -0.45f), 1.8f);
            }
            var shell = (Color)new Color32(0xF6, 0xE6, 0xD8, 255);
            for (int i = 0; i < 4; i++)
            {
                float x = R(60, w - 60), y = R(40, h - 240), r = R(8, 12);
                var fan = new List<Vector2> { V(x, y - r * 0.6f) };
                for (int j = 0; j <= 8; j++) { float a = Mathf.Lerp(20, 160, j / 8f) * Mathf.Deg2Rad; fan.Add(V(x + Mathf.Cos(a) * r, y - r * 0.6f + Mathf.Sin(a) * r)); }
                p.Shape(fan, shell, Shade(shell, -0.5f), 1.6f);
            }
            _arena = p.ToSprite("rs_arena", V(w / 2f, h / 2f));
            return _arena;
        }

        /// <summary>A vertical gradient strip (bottom → top colour) for filled bars (the boss rail).</summary>
        public static Sprite Gradient(Color bottom, Color top)
        {
            string key = "grad:" + ColorUtility.ToHtmlStringRGBA(bottom) + ColorUtility.ToHtmlStringRGBA(top);
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            var p = new IsoPaint(8, 128);
            p.Poly(new[] { V(0, 0), V(8, 0), V(8, 128), V(0, 128) }, VGrad(bottom, top, 0, 128));
            s = p.ToSprite("rs_grad", V(4, 64));
            Cache[key] = s;
            return s;
        }
    }
}
