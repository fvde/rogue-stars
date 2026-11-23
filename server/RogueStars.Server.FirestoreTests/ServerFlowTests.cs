using System.Net.Http.Json;
using RogueStars.Rules.Core;
using RogueStars.Server.Tests;

namespace RogueStars.Server.FirestoreTests;

/// <summary>
/// The whole server on Firestore: session → commands (idempotency, concurrency) → IAP/ads → arena → guild → invasion
/// jobs. (The complete unit suite can also run on the emulator: ROGUESTARS_TEST_STORE=firestore, see scripts/firestore-tests.sh.)
/// </summary>
[Trait("Category", "Firestore")]
public class ServerFlowTests : IDisposable
{
    private readonly TestApp _app = new();

    public ServerFlowTests()
    {
        _app.Settings["RogueStars:Store:Mode"] = "firestore";
        _app.Settings["RogueStars:Store:FirestoreProjectId"] = Emulator.ProjectId;
        _app.Settings["RogueStars:Store:CollectionPrefix"] = Emulator.Options().CollectionPrefix;
    }

    public void Dispose() => _app.Dispose();

    [FirestoreFact]
    public async Task FullFlow_OnFirestore()
    {
        var health = await PlayerClient.Read(await _app.CreateClient().GetAsync("/healthz"));
        Assert.Equal("firestore", health.GetProperty("store").GetString());

        // Session + commands + idempotent replay.
        var a = await _app.PlayerAsync("fa", "Alpha");
        var b = await _app.PlayerAsync("fb", "Beta");
        var first = await a.CommandAsync("BuyStamina", new { method = 2 }, "req-1");
        Assert.True(first.Body.GetProperty("ok").GetBoolean());
        long version = first.Body.GetProperty("version").GetInt64();
        var replay = await a.CommandAsync("BuyStamina", new { method = 2 }, "req-1");
        Assert.True(replay.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(version, replay.Body.GetProperty("version").GetInt64());
        Assert.Equal(409, (await a.CommandAsync("BuyStamina", new { method = 0 }, "req-2", expectedVersion: version - 1)).Status);

        // Concurrent commands on one player: every one applied exactly once (compare-and-set + retry).
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => a.CommandAsync("BuyStamina", new { method = 0 }, "par-" + i)));
        Assert.All(results, r => Assert.Equal(200, r.Status));
        int ok = results.Count(r => r.Body.GetProperty("ok").GetBoolean());
        Assert.Equal(ok, await a.CounterAsync("stamina_refills_diamonds"));
        Assert.Equal(version + ok, (await a.GetAsync("/v1/state")).Body.GetProperty("version").GetInt64());

        // IAP (fake receipts) and ad callbacks (fake verifier) on Firestore claims.
        Assert.True((await a.CommandAsync("PurchaseStart", new { productId = "diamonds_60" })).Body.GetProperty("ok").GetBoolean());
        Assert.True((await a.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "fake-fs-1" })).Body.GetProperty("ok").GetBoolean());
        Assert.True((await a.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "fake-fs-1" })).Body.GetProperty("alreadyCompleted").GetBoolean());
        Assert.Equal(409, (await b.PostAsync("/v1/iap/verify", new { productId = "diamonds_60", purchaseToken = "fake-fs-1" })).Status);
        var http = _app.CreateClient();
        var ad = $"/v1/ads/ssv?user_id={a.PlayerId}&event_id=fs-ev-1&custom_data=daily_diamonds_ad";
        Assert.True((await PlayerClient.Read(await http.GetAsync(ad))).GetProperty("ok").GetBoolean());
        Assert.True((await PlayerClient.Read(await http.GetAsync(ad))).GetProperty("duplicate").GetBoolean());

        // Arena.
        Assert.Equal(200, (await b.PostAsync("/v1/arena/snapshot")).Status);
        var (fs, fight) = await a.PostAsync("/v1/arena/fight");
        Assert.Equal(200, fs);
        Assert.True(fight.GetProperty("won").GetBoolean());
        var board = (await a.GetAsync("/v1/arena/leaderboard")).Body;
        Assert.Equal(1, board.GetProperty("myRank").GetInt32());
        Assert.Equal(System.Net.HttpStatusCode.OK, (await _app.JobClient().PostAsync("/internal/jobs/weekly-arena?week=2026-W46", null)).StatusCode);

        // Guild.
        var (gs, guild) = await a.PostAsync("/v1/guilds", new { name = "Firestore Worms" });
        Assert.Equal(200, gs);
        string guildId = guild.GetProperty("guild").GetProperty("id").GetString()!;
        Assert.Equal(409, (await b.PostAsync("/v1/guilds", new { name = "firestore  worms" })).Status);
        Assert.Equal(200, (await b.PostAsync($"/v1/guilds/{guildId}/join")).Status);
        Assert.Equal(200, (await b.PostAsync("/v1/guilds/chat", new { text = "hello" })).Status);
        var chat = (await a.GetAsync("/v1/guilds/chat?since=0")).Body.GetProperty("messages");
        Assert.Equal("hello", chat[0].GetProperty("text").GetString());
        Assert.Equal(2, (await a.GetAsync("/v1/guilds/mine")).Body.GetProperty("guild").GetProperty("members").GetArrayLength());

        // Invasion: create, attack (guild contribution), resolve job.
        long now = _app.Time.GetUtcNow().ToUnixTimeMilliseconds();
        var created = await _app.JobClient().PostAsJsonAsync("/internal/invasions",
            new { id = "fs-inv", name = "Rust Swarm", typeId = "rust_swarm", startsMs = now - 1000, endsMs = now + 3_600_000, hpPerMember = 500 });
        Assert.Equal(200, (int)created.StatusCode);
        var (atkStatus, atk) = await a.PostAsync("/v1/invasions/fs-inv/attack", new { requestId = "atk-1" });
        Assert.Equal(200, atkStatus);
        Assert.Equal(1000, atk.GetProperty("damage").GetInt64());
        await b.PostAsync("/v1/invasions/fs-inv/attack", new { requestId = "atk-2" });
        var contributions = (await a.GetAsync($"/v1/guilds/{guildId}/contributions")).Body.GetProperty("contributions");
        Assert.Equal(2000, contributions.GetProperty("total").GetInt64());
        _app.Time.Advance(TimeSpan.FromHours(2));
        var resolve = await PlayerClient.Read(await _app.JobClient().PostAsync("/internal/jobs/invasion-resolve", null));
        Assert.True(resolve.GetProperty("ok").GetBoolean());
        var group = (await a.GetAsync("/v1/invasions/fs-inv/group")).Body;
        Assert.Equal("repelled", group.GetProperty("outcome").GetString());
        Assert.Equal(System.Net.HttpStatusCode.OK, (await _app.JobClient().PostAsync("/internal/jobs/daily-reset", null)).StatusCode);
    }
}
