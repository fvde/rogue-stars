using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using RogueStars.Server.Commands;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Tests;

/// <summary>Counts the writes of the player store it wraps (creates and saves).</summary>
public sealed class CountingPlayerStore(IPlayerStore inner) : IPlayerStore
{
    private int _writes;
    public int Writes => Volatile.Read(ref _writes);

    public Task<PlayerRecord?> GetAsync(string playerId, CancellationToken ct = default) => inner.GetAsync(playerId, ct);

    public Task<bool> TryCreateAsync(PlayerRecord record, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _writes);
        return inner.TryCreateAsync(record, ct);
    }

    public Task<bool> TrySaveAsync(PlayerRecord record, long expectedVersion, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _writes);
        return inner.TrySaveAsync(record, expectedVersion, ct);
    }

    /// <summary>Wraps the registered <see cref="IPlayerStore"/>; the counter is resolvable as <see cref="CountingPlayerStore"/>.</summary>
    public static void Decorate(IServiceCollection services)
    {
        var d = services.Last(x => x.ServiceType == typeof(IPlayerStore));
        services.Remove(d);
        services.AddSingleton(sp => new CountingPlayerStore(d.ImplementationInstance as IPlayerStore
            ?? (d.ImplementationFactory != null ? (IPlayerStore)d.ImplementationFactory(sp) : (IPlayerStore)ActivatorUtilities.CreateInstance(sp, d.ImplementationType!))));
        services.AddSingleton<IPlayerStore>(sp => sp.GetRequiredService<CountingPlayerStore>());
    }
}

/// <summary>Sampler for tests: a fixed answer per tier (default: never).</summary>
public sealed class FixedSampler : IBattleSampler
{
    public bool Standard, Elite, Boss;
    public bool All { set { Standard = Elite = Boss = value; } }
    public int Asked;
    public bool Sample(string playerId, BattleTier tier, double rate)
    {
        Asked++;
        return tier switch { BattleTier.Boss => Boss, BattleTier.Elite => Elite, _ => Standard };
    }
}

/// <summary>
/// A local-first client as the Unity client runs it: its own engine and state copy, executes each command locally first,
/// then sends it with expectedVersion, its time, its state hash and its battle claims; adopts the server state on mismatch.
/// </summary>
public sealed class LocalFirstClient(PlayerClient p, GameEngine engine, GameState state, long serverTimeMs)
{
    public PlayerClient Player => p;
    public GameState State = state;
    public long NowMs = serverTimeMs;
    public JsonElement LastReply;
    public int Adopted;
    /// <summary>Where the predicted state differed from the server's at the last mismatch (diagnostics).</summary>
    public List<string> LastDiff = new();

    public static async Task<LocalFirstClient> ConnectAsync(TestApp app, string device)
    {
        var p = await app.PlayerAsync(device);
        var (_, body) = await p.PostAsync("/v1/session", new { });
        var state = body.GetProperty("state").Deserialize<GameState>(StateJson.Wire)!;
        return new LocalFirstClient(p, app.Service<GameEngine>(), state, body.GetProperty("serverTimeMs").GetInt64());
    }

    /// <summary>Executes locally, then on the server. Returns the local result; <see cref="LastReply"/> holds the server's.</summary>
    public async Task<CommandResult> RunAsync(GameCommand cmd, Func<List<BattleResult>, List<BattleResult>>? tamper = null, string? hashOverride = null)
    {
        cmd.RequestId ??= Guid.NewGuid().ToString("N");
        long expected = State.Version;
        long at = Math.Max(NowMs, State.LastCommandMs);
        var before = StateJson.Clone(State);
        var local = engine.Execute(ref State, cmd, at);
        Assert.True(local.Ok, local.Error);
        var claims = local.Battles.Select(b => b.Result.Summary()).ToList();
        if (tamper != null && claims.Count > 0)
        {
            // a cheating client: its battles end the way it wants, and its state follows its claims
            claims = tamper(claims);
            State = before;
            local = engine.Execute(ref State, cmd, at, (ctx, setup, rng, kind, index) => index < claims.Count ? claims[index] : null);
        }
        var args = JsonSerializer.SerializeToElement(cmd, cmd.GetType(), StateJson.Wire);
        var (status, body) = await p.PostAsync("/v1/command", new
        {
            type = cmd.Type, requestId = cmd.RequestId, args, expectedVersion = expected,
            stateHash = hashOverride ?? StateHash.Hex(State), atMs = at, battles = JsonSerializer.SerializeToElement(claims, StateJson.Wire),
        });
        Assert.Equal(200, status);
        LastReply = body;
        if (body.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.Object)
        {
            var server = st.Deserialize<GameState>(StateJson.Wire)!;
            LastDiff.Clear();
            Diff(JsonSerializer.SerializeToNode(State, StateJson.Options), JsonSerializer.SerializeToNode(server, StateJson.Options), "", LastDiff);
            State = server;
            Adopted++;
        }
        return local;
    }

