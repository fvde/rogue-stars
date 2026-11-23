using Google.Cloud.Firestore;
using RogueStars.Server.Infrastructure;

namespace RogueStars.Server.Storage;

public sealed record Doc<T>(string Id, T Value, long Version);

/// <summary>
/// Small versioned document store for the cross-player features (arena groups, guilds, chat, invasions).
/// Every document carries a version; writes are compare-and-set, so read-modify-write loops are safe across
/// Cloud Run instances. Document ids are chosen so that no secondary indexes are required.
/// </summary>
public interface IDocStore
{
    Task<Doc<T>?> GetAsync<T>(string collection, string id, CancellationToken ct = default) where T : class;
    /// <summary>Writes when the stored version equals <paramref name="expectedVersion"/> (0 = must not exist).</summary>
    Task<bool> TryPutAsync<T>(string collection, string id, T value, long expectedVersion, CancellationToken ct = default) where T : class;
    Task<bool> TryDeleteAsync(string collection, string id, long expectedVersion, CancellationToken ct = default);
    /// <summary>Documents ordered by id, optionally restricted to ids starting with <paramref name="idPrefix"/>.</summary>
    Task<IReadOnlyList<Doc<T>>> ListAsync<T>(string collection, string? idPrefix = null, int limit = 100, CancellationToken ct = default) where T : class;
}

public static class DocStoreExtensions
{
    /// <summary>
    /// Optimistic read-modify-write. <paramref name="mutate"/> receives a private copy (null when missing) and
    /// returns the value to store, or null to skip writing. Throw <see cref="ApiException"/> to abort.
    /// Returns the stored value (or the unchanged current one when skipped).
    /// </summary>
    public static async Task<T?> MutateAsync<T>(this IDocStore store, string collection, string id, Func<T?, T?> mutate,
        CancellationToken ct = default) where T : class
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var current = await store.GetAsync<T>(collection, id, ct);
            var next = mutate(current?.Value);
            if (next == null) return current?.Value;
            if (await store.TryPutAsync(collection, id, next, current?.Version ?? 0, ct)) return next;
            // Jittered backoff so many writers on one hot document (guild, arena group) do not keep colliding.
            await Task.Delay(Random.Shared.Next(1, 4 << Math.Min(attempt, 5)), ct);
        }
        throw ApiException.Conflict("contention");
    }
}

public sealed class InMemoryDocStore : IDocStore
{
    private readonly Dictionary<string, SortedDictionary<string, (long Version, string Json)>> _cols = new();
    private readonly object _lock = new();

    private SortedDictionary<string, (long, string)> Col(string c)
    {
        if (!_cols.TryGetValue(c, out var col)) _cols[c] = col = new SortedDictionary<string, (long, string)>(StringComparer.Ordinal);
        return col;
    }

    public Task<Doc<T>?> GetAsync<T>(string collection, string id, CancellationToken ct = default) where T : class
    {
        lock (_lock)
        {
            return Task.FromResult(Col(collection).TryGetValue(id, out var d)
                ? new Doc<T>(id, StateJson.Deserialize<T>(d.Item2), d.Item1)
                : null);
        }
    }

    public Task<bool> TryPutAsync<T>(string collection, string id, T value, long expectedVersion, CancellationToken ct = default) where T : class
    {
        lock (_lock)
        {
            var col = Col(collection);
            long current = col.TryGetValue(id, out var d) ? d.Item1 : 0;
            if (current != expectedVersion) return Task.FromResult(false);
            col[id] = (current + 1, StateJson.Serialize(value));
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryDeleteAsync(string collection, string id, long expectedVersion, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var col = Col(collection);
            if (!col.TryGetValue(id, out var d) || d.Item1 != expectedVersion) return Task.FromResult(false);
            col.Remove(id);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<Doc<T>>> ListAsync<T>(string collection, string? idPrefix = null, int limit = 100, CancellationToken ct = default) where T : class
    {
        lock (_lock)
        {
            IReadOnlyList<Doc<T>> list = Col(collection)
                .Where(kv => idPrefix == null || kv.Key.StartsWith(idPrefix, StringComparison.Ordinal))
                .Take(limit)
                .Select(kv => new Doc<T>(kv.Key, StateJson.Deserialize<T>(kv.Value.Item2), kv.Value.Item1))
                .ToList();
            return Task.FromResult(list);
        }
    }
}

/// <summary>Firestore documents <c>{ json: string, version: long, updatedAt: timestamp }</c>; optimistic compare-and-set (<see cref="FirestoreCas"/>).</summary>
public sealed class FirestoreDocStore(FirestoreDb db, StoreOptions options) : IDocStore
{
    private CollectionReference Col(string name) => db.Collection(options.CollectionPrefix + name);

    public async Task<Doc<T>?> GetAsync<T>(string collection, string id, CancellationToken ct = default) where T : class
    {
        var snap = await Col(collection).Document(id).GetSnapshotAsync(ct);
        return snap.Exists ? ToDoc<T>(snap) : null;
    }

    public Task<bool> TryPutAsync<T>(string collection, string id, T value, long expectedVersion, CancellationToken ct = default) where T : class =>
        FirestoreCas.TryWriteAsync(Col(collection).Document(id), expectedVersion, create: expectedVersion == 0, () => new Dictionary<string, object>
        {
            ["json"] = StateJson.Serialize(value),
            ["version"] = expectedVersion + 1,
            ["updatedAt"] = FieldValue.ServerTimestamp,
        }, ct);

    public Task<bool> TryDeleteAsync(string collection, string id, long expectedVersion, CancellationToken ct = default) =>
        FirestoreCas.TryDeleteAsync(Col(collection).Document(id), expectedVersion, ct);

    public async Task<IReadOnlyList<Doc<T>>> ListAsync<T>(string collection, string? idPrefix = null, int limit = 100, CancellationToken ct = default) where T : class
    {
        Query q = Col(collection).OrderBy(FieldPath.DocumentId);
        if (!string.IsNullOrEmpty(idPrefix))
            q = q.StartAt(idPrefix).EndAt(idPrefix + "");
        var snaps = await q.Limit(limit).GetSnapshotAsync(ct);
        return snaps.Documents.Select(ToDoc<T>).ToList();
    }

    private static Doc<T> ToDoc<T>(DocumentSnapshot s) =>
        new(s.Id, StateJson.Deserialize<T>(s.GetValue<string>("json")), s.GetValue<long>("version"));
}
