using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RogueStars.Server.Infrastructure;

namespace RogueStars.Server.Auth;

/// <summary>Verifies a bearer token and returns the player id (Firebase uid), or null when invalid.</summary>
public interface IAuthVerifier
{
    Task<string?> VerifyAsync(string token, CancellationToken ct);
}

/// <summary>
/// Firebase ID tokens: RS256 signed by Google (securetoken keys), <c>iss = https://securetoken.google.com/&lt;project&gt;</c>,
/// <c>aud = &lt;project&gt;</c>, non-empty <c>sub</c> (the uid), <c>auth_time</c> in the past.
/// </summary>
public sealed class FirebaseAuthVerifier : IAuthVerifier
{
    private readonly ISigningKeySource _keys;
    private readonly TimeProvider _time;
    private readonly string _project;
    private readonly string[] _issuers;

    public FirebaseAuthVerifier(ISigningKeySource keys, TimeProvider time, string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)) throw new InvalidOperationException("RogueStars:Auth:FirebaseProjectId is required in firebase mode");
        _keys = keys;
        _time = time;
        _project = projectId;
        _issuers = new[] { "https://securetoken.google.com/" + projectId };
    }

    public async Task<string?> VerifyAsync(string token, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var claims = await Jwt.ValidateAsync(token, _keys, _issuers, _project, now, ct);
        if (claims is not { } c) return null;
        var sub = c.GetString("sub");
        if (string.IsNullOrEmpty(sub) || sub.Length > 128) return null;
        if (c.TryGetProperty("auth_time", out var at) && at.GetInt64() - 300 > now.ToUnixTimeSeconds()) return null;
        return sub;
    }
}

/// <summary>
/// Development tokens: <c>dev.&lt;base64url(playerId)&gt;.&lt;base64url(HMAC-SHA256(secret, playerId))&gt;</c>, issued by
/// <c>POST /v1/auth/dev</c> for a device id. Never enabled in Production (startup check).
/// </summary>
public sealed class DevAuthVerifier(IOptions<ServerOptions> options) : IAuthVerifier
{
    private byte[] Secret => Encoding.UTF8.GetBytes(options.Value.Auth.DevSecret);

    public static string PlayerIdForDevice(string deviceId)
    {
        var clean = new string(deviceId.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(64).ToArray());
        if (clean.Length == 0) throw ApiException.BadRequest("invalid_device_id");
        return "dev_" + clean;
    }

    public string Issue(string playerId) =>
        "dev." + Jwt.ToBase64Url(Encoding.UTF8.GetBytes(playerId)) + "." + Jwt.ToBase64Url(HMACSHA256.HashData(Secret, Encoding.UTF8.GetBytes(playerId)));

    public Task<string?> VerifyAsync(string token, CancellationToken ct)
    {
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != "dev") return Task.FromResult<string?>(null);
        try
        {
            var id = Encoding.UTF8.GetString(Jwt.Base64Url(parts[1]));
            var expected = HMACSHA256.HashData(Secret, Encoding.UTF8.GetBytes(id));
            return Task.FromResult(CryptographicOperations.FixedTimeEquals(expected, Jwt.Base64Url(parts[2])) ? id : null);
        }
        catch (FormatException)
        {
            return Task.FromResult<string?>(null);
        }
    }
}

/// <summary>Endpoint filter: requires a valid <c>Authorization: Bearer</c> token and stores the player id.</summary>
public sealed class PlayerAuthFilter(IAuthVerifier verifier) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        string? header = http.Request.Headers.Authorization;
        if (header == null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new ApiException(401, "missing_token");
        var playerId = await verifier.VerifyAsync(header["Bearer ".Length..].Trim(), http.RequestAborted);
        if (playerId == null) throw new ApiException(401, "invalid_token");
        http.SetPlayerId(playerId);
        return await next(context);
    }
}
