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
    /// Owner playtest 0.1.224: one skill tile everywhere (tier frame, the icon art filling the centre, code-drawn fallback)
    /// and a tap tooltip with the full text — battle grid, draft / shop / Train cards, skills book, result lines.
    /// </summary>
    public sealed class SkillTileTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;
        private static readonly Sprite Fake = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; RunFx.Instant = true; }

        [TearDown]
        public void TearDown()
        {
            SkillTooltip.Close();
            RunScreen.InstantOutcomes = false; RunFx.Instant = false; BattlePlaybackScreen.Instant = false;
            ArtCatalog.Loader = null;
            _f.Dispose();
        }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static string Texts(Transform root) => string.Join("|", root.GetComponentsInChildren<Text>(true).Select(t => t.text));

        private List<SkillDef> RunSkills(Rarity tier, int n)
        {
            var list = new List<SkillDef>();
            foreach (var s in C.Skills.Values)
            {
                if (s.Source != SkillSource.Run || !s.Draftable || s.RequiresFamilyCount > 0 || s.Requires.Count > 0 || s.UpgradesFrom != null || s.Signature) continue;
                if (RunTuning.RunTier(s.Rarity) != tier) continue;
                list.Add(s);
                if (list.Count == n) break;
            }
            Assert.That(list.Count, Is.EqualTo(n));
            return list;
        }

        private ActiveRun StartRun()
        {
            var level = C.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            return S.Run.Active;
        }

        [Test]
        public void Tile_ArtFillsTheFace_NoGlyph_FallbackDrawsOne()
        {
            var sk = RunSkills(Rarity.Legendary, 1)[0];
            ArtCatalog.Loader = p => p == "Art/Skills/" + sk.Id ? Fake : null;
            var tile = SkillTileView.Build(_f.Canvas, C, sk.Id, 200);
            Assert.That(tile.name, Is.EqualTo("Slot:" + sk.Id));
            Assert.That(FindNamed(tile, "RarityBorder").GetComponent<Image>().color, Is.EqualTo(Theme.SkillTierColor(Rarity.Legendary)), "tier frame");
            var face = FindNamed(tile, "Face");
            var art = (RectTransform)FindNamed(tile, "IconArt");
            Assert.That(art, Is.Not.Null);
            Assert.That(art.parent, Is.SameAs(face), "the art lives in the face");
            Assert.That(art.anchorMin, Is.EqualTo(Vector2.zero)); Assert.That(art.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(art.offsetMin, Is.EqualTo(Vector2.zero)); Assert.That(art.offsetMax, Is.EqualTo(Vector2.zero));
            Assert.That(art.GetComponent<Image>().preserveAspect, Is.False, "square art fills the face edge to edge");
            Assert.That(face.GetComponent<Mask>(), Is.Not.Null, "clipped to the rounded face");
            Assert.That(FindNamed(tile, "Glyph"), Is.Null, "no letter when art exists");

            var other = RunSkills(Rarity.Common, 1)[0];
            var drawn = SkillTileView.Build(_f.Canvas, C, other.Id, 200);
            Assert.That(FindNamed(drawn, "IconArt"), Is.Null);
            Assert.That(FindNamed(drawn, "Glyph"), Is.Not.Null, "code-drawn fallback");
        }

        [Test]
        public void TooltipLines_NameTierLevel_FullText_UpgradeLine()
        {
            var sk = RunSkills(Rarity.Common, 1)[0];
            var lines = SkillTooltip.Lines(C, SkillTip.Of(sk.Id, 1));
            Assert.That(lines[0], Is.EqualTo(SkillText.Title(C, sk, 1)));
            Assert.That(lines[1], Does.StartWith("Common"));
            Assert.That(lines[0] + " " + lines[1], Does.Contain("Lv 1/" + C.RunTuning.MaxSkillLevelOf(sk.Rarity)), "the level, once");
            Assert.That((lines[0] + " " + lines[1]).Split(new[] { " Lv " }, System.StringSplitOptions.None).Length, Is.EqualTo(2));
            Assert.That(lines[2], Is.EqualTo(SkillText.Full(C, sk, 1)));
            string up = SkillText.UpgradeFull(C, sk, 1, 2);
            if (!string.IsNullOrEmpty(up)) Assert.That(lines, Does.Contain("Next level: " + up));
            // an offered level-up: the target level and the upgrade
            var offer = SkillTooltip.Lines(C, RunScreen.OfferTip(sk.Id, 1));
            Assert.That(offer[0], Is.EqualTo(SkillText.Title(C, sk, 2)));
            Assert.That(offer.Last(), Does.StartWith("Level 1 → 2: "));
            // at the cap: no upgrade line
            int max = C.RunTuning.MaxSkillLevelOf(sk.Rarity);
            Assert.That(SkillTooltip.Lines(C, SkillTip.Of(sk.Id, max)).Count, Is.EqualTo(3));
        }

        [Test]
        public void BattleGrid_UsesTheSharedTile_TapShowsFullText_TapElsewhereCloses()
        {
            var sk = RunSkills(Rarity.Common, 2);
            var run = StartRun();
            run.Skills.Add(sk[0].Id); run.Skills.Add(sk[1].Id);
            var b = new BattleResult { Winner = BattleSide.Player, Rounds = 1 };
            b.Fighters.Add(new BattleFighterInfo { Index = 0, Name = "Worm", Role = CombatRole.Worm, Side = BattleSide.Player, Targetable = true, MaxHp = 1000, StartHp = 900, EndHp = 890, Alive = true });
            b.Fighters.Add(new BattleFighterInfo { Index = 1, Name = "Hound", Role = CombatRole.Enemy, Side = BattleSide.Enemy, Targetable = true, MaxHp = 500, StartHp = 500, EndHp = 0, Alive = false });
            b.Log.Add(new BattleLogEntry { Round = 1, Actor = 0, Target = 1, Action = BattleAction.Skill, Amount = 120, SkillId = sk[0].Id, TargetHp = 380 });
            BattlePlaybackScreen.Instant = true;
            var screen = new BattlePlaybackScreen(b);
            _f.App.Router.Push(screen);
            var tile = FindNamed(screen.Root, "Slot:" + sk[0].Id);
            Assert.That(tile, Is.Not.Null);
            Assert.That(FindNamed(tile, "RarityBorder"), Is.Not.Null);
            Assert.That(FindNamed(tile, "Face"), Is.Not.Null);
            tile.GetComponent<Button>().onClick.Invoke();
            Assert.That(SkillTooltip.IsOpen, Is.True);
            var tip = SkillTooltip.Current;
            Assert.That(Texts(tip), Does.Contain(sk[0].Name));
            Assert.That(FindNamed(tip, "TipText").GetComponent<Text>().text, Does.Contain(SkillText.Full(C, sk[0], 1).Split(' ')[0]));
            tip.GetComponent<Button>().onClick.Invoke(); // the full-screen catcher: a tap anywhere
            Assert.That(SkillTooltip.IsOpen, Is.False);
        }

        [Test]
        public void DraftCard_TapStillPicks_QuestionMarkShowsTheTooltip()
        {
            var sk = RunSkills(Rarity.Common, 3);
            var run = StartRun();
            run.Phase = RunPhase.Draft;
            run.DraftOffer.Clear(); foreach (var s in sk) run.DraftOffer.Add(s.Id);
            _f.App.Router.Push(new RunScreen());
            var card = FindNamed(_f.App.ModalLayer, "Draft:" + sk[0].Id);
            Assert.That(card, Is.Not.Null);
            Assert.That(FindNamed(card, "Frame/RarityBorder") ?? FindNamed(FindNamed(card, "Frame"), "RarityBorder"), Is.Not.Null, "the shared tile on the card");
            Assert.That(FindNamed(card, "Frame").GetComponent<Button>(), Is.Null, "the tile does not catch the pick tap");
            FindNamed(card, "InfoButton").GetComponent<Button>().onClick.Invoke();
            Assert.That(SkillTooltip.IsOpen, Is.True);
            Assert.That(Texts(SkillTooltip.Current), Does.Contain(sk[0].Name));
            SkillTooltip.Close();
            card.parent.GetComponent<Button>().onClick.Invoke();
            Assert.That(S.Run.Active.Skills, Does.Contain(sk[0].Id), "a tap on the card picks");
        }

        [Test]
        public void ShopCard_TileTap_ShowsTooltip_WithTheUpgradeLine()
        {
            var run = StartRun();
            var sk = RunSkills(Rarity.Common, 1)[0];
            run.Skills.Add(sk.Id);
            run.Phase = RunPhase.Shop;
            run.Shop.Clear();
            run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Skill, SkillId = sk.Id, Price = 10 });
            _f.App.Router.Push(new RunScreen());
            var slot = FindNamed(_f.App.ModalLayer, "Offer:0");
            Assert.That(slot, Is.Not.Null);
            var frame = FindNamed(slot, "Frame");
            Assert.That(frame.GetComponent<Button>(), Is.Not.Null, "the shop card does not pick on tap: its tile opens the tooltip");
            frame.GetComponent<Button>().onClick.Invoke();
            Assert.That(SkillTooltip.IsOpen, Is.True);
            var upgrade = FindNamed(SkillTooltip.Current, "TipUpgrade");
            Assert.That(upgrade, Is.Not.Null, "the offered level-up's upgrade line");
            Assert.That(SkillText.StripMarks(upgrade.GetComponent<Text>().text), Does.StartWith("Level "));
            Assert.That(SkillTooltip.Lines(C, RunScreen.OfferTip(sk.Id, 1)), Has.Some.StartsWith("Level 1 → 2: "));
        }

        [Test]
        public void SkillsBook_ShowsTiles_TapForTheText()
        {
            var sk = RunSkills(Rarity.Common, 2);
            var run = StartRun();
            run.Skills.Add(sk[0].Id); run.Skills.Add(sk[1].Id);
            _f.App.Router.Push(new RunScreen());
            FindNamed(_f.App.Router.Current.Root, "SkillsButton").GetComponent<Button>().onClick.Invoke();
            var cell = FindNamed(_f.App.ModalLayer, "Skill:" + sk[1].Id);
            Assert.That(cell, Is.Not.Null);
            var tile = FindNamed(cell, "Slot:" + sk[1].Id);
            Assert.That(tile, Is.Not.Null, "the shared tile");
            Assert.That(Texts(cell), Does.Contain(sk[1].Name));
            tile.GetComponent<Button>().onClick.Invoke();
            Assert.That(Texts(SkillTooltip.Current), Does.Contain(SkillText.Title(C, sk[1], 1)));
        }
    }
}
