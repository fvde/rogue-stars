using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>A spot for a building: the streets to pave first (in order, may be empty), then the placement.</summary>
    public sealed class CityPlan
    {
        public List<int> Streets = new List<int>();
        public int X, Y;
        public bool Rotated;
        /// <summary>Placement bonus the building gets there (percent after auras and cap) and its flat bonus.</summary>
        public double Pct, Flat;
    }

    /// <summary>
    /// Greedy spot finder (sim bot, tests, an editor "suggest a spot"): every free footprint position and rotation that touches the
    /// street network, or that a short new street (up to <c>maxNewStreets</c> tiles grown out of the network) would connect; best
    /// placement bonus first, then fewer new streets, then closer to the fireplace. Deterministic (grid scan order).
    /// </summary>
    public static class CityPlanner
    {
        private static readonly int[] DX = { 1, -1, 0, 0 };
        private static readonly int[] DY = { 0, 0, 1, -1 };

        /// <param name="reserved">Cells the building must not cover (e.g. planned streets), or null.</param>
        public static CityPlan Find(GameState s, ContentDb c, string blueprintId, string storedUid = null, int maxNewStreets = 2, ICollection<int> reserved = null)
        {
            var def = c.Blueprint(blueprintId);
            if (def == null) return null;
            var g = CityLayout.Grid(s, c);
            var linked = CityLayout.LinkedStreets(g);
            CityPlan best = null;
            double bestScore = double.MinValue;

            void Consider(List<int> streets)
            {
                var extra = new HashSet<int>(streets);
                for (int rot = 0; rot < (def.Width == def.Height ? 1 : 2); rot++)
                {
                    CityLayout.Footprint(def, rot == 1, out int w, out int h);
                    for (int y = 0; y + h <= g.H; y++)
                        for (int x = 0; x + w <= g.W; x++)
                        {
                            if (!Fits(g, x, y, w, h, extra, reserved)) continue;
                            // must touch the network: the last new street, or (no new streets) a linked cell
                            int touch = streets.Count > 0 ? streets[streets.Count - 1] : -1;
                            if (!Touches(g, x, y, w, h, linked, touch)) continue;
                            var edit = CityEdit.Place(def.Id, x, y, rot == 1, storedUid);
                            var grid = CityLayout.Grid(s, c, edit, streets);
                            var it = grid.Find(storedUid ?? CityLayout.NewUid);
                            if (it == null) continue;
                            var bonus = CityLayout.Evaluate(grid, it);
                            double pct = CityLayout.Total(s, c, bonus);
                            int dist = CityLayout.FireDistance(c, it);
                            double score = pct + bonus.Flat * 20 - streets.Count * 4 - dist * 0.05;
                            if (score > bestScore + 1e-9)
                            {
                                bestScore = score;
                                best = new CityPlan { Streets = new List<int>(streets), X = x, Y = y, Rotated = rot == 1, Pct = pct, Flat = bonus.Flat };
                            }
                        }
                }
            }

            Consider(new List<int>());
            if (best != null && best.Pct >= c.CityTuning.PlacementCapPct - 1e-9 || maxNewStreets <= 0) return best;
            var plain = best;
            // Grow streets out of the network: every open cell next to a linked cell, then one step further.
            var starts = new List<int>();
            for (int cell = 0; cell < g.W * g.H; cell++)
            {
                if (!g.IsOpen(cell % g.W, cell / g.W)) continue;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cell % g.W + DX[d], ny = cell / g.W + DY[d];
                    if (g.In(nx, ny) && linked[g.Cell(nx, ny)]) { starts.Add(cell); break; }
                }
            }
            foreach (var a in starts)
            {
                Consider(new List<int> { a });
                if (maxNewStreets < 2) continue;
                for (int d = 0; d < 4; d++)
                {
                    int nx = a % g.W + DX[d], ny = a / g.W + DY[d];
                    if (!g.IsOpen(nx, ny)) continue;
                    int b = g.Cell(nx, ny);
                    if (starts.Contains(b)) continue;
                    Consider(new List<int> { a, b });
                }
            }
            // Only pave new streets when they buy a better bonus (or there is no spot at all).
            if (plain != null && best != null && best.Streets.Count > 0 && best.Pct <= plain.Pct + 1e-9 && best.Flat <= plain.Flat) return plain;
            return best;
        }

        private static bool Fits(CityGrid g, int x, int y, int w, int h, HashSet<int> extra, ICollection<int> reserved)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                {
                    if (!g.IsOpen(xx, yy)) return false;
                    int cell = g.Cell(xx, yy);
                    if (extra.Contains(cell) || (reserved != null && reserved.Contains(cell))) return false;
                }
            return true;
        }

        /// <summary>
        /// A street spine (sim bot / "auto streets"): vertical streets every <paramref name="spacing"/> columns from the fireplace's
        /// column and the fireplace's row as the cross street, so every building band is at most spacing − 1 tiles wide.
        /// </summary>
        public static HashSet<int> SpineCells(ContentDb c, int spacing = 4)
        {
            var t = c.CityTuning;
            var set = new HashSet<int>();
            for (int y = 0; y < t.GridHeight; y++)
                for (int x = 0; x < t.GridWidth; x++)
                    if (((x - t.FireX) % spacing + spacing) % spacing == 0 || y == t.FireY)
                        if (!CityLayout.IsPlaza(c, x, y)) set.Add(y * t.GridWidth + x);
            return set;
        }

        /// <summary>Spine cells that can be paved now: open land reachable from the street network through other spine cells.</summary>
        public static List<int> PaveableSpine(GameState s, ContentDb c, ICollection<int> spine)
        {
            var g = CityLayout.Grid(s, c);
            var linked = CityLayout.LinkedStreets(g);
            var adds = new List<int>();
            var q = new Queue<int>();
            for (int i = 0; i < linked.Length; i++) if (linked[i]) q.Enqueue(i);
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    int nx = cur % g.W + DX[d], ny = cur / g.W + DY[d];
                    if (!g.In(nx, ny)) continue;
                    int n = g.Cell(nx, ny);
                    if (linked[n] || !spine.Contains(n) || !g.IsOpen(nx, ny)) continue;
                    linked[n] = true;
                    adds.Add(n);
                    q.Enqueue(n);
                }
            }
            return adds;
        }

        private static bool Touches(CityGrid g, int x, int y, int w, int h, bool[] linked, int street)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = xx + DX[d], ny = yy + DY[d];
                        if (nx >= x && nx < x + w && ny >= y && ny < y + h) continue;
                        if (!g.In(nx, ny)) continue;
                        int n = g.Cell(nx, ny);
                        if (street >= 0 ? n == street : linked[n]) return true;
                    }
            return false;
        }

        /// <summary>Applies a plan through the engine's commands (streets stroke, then the placement). Returns the place result.</summary>
        public static CommandResult Apply(GameEngine e, ref GameState s, CityPlan plan, string blueprintId, string storedUid, long nowMs)
        {
            if (plan.Streets.Count > 0)
            {
                var r = e.Execute(ref s, new CityStreets { Adds = new List<int>(plan.Streets) }, nowMs);
                if (!r.Ok) return r;
            }
            return e.Execute(ref s, new CityPlace { BlueprintId = blueprintId, Uid = storedUid, X = plan.X, Y = plan.Y, Rotated = plan.Rotated }, nowMs);
        }
    }
}
