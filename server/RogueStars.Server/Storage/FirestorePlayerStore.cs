using System.IO.Compression;
using System.Text;
using Google.Cloud.Firestore;
using RogueStars.Rules.Core;
using RogueStars.Server.Infrastructure;

namespace RogueStars.Server.Storage;

/// <summary>
/// One Firestore document per player in <c>players/{playerId}</c>:
/// <c>{ state: json, requests: json, version: long, updatedAt: timestamp }</c>.
/// Compare-and-set runs in a Firestore transaction. Firestore documents are limited to 1 MiB: a JSON field larger than
/// <see cref="CompressAbove"/> bytes is stored gzip-compressed instead (<c>stateGz</c> / <c>requestsGz</c>, bytes), and if
/// the document would still exceed <see cref="MaxDocumentBytes"/> the oldest request payloads are dropped (their replays
/// then return ok/events without payload). A 2-year player is far below the threshold (docs/server.md, Storage).
/// </summary>
public sealed class FirestorePlayerStore(FirestoreDb db, StoreOptions options) : IPlayerStore
{
    public const int CompressAbove = 256 * 1024;
    public const int MaxDocumentBytes = 1_000_000;
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

    public async Task<bool> TrySaveAsync(PlayerRecord record, long expectedVersion, CancellationToken ct = default)
    {
        // Saves never create the player (TryCreateAsync does), whatever the version. A record read by GetAsync carries the
        // document's update time: the write is preconditioned on it directly (one read per command, not two).
        Timestamp? known = record.UpdateTime != null && record.LoadedVersion == expectedVersion ? record.UpdateTime : null;
        var written = await FirestoreCas.TryWriteAsync(Players.Document(record.State.PlayerId), expectedVersion, create: false, () =>
        {
            var fields = ToFields(record);
            // Field updates keep untouched fields: remove the representation (plain/gzip) that is not written now.
            foreach (var name in new[] { "state", "requests" })
                if (!fields.ContainsKey(name)) fields[name] = FieldValue.Delete;
                else fields[name + "Gz"] = FieldValue.Delete;
            return fields;
        }, ct, known);
        if (written == null) return false;
        record.UpdateTime = written;
        record.LoadedVersion = record.State.Version;
        return true;
    }

    /// <summary>Approximate stored size of the record in bytes (Firestore counts UTF-8 string and byte lengths).</summary>
    public static int EncodedSize(PlayerRecord r) => Size(ToFields(r));

    private static int Size(Dictionary<string, object> fields) => fields.Sum(kv => kv.Key.Length + 1 + kv.Value switch
    {
        string str => Encoding.UTF8.GetByteCount(str) + 1,
        Blob b => b.ByteString.Length,
        _ => 8,
    });

    private static Dictionary<string, object> ToFields(PlayerRecord r)
    {
        var fields = new Dictionary<string, object> { ["version"] = r.State.Version, ["updatedAt"] = FieldValue.ServerTimestamp };
        Put(fields, "state", StateJson.Serialize(r.State));
        var requests = r.Requests;
        Put(fields, "requests", StateJson.Serialize(requests));
        // Last resort: drop the oldest payloads until the document fits.
        for (int i = 0; Size(fields) > MaxDocumentBytes && i < requests.Count; i++)
        {
            if (i == 0) requests = StateJson.Clone(requests);
            requests[i].Payload = null;
            Put(fields, "requests", StateJson.Serialize(requests));
        }
        return fields;
    }