    public Task<(int Status, JsonElement Body)> Raw(string type, object args, long expectedVersion, string hash, string? requestId = null) =>
        p.PostAsync("/v1/command", new { type, requestId = requestId ?? Guid.NewGuid().ToString("N"), args, expectedVersion, stateHash = hash, atMs = NowMs });

    public static void Diff(System.Text.Json.Nodes.JsonNode? a, System.Text.Json.Nodes.JsonNode? b, string path, List<string> res)
    {
        if (res.Count > 20) return;
        if (a is System.Text.Json.Nodes.JsonObject oa && b is System.Text.Json.Nodes.JsonObject ob)
        {
            foreach (var kv in oa) Diff(kv.Value, ob[kv.Key], path + "." + kv.Key, res);
            foreach (var kv in ob) if (!oa.ContainsKey(kv.Key)) res.Add(path + "." + kv.Key + " only on the server");
            return;
        }
        if (a is System.Text.Json.Nodes.JsonArray aa && b is System.Text.Json.Nodes.JsonArray ab)
        {
            if (aa.Count != ab.Count) { res.Add(path + " count " + aa.Count + " vs " + ab.Count); return; }
            for (int i = 0; i < aa.Count; i++) Diff(aa[i], ab[i], path + "[" + i + "]", res);
            return;
        }
        string sa = a?.ToJsonString() ?? "null", sb = b?.ToJsonString() ?? "null";
        if (sa != sb) res.Add(path + ": " + sa + " vs " + sb);
    }

    public bool Matched => LastReply.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.True;
}

public class LocalFirstTests : IDisposable
{
    private readonly TestApp _app = new() { RealBattles = true };
    private readonly FixedSampler _sampler = new();

    public LocalFirstTests()
    {
        _app.ConfigureServices = s =>
        {
            s.AddSingleton<IBattleSampler>(_sampler);
            CountingPlayerStore.Decorate(s);
        };
    }

    public void Dispose() => _app.Dispose();

    private Rules.Content.ContentDb Content => _app.Service<GameEngine>().Content;

    [Fact]
    public async Task LocalApply_ThenServerAgrees_SmallAckWithoutState()
    {
        var c = await LocalFirstClient.ConnectAsync(_app, "lf-ack");
        await c.RunAsync(new TestIncrement { Amount = 2 });
        Assert.True(c.Matched);
        Assert.True(c.LastReply.GetProperty("ok").GetBoolean());
        Assert.False(c.LastReply.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.Object);
        Assert.Equal(StateHash.Hex(c.State), c.LastReply.GetProperty("stateHash").GetString());
        Assert.Equal(c.State.Version, c.LastReply.GetProperty("version").GetInt64());
        Assert.True(c.LastReply.GetRawText().Length < 400, c.LastReply.GetRawText());
        Assert.Equal(0, c.Adopted);
        // the stored state is what the client predicted
        var stored = (await _app.Service<IPlayerStore>().GetAsync(c.State.PlayerId))!.State;
        Assert.Equal(StateHash.Hex(c.State), StateHash.Hex(stored));
    }

    [Fact]
    public async Task LocalApply_AfterTimePassed_UsesTheClientsTime()
    {
        var c = await LocalFirstClient.ConnectAsync(_app, "lf-time");
        _app.Time.Advance(TimeSpan.FromMinutes(3)); // network delay / queue: the server receives it later than it ran
        c.NowMs += 60_000;
        await c.RunAsync(new TestIncrement { Amount = 1 });
        Assert.True(c.Matched, string.Join("\n", c.LastDiff));
        Assert.Equal(c.NowMs, (await _app.Service<IPlayerStore>().GetAsync(c.State.PlayerId))!.State.LastCommandMs);
    }

