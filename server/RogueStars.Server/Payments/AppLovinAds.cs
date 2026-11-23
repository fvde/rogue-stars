using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RogueStars.Server.Infrastructure;

namespace RogueStars.Server.Payments;

/// <summary>A verified rewarded-ad completion. <see cref="Placement"/> is our rules ad placement id.</summary>
public sealed record AdEvent(string PlayerId, string EventId, string? Placement, string? AdUnitId);

public enum AdVerifyStatus { Ok, Malformed, InvalidSignature, AdUnitNotAllowed }

public sealed record AdVerifyResult(AdVerifyStatus Status, AdEvent? Event)
{
    public static AdVerifyResult Fail(AdVerifyStatus s) => new(s, null);
}

/// <summary>Verifies a rewarded-ad server-to-server callback (query string of the GET request).</summary>
public interface IAdVerifier
{
    AdVerifyResult Verify(IQueryCollection query);
}

/// <summary>Dev/test: accepts any well-formed callback (user_id + event_id).</summary>
public sealed class FakeAdVerifier : IAdVerifier
{
    public AdVerifyResult Verify(IQueryCollection q) =>
        AppLovinAdVerifier.Parse(q) is { } ev ? new AdVerifyResult(AdVerifyStatus.Ok, ev) : AdVerifyResult.Fail(AdVerifyStatus.Malformed);
}

/// <summary>
/// AppLovin MAX S2S rewarded callback, verified exactly as documented at
/// https://support.applovin.com/en/max/advanced-features/s2s-rewarded-callback-api (macro table) and
/// https://support.applovin.com/en/max/faq/how-server-to-server-callback-works (worked example):
/// <list type="bullet">
/// <item><c>{EVENT_TOKEN}</c> = <c>sha1( EVENT_ID + Your-Event-Key )</c>, lowercase hex (default; official test vector in the tests).</item>
/// <item><c>{EVENT_TOKEN_ALL}</c> = <c>sha256( All macros alphabetically, URL-decoded + Your-Event-Key )</c>, where EVENT_TOKEN is
/// omitted from the string when it is one of your macros. Interpreted as: the URL-decoded values of every macro in the
/// callback URL (query parameter = lowercase macro name), ordered by macro name, concatenated, then the event key.
/// Opt-in (<c>Ads:Verification = event_token_all</c>) because AppLovin publishes no worked example; in the default mode it
/// is checked in shadow and logged, so one real callback confirms the interpretation before switching.</item>
/// </list>
/// The callback never passes through the device. Replays are rejected by the endpoint (one claim per event id).
/// </summary>
public sealed class AppLovinAdVerifier(IOptions<ServerOptions> options, ILogger<AppLovinAdVerifier> log) : IAdVerifier
{
    public const string ModeEventToken = "event_token";
    public const string ModeEventTokenAll = "event_token_all";

    /// <summary>The macros of AppLovin's table (query parameter = lowercase name). EVENT_TOKEN(_ALL) are the signatures.</summary>
    public static readonly string[] Macros =
    {
        "AD_UNIT_ID", "AD_UNIT_NAME", "AMOUNT", "CC", "CURRENCY", "CUSTOM_DATA", "EVENT_ID", "IDFA", "IDFV", "IP",
        "NETWORK_NAME", "PACKAGE_NAME", "PLACEMENT", "PLATFORM", "TS", "USER_ID",
    };

    public AdVerifyResult Verify(IQueryCollection q)
    {
        var o = options.Value.Ads;
        var ev = Parse(q);
        if (ev == null) return AdVerifyResult.Fail(AdVerifyStatus.Malformed);
        if (string.IsNullOrEmpty(o.EventKey)) return AdVerifyResult.Fail(AdVerifyStatus.InvalidSignature);

        bool all = string.Equals(o.Verification, ModeEventTokenAll, StringComparison.OrdinalIgnoreCase);
        string? tokenAll = q["event_token_all"];
        bool ok;
        if (all)
        {
            ok = Matches(EventTokenAll(q, o.EventKey), tokenAll);
        }
        else
        {
            ok = Matches(EventToken(ev.EventId, o.EventKey), q["event_token"]);
            if (ok && !string.IsNullOrEmpty(tokenAll))
            {
                bool shadow = Matches(EventTokenAll(q, o.EventKey), tokenAll);
                log.LogInformation("AppLovin EVENT_TOKEN_ALL shadow check for {EventId}: {Match}", ev.EventId, shadow ? "match" : "MISMATCH");
            }
        }
        if (!ok) return AdVerifyResult.Fail(AdVerifyStatus.InvalidSignature);

        if (o.AllowedAdUnitIds.Count > 0 && (ev.AdUnitId == null || !o.AllowedAdUnitIds.Contains(ev.AdUnitId, StringComparer.OrdinalIgnoreCase)))
            return AdVerifyResult.Fail(AdVerifyStatus.AdUnitNotAllowed);
        return new AdVerifyResult(AdVerifyStatus.Ok, ev);
    }

    /// <summary>sha1(EVENT_ID + event key), lowercase hex.</summary>
    public static string EventToken(string eventId, string eventKey) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(eventId + eventKey))).ToLowerInvariant();

    /// <summary>sha256(values of the present macros ordered by macro name, URL-decoded + event key), lowercase hex.</summary>
    public static string EventTokenAll(IQueryCollection q, string eventKey)
    {
        var sb = new StringBuilder();
        foreach (var macro in Macros.OrderBy(m => m, StringComparer.Ordinal))
        {
            // ASP.NET Core has already URL-decoded query values.
            if (q.TryGetValue(macro.ToLowerInvariant(), out var v)) sb.Append(v.ToString());
        }
        sb.Append(eventKey);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    private static bool Matches(string expected, string? given) =>
        !string.IsNullOrEmpty(given) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(given.Trim().ToLowerInvariant()));

    /// <summary>
    /// user_id = {USER_ID} (MaxSdk.SetUserId = our player id), event_id = {EVENT_ID}; the rules placement comes from
    /// custom_data = {CUSTOM_DATA} (ShowRewardedAd(adUnit, placement, customData)), falling back to placement = {PLACEMENT}.
    /// </summary>
    public static AdEvent? Parse(IQueryCollection q)
    {
        string? user = q["user_id"], ev = q["event_id"];
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(ev) || user.Length > 128 || ev.Length > 128) return null;
        string? placement = q["custom_data"];
        if (string.IsNullOrWhiteSpace(placement)) placement = q["placement"];
        if (placement is { Length: > 64 }) return null;
        return new AdEvent(user, ev, string.IsNullOrWhiteSpace(placement) ? null : placement, q["ad_unit_id"]);
    }
}
