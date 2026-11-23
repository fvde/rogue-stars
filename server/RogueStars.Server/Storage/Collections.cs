namespace RogueStars.Server.Storage;

/// <summary>Collection names of the cross-player document store (prefixed by Store:CollectionPrefix).</summary>
public static class Collections
{
    public const string MailOutbox = "mail_outbox";            // {playerId}
    public const string ArenaSnapshots = "arena_snapshots";    // {playerId}
    public const string ArenaPlayers = "arena_players";        // {playerId}
    public const string ArenaGroups = "arena_groups";          // {week}_L{league}_{n}
    public const string ArenaCursors = "arena_cursors";        // {week}_L{league}
    public const string ArenaJobs = "arena_jobs";              // {week}
    public const string Guilds = "guilds";                     // {guildId}
    public const string GuildNames = "guild_names";            // {normalized name}
    public const string GuildMembers = "guild_members";        // {playerId}
    public const string GuildChats = "guild_chats";            // {guildId}
    public const string ChatReports = "chat_reports";          // {reportId}
    public const string Invasions = "invasions";               // {invasionId}
    public const string InvasionGroups = "invasion_groups";    // {invasionId}_{n}
    public const string InvasionCursors = "invasion_cursors";  // {invasionId}
    public const string InvasionPlayers = "invasion_players";  // {invasionId}_{playerId}
    public const string IapReceipts = "iap_receipts";          // {sha256(purchaseToken)}
    public const string AdEvents = "ad_events";                // {sha256(eventId)}
    public const string AdCredits = "ad_credits";              // {playerId}
}
