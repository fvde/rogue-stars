using System.Net;
using System.Text;
using System.Text.Json;
using Google.Apis.AndroidPublisher.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Payments;

namespace RogueStars.Server.Tests;

/// <summary>Fake Google Play Developer API: answers productsv2 lookups and records consume/acknowledge calls.</summary>
public sealed class FakePlayApi : HttpMessageHandler
{
    public readonly List<string> Calls = new();
    /// <summary>purchase token → productsv2 JSON (null = 404).</summary>
    public readonly Dictionary<string, object?> Purchases = new();
    public HttpStatusCode? FailWith;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        Calls.Add(request.Method + " " + path);
        if (FailWith is { } f) return Task.FromResult(Json(f, new { error = new { code = (int)f, message = "backend error" } }));
        if (path.Contains("/purchases/productsv2/tokens/"))
        {
            string token = path[(path.LastIndexOf('/') + 1)..];
            return Task.FromResult(Purchases.TryGetValue(token, out var p) && p != null
                ? Json(HttpStatusCode.OK, p)
                : Json(HttpStatusCode.NotFound, new { error = new { code = 404, message = "The purchase token was not found." } }));
        }
        if (path.EndsWith(":consume") || path.EndsWith(":acknowledge")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotImplemented));
    }

    private static HttpResponseMessage Json(HttpStatusCode code, object body) =>
        new(code) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    /// <summary>A productsv2 response as documented (purchases.productsv2 ProductPurchaseV2).</summary>
    public static object Purchase(string sku, string state = "PURCHASED", string? account = null, bool test = false, int quantity = 1,
        string consumption = "CONSUMPTION_STATE_YET_TO_BE_CONSUMED", string ack = "ACKNOWLEDGEMENT_STATE_PENDING", string? orderId = "GPA.3301-0000-0000-00001")
    {
        var d = new Dictionary<string, object?>
        {
            ["kind"] = "androidpublisher#productPurchaseV2",
            ["productLineItem"] = new[] { new { productId = sku, productOfferDetails = new { quantity, consumptionState = consumption, refundableQuantity = quantity } } },
            ["purchaseStateContext"] = new { purchaseState = state },
            ["acknowledgementState"] = ack,
            ["regionCode"] = "DE",
        };
        if (orderId != null) d["orderId"] = orderId;
        if (account != null) d["obfuscatedExternalAccountId"] = account;
        if (test) d["testPurchaseContext"] = new { fopType = "TEST" };
        return d;
    }

    private sealed class Factory(HttpMessageHandler handler) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
    }

    public AndroidPublisherService Service() => new(new BaseClientService.Initializer { HttpClientFactory = new Factory(this), ApplicationName = "test" });
}

public class GooglePlayTests : IDisposable
{
    private readonly TestApp _app = new();
    private readonly FakePlayApi _play = new();

    public GooglePlayTests()
    {
        _app.Settings["RogueStars:Iap:Mode"] = "google";
        _app.Settings["RogueStars:Iap:PackageName"] = "com.roguestars.game";
        _app.ConfigureServices = s => s.AddSingleton<IReceiptValidator>(sp => new GooglePlayReceiptValidator(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServerOptions>>(), NullLogger<GooglePlayReceiptValidator>.Instance, _play.Service));
    }

    public void Dispose() => _app.Dispose();

    private async Task<PlayerClient> Buyer(string product)
    {
        var p = await _app.PlayerAsync("buyer-" + Guid.NewGuid().ToString("N")[..6]);
        Assert.True((await p.CommandAsync("PurchaseStart", new { productId = product })).Body.GetProperty("ok").GetBoolean());
        return p;
    }

    private static string Account(PlayerClient p) => GooglePlayReceiptValidator.ObfuscatedAccountId(p.PlayerId);