    [Fact]
    public async Task ClientTime_IsClamped_ToTheServerClockWindow()
    {
        var svc = _app.Service<CommandService>();
        var clock = _app.Service<Clock>();
        var s = new GameState { LastCommandMs = clock.NowMs - 1000 };
        Assert.Equal(clock.NowMs + 5_000, svc.CommandTime(clock.NowMs + 3_600_000, s)); // no fast-forwarded clock
        Assert.Equal(clock.NowMs - 1000, svc.CommandTime(clock.NowMs - 5000, s)); // never before the last command
        s.LastCommandMs = 0;
        Assert.Equal(clock.NowMs - 600_000, svc.CommandTime(1, s)); // at most 10 minutes in the past
        Assert.Equal(clock.NowMs, svc.CommandTime(null, s));
    }

    private async Task<LocalFirstClient> InRunAsync(string device)
    {
        var c = await LocalFirstClient.ConnectAsync(_app, device);
        await c.RunAsync(new RunStart { LevelId = Content.MainLevel(1).Id, Mode = RunMode.Normal });
        Assert.True(c.Matched, c.LastReply.GetRawText());
        return c;
    }

    /// <summary>Steps until <paramref name="battles"/> battles were fought (or the run ended).</summary>
    private static async Task<int> FightAsync(LocalFirstClient c, int battles, Func<List<BattleResult>, List<BattleResult>>? tamper = null)
    {
        int fought = 0;
        for (int i = 0; i < 300 && c.State.Run.Active != null && fought < battles; i++)
        {
            var r = await c.RunAsync(new RunAutoStep { Farming = true, FarmShops = true }, fought == battles - 1 ? tamper : null);
            fought += r.Battles.Count;
        }
        return fought;
    }

    [Fact]
    public async Task Battles_RunOnTheClient_ServerSamples_AndAgrees()
    {
        _sampler.All = true; // re-simulate every battle
        var c = await InRunAsync("lf-battles");
        int fought = await FightAsync(c, 3);
        Assert.True(fought >= 1);
        Assert.True(c.Adopted == 0, string.Join("\n", c.LastDiff));
        var audit = (await _app.Service<BattleAuditService>().GetAsync(c.State.PlayerId))!.Value;
        Assert.Equal(fought, audit.Validated);
        if (audit.Recent.Count > 0)
        {
            var d = new List<string>();
            LocalFirstClient.Diff(JsonSerializer.SerializeToNode(audit.Recent[0].Client, StateJson.Options), JsonSerializer.SerializeToNode(audit.Recent[0].Server, StateJson.Options), "", d);
            Assert.Fail(string.Join("\n", d));
        }
        Assert.Equal(0, audit.Mismatches);
        Assert.False(audit.Flagged);
    }

    [Fact]
    public async Task UnsampledBattles_TakeTheClaim_WithoutAudit()
    {
        _sampler.All = false;
        var c = await InRunAsync("lf-trust");
        int fought = await FightAsync(c, 2);
        Assert.True(fought >= 1 && _sampler.Asked >= fought);
        Assert.Equal(0, c.Adopted);
        Assert.Null(await _app.Service<BattleAuditService>().GetAsync(c.State.PlayerId));
    }

    private static List<BattleResult> InflateWorm(List<BattleResult> claims)
    {
        foreach (var b in claims) { b.WormHpLeft = b.WormMaxHp; b.Winner = BattleSide.Player; b.Rounds = 1; b.RunCoins += 50; }
        return claims;
    }

    [Fact]
    public async Task SampledBattle_Disagreeing_ServerResultWins_ClientAdoptsServerState()
    {
        _sampler.All = true;
        var c = await InRunAsync("lf-cheat");
        int fought = await FightAsync(c, 1, InflateWorm);
        Assert.Equal(1, fought);
        Assert.False(c.Matched);
        Assert.Equal(1, c.Adopted);
        // the adopted state is the server's: identical to what is stored
        var stored = (await _app.Service<IPlayerStore>().GetAsync(c.State.PlayerId))!.State;
        Assert.Equal(StateHash.Hex(stored), StateHash.Hex(c.State));
        Assert.Equal(c.LastReply.GetProperty("stateHash").GetString(), StateHash.Hex(c.State));
        var audit = (await _app.Service<BattleAuditService>().GetAsync(c.State.PlayerId))!.Value;
        Assert.Equal(1, audit.Mismatches);
        var m = Assert.Single(audit.Recent);
        Assert.Equal(m.Server!.WormHpLeft, stored.Run.Active?.LastBattle?.WormHpLeft ?? stored.Run.LastSummary!.LastBattle!.WormHpLeft);
        // dev server: the developer sees both results
        var check = Assert.Single(c.LastReply.GetProperty("battleChecks").EnumerateArray());
        Assert.False(check.GetProperty("match").GetBoolean());
        Assert.NotEqual(check.GetProperty("client").GetString(), check.GetProperty("server").GetString());
        // the client keeps playing on the server's state
        await c.RunAsync(new TestIncrement { Amount = 1 });
        Assert.True(c.Matched);
    }

