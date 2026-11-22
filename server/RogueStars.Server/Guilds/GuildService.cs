using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RogueStars.Server.Commands;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Guilds;

public static class GuildRoles
{
    public const string Leader = "leader";
    public const string Officer = "officer";
    public const string Member = "member";
}

public sealed class GuildMember
{
    public string PlayerId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = GuildRoles.Member;
    public long JoinedMs { get; set; }
}

public sealed class Guild
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public long CreatedMs { get; set; }
    /// <summary>Activity XP (invasion contributions); level derived from it.</summary>
    public long Xp { get; set; }
    public List<GuildMember> Members { get; set; } = new();
    /// <summary>Weekly invasion contribution: week → playerId → damage. Only the last few weeks are kept.</summary>
    public Dictionary<string, Dictionary<string, long>> InvasionContributions { get; set; } = new();

    public GuildMember? Member(string playerId) => Members.FirstOrDefault(m => m.PlayerId == playerId);
    public int Level => 1 + (int)Math.Floor(Math.Sqrt(Xp / 1000.0));
}

/// <summary>Reverse index player → guild (also enforces "one guild per player" via create-if-absent).</summary>
public sealed class GuildMembership
{
    public string GuildId { get; set; } = "";
}

public sealed class GuildNameClaim
{
    public string GuildId { get; set; } = "";
}

public sealed class ChatMessage
{
    public string Id { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Text { get; set; } = "";
    public long AtMs { get; set; }
}

public sealed class GuildChat
{
    public List<ChatMessage> Messages { get; set; } = new();
    /// <summary>Recent send times per player (rate limiting that holds across server instances).</summary>
    public Dictionary<string, List<long>> RecentSends { get; set; } = new();
}

public sealed class ChatReport
{
    public string Id { get; set; } = "";
    public string GuildId { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string ReporterId { get; set; } = "";
    public string AuthorId { get; set; } = "";
    public string Text { get; set; } = "";
    public string Reason { get; set; } = "";
    public long AtMs { get; set; }
}

/// <summary>Chat moderation hook: return null to reject the message, or the (possibly masked) text to store.</summary>
public interface IChatFilter
{
    string? Filter(string text);
}

/// <summary>Default filter: masks configured words (whole-word, case-insensitive) with asterisks.</summary>
public sealed class WordListChatFilter(IOptions<ServerOptions> options) : IChatFilter
{
    public string? Filter(string text)
    {
        foreach (var word in options.Value.Guilds.BlockedWords.Where(w => !string.IsNullOrWhiteSpace(w)))
            text = Regex.Replace(text, @"\b" + Regex.Escape(word) + @"\b", new string('*', word.Length), RegexOptions.IgnoreCase);
        return text;
    }
}

public sealed record CreateGuildRequest(string? Name, string? Description);
public sealed record MemberRequest(string? PlayerId, string? Role);
public sealed record ChatRequest(string? Text);
public sealed record ReportRequest(string? Reason);

public sealed class GuildService(IDocStore docs, PlayerService players, ServerHooks hooks, IChatFilter filter, Clock clock,
    IOptions<ServerOptions> options, ILogger<GuildService> log)
{
    private GuildOptions O => options.Value.Guilds;

    public static string NormalizeName(string name) => Regex.Replace(name.Trim().ToLowerInvariant(), @"\s+", " ");

    public async Task<string?> GuildIdOfAsync(string playerId, CancellationToken ct) =>
        (await docs.GetAsync<GuildMembership>(Collections.GuildMembers, playerId, ct))?.Value.GuildId;

    public async Task<Guild> GetAsync(string guildId, CancellationToken ct) =>
        (await docs.GetAsync<Guild>(Collections.Guilds, guildId, ct))?.Value ?? throw ApiException.NotFound("guild_not_found");

    private async Task<string> RequireGuildAsync(string playerId, CancellationToken ct) =>
        await GuildIdOfAsync(playerId, ct) ?? throw ApiException.BadRequest("not_in_guild");

    private async Task<string> DisplayNameAsync(string playerId, CancellationToken ct)
    {
        var state = await players.SettledAsync(playerId, ct);
        return state.DisplayName ?? playerId;
    }

    public async Task<Guild> CreateAsync(string playerId, CreateGuildRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length < 3 || name.Length > 24) throw ApiException.BadRequest("invalid_name");
        if (filter.Filter(name) != name) throw ApiException.BadRequest("invalid_name");
        var description = (req.Description ?? "").Trim();
        if (description.Length > 200) throw ApiException.BadRequest("invalid_description");
        string displayName = await DisplayNameAsync(playerId, ct);

        string guildId = Ids.New(6);
        string key = Ids.Hash(NormalizeName(name));
        if (!await docs.TryPutAsync(Collections.GuildMembers, playerId, new GuildMembership { GuildId = guildId }, 0, ct))
            throw ApiException.Conflict("already_in_guild");
        if (!await docs.TryPutAsync(Collections.GuildNames, key, new GuildNameClaim { GuildId = guildId }, 0, ct))
        {
            await RemoveMembershipAsync(playerId, guildId, ct);
            throw ApiException.Conflict("name_taken");
        }
        var guild = new Guild
        {
            Id = guildId, Name = name, Description = description, CreatedMs = clock.NowMs,
            Members = { new GuildMember { PlayerId = playerId, DisplayName = displayName, Role = GuildRoles.Leader, JoinedMs = clock.NowMs } },
        };
        await docs.TryPutAsync(Collections.Guilds, guildId, guild, 0, ct);
        await NotifyAsync(playerId, guildId, GuildRoles.Leader, ct);
        log.LogInformation("Guild {GuildId} created by {PlayerId}", guildId, playerId);
        return guild;
    }

