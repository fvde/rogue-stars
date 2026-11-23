using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>Daily / weekly quests with point chests, achievements, login calendar and the new-player event.</summary>
    public sealed class QuestsScreen : UiScreen
    {
        private static readonly string[] Tabs = { "Daily", "Weekly", "Achieve", "Login", "Newbie" };
        private int _tab;

        public override string Title => "Quests";

        protected override void Build(RectTransform body)
        {
            Ui.TabBar(body, Tabs, _tab, i => { _tab = i; Rebuild(); });
            Ui.Button(body, "Claim all completed", () => Run(new QuestClaimAll()), Theme.Good);
            switch (_tab)
            {
                case 0: Chests(body, PointTrack.Daily, State.Quests.DailyPoints); QuestList(body, QuestKind.Daily); break;
                case 1: Chests(body, PointTrack.Weekly, State.Quests.WeeklyPoints); QuestList(body, QuestKind.Weekly); break;
                case 2: QuestList(body, QuestKind.Achievement); break;
                case 3: Login(body); break;
                case 4: NewPlayer(body); break;
            }
        }

        private void QuestList(RectTransform body, QuestKind kind)
        {
            var list = QuestSystem.Visible(State, Content, kind, Now);
            if (list.Count == 0) Note(body, "No quests available.");
            foreach (var quest in list)
            {
                var q = quest;
                bool claimed = State.Quests.Claimed.Contains(q.Id);
                long progress = QuestSystem.Progress(State, q);
                var card = Card(body, claimed ? Theme.Panel : Theme.PanelAlt);
                Ui.Label(card, q.Name, Theme.FontNormal, claimed ? Theme.TextDim : Theme.Text);
                Ui.Bar(card, q.Target > 0 ? (float)progress / q.Target : 1, claimed ? Theme.Locked : Theme.Good, System.Math.Min(progress, q.Target) + " / " + q.Target);
                var row = Ui.Row(card, 80);
                string extra = (q.Points > 0 ? "  +" + q.Points + " pts" : "") + (q.SeasonXp > 0 ? "  +" + q.SeasonXp + " season XP" : "");
                Ui.Label(row, Fmt.Rewards(q.Rewards, Content, State, Now) + extra, Theme.FontSmall, Theme.TextDim);
                if (claimed) Ui.Label(row, "Claimed", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight);
                else Ui.Button(row, "Claim", () => Run(new QuestClaim { QuestId = q.Id }), Theme.Good, 200, 80, QuestSystem.CanClaim(State, Content, q, Now));
            }
        }

        private void Chests(RectTransform body, PointTrack track, long points)
        {
            var card = Card(body, Theme.Header);
            Ui.Label(card, track + " points: " + points, Theme.FontNormal);
            var row = Ui.Row(card, 110);
            foreach (var chest in Content.PointChests)
            {
                if (chest.Track != track) continue;
                var c = chest;
                bool claimed = State.Quests.ChestsClaimed.Contains(track + ":" + c.Points);
                bool ready = points >= c.Points && !claimed;
                Ui.Button(row, c.Points + (claimed ? "\ndone" : "\nchest"), () => Modal(track + " chest " + c.Points, b => Ui.Label(b, Fmt.Rewards(c.Rewards, Content, State, Now), Theme.FontNormal),
                        ready ? new[] { new ModalButton("Open", () => Run(new QuestClaimChest { Track = track, Points = c.Points }), Theme.Good), new ModalButton("Close", null) }
                              : new[] { new ModalButton("Close", null) }),
                    ready ? Theme.Good : claimed ? Theme.Locked : Theme.Button, -1, 110, true, Theme.FontSmall);
            }
        }

        private void Login(RectTransform body)
        {
            var cycle = Content.Retention.LoginCycle;
            bool canClaim = QuestSystem.CanClaimLogin(State, Content, Now);
            int next = cycle.Count == 0 ? 0 : State.Quests.LoginCycleIndex % cycle.Count;
            Note(body, "7-day cycle, no streak loss. Day " + (next + 1) + " is next.");
            var grid = Ui.Grid(body, new Vector2(300, 170), 3, 12);
            for (int i = 0; i < cycle.Count; i++)
            {
                bool isNext = i == next;
                var cell = Ui.Panel(grid, isNext ? (canClaim ? Theme.Good : Theme.Accent) : i < next ? Theme.Locked : Theme.PanelAlt, "Day" + (i + 1));
                var t = Ui.Label(cell.transform, "Day " + (i + 1) + "\n<size=22>" + Fmt.Rewards(cycle[i], Content, State, Now) + "</size>", Theme.FontNormal, Theme.Text, TextAnchor.MiddleCenter);
                Ui.Stretch(t.rectTransform, 8, 8, 8, 8);
            }
            Ui.Button(body, canClaim ? "Claim today's reward" : "Come back tomorrow", () => Run(new LoginClaim()), Theme.Good, -1, Theme.ButtonHeight, canClaim);
        }

        private void NewPlayer(RectTransform body)
        {
            var days = Content.Retention.NewPlayerDays;
            if (!QuestSystem.NewPlayerEventOpen(State, Content, Now)) { Note(body, "The new-player event has ended."); return; }
            int today = QuestSystem.NewPlayerDay(State, Content, Now);
            Note(body, "Day " + today + " of the new-player event.");
            for (int d = 1; d <= days.Count; d++)
            {
                int day = d;
                bool claimed = State.Quests.NewPlayerDaysClaimed.Contains(day);
                var row = Ui.Row(body, 90, 10, Theme.Panel, 12);
                Ui.Label(row, "Day " + day + ": " + Fmt.Rewards(days[day - 1], Content, State, Now), Theme.FontSmall, claimed ? Theme.TextDim : Theme.Text);
                Ui.Button(row, claimed ? "Claimed" : day <= today ? "Claim" : "Locked", () => Run(new NewPlayerClaim { Day = day }), Theme.Good, 220, 76, !claimed && day <= today);
            }
            Section(body, "New-player quests");
            QuestList(body, QuestKind.NewPlayer);
        }
    }
}