    [Fact]
    public async Task Consumable_IsVerifiedGrantedAndConsumedServerSide_Once()
    {
        var p = await Buyer("diamonds_60");
        long diamonds = await p.BalanceAsync(Currencies.Diamonds);
        _play.Purchases["tok-1"] = FakePlayApi.Purchase("rs.diamonds_60", account: Account(p));

        var (s, body) = await p.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "tok-1" });
        Assert.Equal(200, s);
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.Equal(diamonds + 120, await p.BalanceAsync(Currencies.Diamonds));
        Assert.Equal(new[]
        {
            "GET /androidpublisher/v3/applications/com.roguestars.game/purchases/productsv2/tokens/tok-1",
            "POST /androidpublisher/v3/applications/com.roguestars.game/purchases/products/rs.diamonds_60/tokens/tok-1:consume",
        }, _play.Calls);

        // Client retry after a lost response: no second lookup, no second grant.
        var again = await p.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "tok-1" });
        Assert.True(again.Body.GetProperty("alreadyCompleted").GetBoolean());
        Assert.Equal("GPA.3301-0000-0000-00001", again.Body.GetProperty("orderId").GetString());
        Assert.Equal(2, _play.Calls.Count);
        Assert.Equal(diamonds + 120, await p.BalanceAsync(Currencies.Diamonds));

        var thief = await Buyer("diamonds_60");
        Assert.Equal(409, (await thief.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "tok-1" })).Status);
    }

    [Fact]
    public async Task NonConsumable_IsAcknowledged_AndRestoreIsAlreadyCompleted()
    {
        var p = await Buyer("perm_no_ads");
        _play.Purchases["tok-na"] = FakePlayApi.Purchase("rs.perm_no_ads", account: Account(p), orderId: null); // promo code: no order id
        var r = await p.PostAsync("/v1/iap/verify", new { productId = "perm_no_ads", purchaseToken = "tok-na" });
        Assert.True(r.Body.GetProperty("ok").GetBoolean());
        Assert.Contains(_play.Calls, c => c.EndsWith("/purchases/products/rs.perm_no_ads/tokens/tok-na:acknowledge"));
        Assert.DoesNotContain(_play.Calls, c => c.EndsWith(":consume"));
        Assert.Contains("ent_no_ads", (await p.StateAsync()).GetProperty("flags").EnumerateArray().Select(f => f.GetString()));

        var restore = await p.PostAsync("/v1/iap/verify", new { productId = "perm_no_ads", purchaseToken = "tok-na" });
        Assert.True(restore.Body.GetProperty("alreadyCompleted").GetBoolean());
    }

    [Theory]
    [InlineData("PENDING", null, 1, false, "purchase_pending")]
    [InlineData("CANCELLED", null, 1, false, "purchase_not_completed")]
    [InlineData("PURCHASED", "rs.diamonds_330", 1, false, "product_mismatch")]
    [InlineData("PURCHASED", null, 2, false, "unsupported_quantity")]
    [InlineData("PURCHASED", null, 1, true, "account_mismatch")]
    public async Task InvalidPurchases_AreNotGranted_NorFinalized(string state, string? sku, int quantity, bool otherAccount, string error)
    {
        var p = await Buyer("diamonds_60");
        long diamonds = await p.BalanceAsync(Currencies.Diamonds);
        _play.Purchases["tok"] = FakePlayApi.Purchase(sku ?? "rs.diamonds_60", state, otherAccount ? "someone-else" : Account(p), quantity: quantity);
        var (s, body) = await p.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "tok" });
        Assert.Equal(402, s);
        Assert.Equal(error, body.GetProperty("error").GetString());
        Assert.Equal(diamonds, await p.BalanceAsync(Currencies.Diamonds));
        Assert.DoesNotContain(_play.Calls, c => c.EndsWith(":consume") || c.EndsWith(":acknowledge"));
    }

    [Fact]
    public async Task PendingPurchase_CompletesLater()
    {
        var p = await Buyer("diamonds_60");
        _play.Purchases["tok-p"] = FakePlayApi.Purchase("rs.diamonds_60", "PENDING", Account(p));
        Assert.Equal(402, (await p.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "tok-p" })).Status);
        _play.Purchases["tok-p"] = FakePlayApi.Purchase("rs.diamonds_60", "PURCHASED", Account(p));
        Assert.True((await p.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "tok-p" })).Body.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task UnknownToken_StoreOutage_ConsumedElsewhere_AndTestPurchasePolicy()
    {
        var p = await Buyer("diamonds_60");
        Assert.Equal(402, (await p.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "nope" })).Status);
        Assert.Equal(400, (await p.PostAsync("/v1/iap/verify", new { productId = "no_such_product", purchaseToken = "x" })).Status);

        _play.Purchases["tok-c"] = FakePlayApi.Purchase("rs.diamonds_60", account: Account(p), consumption: "CONSUMPTION_STATE_CONSUMED");
        Assert.Equal("already_consumed", (await p.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "tok-c" })).Body.GetProperty("error").GetString());

        _play.FailWith = HttpStatusCode.InternalServerError;
        Assert.Equal(503, (await p.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "tok-c" })).Status);
        _play.FailWith = null;

        // License testers are accepted by default and flagged; production can refuse them.
        _play.Purchases["tok-t"] = FakePlayApi.Purchase("rs.diamonds_60", account: Account(p), test: true, orderId: "GPA.T");
        Assert.True((await p.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "tok-t" })).Body.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task TestPurchases_CanBeRefused_AndAccountIdRequired()
    {
        _app.Settings["RogueStars:Iap:AllowTestPurchases"] = "false";
        _app.Settings["RogueStars:Iap:RequireAccountId"] = "true";
        var p = await Buyer("diamonds_60");
        _play.Purchases["tok-t"] = FakePlayApi.Purchase("rs.diamonds_60", account: Account(p), test: true);
        Assert.Equal("test_purchase_not_allowed", (await p.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "tok-t" })).Body.GetProperty("error").GetString());
        _play.Purchases["tok-n"] = FakePlayApi.Purchase("rs.diamonds_60");
        Assert.Equal("account_mismatch", (await p.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "tok-n" })).Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task IapConfig_ListsStoreProductsAndAccountId()
    {
        var p = await _app.PlayerAsync("cfg");
        var body = (await p.GetAsync("/v1/iap/config")).Body;
        Assert.Equal(Account(p), body.GetProperty("obfuscatedAccountId").GetString());
        var products = body.GetProperty("products").EnumerateArray().ToList();
        Assert.Contains(products, x => x.GetProperty("storeProductId").GetString() == "rs.monthly_pass" || x.GetProperty("productId").GetString() == ContentDb.Default.Shop.MonthlyPassProductId);
        Assert.All(products, x => Assert.False(string.IsNullOrEmpty(x.GetProperty("storeProductId").GetString())));
    }
}

