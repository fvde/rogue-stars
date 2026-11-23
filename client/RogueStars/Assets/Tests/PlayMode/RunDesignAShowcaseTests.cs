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
    /// Renders run screen design A ("Top status bar") at 1080×2340: the lava board (ember portal) and the seabed board
    /// (glacier portal) with the hero token, slabs and the skill tray, a fork, and the battle with its skill strip firing.
    /// Explicit (<c>-testFilter RunDesignAShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR or
    /// client/RogueStars/Screenshots/design-a. Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders design A screenshots; run with -testFilter RunDesignAShowcaseTests")]
    public sealed class RunDesignAShowcaseTests
    {
        private const int W = 1080, H = 2340;
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
            _dir = Path.Combine(Application.temporaryCachePath, "design_a_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_design_a", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "design-a"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
                foreach (var uid in PartsSystem.SortedPartUids(_svc.State))
                {
                    if (PartsSystem.IsEquipped(_svc.State, uid)) continue;
                    if (_svc.ExecuteNow(new PartEquip { Uid = uid, Slot = slot }).Ok) break;
                }
            var c = _svc.Content;
            _svc.State.Counters[Requirements.MainLevelCounter] = 12;
            for (int i = 1; i <= 12; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.Refresh(_ => { });

            // 1. lava board (level 3, ember portal): a few steps in, a build of skills on the tray
            yield return PlayLevel(c, 3, 5);
            GiveSkills(c, 9);
            ((RunScreen)_app.Router.Current).Rebuild();
            yield return Wait(0.9f);
            yield return Shot("a1_run_lava");

            // 2. mid-hop: roll by hand and catch the token in the air
            var rs = (RunScreen)_app.Router.Current;
            var run = _svc.State.Run.Active;
            if (run != null && run.Phase == RunPhase.AwaitRoll)
            {
                _svc.ExecuteNow(new RunRoll());
                yield return Wait(1.25f);
                yield return Shot("a2_run_roll_hop");
                for (int f = 0; f < 600 && (rs.Animating || _app.Router.Current is BattlePlaybackScreen); f++)
                {
                    if (_app.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); b.Continue(); }
                    yield return null;
                }
                Ui.Clear(_app.ModalLayer);
            }

            // 3. the battle in the same layout: run skills that fire (burn, freeze, poison…), mid-fight and at the end
            var skills = FiringSkills(c, 7);
            var battle = SkillBattle(c, skills);
            var ctx = BattleHudContext.FromRun(_svc.State.Run.Active, c);
            ctx.Skills = skills;
            ctx.Levels = new Dictionary<string, int>();
            for (int i = 0; i < skills.Count; i++) ctx.Levels[skills[i]] = 1 + i % 3;
            var playback = new BattlePlaybackScreen(battle, false, ctx);
            _app.Router.Push(playback);
            yield return Wait(1.9f);
            yield return Shot("a3_battle_skill_fire");
            yield return Wait(1.3f);
            yield return Shot("a4_battle_skill_fire_later");
            playback.PlayAll();
            yield return Wait(0.6f);
            yield return Shot("a5_battle_end");
            playback.Continue();
            yield return Wait(0.3f);

            // 4. seabed board (level 11, glacier portal)
            _svc.ExecuteNow(new RunAbandon());
            _app.Router.Reset(new HubScreen());
            yield return PlayLevel(c, 11, 0);
            GiveSkills(c, 4);
            ((RunScreen)_app.Router.Current).Rebuild();
            yield return Wait(0.9f);
            yield return Shot("a6_run_seabed");

            // 5. a few rolls later on the seabed board (it stops at the first fork)
            run = _svc.State.Run.Active;
            if (run != null) { run.WormHp = -1; run.RunStats.Add(new StatMod(StatId.Def, 0, 400)); }
            if (run != null)
            {
                BattlePlaybackScreen.Instant = true;
                for (int i = 0; i < 40 && _svc.State.Run.Active != null && _svc.State.Run.Active.Phase != RunPhase.ChooseBranch; i++)
                {
                    _svc.ExecuteNow(new RunAutoStep { Farming = false });
                    while (_app.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); _app.Router.Pop(); }
                    var r = _svc.State.Run.Active;
                    if (r != null && r.Phase != RunPhase.AwaitRoll && r.Phase != RunPhase.ChooseBranch) { Ui.Clear(_app.ModalLayer); _svc.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = true }); }
                    yield return null;
                }
                BattlePlaybackScreen.Instant = false;
                if (_svc.State.Run.Active != null && _svc.State.Run.Active.Phase == RunPhase.ChooseBranch)
                {
                    for (int f = 0; f < 300 && ((RunScreen)_app.Router.Current).Animating; f++) yield return null;
                    Ui.Clear(_app.ModalLayer);
                    yield return Wait(1.2f);
                    yield return Shot("a7_run_seabed_after_roll");
                }
            }
        }

        private IEnumerator PlayLevel(ContentDb c, int number, int steps)
        {
            var level = c.MainLevel(number);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            // a sturdy worm for the pictures (the starter kit alone does not survive level 11)
            _svc.State.Run.Active.RunStats.Add(new StatMod(StatId.Hp, 0, 400));
            _svc.State.Run.Active.RunStats.Add(new StatMod(StatId.Atk, 0, 300));
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

        /// <summary>Draftable run skills of mixed tiers and families (the tray's colours and "+N").</summary>
        private void GiveSkills(ContentDb c, int n)
        {
            var run = _svc.State.Run.Active;
            if (run == null) return;
            int added = 0;
            foreach (var s in c.Skills.Values)
            {
                if (added >= n) break;
                if (s.Source != SkillSource.Run || !s.Draftable || s.RequiresFamilyCount > 0 || s.Requires.Count > 0 || run.Skills.Contains(s.Id)) continue;
                if (added % 3 == 1 && RunTuning.RunTier(s.Rarity) != Rarity.Legendary) continue;
                if (added == 4 && RunTuning.RunTier(s.Rarity) != Rarity.Mythic) continue;
                run.Skills.Add(s.Id);
                if (added % 2 == 0) run.SkillLevels[s.Id] = 2;
                added++;
            }
        }

        /// <summary>Run skills that act in a fight (their effects are logged with their id): statuses, hits, heals.</summary>
        private static List<string> FiringSkills(ContentDb c, int n)
        {
            var list = new List<string>();
            var families = new List<SkillFamily>();
            foreach (var s in c.Skills.Values)
            {
                if (list.Count >= n) break;
                if (s.Source != SkillSource.Run || !s.Draftable || s.RequiresFamilyCount > 0 || s.Requires.Count > 0) continue;
                if (s.Triggers == null || s.Triggers.Count == 0) continue;
                if (families.Contains(s.Family) && list.Count < 4) continue;
                families.Add(s.Family);
                list.Add(s.Id);
            }
            return list;
        }

        private static BattleResult SkillBattle(ContentDb c, List<string> skills)
        {
            var setup = new BattleSetup { RoundCap = 12 };
            var lo = new CombatLoadout { MountId = "skiff" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 4000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Speed, 100)).Add(new StatMod(StatId.CritChance, 25));
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, skills));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 4));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 4));
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
