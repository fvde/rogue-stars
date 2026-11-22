using Microsoft.Extensions.Options;
using RogueStars.Rules.Core;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Commands;

/// <summary>
/// Server-issued commands resolved by name at runtime (the rules workstreams own the command classes).
/// Each call is idempotent through a deterministic request id.
/// </summary>
public sealed class ServerHooks(CommandCatalog catalog, CommandService commands, IDocStore docs,
    IOptions<ServerOptions> options, ILogger<ServerHooks> log)
{
    public CommandOptions Names => options.Value.Commands;

    public bool Has(string name) => catalog.IsRegistered(name);

    /// <summary>Executes the named command for a player. Returns null when the command is not registered.</summary>
    public async Task<CommandResponse?> TryExecuteAsync(string playerId, string commandName, object args, string requestId,
        CancellationToken ct = default)
    {
        var cmd = catalog.FromServer(commandName, args);
        if (cmd == null)
        {
            log.LogWarning("Server hook command {Command} is not registered; skipped for {PlayerId}", commandName, playerId);
            return null;
        }
        return await commands.ExecuteAsync(playerId, cmd, requestId, null, ct);
    }

    /// <summary>
    /// Mails rewards to a player via the mail command. If the mail command is not registered yet the mail is kept
    /// in a per-player outbox document and delivered by the daily job once it is.
    /// </summary>
    public async Task<bool> SendMailAsync(string playerId, MailArgs mail, string requestId, CancellationToken ct = default)
    {
        if (Has(Names.MailDeliver))
        {
            var r = await TryExecuteAsync(playerId, Names.MailDeliver, mail, requestId, ct);
            if (r is { Ok: true }) return true;
            log.LogWarning("Mail {RequestId} to {PlayerId} failed: {Error}", requestId, playerId, r?.Error);
        }
        await QueueAsync(playerId, mail, requestId, ct);
        return false;
    }

    private async Task QueueAsync(string playerId, MailArgs mail, string requestId, CancellationToken ct)
    {
        await docs.MutateAsync<MailOutbox>(Collections.MailOutbox, playerId, box =>
        {
            box ??= new MailOutbox();
            if (box.Items.Any(m => m.RequestId == requestId)) return null;
            box.Items.Add(new PendingMail { RequestId = requestId, Mail = mail });
            return box;
        }, ct);
    }

    /// <summary>Delivers queued mail for every player with an outbox (daily job). Returns the number delivered.</summary>
    public async Task<int> FlushOutboxesAsync(CancellationToken ct = default)
    {
        if (!Has(Names.MailDeliver)) return 0;
        int delivered = 0;
        foreach (var doc in await docs.ListAsync<MailOutbox>(Collections.MailOutbox, null, 1000, ct))
        {
            var sent = new HashSet<string>();
            foreach (var pending in doc.Value.Items)
            {
                var r = await TryExecuteAsync(doc.Id, Names.MailDeliver, pending.Mail, pending.RequestId, ct);
                if (r is { Ok: true }) sent.Add(pending.RequestId);
            }
            delivered += sent.Count;
            var left = await docs.MutateAsync<MailOutbox>(Collections.MailOutbox, doc.Id, box =>
            {
                if (box == null) return null;
                box.Items.RemoveAll(m => sent.Contains(m.RequestId));
                return box;
            }, ct);
            if (left is { Items.Count: 0 })
            {
                var current = await docs.GetAsync<MailOutbox>(Collections.MailOutbox, doc.Id, ct);
                if (current is { Value.Items.Count: 0 }) await docs.TryDeleteAsync(Collections.MailOutbox, doc.Id, current.Version, ct);
            }
        }
        return delivered;
    }
}

public sealed class MailOutbox
{
    public List<PendingMail> Items { get; set; } = new();
}

public sealed class PendingMail
{
    public string RequestId { get; set; } = "";
    public MailArgs Mail { get; set; } = new();
}

/// <summary>Arguments the server passes to the mail command (field names are the contract, see docs/server.md).</summary>
public sealed class MailArgs
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Source { get; set; } = "";
    public List<Reward> Rewards { get; set; } = new();
}
