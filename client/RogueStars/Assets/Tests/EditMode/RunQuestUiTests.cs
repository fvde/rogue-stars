using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Content.Quests;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Run Quests in the client (docs/design/run-quests/recipe.md): the encounter card (portrait, name, the chapter without a
    /// total, text, options with cost / risk chips), a skill sacrifice taking a random skill of its tier (no picker), a lost
    /// battle ending in the normal defeat, epilogue scenes, the result sheet and popups of the coin bonus, the board marker and
    /// the journal screen under "More" (no feature gate, no chapter total).
    /// </summary>
    public sealed class RunQuestUiTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;
        private const string Q = QuestMothwick.Id;

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; }
        [TearDown] public void TearDown() { RunScreen.InstantOutcomes = false; _f.Dispose(); }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static string Texts(Transform root)
        {
            var parts = new List<string>();
            foreach (var t in root.GetComponentsInChildren<Text>(true)) if (t.gameObject.activeInHierarchy) parts.Add(t.text);
            return string.Join(" | ", parts);
        }

        /// <summary>A run waiting at a quest encounter on <paramref name="node"/> (or an epilogue scene), shown on the run screen.</summary>
        private RunScreen QuestOnScreen(string node, System.Action<ActiveRun> setup = null, bool epilogue = false)
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            var run = S.Run.Active;
            run.Tiles.Clear();
            for (int i = 0; i <= 20; i++) { var t = new RunTile { Id = i, TileId = i == 0 ? "start" : i == 1 ? ContentDb.QuestTileId : "empty", X = i }; if (i < 20) t.Next.Add(i + 1); run.Tiles.Add(t); }
            run.BossGateTile = -1; run.Position = 1; run.Trail.Clear(); run.Trail.AddRange(new[] { 0, 1 });
            var q = C.RunQuest(Q);
            S.RunQuests.Quests[Q] = new QuestProgress { Node = epilogue ? null : node, Completed = epilogue, CoinBonus = epilogue ? 3 : 1, Encounters = 3 };
            S.RunQuests.Quests[Q].Journal.Add(new QuestJournalEntry { Chapter = 1, NodeId = "m01", Choice = "Open the parcel", Line = "I opened Mothwick’s parcel." });
            run.Quest = new RunQuestRun { QuestId = Q, Placed = true, Tile = 1, Met = true, CoinBonus = epilogue ? 3 : 1, BonusName = q.BonusName, Epilogue = epilogue, EpilogueId = epilogue ? q.Epilogues[0].Id : null };
            setup?.Invoke(run);
            run.Phase = RunPhase.Quest;
            _f.App.Router.Push(new RunScreen());
            return (RunScreen)_f.App.Router.Current;
        }

        private Button Option(int i) => FindNamed(_f.App.ModalLayer, "QuestOption:" + i)?.GetComponent<Button>();

        [Test]
        public void EncounterCard_ShowsPortrait_Name_ChapterProgress_Text_AndOptions()
        {
            QuestOnScreen("m04");
            var modal = FindNamed(_f.App.ModalLayer, "Modal:Quest");
            Assert.That(modal, Is.Not.Null, "the encounter card");
            string texts = Texts(modal);
            Assert.That(texts, Does.Contain("Mothwick"));
            Assert.That(texts, Does.Contain("Chapter 4"));
            Assert.That(texts, Does.Not.Contain("/ 30"), "the story's length is not spoiled");
            Assert.That(texts, Does.Contain("The Address"));
            Assert.That(texts, Does.Contain("Top of the Sky"));
            Assert.That(FindNamed(modal, "Prop").GetComponent<Image>().sprite, Is.Not.Null, "the portrait (placeholder until art exists)");
            Assert.That(Option(0), Is.Not.Null); Assert.That(Option(1), Is.Not.Null);
            Assert.That(texts, Does.Contain("Follow the brightest star"));
            Assert.That(texts, Does.Contain("Story"), "story chip");
            Assert.That(FindNamed(modal, "QuestJournal"), Is.Not.Null, "the story so far");

            Option(1).onClick.Invoke();
            Assert.That(RunSystem.QuestProgressOf(S, Q).Node, Is.EqualTo("m05b"));
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.AwaitRoll));
            Assert.That(S.Run.Active.LastOutcome.Source, Is.EqualTo("quest"));
        }

        [Test]
        public void Battle_And_Price_ShowTheirChips_AndAnUnaffordablePriceIsDisabled()
        {
            QuestOnScreen("m03");
            string texts = Texts(FindNamed(_f.App.ModalLayer, "Modal:Quest"));
            Assert.That(texts, Does.Contain("Hard battle"));
            Assert.That(texts, Does.Contain("Story waits"));
            Assert.That(texts, Does.Contain("Lose: the run ends"));
            Assert.That(_f.Service.ExecuteNow(new RunAbandon()).Ok, Is.True);
            _f.App.Router.Pop();

            QuestOnScreen("m18a", run => run.Coins = 120); // 300 coins
            texts = Texts(FindNamed(_f.App.ModalLayer, "Modal:Quest"));
            Assert.That(texts, Does.Contain("−300 coins"));
            Assert.That(texts, Does.Contain("Not enough coins"));
            Assert.That(Option(0).interactable, Is.False);
            Assert.That(Option(1).interactable, Is.True, "not now is always possible");
            Option(1).onClick.Invoke();
            Assert.That(RunSystem.QuestProgressOf(S, Q).Node, Is.EqualTo("m18a"), "the story waits");
        }

        [Test]
        public void SkillSacrifice_TakesARandomSkillOfTheTier_WithoutAPicker_AndTheResultNamesIt()
        {
            var mythics = C.Skills.Values.Where(s => s.Draftable && !s.Signature && RunTuning.RunTier(s.Rarity) == Rarity.Mythic).OrderBy(s => s.Id).Take(2).Select(s => s.Id).ToList();
            var screen = QuestOnScreen("m22", run => run.Skills.AddRange(mythics));
            string texts = Texts(FindNamed(_f.App.ModalLayer, "Modal:Quest"));
            Assert.That(texts, Does.Contain("A random Mythic skill"));
            Assert.That(texts, Does.Contain("picked at random"));
            Option(0).onClick.Invoke();
            Assert.That(FindNamed(_f.App.ModalLayer, "QuestGive:" + mythics[0]), Is.Null, "no picker");
            var kept = mythics.Where(id => S.Run.Active.Skills.Contains(id)).ToList();
            Assert.That(kept.Count, Is.EqualTo(1), "exactly one Mythic skill was taken");
            string taken = mythics.First(id => !kept.Contains(id));
            Assert.That(RunSystem.QuestProgressOf(S, Q).Node, Is.EqualTo("m23"));
            screen.Rebuild();
            string sheet = Texts(FindNamed(_f.App.ModalLayer, "Modal:Outcome"));
            Assert.That(sheet, Does.Contain("Lost skill: " + C.Skill(taken).Name));
        }

        [Test]
        public void LostBattle_IsTheNormalDefeat_WithTheReviveOffer()
        {
            QuestOnScreen("m03", run => run.WormHp = 1); // one hit point against a hard battle
            Option(0).onClick.Invoke(); // Fight
            Assert.That(S.Run.Active, Is.Not.Null);
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Defeated), "a lost quest battle is a defeat");
            Assert.That(RunSystem.Decision(C, S.Run.Active), Is.EqualTo(RunDecision.Revive));
            Assert.That(RunSystem.QuestProgressOf(S, Q).Node, Is.EqualTo("m03"), "the story waits");
        }

        [Test]
        public void Boon_ShowsTheForeverBonus_AndTheResultSheetSaysSo()
        {
            var screen = QuestOnScreen("m19");
            string texts = Texts(FindNamed(_f.App.ModalLayer, "Modal:Quest"));
            Assert.That(texts, Does.Contain("+1 coin per gain, forever"));
            Assert.That(texts, Does.Contain("Starglint: +1 → +2"));
            Option(0).onClick.Invoke();
            screen.Rebuild();
            Assert.That(screen.OutcomeOpen, Is.True);
            string sheet = Texts(FindNamed(_f.App.ModalLayer, "Modal:Outcome"));
            Assert.That(sheet, Does.Contain("STORY RESULT"));
            Assert.That(sheet, Does.Contain("Starglint: +2 coins on every coin gain, forever"));
            Assert.That(sheet, Does.Contain("The story moves on: chapter 20"));
        }

        [Test]
        public void EpilogueScene_IsOneFriendlyOption_WithItsReward()
        {
            QuestOnScreen(null, null, epilogue: true);
            var modal = FindNamed(_f.App.ModalLayer, "Modal:Quest");
            string texts = Texts(modal);
            var ep = C.RunQuest(Q).Epilogues[0];
            Assert.That(texts, Does.Contain(ep.Title));
            Assert.That(texts, Does.Contain(QuestView.EpilogueReward(ep)));
            Assert.That(texts, Does.Contain("Story complete"));
            Assert.That(Option(1), Is.Null, "a single option");
            long coins = S.Run.Active.Coins;
            Option(0).onClick.Invoke();
            Assert.That(S.Run.Active.Coins, Is.EqualTo(coins + (long)ep.Amount + 3), "coins + Starglint");
        }

        [Test]
        public void CoinBonus_ReadsAsItsOwnPopup_AndResultLine()
        {
            var l = new OutcomeLine(OutcomeKind.CoinBonus, "Starglint", 3, true);
            Assert.That(RunPresentation.FromLine(l, C).Value.Text, Is.EqualTo("+3 Starglint"));
            Assert.That(RunScreen.OutcomeLineText(l, C), Is.EqualTo("+3 coins from Starglint"));
            Assert.That(RunScreen.OutcomeIconId(l), Is.EqualTo("coin"));
            Assert.That(RunPresentation.IsSheetSource("quest"), Is.True);
        }

        [Test]
        public void QuestTile_HasAMarker_AndALabel()
        {
            Assert.That(IsoArt.MarkerOf(TileCategory.Quest), Is.EqualTo("quest"));
            Assert.That(IsoArt.Marker("quest"), Is.Not.Null);
            Assert.That(Theme.TileLabel(TileCategory.Quest), Is.EqualTo("VISITOR"));
            Assert.That(ScreenArt.Character(C.RunQuest(Q).Portrait), Is.Not.Null);
        }

        [Test]
        public void QuestView_CostTexts_AreRoundAndClear()
        {
            Assert.That(QuestView.CostText(new QuestCostDef(QuestCostKind.Coins, 150)), Is.EqualTo("−150 coins"));
            Assert.That(QuestView.CostText(new QuestCostDef(QuestCostKind.CurrentHpPct, 50)), Is.EqualTo("−50% current HP"));
            Assert.That(QuestView.CostText(new QuestCostDef(QuestCostKind.LegendarySkill, 1)), Is.EqualTo("A random Legendary skill"));
            Assert.That(QuestView.CostText(new QuestCostDef(QuestCostKind.MythicSkill, 1)), Is.EqualTo("A random Mythic skill"));
            Assert.That(QuestView.CostText(new QuestCostDef(QuestCostKind.AllRunSkills)), Is.EqualTo("−ALL run skills"));
            Assert.That(QuestView.Progress(C.RunQuest(Q), 7), Is.EqualTo("Chapter 7"));
            Assert.That(QuestView.Reached(C.RunQuest(Q), 7), Is.EqualTo("Reached chapter 7"));
            Assert.That(QuestView.Progress(C.RunQuest(Q), 31), Is.EqualTo("Story complete"));
        }

        [Test]
        public void Journal_IsUnderMore_AndShowsTheBonus_ChapterReached_AndTheStorySoFar()
        {
            S.Flags.Remove(Features.DevUnlockAllFlag);
            Assert.That(HubLayout.MoreScreens(S, C).Any(e => e.Id == "journal"), Is.False, "hidden until the first meeting");
            S.RunQuests.Encounters = 1;
            Assert.That(HubLayout.MoreScreens(S, C).Any(e => e.Id == "journal"), Is.True, "no feature gate: after the first meeting, whatever the level");
            var p = new QuestProgress { Node = "m09", CoinBonus = 1 };
            p.Journal.Add(new QuestJournalEntry { Chapter = 1, NodeId = "m01", Line = "I opened Mothwick’s parcel." });
            p.Journal.Add(new QuestJournalEntry { Chapter = 8, NodeId = "m08", Line = "Pim gave me Starglint." });
            S.RunQuests.Quests[Q] = p;
            _f.App.Router.Push(new JournalScreen());
            var root = _f.App.Router.Current.Root;
            string texts = Texts(root);
            Assert.That(texts, Does.Contain("Starglint: +1 coin on every coin gain in runs"));
            Assert.That(texts, Does.Contain("Mothwick"));
            Assert.That(texts, Does.Contain("Reached chapter 9"));
            Assert.That(texts, Does.Not.Contain("/ 30"), "no total");
            Assert.That(texts, Does.Not.Contain("left"), "no chapters left");
            Assert.That(texts, Does.Contain("Next time: A Letter for Mothwick (a choice)"));
            Assert.That(texts, Does.Contain("Chapter 8 · Starglint"));
            Assert.That(texts, Does.Contain("Pim gave me Starglint."));
            Assert.That(FindNamed(root, "Portrait").GetComponent<Image>().sprite, Is.Not.Null);
        }

        [Test]
        public void Journal_BeforeTheFirstMeeting_SaysNoOneCameYet()
        {
            _f.App.Router.Push(new JournalScreen());
            string texts = Texts(_f.App.Router.Current.Root);
            Assert.That(texts, Does.Contain("No visitors yet"));
            Assert.That(texts, Does.Contain("Starglint: not earned yet"));
        }
    }
}
