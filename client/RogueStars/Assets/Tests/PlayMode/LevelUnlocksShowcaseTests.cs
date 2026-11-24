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
    /// GDD §16 level unlocks in play mode: the tutorial run's coach marks (roll, pick a skill, the boss), two unlock screens
    /// with their animated intro panels (Shops: tap, Two Dice: spin) and the Laboratory's "?" replay; lu_*.png at
    /// 1080 × 2400 into $ROGUESTARS_SHOTS_DIR (or client/RogueStars/Screenshots/unlocks, git-ignored). Checks the flow too:
    /// the panels animate (the art moves between two frames), the replay claims nothing. Needs a graphics device: run
    /// WITHOUT -nographics (<c>-testFilter LevelUnlocksShowcaseTests</c>).
    /// </summary>
    [Explicit("renders the level-unlock screenshots; run with -testFilter LevelUnlocksShowcaseTests")]
    public sealed class LevelUnlocksShowcaseTests
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
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "unlocks_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_unlocks", "Wiggles", new ManualClock(T0), 5);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "unlocks"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);
            var c = _svc.Content;

            // 1) the tutorial run: roll
            var l1 = c.MainLevel(1);
            Assert.That(_svc.ExecuteNow(new RunStart { PortalId = l1.PortalId, LevelId = l1.Id, Mode = RunMode.Normal }).Ok, Is.True);
            _app.Router.Push(new RunScreen());
            yield return Wait(1.2f);
            var rs = (RunScreen)_app.Router.Current;
            Assert.That(rs.CoachMark, Is.EqualTo(CoachMarks.Roll));
            yield return Shot("lu_tutorial_1_roll");

            // 2) a level-up: pick a skill
            _app.Router.Pop();
            var run = _svc.State.Run.Active;
            run.Phase = RunPhase.Draft; run.MythicDraft = false; run.PendingDrafts = 1;
            run.DraftOffer.Clear(); run.DraftOffer.AddRange(new[] { "crit_keen", "gen_power", "gen_guard" });
            _app.Router.Push(new RunScreen());
            yield return Wait(1.2f);
            Assert.That(((RunScreen)_app.Router.Current).CoachMark, Is.EqualTo(CoachMarks.Pick));
            yield return Shot("lu_tutorial_2_pick");

            // 3) the boss
            Ui.Clear(_app.ModalLayer);
            _app.Router.Pop();
            run = _svc.State.Run.Active;
            run.Phase = RunPhase.Encounter; run.EncounterKind = "boss"; run.Awakening = 100;
            _app.Router.Push(new RunScreen());
            yield return Wait(1.2f);
            Assert.That(((RunScreen)_app.Router.Current).CoachMark, Is.EqualTo(CoachMarks.Boss));
            yield return Shot("lu_tutorial_3_boss");
            _app.Router.Pop();
            _svc.ExecuteNow(new RunAbandon());
            Ui.Clear(_app.ModalLayer);

            // 4) unlock screens: Shops (run content, a tapping hand), Two Dice & Forks (spinning dice)
            _svc.State.Counters[Requirements.MainLevelCounter] = 2;
            _svc.State.Run.Levels[c.MainLevelId(1)] = new LevelProgress { Cleared = true, Clears = 1 };
            _svc.State.Run.Levels[c.MainLevelId(2)] = new LevelProgress { Cleared = true, Clears = 1 };
            _svc.Refresh(_ => { });
            _svc.ExecuteNow(new FeatureCelebrate { FeatureId = Features.Parts });
            var hub = new HubScreen();
            _app.Router.Reset(hub);
            yield return Wait(0.3f);
            if (!(_app.Router.Current is UnlockScreen)) Assert.That(hub.ShowPendingUnlock(), Is.True); // the hub opens it by itself
            var moment = (UnlockScreen)_app.Router.Current;
            Assert.That(moment.FeatureIds, Is.EqualTo(new[] { Features.RunShop }));
            var art = moment.Root.GetComponentsInChildren<Image>(true).First(i => i.name == "IntroArt").rectTransform;
            Vector2 a0 = art.anchoredPosition;
            yield return Wait(0.45f);
            yield return Shot("lu_unlock_shops_1");
            Assert.That(Vector2.Distance(a0, art.anchoredPosition), Is.GreaterThan(1), "the shop hops (bounce)");
            Click("IntroNext");
            yield return Wait(0.9f);
            yield return Shot("lu_unlock_shops_2");
            Click("IntroNext");
            Assert.That(moment.OnLastPage, Is.True);
            var hand = moment.Root.GetComponentsInChildren<Image>(true).First(i => i.name == "Hand").rectTransform;
            Vector2 h0 = hand.anchoredPosition;
            yield return Wait(0.25f); // mid-press (a tap takes 1.4 s)
            Assert.That(Vector2.Distance(h0, hand.anchoredPosition), Is.GreaterThan(1), "the hand taps");
            yield return Shot("lu_unlock_shops_3_last");
            moment.Claim();
            yield return Wait(0.3f);

            _svc.State.Counters[Requirements.MainLevelCounter] = 5;
            for (int i = 3; i <= 5; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1 };
            _svc.Refresh(_ => { });
            foreach (var id in new[] { Features.WormLevel, Features.RunMiniboss }) _svc.ExecuteNow(new FeatureCelebrate { FeatureId = id });
            hub = new HubScreen();
            _app.Router.Reset(hub);
            yield return Wait(0.3f);
            if (!(_app.Router.Current is UnlockScreen)) Assert.That(hub.ShowPendingUnlock(), Is.True); // the hub opens it by itself
            moment = (UnlockScreen)_app.Router.Current;
            Assert.That(moment.FeatureIds, Is.EqualTo(new[] { Features.RunTwoDice }));
            yield return Wait(0.7f);
            var dice = moment.Root.GetComponentsInChildren<Image>(true).First(i => i.name == "IntroArt").rectTransform;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(0, dice.localEulerAngles.z)), Is.GreaterThan(5), "the dice turn");
            yield return Shot("lu_unlock_two_dice_1");
            moment.ShowPage(moment.PageCount - 1);
            yield return Wait(0.6f);
            yield return Shot("lu_unlock_two_dice_last");
            moment.Claim();
            yield return Wait(0.3f);

            // 5) "?" on the Laboratory replays its intro, claims nothing
            _svc.State.Onboarding.Unlocked[Features.Laboratory] = _svc.NowMs;
            _svc.State.Onboarding.Celebrated.Add(Features.Laboratory);
            _app.Router.Reset(new HubScreen());
            _app.Open("laboratory");
            yield return Wait(0.6f);
            var lab = _app.Router.Current;
            Assert.That(lab.HelpButton != null && lab.HelpButton.gameObject.activeInHierarchy, Is.True);
            yield return Shot("lu_lab_help_button");
            long dia = _svc.State.Wallet.Get(Currencies.Diamonds);
            lab.HelpButton.onClick.Invoke();
            yield return Wait(0.8f);
            var replay = (UnlockScreen)_app.Router.Current;
            Assert.That(replay.IsReplay, Is.True);
            yield return Shot("lu_replay_lab_1");
            Click("IntroSkip");
            yield return Wait(0.6f);
            yield return Shot("lu_replay_lab_last");
            Click("IntroDone");
            yield return Wait(0.3f);
            Assert.That(_app.Router.Current, Is.SameAs(lab));
            Assert.That(_svc.State.Wallet.Get(Currencies.Diamonds), Is.EqualTo(dia));
            Debug.Log("[LevelUnlocks] screenshots in " + _shots);
        }

        [TearDown]
        public void TearDown()
        {
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null && Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private void Click(string name)
        {
            var b = _app.Router.Current.Root.GetComponentsInChildren<Button>(true).FirstOrDefault(x => x.name == name);
            Assert.That(b, Is.Not.Null, name);
            b.onClick.Invoke();
        }

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("UnlocksCamera");
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
            if (_app.Router.Current is RunScreen run && run.World != null && run.World.Cam.enabled) run.World.Cam.Render();
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
