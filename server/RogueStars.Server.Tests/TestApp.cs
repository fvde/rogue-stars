using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using RogueStars.Server.Arena;
using RogueStars.Server.Infrastructure;

namespace RogueStars.Server.Tests;

public sealed class ManualTime : TimeProvider
{
    // Wednesday of ISO week 2026-W47, 12:00 UTC.
    public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Attacker always wins; damage = 1000 (or the configured amount).</summary>
public sealed class TestBattleResolver : IBattleResolver
{
    public long Damage { get; set; } = 1000;
    public BattleOutcome ResolveDuel(CombatLoadout attacker, CombatLoadout defender, long seed) =>
        new() { AttackerWon = true, Damage = Damage };
    public BattleOutcome ResolveInvasionAttack(CombatLoadout attacker, string invasionTypeId, long invaderPower, long remainingHp, long seed) =>
        new() { AttackerWon = true, Damage = Damage };
}

/// <summary>In-process server: Development environment (dev auth, in-memory stores, fake IAP/ads, secret job auth).</summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    public const string JobSecret = "local-job-secret";

    public ManualTime Time { get; } = new();
    public TestBattleResolver Battles { get; } = new();
    public Dictionary<string, string?> Settings { get; } = new();
    public string Environment { get; set; } = "Development";
    /// <summary>
    /// New players get the rules dev-unlock flag (every feature gate open) because the server tests are not about
    /// gating. Hook commands are the real rules commands; point a hook name at a missing command to test "absent".
    /// </summary>
    public bool DevUnlockAll { get; set; } = true;
    /// <summary>Use the production resolver (rules BattleSimulator) instead of the scripted <see cref="Battles"/>.</summary>
    public bool RealBattles { get; set; }
    public Action<GameEngine>? ConfigureEngine { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        foreach (var kv in Settings) builder.UseSetting(kv.Key, kv.Value);
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<TimeProvider>(Time);
            if (!RealBattles) s.AddSingleton<IBattleResolver>(Battles);
            s.AddSingleton(_ =>
            {
                var engine = new GameEngine(ContentDb.Default) { Cloner = StateJson.Clone };
                TestCommands.Register(engine);
                if (DevUnlockAll) engine.RegisterNewPlayer(ctx => ctx.State.Flags.Add(Features.DevUnlockAllFlag));
                ConfigureEngine?.Invoke(engine);
                return engine;
            });
        });
    }

    public T Service<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Point a server hook (RogueStars:Commands:&lt;key&gt;) at a command that does not exist.</summary>
    public void DisableHook(string key) => Settings["RogueStars:Commands:" + key] = "NotRegistered" + key;

    /// <summary>Dev-auth client for a device, with a session already created.</summary>
    public async Task<PlayerClient> PlayerAsync(string deviceId, string? displayName = null)
    {
        var http = CreateClient();
        var auth = await (await http.PostAsJsonAsync("/v1/auth/dev", new { deviceId })).Content.ReadFromJsonAsync<JsonElement>();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("token").GetString());
        var session = await http.PostAsJsonAsync("/v1/session", new { displayName = displayName ?? deviceId });
        session.EnsureSuccessStatusCode();
        return new PlayerClient(http, auth.GetProperty("playerId").GetString()!);
    }

    public HttpClient JobClient(string? secret = JobSecret)
    {
        var http = CreateClient();
        if (secret != null) http.DefaultRequestHeaders.Add("X-Job-Secret", secret);
        return http;
    }
}

public sealed record PlayerClient(HttpClient Http, string PlayerId)
{
    public async Task<(int Status, JsonElement Body)> PostAsync(string url, object? body = null)
    {
        var resp = await Http.PostAsJsonAsync(url, body ?? new { });
        return ((int)resp.StatusCode, await Read(resp));
    }

    public async Task<(int Status, JsonElement Body)> GetAsync(string url)
    {
        var resp = await Http.GetAsync(url);
        return ((int)resp.StatusCode, await Read(resp));
    }

    public Task<(int Status, JsonElement Body)> CommandAsync(string type, object? args = null, string? requestId = null, long? expectedVersion = null) =>
        PostAsync("/v1/command", new { type, requestId, args, expectedVersion });

    public async Task<JsonElement> StateAsync() => (await GetAsync("/v1/state")).Body.GetProperty("state");

    public async Task<long> BalanceAsync(string currency)
    {
        var state = await StateAsync();
        return state.GetProperty("wallet").GetProperty("balances").TryGetProperty(currency, out var v) ? v.GetInt64() : 0;
    }

    /// <summary>Inbox mails with this title.</summary>
    public async Task<List<JsonElement>> MailsAsync(string title) =>
        (await StateAsync()).GetProperty("mail").GetProperty("inbox").EnumerateArray()
            .Where(m => m.GetProperty("title").GetString() == title).ToList();

    public async Task<List<JsonElement>> ModifiersAsync() =>
        (await StateAsync()).GetProperty("modifiers").EnumerateArray().ToList();

    public async Task<long> CounterAsync(string key)
    {
        var state = await StateAsync();
        return state.GetProperty("counters").TryGetProperty(key, out var v) ? v.GetInt64() : 0;
    }

    internal static async Task<(int Status, JsonElement Body)> ReadWithStatus(HttpResponseMessage resp) => ((int)resp.StatusCode, await Read(resp));

    internal static async Task<JsonElement> Read(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
}
