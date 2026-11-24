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
    /// Skill redesign screenshots at 1080×2400: a level-up draft with the new Legendaries, a miniboss Mythic choice that offers a
    /// signature, the Resonance event, a battle showing Thermal Shock's stun and Heat lock, the skill book with two signatures and
    /// the Time package in the laboratory. Explicit (<c>-testFilter SkillRedesignShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR
    /// or client/RogueStars/Screenshots/skill-redesign. Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the skill redesign screenshots; run with -testFilter SkillRedesignShowcaseTests")]
    public sealed class SkillRedesignShowcaseTests
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
            _dir = Path.Combine(Application.temporaryCachePath, "redesign_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_redesign", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "skill-redesign"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            var c = _svc.Content;
            _svc.State.Counters[Requirements.MainLevelCounter] = 12;
            for (int i = 1; i <= 12; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.Refresh(_ => { });

            var level = c.MainLevel(3);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal, SignatureId = "sig_fire" });
            Assert.That(start.Ok, Is.True, start.Error);
            var run = _svc.State.Run.Active;
            run.Skills.Add("fire_ember"); run.Skills.Add("fire_kindle"); run.Skills.Add("bleed_serrated");

            // 1. a level-up draft with new Legendaries
            run.Phase = RunPhase.Draft; run.MythicDraft = false; run.PendingDrafts = 1;
            run.DraftOffer.Clear(); run.DraftOffer.AddRange(new[] { "fire_flashpoint", "fire_controlled", "gen_overclocked" });
            _app.Router.Push(new RunScreen());
            yield return Wait(1.2f);
            yield return Shot("redesign1_levelup_legendaries");
            Ui.Clear(_app.ModalLayer);

            // 2. a miniboss Mythic choice with a signature offered in a Mythic's place
            run.Phase = RunPhase.Draft; run.MythicDraft = true; run.PendingMythicDrafts = 1;
            run.DraftOffer.Clear(); run.DraftOffer.AddRange(new[] { "mythic_thermal_shock", "fire_heat_siphon", "sig_bleed" });
            ((RunScreen)_app.Router.Current).Rebuild();
            yield return Wait(1.0f);
            yield return Shot("redesign2_mythic_with_signature");
            FindButtonWithin(_app.ModalLayer, "Draft:sig_bleed")?.onClick.Invoke();
            yield return Wait(0.6f);
            Ui.Clear(_app.ModalLayer);
            run = _svc.State.Run.Active;

            // 3. the skill book with two signatures
            run.Phase = RunPhase.AwaitRoll; run.DraftOffer.Clear(); run.PendingMythicDrafts = 0;
            if (!run.Skills.Contains("sig_bleed")) { run.Skills.Add("sig_bleed"); run.SignatureIds.Add("sig_bleed"); }
            ((RunScreen)_app.Router.Current).Rebuild();
            yield return Wait(0.4f);
            Ui.Clear(_app.ModalLayer);
            FindButton(_app.Root, "SkillsButton")?.onClick.Invoke();
            yield return Wait(0.6f);
            yield return Shot("redesign3_skillbook_two_signatures");
            Ui.Clear(_app.ModalLayer);

            // 4. the Resonance event: two signatures or coins
            run.Phase = RunPhase.Event; run.EventId = RunSystem.ResonanceEvent; run.EventTile = -1;
            run.SignatureChoice.Clear(); run.SignatureChoice.AddRange(new[] { "sig_radiation", "sig_ice" });
            ((RunScreen)_app.Router.Current).Rebuild();
            yield return Wait(1.0f);
            yield return Shot("redesign4_resonance_event");
            Ui.Clear(_app.ModalLayer);
            run.Phase = RunPhase.AwaitRoll; run.EventId = null;

            // 5. a battle: Thermal Shock stuns a heated enemy and locks its Heat
            var skills = new List<string> { "sig_fire", "mythic_thermal_shock", "fire_flashpoint" };
            var battle = ShockBattle(c, skills);
            int lockAt = battle.Log.FindIndex(e => e.Action == BattleAction.StatusBlocked);
            Assert.That(lockAt, Is.GreaterThan(0), "the showcase fight locks Heat");
            var ctx = BattleHudContext.FromRun(run, c);
            ctx.Skills = skills;
            var playback = new BattlePlaybackScreen(battle, ctx);
            _app.Router.Push(playback);
            yield return Wait(0.5f);
            playback.PlaySteps(Math.Max(0, lockAt + 1 - playback.NextIndex));
            yield return Wait(playback.StepSeconds * 0.6f);
            yield return Shot("redesign5_battle_thermal_shock");
            int skip = battle.Log.FindIndex(lockAt, e => e.Action == BattleAction.SkipTurn);
            if (skip > 0)
            {
                playback.PlaySteps(Math.Max(0, skip + 1 - playback.NextIndex));
                yield return Wait(playback.StepSeconds * 0.6f);
                yield return Shot("redesign5b_battle_stunned");
            }
            BattlePlaybackScreen.Instant = true;
            yield return Wait(0.2f);
            BattlePlaybackScreen.Instant = false;
            _app.Router.Pop();
            yield return Wait(0.3f);

            // 6. the laboratory: the Time package after Alien
            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            foreach (var p in new[] { "pkg_combo", "pkg_ice", "pkg_lightning", "pkg_counter", "pkg_alien" }) _svc.State.Lab.Entries[p] = new LabEntry { Level = 1 };
            foreach (var n in new[] { "lab_atk_1", "lab_hp_1", "lab_def_1", "lab_alien_power_1" }) _svc.State.Lab.Entries[n] = new LabEntry { Level = 2 };
            _app.Open("laboratory");
            yield return Wait(0.8f);
            ((LaboratoryScreen)_app.Router.Current).NodeModal("pkg_time");
            yield return Wait(0.5f);
            yield return Shot("redesign6_lab_time_package");
        }

        private static BattleResult ShockBattle(ContentDb c, List<string> skills)
        {
            BattleResult best = null;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var setup = new BattleSetup { RoundCap = 12 };
                var lo = new CombatLoadout { MountId = "skiff" };
                lo.WormStats.Add(new StatMod(StatId.Hp, 6000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Speed, 100));
                setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, skills, null, null));
                setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 3));
                setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "ember_imp", 3));
                var r = BattleSimulator.Simulate(setup, c, new Rng(seed));
                best = r;
                if (r.Log.Exists(e => e.Action == BattleAction.StatusBlocked) && r.Log.Exists(e => e.Action == BattleAction.SkipTurn)) break;
            }
            return best;
        }

        private static Button FindButton(Transform root, string name)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(true)) if (b.name == name) return b;
            return null;
        }

        private static Button FindButtonWithin(Transform root, string childName)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(true))
                foreach (var t in b.GetComponentsInChildren<Transform>(true))
                    if (t.name == childName) return b;
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
