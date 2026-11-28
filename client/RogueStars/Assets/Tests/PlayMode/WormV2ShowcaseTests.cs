using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Renders the parts v2 Worm screen (board "New · worm") at 1080×2400 and 720×1600: a new player, a mid-game player with
    /// Mythics, the part sheet (Common, Legendary, Mythic S with its locked 5th skill), the Climb picker, Feed spares and
    /// the Replicator. Explicit (<c>-testFilter WormV2ShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR or
    /// client/RogueStars/Screenshots/wormv2. Needs a graphics device: batchmode WITHOUT -nographics.
    /// </summary>
    [Explicit("renders worm v2 screenshots; run with -testFilter WormV2ShowcaseTests")]
    public sealed class WormV2ShowcaseTests
    {
        private const long T0 = 1_772_452_800_000L;
        private static readonly (int w, int h)[] Sizes = { (1080, 2400), (720, 1600) };
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;
        private int _w, _h;

        private GameState S => _svc.State;

        [UnityTest, Timeout(900000)]
        public IEnumerator WormV2()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "wormv2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_wormv2", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "wormv2"));
            Directory.CreateDirectory(_shots);
            yield return Wait(0.3f);

            // ---- a new player: main level 2, Parts just unlocked, the starter set worn ----
            S.Counters[Requirements.MainLevelCounter] = 2;
            _svc.DevSetFlag("wormv2_showcase", false);
            foreach (var id in OnboardingSystem.PendingCelebrations(S, _svc.Content).ToArray()) _svc.ExecuteNow(new FeatureCelebrate { FeatureId = id });
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
                foreach (var uid in PartsSystem.SortedPartUids(S))
                    if (!PartsSystem.IsEquipped(S, uid) && _svc.ExecuteNow(new PartEquip { Uid = uid, Slot = slot }).Ok) break;
            _svc.Refresh(_ => { });
            foreach (var size in Sizes)
            {
                SetupCapture(size.w, size.h);
                OpenWorm();
                yield return Wait(0.4f);
                yield return Shot("w01_worm_new_player");
            }

            // ---- mid-game: Mythics on the ladder, a full bag ----
            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            foreach (var slot in new List<PartSlot>(S.Parts.Equipped.Keys)) _svc.ExecuteNow(new PartUnequip { Slot = slot });
            string Give(string def, PartRarity r, int lv = 1)
            {
                var before = new HashSet<string>(S.Parts.Items.Keys);
                _svc.DevGrant(new[] { new Reward(RewardKinds.Part, def, 1, (int)r) });
                foreach (var k in S.Parts.Items.Keys)
                    if (!before.Contains(k)) { S.Parts.Items[k].Level = lv; return k; }
                return null;
            }
            var head = Give("myth_cinder_crown", PartRarity.Mythic1, 64);
            var body = Give("myth_rimeguard_plate", PartRarity.MythicS, 82);
            var left = Give("myth_firebrand_blaster", PartRarity.MythicS, 85);
            var right = Give("l_arm_fury", PartRarity.Legendary, 48);
            var legs = Give("e_legs_swift", PartRarity.Epic, 26);
            var tail = Give("myth_storm_coil", PartRarity.Mythic, 70);
            _svc.ExecuteNow(new PartEquip { Uid = head, Slot = PartSlot.Head });
            _svc.ExecuteNow(new PartEquip { Uid = body, Slot = PartSlot.Body });
            _svc.ExecuteNow(new PartEquip { Uid = left, Slot = PartSlot.LeftArm });
            _svc.ExecuteNow(new PartEquip { Uid = right, Slot = PartSlot.RightArm });
            _svc.ExecuteNow(new PartEquip { Uid = legs, Slot = PartSlot.Legs });
            _svc.ExecuteNow(new PartEquip { Uid = tail, Slot = PartSlot.Tail });
            // spares: climb copies, other Mythics, standard parts of every rarity
            Give("myth_firebrand_blaster", PartRarity.Mythic); Give("myth_firebrand_blaster", PartRarity.Mythic);
            Give("myth_shatter_cannon", PartRarity.Mythic); Give("myth_chainlash_whip", PartRarity.Mythic);
            Give("myth_cinder_crown", PartRarity.Mythic); Give("myth_deadeye_visor", PartRarity.Mythic);
            var legendary = Give("l_head_keen", PartRarity.Legendary, 12);
            Give("l_body_mend", PartRarity.Legendary, 3);
            Give("e_tail_fury", PartRarity.Epic, 5); Give("e_arm_swift", PartRarity.Epic); Give("e_head_guard", PartRarity.Epic);
            Give("r_body_guard", PartRarity.Rare); Give("r_legs_keen", PartRarity.Rare, 4); Give("r_arm_strike", PartRarity.Rare); Give("r_head_mend", PartRarity.Rare);
            var common = Give("c_tail_strike", PartRarity.Common, 3);
            Give("c_legs_guard", PartRarity.Common); Give("c_head_swift", PartRarity.Common); Give("c_arm_keen", PartRarity.Common);
            // runes in a few sockets
            var runeIds = new List<string>(_svc.Content.PartsConfig.Runes.Keys);
            runeIds.Sort(string.CompareOrdinal);
            for (int i = 0; i < 5; i++) _svc.DevGrant(new[] { new Reward(RewardKinds.Rune, runeIds[i], 1, i % 4) });
            var runes = new List<string>(S.Parts.Runes.Keys);
            runes.Sort(string.CompareOrdinal);
            _svc.ExecuteNow(new RuneSocket { RuneUid = runes[0], PartUid = left, SlotIndex = 0 });
            _svc.ExecuteNow(new RuneSocket { RuneUid = runes[1], PartUid = left, SlotIndex = 1 });
            _svc.ExecuteNow(new RuneSocket { RuneUid = runes[2], PartUid = body, SlotIndex = 0 });
            _svc.ExecuteNow(new RuneSocket { RuneUid = runes[3], PartUid = right, SlotIndex = 0 });
            _svc.DevGrant(new[] { Reward.Cur(Currencies.Ore, 152_000), Reward.Cur(Currencies.Metal, 640), Reward.Cur(Currencies.Diamonds, 640) });
            _svc.Refresh(_ => { });

            foreach (var size in Sizes)
            {
                SetupCapture(size.w, size.h);
                var worm = OpenWorm();
                yield return Wait(0.4f);
                yield return Shot("w02_worm_midgame");
                worm.Detail(common);
                yield return Shot("w03_sheet_common");
                worm.Detail(legendary);
                yield return Shot("w04_sheet_legendary");
                worm.Detail(left);
                yield return Shot("w05_sheet_mythic_s");
                ScrollSheet(0);
                yield return Shot("w05b_sheet_mythic_s_skills");
                worm.OpenMergePicker(left, false);
                yield return Shot("w06_climb_picker");
                Ui.Clear(_app.ModalLayer);
                worm.OpenFeed();
                yield return Shot("w07_feed_spares");
                worm.OpenReplicator();
                yield return Shot("w08_replicator_tab");
                worm.OpenMergePicker(left, true);
                yield return Shot("w09_replicator_picker");
                Ui.Clear(_app.ModalLayer);
                worm.SetTab(WormScreen.WormTab.Levels);
                yield return Shot("w10_worm_levels");
                worm.SetTab(WormScreen.WormTab.Parts);
            }
        }

        private WormScreen OpenWorm()
        {
            Ui.Clear(_app.ModalLayer);
            _app.Router.Reset(new HubScreen());
            _app.Open("worm");
            return (WormScreen)_app.Router.Current;
        }

        /// <summary>Scroll the open sheet's body to the bottom (normalized 0).</summary>
        private void ScrollSheet(float pos)
        {
            foreach (var sr in _app.ModalLayer.GetComponentsInChildren<ScrollRect>(true))
                if (sr.transform.parent != null && sr.transform.parent.name == "ScrollFrame") { Canvas.ForceUpdateCanvases(); sr.verticalNormalizedPosition = pos; }
        }

        private void SetupCapture(int w, int h)
        {
            if (_rt != null) _rt.Release();
            _w = w; _h = h;
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            if (_cam == null)
            {
                var go = new GameObject("WormV2Camera");
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
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
