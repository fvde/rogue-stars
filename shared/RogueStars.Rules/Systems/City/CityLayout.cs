using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    public enum CityEditKind { None, Place, Move, Store, Streets }

    /// <summary>
    /// One layout edit, as a command applies it and as the client editor previews it (the same check runs on both sides).
    /// Place: BlueprintId (+ Uid of a stored copy, null = a new copy), X, Y, Rotated. Move: Uid, X, Y, Rotated. Store: Uid.
    /// Streets: Adds / Removes (cells y × GridWidth + x).
    /// </summary>
    public sealed class CityEdit
    {
        public CityEditKind Kind;
        public string Uid;
        public string BlueprintId;
        public int X, Y;
        public bool Rotated;
        public List<int> Adds = new List<int>();
        public List<int> Removes = new List<int>();

        public static CityEdit Place(string blueprintId, int x, int y, bool rotated, string storedUid = null) =>
            new CityEdit { Kind = CityEditKind.Place, BlueprintId = blueprintId, Uid = storedUid, X = x, Y = y, Rotated = rotated };
        public static CityEdit Move(string uid, int x, int y, bool rotated) => new CityEdit { Kind = CityEditKind.Move, Uid = uid, X = x, Y = y, Rotated = rotated };
        public static CityEdit Store(string uid) => new CityEdit { Kind = CityEditKind.Store, Uid = uid };
        public static CityEdit Streets(IEnumerable<int> adds, IEnumerable<int> removes)
        {
            var e = new CityEdit { Kind = CityEditKind.Streets };
            if (adds != null) e.Adds.AddRange(adds);
            if (removes != null) e.Removes.AddRange(removes);
            return e;
        }
    }

    /// <summary>Result of <see cref="CityLayout.Validate"/>: Error null = valid. BadCells / BadUids: what to flash red.</summary>
    public sealed class CityCheck
    {
        public string Error;
        public List<int> BadCells = new List<int>();
        public List<string> BadUids = new List<string>();
        public bool Ok => Error == null;
    }

    /// <summary>A building on the grid (a placed copy, or the edited / previewed one).</summary>
    public sealed class CityGridItem
    {
        public string Uid;
        public BlueprintDef Def;
        public int X, Y, W, H;
        /// <summary>The state's building (null for a new copy being previewed).</summary>
        public CityBuilding Building;

        public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
    }

    /// <summary>Placement bonus of one building: own skill (percent, before the cap), flat bonus, and the aura buildings touching it.</summary>
    public sealed class CityBonus
    {
        public double OwnPct;
        public double Flat;
        public List<string> AuraFrom = new List<string>();
    }

    /// <summary>
    /// A snapshot of the land: what every cell holds. Built from the state (optionally with one edit applied); records overlaps and
    /// off-land cells instead of throwing so <see cref="CityLayout.Check"/> can report them.
    /// </summary>
    public sealed class CityGrid
    {
        public readonly ContentDb Content;
        public readonly int W, H;
        public readonly bool[] Land, Plaza, Feature, Street;
        /// <summary>Index into <see cref="Items"/> of the building on a cell, or −1.</summary>
        public readonly int[] Owner;
        public readonly List<CityGridItem> Items = new List<CityGridItem>();
        /// <summary>Cells where two things overlap or a building / street sits off land, on a feature or on the plaza.</summary>
        public readonly List<int> Conflicts = new List<int>();
        public readonly List<string> ConflictUids = new List<string>();

        public CityGrid(ContentDb content)
        {
            Content = content;
            W = content.CityTuning.GridWidth; H = content.CityTuning.GridHeight;
            Land = new bool[W * H]; Plaza = new bool[W * H]; Feature = new bool[W * H]; Street = new bool[W * H];
            Owner = new int[W * H];
            for (int i = 0; i < Owner.Length; i++) Owner[i] = -1;
        }

        public int Cell(int x, int y) => y * W + x;
        public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
        public bool IsLand(int x, int y) => In(x, y) && Land[Cell(x, y)];
        public CityGridItem ItemAt(int x, int y) => In(x, y) && Owner[Cell(x, y)] >= 0 ? Items[Owner[Cell(x, y)]] : null;
        public CityGridItem Find(string uid)
        {
            foreach (var it in Items) if (it.Uid == uid) return it;
            return null;
        }

        /// <summary>Empty dug land: no building, street, feature or plaza.</summary>
        public bool IsOpen(int x, int y)
        {
            if (!IsLand(x, y)) return false;
            int c = Cell(x, y);
            return !Plaza[c] && !Feature[c] && !Street[c] && Owner[c] < 0;
        }

        internal void AddStreet(int cell)
        {
            if (cell < 0 || cell >= Street.Length) { Conflicts.Add(cell); return; }
            if (Street[cell]) return;
            Street[cell] = true;
            if (!Land[cell] || Plaza[cell] || Feature[cell] || Owner[cell] >= 0) Conflicts.Add(cell);
        }

        internal void AddItem(CityGridItem it)
        {
            int idx = Items.Count;
            Items.Add(it);
            bool bad = false;
            for (int y = it.Y; y < it.Y + it.H; y++)
                for (int x = it.X; x < it.X + it.W; x++)
                {
                    if (!In(x, y)) { bad = true; continue; }
                    int c = Cell(x, y);
                    if (!Land[c] || Plaza[c] || Feature[c] || Street[c] || Owner[c] >= 0) { Conflicts.Add(c); bad = true; }
                    if (Owner[c] < 0) Owner[c] = idx;
                }
            if (bad) ConflictUids.Add(it.Uid);
        }
    }

    /// <summary>
    /// City layout rules (City design §3 and §5), shared by the command handlers and the client editor:
    /// <list type="bullet">
    /// <item>Grid W×H; land = start cells + dug patches; the 3×3 plaza (fireplace) is fixed; features are obstacles.</item>
    /// <item>Streets are 1×1, on free land, and every street links back to the plaza (side steps, BFS).</item>
    /// <item>Every building has a footprint tile touching (side, not corner) a linked street or the plaza. Nothing overlaps.</item>
    /// <item>Copies per blueprint are capped (BlueprintDef.Copies).</item>
    /// </list>
    /// Placement bonuses (<see cref="Bonus"/>) are computed per building from the layout and cached until the layout changes
    /// (CityState.LayoutRev).
    /// </summary>
    public static class CityLayout
    {
        private static readonly int[] DX = { 1, -1, 0, 0 };
        private static readonly int[] DY = { 0, 0, 1, -1 };

        // ---- grid --------------------------------------------------------------------------------

        public static int Cell(ContentDb c, int x, int y) => y * c.CityTuning.GridWidth + x;
        public static int CellX(ContentDb c, int cell) => cell % c.CityTuning.GridWidth;
        public static int CellY(ContentDb c, int cell) => cell / c.CityTuning.GridWidth;

        /// <summary>Footprint of a blueprint (rotated swaps width and height).</summary>
        public static void Footprint(BlueprintDef def, bool rotated, out int w, out int h)
        {
            w = rotated ? def.Height : def.Width;
            h = rotated ? def.Width : def.Height;
        }

        public static bool IsPlaza(ContentDb c, int x, int y)
        {
            var t = c.CityTuning;
            return x >= t.PlazaX && x < t.PlazaX + t.PlazaSize && y >= t.PlazaY && y < t.PlazaY + t.PlazaSize;
        }

        /// <summary>True if the cell is the player's land now (start or a fully dug patch).</summary>
        public static bool IsLand(GameState s, ContentDb c, int cell)
        {
            if (c.CityTuning.StartCells.Contains(cell)) return true;
            foreach (var id in s.City.DugPatches)
            {
                var p = c.CityPatch(id);
                if (p != null && p.Cells.Contains(cell)) return true;
            }
            return false;
        }

        /// <summary>The land, streets and placed buildings of the state, with an optional edit applied (not validated).</summary>
        public static CityGrid Grid(GameState s, ContentDb c, CityEdit edit = null, IList<int> extraStreets = null)
        {
            var g = new CityGrid(c);
            var t = c.CityTuning;
            foreach (var cell in t.StartCells) g.Land[cell] = true;
            foreach (var id in s.City.DugPatches)
            {
                var p = c.CityPatch(id);
                if (p == null) continue;
                foreach (var cell in p.Cells) g.Land[cell] = true;
                foreach (var f in p.Features) g.Feature[g.Cell(f.X, f.Y)] = true;
            }
            for (int y = t.PlazaY; y < t.PlazaY + t.PlazaSize; y++)
                for (int x = t.PlazaX; x < t.PlazaX + t.PlazaSize; x++)
                    if (g.In(x, y)) g.Plaza[g.Cell(x, y)] = true;

            var streets = new List<int>(s.City.Streets);
            if (edit != null && edit.Kind == CityEditKind.Streets)
            {
                foreach (var r in edit.Removes) streets.Remove(r);
                foreach (var a in edit.Adds) if (!streets.Contains(a)) streets.Add(a);
            }
            if (extraStreets != null) foreach (var a in extraStreets) if (!streets.Contains(a)) streets.Add(a);
            foreach (var cell in streets) g.AddStreet(cell);

            string moved = edit != null && (edit.Kind == CityEditKind.Move || edit.Kind == CityEditKind.Store || edit.Kind == CityEditKind.Place) ? edit.Uid : null;
            foreach (var b in s.City.Buildings)
            {
                if (!b.Placed || b.Uid == moved) continue;
                var def = c.Blueprint(b.BlueprintId);
                if (def == null) continue;
                Footprint(def, b.Rotated, out int w, out int h);
                g.AddItem(new CityGridItem { Uid = b.Uid, Def = def, X = b.X, Y = b.Y, W = w, H = h, Building = b });
            }
            if (edit != null && (edit.Kind == CityEditKind.Place || edit.Kind == CityEditKind.Move))
            {
                var b = edit.Uid != null ? CitySystem.Building(s, edit.Uid) : null;
                var def = c.Blueprint(edit.Kind == CityEditKind.Place ? edit.BlueprintId : b?.BlueprintId);
                if (def != null)
                {
                    Footprint(def, edit.Rotated, out int w, out int h);
                    g.AddItem(new CityGridItem { Uid = edit.Uid ?? NewUid, Def = def, X = edit.X, Y = edit.Y, W = w, H = h, Building = b });
                }
            }
            return g;
        }

        /// <summary>Uid the grid gives a new copy being placed or previewed.</summary>
        public const string NewUid = "new";

        // ---- validation --------------------------------------------------------------------------

        /// <summary>
        /// Checks an edit against the state: the edit's own preconditions (owned, copies, stored / placed, street cells), then the
        /// whole resulting layout (<see cref="Check"/>). The command handlers call this before applying an edit; the client editor
        /// calls it for the ghost and for street strokes.
        /// </summary>
        public static CityCheck Validate(GameState s, ContentDb c, CityEdit edit)
        {
            var res = new CityCheck();
            if (edit == null || edit.Kind == CityEditKind.None) { res.Error = "nothing to do"; return res; }
            switch (edit.Kind)
            {
                case CityEditKind.Place:
                {
                    var def = c.Blueprint(edit.BlueprintId);
                    if (def == null) { res.Error = "unknown blueprint"; return res; }
                    if (CitySystem.Owned(s, def.Id) == null) { res.Error = "blueprint not owned"; return res; }
                    if (edit.Uid != null)
                    {
                        var b = CitySystem.Building(s, edit.Uid);
                        if (b == null || b.BlueprintId != def.Id) { res.Error = "unknown building"; return res; }
                        if (b.Placed) { res.Error = "already placed"; return res; }
                    }
                    else if (CitySystem.CopiesBuilt(s, def.Id) >= def.Copies)
                    {
                        res.Error = CitySystem.CopiesBuilt(s, def.Id) + "/" + def.Copies + " built"; return res;
                    }
                    break;
                }
                case CityEditKind.Move:
                case CityEditKind.Store:
                {
                    var b = CitySystem.Building(s, edit.Uid);
                    if (b == null || !b.Placed) { res.Error = "building not placed"; return res; }
                    break;
                }
                case CityEditKind.Streets:
                {
                    if (edit.Adds.Count == 0 && edit.Removes.Count == 0) { res.Error = "nothing to do"; return res; }
                    foreach (var r in edit.Removes)
                        if (!s.City.Streets.Contains(r) || edit.Adds.Contains(r)) { res.Error = "not a street"; res.BadCells.Add(r); return res; }
                    foreach (var a in edit.Adds)
                        if (s.City.Streets.Contains(a)) { res.Error = "already a street"; res.BadCells.Add(a); return res; }
                    break;
                }
            }
            var g = Grid(s, c, edit);
            return Check(g, edit.Kind == CityEditKind.Place || edit.Kind == CityEditKind.Move ? (edit.Uid ?? NewUid) : null);
        }

        /// <summary>
        /// Whole-layout check of a grid: every building and street on free dug land, no overlap, every street linked to the plaza,
        /// every building touching a linked street or the plaza. <paramref name="focusUid"/>: the edited building (its own problems
        /// are reported first, with player-facing reasons).
        /// </summary>
        public static CityCheck Check(CityGrid g, string focusUid = null)
        {
            var res = new CityCheck();
            if (g.Conflicts.Count > 0 || g.ConflictUids.Count > 0)
            {
                res.BadCells.AddRange(g.Conflicts);
                res.BadUids.AddRange(g.ConflictUids);
                var focus = focusUid != null ? g.Find(focusUid) : null;
                res.Error = focus != null && g.ConflictUids.Contains(focusUid) ? FootprintReason(g, focus) : "not on free land";
                return res;
            }
            var linked = LinkedStreets(g);
            foreach (var cell in AllStreets(g))
                if (!linked[cell]) res.BadCells.Add(cell);
            foreach (var it in g.Items)
                if (!TouchesLink(g, it, linked)) res.BadUids.Add(it.Uid);
            if (res.BadUids.Contains(focusUid ?? "")) { res.Error = "must touch a street"; return res; }
            if (res.BadCells.Count > 0) { res.Error = "streets must link to the plaza"; return res; }
            if (res.BadUids.Count > 0) { res.Error = "a building would lose its street"; return res; }
            return res;
        }

        private static string FootprintReason(CityGrid g, CityGridItem it)
        {
            for (int y = it.Y; y < it.Y + it.H; y++)
                for (int x = it.X; x < it.X + it.W; x++)
                {
                    if (!g.In(x, y) || !g.Land[g.Cell(x, y)]) return "not dug land";
                    int c = g.Cell(x, y);
                    if (g.Plaza[c]) return "on the plaza";
                    if (g.Feature[c]) return "blocked by terrain";
                }
            return "space taken";
        }

        private static IEnumerable<int> AllStreets(CityGrid g)
        {
            for (int i = 0; i < g.Street.Length; i++) if (g.Street[i]) yield return i;
        }

        /// <summary>Streets reachable from the plaza by side steps over streets.</summary>
        public static bool[] LinkedStreets(CityGrid g)
        {
            var linked = new bool[g.W * g.H];
            var q = new Queue<int>();
            for (int i = 0; i < g.Plaza.Length; i++)
                if (g.Plaza[i]) { linked[i] = true; q.Enqueue(i); }
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                int x = cur % g.W, y = cur / g.W;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], ny = y + DY[d];
                    if (!g.In(nx, ny)) continue;
                    int n = g.Cell(nx, ny);
                    if (linked[n] || !g.Street[n]) continue;
                    linked[n] = true;
                    q.Enqueue(n);
                }
            }
            return linked;
        }

        private static bool TouchesLink(CityGrid g, CityGridItem it, bool[] linked)
        {
            foreach (var n in SideCells(g, it))
                if (n >= 0 && linked[n]) return true;
            return false;
        }

        /// <summary>Cells side-adjacent to a footprint (outside it); −1 for cells beyond the grid.</summary>
        public static List<int> SideCells(CityGrid g, CityGridItem it)
        {
            var list = new List<int>();
            for (int y = it.Y; y < it.Y + it.H; y++)
                for (int x = it.X; x < it.X + it.W; x++)
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + DX[d], ny = y + DY[d];
                        if (it.Contains(nx, ny)) continue;
                        int n = g.In(nx, ny) ? g.Cell(nx, ny) : -1;
                        if (n < 0 || !list.Contains(n)) list.Add(n);
                    }
            return list;
        }

        /// <summary>Buildings touching a building's sides (distinct, grid order).</summary>
        public static List<CityGridItem> Touching(CityGrid g, CityGridItem it)
        {
            var list = new List<CityGridItem>();
            foreach (var n in SideCells(g, it))
            {
                if (n < 0 || g.Owner[n] < 0) continue;
                var o = g.Items[g.Owner[n]];
                if (o != it && !list.Contains(o)) list.Add(o);
            }
            return list;
        }

        /// <summary>Straight-step distance from the closest footprint tile to the fireplace.</summary>
        public static int FireDistance(ContentDb c, CityGridItem it)
        {
            int fx = c.CityTuning.FireX, fy = c.CityTuning.FireY;
            int dx = fx < it.X ? it.X - fx : fx >= it.X + it.W ? fx - (it.X + it.W - 1) : 0;
            int dy = fy < it.Y ? it.Y - fy : fy >= it.Y + it.H ? fy - (it.Y + it.H - 1) : 0;
            return dx + dy;
        }

        // ---- placement skills ----------------------------------------------------------------------

        /// <summary>True if the building matches a placement target: blueprint id, "tag:x", "cat:Category"; null = any.</summary>
        public static bool Matches(BlueprintDef def, string target)
        {
            if (def == null) return false;
            if (string.IsNullOrEmpty(target)) return true;
            if (target.StartsWith("tag:", StringComparison.Ordinal)) return def.Tags.Contains(target.Substring(4));
            if (target.StartsWith("cat:", StringComparison.Ordinal)) return def.Category.ToString() == target.Substring(4);
            return def.Id == target;
        }

        /// <summary>Own placement skill of one building on a grid (before auras and the cap).</summary>
        public static CityBonus Evaluate(CityGrid g, CityGridItem it)
        {
            var res = new CityBonus();
            foreach (var o in Touching(g, it))
                if (o.Def.Placement != null && o.Def.Placement.Kind == PlacementKind.Aura) res.AuraFrom.Add(o.Uid);
            var p = it.Def.Placement;
            if (p == null || p.Kind == PlacementKind.Aura) return res;
            bool met = false;
            double pct = 0;
            switch (p.Kind)
            {
                case PlacementKind.Neighbours:
                {
                    int n = 0;
                    foreach (var o in Touching(g, it)) if (Matches(o.Def, p.Target)) n++;
                    pct = Math.Min(p.MaxPct, n * p.Pct);
                    met = n > 0;
                    break;
                }
                case PlacementKind.MeltingPot:
                {
                    var cats = new List<BuildingCategory>();
                    foreach (var o in Touching(g, it)) if (!cats.Contains(o.Def.Category)) cats.Add(o.Def.Category);
                    pct = Math.Min(p.MaxPct, cats.Count * p.Pct);
                    met = cats.Count > 0;
                    break;
                }
                case PlacementKind.Diagonal:
                {
                    int n = CornerOnly(g, it).Count;
                    pct = Math.Min(p.MaxPct, n * p.Pct);
                    met = n > 0;
                    break;
                }
                case PlacementKind.RowOfThree: met = InRowOfThree(g, it, p.Target); break;
                case PlacementKind.AcrossStreet: met = AcrossStreet(g, it, p.Target); break;
                case PlacementKind.Hearth: met = FireDistance(g.Content, it) <= p.Count; break;
                case PlacementKind.Outskirts: met = FireDistance(g.Content, it) >= p.Count; break;
                case PlacementKind.OpenGround:
                {
                    int n = 0;
                    foreach (var cell in SideCells(g, it)) if (cell >= 0 && g.IsOpen(cell % g.W, cell / g.W)) n++;
                    met = n >= p.Count;
                    break;
                }
                case PlacementKind.Cliffside:
                    foreach (var cell in SideCells(g, it)) if (cell < 0 || !g.Land[cell]) { met = true; break; }
                    break;
                case PlacementKind.TouchPlaza:
                    foreach (var cell in SideCells(g, it)) if (cell >= 0 && g.Plaza[cell]) { met = true; break; }
                    break;
            }
            if (!met) return res;
            if (p.Kind != PlacementKind.Neighbours && p.Kind != PlacementKind.MeltingPot && p.Kind != PlacementKind.Diagonal) pct = p.Pct;
            res.OwnPct = pct;
            res.Flat = p.Flat;
            return res;
        }

        private static List<CityGridItem> CornerOnly(CityGrid g, CityGridItem it)
        {
            var side = Touching(g, it);
            var list = new List<CityGridItem>();
            int[,] corners = { { it.X - 1, it.Y - 1 }, { it.X + it.W, it.Y - 1 }, { it.X - 1, it.Y + it.H }, { it.X + it.W, it.Y + it.H } };
            for (int i = 0; i < 4; i++)
            {
                var o = g.ItemAt(corners[i, 0], corners[i, 1]);
                if (o != null && o != it && !side.Contains(o) && !list.Contains(o)) list.Add(o);
            }
            return list;
        }

        /// <summary>Buildings matching <paramref name="target"/> touching one side of <paramref name="it"/> (dir: 0 east, 1 west, 2 south, 3 north).</summary>
        private static List<CityGridItem> SideNeighbours(CityGrid g, CityGridItem it, int dir, string target)
        {
            var list = new List<CityGridItem>();
            if (dir < 2)
            {
                int x = dir == 0 ? it.X + it.W : it.X - 1;
                for (int y = it.Y; y < it.Y + it.H; y++)
                {
                    var o = g.ItemAt(x, y);
                    if (o != null && o != it && Matches(o.Def, target) && !list.Contains(o)) list.Add(o);
                }
            }
            else
            {
                int y = dir == 2 ? it.Y + it.H : it.Y - 1;
                for (int x = it.X; x < it.X + it.W; x++)
                {
                    var o = g.ItemAt(x, y);
                    if (o != null && o != it && Matches(o.Def, target) && !list.Contains(o)) list.Add(o);
                }
            }
            return list;
        }

        /// <summary>
        /// Row of Three: the building and two buildings of the target kind side by side in one line (east–west or north–south): a
        /// neighbour on both sides, or a neighbour with a further neighbour on the same side.
        /// </summary>
        private static bool InRowOfThree(CityGrid g, CityGridItem it, string target)
        {
            for (int axis = 0; axis < 2; axis++)
            {
                int a = axis * 2, b = axis * 2 + 1;
                var first = SideNeighbours(g, it, a, target);
                var second = SideNeighbours(g, it, b, target);
                if (first.Count > 0 && second.Count > 0) return true;
                foreach (var o in first) foreach (var o2 in SideNeighbours(g, o, a, target)) if (o2 != it) return true;
                foreach (var o in second) foreach (var o2 in SideNeighbours(g, o, b, target)) if (o2 != it) return true;
            }
            return false;
        }

        /// <summary>A target building directly opposite across a 1-wide street (footprint tile → street → target tile, straight).</summary>
        private static bool AcrossStreet(CityGrid g, CityGridItem it, string target)
        {
            for (int y = it.Y; y < it.Y + it.H; y++)
                for (int x = it.X; x < it.X + it.W; x++)
                    for (int d = 0; d < 4; d++)
                    {
                        int sx = x + DX[d], sy = y + DY[d];
                        if (it.Contains(sx, sy) || !g.In(sx, sy) || !g.Street[g.Cell(sx, sy)]) continue;
                        var o = g.ItemAt(sx + DX[d], sy + DY[d]);
                        if (o != null && o != it && Matches(o.Def, target)) return true;
                    }
            return false;
        }

        // ---- cache + queries -------------------------------------------------------------------------

        private sealed class Cache
        {
            public int Rev;
            public ContentDb Content;
            public Dictionary<string, CityBonus> Bonuses = new Dictionary<string, CityBonus>();
        }

        /// <summary>Placement bonuses of every placed building (cached per layout revision).</summary>
        public static Dictionary<string, CityBonus> Bonuses(GameState s, ContentDb c)
        {
            if (s.City.LayoutCache is Cache cache && cache.Rev == s.City.LayoutRev && ReferenceEquals(cache.Content, c)) return cache.Bonuses;
            cache = new Cache { Rev = s.City.LayoutRev, Content = c };
            var g = Grid(s, c);
            foreach (var it in g.Items) cache.Bonuses[it.Uid] = Evaluate(g, it);
            s.City.LayoutCache = cache;
            return cache.Bonuses;
        }

        /// <summary>Marks the layout changed (bonuses are rebuilt on the next query). Every layout command calls this.</summary>
        public static void Invalidate(GameState s)
        {
            s.City.LayoutRev++;
            s.City.LayoutCache = null;
        }

        /// <summary>
        /// Placement percent of a building: its own skill plus auras of active buildings touching it, capped at
        /// CityTuning.PlacementCapPct. 0 for stored buildings.
        /// </summary>
        public static double PlacementPct(GameState s, ContentDb c, string uid)
        {
            if (uid == null || !Bonuses(s, c).TryGetValue(uid, out var b)) return 0;
            return Total(s, c, b);
        }

        /// <summary>Flat whole-number placement bonus of a building (Scout Post / War Academy), 0 if the rule is not met.</summary>
        public static double PlacementFlat(GameState s, ContentDb c, string uid) =>
            uid != null && Bonuses(s, c).TryGetValue(uid, out var b) ? b.Flat : 0;

        internal static double Total(GameState s, ContentDb c, CityBonus b)
        {
            double pct = b.OwnPct;
            foreach (var src in b.AuraFrom)
            {
                var ab = CitySystem.Building(s, src);
                if (ab == null || !CitySystem.IsActive(s, c, ab)) continue;
                pct += c.Blueprint(ab.BlueprintId)?.Placement?.Pct ?? 0;
            }
            return Math.Min(c.CityTuning.PlacementCapPct, pct);
        }

        /// <summary>
        /// Editor preview: the layout check of an edit plus the placement bonus the edited building (Place / Move) would get
        /// (percent after auras and cap, and flat). Bonus is 0 when the check fails.
        /// </summary>
        public static CityCheck Preview(GameState s, ContentDb c, CityEdit edit, out double pct, out double flat)
        {
            pct = 0; flat = 0;
            var check = Validate(s, c, edit);
            if (!check.Ok || (edit.Kind != CityEditKind.Place && edit.Kind != CityEditKind.Move)) return check;
            var g = Grid(s, c, edit);
            var it = g.Find(edit.Uid ?? NewUid);
            if (it == null) return check;
            var b = Evaluate(g, it);
            pct = Total(s, c, b);
            flat = b.Flat;
            return check;
        }

        // ---- digging -----------------------------------------------------------------------------------

        /// <summary>True if any patch cell side-touches the player's land.</summary>
        public static bool PatchTouchesLand(GameState s, ContentDb c, CityPatchDef p)
        {
            int w = c.CityTuning.GridWidth, h = c.CityTuning.GridHeight;
            foreach (var cell in p.Cells)
            {
                int x = cell % w, y = cell / w;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], ny = y + DY[d];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    if (IsLand(s, c, ny * w + nx)) return true;
                }
            }
            return false;
        }
    }
}
