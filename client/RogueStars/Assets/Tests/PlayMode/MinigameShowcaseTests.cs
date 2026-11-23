using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Minigames;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// Renders every run mini-game mid-play (its scripted player aiming Silver) plus one intro screen at 1080×2340 into
    /// $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/minigames. Explicit (run with -testFilter MinigameShowcaseTests);
    /// needs a graphics device: run in batchmode WITHOUT -nographics.
    /// </summary>
    [Explicit("renders mini-game screenshots; run with -testFilter MinigameShowcaseTests")]
    public sealed class MinigameShowcaseTests
    {
        private const int W = 1080, H = 2340;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;

        [UnityTest, Timeout(600000)]
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "minigames_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            var svc = new LocalGameService(_dir, "local_minigames", "Wiggles", new ManualClock(T0), 11);
            _app.Start(svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "minigames"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            var level = svc.Content.MainLevel(1);
            Assert.That(svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            yield return Wait(0.3f);

            int n = 0;
            foreach (var def in svc.Content.Minigames)
            {
                var run = svc.State.Run.Active;
                run.Phase = RunPhase.Minigame;
                run.Minigame.Started = true;
                run.Minigame.GameId = def.Id;
                run.Minigame.Seed = 7919;
                run.Minigame.Reward = n % 2 == 0 ? MinigameReward.Coins : MinigameReward.SkillRoll;
                MinigameHost.Autoplay = null;
                var modal = MinigameHost.Open(_app.ModalLayer, _app.Session);
                var host = modal.Root.GetComponent<MinigameHost>();
                if (n == 0) { yield return Wait(0.9f); yield return Shot("00_intro_" + def.Id); }
                MinigameHost.Autoplay = MinigameTier.Silver;
                host.StartCountdown();
                yield return Wait(MinigameHost.CountdownSeconds + Math.Min(5f, host.Game.TimeLimit * 0.3f));
                yield return Shot((++n).ToString("00") + "_" + def.Id);
                modal.Close();
                yield return null;
            }
            MinigameHost.Autoplay = null;
        }

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("ShowcaseCamera");
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
            MinigameHost.Autoplay = null;
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
