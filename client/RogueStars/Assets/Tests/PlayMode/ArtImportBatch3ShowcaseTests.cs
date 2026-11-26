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
    /// The art import of 2026-11-26 (batch 3): the edited / new enemies and the boss sprites in battle, the status icons and the
    /// guard chip in the battle chips, the drone sidekick, the summon / menu icons (event hub, exchange, shop, signals, the
    /// awaken cores in the resource bar) and the portal marker on the board's start tile. Explicit
    /// (<c>-testFilter ArtImportBatch3ShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR. Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the batch-3 art-import screenshots; run with -testFilter ArtImportBatch3ShowcaseTests")]
    public sealed class ArtImportBatch3ShowcaseTests
    {
        private static int W = 1080, H = 2400;
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
            var size = (Environment.GetEnvironmentVariable("ROGUESTARS_SHOT_SIZE") ?? "").Split('x');
            if (size.Length == 2 && int.TryParse(size[0], out int sw) && int.TryParse(size[1], out int sh)) { W = sw; H = sh; }
            else { W = 1080; H = 2400; }
            _dir = Path.Combine(Application.temporaryCachePath, "artimport3_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            var clock = new ManualClock(T0);
            _svc = new LocalGameService(_dir, "local_artimport3", "Wiggles", clock, 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "art-import"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            var c = _svc.Content;
            _svc.State.Counters[Requirements.MainLevelCounter] = 12;
            for (int i = 1; i <= 12; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.State.Flags.Add(Features.DevUnlockAllFlag);
            foreach (var f in c.FeatureDefs.Keys) _svc.State.Onboarding.Celebrated.Add(f); // no unlock moments over the shots
            _svc.DevGrant(new[]
            {
                Reward.Cur(Currencies.HeroTickets, 24), Reward.Cur(Currencies.MountTickets, 14), Reward.Cur(Currencies.Diamonds, 2000),
                Reward.Cur(Currencies.HeroSigils, 640), Reward.Cur(Currencies.MountBolts, 380), Reward.Cur(ProgressionCurrencies.PartAwakenCores, 12),
            });
            _svc.Refresh(_ => { });

            // 1. the board: the portal marker on the start tile (fresh board), then a few rolls in (the worm has left it)
            var level = c.MainLevel(3);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            _svc.State.Run.Active.RunStats.Add(new StatMod(StatId.Hp, 0, 400));
            _svc.State.Run.Active.RunStats.Add(new StatMod(StatId.Atk, 0, 300));
            _app.Router.Push(new RunScreen());
            yield return Wait(1.0f);
            Ui.Clear(_app.ModalLayer);
            yield return Shot("b3_board_start");
            yield return Steps(2);
            yield return Wait(1.0f);
            yield return Shot("b3_board_rolled");

            // 2. battles: the edited / new enemies with status skills, the drone and a guarding alien; then the bosses
            var ctx = BattleHudContext.FromRun(_svc.State.Run.Active, c);
            var skills = StatusSkills(c, 8);
            skills.Insert(0, "alien_rally"); // battle start: a drone sidekick
            ctx.Skills = skills;
            _app.Router.Push(new BattlePlaybackScreen(Battle(c, new[] { "alien_pebblehorn", "alien_shiverpup" }, skills, new[] { "magma_slug", "null_shade", "cinder_hound" }, 7), ctx));
            yield return Wait(2.2f);
            yield return Shot("b3_battle_enemies_status");
            yield return Wait(2.4f);
            yield return Shot("b3_battle_enemies_status_later");
            _app.Router.Pop();
            _app.Router.Push(new BattlePlaybackScreen(Battle(c, new[] { "alien_emberkit", "alien_zib" }, skills, new[] { "ash_wraith", "elite_burrow_horror", "boss_frost_leviathan" }, 9), ctx));
            yield return Wait(2.6f);
            yield return Shot("b3_battle_boss_frost");
            _app.Router.Pop();
            _app.Router.Push(new BattlePlaybackScreen(Battle(c, new[] { "alien_solarch" }, skills, new[] { "boss_gloop_matriarch" }, 13), ctx));
            yield return Wait(2.6f);
            yield return Shot("b3_battle_boss_gloop");
            _app.Router.Pop();
            BattlePlaybackScreen.Instant = true;
            _svc.ExecuteNow(new RunAbandon());
            BattlePlaybackScreen.Instant = false;

            // 3. the summon screens: the event hub (exchange / packs / pass / tasks column, star sigils), the exchange, the shop, signals
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.4f);
            var hub = new SummonEventScreen(SummonEventType.Hero);
            _app.Router.Push(hub);
            yield return Wait(0.6f);
            Ui.Clear(_app.ModalLayer);
            yield return Shot("b3_summon_hub");
            hub.PacksSheet();
            yield return Wait(0.5f);
            yield return Shot("b3_summon_packs");
            Ui.Clear(_app.ModalLayer);
            hub.TasksSheet();
            yield return Wait(0.5f);
            yield return Shot("b3_summon_tasks");
            Ui.Clear(_app.ModalLayer);
            _app.Router.Push(new SummonExchangeScreen(SummonEventType.Hero));
            yield return Wait(0.5f);
            yield return Shot("b3_summon_exchange");
            _app.Router.Pop();
            _app.Router.Push(new SummonPassScreen(SummonEventType.Hero));
            yield return Wait(0.5f);
            yield return Shot("b3_summon_pass");
            _app.Router.Pop();
            _app.Router.Reset(new HubScreen());
            _app.Open("signals");
            yield return Wait(0.8f);
            Ui.Clear(_app.ModalLayer);
            yield return Shot("b3_signals");
            _app.Router.Reset(new HubScreen());
            _app.Open("shop");
            yield return Wait(0.8f);
            Ui.Clear(_app.ModalLayer);
            yield return Shot("b3_shop");
            _app.Router.Reset(new HubScreen());
            _app.Open("parts");
            yield return Wait(0.8f);
            Ui.Clear(_app.ModalLayer);
            yield return Shot("b3_parts_awaken_cores");
        }

        /// <summary>Draftable run skills that apply a status, one per status (so the battle shows many status chips).</summary>
        private static List<string> StatusSkills(ContentDb c, int n)
        {
            var list = new List<string>();
            var seen = new HashSet<StatusId>();
            var ids = new List<string>(c.Skills.Keys);
            ids.Sort(string.CompareOrdinal);
            foreach (var id in ids)
            {
                if (list.Count >= n) break;
                var s = c.Skills[id];
                if (s.Source != SkillSource.Run || !s.Draftable || s.RequiresFamilyCount > 0 || s.Requires.Count > 0 || s.Triggers == null) continue;
                foreach (var t in s.Triggers)
                {
                    if (t.Effect != EffectKind.ApplyStatus || t.Status == StatusId.None || seen.Contains(t.Status)) continue;
                    seen.Add(t.Status);
                    list.Add(id);
                    break;
                }
            }
            return list;
        }

        /// <summary>The worm on its mount with its skills and aliens against the given enemies.</summary>
        private static BattleResult Battle(ContentDb c, string[] aliens, List<string> skills, string[] enemies, int seed)
        {
            var lo = new CombatLoadout { DisplayName = "Wiggles", MountId = "skiff" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 9000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Def, 10)).Add(new StatMod(StatId.Speed, 100));
            foreach (var id in aliens)
            {
                var def = c.AlienType(id);
                Assert.That(def, Is.Not.Null, id);
                var a = new CombatAlien { AlienTypeId = id, Name = def.Name, Stars = 3 };
                a.Stats.Add(new StatMod(StatId.Speed, def.BaseSpeed));
                a.SkillIds.Add(AlienSystem.AbilityAt(c, id, 3));
                lo.Aliens.Add(a);
            }
            var setup = new BattleSetup { RoundCap = 12 };
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, skills));
            foreach (var e in enemies)
            {
                Assert.That(c.Enemies.ContainsKey(e), Is.True, e);
                setup.Enemy.Add(BattleSimulator.BuildEnemy(c, e, 2, 0, 600));
            }
            return BattleSimulator.Simulate(setup, c, new Rng((ulong)seed));
        }

        private IEnumerator Steps(int steps)
        {
            BattlePlaybackScreen.Instant = true;
            for (int i = 0; i < steps && _svc.State.Run.Active != null; i++)
            {
                _svc.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = false });
                for (int f = 0; f < 400; f++)
                {
                    if (_app.Router.Current is BattlePlaybackScreen b) { b.PlayAll(); b.Continue(); }
                    if (_app.Router.Current is RunScreen s && !s.Animating) break;
                    yield return null;
                }
                var run = _svc.State.Run.Active;
                if (run != null && run.Phase != RunPhase.AwaitRoll) { Ui.Clear(_app.ModalLayer); _svc.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = false }); }
                yield return Wait(0.15f);
            }
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
            _app.Feedback.Tick(60); // toasts of the setup commands are gone
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
            ArtCatalog.Loader = null;
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
