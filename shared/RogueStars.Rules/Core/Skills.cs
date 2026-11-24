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
    /// <remarks>Time (skill redesign, R13): the Laboratory package after Alien (Echo signature); appended, saves store ints.</remarks>
    public enum SkillFamily { None, Fire, Ice, Lightning, Poison, Bleed, AlienSynergy, Counter, Combo, Crit, Time }

    /// <summary>Where a skill comes from. Same format for all sources.</summary>
    public enum SkillSource { Run, Laboratory, Hero, Mount, Alien, Building, Part }

    /// <summary>
    /// Battle statuses. <c>Burn</c> is legacy (Heat replaced it as Fire's status; the value stays so old saved battle logs load and
    /// have no content), <c>Poison</c> is shown as "Radiation" (SkillText.StatusName). New values are appended (saves store ints).
    /// </summary>
    /// <remarks>
    /// Skill redesign: <c>Exposed</c> (damage taken +1% per stack, permanent; Critical Mass), <c>Stagger</c> (a boss's next rage
    /// attack is lost; Thermal Shock on bosses) and <c>Delay</c> (the fighter loses its next turn, no re-apply cooldown; Time).
    /// </remarks>
    public enum StatusId { None, Burn, Freeze, Poison, Bleed, Stun, Shock, Charm, Heat, Chill, Exposed, Stagger, Delay }

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
        // ---- skill redesign (R8): appended, saves store ints ----
        /// <summary>Any fighter of the other side died (context = the dead fighter; its statuses are still readable).</summary>
        OnEnemyDeath,
        /// <summary>One of the owner's Arc jumps (Damage to OtherEnemies) struck (context = the struck enemy).</summary>
        OnArcJump,
        /// <summary>The owner's shield absorbed (part of) an attack (context = the attacker, amount = damage absorbed).</summary>
        OnShieldHit,
        /// <summary>A hit would kill the owner (context = the attacker): CheatDeath / Sacrifice / Loop effects can save it.</summary>
        OnFatal,
        /// <summary>One of the owner's sidekicks intercepted a hit on it (context = the attacker).</summary>
        OnBlock,
        /// <summary>A status the owner applied reached a stack threshold on a fighter (Param "Status:N"; context = that fighter).</summary>
        OnStackThreshold,
        /// <summary>The owner consumed stacks of a status (Param = status filter; context = the fighter, amount = stacks consumed).</summary>
        OnStatusConsumed,
        /// <summary>End of the owner's own turn (after its attacks).</summary>
        OnTurnEnd,
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
        // ---- skill redesign (R2, R6, R7, R13, R14): appended, saves store ints ----
        BlockStatus,       // Status, Duration: the target can't gain the status for Duration rounds (per-target StatusCd)
        Rule,              // Passive rule (Param = rule name[:arg], Status = the status it changes or None, Value = its growing number)
        ForceSupernova,    // the target's Heat blows now (Value = +% damage)
        MultiplyStatus,    // Status: stacks × Value/100 (Param "cap:N" = at most N stacks added)
        TransferStatus,    // Status: Value % of the context (dead) fighter's stacks go to every other enemy
        TickStatus,        // Status: tick now at Value % of a normal tick (once per target per round)
        Execute,           // a non-boss target below Value % HP dies (0 = any HP)
        Reflect,           // Value % of the trigger's amount (damage taken / absorbed) as damage to the target(s)
        CheatDeath,        // OnFatal: the owner survives at 1 HP
        FreeRageAttack,    // the owner makes a rage attack without spending rage
        Charge,            // the owner's next counterattack deals +Value % (stacks)
        LoseHp,            // the owner loses Value % of its max HP (never below 1 HP)
        StackAll,          // the target gains Stacks of each multi-stack status it has (once per target per round)
        Sacrifice,         // OnFatal: a sidekick leaves the battle in the owner's place; the owner heals Value % max HP
        Echo,              // repeat the owner's last (or strongest) attack Stacks times
        Rewind,            // undo the damage of the owner's last Stacks hits taken
        Loop,              // OnFatal: rewind every fighter to the start of the round before last
        StartRage,         // board (OnTileLand tile:Enemy): the next battle starts with Value rage
    }

    /// <summary>
    /// Which number of a trigger grows with the skill's level (SkillLeveling). Auto: the default rule (count effects grow their
    /// chance then their count, value effects their value); Chance: the proc chance (Ice Shard); Value: the value; Fixed: never
    /// (a part of a skill that stays put while another trigger grows, such as Frost Armor's chill).
    /// </summary>
    public enum GrowKind { Auto, Chance, Value, Fixed }

    /// <summary>OtherEnemies: up to Stacks random enemies other than the context target (Arc's jumps).</summary>
    /// <remarks>AllOtherEnemies: every enemy other than the context target (Cold Snap, Fission).</remarks>
    public enum TargetKind { Self, Enemy, AllEnemies, RandomEnemy, LowestHpEnemy, Worm, AllAllies, OtherEnemies, AllOtherEnemies }

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
        /// <summary>
        /// Gate (R1): the trigger only fires while this condition holds, e.g. "has:Heat:6", "max:Bleed &amp; not_boss",
        /// "self_hp_below:30" (BattleConditions; null = always).
        /// </summary>
        public string If;
        /// <summary>
        /// Counter (R4): fires on every Nth unit only (0 = every time). Units: stacks applied for OnStatusApplied, otherwise one per
        /// event (hits, Arc jumps, combo strikes, rounds, turns). Counted per fighter × trigger, before the chance roll.
        /// </summary>
        public int EveryN;
        /// <summary>At most this many times per battle (0 = no limit): "once per battle".</summary>
        public int MaxPerBattle;
        /// <summary>Which number grows with the skill's level (default: the SkillLeveling rule).</summary>
        public GrowKind Grow;
        /// <summary>
        /// Linear level growth (CC pass, owner-set ladders such as Knockout's 10% → 15% → 20% stun): when &gt; 0 the growing number
        /// (the chance of a count effect / Grow = Chance, else the value) rises by Step per level instead of the ladder factor.
        /// </summary>
        public double Step;
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
        /// <summary>
        /// Offer gate (skill redesign): the skill is only offered once the run owns a source for it: "arc" (an Arc jump), "freeze",
        /// "aliens" (a sidekick), "blocks" (a blocking sidekick or Bodyguard), "echo" (an Echo). Null = always (RunDraft.HasSource).
        /// </summary>
        public string Needs;
        /// <summary>
        /// Card text template (optional): lines separated by '|', a line starting with '!' is a cost (shown in the warning colour).
        /// Placeholders take the leveled numbers: {vN} trigger N's value, {cN} chance, {sN} stacks, {dN} duration, {bN} its buff
        /// stat, {pN} passive stat N. Without a template the text is generated from the triggers (SkillText).
        /// </summary>
        public string Text;
    }
}
