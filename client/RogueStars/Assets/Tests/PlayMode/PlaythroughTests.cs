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

            // 2) the hub's next goal asks to equip the starter parts first
            Assert.That(Tutorial.Next(_svc.State, _svc.Content)?.ScreenId, Is.EqualTo("worm"));
            _app.Open("worm");
            ClickNamed("AutoEquip");
            yield return Wait(0.3f);
            yield return Shot("02_worm_equipped");
            Assert.That(_svc.State.Parts.Equipped.Count, Is.GreaterThanOrEqualTo(3));
            _app.Router.Pop();

            // 3) portals → first level
            _app.Open("portals");
            yield return Shot("02b_portals");
            var level = FirstLevel();
            ClickNamed("Level:" + level.Id);
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
            yield return WaitUntil(() => !run.Animating && !(_app.Router.Current is BattlePlaybackScreen), 20f, () => { if (_app.Router.Current is BattlePlaybackScreen) BattlePlaybackScreen.TimeScale = 10; });
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

            // 4) spend rewards: equip parts, level the worm, build the farm
            _app.Router.Reset(new HubScreen());
            yield return Shot("11_hub_after_first_run");
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

        private IEnumerator PlayRunOnAuto()
        {
            var run = _app.Router.Current as RunScreen;
            if (run == null && _svc.State.Run.Active == null) yield break;
            run?.StartAuto();
            bool battleShot = false, draftShot = false, eventShot = false, otherShot = false;
            float deadline = Time.realtimeSinceStartup + 600;
            while (Time.realtimeSinceStartup < deadline)
            {
                var cur = _app.Router.Current;
                if (cur is RunResultScreen) yield break;
                if (cur is BattlePlaybackScreen b)
                {
                    if (!battleShot)
                    {
                        BattlePlaybackScreen.TimeScale = 1;
                        yield return Wait(2.2f);
                        yield return Shot("06_battle_playback");
                        battleShot = true;
                    }
                    BattlePlaybackScreen.TimeScale = 12;
                }
                var active = _svc.State.Run.Active;
                if (active != null && cur is RunScreen && _app.ModalLayer.childCount > 1)
                {
                    if (active.Phase == RunPhase.Draft && !draftShot) { draftShot = true; yield return Shot("07_draft"); }
                    else if (active.Phase == RunPhase.Event && !eventShot) { eventShot = true; yield return Shot("08_event"); }
                    else if ((active.Phase == RunPhase.Shop || active.Phase == RunPhase.ChooseBranch || active.Phase == RunPhase.Defeated) && !otherShot) { otherShot = true; yield return Shot("09_" + active.Phase.ToString().ToLowerInvariant()); }
                }
                if (cur is RunScreen rs && !rs.Auto && _svc.State.Run.Active != null) rs.StartAuto();
                yield return null;
            }
            Assert.Fail("run did not finish on auto in time");
        }

        // ---- setup / helpers ---------------------------------------------------------------------

        private IEnumerator Boot()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            Assert.That(_app, Is.Not.Null, "AppBootstrap built the app");
            _dir = Path.Combine(Application.temporaryCachePath, "playthrough_" + DateTime.UtcNow.Ticks);
            Directory.CreateDirectory(_dir);
            _clock = new ManualClock(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _svc = new LocalGameService(_dir, "local_playthrough", "Wiggles", _clock, 11);
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

        private static IEnumerator WaitUntil(Func<bool> cond, float timeout, Action each = null)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!cond() && Time.realtimeSinceStartup < end) { each?.Invoke(); yield return null; }
        }

        private LevelDef FirstLevel()
        {
            PortalDef first = null;
            foreach (var p in _svc.Content.Portals.Values)
                if (p.Kind == PortalKind.Main && (first == null || p.Order < first.Order)) first = p;
            return _svc.Content.RunLevel(first.LevelIds[0]);
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
