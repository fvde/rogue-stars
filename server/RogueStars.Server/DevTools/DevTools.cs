using Microsoft.Extensions.Options;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using RogueStars.Server.Commands;
using RogueStars.Server.Guilds;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;
using RogueStars.Sim;

namespace RogueStars.Server.DevTools;

public sealed record JumpRequest(int? Day, string? Profile, long? Seed);

/// <summary>
/// Owner dev tools (docs/server.md → Dev tools): replace the caller's player record with a brand-new player (reset) or
/// with a player that played N days in a simulator profile (<see cref="AccountAgeJump"/>). Only players on
/// <see cref="DevToolsOptions.AllowedPlayerIds"/> (or everyone with <see cref="DevToolsOptions.AllowAll"/>, refused in
/// Production) may call them. The new state keeps the player id and display name, continues the version (so other
/// clients of the player see a newer version and resync) and clears the request log. Cross-player records (guild,
/// arena group, invasion groups, mail outbox) stay; the rules' guild membership is re-applied from the server's record.
/// </summary>
public sealed class DevToolsService(GameEngine engine, IPlayerStore store, GuildService guilds, Clock clock,
    IOptions<ServerOptions> options, ILogger<DevToolsService> log)
{
    private const int MaxAttempts = 8;
    // A 60-day jump is a few seconds of one CPU: one at a time per instance.
    private static readonly SemaphoreSlim Busy = new(1, 1);

    public bool IsAllowed(string playerId) => options.Value.DevTools.IsAllowed(playerId);

    /// <summary>403 (and a warning in the log) unless the player may use the dev tools.</summary>
    public void Require(string playerId, string action)
    {
        if (IsAllowed(playerId)) return;
        log.LogWarning("Dev tools refused: {Action} by {PlayerId} (not in RogueStars:DevTools:AllowedPlayerIds)", action, playerId);
        throw ApiException.Forbidden("dev_tools_forbidden");
    }

    public async Task<object> ResetAsync(string playerId, CancellationToken ct)
    {
        Require(playerId, "reset");
        long now = clock.NowMs;
        var state = engine.NewPlayer(playerId, now, Ids.NewSeed());
        var saved = await ReplaceAsync(playerId, state, ct);
        log.LogInformation("Dev tools: {PlayerId} reset to a new player (version {Version})", playerId, saved.Version);
        return Response(saved, 1, null, null, 0, 0);
    }

    public async Task<object> JumpAsync(string playerId, JumpRequest req, CancellationToken ct)
    {
        Require(playerId, "jump");
        int day = req.Day ?? 0;
        if (day < AccountAgeJump.MinDays || day > AccountAgeJump.MaxDays) throw ApiException.BadRequest("invalid_day");
        var profile = AccountAgeJump.Profile(req.Profile ?? "active") ?? throw ApiException.BadRequest("unknown_profile");
        long seed = req.Seed ?? Math.Abs(Ids.NewSeed() % 1_000_000_000);

        if (!await Busy.WaitAsync(TimeSpan.FromSeconds(30), ct)) throw new ApiException(StatusCodes.Status429TooManyRequests, "dev_tools_busy");
        AgeJumpResult jump;
        try
        {
            jump = await Task.Run(() => AccountAgeJump.Run(engine.Content, playerId, day, profile, seed, clock.NowMs), ct);
        }
        finally
        {
            Busy.Release();
        }
        var saved = await ReplaceAsync(playerId, jump.State, ct);
        log.LogInformation("Dev tools: {PlayerId} jumped to day {Day} ({Profile}, seed {Seed}): main level {Level}, {Commands} commands in {Ms} ms",
            playerId, day, jump.Profile, seed, jump.MainLevel, jump.Commands, jump.ElapsedMs);
        return Response(saved, day, jump.Profile, seed, jump.Commands, jump.ElapsedMs);
    }

    /// <summary>Replaces the stored record (compare-and-set; retried on concurrent writes).</summary>
    private async Task<GameState> ReplaceAsync(string playerId, GameState fresh, CancellationToken ct)
    {
        long now = clock.NowMs;
        fresh = await SyncGuildAsync(playerId, fresh, now, ct);
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var record = await store.GetAsync(playerId, ct) ?? throw ApiException.NotFound("no_session");
            long before = record.State.Version;
            var state = StateJson.Clone(fresh);
            state.PlayerId = playerId;
            state.DisplayName = record.State.DisplayName;
            state.Version = before + 1;
            engine.Settle(state, now);
            record.State = state;
            record.Requests = new List<RequestRecord>();
            if (await store.TrySaveAsync(record, before, ct)) return state;
            await Task.Delay(Random.Shared.Next(1, 4 << Math.Min(attempt, 4)), ct);
        }
        throw ApiException.Conflict("contention");
    }

    /// <summary>The rules' guild membership follows the server's guild record (rejected while Guilds is still locked).</summary>
    private async Task<GameState> SyncGuildAsync(string playerId, GameState state, long now, CancellationToken ct)
    {
        state.Guild = new GuildState();
        var guildId = await guilds.GuildIdOfAsync(playerId, ct);
        if (guildId == null) return state;
        string role = Rules.Systems.GuildRoles.Member;
        try { role = (await guilds.GetAsync(guildId, ct)).Member(playerId)?.Role ?? role; }
        catch (ApiException) { return state; }
        var cmd = new GuildSetMembership { GuildId = guildId, Role = role };
        var s = state;
        var r = engine.Execute(ref s, cmd, now);
        if (!r.Ok) log.LogInformation("Dev tools: guild membership of {PlayerId} not applied ({Error})", playerId, r.Error);
        return s;
    }

    private object Response(GameState state, int day, string? profile, long? seed, long commands, long elapsedMs)
    {
        long now = clock.NowMs;
        return new
        {
            ok = true,
            day,
            profile,
            seed,
            mainLevel = state.Counter(Requirements.MainLevelCounter),
            wormLevel = state.Counter(Requirements.WormLevelCounter),
            power = engine.BuildLoadout(state, now).Power,
            commands,
            elapsedMs,
            createdMs = state.CreatedMs,
            serverTimeMs = now,
            version = state.Version,
            state = StateJson.ToWire(state),
        };
    }
}

public static class DevToolsEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/dev");
        g.MapPost("/reset", async (HttpContext http, DevToolsService dev, CancellationToken ct) =>
            Results.Json(await dev.ResetAsync(http.PlayerId(), ct), StateJson.Wire));
        g.MapPost("/jump", async (HttpContext http, JumpRequest? req, DevToolsService dev, CancellationToken ct) =>
            Results.Json(await dev.JumpAsync(http.PlayerId(), req ?? new JumpRequest(null, null, null), ct), StateJson.Wire));
    }
}