    private static void Put(Dictionary<string, object> fields, string name, string json)
    {
        fields.Remove(name);
        fields.Remove(name + "Gz");
        if (Encoding.UTF8.GetByteCount(json) <= CompressAbove) { fields[name] = json; return; }
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal)) gz.Write(Encoding.UTF8.GetBytes(json));
        fields[name + "Gz"] = Blob.CopyFrom(ms.ToArray());
    }

    private static string? Read(DocumentSnapshot s, string name)
    {
        if (s.TryGetValue<string>(name, out var json)) return json;
        if (!s.TryGetValue<Blob>(name + "Gz", out var blob)) return null;
        using var gz = new GZipStream(new MemoryStream(blob.ByteString.ToByteArray()), CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static PlayerRecord FromSnapshot(DocumentSnapshot s) => new()
    {
        UpdateTime = s.UpdateTime,
        LoadedVersion = s.TryGetValue<long>("version", out var v) ? v : null,
        State = StateJson.Deserialize<GameState>(Read(s, "state") ?? throw new InvalidOperationException("player document without state: " + s.Id)),
        Requests = Read(s, "requests") is { } req ? StateJson.Deserialize<List<RequestRecord>>(req) : new(),
    };
}

/// <summary>
/// Optimistic compare-and-set on one Firestore document without transaction locks: create = the document must not exist (fails when the
/// document exists); otherwise read the document, compare its <c>version</c> field and write with an exact
/// <c>update_time</c> precondition. A concurrent writer makes the precondition fail and the call returns false, so callers
/// retry on fresh data (CommandService, DocStoreExtensions.MutateAsync). Pessimistic transactions were dropped because
/// hot documents (guilds, arena groups) hit lock timeouts under contention (found by the emulator tests).
/// </summary>
internal static class FirestoreCas
{
    public static async Task<bool> TryWriteAsync(DocumentReference doc, long expectedVersion, bool create, Func<Dictionary<string, object>> fields, CancellationToken ct) =>
        await TryWriteAsync(doc, expectedVersion, create, fields, ct, null) != null;

    /// <summary>
    /// Compare-and-set returning the write's update time (null = lost the race / version differs). With
    /// <paramref name="knownUpdateTime"/> (the update time of the read that saw <paramref name="expectedVersion"/>) the
    /// document is not read again: the update-time precondition alone rejects any write that happened since.
    /// </summary>
    public static async Task<Timestamp?> TryWriteAsync(DocumentReference doc, long expectedVersion, bool create, Func<Dictionary<string, object>> fields,
        CancellationToken ct, Timestamp? knownUpdateTime)
    {
        try
        {
            if (create)
            {
                var initial = fields();
                foreach (var k in initial.Where(kv => ReferenceEquals(kv.Value, FieldValue.Delete)).Select(kv => kv.Key).ToList()) initial.Remove(k);
                return (await doc.CreateAsync(initial, ct)).UpdateTime;
            }
            Timestamp updated;
            if (knownUpdateTime != null) updated = knownUpdateTime.Value;
            else
            {
                var snap = await doc.GetSnapshotAsync(ct);
                if (!snap.Exists || snap.GetValue<long>("version") != expectedVersion) return null;
                updated = snap.UpdateTime!.Value;
            }
            return (await doc.UpdateAsync(fields(), Precondition.LastUpdated(updated), ct)).UpdateTime;
        }
        catch (Grpc.Core.RpcException ex) when (IsLostRace(ex))
        {
            return null;
        }
    }

    public static async Task<bool> TryDeleteAsync(DocumentReference doc, long expectedVersion, CancellationToken ct)
    {
        try
        {
            var snap = await doc.GetSnapshotAsync(ct);
            if (!snap.Exists || snap.GetValue<long>("version") != expectedVersion) return false;
            await doc.DeleteAsync(Precondition.LastUpdated(snap.UpdateTime!.Value), ct);
            return true;
        }
        catch (Grpc.Core.RpcException ex) when (IsLostRace(ex))
        {
            return false;
        }
    }

    private static bool IsLostRace(Grpc.Core.RpcException ex) =>
        ex.StatusCode is Grpc.Core.StatusCode.AlreadyExists or Grpc.Core.StatusCode.FailedPrecondition or Grpc.Core.StatusCode.Aborted or Grpc.Core.StatusCode.NotFound;
}
