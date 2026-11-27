using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RogueStars.Rules.Core;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Commands;

/// <summary>Wire response of a command (also replayed for duplicate request ids).</summary>
public sealed class CommandResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public List<GameEvent> Events { get; set; } = new();
    public JsonElement? Payload { get; set; }
    public long Version { get; set; }
    /// <summary>True when this response is a stored result for an already-processed request id.</summary>
    public bool Replayed { get; set; }
    /// <summary>Full state: always for old clients; for local-first clients only when their prediction disagreed.</summary>
    public JsonElement? State { get; set; }
    /// <summary>Local-first: the server state's <see cref="StateHash"/> (hex) after the command.</summary>
    public string? StateHash { get; set; }
    /// <summary>Local-first: true = the client's predicted state hash matched (ack only, no state); false = adopt <see cref="State"/>.</summary>
    public bool? Match { get; set; }
    /// <summary>Dev servers only (Battles:ReportToClient): the re-simulated battles' comparison.</summary>
    public List<BattleCheckInfo>? BattleChecks { get; set; }
}

/// <summary>
/// Local-first fields of a command request (docs/server.md → Local-first commands): the client already executed the command
/// at <see cref="AtMs"/> on its copy, which ended with <see cref="StateHash"/>, and claims these battle results.
/// </summary>
public sealed class LocalFirstRequest
{
    public long? AtMs { get; set; }
    public string StateHash { get; set; } = "";
    public List<Rules.Systems.BattleResult>? Battles { get; set; }
}

/// <summary>A re-simulated battle as the dev client shows it.</summary>
public sealed class BattleCheckInfo
{
    public int Index { get; set; }
    public string Kind { get; set; } = "";
    public string Tier { get; set; } = "";
    public bool Match { get; set; }
    public string Client { get; set; } = "";
    public string Server { get; set; } = "";
}

/// <summary>Resolves command names to rules command types and decides what clients may send.</summary>
public sealed class CommandCatalog(GameEngine engine, IOptions<ServerOptions> options)
{
    public static readonly JsonSerializerOptions ArgsJson = new() { IncludeFields = true, PropertyNameCaseInsensitive = true };

    public Type? Resolve(string name) => string.IsNullOrEmpty(name) ? null : engine.CommandType(name);

    public bool IsRegistered(string name) => Resolve(name) != null;

    /// <summary>Server-only: [ServerOnly] attribute, a configured hook name, or the configured deny list.</summary>
    public bool IsServerOnly(Type type)
    {
        var o = options.Value.Commands;
        return type.GetCustomAttribute<ServerOnlyAttribute>() != null
               || o.HookNames().Contains(type.Name, StringComparer.Ordinal)
               || o.ClientDenyList.Contains(type.Name, StringComparer.Ordinal);
    }

    public bool ClientMaySend(Type type)
    {
        var allow = options.Value.Commands.ClientAllowList;
        return !IsServerOnly(type) && (allow.Count == 0 || allow.Contains(type.Name, StringComparer.Ordinal));
    }

    public IEnumerable<string> ClientCommands() =>
        engine.CommandNames.Select(Resolve).Where(t => t != null && ClientMaySend(t)).Select(t => t!.Name).OrderBy(n => n, StringComparer.Ordinal);

    /// <summary>Builds a command from a client request; rejects unknown and server-only commands.</summary>
    public GameCommand FromClient(string? name, JsonElement? args)
    {
        var type = Resolve(name ?? "") ?? throw ApiException.BadRequest("unknown_command");
        if (!ClientMaySend(type)) throw ApiException.Forbidden("server_only_command");
        return Create(type, args);
    }

    /// <summary>Builds a server-issued command from an anonymous/dictionary args object. Null when not registered.</summary>
    public GameCommand? FromServer(string name, object args)
    {
        var type = Resolve(name);
        return type == null ? null : Create(type, JsonSerializer.SerializeToElement(args, args.GetType(), ArgsJson));
    }

