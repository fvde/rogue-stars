using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using RogueStars.Server.Infrastructure;
using RogueStars.Sim;
using Xunit.Abstractions;

namespace RogueStars.Server.Tests;

/// <summary>Owner dev tools: allowlist, reset progress, jump to account day N (docs/server.md → Dev tools).</summary>
public class DevToolsTests : IDisposable
{
    private readonly TestApp _app = new() { DevUnlockAll = false };
    private readonly LogCapture _logs = new();
    private readonly ITestOutputHelper _out;

    public DevToolsTests(ITestOutputHelper output)
    {
        _out = output;
        // Development server, but with the production-like allowlist instead of "everyone".
        _app.Settings["RogueStars:DevTools:AllowAll"] = "false";
        _app.Settings["RogueStars:DevTools:AllowedPlayerIds:0"] = "dev_owner";
        _app.Settings["RogueStars:DevTools:AllowedPlayerIds:1"] = "dev_second, dev_third";
        _app.ConfigureServices = s => s.AddSingleton<ILoggerProvider>(_logs);
    }

    public void Dispose() => _app.Dispose();

    private ContentDb Content => _app.Service<GameEngine>().Content;
    private long Now => _app.Time.GetUtcNow().ToUnixTimeMilliseconds();

    // ---- access control ------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyAllowlistedPlayers_GetTheDevTools()
    {
        var stranger = await _app.PlayerAsync("stranger");
        var (status, session) = await stranger.PostAsync("/v1/session", new { });
        Assert.Equal(200, status);
        Assert.False(session.GetProperty("devTools").GetBoolean());
        foreach (var (url, body) in new (string, object)[] { ("/v1/dev/reset", new { }), ("/v1/dev/jump", new { day = 5, profile = "active" }) })
        {
            var (s, b) = await stranger.PostAsync(url, body);
            Assert.Equal(403, s);
            Assert.Equal("dev_tools_forbidden", b.GetProperty("error").GetString());
        }
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Dev tools refused") && e.Message.Contains("dev_stranger"));
        Assert.Equal(0, await stranger.CounterAsync(Requirements.MainLevelCounter)); // untouched

        foreach (var device in new[] { "owner", "second", "third" })
        {
            var owner = await _app.PlayerAsync(device);
            Assert.True((await owner.PostAsync("/v1/session", new { })).Body.GetProperty("devTools").GetBoolean());
            Assert.Equal(200, (await owner.PostAsync("/v1/dev/reset")).Status);
        }
    }

    [Fact]
    public async Task DevServers_AllowEveryone_WithTheDevFlag()
    {
        using var app = new TestApp(); // appsettings.Development.json: DevTools:AllowAll = true
        var p = await app.PlayerAsync("anyone");
        Assert.True((await p.PostAsync("/v1/session", new { })).Body.GetProperty("devTools").GetBoolean());
        var reset = await p.PostAsync("/v1/dev/reset");
        Assert.Equal(200, reset.Status);
        // The client deserializes every numeric field as a number (a null seed broke "Reset progress" on device).
        foreach (var field in new[] { "day", "seed", "mainLevel", "wormLevel", "commands", "elapsedMs", "createdMs" })
            Assert.Equal(System.Text.Json.JsonValueKind.Number, reset.Body.GetProperty(field).ValueKind);
    }

