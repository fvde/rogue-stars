using Google;
using Google.Apis.AndroidPublisher.v3;
using Google.Apis.AndroidPublisher.v3.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Microsoft.Extensions.Options;
using RogueStars.Server.Infrastructure;

namespace RogueStars.Server.Payments;

/// <summary>What the server knows about the product being verified.</summary>
public sealed record ReceiptRequest(string PlayerId, string ProductId, string StoreProductId, StoreProductType Type, string PurchaseToken);

/// <summary>
/// Outcome of a store lookup. <see cref="OrderId"/> may be null (promo-code purchases have none);
/// <see cref="AlreadyFinalized"/> = consumed (consumable) or acknowledged (non-consumable) before.
/// </summary>
public sealed record ReceiptResult(bool Valid, string? OrderId, string? Error, bool Test = false, bool AlreadyFinalized = false)
{
    public static ReceiptResult Invalid(string error, string? orderId = null) => new(false, orderId, error);
}

/// <summary>Validates a store purchase token and finalizes (consume/acknowledge) it after the grant.</summary>
public interface IReceiptValidator
{
    Task<ReceiptResult> ValidateAsync(ReceiptRequest request, CancellationToken ct);
    /// <summary>Consumable → consume; non-consumable → acknowledge. Returns false on a store error (retried on the next verify).</summary>
    Task<bool> FinalizeAsync(ReceiptRequest request, CancellationToken ct);
}

/// <summary>Dev/test: tokens starting with "fake-" are valid; the order id is derived from the token.</summary>
public sealed class FakeReceiptValidator : IReceiptValidator
{
    public Task<ReceiptResult> ValidateAsync(ReceiptRequest r, CancellationToken ct) =>
        Task.FromResult(r.PurchaseToken.StartsWith("fake-", StringComparison.Ordinal)
            ? new ReceiptResult(true, "FAKE." + r.PurchaseToken[5..], null, Test: true)
            : ReceiptResult.Invalid("invalid_receipt"));

    public Task<bool> FinalizeAsync(ReceiptRequest r, CancellationToken ct) => Task.FromResult(true);
}

/// <summary>
/// Google Play Developer API (Android Publisher v3), as recommended by
/// https://developer.android.com/google/play/billing/security#verify:
/// <list type="bullet">
/// <item>Lookup: <c>purchases.productsv2.getproductpurchasev2(packageName, token)</c>. Grant only when
/// <c>purchaseStateContext.purchaseState == PURCHASED</c> (PENDING → <c>purchase_pending</c>, never granted), the line item's
/// <c>productId</c> equals the expected SKU, quantity is 1, and <c>obfuscatedExternalAccountId</c> (set by the client to
/// <see cref="ObfuscatedAccountId"/>) matches the caller when present (required with <c>Iap:RequireAccountId</c>).
/// <c>testPurchaseContext</c> marks license-tester purchases (refused unless <c>Iap:AllowTestPurchases</c>).</item>
/// <item>Uniqueness: the purchase token (globally unique) is the primary key of <c>iap_receipts</c>; the order id is not
/// (promo purchases have none).</item>
/// <item>Finalize server-side after the grant: consumables <c>purchases.products.consume</c>, non-consumables
/// <c>purchases.products.acknowledge</c> (Play refunds unacknowledged purchases after 3 days).</item>
/// </list>
/// Credentials: <c>Iap:ServiceAccountJson</c> (Secret Manager) or the runtime identity (ADC); scope androidpublisher.
/// </summary>
public sealed class GooglePlayReceiptValidator : IReceiptValidator
{
    public const string Scope = "https://www.googleapis.com/auth/androidpublisher";
    private readonly Lazy<AndroidPublisherService> _service;
    private readonly IOptions<ServerOptions> _options;
    private readonly ILogger<GooglePlayReceiptValidator> _log;

    public GooglePlayReceiptValidator(IOptions<ServerOptions> options, ILogger<GooglePlayReceiptValidator> log)
        : this(options, log, () => new AndroidPublisherService(new BaseClientService.Initializer
        {
            HttpClientInitializer = CreateCredential(options.Value.Iap),
            ApplicationName = "rogue-stars-server",
        }))
    {
    }

