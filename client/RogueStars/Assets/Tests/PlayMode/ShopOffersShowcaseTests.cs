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
    /// The Shop's offers from the start (owner, 2026-11-27) and the hub's bottom tab labels on screen: the hub of a brand-new
    /// player (level 1: Planet + Shop), the Shop of that player (opens on Offers) and the hub of a late player (every tab), at
    /// 1080×2340 and 720×1600, each also with a 48 px bottom safe-area inset ("_inset48"). Every shot asserts that each tab
    /// label ends above the bottom edge (of the safe area). Explicit; run WITHOUT -nographics (ROGUESTARS_SHOTS_DIR = output
    /// folder, ROGUESTARS_SHOT_SIZES = "1080x2340,720x1600").
    /// </summary>
    [Explicit("renders the shop offers / hub tab screenshots; run with -testFilter ShopOffersShowcaseTests")]
    public sealed class ShopOffersShowcaseTests
    {
        private const long T0 = 1_772_452_800_000L;
        private const int InsetPx = 48;
        private int W = 1080, H = 2340, _inset;
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
            _dir = Path.Combine(Application.temporaryCachePath, "shop2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_shop2", "Wiggles", new ManualClock(T0), 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "shop2"));
            Directory.CreateDirectory(_shots);
            var sizes = (Environment.GetEnvironmentVariable("ROGUESTARS_SHOT_SIZES") ?? "1080x2340,720x1600").Split(',');
            SetupCapture(1080, 2340, 0);
            yield return Wait(0.4f);

            // a brand-new player: nothing cleared, the Shop tab already there
            Assert.That(S.Counter(Requirements.MainLevelCounter), Is.EqualTo(0));
            foreach (var size in sizes)
                foreach (var inset in new[] { 0, InsetPx })
                {
                    Size(size, inset);
                    string p = "new_" + W + "x" + H + (inset > 0 ? "_inset" + inset : "");
                    yield return HubShot(p + "_1_hub");
                    _app.Open("shop");
                    yield return Wait(0.6f);
                    Assert.That(((ShopScreen)_app.Router.Current).Section, Is.EqualTo("Offers"));
                    yield return Shot(p + "_2_shop_offers");
                }

            // a late player: every tab
            S.Flags.Add(Features.DevUnlockAllFlag);
            Level(30);
            _svc.DevGrant(new[] { Reward.Cur(Currencies.Diamonds, 5000) });
            foreach (var size in sizes)
                foreach (var inset in new[] { 0, InsetPx })
                {
                    Size(size, inset);
                    yield return HubShot("late_" + W + "x" + H + (inset > 0 ? "_inset" + inset : "") + "_1_hub");
                }
        }

        private IEnumerator HubShot(string name)
        {
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.8f);
            yield return Shot(name);
            yield return ShotCrop(name + "_bar", new Rect(0, H - H * 0.14f, W, H * 0.14f));
            // every tab label ends above the bottom edge of the (safe) screen
            var hub = (HubScreen)_app.Router.Current;
            var labels = hub.Root.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Tab:"))
                .Select(b => b.GetComponentsInChildren<Text>(true).First(t => t.name == "Label")).ToList();
            Assert.That(labels, Is.Not.Empty);
            var c = new Vector3[4];
            foreach (var l in labels)
            {
                l.rectTransform.GetWorldCorners(c);
                float bottom = RectTransformUtility.WorldToScreenPoint(_cam, c[0]).y;
                float top = RectTransformUtility.WorldToScreenPoint(_cam, c[1]).y;
                Assert.That(bottom, Is.GreaterThanOrEqualTo(_inset - 1), name + ": " + l.text + " label below the screen edge");
                Assert.That(top, Is.LessThanOrEqualTo(H), name + ": " + l.text);
            }
        }

        private void Level(int n)
        {
            S.Counters[Requirements.MainLevelCounter] = n;
            for (int i = 1; i <= n; i++) S.Run.Levels[C.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.Refresh(_ => { });
            foreach (var id in S.Onboarding.Unlocked.Keys.ToList()) S.Onboarding.Celebrated.Add(id);
            _svc.Refresh(_ => { });
        }

        private void Size(string wh, int inset)
        {
            var p = wh.Split('x');
            SetupCapture(int.Parse(p[0]), int.Parse(p[1]), inset);
        }

        private void SetupCapture(int w, int h, int insetPx)
        {
            W = w; H = h; _inset = insetPx;
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
            // the simulated safe area: the bottom inset in canvas units (canvas height / screen px)
            var canvasRt = (RectTransform)canvas.transform;
            float units = insetPx * (canvasRt.rect.height > 0 ? canvasRt.rect.height / H : 1);
            Ui.Stretch(_app.Root, 0, 0, 0, units);
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
