using System.Collections.Generic;
using UnityEngine;
using static RogueStars.Client.World.IsoPaint;

namespace RogueStars.Client.World
{
    /// <summary>
    /// The run screen's dice button and its stone pedestal as one small 3D model seen through ONE perspective camera (in
    /// front, slightly above, looking down by <see cref="Pitch"/>): the pedestal (a carved slab on a coursed column) and the
    /// key cap (a rounded, slightly tapered block with a rounded top edge) share the camera, so the cap's base outline is the
    /// footprint it leaves on the slab, its sides converge like the column's and its top face squashes like the slab's.
    /// Both sprites are drawn into the same frame (<see cref="FrameW"/> × <see cref="FrameH"/> px, 1 px = 1 reference px) with
    /// the cap's base centre (the model origin) at <see cref="Base"/>; laid over each other at the same rect they line up.
    /// Model units = pixels at the camera's target depth; y up, z towards the camera.
    /// </summary>
    public static class DiceStand
    {
        /// <summary>Sprite frame of both layers (reference px).</summary>
        public const int FrameW = 860, FrameH = 720;
        /// <summary>Frame pixel (y up from the bottom) the model origin (cap base centre on the slab top) projects to.</summary>
        public static readonly Vector2 Base = new Vector2(FrameW / 2f, 436);
        /// <summary>Camera: looks down by Pitch degrees at a point TargetY over the slab from Distance away.</summary>
        public const float Pitch = 34f, Distance = 2300f, TargetY = 60f;
        /// <summary>Key cap: half width / depth at its shoulder, corner radius, height, rounded top edge, wider base (taper).</summary>
        public const float CapHalfW = 252, CapHalfD = 192, CapRadius = 96, CapHeight = 128, CapBevel = 38, CapTaper = 12;
        /// <summary>Pedestal: the top slab (flush with the cap's base) and the column under it.</summary>
        public const float SlabHalfW = 314, SlabHalfD = 256, SlabRadius = 58, SlabThick = 50;
        public const float ColHalfW = 290, ColHalfD = 234, ColRadius = 40, ColDepth = 900, Course = 60;

        private const int Seg = 8;
        private static readonly Color Ink = IsoArt.Ink;
        /// <summary>Key light: from the upper left, in front.</summary>
        private static readonly Vector3 Light = new Vector3(-0.45f, 0.78f, 0.45f).normalized;

        // ---- camera ------------------------------------------------------------------------------------------------------

        private static Vector3 CamPos
        {
            get
            {
                float th = Pitch * Mathf.Deg2Rad;
                return new Vector3(0, TargetY + Distance * Mathf.Sin(th), Distance * Mathf.Cos(th));
            }
        }

        private static Vector2 Raw(Vector3 p)
        {
            float th = Pitch * Mathf.Deg2Rad;
            var f = new Vector3(0, -Mathf.Sin(th), -Mathf.Cos(th));
            var u = new Vector3(0, Mathf.Cos(th), -Mathf.Sin(th));
            var d = p - CamPos;
            float z = Mathf.Max(1, Vector3.Dot(d, f));
            return new Vector2(Distance * d.x / z, Distance * Vector3.Dot(d, u) / z);
        }

        /// <summary>A model point in frame pixels (y up).</summary>
        public static Vector2 Project(Vector3 p) => Base + Raw(p) - Raw(Vector3.zero);

        /// <summary>Whether a surface at <paramref name="at"/> with outward normal <paramref name="n"/> faces the camera.</summary>
        private static bool Faces(Vector3 at, Vector3 n) => Vector3.Dot(n, CamPos - at) > 0;

        // ---- geometry ----------------------------------------------------------------------------------------------------

        /// <summary>A rounded rectangle ring in the xz plane at height y (counter-clockwise seen from above).</summary>
        public static List<Vector3> Ring(float halfW, float halfD, float r, float y)
        {
            var pts = new List<Vector3>();
            foreach (var v in RoundRect(-halfW, -halfD, 2 * halfW, 2 * halfD, r, Seg)) pts.Add(new Vector3(v.x, y, v.y));
            return pts;
        }

