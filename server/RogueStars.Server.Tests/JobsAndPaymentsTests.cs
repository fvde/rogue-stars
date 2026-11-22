using RogueStars.Rules.Core;
using System.Net.Http.Json;

namespace RogueStars.Server.Tests;

public class JobsAndPaymentsTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Jobs_RequireSecret()
    {
        Assert.Equal(401, (int)(await _app.JobClient(null).PostAsync("/internal/jobs/daily-reset", null)).StatusCode);
        Assert.Equal(401, (int)(await _app.JobClient("wrong").PostAsync("/internal/jobs/daily-reset", null)).StatusCode);
        Assert.Equal(200, (int)(await _app.JobClient().PostAsync("/internal/jobs/daily-reset", null)).StatusCode);
        Assert.Equal(404, (int)(await _app.JobClient().PostAsync("/internal/jobs/nope", null)).StatusCode);
        Assert.Equal(401, (int)(await _app.JobClient(null).PostAsJsonAsync("/internal/invasions", new { id = "x", startsMs = 1, endsMs = 2 })).StatusCode);
    }

    [Fact]
    public async Task Jobs_OidcMode_RejectsSecretAndMissingToken()
    {
        using var app = new TestApp();
        app.Settings["RogueStars:Jobs:Mode"] = "oidc";
        app.Settings["RogueStars:Jobs:Audience"] = "https://example.run.app";
        app.Settings["RogueStars:Jobs:ServiceAccountEmail"] = "scheduler@example.iam.gserviceaccount.com";
        Assert.Equal(401, (int)(await app.JobClient().PostAsync("/internal/jobs/daily-reset", null)).StatusCode);
    }

    [Fact]
    public async Task Iap_FakeReceipt_CompletesPurchaseOnce_AndCannotBeReused()
    {
        var p = await _app.PlayerAsync("buyer");
        long diamonds = await p.BalanceAsync(Currencies.Diamonds);

        // Rules require the client's PurchaseStart (pending record) before the server completes the purchase.
        var (s0, early) = await p.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "fake-tok1" });
        Assert.Equal(200, s0);
        Assert.False(early.GetProperty("ok").GetBoolean());
        Assert.Equal("no pending purchase", early.GetProperty("error").GetString());

        Assert.True((await p.CommandAsync("PurchaseStart", new { productId = "diamonds_60" })).Body.GetProperty("ok").GetBoolean());
        Assert.Equal(403, (await p.CommandAsync("PurchaseComplete", new { productId = "diamonds_60", receiptId = "x" })).Status);
        var (s, body) = await p.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "fake-tok1" });
        Assert.Equal(200, s);
        Assert.True(body.GetProperty("ok").GetBoolean());
        // Real PurchaseComplete: 60 Diamonds + first-purchase bonus 60.
        Assert.Equal(diamonds + 120, await p.BalanceAsync(Currencies.Diamonds));
        Assert.Equal(1, await p.CounterAsync("iap_purchases"));

        var (s2, again) = await p.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "fake-tok1" });
        Assert.Equal(200, s2);
        Assert.True(again.GetProperty("alreadyCompleted").GetBoolean());
        Assert.Equal(diamonds + 120, await p.BalanceAsync(Currencies.Diamonds));

        var thief = await _app.PlayerAsync("thief");
        Assert.Equal(409, (await thief.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "fake-tok1" })).Status);
        Assert.Equal(402, (await thief.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "forged" })).Status);
    }

    [Fact]
    public async Task Iap_WithoutPurchaseCommand_Is501()
    {
        _app.DisableHook("PurchaseComplete");
        var p = await _app.PlayerAsync("buyer");
        Assert.Equal(501, (await p.PostAsync("/v1/iap/verify", new { productId = "x", purchaseToken = "fake-1" })).Status);
    }

    [Fact]
    public async Task AdsSsv_GrantsOncePerEvent()
    {
        var p = await _app.PlayerAsync("viewer");
        long diamonds = await p.BalanceAsync(Currencies.Diamonds);
        var http = _app.CreateClient();
        // Real AdWatched on a placement with direct rewards (Diamond snack: +10, daily cap via ShopSystem.ConsumeAdView).
        var url = $"/v1/ads/ssv?user_id={p.PlayerId}&event_id=ev1&custom_data=daily_diamonds_ad";
        var first = await PlayerClient.Read(await http.GetAsync(url));
        Assert.True(first.GetProperty("ok").GetBoolean());
        var dup = await PlayerClient.Read(await http.GetAsync(url));
        Assert.True(dup.GetProperty("duplicate").GetBoolean());
        Assert.Equal(1, await p.CounterAsync("ads_watched"));
        Assert.Equal(diamonds + 10, await p.BalanceAsync(Currencies.Diamonds));
        Assert.Equal(401, (int)(await http.GetAsync("/v1/ads/ssv?event_id=x")).StatusCode);
    }

    [Fact]
    public async Task AdsSsv_WithoutAdCommand_Is501()
    {
        _app.DisableHook("AdWatched");
        var p = await _app.PlayerAsync("viewer");
        var resp = await _app.CreateClient().GetAsync($"/v1/ads/ssv?user_id={p.PlayerId}&event_id=ev2&custom_data=daily_diamonds_ad");
        Assert.Equal(501, (int)resp.StatusCode);
    }

    [Fact]
    public void AppLovinVerifier_ChecksHash()
    {
        var o = Microsoft.Extensions.Options.Options.Create(new RogueStars.Server.Infrastructure.ServerOptions());
        o.Value.Ads.EventKey = "key";
        var v = new RogueStars.Server.Payments.AppLovinAdVerifier(o);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("ev1key"))).ToLowerInvariant();
        var good = new Microsoft.AspNetCore.Http.QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["user_id"] = "u", ["event_id"] = "ev1", ["event_token"] = hash,
        });
        Assert.NotNull(v.Verify(good));
        var bad = new Microsoft.AspNetCore.Http.QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["user_id"] = "u", ["event_id"] = "ev1", ["event_token"] = "00",
        });
        Assert.Null(v.Verify(bad));
    }
}
