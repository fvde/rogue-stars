using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    /// Captures the run screen's motion at 1080×2340: the HUD, the dice tumbling, the worm mid-hop, the landing pop with
    /// its popups (a buff tile, then a supply cache with flying coins), and a two-dice roll. Explicit (run with
    /// <c>-testFilter RunMotionShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/run-motion.
    /// Needs a graphics device: run in batchmode WITHOUT -nographics.
    /// </summary>
    [Explicit("renders run motion screenshots; run with -testFilter RunMotionShowcaseTests")]
    public sealed class RunMotionShowcaseTests
    {
        private const int W = 1080, H = 2340;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;

        [UnityTest, Timeout(300000)]
        public IEnumerator RunMotion()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "runmotion_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_motion", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "run-motion"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.3f);

            var c = _svc.Content;
            var level = c.MainLevel(1);
            Assert.That(_svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            var run = _svc.State.Run.Active;
            run.Tiles.Clear();
            var ids = new List<string> { "start" };
            for (int i = 0; i < 6; i++) ids.Add("buff_atk");
            for (int i = 0; i < 6; i++) ids.Add("loot");
            for (int i = 0; i < 8; i++) ids.Add(i % 2 == 0 ? "event" : "enemy_1");
            for (int i = 0; i < ids.Count; i++) { var t = new RunTile { Id = i, TileId = ids[i], X = i }; if (i + 1 < ids.Count) t.Next.Add(i + 1); run.Tiles.Add(t); }
            run.BoardEnd = ids.Count - 1; run.BossGateTile = -1; run.Position = 0; run.Trail.Clear(); run.Trail.Add(0);
            run.Skills.Add("fire_ember");
            _app.Router.Push(new RunScreen());
            var screen = (RunScreen)_app.Router.Current;
            yield return Wait(0.8f);
            yield return Shot("01_hud_board");

            // roll 1: dice tumbling → mid-hop → landing pop on a buff
            yield return Roll(screen, "02");

            // roll 2 (from a buff tile into the supply caches): coins fly to the coin chip
            if (_svc.State.Run.Active.Phase == RunPhase.AwaitRoll) yield return Roll(screen, "03");

            // a two-dice roll
            var a = _svc.State.Run.Active;
            if (a != null && a.Phase == RunPhase.AwaitRoll)
            {
                a.Dice = 2;
                _app.Session.Run(new RunRoll());
                yield return Wait(0.5f);
                yield return Shot("04_two_dice");
            }
            yield return Wait(2f);
            yield return Shot("05_after");
        }

        private IEnumerator Roll(RunScreen screen, string prefix)
        {
            int landed = screen.LastLandedTile;
            _app.Session.Run(new RunRoll());
            yield return Wait(0.45f);
            yield return Shot(prefix + "a_dice_tumbling");
            // wait for the hops (dice ~1.25 s), capture one mid-hop
            yield return Wait(0.95f);
            yield return Shot(prefix + "b_mid_hop");
            float end = Time.realtimeSinceStartup + 4;
            while (screen.LastLandedTile == landed && Time.realtimeSinceStartup < end) yield return null;
            yield return Wait(0.12f);
            yield return Shot(prefix + "c_landing_pop");
            yield return Wait(0.5f);
            yield return Shot(prefix + "d_popups");
            end = Time.realtimeSinceStartup + 4;
            while ((screen.Animating || _app.Router.Current is BattlePlaybackScreen) && Time.realtimeSinceStartup < end)
            {
                if (_app.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); b.Continue(); }
                yield return null;
            }
            Ui.Clear(_app.ModalLayer);
            yield return Wait(0.6f);
        }

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("MotionCamera");
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
