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
    /// Renders the game look at a modern phone size (1080×2340): portal map, run board, event / draft / shop popups,
    /// battle mid-animation and at the end, run result, worm and city. Explicit (run with
    /// <c>-testFilter UiShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/showcase.
    /// Needs a graphics device: run in batchmode WITHOUT -nographics.
    /// </summary>
    [Explicit("renders showcase screenshots; run with -testFilter UiShowcaseTests")]
    public sealed class UiShowcaseTests
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
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "showcase_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_showcase", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "showcase"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            // a player two levels in: starter parts equipped, levels 1–2 cleared with some stars
            _svc.DevGrantStarterKit();
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
                foreach (var uid in PartsSystem.SortedPartUids(_svc.State))
                {
                    if (PartsSystem.IsEquipped(_svc.State, uid)) continue;
                    if (_svc.ExecuteNow(new PartEquip { Uid = uid, Slot = slot }).Ok) break;
                }
            var c = _svc.Content;
            _svc.State.Counters[Requirements.MainLevelCounter] = 2;
            _svc.State.Run.Levels[c.MainLevelId(1)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, true } };
            _svc.State.Run.Levels[c.MainLevelId(2)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.Refresh(_ => { });

            _app.Open("portals");
            yield return Wait(0.6f);
            yield return Shot("01_portal_map");

            var level = c.MainLevel(3);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            _app.Router.Push(new RunScreen());
            BattlePlaybackScreen.Instant = true;
            // a few clear steps so the board shows walked tiles
            for (int i = 0; i < 4 && _svc.State.Run.Active != null; i++)
            {
                _svc.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = true });
                yield return Settle();
            }
            BattlePlaybackScreen.Instant = false;
            yield return Wait(0.8f);
            yield return Shot("02_run_board");

            // the decision popups, forced on the live run for the picture
            var run = _svc.State.Run.Active;
            if (run != null)
            {
                var screen = (RunScreen)_app.Router.Current;
                EventDef ev = null;
                foreach (var e in c.RunEvents.Values) if (e.Options.Count >= 2 && (ev == null || e.Options.Count > ev.Options.Count)) ev = e;
                run.Phase = RunPhase.Event;
                run.EventId = ev.Id;
                screen.OnShow();
                screen.Rebuild();
                yield return Wait(0.5f);
                yield return Shot("03_event");
                Ui.Clear(_app.ModalLayer);

                run.Phase = RunPhase.Draft;
                run.DraftOffer.Clear();
                foreach (var s in c.Skills.Values)
                {
                    if (run.DraftOffer.Count >= 3) break;
                    if (s.Rarity >= Rarity.Rare && !run.DraftOffer.Exists(id => c.Skill(id).Family == s.Family)) run.DraftOffer.Add(s.Id);
                }
                run.DraftRerolls = 2;
                screen.OnShow();
                screen.Rebuild();
                yield return Wait(0.5f);
                yield return Shot("04_draft");
                Ui.Clear(_app.ModalLayer);
                run.Phase = RunPhase.AwaitRoll;
                run.DraftOffer.Clear();
                screen.OnShow();
                screen.Rebuild();
            }

            // battle mid-animation and at the end
            var battle = SampleBattle(c);
            var playback = new BattlePlaybackScreen(battle);
            _app.Router.Push(playback);
            yield return Wait(1.45f);
            yield return Shot("05_battle");
            playback.PlayAll();
            yield return Wait(0.5f);
            yield return Shot("06_battle_end");
            playback.Continue();

            // finish the run quickly and show the result
            BattlePlaybackScreen.Instant = true;
            for (int i = 0; i < 400 && _svc.State.Run.Active != null; i++)
            {
                _svc.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = true });
                while (_app.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); _app.Router.Pop(); }
            }
            BattlePlaybackScreen.Instant = false;
            _app.Router.Reset(new HubScreen());
            _app.Router.Push(new RunResultScreen());
            yield return Wait(0.6f);
            yield return Shot("07_run_result");

            _app.Router.Reset(new HubScreen());
            _app.Open("worm");
            yield return Wait(0.4f);
            yield return Shot("08_worm");
            _app.Router.Pop();
            _app.Open("city");
            yield return Wait(0.4f);
            yield return Shot("09_city");
        }

        private static BattleResult SampleBattle(ContentDb c)
        {
            var setup = new BattleSetup { RoundCap = 10 };
            var lo = new CombatLoadout { MountId = "skiff" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 3000)).Add(new StatMod(StatId.Atk, 70)).Add(new StatMod(StatId.Speed, 100)).Add(new StatMod(StatId.CritChance, 30));
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 3));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 3));
            return BattleSimulator.Simulate(setup, c, new Rng(5));
        }

        /// <summary>Let animations, playbacks and decisions of an auto step play out (decisions are farmed).</summary>
        private IEnumerator Settle()
        {
            for (int f = 0; f < 240; f++)
            {
                if (_app.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); b.Continue(); }
                if (_app.Router.Current is RunScreen rs && !rs.Animating) break;
                yield return null;
            }
            var run = _svc.State.Run.Active;
            if (run != null && run.Phase != RunPhase.AwaitRoll)
            {
                Ui.Clear(_app.ModalLayer);
                _svc.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = true });
            }
            yield return Wait(0.3f);
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
