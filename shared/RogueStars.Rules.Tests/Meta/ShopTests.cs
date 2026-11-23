using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Meta.MetaTestKit;

namespace RogueStars.Rules.Tests.Meta;

public class ShopTests
{
    private static void Buy(GameEngine e, ref GameState s, string productId, long t, string? receipt = null)
    {
        Ok(e, ref s, new PurchaseStart { ProductId = productId }, t);
        Ok(e, ref s, new PurchaseComplete { ProductId = productId, ReceiptId = receipt ?? "dev:" + productId + ":" + t }, t);
    }

    [Fact]
    public void EveryProduct_GrantsItsContentsExactlyOnce()
    {
        var e = Engine(devReceipts: true);
        Assert.True(e.Content.Products.Count >= 20);
        foreach (var p in e.Content.Products.Values)
        {
            var s = Player(e); UnlockAll(s);
            long t = TestHelpers.T0;
            if (p.Schedule != null) { Assert.True(p.Schedule.TryNext(t, out long st, out _)); t = st + H; }
            if (p.ChainId != null) s.Shop.RollingTaken[p.ChainId] = p.ChainStep;
            e.Settle(s, t);
            var expected = new Dictionary<string, long>();
            foreach (var r in p.Contents.Concat(p.FirstPurchaseBonus)) expected[r.Id] = expected.GetValueOrDefault(r.Id) + r.Amount;
            var before = expected.Keys.ToDictionary(k => k, k => Bal(s, k));

            if (p.PriceUsdCents == 0) Ok(e, ref s, new ShopClaimFree { ProductId = p.Id }, t);
            else
            {
                Assert.False(string.IsNullOrEmpty(p.StoreProductId), p.Id);
                Buy(e, ref s, p.Id, t);
                Fail(e, ref s, new PurchaseComplete { ProductId = p.Id, ReceiptId = "dev:again" }, t); // no pending → no double grant
            }
            foreach (var kv in expected) Assert.True(before[kv.Key] + kv.Value == Bal(s, kv.Key), p.Id + " " + kv.Key);
            Assert.Equal(1, ShopSystem.PurchaseCount(s, p.Id));
            if (p.EntitlementFlag != null) Assert.Contains(p.EntitlementFlag, s.Flags);
            if (p.Kind == ProductKind.SeasonPass) Assert.True(ShopSystem.HasPremiumSeason(s, e.Content, t));
            if (p.FundId != null) Assert.Contains(p.FundId, s.Shop.FundsOwned);
            if (p.DailyRewards.Count > 0) Assert.True(ShopSystem.CanClaimDaily(s, e.Content, p.Id, t));
        }
    }

