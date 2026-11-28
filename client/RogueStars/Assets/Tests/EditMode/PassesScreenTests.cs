using System.Collections.Generic;
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
    /// <summary>The Passes page: the cards, their states (buy / claim / claimed / owned), Claim all, and how it is reached.</summary>
    public sealed class PassesScreenTests
    {
        private AppFixture _f;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        private void Unlock(params string[] ids)
        {
            foreach (var id in ids) S.Onboarding.Unlocked[id] = _f.Service.NowMs;
        }

        private PassesScreen Open()
        {
            Unlock(Features.Parts);
            _f.App.Open("passes");
            var screen = _f.App.Router.Current as PassesScreen;
            Assert.That(screen, Is.Not.Null);
            return screen;
        }

        private Button Button(string name) =>
            _f.App.Router.Current.Root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        private List<string> Names() => _f.App.Router.Current.Root.GetComponentsInChildren<RectTransform>(true).Select(t => t.name).ToList();

        private void Buy(string productId)
        {
            CommandResult r = null;
            _f.App.Session.Purchase(productId, x => r = x);
            Assert.That(r, Is.Not.Null, productId);
            Assert.That(r.Ok, Is.True, productId + ": " + r.Error);
        }

        [Test]
        public void Page_ShowsTheGift_ThePassCards_AndSeasonAndFunds()
        {
            Open();
            var names = Names();
            Assert.That(names, Does.Contain("PassCard:" + C.Shop.DailyGiftItemId));
            foreach (var id in new[] { "monthly_pass", "perm_no_ads", "perm_daily_diamonds" }) Assert.That(names, Does.Contain("PassCard:" + id), id);
            Assert.That(names, Does.Not.Contain("PassCard:gift_diamond_week"), "gift packs show only while they run");
            Assert.That(names, Does.Contain("Ribbon:Season pass").And.Contain("Ribbon:Funds"));
            Assert.That(names.Count(n => n == "Ticket"), Is.EqualTo(3));
            Assert.That(names, Does.Not.Contain("ActiveTag"), "nothing owned yet");
            // order: gift, Monthly Pass, then the Forever products by price
            var cards = names.Where(n => n.StartsWith("PassCard:")).ToList();
            Assert.That(cards, Is.EqualTo(new[] { "PassCard:daily_free_gift", "PassCard:monthly_pass", "PassCard:perm_no_ads", "PassCard:perm_daily_diamonds" }));
            Assert.That(Button("Claim:" + C.Shop.DailyGiftItemId), Is.Not.Null);
        }

        [Test]
        public void Perks_UseTheRealNumbers_AndTitlesHighlightOneWord()
        {
            var monthly = C.Product("monthly_pass");
            var perks = string.Join(" | ", PassesScreen.Perks(C, monthly));
            Assert.That(perks, Does.Contain("×3").And.Contain("+" + C.Shop.MonthlyPassStaminaCapBonus).And.Contain("+20%").And.Contain("30 days"));
            Assert.That(PassesScreen.CardTitle(C, monthly), Is.EqualTo("<color=#FFD54A>Monthly</color> Pass"));
            Assert.That(PassesScreen.CardTitle(C, C.Product("perm_no_ads")), Is.EqualTo("No <color=#FFD54A>Ads</color>"));
            Assert.That(PassesScreen.CardTitle(C, C.Product("perm_daily_diamonds")), Is.EqualTo("Daily Diamonds <color=#FFD54A>Forever</color>"));
            Assert.That(string.Join(" ", PassesScreen.Perks(C, C.Product("perm_daily_diamonds"))), Does.Contain("50 Diamonds").And.Contain("forever"));
            // four different card colours
            var colours = new[] { PassesScreen.GiftColor, PassesScreen.CardColor(C, monthly), PassesScreen.CardColor(C, C.Product("perm_no_ads")), PassesScreen.CardColor(C, C.Product("perm_daily_diamonds")) };
            Assert.That(colours.Distinct().Count(), Is.EqualTo(4));
        }

        [Test]
        public void Owned_Pass_ShowsActive_Claim_ThenClaimed_AndTimeLeft()
        {
            Open();
            Buy("monthly_pass");
            var names = Names();
            Assert.That(names, Does.Contain("ActiveTag"));
            var claim = Button("Claim:monthly_pass");
            Assert.That(claim, Is.Not.Null);
            Assert.That(claim.interactable, Is.True);
            var time = _f.App.Router.Current.Root.GetComponentsInChildren<Text>(true).First(t => t.name == "TimeLeft" && t.transform.parent.parent.parent.name == "PassCard:monthly_pass");
            Assert.That(time.text, Does.StartWith("Ends in"));
            long dia = S.Wallet.Get(Currencies.Diamonds);
            claim.onClick.Invoke();
            Assert.That(S.Wallet.Get(Currencies.Diamonds), Is.EqualTo(dia + 100));
            var claimed = Button("Claimed:monthly_pass");
            Assert.That(claimed, Is.Not.Null);
            Assert.That(claimed.interactable, Is.False, "greyed once claimed today");
            Assert.That(Button("Claim:monthly_pass"), Is.Null);
            // the monthly pass can be extended while it runs
            Assert.That(_f.App.Router.Current.Root.GetComponentsInChildren<Button>(true).Any(b => b.name.EndsWith(":extend")), Is.True);
        }

        [Test]
        public void ClaimAll_ClaimsEverything_ThenGreysOut_UntilTheReset()
        {
            Open();
            Buy("perm_daily_diamonds");
            var all = Button("ClaimAll");
            Assert.That(all.interactable, Is.True);
            Assert.That(all.GetComponentInChildren<Text>().text, Is.EqualTo("Claim all (2)"));
            long dia = S.Wallet.Get(Currencies.Diamonds);
            all.onClick.Invoke();
            Assert.That(S.Wallet.Get(Currencies.Diamonds), Is.EqualTo(dia + 10 + 50));
            Assert.That(Button("ClaimAll").interactable, Is.False);
            Assert.That(Button("Claimed:perm_daily_diamonds"), Is.Not.Null);
            Assert.That(Button("Claimed:" + C.Shop.DailyGiftItemId), Is.Not.Null);
            // after the daily reset both are back
            _f.Clock.Advance(ShopSystem.NextDailyResetMs(C, _f.Service.NowMs) - _f.Service.NowMs + 1000);
            _f.App.Session.Refresh();
            _f.App.Router.Current.Rebuild();
            Assert.That(Button("ClaimAll").interactable, Is.True);
            Assert.That(Button("Claim:perm_daily_diamonds"), Is.Not.Null);
        }

        [Test]
        public void NoAds_Owned_ShowsOwned_WithoutADailySlot()
        {
            Open();
            Assert.That(Button("ComingSoon:perm_no_ads"), Is.Not.Null, "not in the first Play product set: Soon");
            _f.App.Session.ProductSet = RogueStars.Client.Services.StoreCatalog.SetAll;
            Buy("perm_no_ads");
            Assert.That(Button("Owned:perm_no_ads"), Is.Not.Null);
            var card = _f.App.Router.Current.Root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "PassCard:perm_no_ads");
            Assert.That(card.GetComponentsInChildren<RectTransform>(true).Any(t => t.name == "DailySlots"), Is.False);
            Assert.That(card.GetComponentsInChildren<RectTransform>(true).Any(t => t.name == "ActiveTag"), Is.True);
        }

        [Test]
        public void Shop_PassesChip_OpensThePage_AndTheForeverTabIsGone()
        {
            Unlock(Features.Parts);
            _f.App.Open("shop");
            var shop = _f.App.Router.Current;
            var texts = shop.Root.GetComponentsInChildren<Button>(true).Select(b => b.name).ToList();
            Assert.That(texts, Does.Contain("Tab:Passes").And.Not.Contain("Tab:Forever"));
            var passes = shop.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "Tab:Passes");
            Assert.That(passes.GetComponentsInChildren<RectTransform>(true).Any(t => t.name == "NotifyDot"), Is.True, "the free gift waits");
            passes.onClick.Invoke();
            Assert.That(_f.App.Router.Current, Is.InstanceOf<PassesScreen>());
            _f.App.Router.Pop();
            Assert.That(_f.App.Router.Current, Is.SameAs(shop));
            // the free daily gift lives on the Passes page only
            shop.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "Tab:Items").onClick.Invoke();
            Assert.That(shop.Root.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("Daily Free Gift")), Is.False);
        }

        [Test]
        public void Hub_PassIcon_And_UnlockActions_OpenThePassesPage()
        {
            Unlock(Features.Parts, Features.SeasonPass);
            var right = HubLayout.LeftRail(S, C, _f.Service.NowMs); // the pass sits on the left rail (board "New · home")
            Assert.That(right.First(e => e.Id == "pass").ScreenId, Is.EqualTo("passes"));
            Assert.That(HubLayout.UnlockAction(Features.SeasonPass).ScreenId, Is.EqualTo("passes"));
            Assert.That(HubLayout.UnlockAction(Features.Funds).ScreenId, Is.EqualTo("passes"));
            Assert.That(HubLayout.Reachable(S, C, _f.Service.NowMs), Does.Contain("passes"));
            Assert.That(HubLayout.MoreScreens(S, C).Any(e => e.Id == "passes"), Is.False);
        }
    }
}
