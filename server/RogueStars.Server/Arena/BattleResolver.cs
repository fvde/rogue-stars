using RogueStars.Rules.Core;

namespace RogueStars.Server.Arena;

public sealed class BattleOutcome
{
    public bool AttackerWon { get; set; }
    /// <summary>Damage dealt by the attacker (used for invasion contributions).</summary>
    public long Damage { get; set; }
    /// <summary>Optional serialized battle log / replay for the client.</summary>
    public object? Log { get; set; }
}

/// <summary>
/// Server-side battle resolution. TODO(lead): after merge, wire an implementation backed by the rules
/// <c>BattleSimulator</c> (workstream Run) and register it instead of <see cref="PowerBattleResolver"/>.
/// Resolution must be deterministic for a given seed so clients can replay it.
/// </summary>
public interface IBattleResolver
{
    /// <summary>Arena: attacker snapshot vs. defender snapshot.</summary>
    BattleOutcome ResolveDuel(CombatLoadout attacker, CombatLoadout defender, long seed);

    /// <summary>Invasion: one attack of a player against the invader; returns damage dealt.</summary>
    BattleOutcome ResolveInvasionAttack(CombatLoadout attacker, string invasionId, long invaderPower, long seed);
}

/// <summary>Placeholder: compares <see cref="CombatLoadout.Power"/> with ±15% seeded variance.</summary>
public sealed class PowerBattleResolver : IBattleResolver
{
    public BattleOutcome ResolveDuel(CombatLoadout attacker, CombatLoadout defender, long seed)
    {
        var rng = Rng.FromSeed(seed);
        double a = Math.Max(1, attacker.Power) * Variance(rng);
        double d = Math.Max(1, defender.Power) * Variance(rng);
        return new BattleOutcome { AttackerWon = a >= d, Damage = (long)a, Log = new { resolver = "power", attacker = a, defender = d } };
    }

    public BattleOutcome ResolveInvasionAttack(CombatLoadout attacker, string invasionId, long invaderPower, long seed)
    {
        var rng = Rng.FromSeed(seed);
        long damage = Math.Max(1, (long)(Math.Max(1, attacker.Power) * Variance(rng)));
        return new BattleOutcome { AttackerWon = damage >= invaderPower, Damage = damage, Log = new { resolver = "power", damage } };
    }

    private static double Variance(Rng rng) => 0.85 + 0.3 * rng.NextDouble();
}
