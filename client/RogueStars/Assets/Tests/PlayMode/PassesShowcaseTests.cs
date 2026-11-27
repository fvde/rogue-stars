using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
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
    /// The Passes page at 1080 x 2400: nothing owned (top, scrolled), then every pass bought (top with Claim, after Claim all,
    /// scrolled). Explicit (<c>-testFilter PassesShowcaseTests</c>, needs graphics: run WITHOUT -nographics); writes to
    /// <c>$ROGUESTARS_SHOTS_DIR</c> or client/RogueStars/Screenshots/passes.
    /// </summary>
    [Explicit("renders the Passes page screenshots; run with -testFilter PassesShowcaseTests")]
    public sealed class PassesShowcaseTests
    {
        private const int W = 1080, H = 2400;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private LocalGameService _svc;
        private ManualClock _clock;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;

        private GameState S => _svc.State;
        private ContentDb C => _svc.Content;

        [UnityTest, Timeout(600000)]
        public IEnumerator Passes()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "passes_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _clock = new ManualClock(T0);
            _svc = new LocalGameService(_dir, "local_passes", "Wiggles", _clock, 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "passes"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            S.Counters[Requirements.MainLevelCounter] = 30;
            for (int i = 1; i <= 30; i++) S.Run.Levels[C.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            S.Flags.Add(Features.DevUnlockAllFlag);
            _svc.Refresh(_ => { });
            foreach (var id in S.Onboarding.Unlocked.Keys.ToList()) S.Onboarding.Celebrated.Add(id);

            // reached through the Shop's "Passes" chip
            _app.Router.Reset(new HubScreen());
            _app.Open("shop");
            yield return Wait(0.5f);
            yield return Shot("p0_shop_passes_chip");
            _app.Router.Current.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "Tab:Passes").onClick.Invoke();
            yield return Wait(0.6f);
            Assert.That(_app.Router.Current, Is.InstanceOf<PassesScreen>());
            yield return Shot("p1_not_owned_top");
            yield return Scroll(0.55f);
            yield return Shot("p2_not_owned_middle");
            yield return Scroll(0f);
            yield return Shot("p3_not_owned_bottom");

            // buy every pass (offline: dev receipts) and a gift pack; No Ads and gift packs are not in the first Play set yet
            _app.Session.ProductSet = StoreCatalog.SetAll;
            foreach (var id in new[] { "monthly_pass", "perm_no_ads", "perm_daily_diamonds", "gift_diamond_week" })
            {
                CommandResult r = null;
                _app.Session.Purchase(id, x => r = x);
                yield return WaitFor(() => r != null, 5f);
                Assert.That(r.Ok, Is.True, id + ": " + r.Error);
            }
            _clock.Advance(5 * 3_600_000L); // a few hours into the day: the timers count down
            _app.Session.Refresh();
            yield return Wait(0.4f);
            _app.Router.Current.Rebuild();
            yield return Scroll(1f);
            yield return Shot("p4_owned_top_claimable");
            yield return Scroll(0.5f);
            yield return Shot("p5_owned_middle_claimable");

            // claim one by hand, then Claim all
            _app.Router.Current.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "Claim:monthly_pass").onClick.Invoke();
            yield return Wait(0.6f);
            yield return Scroll(1f);
            yield return Shot("p6_owned_top_monthly_claimed");
            _app.Router.Current.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "ClaimAll").onClick.Invoke();
            yield return Wait(0.8f);
            Assert.That(ShopSystem.PassClaimables(S, C, _svc.NowMs), Is.Empty);
            yield return Scroll(1f);
            yield return Shot("p7_owned_top_all_claimed");
            yield return Scroll(0.5f);
            yield return Shot("p8_owned_middle_all_claimed");
            yield return Scroll(0f);
            yield return Shot("p9_owned_bottom");
        }

        private IEnumerator Scroll(float pos)
        {
            var sr = _app.Router.Current.Root.GetComponentInChildren<ScrollRect>();
            if (sr != null) sr.verticalNormalizedPosition = pos;
            yield return Wait(0.3f);
        }

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("PassesShotCamera");
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
            _cam.cullingMask = ~(1 << CityWorld.Layer | 1 << WorldBoard.Layer);
            var fitter = _app.Root.GetComponent<SafeAreaFitter>();
            if (fitter != null) fitter.enabled = false;
            Ui.Stretch(_app.Root);
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        }

        private IEnumerator Shot(string name)
        {
            _app.Feedback.Tick(0.016f);
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

        private static IEnumerator WaitFor(Func<bool> done, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(done(), Is.True, "timed out");
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
