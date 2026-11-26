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
    /// Daily Quests (owner, 2026-11-26) at 1080×2400: the level 1 unlock moment (Parts only, no quests moment), the screen at level 1 and 12
    /// with points only and the chest bar, the weekly and achievements tabs and the daily top chest opening (three tickets flip out).
    /// Explicit (<c>-testFilter QuestsShowcaseTests</c>, needs graphics); writes to $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/quests.
    /// </summary>
    [Explicit("renders the Daily Quests screenshots; run with -testFilter QuestsShowcaseTests")]
    public sealed class QuestsShowcaseTests
    {
        private const int W = 1080, H = 2400;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;

        private GameState S => _svc.State;
        private ContentDb C => _svc.Content;

        [UnityTest, Timeout(600000)]
        public IEnumerator Quests()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "quests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_quests", "Wiggles", new ManualClock(T0), 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "quests"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.3f);

            // day 1, before level 1: nothing but the play button
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.5f);
            yield return Shot("quests_01_hub_before_level1");

            // level 1 cleared: the one unlock moment is Parts (no moment for the quests), then the hub shows the Daily Quests button
            Reach(1);
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.3f);
            if (!(_app.Router.Current is UnlockScreen)) ((HubScreen)_app.Router.Current).ShowPendingUnlock();
            var moment = (UnlockScreen)_app.Router.Current;
            Assert.That(moment.FeatureIds, Is.EqualTo(new[] { Features.Parts }));
            yield return Wait(0.6f);
            yield return Shot("quests_02_unlock_moment_parts_only");
            moment.Dismiss();
            yield return Wait(0.3f);
            CelebrateAll();
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.8f);
            yield return Shot("quests_03_hub_with_daily_quests_button");

            // the Daily Quests screen at level 1: points only, the chest bar (two chests ready)
            S.Count("login_days"); S.Count("runs_completed", 3); S.Count("run_skills_drafted", 6);
            _svc.ExecuteNow(new QuestClaimAll());
            S.Count("run_battles_won", 4);
            _app.Open("quests");
            yield return Wait(0.8f);
            yield return Shot("quests_04_daily_quests_level1");
            yield return OpenTab("Weekly");
            yield return Shot("quests_05_weekly_level1");
            yield return OpenTab("Achieve");
            yield return Shot("quests_06_achievements_level1");
            _app.Router.Pop();

            // level 12: the Shop quests appear (the ad quest stays hidden: ads are disabled here)
            Reach(12);
            CelebrateAll();
            _app.Open("quests");
            yield return Wait(0.8f);
            yield return Shot("quests_07_daily_quests_level12");
            _app.Router.Pop();

            // the daily top chest at level 15 (all three Signals open): the three tickets flip out
            Reach(15);
            CelebrateAll();
            S.Quests.DailyPoints = 100;
            _app.Open("quests");
            yield return Wait(0.5f);
            yield return Shot("quests_08_chests_ready");
            var screen = (QuestsScreen)_app.Router.Current;
            PullReveal.AnimateInEditMode = true;
            screen.OpenChest(PointTrack.Daily, C.PointChests.First(c => c.RandomTokens > 0));
            for (int i = 0; i < 8; i++)
            {
                yield return Wait(0.7f);
                yield return Shot("quests_09_chest_open_" + i);
            }
        }

        private IEnumerator OpenTab(string name)
        {
            var b = _app.Router.Current.Root.GetComponentsInChildren<Button>(true).First(x => x.GetComponentInChildren<Text>() != null && x.GetComponentInChildren<Text>().text == name);
            b.onClick.Invoke();
            yield return Wait(0.6f);
        }

        private void Reach(int level)
        {
            S.Counters[Requirements.MainLevelCounter] = level;
            _svc.Refresh(_ => { });
        }

        private void CelebrateAll()
        {
            foreach (var id in S.Onboarding.Unlocked.Keys.ToList()) S.Onboarding.Celebrated.Add(id);
        }

        // ---- capture (as CityShowcaseTests) ---------------------------------------------------------------------------------

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("CrashShotCamera");
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

        [TearDown]
        public void TearDown()
        {
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
