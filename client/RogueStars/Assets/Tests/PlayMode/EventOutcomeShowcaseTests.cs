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
    /// Event outcome rules (owner 2026-11): a gamble's result sheet, a deterministic result flying into the HUD (campfire
    /// Rest), a toast for a result without effect (Leave), and a lost event fight going straight to the revive with its
    /// own text. Explicit; run WITHOUT -nographics (ROGUESTARS_SHOTS_DIR = output folder; 1080×2340).
    /// </summary>
    [Explicit("renders the event outcome screenshots; run with -testFilter EventOutcomeShowcaseTests")]
    public sealed class EventOutcomeShowcaseTests
    {
        private const long T0 = 1_772_452_800_000L;
        private const int W = 1080, H = 2340;
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
            _dir = Path.Combine(Application.temporaryCachePath, "outcomes_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_outcomes", "Wiggles", new ManualClock(T0), 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "event-outcomes"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.6f);
            var c = _svc.Content;
            RunScreen.BoardSpeed = 1;
            _svc.DevGrantStarterKit();
            _svc.State.Counters[Requirements.MainLevelCounter] = 12;
            for (int i = 1; i <= 12; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            _svc.Refresh(_ => { });
            foreach (var f in c.FeatureOrder) { _svc.State.Onboarding.Unlocked[f.Id] = T0; _svc.State.Onboarding.Celebrated.Add(f.Id); }

            // 1. a gamble: the sheet with the chance meter
            var run = EventRun(c, "abandoned_cache");
            var rs = ShowRun();
            yield return Wait(1.0f);
            yield return Shot("outcome_1_gamble_event");
            Choose(c, "abandoned_cache", "Search it");
            yield return Wait(3.4f);
            yield return Shot("outcome_2_gamble_sheet");

            // 2. a deterministic result: campfire Rest flies into the HUD, no sheet
            run = EventRun(c, "campfire");
            run.WormHp = Math.Round(run.WormMaxHp * 0.4);
            rs = ShowRun();
            yield return Wait(1.0f);
            yield return Shot("outcome_3_campfire");
            Choose(c, "campfire", "Rest");
            yield return Wait(0.45f);
            yield return Shot("outcome_4_flyin");
            Assert.That(rs.OutcomeOpen, Is.False, "no sheet after resting");

            // 3. nothing: a toast
            EventRun(c, "strange_spores");
            rs = ShowRun();
            yield return Wait(1.0f);
            Choose(c, "strange_spores", "Leave");
            yield return Wait(0.5f);
            yield return Shot("outcome_5_toast");
            Assert.That(rs.LastToast, Is.EqualTo("You move on."));

            // biomes pass: biome events (an NPC on the stage, an NPC at the counter, a prop), a new shared event, a Rite with its
            // Offering, the skill picker and the result flying in
            foreach (var (ev, shot) in new[] { ("lava_salamander_smith", "biome_1_npc_scene"), ("gas_rig_foreman", "biome_2_npc_vendor"),
                         ("lava_ore_geyser", "biome_3_prop"), ("wishing_well", "biome_4_shared_new") })
            {
                EventRun(c, ev);
                ShowRun();
                yield return Wait(1.0f);
                yield return Shot(shot);
            }
            run = EventRun(c, "rite_lava");
            var ice = new List<string>();
            foreach (var id in RunDraft.SortedSkillIds(c))
            {
                var sk = c.Skills[id];
                if (ice.Count < 2 && sk.Family == SkillFamily.Ice && !sk.Signature && sk.Source == SkillSource.Run && sk.Draftable) ice.Add(id);
            }
            run.Skills.AddRange(ice);
            rs = ShowRun();
            yield return Wait(1.0f);
            yield return Shot("biome_5_rite");
            UnityEngine.UI.Button offer = null;
            foreach (var b in _app.ModalLayer.GetComponentsInChildren<UnityEngine.UI.Button>(true)) if (b.name.StartsWith("Button:Offer an Ice skill")) offer = b;
            Assert.That(offer, Is.Not.Null, "the Offering shows with an Ice skill owned");
            offer.onClick.Invoke();
            yield return Wait(0.6f);
            yield return Shot("biome_6_offering_picker");
            UnityEngine.UI.Button pick = null;
            foreach (var b in _app.ModalLayer.GetComponentsInChildren<UnityEngine.UI.Button>(true)) if (b.name == "Offer:" + ice[0]) pick = b;
            Assert.That(pick, Is.Not.Null);
            pick.onClick.Invoke();
            yield return Wait(0.45f);
            yield return Shot("biome_7_offering_flyin");
            Assert.That(_svc.State.Run.Active.Skills, Does.Not.Contain(ice[0]));

            // 4. a lost event fight: the playback, then straight to the revive with the loss text
            run = EventRun(c, "training_dummy");
            run.WormHp = 1;
            ShowRun();
            yield return Wait(1.0f);
            Assert.That(_svc.ExecuteNow(new RunChooseEventOption { Index = Opt(c, "training_dummy", "Spar with an elite") }).Ok, Is.True);
            for (int f = 0; f < 600 && !(_app.Router.Current is BattlePlaybackScreen); f++) yield return null;
            if (_app.Router.Current is BattlePlaybackScreen bp) { yield return Wait(0.6f); yield return Shot("outcome_6_lost_fight_playback"); bp.PlayAll(); bp.Continue(); }
            for (int f = 0; f < 600 && !(_app.Router.Current is RunScreen); f++) yield return null;
            yield return Wait(1.2f);
            yield return Shot("outcome_7_lost_fight_revive");
            Assert.That(_svc.State.Run.Active.Phase, Is.EqualTo(RunPhase.Defeated));
        }

        private ActiveRun EventRun(ContentDb c, string eventId)
        {
            if (_svc.State.Run.Active != null) _svc.ExecuteNow(new RunAbandon());
            Ui.Clear(_app.ModalLayer);
            var level = c.MainLevel(12);
            _svc.State.Wallet.Add(Currencies.Stamina, 200);
            var r = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(r.Ok, Is.True, r.Error);
            var run = _svc.State.Run.Active;
            if (run.Phase == RunPhase.Signature) { _svc.ExecuteNow(new RunPickSignature()); run = _svc.State.Run.Active; }
            run.Coins = 291;
            run.Phase = RunPhase.Event; run.EventId = eventId; run.EventTile = -1;
            return run;
        }

        private RunScreen ShowRun()
        {
            Ui.Clear(_app.ModalLayer);
            _app.Router.Reset(new HubScreen());
            var rs = new RunScreen();
            _app.Router.Push(rs);
            rs.SkipCoach();
            return rs;
        }

        private static int Opt(ContentDb c, string ev, string text) => c.RunEvents[ev].Options.FindIndex(o => o.Text.StartsWith(text));

        private void Choose(ContentDb c, string ev, string option)
        {
            Ui.Clear(_app.ModalLayer);
            Assert.That(_svc.ExecuteNow(new RunChooseEventOption { Index = Opt(c, ev, option) }).Ok, Is.True);
            if (_app.Router.Current is RunScreen rs && rs.LastToast == null && !rs.OutcomeOpen && rs.LastPopups.Count == 0) rs.Rebuild();
        }

        private void SetupCapture()
        {
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("ShowcaseCamera");
            _cam = go.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.orthographic = true;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 10;
            _cam.cullingMask = ~(1 << World.WorldBoard.Layer);
            _cam.targetTexture = _rt;
            var canvas = _app.Root.GetComponentInParent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _cam;
            canvas.planeDistance = 1;
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
            if (_app.Router.Current is RunScreen run && run.World != null && run.World.Cam.enabled) run.World.Cam.Render();
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
