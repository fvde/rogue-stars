using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using RogueStars.Rules.Core;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Payments;

namespace RogueStars.Server.Tests;

/// <summary>
/// AppLovin MAX S2S rewarded callbacks. Vectors are built exactly as documented:
/// https://support.applovin.com/en/max/advanced-features/s2s-rewarded-callback-api and
/// https://support.applovin.com/en/max/faq/how-server-to-server-callback-works.
/// </summary>
public class AppLovinAdsTests : IDisposable
{
    private const string Key = "test-event-key";
    private readonly TestApp _app = new();

    public AppLovinAdsTests()
    {
        _app.Settings["RogueStars:Ads:Mode"] = "applovin";
        _app.Settings["RogueStars:Ads:EventKey"] = Key;
    }

    public void Dispose() => _app.Dispose();

    private static string Sha1Hex(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    private static string Sha256Hex(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private static AppLovinAdVerifier Verifier(string key = Key, string mode = "event_token", params string[] adUnits)
    {
        var o = new ServerOptions();
        o.Ads.Mode = "applovin";
        o.Ads.EventKey = key;
        o.Ads.Verification = mode;
        o.Ads.AllowedAdUnitIds.AddRange(adUnits);
        return new AppLovinAdVerifier(Microsoft.Extensions.Options.Options.Create(o), NullLogger<AppLovinAdVerifier>.Instance);
    }

    private static QueryCollection Query(params (string K, string V)[] kv) =>
        new(kv.ToDictionary(x => x.K, x => new StringValues(x.V)));

    /// <summary>The worked example of AppLovin's FAQ: sha1(14a97…30bc + uHXO…yYl_) = 3724081c…34e9.</summary>
    [Fact]
    public void EventToken_MatchesAppLovinWorkedExample()
    {
        const string eventKey = "uHXOnmfO2f3wd-syBwXxsj7m53qiJoI9VzXTvE8LOSnIWjS6zzxS8HgSIBJSbQWdS1czmgwXSWDaKwehtjyYl_";
        const string eventId = "14a97273742c0c9e8864fe40193dad5506e930bc";
        Assert.Equal("3724081c17d32a66276ffff2a14b124a278934e9", AppLovinAdVerifier.EventToken(eventId, eventKey));
        var v = Verifier(eventKey);
        Assert.Equal(AdVerifyStatus.Ok, v.Verify(Query(("user_id", "p1"), ("event_id", eventId), ("event_token", "3724081c17d32a66276ffff2a14b124a278934e9"))).Status);
    }

    [Fact]
    public void EventToken_RejectsWrongKeyTamperedEventAndMissingToken()
    {
        string ev = "a7c3e1b2-4f59-4d8a-9c21-8e6b0f3a1d47";
        string token = Sha1Hex(ev + Key);
        Assert.Equal(AdVerifyStatus.Ok, Verifier().Verify(Query(("user_id", "p1"), ("event_id", ev), ("event_token", token.ToUpperInvariant()))).Status);
        Assert.Equal(AdVerifyStatus.InvalidSignature, Verifier("other-key").Verify(Query(("user_id", "p1"), ("event_id", ev), ("event_token", token))).Status);
        Assert.Equal(AdVerifyStatus.InvalidSignature, Verifier().Verify(Query(("user_id", "p1"), ("event_id", ev + "x"), ("event_token", token))).Status);
        Assert.Equal(AdVerifyStatus.InvalidSignature, Verifier().Verify(Query(("user_id", "p1"), ("event_id", ev))).Status);
        // sha256 of the same input (the old assumption) is not the documented token.
        Assert.Equal(AdVerifyStatus.InvalidSignature, Verifier().Verify(Query(("user_id", "p1"), ("event_id", ev), ("event_token", Sha256Hex(ev + Key)))).Status);
        Assert.Equal(AdVerifyStatus.InvalidSignature, Verifier("").Verify(Query(("user_id", "p1"), ("event_id", ev), ("event_token", token))).Status);
        Assert.Equal(AdVerifyStatus.Malformed, Verifier().Verify(Query(("event_id", ev), ("event_token", token))).Status);
    }

    [Fact]
    public void EventTokenAll_HashesDecodedMacroValuesInMacroOrderPlusKey_WithoutEventToken()
    {
        string ev = "a7c3e1b2-4f59-4d8a-9c21-8e6b0f3a1d47";
        // Macro order: AD_UNIT_ID < CUSTOM_DATA < EVENT_ID < PLACEMENT < USER_ID; EVENT_TOKEN is omitted.
        string all = Sha256Hex("9ad0816ac071552a" + "revive" + ev + "Run revive" + "player 1" + Key);
        var q = Query(("user_id", "player 1"), ("event_id", ev), ("custom_data", "revive"), ("placement", "Run revive"),
            ("ad_unit_id", "9ad0816ac071552a"), ("event_token", Sha1Hex(ev + Key)), ("event_token_all", all));
        Assert.Equal(all, AppLovinAdVerifier.EventTokenAll(q, Key));
        Assert.Equal(AdVerifyStatus.Ok, Verifier(mode: "event_token_all").Verify(q).Status);
        var forged = Query(("user_id", "attacker"), ("event_id", ev), ("custom_data", "revive"), ("placement", "Run revive"),
            ("ad_unit_id", "9ad0816ac071552a"), ("event_token", Sha1Hex(ev + Key)), ("event_token_all", all));
        Assert.Equal(AdVerifyStatus.InvalidSignature, Verifier(mode: "event_token_all").Verify(forged).Status);
    }

    [Fact]
    public void AllowedAdUnits_AreEnforced()
    {
        string ev = "e1";
        var q = Query(("user_id", "p"), ("event_id", ev), ("ad_unit_id", "aaaa"), ("event_token", Sha1Hex(ev + Key)));
        Assert.Equal(AdVerifyStatus.AdUnitNotAllowed, Verifier(Key, "event_token", "bbbb").Verify(q).Status);
        Assert.Equal(AdVerifyStatus.Ok, Verifier(Key, "event_token", "AAAA").Verify(q).Status);
    }

    private static string Callback(string user, string eventId, string placement, string? token = null) =>
        $"/v1/ads/ssv?user_id={Uri.EscapeDataString(user)}&event_id={eventId}&custom_data={Uri.EscapeDataString(placement)}" +
        $"&placement={Uri.EscapeDataString(placement)}&ad_unit_id=9ad0816ac071552a&event_token={token ?? Sha1Hex(eventId + Key)}";

    [Fact]
    public async Task SignedCallback_GrantsOnce_ReplayIsDuplicate_ForgedUserIsRejected()
    {
        var p = await _app.PlayerAsync("viewer");
        var other = await _app.PlayerAsync("other");
        long diamonds = await p.BalanceAsync(Currencies.Diamonds);
        var http = _app.CreateClient();
        string ev = Guid.NewGuid().ToString();

        Assert.Equal(401, (int)(await http.GetAsync(Callback(p.PlayerId, ev, "daily_diamonds_ad", token: "deadbeef"))).StatusCode);
        var first = await PlayerClient.ReadWithStatus(await http.GetAsync(Callback(p.PlayerId, ev, "daily_diamonds_ad")));
        Assert.Equal(200, first.Status);
        Assert.Equal("granted", first.Body.GetProperty("outcome").GetString());
        // AppLovin retry / attacker replay of the same callback.
        var again = await PlayerClient.ReadWithStatus(await http.GetAsync(Callback(p.PlayerId, ev, "daily_diamonds_ad")));
        Assert.True(again.Body.GetProperty("duplicate").GetBoolean());
        // EVENT_TOKEN does not cover user_id: a captured event re-targeted at another player is refused by the claim.
        Assert.Equal(409, (int)(await http.GetAsync(Callback(other.PlayerId, ev, "daily_diamonds_ad"))).StatusCode);
        Assert.Equal(diamonds + 10, await p.BalanceAsync(Currencies.Diamonds));
        Assert.Equal(0, await other.CounterAsync("ads_watched"));
    }

    [Fact]
    public async Task Replay_IsRejected_EvenAfterThePlayersRequestWindowMovedOn()
    {
        _app.Settings["RogueStars:Store:IdempotencyWindow"] = "1";
        var p = await _app.PlayerAsync("viewer");
        var http = _app.CreateClient();
        string ev = Guid.NewGuid().ToString();
        Assert.Equal(200, (int)(await http.GetAsync(Callback(p.PlayerId, ev, "daily_diamonds_ad"))).StatusCode);
        await p.CommandAsync("BuyStamina", new { method = 2 }, "evict-1"); // pushes the ad request id out of the window
        var dup = await PlayerClient.Read(await http.GetAsync(Callback(p.PlayerId, ev, "daily_diamonds_ad")));
        Assert.True(dup.GetProperty("duplicate").GetBoolean());
        Assert.Equal(1, await p.CounterAsync("ads_watched"));
    }

    [Fact]
    public async Task UnknownPlacement_IsRecordedNotGranted()
    {
        var p = await _app.PlayerAsync("viewer");
        var body = await PlayerClient.Read(await _app.CreateClient().GetAsync(Callback(p.PlayerId, "ev-x", "no_such_placement")));
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("unknown_placement", body.GetProperty("outcome").GetString());
        Assert.Equal(0, await p.CounterAsync("ads_watched"));
    }

    [Fact]
    public async Task SystemPlacement_CreditsTheEvent_CommandSpendsItOnce()
    {
        var p = await _app.PlayerAsync("viewer");
        var http = _app.CreateClient();
        long stamina = await p.BalanceAsync(Currencies.Stamina);

        // Unverified ad token: refused before the rules run.
        Assert.Equal(403, (await p.CommandAsync("BuyStamina", new { method = 1, adToken = "made-up" }, "r1")).Status);

        string ev = Guid.NewGuid().ToString();
        var cb = await PlayerClient.Read(await http.GetAsync(Callback(p.PlayerId, ev, "stamina_refill")));
        Assert.Equal("credited", cb.GetProperty("outcome").GetString());
        var credits = (await p.GetAsync("/v1/ads/credits")).Body.GetProperty("credits");
        Assert.Equal(ev, credits[0].GetProperty("eventId").GetString());
        Assert.Equal("stamina_refill", credits[0].GetProperty("placement").GetString());

        // A rules rejection refunds the credit.
        var rejected = await p.CommandAsync("BuyStamina", new { method = 99, adToken = ev }, "r2");
        Assert.False(rejected.Body.GetProperty("ok").GetBoolean());
        Assert.Single((await p.GetAsync("/v1/ads/credits")).Body.GetProperty("credits").EnumerateArray());

        var ok = await p.CommandAsync("BuyStamina", new { method = 1, adToken = ev }, "r3");
        Assert.True(ok.Body.GetProperty("ok").GetBoolean());
        Assert.True(await p.BalanceAsync(Currencies.Stamina) > stamina);
        // Same request id again = idempotent replay; a new request id cannot spend the token twice.
        Assert.True((await p.CommandAsync("BuyStamina", new { method = 1, adToken = ev }, "r3")).Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(403, (await p.CommandAsync("BuyStamina", new { method = 1, adToken = ev }, "r4")).Status);
        Assert.Equal(400, (await p.CommandAsync("BuyStamina", new { method = 1, adToken = ev })).Status);
        Assert.Empty((await p.GetAsync("/v1/ads/credits")).Body.GetProperty("credits").EnumerateArray());

        // Credits are per player.
        var other = await _app.PlayerAsync("other");
        string ev2 = Guid.NewGuid().ToString();
        await http.GetAsync(Callback(p.PlayerId, ev2, "stamina_refill"));
        Assert.Equal(403, (await other.CommandAsync("BuyStamina", new { method = 1, adToken = ev2 }, "o1")).Status);
    }

    [Fact]
    public async Task FakeMode_DoesNotCheckAdTokens()
    {
        using var app = new TestApp();
        var p = await app.PlayerAsync("viewer");
        Assert.True((await p.CommandAsync("BuyStamina", new { method = 1, adToken = "fake-ad-1" }, "f1")).Body.GetProperty("ok").GetBoolean());
    }
}
