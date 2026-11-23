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
    /// <summary>
    /// Planet hub (design canvas "Main" / "HubDay1", onboarding v2): live-event icons on the left (once Invasions unlock),
    /// the planet in the centre, shop / offer / pass / mail / more on the right, the next-unlock teaser, the PLAY card
    /// (Continue when a run is active) and the bottom tabs that appear one by one as features unlock (Worm, City, Planet,
    /// Signals, Arena). Locked features are never shown; only <see cref="Features.Next"/> is teased. Pending unlock
    /// celebrations open the full-screen <see cref="UnlockScreen"/>.
    /// </summary>
    public sealed class HubScreen : UiScreen
    {
        public override string Title => "Planet";
        public override bool ShowBack => false;
        public override float HeaderHeight => 0;
        public override float FooterHeight => 420;

        protected override void Build(RectTransform body)
        {
            BuildMiddle(body);
            BuildGuide(body);
            BuildFooter();
        }

        // ---- middle: left column · planet · right column ---------------------------------------------

        private void BuildMiddle(RectTransform body)
        {
            var row = Ui.Row(body, 900, 12, null, 0, "Middle");
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.UpperCenter;

            var left = SideColumn(row, "LeftColumn");
            foreach (var entry in HubLayout.LeftColumn(State, Content))
            {
                var e = entry;
                HubKit.IconButton(left, e.Glyph, e.Label, HubKit.EventGreenBg, HubKit.EventGreen, Theme.Hex(0xCFF5D4), () => OpenEntry(e), "Side:" + e.Id, out var sub);
                if (e.Id == "quests") sub.text = QuestBadge();
            }
            var events = HubLayout.LiveEvents(State, Content, Now);
            for (int i = 0; i < events.Count && i < HubLayout.MaxLiveIcons; i++)
            {
                var ev = events[i];
                var e = ev;
                bool invasion = e.Kind == LiveOpsKind.Invasion;
                HubKit.IconButton(left, invasion ? "!" : "*", Short(e.Name), invasion ? HubKit.EventRedBg : HubKit.EventGreenBg,
                    invasion ? HubKit.EventRed : HubKit.EventGreen, HubKit.Ink, () => App.Open("observatory"), "Live:" + e.OccurrenceId, out var sub);
                Live(sub, () => Fmt.Duration(System.Math.Max(0, e.EndMs - Now)));
            }
            if (Session.IsUnlocked(Features.Invasions) && (events.Count == 0 || events.Count > HubLayout.MaxLiveIcons))
                HubKit.IconButton(left, "*", "Events", HubKit.Card, HubKit.MailBorder, HubKit.Ink, () => App.Open("observatory"), "Side:observatory", out _);

            var centre = Ui.Column(row, 18, 0, null, "Centre");
            Ui.Size(centre, -1, -1, 1);
            var cl = centre.GetComponent<VerticalLayoutGroup>();
            cl.childAlignment = TextAnchor.MiddleCenter;
            if (!Session.IsUnlocked(Features.Parts))
            {
                var k = Ui.Label(centre, "AN EMPTY PLANET", 30, HubKit.InkDim, TextAnchor.MiddleCenter, "Kicker");
                k.fontStyle = FontStyle.Bold;
            }
            HubKit.Planet(centre, 520);
            var chips = Ui.Row(centre, 60, 12, null, 0, "Chips");
            var chl = chips.GetComponent<HorizontalLayoutGroup>();
            chl.childAlignment = TextAnchor.MiddleCenter;
            chl.childForceExpandHeight = false; // a force-expanding row would soak up the column's spare height
            var portal = Content.MainPortal(PortalsScreen.FrontierPortalIndex(State, Content));
            if (portal != null && Session.IsUnlocked(Features.Parts))
                HubKit.Chip(chips, "Portal " + (portal.Order + 1) + " · " + portal.Name, HubKit.Orange, null, -1, 58, "PortalChip");
            if (!Session.IsUnlocked(Features.Parts))
                Ui.Label(centre, "Just you, a worm with no arms. A portal hums nearby…", 32, HubKit.InkSoft, TextAnchor.MiddleCenter, "Story");

            var right = SideColumn(row, "RightColumn");
            foreach (var entry in HubLayout.RightColumn(State, Content, Now))
            {
                var e = entry;
                Color bg = HubKit.Card, border = HubKit.MailBorder, ink = HubKit.Ink;
                if (e.Id == "shop") { bg = HubKit.ShopGoldBg; border = HubKit.Gold; ink = Theme.Hex(0xFFE0B8); }
                else if (e.Id == "offer") { bg = HubKit.ShopGoldBg; border = HubKit.OrangeDeep; ink = Theme.Hex(0xFFE0B8); }
                else if (e.Id == "pass") { bg = HubKit.PassBg; border = HubKit.Cyan; ink = Theme.Hex(0xB8F0FF); }
                HubKit.IconButton(right, e.Glyph, e.Label, bg, border, ink, () => OpenEntry(e), "Side:" + e.Id, out var sub);
                if (e.Id == "mail") { int unread = MailSystem.UnreadCount(State); sub.text = unread > 0 ? unread + " new" : ""; }
                else if (e.Id == "offer") Live(sub, OfferTimer);
                else if (e.Id == "pass") sub.text = "Tier " + ShopSystem.SeasonLevel(State, Content);
            }
        }

        /// <summary>Claimable daily/weekly goals ("2 ready"), empty when none.</summary>
        private string QuestBadge()
        {
            int n = 0;
            foreach (var q in Content.Quests.Values) if (QuestSystem.CanClaim(State, Content, q, Now)) n++;
            return n > 0 ? n + " ready" : "";
        }

        private static RectTransform SideColumn(Transform parent, string name)
        {
            var col = Ui.Column(parent, 8, 0, null, name);
            Ui.Size(col, 170, -1, 0);
            var lg = col.GetComponent<VerticalLayoutGroup>();
            lg.childAlignment = TextAnchor.UpperCenter;
            lg.childForceExpandWidth = false;
            lg.padding = new RectOffset(0, 0, 10, 0);
            return col;
        }

        private static string Short(string name) => string.IsNullOrEmpty(name) ? "Event" : name.Length <= 10 ? name : name.Substring(0, 9) + "…";

        private string OfferTimer()
        {
            var offer = HubLayout.LimitedOffer(State, Content, Now);
            if (offer?.Schedule != null && offer.Schedule.TryActive(Now, out _, out long end)) return Fmt.Duration(System.Math.Max(0, end - Now));
            return "";
        }

        private void OpenEntry(HubLayout.Entry e)
        {
            if (e.Id == "more") { ShowMore(); return; }
            if (e.ScreenId != null) App.Open(e.ScreenId);
        }

        /// <summary>Every other unlocked screen (laboratory, aliens, heroes, mounts, breeding, guild...).</summary>
        public void ShowMore()
        {
            Modal("More", b =>
            {
                var grid = Ui.Grid(b, new Vector2(280, 130), 3, 14);
                foreach (var entry in HubLayout.MoreScreens(State, Content))
                {
                    var e = entry;
                    HubKit.RoundButton(grid, e.Title, () => { Ui.Clear(App.ModalLayer); App.Router.Push(e.Create()); }, HubKit.Card, HubKit.Ink, -1, 130).name = "More:" + e.Id;
                }
            }, new ModalButton("Close", null));
        }

        // ---- the guide: a tutorial goal that needs another screen, and the next-unlock teaser -----------------

        private void BuildGuide(RectTransform body)
        {
            var snoozed = HubLayout.PendingGroup(State, Content);
            if (snoozed != null && App.SnoozedUnlocks.Contains(snoozed[0]))
            {
                var row = HubKit.RoundPanel(body, HubKit.TabActive, "NewUnlock");
                Ui.Size(row, -1, 120);
                var btn = row.gameObject.AddComponent<Button>();
                btn.targetGraphic = row;
                btn.onClick.AddListener(() => { App.SnoozedUnlocks.Clear(); ShowPendingUnlock(); });
                var t = Ui.Label(row.transform, "NEW: " + Content.Feature(snoozed[0])?.Name + " — tap to open", 32, HubKit.Cyan, TextAnchor.MiddleCenter, "Text");
                t.fontStyle = FontStyle.Bold;
                Ui.Stretch(t.rectTransform);
            }

            var goal = Tutorial.Next(State, Content, Now);
            if (goal != null && goal.ScreenId != "portals" && goal.ScreenId != null)
            {
                var g = HubKit.RoundPanel(body, HubKit.Card, "Goal");
                Ui.Size(g, -1, 120);
                var row = Ui.Row(g.transform, -1, 16, null, 0, "Row");
                Ui.Stretch(row, 30, 12, 16, 12);
                var label = Ui.Label(row, (goal.StepId != null ? "GOAL  " : "") + goal.Text, 32, HubKit.Gold, TextAnchor.MiddleLeft, "GoalText");
                label.fontStyle = FontStyle.Bold;
                HubKit.RoundButton(row, "Go", () => App.Open(goal.ScreenId), HubKit.EventGreen, HubKit.EventGreenBg, 170, 90).name = "GoalGo";
            }

            var next = Features.Next(State, Content, Now);
            if (next != null)
                HubKit.Teaser(body, "NEXT", TeaserText(next), TeaserFigure(next), HubKit.Card, "Teaser");
        }

        /// <summary>"Laboratory — clear level 14", "City: Biomass Farm — unlocks tomorrow".</summary>
        public static string TeaserText(NextUnlock n) => n.Name + " — " + n.Text;

        /// <summary>Level progress (2/5) while the level gate is open, else the day it unlocks.</summary>
        public static string TeaserFigure(NextUnlock n)
        {
            if (!n.LevelMet && n.Level > 0) return n.CurrentLevel + "/" + n.Level;
            if (n.DaysToWait > 0) return "Day " + (n.CurrentDay + n.DaysToWait);
            return null;
        }

        // ---- footer: PLAY card and the bottom tabs ----------------------------------------------------

        private void BuildFooter()
        {
            var run = State.Run.Active;
            if (run != null)
            {
                var level = Content.RunLevel(run.LevelId);
                HubKit.PlayCard(Footer, "RUN IN PROGRESS" + (level != null ? " · " + level.Name.ToUpperInvariant() : ""), "CONTINUE", null,
                    () => App.Router.Push(new RunScreen()), "ContinueButton", out var k);
                k.name = "HubNextLevel";
            }
            else
            {
                var next = RunSystem.NextMainLevel(State, Content);
                var portal = next != null ? Content.Portal(next.PortalId) : null;
                string kicker = next == null ? "PORTALS" : (portal?.Name ?? "").ToUpperInvariant() + " · LEVEL " + PortalsScreen.LevelNum(next.Number);
                string pill = next == null ? null : RunSystem.EntryCost(Content, next.Id, RunMode.Normal).Amount + " stamina";
                HubKit.PlayCard(Footer, kicker, "PLAY", pill, () => App.Open("portals"), "PlayButton", out var k);
                k.name = "HubNextLevel";
            }

            var tabs = Ui.Row(Footer, 180, 14, null, 0, "Tabs");
            var tl = tabs.GetComponent<HorizontalLayoutGroup>();
            tl.childAlignment = TextAnchor.LowerCenter;
            tl.childForceExpandHeight = false;
            tl.childForceExpandWidth = false;
            var list = HubLayout.Tabs(State, Content);
            foreach (var t in list)
            {
                var e = t;
                string badge = TabBadge(e.Id);
                var b = HubKit.Tab(tabs, e.Glyph, e.Label, e.Id == HubLayout.PlanetTab, badge, () => { if (e.ScreenId != null) App.Open(e.ScreenId); }, "Tab:" + e.Id);
                if (list.Count < 4) Ui.Size(b.transform.parent, 250, 180, 0);
            }
        }

        /// <summary>
        /// Badge of a bottom tab: "▲ UP" on the Worm when an equipped part can be levelled with the Ore the player has
        /// (<see cref="GearGuide.HasAffordableUpgrade"/>), else "NEW" on the day its feature unlocked.
        /// </summary>
        public string TabBadge(string tabId)
        {
            if (tabId == "worm" && GearGuide.HasAffordableUpgrade(State, Content)) return UpgradeBadge;
            return IsNewToday(tabId) ? "NEW" : null;
        }

        public const string UpgradeBadge = "▲ UP";

        /// <summary>A tab's feature was unlocked today (NEW badge).</summary>
        private bool IsNewToday(string tabId)
        {
            string feature = tabId == "worm" ? Features.Parts : tabId == "city" ? Features.BiomassFarm : tabId == "signals" ? Features.PartPulls : tabId == "arena" ? Features.Arena : null;
            if (feature == null || !State.Onboarding.Unlocked.TryGetValue(feature, out long at)) return false;
            return GameClock.DayIndex(Content, at) == GameClock.DayIndex(Content, Now);
        }

        // ---- unlock moment ----------------------------------------------------------------------------

        /// <summary>Every frame while the hub is on top (never from a rebuild): open a pending unlock moment.</summary>
        public override void Frame(float dt) => ShowPendingUnlock();

        /// <summary>
        /// Opens the full-screen unlock moment for the first pending celebration group while the hub is on top and no modal
        /// is open (never during a run). Returns true when it opened one. Snoozed groups (the player backed out) wait for a tap.
        /// </summary>
        public bool ShowPendingUnlock()
        {
            if (State == null || App.Router.Current != this || App.ModalLayer.childCount > 0) return false;
            var group = HubLayout.PendingGroup(State, Content);
            if (group == null || App.SnoozedUnlocks.Contains(group[0])) return false;
            App.SnoozedUnlocks.Add(group[0]); // backing out of the moment does not reopen it on every tick
            App.Router.Push(new UnlockScreen(group));
            return true;
        }
    }

    /// <summary>Clean placeholder for screens whose rules workstream is not merged yet.</summary>
    public sealed class PlaceholderScreen : UiScreen
    {
        private readonly string _title;
        private readonly string _workstream;
        private readonly string _description;

        public PlaceholderScreen(string title, string workstream, string description)
        {
            _title = title; _workstream = workstream; _description = description;
        }

        public override string Title => _title;

        protected override void Build(RectTransform body)
        {
            var card = Card(body, Theme.PanelAlt);
            Ui.Label(card, _description, Theme.FontNormal);
            Note(card, "TODO(merge " + _workstream + "): this screen is wired into navigation; its rules system is still a stub on this branch.");
        }
    }
}
