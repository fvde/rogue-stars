using System.Collections.Generic;
using System.Linq;
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
    /// Owner playtest: the skill "?" explains every mechanic the skill touches (the rules' glossary, with real numbers), and the
    /// run's skills book opens from every decision screen (draft, Mythic, signature, shop, events, rest, chests, results) on top
    /// of it, returning to the same decision without sending anything.
    /// </summary>
    public sealed class SkillHelpTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; RunFx.Instant = true; }

        [TearDown]
        public void TearDown()
        {
            SkillTooltip.Close();
            RunScreen.InstantOutcomes = false; RunFx.Instant = false; RunScreen.ForceChestIntro = false;
            _f.Dispose();
        }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static Button Btn(Transform root, string name) => root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        private static string Texts(Transform root) => string.Join("|", root.GetComponentsInChildren<Text>(true).Select(t => t.text));

        private ActiveRun StartRun()
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            return S.Run.Active;
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

        /// <summary>The modal-layer overlay a node lives in.</summary>
        private Transform OverlayOf(Transform t)
        {
            Assert.That(t, Is.Not.Null);
            while (t.parent != null && t.parent != _f.App.ModalLayer) t = t.parent;
            return t;
        }

        private RunScreen Open()
        {
            _f.App.Router.Push(new RunScreen());
            return (RunScreen)_f.App.Router.Current;
        }

        // ---- the "?" panel ---------------------------------------------------------------------------------------

        [TestCase("bleed_frenzy", new[] { "bleed", "rage" }, "Bleed synergy")]
        [TestCase("ice_deep_freeze", new[] { "chill", "freeze" }, "Ice synergy")]
        [TestCase("light_spark", new[] { "arc", "shock" }, "Lightning synergy")]
        public void QuestionMark_ExplainsEveryMechanic_AndTheSynergy(string id, string[] mechanics, string synergy)
        {
            var sk = C.Skill(id);
            var tile = SkillTileView.Build(_f.Canvas, C, id, 200);
            var panel = SkillTooltip.Show(tile, C, SkillTip.Of(id, 1));
            Assert.That(panel, Is.Not.Null);
            Assert.That(FindNamed(panel, "TipTitle").GetComponent<Text>().text, Is.EqualTo(SkillText.Title(C, sk, 1)));
            Assert.That(SkillText.StripMarks(FindNamed(panel, "TipText").GetComponent<Text>().text), Does.Contain(SkillText.Full(C, sk, 1).Split(' ')[0]));
            Assert.That(FindNamed(panel, "TipUpgrade"), Is.Not.Null, "the next level");
            foreach (var m in mechanics)
            {
                var block = FindNamed(panel, "TipMechanic:" + m);
                Assert.That(block, Is.Not.Null, id + " explains " + m);
                Assert.That(block.GetComponent<Text>().text, Does.Contain(SkillGlossary.Entry(C, m).Text));
            }
            Assert.That(FindNamed(panel, "TipSynergy").GetComponent<Text>().text, Does.Contain(synergy));
            Assert.That(Texts(panel), Does.Not.Contain("SYNERGY"), "no synergy tag chip");
            // the bleed block has the content's real numbers
            if (id == "bleed_frenzy")
                Assert.That(FindNamed(panel, "TipMechanic:bleed").GetComponent<Text>().text, Does.Contain("up to " + C.Combat.Statuses[StatusId.Bleed].MaxStacks));
        }

        [Test]
        public void QuestionMark_OnADraftCard_ShowsTheMechanics()
        {
            var run = StartRun();
            run.Phase = RunPhase.Draft;
            run.DraftOffer.Clear(); run.DraftOffer.AddRange(new[] { "bleed_serrated", "ice_brittle", "fire_ember" });
            Open();
            var card = FindNamed(_f.App.ModalLayer, "Draft:bleed_serrated");
            Btn(card, "InfoButton").onClick.Invoke();
            Assert.That(SkillTooltip.IsOpen, Is.True);
            Assert.That(FindNamed(SkillTooltip.Current, "TipMechanic:bleed"), Is.Not.Null);
        }

        [Test]
        public void GenericSkill_HasNoEmptyBlocks()
        {
            var lines = SkillTooltip.Synergy(C, SkillTip.Of("gen_power", 1));
            Assert.That(lines, Is.Empty);
            var panel = SkillTooltip.Show(SkillTileView.Build(_f.Canvas, C, "gen_power", 200), C, SkillTip.Of("gen_power", 1));
            Assert.That(FindNamed(panel, "TipSynergy"), Is.Null);
            Assert.That(FindNamed(panel, "TipRule"), Is.Null);
        }

        // ---- the skills book on every decision ------------------------------------------------------------------

        /// <summary>
        /// The decision's skills book exists, opens the book on top and closing it leaves the decision (and the run) untouched.
        /// </summary>
        private void AssertBookOpensOver(string decision, string what)
        {
            var layer = _f.App.ModalLayer;
            var modal = FindNamed(layer, decision);
            Assert.That(modal, Is.Not.Null, what + " is open");
            var book = Btn(modal, "SkillsBook");
            Assert.That(book, Is.Not.Null, what + " has the skills book");
            Assert.That(book.interactable, Is.True);
            var run = S.Run.Active;
            string key = RunScreen.DecisionKey(run);
            var skills = run.Skills.ToList();
            book.onClick.Invoke();
            var sheet = FindNamed(layer, "Modal:Skills");
            Assert.That(sheet, Is.Not.Null, what + ": the book opens");
            Assert.That(sheet.GetSiblingIndex(), Is.GreaterThan(modal.GetSiblingIndex()), what + ": on top of the decision");
            Assert.That(FindNamed(layer, decision), Is.SameAs(modal), what + ": the decision stays");
            Assert.That(modal.gameObject.activeInHierarchy, Is.True);
            Btn(sheet, "Button:Close").onClick.Invoke();
            Assert.That(FindNamed(layer, "Modal:Skills"), Is.Null, what + ": the book closes");
            Assert.That(FindNamed(layer, decision), Is.SameAs(modal), what + ": back to the same decision");
            Assert.That(RunScreen.DecisionKey(S.Run.Active), Is.EqualTo(key), what + ": nothing sent, nothing rerolled");
            Assert.That(S.Run.Active.Skills, Is.EqualTo(skills));
        }

        [Test]
        public void SkillsBook_OnTheLevelUpDraft()
        {
            var run = StartRun();
            run.Skills.AddRange(Commons(1));
            run.Phase = RunPhase.Draft;
            run.DraftOffer.Clear(); run.DraftOffer.AddRange(new[] { "bleed_serrated", "ice_brittle", "fire_ember" });
            Open();
            var offer = run.DraftOffer.ToList();
            AssertBookOpensOver("Modal:Choose a skill", "level-up draft");
            Assert.That(S.Run.Active.DraftOffer, Is.EqualTo(offer), "same offer");
        }

        [Test]
        public void SkillsBook_OnTheMythicChest_AndTheMythicDraft()
        {
            RunScreen.ForceChestIntro = true;
            var run = StartRun();
            run.Phase = RunPhase.Draft; run.MythicDraft = true;
            run.DraftOffer.Clear(); run.DraftOffer.AddRange(new[] { "mythic_last_light", "mythic_catalyst", "mythic_overkill" });
            Open();
            // run UI glitches #9: the chest itself is no skill selection (no book); the Mythic choice behind it is
            Assert.That(FindNamed(FindNamed(_f.App.ModalLayer, "Modal:Chest"), "SkillsBook"), Is.Null, "no book on the chest");
            Btn(_f.App.ModalLayer, "ChestTap").onClick.Invoke();
            AssertBookOpensOver("Modal:Mythic choice", "Mythic draft");
        }

        [Test]
        public void SkillsBook_OnTheSignatureChoice()
        {
            var run = StartRun();
            var sigs = C.Skills.Values.Where(s => s.Signature && !run.Skills.Contains(s.Id)).Take(2).Select(s => s.Id).ToList();
            run.SignatureChoice.AddRange(sigs);
            run.Phase = RunPhase.Event; run.EventId = "resonance"; run.EventTile = 1;
            Open();
            AssertBookOpensOver("Modal:Signature", "signature choice");
        }

        [Test]
        public void SkillsBook_InTheShop()
        {
            var run = StartRun();
            run.Phase = RunPhase.Shop;
            run.Shop.Clear();
            run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Skill, SkillId = Commons(1)[0], Price = 10 });
            Open();
            AssertBookOpensOver("Modal:Shop", "run shop");
        }

        [Test]
        public void SkillsBook_AtEvents_Rest_Train_Vendor_AndTheResult()
        {
            var run = StartRun();
            run.Skills.AddRange(Commons(2));
            run.Phase = RunPhase.Event; run.EventId = "campfire"; run.EventTile = -1;
            run.WormHp = run.WormMaxHp / 2; // Rest has something to heal
            var screen = Open();
            // run UI glitches #9: only skill selection screens have the book: Train does, the rest scene and its result do not
            Assert.That(FindNamed(OverlayOf(FindNamed(_f.App.ModalLayer, "Stage")), "SkillsBook"), Is.Null, "no book on the rest scene");
            Btn(_f.App.ModalLayer, "Option:Train").onClick.Invoke();
            AssertBookOpensOver("Modal:Train", "Train picker");
            Btn(FindNamed(_f.App.ModalLayer, "Modal:Train"), "Button:Back").onClick.Invoke();
            // resting flies in (event outcome rules: no result sheet for a deterministic result, so no book on it either)
            Btn(_f.App.ModalLayer, "Button:Rest").onClick.Invoke();
            screen.Rebuild();
            Assert.That(screen.OutcomeOpen, Is.False, "no sheet after resting");
        }

        [Test]
        public void SkillsBook_AtAVendor()
        {
            var run = StartRun();
            run.Phase = RunPhase.Event; run.EventId = "smuggler"; run.EventTile = -1;
            Open();
            var vendor = FindNamed(_f.App.ModalLayer, "Npc:black_marketeer");
            Assert.That(vendor, Is.Not.Null);
            // run UI glitches #9: a vendor is no skill selection: no book (it floated over the scene art)
            Assert.That(FindNamed(OverlayOf(vendor), "SkillsBook"), Is.Null);
        }

        private static Rect WorldRect(RectTransform t)
        {
            var c = new Vector3[4];
            t.GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        [Test]
        public void SkillsBook_OnRevive()
        {
            var run = StartRun();
            run.Phase = RunPhase.Defeated;
            Open();
            // run UI glitches #19 / #9: the Slice-style revive with its tombstone, chunky buttons and no skills book
            var revive = FindNamed(_f.App.ModalLayer, "Modal:Defeated!");
            Assert.That(revive, Is.Not.Null);
            Assert.That(FindNamed(revive, "SkillsBook"), Is.Null);
            Assert.That(FindNamed(revive, "Tombstone").GetComponent<Image>().sprite, Is.SameAs(RunScreen.TombstoneSprite()));
            Assert.That(FindNamed(revive, "ReviveTitle").GetComponent<Text>().text, Is.EqualTo("DEFEATED!"));
            Assert.That(Btn(revive, "Button:Give up"), Is.Not.Null);
            Assert.That(Btn(revive, "Button:" + C.RunTuning.ReviveDiamondCost + " Diamonds"), Is.Not.Null);
        }
    }
}
