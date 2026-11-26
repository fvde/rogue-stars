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
    /// The art import of 2026-11-26 (batch 2): the new alien sprites (the Aliens collection, a squad in battle), the new enemy
    /// sprites in two battles, the home planet stages and the skill icons (a run draft of the imported skills). Explicit
    /// (<c>-testFilter ArtImportBatch2ShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR. Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the batch-2 art-import screenshots; run with -testFilter ArtImportBatch2ShowcaseTests")]
    public sealed class ArtImportBatch2ShowcaseTests
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

            // 1. the home planet (stage 4 art) and the aliens collection
            Ui.Clear(_app.ModalLayer);
            while (_app.Router.Depth > 1) _app.Router.Pop();
            _app.Router.RebuildCurrent();
            yield return Wait(0.8f);
            Ui.Clear(_app.ModalLayer);
            yield return Shot("b2_hub");
            foreach (var id in Species) _svc.State.Aliens.Owned[id] = new OwnedAlien { TypeId = id, Stars = 1 };
            _app.Open("aliens");
            yield return Wait(1.2f);
            Ui.Clear(_app.ModalLayer);
            yield return Shot("b2_aliens");
            while (_app.Router.Depth > 1) _app.Router.Pop();

            // 2. battles with the new aliens and the new enemies
            var level = c.MainLevel(5);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            var run = _svc.State.Run.Active;
            var ctx = BattleHudContext.FromRun(run, c);
            _app.Router.Push(new BattlePlaybackScreen(Battle(c, new[] { "alien_shiverpup", "alien_emberkit", "alien_solarch" }, new[] { "volt_beetle", "void_spawn", "tunnel_stalker" }), ctx));
            yield return Wait(1.4f);
            yield return Shot("b2_battle_a");
            _app.Router.Pop();
            _app.Router.Push(new BattlePlaybackScreen(Battle(c, new[] { "alien_zib", "alien_nebulisk", "alien_thornqueen" }, new[] { "frost_mite", "elite_void_reaver", "elite_frost_knight" }), ctx));
            yield return Wait(1.4f);
            yield return Shot("b2_battle_b");
            _app.Router.Pop();
        }

        private static readonly string[] Species =
        {
            "alien_mossling", "alien_shiverpup", "alien_zib", "alien_arcwhisk", "alien_whirrbit", "alien_sporeling",
            "alien_emberkit", "alien_thornqueen", "alien_nebulisk", "alien_solarch", "alien_swarmheart", "alien_chronoslug",
        };

        /// <summary>The worm on its mount with three aliens against three enemies.</summary>
        private static BattleResult Battle(ContentDb c, string[] aliens, string[] enemies)
        {
            var lo = new CombatLoadout { DisplayName = "Wiggles", MountId = "skiff" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 6000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Def, 10)).Add(new StatMod(StatId.Speed, 100));
            foreach (var id in aliens)
            {
                var def = c.AlienType(id);
                Assert.That(def, Is.Not.Null, id);
                var a = new CombatAlien { AlienTypeId = id, Name = def.Name, Stars = 3 };
                a.Stats.Add(new StatMod(StatId.Speed, def.BaseSpeed));
                a.SkillIds.Add(AlienSystem.AbilityAt(c, id, 3));
                lo.Aliens.Add(a);
            }
            var setup = new BattleSetup { RoundCap = 10 };
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, new string[0]));
            foreach (var e in enemies) setup.Enemy.Add(BattleSimulator.BuildEnemy(c, e, 2, 0, 600));
            return BattleSimulator.Simulate(setup, c, new Rng(3));
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
