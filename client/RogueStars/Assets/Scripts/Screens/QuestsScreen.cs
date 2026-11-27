using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>Daily / weekly quests with point chests, achievements, login calendar and the new-player event.</summary>
    public sealed class QuestsScreen : UiScreen
    {
        private static readonly string[] Tabs = { "Daily", "Weekly", "Achieve", "Login", "Newbie" };
        private int _tab;

        private static readonly string[] RibbonTitles = { "Daily Quests", "Weekly Quests", "Achievements", "Daily Login", "New Player" };

        public override string Title => "Daily Quests";

        /// <summary>The tab chips sit at the bottom (kit chip tabs in the footer, a "!" dot where something can be claimed).</summary>
        public override float FooterHeight => 124;

        private bool AnyClaimable(QuestKind kind)
        {
            foreach (var q in QuestSystem.Visible(State, Content, kind, Now)) if (QuestSystem.CanClaim(State, Content, q, Now)) return true;
            return false;
        }

        protected override void Build(RectTransform body)
        {
            if (Footer != null)
            {
                var dots = new List<bool>
                {
                    AnyClaimable(QuestKind.Daily), AnyClaimable(QuestKind.Weekly), AnyClaimable(QuestKind.Achievement),
                    QuestSystem.CanClaimLogin(State, Content, Now), QuestSystem.NewPlayerEventOpen(State, Content, Now) && AnyClaimable(QuestKind.NewPlayer),
                };
                Kit.TabChips(Footer, Tabs, _tab, i => { _tab = i; Rebuild(); }, 84, dots);
            }
            Kit.Ribbon(body, RibbonTitles[_tab], _tab == 0 ? new Color32(0xB0, 0x84, 0x10, 255) : new Color32(0x8A, 0x5A, 0x3C, 255), 84);
            Kit.Button(body, "Claim all completed", () => Run(new QuestClaimAll()), KitButtonColor.Positive);
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
                bool claimed = q.Every == 0 && State.Quests.Claimed.Contains(q.Id);
                long progress = QuestSystem.Progress(State, q);
                bool can = !claimed && QuestSystem.CanClaim(State, Content, q, Now);
                var card = Kit.Card(body, claimed ? KitTone.Well : KitTone.Raised, 16, 8, "Quest:" + q.Id);
                var row = Ui.Row(card, 0, 16, null, 0, "QuestRow");
                row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                // daily and weekly quests pay points only (the bar's chests are the reward): the points are the badge on the left
                string line = q.Points > 0 ? "+" + q.Points + " pts" : Fmt.Rewards(q.Rewards, Content, State, Now);
                if (q.Every > 0) line += "   ·   next at " + QuestSystem.NextMilestone(State, q);
                bool badge = q.Points > 0 && q.Every == 0;
                if (badge)
                {
                    var b = Kit.LevelBadge(row, line, claimed ? Theme.TextDim : Theme.Gold, 56, "PointsBadge");
                    Ui.Size(b, 150, 56, 0, 0);
                    b.GetComponentInChildren<Text>().name = "Reward:" + q.Id;
                }
                var mid = Ui.Column(row, 6, 0, null, "Mid");
                Ui.Size(mid, -1, -1, 1);
                Ui.Label(mid, q.Name, Theme.FontNormal, claimed ? Theme.TextDim : Theme.Text);
                Kit.ProgressPill(mid, q.Target > 0 ? (float)progress / q.Target : 1, claimed ? Theme.Locked : Theme.Good, System.Math.Min(progress, q.Target) + " / " + q.Target, 34);
                if (!badge) Ui.Label(mid, line, Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleLeft, "Reward:" + q.Id);
                if (claimed)
                {
                    var chk = Kit.Title(row, "✓", 64, Theme.Good, TextAnchor.MiddleCenter, "Claimed");
                    Ui.Size(chk, 120, 80, 0, 0);
                }
                else
                {
                    // G4: a disabled Claim says why
                    var col = Ui.Column(row, 4, 0, null, "ClaimColumn");
                    Ui.Size(col, 190, -1, 0);
                    Ui.ButtonWithReason(col, "Claim", () => Run(new QuestClaim { QuestId = q.Id }), can ? Theme.Good : Theme.Button, 190, 76, can,
                        progress < q.Target ? (q.Target - System.Math.Min(progress, q.Target)) + " to go" : "Not yet").name = "Claim:" + q.Id;
                }
            }
        }

        /// <summary>The chests' reach state: grey until the points reach the threshold, gold while unclaimed, opened once claimed.</summary>
        public enum ChestState { Locked, Ready, Claimed }

        public static ChestState StateOf(long points, long threshold, bool claimed) => claimed ? ChestState.Claimed : points >= threshold ? ChestState.Ready : ChestState.Locked;

        /// <summary>Fill of the points bar: points over the top chest's threshold, clamped to 0..1.</summary>
        public static float FillRatio(long points, long top) => top <= 0 ? 0 : Mathf.Clamp01((float)points / top);

        /// <summary>Where a chest sits along the bar (0..1): proportional to its threshold, so the top chest ends the bar.</summary>
        public static float ChestRatio(long threshold, long top) => top <= 0 ? 0 : Mathf.Clamp01((float)threshold / top);

        private const float BarHeight = 52, ChestSize = 110, BarInset = 70;
        private readonly System.Collections.Generic.Dictionary<PointTrack, float> _shownFill = new System.Collections.Generic.Dictionary<PointTrack, float>();

        private void Chests(RectTransform body, PointTrack track, long points)
        {
            var chests = new System.Collections.Generic.List<PointChestDef>();
            foreach (var chest in Content.PointChests) if (chest.Track == track) chests.Add(chest);
            chests.Sort((a, b) => a.Points.CompareTo(b.Points));
            long top = chests.Count == 0 ? 0 : chests[chests.Count - 1].Points;
            var card = Kit.Card(body, KitTone.Raised, 24, 8, "PointsCard");
            var head = Ui.Display(card, points + "<size=30>  /  " + top + " " + track.ToString().ToLowerInvariant() + " points</size>", Theme.FontLarge, Theme.Gold, TextAnchor.MiddleLeft, "Points");
            head.supportRichText = true;
            var region = Ui.Rect(card, "Chests");
            Ui.Size(region, height: 200, flexWidth: 1);
            var track_ = Ui.Rect(region, "Bar");
            track_.anchorMin = new Vector2(0, 1); track_.anchorMax = new Vector2(1, 1); track_.pivot = new Vector2(0.5f, 0.5f);
            track_.offsetMin = new Vector2(BarInset, -(80 + BarHeight / 2)); track_.offsetMax = new Vector2(-BarInset, -(80 - BarHeight / 2));
            float r = BarHeight / 2;
            var bg = track_.gameObject.AddComponent<UnityEngine.UI.Image>();
            bg.color = Theme.Track;
            Kit.Decorate(bg, r, "pill_track", false, 0.92f, 1.12f);
            bg.raycastTarget = false;
            var fill = Ui.Panel(track_, Theme.Good, "Fill");
            Kit.Decorate(fill, r, "pill_fill", false, 1.22f, 0.86f);
            fill.raycastTarget = false;
            var frt = fill.rectTransform;
            frt.anchorMin = Vector2.zero; frt.pivot = new Vector2(0, 0.5f);
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            UiShapes.Outline(track_, Theme.TextDim, r, 5);
            float target = FillRatio(points, top);
            _shownFill.TryGetValue(track, out float from);
            _shownFill[track] = target;
            UiTweens.Play(track_.gameObject, "fill", 0.6f, t => // hosted on the bar: the fill hides itself at 0 and a hidden host never ticks
            {
                float v = Mathf.Lerp(from, target, 1 - (1 - t) * (1 - t));
                frt.anchorMax = new Vector2(v, 1);
                frt.offsetMax = Vector2.zero;
                fill.gameObject.SetActive(v > 0.001f);
            });
            foreach (var chest in chests)
            {
                var c = chest;
                bool claimed = State.Quests.ChestsClaimed.Contains(track + ":" + c.Points);
                var st = StateOf(points, c.Points, claimed);
                ChestOnBar(track_, ChestRatio(c.Points, top), c.Points, st, () =>
                {
                    if (st == ChestState.Ready) OpenChest(track, c);
                    else Modal(track + " chest · " + c.Points + " points", b => Ui.Label(b, ChestText(c), Theme.FontNormal), new[] { new ModalButton("Close", null) });
                });
            }
            // the track's reset countdown (the daily points at the day reset, the weekly points at the week reset)
            var refresh = Ui.Label(card, "", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter, "Refresh");
            Live(refresh, () =>
            {
                long next = track == PointTrack.Daily
                    ? RogueStars.Rules.Core.GameClock.DayStartMs(Content, RogueStars.Rules.Core.GameClock.DayIndex(Content, Now) + 1)
                    : RogueStars.Rules.Core.GameClock.WeekStartMs(Content, RogueStars.Rules.Core.GameClock.WeekIndex(Content, Now) + 1);
                return "Refresh in " + Fmt.Duration(System.Math.Max(0, next - Now));
            });
        }

        /// <summary>What a chest holds, as text (the random pull currencies are named by their rule).</summary>
        public string ChestText(PointChestDef c)
        {
            string text = Fmt.Rewards(c.Rewards, Content, State, Now);
            if (c.RandomTokens > 0) text += "\n+" + c.RandomTokens + " pull currencies, each a random Crate, Signal or Schematic you have unlocked";
            if (c.TokensEachType > 0) text += "\n+" + c.TokensEachType + " of every pull currency you have unlocked (Crates, Signals, Schematics)";
            return text;
        }

        /// <summary>
        /// Opens a point chest and shows its contents through the shared reveal (SG1: chests use the pull ceremony): the daily top chest's
        /// three random tickets flip out one by one (the rules store the draw in <c>QuestState.LastChestTokens</c>).
        /// </summary>
        public void OpenChest(PointTrack track, PointChestDef c)
        {
            var shown = RewardGate.Usable(State, Content, c.Rewards, Now);
            var types = QuestSystem.UnlockedTokenTypes(State, Content);
            Run(new QuestClaimChest { Track = track, Points = c.Points }, r =>
            {
                if (!r.Ok) return;
                var all = new System.Collections.Generic.List<Reward>(shown);
                foreach (var t in State.Quests.LastChestTokens) all.Add(Reward.Cur(t, 1));
                if (c.TokensEachType > 0) foreach (var t in types) all.Add(Reward.Cur(t, c.TokensEachType));
                PullReveal.Show(App.ModalLayer, PullReveal.FromRewards(all, Content), new RevealOptions { Title = track + " chest", Skip = SummonEventScreen.SkipAnimation });
            });
        }

        /// <summary>A chest icon sitting on the bar at <paramref name="ratio"/> (grey until reached, gold and pulsing when ready, dimmed with a check once opened) with its threshold under it.</summary>
        private static void ChestOnBar(RectTransform bar, float ratio, long pts, ChestState st, System.Action onClick)
        {
            bool ready = st == ChestState.Ready, claimed = st == ChestState.Claimed;
            var root = Ui.Rect(bar, "Chest:" + pts);
            root.anchorMin = root.anchorMax = new Vector2(ratio, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(ChestSize, ChestSize);
            root.anchoredPosition = Vector2.zero;
            var img = SummonKit.Img(root, World.SummonArt.Icon("chest"), "Icon");
            Ui.Stretch(img.rectTransform);
            img.color = ready ? Theme.Gold : claimed ? new Color(0.55f, 0.5f, 0.45f, 0.75f) : new Color(0.62f, 0.62f, 0.66f, 1f);
            img.raycastTarget = true;
            if (ready) UiPulse.On(img, 0.06f, 1.2f, 1);
            if (claimed)
            {
                var chk = SummonKit.Img(root, World.SummonArt.Icon("check"), "Claimed");
                chk.rectTransform.anchorMin = chk.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                chk.rectTransform.sizeDelta = new Vector2(56, 56);
                chk.rectTransform.anchoredPosition = Vector2.zero;
            }
            var t = Ui.Display(root, pts.ToString(), Theme.FontSmall, ready ? Theme.Gold : Theme.TextDim, TextAnchor.MiddleCenter, "Points");
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0);
            t.rectTransform.sizeDelta = new Vector2(140, 44);
            t.rectTransform.anchoredPosition = new Vector2(0, -22);
            var b = root.gameObject.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => onClick());
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
                var cell = Kit.Surface(grid, isNext ? (canClaim ? Theme.Good : Theme.Accent) : i < next ? Theme.Locked : Theme.PanelAlt, 26, isNext ? Theme.Gold : Theme.Border, "slot_reward", "Day" + (i + 1));
                var t = Ui.Label(cell.transform, "Day " + (i + 1) + "\n<size=22>" + Fmt.Rewards(cycle[i], Content, State, Now) + "</size>", Theme.FontNormal, isNext && canClaim ? Theme.OnAccent : Theme.Text, TextAnchor.MiddleCenter);
                Ui.Stretch(t.rectTransform, 8, 8, 8, 8);
                if (i < next) { var ck = Kit.Title(cell.transform, "✓", 44, Theme.Good, TextAnchor.LowerRight, "Check"); Ui.Stretch(ck.rectTransform, 0, 0, 12, 6); }
            }
            Kit.Button(body, canClaim ? "Claim today's reward" : "Come back tomorrow", () => Run(new LoginClaim()), KitButtonColor.Positive, -1, Theme.ButtonHeight, canClaim);
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
                var rowCard = Kit.Card(body, claimed ? KitTone.Well : KitTone.Raised, 12, 4, "NewPlayerDay" + day);
                var row = Ui.Row(rowCard, 90, 10);
                Ui.Label(row, "Day " + day + ": " + Fmt.Rewards(days[day - 1], Content, State, Now), Theme.FontSmall, claimed ? Theme.TextDim : Theme.Text);
                Kit.Button(row, claimed ? "Claimed" : day <= today ? "Claim" : "Locked", () => Run(new NewPlayerClaim { Day = day }), claimed || day > today ? KitButtonColor.Secondary : KitButtonColor.Positive, 220, 76, !claimed && day <= today);
            }
            Kit.Ribbon(body, "New-player quests", new Color32(0x2E, 0x7A, 0x5A, 255), 76);
            QuestList(body, QuestKind.NewPlayer);
        }
    }
}
