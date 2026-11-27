using System.Net.Http.Json;
using System.Text.Json;
using RogueStars.Rules.Core;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Tests;

public class CoreApiTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Health_IsAnonymous()
    {
        var resp = await _app.CreateClient().GetAsync("/healthz");
        Assert.Equal(200, (int)resp.StatusCode);
    }

    [Fact]
    public async Task DevAuth_IssuesToken_AndSessionCreatesThenLoads()
    {
        var p = await _app.PlayerAsync("device-1", "Wormy");
        Assert.Equal("dev_device-1", p.PlayerId);

        var (status, body) = await p.PostAsync("/v1/session", new { displayName = "Other" });
        Assert.Equal(200, status);
        Assert.False(body.GetProperty("created").GetBoolean());
        Assert.Equal("Wormy", body.GetProperty("state").GetProperty("displayName").GetString());
        Assert.Contains("TestIncrement", body.GetProperty("commands").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("TestServerGrant", body.GetProperty("commands").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Requests_WithoutOrWithBadToken_Are401()
    {
        var anon = _app.CreateClient();
        Assert.Equal(401, (int)(await anon.GetAsync("/v1/state")).StatusCode);
        anon.DefaultRequestHeaders.Authorization = new("Bearer", "dev.ZGV2X3g.forged");
        Assert.Equal(401, (int)(await anon.GetAsync("/v1/state")).StatusCode);
    }

    [Fact]
    public async Task State_WithoutSession_Is404()
    {
        var http = _app.CreateClient();
        var auth = await (await http.PostAsJsonAsync("/v1/auth/dev", new { deviceId = "nosession" })).Content.ReadFromJsonAsync<JsonElement>();
        http.DefaultRequestHeaders.Authorization = new("Bearer", auth.GetProperty("token").GetString());
        Assert.Equal(404, (int)(await http.GetAsync("/v1/state")).StatusCode);
    }

    [Fact]
    public async Task Command_RoundTrip_PersistsAndReturnsEventsPayloadState()
    {
        var p = await _app.PlayerAsync("rt");
        var (status, body) = await p.CommandAsync("TestIncrement", new { amount = 3 }, "r1");
        Assert.Equal(200, status);
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.Single(body.GetProperty("events").EnumerateArray(), ev => ev.GetProperty("type").GetString() == "incremented");
        Assert.Equal(3, body.GetProperty("payload").GetProperty("total").GetInt64());
        Assert.Equal(1, body.GetProperty("version").GetInt64());
        Assert.Equal(3, body.GetProperty("state").GetProperty("counters").GetProperty("test_counter").GetInt64());

        Assert.Equal(3, await p.CounterAsync("test_counter"));
        Assert.Equal(1, (await p.StateAsync()).GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task Command_RuleFailure_IsOkFalse_AndStateUnchanged()
    {
        var p = await _app.PlayerAsync("fail");
        var (status, body) = await p.CommandAsync("TestIncrement", new { amount = 0 });
        Assert.Equal(200, status);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("amount must be positive", body.GetProperty("error").GetString());
        Assert.Equal(0, (await p.StateAsync()).GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task Command_UnknownOrBadArgs_Is400()
    {
        var p = await _app.PlayerAsync("bad");
        Assert.Equal(400, (await p.CommandAsync("NoSuchCommand")).Status);
        Assert.Equal(400, (await p.CommandAsync("TestIncrement", new { amount = "lots" })).Status);
    }

    [Fact]
    public async Task Idempotency_SameRequestId_ExecutesOnce()
    {
        var p = await _app.PlayerAsync("idem");
        var first = await p.CommandAsync("TestIncrement", new { amount = 2 }, "same");
        var second = await p.CommandAsync("TestIncrement", new { amount = 2 }, "same");
        Assert.False(first.Body.GetProperty("replayed").GetBoolean());
        Assert.True(second.Body.GetProperty("replayed").GetBoolean());
        Assert.True(second.Body.GetProperty("ok").GetBoolean());
        Assert.Equal(2, second.Body.GetProperty("payload").GetProperty("total").GetInt64());
        Assert.Equal(2, await p.CounterAsync("test_counter"));

        await p.CommandAsync("TestIncrement", new { amount = 2 }, "other");
        Assert.Equal(4, await p.CounterAsync("test_counter"));
    }

    [Fact]
    public async Task Concurrency_StaleExpectedVersion_AnswersTheCurrentState()
    {
        var p = await _app.PlayerAsync("cc");
        await p.CommandAsync("TestIncrement", new { amount = 1 });
        var (status, body) = await p.CommandAsync("TestIncrement", new { amount = 1 }, expectedVersion: 0);
        Assert.Equal(200, status);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("version_conflict", body.GetProperty("error").GetString());
        Assert.Equal(1, body.GetProperty("version").GetInt64());
        Assert.Equal(1, body.GetProperty("state").GetProperty("version").GetInt64());
        Assert.Equal(200, (await p.CommandAsync("TestIncrement", new { amount = 1 }, expectedVersion: 1)).Status);
    }

    [Fact]
    public async Task Store_CompareAndSet_RejectsStaleVersion()
    {
        var store = new InMemoryPlayerStore();
        var rec = new PlayerRecord { State = new GameState { PlayerId = "x" } };
        Assert.True(await store.TryCreateAsync(rec));
        Assert.False(await store.TryCreateAsync(rec));

        var a = (await store.GetAsync("x"))!;
        var b = (await store.GetAsync("x"))!;
        a.State.Version = 1;
        b.State.Version = 1;
        Assert.True(await store.TrySaveAsync(a, 0));
        Assert.False(await store.TrySaveAsync(b, 0));
        Assert.Equal(1, (await store.GetAsync("x"))!.State.Version);
    }

    [Fact]
    public async Task Concurrency_ParallelCommands_AllApplied()
    {
        var p = await _app.PlayerAsync("par");
        var tasks = Enumerable.Range(0, 10).Select(i => p.CommandAsync("TestIncrement", new { amount = 1 }, "par-" + i));
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal(200, r.Status));
        Assert.Equal(10, await p.CounterAsync("test_counter"));
        Assert.Equal(10, (await p.StateAsync()).GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task ServerOnly_Commands_AreRejectedFromClients()
    {
        var p = await _app.PlayerAsync("so");
        var (status, body) = await p.CommandAsync("TestServerGrant", new { amount = 5 });
        Assert.Equal(403, status);
        Assert.Equal("server_only_command", body.GetProperty("error").GetString());
        // Hook commands are server-only by configuration even without the attribute.
        Assert.Equal(403, (await p.CommandAsync("PurchaseComplete", new { productId = "gems" })).Status);
        Assert.Equal(403, (await p.CommandAsync("LiveOpsApplyOutcome", new { invasionId = "x" })).Status);
        Assert.Equal(0, await p.CounterAsync("granted"));
    }

    [Fact]
    public async Task ClientDenyList_BlocksConfiguredCommands()
    {
        using var app = new TestApp();
        app.Settings["RogueStars:Commands:ClientDenyList:0"] = "TestIncrement";
        var p = await app.PlayerAsync("deny");
        Assert.Equal(403, (await p.CommandAsync("TestIncrement", new { amount = 1 })).Status);
    }

    [Fact]
    public void DevAuth_IsRefusedInProduction()
    {
        using var app = new TestApp { Environment = "Production" };
        app.Settings["RogueStars:Auth:Mode"] = "dev";
        app.Settings["RogueStars:Store:Mode"] = "memory";
        var ex = Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.Contains("Dev auth", ex.ToString());
    }

    [Fact]
    public void StateJson_RoundTripsGameState()
    {
        var engine = new GameEngine { Cloner = StateJson.Clone };
        var s = engine.NewPlayer("p", 1000, 42);
        s.Counters["a"] = 5;
        s.Flags.Add("f");
        var copy = StateJson.Clone(s);
        Assert.Equal(5, copy.Counter("a"));
        Assert.Contains("f", copy.Flags);
        Assert.Equal(s.Rng.S0, copy.Rng.S0);
        Assert.NotSame(s, copy);
    }
}
