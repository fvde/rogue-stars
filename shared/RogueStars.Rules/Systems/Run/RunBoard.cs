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
    /// tile or fork is ever visited twice. Every segment also gets a layout style (RunTuning.BoardLayouts: long straights,
    /// zig-zags, wide turns, hairpins, fork-rich) that puts each tile on a cell of an iso grid (<see cref="RunTile.PosX"/> /
    /// <see cref="RunTile.PosY"/> whole numbers, <see cref="RunTile.Dir"/> a right angle, <see cref="RunTile.Height"/> 0/1) and
    /// leaves set-piece slots inside loops (<see cref="Board.SetPieces"/>; display only, no randomness of its own).
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
            /// <summary>Set-piece slots the layout left free inside loops and hairpins (grid cells no tile ever takes).</summary>
            public List<RunSetPiece> SetPieces = new List<RunSetPiece>();
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
            start.Segment = 0; // laid out (cell 0,0, heading +Y): the board's grid layout starts here
            b.MainPath.Add(start.Id);
            b.EndTile = start.Id;
            int n = Math.Max(1, content.RunTuning.InitialSegments);
            for (int i = 0; i < n; i++) AppendSegment(content, level, b, rng, run);
            return b;
        }

        /// <summary>Wraps a run's tiles to extend them (the run keeps its tiles and the open end).</summary>
        public static Board Wrap(List<RunTile> tiles, int endTile, int segments, List<RunSetPiece> setPieces = null) =>
            new Board { Tiles = tiles, EndTile = endTile, Segments = segments, SetPieces = setPieces ?? new List<RunSetPiece>() };

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
                    // grid layout: both arms run around one rectangle, so their lengths need the same parity
                    if (((lenA - lenB) & 1) != 0) lenA += lenA > lenB ? -1 : 1; // toward the other arm: stays in range, no bias
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

            GridLayout(t, b, startTile, pieces, layout, segment);
        }

        // ---- iso grid layout ------------------------------------------------------------------------------------------

        /// <summary>Grid headings: index 0 +Y, 1 +X, 2 −Y, 3 −X (RunTile.Dir 0 / 90 / 180 / −90 degrees).</summary>
        private static readonly int[] DX = { 0, 1, 0, -1 };
        private static readonly int[] DY = { 1, 0, -1, 0 };
        private static readonly double[] DirDeg = { 0, 90, 180, -90 };

        /// <summary>Screen-horizontal coordinate of a grid cell (iso: +X runs up-right, +Y up-left).</summary>
        public static int Lateral(int x, int y) => x - y;

        /// <summary>The cell a tile sits on (grid layout: PosX / PosY are whole numbers).</summary>
        public static int CellX(RunTile t) => (int)Math.Round(t.PosX);
        public static int CellY(RunTile t) => (int)Math.Round(t.PosY);

        /// <summary>Grid heading index of a tile's Dir (0 +Y, 1 +X, 2 −Y, 3 −X).</summary>
        public static int Heading(RunTile t) =>
            Math.Abs(t.Dir - 90) < 1 ? 1 : Math.Abs(Math.Abs(t.Dir) - 180) < 1 ? 2 : Math.Abs(t.Dir + 90) < 1 ? 3 : 0;

        /// <summary>Deterministic 0..1 value for a layout decision (no Rng draw: the layout never changes the board's content).</summary>
        private static double Hash01(int segment, int salt)
        {
            unchecked
            {
                uint h = 2166136261;
                h = (h ^ (uint)segment) * 16777619;
                h = (h ^ (uint)salt) * 16777619;
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }

        /// <summary>
        /// Lays one segment out on the iso grid (display only, no randomness of its own). Every move is one cell along a grid
        /// axis: straight runs and right-angle turns, hairpins (a U-turn that wraps a set-piece slot), raised sections (Height
        /// 0/1, changing between runs = a step), and forks as rectangles: both arms run around the box from the fork tile to the
        /// join on the opposite corner, the box's inside is a set-piece slot. Invariant: the open end (head) is the only tile in
        /// its closed up-quadrant {x ≥ head.x, y ≥ head.y}; every shape only places cells inside that quadrant and leaves the
        /// new head's quadrant empty, so no two tiles ever share a cell and set-piece slots stay free for good.
        /// </summary>
        private static void GridLayout(RunTuning t, Board b, RunTile start, List<Piece> pieces, BoardLayoutDef style, int segment)
        {
            int x = CellX(start), y = CellY(start), h = start.Height;
            int band = Math.Max(4, t.GridBand);
            var kind = style != null ? style.Kind : BoardLayoutKind.Straight;
            int lastDir = Heading(start) == 1 ? 1 : 0;
            int forcedDir = -1;
            var plan = new Queue<int>();
            int salt = 0;
            for (int p = 0; p < pieces.Count; p++)
            {
                var piece = pieces[p];
                if (piece.ArmA != null)
                {
                    plan.Clear();
                    forcedDir = ForkLayout(b, piece, ref x, ref y, h, band, segment, ref salt);
                    lastDir = forcedDir == 0 ? 1 : 0;
                    continue;
                }
                if (plan.Count == 0)
                {
                    int room = 0;
                    for (int q = p; q < pieces.Count && pieces[q].ArmA == null; q++) room++;
                    bool forkSoon = true; // a fork may follow this stretch (the next segment can fork right after its first tile)
                    h = PlanMacro(b, plan, kind, x, y, h, band, room, forkSoon, ref lastDir, ref forcedDir, segment, ref salt);
                }
                int d = plan.Dequeue();
                x += DX[d]; y += DY[d];
                Place(piece.Tile, x, y, d, h);
            }
        }

        /// <summary>Plans the next trunk shape (a straight run, or a hairpin around a set-piece slot); returns its height.</summary>
        private static int PlanMacro(Board b, Queue<int> plan, BoardLayoutKind kind, int x, int y, int h, int band, int room, bool forkSoon,
            ref int lastDir, ref int forcedDir, int segment, ref int salt)
        {
            int v = Lateral(x, y);
            double hairpinChance = kind == BoardLayoutKind.Hook ? 0.7 : kind == BoardLayoutKind.Straight ? 0.25 : kind == BoardLayoutKind.Diamonds ? 0.2 : 0.4;
            // hairpin: out a, across hb, back a (back ≤ out keeps the quadrant invariant), then hc on (keeps the next run off the U)
            const int ha = 3, hb = 3, hc = 2;
            if (forcedDir < 0 && room >= ha + hb + ha + hc && Hash01(segment, salt++) < hairpinChance)
            {
                // X-major (+X a, +Y b, −X a, +Y c): lateral from v up to v+a, down to v+a−b−a−c; Y-major mirrored
                int xmMax = v + ha, xmMin = v + ha - hb - ha - hc;
                int ymMin = v - ha, ymMax = v - ha + hb + ha + hc;
                bool xm = xmMax <= band && xmMin >= -band, ym = ymMax <= band && ymMin >= -band;
                // before a fork the hairpin must leave room to get back to the middle of the band
                int after = room - (ha + hb + ha + hc);
                if (forkSoon && Math.Abs(xmMin) - 1 > after) xm = false;
                if (forkSoon && Math.Abs(ymMax) - 1 > after) ym = false;
                if (xm && ym)
                {
                    if (v > 0) ym = false;
                    else if (v < 0) xm = false;
                    else if (Hash01(segment, salt++) < 0.5) xm = false;
                    else ym = false;
                }
                if (xm || ym)
                {
                    int f = xm ? 1 : 0, s = xm ? 0 : 1, back = xm ? 3 : 2;
                    for (int i = 0; i < ha; i++) plan.Enqueue(f);
                    for (int i = 0; i < hb; i++) plan.Enqueue(s);
                    for (int i = 0; i < ha; i++) plan.Enqueue(back);
                    for (int i = 0; i < hc; i++) plan.Enqueue(s);
                    // the slot inside the U, between the outgoing and the return run
                    b.SetPieces.Add(xm
                        ? new RunSetPiece { X = x + 1, Y = y + 1, W = ha - 1, H = hb - 1, Height = h, Segment = segment, Shape = "hairpin" }
                        : new RunSetPiece { X = x + 1, Y = y + 1, W = hb - 1, H = ha - 1, Height = h, Segment = segment, Shape = "hairpin" });
                    lastDir = s;
                    return h;
                }
            }

            // straight run: turn (alternate axis) unless the style keeps going or the band forbids it
            int dir;
            if (forcedDir >= 0) dir = forcedDir;
            else
            {
                dir = lastDir == 1 ? 0 : 1;
                bool keep = Hash01(segment, salt++) < (kind == BoardLayoutKind.Straight ? 0.3 : 0.12);
                if (keep) dir = lastDir;
            }
            forcedDir = -1;
            bool steer = forkSoon && Math.Abs(v) > 1 && room <= Math.Abs(v) + 3;
            if (steer) dir = v > 0 ? 0 : 1; // a fork's box needs the whole band: start it mid-band
            if (dir == 1 && v >= band - 1) dir = 0;
            else if (dir == 0 && v <= -band + 1) dir = 1;
            int lo, hi;
            switch (kind)
            {
                case BoardLayoutKind.Straight: lo = 3; hi = 6; break;
                case BoardLayoutKind.ZigZag: lo = 2; hi = 3; break;
                case BoardLayoutKind.WideCurve: lo = 3; hi = 5; break;
                default: lo = 2; hi = 4; break;
            }
            int len = lo + (int)(Hash01(segment, salt++) * (hi - lo + 1));
            int roomBand = dir == 1 ? band - v : v + band;
            if (steer) roomBand = Math.Min(roomBand, Math.Abs(v)); // stop in the middle
            len = Math.Max(1, Math.Min(len, Math.Min(room, roomBand)));
            // before a fork, never run so far out that the trunk left cannot bring the path back to the middle
            int step = dir == 1 ? 1 : -1;
            while (forkSoon && len > 1 && Math.Abs(v + step * len) - 1 > room - len) len--;
            if (forkSoon && Math.Abs(v + step) - 1 > room - 1 && Math.Abs(v - step) <= band) { dir = 1 - dir; step = -step; }
            for (int i = 0; i < len; i++) plan.Enqueue(dir);
            lastDir = dir;
            // a raised section (or the step back down) starts with this run
            if (Hash01(segment, salt++) < 0.3) h = h == 0 ? 1 : 0;
            return h;
        }

        /// <summary>
        /// A fork as a rectangle k × m from the fork tile (corner 0,0) to the join (corner k,m): one arm runs +X then +Y, the
        /// other +Y then +X; the longer arm (arm lengths differ by an even number, AppendSegment) detours w cells further out
        /// on its far side. Every cell is in the fork tile's up-quadrant; the join is the new head. Returns the exit heading
        /// (away from a detour, so the path never runs alongside an arm).
        /// </summary>
        private static int ForkLayout(Board b, Piece piece, ref int x, ref int y, int h, int band, int segment, ref int salt)
        {
            int nA = piece.ArmA.Count, nB = piece.ArmB.Count;
            int sum = Math.Min(nA, nB) + 1; // k + m − 1 = the shorter arm
            int w = Math.Abs(nA - nB) / 2;
            int v = Lateral(x, y);
            // pick the box (k × m) and which arm runs +X first: inside the band (arms reach v − m − wy .. v + k + wx), join
            // nearest the middle; the longer arm detours on its far side
            int bestK = 2, bestScore = int.MaxValue;
            bool bestAX = true;
            for (int k0 = 2; k0 <= sum - 2; k0++)
                for (int ax = 0; ax < 2; ax++)
                {
                    bool aX = ax == 0;
                    int m0 = sum - k0;
                    int wx0 = (aX ? nA : nB) > Math.Min(nA, nB) ? w : 0, wy0 = (aX ? nB : nA) > Math.Min(nA, nB) ? w : 0;
                    int over = Math.Max(0, v + k0 + wx0 - band) + Math.Max(0, -band - (v - m0 - wy0));
                    int score = over * 1000 + Math.Abs(v + k0 - m0) * 10 + Math.Abs(k0 - m0) + (int)(Hash01(segment, salt + k0 * 2 + ax) * 3);
                    if (score < bestScore) { bestScore = score; bestK = k0; bestAX = aX; }
                }
            salt += 64;
            int k = bestK, m = sum - k;
            bool aXFirst = bestAX;
            var xArm = aXFirst ? piece.ArmA : piece.ArmB;
            var yArm = aXFirst ? piece.ArmB : piece.ArmA;
            int wx = (xArm.Count - (k + m - 1)) / 2, wy = (yArm.Count - (k + m - 1)) / 2;

            // X-first arm: (1..k+wx, 0), (k+wx, 1..m−1), then back (k+wx−1 .. k, m−1)
            var cells = new List<int[]>();
            for (int i = 1; i <= k + wx; i++) cells.Add(new[] { i, 0 });
            for (int j = 1; j <= m - 1; j++) cells.Add(new[] { k + wx, j });
            for (int i = k + wx - 1; i >= k; i--) cells.Add(new[] { i, m - 1 });
            PlaceArm(xArm, cells, x, y, h);
            // Y-first arm: (0, 1..m+wy), (1..k−1, m+wy), then down (k−1, m+wy−1 .. m)
            cells.Clear();
            for (int j = 1; j <= m + wy; j++) cells.Add(new[] { 0, j });
            for (int i = 1; i <= k - 1; i++) cells.Add(new[] { i, m + wy });
            for (int j = m + wy - 1; j >= m; j--) cells.Add(new[] { k - 1, j });
            PlaceArm(yArm, cells, x, y, h);

            b.SetPieces.Add(new RunSetPiece { X = x + 1, Y = y + 1, W = k - 1, H = m - 1, Height = h, Segment = segment, Shape = "loop" });
            x += k; y += m;
            Place(piece.Tile, x, y, aXFirst ? 0 : 1, h); // the join, entered from the default arm's side
            int lat = Lateral(x, y);
            int exit = wx > 0 ? 0 : wy > 0 ? 1 : lat > 0 ? 0 : 1;
            if (wx == 0 && wy == 0)
            {
                if (exit == 1 && lat >= band - 1) exit = 0;
                if (exit == 0 && lat <= -band + 1) exit = 1;
            }
            return exit;
        }

        private static void PlaceArm(List<RunTile> arm, List<int[]> cells, int x0, int y0, int h)
        {
            int px = x0, py = y0;
            for (int i = 0; i < arm.Count; i++)
            {
                int cx = x0 + cells[i][0], cy = y0 + cells[i][1];
                int d = cx > px ? 1 : cx < px ? 3 : cy > py ? 0 : 2;
                Place(arm[i], cx, cy, d, h);
                px = cx; py = cy;
            }
        }

        private static void Place(RunTile tile, int x, int y, int dir, int height)
        {
            tile.PosX = x;
            tile.PosY = y;
            tile.Dir = DirDeg[dir];
            tile.Height = height;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>
        /// Layout of the boss arena (RunSystem.OpenEncounter): the free grid cell next to the worm, straight ahead if it can.
        /// Without <paramref name="tiles"/> it goes straight ahead.
        /// </summary>
        public static void PlaceAhead(RunTile from, RunTile tile, List<RunTile> tiles = null)
        {
            tile.Segment = from.Segment;
            int fx = CellX(from), fy = CellY(from);
            int fwd = Heading(from);
            int[] order = { fwd, 0, 1, 3, 2 };
            foreach (int d in order)
            {
                int cx = fx + DX[d], cy = fy + DY[d];
                bool free = true;
                if (tiles != null)
                    foreach (var o in tiles)
                        if (o != tile && o != from && o.Segment >= 0 && CellX(o) == cx && CellY(o) == cy) { free = false; break; }
                if (!free) continue;
                Place(tile, cx, cy, d, from.Height);
                return;
            }
            Place(tile, fx + DX[fwd], fy + DY[fwd], fwd, from.Height);
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