    /// <summary>Tests pass a service whose HTTP handler is mocked.</summary>
    public GooglePlayReceiptValidator(IOptions<ServerOptions> options, ILogger<GooglePlayReceiptValidator> log, Func<AndroidPublisherService> factory)
    {
        _options = options;
        _log = log;
        _service = new Lazy<AndroidPublisherService>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private IapOptions O => _options.Value.Iap;
    private string Package => string.IsNullOrEmpty(O.PackageName) ? throw new InvalidOperationException("RogueStars:Iap:PackageName is required") : O.PackageName;

    /// <summary>Value the client passes as obfuscatedAccountId (Play allows up to 64 chars; no raw ids).</summary>
    public static string ObfuscatedAccountId(string playerId) => Ids.Hash("rs-account:" + playerId);

    public async Task<ReceiptResult> ValidateAsync(ReceiptRequest r, CancellationToken ct)
    {
        ProductPurchaseV2 p;
        try
        {
            p = await _service.Value.Purchases.Productsv2.Getproductpurchasev2(Package, r.PurchaseToken).ExecuteAsync(ct);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Gone)
        {
            _log.LogWarning("Play purchase lookup rejected ({Status}) for {ProductId}", (int)ex.HttpStatusCode, r.ProductId);
            return ReceiptResult.Invalid("invalid_receipt");
        }
        catch (GoogleApiException ex)
        {
            _log.LogError("Play purchase lookup failed ({Status}): {Message}", (int)ex.HttpStatusCode, ex.Message);
            return ReceiptResult.Invalid("store_unavailable");
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex, "Play purchase lookup failed");
            return ReceiptResult.Invalid("store_unavailable");
        }

        string? orderId = p.OrderId;
        string state = p.PurchaseStateContext?.PurchaseState ?? "PURCHASE_STATE_UNSPECIFIED";
        if (state == "PENDING") return ReceiptResult.Invalid("purchase_pending", orderId);
        if (state != "PURCHASED") return ReceiptResult.Invalid("purchase_not_completed", orderId);

        var item = p.ProductLineItem?.FirstOrDefault(i => i.ProductId == r.StoreProductId);
        if (item == null || p.ProductLineItem!.Count != 1) return ReceiptResult.Invalid("product_mismatch", orderId);
        int quantity = item.ProductOfferDetails?.Quantity ?? 1;
        if (quantity != 1) return ReceiptResult.Invalid("unsupported_quantity", orderId);

        string expectedAccount = ObfuscatedAccountId(r.PlayerId);
        if (!string.IsNullOrEmpty(p.ObfuscatedExternalAccountId) ? p.ObfuscatedExternalAccountId != expectedAccount : O.RequireAccountId)
            return ReceiptResult.Invalid("account_mismatch", orderId);

        bool test = p.TestPurchaseContext != null;
        if (test && !O.AllowTestPurchases) return ReceiptResult.Invalid("test_purchase_not_allowed", orderId);

        bool finalized = r.Type == StoreProductType.Consumable
            ? item.ProductOfferDetails?.ConsumptionState == "CONSUMPTION_STATE_CONSUMED"
            : p.AcknowledgementState == "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED";
        return new ReceiptResult(true, orderId, null, test, finalized);
    }

    public async Task<bool> FinalizeAsync(ReceiptRequest r, CancellationToken ct)
    {
        try
        {
            if (r.Type == StoreProductType.Consumable)
                await _service.Value.Purchases.Products.Consume(Package, r.StoreProductId, r.PurchaseToken).ExecuteAsync(ct);
            else
                await _service.Value.Purchases.Products.Acknowledge(new ProductPurchasesAcknowledgeRequest(), Package, r.StoreProductId, r.PurchaseToken).ExecuteAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is GoogleApiException or HttpRequestException)
        {
            _log.LogWarning("Play {Action} failed for {ProductId}: {Message}", r.Type == StoreProductType.Consumable ? "consume" : "acknowledge", r.ProductId, ex.Message);
            return false;
        }
    }

    private static GoogleCredential CreateCredential(IapOptions o) =>
        (string.IsNullOrWhiteSpace(o.ServiceAccountJson)
            ? GoogleCredential.GetApplicationDefault()
            : CredentialFactory.FromJson<ServiceAccountCredential>(o.ServiceAccountJson).ToGoogleCredential())
        .CreateScoped(Scope);
}
