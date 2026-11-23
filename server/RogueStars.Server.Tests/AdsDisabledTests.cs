using RogueStars.Rules.Core;
using RogueStars.Rules.Content;
using RogueStars.Server.Infrastructure;

namespace RogueStars.Server.Tests;

/// <summary>
/// Global ads-disabled mode (<c>Ads:Mode=disabled</c>, the production default for now): every player is treated as a No Ads
/// owner by the rules, no ad network is involved, and the anti-cheat checks of the applovin mode stay in place for when
/// ads are switched on.
/// </summary>
public class AdsDisabledTests : IDisposable
{
    private readonly TestApp _app = new();

    public AdsDisabledTests() => _app.Settings["RogueStars:Ads:Mode"] = "disabled";

    public void Dispose() => _app.Dispose();

    [Fact]
    public void ProductionDefault_IsDisabled_AndNeverMutatesTheSharedContent()
    {
        Assert.True(new ServerOptions().Ads.IsDisabled);
        var content = ServerContent.Create(new ServerOptions());
        Assert.True(content.Shop.AdsDisabled);
        Assert.NotSame(ContentDb.Default, content);
        Assert.False(ContentDb.Default.Shop.AdsDisabled);
        var applovin = new ServerOptions { Ads = { Mode = "applovin" } };
        Assert.Same(ContentDb.Default, ServerContent.Create(applovin));
    }

    [Fact]
    public async Task Session_ReportsTheMode_AndCommandPlacementsCompleteWithoutAToken()
    {
        var p = await _app.PlayerAsync("noads");
        var session = await p.PostAsync("/v1/session", new { });
        Assert.True(session.Body.GetProperty("adsDisabled").GetBoolean());
        long stamina = await p.BalanceAsync(Currencies.Stamina);
        var ok = await p.CommandAsync("BuyStamina", new { method = 1, adToken = "" }, "s1");
        Assert.True(ok.Body.GetProperty("ok").GetBoolean(), ok.Body.ToString());
        Assert.True(await p.BalanceAsync(Currencies.Stamina) > stamina);
        // A made-up token still has to be a verified credit (the check is not switched off).
        Assert.Equal(403, (await p.CommandAsync("BuyStamina", new { method = 1, adToken = "made-up" }, "s2")).Status);
    }

    [Fact]
    public async Task DirectRewardPlacement_IsGrantedInstantly_WithinTheDailyCap()
    {
        var p = await _app.PlayerAsync("noads");
        long diamonds = await p.BalanceAsync(Currencies.Diamonds);
        var r = await p.PostAsync("/v1/ads/instant", new { placementId = "daily_diamonds_ad", requestId = "a1" });
        Assert.True(r.Body.GetProperty("ok").GetBoolean(), r.Body.ToString());
        Assert.Equal(diamonds + 10, await p.BalanceAsync(Currencies.Diamonds));
        // Same request id: idempotent replay, no second grant.
        Assert.True((await p.PostAsync("/v1/ads/instant", new { placementId = "daily_diamonds_ad", requestId = "a1" })).Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(diamonds + 10, await p.BalanceAsync(Currencies.Diamonds));
        int cap = ContentDb.Default.AdPlacement("daily_diamonds_ad")!.DailyCap;
        for (int i = 2; i <= cap; i++) Assert.True((await p.PostAsync("/v1/ads/instant", new { placementId = "daily_diamonds_ad", requestId = "a" + i })).Body.GetProperty("ok").GetBoolean());
        var capped = await p.PostAsync("/v1/ads/instant", new { placementId = "daily_diamonds_ad", requestId = "over" });
        Assert.False(capped.Body.GetProperty("ok").GetBoolean());
        Assert.Equal("ad daily cap reached", capped.Body.GetProperty("error").GetString());

        Assert.Equal(400, (await p.PostAsync("/v1/ads/instant", new { placementId = "stamina_refill", requestId = "x" })).Status);
        Assert.Equal(400, (await p.PostAsync("/v1/ads/instant", new { placementId = "nope", requestId = "y" })).Status);
        Assert.Equal(400, (await p.PostAsync("/v1/ads/instant", new { placementId = "daily_diamonds_ad" })).Status);
    }

    [Fact]
    public async Task S2SCallbacks_AreRefused()
    {
        var p = await _app.PlayerAsync("noads");
        var resp = await _app.CreateClient().GetAsync($"/v1/ads/ssv?user_id={p.PlayerId}&event_id=e1&custom_data=daily_diamonds_ad&event_token=x");
        Assert.Equal(404, (int)resp.StatusCode);
        Assert.Equal(0, await p.CounterAsync("ads_watched"));
    }

    [Fact]
    public async Task InstantGrant_WhenAdsAreOn_NeedsTheNoAdsEntitlement()
    {
        using var app = new TestApp();
        app.Settings["RogueStars:Ads:Mode"] = "applovin";
        app.Settings["RogueStars:Ads:EventKey"] = "k";
        var p = await app.PlayerAsync("viewer");
        var session = await p.PostAsync("/v1/session", new { });
        Assert.False(session.Body.GetProperty("adsDisabled").GetBoolean());
        var r = await p.PostAsync("/v1/ads/instant", new { placementId = "daily_diamonds_ad", requestId = "a1" });
        Assert.False(r.Body.GetProperty("ok").GetBoolean());
        Assert.Equal("ad token required", r.Body.GetProperty("error").GetString());
        var noToken = await p.CommandAsync("BuyStamina", new { method = 1, adToken = "" }, "s1");
        Assert.False(noToken.Body.GetProperty("ok").GetBoolean());
        Assert.Equal("ad token required", noToken.Body.GetProperty("error").GetString());
    }
}
