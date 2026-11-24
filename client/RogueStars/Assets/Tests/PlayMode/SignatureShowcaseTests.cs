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
    /// Signatures pass screenshots at the reference resolution (1080×2400): the run-start signature pick over the board, a battle
    /// with Heat stacks (the near-threshold chip red), the Supernova moment and its hits, the skills book with the signature, the
    /// laboratory packages and the boss gate with the run's boss. Explicit (<c>-testFilter SignatureShowcaseTests</c>); writes to
    /// $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/signatures. Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the signature screenshots; run with -testFilter SignatureShowcaseTests")]
    public sealed class SignatureShowcaseTests
    {
        private const int W = 1080, H = 2400;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots;
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
            _dir = Path.Combine(Application.temporaryCachePath, "sig_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_sig", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "signatures"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            var c = _svc.Content;
            _svc.State.Counters[Requirements.MainLevelCounter] = 12;
            for (int i = 1; i <= 12; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.Refresh(_ => { });

            // 1. the signature pick over the portal screen's board-less backdrop: start from a level like the player does
            var level = c.MainLevel(3);
            var offer = RunSystem.SignatureOffer(_svc.Engine, _svc.State, _svc.NowMs);
            Assert.That(offer.Count, Is.EqualTo(3));
            _app.Open("portals");
            yield return Wait(0.6f);
            string picked = null;
            SignaturePick.Show(_app.ModalLayer, c, offer, sid => picked = sid);
            yield return Wait(0.8f);
            yield return Shot("sig1_pick");
            string fire = offer.Contains("sig_fire") ? "sig_fire" : offer[0];
            FindButton(_app.ModalLayer, "Button:Signature:" + fire).onClick.Invoke();
            Assert.That(picked, Is.EqualTo(fire));
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal, SignatureId = picked });
            Assert.That(start.Ok, Is.True, start.Error);
            _svc.State.Run.Active.RunStats.Add(new StatMod(StatId.Hp, 0, 400));
            _svc.State.Run.Active.RunStats.Add(new StatMod(StatId.Atk, 0, 300));
            _app.Router.Push(new RunScreen());
            yield return Wait(1.0f);

            // 2. the skills book: the signature first
            var run = _svc.State.Run.Active;
            run.Skills.Add("fire_ember"); run.Skills.Add("fire_kindle");
            ((RunScreen)_app.Router.Current).Rebuild();
            yield return Wait(0.3f);
            FindButton(_app.Root, "SkillsButton")?.onClick.Invoke();
            yield return Wait(0.6f);
            yield return Shot("sig2_skillbook");
            Ui.Clear(_app.ModalLayer);

            // 3. a Fire battle: Heat stacks (the chip turns red near 10), then the Supernova and its hits on every fighter
            var skills = new List<string> { "sig_fire", "fire_ember", "fire_flashpoint" };
            var battle = FireBattle(c, skills);
            int hot = battle.Log.FindIndex(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Heat && e.Stacks >= 8);
            int nova = battle.Log.FindIndex(e => e.Action == BattleAction.Supernova && e.Amount == 0);
            Assert.That(nova, Is.GreaterThan(0), "the showcase fight has a Supernova");
            var ctx = BattleHudContext.FromRun(run, c);
            ctx.Skills = skills;
            ctx.Levels = new Dictionary<string, int> { ["sig_fire"] = 2 };
            var playback = new BattlePlaybackScreen(battle, false, ctx);
            _app.Router.Push(playback);
            yield return Wait(0.5f);
            if (hot >= 0 && hot < nova)
            {
                playback.PlaySteps(Math.Max(0, hot + 1 - playback.NextIndex));
                yield return Wait(0.35f);
                yield return Shot("sig3_battle_heat");
            }
            playback.PlaySteps(Math.Max(0, nova - playback.NextIndex));
            // the next frames animate the Supernova marker and its hits
            yield return Wait(playback.StepSeconds * 1.25f);
            yield return Shot("sig4_supernova");
            yield return Wait(playback.StepSeconds * 2.5f);
            yield return Shot("sig4_supernova_hits");
            BattlePlaybackScreen.Instant = true;
            yield return Wait(0.2f);
            BattlePlaybackScreen.Instant = false;
            _app.Router.Pop();
            yield return Wait(0.3f);

            // 4. the boss gate: the run's boss on its tile, large
            run = _svc.State.Run.Active;
            run.Awakening = 99.9;
            BattlePlaybackScreen.Instant = true;
            for (int i = 0; i < 40 && _svc.State.Run.Active != null && _svc.State.Run.Active.BossGateTile < 0; i++)
            {
                _svc.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = false });
                for (int f = 0; f < 400; f++)
                {
                    if (_app.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); b.Continue(); }
                    if (_app.Router.Current is RunScreen s && !s.Animating) break;
                    yield return null;
                }
                Ui.Clear(_app.ModalLayer);
            }
            BattlePlaybackScreen.Instant = false;
            if (_svc.State.Run.Active != null && _svc.State.Run.Active.BossGateTile >= 0 && _app.Router.Current is RunScreen rs)
            {
                rs.Rebuild();
                yield return Wait(1.4f);
                Ui.Clear(_app.ModalLayer);
                yield return Shot("sig6_boss_gate");
            }

            // 5. the laboratory: the synergy packages tab (two researched)
            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            _svc.State.Lab.Entries["pkg_combo"] = new LabEntry { Level = 1 };
            _svc.State.Lab.Entries["pkg_ice"] = new LabEntry { Level = 1 };
            _svc.State.Lab.Entries["lab_hp_1"] = new LabEntry { Level = 2 };
            _app.Open("laboratory");
            yield return Wait(0.8f);
            yield return Shot("sig5_lab_packages");
            ((LaboratoryScreen)_app.Router.Current).NodeModal("pkg_lightning");
            yield return Wait(0.5f);
            yield return Shot("sig5_lab_package_modal");
        }

        private static BattleResult FireBattle(ContentDb c, List<string> skills)
        {
            BattleResult best = null;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var setup = new BattleSetup { RoundCap = 14 };
                var lo = new CombatLoadout { MountId = "skiff" };
                lo.WormStats.Add(new StatMod(StatId.Hp, 6000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Speed, 100));
                setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, skills, null, new Dictionary<string, int> { ["sig_fire"] = 2 }));
                setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 3));
                setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "ember_imp", 3));
                setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 3));
                var r = BattleSimulator.Simulate(setup, c, new Rng(seed));
                if (r.Log.Exists(e => e.Action == BattleAction.Supernova)) { best = r; break; }
                best = r;
            }
            return best;
        }

        private static Button FindButton(Transform root, string name)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(true)) if (b.name == name) return b;
            return null;
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
            BattlePlaybackScreen.Instant = false;
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
