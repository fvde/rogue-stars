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
    /// Renders the vertical slice at the reference resolution (1080×2400): the iso run board with its HUD (a fresh board and
    /// a few rolls in), the level-up draft over the dimmed board, and the battle. Explicit (<c>-testFilter
    /// SliceShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/slice. Needs a graphics
    /// device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the vertical-slice screenshots; run with -testFilter SliceShowcaseTests")]
    public sealed class SliceShowcaseTests
    {
        /// <summary>Capture size: 1080 × 2400, or $ROGUESTARS_SHOT_SIZE ("1440x3200", "1080x1920", …) for other aspects.</summary>
        private static int W = 1080, H = 2400;
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
            var size = (Environment.GetEnvironmentVariable("ROGUESTARS_SHOT_SIZE") ?? "").Split('x');
            if (size.Length == 2 && int.TryParse(size[0], out int sw) && int.TryParse(size[1], out int sh)) { W = sw; H = sh; }
            else { W = 1080; H = 2400; }
            _dir = Path.Combine(Application.temporaryCachePath, "slice_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_slice", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "slice"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            var c = _svc.Content;
            _svc.State.Counters[Requirements.MainLevelCounter] = 12;
            for (int i = 1; i <= 12; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.Refresh(_ => { });

            // 1. a fresh board (level 3)
            yield return PlayLevel(c, 3, 0);
            yield return Wait(1.0f);
            yield return Shot("s1_board_start");

            // 2. a few rolls in (landed tiles coloured, beaten markers gone, the camera following)
            yield return Steps(6);
            yield return Wait(1.2f);
            yield return Shot("s1_board_mid");

            // 2b. the player zoomed out and looked around (pinch / drag), then the view after a roll eased it back
            if (_app.Router.Current is RunScreen zs && zs.World != null)
            {
                var hs = new Vector2(zs.BodyFrame.rect.width, zs.BodyFrame.rect.height);
                var centre = hs / 2;
                zs.MoveView(centre, centre, 0.6f);
                zs.MoveView(centre, centre + new Vector2(0, 260), 1);
                yield return Wait(0.3f);
                yield return Shot("s1_board_zoomed");
            }

            // 3. the level-up draft over the dimmed board
            var run = _svc.State.Run.Active;
            if (run != null && OpenDraft(c, run))
            {
                ((RunScreen)_app.Router.Current).Rebuild();
                yield return Wait(1.0f);
                yield return Shot("s2_levelup");
                Ui.Clear(_app.ModalLayer);
            }

            // 4. the battle mid-fight
            var skills = FiringSkills(c, 9);
            var battle = SkillBattle(c, skills);
            var ctx = BattleHudContext.FromRun(_svc.State.Run.Active, c);
            ctx.Skills = skills;
            ctx.Levels = new Dictionary<string, int>();
            for (int i = 0; i < skills.Count; i++) ctx.Levels[skills[i]] = 1 + i % 3;
            var playback = new BattlePlaybackScreen(battle, ctx);
            _app.Router.Push(playback);
            yield return Wait(1.6f);
            yield return Shot("s3_battle");
            yield return Wait(1.2f);
            yield return Shot("s3_battle_later");
        }

        private IEnumerator PlayLevel(ContentDb c, int number, int steps)
        {
            var level = c.MainLevel(number);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            _svc.State.Run.Active.RunStats.Add(new StatMod(StatId.Hp, 0, 400));
            _svc.State.Run.Active.RunStats.Add(new StatMod(StatId.Atk, 0, 300));
            _app.Router.Push(new RunScreen());
            yield return Steps(steps);
        }

        private IEnumerator Steps(int steps)
        {
            BattlePlaybackScreen.Instant = true;
            for (int i = 0; i < steps && _svc.State.Run.Active != null; i++)
            {
                _svc.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = true });
                for (int f = 0; f < 400; f++)
                {
                    if (_app.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); b.Continue(); }
                    if (_app.Router.Current is RunScreen s && !s.Animating) break;
                    yield return null;
                }
                var run = _svc.State.Run.Active;
                if (run != null && run.Phase != RunPhase.AwaitRoll) { Ui.Clear(_app.ModalLayer); _svc.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = true }); }
                yield return Wait(0.15f);
            }
            for (int f = 0; f < 600; f++)
            {
                if (_app.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); b.Continue(); }
                if (_app.Router.Current is RunScreen s && !s.Animating) break;
                yield return null;
            }
            // leave every decision (shops, events, drafts) with the default choice: back to the board, ready to roll
            for (int i = 0; i < 12 && _svc.State.Run.Active != null && _svc.State.Run.Active.Phase != RunPhase.AwaitRoll; i++)
            {
                _svc.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = false });
                for (int f = 0; f < 400; f++)
                {
                    if (_app.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); b.Continue(); }
                    if (_app.Router.Current is RunScreen s && !s.Animating) break;
                    yield return null;
                }
            }
            BattlePlaybackScreen.Instant = false;
            Ui.Clear(_app.ModalLayer);
            if (_app.Router.Current is RunScreen rs) rs.Rebuild();
            yield return Wait(0.3f);
            Ui.Clear(_app.ModalLayer);
        }

        /// <summary>Puts the run into a level-up draft (three offered skills) so the draft sheet opens.</summary>
        private static bool OpenDraft(ContentDb c, ActiveRun run)
        {
            if (run.Phase != RunPhase.AwaitRoll) return false;
            var offer = new List<string>();
            foreach (var s in c.Skills.Values)
            {
                if (offer.Count >= 3) break;
                if (s.Source != SkillSource.Run || !s.Draftable || s.RequiresFamilyCount > 0 || s.Requires.Count > 0 || run.Skills.Contains(s.Id)) continue;
                if (offer.Count == 1 && RunTuning.RunTier(s.Rarity) != Rarity.Legendary) continue;
                if (offer.Count == 2 && RunTuning.RunTier(s.Rarity) != Rarity.Mythic) continue;
                offer.Add(s.Id);
            }
            if (offer.Count < 3) return false;
            run.DraftOffer.Clear();
            run.DraftOffer.AddRange(offer);
            run.Phase = RunPhase.Draft;
            run.PendingDrafts = Math.Max(1, run.PendingDrafts);
            run.MythicDraft = false;
            return true;
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
