using System;
using System.Collections;
using System.IO;
using System.Linq;
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
    /// Owner playtest decisions: the Mythic chest art, the Mythic choice (Last Light's text), the vendors' Skills button off the
    /// counter, a run result with a Legendary part (the pull reveal) and one without (the strip only), and a kit battle with no
    /// run behind it (account HP / ATK / DEF, no level badge or XP bar). Explicit (<c>-testFilter PlaytestDecisionsShowcaseTests</c>);
    /// writes to $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/decisions. Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the playtest decision screenshots; run with -testFilter PlaytestDecisionsShowcaseTests")]
    public sealed class PlaytestDecisionsShowcaseTests
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
            _dir = Path.Combine(Application.temporaryCachePath, "decide_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_decide", "Wiggles", new ManualClock(T0), 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "decisions"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);
            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            foreach (var f in _svc.Content.FeatureDefs.Keys) _svc.State.Onboarding.Celebrated.Add(f); // no unlock moment over the shots
            _svc.Refresh(_ => { });

            var c = _svc.Content;
            var level = c.MainLevel(1);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            var run = _svc.State.Run.Active;
            run.Skills.AddRange(new[] { "bleed_serrated", "ice_shard", "gen_vitality" });
            run.Coins = 400;

            // the Mythic chest (its own art now), then the Mythic choice with Last Light
            run.Phase = RunPhase.Draft; run.MythicDraft = true;
            run.DraftOffer.Clear(); run.DraftOffer.AddRange(new[] { "mythic_last_light", "mythic_catalyst", "mythic_overkill" });
            Show();
            yield return Wait(0.9f);
            yield return Shot("decide_01_mythic_chest");
            var tap = Btn(_app.ModalLayer, "ChestTap");
            if (tap != null) { tap.onClick.Invoke(); yield return Wait(1.0f); }
            yield return Shot("decide_02_mythic_choice_last_light");

            // vendors: the Skills button on the clear wall, off the counter's crate
            run.MythicDraft = false; run.DraftOffer.Clear();
            run.Phase = RunPhase.Event; run.EventId = "smuggler"; run.EventTile = -1;
            Show();
            yield return Wait(0.8f);
            yield return Shot("decide_03_vendor_smuggler_skills_button");
            run.EventId = "cursed_relic";
            Show();
            yield return Wait(0.8f);
            yield return Shot("decide_04_vendor_cursed_skills_button");

            // run results: with a Legendary part (the reveal over the result) and without (the strip only)
            Ui.Clear(_app.ModalLayer);
            while (_app.Router.Depth > 1) _app.Router.Pop();
            _svc.ExecuteNow(new RunAbandon());
            var part = c.PartsConfig.Defs.Values.First().Id;
            _svc.State.Run.LastSummary = new RunSummary { RunId = "legendary", LevelId = level.Id, Won = true, Rolls = 31, LongestRolls = 34, Level = 9 };
            _svc.State.Run.LastSummary.Granted.AddRange(new[] { Reward.Cur(Currencies.Diamonds, 30), Reward.Cur(Currencies.Ore, 1200), new Reward(RewardKinds.Part, part, 1, (int)PartRarity.Epic), new Reward(RewardKinds.Part, part, 1, (int)PartRarity.Legendary) });
            var result = new RunResultScreen();
            _app.Router.Push(result);
            Assert.That(result.Reveal, Is.Not.Null, "a Legendary part opens the reveal");
            yield return Wait(2.5f);
            result.Reveal.Root.GetComponent<PullRevealPlayer>().Skip();
            yield return Wait(0.4f);
            yield return Shot("decide_05_result_legendary_reveal");
            result.Reveal.Close();
            yield return Wait(0.3f);
            yield return Shot("decide_06_result_legendary_after_reveal");
            _app.Router.Pop();
            _svc.State.Run.LastSummary = new RunSummary { RunId = "plain", LevelId = level.Id, Won = true, Rolls = 28, LongestRolls = 34, Level = 8 };
            _svc.State.Run.LastSummary.Granted.AddRange(new[] { Reward.Cur(Currencies.Diamonds, 30), Reward.Cur(Currencies.Ore, 1200), new Reward(RewardKinds.Part, part, 1, (int)PartRarity.Epic) });
            result = new RunResultScreen();
            _app.Router.Push(result);
            Assert.That(result.Reveal, Is.Null, "no Legendary: no reveal");
            yield return Wait(0.8f);
            yield return Shot("decide_07_result_no_legendary");
            _app.Router.Pop();

            // a kit battle with no run: account HP / ATK / DEF only
            var s = _svc.State;
            s.Heroes.Owned["hero_star_knight"] = new HeroEntry { Stars = 5, Level = 30 };
            s.Heroes.Equipped = "hero_star_knight";
            s.Mounts.Owned["mount_scout_saucer"] = new MountEntry { Stars = 5, Level = 18 };
            s.Mounts.Active = "mount_scout_saucer";
            _svc.Refresh(_ => { });
            var lo = _svc.Engine.BuildLoadout(s, _svc.NowMs);
            var setup = new BattleSetup { RoundCap = 12 };
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, new[] { "sig_fire" }));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "magma_slug", 3, 0, 300));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "ash_wraith", 3, 0, 300));
            var battle = BattleSimulator.Simulate(setup, c, new Rng(5UL));
            var play = new BattlePlaybackScreen(battle);
            _app.Router.Push(play);
            yield return Wait(1.2f);
            var strip = Find(play.Root, "TopStrip");
            Assert.That(Find(strip, "RunLevel").gameObject.activeSelf, Is.False, "no level badge without a run");
            yield return Shot("decide_08_kit_battle_hud");
        }

        private RunScreen Show()
        {
            SkillTooltip.Close();
            Ui.Clear(_app.ModalLayer);
            while (_app.Router.Current is RunScreen) _app.Router.Pop();
            var rs = new RunScreen();
            _app.Router.Push(rs);
            rs.SkipCoach();
            return rs;
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
