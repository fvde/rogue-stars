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
    /// Run shop fixes (owner playtest): the worm rolls onto a shop like in play, buys a +ATK perk (the HUD's ATK changes while the
    /// shop is open), rerolls, leaves and reaches a second shop (the reroll price is back at the base). Explicit
    /// (<c>-testFilter ShopFixShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/shopfix, plus
    /// shopfix_hud.txt with the HUD texts of every shot. Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the shop fix screenshots; run with -testFilter ShopFixShowcaseTests")]
    public sealed class ShopFixShowcaseTests
    {
        private const int W = 1080, H = 2400;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;
        private readonly List<string> _log = new List<string>();

        [UnityTest, Timeout(600000)]
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "shop_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_shop", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "shopfix"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            var c = _svc.Content;
            // every shop offers the ATK perk (this showcase's own content instance)
            foreach (var t in c.RunShopTables.Values) { t.StatOffers.RemoveAll(m => m.Stat != StatId.Atk); }
            var level = c.MainLevel(1);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            var run = _svc.State.Run.Active;
            // a straight board with two shops in a row (a shop stops the worm when passed)
            run.Tiles.Clear();
            for (int i = 0; i <= 40; i++)
            {
                var tile = new RunTile { Id = i, TileId = i == 0 ? "start" : i == 1 || i == 2 ? "shop" : "empty", X = i };
                if (i < 40) tile.Next.Add(i + 1);
                run.Tiles.Add(tile);
            }
            run.BoardEnd = 40; run.BossGateTile = -1; run.Position = 0; run.Trail.Clear(); run.Trail.Add(0);
            run.Coins = 600;
            _app.Router.Push(new RunScreen());
            var screen = (RunScreen)_app.Router.Current;
            yield return Wait(1.0f);

            // roll like the player: the worm hops and stops at the first shop
            _app.Session.Run(new RunRoll());
            yield return WaitFor(() => _svc.State.Run.Active.Phase == RunPhase.Shop && FindButton(_app.ModalLayer, "Button:Leave") != null, 12f);
            yield return Wait(1.0f);
            yield return Shot("shop1_before_buy", screen);

            run = _svc.State.Run.Active;
            int atk = run.Shop.FindIndex(o => o.Kind == ShopOfferKind.Stat && o.Stat.Stat == StatId.Atk);
            Assert.That(atk, Is.GreaterThanOrEqualTo(0), "the shop offers +ATK");
            double before = RunSystem.WormStats(c, run).Atk;
            var buy = SlotButton(atk);
            Assert.That(buy, Is.Not.Null);
            buy.onClick.Invoke();
            yield return Wait(0.15f);
            yield return Shot("shop2_buy_atk_0.15s", screen);
            yield return Wait(0.6f);
            yield return Shot("shop3_after_buy", screen);
            double after = RunSystem.WormStats(c, _svc.State.Run.Active).Atk;
            _log.Add("worm ATK " + before + " -> " + after);
            Assert.That(_svc.State.Run.Active.Phase, Is.EqualTo(RunPhase.Shop));
            Assert.That(ShopAtk(), Is.EqualTo(Fmt.Num(Math.Round(after))), "the shop HUD shows the new ATK while the shop is open");

            // reroll twice: the price rises within the visit
            FindReroll().onClick.Invoke();
            yield return Wait(0.5f);
            FindReroll().onClick.Invoke();
            yield return Wait(0.5f);
            yield return Shot("shop4_after_2_rerolls", screen);

            // leave, roll onto the next shop: its reroll starts at the base price again
            FindButton(_app.ModalLayer, "Button:Leave").onClick.Invoke();
            yield return Wait(0.8f);
            _app.Session.Run(new RunRoll());
            yield return WaitFor(() => _svc.State.Run.Active != null && _svc.State.Run.Active.ShopsVisited >= 2 && FindButton(_app.ModalLayer, "Button:Leave") != null, 12f);
            yield return Wait(1.0f);
            yield return Shot("shop5_next_shop_base_reroll", screen);
            Assert.That(FindReroll().name, Is.EqualTo("Button:Reroll (" + RunSystem.ShopRerollCost(c, _svc.State.Run.Active) + ")"));
            File.WriteAllLines(Path.Combine(_shots, "shopfix_hud.txt"), _log);
        }

        private string ShopAtk()
        {
            foreach (var t in _app.ModalLayer.GetComponentsInChildren<Text>(true)) if (t.name == "AtkChip") return t.text;
            return null;
        }

        private Button SlotButton(int index)
        {
            foreach (var t in _app.ModalLayer.GetComponentsInChildren<Transform>(true))
                if (t.name == "OfferSlot:" + index) return t.GetComponentInChildren<Button>(true);
            return null;
        }

        private Button FindReroll()
        {
            foreach (var b in _app.ModalLayer.GetComponentsInChildren<Button>(true)) if (b.name.StartsWith("Button:Reroll")) return b;
            return null;
        }

        private static Button FindButton(Transform root, string name)
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

        private IEnumerator Shot(string name, RunScreen screen)
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
            var run = _svc.State.Run.Active;
            _log.Add(name + ": shop HUD ATK " + ShopAtk() + " | run HUD " + screen.HudText + " | coins " + run?.Coins + " | reroll " + FindReroll()?.name);
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        private static IEnumerator WaitFor(Func<bool> done, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(done(), Is.True, "timed out");
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
