using System;
using System.Collections;
using System.Collections.Generic;
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

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// The art import of 2026-11-27 (batch 5): the four biome elites with their own sprite in battle (each alone, then next to
    /// the Frost Knight for scale), and the Revive screen with the tombstone, at 1080 × 2340. Explicit
    /// (<c>-testFilter ArtImportBatch5ShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR. Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the batch-5 art-import screenshots; run with -testFilter ArtImportBatch5ShowcaseTests")]
    public sealed class ArtImportBatch5ShowcaseTests
    {
        private const int W = 1080, H = 2340;
        private const long T0 = 1_772_452_800_000L;
        private static readonly string[] Elites = { "elite_stalker_prime", "elite_glacier_warden", "elite_cinder_alpha", "elite_ash_matriarch" };
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;

        private GameState S => _svc.State;
        private ContentDb C => _svc.Content;

        [UnityTest, Timeout(600000)]
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "artimport5_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_artimport5", "Wiggles", new ManualClock(T0), 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "art-import"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);
            _svc.DevGrantStarterKit();
            Reach(12);
            foreach (var f in C.FeatureOrder) { S.Onboarding.Unlocked[f.Id] = T0; S.Onboarding.Celebrated.Add(f.Id); }
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.4f);
            Ui.Clear(_app.ModalLayer);

            // 1. the elites in battle (the battle stays at the start: every fighter stands in place)
            foreach (var id in Elites)
            {
                Assert.That(ArtCatalog.Enemy(id), Is.Not.Null, id);
                _app.Router.Push(new BattlePlaybackScreen(Battle(new[] { "alien_emberkit", "alien_zib" }, new[] { id }, 5)));
                yield return Wait(1.2f);
                yield return Shot("b5_battle_" + id);
                _app.Router.Pop();
            }
            foreach (var (name, ids) in new[]
            {
                ("b5_battle_compare_a", new[] { "elite_frost_knight", "elite_stalker_prime", "elite_glacier_warden" }),
                ("b5_battle_compare_b", new[] { "elite_frost_knight", "elite_cinder_alpha", "elite_ash_matriarch" }),
            })
            {
                _app.Router.Push(new BattlePlaybackScreen(Battle(new[] { "alien_emberkit", "alien_zib" }, ids, 5)));
                yield return Wait(1.2f);
                yield return Shot(name);
                _app.Router.Pop();
            }

            // 2. the revive with the tombstone
            var level = C.MainLevel(12);
            S.Wallet.Add(Currencies.Stamina, 200);
            var r = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(r.Ok, Is.True, r.Error);
            Ui.Clear(_app.ModalLayer);
            _app.Router.Reset(new HubScreen());
            var rs = new RunScreen();
            _app.Router.Push(rs);
            rs.SkipCoach();
            yield return Wait(1.0f);
            var run = S.Run.Active;
            run.Phase = RunPhase.Defeated;
            run.WormHp = 0;
            Ui.Clear(_app.ModalLayer);
            rs.Rebuild();
            yield return Wait(1.0f);
            Assert.That(RunScreen.TombstoneSprite(), Is.SameAs(ArtCatalog.RunIcon("tombstone")).And.Not.Null);
            yield return Shot("b5_revive_tombstone");
        }

        private void Reach(int level)
        {
            S.Counters[Requirements.MainLevelCounter] = level;
            for (int i = 1; i <= level; i++) S.Run.Levels[C.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.Refresh(_ => { });
        }

        // ---- battles -------------------------------------------------------------------------------------------------------

        /// <summary>The worm on its mount with its aliens against the elite(s).</summary>
        private BattleResult Battle(string[] aliens, string[] enemies, int seed)
        {
            var lo = new CombatLoadout { DisplayName = "Wiggles", MountId = "skiff" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 9000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Def, 10)).Add(new StatMod(StatId.Speed, 100));
            foreach (var id in aliens)
            {
                var def = C.AlienType(id);
                Assert.That(def, Is.Not.Null, id);
                var a = new CombatAlien { AlienTypeId = id, Name = def.Name, Stars = 3 };
                a.Stats.Add(new StatMod(StatId.Speed, def.BaseSpeed));
                a.SkillIds.Add(AlienSystem.AbilityAt(C, id, 3));
                lo.Aliens.Add(a);
            }
            var setup = new BattleSetup { RoundCap = 12 };
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, C, new List<string>()));
            foreach (var e in enemies)
            {
                Assert.That(C.Enemies.ContainsKey(e), Is.True, e);
                setup.Enemy.Add(BattleSimulator.BuildEnemy(C, e, 2, 0, 600));
            }
            return BattleSimulator.Simulate(setup, C, new Rng((ulong)seed));
        }

        // ---- capture (as ArtImportBatch4ShowcaseTests) -------------------------------------------------------------------

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("Batch5ShotCamera");
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
            _app.Feedback.Tick(60);
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (_app.Router.Current is RunScreen run && run.World != null && run.World.Cam.enabled) run.World.Cam.Render(); // the board under the revive
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
