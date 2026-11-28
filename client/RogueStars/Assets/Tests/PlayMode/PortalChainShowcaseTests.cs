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

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// The level selection as a chain of portals (one per level, its biome showing through): a brand-new player (level 1
    /// next) and a mid-game player (54 cleared, level 55 next, scrolled to it), at 1080×2400 and 720×1600. Explicit; run
    /// WITHOUT -nographics (ROGUESTARS_SHOTS_DIR = output folder, ROGUESTARS_SHOT_SIZES = "1080x2400,720x1600").
    /// </summary>
    [Explicit("renders the portal chain screenshots; run with -testFilter PortalChainShowcaseTests")]
    public sealed class PortalChainShowcaseTests
    {
        private const long T0 = 1_772_452_800_000L;
        private int W = 1080, H = 2400;
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
            _dir = Path.Combine(Application.temporaryCachePath, "portals_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_portals", "Wiggles", new ManualClock(T0), 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "portals"));
            Directory.CreateDirectory(_shots);
            var sizes = (Environment.GetEnvironmentVariable("ROGUESTARS_SHOT_SIZES") ?? "1080x2400,720x1600").Split(',');
            SetupCapture(1080, 2400);
            yield return Wait(0.4f);

            foreach (var size in sizes)
            {
                Size(size);
                yield return PortalsShot("new_" + W + "x" + H, "main_1");
            }

            // mid-game: 54 levels cleared (stars vary), level 55 next
            S.Flags.Add(Features.DevUnlockAllFlag);
            Level(54);
            foreach (var size in sizes)
            {
                Size(size);
                yield return PortalsShot("mid55_" + W + "x" + H, C.MainLevelId(55));
            }
        }

        private IEnumerator PortalsShot(string name, string nextId)
        {
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.3f);
            _app.Open("portals");
            yield return Wait(0.9f);
            var screen = (PortalsScreen)_app.Router.Current;
            Assert.That(screen.SelectedLevel.Id, Is.EqualTo(nextId));
            // the next level is on screen (the map scrolled to it)
            var node = screen.Root.GetComponentsInChildren<RectTransform>(true).First(r => r.name == "Level:" + nextId);
            var c = new Vector3[4];
            node.GetWorldCorners(c);
            float y = RectTransformUtility.WorldToScreenPoint(_cam, (c[0] + c[2]) / 2).y;
            Assert.That(y, Is.InRange(0, H), name + ": the next portal is visible");
            yield return Shot(name);
        }

        private void Level(int n)
        {
            S.Counters[Requirements.MainLevelCounter] = n;
            for (int i = 1; i <= n; i++)
                S.Run.Levels[C.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, i % 3 != 0, i % 4 == 0 } };
            _svc.Refresh(_ => { });
            foreach (var id in S.Onboarding.Unlocked.Keys.ToList()) S.Onboarding.Celebrated.Add(id);
            _svc.Refresh(_ => { });
        }

        private void Size(string wh)
        {
            var p = wh.Split('x');
            SetupCapture(int.Parse(p[0]), int.Parse(p[1]));
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
            Ui.Stretch(_app.Root, 0, 0, 0, 0);
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

        [TearDown]
        public void TearDown()
        {
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
