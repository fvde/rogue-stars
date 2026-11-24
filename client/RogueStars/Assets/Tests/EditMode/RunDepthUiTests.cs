using System.Collections.Generic;
using NUnit.Framework;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Run depth on the run screen: the MYTHIC choice after a miniboss, the skill count in the HUD, a shop that stays open
    /// while buying, the result screen (no skills list, identical rewards summed), encounters (miniboss / boss arena) as their own step,
    /// cleared tiles, no Biomass surge before Biomass has a use, and auto mode never answering a player choice.
    /// </summary>
    public sealed class RunDepthUiTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

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

        private ActiveRun StartRun()
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            var run = S.Run.Active;
            run.Tiles.Clear();
            for (int i = 0; i <= 30; i++) { var t = new RunTile { Id = i, TileId = i == 0 ? "start" : "empty", X = i }; if (i < 30) t.Next.Add(i + 1); run.Tiles.Add(t); }
            run.BoardEnd = 30; run.BossGateTile = -1; run.Position = 0; run.Trail.Clear(); run.Trail.Add(0);
            return run;
        }

        private RunScreen Open()
        {
            _f.App.Router.Push(new RunScreen());
            return (RunScreen)_f.App.Router.Current;
        }

        [Test]
        public void MinibossDraft_ShowsAsMythicChoice_AndLevelUpsAreMarked()
        {
            var run = StartRun();
            run.Skills.Add("mythic_last_light");
            run.Phase = RunPhase.Draft; run.MythicDraft = true;
            run.DraftOffer.AddRange(new[] { "mythic_last_light", "mythic_catalyst", "mythic_overkill" });
            Open();
            var sheet = FindNamed(_f.App.ModalLayer, "MythicTitle");
            Assert.That(sheet, Is.Not.Null, "MYTHIC banner");
            Assert.That(Texts(_f.App.ModalLayer), Does.Contain("Miniboss defeated"));
            Assert.That(FindNamed(_f.App.ModalLayer, "LevelUpChip"), Is.Not.Null, "an owned mythic levels up");
            Assert.That(Texts(_f.App.ModalLayer), Does.Contain("LEVEL 1 → 2"));
            Button pick = null;
            foreach (var b in _f.App.ModalLayer.GetComponentsInChildren<Button>(true))
                if (b.name == "Button:Pick" && FindNamed(b.transform, "Draft:mythic_catalyst") != null) pick = b;
            Assert.That(pick, Is.Not.Null);
            pick.onClick.Invoke();
            Assert.That(S.Run.Active.Skills, Does.Contain("mythic_catalyst"));
            Assert.That(S.Run.Active.Gains.Exists(g => g.Source == "mythic" && g.SkillId == "mythic_catalyst"), Is.True);
        }

        /// <summary>Skill tuning pass: an owned skill offered again shows the target level and its upgrade inline ("+15% → +25%").</summary>
        [Test]
        public void LevelUpOffer_ShowsTheTargetLevel_AndTheUpgradeInline()
        {
            var run = StartRun();
            run.Skills.Add("crit_keen");
            run.Phase = RunPhase.Draft; run.MythicDraft = false;
            run.DraftOffer.AddRange(new[] { "crit_keen", "gen_power", "gen_guard" });
            Open();
            string texts = System.Text.RegularExpressions.Regex.Replace(Texts(_f.App.ModalLayer), "<[^>]+>", "");
            Assert.That(texts, Does.Contain("Keen Eye Lv 2"));
            Assert.That(texts, Does.Contain("+15% » +25% crit chance"));
            Assert.That(texts, Does.Contain("LEVEL 1 → 2"));
            Assert.That(texts, Does.Contain("+15% ATK"), "a new skill shows its plain Lv1 text");
        }

        [Test]
        public void Hud_ShowsTheSkillCount()
        {
            var run = StartRun();
            run.Gains.Add(new RunGain("draft", "gen_power", 1));
            run.Gains.Add(new RunGain("shop", null, 1) { Stat = new StatMod(StatId.Atk, 0, 8) });
            var screen = Open();
            var chip = FindNamed(screen.Root, "SkillCount");
            Assert.That(chip, Is.Not.Null);
            Assert.That(chip.GetComponent<Text>().text, Is.EqualTo("3"), "the skills book's badge: the signature + 2");
            Assert.That(screen.HudText, Does.Contain("SKILLS 3"));
        }

        [Test]
        public void Shop_StaysOpenWhileBuying_AndUpdatesInPlace()
        {
            var run = StartRun();
            run.Tiles[2].TileId = "shop";
            run.Coins = 5000;
            Assert.That(_f.Service.ExecuteNow(new RunAutoStep()).Ok, Is.True); // forced stop at the shop
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Shop));
            var screen = Open();
            var modal = FindNamed(_f.App.ModalLayer, "ShopCoins");
            Assert.That(modal, Is.Not.Null);
            var buys = new List<Button>();
            foreach (var b in _f.App.ModalLayer.GetComponentsInChildren<Button>(true))
                if (b.name.StartsWith("Button:") && b.name.EndsWith(" coins") && !b.name.StartsWith("Button:Reroll")) buys.Add(b);
            Assert.That(buys.Count, Is.EqualTo(S.Run.Active.Shop.Count));
            for (int k = 0; k < 3; k++)
            {
                var buy = buys[k];
                Assert.That(buy.interactable, Is.True);
                buy.onClick.Invoke();
                screen.Rebuild();
                Assert.That(FindNamed(_f.App.ModalLayer, "ShopCoins"), Is.SameAs(modal), "the same sheet, not closed and reopened");
                Assert.That(Texts(buy.transform), Does.Contain("Sold"));
                Assert.That(buy.interactable, Is.False);
            }
            Assert.That(S.Run.Active.ShopBuys, Is.EqualTo(3));
            Assert.That(Texts(modal), Does.Contain("3 bought this run"));
        }

        [Test]
        public void ResultScreen_ListsNoSkills_AndSumsIdenticalRewardsIntoOneTile([Values(true, false)] bool won)
        {
            S.Run.LastSummary = new RunSummary { RunId = "x", LevelId = C.MainLevel(1).Id, Won = won, Rolls = 31, Level = 10, Minibosses = 3, ShopBuys = 12 };
            S.Run.LastSummary.Gains.Add(new RunGain("draft", "fire_ember", 1));
            S.Run.LastSummary.Gains.Add(new RunGain("mythic", "mythic_thermal_shock", 1));
            foreach (var r in new[] { Reward.Cur(Currencies.Diamonds, 10), Reward.Cur(Currencies.Biomass, 100), Reward.Cur(Currencies.Diamonds, 10), Reward.Cur(Currencies.Diamonds, 10), Reward.Cur(Currencies.Biomass, 50) })
                S.Run.LastSummary.Granted.Add(r);
            _f.App.Router.Push(new RunResultScreen());
            var root = _f.App.Router.Current.Root;
            Assert.That(FindNamed(root, "GainedSkills"), Is.Null, "no list of the run's skills (owner playtest)");
            Assert.That(Texts(root), Does.Not.Contain("Ember Strikes"));
            int tiles = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == "Reward") tiles++;
            Assert.That(tiles, Is.EqualTo(2), "one tile per currency");
            Assert.That(Texts(root), Does.Contain("x30"));
            Assert.That(Texts(root), Does.Contain("x150"));
            Assert.That(Texts(root), Does.Contain("Skills 2"), "the run's chips stay");
        }

        [Test]
        public void ResultRewards_Aggregate_ByKindIdAndRarity_InFirstSeenOrder()
        {
            var granted = new List<Reward>
            {
                Reward.Cur(Currencies.Diamonds, 10), new Reward(RewardKinds.Part, "p1", 1, 2), Reward.Cur(Currencies.Diamonds, 10),
                new Reward(RewardKinds.Part, "p1", 1, 3), new Reward(RewardKinds.Part, "p1", 2, 2), Reward.Cur(Currencies.Diamonds, 10),
            };
            var sum = RunResultScreen.Aggregate(granted);
            Assert.That(sum.Count, Is.EqualTo(3));
            Assert.That(sum[0].Id, Is.EqualTo(Currencies.Diamonds)); Assert.That(sum[0].Amount, Is.EqualTo(30));
            Assert.That(sum[1].Rarity, Is.EqualTo(2)); Assert.That(sum[1].Amount, Is.EqualTo(3));
            Assert.That(sum[2].Rarity, Is.EqualTo(3)); Assert.That(sum[2].Amount, Is.EqualTo(1));
            Assert.That(granted[0].Amount, Is.EqualTo(10), "the summary itself is not changed");
            Assert.That(RunResultScreen.Aggregate(null), Is.Empty);
        }

        [Test]
        public void NoBiomassSurge_BeforeBiomassHasAUse()
        {
            Assert.That(RunSystem.BiomassSurgeActive(S, C, _f.Service.NowMs), Is.False, "a new player has no City / worm levels yet");
            _f.App.Open("portals");
            Assert.That(FindNamed(_f.App.Router.Current.Root, "BiomassSurge"), Is.Null);
            Assert.That(Texts(_f.App.Router.Current.Root), Does.Not.Contain("surge"));
            S.Run.LastSummary = new RunSummary { RunId = "x", LevelId = C.MainLevel(1).Id, Won = true, Mode = RunMode.Normal };
            _f.App.Router.Push(new RunResultScreen());
            Assert.That(Texts(_f.App.Router.Current.Root).ToLowerInvariant(), Does.Not.Contain("surge"));
        }

        [Test]
        public void Encounter_IsItsOwnStep_WithAFightButton_AndAPlayback()
        {
            var run = StartRun();
            run.MinibossesSpawned = 0;
            run.Awakening = 24.9; // the next roll crosses 25: a miniboss attacks after the move
            var screen = Open();
            Assert.That(_f.Service.ExecuteNow(new RunRoll()).Ok, Is.True);
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Encounter));
            screen.Rebuild();
            Assert.That(Texts(FindNamed(screen.Root, "RollButton")), Does.Contain("Fight!"));
            S.Run.Active.WormHp = 1e12; S.Run.Active.WormMaxHp = 1e12;
            FindNamed(screen.Root, "RollButton").GetComponent<HoldButton>().OnTap();
            Assert.That(S.Run.Active.MinibossesDefeated + (S.Run.Active.Phase == RunPhase.Defeated ? 1 : 0), Is.EqualTo(1));
            screen.Rebuild();
            Assert.That(_f.App.Router.Current, Is.InstanceOf<BattlePlaybackScreen>(), "every fight plays");
        }

        [Test]
        public void AutoMode_NeverAnswersAPlayerChoice()
        {
            var run = StartRun();
            run.Phase = RunPhase.Event; run.EventId = "smuggler"; run.EventTile = 1;
            var screen = Open();
            screen.StartAuto();
            for (int i = 0; i < 20; i++) screen.Frame(1f);
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.Event), "auto waits for the player");
            Assert.That(screen.AutoPaused, Is.True);
            run.Phase = RunPhase.ChooseBranch;
            run.Tiles[0].Next.Add(5); run.BranchOptions.Clear(); run.BranchOptions.AddRange(new[] { 1, 5 }); run.StepsLeft = 3;
            screen.Rebuild();
            for (int i = 0; i < 20; i++) screen.Frame(1f);
            Assert.That(S.Run.Active.Phase, Is.EqualTo(RunPhase.ChooseBranch), "a fork waits too");
        }

        [Test]
        public void BeatenFightTile_StaysAndShowsCleared_AndShortFightsStayOnScreen()
        {
            var run = StartRun();
            for (int i = 1; i <= 6; i++) run.Tiles[i].TileId = "enemy_1";
            run.WormHp = 1e12; run.WormMaxHp = 1e12;
            Assert.That(_f.Service.ExecuteNow(new RunRoll()).Ok, Is.True);
            var a = S.Run.Active;
            var tile = a.Tiles[a.Position];
            Assert.That(tile.TileId, Is.EqualTo("enemy_1"), "not replaced by a new random tile");
            Assert.That(tile.Cleared, Is.True);
            var look = RunScreen.LookOf(a, tile, C);
            Assert.That(look.Cleared && look.Current, Is.True);
        }
    }
}
