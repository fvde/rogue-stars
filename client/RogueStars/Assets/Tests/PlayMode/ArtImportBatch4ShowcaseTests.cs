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
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// The art import of 2026-11-27 (batch 4): the five cleaned bosses in battle, and the 25 City buildings — the camp (the
    /// Launch Pod), the full City with every blueprint placed by day, at night (lights on) and close up, a construction site,
    /// the building card, the build rail, a blueprint pull reveal and the Signals banner. Explicit
    /// (<c>-testFilter ArtImportBatch4ShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR. Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the batch-4 art-import screenshots; run with -testFilter ArtImportBatch4ShowcaseTests")]
    public sealed class ArtImportBatch4ShowcaseTests
    {
        private const int W = 1080, H = 2400;
        private const long T0 = 1_772_452_800_000L;
        private static readonly string[] Bosses = { "boss_magma_tyrant", "boss_void_maw", "boss_prism_tyrant", "boss_rust_queen", "boss_storm_hive" };
        private ClientApp _app;
        private LocalGameService _svc;
        private ManualClock _clock;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;

        private GameState S => _svc.State;
        private ContentDb C => _svc.Content;

        [UnityTest, Timeout(900000)]
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "artimport4_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _clock = new ManualClock(T0);
            _svc = new LocalGameService(_dir, "local_artimport4", "Wiggles", _clock, 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "art-import"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            // 1. camp mode (level 14): the plaza with the Launch Pod
            Reach(14);
            CelebrateAll();
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.5f);
            yield return Shot("b4_hub_crash_site_pod");
            _app.Open("city");
            var camp = (CityScreen)_app.Router.Current;
            Assert.That(camp.CampMode, Is.True);
            var t = C.CityTuning;
            camp.Focus(t.FireX + 0.5f, t.FireY + 0.5f, CityWorld.OverviewWidth);
            yield return Wait(2f);
            yield return Shot("b4_city_camp_mode");
            camp.Focus(t.FireX + 0.5f, t.FireY + 0.5f, CityWorld.MinWidth);
            yield return Wait(0.5f);
            yield return Shot("b4_city_camp_close");
            _app.Router.Pop();

            // 2. the bosses in battle (the battle stays at the start: every fighter stands in place)
            _svc.DevGrantStarterKit();
            Reach(12);
            foreach (var id in Bosses)
            {
                _app.Router.Push(new BattlePlaybackScreen(Battle(new[] { "alien_emberkit", "alien_zib" }, new[] { id }, 5)));
                yield return Wait(1.2f);
                yield return Shot("b4_battle_" + id);
                _app.Router.Pop();
            }

            // 3. the full City: every blueprint placed
            var plan = Populate();
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.2f);
            for (int k = 0; k < 6; k++)
            {
                yield return Wait(0.3f);
                while (_app.Router.Current is UnlockScreen) _app.Router.Pop();
            }
            _app.Open("city");
            var city = (CityScreen)_app.Router.Current;
            city.World.Sim.Roll = () => 1; // no social bubbles over the buildings
            city.Focus(9.5f, 9.5f, CityWorld.OverviewWidth);
            yield return Wait(3f);
            yield return Shot("b4_city_full_overview_day");
            city.Focus(9.5f, 9.5f, CityWorld.MaxWidth);
            yield return Wait(0.6f);
            yield return Shot("b4_city_full_zoomed_out");
            foreach (var spot in plan.CloseUps)
            {
                city.Focus(spot.x, spot.y, CityWorld.EditWidth);
                yield return Wait(0.5f);
                yield return Shot("b4_city_close_" + spot.z);
            }
            // night: lit windows (the glows over working buildings), lamps
            _clock.NowMs = CityDayNight.MiddleOf("night", _clock.NowMs);
            city.Rebuild();
            city.Focus(9.5f, 9.5f, CityWorld.OverviewWidth);
            yield return Wait(2f);
            yield return Shot("b4_city_full_night");
            _clock.NowMs = CityDayNight.MiddleOf("day", _clock.NowMs + CityDayNight.CycleMs);
            city.Rebuild();

            // a construction site (scaffold over the half-built building)
            if (plan.SiteUid != null)
            {
                city.Focus(plan.SiteX, plan.SiteY, CityWorld.EditWidth);
                yield return Wait(0.5f);
                yield return Shot("b4_city_construction_site");
            }

            // the building card (info sheet)
            foreach (var bp in new[] { "bp_command_spire", "bp_gene_lab" })
            {
                var b = CitySystem.Copies(S, bp).FirstOrDefault(x => x.Placed);
                if (b == null) continue;
                city.Focus(b.X, b.Y, CityWorld.OverviewWidth);
                city.Select(b.Uid);
                yield return Wait(0.6f);
                yield return Shot("b4_city_card_" + bp);
                city.Select(b.Uid);
            }

            // build mode: the rail with the building cards and a ghost
            city.EnterEdit();
            city.StartPlace("bp_ore_mine", null);
            if (city.Ghost != null) city.Focus(city.Ghost.X, city.Ghost.Y, CityWorld.EditWidth);
            city.Rebuild();
            yield return Wait(0.8f);
            yield return Shot("b4_city_build_rail_ghost");
            city.CancelGhost();
            city.ExitEdit();

            // the buildings the land had no room for: store the big ones, place the rest, a second overview
            if (plan.Missing.Count > 0)
            {
                foreach (var b in S.City.Buildings.Where(b => b.Placed && b.BlueprintId != ContentDb.LaunchPodId).OrderByDescending(b => C.Blueprint(b.BlueprintId).Width * C.Blueprint(b.BlueprintId).Height).Take(8).ToList())
                    _svc.ExecuteNow(new CityStore { Uid = b.Uid });
                var still = new List<string>();
                foreach (var bp in plan.Missing)
                {
                    var spot = CityPlanner.Find(S, C, bp, null, 3, null);
                    var r = spot == null ? null : _svc.ExecuteNow(new CityPlace { BlueprintId = bp, X = spot.X, Y = spot.Y, Rotated = spot.Rotated });
                    if (r == null || !r.Ok) still.Add(bp);
                }
                Debug.Log("[Batch4Showcase] second set placed; still not placed: " + string.Join(", ", still));
                _clock.Advance(14 * 3_600_000L);
                _svc.Refresh(_ => { });
                city.Rebuild();
                city.Focus(9.5f, 9.5f, CityWorld.OverviewWidth);
                yield return Wait(1.5f);
                yield return Shot("b4_city_second_set_overview");
                foreach (var bp in plan.Missing)
                {
                    var b = CitySystem.Copies(S, bp).FirstOrDefault(x => x.Placed);
                    if (b == null) continue;
                    city.Focus(b.X, b.Y, CityWorld.EditWidth);
                    yield return Wait(0.4f);
                    yield return Shot("b4_city_close_" + bp);
                }
            }

            // 4. a blueprint pull reveal (Legendary / Mythic buildings earn the ceremony), the Signals banner
            var rewards = new List<Reward>
            {
                new Reward(RewardKinds.Blueprint, ContentDb.LaboratoryId, 1), new Reward(RewardKinds.Blueprint, "bp_token_relay", 1),
                new Reward(RewardKinds.Blueprint, "bp_gene_lab", 1), new Reward(RewardKinds.Blueprint, "bp_fuel_refinery", 1),
                new Reward(RewardKinds.Blueprint, "bp_ore_mine", 1),
            };
            var reveal = PullReveal.ShowIfEarned(_app.ModalLayer, rewards, C, new RevealOptions { Title = "Blueprints" });
            Assert.That(reveal, Is.Not.Null);
            yield return Wait(0.5f);
            yield return Shot("b4_blueprint_reveal_start");
            reveal.Root.GetComponent<PullRevealPlayer>().Skip();
            yield return Wait(1.2f);
            yield return Shot("b4_blueprint_reveal_all");
            Ui.Clear(_app.ModalLayer);
            _app.Router.Reset(new HubScreen());
            _app.Open("signals");
            yield return Wait(0.8f);
            Ui.Clear(_app.ModalLayer);
            yield return Shot("b4_signals");
        }

        // ---- the city ------------------------------------------------------------------------------------------------------

        private sealed class Plan
        {
            public string SiteUid;
            public float SiteX, SiteY;
            public List<string> Missing = new List<string>();
            /// <summary>(x, y, name) map points for close-ups.</summary>
            public List<(float x, float y, string z)> CloseUps = new List<(float, float, string)>();
        }

        /// <summary>A late City: up to 20 digs, the street spine, every blueprint placed once (largest first), aliens at work, one site.</summary>
        private Plan Populate()
        {
            Reach(30);
            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            var rewards = new List<Reward> { Reward.Cur(Currencies.Biomass, 90_000_000), Reward.Cur(Currencies.Ore, 90_000_000), Reward.Cur(Currencies.Diamonds, 90_000) };
            foreach (var b in C.Blueprints.Keys) rewards.Add(new Reward(RewardKinds.Blueprint, b, 1));
            foreach (var a in C.AlienTypes.Keys.OrderBy(k => k).Take(21)) rewards.Add(new Reward(RewardKinds.Alien, a, 1));
            _svc.DevGrant(rewards);
            for (int i = 0; i < 20; i++)
            {
                var p = C.CityPatches.Where(x => CitySystem.DigBlockReason(S, C, x.Id) == null)
                    .OrderBy(x => Mathf.Abs(Centre(x).x - 9.5f) + Mathf.Abs(Centre(x).y - 9.5f)).FirstOrDefault();
                if (p == null) break;
                _svc.ExecuteNow(new CityDig { PatchId = p.Id });
                _clock.Advance(10 * 24 * 3_600_000L); // later digs take days
                _svc.ExecuteNow(new CityCollect());
            }
            Debug.Log("[Batch4Showcase] dug " + S.City.DugPatches.Count + " patches");
            var spine = CityPlanner.SpineCells(C);
            _svc.ExecuteNow(new CityStreets { Adds = CityPlanner.PaveableSpine(S, C, spine) });
            var order = C.Blueprints.Values.Where(d => d.Id != ContentDb.LaunchPodId).OrderByDescending(d => d.Width * d.Height).ThenBy(d => d.Id, StringComparer.Ordinal).Select(d => d.Id).ToList();
            string site = order.Last(); // the smallest: placed last, after the clock moved on (still under construction)
            order.Remove(site);
            var missing = new List<string>();
            foreach (var bp in order)
            {
                // keep the street spine free first; when the land is full, let the building take a spine cell
                var spot = CityPlanner.Find(S, C, bp, null, 2, spine) ?? CityPlanner.Find(S, C, bp, null, 3, null);
                if (spot == null) { missing.Add(bp); continue; }
                if (spot.Streets.Count > 0) _svc.ExecuteNow(new CityStreets { Adds = spot.Streets });
                var r = _svc.ExecuteNow(new CityPlace { BlueprintId = bp, X = spot.X, Y = spot.Y, Rotated = spot.Rotated });
                if (!r.Ok) missing.Add(bp + " (" + r.Error + ")");
            }
            _clock.Advance(14 * 3_600_000L);
            _svc.Refresh(_ => { });
            var placed = S.City.Buildings.Where(b => b.Placed && b.BlueprintId != ContentDb.LaunchPodId).ToList();
            var aliens = S.Aliens.Owned.Keys.OrderBy(k => k).ToList();
            var pod = CitySystem.LaunchPod(S);
            int ai = 0;
            if (pod != null)
                for (; ai < 2 && ai < aliens.Count; ai++) _svc.ExecuteNow(new AlienAssign { AlienTypeId = aliens[ai], BuildingUid = pod.Uid });
            for (int bi = 0; bi < placed.Count && ai < aliens.Count; bi++, ai++)
                _svc.ExecuteNow(new AlienAssign { AlienTypeId = aliens[ai], BuildingUid = placed[bi].Uid });
            _clock.Advance(2 * 3_600_000L);
            _svc.Refresh(_ => { });

            var plan = new Plan();
            var siteSpot = CityPlanner.Find(S, C, site, null, 2, spine);
            if (siteSpot != null)
            {
                if (siteSpot.Streets.Count > 0) _svc.ExecuteNow(new CityStreets { Adds = siteSpot.Streets });
                var r = _svc.ExecuteNow(new CityPlace { BlueprintId = site, X = siteSpot.X, Y = siteSpot.Y, Rotated = siteSpot.Rotated });
                if (r.Ok) { plan.SiteUid = (string)r.Payload; plan.SiteX = siteSpot.X; plan.SiteY = siteSpot.Y; }
            }
            else missing.Add(site);
            _svc.Refresh(_ => { });
            plan.Missing.AddRange(missing.Select(m => m.Split(' ')[0]));
            Debug.Log("[Batch4Showcase] placed " + S.City.Buildings.Count(b => b.Placed) + " of " + C.Blueprints.Count + "; not placed: " + string.Join(", ", missing));
            foreach (var b in S.City.Buildings.Where(b => b.Placed).OrderBy(b => b.BlueprintId, StringComparer.Ordinal))
            {
                var d = C.Blueprint(b.BlueprintId);
                CityLayout.Footprint(d, b.Rotated, out int w, out int h);
                Debug.Log("[Batch4Showcase] " + b.BlueprintId + " at " + b.X + "," + b.Y + " " + w + "x" + h + (b.Rotated ? " rotated" : ""));
            }
            // close-ups: around the plaza and the four quarters of the land
            plan.CloseUps.Add((C.CityTuning.FireX + 0.5f, C.CityTuning.FireY + 0.5f, "plaza"));
            foreach (var q in new[] { (5.5f, 5.5f, "nw"), (13.5f, 5.5f, "ne"), (5.5f, 13.5f, "sw"), (13.5f, 13.5f, "se") }) plan.CloseUps.Add(q);
            return plan;
        }

        private static Vector2 Centre(CityPatchDef p)
        {
            float sx = 0, sy = 0;
            foreach (var c in p.Cells) { sx += c % 20; sy += c / 20; }
            return new Vector2(sx / p.Cells.Count, sy / p.Cells.Count);
        }

        private void Reach(int level)
        {
            S.Counters[Requirements.MainLevelCounter] = level;
            for (int i = 1; i <= level; i++) S.Run.Levels[C.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.Refresh(_ => { });
        }

        private void CelebrateAll()
        {
            foreach (var id in S.Onboarding.Unlocked.Keys.ToList()) S.Onboarding.Celebrated.Add(id);
            foreach (var f in C.FeatureDefs.Keys) S.Onboarding.Celebrated.Add(f);
        }

        // ---- battles -------------------------------------------------------------------------------------------------------

        /// <summary>The worm on its mount with its aliens against the boss.</summary>
        private BattleResult Battle(string[] aliens, string[] enemies, int seed)
        {
            var lo = new CombatLoadout { DisplayName = "Wiggles", MountId = "skiff" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 9000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Def, 10)).Add(new StatMod(StatId.Speed, 100));
            foreach (var id in aliens)
            {
                var def = C.AlienType(id);
                Assert.That(def, Is.Not.Null, id);
                var a = new CombatAlien { AlienTypeId = id, Name = def.Name, Stars = 3 };
                a.Stats.Add(new StatMod(StatId.Speed, def.BaseSpeed));
                a.SkillIds.Add(AlienSystem.AbilityAt(C, id, 3));
                lo.Aliens.Add(a);
            }
            var setup = new BattleSetup { RoundCap = 12 };
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, C, new List<string>()));
            foreach (var e in enemies)
            {
                Assert.That(C.Enemies.ContainsKey(e), Is.True, e);
                setup.Enemy.Add(BattleSimulator.BuildEnemy(C, e, 2, 0, 600));
            }
            return BattleSimulator.Simulate(setup, C, new Rng((ulong)seed));
        }

        // ---- capture (as CityShowcaseTests) ----------------------------------------------------------------------------------

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("Batch4ShotCamera");
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
            _cam.cullingMask = ~(1 << CityWorld.Layer | 1 << WorldBoard.Layer);
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
