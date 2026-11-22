using System;
using System.Collections.Generic;

namespace RogueStars.Rules.Core
{
    public enum Rarity { Common = 0, Rare = 1, Epic = 2, Legendary = 3, Mythic = 4 }

    /// <summary>Build archetypes. Loose coupling: parts, heroes and aliens boost families without locking others out.</summary>
    public enum SkillFamily { None, Fire, Ice, Lightning, Poison, Bleed, AlienSynergy, Counter, Combo, Crit }

    /// <summary>Where a skill comes from. Same format for all sources.</summary>
    public enum SkillSource { Run, Laboratory, Hero, Mount, Alien, Building, Part }

    public enum StatusId { None, Burn, Freeze, Poison, Bleed, Stun, Shock, Charm }

    public enum TriggerKind
    {
        Passive,          // only the stat package applies
        OnBattleStart, OnRoundStart, OnRoundEnd, OnTurnStart,
        OnAttack, OnHit, OnCrit, OnCombo, OnCounter, OnDodge, OnParry,
        OnDamaged, OnKill, OnLowHp, OnRageAttack, OnStatusApplied,
        OnRunStart, OnRoll, OnTileLand, OnTilePass, OnLevelUp, OnBossAwaken,
        OnBattleWon,
    }

    public enum EffectKind
    {
        Damage,            // Value = % of source ATK
        TrueDamage,        // Value = % of source ATK, ignores DEF/DR
        ApplyStatus,       // Status, Stacks, Duration
        Heal,              // Value = % of max HP
        Shield,            // Value = % of max HP
        GainRage,          // Value = rage points
        BuffStat,          // StatMod for Duration rounds (0 = rest of battle)
        RunStatGrowth,     // permanent for the run: StatMod
        GainRunCoins,      // Value = coins
        ExtraDice,         // Value = extra dice for the next roll
        HealOnTile,        // Value = % max HP
        SlowAwakening,     // Value = meter points removed
        ExtraLoot,         // Value = % more pending loot
        Summon,            // Param = enemy/alien def id
    }

    public enum TargetKind { Self, Enemy, AllEnemies, RandomEnemy, LowestHpEnemy, Worm, AllAllies }

    [Serializable]
    public sealed class SkillTrigger
    {
        public TriggerKind When = TriggerKind.Passive;
        public double Chance = 100;          // percent
        public EffectKind Effect;
        public TargetKind Target = TargetKind.Enemy;
        public double Value;
        public StatusId Status;
        public int Stacks = 1;
        public int Duration;                 // rounds; 0 = whole battle / instant
        public StatMod Stat;                 // for BuffStat / RunStatGrowth
        public string Param;                 // free parameter (e.g. summon id, family filter)
        public int CooldownRounds;
    }

    /// <summary>
    /// Universal skill format: a passive stat package plus triggered effects.
    /// Used by run picks, laboratory research, hero traits, mount weapons, alien skills, building boons.
    /// </summary>
    [Serializable]
    public sealed class SkillDef
    {
        public string Id;
        public string Name;
        public string Description;
        public SkillFamily Family;
        public Rarity Rarity;
        public SkillSource Source;
        public List<StatMod> Stats = new List<StatMod>();
        public List<SkillTrigger> Triggers = new List<SkillTrigger>();
        /// <summary>If set, this skill replaces the given skill when drafted (upgrade chain).</summary>
        public string UpgradesFrom;
        /// <summary>Number of skills of the same family the player must already own before this is offered.</summary>
        public int RequiresFamilyCount;
        /// <summary>Skill ids that must be owned before this can be offered.</summary>
        public List<string> Requires = new List<string>();
        /// <summary>If false the skill never appears in the run draft pool (e.g. hero traits).</summary>
        public bool Draftable = true;
    }
}
