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
    /// The menu redesign (UI kit) at 1080 x 2400: worm (parts, levels, stats), shop, laboratory, aliens, quests. Explicit
    /// (<c>-testFilter MenuRedesignShowcaseTests</c>, needs graphics: run WITHOUT -nographics); <c>$ROGUESTARS_SHOTS_ONLY</c>
    /// picks groups (worm, shop, lab, aliens, quests); writes to <c>$ROGUESTARS_SHOTS_DIR</c> or client/RogueStars/Screenshots/menu.
    /// </summary>
    [Explicit("renders the menu redesign screenshots; run with -testFilter MenuRedesignShowcaseTests")]
    public sealed class MenuRedesignShowcaseTests
    {
        private const int W = 1080, H = 2400;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots, _only;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;

        private GameState S => _svc.State;
        private ContentDb C => _svc.Content;
        private bool Want(string g) => string.IsNullOrEmpty(_only) || _only.Split(',').Contains(g);

        [UnityTest, Timeout(900000)]
        public IEnumerator Menus()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _only = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_ONLY");
            _dir = Path.Combine(Application.temporaryCachePath, "menu_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_menu", "Wiggles", new ManualClock(T0), 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "menu"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            S.Counters[Requirements.MainLevelCounter] = 30;
            for (int i = 1; i <= 30; i++) S.Run.Levels[C.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            S.Flags.Add(Features.DevUnlockAllFlag);
            _svc.Refresh(_ => { });
            foreach (var id in S.Onboarding.Unlocked.Keys.ToList()) S.Onboarding.Celebrated.Add(id);

            if (Want("worm"))
            {
                // a populated inventory of every slot and several rarities, equipped
                var defs = new List<string>(C.PartsConfig.Defs.Keys);
                defs.Sort(string.CompareOrdinal);
                var grants = new List<Reward>();
                for (int i = 0; i < defs.Count; i++) grants.Add(new Reward(RewardKinds.Part, defs[i], 1 + i % 2, i % 5));
                _svc.DevGrant(grants);
                foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
                    foreach (var uid in PartsSystem.SortedPartUids(S))
                        if (!PartsSystem.IsEquipped(S, uid) && _svc.ExecuteNow(new PartEquip { Uid = uid, Slot = slot }).Ok) break;
                _svc.ExecuteNow(new WormLevelUp { Levels = 10 });
                _svc.Refresh(_ => { });
                _app.Router.Reset(new HubScreen());
                _app.Open("worm");
                yield return Wait(0.6f);
                yield return Shot("w1_worm_parts");
                yield return Scroll(1f);
                yield return Shot("w1b_worm_parts_scrolled");
                yield return Scroll(0f);
                yield return OpenTab("Levels");
                yield return Shot("w2_worm_levels");
                yield return Scroll(0f);
                yield return Scroll(1f);
                yield return Shot("w2b_worm_levels_scrolled");
                yield return OpenTab("Stats");
                yield return Shot("w3_worm_stats");
            }
            if (Want("shop"))
            {
                _app.Router.Reset(new HubScreen());
                _app.Open("shop");
                yield return Wait(0.6f);
                yield return Shot("s1_shop_offers");
                yield return Scroll(0f);
                yield return Shot("s1b_shop_offers_scrolled");
                foreach (var tab in new[] { "Diamonds", "Passes", "Forever", "Items", "Ads" })
                {
                    yield return OpenTab(tab);
                    yield return Shot("s2_shop_" + tab.ToLowerInvariant());
                }
            }
        }

        private IEnumerator Scroll(float pos)
        {
            var sr = _app.Router.Current.Root.GetComponentInChildren<ScrollRect>();
            if (sr != null) sr.verticalNormalizedPosition = pos;
            yield return Wait(0.3f);
        }

        private IEnumerator OpenTab(string name)
        {
            var b = _app.Router.Current.Root.GetComponentsInChildren<Button>(true).First(x => x.name == "Tab:" + name);
            b.onClick.Invoke();
            yield return Wait(0.6f);
        }

        // ---- capture (as QuestsShowcaseTests) --------------------------------------------------------------------------

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("MenuShotCamera");
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
