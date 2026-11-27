using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Role of a combatant. Turn order within a side: Mount → Aliens → Summons → Worm → Enemies/Elites/Boss. Aliens and player-side
    /// summons (drones) are <b>sidekicks</b>: never targeted, they charge by Speed and act with their single ability (BattleSim).
    /// </summary>
    public enum CombatRole { Worm, Alien, Mount, Summon, Enemy, Elite, Boss }

    public enum BattleSide { Player = 0, Enemy = 1 }

    public enum BattleEndReason { EnemiesDefeated, PlayerDefeated, RoundCap }

    /// <summary>What a log entry shows. Amount is damage, heal, shield, rage or stacks depending on the action.</summary>
    public enum BattleAction
    {
        Basic, Rage, Combo, Counter, Skill, StatusTick, StatusApplied, StatusExpired, StatusResisted,
        Heal, Shield, GainRage, Buff, Summon, SkipTurn, Death, Regen, LifeSteal,
        /// <summary>Heat burst: a marker (Amount 0, Stacks = the Heat that blew, Actor = Target = the exploding fighter), then one entry per hit (Amount = damage; the worm's recoil too).</summary>
        Supernova,
        /// <summary>Stacks removed (cooling, consumption): Stacks = stacks left, Amount = stacks removed.</summary>
        StatusRemoved,
        /// <summary>A sidekick's turn: marker before its ability (Actor = Target = the sidekick, SkillId = the ability, Stacks = 1 for an
        /// extra action such as Command's, 0 for its own charged turn). The ability's effects follow as their own entries.</summary>
        SidekickAct,
        /// <summary>A blocking sidekick raised a guard (Actor = sidekick, Target = the guarded worm, Stacks = guards up).</summary>
        Guard,
        /// <summary>A guard intercepted a hit (Actor = the blocking sidekick, Target = the guarded worm, Amount = the damage stopped, Stacks = guards left).</summary>
        Block,
        // ---- skill redesign (appended; saved logs store ints) ----
        /// <summary>The target can't gain Status for Stacks rounds (Thermal Shock's Heat lock).</summary>
        StatusBlocked,
        /// <summary>An execute (Cull, Exsanguinate): Amount = the HP it took.</summary>
        Execute,
        /// <summary>A fatal hit left the actor at 1 HP (Last Word).</summary>
        CheatDeath,
        /// <summary>An Echo repeats the actor's attack on the target (the repeat follows as its own entry).</summary>
        Echo,
        /// <summary>Loop: rewound to the start of round Amount. First the looping fighter, then one entry per other fighter
        /// (Target = it, TargetHp / TargetShield / TargetRage = restored, Stacks 1 = alive again / still alive, 0 = not there).</summary>
        Loop,
        /// <summary>A staggered fighter lost its rage attack.</summary>
        Staggered,
        /// <summary>A sidekick (Actor) left the battle in the worm's (Target) place (Sacrifice Protocol).</summary>
        Sacrifice,
        // ---- aliens pass (appended) ----
        /// <summary>A guard softened a hit on the worm (Actor = the guarding sidekick, Target = the worm, Amount = damage prevented,
        /// Stacks = guarded hits left). The hit itself follows and lands (on-hit triggers and counters fire).</summary>
        Guarded,
        // ---- hero & mount kits (appended) ----
        /// <summary>The mount fires its active (Actor = the mount, Target = its mark / the front enemy, SkillId = the active, Stacks = its
        /// reload in rounds, Amount 1 = an extra shot fired now: Short Circuit, Whistle). The shot's effects follow as their own entries.</summary>
        Volley,
        /// <summary>The worm's hero Ability fires (Actor = Target = the worm, SkillId = the ability skill). Its effects follow.</summary>
        Ability,
        /// <summary>A kit counter changed (Actor = Target = its owner, SkillId = the skill that owns it, Amount = the value, Stacks = the
        /// max): "Starfall 2/4" (shield hits), Stillness 0–3, the rage bank 0–100, a timer's rounds / turns left ("Feast in 2"; the
        /// skill's ability_every / ability_turns rule says it is a timer), the Aegis Walker's coil, the Star Lancer's charge. Round 0
        /// holds each hero's starting value.</summary>
        KitCounter,
        /// <summary>The mount marked an enemy (Actor = the mount, Target = the marked enemy; Scout Saucer's Spotlight).</summary>
        Mark,
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
        /// <summary>A sidekick's effect traits (aliens pass: Twin, Wide, Lingering, Bulwark, Eager; trait ids).</summary>
        public List<string> Traits = new List<string>();
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
        /// <summary>Rage at battle start (Ambush Instinct: landing on an enemy tile).</summary>
        public double StartRage;
        /// <summary>Statuses every enemy starts the battle with, applied by this fighter (Plague: status name → stacks); null = none.</summary>
        public Dictionary<string, int> OpeningStatuses;
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
        /// <summary>Legacy (parry was removed in the skill redesign): old saved logs may still carry it.</summary>
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
        /// <summary>Run coins the player side earned in the battle (Void Pirate's Plunder); the run adds them.</summary>
        public long RunCoins;
        /// <summary>Plague: stacks the player side added to enemies, per status name (only while its worm owns Plague; else null).</summary>
        public Dictionary<string, int> StacksInflicted;
        /// <summary>Heat bursts (Supernova markers) in the battle: counted from the log so a summary without the log keeps it.</summary>
        public int Novas;

        public bool PlayerWon() => Winner == BattleSide.Player;

        /// <summary>
        /// The result without its log: everything the rules read after a battle (winner, rounds, fighters' end HP, worm HP,
        /// damage, run coins, Plague stacks). This is what a run keeps in its state (ActiveRun.LastBattle) and what a
        /// local-first client claims to the server (BattleClaim); the log only drives the client's playback.
        /// </summary>
        public BattleResult Summary()
        {
            var s = new BattleResult
            {
                Winner = Winner, EndReason = EndReason, Rounds = Rounds, LogTruncated = false,
                PlayerDamageDealt = PlayerDamageDealt, PlayerDamageTaken = PlayerDamageTaken,
                WormHpLeft = WormHpLeft, WormMaxHp = WormMaxHp, RunCoins = RunCoins, Novas = Novas,
                StacksInflicted = StacksInflicted == null ? null : new Dictionary<string, int>(StacksInflicted),
            };
            foreach (var f in Fighters)
                s.Fighters.Add(new BattleFighterInfo
                {
                    Index = f.Index, Id = f.Id, Name = f.Name, Role = f.Role, Side = f.Side, Targetable = f.Targetable, MaxHp = f.MaxHp,
                    StartHp = f.StartHp, StartShield = f.StartShield, StartRage = f.StartRage, EndHp = f.EndHp, EndShield = f.EndShield,
                    EndRage = f.EndRage, Alive = f.Alive, DamageDealt = f.DamageDealt, SummonedBy = f.SummonedBy,
                });
            return s;
        }
    }

    /// <summary>
    /// How the server samples a run battle for re-simulation (local-first battles): standard fights, elites (elite tiles,
    /// minibosses, ambushes) and bosses each have their own rate (server config Battles:*SampleRate).
    /// </summary>
    public enum BattleTier { Standard, Elite, Boss }

    /// <summary>One battle fought inside a command: its tier and full result (log included).</summary>
    public sealed class FoughtBattle
    {
        /// <summary>The run's battle kind (enemy, elite, miniboss, ambush, boss, event, quest).</summary>
        public string Kind;
        public BattleTier Tier;
        /// <summary>Full result, log included (the client plays it back; never persisted).</summary>
        public BattleResult Result;
        /// <summary>True when the result came from a client claim the server did not re-simulate.</summary>
        public bool Claimed;

        public static BattleTier TierOf(string kind) =>
            kind == "boss" ? BattleTier.Boss : (kind == "elite" || kind == "miniboss" || kind == "ambush") ? BattleTier.Elite : BattleTier.Standard;
    }

    /// <summary>
    /// Resolves a battle inside a command. The default (null resolver) simulates it. The server installs its own to use a
    /// local-first client's claimed result and re-simulate only a sample (server BattleAudit). <paramref name="index"/> is the
    /// battle's position within the command (0, 1, …), which matches the client's claims.
    /// </summary>
    public delegate BattleResult BattleResolver(CommandContext ctx, BattleSetup setup, Rng rng, string kind, int index);
}
