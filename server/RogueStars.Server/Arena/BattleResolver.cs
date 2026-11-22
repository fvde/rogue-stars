using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Server.Arena;

public sealed class BattleOutcome
{
    public bool AttackerWon { get; set; }
    /// <summary>Damage dealt by the attacker (used for invasion contributions).</summary>
    public long Damage { get; set; }
    /// <summary>Optional serialized battle log / replay for the client.</summary>
    public object? Log { get; set; }
}

/// <summary>Server-side battle resolution. Deterministic for a given seed so clients can replay it.</summary>
public interface IBattleResolver
{
    /// <summary>Arena: attacker snapshot vs. defender snapshot.</summary>
    BattleOutcome ResolveDuel(CombatLoadout attacker, CombatLoadout defender, long seed);

    /// <summary>
    /// Invasion: one attack of a player against the invader of content invasion <paramref name="invasionTypeId"/>,
    /// whose HP pool is the group's remaining HP. Returns the damage dealt (contribution score).
    /// </summary>
    BattleOutcome ResolveInvasionAttack(CombatLoadout attacker, string invasionTypeId, long invaderPower, long remainingHp, long seed);
}

/// <summary>
/// The rules <see cref="BattleSimulator"/> (Run workstream): arena = <c>PlayerVsSnapshot</c>, invasion =
/// <c>PlayerVsBoss</c> against the content invasion's boss (score = <c>PlayerDamageDealt</c>). The invader's
/// HP/ATK/DEF scale by <c>invaderPower / InvaderPowerPerThreat</c> (1000 = content stats).
/// </summary>
public sealed class SimulatorBattleResolver(GameEngine engine) : IBattleResolver
{
    public const double InvaderPowerPerThreat = 1000;

    private ContentDb Content => engine.Content;

    public BattleOutcome ResolveDuel(CombatLoadout attacker, CombatLoadout defender, long seed)
    {
        var r = BattleSimulator.PlayerVsSnapshot(Content, attacker, defender, Rng.FromSeed(seed));
        return new BattleOutcome { AttackerWon = r.PlayerWon(), Damage = r.PlayerDamageDealt, Log = r };
    }

    public BattleOutcome ResolveInvasionAttack(CombatLoadout attacker, string invasionTypeId, long invaderPower, long remainingHp, long seed)
    {
        var def = Content.Invasion(invasionTypeId) ?? throw new InvalidOperationException("unknown invasion type " + invasionTypeId);
        double scale = Math.Max(0.1, invaderPower / InvaderPowerPerThreat);
        var boss = BattleSimulator.BuildEnemy(Content, def.BossId, scale);
        boss.StartHp = Math.Max(1, remainingHp);
        var r = BattleSimulator.PlayerVsBoss(Content, attacker, boss, Rng.FromSeed(seed));
        return new BattleOutcome { AttackerWon = r.PlayerWon(), Damage = r.PlayerDamageDealt, Log = r };
    }
}
