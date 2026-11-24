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
    /// Sidekick pass screenshots at the reference resolution (1080×2400): a battle with the squad and a Hive Carrier drone along
    /// the left edge, a sidekick acting (hop, ability name, effect; sidekick pips in NEXT), a guard intercepting a hit, and the
    /// aliens screen (ability, power, act frequency). Explicit (<c>-testFilter SidekickShowcaseTests</c>); writes to
    /// $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/sidekicks. Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the sidekick screenshots; run with -testFilter SidekickShowcaseTests")]
    public sealed class SidekickShowcaseTests
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
            _dir = Path.Combine(Application.temporaryCachePath, "sk_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_sk", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "sidekicks"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            var c = _svc.Content;
            _svc.State.Counters[Requirements.MainLevelCounter] = 12;
            for (int i = 1; i <= 12; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            var level = c.MainLevel(3);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal, SignatureId = "sig_alien" });
            if (!start.Ok) start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            var run = _svc.State.Run.Active;

            // 1. a battle with sidekicks: Pebblehorn (Block), Emberkit (Kindle), Mossling (heal) and a drone from the Hive Carrier
            var battle = Battle(c);
            int act = battle.Log.FindIndex(e => e.Action == BattleAction.SidekickAct && e.Stacks == 0 && battle.Fighters[e.Actor].Id == "alien_emberkit");
            if (act < 0) act = battle.Log.FindIndex(e => e.Action == BattleAction.SidekickAct);
            int block = battle.Log.FindIndex(e => e.Action == BattleAction.Block);
            Assert.That(act, Is.GreaterThanOrEqualTo(0), "a sidekick acts");
            Assert.That(block, Is.GreaterThan(0), "a guard intercepts a hit");
            var ctx = BattleHudContext.FromRun(run, c);
            ctx.Skills = new List<string> { "sig_alien", "alien_hivemind", "alien_bond" };
            var playback = new BattlePlaybackScreen(battle, false, ctx);
            _app.Router.Push(playback);
            yield return Wait(0.5f);
            yield return Shot("sk0_battle_start");
            playback.PlaySteps(Math.Max(0, act - playback.NextIndex));
            yield return Wait(playback.StepSeconds * 1.3f); // the act marker plays (hop + name), then its effect
            yield return Shot("sk1_sidekick_act");
            yield return Wait(playback.StepSeconds * 0.9f);
            yield return Shot("sk1b_sidekick_effect");
            playback.PlaySteps(Math.Max(0, block - playback.NextIndex));
            yield return Wait(playback.StepSeconds * 0.75f);
            yield return Shot("sk2_block");
            BattlePlaybackScreen.Instant = true;
            yield return Wait(0.2f);
            BattlePlaybackScreen.Instant = false;
            _app.Router.Pop();
            yield return Wait(0.3f);

            // 2. the aliens screen: each alien's ability, ability power and how often it acts
            foreach (var (id, lv, st) in new[] { ("alien_pebblehorn", 12, 3), ("alien_emberkit", 20, 5), ("alien_nebulisk", 8, 1), ("alien_mossling", 5, 1) })
            {
                var a = AlienSystem.RollNew(new Rng(7), c, c.AlienType(id));
                a.Level = lv; a.Stars = st;
                _svc.State.Aliens.Owned[id] = a;
            }
            _svc.Refresh(_ => { });
            _app.Open("aliens");
            yield return Wait(0.8f);
            yield return Shot("sk3_aliens_screen");
        }

        private static CombatAlien Alien(ContentDb c, string id)
        {
            var def = c.AlienType(id);
            var a = new CombatAlien { AlienTypeId = id, Name = def.Name };
            a.Stats.Add(new StatMod(StatId.Hp, def.BaseHp)).Add(new StatMod(StatId.Atk, def.BaseAtk)).Add(new StatMod(StatId.Def, def.BaseDef))
                .Add(new StatMod(StatId.Speed, def.BaseSpeed));
            a.SkillIds.Add(def.ArchetypeSkills[0].SkillId);
            return a;
        }

        private static BattleResult Battle(ContentDb c)
        {
            BattleResult best = null;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var lo = new CombatLoadout { DisplayName = "Wiggles", MountId = "mount_hive_carrier" };
                lo.WormStats.Add(new StatMod(StatId.Hp, 6000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Def, 10)).Add(new StatMod(StatId.Speed, 100));
                lo.MountStats.Add(new StatMod(StatId.Atk, 40)).Add(new StatMod(StatId.Hp, 10));
                lo.MetaSkillIds.Add("mount_hive_carrier_w1");
                lo.Aliens.Add(Alien(c, "alien_pebblehorn"));
                lo.Aliens.Add(Alien(c, "alien_emberkit"));
                lo.Aliens.Add(Alien(c, "alien_mossling"));
                var setup = new BattleSetup { RoundCap = 10 };
                setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, new[] { "sig_alien", "alien_hivemind" }));
                setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 2, 0, 600));
                setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "ember_imp", 2, 0, 600));
                var r = BattleSimulator.Simulate(setup, c, new Rng(seed));
                best = r;
                if (r.Log.Exists(e => e.Action == BattleAction.Block)) break;
            }
            return best;
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
