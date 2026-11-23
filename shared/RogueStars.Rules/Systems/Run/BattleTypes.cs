using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Role of a combatant. Turn order within a side: Mount → Aliens → Summons → Worm → Enemies/Elites/Boss.</summary>
    public enum CombatRole { Worm, Alien, Mount, Summon, Enemy, Elite, Boss }

    public enum BattleSide { Player = 0, Enemy = 1 }

    public enum BattleEndReason { EnemiesDefeated, PlayerDefeated, RoundCap }

    /// <summary>What a log entry shows. Amount is damage, heal, shield, rage or stacks depending on the action.</summary>
    public enum BattleAction
    {
        Basic, Rage, Combo, Counter, Skill, StatusTick, StatusApplied, StatusExpired, StatusResisted,
        Heal, Shield, GainRage, Buff, Summon, SkipTurn, Death, Regen, LifeSteal,
    }

    /// <summary>
    /// One combatant as input to the simulator. Stats are the base stat block WITHOUT the passive stats of
    /// SkillIds (the simulator adds each skill's passive package). Built by BattleSimulator.BuildPlayerSide /
    /// BuildEnemy, or by hand (arena snapshots, invasion bosses).
    /// </summary>
    [Serializable]
    public sealed class CombatantSpec
    {
        public string Id;
        public string Name;
        public CombatRole Role;
        public StatBlock Stats = new StatBlock();
        public List<string> SkillIds = new List<string>();
        /// <summary>Run skill levels above 1 (skill id → level): those skills fight at their leveled form (SkillLeveling).</summary>
        public Dictionary<string, int> SkillLevels = new Dictionary<string, int>();
        /// <summary>The skill as this combatant uses it (leveled run skills; the content def otherwise).</summary>
        public SkillDef Skill(ContentDb content, string id)
        {
            var def = content.Skill(id);
            return def != null && SkillLevels.TryGetValue(id, out var lv) && lv > 1 ? SkillLeveling.At(content, def, lv) : def;
        }
        /// <summary>Only targetable fighters can be attacked. Default: worm and enemies yes, aliens/mount/summons no.</summary>
        public bool Targetable = true;
        public AttackStyle Style = AttackStyle.Slam;
        /// <summary>Current HP at battle start; &lt; 0 = full. May exceed the HP stat (boss HP pools).</summary>
        public double StartHp = -1;
        public double StartShield;
        /// <summary>Threat multiplier used for enemy summons (keeps summons on the same curve).</summary>
        public double ThreatScale = 1;
    }

    [Serializable]
    public sealed class BattleSetup
    {
        public List<CombatantSpec> Player = new List<CombatantSpec>();
        public List<CombatantSpec> Enemy = new List<CombatantSpec>();
        /// <summary>0 = content default (Combat.RoundCap). Reaching the cap with enemies alive is a loss.</summary>
        public int RoundCap;
    }

    [Serializable]
    public sealed class BattleFighterInfo
    {
        public int Index;
        public string Id;
        public string Name;
        public CombatRole Role;
        public BattleSide Side;
        public bool Targetable;
        public long MaxHp;
        public long StartHp;
        /// <summary>Shield / rage when the first round starts (mount shield on the worm, start-rage effects); summons: when summoned.</summary>
        public long StartShield;
        public long StartRage;
        public long EndHp;
        public long EndShield;
        public long EndRage;
        public bool Alive;
        public long DamageDealt;
        /// <summary>Index of the fighter that summoned this one (-1 = present from the start).</summary>
        public int SummonedBy = -1;
    }

    /// <summary>Compact log entry for the client to animate. Actor/Target are indices into BattleResult.Fighters.</summary>
    [Serializable]
    public sealed class BattleLogEntry
    {
        public int Round;
        public int Actor = -1;
        public BattleAction Action;
        public int Target = -1;
        public long Amount;
        public bool Crit;
        public bool Dodge;
        public bool Parry;
        public StatusId Status;
        public int Stacks;
        public string SkillId;
        /// <summary>Target HP / shield / rage right after this entry (rounded). Rage gained for acting or being hit is
        /// added after the entry and shows up in the fighter's next entry.</summary>
        public long TargetHp;
        public long TargetShield;
        public long TargetRage;
        /// <summary>Actor shield / rage right after this entry (0 without an actor); a Rage attack shows the rage left after spending.</summary>
        public long ActorShield;
        public long ActorRage;

        public override string ToString() =>
            "r" + Round + " " + Actor + ">" + Target + " " + Action + " " + Amount + (Crit ? " C" : "") + (Dodge ? " D" : "") + (Parry ? " P" : "") +
            (Status != StatusId.None ? " " + Status + "x" + Stacks : "") + (SkillId != null ? " [" + SkillId + "]" : "") + " hp=" + TargetHp;
    }

    [Serializable]
    public sealed class BattleResult
    {
        public BattleSide Winner;
        public BattleEndReason EndReason;
        public int Rounds;
        public List<BattleFighterInfo> Fighters = new List<BattleFighterInfo>();
        public List<BattleLogEntry> Log = new List<BattleLogEntry>();
        public bool LogTruncated;
        /// <summary>Total damage the player side dealt (the score for HP-pool boss fights).</summary>
        public long PlayerDamageDealt;
        public long PlayerDamageTaken;
        /// <summary>Worm (first player-side Worm) HP after the battle (after OnBattleWon heals).</summary>
        public double WormHpLeft;
        public double WormMaxHp;

        public bool PlayerWon() => Winner == BattleSide.Player;
    }
}