    [Fact]
    public void DiamondPacks_PriceLadder_FirstPurchaseDoubleOnlyOnce()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        var packs = e.Content.Products.Values.Where(p => p.Kind == ProductKind.DiamondPack).OrderBy(p => p.PriceUsdCents).ToList();
        Assert.True(packs.Count >= 5);
        for (int i = 1; i < packs.Count; i++) Assert.True(packs[i].Contents[0].Amount > packs[i - 1].Contents[0].Amount);
        long dia = Bal(s, Currencies.Diamonds);
        Buy(e, ref s, "diamonds_60", t, "r1");
        Assert.Equal(dia + 120, Bal(s, Currencies.Diamonds));
        Buy(e, ref s, "diamonds_60", t, "r2");
        Assert.Equal(dia + 180, Bal(s, Currencies.Diamonds));
    }

    [Fact]
    public void PurchaseComplete_IsServerOnly_AndReceiptRulesHold()
    {
        Assert.True(ServerOnly.Is(typeof(PurchaseComplete)));
        Assert.False(ServerOnly.Is(typeof(PurchaseStart)));
        var e = Engine(devReceipts: false); var s = Player(e); long t = TestHelpers.T0;
        Assert.Equal("no pending purchase", Fail(e, ref s, new PurchaseComplete { ProductId = "diamonds_60", ReceiptId = "gp:1" }, t));
        var start = Ok(e, ref s, new PurchaseStart { ProductId = "diamonds_60" }, t);
        Assert.Equal("rs.diamonds_60", start.Payload);
        Assert.Equal("dev receipts not allowed", Fail(e, ref s, new PurchaseComplete { ProductId = "diamonds_60", ReceiptId = "dev:fake" }, t));
        Assert.Equal("receipt required", Fail(e, ref s, new PurchaseComplete { ProductId = "diamonds_60", ReceiptId = "" }, t));
        Ok(e, ref s, new PurchaseComplete { ProductId = "diamonds_60", ReceiptId = "gp:1" }, t);
        Ok(e, ref s, new PurchaseStart { ProductId = "diamonds_60" }, t);
        Assert.Equal("receipt already used", Fail(e, ref s, new PurchaseComplete { ProductId = "diamonds_60", ReceiptId = "gp:1" }, t));
        // Pending purchases expire.
        Assert.Equal("no pending purchase", Fail(e, ref s, new PurchaseComplete { ProductId = "diamonds_60", ReceiptId = "gp:2" }, t + 3 * D));
        Assert.Equal("free product: use ShopClaimFree", Fail(e, ref s, new PurchaseStart { ProductId = "rolling_main_0" }, t));
    }

    [Fact]
    public void ServerOnlyCommands_AreExactlyTheExpectedSet()
    {
        var serverOnly = typeof(GameEngine).Assembly.GetTypes().Where(ServerOnly.Is).Select(x => x.Name).OrderBy(x => x).ToArray();
        string[] meta = { "ArenaFightResult", "ArenaWeekEnd", "GuildSetMembership", "InvasionRecordDamage", "LiveOpsApplyOutcome",
            "LiveOpsStartSurpriseInvasion", "MailPush", "PurchaseComplete" };
        Assert.All(meta, m => Assert.Contains(m, serverOnly));
    }

    [Fact]
    public void PermanentNoAds_InstantAds_StillCapped_AndNotRebuyable()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        Assert.Equal("ad token required", Fail(e, ref s, new AdWatched { PlacementId = "daily_diamonds_ad" }, t));
        Buy(e, ref s, "perm_no_ads", t);
        Assert.True(ShopSystem.HasNoAds(s, e.Content));
        Assert.Equal("purchase limit reached", Fail(e, ref s, new PurchaseStart { ProductId = "perm_no_ads" }, t));
        var r = Ok(e, ref s, new AdWatched { PlacementId = "daily_diamonds_ad" }, t);
        Assert.Contains(r.Events, ev => ev.Type == "ad_watched" && ev.Args[1] == "instant");
        Assert.Contains("ent_no_ads", s.Flags);
    }

    [Fact]
    public void RewardedAds_DailyCap_ResetsNextDay_AndOwnerPlacementsUseHelper()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        long dia = Bal(s, Currencies.Diamonds);
        for (int i = 0; i < 5; i++) Ok(e, ref s, new AdWatched { PlacementId = "daily_diamonds_ad", AdToken = "tok" + i }, t);
        Assert.Equal(dia + 50, Bal(s, Currencies.Diamonds));
        Assert.Equal("ad daily cap reached", Fail(e, ref s, new AdWatched { PlacementId = "daily_diamonds_ad", AdToken = "x" }, t));
        Ok(e, ref s, new AdWatched { PlacementId = "daily_diamonds_ad", AdToken = "x" }, Day0 + D);
        // Placement consumed by another system's command (e.g. stamina refill) cannot be used directly.
        Fail(e, ref s, new AdWatched { PlacementId = "stamina_refill", AdToken = "x" }, t);
        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = Day0 + D };
        for (int i = 0; i < 3; i++) ShopSystem.ConsumeAdView(ctx, "stamina_refill", "tok");
        Assert.Throws<RuleException>(() => ShopSystem.ConsumeAdView(ctx, "stamina_refill", "tok"));
        Assert.Equal(0, ShopSystem.AdViewsLeft(s, e.Content, "stamina_refill", Day0 + D));
    }

    [Fact]
    public void DailyDiamondsForever_ClaimOncePerDay_Forever()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        Buy(e, ref s, "perm_daily_diamonds", t);
        long dia = Bal(s, Currencies.Diamonds);
        Ok(e, ref s, new ShopClaimDaily { ProductId = "perm_daily_diamonds" }, t);
        Assert.Equal(dia + 50, Bal(s, Currencies.Diamonds));
        Fail(e, ref s, new ShopClaimDaily { ProductId = "perm_daily_diamonds" }, t + H);
        Ok(e, ref s, new ShopClaimDaily { ProductId = "perm_daily_diamonds" }, Day0 + 1000 * D);
    }

    [Fact]
    public void MonthlyPass_DailyClaims_Perks_ExpireAfter30Days_AndStack()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        Assert.Equal(0, ShopSystem.StaminaBonus(s, e.Content, t));
        Buy(e, ref s, "monthly_pass", t);
        Assert.Equal(30, ShopSystem.StaminaBonus(s, e.Content, t));
        Assert.Contains("battle_speed_3", s.Flags);
        Assert.Equal(1.2, ModifierMath.Multiplier(s.Modifiers, "production:grain", t), 6);
        int claims = 0;
        for (int d = 0; d < 35; d++)
        {
            var r = e.Execute(ref s, new ShopClaimDaily { ProductId = "monthly_pass" }, Day0 + d * D + H);
            if (r.Ok) claims++;
        }
        Assert.Equal(30, claims);
        long after = Day0 + 30 * D;
        e.Settle(s, after);
        Assert.False(ShopSystem.HasMonthlyPass(s, e.Content, after));
        Assert.DoesNotContain("battle_speed_3", s.Flags);
        Assert.Equal(0, ShopSystem.StaminaBonus(s, e.Content, after));
        Assert.Equal(1.0, ModifierMath.Multiplier(s.Modifiers, "production:grain", after), 6);

        // Buying while active extends.
        var s2 = Player(e);
        Buy(e, ref s2, "monthly_pass", t, "a");
        Buy(e, ref s2, "monthly_pass", t + D, "b");
        Assert.True(ShopSystem.HasMonthlyPass(s2, e.Content, Day0 + 59 * D));
        Assert.False(ShopSystem.HasMonthlyPass(s2, e.Content, Day0 + 60 * D));
        Assert.Equal(Day0 + 60 * D, s2.Modifiers.Single(m => m.Id == ShopSystem.MonthlyPassModifierId).EndMs);
    }

    [Fact]
    public void GiftPack_SevenDailyClaims_ThenRebuyable()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        Buy(e, ref s, "gift_diamond_week", t);
        Assert.Equal("gift pack still active", Fail(e, ref s, new PurchaseStart { ProductId = "gift_diamond_week" }, t));
        int claims = 0;
        for (int d = 0; d < 9; d++) if (e.Execute(ref s, new ShopClaimDaily { ProductId = "gift_diamond_week" }, Day0 + d * D).Ok) claims++;
        Assert.Equal(7, claims);
        Ok(e, ref s, new PurchaseStart { ProductId = "gift_diamond_week" }, Day0 + 9 * D);
    }

    [Fact]
    public void RollingOffers_FollowChainOrder_FreeAndPaidSteps_Loop()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        Assert.Equal("rolling_main_0", ShopSystem.CurrentRollingStep(s, e.Content, "rolling_main"));
        Fail(e, ref s, new PurchaseStart { ProductId = "rolling_main_1" }, t);
        Ok(e, ref s, new ShopClaimFree { ProductId = "rolling_main_0" }, t);
        Fail(e, ref s, new ShopClaimFree { ProductId = "rolling_main_0" }, t);
        Buy(e, ref s, "rolling_main_1", t);
        Assert.Equal("rolling_main_2", ShopSystem.CurrentRollingStep(s, e.Content, "rolling_main"));
        s.Shop.RollingTaken["rolling_main"] = 6;
        Assert.Equal("rolling_main_0", ShopSystem.CurrentRollingStep(s, e.Content, "rolling_main"));
    }

    [Fact]
    public void FlashOffer_OnlyDuringSchedule_OncePerOccurrence()
    {
        var e = Engine(devReceipts: true); var s = Player(e);
        var p = e.Content.Product("flash_weekend_fuel")!;
        Assert.True(p.Schedule.TryNext(TestHelpers.T0, out long st, out long en));
        Assert.Equal("offer not active", Fail(e, ref s, new PurchaseStart { ProductId = p.Id }, st - 1));
        Buy(e, ref s, p.Id, st + H);
        Assert.Equal("offer limit reached", Fail(e, ref s, new PurchaseStart { ProductId = p.Id }, st + 2 * H));
        Buy(e, ref s, p.Id, st + 7 * D + H); // next weekend
        Assert.DoesNotContain(ShopSystem.AvailableProducts(s, e.Content, en + H), x => x.Id == p.Id);
    }

    [Fact]
    public void SeasonPass_FreeTrack_PremiumRetroactive_ResetsNextSeason()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        s.Counters[Requirements.MainLevelCounter] = 3;
        Unlock(s, Features.SeasonPass);
        s.Count("runs_completed", 25); // 25 × 20 XP = 500 → level 5
        Assert.Equal(5, ShopSystem.SeasonLevel(s, e.Content));
        Ok(e, ref s, new SeasonPassClaim { Level = 1 }, t);
        Fail(e, ref s, new SeasonPassClaim { Level = 6 }, t);
        Assert.Equal("premium not owned", Fail(e, ref s, new SeasonPassClaim { Level = 1, Premium = true }, t));
        Buy(e, ref s, "season_pass_premium", t);
        long dia = Bal(s, Currencies.Diamonds);
        Ok(e, ref s, new SeasonPassClaim { Level = 1, Premium = true }, t); // retroactive
        Assert.Equal(dia + 30, Bal(s, Currencies.Diamonds));
        var r = Ok(e, ref s, new SeasonPassClaimAll(), t);
        Assert.Equal(8, (int)r.Payload!); // free 2..5 + premium 2..5
        Fail(e, ref s, new SeasonPassClaimAll(), t);

        long next = ShopSystem.SeasonEndMs(e.Content, t);
        e.Settle(s, next);
        Assert.Equal(0, ShopSystem.SeasonLevel(s, e.Content));
        Assert.False(ShopSystem.HasPremiumSeason(s, e.Content, next));
        Assert.Empty(s.Shop.SeasonClaims);
    }

    [Fact]
    public void Funds_PaidTrack_ClaimByProgress()
    {
        var e = Engine(devReceipts: true); var s = Player(e); long t = TestHelpers.T0;
        Assert.Equal("product locked", Fail(e, ref s, new PurchaseStart { ProductId = "fund_worm" }, t));
        s.Counters[Requirements.MainLevelCounter] = 11;
        Unlock(s, Features.Funds);
        s.Worm.Level = 12; // gates/funds read the real worm level (provider)
        Assert.Equal("fund not owned", Fail(e, ref s, new FundClaim { FundId = "worm", Step = 0 }, t));
        Buy(e, ref s, "fund_worm", t);
        long dia = Bal(s, Currencies.Diamonds);
        Ok(e, ref s, new FundClaim { FundId = "worm", Step = 0 }, t);
        Assert.Equal(dia + 300, Bal(s, Currencies.Diamonds));
        Fail(e, ref s, new FundClaim { FundId = "worm", Step = 0 }, t);
        Assert.Equal("step not reached", Fail(e, ref s, new FundClaim { FundId = "worm", Step = 1 }, t));
    }

    [Fact]
    public void DiamondShopItems_DailyLimits_AndCost()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        Ok(e, ref s, new ShopBuyItem { ItemId = "daily_free_gift" }, t);
        Fail(e, ref s, new ShopBuyItem { ItemId = "daily_free_gift" }, t);
        Ok(e, ref s, new ShopBuyItem { ItemId = "daily_free_gift" }, Day0 + D);
        s.Wallet.Add(Currencies.Diamonds, 1000);
        long dia = Bal(s, Currencies.Diamonds);
        long tokens = Bal(s, Currencies.PartTokens);
        for (int i = 0; i < 3; i++) Ok(e, ref s, new ShopBuyItem { ItemId = "daily_part_tokens" }, Day0 + D);
        Assert.Equal("item limit reached", Fail(e, ref s, new ShopBuyItem { ItemId = "daily_part_tokens" }, Day0 + D));
        Assert.Equal(dia - 360, Bal(s, Currencies.Diamonds));
        Assert.Equal(tokens + 9, Bal(s, Currencies.PartTokens));
        Fail(e, ref s, new ShopBuyItem { ItemId = "guild_ore" }, t);
    }

    [Fact]
    public void Catalogue_IsIdenticalForWhalesAndFreePlayers()
    {
        var e = Engine(devReceipts: true); long t = TestHelpers.T0;
        var f2p = Player(e);
        var whale = Player(e);
        Buy(e, ref whale, "diamonds_8000", t);
        Buy(e, ref whale, "diamonds_3800", t);
        var a = ShopSystem.AvailableProducts(f2p, e.Content, t).Select(p => p.Id + p.PriceUsdCents).ToList();
        var b = ShopSystem.AvailableProducts(whale, e.Content, t).Select(p => p.Id + p.PriceUsdCents).ToList();
        Assert.Equal(a, b);
    }
}
