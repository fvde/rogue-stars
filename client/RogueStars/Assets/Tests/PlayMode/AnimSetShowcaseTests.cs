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
using RogueStars.Client.World.Anim;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RogueStars.Client.PlayTests
{
    /// <summary>The animation level control in Settings and the hub worm mid-celebrate (hearts), at 1080 x 2400, into $ROGUESTARS_SHOTS_DIR.</summary>
    [Explicit("renders the animation settings screenshots; run with -testFilter AnimSetShowcaseTests")]
    public sealed class AnimSetShowcaseTests
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
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null; yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "animset_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_animset", "Wiggles", new ManualClock(T0), 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "anim-settings"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            AnimSettings.Override = null;
            AnimSettings.Level = AnimLevel.Auto;
            AnimSettings.Device = new DeviceSpecs(8000, 8, 4000);
            AnimSettings.ResetCache();
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            var c = _svc.Content;
            _svc.State.Counters[Requirements.MainLevelCounter] = 12;
            for (int i = 1; i <= 12; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.State.Flags.Add(Features.DevUnlockAllFlag);
            foreach (var f in c.FeatureDefs.Keys) _svc.State.Onboarding.Celebrated.Add(f);
            var rewards = new List<Reward>();
            foreach (var a in c.AlienTypes.Keys.OrderBy(k => k).Take(6)) rewards.Add(new Reward(RewardKinds.Alien, a, 1));
            _svc.DevGrant(rewards);
            for (int i = 0; i < 3; i++) { var al = _svc.State.Aliens.Owned.Keys.OrderBy(k => k).Skip(i).First(); if (!_svc.State.Aliens.Squad.Contains(al)) _svc.State.Aliens.Squad.Add(al); }
            _svc.Refresh(_ => { });

            // 1. Settings with the control (Auto (Full) on this fake strong device), then Reduced
            _app.Router.Push(new SettingsScreen());
            yield return Wait(0.6f);
            ScrollToAnim();
            yield return Shot("settings_animation_auto");
            AnimSettings.Level = AnimLevel.Reduced;
            _app.Router.RebuildCurrent();
            yield return Wait(0.4f);
            ScrollToAnim();
            yield return Shot("settings_animation_reduced");
            AnimSettings.Level = AnimLevel.Auto;
            _app.Router.Pop();

            // 2. the hub worm mid-celebrate
            HubScreen.SkyOverride = "day";
            _app.Router.Reset(new HubScreen());
            yield return Wait(1.0f);
            var hub = (HubScreen)_app.Router.Current;
            hub.WormTap.Tap(Time.unscaledTime);
            yield return null;
            hub.WormTap.Creature.Tick(0.3f); // mid-hop (frames are slow in batch mode: drive the clip)
            yield return ShotNow("hub_worm_celebrate");
            foreach (var kv in hub.Squad.Taps) { kv.Value.Tap(Time.unscaledTime); kv.Value.Creature.Tick(0.25f); break; }
            yield return null;
            hub.WormTap.Creature.Tick(0.2f);
            yield return ShotNow("hub_worm_and_alien_celebrate");
        }

        private IEnumerator ShotNow(string name)
        {
            Canvas.ForceUpdateCanvases();
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(_shots, name + ".png"), _tex.EncodeToPNG());
            yield break;
        }

        private void ScrollToAnim()
        {
            Canvas.ForceUpdateCanvases();
            var sr = _app.Router.Current.Root.GetComponentInChildren<ScrollRect>(true);
            RectTransform card = null;
            foreach (var t in _app.Router.Current.Root.GetComponentsInChildren<RectTransform>(true)) if (t.name == "AnimCard") card = t;
            if (sr == null || card == null) return;
            sr.content.anchoredPosition = new Vector2(0, -card.anchoredPosition.y - 250);
        }

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("AnimShotCamera");
            _cam = go.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.orthographic = true;
            _cam.cullingMask = ~(1 << WorldBoard.Layer | 1 << CityWorld.Layer);
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
            _app.Feedback.Tick(0.016f);
            yield return null; yield return null;
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
            HubScreen.SkyOverride = null;
            AnimSettings.Override = null; AnimSettings.Device = null; AnimSettings.Level = AnimLevel.Auto;
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
