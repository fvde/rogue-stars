using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RogueStars.Server.Auth;
using RogueStars.Server.Infrastructure;

namespace RogueStars.Server.Jobs;

/// <summary>
/// Protects <c>/internal/*</c>. "oidc": Cloud Scheduler Google ID token (issuer accounts.google.com, audience =
/// configured service URL, e-mail = scheduler service account, verified). "secret": <c>X-Job-Secret</c> header (dev).
/// </summary>
public sealed class JobAuthFilter(IOptions<ServerOptions> options, IServiceProvider services, TimeProvider time) : IEndpointFilter
{
    private static readonly string[] Issuers = { "https://accounts.google.com", "accounts.google.com" };

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var o = options.Value.Jobs;
        var http = context.HttpContext;
        bool ok = o.Mode switch
        {
            "secret" => CheckSecret(http, o.Secret),
            "oidc" => await CheckOidcAsync(http, o),
            _ => false,
        };
        if (!ok) throw new ApiException(401, "job_unauthorized");
        return await next(context);
    }

    private static bool CheckSecret(HttpContext http, string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return false;
        string? given = http.Request.Headers["X-Job-Secret"];
        return given != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(secret));
    }

    private async Task<bool> CheckOidcAsync(HttpContext http, JobOptions o)
    {
        if (string.IsNullOrEmpty(o.Audience) || string.IsNullOrEmpty(o.ServiceAccountEmail)) return false;
        string? header = http.Request.Headers.Authorization;
        if (header == null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var keys = services.GetRequiredKeyedService<ISigningKeySource>("google-oidc");
        var claims = await Jwt.ValidateAsync(header["Bearer ".Length..].Trim(), keys, Issuers, o.Audience, time.GetUtcNow(), http.RequestAborted);
        if (claims is not { } c) return false;
        bool verified = c.TryGetProperty("email_verified", out var ev) && ev.ValueKind == System.Text.Json.JsonValueKind.True;
        return verified && string.Equals(c.GetString("email"), o.ServiceAccountEmail, StringComparison.OrdinalIgnoreCase);
    }
}
