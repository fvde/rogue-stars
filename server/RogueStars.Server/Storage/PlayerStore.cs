using System.Collections.Concurrent;
using Google.Cloud.Firestore;
using RogueStars.Rules.Core;
using RogueStars.Server.Infrastructure;

namespace RogueStars.Server.Storage;

/// <summary>A persisted player: authoritative state plus recent request results for idempotency.</summary>
public sealed class PlayerRecord
{
    public GameState State { get; set; } = new();
    /// <summary>Most recent last; trimmed to the idempotency window.</summary>
    public List<RequestRecord> Requests { get; set; } = new();
}

public sealed class RequestRecord
{
    public string RequestId { get; set; } = "";
    public long AtMs { get; set; }
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public List<GameEvent> Events { get; set; } = new();
    public System.Text.Json.JsonElement? Payload { get; set; }
}

/// <summary>
/// Player state storage with optimistic concurrency on <see cref="GameState.Version"/>:
/// a save only succeeds when the stored version still equals the version the caller loaded.
/// </summary>
public interface IPlayerStore
{
    Task<PlayerRecord?> GetAsync(string playerId, CancellationToken ct = default);
    /// <summary>Creates the record; false when the player already exists.</summary>
    Task<bool> TryCreateAsync(PlayerRecord record, CancellationToken ct = default);
    /// <summary>Compare-and-set: false when the stored version differs from <paramref name="expectedVersion"/>.</summary>
    Task<bool> TrySaveAsync(PlayerRecord record, long expectedVersion, CancellationToken ct = default);
}

public sealed class InMemoryPlayerStore : IPlayerStore
{
    // Serialized copies so callers never share instances with the store (same semantics as Firestore).
    private readonly ConcurrentDictionary<string, (long Version, string Json)> _docs = new();
    private readonly object _lock = new();

    public Task<PlayerRecord?> GetAsync(string playerId, CancellationToken ct = default) =>
        Task.FromResult(_docs.TryGetValue(playerId, out var d) ? StateJson.Deserialize<PlayerRecord>(d.Json) : null);

    public Task<bool> TryCreateAsync(PlayerRecord record, CancellationToken ct = default) =>
        Task.FromResult(_docs.TryAdd(record.State.PlayerId, (record.State.Version, StateJson.Serialize(record))));

    public Task<bool> TrySaveAsync(PlayerRecord record, long expectedVersion, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_docs.TryGetValue(record.State.PlayerId, out var current) || current.Version != expectedVersion)
                return Task.FromResult(false);
            _docs[record.State.PlayerId] = (record.State.Version, StateJson.Serialize(record));
            return Task.FromResult(true);
        }
    }
}

/// <summary>
/// One Firestore document per player in <c>players/{playerId}</c>:
/// <c>{ state: json, requests: json, version: long, updatedAt: timestamp }</c>.
/// Compare-and-set runs in a Firestore transaction.
/// </summary>
public sealed class FirestorePlayerStore(FirestoreDb db, StoreOptions options) : IPlayerStore
{
    private CollectionReference Players => db.Collection(options.CollectionPrefix + "players");

    public async Task<PlayerRecord?> GetAsync(string playerId, CancellationToken ct = default)
    {
        var snap = await Players.Document(playerId).GetSnapshotAsync(ct);
        return snap.Exists ? FromSnapshot(snap) : null;
    }

    public async Task<bool> TryCreateAsync(PlayerRecord record, CancellationToken ct = default)
    {
        try
        {
            await Players.Document(record.State.PlayerId).CreateAsync(ToFields(record), ct);
            return true;
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.AlreadyExists)
        {
            return false;
        }
    }

    public Task<bool> TrySaveAsync(PlayerRecord record, long expectedVersion, CancellationToken ct = default)
    {
        var doc = Players.Document(record.State.PlayerId);
        return db.RunTransactionAsync(async tx =>
        {
            var snap = await tx.GetSnapshotAsync(doc, ct);
            if (!snap.Exists || snap.GetValue<long>("version") != expectedVersion) return false;
            tx.Set(doc, ToFields(record));
            return true;
        }, cancellationToken: ct);
    }

    private static Dictionary<string, object> ToFields(PlayerRecord r) => new()
    {
        ["state"] = StateJson.Serialize(r.State),
        ["requests"] = StateJson.Serialize(r.Requests),
        ["version"] = r.State.Version,
        ["updatedAt"] = FieldValue.ServerTimestamp,
    };

    private static PlayerRecord FromSnapshot(DocumentSnapshot s) => new()
    {
        State = StateJson.Deserialize<GameState>(s.GetValue<string>("state")),
        Requests = s.TryGetValue<string>("requests", out var req) ? StateJson.Deserialize<List<RequestRecord>>(req) : new(),
    };
}
