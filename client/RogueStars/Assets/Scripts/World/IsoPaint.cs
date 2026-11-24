using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueStars.Client.World
{
    /// <summary>
    /// A tiny anti-aliased raster painter for the code-drawn placeholder art of the iso board (tile blocks, props, HUD
    /// icons): filled polygons (scanline coverage, 4 sub-rows), ellipses, thick lines with round caps, gradients through a
    /// shade function, source-over blending. Pixel space is y-up (row 0 = the bottom of the sprite). No assets.
    /// </summary>
    public sealed class IsoPaint
    {
        public readonly int W, H;
        private readonly Color[] _px;

        public IsoPaint(int w, int h)
        {
            W = Math.Max(1, w);
            H = Math.Max(1, h);
            _px = new Color[W * H];
        }

        /// <summary>Source-over blend of <paramref name="c"/> with coverage <paramref name="a"/> into a pixel.</summary>
        private void Blend(int x, int y, Color c, float a)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            float sa = c.a * Mathf.Clamp01(a);
            if (sa <= 0.0001f) return;
            int i = y * W + x;
            var d = _px[i];
            float oa = sa + d.a * (1 - sa);
            if (oa <= 0) return;
            float k = d.a * (1 - sa);
            _px[i] = new Color((c.r * sa + d.r * k) / oa, (c.g * sa + d.g * k) / oa, (c.b * sa + d.b * k) / oa, oa);
        }

        /// <summary>A filled polygon in one colour.</summary>
        public void Poly(IList<Vector2> pts, Color c) => Poly(pts, (x, y) => c);

        /// <summary>A filled polygon, coloured per pixel by <paramref name="shade"/> (x, y in pixels).</summary>
        public void Poly(IList<Vector2> pts, Func<float, float, Color> shade)
        {
            if (pts == null || pts.Count < 3) return;
            float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
            foreach (var p in pts) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); }
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(maxY));
            int x0 = Mathf.Max(0, Mathf.FloorToInt(minX)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(maxX));
            if (x1 < x0 || y1 < y0) return;
            var cover = new float[x1 - x0 + 2];
            var xs = new List<float>(8);
            const int Sub = 4;
            for (int y = y0; y <= y1; y++)
            {
                Array.Clear(cover, 0, cover.Length);
                bool any = false;
                for (int s = 0; s < Sub; s++)
                {
                    float sy = y + (s + 0.5f) / Sub;
                    xs.Clear();
                    for (int i = 0; i < pts.Count; i++)
                    {
                        var a = pts[i];
                        var b = pts[(i + 1) % pts.Count];
                        if ((a.y <= sy && b.y > sy) || (b.y <= sy && a.y > sy))
                            xs.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
                    }
                    if (xs.Count < 2) continue;
                    xs.Sort();
                    for (int k = 0; k + 1 < xs.Count; k += 2)
                    {
                        float l = Mathf.Max(xs[k], x0), r = Mathf.Min(xs[k + 1], x1 + 1);
                        if (r <= l) continue;
                        any = true;
                        int li = Mathf.FloorToInt(l), ri = Mathf.FloorToInt(r);
                        for (int x = li; x <= ri && x <= x1; x++)
                        {
                            float cl = Mathf.Max(l, x), cr = Mathf.Min(r, x + 1);
                            if (cr > cl) cover[x - x0] += (cr - cl) / Sub;
                        }
                    }
                }
                if (!any) continue;
                for (int x = x0; x <= x1; x++)
                {
                    float cv = cover[x - x0];
                    if (cv > 0.002f) Blend(x, y, shade(x + 0.5f, y + 0.5f), cv);
                }
            }
        }

        /// <summary>A filled ellipse.</summary>
        public void Ellipse(float cx, float cy, float rx, float ry, Color c) => Ellipse(cx, cy, rx, ry, (x, y) => c);

        public void Ellipse(float cx, float cy, float rx, float ry, Func<float, float, Color> shade)
        {
            if (rx <= 0 || ry <= 0) return;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rx - 1)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + rx + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry - 1)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + ry + 1));
            float m = Mathf.Min(rx, ry);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    float f = Mathf.Sqrt(dx * dx + dy * dy);
                    float cov = Mathf.Clamp01((1 - f) * m + 0.5f);
                    if (cov > 0) Blend(x, y, shade(x + 0.5f, y + 0.5f), cov);
                }
        }

        public void Circle(float cx, float cy, float r, Color c) => Ellipse(cx, cy, r, r, c);

        /// <summary>A thick line with round caps.</summary>
        public void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            float r = width / 2;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - r - 1)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + r + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - r - 1)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + r + 1));
            var ab = b - a;
            float len2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    float d = Vector2.Distance(p, a + ab * t);
                    float cov = Mathf.Clamp01(r - d + 0.5f);
                    if (cov > 0) Blend(x, y, c, cov);
                }
        }

        /// <summary>Lines through the points (closed: back to the first) — outlines.</summary>
        public void Stroke(IList<Vector2> pts, float width, Color c, bool closed = true)
        {
            if (pts == null) return;
            int n = closed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < n; i++) Line(pts[i], pts[(i + 1) % pts.Count], width, c);
        }

        /// <summary>A filled polygon with a dark outline (the cartoon look of every prop).</summary>
        public void Shape(IList<Vector2> pts, Color fill, Color outline, float width)
        {
            Poly(pts, fill);
            if (width > 0) Stroke(pts, width, outline);
        }

        public void Shape(IList<Vector2> pts, Func<float, float, Color> fill, Color outline, float width)
        {
            Poly(pts, fill);
            if (width > 0) Stroke(pts, width, outline);
        }

        /// <summary>An ellipse with an outline ring.</summary>
        public void Blob(float cx, float cy, float rx, float ry, Color fill, Color outline, float width)
        {
            if (width > 0) Ellipse(cx, cy, rx + width / 2, ry + width / 2, outline);
            Ellipse(cx, cy, Mathf.Max(0.5f, rx - width / 2), Mathf.Max(0.5f, ry - width / 2), fill);
        }

        public void Blob(float cx, float cy, float rx, float ry, Func<float, float, Color> fill, Color outline, float width)
        {
            if (width > 0) Ellipse(cx, cy, rx + width / 2, ry + width / 2, outline);
            Ellipse(cx, cy, Mathf.Max(0.5f, rx - width / 2), Mathf.Max(0.5f, ry - width / 2), fill);
        }

        /// <summary>Rounded rectangle polygon points (x, y = bottom-left).</summary>
        public static List<Vector2> RoundRect(float x, float y, float w, float h, float r, int seg = 6)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) / 2);
            var pts = new List<Vector2>();
            void Arc(float cx, float cy, float a0)
            {
                for (int i = 0; i <= seg; i++)
                {
                    float a = (a0 + 90f * i / seg) * Mathf.Deg2Rad;
                    pts.Add(new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r));
                }
            }
            Arc(x + w - r, y + r, -90);
            Arc(x + w - r, y + h - r, 0);
            Arc(x + r, y + h - r, 90);
            Arc(x + r, y + r, 180);
            return pts;
        }

        /// <summary>Regular polygon points (hexagons, stars with <paramref name="inner"/> &lt; 1).</summary>
        public static List<Vector2> Star(float cx, float cy, float r, int points, float inner = 1, float rot = 90)
        {
            var pts = new List<Vector2>();
            int n = inner < 1 ? points * 2 : points;
            for (int i = 0; i < n; i++)
            {
                float rr = inner < 1 && i % 2 == 1 ? r * inner : r;
                float a = (rot + 360f * i / n) * Mathf.Deg2Rad;
                pts.Add(new Vector2(cx + Mathf.Cos(a) * rr, cy + Mathf.Sin(a) * rr));
            }
            return pts;
        }

        public static Vector2 V(float x, float y) => new Vector2(x, y);

        /// <summary>A vertical gradient shade (bottom → top colour) over [y0, y1].</summary>
        public static Func<float, float, Color> VGrad(Color bottom, Color top, float y0, float y1) =>
            (x, y) => Color.Lerp(bottom, top, Mathf.Clamp01((y - y0) / Mathf.Max(1, y1 - y0)));

        /// <summary>A radial highlight: centre colour fading to the edge colour.</summary>
        public static Func<float, float, Color> Radial(Color centre, Color edge, float cx, float cy, float r) =>
            (x, y) => Color.Lerp(centre, edge, Mathf.Clamp01(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / Mathf.Max(1, r)));

        /// <summary>The painting as a sprite (bilinear, clamped, no mip maps).</summary>
        public Sprite ToSprite(string name, Vector2 pivotPixels, float ppu = 100)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(_px);
            tex.Apply(false, true);
            var s = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(pivotPixels.x / W, pivotPixels.y / H), ppu, 0, SpriteMeshType.FullRect);
            s.name = name;
            return s;
        }

        /// <summary>Darker / lighter variants of a colour (keeps alpha).</summary>
        public static Color Shade(Color c, float k) => k >= 0 ? Color.Lerp(c, Color.white, k) : Color.Lerp(c, Color.black, -k);
    }
}
