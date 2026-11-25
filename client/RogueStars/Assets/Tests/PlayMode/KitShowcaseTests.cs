using System;
using System.Collections;
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
    /// Hero and mount kits: renders the Heroes screen, the Mounts screen and a battle with a mount shot (Scout Saucer's Scan Pulse
    /// on its reload) and a hero Ability (Star Knight's Starfall, "Starfall 2/4" in the kit HUD). Explicit (run with
    /// <c>-testFilter KitShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/kits. Needs a graphics
    /// device: run in batchmode WITHOUT -nographics.
    /// </summary>
    [Explicit("renders hero / mount kit screenshots; run with -testFilter KitShowcaseTests")]
    public sealed class KitShowcaseTests
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
        public IEnumerator Kits()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "kits_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_kits", "Wiggles", new ManualClock(T0), 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "kits"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.3f);

            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            var s = _svc.State;
            s.Heroes.Owned["hero_star_knight"] = new HeroEntry { Stars = 5, Level = 30 };
            s.Heroes.Owned["hero_chef"] = new HeroEntry { Stars = 3, Level = 12 };
            s.Heroes.Owned["hero_hourglass_hermit"] = new HeroEntry { Stars = 1, Level = 1 };
            s.Heroes.Equipped = "hero_star_knight";
            s.Mounts.Owned["mount_scout_saucer"] = new MountEntry { Stars = 5, Level = 18 };
            s.Mounts.Owned["mount_star_lancer"] = new MountEntry { Stars = 8, Level = 40 };
            s.Mounts.Owned["mount_chrono_skiff"] = new MountEntry { Stars = 2, Level = 5 };
            s.Mounts.Active = "mount_scout_saucer";
            _svc.Refresh(_ => { });

            _app.Router.Reset(new HubScreen());
            _app.Router.Push(new HeroesScreen());
            yield return Wait(0.4f);
            yield return Shot("kits_01_heroes");
            _app.Router.Reset(new HubScreen());
            _app.Router.Push(new MountsScreen());
            yield return Wait(0.4f);
            yield return Shot("kits_02_mounts");

            // a battle: Star Knight 5★ on a Scout Saucer 5★ with Ignite, three hitters (shields absorb → Starfall)
            var c = _svc.Content;
            var lo = new CombatLoadout { DisplayName = "Wiggles", MountId = "mount_scout_saucer", HeroId = "hero_star_knight" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 4000)).Add(new StatMod(StatId.Atk, 160)).Add(new StatMod(StatId.Speed, 100)).Add(new StatMod(StatId.CritChance, 15));
            lo.MetaSkillIds.AddRange(HeroSystem.TraitSkills(c, "hero_star_knight", 5));
            lo.MetaSkillIds.AddRange(MountSystem.WeaponSkills(c, "mount_scout_saucer", 5));
            var setup = new BattleSetup { RoundCap = 12 };
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, new[] { "sig_fire" }));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "magma_slug", 3, 0, 300));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "ash_wraith", 3, 0, 300));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 3, 0, 300));
            var battle = BattleSimulator.Simulate(setup, c, new Rng(5UL));
            int volley = battle.Log.FindIndex(e => e.Action == BattleAction.Volley && e.Round >= 1);
            int ability = battle.Log.FindIndex(e => e.Action == BattleAction.Ability);
            Assert.That(volley, Is.GreaterThanOrEqualTo(0), "a mount shot");
            Assert.That(ability, Is.GreaterThanOrEqualTo(0), "a hero ability");

            var play = new BattlePlaybackScreen(battle);
            _app.Router.Reset(new HubScreen());
            _app.Router.Push(play);
            yield return Wait(0.2f);
            play.PlaySteps(Math.Max(0, volley - 1));
            yield return WaitIndex(play, volley + 2);
            yield return Shot("kits_03_battle_mount_shot");
            if (play.NextIndex < ability - 1) play.PlaySteps(ability - 1 - play.NextIndex);
            yield return WaitIndex(play, ability + 2);
            yield return Shot("kits_04_battle_hero_ability");
            Assert.That(play.KitHud.Any(t => t.StartsWith("Starfall ")), Is.True, "the kit HUD shows Starfall n/4");
        }

        private static IEnumerator WaitIndex(BattlePlaybackScreen play, int index)
        {
            float end = Time.realtimeSinceStartup + 20;
            while (play.NextIndex < index && !play.Finished && Time.realtimeSinceStartup < end) yield return null;
            yield return Wait(0.15f);
        }

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("KitCamera");
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
            _app.Feedback.Tick(60);
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
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
