using System;
using System.Collections;
using System.IO;
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
    /// Renders the home on the board "New · home" at 1080×2400 and 720×1600: a new player and a mid-game player (season pass,
    /// a live event and an invasion with timers, every rail entry), and each sky (day, night, event, invasion) with the calm
    /// backdrop treatment off ("before") and on ("after"). Explicit (<c>-testFilter HomeV2ShowcaseTests</c>); writes to
    /// $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/homev2. Needs a graphics device: batchmode WITHOUT -nographics.
    /// </summary>
    [Explicit("renders home v2 screenshots; run with -testFilter HomeV2ShowcaseTests")]
    public sealed class HomeV2ShowcaseTests
    {
        private const long T0 = 1_772_452_800_000L;
        private static readonly (int w, int h)[] Sizes = { (1080, 2400), (720, 1600) };
        private ClientApp _app;
        private LocalGameService _svc;
        private ManualClock _clock;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;
        private int _w, _h;

        private GameState S => _svc.State;

        [UnityTest, Timeout(900000)]
        public IEnumerator HomeV2()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "homev2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _clock = new ManualClock(T0);
            _svc = new LocalGameService(_dir, "local_homev2", "Wiggles", _clock, 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "homev2"));
            Directory.CreateDirectory(_shots);
            yield return Wait(0.3f);

            // ---- a new player: nothing cleared yet (START, the planet, the Shop) ----
            HubScreen.SkyOverride = ArtCatalog.SkyDay;
            foreach (var size in Sizes)
            {
                SetupCapture(size.w, size.h);
                OpenHub();
                yield return Wait(0.4f);
                yield return Shot("h01_home_new_player");
            }

            // ---- mid-game: level 55 next, every feature, season pass, a live event and an invasion ----
            S.Counters[Requirements.MainLevelCounter] = 54;
            S.Run.MainArchive = new ProgressArchive { Full = 54 };
            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            foreach (var id in OnboardingSystem.PendingCelebrations(S, _svc.Content).ToArray()) _svc.ExecuteNow(new FeatureCelebrate { FeatureId = id });
            var next = RunSystem.NextMainLevel(S, _svc.Content);
            if (next != null) S.Run.LongestRolls.Add(new LevelRolls { LevelId = next.Id, Rolls = 21 });
            // a scheduled (non-invasion) live event running now
            foreach (var ev in _svc.Content.LiveOpsSchedule)
            {
                if (ev.Kind == LiveOpsKind.Invasion) continue;
                if (ev.Schedule.TryNext(_clock.NowMs, out long start, out long end)) { _clock.NowMs = start + (end - start) / 3; break; }
            }
            // a surprise invasion on top
            foreach (var inv in _svc.Content.Invasions.Values)
            {
                S.LiveOps.ServerInvasions.Add(new ServerInvasion { OccurrenceId = "inv_showcase", InvasionId = inv.Id, StartMs = _clock.NowMs - 3_600_000, EndMs = _clock.NowMs + 2 * 3_600_000 + 300_000 });
                break;
            }
            _svc.DevGrant(new[] { Reward.Cur(Currencies.Ore, 15_200), Reward.Cur(Currencies.Diamonds, 640) });
            S.Shop.SeasonQuestXp += 12 * Math.Max(1, _svc.Content.SeasonPass.XpPerLevel) + _svc.Content.SeasonPass.XpPerLevel / 2;
            _svc.Refresh(_ => { });
            foreach (var size in Sizes)
            {
                SetupCapture(size.w, size.h);
                OpenHub();
                yield return Wait(0.4f);
                yield return Shot("h02_home_midgame");
            }

            // ---- each sky before / after the calm treatment (mid-game, 1080×2400) ----
            SetupCapture(1080, 2400);
            foreach (var sky in ArtCatalog.SkyStates)
            {
                HubScreen.SkyOverride = sky;
                foreach (bool calm in new[] { false, true })
                {
                    HubBackdrop.Enabled = calm;
                    OpenHub();
                    yield return Wait(0.3f);
                    yield return Shot("h1_sky_" + sky + (calm ? "_after" : "_before"));
                }
            }
            HubBackdrop.Enabled = true;
            HubScreen.SkyOverride = null;
        }

        private HubScreen OpenHub()
        {
            Ui.Clear(_app.ModalLayer);
            _app.Router.Reset(new HubScreen());
            _app.Bar.Rebuild();
            return (HubScreen)_app.Router.Current;
        }

        private void SetupCapture(int w, int h)
        {
            if (_rt != null) _rt.Release();
            _w = w; _h = h;
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            if (_cam == null)
            {
                var go = new GameObject("HomeV2Camera");
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
            _tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        }

        private IEnumerator Shot(string name)
        {
            yield return null;
            yield return null;
            yield return Wait(0.25f);
            Canvas.ForceUpdateCanvases();
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, _w, _h), 0, 0);
            _tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(_shots, name + "_" + _w + "x" + _h + ".png"), _tex.EncodeToPNG());
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            HubBackdrop.Enabled = true;
            HubScreen.SkyOverride = null;
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
