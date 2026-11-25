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
    /// The screens pass (rest, events, chest, shop, home, run result) at the reference resolution 1080 × 2400 (or
    /// $ROGUESTARS_SHOT_SIZE): sc_board, sc_levelup, sc_rest, sc_rest_train, sc_event, sc_event_vendor, sc_event_pod / _shrine / _wreck / _cursed / _medic, sc_event_result,
    /// sc_chest, sc_chest_reveal, sc_shop, sc_home, sc_result_victory, sc_result_defeat. Explicit
    /// (<c>-testFilter ScreensShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR or client/RogueStars/Screenshots/screens.
    /// Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the screens-pass screenshots; run with -testFilter ScreensShowcaseTests")]
    public sealed class ScreensShowcaseTests
    {
        private static int W = 1080, H = 2400;
        private const long T0 = 1_772_452_800_000L;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots, _only;
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
            _only = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_ONLY"); // e.g. "rest,event": only these groups
            _dir = Path.Combine(Application.temporaryCachePath, "screens_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_screens", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "screens"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);

            _svc.DevGrantStarterKit();
            var c = _svc.Content;
            _svc.State.Counters[Requirements.MainLevelCounter] = 12;
            for (int i = 1; i <= 12; i++) _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            _svc.State.Flags.Add(Features.DevUnlockAllFlag);
            _svc.Refresh(_ => { });

            var level = c.MainLevel(3);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            Assert.That(start.Ok, Is.True, start.Error);
            var run = _svc.State.Run.Active;
            run.RunStats.Add(new StatMod(StatId.Hp, 0, 400));
            run.RunStats.Add(new StatMod(StatId.Atk, 0, 300));
            run.Coins = 291;
            GiveSkills(c, run, 4);
            int sidekicks = 0;
            foreach (var id in c.AlienTypes.Keys) { if (sidekicks++ >= 3) break; run.Loadout.Aliens.Add(new CombatAlien { AlienTypeId = id, Name = id }); }
            _app.Router.Push(new RunScreen());
            yield return Wait(1.0f);
            if (Want("board")) yield return Shot("sc_board");

            if (Want("levelup") && OpenDraft(c, run, false))
            {
                yield return Rebuild(1.0f);
                yield return Shot("sc_levelup");
                Reset(run);
            }

            // 1. rest (campfire): the scene, then the Train picker
            if (Want("rest"))
            {
                OpenEvent(run, "campfire");
                yield return Rebuild(1.0f);
                yield return Shot("sc_rest");
                if (Click("Option:Train"))
                {
                    yield return Wait(0.6f);
                    yield return Shot("sc_rest_train");
                }
                Reset(run);
            }

            // 2. events: a scene event, a vendor event, a chance result
            if (Want("event"))
            {
                OpenEvent(run, "abandoned_cache");
                yield return Rebuild(1.0f);
                yield return Shot("sc_event");
                Reset(run);
                OpenEvent(run, "smuggler");
                yield return Rebuild(1.0f);
                yield return Shot("sc_event_vendor");
                Reset(run);
                // every imported scene prop and vendor (Art/Run/Props, Art/Run/Characters)
                foreach (var (id, shot) in new[]
                {
                    ("treasure_pod", "sc_event_pod"), ("echoing_shrine", "sc_event_shrine"), ("strange_spores", "sc_event_wreck"),
                    ("cursed_relic", "sc_event_cursed"), ("alien_medic", "sc_event_medic"),
                })
                {
                    OpenEvent(run, id);
                    yield return Rebuild(1.0f);
                    yield return Shot(shot);
                    Reset(run);
                }
                OpenEvent(run, "strange_spores");
                yield return Rebuild(0.2f);
                Ui.Clear(_app.ModalLayer);
                _svc.ExecuteNow(new RunChooseEventOption { Index = 0 });
                if (_app.Router.Current is RunScreen rs1) rs1.Rebuild();
                yield return Wait(3.2f);
                yield return Shot("sc_event_result");
                run = _svc.State.Run.Active; // commands replace the state
                Reset(run);
            }

            // 3. chest: a Mythic choice after a miniboss
            if (Want("chest") && OpenDraft(c, run, true))
            {
                yield return Rebuild(1.0f);
                yield return Shot("sc_chest");
                if (Click("ChestTap"))
                {
                    yield return Wait(1.2f);
                    yield return Shot("sc_chest_reveal");
                }
                Reset(run);
            }

            // 4. shop (one offer on SALE)
            if (Want("shop"))
            {
                run.ShopsVisited = 1;
                typeof(RunSystem).GetMethod("FillShop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    .Invoke(null, new object[] { c, run });
                if (!run.Shop.Exists(o => o.ListPrice > 0))
                {
                    var o = run.Shop.Find(x => x.Kind == ShopOfferKind.Skill);
                    if (o != null) { o.ListPrice = o.Price; o.Price /= 2; }
                }
                run.Phase = RunPhase.Shop;
                yield return Rebuild(1.0f);
                yield return Shot("sc_shop");
                Reset(run);
            }

            // 5. run result (victory, defeat)
            if (Want("result"))
            {
                Ui.Clear(_app.ModalLayer);
                _svc.State.Run.LastSummary = Summary(c, level, true);
                _app.Router.Push(new RunResultScreen());
                yield return Wait(1.4f);
                yield return Shot("sc_result_victory");
                _app.Router.Pop();
                _svc.State.Run.LastSummary = Summary(c, level, false);
                _app.Router.Push(new RunResultScreen());
                yield return Wait(1.4f);
                yield return Shot("sc_result_defeat");
                _app.Router.Pop();
            }

            // 6. home
            if (Want("home"))
            {
                Ui.Clear(_app.ModalLayer);
                _svc.ExecuteNow(new RunAbandon());
                while (_app.Router.Depth > 1) _app.Router.Pop();
                _svc.State.Run.Levels[c.MainLevelId(12)].Feats = new List<bool> { true, true, true };
                _app.Router.RebuildCurrent();
                foreach (var f in c.FeatureDefs.Keys) _app.SnoozedUnlocks.Add(f);
                yield return Wait(1.0f);
                Ui.Clear(_app.ModalLayer);
                if (!(_app.Router.Current is HubScreen)) { while (_app.Router.Depth > 1) _app.Router.Pop(); }
                _app.Router.RebuildCurrent();
                yield return Wait(0.8f);
                yield return Shot("sc_home");
            }
        }

        private bool Want(string group) => string.IsNullOrEmpty(_only) || Array.IndexOf(_only.Split(','), group) >= 0;

        private static RunSummary Summary(ContentDb c, LevelDef level, bool won)
        {
            var s = new RunSummary { RunId = "show", LevelId = level.Id, Mode = RunMode.Normal, Won = won, Rolls = won ? 30 : 17, Level = won ? 9 : 6, Minibosses = won ? 2 : 1, ShopBuys = 3 };
            s.Granted.Add(Reward.Cur(Currencies.Biomass, won ? 1250 : 0));
            s.Granted.Add(Reward.Cur(Currencies.Diamonds, won ? 30 : 0));
            s.Granted.Add(new Reward(RewardKinds.Part, "*", 1, 2));
            s.Granted.RemoveAll(r => r.Amount <= 0);
            if (won) s.Doublable.AddRange(s.Granted);
            return s;
        }

        private static void GiveSkills(ContentDb c, ActiveRun run, int n)
        {
            int added = 0;
            foreach (var id in RunDraft.SortedSkillIds(c))
            {
                if (added >= n) break;
                var s = c.Skills[id];
                if (s.Source != SkillSource.Run || s.Signature || !s.Draftable || s.Rarity == Rarity.Mythic || RunDraft.HasUpgrade(c, id) || run.Skills.Contains(id)) continue;
                if (!SkillLeveling.Grows(c, s)) continue;
                run.Skills.Add(id);
                if (added == 1) run.SkillLevels[id] = 2;
                added++;
            }
        }

        private void OpenEvent(ActiveRun run, string id)
        {
            run.Phase = RunPhase.Event;
            run.EventId = id;
            run.EventTile = -1;
        }

        private void Reset(ActiveRun run)
        {
            Ui.Clear(_app.ModalLayer);
            run = _svc.State.Run.Active ?? run;
            run.Phase = RunPhase.AwaitRoll;
            run.EventId = null;
            run.MythicDraft = false;
            run.DraftOffer.Clear();
        }

        private IEnumerator Rebuild(float wait)
        {
            Ui.Clear(_app.ModalLayer);
            if (_app.Router.Current is RunScreen rs) rs.Rebuild();
            yield return Wait(wait);
        }

        /// <summary>Clicks the first active button with this name (in the modal layer or the screen).</summary>
        private bool Click(string name)
        {
            foreach (var b in _app.ModalLayer.GetComponentsInChildren<Button>(false))
                if (b.name == name && b.interactable) { b.onClick.Invoke(); return true; }
            return false;
        }

        private static bool OpenDraft(ContentDb c, ActiveRun run, bool mythic)
        {
            var offer = new List<string>();
            foreach (var s in c.Skills.Values)
            {
                if (offer.Count >= 3) break;
                if (s.Source != SkillSource.Run || !s.Draftable || s.Signature || s.RequiresFamilyCount > 0 || s.Requires.Count > 0 || run.Skills.Contains(s.Id)) continue;
                var tier = RunTuning.RunTier(s.Rarity);
                if (mythic ? tier != Rarity.Mythic : tier == Rarity.Mythic) continue;
                offer.Add(s.Id);
            }
            if (offer.Count < 3) return false;
            run.DraftOffer.Clear();
            run.DraftOffer.AddRange(offer);
            run.Phase = RunPhase.Draft;
            run.PendingDrafts = Math.Max(1, run.PendingDrafts);
            run.MythicDraft = mythic;
            return true;
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
    }
}
