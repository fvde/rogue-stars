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
    /// Art pass 1 at 1080×2340: the layered board (ground locked to the board, path tiles, decor) on several biomes and a
    /// battle with enemy / alien sprites. Explicit (<c>-testFilter ArtPassShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR
    /// or client/RogueStars/Screenshots/art-pass. Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders art pass screenshots; run with -testFilter ArtPassShowcaseTests")]
    public sealed class ArtPassShowcaseTests
    {
        private const int W = 1080, H = 2340;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private LocalGameService _svc;
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
            _dir = Path.Combine(Application.temporaryCachePath, "art_pass_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_art_pass", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "art-pass"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            var c = _svc.Content;
            _svc.State.Counters[Requirements.MainLevelCounter] = 45;
            for (int i = 1; i <= 45; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.Refresh(_ => { });

            int shot = 1;
            foreach (var family in new[] { "ember", "static", "void", "glacier" })
            {
                int n = FirstLevel(c, family);
                if (n < 0) continue;
                yield return PlayLevel(c, n, 4);
                var rs = (RunScreen)_app.Router.Current;
                rs.Rebuild();
                yield return Wait(0.9f);
                yield return Shot("b" + shot++ + "_run_" + rs.Biome.Id);
                _svc.ExecuteNow(new RunAbandon());
                _app.Router.Reset(new HubScreen());
                yield return Wait(0.2f);
            }

            // the battle with sprites: an alien companion, a regular enemy, an elite with its own art and a boss drawn with its
            // family's sprite (scaled up)
            var battle = SpriteBattle(c);
            var playback = new BattlePlaybackScreen(battle);
            _app.Router.Push(playback);
            yield return Wait(1.6f);
            yield return Shot("b" + shot++ + "_battle_sprites");
            yield return Wait(1.2f);
            yield return Shot("b" + shot++ + "_battle_sprites_later");
            playback.PlayAll();
            yield return Wait(0.6f);
            yield return Shot("b" + shot + "_battle_end");
            playback.Continue();
        }

        private static int FirstLevel(ContentDb c, string family)
        {
            for (int n = 1; n <= 45; n++)
                if (ArtCatalog.FamilyOf(c.MainLevel(n)) == family) return n;
            return -1;
        }

        private IEnumerator PlayLevel(ContentDb c, int number, int steps)
        {
            var level = c.MainLevel(number);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            _svc.State.Run.Active.RunStats.Add(new StatMod(StatId.Hp, 0, 2000));
            _svc.State.Run.Active.RunStats.Add(new StatMod(StatId.Atk, 0, 1500));
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

        private static BattleResult SpriteBattle(ContentDb c)
        {
            var setup = new BattleSetup { RoundCap = 12 };
            var lo = new CombatLoadout { MountId = "skiff" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 5000)).Add(new StatMod(StatId.Atk, 90)).Add(new StatMod(StatId.Speed, 100));
            var alien = new CombatAlien { AlienTypeId = "alien_blorp", Name = "Blorp" };
            alien.Stats.Add(new StatMod(StatId.Hp, 400)).Add(new StatMod(StatId.Atk, 40));
            lo.Aliens.Add(alien);
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, new List<string>()));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "ember_imp", 4));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "elite_lava_brute", 4));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "boss_magma_tyrant", 4));
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
            BattlePlaybackScreen.Instant = false;
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
