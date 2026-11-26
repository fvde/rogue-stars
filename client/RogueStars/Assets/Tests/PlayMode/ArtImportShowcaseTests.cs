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

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// The art import of 2026-11-25 at 1080 × 2400 (or $ROGUESTARS_SHOT_SIZE): ai_hub_day / _night / _event / _invasion (the
    /// space backdrops, home icons, START rocket, the profile's worm icon and level star), ai_board (markers, the walking worm,
    /// the dice stand), ai_battle_hud (the HUD icons), ai_result_victory / _defeat (emblems), ai_worm (the Worm screen), and
    /// the battle-background TEST: ai_battlebg_&lt;file&gt; puts each side-view candidate of
    /// <c>$ROGUESTARS_BATTLE_BG_DIR</c> (default ~/AI/out/pkg-slice-battle2) behind a real fight (worm on its mount, three
    /// sidekicks, three enemies) through a swapped <see cref="ArtCatalog.Loader"/> — the candidates are NOT imported or
    /// shipped. Explicit (<c>-testFilter ArtImportShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR or
    /// client/RogueStars/Screenshots/art-import. Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the art-import screenshots; run with -testFilter ArtImportShowcaseTests")]
    public sealed class ArtImportShowcaseTests
    {
        private static int W = 1080, H = 2400;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;
        private readonly List<UnityEngine.Object> _made = new List<UnityEngine.Object>();

        [UnityTest, Timeout(900000)]
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            var size = (Environment.GetEnvironmentVariable("ROGUESTARS_SHOT_SIZE") ?? "").Split('x');
            if (size.Length == 2 && int.TryParse(size[0], out int sw) && int.TryParse(size[1], out int sh)) { W = sw; H = sh; }
            else { W = 1080; H = 2400; }
            _dir = Path.Combine(Application.temporaryCachePath, "artimport_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_artimport", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "art-import"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            var c = _svc.Content;
            _svc.State.Counters[Requirements.MainLevelCounter] = 12;
            for (int i = 1; i <= 12; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.State.Flags.Add(Features.DevUnlockAllFlag);
            foreach (var f in c.FeatureDefs.Keys) _svc.State.Onboarding.Celebrated.Add(f); // no unlock moments over the shots
            _svc.Refresh(_ => { });

            // 1. the hub in every sky state
            foreach (var sky in ArtCatalog.SkyStates)
            {
                HubScreen.SkyOverride = sky;
                Ui.Clear(_app.ModalLayer);
                while (_app.Router.Depth > 1) _app.Router.Pop();
                _app.Router.RebuildCurrent();
                yield return Wait(0.8f);
                Ui.Clear(_app.ModalLayer);
                yield return Shot("ai_hub_" + sky);
            }
            HubScreen.SkyOverride = null;

            // 2. the worm screen
            _app.Open("worm");
            yield return Wait(1.0f);
            Ui.Clear(_app.ModalLayer);
            yield return Shot("ai_worm");
            while (_app.Router.Depth > 1) _app.Router.Pop();

            // 3. the run board (markers incl. the elite, the walking worm, the dice stand)
            var level = c.MainLevel(5);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            var run = _svc.State.Run.Active;
            run.RunStats.Add(new StatMod(StatId.Hp, 0, 400));
            run.RunStats.Add(new StatMod(StatId.Atk, 0, 300));
            run.Coins = 291;
            _app.Router.Push(new RunScreen());
            yield return Wait(1.2f);
            Ui.Clear(_app.ModalLayer);
            yield return Shot("ai_board");

            // 4. the battle HUD with the shipped arena
            var battle = Battle(c);
            var ctx = BattleHudContext.FromRun(run, c);
            _app.Router.Push(new BattlePlaybackScreen(battle, ctx));
            yield return Wait(1.4f);
            yield return Shot("ai_battle_hud");
            _app.Router.Pop();

            // 5. the shared battle background: a normal run battle (above, ai_battle_hud) and a kit battle (no run behind it)
            _app.Router.Push(new BattlePlaybackScreen(battle, ctx));
            yield return Wait(1.0f);
            AssertArenaUsesBackground();
            yield return Shot("ai_battlebg_run_" + W + "x" + H);
            _app.Router.Pop();
            _app.Router.Push(new BattlePlaybackScreen(battle, BattleHudContext.FromRun(null, c)));
            yield return Wait(1.0f);
            AssertArenaUsesBackground();
            yield return Shot("ai_battlebg_kit_" + W + "x" + H);
            _app.Router.Pop();

            // 6. run result (victory, defeat)
            _svc.State.Run.LastSummary = Summary(level, true);
            _app.Router.Push(new RunResultScreen());
            yield return Wait(1.4f);
            yield return Shot("ai_result_victory");
            _app.Router.Pop();
            _svc.State.Run.LastSummary = Summary(level, false);
            _app.Router.Push(new RunResultScreen());
            yield return Wait(1.4f);
            yield return Shot("ai_result_defeat");
            _app.Router.Pop();
        }

        private static void AssertArenaUsesBackground()
        {
            var bg = ArtCatalog.BattleBackground(null);
            Assert.That(bg, Is.Not.Null, "the battle background is imported");
            bool found = false;
            foreach (var img in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Image>(FindObjectsSortMode.None))
                if (img.name == "Art" && img.transform.parent != null && img.transform.parent.name == "Arena" && img.sprite == bg) found = true;
            Assert.That(found, Is.True, "the battle screen's Arena shows the shared battle background");
        }

        private Sprite LoadSprite(string file)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(file));
            var s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100);
            _made.Add(t);
            _made.Add(s);
            return s;
        }

        /// <summary>The worm on its mount with three sidekicks against three enemies (the battle screen's full line-up).</summary>
        private static BattleResult Battle(ContentDb c)
        {
            var lo = new CombatLoadout { DisplayName = "Wiggles", MountId = "skiff" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 6000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Def, 10)).Add(new StatMod(StatId.Speed, 100));
            foreach (var id in new[] { "alien_pebblehorn", "alien_swarmheart", "alien_voidmaw" })
            {
                var def = c.AlienType(id);
                if (def == null) continue;
                var a = new CombatAlien { AlienTypeId = id, Name = def.Name, Stars = 3 };
                a.Stats.Add(new StatMod(StatId.Speed, def.BaseSpeed));
                a.SkillIds.Add(AlienSystem.AbilityAt(c, id, 3));
                lo.Aliens.Add(a);
            }
            var setup = new BattleSetup { RoundCap = 10 };
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, new string[0]));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 2, 0, 600));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "ember_imp", 2, 0, 600));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 2, 0, 600));
            return BattleSimulator.Simulate(setup, c, new Rng(3));
        }

        private static RunSummary Summary(LevelDef level, bool won)
        {
            var s = new RunSummary { RunId = "show", LevelId = level.Id, Mode = RunMode.Normal, Won = won, Rolls = won ? 30 : 17, Level = won ? 9 : 6, Minibosses = won ? 2 : 1, ShopBuys = 3 };
            s.Granted.Add(Reward.Cur(Currencies.Biomass, won ? 1250 : 0));
            s.Granted.Add(Reward.Cur(Currencies.Diamonds, won ? 30 : 0));
            s.Granted.Add(new Reward(RewardKinds.Part, "*", 1, 2));
            s.Granted.RemoveAll(r => r.Amount <= 0);
            if (won) s.Doublable.AddRange(s.Granted);
            return s;
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
            if (_app.Router.Current is RunScreen rs && rs.World != null && rs.World.Cam.enabled) rs.World.Cam.Render();
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
            HubScreen.SkyOverride = null;
            ArtCatalog.Loader = null;
            foreach (var o in _made) if (o != null) UnityEngine.Object.Destroy(o);
            _made.Clear();
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
