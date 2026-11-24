using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Server.Tests;

/// <summary>
/// Summon events through the authoritative command endpoint (docs/design/summon-events.md): the server knows no summon
/// command by name; the rules validate pulls, claims and purchases, and the state persists between requests.
/// </summary>
public class SummonEventServerTests : IDisposable
{
    private readonly TestApp _app = new();

    public SummonEventServerTests() => _app.Settings["RogueStars:Ads:Mode"] = "disabled";

    public void Dispose() => _app.Dispose();

    private static bool Ok((int Status, System.Text.Json.JsonElement Body) r) => r.Status == 200 && r.Body.GetProperty("ok").GetBoolean();

    [Fact]
    public async Task AdTicket_Pull_AndTheStateCarriesEventLuckAndTokens()
    {
        var p = await _app.PlayerAsync("summoner");
        var def = ContentDb.Default.SummonType(SummonEventType.Hero)!;
        // No tickets yet: the rules refuse, nothing changes.
        var refused = await p.CommandAsync("SummonPull", new { eventType = 0, count = 1, payment = 0 });
        Assert.False(refused.Body.GetProperty("ok").GetBoolean());
        Assert.Contains("not enough", refused.Body.GetProperty("error").GetString());

        // The event's ad ticket is instant while ads are disabled.
        Assert.True((await p.PostAsync("/v1/ads/instant", new { placementId = def.AdPlacementId, requestId = "t1" })).Body.GetProperty("ok").GetBoolean());
        Assert.Equal(1, await p.BalanceAsync(def.TicketCurrency));

        var pull = await p.CommandAsync("SummonPull", new { eventType = 0, count = 1, payment = 0 }, "pull-1");
        Assert.True(Ok(pull), pull.Body.ToString());
        var payload = pull.Body.GetProperty("payload");
        Assert.Single(payload.GetProperty("items").EnumerateArray());
        Assert.Equal(def.TokensPerPull, payload.GetProperty("tokens").GetInt64());
        Assert.Equal(def.Gacha.MythicPity, payload.GetProperty("luckMax").GetInt32());

        // Replaying the same request id does not pull again.
        var again = await p.CommandAsync("SummonPull", new { eventType = 0, count = 1, payment = 0 }, "pull-1");
        Assert.True(again.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(1, await p.CounterAsync("summon_pulls_hero"));
        Assert.Equal(0, await p.BalanceAsync(def.TicketCurrency));
        Assert.Equal(def.TokensPerPull, await p.BalanceAsync(def.TokenCurrency));

        var state = await p.StateAsync();
        var hero = state.GetProperty("summons").GetProperty("events").GetProperty("Hero");
        Assert.Equal(1, hero.GetProperty("pulls").GetInt32());
        Assert.StartsWith("hero@", hero.GetProperty("eventId").GetString());
        Assert.True(state.GetProperty("summons").GetProperty("luck").TryGetProperty("Hero", out _));
    }

    [Fact]
    public async Task PassPurchase_FakeReceipt_UnlocksThePremiumTrack_ClaimsAreValidated()
    {
        var p = await _app.PlayerAsync("passbuyer");
        var def = ContentDb.Default.SummonType(SummonEventType.Mount)!;
        Assert.Contains("premium", (await p.CommandAsync("SummonPassClaim", new { eventType = 1, day = 1, premium = true })).Body.GetProperty("error").GetString());
        Assert.True(Ok(await p.CommandAsync("SummonPassClaim", new { eventType = 1, day = 1, premium = false })));
        Assert.True(Ok(await p.CommandAsync("PurchaseStart", new { productId = def.PassProductId })));
        var (s, body) = await p.PostAsync("/v1/iap/verify", new { productId = def.PassProductId, purchaseToken = "fake-pass-1" });
        Assert.Equal(200, s);
        Assert.True(body.GetProperty("ok").GetBoolean(), body.ToString());
        var claim = await p.CommandAsync("SummonPassClaim", new { eventType = 1, day = 1, premium = true });
        Assert.True(Ok(claim), claim.Body.ToString());
        Assert.Equal(def.Pass[0].Free[0].Amount + def.Pass[0].Premium[0].Amount, await p.BalanceAsync(def.TicketCurrency));
        // The pass cannot be bought twice for the same event.
        Assert.False((await p.CommandAsync("PurchaseStart", new { productId = def.PassProductId })).Body.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Exchange_And_TaskClaims_AreRuleChecked()
    {
        var p = await _app.PlayerAsync("trader");
        var nothing = await p.CommandAsync("SummonTaskClaimAll", new { eventType = 0 });
        Assert.False(nothing.Body.GetProperty("ok").GetBoolean());
        Assert.Equal("nothing to claim", nothing.Body.GetProperty("error").GetString());
        var broke = await p.CommandAsync("SummonExchangeBuy", new { eventType = 0, itemId = "hero_xp" });
        Assert.Contains("not enough", broke.Body.GetProperty("error").GetString());
        Assert.False((await p.CommandAsync("SummonExchangeBuy", new { eventType = 0, itemId = "nope" })).Body.GetProperty("ok").GetBoolean());
    }
}