    [Fact]
    public async Task ProductionServer_NeverReportsChecksToTheClient()
    {
        _app.Settings["RogueStars:Battles:ReportToClient"] = "false";
        _app.Settings["RogueStars:DevTools:AllowAll"] = "false"; // not an owner either
        _sampler.All = true;
        var c = await InRunAsync("lf-prod");
        await FightAsync(c, 1, InflateWorm);
        Assert.False(c.LastReply.TryGetProperty("battleChecks", out var bc) && bc.ValueKind == JsonValueKind.Array);
        Assert.Equal(1, (await _app.Service<BattleAuditService>().GetAsync(c.State.PlayerId))!.Value.Mismatches);
    }

    [Fact]
    public async Task WrongPrediction_IsAnsweredWithTheFullState()
    {
        var c = await LocalFirstClient.ConnectAsync(_app, "lf-wrong");
        await c.RunAsync(new TestIncrement { Amount = 1 }, hashOverride: "0000000000000000");
        Assert.False(c.Matched);
        Assert.True(c.LastReply.GetProperty("ok").GetBoolean());
        Assert.Equal(1, c.Adopted);
        Assert.Equal(1, c.State.Counter("test_counter"));
    }

    [Fact]
    public async Task VersionConflict_And_Rejection_AnswerWithTheServerState()
    {
        var c = await LocalFirstClient.ConnectAsync(_app, "lf-conflict");
        // another device / a server hook moved the player on
        var other = await _app.PlayerAsync("lf-conflict");
        await other.CommandAsync("TestIncrement", new { amount = 5 }, "elsewhere");
        await c.RunAsync(new TestIncrement { Amount = 1 });
        Assert.False(c.LastReply.GetProperty("ok").GetBoolean());
        Assert.Equal("version_conflict", c.LastReply.GetProperty("error").GetString());
        Assert.Equal(5, c.State.Counter("test_counter")); // adopted; the local +1 is gone (the client re-runs its queue on it)
        // a command the server rejects (prediction said ok: forged hash on an invalid command is impossible locally, so send raw)
        var (status, body) = await c.Raw("TestIncrement", new { amount = -1 }, c.State.Version, StateHash.Hex(c.State));
        Assert.Equal(200, status);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Object, body.GetProperty("state").ValueKind);
    }

    [Fact]
    public async Task Retry_OfAnAppliedRequest_IsAnAck()
    {
        var c = await LocalFirstClient.ConnectAsync(_app, "lf-retry");
        var cmd = new TestIncrement { Amount = 1, RequestId = "once" };
        await c.RunAsync(cmd);
        Assert.True(c.Matched);
        // the response was lost: the client sends the same request again
        var (status, body) = await c.Raw("TestIncrement", new { amount = 1 }, c.State.Version - 1, StateHash.Hex(c.State), "once");
        Assert.Equal(200, status);
        Assert.True(body.GetProperty("replayed").GetBoolean());
        Assert.True(body.GetProperty("match").GetBoolean());
        Assert.Equal(1, (await _app.Service<IPlayerStore>().GetAsync(c.State.PlayerId))!.State.Counter("test_counter"));
    }

    /// <summary>Session, state and the version check only read: nothing is written however often a client calls them.</summary>
    [Fact]
    public async Task Session_State_AndVersionCheck_NeverWrite()
    {
        var c = await LocalFirstClient.ConnectAsync(_app, "lf-reads");
        await c.RunAsync(new RunStart { LevelId = Content.MainLevel(1).Id, Mode = RunMode.Normal }); // stamina below the cap: regen runs
        var store = _app.Service<CountingPlayerStore>();
        string stored = StateJson.Serialize((await store.GetAsync(c.State.PlayerId))!);
        int writes = store.Writes;
        for (int i = 0; i < 6; i++)
        {
            _app.Time.Advance(TimeSpan.FromMinutes(7));
            Assert.Equal(200, (await c.Player.PostAsync("/v1/session", new { })).Status);
            var (st, state) = await c.Player.GetAsync("/v1/state");
            Assert.Equal(200, st);
            Assert.Equal(c.State.Version, state.GetProperty("version").GetInt64());
            var (sv, version) = await c.Player.GetAsync("/v1/version");
            Assert.Equal(200, sv);
            Assert.Equal(c.State.Version, version.GetProperty("version").GetInt64());
        }
        Assert.Equal(writes, store.Writes);
        Assert.Equal(stored, StateJson.Serialize((await store.GetAsync(c.State.PlayerId))!));
    }

    /// <summary>
    /// The client adopts a settled (unsaved) state, settles it locally as time passes (its refresh), and its next commands still
    /// match the server, which settles the stored state at each command's time.
    /// </summary>
    [Fact]
    public async Task AfterUnsavedSettles_AndLocalRefreshes_CommandHashesStillMatch()
    {
        var c = await LocalFirstClient.ConnectAsync(_app, "lf-refresh");
        await c.RunAsync(new RunStart { LevelId = Content.MainLevel(1).Id, Mode = RunMode.Normal });
        await c.RunAsync(new RunAbandon());
        Assert.True(c.Matched, string.Join("\n", c.LastDiff));
        var engine = _app.Service<GameEngine>();
        for (int i = 0; i < 3; i++)
        {
            // hours later the app opens: /v1/state settles at server time (not saved), the client adopts it ...
            _app.Time.Advance(TimeSpan.FromMinutes(97));
            var (_, body) = await c.Player.GetAsync("/v1/state");
            c.State = body.GetProperty("state").Deserialize<GameState>(StateJson.Wire)!;
            Assert.Equal(body.GetProperty("stateHash").GetString(), StateHash.Hex(c.State));
            c.NowMs = body.GetProperty("serverTimeMs").GetInt64();
            // ... and refreshes locally every 30 s for a while
            for (int k = 0; k < 5; k++)
            {
                _app.Time.Advance(TimeSpan.FromSeconds(30));
                c.NowMs += 30_000;
                engine.Settle(c.State, c.NowMs);
            }
            await c.RunAsync(new RunStart { LevelId = Content.MainLevel(1).Id, Mode = RunMode.Normal });
            Assert.True(c.Matched, string.Join("\n", c.LastDiff));
            await c.RunAsync(new RunAbandon());
            Assert.True(c.Matched, string.Join("\n", c.LastDiff));
        }
        Assert.Equal(0, c.Adopted);
    }

    /// <summary>A change made on the server (another device, a hook: mail, arena, live ops) bumps the version the check reads.</summary>
    [Fact]
    public async Task ExternalChange_IsSeenByTheVersionCheck_AndPickedUpWithTheState()
    {
        var c = await LocalFirstClient.ConnectAsync(_app, "lf-external");
        await c.RunAsync(new TestIncrement { Amount = 1 });
        var (_, v0) = await c.Player.GetAsync("/v1/version");
        Assert.Equal(c.State.Version, v0.GetProperty("version").GetInt64()); // nothing new
        var hooks = _app.Service<ServerHooks>();
        Assert.True(await hooks.SendMailAsync(c.State.PlayerId, new MailArgs { Title = "Hello", Body = "From the server" }, "ext-mail"));
        var (_, v1) = await c.Player.GetAsync("/v1/version");
        Assert.Equal(c.State.Version + 1, v1.GetProperty("version").GetInt64());
        var (_, body) = await c.Player.GetAsync("/v1/state");
        c.State = body.GetProperty("state").Deserialize<GameState>(StateJson.Wire)!;
        Assert.Contains(c.State.Mail.Inbox, m => m.Title == "Hello");
        await c.RunAsync(new TestIncrement { Amount = 1 });
        Assert.True(c.Matched, string.Join("\n", c.LastDiff));
    }
}