    public async Task<IReadOnlyList<object>> ListAsync(string? query, CancellationToken ct)
    {
        var all = await docs.ListAsync<Guild>(Collections.Guilds, null, 200, ct);
        return all.Select(d => d.Value)
            .Where(g => string.IsNullOrWhiteSpace(query) || g.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(g => g.Members.Count < O.MaxMembers)
            .Take(50)
            .Select(g => (object)new { g.Id, g.Name, g.Description, g.Level, members = g.Members.Count, maxMembers = O.MaxMembers })
            .ToList();
    }

    public async Task<Guild> JoinAsync(string playerId, string guildId, CancellationToken ct)
    {
        await GetAsync(guildId, ct);
        string displayName = await DisplayNameAsync(playerId, ct);
        if (!await docs.TryPutAsync(Collections.GuildMembers, playerId, new GuildMembership { GuildId = guildId }, 0, ct))
            throw ApiException.Conflict("already_in_guild");
        try
        {
            var guild = await docs.MutateAsync<Guild>(Collections.Guilds, guildId, g =>
            {
                if (g == null) throw ApiException.NotFound("guild_not_found");
                if (g.Member(playerId) != null) return null;
                if (g.Members.Count >= O.MaxMembers) throw ApiException.Conflict("guild_full");
                g.Members.Add(new GuildMember { PlayerId = playerId, DisplayName = displayName, Role = GuildRoles.Member, JoinedMs = clock.NowMs });
                return g;
            }, ct);
            await NotifyAsync(playerId, guildId, GuildRoles.Member, ct);
            return guild!;
        }
        catch (ApiException)
        {
            await RemoveMembershipAsync(playerId, guildId, ct);
            throw;
        }
    }

    public async Task LeaveAsync(string playerId, CancellationToken ct)
    {
        string guildId = await RequireGuildAsync(playerId, ct);
        await RemoveFromGuildAsync(guildId, playerId, ct);
    }

    public async Task<Guild> KickAsync(string actorId, string targetId, CancellationToken ct)
    {
        string guildId = await RequireGuildAsync(actorId, ct);
        var guild = await GetAsync(guildId, ct);
        var actor = guild.Member(actorId)!;
        var target = guild.Member(targetId) ?? throw ApiException.NotFound("member_not_found");
        if (actorId == targetId) throw ApiException.BadRequest("cannot_kick_self");
        bool allowed = actor.Role == GuildRoles.Leader || (actor.Role == GuildRoles.Officer && target.Role == GuildRoles.Member);
        if (!allowed) throw ApiException.Forbidden("insufficient_role");
        return await RemoveFromGuildAsync(guildId, targetId, ct) ?? guild;
    }

    /// <summary>Leader only. Promoting someone to leader transfers leadership (old leader becomes officer).</summary>
    public async Task<Guild> SetRoleAsync(string actorId, string targetId, string role, CancellationToken ct)
    {
        if (role is not (GuildRoles.Leader or GuildRoles.Officer or GuildRoles.Member)) throw ApiException.BadRequest("invalid_role");
        string guildId = await RequireGuildAsync(actorId, ct);
        var guild = await docs.MutateAsync<Guild>(Collections.Guilds, guildId, g =>
        {
            if (g == null) throw ApiException.NotFound("guild_not_found");
            var actor = g.Member(actorId);
            if (actor?.Role != GuildRoles.Leader) throw ApiException.Forbidden("insufficient_role");
            var target = g.Member(targetId) ?? throw ApiException.NotFound("member_not_found");
            if (target.PlayerId == actorId) throw ApiException.BadRequest("cannot_change_own_role");
            if (role == GuildRoles.Leader) actor.Role = GuildRoles.Officer;
            target.Role = role;
            return g;
        }, ct);
        await NotifyAsync(targetId, guildId, role, ct);
        if (role == GuildRoles.Leader) await NotifyAsync(actorId, guildId, GuildRoles.Officer, ct);
        return guild!;
    }

    private async Task<Guild?> RemoveFromGuildAsync(string guildId, string playerId, CancellationToken ct)
    {
        bool deleteGuild = false;
        var guild = await docs.MutateAsync<Guild>(Collections.Guilds, guildId, g =>
        {
            deleteGuild = false;
            if (g == null) return null;
            var m = g.Member(playerId);
            if (m == null) return null;
            g.Members.Remove(m);
            if (g.Members.Count == 0) { deleteGuild = true; return g; }
            if (m.Role == GuildRoles.Leader)
            {
                // Succession: oldest officer, else oldest member.
                var heir = g.Members.Where(x => x.Role == GuildRoles.Officer).OrderBy(x => x.JoinedMs).FirstOrDefault()
                           ?? g.Members.OrderBy(x => x.JoinedMs).First();
                heir.Role = GuildRoles.Leader;
            }
            return g;
        }, ct);
        await RemoveMembershipAsync(playerId, guildId, ct);
        await NotifyAsync(playerId, null, null, ct);
        if (deleteGuild && guild != null)
        {
            var doc = await docs.GetAsync<Guild>(Collections.Guilds, guildId, ct);
            if (doc != null && doc.Value.Members.Count == 0)
            {
                await docs.TryDeleteAsync(Collections.Guilds, guildId, doc.Version, ct);
                var name = await docs.GetAsync<GuildNameClaim>(Collections.GuildNames, Ids.Hash(NormalizeName(guild.Name)), ct);
                if (name != null && name.Value.GuildId == guildId)
                    await docs.TryDeleteAsync(Collections.GuildNames, name.Id, name.Version, ct);
                log.LogInformation("Guild {GuildId} disbanded", guildId);
            }
        }
        return guild;
    }

    private async Task RemoveMembershipAsync(string playerId, string guildId, CancellationToken ct)
    {
        var m = await docs.GetAsync<GuildMembership>(Collections.GuildMembers, playerId, ct);
        if (m != null && m.Value.GuildId == guildId) await docs.TryDeleteAsync(Collections.GuildMembers, playerId, m.Version, ct);
    }

    /// <summary>Optional rules hook so the player state knows its guild (shop access, quests...).</summary>
    private async Task NotifyAsync(string playerId, string? guildId, string? role, CancellationToken ct)
    {
        if (!hooks.Has(hooks.Names.GuildMembership)) return;
        try
        {
            await hooks.TryExecuteAsync(playerId, hooks.Names.GuildMembership, new { GuildId = guildId, Role = role },
                "guild-" + Ids.New(), ct);
        }
        catch (ApiException ex)
        {
            log.LogWarning("Guild membership hook for {PlayerId} failed: {Error}", playerId, ex.Error);
        }
    }

    // ---- chat -----------------------------------------------------------------------------------------

    public async Task<ChatMessage> PostChatAsync(string playerId, string text, CancellationToken ct)
    {
        string guildId = await RequireGuildAsync(playerId, ct);
        var guild = await GetAsync(guildId, ct);
        var member = guild.Member(playerId) ?? throw ApiException.BadRequest("not_in_guild");
        text = (text ?? "").Trim();
        if (text.Length == 0) throw ApiException.BadRequest("empty_message");
        if (text.Length > O.ChatMaxLength) throw ApiException.BadRequest("message_too_long");
        var filtered = filter.Filter(text) ?? throw ApiException.BadRequest("message_rejected");

        long now = clock.NowMs;
        var message = new ChatMessage { Id = Ids.New(8), PlayerId = playerId, DisplayName = member.DisplayName, Text = filtered, AtMs = now };
        await docs.MutateAsync<GuildChat>(Collections.GuildChats, guildId, chat =>
        {
            chat ??= new GuildChat();
            long windowStart = now - O.ChatWindowSeconds * 1000L;
            if (!chat.RecentSends.TryGetValue(playerId, out var sends)) chat.RecentSends[playerId] = sends = new List<long>();
            sends.RemoveAll(t => t < windowStart);
            if (sends.Count >= O.ChatMaxMessagesPerWindow || (sends.Count > 0 && now - sends[^1] < O.ChatMinIntervalMs))
                throw new ApiException(StatusCodes.Status429TooManyRequests, "rate_limited");
            sends.Add(now);
            foreach (var key in chat.RecentSends.Where(kv => kv.Value.All(t => t < windowStart)).Select(kv => kv.Key).ToList())
                chat.RecentSends.Remove(key);
            chat.Messages.Add(message);
            if (chat.Messages.Count > O.ChatHistory) chat.Messages.RemoveRange(0, chat.Messages.Count - O.ChatHistory);
            return chat;
        }, ct);
        return message;
    }

    public async Task<IReadOnlyList<ChatMessage>> ChatAsync(string playerId, long sinceMs, CancellationToken ct)
    {
        string guildId = await RequireGuildAsync(playerId, ct);
        var chat = (await docs.GetAsync<GuildChat>(Collections.GuildChats, guildId, ct))?.Value ?? new GuildChat();
        return chat.Messages.Where(m => m.AtMs > sinceMs).ToList();
    }

    public async Task<ChatReport> ReportAsync(string playerId, string messageId, string? reason, CancellationToken ct)
    {
        string guildId = await RequireGuildAsync(playerId, ct);
        var chat = (await docs.GetAsync<GuildChat>(Collections.GuildChats, guildId, ct))?.Value ?? new GuildChat();
        var msg = chat.Messages.FirstOrDefault(m => m.Id == messageId) ?? throw ApiException.NotFound("message_not_found");
        var report = new ChatReport
        {
            Id = Ids.Hash(guildId + "/" + messageId + "/" + playerId)[..24], GuildId = guildId, MessageId = messageId, ReporterId = playerId,
            AuthorId = msg.PlayerId, Text = msg.Text, Reason = (reason ?? "").Trim()[..Math.Min((reason ?? "").Trim().Length, 200)], AtMs = clock.NowMs,
        };
        await docs.TryPutAsync(Collections.ChatReports, report.Id, report, 0, ct); // duplicate reports are ignored
        log.LogWarning("Chat report {ReportId}: message {MessageId} in guild {GuildId} by {AuthorId}", report.Id, messageId, guildId, msg.PlayerId);
        return report;
    }

    // ---- invasion contributions ------------------------------------------------------------------------

    public async Task AddInvasionContributionAsync(string playerId, long damage, CancellationToken ct)
    {
        var guildId = await GuildIdOfAsync(playerId, ct);
        if (guildId == null || damage <= 0) return;
        string week = clock.WeekKey();
        await docs.MutateAsync<Guild>(Collections.Guilds, guildId, g =>
        {
            if (g == null || g.Member(playerId) == null) return null;
            if (!g.InvasionContributions.TryGetValue(week, out var byPlayer)) g.InvasionContributions[week] = byPlayer = new();
            byPlayer[playerId] = (byPlayer.TryGetValue(playerId, out var v) ? v : 0) + damage;
            foreach (var old in g.InvasionContributions.Keys.OrderByDescending(k => k, StringComparer.Ordinal).Skip(4).ToList())
                g.InvasionContributions.Remove(old);
            g.Xp += damage / 100 + 1;
            return g;
        }, ct);
    }

    public static object ContributionSummary(Guild g, string week)
    {
        var byPlayer = g.InvasionContributions.TryGetValue(week, out var d) ? d : new Dictionary<string, long>();
        return new
        {
            week,
            total = byPlayer.Values.Sum(),
            members = byPlayer.OrderByDescending(kv => kv.Value)
                .Select(kv => new { playerId = kv.Key, displayName = g.Member(kv.Key)?.DisplayName, damage = kv.Value }),
        };
    }
}
