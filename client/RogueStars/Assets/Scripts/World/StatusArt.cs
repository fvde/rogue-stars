using System.Collections.Generic;
using RogueStars.Rules.Core;
using UnityEngine;
using static RogueStars.Client.World.IsoPaint;

namespace RogueStars.Client.World
{
    /// <summary>
    /// Battle status icons drawn in code (BT4; placeholder until art exists): Heat / Burn a flame, Chill a snowflake, Freeze
    /// an ice crystal, Radiation (Poison) a trefoil, Bleed a blood drop, Stun stars, Shock a bolt, Charm a heart, Exposed a
    /// cracked shield, Stagger a spiral, Delay an hourglass. White-on-transparent glyphs with a dark ink outline, tinted by
    /// the icon tile behind them.
    /// </summary>
    public static class StatusArt
    {
        private static readonly Dictionary<StatusId, Sprite> Cache = new Dictionary<StatusId, Sprite>();
        private static readonly Color Ink = new Color32(0x1A, 0x10, 0x18, 255);

        public static Sprite Icon(StatusId s)
        {
            if (Cache.TryGetValue(s, out var sp) && sp != null) return sp;
            sp = Draw(s);
            Cache[s] = sp;
            return sp;
        }

        private static Sprite Draw(StatusId s)
        {
            const int n = 96;
            float c = n / 2f;
            var p = new IsoPaint(n, n);
            var w = Color.white;
            switch (s)
            {
                case StatusId.Burn:
                case StatusId.Heat:
                    p.Shape(new[] { V(c, 88), V(c + 18, 64), V(c + 28, 40), V(c + 22, 18), V(c, 8), V(c - 22, 18), V(c - 28, 40), V(c - 14, 58), V(c - 8, 44), V(c - 2, 66) }, w, Ink, 5);
                    p.Shape(new[] { V(c, 50), V(c + 10, 32), V(c + 6, 18), V(c, 14), V(c - 6, 18), V(c - 10, 30) }, new Color32(0xFF, 0xD2, 0x4A, 255), Ink, 3);
                    break;
                case StatusId.Chill:
                case StatusId.Freeze:
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * Mathf.PI / 3;
                        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 36;
                        p.Line(V(c, c) - d, V(c, c) + d, 11, Ink);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * Mathf.PI / 3;
                        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 34;
                        p.Line(V(c, c) - d, V(c, c) + d, 5, w);
                    }
                    if (s == StatusId.Freeze) p.Shape(Star(c, c, 14, 6, 0.55f), w, Ink, 3);
                    else p.Circle(c, c, 7, w);
                    break;
                case StatusId.Poison:
                    p.Circle(c, c, 40, Ink);
                    for (int i = 0; i < 3; i++)
                    {
                        float a0 = 90 + i * 120 - 30, a1 = a0 + 60;
                        var pts = new List<Vector2> { V(c, c) };
                        for (int k = 0; k <= 8; k++)
                        {
                            float a = Mathf.Lerp(a0, a1, k / 8f) * Mathf.Deg2Rad;
                            pts.Add(V(c + Mathf.Cos(a) * 36, c + Mathf.Sin(a) * 36));
                        }
                        p.Poly(pts, new Color32(0xD8, 0xFF, 0x5A, 255));
                    }
                    p.Circle(c, c, 12, Ink);
                    p.Circle(c, c, 7, new Color32(0xD8, 0xFF, 0x5A, 255));
                    break;
                case StatusId.Bleed:
                {
                    var pts = new List<Vector2> { V(c, 90) };
                    for (int k = 0; k <= 16; k++)
                    {
                        float a = Mathf.Lerp(20, 160, k / 16f) * Mathf.Deg2Rad;
                        pts.Add(V(c + Mathf.Cos(a) * 26, 36 - Mathf.Sin(a) * 26));
                    }
                    p.Shape(pts, w, Ink, 5);
                    p.Ellipse(c - 9, 38, 5, 9, new Color(1, 1, 1, 0.6f));
                    break;
                }
                case StatusId.Stun:
                    p.Shape(Star(c - 18, c + 10, 18, 5, 0.45f), w, Ink, 4);
                    p.Shape(Star(c + 18, c + 16, 14, 5, 0.45f), w, Ink, 4);
                    p.Shape(Star(c + 2, c - 20, 12, 5, 0.45f), w, Ink, 4);
                    break;
                case StatusId.Shock:
                    p.Shape(new[] { V(c + 8, 90), V(c - 22, 44), V(c - 2, 44), V(c - 10, 6), V(c + 24, 56), V(c + 4, 56) }, w, Ink, 5);
                    break;
                case StatusId.Charm:
                {
                    var pts = new List<Vector2>();
                    for (int i = 0; i <= 40; i++)
                    {
                        float t = i / 40f * Mathf.PI * 2;
                        float x = 16 * Mathf.Pow(Mathf.Sin(t), 3);
                        float y = 13 * Mathf.Cos(t) - 5 * Mathf.Cos(2 * t) - 2 * Mathf.Cos(3 * t) - Mathf.Cos(4 * t);
                        pts.Add(V(c + x * 2.4f, c + 4 + y * 2.4f));
                    }
                    p.Shape(pts, w, Ink, 5);
                    break;
                }
                case StatusId.Exposed:
                    p.Shape(new[] { V(c, 88), V(c + 32, 76), V(c + 28, 36), V(c, 8), V(c - 28, 36), V(c - 32, 76) }, w, Ink, 5);
                    p.Line(V(c + 4, 84), V(c - 8, 56), 5, Ink);
                    p.Line(V(c - 8, 56), V(c + 8, 40), 5, Ink);
                    p.Line(V(c + 8, 40), V(c - 2, 14), 5, Ink);
                    break;
                case StatusId.Stagger:
                {
                    var pts = new List<Vector2>();
                    for (int k = 0; k <= 40; k++)
                    {
                        float t = k / 40f * Mathf.PI * 4;
                        float r = 4 + k * 0.85f;
                        pts.Add(V(c + Mathf.Cos(t) * r, c + Mathf.Sin(t) * r));
                    }
                    p.Stroke(pts, 11, Ink, false);
                    p.Stroke(pts, 5, w, false);
                    break;
                }
                case StatusId.Delay:
                    p.Shape(new[] { V(c - 26, 86), V(c + 26, 86), V(c + 4, c), V(c + 26, 10), V(c - 26, 10), V(c - 4, c) }, w, Ink, 5);
                    p.Shape(new[] { V(c - 14, 18), V(c + 14, 18), V(c, 34) }, new Color32(0xFF, 0xD2, 0x4A, 255), Ink, 2);
                    break;
                default:
                    p.Circle(c, c, 30, Ink);
                    p.Circle(c, c, 24, w);
                    break;
            }
            return p.ToSprite("rs_status_" + s, V(c, c));
        }
    }
}
