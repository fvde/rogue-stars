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

    /// <summary>
    /// Firestore update time of the document this record was read from (not persisted). A save of the record uses it as its
    /// precondition, so compare-and-set needs no second read (FirestorePlayerStore.TrySaveAsync).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Timestamp? UpdateTime { get; set; }

    /// <summary>The stored version when the record was read (not persisted); pairs with <see cref="UpdateTime"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public long? LoadedVersion { get; set; }
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

