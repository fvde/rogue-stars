using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;
using RogueStars.Server.Commands;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Payments;

public sealed record ReceiptResult(bool Valid, string? OrderId, string? Error, bool Test = false);

/// <summary>Validates a store purchase token for a product.</summary>
public interface IReceiptValidator
{
    Task<ReceiptResult> ValidateAsync(string productId, string purchaseToken, CancellationToken ct);
}

/// <summary>Dev/test: tokens starting with "fake-" are valid; the order id is derived from the token.</summary>
public sealed class FakeReceiptValidator : IReceiptValidator
{
    public Task<ReceiptResult> ValidateAsync(string productId, string purchaseToken, CancellationToken ct) =>
        Task.FromResult(purchaseToken.StartsWith("fake-", StringComparison.Ordinal)
            ? new ReceiptResult(true, "FAKE." + purchaseToken[5..], null, Test: true)
            : new ReceiptResult(false, null, "invalid_receipt"));
}

/// <summary>
/// Google Play: <c>GET androidpublisher/v3/applications/{package}/purchases/products/{productId}/tokens/{token}</c>
/// (purchaseState 0 = purchased). Credentials: the Play service account JSON (Secret Manager) or ADC.
/// Consumption/acknowledgement is done by the client Billing Library after the server confirms.
/// </summary>
public sealed class GooglePlayReceiptValidator(HttpClient http, IOptions<ServerOptions> options, ILogger<GooglePlayReceiptValidator> log)
    : IReceiptValidator
{
    private const string Scope = "https://www.googleapis.com/auth/androidpublisher";
    private ICredential? _credential;

    public async Task<ReceiptResult> ValidateAsync(string productId, string purchaseToken, CancellationToken ct)
    {
        var o = options.Value.Iap;
        if (string.IsNullOrEmpty(o.PackageName)) throw new InvalidOperationException("RogueStars:Iap:PackageName is required");
        _credential ??= await CreateCredentialAsync(o, ct);
        string token = await _credential.GetAccessTokenForRequestAsync(Scope, ct);
        string url = $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{Uri.EscapeDataString(o.PackageName)}" +
                     $"/purchases/products/{Uri.EscapeDataString(productId)}/tokens/{Uri.EscapeDataString(purchaseToken)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning("Play purchase lookup failed: {Status}", (int)resp.StatusCode);
            return new ReceiptResult(false, null, resp.StatusCode == System.Net.HttpStatusCode.NotFound ? "invalid_receipt" : "store_unavailable");
        }
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        int state = root.TryGetProperty("purchaseState", out var ps) ? ps.GetInt32() : -1;
        string? orderId = root.TryGetProperty("orderId", out var oid) ? oid.GetString() : null;
        bool test = root.TryGetProperty("purchaseType", out var pt) && pt.GetInt32() == 0;
        return state == 0 && orderId != null
            ? new ReceiptResult(true, orderId, null, test)
            : new ReceiptResult(false, orderId, state == 2 ? "purchase_pending" : "purchase_not_completed");
    }

    private static async Task<ICredential> CreateCredentialAsync(IapOptions o, CancellationToken ct)
    {
        GoogleCredential cred = string.IsNullOrWhiteSpace(o.ServiceAccountJson)
            ? await GoogleCredential.GetApplicationDefaultAsync(ct)
            : CredentialFactory.FromJson<ServiceAccountCredential>(o.ServiceAccountJson).ToGoogleCredential();
        return cred.CreateScoped(Scope);
    }
}

public sealed record AdEvent(string PlayerId, string EventId, string? Placement, string? AdUnitId);

/// <summary>Verifies a rewarded-ad server-to-server callback. Returns null when the signature is invalid.</summary>
public interface IAdVerifier
{
    AdEvent? Verify(IQueryCollection query);
}

/// <summary>Dev/test: accepts any callback with user_id and event_id.</summary>
public sealed class FakeAdVerifier : IAdVerifier
{
    public AdEvent? Verify(IQueryCollection q) => AppLovinAdVerifier.Parse(q);
}

/// <summary>
/// AppLovin MAX S2S rewarded callback. The callback URL template configured in the MAX dashboard must pass
/// <c>user_id={USER_ID}&amp;event_id={EVENT_ID}&amp;custom_data={CUSTOM_DATA}&amp;ad_unit_id={AD_UNIT_ID}&amp;event_token={EVENT_TOKEN}</c>.
/// Verification: hex(hash(event_id + event key)) must equal the token parameter. Confirm parameter name and hash
/// algorithm against the current AppLovin documentation before launch (both are configurable).
/// </summary>
public sealed class AppLovinAdVerifier(IOptions<ServerOptions> options) : IAdVerifier
{
    public AdEvent? Verify(IQueryCollection q)
    {
        var o = options.Value.Ads;
        var ev = Parse(q);
        string? given = q[o.HashParameter];
        if (ev == null || string.IsNullOrEmpty(o.EventKey) || string.IsNullOrEmpty(given)) return null;
        var data = Encoding.UTF8.GetBytes(ev.EventId + o.EventKey);
        byte[] hash = o.HashAlgorithm.Equals("sha1", StringComparison.OrdinalIgnoreCase) ? SHA1.HashData(data) : SHA256.HashData(data);
        var expected = Encoding.ASCII.GetBytes(Convert.ToHexString(hash).ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(given.ToLowerInvariant())) ? ev : null;
    }

