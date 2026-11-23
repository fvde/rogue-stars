using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RogueStars.Server.Infrastructure;

/// <summary>
/// JSON settings shared by storage, engine cloning and command argument parsing.
/// Rules classes use public fields, so <c>IncludeFields</c> is mandatory everywhere.
/// </summary>
public static class StateJson
{
    /// <summary>Storage format: C# member names (PascalCase), fields included.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Wire format: web defaults (camelCase, case-insensitive) plus fields.</summary>
    public static readonly JsonSerializerOptions Wire = CreateWire();

    public static JsonSerializerOptions CreateWire()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        return o;
    }

    public static void ConfigureWire(JsonSerializerOptions o)
    {
        o.IncludeFields = true;
        o.PropertyNameCaseInsensitive = true;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
    public static T Clone<T>(T value) => Deserialize<T>(Serialize(value));

    /// <summary>Converts any object into a wire-format JSON element (used for payloads and state in responses).</summary>
    public static JsonElement ToWire(object? value) =>
        value == null ? default : JsonSerializer.SerializeToElement(value, value.GetType(), Wire);
}

/// <summary>Expected API failure; turned into <c>{ ok:false, error }</c> with the given status code.</summary>
public sealed class ApiException(int status, string error, object? details = null) : Exception(error)
{
    public int Status { get; } = status;
    public string Error { get; } = error;
    public object? Details { get; } = details;

    public static ApiException BadRequest(string error) => new(StatusCodes.Status400BadRequest, error);
    public static ApiException NotFound(string error) => new(StatusCodes.Status404NotFound, error);
    public static ApiException Conflict(string error, object? details = null) => new(StatusCodes.Status409Conflict, error, details);
    public static ApiException Forbidden(string error) => new(StatusCodes.Status403Forbidden, error);
}

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (ApiException ex) when (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = ex.Status;
            await ctx.Response.WriteAsJsonAsync(new ErrorBody(false, ex.Error, ex.Details), StateJson.Wire);
        }
        catch (JsonException ex) when (!ctx.Response.HasStarted)
        {
            log.LogInformation("Bad JSON on {Path}: {Message}", ctx.Request.Path, ex.Message);
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new ErrorBody(false, "invalid_json", null), StateJson.Wire);
        }
        catch (BadHttpRequestException ex) when (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = ex.StatusCode;
            await ctx.Response.WriteAsJsonAsync(new ErrorBody(false, "bad_request", null), StateJson.Wire);
        }
    }

    private sealed record ErrorBody(bool Ok, string Error, object? Details);
}

/// <summary>Unix-millisecond clock over <see cref="TimeProvider"/> (tests swap the provider).</summary>
public sealed class Clock(TimeProvider time)
{
    public long NowMs => time.GetUtcNow().ToUnixTimeMilliseconds();
    public DateTimeOffset Now => time.GetUtcNow();

    /// <summary>ISO week key, e.g. "2026-W47". Arena seasons and guild weekly totals use it.</summary>
    public string WeekKey(DateTimeOffset? at = null) => Week((at ?? Now).UtcDateTime);

    public static string Week(DateTime utc) =>
        string.Create(CultureInfo.InvariantCulture, $"{ISOWeek.GetYear(utc)}-W{ISOWeek.GetWeekOfYear(utc):00}");

    public static string PreviousWeek(DateTime utc) => Week(utc.AddDays(-7));
}

public static class Ids
{
    public static string New(int bytes = 9) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    public static long NewSeed() => BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8));

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public static class PlayerContext
{
    private const string Key = "rs.playerId";

    public static void SetPlayerId(this HttpContext ctx, string playerId) => ctx.Items[Key] = playerId;

    public static string PlayerId(this HttpContext ctx) =>
        ctx.Items.TryGetValue(Key, out var v) && v is string s ? s : throw new ApiException(401, "unauthenticated");
}

/// <summary>Names shown to other players: the display name, never the internal player id.</summary>
public static class PlayerNames
{
    public const string Fallback = "Unnamed worm";
    public static string Display(string? displayName) => string.IsNullOrWhiteSpace(displayName) ? Fallback : displayName.Trim();
}
