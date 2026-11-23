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
    /// Boots Main.unity, plays a fresh offline save (first run on auto to completion, spend rewards, city, pulls)
    /// and captures 1080×1920 screenshots of every screen into client/RogueStars/Screenshots/.
    /// Needs a graphics device: run in batchmode WITHOUT -nographics.
    /// </summary>
    public sealed class PlaythroughTests
    {
        private const int W = 1080, H = 1920;
        /// <summary>2026-03-02 12:00 UTC: the fixed game clock of the playthrough (same as the EditMode tests).</summary>
        private const long T0 = 1_772_452_800_000L;
        private const int Seed = 11;
        private ClientApp _app;
        private LocalGameService _svc;
        private ManualClock _clock;
        private string _dir;
        private string _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;
        private readonly List<string> _taken = new List<string>();

        [UnityTest, Timeout(1200000)]
        public IEnumerator FreshSave_Playthrough_WithScreenshots()
        {
            yield return Boot();

            // 1) hub of a brand-new worm
            yield return Shot("01_hub_new_player");
            Assert.That(_svc.State.Run.RunsStarted, Is.EqualTo(0));

            // 2) day 1 (onboarding v2): only PLAY — the first run is played by a worm without parts
            Assert.That(Tutorial.Next(_svc.State, _svc.Content, _svc.NowMs)?.ScreenId, Is.EqualTo("portals"));
            Assert.That(FirstNamed("Tab:worm"), Is.Null, "no Worm tab before level 1");
            Assert.That(_svc.State.Parts.Equipped, Is.Empty);

            // 3) portals → first level
            _app.Open("portals");
            yield return Shot("02b_portals");
            var level = FirstLevel();
            ClickNamed("Level:" + level.Id);
            yield return Wait(0.3f);
            yield return Shot("02c_portal_level_card");
            ClickNamed("LevelDetails");
            yield return Wait(0.3f);
            yield return Shot("03_level_modal");
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            Ui.Clear(_app.ModalLayer);
            _app.Router.Push(new RunScreen());
            yield return Wait(0.5f);
            yield return Shot("04_run_board");

            // 3) one manual roll (dice + token animation), then auto mode to the end
            var run = (RunScreen)_app.Router.Current;
            _app.Session.Run(new RunRoll());
            yield return Wait(0.25f);
            yield return Shot("05_run_rolling");
            yield return SettleManualRoll(run);
            yield return PlayRunOnAuto();
            Assert.That(_svc.State.Run.Active, Is.Null, "run finished");
            Assert.That(_app.Router.Current, Is.InstanceOf<RunResultScreen>());
            yield return Shot("10_run_result");
            // a lost run is replayed (stamina allows several tries) until the first clear
            for (int attempt = 0; attempt < 3 && !_svc.State.Run.LastSummary.Won; attempt++)
            {
                var again = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
                Assert.That(again.Ok, Is.True, again.Error);
                _app.Router.Replace(new RunScreen());
                yield return PlayRunOnAuto();
                yield return Shot("10_run_result_retry" + attempt);
            }
            Assert.That(_svc.State.Run.LastSummary.Won, Is.True, "first level cleared from a fresh save");
            Assert.That(_svc.State.Onboarding.TutorialDone, Does.Contain("first_run"));
            Assert.That(_svc.State.Counter("run_rolls"), Is.GreaterThan(0));

            // 4) the unlock moment (Parts) → its one action opens the worm → equip the starter parts
            Ui.Clear(_app.ModalLayer);
            var hub = new HubScreen();
            _app.Router.Reset(hub);
            Assert.That(hub.ShowPendingUnlock(), Is.True, "Parts unlock moment after level 1");
            yield return Wait(0.5f);
            yield return Shot("11a_unlock_parts");
            ClickNamed("UnlockAction");
            yield return Wait(0.3f);
            Assert.That(_app.Router.Current, Is.InstanceOf<WormScreen>());
            ClickNamed("AutoEquip");
            yield return Wait(0.3f);
            yield return Shot("11b_worm_equipped");
            Assert.That(_svc.State.Parts.Equipped.Count, Is.GreaterThanOrEqualTo(3));
            _app.Router.Pop();
            yield return Wait(0.3f);
            Assert.That(_app.Router.Current, Is.InstanceOf<HubScreen>());
            Assert.That(FirstNamed("Tab:worm"), Is.Not.Null, "Worm tab after level 1");
            yield return Shot("11_hub_after_first_run");

            // spend rewards: equip parts, level the worm, build the farm
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
                foreach (var uid in PartsSystem.SortedPartUids(_svc.State))
                {
                    if (PartsSystem.IsEquipped(_svc.State, uid)) continue;
                    if (_svc.ExecuteNow(new PartEquip { Uid = uid, Slot = slot }).Ok) break;
                }
            _svc.ExecuteNow(new WormLevelUp { Levels = 1 });
            _app.Open("worm");
            yield return Shot("12_worm");
            _app.Router.Pop();
            _app.Open("parts");
            yield return Shot("13_parts");
            var first = FirstNamed("Part:");
            if (first != null) { first.onClick.Invoke(); yield return Wait(0.3f); yield return Shot("14_part_detail"); Ui.Clear(_app.ModalLayer); }
            _app.Router.Pop();

            PlaceFirstBuildings(3);
            _clock.Advance(2L * 3600 * 1000);
            _svc.Refresh(_ => { });
            _app.Open("city");
            var building = FirstNamed("Building:");
            if (building != null) building.onClick.Invoke();
            yield return Wait(0.3f);
            yield return Shot("15_city");
            _app.Router.Pop();

            // 5) unlock the rest (dev) to show every screen populated; pull signals with the reveal
            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            _svc.DevGrantStarterKit();
            yield return Wait(0.3f);
            Assert.That(_app.Router.Current, Is.InstanceOf<HubScreen>());
            yield return Shot("15b_hub_all_unlocked");
            _app.Open("signals");
            yield return Shot("16_signals");
            var pull = _svc.ExecuteNow(new GachaPull { PullType = GachaType.Part, Count = 10, Payment = GachaPayment.Tokens });
            Assert.That(pull.Ok, Is.True, pull.Error);
            ((SignalsScreen)_app.Router.Current).Reveal((GachaPullResult)pull.Payload);
            yield return Wait(2f);
            yield return Shot("17_signal_reveal");
            Ui.Clear(_app.ModalLayer);
            _app.Router.Pop();
            StaminaModal.Show(_app.Router.Current, _app);
            yield return Wait(0.3f);
            yield return Shot("18_stamina_refill");
            Ui.Clear(_app.ModalLayer);

            // 5b) the redesigned parts ladder, laboratory tree, arena shop and Dark Side
            yield return RedesignScreens();

            // 6) every catalog screen
            int n = 30;
            foreach (var entry in ScreenCatalog.All)
            {
                _app.Router.Push(entry.Create());
                yield return Wait(0.4f);
                yield return Shot((n++) + "_" + entry.Id);
                Ui.Clear(_app.ModalLayer);
                _app.Router.Pop();
            }
            Assert.That(_taken.Count, Is.GreaterThan(25));
            Debug.Log("[Playthrough] screenshots: " + string.Join(", ", _taken));
        }

        /// <summary>Parts (detail with five skills, merge picker, awakening preview), laboratory tree + node, arena shop, Dark Side.</summary>
        private IEnumerator RedesignScreens()
        {
            var ids = new List<string>(_svc.Content.PartsConfig.Defs.Keys);
            ids.Sort(string.CompareOrdinal);
            var before = new HashSet<string>(_svc.State.Parts.Items.Keys);
            _svc.DevGrant(new[]
            {
                new Reward(RewardKinds.Part, ids[0], 2, (int)PartRarity.Legendary), new Reward(RewardKinds.Part, ids[1], 3, (int)PartRarity.Legendary),
                new Reward(RewardKinds.Part, ids[2], 1, (int)PartRarity.MythicS),
                Reward.Cur(Currencies.Ore, 20000), Reward.Cur(ProgressionCurrencies.PartAwakenCores, 50), Reward.Cur(Currencies.Data, 2000), Reward.Cur(Currencies.ArenaMedals, 450),
            });
            string legendary = null, mythic = null;
            foreach (var uid in PartsSystem.SortedPartUids(_svc.State))
            {
                if (before.Contains(uid)) continue;
                var p = _svc.State.Parts.Items[uid];
                if (legendary == null && p.DefId == ids[0]) legendary = uid;
                if (mythic == null && p.DefId == ids[2]) mythic = uid;
            }
            Assert.That(legendary, Is.Not.Null);
            Assert.That(mythic, Is.Not.Null);
            _app.Open("parts");
            yield return Wait(0.3f);
            yield return Shot("19_parts_ladder");
            var parts = (PartsScreen)_app.Router.Current;
            parts.Detail(mythic);
            yield return Wait(0.3f);
            yield return Shot("20_part_detail_skills");
            parts.OpenMergePicker(legendary, false);
            yield return Wait(0.3f);
            yield return Shot("21_merge_picker");
            Assert.That(parts.PickerPlan.Valid, Is.True, parts.PickerPlan.Error);
            parts.AwakenModal(mythic);
            yield return Wait(0.3f);
            yield return Shot("22_awaken_preview");
            Ui.Clear(_app.ModalLayer);
            _app.Router.Pop();

            var research = _svc.ExecuteNow(new LabResearch { ResearchId = "lab_atk_1" });
            Assert.That(research.Ok, Is.True, research.Error);
            _app.Open("laboratory");
            yield return Wait(0.3f);
            yield return Shot("23_laboratory_tree");
            ((LaboratoryScreen)_app.Router.Current).NodeModal("lab_boss_damage");
            yield return Wait(0.3f);
            yield return Shot("24_lab_node");
            Ui.Clear(_app.ModalLayer);
            _app.Router.Pop();

            _app.Open("arena");
            ((ArenaScreen)_app.Router.Current).ShowShop();
            yield return Wait(0.3f);
            yield return Shot("25_arena_shop");
            _app.Router.Pop();

            _app.Open("portals");
            ClickNamed("Button:Dark Side");
            yield return Wait(0.3f);
            yield return Shot("26_dark_side");
            _app.Router.Pop();
        }

        /// <summary>
        /// The manual roll's animation ends either on the board or in a battle playback. A playback opened outside auto
        /// mode waits for its Continue button (it never closes itself), so it is played to the end and continued here.
        /// </summary>
        private IEnumerator SettleManualRoll(RunScreen run)
        {
            var guard = new ProgressGuard(this, "manual roll");
            while (run.Animating || _app.Router.Current is BattlePlaybackScreen)
            {
                if (_app.Router.Current is BattlePlaybackScreen b && !b.AutoClose)
                {
                    b.PlayAll();
                    b.Continue();
                }
                guard.Tick();
                yield return null;
            }
        }

        /// <summary>
        /// Auto mode until the result screen. Progress is measured in game decisions (state versions), not wall-clock
        /// time: the run must end within <see cref="MaxRunDecisions"/> commands, and the loop fails with the last
        /// decision and the run/UI state if nothing happens for a long stretch (stall).
        /// </summary>
        private IEnumerator PlayRunOnAuto()
        {
            var run = _app.Router.Current as RunScreen;
            if (run == null && _svc.State.Run.Active == null) yield break;
            run?.StartAuto();
            bool battleShot = false, draftShot = false, eventShot = false, otherShot = false;
            long startVersion = _svc.State.Version;
            var guard = new ProgressGuard(this, "auto run");
            while (true)
            {
                var cur = _app.Router.Current;
                if (cur is RunResultScreen) yield break;
                if (_svc.State.Version - startVersion > MaxRunDecisions)
                    Assert.Fail("run did not finish within " + MaxRunDecisions + " decisions: " + Diagnostics());
                if (cur is BattlePlaybackScreen b)
                {
                    if (!battleShot)
                    {
                        BattlePlaybackScreen.TimeScale = 1;
                        b.PlaySteps(4);
                        yield return Wait(0.5f);
                        yield return Shot("06_battle_playback");
                        battleShot = true;
                    }
                    BattlePlaybackScreen.TimeScale = 12;
                    // a playback that does not close itself (opened outside auto mode) waits for Continue
                    if (!b.AutoClose) { b.PlayAll(); b.Continue(); }
                }
                var active = _svc.State.Run.Active;
                // auto pauses at every real decision (shop, draft, event, chest, campfire, branch, revive): decide here
                if (active != null && cur is RunScreen paused && paused.AutoPaused && !paused.Animating && _app.ModalLayer.childCount > 0)
                {
                    if (active.Phase == RunPhase.Draft && !draftShot) { draftShot = true; yield return Shot("07_draft"); }
                    else if (active.Phase == RunPhase.Event && !eventShot) { eventShot = true; yield return Shot("08_event"); }
                    else if ((active.Phase == RunPhase.Shop || active.Phase == RunPhase.ChooseBranch || active.Phase == RunPhase.Defeated) && !otherShot) { otherShot = true; yield return Shot("09_" + active.Phase.ToString().ToLowerInvariant()); }
                    Decide(active);
                    Assert.That(paused.Auto, Is.True, "auto stays on across a decision");
                }
                if (cur is RunScreen rs && !rs.Auto && _svc.State.Run.Active != null) rs.StartAuto();
                guard.Tick();
                yield return null;
            }
        }

        /// <summary>
        /// The player's decision at an auto pause, made through the decision modal's buttons: shop → buy the first
        /// affordable skill (once per shop visit), then leave; draft → first card; event / chest / campfire → the default
        /// option (first affordable otherwise); branch → path A; defeat → give up (the loop retries the level).
        /// </summary>
        private void Decide(ActiveRun a)
        {
            Button pick = null;
            switch (a.Phase)
            {
                case RunPhase.Shop:
                {
                    int skill = a.Shop.FindIndex(o => o.Kind == ShopOfferKind.Skill && !o.Sold && o.Price <= a.Coins);
                    bool bought = a.Shop.Exists(o => o.Sold);
                    if (skill >= 0 && !bought)
                    {
                        string name = _svc.Content.Skill(a.Shop[skill].SkillId)?.Name;
                        pick = ModalButton(b => b.name == "Button:" + a.Shop[skill].Price + " coins" && b.interactable && RowHasText(b, name));
                    }
                    if (pick == null) pick = ModalButton(b => b.name == "Button:Leave");
                    break;
                }
                case RunPhase.Draft: pick = ModalButton(b => b.name == "Button:Pick"); break;
                case RunPhase.ChooseBranch: pick = ModalButton(b => b.name.StartsWith("Button:Path A")); break;
                case RunPhase.Defeated: pick = ModalButton(b => b.name == "Button:Give up"); break;
                case RunPhase.BurrowChoice: pick = ModalButton(b => b.name == "Button:Cash out"); break;
                case RunPhase.Event:
                {
                    var ev = _svc.Content.RunEvents[a.EventId];
                    int opt = ev.DefaultOption;
                    bool Affordable(EventOption o) => a.Coins >= o.CoinCost && a.Shards >= o.ShardCost;
                    if (!Affordable(ev.Options[opt])) opt = ev.Options.FindIndex(Affordable);
                    string text = ev.Options[opt].Text;
                    pick = ModalButton(b => b.name.StartsWith("Button:" + text));
                    break;
                }
            }
            if (pick != null) pick.onClick.Invoke(); // not found yet (modal still opening): try again next frame
        }

        private Button ModalButton(Predicate<Button> match)
        {
            foreach (var b in _app.ModalLayer.GetComponentsInChildren<Button>(true)) if (match(b)) return b;
            return null;
        }

        private static bool RowHasText(Button b, string text)
        {
            if (text == null) return false;
            foreach (var t in b.transform.parent.GetComponentsInChildren<Text>(true)) if (t.text == text) return true;
            return false;
        }

        /// <summary>Upper bound of commands for one run of the first level on auto (a run takes well under 100).</summary>
        private const int MaxRunDecisions = 400;

        /// <summary>
        /// Fails when neither the game state nor the current screen changed for both <see cref="StallFrames"/> frames
        /// and <see cref="StallSeconds"/> (unscaled), i.e. nothing can make progress any more; the message carries the
        /// last decision and the run/UI state. Both bounds are far above any animation or auto-mode pause.
        /// </summary>
        private sealed class ProgressGuard
        {
            private const int StallFrames = 900;
            private const float StallSeconds = 30f;
            private readonly PlaythroughTests _t;
            private readonly string _what;
            private string _mark;
            private int _frames;
            private float _since;

            public ProgressGuard(PlaythroughTests t, string what) { _t = t; _what = what; _mark = t.ProgressMark(); _since = Time.realtimeSinceStartup; }

            public void Tick()
            {
                string mark = _t.ProgressMark();
                if (mark != _mark) { _mark = mark; _frames = 0; _since = Time.realtimeSinceStartup; return; }
                if (++_frames >= StallFrames && Time.realtimeSinceStartup - _since >= StallSeconds)
                    Assert.Fail(_what + " stalled (no state or screen change for " + _frames + " frames): " + _t.Diagnostics());
            }
        }

        private string ProgressMark()
        {
            var cur = _app.Router.Current;
            string screen = cur == null ? "-" : cur.GetType().Name + (cur is BattlePlaybackScreen b ? (b.Finished ? ":end" : ":play") : "");
            return _svc.State.Version + "|" + screen + "|" + _app.ModalLayer.childCount;
        }

        private string Diagnostics()
        {
            var a = _svc.State.Run.Active;
            var cur = _app.Router.Current;
            var last = _app.Session.LastResult;
            string run = a == null ? "no active run" :
                "phase=" + a.Phase + " rolls=" + a.Rolls + "/" + a.MaxRolls + " pos=" + a.Position + " hp=" + a.WormHp + "/" + a.WormMaxHp +
                " battles=" + a.BattleCount + " event=" + a.EventId + " draft=[" + string.Join(",", a.DraftOffer) + "] coins=" + a.Coins + " awakening=" + a.Awakening;
            string screen = cur == null ? "none" : cur.GetType().Name;
            if (cur is RunScreen rs) screen += " auto=" + rs.Auto + " paused=" + rs.AutoPaused + " animating=" + rs.Animating;
            if (cur is BattlePlaybackScreen bp) screen += " finished=" + bp.Finished + " autoClose=" + bp.AutoClose;
            return "version=" + _svc.State.Version + " lastResult=" + (last == null ? "none" : last.Ok ? "ok" : "fail:" + last.Error) +
                   " inFlight=" + _app.Session.InFlight + " modals=" + _app.ModalLayer.childCount + " screen=" + screen + " run: " + run;
        }

        // ---- setup / helpers ---------------------------------------------------------------------

        private IEnumerator Boot()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            Assert.That(_app, Is.Not.Null, "AppBootstrap built the app");
            // Deterministic fresh save: own empty save directory, fixed game clock, fixed service seed (which fixes the
            // new player's rules RNG), so every run of this test plays the same dice, boards and battles.
            _dir = Path.Combine(Application.temporaryCachePath, "playthrough_" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);
            _clock = new ManualClock(T0);
            _svc = new LocalGameService(_dir, "local_playthrough", "Wiggles", _clock, Seed);
            _app.Start(_svc);
            _shots = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.5f);
        }

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("ScreenshotCamera");
            _cam = go.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.orthographic = true;
            _cam.cullingMask = ~0;
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
            // (WaitForEndOfFrame never fires in batchmode; the capture camera is rendered by hand.)
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
            _taken.Add(name);
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        private LevelDef FirstLevel()
        {
            return _svc.Content.MainLevel(1);
        }

        private Button FirstNamed(string prefix)
        {
            var root = _app.Router.Current.Root;
            foreach (var b in root.GetComponentsInChildren<Button>(true)) if (b.name.StartsWith(prefix)) return b;
            return null;
        }

        private void ClickNamed(string name)
        {
            var b = FirstNamed(name);
            Assert.That(b, Is.Not.Null, name);
            b.onClick.Invoke();
        }

        private void PlaceFirstBuildings(int count)
        {
            int placed = 0;
            foreach (var b in CitySystem.SortedBlueprints(_svc.State))
            {
                if (b.Placed) continue;
                var def = _svc.Content.Blueprint(b.Id);
                bool done = false;
                for (int y = 0; y < _svc.Content.CityTuning.GridHeight && !done; y++)
                    for (int x = 0; x < _svc.Content.CityTuning.GridWidth && !done; x++)
                        if (CitySystem.PlacementError(_svc.State, _svc.Content, def, x, y) == null)
                            done = _svc.ExecuteNow(new CityBuild { BlueprintId = b.Id, X = x, Y = y }).Ok;
                if (done && ++placed >= count) break;
            }
        }

        [TearDown]
        public void TearDown()
        {
            BattlePlaybackScreen.TimeScale = 1;
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
