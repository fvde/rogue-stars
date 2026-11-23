using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Async PvP: league, points, tickets, opponents and the weekly group leaderboard. Online the server resolves
    /// fights; offline LocalGameService fights local bots (power comparison until the Run BattleSimulator lands).
    /// </summary>
    public sealed class ArenaScreen : UiScreen
    {
        private int _tab;
        private List<ArenaOpponent> _opponents;
        private ArenaLeaderboard _board;
        private string _error;
        private bool _fighting;

        public override string Title => "Arena";

        public override void OnShow() => Load();

        private void Load()
        {
            if (!Session.IsReady) return;
            Session.Service.ArenaOpponents(r => { _opponents = r.Ok ? r.Value : null; _error = r.Ok ? null : r.Error; Rebuild(); });
            Session.Service.ArenaLeaderboard(r => { _board = r.Ok ? r.Value : null; if (!r.Ok) _error = r.Error; Rebuild(); });
        }

        protected override void Build(RectTransform body)
        {
            var a = State.Arena;
            var leagues = Content.Arena.Leagues;
            string league = a.League >= 0 && a.League < leagues.Count ? leagues[a.League].Name : "League " + a.League;
            var card = Card(body, Theme.Header);
            Ui.Label(card, league, Theme.FontLarge, Theme.Warn);
            KeyValue(card, "Points", a.Points + " (best " + a.BestPoints + ")");
            KeyValue(card, "This week", a.WeekWins + " W / " + a.WeekLosses + " L");
            KeyValue(card, "Tickets", State.Wallet.Get(Currencies.ArenaTickets).ToString());
            Note(card, Session.IsOnline ? "Fights are resolved by the server." : "Offline: fighting local bots (power comparison placeholder).");

            Ui.TabBar(body, new[] { "Opponents", "Leaderboard" }, _tab, i => { _tab = i; Rebuild(); });
            if (_error != null) Ui.Label(body, Fmt.Humanize(_error), Theme.FontSmall, Theme.Bad);
            if (_tab == 0) Opponents(body); else Board(body);
            Ui.Button(body, "Refresh", Load);
        }

        private void Opponents(RectTransform body)
        {
            if (_opponents == null) { Note(body, "Loading…"); return; }
            if (_opponents.Count == 0) Note(body, "No opponents found yet.");
            bool tickets = State.Wallet.Get(Currencies.ArenaTickets) > 0;
            foreach (var o in _opponents)
            {
                var opp = o;
                var row = Ui.Row(body, 96, 10, Theme.Panel, 12);
                Ui.Label(row, Fmt.PlayerName(opp.DisplayName) + "\n<size=22>" + opp.Points + " pts · power " + Fmt.Num(opp.Power) + "</size>", Theme.FontNormal);
                Ui.Button(row, "Fight", () => Fight(opp), Theme.ValenceColor(Valence.Enemy), 200, 80, tickets && !_fighting);
            }
        }

        private void Fight(ArenaOpponent opp)
        {
            _fighting = true;
            Session.Service.ArenaFight(opp.PlayerId, r =>
            {
                _fighting = false;
                if (!r.Ok) { Session.Error(Fmt.Humanize(r.Error)); Rebuild(); return; }
                var o = r.Value;
                Modal(o.Won ? "Victory!" : "Defeat", b =>
                {
                    Ui.Label(b, "vs " + Fmt.PlayerName(o.Opponent?.DisplayName ?? opp.DisplayName), Theme.FontNormal, Theme.Text, TextAnchor.MiddleCenter);
                    Ui.Label(b, (o.PointsDelta >= 0 ? "+" : "") + o.PointsDelta + " points → " + o.Points, Theme.FontLarge, o.Won ? Theme.Good : Theme.Bad, TextAnchor.MiddleCenter);
                });
                Load();
            });
        }

        private void Board(RectTransform body)
        {
            if (_board == null) { Note(body, "Loading…"); return; }
            Note(body, "Week " + _board.Week + " · group " + _board.GroupId + " (" + _board.GroupSize + ") · your rank " + (_board.MyRank > 0 ? "#" + _board.MyRank : "—"));
            foreach (var e in _board.Entries)
            {
                bool me = e.PlayerId == State.PlayerId;
                var row = Ui.Row(body, 64, 10, me ? Theme.Header : Theme.Panel, 12);
                var rank = Ui.Label(row, "#" + e.Rank, Theme.FontSmall, me ? Theme.Warn : Theme.TextDim);
                Ui.Size(rank, 100, -1, 0);
                Ui.Label(row, Fmt.PlayerName(e.DisplayName), Theme.FontSmall);
                Ui.Label(row, e.Points + " pts", Theme.FontSmall, Theme.Text, TextAnchor.MiddleRight);
            }
        }
    }
}
