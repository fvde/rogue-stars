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
    /// Playtest fixes 4 pictures: the battle speed button at each speed, the worm facing in battle (also on its mount) and on the
    /// board, the hub Pass label, the pause cog and stamina at 0 regenerating. Uses only names that exist before and after the
    /// fixes (buttons are found by name) so the same test renders the "before" set. Explicit; run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the playtest-fixes-4 screenshots; run with -testFilter PlaytestFixes4ShowcaseTests")]
    public sealed class PlaytestFixes4ShowcaseTests
    {
        private const int W = 1080, H = 2340;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private LocalGameService _svc;
        private ManualClock _clock;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;

        [UnityTest, Timeout(900000)]
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "pf4_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _clock = new ManualClock(T0);
            _svc = new LocalGameService(_dir, "local_pf4", "Wiggles", _clock, 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "playtest-fixes-4"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
                foreach (var uid in PartsSystem.SortedPartUids(_svc.State))
                {
                    if (PartsSystem.IsEquipped(_svc.State, uid)) continue;
                    if (_svc.ExecuteNow(new PartEquip { Uid = uid, Slot = slot }).Ok) break;
                }
            var c = _svc.Content;
            _svc.State.Counters[Requirements.MainLevelCounter] = 12;
            for (int i = 1; i <= 12; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            // the monthly pass: x3 battle speed allowed
            _svc.State.Shop.DailyGrants[c.Shop.MonthlyPassProductId] = new DailyGrant { ProductId = c.Shop.MonthlyPassProductId, EndDay = long.MaxValue };
            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            _svc.Refresh(_ => { });
            foreach (var f in c.FeatureOrder) { _svc.State.Onboarding.Unlocked[f.Id] = T0; _svc.State.Onboarding.Celebrated.Add(f.Id); } // no unlock moments over the pictures

            // 1. the hub: the Pass label and the stamina bar
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.8f);
            yield return Shot("f1_hub_pass_label");

            // 2. the battle at each speed (worm on its mount; the speed control bottom-left)
            var battle = SampleBattle(c);
            var playback = new BattlePlaybackScreen(battle);
            _app.Router.Push(playback);
            yield return Wait(0.6f);
            playback.PlaySteps(3);
            foreach (var label in new[] { "x0.5", "x1", "x2", "x3" })
            {
                if (!SetSpeed(playback, label)) continue;
                yield return Wait(0.3f);
                yield return Shot("f2_battle_speed_" + label.Replace(".", "_"));
            }
            // 3. worm facing in battle: a few steps in, mid-fight, then at the end
            SetSpeed(playback, "x1");
            playback.PlaySteps(6);
            yield return Wait(0.5f);
            yield return Shot("f3_battle_worm_facing");
            playback.PlayAll();
            playback.Continue();
            yield return Wait(0.3f);

            // 4. the board: the worm after a few hops, then the pause cog (top right) crop
            _app.Router.Reset(new HubScreen());
            yield return PlayLevel(c, 3, 3);
            yield return Wait(0.9f);
            yield return Shot("f4_board_worm_facing");
            yield return ShotCrop("f5_hud_cog", new Rect(W - 300, 0, 300, 420));

            // 5. stamina at 0, then regenerating
            _svc.ExecuteNow(new RunAbandon());
            _app.Router.Reset(new HubScreen());
            var w = _svc.State.Wallet;
            w.TrySpend(Currencies.Stamina, w.Get(Currencies.Stamina));
            _svc.Refresh(_ => { });
            yield return Wait(0.8f);
            yield return ShotCrop("f6_stamina_0", new Rect(0, 0, W, 420));
            _clock.Advance(7 * 60_000);
            for (int i = 0; i < 20; i++) { _app.Update(0.6f); yield return null; }
            yield return Wait(0.4f);
            yield return ShotCrop("f7_stamina_after_7min", new Rect(0, 0, W, 420));
        }

        /// <summary>Taps the speed control until it shows <paramref name="label"/>: one cycling button now, a row of buttons before.</summary>
        private bool SetSpeed(BattlePlaybackScreen screen, string label)
        {
            var chip = Find(screen.Root, "SpeedChip");
            if (chip != null)
            {
                for (int i = 0; i < 5; i++)
                {
                    if (Find(chip, "SpeedText").GetComponent<Text>().text == label) return true;
                    chip.GetComponent<Button>().onClick.Invoke();
                }
                return false;
            }
            var seg = Find(screen.Root, "Speed" + label.Substring(1));
            if (seg == null) return false;
            seg.GetComponent<Button>().onClick.Invoke();
            return true;
        }

        private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        private static BattleResult SampleBattle(ContentDb c)
        {
            var setup = new BattleSetup { RoundCap = 12 };
            var lo = new CombatLoadout { MountId = "skiff" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 4000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Speed, 100)).Add(new StatMod(StatId.CritChance, 25));
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 4));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 4));
            return BattleSimulator.Simulate(setup, c, new Rng(5));
        }

        private IEnumerator PlayLevel(ContentDb c, int number, int steps)
        {
            var level = c.MainLevel(number);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            _svc.State.Run.Active.RunStats.Add(new StatMod(StatId.Hp, 0, 400));
            _svc.State.Run.Active.RunStats.Add(new StatMod(StatId.Atk, 0, 300));
            _app.Router.Push(new RunScreen());
            BattlePlaybackScreen.Instant = true;
            for (int i = 0; i < steps && _svc.State.Run.Active != null; i++)
            {
                _svc.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = true });
                for (int f = 0; f < 240; f++)
                {
                    if (_app.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); b.Continue(); }
                    if (_app.Router.Current is RunScreen s && !s.Animating) break;
                    yield return null;
                }
                var run = _svc.State.Run.Active;
                if (run != null && run.Phase != RunPhase.AwaitRoll) { Ui.Clear(_app.ModalLayer); _svc.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = true }); }
                yield return Wait(0.2f);
            }
            for (int f = 0; f < 600; f++)
            {
                if (_app.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); b.Continue(); }
                if (_app.Router.Current is RunScreen s && !s.Animating) break;
                yield return null;
            }
            BattlePlaybackScreen.Instant = false;
            Ui.Clear(_app.ModalLayer);
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

        private IEnumerator Shot(string name) => ShotCrop(name, new Rect(0, 0, W, H));

        /// <summary>A part of the screen (top-left origin, px) at full resolution.</summary>
        private IEnumerator ShotCrop(string name, Rect topLeft)
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
            var bytes = _tex.EncodeToPNG();
            if (topLeft.width < W || topLeft.height < H)
            {
                int x = (int)topLeft.x, h = (int)topLeft.height, y = H - (int)topLeft.y - h, w = (int)topLeft.width;
                var part = new Texture2D(w, h, TextureFormat.RGB24, false);
                part.SetPixels(_tex.GetPixels(x, y, w, h));
                part.Apply();
                bytes = part.EncodeToPNG();
                UnityEngine.Object.Destroy(part);
            }
            File.WriteAllBytes(Path.Combine(_shots, name + ".png"), bytes);
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            BattlePlaybackScreen.Instant = false;
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