/// <summary>Battle sampling rates and the cheater flag rule (service level).</summary>
public class BattleAuditTests
{
    private static BattleAuditService Service(BattleAuditOptions? o = null, IBattleSampler? sampler = null, IDocStore? docs = null) =>
        new(docs ?? new InMemoryDocStore(), sampler ?? new RandomBattleSampler(),
            Microsoft.Extensions.Options.Options.Create(new ServerOptions { Battles = o ?? new BattleAuditOptions() }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BattleAuditService>.Instance);

    [Fact]
    public void SamplingRates_Standard10_Elite50_Boss100()
    {
        var svc = Service();
        Assert.Equal(0.10, svc.Rate(BattleTier.Standard));
        Assert.Equal(0.50, svc.Rate(BattleTier.Elite));
        Assert.Equal(1.0, svc.Rate(BattleTier.Boss));
        var sampler = new RandomBattleSampler();
        const int n = 20_000;
        double Share(BattleTier t) => Enumerable.Range(0, n).Count(_ => sampler.Sample("p", t, svc.Rate(t))) / (double)n;
        Assert.InRange(Share(BattleTier.Standard), 0.085, 0.115);
        Assert.InRange(Share(BattleTier.Elite), 0.47, 0.53);
        Assert.Equal(1.0, Share(BattleTier.Boss));
        Assert.Equal(0, Enumerable.Range(0, 1000).Count(_ => sampler.Sample("p", BattleTier.Standard, 0)));
    }

    [Fact]
    public void Resolver_SamplesByTier_UsingTheConfiguredRates()
    {
        var seen = new List<(BattleTier, double)>();
        var sampler = new RecordingSampler(seen);
        var svc = Service(new BattleAuditOptions { StandardSampleRate = 0.2, EliteSampleRate = 0.6, BossSampleRate = 0.9 }, sampler);
        var engine = new GameEngine();
        var ctx = new CommandContext { Content = engine.Content, Engine = engine };
        var scenario = BattleDeterminism.Scenarios(engine.Content, 1)[0];
        var claim = BattleSimulator.Simulate(scenario.Key, engine.Content, Rng.FromSeed(scenario.Value)).Summary();
        var resolver = svc.Resolver("p", new List<BattleResult> { claim, claim, claim }, new List<BattleCheck>());
        resolver(ctx, scenario.Key, Rng.FromSeed(scenario.Value), "enemy", 0);
        resolver(ctx, scenario.Key, Rng.FromSeed(scenario.Value), "miniboss", 1);
        resolver(ctx, scenario.Key, Rng.FromSeed(scenario.Value), "boss", 2);
        Assert.Equal(new[] { (BattleTier.Standard, 0.2), (BattleTier.Elite, 0.6), (BattleTier.Boss, 0.9) }, seen);
    }

    private sealed class RecordingSampler(List<(BattleTier, double)> seen) : IBattleSampler
    {
        public bool Sample(string playerId, BattleTier tier, double rate) { seen.Add((tier, rate)); return false; }
    }

    private static BattleCheck Check(bool match) => new()
    {
        Kind = "enemy", Match = match, Client = new BattleResult { Rounds = 3 }, Server = new BattleResult { Rounds = 4 },
    };

    [Fact]
    public async Task MismatchFlagging_ThreeInTheLastTwenty()
    {
        var svc = Service();
        await svc.RecordAsync("p", new[] { Check(false), Check(true), Check(false) }, 1);
        var a = (await svc.GetAsync("p"))!.Value;
        Assert.Equal(3, a.Validated);
        Assert.Equal(2, a.Mismatches);
        Assert.False(a.Flagged);
        Assert.Equal(2, a.Recent.Count);
        await svc.RecordAsync("p", new[] { Check(false) }, 2);
        a = (await svc.GetAsync("p"))!.Value;
        Assert.True(a.Flagged);
        Assert.Equal(2, a.FlaggedAtMs);
        Assert.Equal(3, a.WindowMismatches);
    }

    [Fact]
    public async Task MismatchFlagging_RareMismatchesNeverFlag_AndDetailsAreCapped()
    {
        var svc = Service();
        for (int i = 0; i < 10; i++)
        {
            var batch = Enumerable.Range(0, 10).Select(k => Check(k != 0)).ToList(); // 1 in 10: never 3 within 20
            await svc.RecordAsync("p", batch, i);
        }
        var a = (await svc.GetAsync("p"))!.Value;
        Assert.Equal(100, a.Validated);
        Assert.Equal(10, a.Mismatches);
        Assert.False(a.Flagged);
        Assert.Equal(20, a.Window.Count);
        Assert.Equal(5, a.Recent.Count);
    }

    [Fact]
    public async Task MismatchFlagging_ThresholdsComeFromConfig()
    {
        var svc = Service(new BattleAuditOptions { FlagMismatches = 1, FlagWindow = 5 });
        await svc.RecordAsync("p", new[] { Check(false) }, 1);
        Assert.True((await svc.GetAsync("p"))!.Value.Flagged);
    }
}
