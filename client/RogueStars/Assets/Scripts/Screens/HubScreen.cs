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
    /// Planet hub: portals + play / continue, next tutorial goal, key city buildings, live ops ticker, unlock celebrations and
    /// navigation to every feature. Locked features show their requirement text (Meta Features).
    /// </summary>
    public sealed class HubScreen : UiScreen
    {
        public override string Title => "Planet";
        public override bool ShowBack => false;

        protected override void Build(RectTransform body)
        {
            BuildPlanet(body);
            BuildCelebrations(body);
            BuildLiveOps(body);
            BuildNavigation(body);
        }

        private void BuildPlanet(RectTransform body)
        {
            var card = Card(body, Theme.PanelAlt);
            Section(card, "Portals");
            var portals = Ui.Row(card, 150, 12);
            var mains = new System.Collections.Generic.List<PortalDef>();
            foreach (var p in Content.Portals.Values) if (p.Kind == PortalKind.Main) mains.Add(p);
            mains.Sort((a, b) => a.Order.CompareTo(b.Order));
            for (int i = 0; i < mains.Count && i < 3; i++)
            {
                var portal = mains[i];
                bool open = portal.LevelIds.Count > 0 && RunSystem.IsLevelUnlocked(State, Content, portal.LevelIds[0], RunMode.Normal);
                int cleared = 0;
                foreach (var id in portal.LevelIds) if (RunSystem.Progress(State, id).Cleared) cleared++;
                Ui.Button(portals, portal.Name + "\n<size=22>" + (open ? cleared + "/" + portal.LevelIds.Count + " cleared" : "locked") + "</size>",
                    () => App.Open("portals"), open ? Theme.ValenceColor(Valence.Special) : Theme.Locked, -1, 150, true, Theme.FontNormal);
            }
            if (State.Run.Active != null)
                Ui.Button(card, "Continue run", () => App.Router.Push(new RunScreen()), Theme.Warn);
            else
                Ui.Button(card, "PLAY", () => App.Open("portals"), Theme.Accent, -1, 110, true, Theme.FontTitle).name = "PlayButton";
            var goal = Tutorial.Next(State, Content);
            if (goal != null)
            {
                var g = Ui.Row(card, 90, 10, Theme.Header, 12);
                Ui.Label(g, (goal.StepId != null ? "Next goal: " : "") + goal.Text, Theme.FontNormal, Theme.Warn);
                Ui.Button(g, "Go", () => App.Open(goal.ScreenId), Theme.Good, 140, 72);
            }

            Section(card, "Key buildings");
            var grid = Ui.Grid(card, new Vector2(300, 110), 3, 10);
            int shown = 0;
            foreach (var b in CitySystem.SortedBlueprints(State))
            {
                if (!b.Placed) continue;
                var def = Content.Blueprint(b.Id);
                if (def == null) continue;
                var cell = Ui.Panel(grid, Color.Lerp(Theme.RarityColor(def.Rarity), Theme.Panel, 0.55f), "Building:" + b.Id);
                string status = b.TimerId != null ? "building…" : "Lv " + b.Level;
                var t = Ui.Label(cell.transform, def.Name + "\n" + status, Theme.FontSmall, Theme.Text, TextAnchor.MiddleCenter);
                Ui.Stretch(t.rectTransform, 6, 4, 6, 4);
                if (++shown >= 6) break;
            }
            if (shown == 0) Note(card, "No buildings yet. Open the City to place blueprints.");
            Ui.Button(card, "Open City", () => App.Open("city"), Theme.Accent);
        }

        private void BuildCelebrations(RectTransform body)
        {
            var pending = OnboardingSystem.PendingCelebrations(State, Content);
            if (pending.Count == 0) return;
            var card = Card(body, Theme.Header);
            Section(card, "New unlocks!");
            foreach (var id in pending)
            {
                string fid = id;
                var def = Content.Feature(fid);
                Ui.Button(card, "Celebrate: " + (def?.Name ?? fid), () => Run(new FeatureCelebrate { FeatureId = fid }), Theme.Good);
            }
        }

        private void BuildLiveOps(RectTransform body)
        {
            var events = LiveOpsSystem.ActiveEvents(State, Content, Now);
            var card = Card(body);
            Section(card, "Live now");
            if (events.Count == 0) Note(card, "Nothing active. Check the Observatory for upcoming events.");
            foreach (var ev in events)
            {
                var e = ev;
                var color = e.Kind == LiveOpsKind.Invasion ? Theme.ValenceColor(Valence.Boss) : Theme.ValenceColor(Valence.Special);
                Live(Ui.Label(card, "", Theme.FontSmall, color), () => (e.Kind == LiveOpsKind.Invasion ? "Invasion: " : "Event: ") + e.Name + " — ends in " + Fmt.Duration(e.EndMs - Now));
            }
            Ui.Button(card, "Observatory", () => App.Open("observatory"));
        }

        private void BuildNavigation(RectTransform body)
        {
            Section(body, "Explore");
            var grid = Ui.Grid(body, new Vector2(320, 170), 3, 14);
            var statuses = Features.List(State, Content, Now);
            foreach (var entry in ScreenCatalog.All)
            {
                var e = entry;
                bool unlocked = e.FeatureId == null || Session.IsUnlocked(e.FeatureId);
                var status = e.FeatureId == null ? null : statuses.Find(f => f.Id == e.FeatureId);
                string label = e.Title;
                if (e.Id == "mail") { int unread = MailSystem.UnreadCount(State); if (unread > 0) label += " (" + unread + ")"; }
                if (!unlocked) label += "\n<size=20>" + (status?.RequirementText ?? "Locked") + "</size>";
                else if (e.Placeholder) label += "\n<size=22>coming soon</size>";
                Color color = !unlocked ? Theme.Locked : e.Placeholder ? Theme.PanelAlt : Theme.Button;
                var btn = Ui.Button(grid, label, () =>
                {
                    if (unlocked) App.Router.Push(e.Create());
                    else ShowLocked(e, status);
                }, color, -1, 170, true, Theme.FontNormal);
                btn.name = "Nav:" + e.Id;
            }
        }

        private void ShowLocked(ScreenEntry e, FeatureStatus status)
        {
            Modal(e.Title + " (locked)", b =>
            {
                if (status == null) { Ui.Label(b, "Locked", Theme.FontNormal); return; }
                Ui.Label(b, status.RequirementText, Theme.FontNormal, Theme.TextDim, TextAnchor.MiddleCenter);
                foreach (var p in status.Progress)
                    Ui.Bar(b, p.Target > 0 ? (float)p.Current / p.Target : (p.Met ? 1 : 0), p.Met ? Theme.Good : Theme.Warn, p.Text + "  " + p.Current + "/" + p.Target);
            });
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
