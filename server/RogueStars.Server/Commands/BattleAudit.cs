using Microsoft.Extensions.Options;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Commands;

/// <summary>
/// Local-first battles (docs/server.md → Local-first commands): the client simulates run battles itself and sends each
/// battle's result (<see cref="BattleResult.Summary"/>) with the command as a claim. The server takes the claim as the
/// result and re-simulates only a sample from the same inputs (setup + battle RNG): standard battles
/// <see cref="BattleAuditOptions.StandardSampleRate"/>, elites/minibosses <see cref="BattleAuditOptions.EliteSampleRate"/>,
/// bosses <see cref="BattleAuditOptions.BossSampleRate"/>. A sampled battle always resolves to the server's result (a
/// disagreeing claim is silently corrected through the state hash mismatch). Disagreements are recorded per player
/// (<see cref="Collections.BattleAudits"/>) and a player with too many in the recent window is flagged.
/// </summary>
public sealed class BattleAuditService(IDocStore docs, IBattleSampler sampler, IOptions<ServerOptions> options, ILogger<BattleAuditService> log)
{
    public BattleAuditOptions Options => options.Value.Battles;

    /// <summary>
    /// The rules battle resolver for one command attempt: claims by index; sampled (or malformed) claims are re-simulated and
    /// the comparison lands in <paramref name="checks"/>. Battles without a claim are simulated (old clients, a client whose
    /// prediction took another path).
    /// </summary>
    public BattleResolver Resolver(string playerId, IReadOnlyList<BattleResult>? claims, List<BattleCheck> checks) => (ctx, setup, rng, kind, index) =>
    {
        var claim = claims != null && index < claims.Count ? claims[index] : null;
        if (claim == null) return null; // simulate
        var tier = FoughtBattle.TierOf(kind);
        bool wellFormed = claim.Fighters != null && claim.Fighters.Count >= setup.Player.Count + setup.Enemy.Count;
        if (wellFormed && !sampler.Sample(playerId, tier, Rate(tier))) return claim.Summary();
        var server = BattleSimulator.Simulate(setup, ctx.Content, rng);
        var serverSummary = server.Summary();
        var claimSummary = wellFormed ? claim.Summary() : null;
        bool match = claimSummary != null && StateHash.Of(serverSummary) == StateHash.Of(claimSummary);
        checks.Add(new BattleCheck { Index = index, Kind = kind, Tier = tier, Match = match, Client = claimSummary, Server = serverSummary });
        return server; // a sampled battle is always the server's result
    };

    public double Rate(BattleTier tier) => tier switch
    {
        BattleTier.Boss => Options.BossSampleRate,
        BattleTier.Elite => Options.EliteSampleRate,
        _ => Options.StandardSampleRate,
    };

    /// <summary>Records the validated battles of a saved command; flags the player when the mismatch rule trips.</summary>
    public async Task RecordAsync(string playerId, IReadOnlyList<BattleCheck> checks, long nowMs, CancellationToken ct = default)
    {
        if (checks.Count == 0) return;
        var o = Options;
        bool newlyFlagged = false;
        var stored = await docs.MutateAsync<BattleAuditRecord>(Collections.BattleAudits, playerId, a =>
        {
            a ??= new BattleAuditRecord();
            bool wasFlagged = a.Flagged;
            foreach (var c in checks)
            {
                a.Validated++;
                a.Window.Add(c.Match);
                if (!c.Match)
                {
                    a.Mismatches++;
                    a.Recent.Add(new BattleMismatch { AtMs = nowMs, Kind = c.Kind, Tier = c.Tier.ToString(), Client = c.Client, Server = c.Server });
                }
            }
            int window = Math.Max(1, o.FlagWindow);
            if (a.Window.Count > window) a.Window.RemoveRange(0, a.Window.Count - window);
            int keep = Math.Max(0, o.KeepRecentMismatches);
            if (a.Recent.Count > keep) a.Recent.RemoveRange(0, a.Recent.Count - keep);
            a.WindowMismatches = a.Window.Count(m => !m);
            if (!a.Flagged && a.WindowMismatches >= Math.Max(1, o.FlagMismatches))
            {
                a.Flagged = true;
                a.FlaggedAtMs = nowMs;
            }
            newlyFlagged = a.Flagged && !wasFlagged;
            return a;
        }, ct);
        foreach (var c in checks.Where(c => !c.Match))
            log.LogWarning("Battle mismatch for {PlayerId}: {Kind} battle {Index}, client {Client} vs server {Server}", playerId, c.Kind, c.Index,
                Describe(c.Client), Describe(c.Server));
        if (newlyFlagged)
            log.LogWarning("Player {PlayerId} flagged as a potential cheater: {Mismatches} battle mismatches in the last {Window} validated battles ({Total} of {Validated} overall)",
                playerId, stored!.WindowMismatches, stored.Window.Count, stored.Mismatches, stored.Validated);
    }

