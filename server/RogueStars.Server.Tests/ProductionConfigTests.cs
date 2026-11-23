using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RogueStars.Server.Auth;

namespace RogueStars.Server.Tests;

/// <summary>
/// The Production configuration (appsettings.json + appsettings.Production.json, as on Cloud Run): Firebase ID tokens of
/// project rogue-stars-515610 only, no dev auth, ads disabled, initial product set.
/// </summary>
public class ProductionConfigTests : IDisposable
{
    private const string Project = "rogue-stars-515610";
    private static readonly RSA Key = RSA.Create(2048);
    private readonly TestApp _app = new() { Environment = "Production" };

    private sealed class StaticKeys(RSA key) : ISigningKeySource
    {
        public Task<RSA?> GetKeyAsync(string kid, CancellationToken ct) => Task.FromResult(kid == "k1" ? key : null);
    }

    public ProductionConfigTests()
    {
        // Only what the test host cannot have: in-memory storage and job settings (Terraform provides the real ones).
        _app.Settings["RogueStars:Store:Mode"] = "memory";
        _app.Settings["RogueStars:Jobs:Audience"] = "rogue-stars-jobs";
        _app.Settings["RogueStars:Jobs:ServiceAccountEmail"] = "scheduler@example.iam.gserviceaccount.com";
        _app.ConfigureServices = s => s.AddKeyedSingleton<ISigningKeySource>("firebase", new StaticKeys(Key));
    }

    public void Dispose() => _app.Dispose();

    private string Token(string project, string sub = "firebase-uid-1")
    {
        long now = _app.Time.GetUtcNow().ToUnixTimeSeconds();
        string B64(byte[] b) => Jwt.ToBase64Url(b);
        var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", kid = "k1", typ = "JWT" }));
        var payload = B64(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = "https://securetoken.google.com/" + project, aud = project, sub, iat = now - 5, auth_time = now - 5, exp = now + 3600,
            firebase = new { sign_in_provider = "playgames.google.com" },
        }));
        var sig = Key.SignData(Encoding.ASCII.GetBytes(header + "." + payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return header + "." + payload + "." + B64(sig);
    }

    [Fact]
    public async Task FirebaseTokensOfTheProductionProject_AreTheOnlyWayIn()
    {
        var http = _app.CreateClient();
        Assert.Equal(200, (int)(await http.GetAsync("/healthz")).StatusCode);
        var health = await http.GetFromJsonAsync<JsonElement>("/health"); // public path (Cloud Run reserves paths ending in z)
        Assert.Equal("firebase", health.GetProperty("auth").GetString());
        Assert.Equal("disabled", health.GetProperty("iap").GetString());
        Assert.Equal("disabled", health.GetProperty("ads").GetString());
        Assert.Equal("initial", health.GetProperty("iapProducts").GetString());

        Assert.Equal(404, (int)(await http.PostAsJsonAsync("/v1/auth/dev", new { deviceId = "x" })).StatusCode);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("other-project"));
        Assert.Equal(401, (int)(await http.PostAsJsonAsync("/v1/session", new { displayName = "A" })).StatusCode);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(Project));
        var session = await PlayerClient.ReadWithStatus(await http.PostAsJsonAsync("/v1/session", new { displayName = "A" }));
        Assert.Equal(200, session.Status);
        Assert.Equal("firebase-uid-1", session.Body.GetProperty("playerId").GetString());
        Assert.True(session.Body.GetProperty("adsDisabled").GetBoolean());

        // Payments on hold (Iap:Mode=disabled): no products, verify refuses clearly, Diamond-priced items still work.
        Assert.True(session.Body.GetProperty("purchasesDisabled").GetBoolean());
        var config = await http.GetFromJsonAsync<JsonElement>("/v1/iap/config");
        Assert.False(config.GetProperty("enabled").GetBoolean());
        Assert.Empty(config.GetProperty("products").EnumerateArray());
        var verify = await PlayerClient.ReadWithStatus(await http.PostAsJsonAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "tok" }));
        Assert.Equal(503, verify.Status);
        Assert.Equal("purchases_disabled", verify.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task DevTools_OnlyForAllowlistedFirebaseUids()
    {
        _app.Settings["RogueStars:DevTools:AllowedPlayerIds:0"] = "owner-uid";
        var http = _app.CreateClient();
        Assert.Equal("1", (await http.GetFromJsonAsync<JsonElement>("/health")).GetProperty("devTools").GetString());

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(Project, "player-uid"));
        var session = await PlayerClient.ReadWithStatus(await http.PostAsJsonAsync("/v1/session", new { displayName = "P" }));
        Assert.False(session.Body.GetProperty("devTools").GetBoolean());
        var denied = await PlayerClient.ReadWithStatus(await http.PostAsJsonAsync("/v1/dev/reset", new { }));
        Assert.Equal(403, denied.Status);
        Assert.Equal("dev_tools_forbidden", denied.Body.GetProperty("error").GetString());
        Assert.Equal(403, (int)(await http.PostAsJsonAsync("/v1/dev/jump", new { day = 3, profile = "casual" })).StatusCode);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(Project, "owner-uid"));
        session = await PlayerClient.ReadWithStatus(await http.PostAsJsonAsync("/v1/session", new { displayName = "Owner" }));
        Assert.True(session.Body.GetProperty("devTools").GetBoolean());
        var jump = await PlayerClient.ReadWithStatus(await http.PostAsJsonAsync("/v1/dev/jump", new { day = 3, profile = "casual", seed = 5 }));
        Assert.Equal(200, jump.Status);
        Assert.Equal("owner-uid", jump.Body.GetProperty("state").GetProperty("playerId").GetString());
        Assert.Equal(200, (await PlayerClient.ReadWithStatus(await http.PostAsJsonAsync("/v1/dev/reset", new { }))).Status);
    }

    [Fact]
    public void DevToolsForEveryone_AreRefusedAtStartupInProduction()
    {
        _app.Settings["RogueStars:DevTools:AllowAll"] = "true";
        Assert.ThrowsAny<Exception>(() => _app.CreateClient());
    }

    [Fact]
    public void DevAuth_IsRefusedAtStartupInProduction()
    {
        using var app = new TestApp { Environment = "Production" };
        app.Settings["RogueStars:Store:Mode"] = "memory";
        app.Settings["RogueStars:Auth:Mode"] = "dev";
        app.Settings["RogueStars:Jobs:Mode"] = "oidc";
        Assert.ThrowsAny<Exception>(() => app.CreateClient());
    }
}
