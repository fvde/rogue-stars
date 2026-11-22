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
            Note(card, "Dev auth device id: " + s.DeviceId + "  (server player id dev_" + s.DeviceId + ")");
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
            Ui.Button(dev, "Grant starter kit (blueprints, aliens, resources)", () => { local.DevGrantStarterKit(); Session.Info("Starter kit granted"); });
            bool all = State.Flags.Contains(Features.DevUnlockAllFlag);
            Ui.Button(dev, all ? "Relock features (dev)" : "Unlock all features (dev)", () => { local.DevSetFlag(Features.DevUnlockAllFlag, !all); Rebuild(); });
        }
    }
}
