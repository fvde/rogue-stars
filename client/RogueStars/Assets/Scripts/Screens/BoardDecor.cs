using System;
using System.Collections.Generic;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>One decor prop beside the path (board layout units of <see cref="RunScreen.TileCenter(RunTile)"/>, window origin 0).</summary>
    public struct DecorProp
    {
        public Vector2 Pos;
        /// <summary>Drawn size (square box); its footprint is the circle of <see cref="BoardDecor.Radius"/>.</summary>
        public float Size;
        /// <summary>Which prop sprite (index into the biome's props, modulo their count) / placeholder rock shape.</summary>
        public int Variant;
        /// <summary>A big landmark prop: beside a fork or the boss arena.</summary>
        public bool Landmark;
        /// <summary>Drawn mirrored left-right (never upside down).</summary>
        public bool Flip;
        public int Segment;
    }

    /// <summary>
    /// Decor props scattered by code along the sides of the path (pure, deterministic per run and segment): for every path
    /// tile of a segment, a chance of one prop on each side, a little away from the tile, plus a bigger landmark beside each
    /// fork and the boss arena. A prop never overlaps a tile (with a margin), a path edge or another prop, and stays on the
    /// board. Every candidate draws the same random numbers whether it is kept or not, so a segment's props only depend on the
    /// run, the segment and the tiles around it.
    /// </summary>
    public static class BoardDecor
    {
        public const float PropMin = 62, PropMax = 100, LandmarkMin = 150, LandmarkMax = 180;
        /// <summary>Chance of a prop on each side of a path tile (tuned so the board does not clutter).</summary>
        public const float Density = 0.3f;
        /// <summary>Free margin around a tile slab / between props / beside a path edge (board units).</summary>
        public const float TileGap = 16, PropGap = 10, EdgeGap = 14;
        /// <summary>Half width of the drawn path edge (its under-stroke).</summary>
        public const float EdgeHalf = 11;

        /// <summary>The footprint radius of a prop of drawn size <paramref name="size"/> (sprites have transparent margins).</summary>
        public static float Radius(float size) => size * 0.42f;

        /// <summary>A stable 32-bit seed of a run id (FNV-1a; the same on every platform).</summary>
        public static int Seed(string runId)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in runId ?? "") { h ^= c; h *= 16777619; }
                return (int)h;
            }
        }

        /// <summary>
        /// The props of every laid-out segment of <paramref name="tiles"/>. Segment s is placed against the tiles and path of
        /// segments ≤ s and the props placed before it only, so the board growing ahead never moves a prop behind; afterwards
        /// props that a later tile, path edge or the boss arena now covers are dropped (nothing else changes). The arena
        /// (<paramref name="bossGate"/>, drawn larger; −1 = none) gets its own landmark pass last.
        /// </summary>
        public static List<DecorProp> Place(IList<RunTile> tiles, int seed, int bossGate = -1)
        {
            var props = new List<DecorProp>();
            if (tiles == null || tiles.Count == 0) return props;
            var obstacles = new Obstacles(tiles, bossGate);
            var segments = new SortedSet<int>();
            foreach (var t in tiles) if (RunScreen.HasLayout(t) && t.Id != bossGate) segments.Add(t.Segment);
            foreach (int s in segments) PlaceSegment(tiles, s, seed, bossGate, obstacles, props);
            // later tiles / edges / the arena may now cover a prop: drop it (props are never added or moved here)
            props.RemoveAll(p => !obstacles.Clear(p.Pos, Radius(p.Size), int.MaxValue, true));
            // the boss arena's landmark
            foreach (var t in tiles)
                if (t.Id == bossGate && RunScreen.HasLayout(t))
                {
                    var rng = new System.Random(unchecked(seed * 31 - 7));
                    double side = rng.NextDouble(), size = rng.NextDouble(), variant = rng.NextDouble(), flip = rng.NextDouble(), along = rng.NextDouble();
                    float s = Mathf.Lerp(LandmarkMin, LandmarkMax, (float)size);
                    float first = side < 0.5 ? -1 : 1;
                    foreach (float dirSide in new[] { first, -first })
                    {
                        var p = Candidate(t, dirSide, s, 0.5f, (float)along, true);
                        if (!obstacles.Clear(p, Radius(s), int.MaxValue, true) || !Apart(p, Radius(s), props)) continue;
                        props.Add(new DecorProp { Pos = p, Size = s, Variant = (int)(variant * 1000), Landmark = true, Flip = flip < 0.5, Segment = t.Segment });
                        break;
                    }
                }
            return props;
        }

        private static void PlaceSegment(IList<RunTile> tiles, int segment, int seed, int bossGate, Obstacles obstacles, List<DecorProp> props)
        {
            var rng = new System.Random(unchecked(seed * 31 + segment * 7919 + 17));
            var seg = new List<RunTile>();
            foreach (var t in tiles) if (t.Segment == segment && t.Id != bossGate && RunScreen.HasLayout(t)) seg.Add(t);
            seg.Sort((a, b) => a.Id.CompareTo(b.Id));
            bool Free(Vector2 p, float r) => obstacles.Clear(p, r, segment, false) && Apart(p, r, props);
            // landmarks first: they need the most room (beside every fork)
            foreach (var t in seg)
            {
                double side = rng.NextDouble(), size = rng.NextDouble(), variant = rng.NextDouble(), flip = rng.NextDouble(), along = rng.NextDouble();
                if (t.Next.Count < 2) continue;
                float s = Mathf.Lerp(LandmarkMin, LandmarkMax, (float)size);
                float first = side < 0.5 ? -1 : 1;
                foreach (float dirSide in new[] { first, -first })
                {
                    var p = Candidate(t, dirSide, s, 0.6f, (float)along, false);
                    if (!Free(p, Radius(s))) continue;
                    props.Add(new DecorProp { Pos = p, Size = s, Variant = (int)(variant * 1000), Landmark = true, Flip = flip < 0.5, Segment = segment });
                    break;
                }
            }
            // small props on both sides of every tile
            foreach (var t in seg)
                foreach (float dirSide in new[] { -1f, 1f })
                {
                    double chance = rng.NextDouble(), size = rng.NextDouble(), dist = rng.NextDouble(), along = rng.NextDouble(), variant = rng.NextDouble(), flip = rng.NextDouble();
                    if (chance >= Density) continue;
                    float s = Mathf.Lerp(PropMin, PropMax, (float)size);
                    var p = Candidate(t, dirSide, s, (float)dist, (float)along, false);
                    if (!Free(p, Radius(s))) continue;
                    props.Add(new DecorProp { Pos = p, Size = s, Variant = (int)(variant * 1000), Flip = flip < 0.5, Segment = segment });
                }
        }

        private static bool Apart(Vector2 p, float r, List<DecorProp> props)
        {
            foreach (var q in props)
                if ((q.Pos - p).magnitude < r + Radius(q.Size) + PropGap) return false;
            return true;
        }

        /// <summary>A point beside a tile: across the path direction on one side, <paramref name="dist"/> 0..1 further out.</summary>
        private static Vector2 Candidate(RunTile t, float side, float size, float dist, float along, bool gate)
        {
            var c = RunScreen.TileCenter(t, 0);
            float a = (float)t.Dir * Mathf.Deg2Rad;
            var fwd = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            var across = new Vector2(fwd.y, -fwd.x);
            float half = (gate ? RunScreen.TileW + RunScreen.GateGrow : RunScreen.TileW) / 2;
            float d = half + Radius(size) + TileGap + 6 + dist * 110;
            return c + across * (side * d) + fwd * ((along - 0.5f) * 70);
        }

        /// <summary>Tile slabs and path edges (tagged with their segment) and the board's sides a prop must keep clear of.</summary>
        private sealed class Obstacles
        {
            private readonly List<Rect> _rects = new List<Rect>();
            private readonly List<int> _rectSeg = new List<int>();
            private readonly List<bool> _rectGate = new List<bool>();
            private readonly List<Vector2> _a = new List<Vector2>(), _b = new List<Vector2>();
            private readonly List<int> _edgeSeg = new List<int>();
            private readonly List<bool> _edgeGate = new List<bool>();

            public Obstacles(IList<RunTile> tiles, int bossGate)
            {
                var byId = new Dictionary<int, RunTile>();
                foreach (var t in tiles) byId[t.Id] = t;
                foreach (var t in tiles)
                {
                    if (!RunScreen.HasLayout(t)) continue;
                    _rects.Add(RunScreen.TileRect(t, t.Id == bossGate));
                    _rectSeg.Add(t.Segment);
                    _rectGate.Add(t.Id == bossGate);
                    foreach (int n in t.Next)
                        if (byId.TryGetValue(n, out var nt) && RunScreen.HasLayout(nt))
                        {
                            _a.Add(RunScreen.TileCenter(t, 0));
                            _b.Add(RunScreen.TileCenter(nt, 0));
                            _edgeSeg.Add(Math.Max(t.Segment, nt.Segment));
                            _edgeGate.Add(t.Id == bossGate || n == bossGate);
                        }
                }
            }

            /// <summary>True if a footprint stays on the board and clear of the tiles / edges of segments ≤ <paramref name="maxSegment"/> (and the arena when asked).</summary>
            public bool Clear(Vector2 p, float r, int maxSegment, bool withGate)
            {
                if (p.x - r * 0.7f < 0 || p.x + r * 0.7f > RunScreen.BoardWidth) return false;
                for (int i = 0; i < _rects.Count; i++)
                {
                    if (_rectSeg[i] > maxSegment || (_rectGate[i] && !withGate)) continue;
                    var rect = _rects[i];
                    if (Mathf.Abs(rect.center.y - p.y) > 400) continue;
                    if (CircleRectGap(p, rect) < r + TileGap) return false;
                }
                for (int i = 0; i < _a.Count; i++)
                {
                    if (_edgeSeg[i] > maxSegment || (_edgeGate[i] && !withGate)) continue;
                    if (Mathf.Min(_a[i].y, _b[i].y) - p.y > 400 || p.y - Mathf.Max(_a[i].y, _b[i].y) > 400) continue;
                    if (SegmentDistance(p, _a[i], _b[i]) < r + EdgeHalf + EdgeGap) return false;
                }
                return true;
            }
        }

        /// <summary>Distance from a point to a rect (0 inside).</summary>
        public static float CircleRectGap(Vector2 p, Rect r)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0, p.x - r.xMax);
            float dy = Mathf.Max(r.yMin - p.y, 0, p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Distance from a point to a line segment.</summary>
        public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len = ab.sqrMagnitude;
            float k = len > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len) : 0;
            return (p - (a + ab * k)).magnitude;
        }
    }
}
