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
    /// The one Shop (pull sections Aliens / Blueprints / Parts, then the offer sections) and the hub's bottom bar, at 1080×2340
    /// and 720×1600: an early player (level 6: Parts and Aliens pulls and the offers, which need no unlock) and a level-30 player (everything).
    /// Opens "signals" (the alias) and "shop", then every footer chip. Explicit; run WITHOUT -nographics
    /// (ROGUESTARS_SHOTS_DIR = output folder, ROGUESTARS_SHOT_SIZES = "1080x2340,720x1600").
    /// </summary>
    [Explicit("renders the shop screenshots; run with -testFilter ShopMergeShowcaseTests")]
    public sealed class ShopMergeShowcaseTests
    {
        private const long T0 = 1_772_452_800_000L;
        private int W = 1080, H = 2340;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;

        private GameState S => _svc.State;
        private ContentDb C => _svc.Content;

        [UnityTest, Timeout(900000)]
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "shop_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_shop", "Wiggles", new ManualClock(T0), 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "shop"));
            Directory.CreateDirectory(_shots);
            var sizes = (Environment.GetEnvironmentVariable("ROGUESTARS_SHOT_SIZES") ?? "1080x2340,720x1600").Split(',');
            SetupCapture(1080, 2340);
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            Level(6);
            foreach (var size in sizes) { Size(size); yield return Pages("early_" + W + "x" + H); }

            S.Flags.Add(Features.DevUnlockAllFlag);
            Level(30);
            _svc.DevGrant(new[] { Reward.Cur(Currencies.Diamonds, 5000), Reward.Cur(Currencies.PartTokens, 12), Reward.Cur(Currencies.AlienTokens, 3), Reward.Cur(Currencies.BlueprintTokens, 10) });
            foreach (var size in sizes) { Size(size); yield return Pages("all_" + W + "x" + H); }
        }

        private void Level(int n)
        {
            S.Counters[Requirements.MainLevelCounter] = n;
            for (int i = 1; i <= n; i++) S.Run.Levels[C.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.Refresh(_ => { });
            foreach (var id in S.Onboarding.Unlocked.Keys.ToList()) S.Onboarding.Celebrated.Add(id);
            _svc.Refresh(_ => { });
        }

        private void Size(string wh)
        {
            var p = wh.Split('x');
            SetupCapture(int.Parse(p[0]), int.Parse(p[1]));
        }

        private IEnumerator Pages(string prefix)
        {
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.8f);
            yield return Shot(prefix + "_1_hub");
            yield return ShotCrop(prefix + "_1b_hub_bar", new Rect(0, H - H * 0.14f, W, H * 0.14f));
            foreach (var id in new[] { "signals", "shop" })
            {
                if (ScreenCatalog.Find(id) == null) continue;
                _app.Router.Reset(new HubScreen());
                _app.Open(id);
                yield return Wait(0.6f);
                yield return Shot(prefix + "_2_open_" + id);
            }
            // every footer chip of the screen on top (the Passes chip opens its own page: skipped)
            var screen = _app.Router.Current;
            var chips = screen.Root.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Tab:") && b.name != "Tab:Passes").Select(b => b.name).ToList();
            int n = 0;
            foreach (var name in chips)
            {
                var b = _app.Router.Current.Root.GetComponentsInChildren<Button>(true).FirstOrDefault(x => x.name == name);
                if (b == null) continue;
                b.onClick.Invoke();
                yield return Wait(0.4f);
                yield return Shot(prefix + "_3_" + (++n) + "_" + name.Substring(4).Replace(' ', '_').ToLowerInvariant());
            }
        }

        private void SetupCapture(int w, int h)
        {
            W = w; H = h;
            if (_rt != null) { if (_cam != null) _cam.targetTexture = null; _rt.Release(); }
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            if (_cam == null)
            {
                var go = new GameObject("ShowcaseCamera");
                _cam = go.AddComponent<Camera>();
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = Color.black;
                _cam.orthographic = true;
                _cam.nearClipPlane = 0.1f;
                _cam.farClipPlane = 10;
            }
            _cam.targetTexture = _rt;
            var canvas = _app.Root.GetComponentInParent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _cam;
            canvas.planeDistance = 1;
            var fitter = _app.Root.GetComponent<SafeAreaFitter>();
            if (fitter != null) fitter.enabled = false;
            Ui.Stretch(_app.Root);
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        }

        private IEnumerator Shot(string name) => ShotCrop(name, new Rect(0, 0, W, H));

        /// <summary>A part of the screen (top-left origin, px) at full resolution.</summary>
        private IEnumerator ShotCrop(string name, Rect topLeft)
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
            var bytes = _tex.EncodeToPNG();
            if (topLeft.width < W || topLeft.height < H)
            {
                int x = (int)topLeft.x, h = (int)topLeft.height, y = H - (int)topLeft.y - h, w = (int)topLeft.width;
                var part = new Texture2D(w, h, TextureFormat.RGB24, false);
                part.SetPixels(_tex.GetPixels(x, y, w, h));
                part.Apply();
                bytes = part.EncodeToPNG();
                UnityEngine.Object.Destroy(part);
            }
            File.WriteAllBytes(Path.Combine(_shots, name + ".png"), bytes);
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
