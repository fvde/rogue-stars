using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    // ------------------------------------------------------------------------------------------
    // Parts v2 (docs/design/parts.md, docs/design/parts-v2/ "engine needs"): extra turns, sure crits, forced misses, next-hit riders,
    // relative rule modifiers with floors / ceilings (Supernova threshold ≥ 6, rage cost ≥ 50, sidekick action ≥ 50 charge, dodge ≤ 85%),
    // once-per-enemy triggers, shield-break / Echo / undo / status-tick triggers, and the shared blow-up caps (1 fully avoided hit per
    // round, 2 extra turns per round, 2 free rage attacks per turn, 3 ticks per enemy per round, Echo depth 2, 5 Drones).
    // ------------------------------------------------------------------------------------------

    internal sealed partial class StatusRules
    {
        /// <summary>Supernova comes this many Heat sooner (relative, all sources add up; floor Combat.SupernovaFloor).</summary>
        public int BurstDelta;
        /// <summary>The status never wears off while its applier is shielded (Rimeguard Plate).</summary>
        public bool NoExpireShielded;
        /// <summary>The status ticks twice on a carrier at N+ stacks (0 = at its cap; -1 = never): Bloodtide Fin.</summary>
        public int DotTwiceAt = -1;
        /// <summary>Stacks of this status spread or copied to other enemies × (1 + SpreadPct / 100): Isotope Visor.</summary>
        public double SpreadPct;
    }

    internal sealed partial class RuleSet
    {
        public bool FirstHitCrit, RageTargetLowest, GuardFull, CounterAoe, CounterAoeShielded, ArcCrit, DodgeNova, ComboKillCarry, RageCounter,
            ArcVolleyFirst, ComboTargetLowest;
        public int NthHitCrit, NthMiss, ActFirstRound, SidekickTwice, DroneStatus, DroneGuardHits, ArcVolleyEvery, VolleyStunAt, ComboFullEvery;
        public double CritDefIgnorePct, HitCapPct, OverhealCapPct, RageCostDelta, RageCostLow, RageCostLowAt, SidekickActDelta, CritOverflow,
            DodgeAsComboPct, RageAoePct, ComboKillExtra, ComboLowestBonus, RageCounterPct, DodgeNovaBonus, UndoReflectPct, GuardReflectPct,
            LowHpAoeAt, LowHpAoePct, FirstHitCounterPct, ArcCritBonus, DroneGuardPct, ArcVolleyPct, OverkillHealPct, SlowerTakenPct, FirstTakenPct;
        // status-keyed owner rules
        public StatusId DodgeCapVs = StatusId.None, DodgePerStackStatus = StatusId.None, TakenVs = StatusId.None, ExecuteStatus = StatusId.None,
            RageAreaVs = StatusId.None;
        public double DodgeCapVsPct, DodgePerStack, TakenVsPct, ExecuteBelowPct;
        public int TakenVsAt, ExecuteAt;
        public string VolleySkill;

        /// <summary>Parts v2 owner rules (Passive Rule triggers without a status).</summary>
        public void AddPartRule(string name, double arg, double value, string skillId)
        {
            switch (name)
            {
                case "first_hit_crit": FirstHitCrit = true; break;
                case "nth_hit_crit": NthHitCrit = NthHitCrit > 0 ? Math.Min(NthHitCrit, (int)arg) : (int)arg; break;
                case "crit_def_ignore": CritDefIgnorePct = Math.Max(CritDefIgnorePct, value); break;
                case "hit_cap": HitCapPct = HitCapPct > 0 ? Math.Min(HitCapPct, value) : value; break;
                case "overheal_cap": OverhealCapPct += value; break; // caps from all sources add up (rev 3e)
                case "rage_cost": RageCostDelta += value; break;
                case "rage_cost_low": RageCostLow += value; RageCostLowAt = Math.Max(RageCostLowAt, arg); break;
                case "act_first": ActFirstRound = ActFirstRound > 0 ? Math.Min(ActFirstRound, (int)arg) : (int)arg; break;
                case "rage_target_lowest": RageTargetLowest = true; break;
                case "sidekick_act": SidekickActDelta += value; break;
                case "crit_overflow": CritOverflow = Math.Max(CritOverflow, value); break;
                case "dodge_as_combo": DodgeAsComboPct = Math.Max(DodgeAsComboPct, value); break;
                case "rage_aoe": RageAoePct = Math.Max(RageAoePct, value); break; // area attacks use the highest %
                case "combo_kill_carry": ComboKillCarry = true; ComboKillExtra = Math.Max(ComboKillExtra, value); break;
                case "combo_target_lowest": ComboTargetLowest = true; ComboLowestBonus = Math.Max(ComboLowestBonus, value); break;
                case "combo_full_every": ComboFullEvery = ComboFullEvery > 0 ? Math.Min(ComboFullEvery, (int)arg) : (int)arg; break;
                case "rage_counter": RageCounter = true; RageCounterPct = Math.Max(RageCounterPct, value); break;
                case "dodge_nova": DodgeNova = true; DodgeNovaBonus = Math.Max(DodgeNovaBonus, value); break;
                case "undo_reflect": UndoReflectPct += value; break;
                case "guard_full": GuardFull = true; break;
                case "guard_reflect": GuardReflectPct += value; break;
                case "low_hp_aoe": if (arg > LowHpAoeAt) LowHpAoeAt = arg; LowHpAoePct = Math.Max(LowHpAoePct, value); break;
                case "first_hit_counter": FirstHitCounterPct += value; break;
                case "arc_crit": ArcCrit = true; ArcCritBonus += value; break;
                case "sidekick_twice": SidekickTwice = Math.Max(SidekickTwice, (int)arg); break;
                case "drone_status": DroneStatus = Math.Max(DroneStatus, (int)arg); break;
                case "drone_guard": DroneGuardPct = Math.Max(DroneGuardPct, value); DroneGuardHits = Math.Max(DroneGuardHits, Math.Max(1, (int)arg)); break;
                case "arc_volley": ArcVolleyEvery = ArcVolleyEvery > 0 ? Math.Min(ArcVolleyEvery, (int)arg) : (int)arg; ArcVolleyPct = Math.Max(ArcVolleyPct, value); VolleySkill = VolleySkill ?? skillId; break;
                case "arc_volley_first": ArcVolleyFirst = true; break;
                case "volley_stun": VolleyStunAt = VolleyStunAt > 0 ? Math.Min(VolleyStunAt, (int)arg) : (int)arg; break;
                case "overkill_heal": OverkillHealPct += value; break;
                case "nth_miss": NthMiss = NthMiss > 0 ? Math.Min(NthMiss, (int)arg) : (int)arg; break;
                case "counter_aoe": CounterAoe = true; break;
                case "counter_aoe_shielded": CounterAoeShielded = true; break;
                case "slower_taken": SlowerTakenPct += value; break;
                case "first_taken": FirstTakenPct += value; break;
            }
        }

        /// <summary>Parts v2 status-keyed rules (a Rule trigger with a Status).</summary>
        public void AddPartStatusRule(StatusId st, string name, double arg, double value)
        {
            switch (name)
            {
                case "burst_delta": Edit(st).BurstDelta += (int)Math.Round(value); break;
                case "no_expire_shielded": Edit(st).NoExpireShielded = true; break;
                case "dot_twice_at":
                {
                    var r = Edit(st);
                    int at = (int)arg;
                    r.DotTwiceAt = r.DotTwiceAt < 0 ? at : (at == 0 || r.DotTwiceAt == 0 ? (at == 0 ? r.DotTwiceAt : at) : Math.Min(r.DotTwiceAt, at));
                    break;
                }
                case "spread_mult": Edit(st).SpreadPct += value; break;
                case "dodge_cap_vs": DodgeCapVs = st; DodgeCapVsPct += value; break;
                case "dodge_per_stack": DodgePerStackStatus = st; DodgePerStack += value; break;
                case "taken_vs":
                    TakenVs = st; TakenVsPct = Math.Max(TakenVsPct, value);
                    TakenVsAt = TakenVsAt > 0 ? Math.Min(TakenVsAt, (int)arg) : (int)arg;
                    break;
                case "execute_at":
                    // executes never add up: the highest HP threshold applies (rev 3e)
                    ExecuteStatus = st; ExecuteBelowPct = Math.Max(ExecuteBelowPct, value);
                    ExecuteAt = ExecuteAt > 0 ? Math.Min(ExecuteAt, (int)arg) : (int)arg;
                    break;
                case "rage_area_vs":
                    // "on a chilled enemy" (any Chill) is the looser gate than "on a frozen enemy"
                    if (RageAreaVs == StatusId.None || st == StatusId.Chill) RageAreaVs = st;
                    break;
            }
        }
    }

    internal sealed partial class BuffInst
    {
        /// <summary>Ends when its holder crits / dodges / finishes its next attack (parts v2).</summary>
        public bool EndOnCrit, EndOnDodge, EndAfterAttack;
        /// <summary>until_turn:N: ends when the holder's N-th next turn starts.</summary>
        public int TurnsLeft;
    }

    internal sealed partial class Fighter
    {
        // ---- parts v2 ----
        public int SureCrits, SureCritTurn = -1, CritRolls, ForcedMisses, AttacksOnMe, AvoidRound = -1, AvoidCount;
        public readonly HashSet<int> CritFirstHit = new HashSet<int>();
        public double NextHitBonus, NextRageBonus, NextDefIgnore, RageCharge;
        public int AoeTurn = -1, CounterWindowUntil = -1, ForcedStrikes, RageDepth, EchoDepth;
        public bool ForceCounterHit, RepeatPending, CritThisTurn;
        public double RepeatPct;
        public int PendingExtraTurns, ExtraRound = -1, ExtraCount;
        public bool InExtraTurn;
        public bool LastTakenCrit;
        public AttackKind LastTakenKind, CurKind;
        public double LastTakenDealt, OverhealShield, LastConsumeDealt;
        public int FreeRageKey = -1, FreeRageCount;
        public readonly Dictionary<SkillTrigger, Dictionary<int, int>> PerTarget = new Dictionary<SkillTrigger, Dictionary<int, int>>();
        public readonly Dictionary<SkillTrigger, Dictionary<int, int>> EveryTarget = new Dictionary<SkillTrigger, Dictionary<int, int>>();
        public readonly List<KeyValuePair<SkillDef, SkillTrigger>> Armed = new List<KeyValuePair<SkillDef, SkillTrigger>>();
        public int DroneGuardHits;
        public readonly int[] TickCount = new int[32], TickCountRound = new int[32];
        public int KickActRound = -1, KickActCount;
    }

    internal sealed partial class BattleSim
    {
        private BattleAction _killAction;
        private int _dodgeDepth;
        private bool _forcedMiss;
        private double _checkAmount;

        /// <summary>Floors and ceilings of the relative modifiers (owner, rev 3d).</summary>
        private const int SupernovaFloor = 6;
        private const double RageCostFloor = 50, SidekickActFloor = 50, DodgeCeiling = 85;
        private const int MaxExtraTurnsPerRound = 2, MaxFreeRagePerTurn = 2, MaxTicksPerRound = 3, MaxEchoDepth = 2, MaxDrones = 5, MaxAvoidedPerRound = 1;

        /// <summary>Rage a rage attack needs: RageMax minus the owner's relative cuts (Rage Turbine, Quantum Fury Shell below 50% HP), floor 50.</summary>
        private double RageCost(Fighter f)
        {
            var r = f.Rules;
            double cut = r.RageCostDelta + (r.RageCostLow > 0 && f.HpPct < r.RageCostLowAt ? r.RageCostLow : 0);
            return cut <= 0 ? T.RageMax : Math.Max(RageCostFloor, T.RageMax - cut);
        }

        /// <summary>Charge a sidekick needs to act: SidekickActAt minus its worm's cuts (Swarmcaller Antenna), floor 50.</summary>
        private double ActAt(Fighter kick)
        {
            var w = FirstWorm(kick.Side);
            double cut = w != null ? w.Rules.SidekickActDelta : 0;
            return cut <= 0 ? T.SidekickActAt : Math.Max(SidekickActFloor, T.SidekickActAt - cut);
        }

        /// <summary>Supernova threshold with the applier's rules: burst_at (Controlled Burn) minus every "Heat sooner", floor 6.</summary>
        private static int BurstWithDelta(int burst, StatusRules r) => burst <= 0 || r.BurstDelta <= 0 ? burst : Math.Max(Math.Min(burst, SupernovaFloor), burst - r.BurstDelta);

        /// <summary>One more fully avoided hit on <paramref name="f"/> this round (forced misses, full sidekick guards)? Records it.</summary>
        private bool AvoidOk(Fighter f)
        {
            if (f.AvoidRound != Round) { f.AvoidRound = Round; f.AvoidCount = 0; }
            if (f.AvoidCount >= MaxAvoidedPerRound) return false;
            f.AvoidCount++;
            return true;
        }

        /// <summary>A forced miss on the defender this attack (ForceMiss effects, every Nth attack misses)?</summary>
        private bool ForcedMiss(Fighter def)
        {
            if (def.Role != CombatRole.Worm) return false;
            def.AttacksOnMe++;
            bool want = def.ForcedMisses > 0 || (def.Rules.NthMiss > 0 && def.AttacksOnMe % def.Rules.NthMiss == 0);
            if (!want || !AvoidOk(def)) return false;
            if (def.ForcedMisses > 0) def.ForcedMisses--;
            return true;
        }

        /// <summary>Dodge chance of the defender against this attacker (Frostglide Skates: + per Chill stack on the attacker, a higher cap).</summary>
        private double DodgeChance(Fighter att, Fighter def)
        {
            double dodge = def.S(StatId.Dodge);
            var r = def.Rules;
            if (r.DodgePerStack > 0 && r.DodgePerStackStatus != StatusId.None)
            {
                var st = att.Status(r.DodgePerStackStatus);
                if (st != null) dodge += r.DodgePerStack * st.Stacks;
            }
            double max = T.MaxDodge;
            if (r.DodgeCapVsPct > 0 && r.DodgeCapVs != StatusId.None && att.Status(r.DodgeCapVs) != null) max = Math.Min(DodgeCeiling, max + r.DodgeCapVsPct);
            double d = CombatMath.Clamp(CombatMath.SoftCap(dodge - att.S(StatId.DodgeIgnore), T.DodgeKnee), 0, max) / 100.0;
            return d * CombatMath.PowInt(T.DodgeDecay, def.ConsecutiveDodges);
        }

        /// <summary>Damage the defender's parts take off this attacker's hits (slower attackers, irradiated attackers, the first hit each round).</summary>
        private double DefendMultiplier(Fighter att, Fighter def)
        {
            if (att == null || def == null || att.Side == def.Side) return 1;
            var r = def.Rules;
            double sum = 0;
            if (r.SlowerTakenPct > 0 && att.S(StatId.Speed) < def.S(StatId.Speed)) sum += r.SlowerTakenPct;
            if (r.FirstTakenPct > 0 && (def.HitsTakenRoundNo != Round || def.HitsTakenRound == 0)) sum += r.FirstTakenPct;
            if (r.TakenVsPct > 0 && r.TakenVs != StatusId.None) { var st = att.Status(r.TakenVs); if (st != null && st.Stacks >= Math.Max(1, r.TakenVsAt)) sum += r.TakenVsPct; }
            return sum <= 0 ? 1 : Math.Max(0, 1 - sum / 100.0);
        }

        /// <summary>Sure crits on an attack (Keen parts, Deadeye Visor): consumed once the attack rolls.</summary>
        private bool SureCrit(Fighter att, Fighter def)
        {
            if (att.Role != CombatRole.Worm) return false;
            bool sure = false;
            if (att.SureCritTurn == att.Turns && _acting == att) sure = true;
            if (att.SureCrits > 0) { att.SureCrits--; sure = true; }
            var r = att.Rules;
            if (r.NthHitCrit > 0) { att.CritRolls++; if (att.CritRolls % r.NthHitCrit == 0) sure = true; }
            if (r.FirstHitCrit && def != null && !att.CritFirstHit.Contains(def.Index)) sure = true;
            return sure;
        }

        /// <summary>Marks the first landed hit on each enemy (Plasma Keen Helm's sure crit is spent).</summary>
        private static void MarkFirstHit(Fighter att, Fighter def)
        {
            if (att.Rules.FirstHitCrit) att.CritFirstHit.Add(def.Index);
        }

        /// <summary>Buffs that end on the holder's crit / dodge / attack.</summary>
        private void EndBuffs(Fighter f, bool crit, bool dodge, bool attack)
        {
            for (int i = f.Buffs.Count - 1; i >= 0; i--)
            {
                var b = f.Buffs[i];
                if ((crit && b.EndOnCrit) || (dodge && b.EndOnDodge) || (attack && b.EndAfterAttack)) RemoveBuff(f, i);
            }
        }

        /// <summary>Basic attacks hit every enemy this turn (Quantum Strike Tail's rhythm, Last Stand Plate below 30% HP); the highest % applies.</summary>
        private double TurnAoePct(Fighter f)
        {
            double pct = f.Rules.BasicAoePct;
            if (f.AoeTurn == f.Turns && _acting == f) pct = Math.Max(pct, 100);
            if (f.Rules.LowHpAoePct > 0 && f.HpPct < f.Rules.LowHpAoeAt) pct = Math.Max(pct, f.Rules.LowHpAoePct);
            return pct;
        }

        /// <summary>A rage attack on <paramref name="target"/> (the turn's, a free one, a counter): its charge, its triggers, Rage Turbine's area.</summary>
        private double RageAttack(Fighter f, Fighter target, double mult = 1, bool asCounter = false)
        {
            if (target == null || !f.Alive || Over) return 0;
            f.RageDepth++;
            double dealt;
            try
            {
                var area = f.Rules.RageAreaVs != StatusId.None && target.Status(f.Rules.RageAreaVs) != null;
                double bonus = f.NextRageBonus + f.RageCharge;
                f.NextRageBonus = 0;
                if (f.RageCharge > 0) { f.RageCharge = 0; }
                dealt = Attack(f, target, AttackKind.Rage, asCounter ? 1 : 0, mult * (1 + bonus / 100.0));
                Fire(f, TriggerKind.OnRageAttack, target, StatusId.None, dealt);
                if (asCounter && !Over && f.Alive) Fire(f, TriggerKind.OnCounter, target, StatusId.None, dealt);
                // Rage Turbine: the rage attack also hits every other enemy (the highest % of all sources)
                if (f.Rules.RageAoePct > 0 && !Over && f.Alive)
                    foreach (var o in new List<Fighter>(F))
                    {
                        if (Over || !f.Alive) break;
                        if (o == target || o.Side == f.Side || !o.Alive || !o.Targetable) continue;
                        Attack(f, o, AttackKind.Rage, 1, mult * f.Rules.RageAoePct / 100.0);
                    }
                // Shatter Cannon: a rage attack on a frozen (awakened: chilled) enemy also hits every chilled enemy
                if (area && dealt > 0 && !Over && f.Alive)
                    foreach (var o in new List<Fighter>(F))
                    {
                        if (Over || !f.Alive) break;
                        if (o == target || o.Side == f.Side || !o.Alive || !o.Targetable || o.Status(StatusId.Chill) == null) continue;
                        double d = dealt * CombatMath.DamageReductionFactor(o.S(StatId.DamageReduction), T) * TakenMultiplier(o);
                        DealDamage(f, o, d, false, BattleAction.Rage, false, StatusId.None, null);
                    }
            }
            finally { f.RageDepth--; }
            return dealt;
        }

        /// <summary>Free rage attacks (Rage Turbine, Backlash Hammer, Firebrand Blaster …): at most 2 per turn from every source.</summary>
        private bool FreeRageOk(Fighter f)
        {
            int key = Round * 1000 + (_acting != null ? _acting.Index : 999);
            if (f.FreeRageKey != key) { f.FreeRageKey = key; f.FreeRageCount = 0; }
            if (f.FreeRageCount >= MaxFreeRagePerTurn) return false;
            f.FreeRageCount++;
            return true;
        }

        /// <summary>Queues extra turns; they are taken after the current turn (at most 2 per round, the rest move to the next round).</summary>
        private void GrantExtraTurns(Fighter f, int n, SkillDef skill)
        {
            if (f.InExtraTurn || n <= 0 || f.Sidekick || f.Role == CombatRole.Mount) return; // an extra turn never grants another
            f.PendingExtraTurns += n;
            var e = Log(BattleAction.Buff, f, f, n);
            if (e != null) e.SkillId = skill?.Id;
        }

        /// <summary>Takes the pending extra turns of every fighter (after any turn).</summary>
        private void RunExtraTurns()
        {
            for (int i = 0; i < F.Count && !Over; i++)
            {
                var f = F[i];
                while (!Over && f.Alive && f.PendingExtraTurns > 0)
                {
                    if (f.ExtraRound != Round) { f.ExtraRound = Round; f.ExtraCount = 0; }
                    if (f.ExtraCount >= MaxExtraTurnsPerRound) break; // the rest move on to the next round
                    f.ExtraCount++;
                    f.PendingExtraTurns--;
                    var prev = _acting;
                    _acting = f;
                    f.InExtraTurn = true;
                    try
                    {
                        EndTurnScopedEffects(f);
                        f.ComboThisTurn = false;
                        Act(f, false);
                        if (f.Alive && !Over) EndTurn(f);
                    }
                    finally { f.InExtraTurn = false; _acting = prev; }
                }
            }
        }

        /// <summary>Storm Coil's Arc volley: every Nth turn (and the first, awakened) Arc to every enemy; stuns enemies at N+ Shock once per enemy.</summary>
        private void ArcVolley(Fighter f)
        {
            var r = f.Rules;
            if (r.ArcVolleyEvery <= 0 || r.ArcVolleyPct <= 0 || f.InExtraTurn) return;
            if (f.Turns % r.ArcVolleyEvery != 0 && !(r.ArcVolleyFirst && f.Turns == 1)) return;
            var skill = f.Skills.Find(s => s.Id == r.VolleySkill);
            if (skill == null) return;
            var t = new SkillTrigger { When = TriggerKind.OnTurnStart, Effect = EffectKind.Damage, Target = TargetKind.OtherEnemies, Stacks = 99, Value = r.ArcVolleyPct };
            Execute(f, skill, t, null, 0);
            if (r.VolleyStunAt <= 0 || Over || !f.Alive) return;
            foreach (var o in new List<Fighter>(F))
            {
                if (Over) break;
                if (o.Side == f.Side || !o.Alive || !o.Targetable) continue;
                var st = o.Status(StatusId.Shock);
                if (st == null || st.Stacks < r.VolleyStunAt || !TakePerTarget(f, VolleyStunKey, o, 1)) continue;
                if (o.Role == CombatRole.Boss) { AddRage(o, -20, true); var be = Log(BattleAction.GainRage, f, o, -20); if (be != null) be.SkillId = skill.Id; continue; }
                ApplyStatus(f, o, StatusId.Stun, 1, 1, skill.Id);
            }
        }

        private static readonly SkillTrigger VolleyStunKey = new SkillTrigger();

        /// <summary>Once per enemy (per_target:N): true while this trigger has fired on that fighter fewer than N times; records it.</summary>
        private static bool TakePerTarget(Fighter owner, SkillTrigger t, Fighter tg, int max)
        {
            if (tg == null) return true;
            if (!owner.PerTarget.TryGetValue(t, out var map)) owner.PerTarget[t] = map = new Dictionary<int, int>();
            map.TryGetValue(tg.Index, out int n);
            if (n >= max) return false;
            map[tg.Index] = n + 1;
            return true;
        }

        /// <summary>every_target:N: counts this trigger's events per context fighter; true on every Nth.</summary>
        private static bool EveryTarget(Fighter owner, SkillTrigger t, Fighter ctx, int every)
        {
            if (ctx == null) return false;
            if (!owner.EveryTarget.TryGetValue(t, out var map)) owner.EveryTarget[t] = map = new Dictionary<int, int>();
            map.TryGetValue(ctx.Index, out int n);
            map[ctx.Index] = ++n;
            return n % every == 0;
        }

        /// <summary>A status tick may happen on this fighter this round (its own, Time Dilation, "ticks twice", every "tick now": at most 3).</summary>
        private bool TickOk(Fighter f, StatusId id)
        {
            int i = (int)id;
            if (f.TickCountRound[i] != Round) { f.TickCountRound[i] = Round; f.TickCount[i] = 0; }
            if (f.TickCount[i] >= MaxTicksPerRound) return false;
            f.TickCount[i]++;
            return true;
        }

        /// <summary>Living targetable enemies of <paramref name="owner"/> in line order.</summary>
        private List<Fighter> EnemyLine(Fighter owner)
        {
            var line = new List<Fighter>();
            foreach (var o in F) if (o.Side != owner.Side && o.Alive && o.Targetable) line.Add(o);
            return line;
        }

        /// <summary>
        /// Target picks (parts v2 Param tokens): "pick_most:Status" (the enemy with the most stacks), "pick_least:Status[:n]" (the n
        /// enemies with the fewest; ties front first), "pick_has:Status:N" (the first enemy with N+ stacks), "pick_front:n" (the first n
        /// enemies), "behind[:n]" (the n enemies behind the context / front enemy). Null when the Param has none.
        /// </summary>
        private List<Fighter> PickTargets(Fighter owner, string param, Fighter ctx)
        {
            if (string.IsNullOrEmpty(param) || param.IndexOf("pick_", StringComparison.Ordinal) < 0 && param.IndexOf("behind", StringComparison.Ordinal) < 0) return null;
            foreach (var raw in param.Split(';'))
            {
                var a = raw.Trim().Split(':');
                var line = EnemyLine(owner);
                StatusId st = StatusId.None;
                int Num(int i, int def) => a.Length > i && int.TryParse(a[i], out var v) ? v : def;
                int Stacks(Fighter f) { var s = f.Status(st); return s != null ? s.Stacks : 0; }
                switch (a[0])
                {
                    case "pick_most":
                    {
                        if (a.Length < 2 || !Enum.TryParse(a[1], out st)) continue;
                        Fighter best = null;
                        foreach (var o in line) if (Stacks(o) > 0 && (best == null || Stacks(o) > Stacks(best))) best = o;
                        return best == null ? new List<Fighter>() : new List<Fighter> { best };
                    }
                    case "pick_least":
                    {
                        if (a.Length < 2 || !Enum.TryParse(a[1], out st)) continue;
                        int n = Num(2, 1);
                        var sorted = new List<Fighter>(line);
                        // stable: fewest stacks first, the line order breaks ties
                        sorted.Sort((x, y) => { int c = Stacks(x).CompareTo(Stacks(y)); return c != 0 ? c : line.IndexOf(x).CompareTo(line.IndexOf(y)); });
                        return sorted.GetRange(0, Math.Min(n, sorted.Count));
                    }
                    case "pick_has":
                    {
                        if (a.Length < 2 || !Enum.TryParse(a[1], out st)) continue;
                        int n = Num(2, 1);
                        foreach (var o in line) if (o != ctx && Stacks(o) >= n) return new List<Fighter> { o };
                        return new List<Fighter>();
                    }
                    case "pick_front":
                        return line.GetRange(0, Math.Min(Num(1, 1), line.Count));
                    case "behind":
                    {
                        var basis = ctx != null && line.Contains(ctx) ? ctx : (line.Count > 0 ? line[0] : null);
                        int at = basis != null ? line.IndexOf(basis) : -1;
                        var list = new List<Fighter>();
                        for (int k = 1; k <= Num(1, 1) && at >= 0 && at + k < line.Count; k++) list.Add(line[at + k]);
                        return list;
                    }
                }
            }
            return null;
        }

        /// <summary>Target filters "tg_has:Status[:N]" / "tg_lacks:Status".</summary>
        private static void FilterTargets(List<Fighter> targets, string param)
        {
            if (string.IsNullOrEmpty(param) || param.IndexOf("tg_", StringComparison.Ordinal) < 0) return;
            foreach (var raw in param.Split(';'))
            {
                var a = raw.Trim().Split(':');
                if (a.Length < 2 || (a[0] != "tg_has" && a[0] != "tg_lacks") || !Enum.TryParse(a[1], out StatusId st)) continue;
                int n = a.Length > 2 && int.TryParse(a[2], out var v) ? v : 1;
                bool has = a[0] == "tg_has";
                targets.RemoveAll(tg => tg != null && ((tg.Status(st) != null && tg.Status(st).Stacks >= n) != has));
            }
        }

        /// <summary>
        /// Scaled amounts (parts v2): "per_round" (× the round), "per_enemy" (× living enemies), "per_status_enemy:Status" (× enemies with it),
        /// "per_total:Status:N" (× every N stacks on all enemies), "per_ctx_stack:Status" (× the context fighter's stacks), "per_stat:Stat:N"
        /// (× every N points of an owner stat), "half" (× ½ of the trigger amount: consumed stacks), then "max:N" caps the result.
        /// </summary>
        private double Scale(Fighter owner, Fighter ctx, string param, double v, double amount)
        {
            if (string.IsNullOrEmpty(param)) return v;
            double cap = 0;
            foreach (var raw in param.Split(';'))
            {
                var a = raw.Trim().Split(':');
                StatusId st;
                switch (a[0])
                {
                    case "per_round": v *= Round; break;
                    case "per_enemy": v *= EnemyLine(owner).Count; break;
                    case "per_status_enemy":
                    {
                        if (a.Length < 2 || !Enum.TryParse(a[1], out st)) break;
                        int n = 0;
                        foreach (var o in EnemyLine(owner)) if (o.Status(st) != null) n++;
                        v *= n;
                        break;
                    }
                    case "per_total":
                    {
                        if (a.Length < 3 || !Enum.TryParse(a[1], out st) || !int.TryParse(a[2], out int per) || per <= 0) break;
                        int total = 0;
                        foreach (var o in EnemyLine(owner)) { var s = o.Status(st); if (s != null) total += s.Stacks; }
                        v *= total / per;
                        break;
                    }
                    case "per_ctx_stack":
                    {
                        if (a.Length < 2 || !Enum.TryParse(a[1], out st)) break;
                        var s = ctx?.Status(st);
                        v *= s != null ? s.Stacks : 0;
                        break;
                    }
                    case "per_stat":
                    {
                        if (a.Length < 3 || !Enum.TryParse(a[1], out StatId stat) || !int.TryParse(a[2], out int per) || per <= 0) break;
                        v *= Math.Floor(Math.Max(0, owner.S(stat)) / per);
                        break;
                    }
                    case "half": v *= Math.Floor(Math.Max(0, amount) / 2); break;
                    case "max": if (a.Length > 1) double.TryParse(a[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cap); break;
                }
            }
            return cap > 0 ? Math.Min(cap, v) : v;
        }

        private static bool HasScale(string p) =>
            !string.IsNullOrEmpty(p) && (p.Contains("per_round") || p.Contains("per_enemy") || p.Contains("per_status_enemy") || p.Contains("per_total")
                || p.Contains("per_ctx_stack") || p.Contains("per_stat") || HasToken(p, "half"));

        /// <summary>Spread / copy of a status to other enemies with the applier's spread rule (Isotope Visor: doubled, tripled).</summary>
        private int SpreadStacks(Fighter owner, StatusId id, int n)
        {
            var rs = RulesOf(owner);
            double pct = rs != null ? rs.Of(id).SpreadPct : 0;
            return pct > 0 ? (int)Math.Round(n * (1 + pct / 100.0)) : n;
        }

        /// <summary>Parts v2 effects (ExtraTurn … MultiplyShield).</summary>
        private bool ExecutePart(Fighter owner, SkillDef skill, SkillTrigger t, Fighter ctx, double amount, List<Fighter> targets)
        {
            switch (t.Effect)
            {
                case EffectKind.ExtraTurn:
                    GrantExtraTurns(owner, Math.Max(1, t.Stacks), skill);
                    return true;
                case EffectKind.SureCrit:
                    if (HasToken(t.Param, "turn")) owner.SureCritTurn = owner.Turns + (_acting == owner ? 0 : 1);
                    else owner.SureCrits += Math.Max(1, t.Stacks);
                    Mark(owner, owner, skill);
                    return true;
                case EffectKind.ForceMiss:
                    owner.ForcedMisses += Math.Max(1, t.Stacks);
                    Mark(owner, owner, skill);
                    return true;
                case EffectKind.Empower:
                    if (HasToken(t.Param, "rage")) owner.NextRageBonus += t.Value;
                    else if (HasToken(t.Param, "def")) owner.NextDefIgnore = Math.Max(owner.NextDefIgnore, t.Value);
                    else owner.NextHitBonus += t.Value;
                    Mark(owner, owner, skill, t.Value);
                    return true;
                case EffectKind.TurnFlag:
                    if (HasToken(t.Param, "aoe")) owner.AoeTurn = owner.Turns + (_acting == owner ? 0 : 1);
                    if (HasToken(t.Param, "counter_hit")) owner.ForceCounterHit = true;
                    if (HasToken(t.Param, "counter_window"))
                    {
                        if (t.Duration > 0) owner.CounterWindowUntil = Math.Max(owner.CounterWindowUntil, Round + t.Duration);
                        else owner.CounterWindow = true;
                    }
                    Mark(owner, owner, skill);
                    return true;
                case EffectKind.ForceCombo:
                    owner.ForcedStrikes = Math.Max(owner.ForcedStrikes, Math.Max(1, t.Stacks));
                    Mark(owner, owner, skill, t.Stacks);
                    return true;
                case EffectKind.Repeat:
                    owner.RepeatPending = true;
                    owner.RepeatPct = t.Value > 0 ? t.Value : 100;
                    return true;
                case EffectKind.MultiplyShield:
                {
                    if (owner.Shield <= 0 || !owner.Alive) return true;
                    double add = owner.Shield * (t.Value / 100.0 - 1);
                    double cap = ParamNumber(t.Param, "cap");
                    if (cap > 0) add = Math.Min(add, owner.MaxHp * cap / 100.0);
                    if (add <= 0) return true;
                    owner.Shield += add;
                    var e = Log(BattleAction.Shield, owner, owner, add);
                    if (e != null) e.SkillId = skill.Id;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Armed riders ("arm" token): triggers stored by an event that run on the owner's next landed hit (Thornstep Legs, Quantum Strike Shell).</summary>
        private void RunArmed(Fighter att, Fighter def, double dealt)
        {
            if (att.Armed.Count == 0 || Over) return;
            var list = new List<KeyValuePair<SkillDef, SkillTrigger>>(att.Armed);
            att.Armed.Clear();
            foreach (var kv in list)
            {
                if (Over || !att.Alive) break;
                Execute(att, kv.Key, kv.Value, def, dealt);
            }
        }

        /// <summary>Drone cap across every source: at most 5 Drones alive on a side.</summary>
        private bool DroneRoom(BattleSide side)
        {
            int n = 0;
            foreach (var f in F) if (f.Side == side && f.Alive && f.Role == CombatRole.Summon) n++;
            return n < MaxDrones;
        }
    }
}
