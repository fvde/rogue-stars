using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RogueStars.Server.Auth;

namespace RogueStars.Server.Tests;

public class AuthTests
{
    private sealed class StaticKeys(string kid, RSA key) : ISigningKeySource
    {
        public Task<RSA?> GetKeyAsync(string k, CancellationToken ct) => Task.FromResult(k == kid ? key : null);
    }

    private static readonly RSA Key = RSA.Create(2048);
    private static readonly ManualTime Time = new();

    private static string Token(object claims, string kid = "k1", RSA? key = null)
    {
        string B64(byte[] b) => Jwt.ToBase64Url(b);
        var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", kid, typ = "JWT" }));
        var payload = B64(JsonSerializer.SerializeToUtf8Bytes(claims));
        var sig = (key ?? Key).SignData(Encoding.ASCII.GetBytes(header + "." + payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return header + "." + payload + "." + B64(sig);
    }

    private static object Claims(string iss = "https://securetoken.google.com/rogue-stars", string aud = "rogue-stars", long? exp = null, string sub = "uid123")
    {
        long now = Time.GetUtcNow().ToUnixTimeSeconds();
        return new { iss, aud, sub, iat = now - 10, auth_time = now - 10, exp = exp ?? now + 3600 };
    }

    private static FirebaseAuthVerifier Verifier() => new(new StaticKeys("k1", Key), Time, "rogue-stars");

    [Fact]
    public async Task Firebase_ValidToken_ReturnsUid()
    {
        Assert.Equal("uid123", await Verifier().VerifyAsync(Token(Claims()), default));
    }

    [Fact]
    public async Task Firebase_RejectsWrongAudienceIssuerExpiryKeyAndSignature()
    {
        var v = Verifier();
        Assert.Null(await v.VerifyAsync(Token(Claims(aud: "other")), default));
        Assert.Null(await v.VerifyAsync(Token(Claims(iss: "https://securetoken.google.com/other")), default));
        Assert.Null(await v.VerifyAsync(Token(Claims(exp: Time.GetUtcNow().ToUnixTimeSeconds() - 3600)), default));
        Assert.Null(await v.VerifyAsync(Token(Claims(), kid: "unknown"), default));
        Assert.Null(await v.VerifyAsync(Token(Claims(), key: RSA.Create(2048)), default));
        Assert.Null(await v.VerifyAsync(Token(Claims(sub: "")), default));
        Assert.Null(await v.VerifyAsync("not.a.jwt", default));
    }

    [Fact]
    public async Task DevTokens_RoundTrip_AndRejectTampering()
    {
        var o = Microsoft.Extensions.Options.Options.Create(new RogueStars.Server.Infrastructure.ServerOptions());
        var dev = new DevAuthVerifier(o);
        var token = dev.Issue("dev_abc");
        Assert.Equal("dev_abc", await dev.VerifyAsync(token, default));
        var forged = "dev." + Jwt.ToBase64Url(Encoding.UTF8.GetBytes("dev_victim")) + "." + token.Split('.')[2];
        Assert.Null(await dev.VerifyAsync(forged, default));
    }
}
