using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.App
{
    /// <summary>
    /// The City editor's ghost (City design §2): the footprint under the finger checked with the rules' own
    /// <see cref="CityLayout.Preview"/> (= <see cref="CityLayout.Validate"/> + the placement bonus), so the client and the server
    /// always agree. The chip says the reason ("Must touch a street") or the bonus ("+20% output · Fields"). Pure; tested.
    /// </summary>
    public sealed class CityGhost
    {
        public string BlueprintId;
        /// <summary>Stored copy being placed, or the building being moved; null = a new copy.</summary>
        public string Uid;
        public bool Move;
        public int X, Y;
        public bool Rotated;
        public CityCheck Check;
        public double Pct, Flat;
        /// <summary>The chip under the ghost: the reason it can't go here, or the bonus it gets here.</summary>
        public string Chip;
        public bool Ok => Check != null && Check.Ok;
        public int W, H;

        public CityEdit Edit => Move ? CityEdit.Move(Uid, X, Y, Rotated) : CityEdit.Place(BlueprintId, X, Y, Rotated, Uid);

        /// <summary>The command this ghost sends when dropped on a valid spot.</summary>
        public GameCommand Command => Move
            ? (GameCommand)new CityMove { Uid = Uid, X = X, Y = Y, Rotated = Rotated }
            : new CityPlace { BlueprintId = BlueprintId, Uid = Uid, X = X, Y = Y, Rotated = Rotated };

        /// <summary>Footprint cells (y × GridWidth + x).</summary>
        public List<int> Cells(ContentDb c)
        {
            var list = new List<int>();
            for (int y = Y; y < Y + H; y++)
                for (int x = X; x < X + W; x++)
                    list.Add(y * c.CityTuning.GridWidth + x);
            return list;
        }

        /// <summary>Re-checks the ghost at (x, y) with the current rotation.</summary>
        public CityGhost At(GameState s, ContentDb c, int x, int y)
        {
            X = x; Y = y;
            return Evaluate(s, c);
        }

        public CityGhost Evaluate(GameState s, ContentDb c)
        {
            var def = c.Blueprint(BlueprintId);
            if (def != null) CityLayout.Footprint(def, Rotated, out W, out H);
            Check = CityLayout.Preview(s, c, Edit, out Pct, out Flat);
            Chip = Check.Ok ? BonusChip(def, Pct, Flat) : Reason(Check.Error);
            return this;
        }

        public static CityGhost ForPlace(GameState s, ContentDb c, string blueprintId, string storedUid, int x, int y, bool rotated) =>
            new CityGhost { BlueprintId = blueprintId, Uid = storedUid, X = x, Y = y, Rotated = rotated }.Evaluate(s, c);

        public static CityGhost ForMove(GameState s, ContentDb c, string uid, int x, int y, bool rotated)
        {
            var b = CitySystem.Building(s, uid);
            return new CityGhost { BlueprintId = b?.BlueprintId, Uid = uid, Move = true, X = x, Y = y, Rotated = rotated }.Evaluate(s, c);
        }

        /// <summary>
        /// Rotate (⟳): swaps the footprint of non-square buildings around the same top-left tile. Returns false for square ones.
        /// </summary>
        public bool Rotate(GameState s, ContentDb c)
        {
            var def = c.Blueprint(BlueprintId);
            if (def == null || def.Width == def.Height) return false;
            Rotated = !Rotated;
            Evaluate(s, c);
            return true;
        }

        /// <summary>A rules error as a player-facing chip ("must touch a street" → "Must touch a street").</summary>
        public static string Reason(string error)
        {
            if (string.IsNullOrEmpty(error)) return null;
            switch (error)
            {
                case "not dug land": return "Not on dug land";
                case "on the plaza": return "That is the crash site";
                case "blocked by terrain": return "Terrain is in the way";
                case "space taken": return "Something is in the way";
                case "not on free land": return "Something is in the way";
            }
            if (error.EndsWith(" built", StringComparison.Ordinal)) return error.Replace(" built", " placed");
            return char.ToUpperInvariant(error[0]) + error.Substring(1);
        }

        /// <summary>"+20% output · Fields", "+1 skip · Outskirts", "No bonus here" (valid spot, rule not met).</summary>
        public static string BonusChip(BlueprintDef def, double pct, double flat)
        {
            if (def == null) return null;
            if (def.Placement == null || def.Placement.Kind == PlacementKind.Aura) return "Fits here";
            string rule = SkillName(def.Placement);
            if (flat > 0) return "+" + Fmt.Whole(flat) + " " + FlatWord(def, flat) + " · " + rule;
            if (pct <= 0) return "No bonus here · " + rule;
            return "+" + Fmt.Pct(Math.Round(pct)) + " " + EffectWord(def) + " · " + rule;
        }

        /// <summary>Short name of a placement rule: the text's own prefix ("Fields: …", "Hearth: …") else the kind.</summary>
        public static string SkillName(PlacementSkillDef p)
        {
            if (p == null) return "";
            if (!string.IsNullOrEmpty(p.Text))
            {
                int colon = p.Text.IndexOf(':');
                if (colon > 0 && colon <= 12 && !p.Text.StartsWith("+", StringComparison.Ordinal)) return p.Text.Substring(0, colon);
            }
            switch (p.Kind)
            {
                case PlacementKind.Neighbours: return "Neighbours";
                case PlacementKind.MeltingPot: return "Melting Pot";
                case PlacementKind.Diagonal: return "Diagonal";
                case PlacementKind.AcrossStreet: return "Across the Street";
                case PlacementKind.Hearth: return "Hearth";
                case PlacementKind.Outskirts: return "Outskirts";
                case PlacementKind.OpenGround: return "Open Ground";
                case PlacementKind.Cliffside: return "Cliffside";
                case PlacementKind.Aura: return "Aura";
                case PlacementKind.TouchPlaza: return "Plaza";
                default: return "";
            }
        }

        /// <summary>What the placement bonus raises: "output", "speed" (pull buildings), or the boon ("Stamina", "regen" …).</summary>
        public static string EffectWord(BlueprintDef def)
        {
            if (def.Pull != null) return "speed";
            if (def.ProduceCurrency != null) return "output";
            foreach (var b in def.Boons)
                if (b.Placement)
                    switch (b.Kind)
                    {
                        case BoonKind.StaminaCap: return "Stamina";
                        case BoonKind.StaminaRegenPct: return "regen";
                        case BoonKind.RunStartCoins: return "run coins";
                        case BoonKind.BreedingSpeedPct: return "breeding speed";
                        case BoonKind.BreedingQualityPct: return "gene quality";
                        case BoonKind.RunXpPct: return "run XP";
                        case BoonKind.ProductionPct: return "production bonus";
                        case BoonKind.AwakeningSlowPct: return "boss slowdown";
                        default: return "boon";
                    }
            return "boon";
        }

        private static string FlatWord(BlueprintDef def, double flat)
        {
            foreach (var b in def.Boons)
                if (b.Placement)
                {
                    if (b.Kind == BoonKind.SkipNegativeEvents) return flat == 1 ? "skip" : "skips";
                    if (b.Kind == BoonKind.DraftRerolls) return flat == 1 ? "reroll" : "rerolls";
                }
            return "";
        }
    }

    /// <summary>
    /// Undo (City design §2): every edit is applied at once; undo sends the reverse command (up to <see cref="Max"/> steps, while in
    /// edit mode). Undoing a new build sends CityStore, which cancels it and refunds the cost. Pure; tested.
    /// </summary>
    public sealed class CityUndo
    {
        public const int Max = 50;
        private readonly List<GameCommand> _stack = new List<GameCommand>();

        public int Count => _stack.Count;
        public void Clear() => _stack.Clear();
        public GameCommand Peek() => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        public GameCommand Pop()
        {
            if (_stack.Count == 0) return null;
            var c = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            return c;
        }

        /// <summary>Records the reverse of an applied edit (oldest dropped past <see cref="Max"/>).</summary>
        public void Push(GameCommand reverse)
        {
            if (reverse == null) return;
            _stack.Add(reverse);
            while (_stack.Count > Max) _stack.RemoveAt(0);
        }

        /// <summary>
        /// The reverse of <paramref name="cmd"/>, computed from the state BEFORE it runs. A new copy's uid is only known afterwards:
        /// call <see cref="Applied"/> with the command's payload.
        /// </summary>
        public static GameCommand Reverse(GameState before, GameCommand cmd)
        {
            switch (cmd)
            {
                case CityPlace p: return new CityStore { Uid = p.Uid };
                case CityMove m:
                {
                    var b = CitySystem.Building(before, m.Uid);
                    return b == null ? null : new CityMove { Uid = b.Uid, X = b.X, Y = b.Y, Rotated = b.Rotated };
                }
                case CityStore s:
                {
                    var b = CitySystem.Building(before, s.Uid);
                    if (b == null) return null;
                    // a cancelled first construction is gone: undo builds a new copy on the same spot (paid again)
                    return new CityPlace { BlueprintId = b.BlueprintId, Uid = b.Level == 0 ? null : b.Uid, X = b.X, Y = b.Y, Rotated = b.Rotated };
                }
                case CityStreets st: return new CityStreets { Adds = new List<int>(st.Removes), Removes = new List<int>(st.Adds) };
                default: return null;
            }
        }

        /// <summary>Fills in what only the result knows (a new copy's uid from CityPlace's payload) and pushes the reverse.</summary>
        public void Applied(GameCommand reverse, object payload)
        {
            if (reverse is CityStore st && string.IsNullOrEmpty(st.Uid)) st.Uid = PayloadString(payload);
            if (reverse is CityStore st2 && string.IsNullOrEmpty(st2.Uid)) return;
            Push(reverse);
        }

        /// <summary>A string payload (offline: the string; online: a JSON value).</summary>
        public static string PayloadString(object payload)
        {
            if (payload == null) return null;
            if (payload is string s) return s;
            var t = payload.ToString();
            return t != null && t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"' ? t.Substring(1, t.Length - 2) : t;
        }
    }

    /// <summary>
    /// One street stroke (City design §2): painting grows out of the plaza or a linked street, each new tile side-touching the last
    /// one (a skipped tile is filled with straight steps, backing up onto the previous tile takes the last one back); erasing removes
    /// street tiles, and a tile whose removal would cut off a building or other streets is refused (its BadCells / BadUids flash red).
    /// The whole stroke is ONE CityStreets command. Pure; tested.
    /// </summary>
    public sealed class StreetStroke
    {
        public readonly bool Erase;
        /// <summary>The stroke's tiles in drag order (new streets, or streets to remove).</summary>
        public readonly List<int> Cells = new List<int>();
        /// <summary>Last refusal ("Not on dug land", "Streets must start from a street or the fireplace" …) and what to flash.</summary>
        public string Refused;
        public readonly List<int> FlashCells = new List<int>();
        public readonly List<string> FlashUids = new List<string>();
        private int _last = -1;
        private readonly GameState _s;
        private readonly ContentDb _c;

        public StreetStroke(GameState s, ContentDb c, bool erase) { _s = s; _c = c; Erase = erase; }

        public bool Started => _last >= 0;
        private int W => _c.CityTuning.GridWidth;
        private int H => _c.CityTuning.GridHeight;

        /// <summary>The finger went down on <paramref name="cell"/>: an anchor (plaza / street) or the first tile.</summary>
        public bool Begin(int cell)
        {
            Refused = null;
            if (Erase) { bool ok = Add(cell); _last = cell; return ok; }
            var g = CityLayout.Grid(_s, _c, CityEdit.Streets(Cells, null));
            var linked = CityLayout.LinkedStreets(g);
            if (cell >= 0 && cell < g.Plaza.Length && (g.Plaza[cell] || (g.Street[cell] && linked[cell]))) { _last = cell; return true; }
            if (!Touches(g, linked, cell)) { Refused = "Streets must start from a street or the fireplace"; return false; }
            return Add(cell);
        }

        /// <summary>The finger moved over <paramref name="cell"/> (steps through skipped tiles; backing up takes the last tile back).</summary>
        public void Extend(int cell)
        {
            if (cell < 0 || cell == _last) return;
            if (!Started) { Begin(cell); return; }
            if (Erase) { Add(cell); _last = cell; return; }
            if (Cells.Count >= 2 && cell == Cells[Cells.Count - 2] && _last == Cells[Cells.Count - 1])
            {
                Cells.RemoveAt(Cells.Count - 1);
                _last = cell;
                return;
            }
            int tx = cell % W, ty = cell / W;
            int guard = 0;
            while (_last != cell && guard++ < 64)
            {
                int lx = _last % W, ly = _last / W;
                int nx = lx, ny = ly;
                if (Math.Abs(tx - lx) >= Math.Abs(ty - ly)) nx += Math.Sign(tx - lx); else ny += Math.Sign(ty - ly);
                int next = ny * W + nx;
                if (!Step(next)) return;
            }
        }

        private bool Step(int next)
        {
            if (Erase) return Add(next);
            if (Cells.Contains(next)) { _last = next; return true; }
            var g = CityLayout.Grid(_s, _c, CityEdit.Streets(Cells, null));
            if (next >= 0 && next < g.Street.Length && (g.Plaza[next] || g.Street[next])) { _last = next; return true; } // walking along the network
            return Add(next);
        }

        private bool Touches(CityGrid g, bool[] linked, int cell)
        {
            int x = cell % W, y = cell / W;
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            for (int d = 0; d < 4; d++)
            {
                int nx = x + dx[d], ny = y + dy[d];
                if (!g.In(nx, ny)) continue;
                int n = g.Cell(nx, ny);
                if (g.Plaza[n] || (g.Street[n] && linked[n])) return true;
            }
            return false;
        }

        private bool Add(int cell)
        {
            Refused = null;
            if (cell < 0 || cell >= W * H) { Refused = "Not on dug land"; return false; }
            if (Erase)
            {
                if (Cells.Contains(cell)) { _last = cell; return true; }
                if (!_s.City.Streets.Contains(cell)) { _last = cell; return false; } // nothing to erase here (keep going)
                var probe = new List<int>(Cells) { cell };
                var check = CityLayout.Validate(_s, _c, CityEdit.Streets(null, probe));
                if (!check.Ok)
                {
                    Refused = check.Error == "a building would lose its street" ? "That would cut off a building"
                        : check.Error == "streets must link to the plaza" ? "That would cut off other streets" : CityGhost.Reason(check.Error);
                    FlashCells.Clear(); FlashCells.AddRange(check.BadCells);
                    FlashUids.Clear(); FlashUids.AddRange(check.BadUids);
                    return false;
                }
                Cells.Add(cell);
                _last = cell;
                return true;
            }
            int x = cell % W, y = cell / W;
            var g = CityLayout.Grid(_s, _c, CityEdit.Streets(Cells, null));
            if (!g.IsLand(x, y)) { Refused = "Not on dug land"; return false; }
            if (g.Plaza[cell]) { _last = cell; return true; }
            if (g.Feature[cell]) { Refused = "Terrain is in the way"; return false; }
            if (g.Owner[cell] >= 0) { Refused = "Something is built here"; return false; }
            if (g.Street[cell]) { _last = cell; return true; }
            // the new tile must touch the last tile of the stroke (or the network when the stroke is empty)
            bool adjacent = _last >= 0 && Math.Abs(_last % W - x) + Math.Abs(_last / W - y) == 1;
            if (!adjacent && !Touches(g, CityLayout.LinkedStreets(g), cell)) { Refused = "Streets must start from a street or the fireplace"; return false; }
            Cells.Add(cell);
            _last = cell;
            return true;
        }

        /// <summary>The stroke as one command (null when it changed nothing).</summary>
        public CityStreets ToCommand()
        {
            if (Cells.Count == 0) return null;
            var cmd = new CityStreets();
            if (Erase) cmd.Removes.AddRange(Cells); else cmd.Adds.AddRange(Cells);
            return cmd;
        }

        /// <summary>The rules' check of the whole stroke (the same one the server runs).</summary>
        public CityCheck Check() => CityLayout.Validate(_s, _c, Erase ? CityEdit.Streets(null, Cells) : CityEdit.Streets(Cells, null));
    }

    /// <summary>Street autotile (City design §8): the 4-neighbour mask of a street tile; the plaza counts as a street.</summary>
    public static class StreetTiles
    {
        /// <summary>Mask bits: 1 = x+1, 2 = y+1, 4 = x−1, 8 = y−1 (16 variants: end, straight, corner, T, cross).</summary>
        public static int Mask(Func<int, int, bool> isStreet, int x, int y) =>
            (isStreet(x + 1, y) ? 1 : 0) | (isStreet(x, y + 1) ? 2 : 0) | (isStreet(x - 1, y) ? 4 : 0) | (isStreet(x, y - 1) ? 8 : 0);

        public static string Variant(int mask)
        {
            int n = 0;
            for (int b = 0; b < 4; b++) if ((mask & (1 << b)) != 0) n++;
            switch (n)
            {
                case 0: return "dot";
                case 1: return "end";
                case 2: return mask == 5 || mask == 10 ? "straight" : "corner";
                case 3: return "t";
                default: return "cross";
            }
        }
    }

    /// <summary>
    /// The fixed day / night cycle (City design §7, decided): 20 minutes of wall-clock time — 12 min day, 2 min dusk, 5 min night,
    /// 1 min dawn — the same for everyone. Pure; tested.
    /// </summary>
    public static class CityDayNight
    {
        public const long Minute = 60_000L;
        public const long CycleMs = 20 * Minute;
        public const long DayMs = 12 * Minute, DuskMs = 2 * Minute, NightMs = 5 * Minute, DawnMs = 1 * Minute;

        public static long InCycle(long nowMs) => ((nowMs % CycleMs) + CycleMs) % CycleMs;

        public static string Phase(long nowMs)
        {
            long t = InCycle(nowMs);
            if (t < DayMs) return "day";
            if (t < DayMs + DuskMs) return "dusk";
            if (t < DayMs + DuskMs + NightMs) return "night";
            return "dawn";
        }

        /// <summary>0 by day, 1 at night, ramping through dusk and dawn.</summary>
        public static float Darkness(long nowMs)
        {
            long t = InCycle(nowMs);
            if (t < DayMs) return 0;
            if (t < DayMs + DuskMs) return (t - DayMs) / (float)DuskMs;
            if (t < DayMs + DuskMs + NightMs) return 1;
            return 1 - (t - DayMs - DuskMs - NightMs) / (float)DawnMs;
        }

        /// <summary>A time (ms) in the middle of a phase of the cycle containing <paramref name="nowMs"/> (tests, showcase).</summary>
        public static long MiddleOf(string phase, long nowMs)
        {
            long start = nowMs - InCycle(nowMs);
            switch (phase)
            {
                case "dusk": return start + DayMs + DuskMs / 2;
                case "night": return start + DayMs + DuskMs + NightMs / 2;
                case "dawn": return start + DayMs + DuskMs + NightMs + DawnMs / 2;
                default: return start + DayMs / 2;
            }
        }
    }

    /// <summary>
    /// Walker routes (City design §8): BFS over the plaza and linked street cells, building door → the fireplace, cached per layout
    /// revision. Cells are (x, y) grid points. Pure; tested.
    /// </summary>
    public sealed class CityRoutes
    {
        private int _rev = -1;
        private int _streetsHash;
        private readonly Dictionary<string, List<int>> _routes = new Dictionary<string, List<int>>();
        public CityGrid Grid { get; private set; }
        public bool[] Walkable { get; private set; }
        /// <summary>Rebuilds counted (tests: cached until the layout changes).</summary>
        public int Builds { get; private set; }

        public void Ensure(GameState s, ContentDb c)
        {
            int hash = s.City.Streets.Count * 7919 + s.City.DugPatches.Count;
            foreach (var st in s.City.Streets) hash = hash * 31 + st;
            if (Grid != null && _rev == s.City.LayoutRev && hash == _streetsHash) return;
            _rev = s.City.LayoutRev;
            _streetsHash = hash;
            _routes.Clear();
            Builds++;
            Grid = CityLayout.Grid(s, c);
            var linked = CityLayout.LinkedStreets(Grid);
            Walkable = new bool[linked.Length];
            for (int i = 0; i < linked.Length; i++) Walkable[i] = Grid.Plaza[i] || (Grid.Street[i] && linked[i]);
        }

        /// <summary>The cell a building is entered from: a walkable side cell, the one closest to the fireplace; −1 if none.</summary>
        public int Door(string uid)
        {
            var it = Grid?.Find(uid);
            if (it == null) return -1;
            int best = -1, bestD = int.MaxValue;
            int fx = Grid.Content.CityTuning.FireX, fy = Grid.Content.CityTuning.FireY;
            foreach (var n in CityLayout.SideCells(Grid, it))
            {
                if (n < 0 || !Walkable[n]) continue;
                int d = Math.Abs(n % Grid.W - fx) + Math.Abs(n / Grid.W - fy);
                if (d < bestD) { bestD = d; best = n; }
            }
            return best;
        }

        /// <summary>Cells from the building's door to a plaza cell next to the fireplace (inclusive); null if unreachable.</summary>
        public List<int> ToFire(string uid)
        {
            if (Grid == null) return null;
            if (_routes.TryGetValue(uid, out var cached)) return cached;
            int door = Door(uid);
            var route = door < 0 ? null : Bfs(door);
            _routes[uid] = route;
            return route;
        }

        private List<int> Bfs(int from)
        {
            int w = Grid.W;
            int fx = Grid.Content.CityTuning.FireX, fy = Grid.Content.CityTuning.FireY;
            var prev = new int[Walkable.Length];
            for (int i = 0; i < prev.Length; i++) prev[i] = -2;
            var q = new Queue<int>();
            q.Enqueue(from);
            prev[from] = -1;
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                int x = cur % w, y = cur / w;
                if (Math.Abs(x - fx) + Math.Abs(y - fy) == 1)
                {
                    var path = new List<int>();
                    for (int c = cur; c != -1; c = prev[c]) path.Add(c);
                    path.Reverse();
                    return path;
                }
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], ny = y + dy[d];
                    if (!Grid.In(nx, ny)) continue;
                    int n = Grid.Cell(nx, ny);
                    if (prev[n] != -2 || !Walkable[n]) continue;
                    prev[n] = cur;
                    q.Enqueue(n);
                }
            }
            return null;
        }
    }
}
