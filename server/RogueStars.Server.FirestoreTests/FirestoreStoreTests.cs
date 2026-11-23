using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;

namespace RogueStars.Server.FirestoreTests;

public sealed class Counter
{
    public int N { get; set; }
    public List<string> Seen { get; set; } = new();
}

[Trait("Category", "Firestore")]
public class FirestorePlayerStoreTests
{
    private readonly FirestorePlayerStore _store = new(Emulator.Db, Emulator.Options());
    private static readonly GameEngine Engine = new(ContentDb.Default) { Cloner = StateJson.Clone };

    private static PlayerRecord NewRecord(string id) => new() { State = Engine.NewPlayer(id, 1_800_000_000_000, 42) };

    [FirestoreFact]
    public async Task Crud_And_CompareAndSet()
    {
        var rec = NewRecord("p1");
        rec.State.DisplayName = "Wörm ✨";
        Assert.Null(await _store.GetAsync("p1"));
        Assert.True(await _store.TryCreateAsync(rec));
        Assert.False(await _store.TryCreateAsync(NewRecord("p1")));

        var loaded = await _store.GetAsync("p1");
        Assert.NotNull(loaded);
        Assert.Equal(StateJson.Serialize(rec.State), StateJson.Serialize(loaded!.State));
        Assert.Equal("Wörm ✨", loaded.State.DisplayName);

        long v = loaded.State.Version;
        loaded.State.Version = v + 1;
        loaded.Requests.Add(new RequestRecord { RequestId = "r1", Ok = true, AtMs = 5 });
        Assert.True(await _store.TrySaveAsync(loaded, v));
        // Stale writer (still holds version v) loses.
        Assert.False(await _store.TrySaveAsync(loaded, v));
        var again = await _store.GetAsync("p1");
        Assert.Equal(v + 1, again!.State.Version);
        Assert.Equal("r1", Assert.Single(again.Requests).RequestId);
        // Saving a player that does not exist fails instead of creating it.
        Assert.False(await _store.TrySaveAsync(NewRecord("ghost"), 0));
        Assert.Null(await _store.GetAsync("ghost"));
    }

    [FirestoreFact]
    public async Task ConcurrentWriters_ExactlyOneWinsPerVersion()
    {
        var rec = NewRecord("hot");
        Assert.True(await _store.TryCreateAsync(rec));
        long v = rec.State.Version;
        var tasks = Enumerable.Range(0, 12).Select(async i =>
        {
            var mine = (await _store.GetAsync("hot"))!;
            mine.State.DisplayName = "writer" + i;
            mine.State.Version = v + 1;
            return await _store.TrySaveAsync(mine, v);
        }).ToList();
        var results = await Task.WhenAll(tasks);
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(v + 1, (await _store.GetAsync("hot"))!.State.Version);
    }
}

[Trait("Category", "Firestore")]
public class FirestoreDocStoreTests
{
    private readonly FirestoreDocStore _docs = new(Emulator.Db, Emulator.Options());

    [FirestoreFact]
    public async Task Crud_Versions_Delete()
    {
        Assert.Null(await _docs.GetAsync<Counter>("c", "a"));
        Assert.True(await _docs.TryPutAsync("c", "a", new Counter { N = 1 }, 0));
        Assert.False(await _docs.TryPutAsync("c", "a", new Counter { N = 9 }, 0)); // must not exist
        var d = await _docs.GetAsync<Counter>("c", "a");
        Assert.Equal((1, 1L), (d!.Value.N, d.Version));
        Assert.True(await _docs.TryPutAsync("c", "a", new Counter { N = 2 }, 1));
        Assert.False(await _docs.TryPutAsync("c", "a", new Counter { N = 3 }, 1));
        Assert.False(await _docs.TryDeleteAsync("c", "a", 1));
        Assert.True(await _docs.TryDeleteAsync("c", "a", 2));
        Assert.Null(await _docs.GetAsync<Counter>("c", "a"));
        Assert.False(await _docs.TryDeleteAsync("c", "missing", 0));
    }

    [FirestoreFact]
    public async Task List_ByIdPrefix_InIdOrder_WithLimit()
    {
        foreach (var id in new[] { "2026-W47_L0_1", "2026-W47_L0_0", "2026-W47_L1_0", "2026-W48_L0_0", "2026-W47_L0_10", "2026-W47" })
            Assert.True(await _docs.TryPutAsync("g", id, new Counter { N = id.Length }, 0));
        var l0 = (await _docs.ListAsync<Counter>("g", "2026-W47_L0_")).Select(d => d.Id).ToList();
        Assert.Equal(new[] { "2026-W47_L0_0", "2026-W47_L0_1", "2026-W47_L0_10" }, l0);
        Assert.Equal(5, (await _docs.ListAsync<Counter>("g", "2026-W47")).Count);
        Assert.Equal(6, (await _docs.ListAsync<Counter>("g")).Count);
        Assert.Equal(2, (await _docs.ListAsync<Counter>("g", null, 2)).Count);
        Assert.Empty(await _docs.ListAsync<Counter>("g", "2027"));
    }

    [FirestoreFact]
    public async Task MutateAsync_UnderContention_LosesNoUpdate()
    {
        const int writers = 16;
        await Task.WhenAll(Enumerable.Range(0, writers).Select(i => _docs.MutateAsync<Counter>("m", "x", c =>
        {
            c ??= new Counter();
            c.N++;
            c.Seen.Add("w" + i);
            return c;
        })));
        var final = await _docs.GetAsync<Counter>("m", "x");
        Assert.Equal(writers, final!.Value.N);
        Assert.Equal(writers, final.Value.Seen.Distinct().Count());
        Assert.Equal(writers, final.Version);
    }

    [FirestoreFact]
    public async Task IdempotencyClaims_FirstWriterWins()
    {
        // Same pattern as iap_receipts / ad_events: create-if-absent decides the owner.
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            _docs.TryPutAsync(Collections.AdEvents, "evt", new Counter { N = i }, 0)));
        Assert.Equal(1, results.Count(r => r));
    }
}
