using System;
using System.Collections.Generic;

namespace RogueStars.Rules.Core
{
    public enum Rarity { Common = 0, Rare = 1, Epic = 2, Legendary = 3, Mythic = 4 }

    /// <summary>Build archetypes. Loose coupling: parts, heroes and aliens boost families without locking others out.</summary>
    /// <remarks>
    /// <c>Poison</c> is the internal id of the Radiation synergy (renamed player-facing in the signatures pass; ids are kept so
    /// saves and content keep working). Display names: <see cref="SkillText.FamilyName"/>.
    /// </remarks>
    public enum SkillFamily { None, Fire, Ice, Lightning, Poison, Bleed, AlienSynergy, Counter, Combo, Crit }

    /// <summary>Where a skill comes from. Same format for all sources.</summary>
    public enum SkillSource { Run, Laboratory, Hero, Mount, Alien, Building, Part }

    /// <summary>
    /// Battle statuses. <c>Burn</c> is legacy (Heat replaced it as Fire's status; the value stays so old saved battle logs load and
    /// have no content), <c>Poison</c> is shown as "Radiation" (SkillText.StatusName). New values are appended (saves store ints).
    /// </summary>
    public enum StatusId { None, Burn, Freeze, Poison, Bleed, Stun, Shock, Charm, Heat, Chill }

    public enum TriggerKind
    {
        Passive,          // only the stat package applies
        OnBattleStart, OnRoundStart, OnRoundEnd, OnTurnStart,
        OnAttack, OnHit, OnCrit, OnCombo, OnCounter, OnDodge, OnParry,
        OnDamaged, OnKill, OnLowHp, OnRageAttack, OnStatusApplied,
        OnRunStart, OnRoll, OnTileLand, OnTilePass, OnLevelUp, OnBossAwaken,
        OnBattleWon,
        /// <summary>A Heat-stacked fighter the owner heated went Supernova (context = the exploding fighter).</summary>
        OnSupernova,
        /// <summary>The owner is a sidekick and it is its turn to act (its charge is full, or an extra action): its single ability.</summary>
        OnAct,
        /// <summary>One of the owner's sidekicks just acted (fires on the side's worm and mount; context = the front enemy).</summary>
        OnSidekickAct,
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
        Summon,            // enemy skills: Param = enemy def id (bosses calling their guard); player side: Param = sidekick def id
                           // (ContentDb.Sidekicks), Value = its ability power as % of the summoner's ATK, Stacks = how many (battle-scoped)
        SkipNegativeEvent, // Value = number of negative event outcomes skipped this run (city boon buildings)
        RemoveStatus,      // Status, Stacks (0 = all): remove stacks from the target (cooling removes Heat)
        ConsumeStatus,     // Status: remove all its stacks from the target; Value = % ATK damage per stack; Param "heal:N" heals N% of it
        Strike,            // the owner attacks the context target again (Param "counter" = a counterattack, else a combo strike)
        AllyAction,        // Stacks = extra sidekick actions (round-robin over the owner's sidekicks): each activates its ability
        Block,             // Stacks = hits on the target (the worm) the owner (a sidekick) intercepts; guards stack up to Combat.MaxGuards
    }

    /// <summary>OtherEnemies: up to Stacks random enemies other than the context target (Arc's jumps).</summary>
    public enum TargetKind { Self, Enemy, AllEnemies, RandomEnemy, LowestHpEnemy, Worm, AllAllies, OtherEnemies }

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
        /// <summary>
        /// Signature skill (one per synergy): never drafted, bought or levelled by drafts; the run starts with one (picked from
        /// 3) and it awakens at 3 / 6 owned skills of its synergy (SignatureSystem).
        /// </summary>
        public bool Signature;
    }
}
