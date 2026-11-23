using RogueStars.Rules.Core;

namespace RogueStars.Server.Infrastructure;

/// <summary>
/// All server configuration, bound from the "RogueStars" section. Every value can be set through environment
/// variables using the ASP.NET Core convention, e.g. <c>RogueStars__Auth__Mode=dev</c>.
/// </summary>
public sealed class ServerOptions
{
    public const string Section = "RogueStars";

    public AuthOptions Auth { get; set; } = new();
    public StoreOptions Store { get; set; } = new();
    public JobOptions Jobs { get; set; } = new();
    public IapOptions Iap { get; set; } = new();
    public AdsOptions Ads { get; set; } = new();
    public CommandOptions Commands { get; set; } = new();
    public ArenaOptions Arena { get; set; } = new();
    public GuildOptions Guilds { get; set; } = new();
    public InvasionOptions Invasions { get; set; } = new();
}

public sealed class AuthOptions
{
    /// <summary>"firebase" (production) or "dev" (local tokens per device id; refused in Production).</summary>
    public string Mode { get; set; } = "firebase";
    public string? FirebaseProjectId { get; set; }
    /// <summary>HMAC key for dev tokens. Only used in dev mode.</summary>
    public string DevSecret { get; set; } = "rogue-stars-dev-secret";
}

public sealed class StoreOptions
{
    /// <summary>"memory" (tests/dev) or "firestore".</summary>
    public string Mode { get; set; } = "firestore";
    public string? FirestoreProjectId { get; set; }
    public string? FirestoreDatabase { get; set; }
    /// <summary>Prefix for every collection name (lets several environments share one database).</summary>
    public string CollectionPrefix { get; set; } = "";
    /// <summary>How many recent request ids (with their results) are kept per player for idempotency.</summary>
    public int IdempotencyWindow { get; set; } = 64;
}

public sealed class JobOptions
{
    /// <summary>"oidc" (Cloud Scheduler Google ID token) or "secret" (shared secret header, dev only).</summary>
    public string Mode { get; set; } = "oidc";
    public string? Secret { get; set; }
    /// <summary>Expected audience of the scheduler OIDC token (the Cloud Run service URL).</summary>
    public string? Audience { get; set; }
    /// <summary>Service account e-mail Cloud Scheduler signs tokens as.</summary>
    public string? ServiceAccountEmail { get; set; }
}

public sealed class IapOptions
{
    /// <summary>"fake" (tokens starting with "fake-" are valid) or "google" (Android Publisher API).</summary>
    public string Mode { get; set; } = "google";
    public string? PackageName { get; set; }
    /// <summary>Service account JSON with Play Console access (from Secret Manager). Empty = use ADC.</summary>
    public string? ServiceAccountJson { get; set; }
    /// <summary>Accept license-tester purchases (<c>testPurchaseContext</c> set). Needed for internal testing; turn off for production if desired.</summary>
    public bool AllowTestPurchases { get; set; } = true;
    /// <summary>Require <c>obfuscatedExternalAccountId</c> on every purchase (the client always sets it; older clients may not).</summary>
    public bool RequireAccountId { get; set; }
}

public sealed class AdsOptions
{
    /// <summary>"fake" (accept every callback, ad tokens unchecked) or "applovin" (AppLovin MAX S2S verification).</summary>
    public string Mode { get; set; } = "applovin";
    /// <summary>AppLovin Event Key (dashboard: Account → General → Keys).</summary>
    public string? EventKey { get; set; }
    /// <summary>"event_token" = sha1(EVENT_ID + key) (default) or "event_token_all" = sha256(all macros + key); see AppLovinAdVerifier.</summary>
    public string Verification { get; set; } = "event_token";
    /// <summary>If non-empty, callbacks must carry one of these MAX ad unit ids ({AD_UNIT_ID}).</summary>
    public List<string> AllowedAdUnitIds { get; set; } = new();
    /// <summary>How long a verified ad view can be spent by a command (revive, stamina, …).</summary>
    public int CreditTtlHours { get; set; } = 24;
}

