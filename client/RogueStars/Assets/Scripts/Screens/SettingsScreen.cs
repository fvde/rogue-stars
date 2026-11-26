using System;
using RogueStars.Client.App;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Client.World.Anim;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Offline/online toggle, server URL, display name, run auto mode and the Developer card (owner dev tools: reset progress,
    /// jump to account day N, offline cheats; offline always, online only for players on the server's allowlist).
    /// </summary>
    public sealed class SettingsScreen : UiScreen
    {
        public override string Title => "Settings";
        public override bool AutoRebuild => false; // keep text inputs intact

        protected override void Build(RectTransform body)
        {
            var s = App.Settings;
            var card = Card(body);
            Section(card, "Mode");
            KeyValue(card, "Current", ModeLine(Session.IsOnline, App.ConnectionStatus), Session.IsOnline ? Theme.Good : Theme.Warn).name = "ModeLine";
            var row = Ui.Row(card);
            Ui.Button(row, "Offline", () => App.SwitchMode(false), !s.Online ? Theme.Accent : Theme.Button);
            Ui.Button(row, "Online", () => App.SwitchMode(true), s.Online ? Theme.Accent : Theme.Button);

            Section(card, "Server");
            var url = Ui.Input(card, s.ServerUrl, "http://host:8080");
            var firebase = (Session.Service as RemoteGameService)?.Auth as FirebaseAuthProvider;
            if (firebase == null) Note(card, "Dev auth device id: " + s.DeviceId + "  (server player id dev_" + s.DeviceId + ")");
            else AccountSection(card, firebase);
            Section(card, "Display name");
            var name = Ui.Input(card, CurrentName(State?.DisplayName, s.DisplayName), "Worm");
            Ui.Button(card, "Save & reconnect", () =>
            {
                s.ServerUrl = url.text.Trim();
                s.DisplayName = string.IsNullOrWhiteSpace(name.text) ? "Worm" : name.text.Trim();
                s.Save();
                App.Start();
            }, Theme.Accent);

            var runCard = Card(body);
            Section(runCard, "Run auto mode");
            Note(runCard, "Hold the dice to keep rolling. Auto only rolls: every fight, chest, event, shop and choice waits for your tap, then rolling goes on. Tap anywhere to stop.");

            if (AnimSettings.Enabled) // animation is deferred (AnimSettings.Enabled = false): no control
            {
            var animCard = Card(body);
            animCard.name = "AnimCard";
            Section(animCard, "Animation");
            Note(animCard, "Auto turns the animation down on a slow phone (little memory or few cores). Reduced: everything draws still and nothing is updated.");
            Ui.Button(animCard, "Animation: " + AnimSettings.Label(AnimSettings.Level, AnimSettings.Reduced), () => { AnimSettings.Level = AnimSettings.Next(AnimSettings.Level); Rebuild(); },
                AnimSettings.Level == AnimLevel.Auto ? Theme.Button : Theme.Accent).name = "AnimLevel";
            }

            // Owner dev tools: offline in the editor/development builds, online only when the server's allowlist says so (session devTools).
            var tools = Session.Service?.DevTools;
            if (tools != null) DeveloperCard(body, tools, Session.Service as LocalGameService);
        }

        /// <summary>MS5: "Offline" (never "offline — connected"); online shows the connection state.</summary>
        public static string ModeLine(bool online, string status) =>
            !online ? "Offline" : "Online" + (string.IsNullOrEmpty(status) || status == "idle" ? "" : " — " + status);

        /// <summary>MS5: the name field shows the name the header shows (the player's display name), else the saved setting.</summary>
        public static string CurrentName(string stateName, string settingName) =>
            !string.IsNullOrWhiteSpace(stateName) ? stateName : settingName;

        // ---- Developer ------------------------------------------------------------------------------------------

        public static readonly string[] JumpProfiles = { "casual", "active", "hardcore" };
        public static readonly string[] JumpProfileLabels = { "Casual", "Active", "Hardcore" };
        public static readonly string[] JumpProfileNotes =
        {
            "Casual: two short sessions a day (~15 min).",
            "Active: four sessions a day (~1 h).",
            "Hardcore: seven sessions a day (~3 h).",
        };

        /// <summary>Day and profile picked in the jump modal (kept while the screen lives).</summary>
        public int JumpDay { get; private set; } = 7;
        public int JumpProfile { get; private set; } = 1;
        /// <summary>A dev tools call is running (buttons ignore taps).</summary>
        public bool DevBusy { get; private set; }

        public static string JumpDayLabel(int day) => "Day " + day;

        private void DeveloperCard(RectTransform body, IDevTools tools, LocalGameService local)
        {
            var dev = Card(body, Theme.PanelAlt);
            dev.name = "DeveloperCard";
            Section(dev, "Developer");
            if (!string.IsNullOrEmpty(App.DevStatus)) Note(dev, App.DevStatus).name = "DevStatus"; // T58: dev tool results stay in the panel
            Note(dev, local != null
                ? "Offline save: " + local.SavePath
                : "Owner tools on this server (player id " + (Session.Service as RemoteGameService)?.PlayerId + "). They replace this account's progress.");
            var row = Ui.Row(dev, Theme.ButtonHeight, 10);
            Ui.Button(row, "Reset progress…", ConfirmReset, Theme.Bad).name = "DevReset";
            Ui.Button(row, "Jump to day…", OpenJump, Theme.Accent).name = "DevJump";
            if (local == null) return;
            Section(dev, "Cheats (offline only)");
            bool all = State.Flags.Contains(Features.DevUnlockAllFlag);
            Ui.Button(dev, all ? "Relock features (dev)" : "Unlock all features (dev)", () => { local.DevSetFlag(Features.DevUnlockAllFlag, !all); Rebuild(); })
                .name = "DevUnlockAll";
        }

        public void ConfirmReset() =>
            Modal("Reset progress?", b => Ui.Label(b, "Replaces this account with a brand-new player on day 1 (starter kit, nothing unlocked). This cannot be undone.",
                    Theme.FontNormal, Theme.TextDim, TextAnchor.MiddleCenter),
                new ModalButton("Cancel", null),
                new ModalButton("Reset", () => RunDevTool("Resetting…", (t, done) => t.Reset(done)), Theme.Bad));

        public void OpenJump()
        {
            Modal("Jump to day", b =>
            {
                Ui.Label(b, "Replaces this account's progress with a player who played every day so far in the chosen style (levels, worm, parts, city, aliens, laboratory, unlocks).",
                    Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter);
                var row = Ui.Row(b, Theme.ButtonHeight, 10);
                Text dayLabel = null;
                Text profileNote = null;
                void Step(int delta)
                {
                    JumpDay = Mathf.Clamp(JumpDay + delta, RogueStars.Sim.AccountAgeJump.MinDays, RogueStars.Sim.AccountAgeJump.MaxDays);
                    dayLabel.text = JumpDayLabel(JumpDay);
                }
                Ui.Button(row, "−7", () => Step(-7), Theme.Button, 120).name = "JumpDay-7";
                Ui.Button(row, "−1", () => Step(-1), Theme.Button, 120).name = "JumpDay-1";
                dayLabel = Ui.Display(row, JumpDayLabel(JumpDay), Theme.FontLarge, Theme.Cream, TextAnchor.MiddleCenter, "JumpDay");
                Ui.Button(row, "+1", () => Step(1), Theme.Button, 120).name = "JumpDay+1";
                Ui.Button(row, "+7", () => Step(7), Theme.Button, 120).name = "JumpDay+7";
                Ui.TabBar(b, JumpProfileLabels, JumpProfile, i => { JumpProfile = i; profileNote.text = JumpProfileNotes[i]; });
                profileNote = Ui.Label(b, JumpProfileNotes[JumpProfile], Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter, "JumpProfileNote");
            },
            new ModalButton("Cancel", null),
            new ModalButton("Jump", () =>
            {
                int day = JumpDay;
                string profile = JumpProfiles[JumpProfile];
                RunDevTool("Simulating " + day + (day == 1 ? " day" : " days") + " of a " + profile + " player…", (t, done) => t.Jump(day, profile, done));
            }, Theme.Accent));
        }

        /// <summary>Runs a dev tool, then reloads the app on the new state (hub) and toasts the result.</summary>
        private void RunDevTool(string working, Action<IDevTools, Action<ServiceResult<DevToolsOutcome>>> call)
        {
            var tools = Session.Service?.DevTools;
            if (tools == null || DevBusy) return;
            DevBusy = true;
            App.DevStatus = working; // T58: shown inside the dev panel
            Rebuild();
            var service = Session.Service;
            call(tools, r =>
            {
                DevBusy = false;
                if (!r.Ok) { App.DevStatus = "Dev tools: " + Fmt.Humanize(r.Error); Rebuild(); return; }
                App.DevStatus = r.Value.Summary;
                App.Start(service);
            });
        }

        /// <summary>Firebase account: Play Games or guest (anonymous), with "Link Play Games" for guests.</summary>
        private void AccountSection(RectTransform card, FirebaseAuthProvider auth)
        {
            Section(card, "Account");
            var user = auth.User;
            string who = user == null ? "not signed in" : user.IsAnonymous ? "Guest (anonymous)" : user.HasPlayGames ? "Google Play Games" : "Signed in";
            KeyValue(card, "Account", who, user != null && !user.IsAnonymous ? Theme.Good : Theme.Warn);
            if (user != null) Note(card, "Player id " + user.Uid);
            if (!string.IsNullOrEmpty(auth.LastEvent)) Note(card, "Last sign-in step: " + auth.LastEvent);
            if (user != null && user.IsAnonymous)
            {
                Note(card, auth.PlayGamesAvailable
                    ? "Guest progress lives on this device only. Link Play Games to keep it across devices."
                    : "Play Games sign-in is not configured in this build.");
                if (auth.PlayGamesAvailable)
                    Ui.Button(card, "Link Play Games", () => auth.LinkPlayGames(r =>
                    {
                        if (r.Ok) App.Start(); // T54: the row switches to "Google Play Games"
                        else Session.Error(Fmt.Humanize(r.Error));
                    }), Theme.Accent);
            }
        }
    }
}
