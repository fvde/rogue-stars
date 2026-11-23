using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Board generation (GDD §3.1): a long path that never repeats. The board is generated ahead of the worm in segments
    /// (<see cref="AppendSegment"/>): each segment is a spine of LevelDef.BoardLength tiles with fork legs that rejoin further
    /// ahead, the level's fixed specials (campfire, shops, miniboss lair) at fractions of the segment, the boss's dedicated
    /// tiles and a weighted pool fill with the segment's own mix (RunTuning.SegmentThemes). Edges only point forward, so no
    /// tile or fork is ever visited twice; RunSystem extends the board whenever the worm gets close to its end and opens the
    /// boss arena ahead of the worm when the awakening meter is full. Deterministic for a given Rng.
    /// </summary>
    public static class RunBoard
    {
        public sealed class Board
        {
            public List<RunTile> Tiles = new List<RunTile>();
            public List<int> MainPath = new List<int>();
            /// <summary>The open end of the path (the last spine tile; the next segment starts after it).</summary>
            public int EndTile;
            public int Segments;
        }

        /// <summary>The start tile plus <see cref="RunTuning.InitialSegments"/> segments.</summary>
        public static Board Generate(ContentDb content, LevelDef level, Rng rng)
        {
            var b = new Board();
            var start = Add(b.Tiles, "start", 0, 0);
            b.MainPath.Add(start.Id);
            b.EndTile = start.Id;
            int n = Math.Max(1, content.RunTuning.InitialSegments);
            for (int i = 0; i < n; i++) AppendSegment(content, level, b, rng);
            return b;
        }

        /// <summary>Wraps a run's tiles to extend them (the run keeps its tiles and the open end).</summary>
        public static Board Wrap(List<RunTile> tiles, int endTile, int segments) => new Board { Tiles = tiles, EndTile = endTile, Segments = segments };

        /// <summary>Appends one segment after the open end (new tiles only; nothing behind the worm changes).</summary>
        public static void AppendSegment(ContentDb content, LevelDef level, Board b, Rng rng)
        {
            var t = content.RunTuning;
            int segment = b.Segments++;
            var theme = segment == 0 || t.SegmentThemes.Count == 0 ? null : t.SegmentThemes[rng.Range(0, t.SegmentThemes.Count)];
            var cur = b.Tiles[b.EndTile];
            int col = cur.X;
            int target = Math.Max(8, level.BoardLength);
            int count = 0;
            var spine = new List<RunTile>();
            var all = new List<RunTile>();
            var joins = new HashSet<int>();
            while (count < target)
            {
                bool canBranch = count > 0 && target - count >= 7 && !joins.Contains(cur.Id);
                if (canBranch && rng.Chance(level.BranchChance))
                {
                    int lenA = rng.Range(2, 5), lenB = rng.Range(2, 6);
                    var endA = Chain(b.Tiles, cur, lenA, col + 1, 0, spine, all);
                    var endB = Chain(b.Tiles, cur, lenB, col + 1, 1, null, all);
                    col += Math.Max(lenA, lenB) + 1;
                    var join = Add(b.Tiles, null, col, 0);
                    all.Add(join);
                    endA.Next.Add(join.Id);
                    endB.Next.Add(join.Id);
                    spine.Add(join);
                    joins.Add(join.Id);
                    count += lenA + 1;
                    cur = join;
                }
                else
                {
                    col++;
                    var nt = Add(b.Tiles, null, col, 0);
                    all.Add(nt);
                    cur.Next.Add(nt.Id);
                    spine.Add(nt);
                    count++;
                    cur = nt;
                }
            }
            b.EndTile = cur.Id;
            foreach (var s in spine) b.MainPath.Add(s.Id);

            // Fixed specials along the segment's spine (the last spine tile stays free: the next segment may fork from it).
            int last = spine.Count - 2;
            foreach (var ft in level.FixedTiles)
            {
                int idx = Math.Max(0, Math.Min(last, (int)Math.Round(ft.At * last)));
                while (idx <= last && spine[idx].TileId != null) idx++;
                if (idx > last) continue;
                spine[idx].TileId = ft.TileId;
                spine[idx].Fixed = true;
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
                if (x.TileId == null) x.TileId = PickPoolTile(content, level.TilePoolId, rng, usedOnce, theme);
        }

        /// <summary>Weighted pick from a tile pool (optionally re-weighted by a segment theme); "once" tiles at most once (usedOnce).</summary>
        public static string PickPoolTile(ContentDb content, string poolId, Rng rng, HashSet<string> usedOnce, SegmentTheme theme = null)
        {
            if (poolId == null || !content.TilePools.TryGetValue(poolId, out var pool) || pool.Count == 0) return "empty";
            var weights = new double[pool.Count];
            for (int i = 0; i < pool.Count; i++)
            {
                var def = content.BoardTiles.TryGetValue(pool[i].Id, out var d) ? d : null;
                bool once = def != null && def.Once;
                weights[i] = once && usedOnce != null && usedOnce.Contains(pool[i].Id) ? 0 : pool[i].Weight * (theme != null && def != null ? theme.Weight(def) : 1);
            }
            int idx = rng.PickWeighted(weights);
            if (idx < 0) return "empty";
            string id = pool[idx].Id;
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

        private static RunTile Chain(List<RunTile> tiles, RunTile from, int length, int x, int y, List<RunTile> spine, List<RunTile> all)
        {
            RunTile cur = from;
            for (int i = 0; i < length; i++)
            {
                var t = Add(tiles, null, x + i, y);
                all.Add(t);
                cur.Next.Add(t.Id);
                spine?.Add(t);
                cur = t;
            }
            return cur;
        }
    }
}
