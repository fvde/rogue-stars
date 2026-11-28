using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Screens pass: the rest scene with the Train picker (owner rule: the player chooses the skill), events on the scene and
    /// vendor layouts, the shop's SALE price, the run result's crest and reward strip, the home's planet and START, the
    /// option texts (gains green, losses red) and the art keys with their drawn fallbacks.
    /// </summary>
    public sealed class ScreensPassTests
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

        private static Button Btn(Transform root, string name) => root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        private static string Texts(Transform root) => string.Join(" | ", root.GetComponentsInChildren<Text>(true).Where(t => t.gameObject.activeInHierarchy).Select(t => t.text));

        /// <summary>The texts without rich-text colour tags.</summary>
        private static string Plain(Transform root) => System.Text.RegularExpressions.Regex.Replace(Texts(root), "<.*?>", "");

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

        private List<string> Commons(int n)
        {
            var list = new List<string>();
            foreach (var id in RunDraft.SortedSkillIds(C))
            {
                var s = C.Skills[id];
                if (list.Count >= n) break;
                if (s.Source != SkillSource.Run || s.Signature || !s.Draftable || s.Rarity != Rarity.Common || RunDraft.HasUpgrade(C, id) || !SkillLeveling.Grows(C, s)) continue;
                list.Add(id);
            }
            return list;
        }

        private RunScreen OpenEvent(string eventId)
        {
            var run = S.Run.Active;
            run.Phase = RunPhase.Event; run.EventId = eventId; run.EventTile = -1;
            _f.App.Router.Push(new RunScreen());
            return (RunScreen)_f.App.Router.Current;
        }

        [Test]
        public void Rest_ShowsTheSceneAndTwoTiles_TrainOpensThePicker_AndThePickTrainsThatSkill()
        {
            StartRun();
            var two = Commons(2);
            S.Run.Active.Skills.Clear();
            S.Run.Active.Skills.AddRange(two);
            OpenEvent("campfire");
            var layer = _f.App.ModalLayer;
            Assert.That(FindNamed(layer, "Stage"), Is.Not.Null, "the stage on top");
            Assert.That(FindNamed(layer, "Prop"), Is.Not.Null, "the campfire at the right");
            Assert.That(Texts(layer), Does.Contain("A Moment to Rest"));
            Assert.That(Texts(layer), Does.Contain("Improve 1 existing skill"));
            Assert.That(Btn(layer, "Button:Rest"), Is.Not.Null);
            Btn(layer, "Option:Train").onClick.Invoke();
            var picker = FindNamed(layer, "Modal:Train");
            Assert.That(picker, Is.Not.Null, "Train opens the picker");
            Assert.That(FindNamed(picker, "Train:" + two[0]), Is.Not.Null);
            Assert.That(FindNamed(picker, "Train:" + two[1]), Is.Not.Null);
            Assert.That(Texts(picker), Does.Contain("Lv 2"), "C3: Name Lv 2, the upgrade inline");
            Assert.That(Texts(picker), Does.Not.Contain("LEVEL 1 → 2"));
            Assert.That(FindNamed(picker, "TrainList"), Is.Not.Null, "C1: the cards scroll between the ribbon and the footer");
            Btn(picker, "Train:" + two[1]).onClick.Invoke();
            Assert.That(RunDraft.Level(S.Run.Active, two[1]), Is.EqualTo(2), "the chosen skill trained");
            Assert.That(RunDraft.Level(S.Run.Active, two[0]), Is.EqualTo(1));
        }

        [Test]
        public void Rest_WithNothingToTrain_SaysSo_AndTrainPays()
        {
            StartRun();
            S.Run.Active.Skills.Clear();
            OpenEvent("campfire");
            Assert.That(Texts(_f.App.ModalLayer), Does.Contain("Nothing to train"));
            long coins = S.Run.Active.Coins;
            Btn(_f.App.ModalLayer, "Option:Train").onClick.Invoke();
            Assert.That(S.Run.Active.Coins, Is.EqualTo(coins + C.RunTuning.DraftEmptyCoins));
        }

        [Test]
        public void EventLayouts_MapEveryEvent()
        {
            foreach (var kv in C.RunEvents)
            {
                string id = kv.Key;
                var ev = kv.Value;
                var look = RunScenes.Look(id, ev);
                Assert.That(look, Is.Not.Null, id);
                // biome / new shared events: their content art id (drawn placeholder until imported); ours: the imported set
                string contentArt = ev.Art != null ? ev.Art.Substring(ev.Art.IndexOf(':') + 1) : null;
                if (look.Kind == EventLayoutKind.Vendor)
                {
                    Assert.That(look.Npc || System.Array.IndexOf(ScreenArt.CharacterIds, look.Art) >= 0, Is.True, id);
                    Assert.That(System.Array.IndexOf(ScreenArt.CharacterIds, look.Art) >= 0 || look.Art == contentArt, Is.True, id);
                    Assert.That(ev.Options.Count, Is.EqualTo(2), id + ": a vendor offers two cards");
                }
                else Assert.That(System.Array.IndexOf(look.Npc ? ScreenArt.CharacterIds : ScreenArt.PropIds, look.Art) >= 0 || look.Art == contentArt, Is.True, id);
                Assert.That(RunScenes.StageArt(look), Is.Not.Null, id + ": art or its placeholder");
            }
            Assert.That(RunScenes.Look("smuggler").Kind, Is.EqualTo(EventLayoutKind.Vendor));
            Assert.That(RunScenes.Look("abandoned_cache").Kind, Is.EqualTo(EventLayoutKind.Scene));
            var smith = RunScenes.Look("lava_salamander_smith", C.RunEvents["lava_salamander_smith"]);
            Assert.That(smith.Npc && smith.Art == "salamander_smith" && smith.Kind == EventLayoutKind.Scene, Is.True, "an NPC with 3 options stands on the stage");
            var geyser = RunScenes.Look("lava_ore_geyser", C.RunEvents["lava_ore_geyser"]);
            Assert.That(!geyser.Npc && geyser.Art == "ore_geyser", Is.True);
        }

        [Test]
        public void RiteOffering_OpensTheSkillPicker_AndGivesUpThePickedSkill()
        {
            StartRun();
            var run = S.Run.Active;
            var ice = RunDraft.SortedSkillIds(C).Where(id => C.Skills[id].Family == SkillFamily.Ice && !C.Skills[id].Signature
                && C.Skills[id].Source == SkillSource.Run && C.Skills[id].Draftable).Take(2).ToList();
            var sig = RunDraft.SortedSkillIds(C).First(id => C.Skills[id].Family == SkillFamily.Ice && C.Skills[id].Signature);
            run.Skills.Clear();
            run.Skills.AddRange(ice);
            run.Skills.Add(sig);
            run.SignatureIds.Clear();
            run.SignatureIds.Add(sig);
            OpenEvent("rite_lava");
            var layer = _f.App.ModalLayer;
            Assert.That(Texts(layer), Does.Contain("Quench the Crust"));
            Assert.That(Plain(layer), Does.Contain("Give up 1 Ice skill"));
            Assert.That(Plain(layer), Does.Contain("Boss −20% ATK"));
            var offer = layer.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name.StartsWith("Button:Offer an Ice skill"));
            Assert.That(offer, Is.Not.Null, "the Offering shows while an Ice skill is owned");
            offer.onClick.Invoke();
            var picker = FindNamed(layer, "Modal:Offering");
            Assert.That(picker, Is.Not.Null, "the Offering opens the skill picker");
            Assert.That(FindNamed(picker, "Offer:" + ice[0]), Is.Not.Null);
            Assert.That(FindNamed(picker, "Offer:" + ice[1]), Is.Not.Null);
            Assert.That(FindNamed(picker, "Offer:" + sig), Is.Null, "a signature can't be offered");
            Assert.That(Texts(picker), Does.Contain("OFFERING"));
            Btn(picker, "Offer:" + ice[1]).onClick.Invoke();
            run = S.Run.Active;
            Assert.That(run.Skills, Does.Not.Contain(ice[1]), "the picked skill is gone");
            Assert.That(run.Skills, Does.Contain(ice[0]));
            Assert.That(run.Skills, Does.Contain(sig));
            Assert.That(run.BossAtkPct, Is.EqualTo(-20));
        }

        [Test]
        public void RiteOffering_IsHidden_WithoutASkillOfItsSynergy()
        {
            StartRun();
            S.Run.Active.Skills.RemoveAll(id => C.Skill(id)?.Family == SkillFamily.Ice);
            OpenEvent("rite_lava");
            var layer = _f.App.ModalLayer;
            Assert.That(layer.GetComponentsInChildren<Button>(true).Any(b => b.name.StartsWith("Button:Offer an Ice skill")), Is.False);
            Assert.That(Btn(layer, "Button:Leave"), Is.Not.Null);
        }

        [Test]
        public void VendorEvent_ShowsBlessingAndCurseCards_WithSign()
        {
            StartRun();
            OpenEvent("smuggler");
            var layer = _f.App.ModalLayer;
            Assert.That(FindNamed(layer, "Npc:black_marketeer"), Is.Not.Null);
            Assert.That(FindNamed(layer, "BubbleText"), Is.Not.Null);
            var texts = Texts(layer);
            Assert.That(texts, Does.Contain("Learn a Legendary+ skill"));
            Assert.That(texts, Does.Contain("Sign"));
            Assert.That(texts, Does.Contain("Leave"));
            Assert.That(layer.GetComponentsInChildren<Button>(true).Count(b => b.name.StartsWith("Button:")), Is.EqualTo(2));
        }

        [Test]
        public void OptionTexts_ColourGainsGreen_AndLossesRed()
        {
            var smuggler = C.RunEvents["smuggler"].Options[0];
            var lines = RunScenes.EffectLines(smuggler, C);
            Assert.That(lines.Exists(l => !l.Key && l.Value.Contains("max HP")), Is.True);
            Assert.That(lines.Exists(l => l.Key && l.Value.Contains("skill")), Is.True);
            string green = ColorUtility.ToHtmlStringRGB(RunScenes.Gain), red = ColorUtility.ToHtmlStringRGB(RunScenes.Loss);
            var rich = RunScenes.Colored(lines);
            Assert.That(rich, Does.Contain("<color=#" + red + ">10%</color>"));
            Assert.That(rich, Does.Not.Contain("−"), "G8: no minus after a verb that already means loss");
            var cache = RunScenes.OutcomeText(C.RunEvents["abandoned_cache"].Options[0], C);
            Assert.That(cache, Does.Contain("60%:"));
            Assert.That(cache, Does.Contain("reward chest"));
            Assert.That(cache, Does.Contain("<color=#" + green + ">"));
            Assert.That(RunScenes.OutcomeText(C.RunEvents["abandoned_cache"].Options[1], C), Is.EqualTo("Nothing happens"));
        }

        [Test]
        public void Shop_ShowsSaleWithTheListPrice_AndBuysAtTheSalePrice()
        {
            var run = StartRun();
            run.Shop.Clear();
            var id = "gen_power";
            run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Skill, SkillId = id, Price = 20, ListPrice = 40 });
            run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Heal, Value = 35, Price = 30 });
            run.Coins = 25;
            run.Phase = RunPhase.Shop;
            _f.App.Router.Push(new RunScreen());
            var layer = _f.App.ModalLayer;
            Assert.That(FindNamed(layer, "Sale"), Is.Not.Null, "SALE!");
            Assert.That(FindNamed(layer, "Was").GetComponent<Text>().text, Is.EqualTo("40"));
            Assert.That(FindNamed(layer, "Npc:shopkeeper"), Is.Not.Null);
            Assert.That(Btn(layer, "Button:Leave"), Is.Not.Null, "Exit");
            Btn(layer, "Button:20 coins").onClick.Invoke();
            Assert.That(S.Run.Active.Coins, Is.EqualTo(5));
            Assert.That(Texts(Btn(layer, "Button:20 coins").transform), Does.Contain("Sold"));
        }

        [Test]
        public void RunResult_HasTheCrestRibbonPennantAndRewardStrip()
        {
            var level = C.MainLevel(1);
            S.Run.LastSummary = new RunSummary { RunId = "x", LevelId = level.Id, Won = true, Rolls = 30, LongestRolls = 34, Level = 9 };
            S.Run.LastSummary.Granted.Add(Reward.Cur(Currencies.Diamonds, 30));
            _f.App.Router.Push(new RunResultScreen());
            var root = _f.App.Router.Current.Root;
            Assert.That(FindNamed(root, "Crest"), Is.Not.Null);
            Assert.That(FindNamed(root, "Banner").GetComponent<Text>().text, Is.EqualTo("VICTORY!"));
            Assert.That(FindNamed(root, "Rolls").GetComponent<Text>().text, Is.EqualTo("30 Rolls"));
            Assert.That(FindNamed(root, "Longest").GetComponent<Text>().text, Does.Contain("34 Rolls"));
            Assert.That(FindNamed(root, "RewardStrip"), Is.Not.Null);
            Assert.That(FindNamed(root, "Reward"), Is.Not.Null);
            Assert.That(Btn(root, "Button:Portals"), Is.Not.Null, "Continue");
            S.Run.LastSummary.Won = false;
            _f.App.Router.Pop();
            _f.App.Router.Push(new RunResultScreen());
            Assert.That(FindNamed(_f.App.Router.Current.Root, "Banner").GetComponent<Text>().text, Is.EqualTo("DEFEATED"));
        }

        [Test]
        public void Home_ShowsThePlanetByStage_AndStart_WithLongestSurvived()
        {
            Assert.That(HubScreen.PlanetStage(S), Is.EqualTo(1));
            S.Counters[Requirements.MainLevelCounter] = 60;
            Assert.That(HubScreen.PlanetStage(S), Is.EqualTo(4));
            S.Counters[Requirements.MainLevelCounter] = 0;
            var next = RunSystem.NextMainLevel(S, C);
            S.Run.LongestRolls.Add(new LevelRolls { LevelId = next.Id, Rolls = 23 });
            _f.App.Router.Reset(new HubScreen());
            var root = _f.App.Router.Current.Root;
            Assert.That(FindNamed(root, "Planet"), Is.Not.Null);
            Assert.That(Texts(root), Does.Contain("START"));
            Assert.That(FindNamed(root, "Longest").GetComponent<Text>().text, Does.Contain("23"));
        }

        [Test]
        public void ArtKeys_FallBackToDrawnPlaceholders()
        {
            foreach (var id in ScreenArt.CharacterIds) Assert.That(ScreenArt.Character(id), Is.Not.Null, id);
            foreach (var id in ScreenArt.PropIds) Assert.That(ScreenArt.Prop(id), Is.Not.Null, id);
            foreach (var id in ScreenArt.IconIds) Assert.That(ScreenArt.Icon(id), Is.Not.Null, id);
            foreach (var id in ScreenArt.ResultIds) Assert.That(ScreenArt.Result(id), Is.Not.Null, id);
            for (int st = 1; st <= 5; st++) Assert.That(ScreenArt.Planet(st), Is.Not.Null);
            Assert.That(ArtCatalog.RunProps, Is.EqualTo("Art/Run/Props"));
            Assert.That(ArtCatalog.HubPlanet, Is.EqualTo("Art/Hub/Planet"));
        }

        [Test]
        public void MythicChest_UsesItsOwnImportedArt_NotTheWoodenMarker()
        {
            ArtCatalog.Loader = null; // the real Resources loader
            var mythic = ArtCatalog.RunProp("chest_mythic");
            Assert.That(mythic, Is.Not.Null, "Assets/Art/Resources/Art/Run/Props/chest_mythic.png is a sprite");
            Assert.That(RunScreen.ChestSprite("chest_mythic"), Is.SameAs(mythic));
            Assert.That(RunScreen.ChestSprite("chest_mythic"), Is.Not.SameAs(ArtCatalog.SliceMarker("chest")), "not the board's wooden chest");
            Assert.That(ScreenArt.Prop("chest_mythic"), Is.SameAs(mythic));
        }
    }
}
