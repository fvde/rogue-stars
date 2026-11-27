using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Meta.MetaTestKit;

namespace RogueStars.Rules.Tests.Meta;

/// <summary>
/// The Passes page (shop): the free daily gift, the passes with a one-time purchase bonus and a daily claim (Monthly Pass,
/// Daily Diamonds Forever, gift packs), No Ads, and "Claim all" (PassesClaimAll).
/// </summary>
public class PassesPageTests
{
    private static void Buy(GameEngine e, ref GameState s, string productId, long t)
    {
        Ok(e, ref s, new PurchaseStart { ProductId = productId }, t);
        Ok(e, ref s, new PurchaseComplete { ProductId = productId, ReceiptId = "dev:" + productId + ":" + t }, t);
    }

    [Fact]
    public void PassProducts_GrantTheirPurchaseBonusOnce_ThenOneClaimPerDay()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        foreach (var id in new[] { "monthly_pass", "perm_daily_diamonds" })
        {
            var p = e.Content.Product(id)!;
            long dia = Bal(s, Currencies.Diamonds);
            Buy(e, ref s, id, t);
            Assert.Equal(dia + p.Contents.Sum(r => r.Amount), Bal(s, Currencies.Diamonds)); // the "on purchase" bonus
            Assert.True(ShopSystem.CanClaimDaily(s, e.Content, id, t));
            Assert.False(ShopSystem.ClaimedDailyToday(s, e.Content, id, t));
            dia = Bal(s, Currencies.Diamonds);
            Ok(e, ref s, new ShopClaimDaily { ProductId = id }, t);
            Assert.Equal(dia + p.DailyRewards.Sum(r => r.Amount), Bal(s, Currencies.Diamonds));
            Assert.True(ShopSystem.ClaimedDailyToday(s, e.Content, id, t));
            Assert.Equal("already claimed today", Fail(e, ref s, new ShopClaimDaily { ProductId = id }, t + H)); // no double claim
        }
        // the monthly pass runs out after its days; Forever never does
        Assert.Equal(Day0 + 30 * D, ShopSystem.DailyGrantEndMs(s, e.Content, "monthly_pass"));
        Assert.Equal(long.MaxValue, ShopSystem.DailyGrantEndMs(s, e.Content, "perm_daily_diamonds"));
        Assert.Equal(0, ShopSystem.DailyGrantEndMs(s, e.Content, "gift_diamond_week"));
        // No Ads is a one-time entitlement without a daily claim
        Buy(e, ref s, "perm_no_ads", t);
        Assert.True(ShopSystem.HasNoAds(s, e.Content));
        Assert.Equal("purchase limit reached", Fail(e, ref s, new PurchaseStart { ProductId = "perm_no_ads" }, t));
        Assert.DoesNotContain(ShopSystem.PassClaimables(s, e.Content, t), c => c.Id == "perm_no_ads");
    }

    [Fact]
    public void DailyClaims_RenewAtTheDailyReset()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        Buy(e, ref s, "perm_daily_diamonds", t);
        Ok(e, ref s, new ShopClaimDaily { ProductId = "perm_daily_diamonds" }, t);
        Ok(e, ref s, new ShopBuyItem { ItemId = e.Content.Shop.DailyGiftItemId }, t);
        long reset = ShopSystem.NextDailyResetMs(e.Content, t);
        Assert.Equal(MetaTime.DayStartMs(e.Content, MetaTime.DayIndex(e.Content, t) + 1), reset);
        Assert.True(reset > t && reset - t <= D);
        // one ms before the reset: still claimed; at the reset: claimable again
        Assert.Empty(ShopSystem.PassClaimables(s, e.Content, reset - 1));
        Assert.False(ShopSystem.CanClaimDailyGift(s, e.Content, reset - 1));
        Assert.Equal("already claimed today", Fail(e, ref s, new ShopClaimDaily { ProductId = "perm_daily_diamonds" }, reset - 1));
        Assert.True(ShopSystem.CanClaimDaily(s, e.Content, "perm_daily_diamonds", reset));
        Assert.True(ShopSystem.CanClaimDailyGift(s, e.Content, reset));
        Assert.Equal(2, ShopSystem.PassClaimables(s, e.Content, reset).Count);
    }

    [Fact]
    public void ClaimAll_TakesTheGiftAndEveryOwnedPass_Once()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        Buy(e, ref s, "monthly_pass", t);
        Buy(e, ref s, "perm_daily_diamonds", t);
        Buy(e, ref s, "gift_diamond_week", t);
        var claims = ShopSystem.PassClaimables(s, e.Content, t);
        Assert.Equal(new[] { PassClaimKind.DailyGift, PassClaimKind.Daily, PassClaimKind.Daily, PassClaimKind.Daily }, claims.Select(c => c.Kind));
        Assert.Equal(new[] { "daily_free_gift", "gift_diamond_week", "monthly_pass", "perm_daily_diamonds" }, claims.Select(c => c.Id));

        long dia = Bal(s, Currencies.Diamonds);
        var r = Ok(e, ref s, new PassesClaimAll(), t);
        Assert.Equal(4, (int)r.Payload!);
        long expected = 10 + 120 + 100 + 50; // gift + gift week + monthly + forever
        Assert.Equal(dia + expected, Bal(s, Currencies.Diamonds));
        foreach (var id in new[] { "monthly_pass", "perm_daily_diamonds", "gift_diamond_week" }) Assert.True(ShopSystem.ClaimedDailyToday(s, e.Content, id, t));

        // double claim: nothing left until the reset; single claims fail too
        Assert.Equal("nothing to claim", Fail(e, ref s, new PassesClaimAll(), t + H));
        Assert.Equal("item limit reached", Fail(e, ref s, new ShopBuyItem { ItemId = "daily_free_gift" }, t + H));
        Assert.Equal(dia + expected, Bal(s, Currencies.Diamonds));

        // next day: everything again
        r = Ok(e, ref s, new PassesClaimAll(), Day0 + D + H);
        Assert.Equal(4, (int)r.Payload!);
        Assert.Equal(dia + 2 * expected, Bal(s, Currencies.Diamonds));
    }

    [Fact]
    public void ClaimAll_WithOnlyTheGift_AndNothingOwned()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        long dia = Bal(s, Currencies.Diamonds);
        Assert.Equal(1, (int)Ok(e, ref s, new PassesClaimAll(), t).Payload!);
        Assert.Equal(dia + 10, Bal(s, Currencies.Diamonds));
        Assert.Equal("nothing to claim", Fail(e, ref s, new PassesClaimAll(), t));
        // a single claim of the gift and then Claim all: no double grant
        Ok(e, ref s, new ShopBuyItem { ItemId = "daily_free_gift" }, Day0 + D);
        Assert.Equal("nothing to claim", Fail(e, ref s, new PassesClaimAll(), Day0 + D));
    }

    [Fact]
    public void ClaimAll_SkipsExpiredPasses()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        Buy(e, ref s, "monthly_pass", t);
        Ok(e, ref s, new ShopBuyItem { ItemId = "daily_free_gift" }, Day0 + 30 * D);
        Assert.False(ShopSystem.DailyGrantActive(s, e.Content, "monthly_pass", Day0 + 30 * D));
        Assert.Empty(ShopSystem.PassClaimables(s, e.Content, Day0 + 30 * D));
        Assert.Equal("nothing to claim", Fail(e, ref s, new PassesClaimAll(), Day0 + 30 * D));
    }

    [Fact]
    public void ClaimAll_AlsoTakesSeasonLevelsAndFundSteps()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        s.Counters[Requirements.MainLevelCounter] = 11;
        Unlock(s, Features.SeasonPass, Features.Funds);
        s.Count("runs_completed", 10); // 200 XP → season level 2
        Buy(e, ref s, "season_pass_premium", t);
        s.Worm.Level = 16; // worm fund steps 10 and 15 reached
        Buy(e, ref s, "fund_worm", t);
        Ok(e, ref s, new SeasonPassClaim { Level = 1 }, t); // already taken one by hand
        var claims = ShopSystem.PassClaimables(s, e.Content, t);
        Assert.Equal(1, claims.Count(c => c.Kind == PassClaimKind.DailyGift));
        Assert.Equal(3, claims.Count(c => c.Kind == PassClaimKind.SeasonLevel)); // p1, f2, p2
        Assert.Equal(2, claims.Count(c => c.Kind == PassClaimKind.FundStep));
        var r = Ok(e, ref s, new PassesClaimAll(), t);
        Assert.Equal(6, (int)r.Payload!);
        Assert.Contains("p:2", s.Shop.SeasonClaims);
        Assert.Contains("worm:1", s.Shop.FundClaims);
        Assert.Empty(ShopSystem.PassClaimables(s, e.Content, t));
    }

    [Fact]
    public void ClaimAll_KeepsPaidRewardsInFull_EvenForLockedUses()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        Buy(e, ref s, "gift_token_week", t); // Schematics (Blueprint pull currency) have no use yet on a fresh account
        s.Stash.Currencies.TryGetValue(Currencies.BlueprintTokens, out long before);
        Ok(e, ref s, new PassesClaimAll(), t);
        // no use yet: waits in the reward stash, kept in full (no stash cap for paid claims)
        Assert.Equal(before + 3, s.Stash.Currencies[Currencies.BlueprintTokens]);
        Assert.Contains("PassesClaimAll", e.Content.GateConfig.KeepAllCommands);
    }
}