    public Task<Doc<BattleAuditRecord>?> GetAsync(string playerId, CancellationToken ct = default) => docs.GetAsync<BattleAuditRecord>(Collections.BattleAudits, playerId, ct);

    /// <summary>"won in 7 rounds, worm 120/300 HP, enemies 0/0 HP" (logs and the dev toast).</summary>
    public static string Describe(BattleResult? r)
    {
        if (r == null) return "malformed claim";
        var enemies = string.Join("/", r.Fighters.Where(f => f.Side == BattleSide.Enemy && f.SummonedBy < 0).Select(f => f.EndHp));
        return (r.PlayerWon() ? "won" : "lost") + " in " + r.Rounds + " rounds, worm " + Math.Round(r.WormHpLeft) + "/" + Math.Round(r.WormMaxHp) +
               " HP, enemies " + (enemies.Length == 0 ? "-" : enemies) + " HP, coins " + r.RunCoins;
    }
}

/// <summary>Decides whether a battle is re-simulated (tests replace it with a deterministic one).</summary>
public interface IBattleSampler
{
    bool Sample(string playerId, BattleTier tier, double rate);
}

public sealed class RandomBattleSampler : IBattleSampler
{
    public bool Sample(string playerId, BattleTier tier, double rate) => rate >= 1 || (rate > 0 && Random.Shared.NextDouble() < rate);
}

/// <summary>One re-simulated battle of a command.</summary>
public sealed class BattleCheck
{
    public int Index { get; set; }
    public string Kind { get; set; } = "";
    public BattleTier Tier { get; set; }
    public bool Match { get; set; }
    /// <summary>The client's claim (null = malformed).</summary>
    public BattleResult? Client { get; set; }
    public BattleResult? Server { get; set; }
}

/// <summary>Per-player battle audit (<c>battle_audits/{playerId}</c>).</summary>
public sealed class BattleAuditRecord
{
    /// <summary>Battles re-simulated so far.</summary>
    public long Validated { get; set; }
    /// <summary>Re-simulated battles whose claim disagreed.</summary>
    public long Mismatches { get; set; }
    /// <summary>Match flags of the last FlagWindow validated battles (oldest first).</summary>
    public List<bool> Window { get; set; } = new();
    public int WindowMismatches { get; set; }
    /// <summary>Potential cheater: the mismatch rule tripped once (sticky; for review, nothing is blocked).</summary>
    public bool Flagged { get; set; }
    public long FlaggedAtMs { get; set; }
    /// <summary>Details of the last few mismatches.</summary>
    public List<BattleMismatch> Recent { get; set; } = new();
}

public sealed class BattleMismatch
{
    public long AtMs { get; set; }
    public string Kind { get; set; } = "";
    public string Tier { get; set; } = "";
    public BattleResult? Client { get; set; }
    public BattleResult? Server { get; set; }
}

/// <summary>Local-first battle sampling and cheater flagging (RogueStars:Battles).</summary>
public sealed class BattleAuditOptions
{
    public double StandardSampleRate { get; set; } = 0.10;
    /// <summary>Elite tiles, minibosses and ambushes.</summary>
    public double EliteSampleRate { get; set; } = 0.50;
    public double BossSampleRate { get; set; } = 1.0;
    /// <summary>Flag a player when at least this many of the last <see cref="FlagWindow"/> validated battles disagreed.</summary>
    public int FlagMismatches { get; set; } = 3;
    public int FlagWindow { get; set; } = 20;
    /// <summary>Mismatch details kept per player.</summary>
    public int KeepRecentMismatches { get; set; } = 5;
    /// <summary>
    /// Dev servers: command responses carry the re-simulated battles' comparison (<c>battleChecks</c>) so a dev build shows a
    /// mismatch to the developer. Off in production (the player is never interrupted).
    /// </summary>
    public bool ReportToClient { get; set; }
}
