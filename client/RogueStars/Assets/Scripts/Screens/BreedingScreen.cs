using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>Breeding Pen: pick two parents, run timers, collect, then replace or discard the child.</summary>
    public sealed class BreedingScreen : UiScreen
    {
        private string _a, _b;

        public override string Title => "Breeding Pen";

        protected override void Build(RectTransform body)
        {
            int slots = BreedingSystem.Slots(State, Content);
            var jobs = State.Breeding.Jobs;
            Note(body, "Slots: " + jobs.Count + "/" + slots + (slots == 0 ? " — build a Breeding Pen in the City" : "") + " · quality bonus +" + Fmt.Pct(BreedingSystem.QualityBonus(State, Content)));
            Pending(body);
            Jobs(body);
            Picker(body, slots);
        }

        private void Pending(RectTransform body)
        {
            if (State.Breeding.Pending.Count == 0) return;
            Section(body, "Newborns waiting");
            foreach (var c in State.Breeding.Pending)
            {
                var child = c;
                var row = Ui.Row(body, 96, 10, Theme.Header, 12);
                Ui.Label(row, "Baby " + (Content.AlienType(child.TypeId)?.Name ?? child.TypeId) + (child.Mutated ? " (mutated!)" : ""), Theme.FontNormal, child.Mutated ? Theme.Warn : Theme.Text);
                Ui.Button(row, "Decide", () => Decide(child), Theme.Accent, 200, 80);
            }
        }

        private void Decide(PendingChild child)
        {
            var current = AlienSystem.Owned(State, child.TypeId);
            Modal("Keep the baby " + (Content.AlienType(child.TypeId)?.Name ?? child.TypeId) + "?", b =>
            {
                Ui.Label(b, "Genes (child vs current):", Theme.FontNormal);
                GeneRow(b, "HP (power)", child.Genes.Hp, current?.Genes.Hp ?? 0);
                GeneRow(b, "ATK (power)", child.Genes.Atk, current?.Genes.Atk ?? 0);
                GeneRow(b, "DEF (power)", child.Genes.Def, current?.Genes.Def ?? 0);
                GeneRow(b, "Speed (acts more)", child.Genes.Speed, current?.Genes.Speed ?? 0);
                GeneRow(b, "Work", child.Genes.Work, current?.Genes.Work ?? 0);
                Note(b, "Traits: " + (child.Traits.Count == 0 ? "none" : string.Join(", ", child.Traits.ConvertAll(t => Content.AlienTrait(t)?.Name ?? t))));
                Note(b, "Replace keeps level and stars of the current alien; discard converts the baby into shards and some Grain.");
            },
            new ModalButton("Replace", () => Run(new BreedResolve { ChildId = child.Id, Keep = BreedKeep.Replace }), Theme.Good),
            new ModalButton("Discard", () => Run(new BreedResolve { ChildId = child.Id, Keep = BreedKeep.Discard }), Theme.Bad),
            new ModalButton("Later", null));
        }

        private static void GeneRow(Transform parent, string name, double child, double current)
        {
            var color = child > current ? Theme.Good : child < current ? Theme.Bad : Theme.Text;
            KeyValue(parent, name, child.ToString("+0.#;-0.#;0") + "%  vs  " + current.ToString("+0.#;-0.#;0") + "%", color);
        }

        private void Jobs(RectTransform body)
        {
            if (State.Breeding.Jobs.Count == 0) return;
            Section(body, "Breeding");
            foreach (var j in State.Breeding.Jobs)
            {
                var job = j;
                var card = Card(body);
                Ui.Label(card, Name(job.ParentA) + " + " + Name(job.ParentB), Theme.FontNormal);
                bool done = TimerSystem.Get(State, job.TimerId) == null || TimerSystem.IsDone(State, job.TimerId, Now);
                if (!done) TimerControls(card, job.TimerId, "Hatching");
                Ui.Button(card, "Collect", () => Run(new BreedCollect { JobId = job.Id }), Theme.Good, -1, Theme.ButtonHeight, done);
            }
        }

        private void Picker(RectTransform body, int slots)
        {
            Section(body, "New pairing");
            var owned = new List<string>(State.Aliens.Owned.Keys);
            owned.Sort(string.CompareOrdinal);
            if (owned.Count < 2) { Note(body, "You need at least two different aliens."); return; }
            ParentRow(body, "Parent A", owned, _a, id => { _a = id; if (_b == id) _b = null; Rebuild(); });
            ParentRow(body, "Parent B", owned, _b, id => { _b = id; if (_a == id) _a = null; Rebuild(); });
            if (_a == null || _b == null) { Note(body, "Pick two parents."); return; }
            long grain = BreedingSystem.GrainCost(Content, _a, _b);
            long ms = BreedingSystem.DurationMs(State, Content, _a, _b, Now);
            KeyValue(body, "Cost", Fmt.Num(grain) + " Grain", State.Wallet.CanAfford(Currencies.Grain, grain) ? Theme.Text : Theme.Bad);
            KeyValue(body, "Duration", Fmt.Duration(ms));
            bool can = slots > State.Breeding.Jobs.Count && State.Wallet.CanAfford(Currencies.Grain, grain);
            Ui.Button(body, "Start breeding", () => Run(new BreedStart { ParentA = _a, ParentB = _b }, r => { if (r.Ok) { _a = _b = null; } }), Theme.Accent, -1, Theme.ButtonHeight, can);
        }

        private void ParentRow(RectTransform body, string label, List<string> ids, string selected, System.Action<string> pick)
        {
            Note(body, label);
            var grid = Ui.Grid(body, new Vector2(240, 80), 4, 8);
            foreach (var id in ids)
            {
                string alien = id;
                var def = Content.AlienType(alien);
                Ui.Button(grid, def?.Name ?? alien, () => pick(alien), alien == selected ? Theme.Accent : Color.Lerp(Theme.RarityColor(def?.Rarity ?? Rarity.Common), Theme.Panel, 0.6f), -1, 80, true, 24);
            }
        }

        private string Name(string typeId) => Content.AlienType(typeId)?.Name ?? typeId;
    }
}
