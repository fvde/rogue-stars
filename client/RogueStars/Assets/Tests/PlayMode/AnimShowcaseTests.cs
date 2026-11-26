using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Client.World.Anim;
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
    /// The animation integration at 1080 × 2400 (or $ROGUESTARS_SHOT_SIZE): the hub (worm + crash-site squad), the Worm screen,
    /// the Aliens screen (idle cards), the run board, a battle recorded as a frame sequence (anim_battle_f###: idle, attacks,
    /// hits, death, victory), the City with its animated buildings; plus perf numbers (update cost, vertices, ticking creatures)
    /// in anim_stats.txt. Explicit (<c>-testFilter AnimShowcaseTests</c>), needs graphics (no -nographics); writes to
    /// $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/anim-integrate.
    /// </summary>
    [Explicit("renders the animation screenshots; run with -testFilter AnimShowcaseTests")]
    public sealed class AnimShowcaseTests
    {
        private static int W = 1080, H = 2400;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private LocalGameService _svc;
        private ManualClock _clock;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;
        private readonly StringBuilder _stats = new StringBuilder();

        private static BattleResult Battle(ContentDb c)
        {
            var lo = new CombatLoadout { DisplayName = "Wiggles", MountId = "skiff" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 9000)).Add(new StatMod(StatId.Atk, 220)).Add(new StatMod(StatId.Def, 10)).Add(new StatMod(StatId.Speed, 100));
            foreach (var id in new[] { "alien_pebblehorn", "alien_glimmerfly", "alien_blorp" })
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
            foreach (var id in new[] { "ember_imp", "spark_drone", "glacier_golem" })
                if (c.Enemies.ContainsKey(id)) setup.Enemy.Add(BattleSimulator.BuildEnemy(c, id, 2, 0, 80));
            return BattleSimulator.Simulate(setup, c, new Rng(5));
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            var size = (Environment.GetEnvironmentVariable("ROGUESTARS_SHOT_SIZE") ?? "").Split('x');
            if (size.Length == 2 && int.TryParse(size[0], out int sw) && int.TryParse(size[1], out int sh)) { W = sw; H = sh; }
            _dir = Path.Combine(Application.temporaryCachePath, "anim_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _clock = new ManualClock(T0);
            _svc = new LocalGameService(_dir, "local_anim", "Wiggles", _clock, 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "anim-integrate"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            AnimSettings.Override = false;
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            var c = _svc.Content;
            _svc.State.Counters[Requirements.MainLevelCounter] = 12;
            for (int i = 1; i <= 12; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.State.Flags.Add(Features.DevUnlockAllFlag);
            foreach (var f in c.FeatureDefs.Keys) _svc.State.Onboarding.Celebrated.Add(f);
            var rewards = new List<Reward>();
            foreach (var a in c.AlienTypes.Keys.OrderBy(k => k).Take(14)) rewards.Add(new Reward(RewardKinds.Alien, a, 1));
            _svc.DevGrant(rewards);
            for (int i = 0; i < 3; i++) { var al = _svc.State.Aliens.Owned.Keys.OrderBy(k => k).Skip(i).First(); if (!_svc.State.Aliens.Squad.Contains(al)) _svc.State.Aliens.Squad.Add(al); }
            _svc.Refresh(_ => { });

            // 1. the hub: the worm breathes and the squad walks (a second frame 0.7 s later: they moved)
            HubScreen.SkyOverride = "day";
            _app.Router.RebuildCurrent();
            yield return Wait(1.2f);
            yield return Shot("anim_hub_a");
            yield return Wait(0.7f);
            yield return Shot("anim_hub_b");
            HubScreen.SkyOverride = null;

            // 2. the worm screen and the aliens screen (idle cards)
            _app.Open("worm");
            yield return Wait(0.8f);
            yield return Shot("anim_worm");
            while (_app.Router.Depth > 1) _app.Router.Pop();
            _app.Open("aliens");
            yield return Wait(0.8f);
            yield return Shot("anim_aliens_a");
            yield return Wait(0.6f);
            yield return Shot("anim_aliens_b");
            _stats.AppendLine("aliens screen: UiCreature cards = " + _app.Router.Current.Root.GetComponentsInChildren<UiCreature>().Length + ", idle-only, " + UiCreature.IdleHz + " Hz");
            while (_app.Router.Depth > 1) _app.Router.Pop();

            // 3. the run board: the worm (hopping), props shaders
            var level = c.MainLevel(5);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            var run = _svc.State.Run.Active;
            run.RunStats.Add(new StatMod(StatId.Hp, 0, 400)); run.RunStats.Add(new StatMod(StatId.Atk, 0, 300));
            run.Coins = 291;
            var board = new RunScreen();
            _app.Router.Push(board);
            yield return Wait(1.0f);
            yield return Shot("anim_board_a");
            board.World.LiftToken(0.4f);
            yield return Wait(0.3f);
            yield return Shot("anim_board_hop");
            board.World.LiftToken(0);
            yield return Wait(0.8f);
            yield return Shot("anim_board_b");
            _stats.AppendLine("board: token creature vertices = " + board.World.TokenCreature.VertexCount);

            // 4. the battle as a frame sequence (15 fps, 8 s): the creatures idle, attack, get hit, die; the worm celebrates
            var battle = Battle(c);
            var ctx = BattleHudContext.FromRun(run, c);
            var screen = new BattlePlaybackScreen(battle, ctx);
            _app.Router.Push(screen);
            yield return Wait(0.5f);
            string frames = Path.Combine(_shots, "battle_frames");
            Directory.CreateDirectory(frames);
            foreach (var f in Directory.GetFiles(frames, "*.png")) File.Delete(f);
            Time.captureFramerate = 15;
            BattlePlaybackScreen.TimeScale = 2.2f;
            int n = 0;
            float cpu = 0; int cpuN = 0;
            try
            {
                int doneAt = -1;
                for (int i = 0; i < 15 * 24; i++)
                {
                    if (screen.Finished && doneAt < 0) doneAt = i;
                    if (doneAt >= 0 && i - doneAt > 24) break; // the victory pose played out
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    var sw2 = System.Diagnostics.Stopwatch.StartNew();
                    for (int k = 0; k < 8; k++) foreach (var uc in screen.Root.GetComponentsInChildren<UiCreature>()) uc.Tick(0.016f);
                    sw2.Stop(); cpu += (float)sw2.Elapsed.TotalMilliseconds; cpuN++;
                    if (i % 2 == 0) yield return ShotFrame(frames, n++);
                }
            }
            finally { Time.captureFramerate = 0; BattlePlaybackScreen.TimeScale = 1f; }
            var uis = screen.Root.GetComponentsInChildren<UiCreature>();
            _stats.AppendLine("battle: " + uis.Length + " creatures, " + SpineRig.VertexCount + " vertices each (" + uis.Length * SpineRig.VertexCount + " total), "
                + string.Join(", ", Enumerable.Range(0, 6).Select(k => ((CreatureClip)k) + " x" + screen.ClipCounts[k])));
            _stats.AppendLine("battle: deform + mesh update of " + uis.Length + " creatures x8 per frame = " + (cpu / Math.Max(1, cpuN)).ToString("0.00") + " ms (editor, Mono)");
            yield return Shot("anim_battle_end");
            while (_app.Router.Depth > 1) _app.Router.Pop();

            // 5. the city with animated buildings
            _app.Open("city");
            var city = (CityScreen)_app.Router.Current;
            Populate(c);
            city.Rebuild();
            var t = c.CityTuning;
            city.Focus(t.FireX + 0.5f, t.FireY + 0.5f, CityWorld.OverviewWidth);
            yield return Wait(3f);
            yield return Shot("anim_city_a");
            yield return Wait(0.8f);
            yield return Shot("anim_city_b");
            _stats.AppendLine("city: sprites " + city.World.ActiveSprites + " (budget " + CityWorld.SpriteBudget + "), walkers " + city.World.WalkersVisible);
            Assert.That(city.World.ActiveSprites, Is.LessThanOrEqualTo(CityWorld.SpriteBudget));
            // reduced animation: everything static
            AnimSettings.Override = true;
            yield return Wait(0.5f);
            yield return Shot("anim_city_reduced");
            AnimSettings.Override = false;
            File.WriteAllText(Path.Combine(_shots, "anim_stats.txt"), _stats.ToString());
            Debug.Log("[AnimShowcase]\n" + _stats);
        }

        private void Populate(ContentDb c)
        {
            var rewards = new List<Reward> { Reward.Cur(Currencies.Biomass, 9_000_000), Reward.Cur(Currencies.Ore, 9_000_000), Reward.Cur(Currencies.GearCores, 200_000), Reward.Cur(Currencies.Diamonds, 90_000) };
            var bps = new[]
            {
                "bp_biomass_farm", "bp_grain_farm", "bp_ore_mine", "bp_fuel_refinery", "bp_gear_works", "bp_plate_foundry", "bp_diamond_mine", "bp_token_relay",
                "bp_signal_beacon", ContentDb.LaunchPodId, "bp_breeding_pen", "bp_observatory", "bp_stamina_well", "bp_supply_depot",
                "bp_regen_spire", "bp_command_spire", "bp_barracks_dome", "bp_part_fabricator", "bp_gene_lab", ContentDb.LaboratoryId, ContentDb.ShardReactorId,
            };
            foreach (var b in bps) rewards.Add(new Reward(RewardKinds.Blueprint, b, 1));
            _svc.DevGrant(rewards);
            GameState S() => _svc.State;
            for (int i = 0; i < 7; i++)
            {
                var p = c.CityPatches.Where(x => CitySystem.DigBlockReason(S(), c, x.Id) == null)
                    .OrderBy(x => Mathf.Abs(PatchCentre(x).x - 9) + Mathf.Abs(PatchCentre(x).y - 9)).FirstOrDefault();
                if (p == null) break;
                _svc.ExecuteNow(new CityDig { PatchId = p.Id });
                _clock.Advance(26 * 3_600_000L);
                _svc.ExecuteNow(new CityCollect());
            }
            var spine = CityPlanner.SpineCells(c);
            _svc.ExecuteNow(new CityStreets { Adds = CityPlanner.PaveableSpine(S(), c, spine) });
            foreach (var bp in bps.Concat(new[] { "bp_biomass_farm", "bp_biomass_farm", "bp_ore_mine" }))
            {
                var plan = CityPlanner.Find(S(), c, bp, null, 2, spine);
                if (plan == null) continue;
                if (plan.Streets.Count > 0) _svc.ExecuteNow(new CityStreets { Adds = plan.Streets });
                _svc.ExecuteNow(new CityPlace { BlueprintId = bp, X = plan.X, Y = plan.Y, Rotated = plan.Rotated });
            }
            _clock.Advance(14 * 3_600_000L);
            _svc.Refresh(_ => { });
            var placed = S().City.Buildings.Where(b => b.Placed && b.BlueprintId != ContentDb.LaunchPodId).ToList();
            var aliens = S().Aliens.Owned.Keys.OrderBy(k => k).ToList();
            for (int bi = 0; bi < placed.Count && bi < aliens.Count; bi++) _svc.ExecuteNow(new AlienAssign { AlienTypeId = aliens[bi], BuildingUid = placed[bi].Uid });
            _clock.Advance(3_600_000L);
            _svc.Refresh(_ => { });
        }

        private static Vector2 PatchCentre(CityPatchDef p)
        {
            float sx = 0, sy = 0;
            foreach (var c in p.Cells) { sx += c % 20; sy += c / 20; }
            return new Vector2(sx / p.Cells.Count, sy / p.Cells.Count);
        }

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("AnimShotCamera");
            _cam = go.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.orthographic = true;
            _cam.cullingMask = ~(1 << WorldBoard.Layer | 1 << CityWorld.Layer);
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

        private IEnumerator ShotFrame(string dir, int n)
        {
            if (_app.Router.Current is RunScreen rs && rs.World != null && rs.World.Cam.enabled) rs.World.Cam.Render();
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(dir, $"f_{n:000}.png"), _tex.EncodeToPNG());
            yield break;
        }

        private IEnumerator Shot(string name)
        {
            _app.Feedback.Tick(0.016f);
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (_app.Router.Current is RunScreen rs && rs.World != null && rs.World.Cam.enabled) rs.World.Cam.Render();
            if (_app.Router.Current is CityScreen cs && cs.World != null && cs.World.Cam.enabled) cs.World.Cam.Render();
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
            AnimSettings.Override = null;
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