    public static AdEvent? Parse(IQueryCollection q)
    {
        string? user = q["user_id"], ev = q["event_id"];
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(ev)) return null;
        return new AdEvent(user, ev, q["custom_data"], q["ad_unit_id"]);
    }
}

public sealed record IapVerifyRequest(string? ProductId, string? PurchaseToken);

public sealed class ReceiptClaim
{
    public string PlayerId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string OrderId { get; set; } = "";
    public long AtMs { get; set; }
    public bool Completed { get; set; }
}

public sealed class AdEventClaim
{
    public string PlayerId { get; set; } = "";
    public long AtMs { get; set; }
}

public static class PaymentEndpoints
{
    public static void Map(RouteGroupBuilder v1, WebApplication app)
    {
        v1.MapPost("/iap/verify", async (HttpContext http, IapVerifyRequest req, ServerHooks hooks, IReceiptValidator validator,
            IDocStore docs, Clock clock, ILoggerFactory lf, CancellationToken ct) =>
        {
            string playerId = http.PlayerId();
            if (!hooks.Has(hooks.Names.PurchaseComplete)) throw new ApiException(StatusCodes.Status501NotImplemented, "purchases_not_available");
            if (string.IsNullOrWhiteSpace(req.ProductId) || string.IsNullOrWhiteSpace(req.PurchaseToken)) throw ApiException.BadRequest("missing_fields");

            var result = await validator.ValidateAsync(req.ProductId, req.PurchaseToken, ct);
            if (!result.Valid) throw new ApiException(StatusCodes.Status402PaymentRequired, result.Error ?? "invalid_receipt");

            // A purchase token can be redeemed exactly once, by exactly one player.
            string key = Ids.Hash(req.PurchaseToken);
            var claim = new ReceiptClaim { PlayerId = playerId, ProductId = req.ProductId, OrderId = result.OrderId!, AtMs = clock.NowMs };
            if (!await docs.TryPutAsync(Collections.IapReceipts, key, claim, 0, ct))
            {
                var existing = await docs.GetAsync<ReceiptClaim>(Collections.IapReceipts, key, ct);
                if (existing?.Value.PlayerId != playerId) throw ApiException.Conflict("receipt_already_used");
                if (existing.Value.Completed)
                    return Results.Json(new { ok = true, alreadyCompleted = true, orderId = existing.Value.OrderId }, StateJson.Wire);
            }

            var response = await hooks.TryExecuteAsync(playerId, hooks.Names.PurchaseComplete, new
            {
                ProductId = req.ProductId, ReceiptId = result.OrderId, OrderId = result.OrderId, PurchaseToken = req.PurchaseToken, Store = "google_play", Test = result.Test,
            }, "iap-" + result.OrderId, ct);
            if (response is { Ok: true })
            {
                await docs.MutateAsync<ReceiptClaim>(Collections.IapReceipts, key, c =>
                {
                    if (c == null || c.Completed) return null;
                    c.Completed = true;
                    return c;
                }, ct);
            }
            lf.CreateLogger("Iap").LogInformation("Purchase {ProductId} order {OrderId} for {PlayerId}: {Ok}", req.ProductId, result.OrderId, playerId, response?.Ok);
            return Results.Json(response, StateJson.Wire);
        });

        // AppLovin calls the S2S callback with GET; POST is accepted as well.
        app.MapMethods("/v1/ads/ssv", new[] { "GET", "POST" }, async (HttpContext http, IAdVerifier verifier, ServerHooks hooks,
            IDocStore docs, Clock clock, ILoggerFactory lf, CancellationToken ct) =>
        {
            var ev = verifier.Verify(http.Request.Query) ?? throw new ApiException(StatusCodes.Status401Unauthorized, "invalid_signature");
            if (!hooks.Has(hooks.Names.AdWatched)) throw new ApiException(StatusCodes.Status501NotImplemented, "ads_not_available");
            string key = Ids.Hash(ev.EventId);
            if (await docs.GetAsync<AdEventClaim>(Collections.AdEvents, key, ct) != null)
                return Results.Ok(new { ok = true, duplicate = true });
            var response = await hooks.TryExecuteAsync(ev.PlayerId, hooks.Names.AdWatched, new
            {
                PlacementId = ev.Placement, AdToken = ev.EventId, Placement = ev.Placement, EventId = ev.EventId, AdUnitId = ev.AdUnitId,
            }, "ad-" + key[..32], ct);
            if (response is { Ok: true })
                await docs.TryPutAsync(Collections.AdEvents, key, new AdEventClaim { PlayerId = ev.PlayerId, AtMs = clock.NowMs }, 0, ct);
            lf.CreateLogger("Ads").LogInformation("Ad reward {EventId} for {PlayerId} ({Placement}): {Ok}", ev.EventId, ev.PlayerId, ev.Placement, response?.Ok);
            return Results.Ok(new { ok = response?.Ok ?? false, error = response?.Error });
        });
    }
}