public class PlayCatalogTests
{
    [Fact]
    public void TypesFollowTheLifetimeLimit()
    {
        var c = ContentDb.Default;
        Assert.Equal(StoreProductType.Consumable, PlayCatalog.TypeOf(c.Product("diamonds_60")!));
        Assert.Equal(StoreProductType.Consumable, PlayCatalog.TypeOf(c.Product(c.Shop.MonthlyPassProductId)!));
        Assert.Equal(StoreProductType.NonConsumable, PlayCatalog.TypeOf(c.Product("perm_no_ads")!));
        Assert.Equal(StoreProductType.NonConsumable, PlayCatalog.TypeOf(c.Product("starter_worm")!));
        Assert.DoesNotContain(PlayCatalog.StoreProducts(c), p => p.PriceUsdCents == 0);
    }

    /// <summary>docs/play-console-products.csv must match the shop content. Regenerate: UPDATE_PLAY_CATALOG=1 dotnet test.</summary>
    [Fact]
    public void PlayConsoleCsv_IsUpToDate()
    {
        string csv = PlayCatalog.Csv(ContentDb.Default);
        string path = Path.Combine(RepoRoot(), "docs", "play-console-products.csv");
        if (Environment.GetEnvironmentVariable("UPDATE_PLAY_CATALOG") == "1") File.WriteAllText(path, csv);
        Assert.True(File.Exists(path), "missing " + path + " (run with UPDATE_PLAY_CATALOG=1)");
        Assert.Equal(csv, File.ReadAllText(path).Replace("\r\n", "\n"));
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RogueStars.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
