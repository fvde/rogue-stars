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
        var health = await http.GetFromJsonAsync<JsonElement>("/healthz");
        Assert.Equal("firebase", health.GetProperty("auth").GetString());
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
