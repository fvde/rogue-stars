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

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// Sidekick + aliens pass screenshots at the reference resolution (1080×2400): a battle with the squad (Pebblehorn's Guard,
    /// Swarmheart hatching drones, Voidmaw echoing) along the left edge, a sidekick acting, the guard softening a hit, a hatch and
    /// an echo; the aliens screen (ability of the star, next star, act frequency, traits, genes "coming soon") and the breeding
    /// screen (Grain's sink). Explicit (<c>-testFilter SidekickShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR or
    /// client/RogueStars/Screenshots/sidekicks. Needs a graphics device: run WITHOUT -nographics.
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

            // 1. a battle with the reworked squad: Pebblehorn 5★ (Stone Guard −35%), Swarmheart 7★ (Hatch: drones), Voidmaw 10★ (Void Echo)
            var battle = Battle(c);
            int guard = battle.Log.FindIndex(e => e.Action == BattleAction.Guard && battle.Fighters[e.Actor].Id == "alien_pebblehorn");
            int guarded = battle.Log.FindIndex(e => e.Action == BattleAction.Guarded);
            int hatch = battle.Log.FindIndex(e => e.Action == BattleAction.Summon && battle.Fighters[e.Actor].Id == "alien_swarmheart");
            int echo = battle.Log.FindIndex(e => e.Action == BattleAction.Echo && battle.Fighters[e.Actor].Id == "alien_voidmaw");
            Assert.That(guard, Is.GreaterThanOrEqualTo(0), "Pebblehorn raises a guard");
            Assert.That(guarded, Is.GreaterThan(guard), "the guard softens a hit");
            var ctx = BattleHudContext.FromRun(run, c);
            ctx.Skills = new List<string> { "sig_alien", "alien_bond" };
            var playback = new BattlePlaybackScreen(battle, ctx);
            _app.Router.Push(playback);
            yield return Wait(0.5f);
            yield return Shot("ar0_battle_start");
            foreach (var (at, name) in new[] { (hatch, "ar1_hatch"), (guard, "ar2_stone_guard_raised"), (guarded, "ar3_guarded_hit"), (echo, "ar4_void_echo") }
                         .Where(x => x.Item1 >= 0).OrderBy(x => x.Item1))
            {
                if (at < playback.NextIndex) continue;
                playback.PlaySteps(Math.Max(0, at - playback.NextIndex)); // up to the entry; the live playback animates it
                yield return Wait(playback.StepSeconds * 1.2f);
                yield return Shot(name);
            }
            BattlePlaybackScreen.Instant = true;
            yield return Wait(0.2f);
            BattlePlaybackScreen.Instant = false;
            _app.Router.Pop();
            yield return Wait(0.3f);

            // 2. the aliens screen: ability of the star, next star, how often it acts, traits, genes "coming soon" — no levels
            _svc.State.Aliens.Owned.Clear();
            foreach (var (id, st, traits) in new[] { ("alien_swarmheart", 7, new[] { "trait_eager", "trait_wide" }), ("alien_pebblehorn", 5, new[] { "trait_bulwark", "trait_deep_digger" }),
                         ("alien_shiverpup", 1, new[] { "trait_swift", "trait_twin" }), ("alien_frostmite", 3, new[] { "trait_wide" }), ("alien_thornqueen", 2, new[] { "trait_lingering" }) })
            {
                var a = AlienSystem.RollNew(new Rng(7), c, c.AlienType(id));
                a.Stars = st;
                a.Traits = new List<string>(traits);
                _svc.State.Aliens.Owned[id] = a;
            }
            _svc.State.Wallet.Add(Currencies.AlienShards("alien_pebblehorn"), 12);
            _svc.State.Aliens.Squad = new List<string> { "alien_shiverpup", "alien_frostmite" };
            _svc.Refresh(_ => { });
            _app.Open("aliens");
            yield return Wait(0.8f);
            yield return Shot("ar5_aliens_screen");

            // 3. the breeding screen: Grain's sink — two parents, the baby's species odds, the release shards; a newborn waiting
            _svc.State.Wallet.Add(Currencies.Grain, 12_000);
            _svc.State.Breeding.Pending.Add(new PendingChild { Id = "breed_demo", TypeId = "alien_pebblehorn", ParentA = "alien_pebblehorn", ParentB = "alien_shiverpup",
                Traits = new List<string> { "trait_swift", "trait_bulwark" } });
            _svc.Refresh(_ => { });
            _app.Router.Push(new BreedingScreen("alien_swarmheart", "alien_shiverpup"));
            yield return Wait(0.8f);
            yield return Shot("ar6_breeding_screen");
        }

        private static CombatAlien Alien(ContentDb c, string id, int star)
        {
            var def = c.AlienType(id);
            var a = new CombatAlien { AlienTypeId = id, Name = def.Name, Stars = star };
            a.Stats.Add(new StatMod(StatId.Speed, def.BaseSpeed));
            a.SkillIds.Add(AlienSystem.AbilityAt(c, id, star));
            return a;
        }

        private static BattleResult Battle(ContentDb c)
        {
            BattleResult best = null;
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var lo = new CombatLoadout { DisplayName = "Wiggles", MountId = "mount_hive_carrier" };
                lo.WormStats.Add(new StatMod(StatId.Hp, 6000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Def, 10)).Add(new StatMod(StatId.Speed, 100));
                lo.MountStats.Add(new StatMod(StatId.Atk, 40)).Add(new StatMod(StatId.Hp, 10));
                lo.Aliens.Add(Alien(c, "alien_pebblehorn", 5));
                lo.Aliens.Add(Alien(c, "alien_swarmheart", 7));
                lo.Aliens.Add(Alien(c, "alien_voidmaw", 10));
                var setup = new BattleSetup { RoundCap = 10 };
                setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, new[] { "sig_alien" }));
                setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 2, 0, 600));
                setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "ember_imp", 2, 0, 600));
                var r = BattleSimulator.Simulate(setup, c, new Rng(seed));
                best = r;
                if (r.Log.Exists(e => e.Action == BattleAction.Guarded) && r.Log.Exists(e => e.Action == BattleAction.Echo)) break;
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
