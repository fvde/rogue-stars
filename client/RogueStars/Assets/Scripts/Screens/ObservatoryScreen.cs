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
            Active(body);
            Attackable(body);
            Upcoming(body);
            Modifiers(body);
        }

        private void Active(RectTransform body)
        {
            Section(body, "Active now");
            var events = LiveOpsSystem.ActiveEvents(State, Content, Now);
            if (events.Count == 0) Note(body, "The skies are quiet.");
            foreach (var ev in events)
            {
                var e = ev;
                bool invasion = e.Kind == LiveOpsKind.Invasion;
                var card = Card(body, Color.Lerp(Theme.ValenceColor(invasion ? Valence.Boss : Valence.Special), Theme.Panel, 0.7f));
                Ui.Label(card, (invasion ? "Invasion: " : "Planetary event: ") + e.Name + (e.Surprise ? " (surprise!)" : ""), Theme.FontNormal);
                if (invasion)
                {
                    var def = Content.Invasion(e.RefId);
                    if (def != null) Note(card, def.Description);
                    if (State.LiveOps.Contributions.TryGetValue(e.OccurrenceId, out var c))
                        KeyValue(card, "Your contribution", "damage " + Fmt.Num(c.Damage) + ", score " + Fmt.Num(c.Score) + ", attempts " + c.Attempts);
                    if (def != null)
                    {
                        Ui.Label(card, "Repelled: " + Describe(def.WinModifiers), Theme.FontSmall, Theme.ValenceColor(Valence.Positive));
                        Ui.Label(card, "Overrun: " + Describe(def.LoseModifiers), Theme.FontSmall, Theme.ValenceColor(Valence.Negative));
                    }
                }
                else
                {
                    var def = Content.PlanetaryEvent(e.RefId);
                    if (def != null) { Note(card, def.Description); Ui.Label(card, Describe(def.Modifiers), Theme.FontSmall, Theme.Warn); }
                }
                Live(Note(card, ""), () => "Ends in " + Fmt.Duration(e.EndMs - Now));
            }
        }

        private void Attackable(RectTransform body)
        {
            Section(body, "Fight invaders");
            if (!Session.IsUnlocked(Features.Invasions)) { Note(body, "Invasions unlock later (" + Requirements.Describe(Content, Content.Feature(Features.Invasions)?.Requires ?? new RequirementSet()) + ")."); return; }
            Note(body, "Attempts left: " + State.Wallet.Get(Currencies.InvasionAttempts) + (Session.IsOnline ? "  (server groups of 20)" : "  (offline: local damage roll)"));
            if (_invasionError != null) Ui.Label(body, _invasionError, Theme.FontSmall, Theme.Bad);
            if (_invasions == null) { Note(body, "Loading…"); return; }
            if (_invasions.Count == 0) Note(body, "No invasion to fight right now.");
            foreach (var inv in _invasions)
            {
                var i = inv;
                var row = Ui.Row(body, 96, 10, Theme.Panel, 12);
                Live(Ui.Label(row, "", Theme.FontSmall), () => (i.Name ?? i.Id) + "\n<size=22>ends in " + Fmt.Duration(i.EndsMs - Now) + "</size>");
                Ui.Button(row, "Attack", () => Session.Service.InvasionAttack(i.Id, r =>
                {
                    if (r.Ok) Session.Info("Dealt " + Fmt.Num(r.Value.Damage) + " damage");
                    else Session.Error(Fmt.Humanize(r.Error));
                    LoadInvasions();
                }), Theme.ValenceColor(Valence.Enemy), 220, 80, State.Wallet.Get(Currencies.InvasionAttempts) > 0);
            }
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
