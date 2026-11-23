using System.Reflection;
using Microsoft.Extensions.Options;
using RogueStars.Rules.Core;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Payments;

/// <summary>Verified rewarded-ad views of one player that a client command can spend (<c>ad_credits/{playerId}</c>).</summary>
public sealed class AdCreditBook
{
    public List<AdCredit> Items { get; set; } = new();
}

public sealed class AdCredit
{
    /// <summary>AppLovin {EVENT_ID}; the client sends it as the command's <c>AdToken</c>.</summary>
    public string EventId { get; set; } = "";
    public string Placement { get; set; } = "";
    public long AtMs { get; set; }
    /// <summary>Request id of the command that spent it (null = unspent). Same request id may re-use it (retries).</summary>
    public string? UsedBy { get; set; }
}

/// <summary>
/// Ad placements without direct rewards (revive, stamina refill, timer speed-up, free pulls, double rewards) are
/// consumed by the owning rules command, which carries an <c>AdToken</c>. Online, that token must be the event id of
/// a verified S2S callback for this player: the callback stores a credit, the command spends it. Fake ads mode
/// (dev/tests) skips the check.
/// </summary>
public sealed class AdCreditService(IDocStore docs, Clock clock, IOptions<ServerOptions> options)
{
    private const int MaxItems = 30;
    private static readonly Dictionary<Type, FieldInfo?> TokenFields = new();

    private AdsOptions O => options.Value.Ads;
    public bool Enforced => !string.Equals(O.Mode, "fake", StringComparison.OrdinalIgnoreCase);
    private long Ttl => (long)TimeSpan.FromHours(Math.Max(1, O.CreditTtlHours)).TotalMilliseconds;

    public async Task AddAsync(string playerId, string eventId, string placement, CancellationToken ct)
    {
        await docs.MutateAsync<AdCreditBook>(Collections.AdCredits, playerId, book =>
        {
            book ??= new AdCreditBook();
            if (book.Items.Any(c => c.EventId == eventId)) return null;
            Prune(book);
            book.Items.Add(new AdCredit { EventId = eventId, Placement = placement, AtMs = clock.NowMs });
            return book;
        }, ct);
    }

    public async Task<List<AdCredit>> UnspentAsync(string playerId, CancellationToken ct)
    {
        var book = await docs.GetAsync<AdCreditBook>(Collections.AdCredits, playerId, ct);
        long cutoff = clock.NowMs - Ttl;
        return book?.Value.Items.Where(c => c.UsedBy == null && c.AtMs >= cutoff).ToList() ?? new();
    }

    /// <summary>The command's AdToken (null when the command has no such field or it is empty).</summary>
    public static string? TokenOf(GameCommand command)
    {
        var type = command.GetType();
        FieldInfo? f;
        lock (TokenFields)
        {
            if (!TokenFields.TryGetValue(type, out f))
                TokenFields[type] = f = type.GetField("AdToken", BindingFlags.Public | BindingFlags.Instance) is { FieldType: var t } fi && t == typeof(string) ? fi : null;
        }
        return f?.GetValue(command) as string is { Length: > 0 } s ? s : null;
    }

    /// <summary>Marks the credit as spent by <paramref name="requestId"/>; false when there is no such unspent credit.</summary>
    public async Task<bool> TrySpendAsync(string playerId, string token, string requestId, CancellationToken ct)
    {
        bool ok = false;
        long cutoff = clock.NowMs - Ttl;
        await docs.MutateAsync<AdCreditBook>(Collections.AdCredits, playerId, book =>
        {
            ok = false;
            var c = book?.Items.FirstOrDefault(x => x.EventId == token);
            if (c == null || c.AtMs < cutoff) return null;
            if (c.UsedBy == requestId) { ok = true; return null; }
            if (c.UsedBy != null) return null;
            c.UsedBy = requestId;
            ok = true;
            return book;
        }, ct);
        return ok;
    }

    /// <summary>Gives the credit back after the command was rejected by the rules.</summary>
    public async Task RefundAsync(string playerId, string token, string requestId, CancellationToken ct)
    {
        await docs.MutateAsync<AdCreditBook>(Collections.AdCredits, playerId, book =>
        {
            var c = book?.Items.FirstOrDefault(x => x.EventId == token && x.UsedBy == requestId);
            if (c == null) return null;
            c.UsedBy = null;
            return book;
        }, ct);
    }

    private void Prune(AdCreditBook book)
    {
        long cutoff = clock.NowMs - Ttl;
        book.Items.RemoveAll(c => c.AtMs < cutoff);
        while (book.Items.Count >= MaxItems)
        {
            int i = book.Items.FindIndex(c => c.UsedBy != null);
            book.Items.RemoveAt(i >= 0 ? i : 0);
        }
    }
}
