using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.Services;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>Playtest 0.1.224 meta-screen fixes: summon costs/amounts/outfits, signals banner, shop, mail, quests, arena, settings.</summary>
    public sealed class MetaPolishTests
    {
        private AppFixture _f;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private GameState S => _f.Service.State;
        private static List<string> Names(Transform root) => root.GetComponentsInChildren<RectTransform>(true).Select(t => t.name).ToList();

        [Test]
        public void Summon_CostsAndCompactAmounts()
        {
            Assert.That(SummonEventScreen.TicketCost(1), Is.EqualTo("1 ticket"));
            Assert.That(SummonEventScreen.TicketCost(10), Is.EqualTo("10 tickets"));
            Assert.That(SignalsScreen.TokenCost(1), Is.EqualTo("1 token"));
            Assert.That(SummonView.Compact(950), Is.EqualTo("950"));
            Assert.That(SummonView.Compact(2000), Is.EqualTo("2K"));
            Assert.That(SummonView.Compact(1500), Is.EqualTo("1.5K"));
            Assert.That(SummonView.Compact(9000), Is.EqualTo("9K"));
            Assert.That(SummonView.Compact(18400), Is.EqualTo("18K"));
        }

        [Test]
        public void Summon_HeroesWithoutArt_WearTheirOwnOutfit()
        {
            var c = ContentDb.Default;
            var seen = new HashSet<Sprite>();
            foreach (var h in c.HeroConfig.Defs.Keys)
            {
                var root = SummonKit.FeaturedArt(new GameObject("t", typeof(RectTransform)).transform, SummonEventType.Hero, h);
                if (SummonArt.HasHeroArt(h)) continue;
                var names = Names(root);
                Assert.That(names, Does.Contain("HeroHat").And.Contain("HeroCape"), h);
                seen.Add(SummonArt.HeroOutfit(h, false));
                Object.DestroyImmediate(root.parent.gameObject);
            }
            Assert.That(seen.Count, Is.GreaterThan(1), "heroes look different from each other");
        }

        [Test]
        public void Mail_CountLine_Grammar()
        {
            Assert.That(MailScreen.CountLine(1, 1), Is.EqualTo("1 message, 1 unread"));
            Assert.That(MailScreen.CountLine(1, 0), Is.EqualTo("1 message"));
            Assert.That(MailScreen.CountLine(3, 2), Is.EqualTo("3 messages, 2 unread"));
        }

        [Test]
        public void Settings_ModeLine_AndPrefilledName()
        {
            Assert.That(SettingsScreen.ModeLine(false, "connected"), Is.EqualTo("Offline"));
            Assert.That(SettingsScreen.ModeLine(true, "connected"), Is.EqualTo("Online — connected"));
            Assert.That(SettingsScreen.CurrentName("Wiggles", "Worm"), Is.EqualTo("Wiggles"));
            Assert.That(SettingsScreen.CurrentName(null, "Worm"), Is.EqualTo("Worm"));
        }

        [Test]
        public void Shop_SectionTitles_AreWords()
        {
            Assert.That(ShopScreen.SectionTitle(ProductKind.StarterPack), Is.EqualTo("Starter Packs"));
            Assert.That(ShopScreen.SectionTitle(ProductKind.FlashOffer), Is.EqualTo("Flash Offers"));
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.App.Open("shop");
            var texts = _f.App.Router.Current.Root.GetComponentsInChildren<Text>(true).Select(t => t.text).ToList();
            Assert.That(texts, Has.None.EqualTo("Starter pack").And.None.EqualTo("Flash offer").And.None.Contains("Offer not active"));
        }

        [Test]
        public void Arena_BotNames_AreDistinct_AndNeverNumbered()
        {
            foreach (long week in new long[] { 0, 3, 2900 })
            {
                var names = Enumerable.Range(0, 20).Select(i => LocalGameService.BotName(week, i)).ToList();
                Assert.That(names.Distinct().Count(), Is.EqualTo(20));
                Assert.That(names.Any(n => n.Any(char.IsDigit)), Is.False);
            }
        }

        [Test]
        public void Quests_PointChestsAreIcons_AndDisabledClaimSaysWhy()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.App.Open("quests");
            var root = _f.App.Router.Current.Root;
            var names = Names(root);
            Assert.That(names.Count(n => n.StartsWith("Chest:")), Is.GreaterThan(0));
            Assert.That(root.GetComponentsInChildren<Text>(true).Any(t => t.text.EndsWith(" chest")), Is.False, "no \"20 chest\" text tiles");
            var claim = root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name.StartsWith("Claim:") && !b.interactable);
            if (claim != null) Assert.That(claim.transform.parent.GetComponentsInChildren<Text>(true).Any(t => t.name == "DisabledReason"), Is.True);
        }

        [Test]
        public void DailyQuests_ShowPointsOnly_TheChestsPay_AndAchievementsNameTheNextMilestone()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.App.Open("quests");
            Assert.That(_f.App.Router.Current.Title, Is.EqualTo("Daily Quests"));
            var daily = _f.App.Router.Current.Root.GetComponentsInChildren<Text>(true).Where(t => t.name.StartsWith("Reward:d_")).ToList();
            Assert.That(daily, Is.Not.Empty);
            foreach (var t in daily) Assert.That(t.text, Does.StartWith("+").And.EndWith(" pts"), t.name);
            // the weekly tab too, then the achievements: reward and the next milestone
            var screen = (RogueStars.Client.Screens.QuestsScreen)_f.App.Router.Current;
            var chest = _f.Service.Content.PointChests.First(c => c.RandomTokens > 0);
            Assert.That(screen.ChestText(chest), Does.Contain("3 pull tickets").And.Contain("random Signal"));
            var tabs = _f.App.Router.Current.Root.GetComponentsInChildren<Button>(true).ToList();
            var ach = tabs.First(b => b.GetComponentInChildren<Text>()?.text == "Achieve");
            ach.onClick.Invoke();
            var texts = _f.App.Router.Current.Root.GetComponentsInChildren<Text>(true).Where(t => t.name.StartsWith("Reward:a_")).Select(t => t.text).ToList();
            Assert.That(texts, Is.Not.Empty);
            Assert.That(texts.All(t => t.Contains("next at")), Is.True, string.Join(" | ", texts));
        }

        [Test]
        public void DailyTopChest_OpensWithThreeTicketsFlippingOut()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.Service.State.Quests.DailyPoints = 100;
            _f.App.Open("quests");
            var screen = (RogueStars.Client.Screens.QuestsScreen)_f.App.Router.Current;
            var chest = _f.Service.Content.PointChests.First(c => c.RandomTokens > 0);
            screen.OpenChest(PointTrack.Daily, chest);
            Assert.That(_f.Service.State.Quests.LastChestTokens.Count, Is.EqualTo(3));
            Assert.That(_f.Service.State.Quests.ChestsClaimed, Does.Contain("Daily:100"));
            var all = _f.Service.State.Quests.LastChestTokens;
            Assert.That(all.All(t => t == Currencies.PartTokens || t == Currencies.AlienTokens || t == Currencies.BlueprintTokens), Is.True);
            var items = PullReveal.FromRewards(all.Select(t => Reward.Cur(t, 1)), _f.Service.Content);
            Assert.That(items.Count, Is.EqualTo(3), "three tickets flip out");
        }
    }
}
