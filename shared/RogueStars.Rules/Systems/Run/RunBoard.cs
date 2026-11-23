using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Board generation (GDD §3.1): a long path that never repeats. The board is generated ahead of the worm in segments
    /// (<see cref="AppendSegment"/>): each segment is a trunk of about LevelDef.BoardLength steps with forks whose two arms
    /// (each RunTuning.BranchMinArm..BranchMaxArm tiles) rejoin further ahead, the level's fixed specials (campfire, shops,
    /// miniboss lair) on trunk tiles only (every path passes them: no fork bypasses a miniboss or a shop), the boss's dedicated
    /// tiles and a weighted pool fill with the segment's own mix (RunTuning.SegmentThemes). Edges only point forward, so no
    /// tile or fork is ever visited twice. Every segment also gets a 2D layout style (RunTuning.BoardLayouts: long straights
    /// with side loops, zig-zags, wide curves, hooks, diamond splits) that sets each tile's <see cref="RunTile.PosX"/> /
    /// <see cref="RunTile.PosY"/> / <see cref="RunTile.Dir"/> (display only; the layout uses no randomness of its own).
    /// RunSystem extends the board whenever the worm gets close to its end and opens the boss arena ahead of the worm when
    /// the awakening meter is full. Deterministic for a given Rng.
    /// </summary>
    public static class RunBoard
    {
        public sealed class Board
        {
            public List<RunTile> Tiles = new List<RunTile>();
            /// <summary>The default path (trunk and every fork's first arm).</summary>
            public List<int> MainPath = new List<int>();
            /// <summary>The open end of the path (the last trunk tile; the next segment starts after it).</summary>
            public int EndTile;
            public int Segments;
        }

        /// <summary>One piece of a segment in path order: a trunk tile, or a fork (two arms that rejoin at the next trunk tile).</summary>
        private sealed class Piece
        {
            public RunTile Tile;            // trunk tile (for a fork: the join)
            public List<RunTile> ArmA, ArmB; // fork arms (null for plain trunk tiles)
        }

        /// <summary>The start tile plus <see cref="RunTuning.InitialSegments"/> segments.</summary>
        public static Board Generate(ContentDb content, LevelDef level, Rng rng, ActiveRun run = null)
        {
            var b = new Board();
            var start = Add(b.Tiles, "start", 0, 0);
            b.MainPath.Add(start.Id);
            b.EndTile = start.Id;
            int n = Math.Max(1, content.RunTuning.InitialSegments);
            for (int i = 0; i < n; i++) AppendSegment(content, level, b, rng, run);
            return b;
        }

        /// <summary>Wraps a run's tiles to extend them (the run keeps its tiles and the open end).</summary>
        public static Board Wrap(List<RunTile> tiles, int endTile, int segments) => new Board { Tiles = tiles, EndTile = endTile, Segments = segments };

        /// <summary>The layout style of a segment index (deterministic from the board's own picks; null without styles).</summary>
        private static BoardLayoutDef PickLayout(RunTuning t, Rng rng)
        {
            if (t.BoardLayouts.Count == 0) return null;
            var w = new double[t.BoardLayouts.Count];
            for (int i = 0; i < w.Length; i++) w[i] = t.BoardLayouts[i].Weight;
            int idx = rng.PickWeighted(w);
            return idx < 0 ? null : t.BoardLayouts[idx];
        }

        /// <summary>Appends one segment after the open end (new tiles only; nothing behind the worm changes).</summary>
        public static void AppendSegment(ContentDb content, LevelDef level, Board b, Rng rng, ActiveRun run = null)
        {
            var t = content.RunTuning;
            int segment = b.Segments++;
            var theme = segment == 0 || t.SegmentThemes.Count == 0 ? null : t.SegmentThemes[rng.Range(0, t.SegmentThemes.Count)];
            var layout = PickLayout(t, rng);
            var startTile = b.Tiles[b.EndTile];
            var cur = startTile;
            int col = cur.X;
            int target = Math.Max(8, level.BoardLength);
            int minArm = Math.Max(1, t.BranchMinArm), maxArm = Math.Max(minArm, t.BranchMaxArm);
            // Trunk tiles the fixed specials need (+2: the open end stays free, one spare).
            int fixedCount = level.FixedTiles.Count;
            double branchChance = level.BranchChance * (layout != null ? layout.BranchMult : 1);
            int count = 0, trunkCount = 0;
            var path = new List<RunTile>();   // default path of the segment (trunk + first arms), in order
            var trunk = new List<RunTile>();  // tiles every path of the segment passes
            var all = new List<RunTile>();
            var pieces = new List<Piece>();
            bool lastWasJoin = false;
            while (count < target)
            {
                int left = target - count;
                // A fork needs room for its first arm + the join, and the trunk must still fit every fixed special.
                bool canBranch = count > 0 && !lastWasJoin && left >= maxArm + 3 && trunkCount + (left - (maxArm + 1)) + 1 >= fixedCount + 2;
                if (canBranch && rng.Chance(branchChance))
                {
                    int lenA = rng.Range(minArm, maxArm + 1), lenB = rng.Range(minArm, maxArm + 1);
                    var armA = Chain(b.Tiles, cur, lenA, col + 1, 0, segment, all);
                    var armB = Chain(b.Tiles, cur, lenB, col + 1, 1, segment, all);
                    col += Math.Max(lenA, lenB) + 1;
                    var join = Add(b.Tiles, null, col, 0);
                    join.Segment = segment;
                    all.Add(join);
                    armA[armA.Count - 1].Next.Add(join.Id);
                    armB[armB.Count - 1].Next.Add(join.Id);
                    path.AddRange(armA);
                    path.Add(join);
                    trunk.Add(join);
                    pieces.Add(new Piece { Tile = join, ArmA = armA, ArmB = armB });
                    count += lenA + 1;
                    trunkCount++;
                    lastWasJoin = true;
                    cur = join;
                }
                else
                {
                    col++;
                    var nt = Add(b.Tiles, null, col, 0);
                    nt.Segment = segment;
                    all.Add(nt);
                    cur.Next.Add(nt.Id);
                    path.Add(nt);
                    trunk.Add(nt);
                    pieces.Add(new Piece { Tile = nt });
                    count++;
                    trunkCount++;
                    lastWasJoin = false;
                    cur = nt;
                }
            }
            b.EndTile = cur.Id;
            foreach (var s in path) b.MainPath.Add(s.Id);

            // Fixed specials at fractions of the segment's default path, on trunk tiles only: every path passes them, so no
            // fork can bypass a miniboss lair, a shop or the campfire. The open end stays free (the next segment may fork there).
            var onTrunk = new HashSet<int>();
            foreach (var x in trunk) onTrunk.Add(x.Id);
            onTrunk.Remove(cur.Id);
            int last = path.Count - 1;
            foreach (var ft in level.FixedTiles)
            {
                int want = Math.Max(0, Math.Min(last, (int)Math.Round(ft.At * last)));
                RunTile slot = null;
                for (int d = 0; d <= last && slot == null; d++)
                    foreach (int idx in new[] { want + d, want - d })
                        if (idx >= 0 && idx <= last && onTrunk.Contains(path[idx].Id) && path[idx].TileId == null) { slot = path[idx]; break; }
                if (slot == null) continue;
                slot.TileId = ft.TileId;
                slot.Fixed = true;
            }

            // Dedicated boss tiles.
            BossDef boss = level.BossId != null && content.RunBosses.TryGetValue(level.BossId, out var bd) ? bd : null;
            if (boss != null && boss.BossTileIds.Count > 0)
            {
                var free = new List<RunTile>();
                foreach (var x in all) if (x.TileId == null) free.Add(x);
                for (int i = 0; i < boss.BossTileCount && free.Count > 0; i++)
                {
                    int k = rng.Range(0, free.Count);
                    var x = free[k];
                    free.RemoveAt(k);
                    x.TileId = boss.BossTileIds[i % boss.BossTileIds.Count];
                    x.Fixed = true;
                }
            }

            // Pool fill with the segment's mix ("once" tiles at most once per segment).
            var usedOnce = new HashSet<string>();
            foreach (var x in all)
                if (x.TileId != null && content.BoardTiles.TryGetValue(x.TileId, out var d) && d.Once) usedOnce.Add(x.TileId);
            foreach (var x in all)
                if (x.TileId == null) x.TileId = PickPoolTile(content, level.TilePoolId, rng, usedOnce, theme, run);

            Layout(t, b.Tiles, startTile, pieces, layout);
        }

        // ---- 2D layout ------------------------------------------------------------------------------------------------

        private const double Deg = Math.PI / 180.0;
        /// <summary>Layout positions are rounded to this (display only; keeps saved states tidy and platform-stable).</summary>
        private static double R(double v) => Math.Round(v, 3);

        /// <summary>
        /// Lays out one segment from its start tile (position and heading carried over): trunk tiles follow the style's heading
        /// program with a limited turn rate, inside the band; forks become diamonds (both arms bulge out symmetrically) or side
        /// loops (one straight arm, one swinging out toward the middle of the band). Uses no randomness.
        /// </summary>
        private static void Layout(RunTuning t, List<RunTile> tiles, RunTile start, List<Piece> pieces, BoardLayoutDef style)
        {
            double x = start.PosX, y = start.PosY, heading = start.Dir;
            double maxHeading = t.LayoutMaxHeading, maxTurn = t.LayoutMaxTurn, half = t.LayoutHalfWidth;
            var kind = style != null ? style.Kind : BoardLayoutKind.Straight;
            double amp = style != null ? style.Amplitude : 15, period = Math.Max(2, style != null ? style.Period : 12);
            // the style's swing starts toward the middle of the band
            double side = x > 0 ? -1 : 1;
            int k = 0;
            for (int p = 0; p < pieces.Count; p++)
            {
                var piece = pieces[p];
                bool forkSoon = false;
                for (int q = p; q < pieces.Count && q <= p + 3; q++) if (pieces[q].ArmA != null) forkSoon = true;
                if (piece.ArmA == null)
                {
                    double want = Target(kind, k, amp, period, side);
                    if (forkSoon) want = Clamp(-x * 18, -35, 35);                 // centre before a fork: arms need room
                    if (x > half && want > -30) want = -40;                         // keep to the band
                    if (x < -half && want < 30) want = 40;
                    want = Clamp(want, -maxHeading, maxHeading);
                    heading += Clamp(want - heading, -maxTurn, maxTurn);
                    heading = Clamp(heading, -maxHeading, maxHeading);
                    x += Math.Sin(heading * Deg);
                    y += Math.Cos(heading * Deg);
                    Place(piece.Tile, x, y, heading);
                    k++;
                    continue;
                }

                // Fork: chord from the fork tile to the join, leaning toward the middle of the band.
                double chord = Clamp(heading * 0.5 - x * 12, -30, 30);
                int nA = piece.ArmA.Count, nB = piece.ArmB.Count;
                bool loop = style != null && style.SideLoops;
                double len = (Math.Min(nA, nB) + 1) * (loop ? 0.9 : 0.8);
                double cx = Math.Sin(chord * Deg), cy = Math.Cos(chord * Deg);   // along the chord
                double px = cy, py = -cx;                                         // perpendicular (to the right)
                if (loop)
                {
                    // the shorter arm runs almost straight (a slight counter-curve keeps the arms apart at the fork); the other swings
                    // out toward the middle of the band
                    bool aStraight = nA <= nB;
                    double outSide = x > 0 ? -1 : 1;
                    ArmLayout(aStraight ? piece.ArmA : piece.ArmB, x, y, cx, cy, px, py, len, -outSide * Bulge(aStraight ? nA : nB, len, 0.5), chord);
                    ArmLayout(aStraight ? piece.ArmB : piece.ArmA, x, y, cx, cy, px, py, len, outSide * Bulge(aStraight ? nB : nA, len, 1.6), chord);
                }
                else
                {
                    ArmLayout(piece.ArmA, x, y, cx, cy, px, py, len, -Bulge(nA, len, 1.2), chord);
                    ArmLayout(piece.ArmB, x, y, cx, cy, px, py, len, Bulge(nB, len, 1.2), chord);
                }
                x += cx * len;
                y += cy * len;
                heading = chord;
                Place(piece.Tile, x, y, heading);
                k += Math.Min(nA, nB) + 1;
            }
        }

        /// <summary>Heading (degrees) the style wants at trunk step k of the segment.</summary>
        private static double Target(BoardLayoutKind kind, int k, double amp, double period, double side)
        {
            switch (kind)
            {
                case BoardLayoutKind.ZigZag: return side * ((k / (int)Math.Round(period)) % 2 == 0 ? amp : -amp);
                case BoardLayoutKind.WideCurve: return side * amp * Math.Sin(2 * Math.PI * k / period);
                case BoardLayoutKind.Hook:
                {
                    // lean out, curl hard, then swing back: a sawtooth whose direction alternates every period
                    int n = (int)Math.Round(period);
                    int phase = k % n;
                    double dir = (k / n) % 2 == 0 ? side : -side;
                    return dir * (phase < n * 0.7 ? amp * phase / (n * 0.7) : -amp * 0.5);
                }
                case BoardLayoutKind.Diamonds: return side * amp * Math.Sin(2 * Math.PI * k / period);
                default: return side * amp * Math.Sin(2 * Math.PI * k / period);
            }
        }

        /// <summary>Sideways bulge of an arm of n tiles over a chord of length len so the tiles are ~1 apart (at least min).</summary>
        private static double Bulge(int n, double len, double min)
        {
            double want = n + 1;
            double lo = 0, hi = 8;
            for (int i = 0; i < 30; i++)
            {
                double mid = (lo + hi) / 2;
                if (ArcLength(len, mid) < want) lo = mid; else hi = mid;
            }
            return Math.Max(min, hi);
        }

        private static double ArcLength(double len, double bulge)
        {
            const int steps = 32;
            double total = 0, px = 0, py = 0;
            for (int i = 1; i <= steps; i++)
            {
                double u = (double)i / steps;
                double x = bulge * Math.Sin(Math.PI * u), y = len * u;
                total += Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                px = x; py = y;
            }
            return total;
        }

        /// <summary>Places an arm's tiles evenly (by arc length) along a half-sine bulge over the chord.</summary>
        private static void ArmLayout(List<RunTile> arm, double x0, double y0, double cx, double cy, double px, double py, double len, double bulge, double chord)
        {
            int n = arm.Count;
            // sample the curve, then pick points at equal arc length
            const int steps = 64;
            var us = new double[steps + 1];
            var acc = new double[steps + 1];
            double lx = 0, ly = 0;
            for (int i = 0; i <= steps; i++)
            {
                double u = (double)i / steps;
                double a = len * u, s = bulge * Math.Sin(Math.PI * u);
                double xx = cx * a + px * s, yy = cy * a + py * s;
                us[i] = u;
                acc[i] = i == 0 ? 0 : acc[i - 1] + Math.Sqrt((xx - lx) * (xx - lx) + (yy - ly) * (yy - ly));
                lx = xx; ly = yy;
            }
            double total = acc[steps];
            int j = 0;
            for (int i = 1; i <= n; i++)
            {
                double target = total * i / (n + 1);
                while (j < steps && acc[j + 1] < target) j++;
                double f = acc[j + 1] > acc[j] ? (target - acc[j]) / (acc[j + 1] - acc[j]) : 0;
                double u = us[j] + (us[j + 1] - us[j]) * f;
                double a = len * u, s = bulge * Math.Sin(Math.PI * u);
                // direction: derivative of the curve
                double da = len, ds = bulge * Math.PI * Math.Cos(Math.PI * u);
                double dx = cx * da + px * ds, dy = cy * da + py * ds;
                Place(arm[i - 1], x0 + cx * a + px * s, y0 + cy * a + py * s, Math.Atan2(dx, dy) / Deg);
            }
        }

        private static void Place(RunTile tile, double x, double y, double dir)
        {
            tile.PosX = R(x);
            tile.PosY = R(y);
            tile.Dir = R(dir);
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>Layout of the boss arena: one step past the worm along its current direction (RunSystem.OpenEncounter).</summary>
        public static void PlaceAhead(RunTile from, RunTile tile, double distance = 1.3)
        {
            Place(tile, from.PosX + Math.Sin(from.Dir * Deg) * distance, from.PosY + Math.Cos(from.Dir * Deg) * distance, from.Dir);
        }

        // ---- pool picks and graph queries -----------------------------------------------------------------------------

        /// <summary>Weighted pick from a tile pool (optionally re-weighted by a segment theme); "once" tiles at most once (usedOnce).</summary>
        public static string PickPoolTile(ContentDb content, string poolId, Rng rng, HashSet<string> usedOnce, SegmentTheme theme = null, ActiveRun run = null)
        {
            if (poolId == null || !content.TilePools.TryGetValue(poolId, out var pool) || pool.Count == 0) return "empty";
            var weights = new double[pool.Count];
            for (int i = 0; i < pool.Count; i++)
            {
                var def = content.BoardTiles.TryGetValue(pool[i].Id, out var d) ? d : null;
                bool once = def != null && def.Once;
                weights[i] = once && usedOnce != null && usedOnce.Contains(pool[i].Id) ? 0 : pool[i].Weight * (theme != null && def != null ? theme.Weight(def) : 1);
                if (!RunTileLimits.Allowed(content, run, pool[i].Id)) weights[i] = 0; // per-run limits (TileDef.MaxPerRun)
            }
            int idx = rng.PickWeighted(weights);
            if (idx < 0) return "empty";
            string id = pool[idx].Id;
            RunTileLimits.Placed(content, run, id);
            if (usedOnce != null && content.BoardTiles.TryGetValue(id, out var def2) && def2.Once) usedOnce.Add(id);
            return id;
        }

        /// <summary>Tiles reachable from a tile (invariants / client previews).</summary>
        public static HashSet<int> Reachable(List<RunTile> tiles, int from)
        {
            var seen = new HashSet<int> { from };
            var stack = new Stack<int>();
            stack.Push(from);
            while (stack.Count > 0)
            {
                int id = stack.Pop();
                foreach (var n in tiles[id].Next)
                    if (seen.Add(n)) stack.Push(n);
            }
            return seen;
        }

        /// <summary>Tiles reachable within `steps` steps ahead of `from` (branch preview, campfire light, boss actions).</summary>
        public static List<int> Ahead(List<RunTile> tiles, int from, int steps)
        {
            var result = new List<int>();
            var frontier = new List<int> { from };
            var seen = new HashSet<int> { from };
            for (int s = 0; s < steps; s++)
            {
                var next = new List<int>();
                foreach (var id in frontier)
                    foreach (var n in tiles[id].Next)
                        if (seen.Add(n)) { next.Add(n); result.Add(n); }
                frontier = next;
            }
            return result;
        }

        /// <summary>Fewest steps from <paramref name="from"/> to the open end (int.MaxValue if unreachable).</summary>
        public static int StepsToEnd(List<RunTile> tiles, int from, int end)
        {
            if (from == end) return 0;
            var dist = new Dictionary<int, int> { { from, 0 } };
            var q = new Queue<int>();
            q.Enqueue(from);
            while (q.Count > 0)
            {
                int id = q.Dequeue();
                foreach (var n in tiles[id].Next)
                {
                    if (dist.ContainsKey(n)) continue;
                    dist[n] = dist[id] + 1;
                    if (n == end) return dist[n];
                    q.Enqueue(n);
                }
            }
            return int.MaxValue;
        }

        public static RunTile Add(List<RunTile> tiles, string tileId, int x, int y)
        {
            var t = new RunTile { Id = tiles.Count, TileId = tileId, X = x, Y = y, Fixed = tileId != null };
            tiles.Add(t);
            return t;
        }

        private static List<RunTile> Chain(List<RunTile> tiles, RunTile from, int length, int x, int y, int segment, List<RunTile> all)
        {
            var arm = new List<RunTile>();
            RunTile cur = from;
            for (int i = 0; i < length; i++)
            {
                var t = Add(tiles, null, x + i, y);
                t.Segment = segment;
                all.Add(t);
                cur.Next.Add(t.Id);
                arm.Add(t);
                cur = t;
            }
            return arm;
        }
    }
}
