using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>Active and upcoming invasions / planetary events, invasion attacks and the active modifier stack.</summary>
    public sealed class ObservatoryScreen : UiScreen
    {
        private List<InvasionInfo> _invasions;
        private string _invasionError;

        public override string Title => "Observatory";

        public override void OnShow() => LoadInvasions();

        private void LoadInvasions()
        {
            if (!Session.IsReady) return;
            Session.Service.Invasions(r =>
            {
                _invasions = r.Ok ? r.Value : null;
                _invasionError = r.Ok ? null : r.Error;
                Rebuild();
            });
        }

        protected override void Build(RectTransform body)
        {
            Invasions(body);
            Events(body);
            Upcoming(body);
            Modifiers(body);
        }

        /// <summary>One invasion on the Observatory: the calendar occurrence (rules) and, when attackable, its service instance.</summary>
        public sealed class InvasionRow
        {
            public ActiveLiveEvent Event;
            public InvasionInfo Instance;
        }

        /// <summary>
        /// One consistent invasion list: every active calendar invasion (rules <see cref="LiveOpsSystem.ActiveEvents"/>)
        /// matched with the service instance of the same rules occurrence id (online: the server materialises one
        /// instance per occurrence; offline: the local service lists the occurrences themselves), then instances the
        /// calendar does not know (server-pushed extras) with their display names. Pure (tests).
        /// </summary>
        public static List<InvasionRow> MergeInvasions(ContentDb content, List<ActiveLiveEvent> calendar, List<InvasionInfo> instances)
        {
            var rows = new List<InvasionRow>();
            var used = new HashSet<InvasionInfo>();
            foreach (var ev in calendar)
            {
                if (ev.Kind != LiveOpsKind.Invasion) continue;
                InvasionInfo match = null;
                if (instances != null)
                    foreach (var i in instances)
                        if (!used.Contains(i) && OccurrenceOf(i) == ev.OccurrenceId) { match = i; break; }
                if (match != null) used.Add(match);
                rows.Add(new InvasionRow { Event = ev, Instance = match });
            }
            if (instances != null)
                foreach (var i in instances)
                {
                    if (used.Contains(i)) continue;
                    var def = content.Invasion(i.TypeId);
                    rows.Add(new InvasionRow
                    {
                        Instance = i,
                        Event = new ActiveLiveEvent
                        {
                            OccurrenceId = OccurrenceOf(i), EventId = OccurrenceOf(i), Kind = LiveOpsKind.Invasion, RefId = i.TypeId,
                            Name = !string.IsNullOrEmpty(i.Name) ? i.Name : def?.Name ?? "Invasion",
                            StartMs = i.StartsMs, EndMs = i.EndsMs, Surprise = !i.Scheduled,
                        },
                    });
                }
            return rows;
        }

        private static string OccurrenceOf(InvasionInfo i) => string.IsNullOrEmpty(i.OccurrenceId) ? i.Id : i.OccurrenceId;

        private void Invasions(RectTransform body)
        {
            Section(body, "Invasions");
            bool unlocked = Session.IsUnlocked(Features.Invasions);
            if (!unlocked) Note(body, "Invasions unlock later (" + Requirements.Describe(Content, Content.Feature(Features.Invasions)?.Requires ?? new RequirementSet()) + ").");
            else
            {
                Note(body, "Attempts left: " + State.Wallet.Get(Currencies.InvasionAttempts) + (Session.IsOnline ? "  (server groups of 20)" : "  (offline: local damage roll)"));
                if (_invasionError != null) Ui.Label(body, Fmt.Humanize(_invasionError), Theme.FontSmall, Theme.Bad);
            }
            var rows = MergeInvasions(Content, LiveOpsSystem.ActiveEvents(State, Content, Now), _invasions);
            if (rows.Count == 0) Note(body, _invasions == null && unlocked ? "Loading…" : "No invasion right now.");
            foreach (var row in rows)
            {
                var e = row.Event;
                var inst = row.Instance;
                var card = Card(body, Color.Lerp(Theme.ValenceColor(Valence.Boss), Theme.Panel, 0.7f));
                Ui.Label(card, e.Name + (e.Surprise ? " (surprise!)" : ""), Theme.FontNormal);
                var def = Content.Invasion(e.RefId);
                if (def != null) Note(card, def.Description);
                if (State.LiveOps.Contributions.TryGetValue(e.OccurrenceId, out var c))
                    KeyValue(card, "Your contribution", "damage " + Fmt.Num(c.Damage) + ", score " + Fmt.Num(c.Score) + ", attempts " + c.Attempts);
                if (def != null)
                {
                    Ui.Label(card, "Repelled: " + Describe(def.WinModifiers), Theme.FontSmall, Theme.ValenceColor(Valence.Positive));
                    Ui.Label(card, "Overrun: " + Describe(def.LoseModifiers), Theme.FontSmall, Theme.ValenceColor(Valence.Negative));
                }
                var row2 = Ui.Row(card, 80, 10);
                Live(Ui.Label(row2, "", Theme.FontSmall, Theme.TextDim), () => "Ends in " + Fmt.Duration(e.EndMs - Now));
                if (unlocked && inst != null)
                    Ui.Button(row2, "Attack", () => Session.Service.InvasionAttack(inst.Id, r =>
                    {
                        if (r.Ok) Session.Info("Dealt " + Fmt.Num(r.Value.Damage) + " damage to " + e.Name);
                        else Session.Error(Fmt.Humanize(r.Error));
                        LoadInvasions();
                    }), Theme.ValenceColor(Valence.Enemy), 220, 80, State.Wallet.Get(Currencies.InvasionAttempts) > 0);
            }
        }

        private void Events(RectTransform body)
        {
            Section(body, "Planetary events");
            int n = 0;
            foreach (var ev in LiveOpsSystem.ActiveEvents(State, Content, Now))
            {
                if (ev.Kind != LiveOpsKind.PlanetaryEvent) continue;
                var e = ev;
                n++;
                var card = Card(body, Color.Lerp(Theme.ValenceColor(Valence.Special), Theme.Panel, 0.7f));
                Ui.Label(card, e.Name, Theme.FontNormal);
                var def = Content.PlanetaryEvent(e.RefId);
                if (def != null) { Note(card, def.Description); Ui.Label(card, Describe(def.Modifiers), Theme.FontSmall, Theme.Warn); }
                Live(Note(card, ""), () => "Ends in " + Fmt.Duration(e.EndMs - Now));
            }
            if (n == 0) Note(body, "The skies are quiet.");
        }

        private void Upcoming(RectTransform body)
        {
            Section(body, "Upcoming (14 days)");
            var list = LiveOpsSystem.UpcomingEvents(Content, Now, 14 * MetaTime.Day);
            if (list.Count == 0) Note(body, "Nothing announced.");
            foreach (var ev in list)
            {
                var e = ev;
                Live(Ui.Label(body, "", Theme.FontSmall, Theme.TextDim), () => (e.Kind == LiveOpsKind.Invasion ? "Invasion " : "Event ") + e.Name + " — starts in " + Fmt.Duration(e.StartMs - Now));
            }
        }

        private void Modifiers(RectTransform body)
        {
            Section(body, "Active modifiers");
            int n = 0;
            foreach (var mod in State.Modifiers)
            {
                if (!mod.ActiveAt(Now)) continue;
                var m = mod;
                n++;
                bool good = m.Multiplier >= 1;
                var row = Ui.Row(body, 80, 10, Theme.Panel, 12);
                Ui.Label(row, (m.Description ?? m.Target) + "  ×" + m.Multiplier.ToString("0.##") + "\n<size=22>" + m.Source + "</size>", Theme.FontSmall, Theme.ValenceColor(good ? Valence.Positive : Valence.Negative));
                Live(Ui.Label(row, "", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight), () => m.EndMs == 0 ? "permanent" : Fmt.Duration(m.EndMs - Now));
            }
            if (n == 0) Note(body, "No modifiers active.");
        }

        private static string Describe(List<ModifierDef> mods)
        {
            var parts = new List<string>();
            foreach (var m in mods) parts.Add(m.Description ?? (m.Target + " ×" + m.Multiplier.ToString("0.##")));
            return parts.Count == 0 ? "—" : string.Join(", ", parts);
        }
    }
}
