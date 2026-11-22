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
    /// City grid: areas, placed buildings, place/move/demolish from owned blueprints, upgrades with timers and
    /// speed-ups, production collection, production priority, blueprint stars and workers.
    /// </summary>
    public sealed class CityScreen : UiScreen
    {
        private enum Mode { Select, Place, Move }

        private const float GridPixels = 1000;
        private Mode _mode;
        private string _selected;   // selected building (blueprint id)
        private string _pending;    // blueprint being placed/moved

        public override string Title => "City";

        protected override void Build(RectTransform body)
        {
            Toolbar(body);
            if (_mode != Mode.Select)
            {
                var banner = Ui.Row(body, 90, 10, Theme.Warn, 12);
                Ui.Label(banner, (_mode == Mode.Place ? "Place " : "Move ") + (Content.Blueprint(_pending)?.Name ?? _pending) + ": tap a free cell (top-left corner)", Theme.FontSmall, Color.black);
                Ui.Button(banner, "Cancel", () => { _mode = Mode.Select; _pending = null; Rebuild(); }, Theme.Button, 180, 76);
            }
            GridView(body);
            Details(body);
            Storage(body);
        }

        // ---- toolbar ----------------------------------------------------------------------------

        private void Toolbar(RectTransform body)
        {
            var row = Ui.Row(body);
            Ui.Button(row, "Collect all", () => Run(new CityCollect()), Theme.Good);
            Ui.Button(row, "Priority: " + (State.City.PriorityCurrency == null ? "none" : Fmt.CurrencyName(State.City.PriorityCurrency)), PriorityModal, Theme.Button);

            var next = CitySystem.NextArea(State, Content);
            if (next != null)
            {
                bool reqOk = next.RequiresCounter == null || State.Counter(next.RequiresCounter) >= next.RequiresCounterValue;
                string req = next.RequiresCounter == null ? "" : " · needs " + Fmt.Humanize(next.RequiresCounter) + " " + State.Counter(next.RequiresCounter) + "/" + next.RequiresCounterValue;
                var r2 = Ui.Row(body, 90, 10, Theme.Panel, 12);
                Ui.Label(r2, "Next area: " + next.Name + "\n<size=22>" + Fmt.Costs(next.UnlockCost, Content) + req + "</size>", Theme.FontSmall);
                Ui.Button(r2, "Unlock", () => Run(new CityUnlockArea { AreaId = next.Id }), Theme.Accent, 200, 76, reqOk && State.Wallet.CanAfford(next.UnlockCost));
            }
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
            int w = Content.CityTuning.GridWidth, h = Content.CityTuning.GridHeight;
            float cell = GridPixels / Mathf.Max(w, h);
            var holder = Ui.Rect(body, "GridHolder");
            Ui.Size(holder, -1, cell * h + 8);
            var grid = Ui.Panel(holder, Theme.Background, "Grid").rectTransform;
            grid.anchorMin = grid.anchorMax = new Vector2(0.5f, 1);
            grid.pivot = new Vector2(0.5f, 1);
            grid.sizeDelta = new Vector2(cell * w, cell * h);
            grid.anchoredPosition = Vector2.zero;

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int cx = x, cy = y;
                    var area = CitySystem.AreaAt(Content, x, y);
                    bool unlocked = area != null && CitySystem.IsAreaUnlocked(State, area.Id);
                    if (area == null) continue;
                    if (!unlocked)
                    {
                        // locked area: plain tile (no button) so it stays readable
                        var locked = Ui.Panel(grid, Theme.Hex(0x2A2A3C), "Locked");
                        locked.raycastTarget = false;
                        Place(locked.rectTransform, x, y, 1, 1, cell, 2);
                        continue;
                    }
                    var btn = Ui.Button(grid, "", () => OnCell(cx, cy), _mode != Mode.Select ? Theme.Hex(0x34345A) : Theme.PanelAlt, -1, -1, true);
                    Place(btn.GetComponent<RectTransform>(), x, y, 1, 1, cell, 2);
                }

            foreach (var a in Content.CityAreas)
            {
                if (CitySystem.IsAreaUnlocked(State, a.Id)) continue;
                var lab = Ui.Label(grid, a.Name + "\n<size=18>locked</size>", 20, Theme.TextDim, TextAnchor.MiddleCenter, "AreaLabel");
                Place(lab.rectTransform, a.X, a.Y, a.Width, a.Height, cell, 4);
            }

            foreach (var b in CitySystem.SortedBlueprints(State))
            {
                if (!b.Placed) continue;
                var def = Content.Blueprint(b.Id);
                if (def == null) continue;
                var owned = b;
                var color = Theme.RarityColor(def.Rarity);
                if (owned.Id == _selected) color = Color.Lerp(color, Color.white, 0.45f);
                else if (owned.TimerId != null) color = Color.Lerp(color, Theme.Background, 0.5f);
                string name = def.Width >= 2 ? def.Name : Initials(def.Name);
                string label = name + "\n" + (owned.Level == 0 ? "build…" : "Lv " + owned.Level) + (owned.Stored >= 1 ? "\n+" + Fmt.Num(owned.Stored) : "");
                var btn = Ui.Button(grid, label, () => { _selected = owned.Id; _mode = Mode.Select; Rebuild(); }, color, -1, -1, true, 20);
                btn.name = "Building:" + owned.Id;
                Place(btn.GetComponent<RectTransform>(), owned.X, owned.Y, def.Width, def.Height, cell, 3);
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
            if (_mode == Mode.Place)
            {
                string id = _pending;
                Run(new CityBuild { BlueprintId = id, X = x, Y = y }, r => { if (r.Ok) { _mode = Mode.Select; _selected = id; _pending = null; Rebuild(); } });
            }
            else if (_mode == Mode.Move)
            {
                string id = _pending;
                Run(new CityMove { BlueprintId = id, X = x, Y = y }, r => { if (r.Ok) { _mode = Mode.Select; _pending = null; Rebuild(); } });
            }
            else if (_selected != null) { _selected = null; Rebuild(); }
        }

        // ---- selected building ------------------------------------------------------------------

        private void Details(RectTransform body)
        {
            var b = _selected != null ? CitySystem.Owned(State, _selected) : null;
            if (b == null || !b.Placed) { Note(body, "Tap a building for details."); return; }
            var def = Content.Blueprint(b.Id);
            var card = Card(body, Color.Lerp(Theme.RarityColor(def.Rarity), Theme.Panel, 0.8f));
            var head = Ui.Row(card, 60);
            Ui.Label(head, def.Name + "  " + Fmt.Stars(b.Stars), Theme.FontLarge, Color.Lerp(Theme.RarityColor(def.Rarity), Color.white, 0.4f));
            Ui.Label(head, def.Rarity + " " + def.Category, Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight);
            if (!string.IsNullOrEmpty(def.Description)) Note(card, def.Description);
            KeyValue(card, "Level", b.Level + " / " + CitySystem.MaxLevel(Content, b));

            if (def.ProduceCurrency != null && CitySystem.IsActive(b))
            {
                double rate = CitySystem.ProductionPerHour(State, Content, b, Now);
                double cap = CitySystem.StorageCap(Content, b);
                KeyValue(card, "Production", Fmt.Num(rate) + " " + Fmt.CurrencyName(def.ProduceCurrency) + "/h");
                Ui.Bar(card, cap > 0 ? (float)(b.Stored / cap) : 0, Theme.CurrencyColor(def.ProduceCurrency), "Stored " + Fmt.Num(b.Stored) + " / " + Fmt.Num(cap));
                Ui.Button(card, "Collect", () => Run(new CityCollect { BlueprintId = b.Id }), Theme.Good, -1, Theme.ButtonHeight, b.Stored >= 1);
            }
            foreach (var boon in def.Boons)
                if (b.Level >= boon.MinLevel)
                    Note(card, Fmt.Humanize(boon.Kind.ToString()) + ": " + CitySystem.LevelBoonValue(Content, def, b, boon.Kind).ToString("0.##"));

            if (b.TimerId != null) TimerControls(card, b.TimerId, b.Level == 0 ? "Construction" : "Upgrade");
            else
            {
                var row = Ui.Row(card, 90, 8);
                bool canUp = CitySystem.IsActive(b) && b.Level < CitySystem.MaxLevel(Content, b);
                var cost = canUp ? CitySystem.UpgradeCost(Content, def, b.Level) : null;
                Ui.Button(row, canUp ? "Upgrade\n<size=20>" + Fmt.Costs(cost, Content) + " · " + Fmt.Duration(CitySystem.UpgradeDurationMs(State, Content, def, b.Level, Now)) + "</size>" : "Max level",
                    () => Run(new CityUpgrade { BlueprintId = b.Id }), Theme.Accent, -1, 90, canUp && State.Wallet.CanAfford(cost), 24);
                Ui.Button(row, "Move", () => { _mode = Mode.Move; _pending = b.Id; Rebuild(); }, Theme.Button, 160, 90, true, 26);
                Ui.Button(row, "Store", () => Modal("Store " + def.Name + "?", x => Note(x, "Removes it from the grid; level and stars are kept and workers are unassigned."),
                    new ModalButton("Cancel", null),
                    new ModalButton("Store", () => Run(new CityDemolish { BlueprintId = b.Id }, r => { if (r.Ok) { _selected = null; Rebuild(); } }), Theme.Bad)), Theme.Bad, 160, 90, true, 26);
            }

            long shards = State.Wallet.Get(Currencies.BlueprintShards(b.Id));
            long need = CitySystem.StarCost(Content, b);
            Ui.Button(card, "Star up (" + shards + "/" + need + " shards)", () => Run(new BlueprintStarUp { BlueprintId = b.Id }), Theme.Warn, -1, 80, b.Stars < CitySystem.MaxStars && shards >= need, 26);

            int slots = CitySystem.WorkerSlots(Content, def, b);
            if (slots > 0)
            {
                var workers = CitySystem.Workers(State, b.Id);
                Note(card, "Workers " + workers.Count + "/" + slots + ": " + (workers.Count == 0 ? "none" : string.Join(", ", workers.ConvertAll(a => Content.AlienType(a.TypeId)?.Name ?? a.TypeId)))
                           + " · bonus +" + Fmt.Pct(CitySystem.WorkerBonusPct(State, Content, b)));
                Ui.Button(card, "Manage aliens", () => App.Open("aliens"), Theme.Button, -1, 76);
            }
        }

        // ---- blueprint storage ------------------------------------------------------------------

        private void Storage(RectTransform body)
        {
            Section(body, "Blueprints in storage");
            int n = 0;
            foreach (var b in CitySystem.SortedBlueprints(State))
            {
                if (b.Placed) continue;
                var def = Content.Blueprint(b.Id);
                if (def == null) continue;
                var owned = b;
                n++;
                var row = Ui.Row(body, 96, 10, Color.Lerp(Theme.RarityColor(def.Rarity), Theme.Panel, 0.8f), 12);
                string info = b.Level == 0 ? "new · build " + Fmt.Costs(CitySystem.BuildCost(Content, def), Content) + " · " + Fmt.Duration(CitySystem.BuildDurationMs(State, Content, def, Now)) : "Lv " + b.Level;
                Ui.Label(row, def.Name + " " + Fmt.Stars(b.Stars) + "  (" + def.Width + "×" + def.Height + ")\n<size=22>" + info + "</size>", Theme.FontSmall);
                bool affordable = b.Level > 0 || State.Wallet.CanAfford(CitySystem.BuildCost(Content, def));
                Ui.Button(row, "Place", () => { _mode = Mode.Place; _pending = owned.Id; _selected = null; Rebuild(); }, Theme.Accent, 180, 80, affordable && b.TimerId == null);
            }
            if (n == 0) Note(body, State.City.Blueprints.Count == 0 ? "No blueprints yet (Blueprint Signals; offline: Settings → starter kit)." : "All blueprints are placed.");
        }
    }
}
