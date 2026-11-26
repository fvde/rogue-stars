using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Visual playtest 0.1.224 fixes on home and app chrome: the currency bar (G2), labelled tabs (H1), one guide card (H3),
    /// the next unlock's silhouette (H4), the pass tier in its label (H5), the level details (P3), the parts intro (U1).
    /// </summary>
    public sealed class HomePlaytestFixes3Tests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private HubScreen Hub
        {
            get
            {
                if (!(_f.App.Router.Current is HubScreen)) _f.App.Router.Reset(new HubScreen());
                var hub = (HubScreen)_f.App.Router.Current;
                hub.Rebuild();
                return hub;
            }
        }

        private void UnlockAll() => _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);

        [Test]
        public void CurrencyBar_AtMostFourIconChips_NumbersOnly_ZeroBalancesHidden_WalletButton()
        {
            UnlockAll();
            S.Wallet.Balances[Currencies.Ore] = 0;
            _f.App.Router.Reset(new HubScreen());
            _f.App.Bar.Rebuild();
            var chips = _f.Canvas.GetComponentsInChildren<Text>(true).Where(t => t.name.StartsWith("Chip:")).ToList();
            Assert.That(chips.Count, Is.InRange(1, ResourceBar.MaxChips));
            Assert.That(chips.Select(t => t.name), Does.Not.Contain("Chip:" + Currencies.Ore), "a zero balance is hidden");
            foreach (var t in chips) Assert.That(t.text, Does.Match(@"^[0-9./KMB]+$"), "numbers only: " + t.text);
            Assert.That(_f.Canvas.GetComponentsInChildren<Image>(true).Any(i => i.name == "Icon" && i.transform.parent.name.StartsWith("ChipBg:")), Is.True, "currency icons");
            Assert.That(_f.Canvas.GetComponentsInChildren<Button>(true).Any(b => b.name == "CurrencyDetails"), Is.True, "the wallet opens every balance");
            Assert.That(_f.Canvas.GetComponentsInChildren<Text>(true).Any(t => t.text.StartsWith("All")), Is.False, "no \"All\" button");
        }

        [Test]
        public void CurrencyBar_StaminaTimerSitsUnderItsPill()
        {
            S.Wallet.Balances[Currencies.Stamina] = 1;
            _f.App.Bar.Rebuild();
            var timer = _f.Canvas.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == "Timer:" + Currencies.Stamina);
            Assert.That(timer, Is.Not.Null);
            Assert.That(timer.transform.parent.name, Is.EqualTo("Cell:" + Currencies.Stamina), "in the stamina cell, below the pill");
            Assert.That(_f.Canvas.GetComponentsInChildren<Text>(true).First(t => t.name == "Chip:" + Currencies.Stamina).text, Does.Not.Contain("m"), "the pill is just the number");
        }

        [Test]
        public void Tabs_AreAllLabelled()
        {
            UnlockAll();
            var hub = Hub;
            foreach (var tab in hub.Root.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Tab:")))
                Assert.That(tab.GetComponentsInChildren<Text>(true).Any(t => t.name == "Label" && t.text.Length > 0), Is.True, tab.name);
        }

        [Test]
        public void Guide_ShowsOneCard_AndAPlusChipThatExpands()
        {
            S.Onboarding.Unlocked[Features.Parts] = _f.Service.NowMs; // a goal (equip parts) and the next unlock teaser
            var hub = Hub;
            var guide = hub.Root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "Guide");
            int active = guide.Cast<Transform>().Count(t => t.gameObject.activeSelf);
            Assert.That(active, Is.EqualTo(1), "one guide card at a time");
            var more = hub.Root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "GuideMore");
            if (guide.childCount > 1)
            {
                Assert.That(more, Is.Not.Null, "+N chip");
                more.onClick.Invoke();
                guide = hub.Root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "Guide");
                Assert.That(guide.Cast<Transform>().Count(t => t.gameObject.activeSelf), Is.GreaterThan(1));
            }
            Assert.That(guide.sizeDelta.x, Is.EqualTo(HubScreen.GuideWidth), "the side columns keep their width");
        }

        [Test]
        public void NextTeaser_NamesNoFeature_OnlyTheLevelsToGo()
        {
            var hub = Hub;
            var teaser = hub.Root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "Teaser");
            Assert.That(teaser.GetComponentsInChildren<Image>(true).Any(i => i.name == "Icon" && i.sprite != null), Is.False, "no feature art: a surprise");
            Assert.That(teaser.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("to the next feature") || t.text.Contains("is unlocking")), Is.True);
        }

        [Test]
        public void LevelDetails_PlayerWords_AndNoStartOnALockedLevel()
        {
            _f.App.Open("portals");
            var screen = (PortalsScreen)_f.App.Router.Current;
            var level = C.MainLevel(3);
            screen.LevelModal(C.Portal(level.PortalId), level, RunMode.Normal);
            var texts = _f.App.ModalLayer.GetComponentsInChildren<Text>(true).Select(t => t.text).ToList();
            Assert.That(texts, Does.Contain("Level 3 · " + level.Name));
            Assert.That(texts.Any(t => t.Contains("d6")), Is.False, "\"2 dice\", not \"2d6\"");
            Assert.That(texts.Any(t => t.StartsWith("Completion")), Is.False, "hidden until played");
            Assert.That(texts, Does.Contain("Clear level 2 first"));
            Assert.That(_f.App.ModalLayer.GetComponentsInChildren<Button>(true).Any(b => b.name.StartsWith("Button:Start")), Is.False);
            Assert.That(PortalsScreen.DiceText(level), Does.EndWith(level.MaxRolls + " rolls"));
        }

        [Test]
        public void PartsIntro_NamesAllSixSlots()
        {
            var intro = C.Feature(Features.Parts).Intro.Select(i => i.Text).ToList();
            Assert.That(intro.Any(t => t.Contains("six slots") || t.Contains("Six slots")), Is.True);
            Assert.That(System.Enum.GetValues(typeof(PartSlot)).Length, Is.EqualTo(6));
        }
    }
}
