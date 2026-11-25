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
    /// "The crash site comes first" (owner, 2026-11-25) at 1080×2400: the hub before the Aliens, the Aliens unlock moment (with the
    /// Launch Pod panels), the hub's crash site with the squad, the pod's card, the City unlock moment and the City right after it.
    /// Explicit (<c>-testFilter CrashSiteShowcaseTests</c>, needs graphics); writes to $ROGUESTARS_SHOTS_DIR or
    /// client/RogueStars/Screenshots/crashsite.
    /// </summary>
    [Explicit("renders crash-site screenshots; run with -testFilter CrashSiteShowcaseTests")]
    public sealed class CrashSiteShowcaseTests
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
        public IEnumerator CrashSite()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "crash_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_crash", "Wiggles", new ManualClock(T0), 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "crashsite"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.3f);

            // level 5: everything before the Aliens seen
            Reach(5);
            CelebrateAll();
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.6f);
            Assert.That(((HubScreen)_app.Router.Current).PodButton, Is.Null);
            yield return Shot("crash_01_hub_before_aliens");

            // level 6: the Aliens moment, now with the Launch Pod (one moment)
            Reach(6);
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.3f);
            if (!(_app.Router.Current is UnlockScreen)) ((HubScreen)_app.Router.Current).ShowPendingUnlock();
            var moment = (UnlockScreen)_app.Router.Current;
            Assert.That(moment.FeatureIds, Is.EqualTo(new[] { Features.Aliens, Features.AlienSquad }));
            for (int p = 0; p < moment.PageCount; p++)
            {
                moment.ShowPage(p);
                yield return Wait(0.7f);
                yield return Shot("crash_02_aliens_intro_" + (p + 1));
            }
            moment.Dismiss();
            yield return Wait(0.3f);

            // the hub: fireplace, saucer, the pod and the squad walking around (a second alien from the free pull waits for a seat)
            _svc.DevGrant(new List<Reward> { new Reward(RewardKinds.Alien, "alien_zib", 1), Reward.Cur(Currencies.Biomass, 400) });
            CelebrateAll();
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.4f);
            var hub = (HubScreen)_app.Router.Current;
            Assert.That(hub.PodButton, Is.Not.Null);
            Assert.That(hub.Squad, Is.Not.Null);
            yield return Wait(2.5f);
            yield return Shot("crash_03_hub_crash_site_squad");

            // the pod's card
            hub.OpenPodCard();
            yield return Wait(0.6f);
            Assert.That(hub.PodCard.IsOpen, Is.True);
            yield return Shot("crash_04_pod_card");
            hub.PodCard.Close();

            // level 15: the City moment (the crash site grows into a City), then the City with the pod already standing
            Reach(15);
            foreach (var id in S.Onboarding.Unlocked.Keys.ToList()) if (id != Features.BiomassFarm) S.Onboarding.Celebrated.Add(id);
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.3f);
            if (!(_app.Router.Current is UnlockScreen)) ((HubScreen)_app.Router.Current).ShowPendingUnlock();
            var city = (UnlockScreen)_app.Router.Current;
            Assert.That(city.FeatureIds, Is.EqualTo(new[] { Features.BiomassFarm }));
            yield return Wait(0.7f);
            yield return Shot("crash_05_city_unlock_intro_1");
            city.ShowPage(1);
            yield return Wait(0.7f);
            yield return Shot("crash_05_city_unlock_intro_2");
            city.ShowPage(city.PageCount - 1);
            yield return Wait(0.5f);
            city.Claim();
            yield return Wait(0.6f);
            var cs = (CityScreen)_app.Router.Current;
            var pod = CitySystem.LaunchPod(S);
            cs.Focus(pod.X + 0.5f, pod.Y + 0.5f, CityWorld.OverviewWidth);
            yield return Wait(3f);
            yield return Shot("crash_06_city_after_unlock");
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