        /// <summary>Outward horizontal normal of ring edge i → i+1.</summary>
        private static Vector3 EdgeNormal(List<Vector3> ring, int i)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Count];
            var n = new Vector3(b.z - a.z, 0, -(b.x - a.x));
            return n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.forward;
        }

        private static List<Vector2> P(List<Vector3> ring)
        {
            var l = new List<Vector2>(ring.Count);
            foreach (var v in ring) l.Add(Project(v));
            return l;
        }

        /// <summary>Convex hull (monotone chain) of projected points: the silhouette for the ink outline.</summary>
        private static List<Vector2> Hull(List<Vector2> pts)
        {
            var s = new List<Vector2>(pts);
            s.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            foreach (var p in s) { while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], p) <= 0) h.RemoveAt(h.Count - 1); h.Add(p); }
            int lower = h.Count + 1;
            for (int i = s.Count - 2; i >= 0; i--) { var p = s[i]; while (h.Count >= lower && Cross(h[h.Count - 2], h[h.Count - 1], p) <= 0) h.RemoveAt(h.Count - 1); h.Add(p); }
            h.RemoveAt(h.Count - 1);
            return h;
        }

        private static List<Vector2> Shift(List<Vector2> pts, Vector2 d)
        {
            var l = new List<Vector2>(pts.Count);
            foreach (var v in pts) l.Add(v + d);
            return l;
        }

        /// <summary>Lambert-ish light of a surface normal: −1 (away) … 1 (facing the key light).</summary>
        private static float Lit(Vector3 n) => Vector3.Dot(n.normalized, Light);

        /// <summary>
        /// The visible quads of a band between two rings (same point count); <paramref name="tilt"/> lifts the horizontal
        /// edge normals (0 = vertical wall, 1 = facing up) for rounded edges and tapers.
        /// </summary>
        private static void Band(IsoPaint p, List<Vector3> lo, List<Vector3> hi, float tilt, System.Func<Vector3, float, Color> colour)
        {
            for (int i = 0; i < lo.Count; i++)
            {
                int j = (i + 1) % lo.Count;
                var nh = EdgeNormal(lo, i);
                var n = new Vector3(nh.x * Mathf.Cos(tilt * Mathf.PI / 2), Mathf.Sin(tilt * Mathf.PI / 2), nh.z * Mathf.Cos(tilt * Mathf.PI / 2));
                var mid = (lo[i] + lo[j] + hi[i] + hi[j]) / 4;
                if (!Faces(mid, n)) continue;
                var c = colour(n, mid.y);
                // a hair of overlap closes the seams between neighbouring quads
                var q = new[] { Project(lo[i]), Project(lo[j]), Project(hi[j]), Project(hi[i]) };
                var cen = (q[0] + q[1] + q[2] + q[3]) / 4;
                for (int k = 0; k < 4; k++) q[k] += new Vector2(Mathf.Sign(q[k].x - cen.x), Mathf.Sign(q[k].y - cen.y)) * 0.9f;
                p.Poly(q, c);
            }
        }

        /// <summary>The point at fraction <paramref name="t"/> of a ring's perimeter and its outward normal there.</summary>
        private static Vector3 AlongRing(List<Vector3> ring, float t, out Vector3 normal)
        {
            float total = 0;
            for (int i = 0; i < ring.Count; i++) total += Vector3.Distance(ring[i], ring[(i + 1) % ring.Count]);
            float want = Mathf.Repeat(t, 1) * total;
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                float len = Vector3.Distance(a, b);
                if (want <= len || i == ring.Count - 1)
                {
                    normal = EdgeNormal(ring, i);
                    return Vector3.Lerp(a, b, len > 0 ? Mathf.Clamp01(want / len) : 0);
                }
                want -= len;
            }
            normal = Vector3.forward;
            return ring[0];
        }

        /// <summary>Strokes the camera-facing part of a ring (seams, rim lines).</summary>
        private static void FrontStroke(IsoPaint p, List<Vector3> ring, float width, Color c, Vector2 offset = default)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                int j = (i + 1) % ring.Count;
                if (!Faces((ring[i] + ring[j]) / 2, EdgeNormal(ring, i))) continue;
                p.Line(Project(ring[i]) + offset, Project(ring[j]) + offset, width, c);
            }
        }

        // ---- the pedestal ------------------------------------------------------------------------------------------------

        public static readonly Color Stone = new Color32(0xA9, 0xB6, 0xC0, 255);

        public static Sprite DrawPedestal()
        {
            var p = new IsoPaint(FrameW, FrameH);
            var slabTop = Ring(SlabHalfW, SlabHalfD, SlabRadius, 0);
            var slabLow = Ring(SlabHalfW, SlabHalfD, SlabRadius, -SlabThick);
            var colTop = Ring(ColHalfW, ColHalfD, ColRadius, -SlabThick);
            var colLow = Ring(ColHalfW + 10, ColHalfD + 10, ColRadius, -SlabThick - ColDepth);

            // ink silhouettes: the column, then the slab over it (the slab overhangs: their union is not convex)
            var colPts = new List<Vector2>(); colPts.AddRange(P(colTop)); colPts.AddRange(P(colLow));
            var hull = Hull(colPts);
            p.Shape(hull, Ink, Ink, 7);

            // the column: stone faces, darker towards the ground (it disappears under the screen edge into shadow)
            float yTop = Project(new Vector3(0, -SlabThick, 0)).y;
            Band(p, colLow, colTop, 0, (n, y) => Shade(Stone, Mathf.Lerp(-0.62f, -0.08f, (Lit(n) + 1) / 2)));
            // a vertical darkening towards the bottom of the frame
            p.Poly(hull, (x, y) => new Color(0.04f, 0.05f, 0.1f, 0.55f * Mathf.Clamp01(1 - y / Mathf.Max(1, yTop)) * Mathf.Clamp01(1 - y / Mathf.Max(1, yTop))));
            // courses: carved horizontal seams with a lit lip under them, staggered joints between them
            var seam = Shade(Stone, -0.68f);
            var lip = Shade(Stone, -0.3f);
            int courses = 0;
            for (float y = -SlabThick - Course; y > -SlabThick - ColDepth; y -= Course, courses++)
            {
                float k = (-SlabThick - y) / ColDepth, hw = ColHalfW + 10 * k, hd = ColHalfD + 10 * k;
                var ring = Ring(hw, hd, ColRadius, y);
                if (Project(ring[0]).y < -10) break;
                FrontStroke(p, ring, 4, seam);
                FrontStroke(p, ring, 2, lip, new Vector2(0, -3));
                // joints of the course above this seam, evenly along the perimeter, staggered course by course
                const int Joints = 14;
                for (int jn = 0; jn < Joints; jn++)
                {
                    var at = AlongRing(ring, (jn + (courses % 2 == 0 ? 0.25f : 0.75f)) / Joints, out var n);
                    if (!Faces(at, n)) continue;
                    p.Line(Project(at + new Vector3(0, Course, 0)) + new Vector2(0, -2), Project(at), 4, seam);
                }
            }
            // the slab: its ink silhouette, its edge band, then the top with a carved border and soft mottling
            var slabPts = new List<Vector2>(); slabPts.AddRange(P(slabTop)); slabPts.AddRange(P(slabLow));
            p.Shape(Hull(slabPts), Ink, Ink, 7);
            var face = Shade(Stone, 0.06f);
            Band(p, slabLow, slabTop, 0, (n, y) => Shade(Stone, Mathf.Lerp(-0.5f, 0.02f, (Lit(n) + 1) / 2)));
            FrontStroke(p, slabLow, 5, Ink);
            var topPts = P(slabTop);
            float yBack = Project(new Vector3(0, 0, -SlabHalfD)).y, yFront = Project(new Vector3(0, 0, SlabHalfD)).y;
            p.Poly(topPts, (x, y) =>
            {
                float n = Mathf.PerlinNoise(x / 38f + 3.1f, y / 22f + 7.7f) * 0.6f + Mathf.PerlinNoise(x / 11f, y / 7f) * 0.4f;
                float back = Mathf.Clamp01((y - yFront) / Mathf.Max(1, yBack - yFront));
                return Shade(face, Mathf.Lerp(-0.12f, 0.16f, back) + (n - 0.5f) * 0.12f);
            });
            // the lit front edge of the top (a chamfer catching the light)
            FrontStroke(p, slabTop, 4, Shade(Stone, 0.4f), new Vector2(0, -2.5f));
            // carved border just inside the edge
            var groove = Ring(SlabHalfW - 20, SlabHalfD - 20, SlabRadius - 14, 0);
            var gp = P(groove);
            p.Stroke(Shift(gp, new Vector2(0, -2.5f)), 2, Shade(Stone, 0.3f));
            p.Stroke(gp, 4, Shade(Stone, -0.45f));

            // the cap's contact shadow on the slab: a soft halo, offset away from the key light, plus the occlusion line
            for (int k = 8; k >= 1; k--)
            {
                var sh = Ring(CapHalfW + CapTaper + k * 5, CapHalfD + CapTaper + k * 4, CapRadius + CapTaper * 0.6f + k * 4, 0);
                var pts = new List<Vector2>();
                foreach (var v in sh) pts.Add(Project(v + new Vector3(14, 0, -10)));
                p.Poly(pts, new Color(0.08f, 0.07f, 0.14f, 0.055f));
            }
            var foot = P(Ring(CapHalfW + CapTaper + 4, CapHalfD + CapTaper + 4, CapRadius + CapTaper * 0.6f + 3, 0));
            p.Poly(foot, new Color(0.06f, 0.05f, 0.1f, 0.4f));

            // weathering: a crack across the slab's front-left corner and one down its edge band
            var crack = Shade(Stone, -0.55f);
            var c0 = Project(new Vector3(-SlabHalfW + 6, 0, SlabHalfD - 60));
            var c1 = Project(new Vector3(-SlabHalfW + 34, 0, SlabHalfD - 36));
            var c2 = Project(new Vector3(-SlabHalfW + 44, 0, SlabHalfD - 4));
            p.Line(c0, c1, 3, crack); p.Line(c1, c2, 2.5f, crack);
            var e0 = Project(new Vector3(SlabHalfW - 70, 0, SlabHalfD)); var e1 = Project(new Vector3(SlabHalfW - 62, -SlabThick * 0.6f, SlabHalfD));
            p.Line(e0 + new Vector2(0, -3), e1, 2.5f, crack);
            return p.ToSprite("rs_hud_pedestal", new Vector2(FrameW / 2f, FrameH / 2f));
        }

        // ---- the key cap -------------------------------------------------------------------------------------------------

        /// <summary>The cap's rings from the base up: the wider base, the shoulder, the rounded top edge, the top outline.</summary>
        private static List<List<Vector3>> CapRings()
        {
            var rings = new List<List<Vector3>> { Ring(CapHalfW + CapTaper, CapHalfD + CapTaper, CapRadius + CapTaper * 0.6f, 0) };
            for (int i = 0; i <= 4; i++)
            {
                float a = i / 4f * Mathf.PI / 2;
                float inset = CapBevel * (1 - Mathf.Cos(a)), y = CapHeight - CapBevel + CapBevel * Mathf.Sin(a);
                rings.Add(Ring(CapHalfW - inset, CapHalfD - inset, Mathf.Max(8, CapRadius - inset), y));
            }
            return rings;
        }

        public static Sprite DrawCap(bool active)
        {
            var p = new IsoPaint(FrameW, FrameH);
            var red = active ? (Color)new Color32(0xEC, 0x4E, 0x3E, 255) : (Color)new Color32(0x8E, 0x88, 0x9C, 255);
            var rings = CapRings();
            var all = new List<Vector2>();
            foreach (var r in rings) all.AddRange(P(r));
            p.Shape(Hull(all), Ink, Ink, 7);
            // the walls (slightly tapered), lighter where they face the key light, a touch darker near the base
            Band(p, rings[0], rings[1], 0.08f, (n, y) => Shade(red, Mathf.Lerp(-0.5f, -0.08f, (Lit(n) + 1) / 2)));
            // the rounded top edge: bands tilting from the wall to the top
            for (int i = 1; i < rings.Count - 1; i++)
            {
                float tilt = (i - 0.5f) / (rings.Count - 2);
                Band(p, rings[i], rings[i + 1], tilt, (n, y) => Shade(red, Mathf.Lerp(-0.35f, 0.22f, (Lit(n) + 1) / 2)));
            }
            // a dark occlusion strip along the foot of the wall (where it meets the stone)
            FrontStroke(p, rings[0], 10, Shade(red, -0.62f), new Vector2(0, 4));
            FrontStroke(p, rings[0], 5, Ink);
            // the top: glossy, lighter towards the back (the light falls from above), a broad soft sheen and a hot spot
            var top = rings[rings.Count - 1];
            var tp = P(top);
            float yBack = Project(new Vector3(0, CapHeight, -CapHalfD)).y, yFront = Project(new Vector3(0, CapHeight, CapHalfD)).y;
            p.Poly(tp, (x, y) => Shade(red, Mathf.Lerp(-0.04f, 0.2f, Mathf.Clamp01((y - yFront) / Mathf.Max(1, yBack - yFront)))));
            var sheen = Project(new Vector3(-CapHalfW * 0.38f, CapHeight, -CapHalfD * 0.42f));
            p.Ellipse(sheen.x, sheen.y, 118, 30, (x, y) => new Color(1, 1, 1, 0.13f));
            p.Ellipse(sheen.x - 26, sheen.y + 6, 52, 12, new Color(1, 1, 1, 0.26f));
            // the shoulder catches a thin highlight along the front, the top rim a lit line
            FrontStroke(p, rings[2], 3.5f, Shade(red, active ? 0.4f : 0.3f));
            return p.ToSprite("rs_hud_dicebutton_" + active, new Vector2(FrameW / 2f, FrameH / 2f));
        }

        // ---- where the run screen lays things onto the cap ---------------------------------------------------------------

        /// <summary>
        /// The print plane on the cap's top (dice): its centre in the frame (0..1, y up) and the plane's depth squash
        /// (projected depth / projected width per unit at that point).
        /// </summary>
        public static void TopPlane(out Vector2 centre01, out float squash)
        {
            var c = Project(new Vector3(0, CapHeight, 0));
            var dx = Project(new Vector3(1, CapHeight, 0)) - c;
            var dz = Project(new Vector3(0, CapHeight, 1)) - c;
            centre01 = new Vector2(c.x / FrameW, c.y / FrameH);
            squash = dz.magnitude / Mathf.Max(1e-4f, dx.magnitude);
        }

        /// <summary>The middle of the cap's front wall in the frame (0..1, y up): the engraved caption.</summary>
        public static Vector2 FrontWall01
        {
            get
            {
                var c = Project(new Vector3(0, (CapHeight - CapBevel) * 0.48f, CapHalfD + CapTaper * 0.5f));
                return new Vector2(c.x / FrameW, c.y / FrameH);
            }
        }

        /// <summary>Height of the front wall on screen (px): the caption fits in it.</summary>
        public static float FrontWallHeight => Project(new Vector3(0, CapHeight - CapBevel, CapHalfD)).y - Project(new Vector3(0, 0, CapHalfD + CapTaper)).y;

        /// <summary>The model origin (the cap's base centre on the slab) in the frame (0..1, y up): the cap's press / pulse pivot.</summary>
        public static Vector2 Base01 => new Vector2(Base.x / FrameW, Base.y / FrameH);

        /// <summary>Highest point of the cap in the frame (px from the frame bottom).</summary>
        public static float CapTop
        {
            get
            {
                // constant geometry: computed once (the run screen's framing asks every frame)
                if (_capTop < 0)
                {
                    float m = 0;
                    foreach (var r in CapRings()) foreach (var v in r) m = Mathf.Max(m, Project(v).y);
                    _capTop = m;
                }
                return _capTop;
            }
        }
        private static float _capTop = -1;

        /// <summary>Highest point of the pedestal's slab in the frame (px from the frame bottom).</summary>
        public static float SlabTop
        {
            get
            {
                float m = 0;
                foreach (var v in Ring(SlabHalfW, SlabHalfD, SlabRadius, 0)) m = Mathf.Max(m, Project(v).y);
                return m;
            }
        }
    }
}
