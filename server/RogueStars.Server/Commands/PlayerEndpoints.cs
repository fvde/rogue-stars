using System.Text.Json;
using RogueStars.Rules.Core;
using RogueStars.Server.Auth;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Commands;

public sealed record SessionRequest(string? DisplayName);
/// <param name="StateHash">Local-first clients: the hash of their state after applying the command locally (presence switches
/// the request to the local-first protocol: ack when matching, state only on mismatch).</param>
/// <param name="AtMs">Local-first: when the client executed the command (rules time).</param>
/// <param name="Battles">Local-first: the client's battle results (summaries, in order) for the battles the command fought.</param>
public sealed record CommandRequest(string? Type, string? RequestId, JsonElement? Args, long? ExpectedVersion,
    string? StateHash = null, long? AtMs = null, List<RogueStars.Rules.Systems.BattleResult>? Battles = null);
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

    /// <summary>
    /// Settles the stored state and persists the settle (best effort: a concurrent write wins and is not retried). A
    /// local-first client starts from exactly what is stored, so its next command's state hash can match (settling twice
    /// at different times is not the same as settling once).
    /// </summary>
    public async Task<GameState> SettleAndSaveAsync(PlayerRecord record, CancellationToken ct)
    {
        long before = record.State.Version;
        engine.Settle(record.State, clock.NowMs);
        await store.TrySaveAsync(record, before, ct);
        return record.State;
    }

    public async Task<GameState> SettledSavedAsync(string playerId, CancellationToken ct) =>
        await SettleAndSaveAsync(await store.GetAsync(playerId, ct) ?? throw ApiException.NotFound("no_session"), ct);

    public async Task<CombatLoadout> LoadoutAsync(string playerId, CancellationToken ct)
    {
        var state = await SettledAsync(playerId, ct);
        var loadout = engine.BuildLoadout(state, clock.NowMs);
        loadout.PlayerId = playerId;
        loadout.DisplayName ??= PlayerNames.Display(state.DisplayName);
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
            GameEngine engine, DevTools.DevToolsService devTools, CancellationToken ct) =>
        {
            var (record, created) = await players.LoadOrCreateAsync(http.PlayerId(), req?.DisplayName, ct);
            var state = await players.SettleAndSaveAsync(record, ct);
            return Results.Ok(new
            {
                ok = true,
                playerId = state.PlayerId,
                created,
                serverTimeMs = clock.NowMs,
                version = state.Version,
                stateHash = StateHash.Hex(state),
                state = StateJson.ToWire(state),
                commands = catalog.ClientCommands(),
                // Global ads-disabled mode (Ads:Mode=disabled): the client mirrors it into its rules content so it takes
                // the instant (No Ads) path for every placement.
                adsDisabled = engine.Content.Shop.AdsDisabled,
                // Iap:Mode=disabled: real-money products are shown as "coming soon" (Diamond-priced items keep working).
                purchasesDisabled = options.Iap.IsDisabled,
                // Owner dev tools (/v1/dev/*) for this player: the client shows Settings → Developer only then.
                devTools = devTools.IsAllowed(state.PlayerId),
            });
        });

        v1.MapGet("/state", async (HttpContext http, PlayerService players, Clock clock, CancellationToken ct) =>
        {
            var state = await players.SettledSavedAsync(http.PlayerId(), ct);
            return Results.Ok(new { ok = true, serverTimeMs = clock.NowMs, version = state.Version, stateHash = StateHash.Hex(state), state = StateJson.ToWire(state) });
        });

        v1.MapPost("/command", async (HttpContext http, CommandRequest req, CommandCatalog catalog, CommandService commands,
            Payments.AdCreditService adCredits, CancellationToken ct) =>
        {
            if (req.RequestId is { Length: > 128 }) throw ApiException.BadRequest("request_id_too_long");
            var command = catalog.FromClient(req.Type, req.Args);
            // Ad-funded commands spend a verified AppLovin S2S event (see Payments/AdCredits.cs).
            string? adToken = adCredits.Enforced ? Payments.AdCreditService.TokenOf(command) : null;
            if (adToken != null)
            {
                if (string.IsNullOrEmpty(req.RequestId)) throw ApiException.BadRequest("request_id_required");
                if (!await adCredits.TrySpendAsync(http.PlayerId(), adToken, req.RequestId, ct)) throw ApiException.Forbidden("ad_not_verified");
            }
            var localFirst = string.IsNullOrEmpty(req.StateHash) ? null
                : new LocalFirstRequest { StateHash = req.StateHash, AtMs = req.AtMs, Battles = req.Battles };
            var response = await commands.ExecuteAsync(http.PlayerId(), command, req.RequestId, req.ExpectedVersion, ct, localFirst);
            if (adToken != null && !response.Ok) await adCredits.RefundAsync(http.PlayerId(), adToken, req.RequestId!, ct);
            return Results.Json(response, StateJson.Wire); // rule rejections are 200 with ok:false
        });
    }
}
