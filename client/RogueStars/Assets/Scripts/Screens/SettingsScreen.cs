using RogueStars.Client.App;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>Offline/online toggle, server URL, display name, save reset and offline dev cheats.</summary>
    public sealed class SettingsScreen : UiScreen
    {
        public override string Title => "Settings";
        public override bool AutoRebuild => false; // keep text inputs intact

        protected override void Build(RectTransform body)
        {
            var s = App.Settings;
            var card = Card(body);
            Section(card, "Mode");
            KeyValue(card, "Current", Session.Service?.ModeName + " — " + App.ConnectionStatus, Session.IsOnline ? Theme.Good : Theme.Warn);
            var row = Ui.Row(card);
            Ui.Button(row, "Offline", () => App.SwitchMode(false), !s.Online ? Theme.Accent : Theme.Button);
            Ui.Button(row, "Online", () => App.SwitchMode(true), s.Online ? Theme.Accent : Theme.Button);

            Section(card, "Server");
            var url = Ui.Input(card, s.ServerUrl, "http://host:8080");
            var firebase = (Session.Service as RemoteGameService)?.Auth as FirebaseAuthProvider;
            if (firebase == null) Note(card, "Dev auth device id: " + s.DeviceId + "  (server player id dev_" + s.DeviceId + ")");
            else AccountSection(card, firebase);
            Section(card, "Display name");
            var name = Ui.Input(card, s.DisplayName, "Worm");
            Ui.Button(card, "Save & reconnect", () =>
            {
                s.ServerUrl = url.text.Trim();
                s.DisplayName = string.IsNullOrWhiteSpace(name.text) ? "Worm" : name.text.Trim();
                s.Save();
                App.Start();
            }, Theme.Accent);

            var local = Session.Service as LocalGameService;
            var dev = Card(body, Theme.PanelAlt);
            Section(dev, "Offline save");
            if (local == null)
            {
                Note(dev, "Switch to offline mode to manage the local save and dev cheats.");
                return;
            }
            Note(dev, local.SavePath);
            Ui.Button(dev, "Reset save…", () => Modal("Reset save?", b => Ui.Label(b, "Deletes the offline save and starts a new worm.", Theme.FontNormal, Theme.TextDim, TextAnchor.MiddleCenter),
                new ModalButton("Cancel", null),
                new ModalButton("Reset", () => local.ResetSave(r => { if (r.Ok) Session.Info("Save reset"); App.Start(local); }), Theme.Bad)), Theme.Bad);
            Section(dev, "Dev cheats (offline only)");
            bool all = State.Flags.Contains(Features.DevUnlockAllFlag);
            Ui.Button(dev, all ? "Relock features (dev)" : "Unlock all features (dev)", () => { local.DevSetFlag(Features.DevUnlockAllFlag, !all); Rebuild(); });
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
                        if (r.Ok) { Session.Info("Play Games linked"); App.Start(); }
                        else Session.Error(Fmt.Humanize(r.Error));
                    }), Theme.Accent);
            }
        }
    }
}