/// <summary>
/// Names of rules commands the server invokes itself. They are resolved dynamically through
/// <see cref="GameEngine.CommandType"/>; a missing command disables the hook (see docs/server.md).
/// </summary>
public sealed class CommandOptions
{
    public string PurchaseComplete { get; set; } = "PurchaseComplete";
    public string AdWatched { get; set; } = "AdWatched";
    public string InvasionOutcome { get; set; } = "LiveOpsApplyOutcome";
    public string InvasionAttack { get; set; } = "InvasionRecordDamage";
    /// <summary>Consumes the arena ticket before the server resolves a fight (clients never start fights themselves).</summary>
    public string ArenaStart { get; set; } = "ArenaStartFight";
    public string ArenaFight { get; set; } = "ArenaFightResult";
    public string MailDeliver { get; set; } = "MailPush";
    public string GuildMembership { get; set; } = "GuildSetMembership";

    /// <summary>Extra commands clients may not send (in addition to [ServerOnly] and the hook names above).</summary>
    public List<string> ClientDenyList { get; set; } = new();
    /// <summary>If non-empty, clients may only send these commands.</summary>
    public List<string> ClientAllowList { get; set; } = new();

    public IEnumerable<string> HookNames() =>
        new[] { PurchaseComplete, AdWatched, InvasionOutcome, InvasionAttack, ArenaStart, ArenaFight, MailDeliver, GuildMembership };
}

public sealed class ArenaOptions
{
    public int GroupSize { get; set; } = 150;
    public int MaxLeague { get; set; } = 6;
    /// <summary>Opponents are drawn at random from the N members closest in points.</summary>
    public int OpponentWindow { get; set; } = 5;
    public int PromoteTop { get; set; } = 15;
    public int DemoteBottom { get; set; } = 15;
    /// <summary>
    /// Optional override of the weekly rank-reward ladder (amounts scale by (1 + 0.5 * league)). Empty (default): the rules'
    /// league ladder from content (Diamonds, Arena Tickets, Arena Medals).
    /// </summary>
    public List<RankReward> RankRewards { get; set; } = new();
}

public sealed class RankReward
{
    public int MaxRank { get; set; }
    public List<RewardSpec> Rewards { get; set; } = new();
}

/// <summary>Configuration-bindable reward (rules <see cref="Reward"/> uses fields, which the binder ignores).</summary>
public sealed class RewardSpec
{
    public string Kind { get; set; } = RewardKinds.Currency;
    public string Id { get; set; } = "";
    public long Amount { get; set; } = 1;
    public int Rarity { get; set; }

    public static RewardSpec Cur(string currency, long amount) => new() { Id = currency, Amount = amount };
    public Reward ToReward(double scale = 1) => new(Kind, Id, Math.Max(1, (long)Math.Round(Amount * scale)), Rarity);
}

public sealed class GuildOptions
{
    public int MaxMembers { get; set; } = 30;
    public int ChatHistory { get; set; } = 200;
    public int ChatMaxLength { get; set; } = 300;
    public int ChatMaxMessagesPerWindow { get; set; } = 5;
    public int ChatWindowSeconds { get; set; } = 30;
    public int ChatMinIntervalMs { get; set; } = 1000;
    /// <summary>Words masked by the default chat filter.</summary>
    public List<string> BlockedWords { get; set; } = new();
}

public sealed class InvasionOptions
{
    public int GroupSize { get; set; } = 20;
    /// <summary>Invader HP per group member and invader power of calendar instances (and default for /internal/invasions).</summary>
    public long HpPerMember { get; set; } = 10_000;
    public long InvaderPower { get; set; } = 1_000;
    public int TopContributors { get; set; } = 3;
    /// <summary>Extra rewards mailed to the top contributors of each group. Defaults applied when empty.</summary>
    public List<RewardSpec> TopRewards { get; set; } = new();
}
