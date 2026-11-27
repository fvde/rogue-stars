using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// Screenshots of the heavy screens for the UI speed work (in-place updates must not change a pixel): each screen as
    /// opened (<c>&lt;id&gt;.png</c>) and after a state change that it updates in place (<c>&lt;id&gt;_changed.png</c>: a few
    /// currencies granted). Player: <c>$ROGUESTARS_PROBE_STATE</c> (a GET /v1/state body, e.g. the 60-day jump), else a
    /// populated offline player. 1080 × 2340 (or $ROGUESTARS_SHOT_SIZE) into $ROGUESTARS_SHOTS_DIR. Explicit
    /// (<c>-testFilter UiSpeedShotsTests</c>); needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the UI speed screenshots; run with -testFilter UiSpeedShotsTests")]
    public sealed class UiSpeedShotsTests
    {
        private static int W = 1080, H = 2340;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;

        [UnityTest, Timeout(900000)]
        public IEnumerator Shots()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            var size = (Environment.GetEnvironmentVariable("ROGUESTARS_SHOT_SIZE") ?? "").Split('x');
            if (size.Length == 2 && int.TryParse(size[0], out int sw) && int.TryParse(size[1], out int sh)) { W = sw; H = sh; }
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "uispeed"));
            Directory.CreateDirectory(_shots);
            string dir = Path.Combine(Application.temporaryCachePath, "uispeed_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            GameState loaded = null;
            string statePath = Environment.GetEnvironmentVariable("ROGUESTARS_PROBE_STATE");
            if (!string.IsNullOrEmpty(statePath) && File.Exists(statePath))
            {
                loaded = JObject.Parse(File.ReadAllText(statePath))["state"].ToObject<GameState>(ClientJson.WireSerializer);
                File.WriteAllText(Path.Combine(dir, LocalGameService.SaveFileName), JsonConvert.SerializeObject(loaded, ClientJson.Storage));
            }
            _svc = new LocalGameService(dir, loaded?.PlayerId ?? "local_shots", "Shots", SystemClock.Instance, 7) { AdsDisabled = true };
            _app.Start(_svc);
            SetupCapture();
            for (int i = 0; i < 10; i++) yield return null;
            if (loaded == null)
            {
                _svc.DevGrantStarterKit();
                _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            }
            foreach (var id in _svc.State.Onboarding.Unlocked.Keys.ToList()) _svc.State.Onboarding.Celebrated.Add(id);
            if (_svc.State.Run.Active != null) _svc.ExecuteNow(new RunAbandon());
            HubScreen.SkyOverride = "day";
            CloseModals();
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.5f);
            yield return Shot("hub");
            yield return Change();
            yield return Shot("hub_changed");

            foreach (var id in new[] { "worm", "parts", "laboratory", "passes", "portals", "heroes", "aliens", "city", "shop" })
            {
                CloseModals();
                _app.Router.Reset(new HubScreen());
                yield return null;
                _app.Open(id);
                yield return Wait(0.6f);
                CloseModals();
                yield return Shot(id);
                yield return Change();
                yield return Shot(id + "_changed");
            }
            HubScreen.SkyOverride = null;
        }

        /// <summary>A state change the screens update in place: a few currencies granted (a new state version).</summary>
        private IEnumerator Change()
        {
            _svc.DevGrant(new[]
            {
                new Reward(RewardKinds.Currency, Currencies.Diamonds, 50), new Reward(RewardKinds.Currency, Currencies.Ore, 500),
                new Reward(RewardKinds.Currency, Currencies.Data, 300), new Reward(RewardKinds.Currency, Currencies.HeroXp, 200),
            });
            yield return Wait(0.6f);
            CloseModals();
        }

        private void CloseModals()
        {
            while (_app.ModalLayer.childCount > 0) Ui.DestroyGo(_app.ModalLayer.GetChild(0).gameObject);
        }

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("ShotsCamera");
            _cam = go.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.orthographic = true;
            _cam.cullingMask = ~(1 << World.WorldBoard.Layer);
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
    }
}
