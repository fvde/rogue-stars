using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.Services;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using RogueStars.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>Settings → Developer card: visibility (offline always, online only with the server's devTools flag) and the reset / jump flows.</summary>
    public sealed class DevToolsCardTests
    {
        private AppFixture _f;

        [SetUp]
        public void SetUp() => _f = new AppFixture();

        [TearDown]
        public void TearDown() => _f.Dispose();

        private SettingsScreen OpenSettings()
        {
            _f.App.Open("settings");
            return (SettingsScreen)_f.App.Router.Current;
        }

        private static Button Find(Transform root, string name) =>
            root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        private static bool HasCard(UiScreen screen) =>
            screen.Root.GetComponentsInChildren<RectTransform>(true).Any(t => t.name == "DeveloperCard");

        private Transform TopModal => _f.App.ModalLayer.childCount > 0 ? _f.App.ModalLayer.GetChild(_f.App.ModalLayer.childCount - 1) : null;

        private void Click(Transform root, string name)
        {
            var b = Find(root, name);
            Assert.That(b, Is.Not.Null, name + " not found");
            b.onClick.Invoke();
        }

        // ---- visibility -----------------------------------------------------------------------------------------

        [Test]
        public void Offline_TheDeveloperCardIsAlwaysThere_WithTheOfflineCheats()
        {
            var s = OpenSettings();
            Assert.That(HasCard(s), Is.True);
            Assert.That(Find(s.Root, "DevReset"), Is.Not.Null);
            Assert.That(Find(s.Root, "DevJump"), Is.Not.Null);
            Assert.That(Find(s.Root, "DevUnlockAll"), Is.Not.Null, "the offline-only unlock-all cheat lives in the same card");
            Click(s.Root, "DevUnlockAll");
            Assert.That(_f.Service.State.Flags.Contains(Features.DevUnlockAllFlag), Is.True);
        }

        [Test]
        public void Online_TheCardFollowsTheServersDevToolsFlag()
        {
            foreach (bool allowed in new[] { false, true })
            {
                var server = new FakeServer(allowed);
                _f.App.Start(server.Service);
                Assert.That(server.Service.DevToolsAvailable, Is.EqualTo(allowed));
                var s = OpenSettings();
                Assert.That(HasCard(s), Is.EqualTo(allowed), "devTools " + allowed);
                Assert.That(Find(s.Root, "DevUnlockAll"), Is.Null, "offline cheats never show online");
            }
        }

        // ---- flows (offline) ------------------------------------------------------------------------------------

        [Test]
        public void Offline_ResetProgress_AsksFirst_ThenStartsADay1PlayerOnTheHub()
        {
            _f.Service.DevGrant(new[] { Reward.Cur(Currencies.Diamonds, 999) });
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            long version = _f.Service.State.Version;
            long diamonds = _f.Service.State.Wallet.Get(Currencies.Diamonds);

            var s = OpenSettings();
            Click(s.Root, "DevReset");
            Assert.That(TopModal.name, Is.EqualTo("Modal:Reset progress?"));
            Click(TopModal, "Button:Cancel");
            Assert.That(_f.Service.State.Wallet.Get(Currencies.Diamonds), Is.EqualTo(diamonds), "cancel changes nothing");

            Click(s.Root, "DevReset");
            Click(TopModal, "Button:Reset");
            var st = _f.Service.State;
            Assert.That(st.Version, Is.GreaterThan(version), "version continues (the hub may run tutorial steps right away)");
            Assert.That(st.CreatedMs, Is.EqualTo(_f.Clock.NowMs));
            Assert.That(st.Flags.Contains(Features.DevUnlockAllFlag), Is.False);
            Assert.That(st.Wallet.Get(Currencies.Diamonds), Is.LessThan(diamonds));
            Assert.That(st.Onboarding.Unlocked, Is.Empty, "day-1 account");
            Assert.That(st.DisplayName, Is.EqualTo("Tester"));
            Assert.That(_f.App.Router.Current, Is.InstanceOf<HubScreen>());
            Assert.That(_f.App.Toasts.LastText, Is.EqualTo("Progress reset · day 1"));
            Assert.That(LocalGameService.Load(_f.Service.SavePath).CreatedMs, Is.EqualTo(_f.Clock.NowMs), "saved");
        }

        [Test]
        public void Offline_JumpToDay_PicksDayAndProfile_AndLoadsThatPlayer()
        {
            var s = OpenSettings();
            Click(s.Root, "DevJump");
            Assert.That(TopModal.name, Is.EqualTo("Modal:Jump to day"));
            var label = TopModal.GetComponentsInChildren<Text>(true).First(t => t.name == "JumpDay");
            Assert.That(label.text, Is.EqualTo("Day 7"));
            Click(TopModal, "JumpDay+7");
            Click(TopModal, "JumpDay+1");
            Click(TopModal, "JumpDay-1");
            Assert.That(label.text, Is.EqualTo("Day 14"));
            Click(TopModal, "JumpDay-7"); Click(TopModal, "JumpDay-7"); Click(TopModal, "JumpDay-7");
            Assert.That(s.JumpDay, Is.EqualTo(AccountAgeJump.MinDays), "clamped at day 1");
            for (int i = 0; i < 12; i++) Click(TopModal, "JumpDay+7");
            Assert.That(s.JumpDay, Is.EqualTo(AccountAgeJump.MaxDays), "clamped at day 60");
            for (int i = 0; i < 8; i++) Click(TopModal, "JumpDay-7");
            Assert.That(s.JumpDay, Is.EqualTo(4));
            Click(TopModal, "Button:Casual");
            Assert.That(s.JumpProfile, Is.EqualTo(0));
            Assert.That(TopModal.GetComponentsInChildren<Text>(true).First(t => t.name == "JumpProfileNote").text, Does.StartWith("Casual"));
            Click(TopModal, "Button:Jump");

            var st = _f.Service.State;
            var c = _f.Service.Content;
            Assert.That(Requirements.AccountAgeDays(st, c, _f.Clock.NowMs), Is.EqualTo(3), "account day 4");
            Assert.That(st.CreatedMs, Is.EqualTo(AccountAgeJump.CreatedMs(c, 4, _f.Clock.NowMs)));
            Assert.That(st.PlayerId, Is.EqualTo("local_test"));
            Assert.That(st.DisplayName, Is.EqualTo("Tester"));
            long level = st.Counter(Requirements.MainLevelCounter);
            Assert.That(level, Is.GreaterThan(1));
            Assert.That(st.Onboarding.Unlocked.Values.All(t => t <= _f.Clock.NowMs), Is.True);
            Assert.That(_f.App.Router.Current, Is.InstanceOf<HubScreen>());
            Assert.That(_f.App.Toasts.LastText, Is.EqualTo("Day 4 · casual player · level " + level));
            Assert.That(LocalGameService.Load(_f.Service.SavePath).Counter(Requirements.MainLevelCounter), Is.EqualTo(level), "saved");
        }

        // ---- flows (online, fake server) -------------------------------------------------------------------------

        [Test]
        public void Online_JumpAndReset_CallTheServer_ApplyItsState_AndReload()
        {
            var server = new FakeServer(true);
            _f.App.Start(server.Service);
            int sessions = server.Sessions;

            var s = OpenSettings();
            Click(s.Root, "DevJump");
            Click(TopModal, "Button:Hardcore");
            Click(TopModal, "JumpDay+7");
            Click(TopModal, "Button:Jump");
            var jump = server.Http.Requests.Last(r => r.Url.EndsWith("/v1/dev/jump"));
            Assert.That(jump.Method, Is.EqualTo("POST"));
            Assert.That((int)JObject.Parse(jump.Body)["day"], Is.EqualTo(14));
            Assert.That((string)JObject.Parse(jump.Body)["profile"], Is.EqualTo("hardcore"));
            Assert.That(jump.TimeoutSeconds, Is.EqualTo(RemoteGameService.DevToolsTimeoutSeconds));
            Assert.That(server.Service.State.Counter(Requirements.MainLevelCounter), Is.EqualTo(33));
            Assert.That(server.Sessions, Is.EqualTo(sessions + 1), "reloads the session");
            Assert.That(_f.App.Router.Current, Is.InstanceOf<HubScreen>());
            Assert.That(_f.App.Toasts.LastText, Is.EqualTo("Day 14 · hardcore player · level 33"));

            s = OpenSettings();
            Click(s.Root, "DevReset");
            Click(TopModal, "Button:Reset");
            Assert.That(server.Http.Requests.Any(r => r.Url.EndsWith("/v1/dev/reset")), Is.True);
            Assert.That(server.Service.State.Counter(Requirements.MainLevelCounter), Is.EqualTo(0));
            Assert.That(_f.App.Toasts.LastText, Is.EqualTo("Progress reset · day 1"));
        }

        [Test]
        public void Online_ARefusal_IsShown_AndNothingChanges()
        {
            var server = new FakeServer(true) { Refuse = true };
            _f.App.Start(server.Service);
            var before = server.Service.State;
            var s = OpenSettings();
            Click(s.Root, "DevReset");
            Click(TopModal, "Button:Reset");
            Assert.That(server.Service.State, Is.SameAs(before));
            Assert.That(_f.App.Toasts.LastText, Does.Contain("Dev tools"));
            Assert.That(s.DevBusy, Is.False);
        }

        /// <summary>Dev-auth server double: /v1/session (with the devTools flag) and /v1/dev/*.</summary>
        private sealed class FakeServer
        {
            public readonly FakeTransport Http = new FakeTransport();
            public readonly RemoteGameService Service;
            public int Sessions;
            public bool Refuse;
            private readonly GameEngine _engine = new GameEngine();
            private GameState _state;
            private readonly bool _devTools;

            public FakeServer(bool devTools)
            {
                _devTools = devTools;
                _state = _engine.NewPlayer("dev_test", TestUtil.T0, 7);
                Http.Handler = Route;
                Service = new RemoteGameService(new RemoteConfig { BaseUrl = "http://fake", DeviceId = "test" }, Http, new ManualClock(TestUtil.T0));
            }

            private string Wire(GameState s) => JsonConvert.SerializeObject(s, ClientJson.Wire);

            private HttpResponse Route(HttpRequest req)
            {
                if (req.Url.EndsWith("/v1/auth/dev")) return FakeTransport.Json(200, "{\"token\":\"tok\",\"playerId\":\"dev_test\"}");
                if (req.Url.EndsWith("/v1/session"))
                {
                    Sessions++;
                    return FakeTransport.Json(200, "{\"ok\":true,\"playerId\":\"dev_test\",\"created\":false,\"serverTimeMs\":" + TestUtil.T0 + ",\"version\":" + _state.Version
                                                   + ",\"devTools\":" + (_devTools ? "true" : "false") + ",\"state\":" + Wire(_state) + ",\"commands\":[]}");
                }
                if (req.Url.EndsWith("/v1/dev/reset") || req.Url.EndsWith("/v1/dev/jump"))
                {
                    if (Refuse) return FakeTransport.Json(403, "{\"ok\":false,\"error\":\"dev_tools_forbidden\"}");
                    bool jump = req.Url.EndsWith("/jump");
                    var body = JObject.Parse(req.Body);
                    long version = _state.Version + 1;
                    _state = _engine.NewPlayer("dev_test", TestUtil.T0, 8);
                    _state.Version = version;
                    if (jump) _state.Counters[Requirements.MainLevelCounter] = 33;
                    return FakeTransport.Json(200, "{\"ok\":true,\"day\":" + (jump ? (int)body["day"] : 1) + ",\"profile\":" + (jump ? "\"" + (string)body["profile"] + "\"" : "null")
                                                   + ",\"seed\":1,\"mainLevel\":" + (jump ? 33 : 0) + ",\"wormLevel\":1,\"power\":100,\"elapsedMs\":5,\"serverTimeMs\":" + TestUtil.T0
                                                   + ",\"version\":" + version + ",\"state\":" + Wire(_state) + "}");
                }
                return FakeTransport.Json(404, "{\"ok\":false,\"error\":\"not_found\"}");
            }
        }
    }
}
