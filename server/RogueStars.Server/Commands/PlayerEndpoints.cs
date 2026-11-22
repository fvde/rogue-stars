using System.Text.Json;
using RogueStars.Rules.Core;
using RogueStars.Server.Auth;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Commands;

public sealed record SessionRequest(string? DisplayName);
public sealed record CommandRequest(string? Type, string? RequestId, JsonElement? Args, long? ExpectedVersion);
public sealed record DevAuthRequest(string? DeviceId);

/// <summary>Loads players and settles state (shared by session/state and the cross-player features).</summary>
public sealed class PlayerService(GameEngine engine, IPlayerStore store, Clock clock, ILogger<PlayerService> log)
{
    public async Task<(PlayerRecord Record, bool Created)> LoadOrCreateAsync(string playerId, string? displayName, CancellationToken ct)
    {
        var existing = await store.GetAsync(playerId, ct);
        if (existing != null) return (existing, false);

        var state = engine.NewPlayer(playerId, clock.NowMs, Ids.NewSeed());
        if (!string.IsNullOrWhiteSpace(displayName)) state.DisplayName = displayName.Trim()[..Math.Min(displayName.Trim().Length, 24)];
        var record = new PlayerRecord { State = state };
        if (await store.TryCreateAsync(record, ct))
        {
            log.LogInformation("Created player {PlayerId}", playerId);
            return (record, true);
        }
        return (await store.GetAsync(playerId, ct) ?? throw ApiException.Conflict("contention"), false);
    }

    public async Task<GameState> SettledAsync(string playerId, CancellationToken ct)
    {
        var record = await store.GetAsync(playerId, ct) ?? throw ApiException.NotFound("no_session");
        var state = record.State;
        engine.Settle(state, clock.NowMs);
        return state;
    }

    public async Task<CombatLoadout> LoadoutAsync(string playerId, CancellationToken ct)
    {
        var state = await SettledAsync(playerId, ct);
        var loadout = engine.BuildLoadout(state, clock.NowMs);
        loadout.PlayerId = playerId;
        loadout.DisplayName ??= state.DisplayName ?? playerId;
        return loadout;
    }
}

public static class PlayerEndpoints
{
    public static void Map(RouteGroupBuilder v1, WebApplication app, ServerOptions options)
    {
        if (options.Auth.Mode == "dev")
        {
            app.MapPost("/v1/auth/dev", (DevAuthRequest req, DevAuthVerifier dev) =>
            {
                var playerId = DevAuthVerifier.PlayerIdForDevice(req.DeviceId ?? "");
                return Results.Ok(new { token = dev.Issue(playerId), playerId });
            });
        }

        v1.MapPost("/session", async (HttpContext http, SessionRequest? req, PlayerService players, CommandCatalog catalog, Clock clock,
            GameEngine engine, CancellationToken ct) =>
        {
            var (record, created) = await players.LoadOrCreateAsync(http.PlayerId(), req?.DisplayName, ct);
            var state = record.State;
            engine.Settle(state, clock.NowMs);
            return Results.Ok(new
            {
                ok = true,
                playerId = state.PlayerId,
                created,
                serverTimeMs = clock.NowMs,
                version = state.Version,
                state = StateJson.ToWire(state),
                commands = catalog.ClientCommands(),
            });
        });

        v1.MapGet("/state", async (HttpContext http, PlayerService players, Clock clock, CancellationToken ct) =>
        {
            var state = await players.SettledAsync(http.PlayerId(), ct);
            return Results.Ok(new { ok = true, serverTimeMs = clock.NowMs, version = state.Version, state = StateJson.ToWire(state) });
        });

        v1.MapPost("/command", async (HttpContext http, CommandRequest req, CommandCatalog catalog, CommandService commands, CancellationToken ct) =>
        {
            if (req.RequestId is { Length: > 128 }) throw ApiException.BadRequest("request_id_too_long");
            var command = catalog.FromClient(req.Type, req.Args);
            var response = await commands.ExecuteAsync(http.PlayerId(), command, req.RequestId, req.ExpectedVersion, ct);
            return Results.Json(response, StateJson.Wire); // rule rejections are 200 with ok:false
        });
    }
}
