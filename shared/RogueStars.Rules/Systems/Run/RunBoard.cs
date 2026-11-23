using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Board generation (GDD §3.1): a circuit. A directed tile graph with a main spine, branch legs that rejoin, an optional
    /// loop leg back to an earlier tile, fixed specials at main-path fractions, dedicated boss tiles, weighted pool fill and
    /// the boss gate at the end of the spine, which leads back to the first tile (a lap): the worm never runs out of board,
    /// the boss comes when its awakening meter is full (RunSystem). Deterministic for a given Rng.
    /// </summary>
    public static class RunBoard
    {
        public sealed class Board
        {
            public List<RunTile> Tiles = new List<RunTile>();
            public List<int> MainPath = new List<int>();
            public int GateTile;
        }

        public static Board Generate(ContentDb content, LevelDef level, Rng rng, ActiveRun run = null)
        {
            var b = new Board();
            var start = Add(b, "start", 0, 0);
            b.MainPath.Add(start.Id);
            RunTile cur = start;
            int col = 0;
            int target = Math.Max(8, level.BoardLength);
            int mainCount = 1;
            var branchJoins = new List<int>();
            while (mainCount < target)
            {
                bool canBranch = cur != start && target - mainCount >= 7 && !branchJoins.Contains(cur.Id);
                if (canBranch && rng.Chance(level.BranchChance))
                {
                    int lenA = rng.Range(2, 5), lenB = rng.Range(2, 6);
                    var endA = Chain(b, cur, lenA, col + 1, 0, b.MainPath);
                    var endB = Chain(b, cur, lenB, col + 1, 1, null);
                    col += Math.Max(lenA, lenB) + 1;
                    var join = Add(b, null, col, 0);
                    endA.Next.Add(join.Id);
                    endB.Next.Add(join.Id);
                    b.MainPath.Add(join.Id);
                    branchJoins.Add(join.Id);
                    mainCount += lenA + 1;
                    cur = join;
                }
                else
                {
                    col++;
                    var t = Add(b, null, col, 0);
                    cur.Next.Add(t.Id);
                    b.MainPath.Add(t.Id);
                    mainCount++;
                    cur = t;
                }
            }
            var gate = Add(b, "boss_gate", col + 1, 0);
            cur.Next.Add(gate.Id);
            b.GateTile = gate.Id;
            // The circuit: past the gate the next lap starts at the first tile after the landing site.
            gate.Next.Add(b.MainPath.Count > 1 ? b.MainPath[1] : start.Id);

            // Optional loop: a leg from a late main tile back to an earlier one (the token can circle).
            if (rng.Chance(level.LoopChance) && b.MainPath.Count >= 12)
            {
                int from = rng.Range(b.MainPath.Count / 2, b.MainPath.Count * 4 / 5);
                int back = Math.Max(1, from - rng.Range(4, 7));
                var src = b.Tiles[b.MainPath[from]];
                if (src.Next.Count == 1 && src.Id != b.GateTile)
                {
                    int len = rng.Range(2, 4);
                    var end = Chain(b, src, len, src.X, -1, null);
                    end.Next.Add(b.MainPath[back]);
                }
            }

            // Fixed specials along the main path.
            int last = b.MainPath.Count - 1;
            foreach (var ft in level.FixedTiles)
            {
                int idx = Math.Max(1, Math.Min(last, (int)Math.Round(ft.At * last)));
                while (idx <= last && b.Tiles[b.MainPath[idx]].TileId != null) idx++;
                if (idx > last) continue;
                var t = b.Tiles[b.MainPath[idx]];
                t.TileId = ft.TileId;
                t.Fixed = true;
            }

            // Dedicated boss tiles.
            BossDef boss = level.BossId != null && content.RunBosses.TryGetValue(level.BossId, out var bd) ? bd : null;
            if (boss != null && boss.BossTileIds.Count > 0)
            {
                var free = FreeTiles(b);
                for (int i = 0; i < boss.BossTileCount && free.Count > 0; i++)
                {
                    int k = rng.Range(0, free.Count);
                    var t = free[k];
                    free.RemoveAt(k);
                    t.TileId = boss.BossTileIds[i % boss.BossTileIds.Count];
                    t.Fixed = true;
                }
            }

            // Pool fill.
            var usedOnce = new HashSet<string>();
            foreach (var t in b.Tiles)
                if (t.TileId != null && content.BoardTiles.TryGetValue(t.TileId, out var d) && d.Once) usedOnce.Add(t.TileId);
            foreach (var t in b.Tiles)
                if (t.TileId == null) t.TileId = PickPoolTile(content, level.TilePoolId, rng, usedOnce, run);
            return b;
        }

        /// <summary>Weighted pick from a tile pool; "once" tiles are picked at most once (tracked in usedOnce).</summary>
        public static string PickPoolTile(ContentDb content, string poolId, Rng rng, HashSet<string> usedOnce, ActiveRun run = null)
        {
            if (poolId == null || !content.TilePools.TryGetValue(poolId, out var pool) || pool.Count == 0) return "empty";
            var weights = new double[pool.Count];
            for (int i = 0; i < pool.Count; i++)
            {
                bool once = content.BoardTiles.TryGetValue(pool[i].Id, out var d) && d.Once;
                weights[i] = once && usedOnce != null && usedOnce.Contains(pool[i].Id) ? 0 : pool[i].Weight;
                if (!RunTileLimits.Allowed(content, run, pool[i].Id)) weights[i] = 0; // per-run limits (TileDef.MaxPerRun)
            }
            int idx = rng.PickWeighted(weights);
            if (idx < 0) return "empty";
            string id = pool[idx].Id;
            RunTileLimits.Placed(content, run, id);
            if (usedOnce != null && content.BoardTiles.TryGetValue(id, out var def) && def.Once) usedOnce.Add(id);
            return id;
        }

        /// <summary>Tiles reachable from the start (for invariants / client previews).</summary>
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

        /// <summary>Tiles reachable within `steps` steps ahead of `from` (branch preview for the client).</summary>
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

        private static List<RunTile> FreeTiles(Board b)
        {
            var list = new List<RunTile>();
            foreach (var t in b.Tiles) if (t.TileId == null) list.Add(t);
            return list;
        }

        private static RunTile Add(Board b, string tileId, int x, int y)
        {
            var t = new RunTile { Id = b.Tiles.Count, TileId = tileId, X = x, Y = y, Fixed = tileId != null };
            b.Tiles.Add(t);
            return t;
        }

        private static RunTile Chain(Board b, RunTile from, int length, int x, int y, List<int> recordMain)
        {
            RunTile cur = from;
            for (int i = 0; i < length; i++)
            {
                var t = Add(b, null, x + i, y);
                cur.Next.Add(t.Id);
                recordMain?.Add(t.Id);
                cur = t;
            }
            return cur;
        }
    }
}
