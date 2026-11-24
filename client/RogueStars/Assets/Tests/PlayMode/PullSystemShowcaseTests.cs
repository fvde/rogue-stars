using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// Pull system screens in play mode (docs/design/pull-system.md): the Signals with the visible pity counters, a 10× reveal with
    /// 50-shard batches, the summon event Luck out of 100, and the Shard Reactor (empty, picking, a running job, a finished job).
    /// Asserts the counters and the job flow while it renders. Explicit (run with <c>-testFilter PullSystemShowcaseTests</c>); writes
    /// to $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/pull-system. Needs a graphics device: batchmode WITHOUT -nographics.
    /// </summary>
    [Explicit("renders pull system screenshots; run with -testFilter PullSystemShowcaseTests")]
    public sealed class PullSystemShowcaseTests
    {
        private const int W = 1080, H = 2400;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;

        [UnityTest, Timeout(600000)]
        public IEnumerator PullSystem()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "pulls_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            var clock = new ManualClock(T0);
            _svc = new LocalGameService(_dir, "local_pulls", "Wiggles", clock, 7) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "pull-system"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.3f);

            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            _svc.DevGrant(new[]
            {
                Reward.Cur(Currencies.Diamonds, 20_000), Reward.Cur(Currencies.AlienTokens, 40), Reward.Cur(Currencies.Grain, 60_000), Reward.Cur(Currencies.Ore, 60_000),
                new Reward(RewardKinds.Blueprint, ContentDb.ShardReactorId, 1), new Reward(RewardKinds.Alien, "alien_blorp", 3),
                new Reward(RewardKinds.Alien, "alien_mossling", 1), new Reward(RewardKinds.Alien, "alien_pebblehorn", 1), new Reward(RewardKinds.Alien, "alien_frostmite", 2),
                Reward.Cur(Currencies.HeroTickets, 10),
            });
            var s = _svc.State;
            s.Gacha.For(GachaType.Alien).SinceMythic = 63;
            s.Gacha.For(GachaType.Alien).SinceLegendary = 27;
            s.Summons.LuckFor(SummonEventType.Hero).SinceMythic = 46;
            _svc.Refresh(_ => { });

            // ---- Signals: the counters, then a 10× reveal ----
            var signals = new SignalsScreen();
            _app.Router.Reset(new HubScreen());
            _app.Router.Push(signals);
            yield return Wait(0.3f);
            ClickTab(signals.Root, "Alien Signal");
            yield return Wait(0.4f);
            Assert.That(TextOf(signals.Root, "PityMythic"), Is.EqualTo("Mythic 63/100"));
            Assert.That(TextOf(signals.Root, "PityLegendary"), Is.EqualTo("Legendary 27/40"));
            yield return Shot("pulls_01_signals_pity");
            var r = _svc.ExecuteNow(new GachaPull { PullType = GachaType.Alien, Count = 10, Payment = GachaPayment.Tokens });
            Assert.That(r.Ok, Is.True, r.Error);
            signals.Reveal((GachaPullResult)r.Payload);
            yield return Wait(2.0f);
            yield return Shot("pulls_02_signals_reveal_batches");
            Ui.Clear(_app.ModalLayer);
            signals.Rebuild();
            yield return Wait(0.3f);
            Assert.That(TextOf(signals.Root, "PityMythic"), Does.EndWith("/100"));
            yield return Shot("pulls_03_signals_after_pull");

            // ---- summon event Luck out of 100 ----
            var hub = new SummonEventScreen(SummonEventType.Hero);
            _app.Router.Push(hub);
            yield return Wait(0.5f);
            Assert.That(TextOf(hub.Root, "Luck"), Does.Contain("/100"));
            yield return Shot("pulls_04_summon_luck");
            _app.Router.Pop();

            // ---- Shard Reactor (the service swaps its state object on every command: always read _svc.State) ----
            s = _svc.State;
            var b = CitySystem.Owned(s, ContentDb.ShardReactorId);
            b.Placed = true; b.Level = 1; b.X = 4; b.Y = 4; b.Stars = 3; // 3★: two worker slots
            AlienSystem.Owned(s, "alien_mossling").WorkBuildingId = b.Id;
            AlienSystem.Owned(s, "alien_pebblehorn").WorkBuildingId = b.Id;
            s.Wallet.Add(Currencies.AlienShards("alien_blorp"), 6_000);
            s.Wallet.Add(Currencies.BlueprintShards("bp_ore_mine"), 1_200);
            _svc.Refresh(_ => { });
            var reactor = new ReactorScreen();
            _app.Router.Push(reactor);
            yield return Wait(0.4f);
            Assert.That(TextOf(reactor.Root, "JobCount"), Is.EqualTo("Jobs 0/2"));
            yield return Shot("pulls_05_reactor_empty");
            Click(reactor.Root, "Source:alien_blorp");
            yield return Wait(0.3f);
            Click(reactor.Root, "Target:alien_nebulisk");
            yield return Wait(0.3f);
            yield return Shot("pulls_06_reactor_pick");
            Click(reactor.Root, "Start");
            yield return Wait(0.3f);
            Assert.That(_svc.State.Reactor.Jobs.Count, Is.EqualTo(1));
            // a second job: Common blueprint shards into a Rare blueprint, 3 units
            _app.Router.Pop();
            reactor = new ReactorScreen(ReactorKind.Blueprint, "bp_ore_mine", "bp_spore_vats", 3);
            _app.Router.Push(reactor);
            yield return Wait(0.3f);
            Click(reactor.Root, "Start");
            yield return Wait(0.3f);
            Assert.That(_svc.State.Reactor.Jobs.Count, Is.EqualTo(2));
            reactor.Rebuild();
            yield return Wait(0.3f);
            yield return Shot("pulls_07_reactor_running");
            clock.NowMs += 4 * 3_600_000L; // the alien job (3 h) is done, the blueprint job (3 × 45 min) too
            _svc.Refresh(_ => { });
            reactor.Rebuild();
            yield return Wait(0.3f);
            yield return Shot("pulls_08_reactor_done");
            var collect = reactor.Root.GetComponentsInChildren<Button>(true).First(x => x.name == "Collect" && x.interactable);
            collect.onClick.Invoke();
            yield return Wait(0.3f);
            Assert.That(_svc.State.Reactor.Jobs.Count, Is.EqualTo(1));
        }

        private static string TextOf(Transform root, string name) => root.GetComponentsInChildren<Text>(true).First(t => t.name == name).text;

        private static void Click(Transform root, string name) => root.GetComponentsInChildren<Button>(true).First(b => b.name == name).onClick.Invoke();

        private static void ClickTab(Transform root, string label)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(true))
                if (b.GetComponentsInChildren<Text>(true).Any(t => t.text == label)) { b.onClick.Invoke(); return; }
        }

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("PullsCamera");
            _cam = go.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.orthographic = true;
            var canvas = _app.Root.GetComponentInParent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _cam;
            canvas.planeDistance = 1;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 10;
            var fitter = _app.Root.GetComponent<SafeAreaFitter>();
            if (fitter != null) fitter.enabled = false;
            Ui.Stretch(_app.Root);
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        }

        private IEnumerator Shot(string name)
        {
            _app.Toasts.Tick(60);
            yield return Wait(0.3f);
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(_shots, name + ".png"), _tex.EncodeToPNG());
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
