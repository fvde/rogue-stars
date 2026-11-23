using System;
using System.Collections;
using System.IO;
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
    /// Renders the gear screens (Worm tab, parts inventory, part detail, Ore explainer, resources) for the player of the
    /// owner's playtest: main level 2, Parts just unlocked (starter parts + the unlock's Ore), plus a populated inventory.
    /// Explicit (run with <c>-testFilter GearShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR or
    /// client/RogueStars/Screenshots/gear. Needs a graphics device: run in batchmode WITHOUT -nographics.
    /// </summary>
    [Explicit("renders gear screenshots; run with -testFilter GearShowcaseTests")]
    public sealed class GearShowcaseTests
    {
        private const int W = 1080, H = 2340;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir;
        private string _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;

        [UnityTest, Timeout(600000)]
        public IEnumerator Gear()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "gear_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_gear", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "gear"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.3f);

            // Level 2 reached: the onboarding settler unlocks Parts (starter parts + the unlock's Ore leave the stash).
            _svc.State.Counters[Requirements.MainLevelCounter] = 2;
            _svc.DevSetFlag("gear_showcase", false); // settles
            foreach (var id in OnboardingSystem.PendingCelebrations(_svc.State, _svc.Content).ToArray())
                _svc.ExecuteNow(new FeatureCelebrate { FeatureId = id });
            _svc.Refresh(_ => { });
            Assert.That(Features.IsUnlocked(_svc.State, _svc.Content, Features.Parts), Is.True);

            _app.Router.Reset(new HubScreen());
            yield return Wait(0.3f);
            yield return Shot("g01_hub_level2");
            _app.Open("worm");
            yield return Wait(0.3f);
            yield return Shot("g02_worm_level2");
            var worm = (WormScreen)_app.Router.Current;
            foreach (var cmd in worm.AutoEquipCommands()) _svc.ExecuteNow(cmd);
            _svc.Refresh(_ => { });
            yield return Wait(0.3f);
            yield return Shot("g02b_worm_equipped");
            OpenSlot("Head");
            yield return Wait(0.3f);
            yield return Shot("g03_worm_slot_tap");
            worm.Detail(_svc.State.Parts.Equipped[PartSlot.Head]);
            yield return Wait(0.3f);
            yield return Shot("g05_part_detail_level2");
            Ui.Clear(_app.ModalLayer);
            _app.Bar.ShowCurrency(Currencies.Ore);
            yield return Wait(0.3f);
            yield return Shot("g05b_ore_explainer");
            Ui.Clear(_app.ModalLayer);

            _app.Router.Reset(new HubScreen());
            yield return Wait(0.3f);
            yield return Shot("g01b_hub_upgrade_badge");
            _app.Open("parts");
            yield return Wait(0.3f);
            yield return Shot("g04_parts_level2");
            _app.Bar.ShowDetails();
            yield return Wait(0.3f);
            yield return Shot("g06_resources_level2");
            Ui.Clear(_app.ModalLayer);

            // A populated inventory: a few dozen parts of every slot and several rarities.
            var defs = new System.Collections.Generic.List<string>(_svc.Content.PartsConfig.Defs.Keys);
            defs.Sort(string.CompareOrdinal);
            var grants = new System.Collections.Generic.List<Reward>();
            for (int i = 0; i < defs.Count; i++) grants.Add(new Reward(RewardKinds.Part, defs[i], 1 + i % 2, i % 5));
            _svc.DevGrant(grants);
            _app.Router.Reset(new HubScreen());
            _app.Open("worm");
            yield return Wait(0.3f);
            yield return Shot("g07_worm_populated");
            _app.Router.Reset(new HubScreen());
            _app.Open("parts");
            yield return Wait(0.3f);
            yield return Shot("g08_parts_populated");
        }

        private void OpenSlot(string slot)
        {
            foreach (var b in _app.Router.Current.Root.GetComponentsInChildren<Button>(true))
                if (b.name == "Slot:" + slot) { b.onClick.Invoke(); return; }
        }

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("GearCamera");
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
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
