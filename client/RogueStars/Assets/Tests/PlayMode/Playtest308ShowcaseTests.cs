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
    /// Playtest 0.1.308 pictures: the board with the miniboss ahead on the path, the boss arena opening (compact banner), the
    /// one speed button, the shop with 3 offers, a red tile's landing, and a battle's "NEXT" strip on three backgrounds.
    /// Explicit; run WITHOUT -nographics (ROGUESTARS_SHOTS_DIR = output folder).
    /// </summary>
    [Explicit("renders the playtest 0.1.308 screenshots; run with -testFilter Playtest308ShowcaseTests")]
    public sealed class Playtest308ShowcaseTests
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
            _dir = Path.Combine(Application.temporaryCachePath, "p308_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _clock = new ManualClock(T0);
            _svc = new LocalGameService(_dir, "local_p308", "Wiggles", _clock, 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "playtest-308"));
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
            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            _svc.Refresh(_ => { });
            foreach (var f in c.FeatureOrder) { _svc.State.Onboarding.Unlocked[f.Id] = T0; _svc.State.Onboarding.Celebrated.Add(f.Id); }
            RunScreen.BoardSpeed = 1;

            // 1. the board: the worm a few tiles before the first miniboss lair (crowned skull + "!"), the speed button
            var level = c.MainLevel(8);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            var run = _svc.State.Run.Active;
            run.RunStats.Add(new StatMod(StatId.Hp, 0, 400));
            run.RunStats.Add(new StatMod(StatId.Atk, 0, 300));
            var path = MainPath(run);
            int lair = path.FindIndex(id => run.Tiles[id].TileId == "miniboss");
            Assert.That(lair, Is.GreaterThan(4), "a lair on the path");
            MoveTo(run, path, lair - 4);
            _app.Router.Reset(new HubScreen());
            _app.Router.Push(new RunScreen());
            yield return Wait(1.6f);
            yield return Shot("p6a_board_miniboss_ahead");
            yield return ShotCrop("p1_speed_button", new Rect(0, H - 360, 560, 360));

            // 2. a red tile: the next tiles become curses / traps, one roll lands on one
            var screen = (RunScreen)_app.Router.Current;
            for (int i = 1; i <= 12 && lair - 4 + i < path.Count; i++) run.Tiles[path[lair - 4 + i]].TileId = i % 2 == 0 ? "debuff_atk" : "hazard_spikes";
            run.Tiles[path[lair - 4 + 1]].TileId = "debuff_def";
            screen.Rebuild();
            yield return Wait(0.6f);
            yield return Shot("p3a_board_red_tiles_ahead");
            int landed = screen.LastLandedTile;
            _app.Session.Run(new RunRoll());
            for (int f = 0; f < 600 && screen.LastLandedTile == landed; f++) yield return null;
            yield return Wait(0.25f);
            yield return Shot("p3b_red_tile_landing");
            yield return Wait(0.5f);
            yield return Shot("p3c_red_tile_landing_later");
            for (int f = 0; f < 600 && screen.Animating; f++) yield return null;

            // 3. the shop: the next tile is a shop, 3 offers (then a reroll: still 3)
            run = _svc.State.Run.Active;
            path = MainPath(run);
            if (path.Count > 1) run.Tiles[path[1]].TileId = "shop";
            run.Coins = 500;
            _app.Session.Run(new RunRoll());
            for (int f = 0; f < 1200 && OfferSlots() == 0; f++) yield return null;
            yield return Wait(0.8f);
            Assert.That(OfferSlots(), Is.EqualTo(3), "three shop offers on screen");
            yield return Shot("p5a_shop_three_offers");
            var reroll = _app.ModalLayer.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name.StartsWith("Button:Reroll"));
            if (reroll != null) { reroll.onClick.Invoke(); yield return Wait(0.8f); yield return Shot("p5b_shop_after_reroll"); }
            Ui.Clear(_app.ModalLayer);
            _svc.ExecuteNow(new RunShopLeave());

            // 4. the boss: the meter is full, the next clear step opens its arena right ahead (compact banner, boss on its tile)
            run = _svc.State.Run.Active;
            run.Awakening = 99.5;
            _app.Router.Reset(new HubScreen());
            _app.Router.Push(new RunScreen());
            yield return Wait(0.6f);
            screen = (RunScreen)_app.Router.Current;
            for (int step = 0; step < 40 && _svc.State.Run.Active != null && _svc.State.Run.Active.Phase != RunPhase.Encounter; step++)
            {
                Ui.Clear(_app.ModalLayer);
                _app.Session.Run(new RunAutoStep { Farming = true, FarmShops = true });
                for (int f = 0; f < 900; f++)
                {
                    if (_app.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); b.Continue(); }
                    if (_app.Router.Current == screen && !screen.Animating) break;
                    yield return null;
                }
            }
            Assert.That(_svc.State.Run.Active.Phase, Is.EqualTo(RunPhase.Encounter), "the boss arena opened");
            Ui.Clear(_app.ModalLayer);
            yield return Wait(0.3f);
            yield return Shot("p6b_board_boss_arena_banner");
            yield return Wait(1.4f);
            yield return Shot("p6c_board_boss_arena");

            // 5. the battle "NEXT" strip on three backgrounds (the meadow and two board backdrops standing in for other arenas)
            var battle = SampleBattle(c);
            foreach (var bg in new[] { (string)null, "Art/Boards/cloud_islands/background", "Art/Boards/lava/background" })
            {
                string key = ArtCatalog.SliceBattleFolder + "/" + ArtCatalog.BattleDefault;
                ArtCatalog.Loader = bg == null ? null : (Func<string, Sprite>)(p => Resources.Load<Sprite>(p == key ? bg : p));
                var playback = new BattlePlaybackScreen(battle, new BattleHudContext { InRun = true });
                _app.Router.Push(playback);
                yield return Wait(0.6f);
                playback.PlaySteps(3);
                yield return Wait(0.4f);
                string name = bg == null ? "meadow" : bg.Split('/')[2];
                yield return Shot("p8_battle_next_" + name);
                yield return ShotCrop("p8_battle_next_" + name + "_crop", new Rect(0, 200, W, 360));
                playback.Continue();
                if (_app.Router.Current == playback) _app.Router.Pop();
                yield return null;
            }
            ArtCatalog.Loader = null;

            // 6. a won battle: VICTORY, then back to the board by itself (no Continue)
            var won = new BattlePlaybackScreen(battle, new BattleHudContext { InRun = true });
            _app.Router.Push(won);
            yield return Wait(0.3f);
            won.PlayAll();
            yield return Wait(0.15f);
            yield return Shot("p4a_battle_won_victory_beat");
            yield return Wait(2.5f);
            Assert.That(_app.Router.Current, Is.Not.SameAs(won), "the won battle closed itself");
            yield return Shot("p4b_after_win_back_on_board");
        }

        private int OfferSlots() => _app.ModalLayer.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("OfferSlot:"));

        private static List<int> MainPath(ActiveRun run)
        {
            var list = new List<int>();
            int id = run.Position;
            for (int guard = 0; guard < 400 && id >= 0; guard++)
            {
                list.Add(id);
                var t = run.Tiles[id];
                if (t.Next.Count == 0) break;
                id = t.Next[0];
            }
            return list;
        }

        private static void MoveTo(ActiveRun run, List<int> path, int index)
        {
            for (int i = 1; i <= index; i++) run.Trail.Add(path[i]);
            run.Position = path[index];
        }

        private static BattleResult SampleBattle(ContentDb c)
        {
            var setup = new BattleSetup { RoundCap = 12 };
            var lo = new CombatLoadout();
            lo.WormStats.Add(new StatMod(StatId.Hp, 6000)).Add(new StatMod(StatId.Atk, 400)).Add(new StatMod(StatId.Speed, 100)).Add(new StatMod(StatId.CritChance, 25));
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 4));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 4));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 4));
            return BattleSimulator.Simulate(setup, c, new Rng(5));
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
            ArtCatalog.Loader = null;
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
