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
using UnityEngine.UI;

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// The City screen (City design, version A + C's build rail) in play mode at 1080×2400: a mid-game city by day and at night,
    /// build mode with a valid ghost (bonus chip) and an invalid one (reason), street painting, the dig pop-up and dig day, the
    /// building card, a level-up party, the pop reveal (×3 banner), the Workers strip, the Output sheet and the squad wandering
    /// around the worm on the hub (C4). Explicit (<c>-testFilter CityShowcaseTests</c>, needs graphics: no -nographics); writes to
    /// $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/city.
    /// </summary>
    [Explicit("renders City screenshots; run with -testFilter CityShowcaseTests")]
    public sealed class CityShowcaseTests
    {
        private const int W = 1080, H = 2400;
        private const long T0 = 1_772_452_800_000L;
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
        public IEnumerator City()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "city_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _clock = new ManualClock(T0);
            _svc = new LocalGameService(_dir, "local_city", "Wiggles", _clock, 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "city"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.3f);

            var plan = Populate();
            _app.Router.Reset(new HubScreen());
            yield return Wait(0.2f);
            _app.Open("city");
            var city = (CityScreen)_app.Router.Current;
            city.World.Sim.Roll = () => 0.05; // social events whenever two walkers meet (the overview shows one)
            var t = C.CityTuning;
            city.Focus(t.FireX + 0.5f, t.FireY + 0.5f, CityWorld.OverviewWidth);
            yield return Wait(5f);
            yield return Shot("city_01_overview_day");
            Debug.Log("[CityShowcase] dug " + S.City.DugPatches.Count + ", buildings " + S.City.Buildings.Count + ", Zz " + city.Root.GetComponentsInChildren<Text>(true).Count(x => x.name.StartsWith("Zz:")));
            Debug.Log("[CityShowcase] sprites " + city.World.ActiveSprites + ", walkers " + city.World.WalkersVisible + ", social " + city.World.SocialShown);
            Assert.That(city.World.ActiveSprites, Is.LessThanOrEqualTo(CityWorld.SpriteBudget));

            // night: lamps, lit windows, the fire brighter, idle aliens by the fire
            _clock.NowMs = CityDayNight.MiddleOf("night", _clock.NowMs);
            city.Rebuild();
            yield return Wait(2.5f);
            Assert.That(city.World.Darkness, Is.EqualTo(1));
            yield return Shot("city_02_night");
            _clock.NowMs = CityDayNight.MiddleOf("day", _clock.NowMs + CityDayNight.CycleMs);
            city.Rebuild();

            // the building card in place
            city.Focus(plan.CardX, plan.CardY, CityWorld.OverviewWidth);
            city.Select(plan.CardUid);
            yield return Wait(0.6f);
            Assert.That(Find<RectTransform>(city.Root, "BuildingCard"), Is.Not.Null);
            yield return Shot("city_03_building_card");
            city.Select(plan.CardUid);

            // build mode: a valid ghost with its bonus chip, then an invalid one with the reason
            city.EnterEdit();
            city.StartPlace("bp_grain_farm", null);
            Assert.That(city.Ghost.Ok, Is.True, city.GhostChip);
            city.Focus(city.Ghost.X, city.Ghost.Y, CityWorld.EditWidth);
            city.Rebuild();
            yield return Wait(0.8f);
            yield return Shot("city_04_build_valid_ghost");
            var bad = FindRubbleNear(city.Ghost.X, city.Ghost.Y);
            city.GhostAt(bad.x, bad.y);
            city.DropGhost();
            Assert.That(city.Ghost.Ok, Is.False);
            yield return Wait(0.6f);
            yield return Shot("city_05_build_invalid_ghost");
            city.CancelGhost();

            // street painting: a stroke out of the network, mid-drag
            city.SetTool(CityTool.Street);
            var stroke = plan.Stroke;
            city.Focus(stroke[0] % 20, stroke[0] / 20, CityWorld.EditWidth);
            city.Rebuild();
            city.StrokeBegin(plan.StrokeAnchor);
            foreach (var cell in stroke) city.StrokeTo(cell);
            yield return Wait(0.6f);
            yield return Shot("city_06_street_painting");
            city.StrokeEnd();
            yield return Wait(0.4f);
            yield return Shot("city_07_street_done_undo");
            city.ExitEdit();

            // the tablet layout: the rail stays on
            city.ForceTablet = true;
            city.Rebuild();
            yield return Wait(0.5f);
            yield return Shot("city_08_rail_always_on");
            city.ForceTablet = null;
            city.Rebuild();

            // dig pop-up, dig day (the drill bot), new land (rubble crumbles + banner)
            var patch = C.CityPatches.First(p => CitySystem.DigBlockReason(S, C, p.Id) == null);
            var pc = city.World.ToGrid(city.World.PatchCentre(patch));
            city.Focus(pc.x, pc.y, CityWorld.OverviewWidth);
            city.OpenDig(patch.Id);
            yield return Wait(0.6f);
            yield return Shot("city_09_dig_popup");
            Find<Button>(city.Root, "Dig").onClick.Invoke();
            yield return Wait(3.2f);
            yield return Shot("city_10_dig_day_drill");
            var timer = S.City.DigTimerId;
            if (timer != null) _app.Session.Run(new TimerSpeedUp { TimerId = timer, Method = SpeedUpMethod.Diamonds }); // through the session: its events reach the screen
            yield return Wait(0.45f);
            yield return Shot("city_11_new_land");
            yield return Wait(2.5f);

            // level-up party (T07)
            var up = plan.UpgradeUid;
            city.Focus(plan.UpgradeX, plan.UpgradeY, CityWorld.OverviewWidth);
            _app.Session.Run(new CityUpgrade { Uid = up });
            _clock.Advance(3 * 3_600_000L);
            _app.Session.Run(new CityCollect());
            yield return Wait(0.35f);
            yield return Shot("city_12_level_up_party");
            yield return Wait(1.5f);

            // the Workers strip and the Output sheet
            Find<Button>(city.Root, "Dock:Workers").onClick.Invoke();
            yield return Wait(0.5f);
            yield return Shot("city_13_workers_strip");
            Find<Button>(city.Root, "Dock:Workers").onClick.Invoke();
            Find<Button>(city.Root, "Dock:Output").onClick.Invoke();
            yield return Wait(0.5f);
            yield return Shot("city_14_output_sheet");
            Ui.Clear(_app.ModalLayer);

            // pull building: stored chances on the building, then the pop reveal (×3 banner)
            city.Focus(plan.RelayX, plan.RelayY, CityWorld.OverviewWidth);
            city.Rebuild();
            yield return Wait(0.5f);
            Assert.That(Find<Button>(city.Root, "Chance:" + plan.RelayUid), Is.Not.Null);
            yield return Shot("city_15_pull_building_chances");
            var results = new List<CityPopResult>
            {
                new CityPopResult { Uid = plan.RelayUid, Currency = Currencies.BlueprintTokens, Pulls = 1 },
                new CityPopResult { Uid = plan.RelayUid, Currency = Currencies.BlueprintTokens, Pulls = 2 },
                new CityPopResult { Uid = plan.RelayUid, Currency = Currencies.BlueprintTokens, Pulls = 3 },
            };
            var reveal = CityScreen.ShowPopReveal(_app.ModalLayer, results, C);
            yield return Wait(0.5f);
            yield return Shot("city_16_pop_reveal_charge");
            reveal.Root.GetComponent<PullRevealPlayer>().Skip();
            yield return Wait(1.2f);
            yield return Shot("city_17_pop_reveal_triple");
            Ui.Clear(_app.ModalLayer);

            // C4: the squad wanders around the worm on the hub
            foreach (var id in S.Onboarding.Unlocked.Keys.ToList()) S.Onboarding.Celebrated.Add(id); // no unlock moments over the shot
            _app.Router.Reset(new HubScreen());
            for (int k = 0; k < 6; k++) // unlock moments (owned blueprints unlock their features): back out = snoozed for the session
            {
                yield return Wait(0.4f);
                while (_app.Router.Current is UnlockScreen) _app.Router.Pop();
            }
            var hub = (HubScreen)_app.Router.Current;
            Assert.That(hub.Squad, Is.Not.Null);
            hub.Squad.Sim.Roll = () => 0.05;
            float until = Time.realtimeSinceStartup + 20;
            while (hub.Squad != null && hub.Squad.BubblesShown == 0 && Time.realtimeSinceStartup < until) yield return null;
            yield return Wait(0.35f);
            yield return Shot("city_18_hub_squad_wandering");
        }

        // ---- the city --------------------------------------------------------------------------------------------------------

        private sealed class Plan
        {
            public string CardUid, RelayUid, UpgradeUid;
            public float CardX, CardY, RelayX, RelayY, UpgradeX, UpgradeY;
            public int StrokeAnchor;
            public List<int> Stroke = new List<int>();
        }

        /// <summary>A mid-game city: 7 digs, a street spine, ~20 buildings across rarities, aliens at work, goods and chances ready.</summary>
        private Plan Populate()
        {
            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            var rewards = new List<Reward> { Reward.Cur(Currencies.Biomass, 9_000_000), Reward.Cur(Currencies.Ore, 9_000_000), Reward.Cur(Currencies.GearCores, 200_000), Reward.Cur(Currencies.Diamonds, 90_000) };
            var bps = new[]
            {
                "bp_biomass_farm", "bp_grain_farm", "bp_ore_mine", "bp_fuel_refinery", "bp_gear_works", "bp_diamond_mine", "bp_token_relay",
                "bp_signal_beacon", ContentDb.LaunchPodId, "bp_breeding_pen", "bp_observatory", "bp_stamina_well", "bp_supply_depot",
                "bp_regen_spire", "bp_command_spire", "bp_barracks_dome",
            };
            foreach (var b in bps) rewards.Add(new Reward(RewardKinds.Blueprint, b, 1));
            foreach (var a in C.AlienTypes.Keys.OrderBy(k => k).Take(14)) rewards.Add(new Reward(RewardKinds.Alien, a, 1));
            _svc.DevGrant(rewards);
            for (int i = 0; i < 7; i++)
            {
                var p = C.CityPatches.Where(x => CitySystem.DigBlockReason(S, C, x.Id) == null)
                    .OrderBy(x => Mathf.Abs(CityWorldCentre(x).x - 9) + Mathf.Abs(CityWorldCentre(x).y - 9)).FirstOrDefault();
                if (p == null) break;
                _svc.ExecuteNow(new CityDig { PatchId = p.Id });
                _clock.Advance(26 * 3_600_000L);
                _svc.ExecuteNow(new CityCollect()); // any command settles: the dig completes
            }
            var spine = CityPlanner.SpineCells(C);
            _svc.ExecuteNow(new CityStreets { Adds = CityPlanner.PaveableSpine(S, C, spine) });
            var order = new[]
            {
                "bp_biomass_farm", "bp_biomass_farm", "bp_biomass_farm", "bp_ore_mine", "bp_ore_mine", "bp_grain_farm", "bp_gear_works",
                ContentDb.LaunchPodId, "bp_supply_depot", "bp_stamina_well", "bp_fuel_refinery", "bp_diamond_mine", "bp_token_relay",
                "bp_breeding_pen", "bp_observatory", "bp_signal_beacon", "bp_regen_spire", "bp_command_spire", "bp_barracks_dome",
            };
            foreach (var bp in order)
            {
                var plan = CityPlanner.Find(S, C, bp, null, 2, spine);
                if (plan == null) continue;
                if (plan.Streets.Count > 0) _svc.ExecuteNow(new CityStreets { Adds = plan.Streets });
                _svc.ExecuteNow(new CityPlace { BlueprintId = bp, X = plan.X, Y = plan.Y, Rotated = plan.Rotated });
            }
            _clock.Advance(14 * 3_600_000L); // built (Mythics take 12 h)
            _svc.Refresh(_ => { });
            // aliens: the squad in the Launch Pod, the rest at work (two buildings stay dark: Zz)
            var placed = S.City.Buildings.Where(b => b.Placed && b.BlueprintId != ContentDb.LaunchPodId).ToList();
            var aliens = S.Aliens.Owned.Keys.OrderBy(k => k).ToList();
            var pod = CitySystem.LaunchPod(S);
            if (pod != null) CitySystem.Owned(S, ContentDb.LaunchPodId).Stars = 10; // three squad seats
            int ai = 0;
            if (pod != null)
                for (; ai < 3 && ai < aliens.Count; ai++) _svc.ExecuteNow(new AlienAssign { AlienTypeId = aliens[ai], BuildingUid = pod.Uid });
            for (int bi = 0; bi < placed.Count - 2 && ai < aliens.Count; bi++, ai++)
                _svc.ExecuteNow(new AlienAssign { AlienTypeId = aliens[ai], BuildingUid = placed[bi].Uid });
            // a few hours of production (bubbles), chances on the relay, one building still under construction
            _clock.Advance(9 * 3_600_000L);
            _svc.Refresh(_ => { });
            var relay = CitySystem.Copies(S, "bp_token_relay").FirstOrDefault(b => b.Placed);
            if (relay != null) relay.Chances = 3;
            var extra = CityPlanner.Find(S, C, "bp_ore_mine", null, 2, spine);
            if (extra != null)
            {
                if (extra.Streets.Count > 0) _svc.ExecuteNow(new CityStreets { Adds = extra.Streets });
                _svc.ExecuteNow(new CityPlace { BlueprintId = "bp_ore_mine", X = extra.X, Y = extra.Y, Rotated = extra.Rotated });
            }
            _svc.Refresh(_ => { });

            var plan2 = new Plan();
            var farm = CitySystem.Copies(S, "bp_biomass_farm").First(b => b.Placed && CitySystem.Workers(S, b.Uid).Count > 0);
            plan2.CardUid = farm.Uid; plan2.CardX = farm.X; plan2.CardY = farm.Y;
            var mine = CitySystem.Copies(S, "bp_ore_mine").First(b => b.Placed && b.Level >= 1);
            plan2.UpgradeUid = mine.Uid; plan2.UpgradeX = mine.X; plan2.UpgradeY = mine.Y;
            if (relay != null) { plan2.RelayUid = relay.Uid; plan2.RelayX = relay.X + 1; plan2.RelayY = relay.Y + 0.5f; }
            // a stroke: from a street out into open land (up to 4 tiles)
            var g = CityLayout.Grid(S, C);
            var linked = CityLayout.LinkedStreets(g);
            foreach (var st in S.City.Streets)
            {
                if (!linked[st]) continue;
                int x = st % 20, y = st / 20;
                foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
                {
                    var cells = new List<int>();
                    for (int k = 1; k <= 4; k++)
                    {
                        int nx = x + d.x * k, ny = y + d.y * k;
                        if (!g.IsOpen(nx, ny)) break;
                        cells.Add(g.Cell(nx, ny));
                    }
                    if (cells.Count >= 3) { plan2.StrokeAnchor = st; plan2.Stroke = cells; return plan2; }
                }
            }
            return plan2;
        }

        private Vector2 CityWorldCentre(CityPatchDef p)
        {
            float sx = 0, sy = 0;
            foreach (var c in p.Cells) { sx += c % 20; sy += c / 20; }
            return new Vector2(sx / p.Cells.Count, sy / p.Cells.Count);
        }

        private Vector2Int FindRubbleNear(int x0, int y0)
        {
            for (int r = 1; r < 10; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = x0 + dx, y = y0 + dy;
                        if (x < 0 || y < 0 || x >= 20 || y >= 20) continue;
                        if (CityLayout.IsLand(S, C, CityLayout.Cell(C, x, y))) continue;
                        if (C.CityPatches.Any(p => p.Cells.Contains(CityLayout.Cell(C, x, y)))) return new Vector2Int(x, y);
                    }
            return new Vector2Int(x0 + 3, y0);
        }

        private static T Find<T>(Transform root, string name) where T : Component => root.GetComponentsInChildren<T>(true).FirstOrDefault(c => c.name == name);

        // ---- capture ----------------------------------------------------------------------------------------------------------

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("CityShotCamera");
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
            _app.Feedback.Tick(0.016f);
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
