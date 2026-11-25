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
    /// Skill help (owner playtest): the "?" panel of a Bleed, an Ice and a Lightning skill (the skill's text, then one block per
    /// mechanic and the synergy), and the skills book on the level-up draft, the Mythic choice, the shop, a scene event and a
    /// vendor (closed and opened over the decision). Explicit (<c>-testFilter SkillHelpShowcaseTests</c>); writes to
    /// $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/skillhelp. Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the skill help screenshots; run with -testFilter SkillHelpShowcaseTests")]
    public sealed class SkillHelpShowcaseTests
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
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "help_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_help", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "skillhelp"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            var c = _svc.Content;
            var level = c.MainLevel(1);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            var run = _svc.State.Run.Active;
            run.Skills.AddRange(new[] { "bleed_serrated", "bleed_scent", "ice_shard", "combo_flurry", "gen_vitality" });
            run.Coins = 400;

            // the level-up draft: a Bleed, an Ice and a Lightning skill, the skills book bottom-right
            run.Phase = RunPhase.Draft; run.MythicDraft = false;
            run.DraftOffer.Clear(); run.DraftOffer.AddRange(new[] { "bleed_frenzy", "ice_deep_freeze", "light_spark" });
            var screen = Show();
            yield return Wait(0.8f);
            yield return Shot("book_draft");
            foreach (var kv in new[] { ("bleed_frenzy", "help_bleed"), ("ice_deep_freeze", "help_ice"), ("light_spark", "help_lightning") })
            {
                var card = Find(_app.ModalLayer, "Draft:" + kv.Item1);
                Assert.That(card, Is.Not.Null, kv.Item1);
                Btn(card, "InfoButton").onClick.Invoke();
                yield return Wait(0.4f);
                yield return Shot(kv.Item2);
                SkillTooltip.Close();
            }
            Btn(_app.ModalLayer, "SkillsBook").onClick.Invoke();
            yield return Wait(0.5f);
            yield return Shot("book_draft_open");
            CloseSkills();
            yield return Wait(0.3f);
            yield return Shot("book_draft_back");

            // the Mythic choice
            run.MythicDraft = true;
            run.DraftOffer.Clear(); run.DraftOffer.AddRange(new[] { "mythic_last_light", "mythic_catalyst", "mythic_exsanguinate" });
            screen = Show();
            yield return Wait(0.8f);
            yield return Shot("book_mythic_chest");
            var tap = Btn(_app.ModalLayer, "ChestTap");
            if (tap != null) { tap.onClick.Invoke(); yield return Wait(0.9f); }
            yield return Shot("book_mythic");

            // the shop
            run.MythicDraft = false; run.DraftOffer.Clear();
            run.Phase = RunPhase.Shop;
            run.Shop.Clear();
            run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Heal, Value = 35, Price = 30 });
            run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Skill, SkillId = "bleed_tearing", Price = 60 });
            run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Skill, SkillId = "ice_brittle", Price = 60 });
            run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Skill, SkillId = "fire_kindle", Price = 60 });
            screen = Show();
            yield return Wait(0.8f);
            yield return Shot("book_shop");
            Btn(_app.ModalLayer, "SkillsBook").onClick.Invoke();
            yield return Wait(0.5f);
            yield return Shot("book_shop_open");
            CloseSkills();

            // events: a scene event and a vendor
            run.Shop.Clear();
            run.Phase = RunPhase.Event; run.EventId = "campfire"; run.EventTile = -1;
            screen = Show();
            yield return Wait(0.8f);
            yield return Shot("book_event_rest");
            run.EventId = "smuggler";
            screen = Show();
            yield return Wait(0.8f);
            yield return Shot("book_event_vendor");
            Btn(_app.ModalLayer, "SkillsBook").onClick.Invoke();
            yield return Wait(0.5f);
            yield return Shot("book_event_vendor_open");
            CloseSkills();
        }

        /// <summary>Opens the run screen afresh so it shows the run's current decision.</summary>
        private RunScreen Show()
        {
            SkillTooltip.Close();
            while (_app.Router.Current is RunScreen) _app.Router.Pop();
            var rs = new RunScreen();
            _app.Router.Push(rs);
            rs.SkipCoach(); // the first-run coach marks would cover the shots
            return rs;
        }

        private void CloseSkills()
        {
            var sheet = Find(_app.ModalLayer, "Modal:Skills");
            if (sheet != null) Btn(sheet, "Button:Close")?.onClick.Invoke();
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static Button Btn(Transform root, string name)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(true)) if (b.name == name) return b;
            return null;
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
            SkillTooltip.Close();
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
