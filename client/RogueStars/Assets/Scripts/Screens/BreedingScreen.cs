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
    /// Genes are bred but inactive (not shown). Without a Breeding Pen: a locked state with "Go to City".
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
            Pending(body);
            Jobs(body);
            if (slots == 0) { NoPen(body); return; }
            Note(body, "Slots: " + jobs.Count + "/" + slots + " · Grain " + Fmt.Num(State.Wallet.Get(Currencies.Grain)));
            Picker(body, slots);
        }

        /// <summary>AL2: without a Breeding Pen the picker is not shown: a locked state with the way to build one.</summary>
        private void NoPen(RectTransform body)
        {
            var card = Ui.Card(body, Theme.PanelAlt, Theme.Border, Theme.RadiusCard, 28, 12, 4, "NoPen");
            var lockRt = Ui.Rect(card, "Lock");
            Ui.Size(lockRt, 96, 96, 0);
            var li = lockRt.gameObject.AddComponent<UnityEngine.UI.Image>();
            li.sprite = RogueStars.Client.World.SummonArt.Icon("lock"); li.preserveAspect = true; li.raycastTarget = false;
            Ui.Display(card, NoPenText, Theme.FontLarge, Theme.Text, TextAnchor.MiddleCenter, "NoPenText");
            Ui.Button(card, "Go to City", () => App.Open("city"), Theme.Accent, -1, Theme.ButtonHeight).name = "GoToCity";
        }

        public const string NoPenText = "Build a Breeding Pen in the City";

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
            bool free = slots > State.Breeding.Jobs.Count;
            bool afford = State.Wallet.CanAfford(Currencies.Grain, grain);
            string why = !free ? "All breeding slots are busy" : !afford ? "Needs " + Fmt.Num(grain - State.Wallet.Get(Currencies.Grain)) + " more Grain" : null;
            Ui.ButtonWithReason(body, "Start breeding", () => Run(new BreedStart { ParentA = _a, ParentB = _b }, r => { if (r.Ok) { _b = null; } }), Theme.Accent, -1, Theme.ButtonHeight, free && afford, why);
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
            // AL2: three flexible columns sized from the available width (no fixed cell clipped at the right edge)
            RectTransform grid = null;
            for (int i = 0; i < ids.Count; i++)
            {
                if (i % 3 == 0) grid = Ui.Row(body, 80, 8, null, 0, "ParentRow");
                string alien = ids[i];
                var def = Content.AlienType(alien);
                var o = AlienSystem.Owned(State, alien);
                Ui.Button(grid, (def?.Name ?? alien) + (o != null ? " " + o.Stars + "★" : ""), () => pick(alien),
                    alien == selected ? Theme.Accent : Color.Lerp(Theme.RarityColor(def?.Rarity ?? Rarity.Common), Theme.Panel, 0.6f), -1, 80, true, 24);
                if (i == ids.Count - 1) for (int k = i % 3; k < 2; k++) Ui.Size(Ui.Rect(grid, "Filler"), -1, 80, 1);
            }
        }

        private string Name(string typeId) => Content.AlienType(typeId)?.Name ?? typeId;
    }
}
