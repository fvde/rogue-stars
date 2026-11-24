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
    /// Shard Reactor (docs/design/pull-system.md): job slots (one per working alien), running jobs with timers / speed-ups / Collect, and
    /// a new job: pick alien or blueprint shards as the source (spare shards beyond 10★ shown), a target of the same type and a higher
    /// rarity, the units; the cost (source shards + Grain / Ore), the time and the output are shown before Start.
    /// </summary>
    public sealed class ReactorScreen : UiScreen
    {
        private ReactorKind _kind;
        private string _source, _target;
        private int _units = 1;

        public ReactorScreen() { }

        public ReactorScreen(ReactorKind kind, string source = null, string target = null, int units = 1)
        {
            _kind = kind; _source = source; _target = target; _units = Mathf.Max(1, units);
        }

        public override string Title => "Shard Reactor";

        public const string Intro = "Fuse spare shards into shards of a rarer alien or blueprint of your choice. One job per alien working here.";

        protected override void Build(RectTransform body)
        {
            Note(body, Intro).name = "Intro";
            var b = ShardReactorSystem.Building(State, Content);
            if (b == null) { Note(body, "Win the Shard Reactor (Mythic blueprint) in the Blueprint Signal to build it.").name = "NotOwned"; return; }
            if (!CitySystem.IsActive(b)) { Note(body, "Place and finish building the Shard Reactor in the City first.").name = "NotBuilt"; return; }

            int slots = ShardReactorSystem.JobSlots(State, Content), workers = ShardReactorSystem.WorkerSlots(State, Content);
            var head = Ui.Row(body, 56, 10, null, 0, "Slots");
            Ui.Label(head, "Jobs " + State.Reactor.Jobs.Count + "/" + slots, Theme.FontLarge, Theme.Text).name = "JobCount";
            Ui.Label(head, "Worker slots " + workers + " · " + Fmt.Stars(b.Stars), Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight);
            Note(body, "Workers: " + CitySystem.Workers(State, b.Id).Count + "/" + workers + " (+1 slot at 3★ and at 6★) · Grain "
                       + W(State.Wallet.Get(Currencies.Grain)) + " · Ore " + W(State.Wallet.Get(Currencies.Ore)));
            if (slots == 0) Ui.Button(body, "Assign an alien to work here", () => App.Open("aliens"), Theme.Accent, -1, 76).name = "ManageAliens";

            Jobs(body);
            NewJob(body, slots);
        }

        // ---- running jobs --------------------------------------------------------------------------------------------

        private void Jobs(RectTransform body)
        {
            if (State.Reactor.Jobs.Count == 0) return;
            Section(body, "Running jobs");
            foreach (var j in State.Reactor.Jobs)
            {
                var job = j;
                var card = Card(body, Color.Lerp(RarityColor(job.Kind, job.TargetId), Theme.Panel, 0.8f));
                card.name = "Job:" + job.Id;
                Ui.Label(card, W(job.SourceShards) + " " + ItemName(job.Kind, job.SourceId) + " shards  →  " + W(job.OutputShards) + " " + ItemName(job.Kind, job.TargetId) + " shards",
                    Theme.FontNormal, Theme.Text);
                bool done = ShardReactorSystem.IsDone(State, job, Now);
                if (!done) TimerControls(card, job.TimerId, "Fusing");
                Ui.Button(card, "Collect", () => Run(new ReactorCollect { JobId = job.Id }), Theme.Good, -1, Theme.ButtonHeight, done).name = "Collect";
            }
        }

        // ---- new job -------------------------------------------------------------------------------------------------

        private void NewJob(RectTransform body, int slots)
        {
            Section(body, "New job");
            Ui.TabBar(body, new List<string> { "Alien shards", "Blueprint shards" }, (int)_kind, i => { _kind = (ReactorKind)i; _source = null; _target = null; Rebuild(); });

            // Sources: every item of the kind with shards, spare (beyond 10★) first.
            var sources = new List<string>();
            foreach (var id in ShardReactorSystem.Items(Content, _kind))
                if (State.Wallet.Get(ShardReactorSystem.ShardCurrency(_kind, id)) > 0 && ShardReactorSystem.Targets(State, Content, _kind, id).Count > 0) sources.Add(id);
            if (sources.Count == 0) { Note(body, "No shards to fuse yet: spare shards come from pulls of items you have maxed.").name = "NoSources"; return; }
            if (_source != null && !sources.Contains(_source)) _source = null;
            Note(body, "Source shards (spare beyond 10★ in brackets)");
            var grid = Ui.Grid(body, new Vector2(300, 84), 3, 8);
            grid.name = "Sources";
            foreach (var id in sources)
            {
                string src = id;
                long have = State.Wallet.Get(ShardReactorSystem.ShardCurrency(_kind, src)), spare = ShardReactorSystem.ExcessShards(State, Content, _kind, src);
                Ui.Button(grid, ItemName(_kind, src) + "\n<size=20>" + W(have) + " (" + W(spare) + ")</size>", () => { _source = src; _target = null; Rebuild(); },
                    src == _source ? Theme.Accent : Color.Lerp(RarityColor(_kind, src), Theme.Panel, 0.55f), -1, 84, true, 22).name = "Source:" + src;
            }
            if (_source == null) { Note(body, "Pick the shards to fuse."); return; }

            var targets = ShardReactorSystem.Targets(State, Content, _kind, _source);
            if (_target != null && !targets.Contains(_target)) _target = null;
            Note(body, "Target (same type, a higher rarity)");
            var tg = Ui.Grid(body, new Vector2(300, 84), 3, 8);
            tg.name = "Targets";
            foreach (var id in targets)
            {
                string tgt = id;
                int stars = ShardReactorSystem.StarsOf(State, _kind, tgt);
                Ui.Button(tg, ItemName(_kind, tgt) + "\n<size=20>" + ShardReactorSystem.RarityOf(Content, _kind, tgt) + (stars > 0 ? " · " + stars + "★" : " · not owned") + "</size>",
                    () => { _target = tgt; Rebuild(); },
                    tgt == _target ? Theme.Accent : Color.Lerp(RarityColor(_kind, tgt), Theme.Panel, 0.4f), -1, 84, true, 22).name = "Target:" + tgt;
            }
            if (_target == null) { Note(body, "Pick the item to fuse into."); return; }

            var units = Ui.Row(body, 80, 10, null, 0, "Units");
            Ui.Button(units, "−", () => { _units = Mathf.Max(1, _units - 1); Rebuild(); }, Theme.Button, 100, 80, _units > 1, 36);
            Ui.Label(units, _units + (_units == 1 ? " unit" : " units"), Theme.FontLarge, Theme.Text, TextAnchor.MiddleCenter).name = "UnitCount";
            Ui.Button(units, "+", () => { _units = Mathf.Min(Content.ShardReactor.MaxUnits, _units + 1); Rebuild(); }, Theme.Button, 100, 80, _units < Content.ShardReactor.MaxUnits, 36);

            var q = ShardReactorSystem.Quote(State, Content, _kind, _source, _target, _units);
            if (q.Error != null) { Note(body, Fmt.Humanize(q.Error)); return; }
            long haveShards = State.Wallet.Get(ShardReactorSystem.ShardCurrency(_kind, _source));
            bool shardsOk = haveShards >= q.SourceShards, fuelOk = State.Wallet.CanAfford(q.FuelCurrency, q.Fuel);
            var cost = Card(body, Theme.PanelAlt);
            cost.name = "Quote";
            KeyValue(cost, "Uses", W(q.SourceShards) + " " + ItemName(_kind, _source) + " shards (have " + W(haveShards) + ")", shardsOk ? Theme.Text : Theme.Bad);
            KeyValue(cost, "Fuel", W(q.Fuel) + " " + Fmt.CurrencyName(q.FuelCurrency), fuelOk ? Theme.Text : Theme.Bad);
            KeyValue(cost, "Time", Fmt.Duration(q.DurationMs));
            KeyValue(cost, "Makes", W(q.OutputShards) + " " + ItemName(_kind, _target) + " shards", RarityColor(_kind, _target));
            bool free = State.Reactor.Jobs.Count < slots;
            Ui.Button(body, free ? "Start fusing" : "All jobs busy", () => Run(new ReactorStart { Kind = _kind, SourceId = _source, TargetId = _target, Units = _units }, r => { if (r.Ok) _units = 1; }),
                Theme.Accent, -1, Theme.ButtonHeight, free && shardsOk && fuelOk).name = "Start";
        }

        /// <summary>Whole numbers in full ("61,400"): costs and balances on this screen are exact.</summary>
        private static string W(long v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        private string ItemName(ReactorKind kind, string id) =>
            kind == ReactorKind.Alien ? Content.AlienType(id)?.Name ?? id : Content.Blueprint(id)?.Name ?? id;

        private Color RarityColor(ReactorKind kind, string id) => Theme.RarityColor(ShardReactorSystem.RarityOf(Content, kind, id) ?? Rarity.Common);
    }
}
