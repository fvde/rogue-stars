using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// City (interim screen until the City redesign's diorama screen lands): the 20×20 land with dug land, rubble patches (tap to
    /// dig), the plaza, streets (paint / erase one tile per tap), features and buildings; a tray of blueprints (new copies and
    /// stored ones); the selected building's card (level, stars, placement bonus, output or chances, upgrade, move, store, workers).
    /// Every edit is one command (CityPlace / CityMove / CityStore / CityStreets / CityDig) checked by CityLayout.Validate.
    /// </summary>
    public sealed class CityScreen : UiScreen
    {
        private enum Mode { Select, Place, Move, Streets }

        private const float GridPixels = 924;
        private Mode _mode;
        private string _selected;     // selected building uid
        private string _pendingBp;    // blueprint being placed
        private string _pendingUid;   // stored copy being placed, or the building being moved
        private bool _rotated;

        public override string Title => "City";
        public override string[] IntroFeatures => new[] { Features.BiomassFarm, Features.GrainFarm, Features.OreMine, Features.AlienSquad };

        /// <summary>Select a placed building (its detail card), as a tap on it does.</summary>
        public void Select(string uid) { _selected = uid; _mode = Mode.Select; Rebuild(); }

        protected override void Build(RectTransform body)
        {
            Toolbar(body);
            if (_mode != Mode.Select)
            {
                var banner = Ui.Row(body, 90, 10, Theme.Warn, 12);
                string what = _mode == Mode.Streets ? "Streets: tap land next to the plaza or a street to pave, a street to erase"
                    : (_mode == Mode.Place ? "Place " : "Move ") + (Content.Blueprint(_pendingBp)?.Name ?? _pendingBp) + ": tap the top-left tile";
                Ui.Label(banner, what, Theme.FontSmall, Color.black);
                if (_mode != Mode.Streets) Ui.Button(banner, "Rotate", () => { _rotated = !_rotated; Rebuild(); }, Theme.Button, 160, 76);
                Ui.Button(banner, "Done", () => { _mode = Mode.Select; _pendingBp = _pendingUid = null; Rebuild(); }, Theme.Button, 160, 76);
            }
            GridView(body);
            Details(body);
            Tray(body);
        }

        // ---- toolbar ----------------------------------------------------------------------------

        private void Toolbar(RectTransform body)
        {
            var row = Ui.Row(body);
            Ui.Button(row, "COLLECT ALL", () => Run(new CityCollect()), Theme.Good, -1, Theme.RowHeight, true, Theme.FontLarge);
            bool chances = false;
            foreach (var b in State.City.Buildings) if (CitySystem.ChancesReady(b) >= 1) chances = true;
            Ui.Button(row, "Pop all", () => Run(new CityPop()), Theme.Purple, 180, Theme.RowHeight, chances).name = "PopAll";
            Ui.Button(row, "Streets", () => { _mode = Mode.Streets; Rebuild(); }, _mode == Mode.Streets ? Theme.Accent : Theme.Button, 180, Theme.RowHeight);
            Ui.Button(row, "Priority: " + (State.City.PriorityCurrency == null ? "none" : Fmt.CurrencyName(State.City.PriorityCurrency)), PriorityModal, Theme.Button);
            int n = CitySystem.NextDigNumber(State);
            var dig = CitySystem.DigDef(Content, n);
            string digInfo = State.City.DigPatchId != null ? "Digging " + Content.CityPatch(State.City.DigPatchId)?.Name
                : dig == null ? "All land dug" : "Dig #" + n + ": " + Fmt.Costs(CitySystem.DigCost(Content, n), Content) + " · " + Fmt.Duration(dig.Ms)
                  + (State.City.FreeDigs > 0 ? " (" + State.City.FreeDigs + " free)" : "") + " · tap rubble next to your land";
            Note(body, digInfo).name = "DigInfo";
            if (State.City.DigTimerId != null) TimerControls(body, State.City.DigTimerId, "Dig");
        }

        private void PriorityModal()
        {
            var produced = new SortedSet<string>();
            foreach (var d in Content.Blueprints.Values) if (d.ProduceCurrency != null) produced.Add(d.ProduceCurrency);
            Modal("Production priority", b =>
            {
                Note(b, "The priority resource gets +" + Fmt.Pct(Content.CityTuning.PriorityBoostPct) + ", others −" + Fmt.Pct(Content.CityTuning.PriorityPenaltyPct) + ".");
                Ui.Button(b, "None", () => Run(new CitySetPriority { Currency = null }), State.City.PriorityCurrency == null ? Theme.Accent : Theme.Button);
                foreach (var c in produced)
                {
                    string cur = c;
                    Ui.Button(b, Fmt.CurrencyName(cur), () => Run(new CitySetPriority { Currency = cur }), State.City.PriorityCurrency == cur ? Theme.Accent : Theme.Button);
                }
            }, new ModalButton("Close", null));
        }

        // ---- grid -------------------------------------------------------------------------------

        private void GridView(RectTransform body)
        {
            var g = CityLayout.Grid(State, Content);
            float cell = GridPixels / Mathf.Max(g.W, g.H);
            var holder = Ui.Rect(body, "GridHolder");
            Ui.Size(holder, -1, cell * g.H + 8);
            var grid = Ui.RoundPanel(holder, Theme.Hex(0x0B120E), 24, "Grid").rectTransform;
            grid.anchorMin = grid.anchorMax = new Vector2(0.5f, 1);
            grid.pivot = new Vector2(0.5f, 1);
            grid.sizeDelta = new Vector2(cell * g.W, cell * g.H);
            grid.anchoredPosition = Vector2.zero;

            var patchOf = new Dictionary<int, CityPatchDef>();
            foreach (var p in Content.CityPatches) foreach (var c in p.Cells) patchOf[c] = p;
            for (int y = 0; y < g.H; y++)
                for (int x = 0; x < g.W; x++)
                {
                    int cx = x, cy = y, c = g.Cell(x, y);
                    if (!g.Land[c])
                    {
                        if (!patchOf.TryGetValue(c, out var patch)) continue;
                        bool diggable = CitySystem.DigBlockReason(State, Content, patch.Id) == null;
                        var rubble = Ui.Button(grid, "", () => DigModal(patch), diggable ? Theme.Hex(0x4A4368) : Theme.Hex(0x2A2638), -1, -1, true);
                        rubble.name = "Patch:" + patch.Id;
                        Place(rubble.GetComponent<RectTransform>(), x, y, 1, 1, cell, 1);
                        continue;
                    }
                    if (g.Owner[c] >= 0) continue;
                    Color col = g.Plaza[c] ? Theme.Hex(0x8F8063) : g.Feature[c] ? Theme.Hex(0x5FC9EA) : g.Street[c] ? Theme.Hex(0xCDB27C)
                        : _mode != Mode.Select ? Theme.Hex(0x2F4A38) : Theme.Hex(0x1C2C22);
                    var btn = Ui.Button(grid, "", () => OnCell(cx, cy), col, -1, -1, true);
                    Place(btn.GetComponent<RectTransform>(), x, y, 1, 1, cell, 1);
                }

            foreach (var it in g.Items)
            {
                var b = it.Building;
                var color = Theme.RarityColor(it.Def.Rarity);
                if (it.Uid == _selected) color = Color.Lerp(color, Color.white, 0.45f);
                else if (b.TimerId != null || !CitySystem.IsActive(State, Content, b)) color = Color.Lerp(color, Theme.Background, 0.5f);
                string name = it.W * it.H >= 4 ? it.Def.Name : Initials(it.Def.Name);
                string label = name + "\n" + (b.Level == 0 ? "build…" : "Lv " + b.Level) + (b.Stored >= 1 ? "\n+" + Fmt.Num(b.Stored) : "")
                               + (CitySystem.ChancesReady(b) >= 1 ? "\n●" + CitySystem.ChancesReady(b) : "") + (CitySystem.IsBuilt(b) && !CitySystem.IsActive(State, Content, b) ? "\nZz" : "");
                string uid = it.Uid;
                var btn = Ui.Button(grid, label, () => { _selected = uid; _mode = Mode.Select; Rebuild(); }, color, -1, -1, true, 16);
                btn.name = "Building:" + uid;
                Place(btn.GetComponent<RectTransform>(), it.X, it.Y, it.W, it.H, cell, 2);
            }
        }

        private static string Initials(string name)
        {
            var parts = name.Split(' ');
            string r = "";
            foreach (var p in parts) if (p.Length > 0) r += char.ToUpperInvariant(p[0]);
            return r;
        }

        private static void Place(RectTransform rt, int x, int y, int w, int h, float cell, float inset)
        {
            Object.DestroyImmediate(rt.GetComponent<UnityEngine.UI.LayoutElement>());
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x * cell + inset, -y * cell - inset);
            rt.sizeDelta = new Vector2(w * cell - inset * 2, h * cell - inset * 2);
        }

        private void OnCell(int x, int y)
        {
            int cell = CityLayout.Cell(Content, x, y);
            switch (_mode)
            {
                case Mode.Place:
                {
                    var check = CityLayout.Validate(State, Content, CityEdit.Place(_pendingBp, x, y, _rotated, _pendingUid));
                    if (!check.Ok) { Refuse(check.Error); return; }
                    Run(new CityPlace { BlueprintId = _pendingBp, Uid = _pendingUid, X = x, Y = y, Rotated = _rotated },
                        r => { if (r.Ok) { _mode = Mode.Select; _selected = r.Payload as string; _pendingBp = _pendingUid = null; Rebuild(); } });
                    break;
                }
                case Mode.Move:
                    Run(new CityMove { Uid = _pendingUid, X = x, Y = y, Rotated = _rotated }, r => { if (r.Ok) { _mode = Mode.Select; _pendingUid = null; Rebuild(); } });
                    break;
                case Mode.Streets:
                {
                    var cmd = new CityStreets();
                    if (State.City.Streets.Contains(cell)) cmd.Removes.Add(cell); else cmd.Adds.Add(cell);
                    Run(cmd);
                    break;
                }
                default:
                    if (_selected != null) { _selected = null; Rebuild(); }
                    break;
            }
        }

        private void Refuse(string reason) => Modal("Can't place here", b => Note(b, reason), new ModalButton("OK", null));

        private void DigModal(CityPatchDef patch)
        {
            string block = CitySystem.DigBlockReason(State, Content, patch.Id);
            int n = CitySystem.NextDigNumber(State);
            var dig = CitySystem.DigDef(Content, n);
            var cost = CitySystem.DigCost(Content, n);
            bool free = State.City.FreeDigs > 0;
            bool can = block == null && (free || State.Wallet.CanAfford(cost));
            var buttons = new List<ModalButton> { new ModalButton("Close", null) };
            if (can) buttons.Add(new ModalButton("Dig", () => Run(new CityDig { PatchId = patch.Id }), Theme.Accent));
            Modal(patch.Name, b =>
            {
                Note(b, patch.Cells.Count + " tiles" + (patch.Features.Count > 0 ? " · " + patch.Features.Count + " terrain feature(s)" : ""));
                if (dig != null) Note(b, "Dig #" + n + ": " + (free ? "free" : Fmt.Costs(cost, Content)) + " · " + (free ? "instant" : Fmt.Duration(dig.Ms)));
                if (block != null) Note(b, block);
                else if (!can) Note(b, "Not enough resources.");
            }, buttons.ToArray());
        }

        // ---- selected building ------------------------------------------------------------------

        private void Details(RectTransform body)
        {
            var b = CitySystem.Building(State, _selected);
            if (b == null || !b.Placed) { Note(body, "Tap a building for details."); return; }
            var def = Content.Blueprint(b.BlueprintId);
            int stars = CitySystem.Stars(State, def.Id);
            var card = Card(body, Color.Lerp(Theme.RarityColor(def.Rarity), Theme.Panel, 0.8f));
            var head = Ui.Row(card, 60);
            Ui.Label(head, def.Name + "  " + Fmt.Stars(stars), Theme.FontLarge, Color.Lerp(Theme.RarityColor(def.Rarity), Color.white, 0.4f));
            Ui.Label(head, def.Rarity + " " + def.Category, Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight);
            if (!string.IsNullOrEmpty(def.Description)) Note(card, def.Description);
            KeyValue(card, "Level", b.Level + " / " + CitySystem.MaxLevel(Content));
            if (def.Placement != null)
            {
                double pct = CityLayout.PlacementPct(State, Content, b.Uid), flat = CityLayout.PlacementFlat(State, Content, b.Uid);
                Note(card, def.Placement.Text + (pct > 0 || flat > 0 ? "  Active: +" + (flat > 0 ? Fmt.Num(flat) : Fmt.Pct(pct)) : "  Not met.")).name = "PlacementSkill";
            }
            if (CitySystem.IsBuilt(b) && !CitySystem.IsActive(State, Content, b)) Note(card, "Zz: no alien works here, so it does nothing.").name = "Unstaffed";

            if (def.ProduceCurrency != null && CitySystem.IsBuilt(b))
            {
                double rate = CitySystem.ProductionPerHour(State, Content, b, Now);
                double cap = CitySystem.StorageCap(State, Content, b);
                KeyValue(card, "Production", Fmt.Num(rate) + " " + Fmt.CurrencyName(def.ProduceCurrency) + "/h");
                Ui.Bar(card, cap > 0 ? (float)(b.Stored / cap) : 0, Theme.CurrencyColor(def.ProduceCurrency), "Stored " + Fmt.Num(b.Stored) + " / " + Fmt.Num(cap));
                Ui.Button(card, "Collect", () => Run(new CityCollect { Uid = b.Uid }), Theme.Good, -1, Theme.ButtonHeight, b.Stored >= 1);
            }
            if (def.Pull != null && CitySystem.IsBuilt(b))
            {
                KeyValue(card, def.Pull.ChanceName + "s", CitySystem.ChancesReady(b) + " / " + CitySystem.ChanceStorage(Content, def, b.Level)
                    + " · " + Fmt.OneDecimal(CitySystem.ChancesPerDay(State, Content, b)) + " a day · up to " + CitySystem.MaxPullsPerPop(Content, stars) + " pulls each");
                Ui.Button(card, "Pop one", () => Run(new CityPop { Uid = b.Uid }), Theme.Purple, -1, 80, CitySystem.ChancesReady(b) >= 1, 26).name = "PopOne";
            }
            foreach (var boon in def.Boons)
                Note(card, Fmt.Humanize(boon.Kind.ToString()) + ": " + Fmt.OneDecimal(CitySystem.BoonValue(State, Content, b, boon.Kind)));

            if (b.TimerId != null) TimerControls(card, b.TimerId, b.Level == 0 ? "Construction" : "Upgrade");
            var row = Ui.Row(card, 90, 8);
            bool canUp = CitySystem.IsBuilt(b) && b.TimerId == null && b.Level < CitySystem.MaxLevel(Content);
            var cost = canUp ? CitySystem.UpgradeCost(Content, def, b.Level) : null;
            Ui.Button(row, canUp ? "Upgrade\n<size=20>" + Fmt.Costs(cost, Content) + " · " + Fmt.Duration(CitySystem.UpgradeDurationMs(State, Content, def, b.Level, Now)) + "</size>"
                    : b.Level >= CitySystem.MaxLevel(Content) ? "Max level" : "Upgrade",
                () => Run(new CityUpgrade { Uid = b.Uid }), Theme.Accent, -1, 90, canUp && State.Wallet.CanAfford(cost), 24);
            Ui.Button(row, "Move", () => { _mode = Mode.Move; _pendingBp = def.Id; _pendingUid = b.Uid; _rotated = b.Rotated; Rebuild(); }, Theme.Button, 160, 90, true, 26);
            bool cancel = b.Level == 0;
            Ui.Button(row, cancel ? "Cancel" : "Store", () => Run(new CityStore { Uid = b.Uid }, r => { if (r.Ok) { _selected = null; Rebuild(); } }),
                Theme.Bad, 160, 90, cancel || b.TimerId == null, 26);

            if (def.Id == ContentDb.ShardReactorId)
                Ui.Button(card, "Open the Shard Reactor", () => App.Router.Push(new ReactorScreen()), Theme.Purple, -1, 80, CitySystem.IsActive(State, Content, b), 26).name = "OpenReactor";
            long shards = State.Wallet.Get(Currencies.BlueprintShards(def.Id));
            var owned = CitySystem.Owned(State, def.Id);
            long need = CitySystem.StarCost(Content, owned);
            Ui.Button(card, "Star up (" + shards + "/" + need + " shards)", () => Run(new BlueprintStarUp { BlueprintId = def.Id }), Theme.Warn, -1, 80, owned.Stars < CitySystem.MaxStars && shards >= need, 26);

            int slots = CitySystem.WorkerSlots(State, Content, b);
            var workers = CitySystem.Workers(State, b.Uid);
            Note(card, (def.Id == ContentDb.LaunchPodId ? "Squad " : "Workers ") + workers.Count + "/" + slots + ": "
                       + (workers.Count == 0 ? "none" : string.Join(", ", workers.ConvertAll(a => Content.AlienType(a.TypeId)?.Name ?? a.TypeId)))
                       + (def.ProduceCurrency != null ? " · bonus +" + Fmt.Pct(CitySystem.WorkerBonusPct(State, Content, b)) : ""));
            Ui.Button(card, "Manage aliens", () => App.Open("aliens"), Theme.Button, -1, 76);
        }

        // ---- tray: new copies and stored buildings -----------------------------------------------

        private void Tray(RectTransform body)
        {
            Section(body, "Build");
            int n = 0;
            foreach (var ob in CitySystem.SortedBlueprints(State))
            {
                var def = Content.Blueprint(ob.Id);
                if (def == null) continue;
                foreach (var stored in CitySystem.Copies(State, def.Id))
                {
                    if (stored.Placed) continue;
                    var s = stored;
                    n++;
                    var row = Ui.Row(body, 96, 10, Color.Lerp(Theme.RarityColor(def.Rarity), Theme.Panel, 0.8f), 12);
                    Ui.Label(row, def.Name + " " + Fmt.Stars(ob.Stars) + "  (" + def.Width + "×" + def.Height + ")\n<size=22>stored · Lv " + s.Level + "</size>", Theme.FontSmall);
                    Ui.Button(row, "Place", () => { _mode = Mode.Place; _pendingBp = def.Id; _pendingUid = s.Uid; _rotated = false; _selected = null; Rebuild(); }, Theme.Accent, 180, 80);
                }
                int built = CitySystem.CopiesBuilt(State, def.Id);
                if (built >= def.Copies) continue;
                n++;
                var r2 = Ui.Row(body, 96, 10, Color.Lerp(Theme.RarityColor(def.Rarity), Theme.Panel, 0.8f), 12);
                var cost = CitySystem.BuildCost(Content, def);
                Ui.Label(r2, def.Name + " " + Fmt.Stars(ob.Stars) + "  (" + def.Width + "×" + def.Height + ") · " + built + "/" + def.Copies + " built\n<size=22>new · "
                             + Fmt.Costs(cost, Content) + " · " + Fmt.Duration(CitySystem.BuildDurationMs(State, Content, def, Now)) + "</size>", Theme.FontSmall);
                Ui.Button(r2, "Build", () => { _mode = Mode.Place; _pendingBp = def.Id; _pendingUid = null; _rotated = false; _selected = null; Rebuild(); }, Theme.Accent, 180, 80,
                    State.Wallet.CanAfford(cost));
            }
            if (n == 0) Note(body, State.City.Blueprints.Count == 0 ? "No blueprints yet: pull them in Blueprint Signals." : "Every copy is built and placed.");
        }
    }
}