    private static GameCommand Create(Type type, JsonElement? args)
    {
        try
        {
            object? cmd = args is { ValueKind: JsonValueKind.Object } a
                ? a.Deserialize(type, ArgsJson)
                : Activator.CreateInstance(type);
            return cmd as GameCommand ?? throw ApiException.BadRequest("invalid_args");
        }
        catch (JsonException)
        {
            throw ApiException.BadRequest("invalid_args");
        }
    }
}

/// <summary>
/// Authoritative execution: load → (idempotency check) → engine.Execute → compare-and-set save.
/// Version conflicts with concurrent writers are retried on fresh state unless the caller pinned a version.
/// </summary>
public sealed class CommandService(GameEngine engine, IPlayerStore store, Clock clock, IOptions<ServerOptions> options,
    BattleAuditService battles, ILogger<CommandService> log)
{
    private const int MaxAttempts = 8;

    /// <summary>
    /// Executes a command. <paramref name="localFirst"/> (new clients): the client already applied it locally; the command runs
    /// at the client's time (clamped), takes the client's battle claims (sampled re-simulation, BattleAuditService) and the
    /// response is a small ack when the resulting state hash matches the client's, else the full state to adopt. A version
    /// conflict is then answered with the current state (200, ok:false) instead of 409.
    /// </summary>
    public async Task<CommandResponse> ExecuteAsync(string playerId, GameCommand command, string? requestId,
        long? expectedVersion = null, CancellationToken ct = default, LocalFirstRequest? localFirst = null)
    {
        command.RequestId = requestId;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var record = await store.GetAsync(playerId, ct) ?? throw ApiException.NotFound("no_session");

            if (requestId != null && record.Requests.FirstOrDefault(r => r.RequestId == requestId) is { } done)
                return Reply(Replay(done, record.State), record.State, localFirst);

            if (expectedVersion.HasValue && expectedVersion.Value != record.State.Version)
            {
                if (localFirst != null)
                    return Reply(new CommandResponse { Ok = false, Error = "version_conflict", Version = record.State.Version }, record.State, localFirst);
                throw ApiException.Conflict("version_conflict", new { version = record.State.Version });
            }

            long before = record.State.Version;
            var state = record.State;
            long now = localFirst != null ? CommandTime(localFirst.AtMs, record.State) : clock.NowMs;
            var checks = new List<BattleCheck>();
            var result = engine.Execute(ref state, command, now, localFirst != null ? battles.Resolver(playerId, localFirst.Battles, checks) : null);
            if (!result.Ok)
            {
                return Reply(new CommandResponse { Ok = false, Error = result.Error, Version = before }, record.State, localFirst);
            }

            var response = new CommandResponse
            {
                Ok = true,
                Events = result.Events,
                Payload = result.Payload == null ? null : StateJson.ToWire(result.Payload),
                Version = state.Version,
            };
            record.State = state;
            if (requestId != null)
            {
                record.Requests.Add(new RequestRecord
                {
                    RequestId = requestId, AtMs = now, Ok = true, Events = response.Events, Payload = response.Payload,
                });
                int window = Math.Max(1, options.Value.Store.IdempotencyWindow);
                if (record.Requests.Count > window) record.Requests.RemoveRange(0, record.Requests.Count - window);
            }

            if (await store.TrySaveAsync(record, before, ct))
            {
                log.LogInformation("Command {Command} by {PlayerId} ok, version {Version}", command.Type, playerId, state.Version);
                if (checks.Count > 0)
                {
                    // Never fail the player's command over the audit write.
                    try { await battles.RecordAsync(playerId, checks, now, ct); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Battle audit for {PlayerId} not recorded", playerId); }
                }
                if (localFirst == null) return Legacy(response, state, result);
                var reply = Reply(response, state, localFirst);
                // Dev servers, and the owner's allowlisted players anywhere (DevTools:AllowedPlayerIds): the developer sees checks.
                if (checks.Count > 0 && (battles.Options.ReportToClient || options.Value.DevTools.IsAllowed(playerId)))
                    reply.BattleChecks = checks.Select(c => new BattleCheckInfo
                    {
                        Index = c.Index, Kind = c.Kind, Tier = c.Tier.ToString(), Match = c.Match,
                        Client = BattleAuditService.Describe(c.Client), Server = BattleAuditService.Describe(c.Server),
                    }).ToList();
                return reply;
            }

            if (expectedVersion.HasValue) throw ApiException.Conflict("version_conflict");
            log.LogInformation("Version conflict for {PlayerId} on {Command}, retrying", playerId, command.Type);
            // Jittered backoff so parallel writers on one player do not keep colliding.
            await Task.Delay(Random.Shared.Next(1, 4 << Math.Min(attempt, 4)), ct);
        }
        throw ApiException.Conflict("contention");
    }

    /// <summary>
    /// The time a local-first command runs at: the client's execution time, never before the state's last command, at most
    /// Commands:ClientTimeMaxLagMs in the past and Commands:ClientTimeMaxLeadMs in the future (no regen from a fast clock).
    /// Outside the window the server clamps it and the state hashes will usually disagree (the client adopts the server's).
    /// </summary>
    public long CommandTime(long? clientAtMs, GameState state)
    {
        long now = clock.NowMs;
        if (clientAtMs is not { } at) return now;
        var o = options.Value.Commands;
        long min = Math.Max(state.LastCommandMs, now - Math.Max(0, o.ClientTimeMaxLagMs));
        long max = now + Math.Max(0, o.ClientTimeMaxLeadMs);
        return Math.Min(Math.Max(at, Math.Min(min, max)), max);
    }

    /// <summary>Local-first reply: ack (hash + version) when the client's prediction matched, else the full state.</summary>
    private static CommandResponse Reply(CommandResponse r, GameState state, LocalFirstRequest? localFirst)
    {
        if (localFirst == null)
        {
            r.State ??= StateJson.ToWire(state);
            return r;
        }
        string hash = Rules.Core.StateHash.Hex(state);
        r.StateHash = hash;
        r.Version = state.Version;
        r.Match = r.Ok && string.Equals(hash, localFirst.StateHash, StringComparison.OrdinalIgnoreCase);
        if (r.Match == true)
        {
            // The client has all of it already (it showed its own events and battle playback).
            r.State = null;
            r.Events = new List<GameEvent>();
            r.Payload = null;
        }
        else r.State = StateJson.ToWire(state);
        return r;
    }

    /// <summary>
    /// Old clients (no local-first fields): full state as before. The run state no longer keeps battle logs; the deployed
    /// client plays the battle from <c>run.active.lastBattle</c> / <c>run.lastSummary.lastBattle</c>, so the command's last
    /// battle (with its log) is put back there in the response only (one-release compatibility).
    /// </summary>
    private static CommandResponse Legacy(CommandResponse r, GameState state, CommandResult result)
    {
        r.State = StateJson.ToWire(state);
        if (result.Battles.Count == 0 || state.Run == null) return r;
        var full = result.Battles[^1].Result;
        var node = System.Text.Json.Nodes.JsonNode.Parse(r.State.Value.GetRawText())!;
        var run = node["run"];
        var battleNode = System.Text.Json.Nodes.JsonNode.Parse(StateJson.ToWire(full).GetRawText());
        if (state.Run.Active != null && run?["active"] is System.Text.Json.Nodes.JsonObject active) active["lastBattle"] = battleNode;
        else if (run?["lastSummary"] is System.Text.Json.Nodes.JsonObject summary) summary["lastBattle"] = battleNode;
        r.State = JsonSerializer.SerializeToElement(node);
        return r;
    }

    private static CommandResponse Replay(RequestRecord r, GameState state) => new()
    {
        Ok = r.Ok,
        Error = r.Error,
        Events = r.Events,
        Payload = r.Payload,
        Version = state.Version,
        Replayed = true,
    };
}
