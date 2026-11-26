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
using RogueStars.Rules.Content.Quests;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// Run Quests (docs/design/run-quests/recipe.md): the visitor tile on the board, Mothwick's encounter card for a story choice,
    /// a hard battle and a lost one (the normal defeat with the revive offer), prices (coins, a random Mythic skill and the
    /// result naming the skill taken), a boon and its result sheet, an epilogue scene with the Starglint coin line, and the
    /// journal (chapter reached, no total). Explicit (<c>-testFilter RunQuestShowcaseTests</c>); writes to $ROGUESTARS_SHOTS_DIR
    /// or client/RogueStars/Screenshots/quests. Needs a graphics device: run WITHOUT -nographics.
    /// </summary>
    [Explicit("renders the Run Quest screenshots; run with -testFilter RunQuestShowcaseTests")]
    public sealed class RunQuestShowcaseTests
    {
        private const int W = 1080, H = 2400;
        private const long T0 = 1_772_452_800_000L;
        private const string Q = QuestMothwick.Id;
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots;
        private Camera _cam;
        private RenderTexture _rt;
        private Texture2D _tex;
        private GameState s => _svc.State;

        [UnityTest, Timeout(600000)]
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "quest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_quest", "Wiggles", new ManualClock(T0), 11) { AdsDisabled = true };
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "quests"));
            Directory.CreateDirectory(_shots);
            SetupCapture();
            yield return Wait(0.4f);
            _svc.DevSetFlag(Features.DevUnlockAllFlag, true);
            foreach (var f in _svc.Content.FeatureDefs.Keys) _svc.State.Onboarding.Celebrated.Add(f);
            _svc.Refresh(_ => { });
            var c = _svc.Content;
            var q = c.RunQuest(Q);

            // 1. the visitor tile on the board, a few steps ahead of the worm
            s.RunQuests.Encounters = 3;
            double chance = c.QuestTuning.ChancePct;
            c.QuestTuning.ChancePct = 100; // this run meets Mothwick (level 2: the tutorial run never does)
            var level = c.MainLevel(2);
            s.Run.Levels[c.MainLevel(1).Id] = new LevelProgress { Cleared = true };
            s.Counters[Requirements.MainLevelCounter] = Math.Max(s.Counter(Requirements.MainLevelCounter), 1);
            var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
            c.QuestTuning.ChancePct = chance;
            Assert.That(start.Ok, Is.True, start.Error);
            var run = s.Run.Active;
            Assert.That(run.Quest.Placed, Is.True);
            int tile = run.Quest.Tile;
            int before = run.Tiles.FindIndex(t => t.Next.Contains(tile));
            int before2 = run.Tiles.FindIndex(t => t.Next.Contains(before));
            run.Position = before2 >= 0 ? before2 : before; run.Trail.Clear(); run.Trail.Add(run.Position);
            Show();
            yield return Wait(1.2f);
            yield return Shot("quest_01_board_visitor_tile");

            // 2. a story choice (chapter 4)
            Meet("m04", r => { });
            yield return Wait(0.8f);
            yield return Shot("quest_02_story_choice");

            // 3. a hard battle (chapter 27)
            Meet("m27", r => { });
            yield return Wait(0.8f);
            yield return Shot("quest_03_battle");

            // 4. prices: coins (affordable), coins (not affordable), a Mythic skill with the picker
            Meet("m18a", r => r.Coins = 420);
            yield return Wait(0.8f);
            yield return Shot("quest_04_price_coins");
            Meet("m05a", r => r.Coins = 40);
            yield return Wait(0.8f);
            yield return Shot("quest_05_price_unaffordable");
            var mythics = c.Skills.Values.Where(x => x.Draftable && !x.Signature && RunTuning.RunTier(x.Rarity) == Rarity.Mythic).OrderBy(x => x.Id).Take(2).Select(x => x.Id).ToList();
            Meet("m22", r => { foreach (var m in mythics) if (!r.Skills.Contains(m)) r.Skills.Add(m); });
            yield return Wait(0.8f);
            yield return Shot("quest_06_price_mythic");
            Btn(_app.ModalLayer, "QuestOption:0").onClick.Invoke();
            yield return Wait(2.5f);
            yield return Shot("quest_07_mythic_taken_result");

            // 5. a boon, then its result sheet
            Meet("m19", r => { }, bonus: 1);
            yield return Wait(0.8f);
            yield return Shot("quest_08_boon");
            Btn(_app.ModalLayer, "QuestOption:0").onClick.Invoke();
            yield return Wait(2.5f);
            yield return Shot("quest_09_boon_result");

            // 6. the final price: forget every run skill
            Meet("m29a", r => { });
            yield return Wait(0.8f);
            yield return Shot("quest_10_price_all_skills");

            // 7. an epilogue scene after the chain, and its result with the Starglint line
            var p = s.RunQuests.Quests[Q];
            p.Completed = true; p.Node = null; p.CoinBonus = 3;
            Meet(null, r => { r.Quest.Epilogue = true; r.Quest.EpilogueId = q.Epilogues[0].Id; r.Quest.CoinBonus = 3; });
            yield return Wait(0.8f);
            yield return Shot("quest_11_epilogue");
            Btn(_app.ModalLayer, "QuestOption:0").onClick.Invoke();
            yield return Wait(2.5f);
            yield return Shot("quest_12_epilogue_result_starglint");

            // 8. a lost quest battle: the normal defeat (revive offer), the story waits
            p.Completed = false; p.EpiloguesSeen = 0;
            Meet("m03", r => r.WormHp = 1, bonus: 3);
            yield return Wait(0.8f);
            Btn(_app.ModalLayer, "QuestOption:0").onClick.Invoke();
            yield return Wait(12f); // the battle playback, then the defeat screen
            yield return Shot("quest_14_battle_lost_defeat");

            // 9. the journal (More → Journal)
            Ui.Clear(_app.ModalLayer);
            while (_app.Router.Current is RunScreen) _app.Router.Pop();
            p = s.RunQuests.Quests[Q];
            p.Completed = false; p.Node = "m20"; p.CoinBonus = 2;
            p.Journal.Clear();
            foreach (var id in new[] { "m01", "m02a", "m03", "m04", "m05a", "m06a", "m07", "m08", "m09", "m10b", "m11b", "m12", "m13", "m14", "m15", "m16a", "m17a", "m18a", "m19" })
            {
                var n = q.Node(id);
                var o = n.Options.First(x => x.Action != QuestOptionAction.Wait);
                p.Journal.Add(new QuestJournalEntry { Chapter = n.Chapter, NodeId = id, Choice = o.Text, Line = o.Journal });
            }
            _app.Router.Push(new JournalScreen());
            yield return Wait(0.8f);
            yield return Shot("quest_13_journal");
        }

        /// <summary>The run waits at Mothwick's encounter on <paramref name="node"/> (null = keep the progress as it is).</summary>
        private void Meet(string node, Action<ActiveRun> setup, int bonus = 1)
        {
            if (!s.RunQuests.Quests.TryGetValue(Q, out var p)) s.RunQuests.Quests[Q] = p = new QuestProgress();
            if (node != null) { p.Node = node; p.Completed = false; p.CoinBonus = bonus; }
            var run = s.Run.Active;
            run.Quest = new RunQuestRun { QuestId = Q, Placed = true, Tile = run.Position, Met = true, CoinBonus = p.CoinBonus, BonusName = "Starglint" };
            setup(run);
            run.Phase = RunPhase.Quest;
            Show();
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
