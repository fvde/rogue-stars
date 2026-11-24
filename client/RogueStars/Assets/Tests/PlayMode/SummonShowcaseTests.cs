using System;
using System.Collections;
using System.IO;
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

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// Renders the summon event screens (hub, 10× draw result, exchange, packs, pass, tasks; hero and mount) for a player
    /// mid-event. Explicit (run with <c>-testFilter SummonShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR or
    /// client/RogueStars/Screenshots/summon. Needs a graphics device: run in batchmode WITHOUT -nographics.
    /// </summary>
    [Explicit("renders summon event screenshots; run with -testFilter SummonShowcaseTests")]
    public sealed class SummonShowcaseTests
    {
        private const int W = 1080, H = 2400;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir;
        private string _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;

        [UnityTest, Timeout(600000)]
        public IEnumerator Summon()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "summon_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            // Two days into the hero event of the global rotation (T0 + 1 h is inside an occurrence; we move to day 2).
            var clock = new ManualClock(T0);
            _svc = new LocalGameService(_dir, "local_summon", "Wiggles", clock, 11) { AdsDisabled = true };
            _app.Start(_svc);
            _app.Session.ProductSet = StoreCatalog.SetInitial; // as on the live server: event products are "Coming soon"
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "summon"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.3f);

            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            var ev = SummonEventSystem.CurrentEvent(_svc.State, _svc.Content, SummonEventType.Hero, clock.NowMs);
            clock.NowMs = Math.Max(clock.NowMs, ev.StartMs + 2 * MetaTime.Day + 5 * MetaTime.Hour);
            _svc.DevGrant(new[]
            {
                Reward.Cur(Currencies.HeroTickets, 24), Reward.Cur(Currencies.MountTickets, 14), Reward.Cur(Currencies.Diamonds, 2000),
                Reward.Cur(Currencies.HeroSigils, 640), Reward.Cur(Currencies.MountBolts, 380), new Reward(RewardKinds.Hero, "hero_star_knight", 2),
            });
            // Progress since the event start: a few tasks done, a luck meter on its way.
            var s = _svc.State;
            s.Summons.Luck[SummonEventType.Hero] = new GachaPityState { SinceMythic = 184 };
            s.Counters["run_enemies_defeated"] = s.Counter("run_enemies_defeated") + 320;
            s.Counters["runs_completed"] = s.Counter("runs_completed") + 6;
            s.Counters["pulls_part"] = s.Counter("pulls_part") + 18;
            _svc.ExecuteNow(new SummonPull { EventType = SummonEventType.Hero, Count = 10 });
            _svc.ExecuteNow(new SummonPull { EventType = SummonEventType.Mount, Count = 10 });
            _svc.ExecuteNow(new SummonMilestoneClaim { EventType = SummonEventType.Hero, Pulls = 10 });
            _svc.ExecuteNow(new SummonPassClaim { EventType = SummonEventType.Hero, Day = 1 });
            _svc.Refresh(_ => { });

            _app.Router.Reset(new HubScreen());
            yield return Wait(0.3f);
            yield return Shot("summon_00_home_entry");

            var hub = new SummonEventScreen(SummonEventType.Hero);
            _app.Router.Push(hub);
            yield return Wait(0.5f);
            yield return Shot("summon_01_event_hub");

            SummonEventScreen.SkipAnimation = true;
            hub.Pull(10, SummonPayment.Tickets, null);
            yield return Wait(0.6f);
            yield return Shot("summon_02_draw_10");
            Ui.Clear(_app.ModalLayer);
            SummonEventScreen.SkipAnimation = false;

            hub.PacksSheet();
            yield return Wait(0.5f);
            yield return Shot("summon_04_packs");
            Ui.Clear(_app.ModalLayer);

            hub.TasksSheet();
            yield return Wait(0.5f);
            yield return Shot("summon_06_tasks");
            Ui.Clear(_app.ModalLayer);

            _app.Router.Push(new SummonExchangeScreen(SummonEventType.Hero));
            yield return Wait(0.5f);
            yield return Shot("summon_03_exchange");
            _app.Router.Pop();

            _app.Router.Push(new SummonPassScreen(SummonEventType.Hero));
            yield return Wait(0.5f);
            yield return Shot("summon_05_pass");
            _app.Router.Pop();

            hub.Type = SummonEventType.Mount;
            hub.Rebuild();
            yield return Wait(0.5f);
            yield return Shot("summon_07_mount_hub");
        }

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("SummonCamera");
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
            _app.Toasts.Tick(60); // toasts of the setup commands are gone
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
            SummonEventScreen.SkipAnimation = false;
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
