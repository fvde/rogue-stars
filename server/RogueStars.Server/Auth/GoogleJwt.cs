using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using RogueStars.Server.Infrastructure;

namespace RogueStars.Server.Auth;

/// <summary>Source of RSA verification keys by key id.</summary>
public interface ISigningKeySource
{
    Task<RSA?> GetKeyAsync(string kid, CancellationToken ct);
}

/// <summary>
/// Google-published x509 certificates (<c>{ kid: PEM }</c>), cached per the Cache-Control max-age.
/// Used for Firebase ID tokens (securetoken) and Google OIDC tokens (Cloud Scheduler).
/// </summary>
public sealed class GoogleCertificateSource(HttpClient http, string url, TimeProvider time) : ISigningKeySource
{
    public const string FirebaseCertsUrl = "https://www.googleapis.com/robot/v1/metadata/x509/securetoken@system.gserviceaccount.com";
    public const string GoogleOidcCertsUrl = "https://www.googleapis.com/oauth2/v1/certs";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, RSA> _keys = new();
    private DateTimeOffset _expires = DateTimeOffset.MinValue;

    public async Task<RSA?> GetKeyAsync(string kid, CancellationToken ct)
    {
        if (time.GetUtcNow() >= _expires || !_keys.ContainsKey(kid))
        {
            await _gate.WaitAsync(ct);
            try
            {
                if (time.GetUtcNow() >= _expires || !_keys.ContainsKey(kid)) await RefreshAsync(ct);
            }
            finally { _gate.Release(); }
        }
        return _keys.TryGetValue(kid, out var key) ? key : null;
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var certs = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>(ct) ?? new();
        var keys = new Dictionary<string, RSA>();
        foreach (var (kid, pem) in certs)
        {
            var cert = X509Certificate2.CreateFromPem(pem);
            var rsa = cert.GetRSAPublicKey();
            if (rsa != null) keys[kid] = rsa;
        }
        _keys = keys;
        var maxAge = resp.Headers.CacheControl?.MaxAge ?? TimeSpan.FromHours(1);
        _expires = time.GetUtcNow() + maxAge;
    }
}

/// <summary>Minimal RS256 JWT validation (signature, exp/iat/nbf, issuer, audience). Returns the claims.</summary>
public static class Jwt
{
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    public static async Task<JsonElement?> ValidateAsync(string token, ISigningKeySource keys, IReadOnlyCollection<string> issuers,
        string audience, DateTimeOffset now, CancellationToken ct)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            using var header = JsonDocument.Parse(Base64Url(parts[0]));
            if (!header.RootElement.TryGetProperty("alg", out var alg) || alg.GetString() != "RS256") return null;
            if (!header.RootElement.TryGetProperty("kid", out var kidEl) || kidEl.GetString() is not { } kid) return null;

            var key = await keys.GetKeyAsync(kid, ct);
            if (key == null) return null;
            var signed = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            if (!key.VerifyData(signed, Base64Url(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;

            var claims = JsonDocument.Parse(Base64Url(parts[1])).RootElement.Clone();
            long nowS = now.ToUnixTimeSeconds();
            long skew = (long)Skew.TotalSeconds;
            if (!claims.TryGetProperty("exp", out var exp) || exp.GetInt64() + skew < nowS) return null;
            if (claims.TryGetProperty("iat", out var iat) && iat.GetInt64() - skew > nowS) return null;
            if (claims.TryGetProperty("nbf", out var nbf) && nbf.GetInt64() - skew > nowS) return null;
            if (!claims.TryGetProperty("iss", out var iss) || !issuers.Contains(iss.GetString())) return null;
            if (!HasAudience(claims, audience)) return null;
            return claims;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool HasAudience(JsonElement claims, string audience)
    {
        if (!claims.TryGetProperty("aud", out var aud)) return false;
        if (aud.ValueKind == JsonValueKind.String) return aud.GetString() == audience;
        return aud.ValueKind == JsonValueKind.Array && aud.EnumerateArray().Any(a => a.GetString() == audience);
    }

    public static byte[] Base64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    public static string ToBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string? GetString(this JsonElement claims, string name) =>
        claims.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
