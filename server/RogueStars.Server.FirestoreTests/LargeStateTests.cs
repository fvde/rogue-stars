using System.Text;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;
using RogueStars.Sim;
using Xunit.Abstractions;

namespace RogueStars.Server.FirestoreTests;

/// <summary>
/// A 2-year player (simulator bot, Hardcore Whale = the largest state) stored through <see cref="FirestorePlayerStore"/>,
/// with a full idempotency window of real run-step results. Firestore's document limit is 1 MiB (1,048,576 bytes).
/// </summary>
[Trait("Category", "Firestore")]
public class LargeStateTests(ITestOutputHelper output)
{
    public const int FirestoreDocLimit = 1_048_576;
    private static readonly Lazy<SimPlayer> TwoYears = new(() =>
    {
        var p = new SimPlayer(TimeProfile.Hardcore, SpendProfile.Whale, 1);
        // The 730-day simulation takes minutes; cache the result per rules build (deterministic build = same MVID).
        string cache = Path.Combine(Path.GetTempPath(), "roguestars-sim", typeof(GameState).Assembly.ManifestModule.ModuleVersionId + "-hardcore-whale-730.json");
        if (File.Exists(cache))
        {
            var cached = StateJson.Deserialize<CachedSim>(File.ReadAllText(cache));
            p.S = cached.State;
            p.Now = cached.Now;
            return p;
        }
        for (int d = 0; d < 730; d++) p.PlayDay(d);
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllText(cache, StateJson.Serialize(new CachedSim { State = p.S, Now = p.Now }));
        return p;
    });

    private sealed class CachedSim
    {
        public GameState State { get; set; } = new();
        public long Now { get; set; }
    }

    /// <summary>Plays one more run with RunAutoStep and keeps the last 64 results exactly as the server stores them.</summary>
    private static List<RequestRecord> RealRequestWindow(SimPlayer p, out int maxPayload)
    {
        var window = new List<RequestRecord>();
        maxPayload = 0;
        p.S.Wallet.Add(Currencies.Stamina, 1000);
        var main = p.C.Portals.Values.First(x => x.Kind == RogueStars.Rules.Content.PortalKind.Main);
        foreach (var level in Enumerable.Reverse(main.LevelIds))
            if (p.Exec(new RunStart { LevelId = level, Mode = RunMode.Normal }).Ok) break;
        Assert.NotNull(p.S.Run.Active);
        for (int i = 0; i < 400 && p.S.Run.Active != null; i++)
        {
            var r = p.Exec(new RunAutoStep { Farming = true, FarmShops = true });
            if (!r.Ok) { p.Exec(new RunAbandon()); break; }
            var rec = new RequestRecord
            {
                RequestId = Guid.NewGuid().ToString(), AtMs = p.Now, Ok = true, Events = r.Events,
                Payload = r.Payload == null ? null : StateJson.ToWire(r.Payload),
            };
            maxPayload = Math.Max(maxPayload, Encoding.UTF8.GetByteCount(StateJson.Serialize(rec)));
            window.Add(rec);
            if (window.Count > 64) window.RemoveAt(0);
        }
        return window;
    }

    [FirestoreFact]
    public async Task TwoYearState_FitsTheDocumentLimit_AndRoundTrips()
    {
        var p = TwoYears.Value;
        var window = RealRequestWindow(p, out int maxRequest);
        var record = new PlayerRecord { State = p.S, Requests = window };

        int stateBytes = Encoding.UTF8.GetByteCount(StateJson.Serialize(record.State));
        int requestBytes = Encoding.UTF8.GetByteCount(StateJson.Serialize(record.Requests));
        int worstWindow = maxRequest * 64;
        output.WriteLine($"2-year Hardcore Whale: state {stateBytes:N0} B, request window (real, {window.Count}) {requestBytes:N0} B, " +
                         $"largest request record {maxRequest:N0} B (worst-case window {worstWindow:N0} B), stored {FirestorePlayerStore.EncodedSize(record):N0} B");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "state-size.txt"),
            $"state={stateBytes} requests={requestBytes} maxRequest={maxRequest} stored={FirestorePlayerStore.EncodedSize(record)}\n");

        var store = new FirestorePlayerStore(Emulator.Db, Emulator.Options());
        Assert.True(await store.TryCreateAsync(record));
        var loaded = await store.GetAsync(p.S.PlayerId);
        Assert.Equal(StateJson.Serialize(record.State), StateJson.Serialize(loaded!.State));
        Assert.Equal(window.Count, loaded.Requests.Count);
        Assert.True(FirestorePlayerStore.EncodedSize(record) < FirestoreDocLimit / 2, "player document above half the 1 MiB limit");
    }

    [FirestoreFact]
    public async Task OversizedRecord_IsCompressed_AndStillRoundTrips()
    {
        // Synthetic worst case: a state padded far beyond any real player (≈ 3 MB of JSON) must still store.
        var p = TwoYears.Value;
        var state = StateJson.Clone(p.S);
        for (int i = 0; i < 30_000; i++) state.Flags.Add("padding_flag_" + i.ToString("D6") + "_" + new string('x', 64));
        var record = new PlayerRecord { State = state };
        Assert.True(Encoding.UTF8.GetByteCount(StateJson.Serialize(state)) > 2 * FirestoreDocLimit);
        var store = new FirestorePlayerStore(Emulator.Db, Emulator.Options());
        Assert.True(await store.TryCreateAsync(record));
        var loaded = await store.GetAsync(state.PlayerId);
        Assert.Equal(state.Flags.Count, loaded!.State.Flags.Count);
    }
}
