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
    public JsonElement? State { get; set; }
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
    ILogger<CommandService> log)
{
    private const int MaxAttempts = 8;

    public async Task<CommandResponse> ExecuteAsync(string playerId, GameCommand command, string? requestId,
        long? expectedVersion = null, CancellationToken ct = default)
    {
        command.RequestId = requestId;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var record = await store.GetAsync(playerId, ct) ?? throw ApiException.NotFound("no_session");

            if (requestId != null && record.Requests.FirstOrDefault(r => r.RequestId == requestId) is { } done)
                return Replay(done, record.State);

            if (expectedVersion.HasValue && expectedVersion.Value != record.State.Version)
                throw ApiException.Conflict("version_conflict", new { version = record.State.Version });

            long before = record.State.Version;
            var state = record.State;
            long now = clock.NowMs;
            var result = engine.Execute(ref state, command, now);
            if (!result.Ok)
            {
                return new CommandResponse { Ok = false, Error = result.Error, Version = before, State = StateJson.ToWire(record.State) };
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
                response.State = StateJson.ToWire(state);
                return response;
            }

            if (expectedVersion.HasValue) throw ApiException.Conflict("version_conflict");
            log.LogInformation("Version conflict for {PlayerId} on {Command}, retrying", playerId, command.Type);
        }
        throw ApiException.Conflict("contention");
    }

    private static CommandResponse Replay(RequestRecord r, GameState state) => new()
    {
        Ok = r.Ok,
        Error = r.Error,
        Events = r.Events,
        Payload = r.Payload,
        Version = state.Version,
        Replayed = true,
        State = StateJson.ToWire(state),
    };
}
