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
    /// Breeding Pen (aliens pass): Grain's only use and the targeted shard source. Pick two parents; the baby is randomly one of their
    /// two species; keep it (its traits replace the current alien's, stars stay) or release it for shards of its species.
    /// Genes are bred but inactive ("coming soon").
    /// </summary>
    public sealed class BreedingScreen : UiScreen
    {
        private string _a, _b;

        public BreedingScreen() { }

        /// <summary>Opened from an alien's card: that alien is parent A (breed it for its shards); parent B optional.</summary>
        public BreedingScreen(string parentA, string parentB = null) { _a = parentA; _b = parentB != parentA ? parentB : null; }

        public override string Title => "Breeding Pen";
        public override string[] IntroFeatures => new[] { Features.Breeding };

        /// <summary>The pen's purpose in one line (Grain's only sink).</summary>
        public const string Intro = "Grain is spent only here. Pick two aliens: the baby is one of their two species (50/50). Keep it for its traits, or release it for shards of its species.";

        protected override void Build(RectTransform body)
        {
            int slots = BreedingSystem.Slots(State, Content);
            var jobs = State.Breeding.Jobs;
            Note(body, Intro).name = "Intro";
            Note(body, "Slots: " + jobs.Count + "/" + slots + (slots == 0 ? " — build a Breeding Pen in the City" : "") + " · Grain " + Fmt.Num(State.Wallet.Get(Currencies.Grain)));
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
                Ui.Label(row, "Baby " + Name(child.TypeId) + (child.Mutated ? " (mutated!)" : ""), Theme.FontNormal, child.Mutated ? Theme.Warn : Theme.Text);
                Ui.Button(row, "Decide", () => Decide(child), Theme.Accent, 200, 80);
            }
        }

        private void Decide(PendingChild child)
        {
            var current = AlienSystem.Owned(State, child.TypeId);
            Modal("Keep the baby " + Name(child.TypeId) + "?", b =>
            {
                Ui.Label(b, "Traits (baby vs current):", Theme.FontNormal);
                KeyValue(b, "Baby", Traits(child.Traits));
                KeyValue(b, "Current", current != null ? Traits(current.Traits) : "—");
                Note(b, AliensScreen.GenesLine);
                Note(b, "Keep: the baby's traits replace the current " + Name(child.TypeId) + "'s; its stars stay. Release: " + ReleaseText(Content, child) + ".");
            },
            new ModalButton("Keep", () => Run(new BreedResolve { ChildId = child.Id, Keep = BreedKeep.Replace }), Theme.Good),
            new ModalButton("Release", () => Run(new BreedResolve { ChildId = child.Id, Keep = BreedKeep.Discard }), Theme.Warn),
            new ModalButton("Later", null));
        }

        /// <summary>"+2 Thornqueen shards and 360 Grain back" (what releasing a baby gives).</summary>
        public static string ReleaseText(ContentDb content, PendingChild child)
        {
            var bt = content.BreedingTuning;
            var def = content.AlienType(child.TypeId);
            long shards = def != null ? bt.DiscardShards[(int)def.Rarity] : 0;
            long grain = (long)System.Math.Floor(BreedingSystem.GrainCost(content, child.ParentA, child.ParentB) * bt.DiscardGrainRefundPct / 100.0);
            return "+" + shards + " " + (def?.Name ?? child.TypeId) + " shard" + (shards == 1 ? "" : "s") + " and " + Fmt.Num(grain) + " Grain back";
        }

        private string Traits(List<string> ids) =>
            ids.Count == 0 ? "none" : string.Join(", ", ids.ConvertAll(t => Content.AlienTrait(t)?.Name ?? t));

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
            if (_a != null && !owned.Contains(_a)) _a = null;
            ParentRow(body, "Parent A", owned, _a, id => { _a = id; if (_b == id) _b = null; Rebuild(); });
            ParentRow(body, "Parent B", owned, _b, id => { _b = id; if (_a == id) _a = null; Rebuild(); });
            if (_a == null || _b == null) { Note(body, "Pick two parents."); return; }
            long grain = BreedingSystem.GrainCost(Content, _a, _b);
            long ms = BreedingSystem.DurationMs(State, Content, _a, _b, Now);
            KeyValue(body, "Baby", OddsText(Content, _a, _b));
            KeyValue(body, "Cost", Fmt.Num(grain) + " Grain", State.Wallet.CanAfford(Currencies.Grain, grain) ? Theme.Text : Theme.Bad);
            KeyValue(body, "Duration", Fmt.Duration(ms));
            bool can = slots > State.Breeding.Jobs.Count && State.Wallet.CanAfford(Currencies.Grain, grain);
            Ui.Button(body, "Start breeding", () => Run(new BreedStart { ParentA = _a, ParentB = _b }, r => { if (r.Ok) { _b = null; } }), Theme.Accent, -1, Theme.ButtonHeight, can);
        }

        /// <summary>"50% Blorp (1 shard if released) · 50% Solarch (2 shards)".</summary>
        public static string OddsText(ContentDb content, string a, string b)
        {
            string One(string id)
            {
                var def = content.AlienType(id);
                long shards = def != null ? content.BreedingTuning.DiscardShards[(int)def.Rarity] : 0;
                return "50% " + (def?.Name ?? id) + " (" + shards + (shards == 1 ? " shard" : " shards") + " if released)";
            }
            return One(a) + " · " + One(b);
        }

        private void ParentRow(RectTransform body, string label, List<string> ids, string selected, System.Action<string> pick)
        {
            Note(body, label);
            var grid = Ui.Grid(body, new Vector2(320, 80), 3, 8);
            foreach (var id in ids)
            {
                string alien = id;
                var def = Content.AlienType(alien);
                var o = AlienSystem.Owned(State, alien);
                Ui.Button(grid, (def?.Name ?? alien) + (o != null ? " " + o.Stars + "★" : ""), () => pick(alien),
                    alien == selected ? Theme.Accent : Color.Lerp(Theme.RarityColor(def?.Rarity ?? Rarity.Common), Theme.Panel, 0.6f), -1, 80, true, 24);
            }
        }

        private string Name(string typeId) => Content.AlienType(typeId)?.Name ?? typeId;
    }
}
