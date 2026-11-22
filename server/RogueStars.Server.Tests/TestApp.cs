using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
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
    public BattleOutcome ResolveInvasionAttack(CombatLoadout attacker, string invasionId, long invaderPower, long seed) =>
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
    /// <summary>Register the fake hook commands (PurchaseComplete, MailDeliver, LiveOpsApplyOutcome...).</summary>
    public bool WithHooks { get; set; } = true;
    public Action<GameEngine>? ConfigureEngine { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        foreach (var kv in Settings) builder.UseSetting(kv.Key, kv.Value);
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<TimeProvider>(Time);
            s.AddSingleton<IBattleResolver>(Battles);
            s.AddSingleton(_ =>
            {
                var engine = new GameEngine(ContentDb.Default) { Cloner = StateJson.Clone };
                TestCommands.Register(engine, WithHooks);
                ConfigureEngine?.Invoke(engine);
                return engine;
            });
        });
    }

    public T Service<T>() where T : notnull => Services.GetRequiredService<T>();

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

    public async Task<long> CounterAsync(string key)
    {
        var state = await StateAsync();
        return state.GetProperty("counters").TryGetProperty(key, out var v) ? v.GetInt64() : 0;
    }

    internal static async Task<JsonElement> Read(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
}