    // ---- reset ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reset_ReplacesThePlayerWithABrandNewOne()
    {
        var owner = await _app.PlayerAsync("owner", "Owner Name");
        var created0 = (await owner.StateAsync()).GetProperty("createdMs").GetInt64();
        Assert.True((await owner.CommandAsync("TestIncrement", new { amount = 3 }, "r1")).Body.GetProperty("ok").GetBoolean());
        await owner.CommandAsync("TestIncrement", new { amount = 4 }, "r2");
        long before = (await owner.StateAsync()).GetProperty("version").GetInt64();
        _app.Time.Advance(TimeSpan.FromDays(3));

        var (status, body) = await owner.PostAsync("/v1/dev/reset");
        Assert.Equal(200, status);
        var state = body.GetProperty("state");
        Assert.Equal(before + 1, body.GetProperty("version").GetInt64());
        Assert.Equal(before + 1, state.GetProperty("version").GetInt64());
        Assert.Equal(Now, state.GetProperty("createdMs").GetInt64());
        Assert.NotEqual(created0, state.GetProperty("createdMs").GetInt64());
        Assert.Equal("dev_owner", state.GetProperty("playerId").GetString());
        Assert.Equal("Owner Name", state.GetProperty("displayName").GetString());
        Assert.Equal(1, body.GetProperty("day").GetInt32());
        Assert.False(state.GetProperty("counters").TryGetProperty("test_counter", out _));
        Assert.Equal(0, body.GetProperty("mainLevel").GetInt64());
        // The starter kit of a new player, and the day-1 hub: nothing unlocked yet.
        var fresh = _app.Service<GameEngine>().NewPlayer("x", Now, 1);
        Assert.Equal(fresh.Wallet.Get(Currencies.Stamina), state.GetProperty("wallet").GetProperty("balances").GetProperty(Currencies.Stamina).GetInt64());
        Assert.Empty(state.GetProperty("onboarding").GetProperty("unlocked").EnumerateObject());

        // The request log is cleared: an old request id runs again instead of replaying.
        var again = await owner.CommandAsync("TestIncrement", new { amount = 5 }, "r1");
        Assert.False(again.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(5, await owner.CounterAsync("test_counter"));
        // A second client still on the old version must resync.
        var stale = await owner.CommandAsync("TestIncrement", new { amount = 1 }, "r9", expectedVersion: before);
        Assert.Equal(409, stale.Status);
    }

    [Fact]
    public async Task ResetAndJump_FollowTheServersGuildRecord()
    {
        using var app = new TestApp(); // dev-unlock: guilds are open for a new player
        var p = await app.PlayerAsync("guildy");
        var (s, created) = await p.PostAsync("/v1/guilds", new { name = "Dev Guild" });
        Assert.Equal(200, s);
        string guildId = created.GetProperty("guild").GetProperty("id").GetString()!;

        var reset = (await p.PostAsync("/v1/dev/reset")).Body.GetProperty("state").GetProperty("guild");
        Assert.Equal(guildId, reset.GetProperty("guildId").GetString());
        Assert.Equal("leader", reset.GetProperty("role").GetString());

        // Day 2: Guilds are still locked for a real day-2 player, and never the simulator's own guild.
        var jump = (await p.PostAsync("/v1/dev/jump", new { day = 2, profile = "casual", seed = 1 })).Body.GetProperty("state").GetProperty("guild");
        Assert.Equal(JsonValueKind.Null, jump.GetProperty("guildId").ValueKind);
    }

    // ---- jump ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, "active", "invalid_day")]
    [InlineData(61, "active", "invalid_day")]
    [InlineData(5, "pro", "unknown_profile")]
    public async Task Jump_RejectsBadInput(int day, string profile, string error)
    {
        var owner = await _app.PlayerAsync("owner");
        var (status, body) = await owner.PostAsync("/v1/dev/jump", new { day, profile });
        Assert.Equal(400, status);
        Assert.Equal(error, body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Jump_IsDeterministic()
    {
        // Library level: same content, player, day, profile, seed and clock → the same state, byte for byte.
        var a = AccountAgeJump.Run(Content, "p", 12, TimeProfile.Active, 42, Now);
        var b = AccountAgeJump.Run(Content, "p", 12, TimeProfile.Active, 42, Now);
        Assert.Equal(StateJson.Serialize(a.State), StateJson.Serialize(b.State));
        var c = AccountAgeJump.Run(Content, "p", 12, TimeProfile.Active, 43, Now);
        Assert.NotEqual(StateJson.Serialize(a.State), StateJson.Serialize(c.State));

        // Endpoint: two allowlisted players with the same request at the same time get the same progress.
        var p2 = await _app.PlayerAsync("second", "Same");
        var p3 = await _app.PlayerAsync("third", "Same");
        var s2 = (await p2.PostAsync("/v1/dev/jump", new { day = 12, profile = "active", seed = 42 })).Body;
        var s3 = (await p3.PostAsync("/v1/dev/jump", new { day = 12, profile = "active", seed = 42 })).Body;
        Assert.Equal(42, s2.GetProperty("seed").GetInt64());
        Assert.Equal(a.MainLevel, s2.GetProperty("mainLevel").GetInt64());
        Assert.Equal(s2.GetProperty("state").GetRawText().Replace("dev_second", "ID"), s3.GetProperty("state").GetRawText().Replace("dev_third", "ID"));
    }

    [Theory]
    [InlineData(1, "casual")]
    [InlineData(2, "hardcore")]
    [InlineData(7, "casual")]
    [InlineData(14, "active")]
    [InlineData(30, "hardcore")]
    [InlineData(60, "active")]
    [InlineData(60, "hardcore")]
    public async Task Jump_ProducesAConsistentState(int day, string profile)
    {
        var owner = await _app.PlayerAsync("owner", "Owner");
        long version0 = (await owner.StateAsync()).GetProperty("version").GetInt64();
        var sw = Stopwatch.StartNew();
        var (status, body) = await owner.PostAsync("/v1/dev/jump", new { day, profile, seed = 3 });
        sw.Stop();
        Assert.Equal(200, status);
        _out.WriteLine($"day {day} {profile}: {sw.ElapsedMilliseconds} ms request, {body.GetProperty("elapsedMs").GetInt64()} ms simulation, "
                       + $"{body.GetProperty("commands").GetInt64()} commands, main level {body.GetProperty("mainLevel").GetInt64()}, "
                       + $"worm level {body.GetProperty("wormLevel").GetInt64()}, power {body.GetProperty("power").GetInt64()}");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), "jump took " + sw.Elapsed);

        var wire = body.GetProperty("state");
        Assert.Equal(version0 + 1, wire.GetProperty("version").GetInt64());
        Assert.Equal("dev_owner", wire.GetProperty("playerId").GetString());
        Assert.Equal("Owner", wire.GetProperty("displayName").GetString());
        Assert.Equal(day, body.GetProperty("day").GetInt32());
        Assert.Equal(profile, body.GetProperty("profile").GetString());

        // The stored state equals the response and is what the rules see.
        var record = await _app.Service<Storage.IPlayerStore>().GetAsync("dev_owner");
        var s = record!.State;
        Assert.Empty(record.Requests);
        Assert.Equal(StateJson.ToWire(s).GetRawText(), wire.GetRawText());
        var c = Content;
        long now = Now;

        // Account age: created at the daily reset day − 1 days ago, the hub shows "day N".
        Assert.Equal(GameClock.DayStartMs(c, GameClock.DayIndex(c, now) - (day - 1)), s.CreatedMs);
        Assert.Equal(day - 1, Requirements.AccountAgeDays(s, c, now));
        Assert.InRange(s.LastCommandMs, s.CreatedMs, now);

        // Unlocks happened on days ≤ N, in the past, with the level gates met.
        foreach (var (id, at) in s.Onboarding.Unlocked)
        {
            Assert.InRange(at, s.CreatedMs, now);
            Assert.True(Requirements.AccountAgeDays(s, c, at) + 1 <= day, id + " unlocked on day " + (Requirements.AccountAgeDays(s, c, at) + 1));
            Assert.True(Features.UnlockDay(c.Feature(id)) <= day, id + " is scheduled after day " + day);
        }
        if (day >= 2) Assert.NotEmpty(s.Onboarding.Unlocked);
        Assert.True(s.Counter(Requirements.MainLevelCounter) >= 1, "no level cleared");
        Assert.Null(s.Run.Active);
        Assert.Null(s.Guild.GuildId);

        // Timers: started in the past, finishing within days (nothing years ago or far in the future).
        foreach (var t in s.Timers.Active)
        {
            Assert.InRange(t.StartMs, s.CreatedMs, now);
            Assert.InRange(t.EndsMs, s.CreatedMs, now + 30 * GameClock.Day);
        }
        // Every timestamp in the state lies between account creation (live-ops occurrences: their week) and now
        // (end/expiry/ready times: within 30 days).
        CheckTimestamps(JsonSerializer.SerializeToElement(s, StateJson.Options), "", s.CreatedMs - GameClock.Week, now);

        // Stamina and regen: settled up to now, never above what a player can hold.
        foreach (var rule in c.RegenRules)
        {
            Assert.True(s.Wallet.Regen.TryGetValue(rule.Currency, out var regen), "no regen state for " + rule.Currency);
            Assert.InRange(regen.LastTickMs, s.CreatedMs, now);
        }
        Assert.InRange(s.Wallet.Get(Currencies.Stamina), 0, 10 * s.Wallet.Cap(c.Regen(Currencies.Stamina)));
        foreach (var (cur, amount) in s.Wallet.Balances) Assert.True(amount >= 0, cur + " is negative");

        // Laboratory research, breeding: running jobs finish within days.
        foreach (var (id, lab) in s.Lab.Entries)
            if (lab.ResearchingLevel > 0) Assert.InRange(LaboratorySystem.ReadyAtMs(s, id), s.CreatedMs, now + 30 * GameClock.Day);

        // Live-ops still running now were left to the server's own instances (no simulated attempts).
        foreach (var ev in LiveOpsSystem.ActiveEvents(s, c, now).Where(e => e.Kind == LiveOpsKind.Invasion))
            Assert.False(JsonSerializer.Serialize(s.LiveOps, StateJson.Options).Contains(ev.OccurrenceId + "\""), "simulated attempts on the live invasion " + ev.OccurrenceId);
    }

    /// <summary>Recursively: properties ending in "Ms" holding a timestamp lie within [from, now] (ends: now + 30 days).</summary>
    private static void CheckTimestamps(JsonElement e, string path, long from, long now)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    string at = path + "." + p.Name;
                    if (p.Name.EndsWith("Ms", StringComparison.Ordinal) && p.Value.ValueKind == JsonValueKind.Number
                        && p.Value.TryGetInt64(out var v) && v > 1_000_000_000_000 && v < long.MaxValue / 2)
                    {
                        bool future = p.Name is "EndMs" or "EndsMs" or "ExpiresMs" or "ReadyAtMs";
                        Assert.True(v >= from && v <= (future ? now + 30 * GameClock.Day : now), $"{at} = {DateTimeOffset.FromUnixTimeMilliseconds(v):u}");
                    }
                    CheckTimestamps(p.Value, at, from, now);
                }
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var x in e.EnumerateArray()) CheckTimestamps(x, path + "[" + i++ + "]", from, now);
                break;
        }
    }

    [Fact]
    public async Task AfterAJump_NormalPlayContinues()
    {
        var owner = await _app.PlayerAsync("owner");
        var jump = (await owner.PostAsync("/v1/dev/jump", new { day = 14, profile = "active", seed = 9 })).Body;
        long level = jump.GetProperty("mainLevel").GetInt64();
        Assert.True(level >= 10, "an active player clears more than 10 levels in two weeks (" + level + ")");

        // Session and state load the new player.
        var session = (await owner.PostAsync("/v1/session", new { })).Body;
        Assert.False(session.GetProperty("created").GetBoolean());
        Assert.Equal(level, session.GetProperty("state").GetProperty("counters").GetProperty(Requirements.MainLevelCounter).GetInt64());

        // The next morning: login, daily quests, the next main level.
        _app.Time.Advance(TimeSpan.FromDays(1));
        Assert.True((await owner.CommandAsync("LoginClaim", null, "login")).Body.GetProperty("ok").GetBoolean());
        var claim = await owner.CommandAsync("QuestClaimAll", null, "quests");
        Assert.True(claim.Body.GetProperty("ok").GetBoolean(), claim.Body.GetProperty("error").ToString());
        string next = Content.MainLevel((int)level + 1).Id;
        var start = await owner.CommandAsync("RunStart", new { levelId = next, mode = (int)RunMode.Normal }, "run");
        Assert.True(start.Body.GetProperty("ok").GetBoolean(), start.Body.GetProperty("error").ToString());
        for (int i = 0; i < 400; i++)
        {
            var st = await owner.StateAsync();
            if (st.GetProperty("run").GetProperty("active").ValueKind == JsonValueKind.Null) break;
            var step = await owner.CommandAsync("RunAutoStep", new { farming = true, farmShops = true }, "step" + i);
            if (!step.Body.GetProperty("ok").GetBoolean()) await owner.CommandAsync("RunAbandon", null, "abandon" + i);
        }
        var run = (await owner.StateAsync()).GetProperty("run");
        Assert.Equal(JsonValueKind.Null, run.GetProperty("active").ValueKind);
        Assert.Equal(next, run.GetProperty("lastSummary").GetProperty("levelId").GetString()); // played to the end (won or lost)
        Assert.Equal(jump.GetProperty("state").GetProperty("counters").GetProperty("runs_started").GetInt64() + 1, await owner.CounterAsync("runs_started"));
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public readonly ConcurrentQueue<(LogLevel Level, string Message)> Entries = new();
        public ILogger CreateLogger(string categoryName) => new Logger(this);
        public void Dispose() { }

        private sealed class Logger(LogCapture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
