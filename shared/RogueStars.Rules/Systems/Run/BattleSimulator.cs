using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Deterministic, seeded auto-battle (GDD §4). Pure: reads content, consumes only the given Rng.
    /// Used by runs (RunSystem), Arena (PlayerVsSnapshot) and Invasions (PlayerVsBoss, damage dealt = score).
    /// </summary>
    public static class BattleSimulator
    {
        /// <summary>Simulate a battle. Same setup + same Rng state → identical result and log.</summary>
        public static BattleResult Simulate(BattleSetup setup, ContentDb content, Rng rng)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            return new BattleSim(setup, content ?? ContentDb.Default, rng ?? new Rng()).Run();
        }

        /// <summary>
        /// Player side from a loadout: Mount (untargetable; MetaSkillIds with Source=Mount), Aliens (untargetable),
        /// Worm (targetable; all other MetaSkillIds + run skills + extra stat mods such as run stat growth).
        /// </summary>
        public static List<CombatantSpec> BuildPlayerSide(CombatLoadout loadout, ContentDb content, IList<string> runSkillIds = null, IList<StatMod> wormExtraStats = null, IDictionary<string, int> runSkillLevels = null)
        {
            content = content ?? ContentDb.Default;
            var list = new List<CombatantSpec>();
            bool hasMount = !string.IsNullOrEmpty(loadout.MountId) || loadout.MountStats.Value(StatId.Atk) > 0;
            var worm = new CombatantSpec
            {
                Id = "worm", Name = string.IsNullOrEmpty(loadout.DisplayName) ? "Worm" : loadout.DisplayName, Role = CombatRole.Worm,
                Stats = loadout.WormStats.Clone(), Targetable = true,
                // parts v2: arms are interchangeable and carry no attack style; the worm always attacks with the neutral style
                // (Slam: basic 100%, combo 60%, counter 70%, rage 220% — what an armless worm used before)
                Style = AttackStyle.Slam,
            };
            if (wormExtraStats != null) worm.Stats.AddAll(wormExtraStats);
            CombatantSpec mount = null;
            if (hasMount)
            {
                mount = new CombatantSpec
                {
                    Id = string.IsNullOrEmpty(loadout.MountId) ? "mount" : loadout.MountId, Name = MountName(content, loadout.MountId), Role = CombatRole.Mount,
                    Stats = loadout.MountStats.Clone(), Targetable = false, Style = AttackStyle.Cannon,
                };
                list.Add(mount);
            }
            foreach (var a in loadout.Aliens)
            {
                var s = new CombatantSpec
                {
                    Id = a.AlienTypeId, Name = a.Name ?? a.AlienTypeId, Role = CombatRole.Alien, Stats = a.Stats.Clone(),
                    Targetable = false, Style = AttackStyle.Claw,
                };
                s.SkillIds.AddRange(a.SkillIds);
                if (a.Traits != null) s.Traits.AddRange(a.Traits);
                list.Add(s);
            }
            // old saves: hero / mount step ids from before the kits read as the kit at that step (KitMigration)
            foreach (var id in KitMigration.MetaSkillIds(content, loadout))
            {
                var def = content.Skill(id);
                if (mount != null && def != null && def.Source == SkillSource.Mount) mount.SkillIds.Add(id);
                else worm.SkillIds.Add(id);
            }
            if (runSkillIds != null) worm.SkillIds.AddRange(runSkillIds);
            if (runSkillLevels != null) foreach (var kv in runSkillLevels) if (kv.Value > 1) worm.SkillLevels[kv.Key] = kv.Value;
            list.Add(worm);
            return list;
        }

        private static string MountName(ContentDb c, string id) =>
            id != null && c.MountConfig.Defs.TryGetValue(id, out var d) ? d.Name : "Mount";

        /// <summary>Enemy combatant: HP/ATK/DEF × threatScale, then + the given percent bonuses (danger, buffs, modifiers).</summary>
        public static CombatantSpec BuildEnemy(ContentDb content, string enemyId, double threatScale, double atkPct = 0, double hpPct = 0, double defPct = 0)
        {
            content = content ?? ContentDb.Default;
            if (!content.Enemies.TryGetValue(enemyId, out var def)) throw new ArgumentException("unknown enemy " + enemyId);
            var spec = new CombatantSpec
            {
                Id = def.Id, Name = def.Name, Role = def.Boss ? CombatRole.Boss : (def.Elite ? CombatRole.Elite : CombatRole.Enemy),
                Style = def.Style, Targetable = true, ThreatScale = threatScale,
            };
            foreach (var m in def.Stats)
            {
                bool scales = m.Stat == StatId.Hp || m.Stat == StatId.Atk || m.Stat == StatId.Def;
                spec.Stats.Add(new StatMod(m.Stat, scales ? m.Flat * threatScale : m.Flat, m.Percent));
            }
            if (atkPct != 0) spec.Stats.Add(new StatMod(StatId.Atk, 0, atkPct));
            if (hpPct != 0) spec.Stats.Add(new StatMod(StatId.Hp, 0, hpPct));
            if (defPct != 0) spec.Stats.Add(new StatMod(StatId.Def, 0, defPct));
            spec.SkillIds.AddRange(def.SkillIds);
            return spec;
        }

        /// <summary>Arena: attacker loadout vs a defender snapshot (both sides Mount → Aliens → Worm). Cap reached = attacker loses.</summary>
        public static BattleResult PlayerVsSnapshot(ContentDb content, CombatLoadout attacker, CombatLoadout defender, Rng rng)
        {
            content = content ?? ContentDb.Default;
            var setup = new BattleSetup { RoundCap = content.Combat.ArenaRoundCap };
            setup.Player.AddRange(BuildPlayerSide(attacker, content));
            setup.Enemy.AddRange(BuildPlayerSide(defender, content));
            return Simulate(setup, content, rng);
        }

        /// <summary>
        /// Invasion: attacker vs a boss whose HP is a pool (set boss.StartHp to the remaining pool, may exceed its HP stat).
        /// The score is result.PlayerDamageDealt; Winner is Player only if the pool is depleted.
        /// </summary>
        public static BattleResult PlayerVsBoss(ContentDb content, CombatLoadout attacker, CombatantSpec boss, Rng rng, int roundCap = 0)
        {
            content = content ?? ContentDb.Default;
            var setup = new BattleSetup { RoundCap = roundCap > 0 ? roundCap : content.Combat.BossRoundCap };
            setup.Player.AddRange(BuildPlayerSide(attacker, content));
            setup.Enemy.Add(boss);
            return Simulate(setup, content, rng);
        }


        /// <summary>Average rounds between a sidekick's own actions at this Speed (UI: "acts about every 3 turns").</summary>
        public static double SidekickActsEvery(double speed, ContentDb content, double hastePct = 0)
        {
            var t = (content ?? ContentDb.Default).Combat;
            double perRound = Math.Max(0, speed) * (1 + hastePct / 100.0);
            return perRound <= 0 ? double.PositiveInfinity : Math.Max(1, t.SidekickActAt / perRound);
        }

        /// <summary>Final stat block of a combatant including its skills' passive packages (for UI and run HP).</summary>
        public static StatBlock FinalStats(CombatantSpec spec, ContentDb content)
        {
            content = content ?? ContentDb.Default;
            var block = spec.Stats.Clone();
            foreach (var id in spec.SkillIds)
            {
                var s = spec.Skill(content, id);
                if (s != null) block.AddAll(s.Stats);
            }
            // parry was removed (skill redesign): leftover parry chance reads as counter chance, like in battle
            double parry = block.Value(StatId.ParryChance);
            if (parry != 0) block.Add(new StatMod(StatId.CounterChance, parry));
            block.Flat.Remove(StatId.ParryChance); block.Percent.Remove(StatId.ParryChance);
            block.Flat.Remove(StatId.ParryReduction); block.Percent.Remove(StatId.ParryReduction);
            return block;
        }
    }


    // ------------------------------------------------------------------------------------------
    // Internal simulation state (never serialized)
    // ------------------------------------------------------------------------------------------

    /// <summary>Arc: a skill-damage jump (Damage to OtherEnemies); conditional bonuses can single it out ("cond:arc").</summary>
    internal enum AttackKind { Basic, Rage, Combo, Counter, Skill, Arc }

    internal sealed class StatusInst
    {
        public StatusId Id;
        public int Stacks;
        public int Turns;
        public double Power;
        public int Source;
        /// <summary>Applied during its carrier's own turn: that turn's end does not count down (a counter's stun, Decoy's charm).</summary>
        public bool Fresh;

        public StatusInst Copy() => new StatusInst { Id = Id, Stacks = Stacks, Turns = Turns, Power = Power, Source = Source, Fresh = Fresh };
    }

    internal sealed partial class BuffInst
    {
        public StatId Stat;
        public double Flat, Percent;
        public int Rounds; // -1 = rest of battle
        /// <summary>A sidekick's buff (its index, else -1): the same sidekick's next action refreshes it instead of stacking.</summary>
        public int Sidekick = -1;
        /// <summary>Ends when its holder's next own turn starts (Brace).</summary>
        public bool UntilTurn;
        /// <summary>Lasts through its holder's next own turn and ends at its end (Rally Cry: the worm's next attacks).</summary>
        public bool ThroughTurn;
        /// <summary>Ends at the end of a turn of its holder without a combo (Battle Rhythm).</summary>
        public bool ResetNoCombo;
        public string SkillId;
    }

    internal sealed class CondBonus
    {
        public string Cond;
        public StatusId Status;
        public double Value;
        /// <summary>missing_hp:N: the bonus is Value per N % HP missing (parts v2: Last Stand Plate).</summary>
        public double Per;
        /// <summary>Gate (SkillTrigger.If), checked with the hit fighter as context; null = none.</summary>
        public string If;
    }

    /// <summary>
    /// How a status behaves when its applier owns rule skills (R6: rules live on the applier and are read from StatusInst.Source;
    /// a sidekick's or mount's statuses use its worm's rules).
    /// </summary>
    internal sealed partial class StatusRules
    {
        public double MaxDelta, DotPct, ConsumeHealPct;
        public int BurstAt, NoExpireAt;
        public bool NoRecoil, KeepsShields, DotCrit, NoCool, Shutdown, DoubleVsChill;
    }

    /// <summary>A fighter's rule skills (Passive <c>Rule</c> triggers), folded once at battle start.</summary>
    internal sealed partial class RuleSet
    {
        public static readonly StatusRules None = new StatusRules();
        public readonly StatusRules[] S = new StatusRules[32];
        public bool ArcSeekChill, ComboNoDecay, ComboAsCounter, CounterCombo, NoTurnAttack, CounterAll, Hivemind, Symbiote,
            Overkill, EchoStrongest, ExtraTurn;
        public double ArcLoopPct, BasicAoePct, ComboFalloffPct, ComboCap, LastLightPct;
        public int ProcCap, LoseTurnEvery, DotTicks, Decay;
        public string OverkillSkill;
        /// <summary>Plague (lab Mythic): the skill that counts the side's inflicted stacks (BattleResult.StacksInflicted); null = none.</summary>
        public string PlagueSkill;

        public StatusRules Of(StatusId id) => S[(int)id] ?? None;

        public StatusRules Edit(StatusId id) => S[(int)id] ?? (S[(int)id] = new StatusRules());

        /// <summary>Adds one Passive Rule trigger (Param "name[:arg]", Status, Value = its growing number).</summary>
        public void Add(SkillTrigger t, string skillId)
        {
            if (string.IsNullOrEmpty(t.Param)) return;
            int colon = t.Param.IndexOf(':');
            string name = colon < 0 ? t.Param : t.Param.Substring(0, colon);
            double arg = 0;
            if (colon >= 0) double.TryParse(t.Param.Substring(colon + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out arg);
            if (t.Status != StatusId.None)
            {
                var r = Edit(t.Status);
                switch (name)
                {
                    case "max": r.MaxDelta += t.Value + arg; break;
                    case "burst_at": r.BurstAt = (int)arg; break;
                    case "no_recoil": r.NoRecoil = true; break;
                    case "keeps_shields": r.KeepsShields = true; break;
                    case "dot_crit": r.DotCrit = true; break;
                    case "dot": r.DotPct += t.Value + arg; break;
                    case "consume_heal": r.ConsumeHealPct += t.Value + arg; break;
                    case "no_expire": r.NoExpireAt = Math.Max(1, (int)arg); break;
                    case "no_cool": r.NoCool = true; break;
                    case "shutdown": r.Shutdown = true; break;
                    case "double_vs_chill": r.DoubleVsChill = true; break;
                    case "deep_wells": r.CapMult = Math.Max(r.CapMult, arg > 0 ? arg : 2); break;
                    default: AddPartStatusRule(t.Status, name, arg, t.Value); break;
                }
                return;
            }
            switch (name)
            {
                case "arc_seek_chill": ArcSeekChill = true; break;
                case "arc_loop": ArcLoopPct += t.Value + arg; break;
                case "basic_aoe": BasicAoePct = t.Value + arg; break;
                case "combo_no_decay": ComboNoDecay = true; break;
                case "combo_falloff": ComboFalloffPct += t.Value + arg; break;
                case "combo_cap": ComboCap = arg; break;
                case "proc_cap": ProcCap = (int)arg; break;
                case "combo_as_counter": ComboAsCounter = true; break;
                case "counter_combo": CounterCombo = true; break;
                case "no_turn_attack": NoTurnAttack = true; break;
                case "counter_all": CounterAll = true; break;
                case "hivemind": Hivemind = true; break;
                case "symbiote": break; // parts v2: Symbiote no longer stops the sidekicks' own turns (old saves' rule does nothing)
                case "overkill": Overkill = true; OverkillSkill = skillId; break;
                case "last_light": LastLightPct = arg; break;
                case "echo_strongest": EchoStrongest = true; break;
                case "extra_turn": ExtraTurn = true; break;
                case "lose_turn_every": LoseTurnEvery = (int)arg; break;
                case "dot_ticks": DotTicks = (int)arg; break;
                case "decay": Decay = (int)arg; break;
                case "plague": PlagueSkill = skillId; break;
                default: AddKitRule(name, arg, t.Value, skillId); break;
            }
        }
    }

    /// <summary>A ConsumeStatus trigger's Param, parsed ("dmg:$;heal:50;take:2;buff:Atk:$;cap:60;refund_max:50;heal_atk:$"; $ = Value).</summary>
    internal sealed class ConsumeSpec
    {
        /// <summary>HealAtk (aliens pass, Thorn Harvest): heal the worm this % of ATK per stack consumed.</summary>
        public double Dmg, Heal, BuffValue, Cap, RefundMax, HealAtk, Splash;
        public int Take;
        /// <summary>Feast (Param "largest"): eat the target's largest status.</summary>
        public bool Largest;
        public StatId BuffStat;
        public bool Buff;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SkillTrigger, ConsumeSpec> Cache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<SkillTrigger, ConsumeSpec>();

        public static ConsumeSpec Of(SkillTrigger t) => Cache.GetValue(t, Parse);

        private static ConsumeSpec Parse(SkillTrigger t)
        {
            var c = new ConsumeSpec();
            bool dollar = false, dmgToken = false;
            double V(string s)
            {
                if (s == "$") { dollar = true; return t.Value; }
                double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v);
                return v;
            }
            if (!string.IsNullOrEmpty(t.Param))
                foreach (var raw in t.Param.Split(';'))
                {
                    var a = raw.Trim().Split(':');
                    if (a.Length == 1 && a[0] == "largest") c.Largest = true;
                    if (a.Length < 2) continue;
                    switch (a[0])
                    {
                        case "dmg": c.Dmg = V(a[1]); dmgToken = true; break;
                        case "heal": c.Heal = V(a[1]); break;
                        case "heal_atk": c.HealAtk = V(a[1]); break;
                        case "take": c.Take = (int)V(a[1]); break;
                        case "cap": c.Cap = V(a[1]); break;
                        case "refund_max": c.RefundMax = V(a[1]); break;
                    case "splash": c.Splash = V(a[1]); break;
                        case "buff":
                            if (a.Length >= 3 && Enum.TryParse(a[1], out StatId st)) { c.Buff = true; c.BuffStat = st; c.BuffValue = V(a[2]); }
                            break;
                    }
                }
            if (!dmgToken && !dollar) c.Dmg = t.Value; // the original form: Value = % ATK per stack, "heal:N"
            return c;
        }
    }

    internal sealed partial class Fighter
    {
        public static readonly int StatCount = Enum.GetValues(typeof(StatId)).Length;
        public int Index;
        public BattleSide Side;
        public CombatRole Role;
        public CombatantSpec Spec;
        public readonly double[] Flat = new double[StatCount], Pct = new double[StatCount];
        public readonly double[] BFlat = new double[StatCount], BPct = new double[StatCount];
        public double MaxHp, Hp, Shield, Rage, StartHp;
        public bool Targetable, Alive = true, LowHpFired;
        public int ConsecutiveDodges;
        public AttackStyle Style;
        public readonly List<SkillDef> Skills = new List<SkillDef>();
        public readonly List<int[]> Ready = new List<int[]>();
        /// <summary>Per trigger: units counted (EveryN) and times fired (MaxPerBattle).</summary>
        public readonly List<int[]> Counts = new List<int[]>(), Fired = new List<int[]>();
        public readonly List<StatusInst> Statuses = new List<StatusInst>();
        public readonly List<BuffInst> Buffs = new List<BuffInst>();
        public readonly int[] StatusCd = new int[32];
        public readonly List<CondBonus> Conds = new List<CondBonus>();
        public readonly RuleSet Rules = new RuleSet();
        /// <summary>Signature skills in the order the run found them (Hivemind tunes sidekicks to them).</summary>
        public readonly List<SkillDef> Signatures = new List<SkillDef>();
        public bool HasFatal, HasLoop;
        public double DamageDealt;
        public int SummonedBy = -1;
        /// <summary>Sidekick (alien, player-side summon): never hit, charges by Speed and acts with its ability.</summary>
        public bool Sidekick;
        public double Charge;
        /// <summary>The side's alien power (%) a sidekick got at its start: + that % on its hits, heals and shields (aliens pass).</summary>
        public double AlienPower;
        /// <summary>A sidekick's power in % of its worm's ATK (aliens 100; a drone its summon's Value).</summary>
        public double AtkPct = 100;
        /// <summary>A sidekick's effect traits (aliens pass) and its actions so far (Twin: every 2nd).</summary>
        public bool Twin, Wide, Lingering, Bulwark, Eager;
        public int ActCount;
        /// <summary>Indices of the sidekicks guarding this fighter, oldest first (Block, Bodyguard): each intercepts one hit.</summary>
        public readonly List<int> Guards = new List<int>();
        /// <summary>Guard (aliens pass): the next GuardHits attack hits taken deal GuardPct % less (GuardBy = the guarding sidekick).</summary>
        public double GuardPct;
        public int GuardHits, GuardBy = -1;

        // ---- per-battle counters for conditions and timing (skill redesign) ----
        public int Turns, HitsLanded, HitsTakenRound, HitsTakenRoundNo = -1, ComboStrikes, Strikes, HiveRound = -1;
        public bool ComboThisTurn, CounterWindow;
        public double CounterCharge;
        public AttackKind LastKind, BestKind;
        public int LastTarget = -1, BestTarget = -1;
        public bool Attacked;
        /// <summary>The owner's latest rage attack landed as a crit (condition rage_crit: lab Frost Nova).</summary>
        public bool RageCrit;
        public double BestDealt;
        /// <summary>Damage of the last few attack hits taken (Rewind).</summary>
        public readonly List<double> HitHistory = new List<double>();
        public readonly int[] TickRound = new int[32], StackAllRound = new int[32];
        /// <summary>The statuses it carried when it died (Fission's transfer, conditions on the dead fighter).</summary>
        public List<StatusInst> DeathStatuses;
        public readonly Dictionary<string, double> ConsumeBuffUsed = new Dictionary<string, double>();

        public double S(StatId s)
        {
            int i = (int)s;
            return (Flat[i] + BFlat[i]) * (1 + (Pct[i] + BPct[i]) / 100.0);
        }

        public StatusInst Status(StatusId id)
        {
            for (int i = 0; i < Statuses.Count; i++) if (Statuses[i].Id == id) return Statuses[i];
            if (!Alive && DeathStatuses != null)
                for (int i = 0; i < DeathStatuses.Count; i++) if (DeathStatuses[i].Id == id) return DeathStatuses[i];
            return null;
        }

        public double HpPct => MaxHp > 0 ? Hp / MaxHp * 100.0 : 0;
    }

    /// <summary>Loop's snapshot of every fighter at a round's start (HP, shield, rage, statuses, charge, alive).</summary>
    internal sealed class BattleSnap
    {
        public int Round;
        public double[] Hp, Shield, Rage, Charge;
        public bool[] Alive;
        public List<StatusInst>[] Statuses;
    }

    internal sealed partial class BattleSim
    {
        private readonly BattleSetup _setup;
        private readonly ContentDb C;
        private readonly BattleTuning T;
        private readonly Rng R;
        private readonly List<Fighter> F = new List<Fighter>();
        private readonly BattleResult Res = new BattleResult();
        private int Round;
        private bool Over;
        private int _depth;
        private double _lastAbsorbed;
        private int _thrPrev;
        private Fighter _acting;
        private readonly List<BattleSnap> _snaps = new List<BattleSnap>();
        private bool _anyLoop;

        public BattleSim(BattleSetup setup, ContentDb content, Rng rng)
        {
            _setup = setup; C = content; T = content.Combat; R = rng;
        }

        // ---- setup ----------------------------------------------------------------------------

        private Fighter MakeFighter(CombatantSpec spec, BattleSide side)
        {
            var f = new Fighter { Index = F.Count, Side = side, Role = spec.Role, Spec = spec, Targetable = spec.Targetable, Style = spec.Style };
            foreach (var kv in spec.Stats.Flat) f.Flat[(int)kv.Key] += kv.Value;
            foreach (var kv in spec.Stats.Percent) f.Pct[(int)kv.Key] += kv.Value;
            foreach (var id in spec.SkillIds)
            {
                var s = spec.Skill(C, id);
                if (s == null) continue;
                f.Skills.Add(s);
                f.Ready.Add(new int[s.Triggers.Count]);
                f.Counts.Add(new int[s.Triggers.Count]);
                f.Fired.Add(new int[s.Triggers.Count]);
                if (s.Signature) f.Signatures.Add(s);
                foreach (var m in s.Stats) { f.Flat[(int)m.Stat] += m.Flat; f.Pct[(int)m.Stat] += m.Percent; }
                foreach (var t in s.Triggers)
                {
                    if (t.When == TriggerKind.OnFatal) f.HasFatal = true;
                    if (t.Effect == EffectKind.Loop) f.HasLoop = true;
                    if (t.When != TriggerKind.Passive) continue;
                    if (t.Effect == EffectKind.Rule) { f.Rules.Add(t, s.Id); continue; }
                    if (t.Param == null || !t.Param.StartsWith("cond:", StringComparison.Ordinal)) continue;
                    var cb = new CondBonus { Cond = t.Param.Substring(5), Value = t.Value, If = string.IsNullOrWhiteSpace(t.If) ? null : t.If };
                    if (cb.Cond.StartsWith("vs_status:", StringComparison.Ordinal))
                    {
                        Enum.TryParse(cb.Cond.Substring(10), out cb.Status);
                        cb.Cond = "vs_status";
                    }
                    else if (cb.Cond.StartsWith("per_stack:", StringComparison.Ordinal))
                    {
                        Enum.TryParse(cb.Cond.Substring(10), out cb.Status);
                        cb.Cond = "per_stack";
                    }
                    else if (cb.Cond.StartsWith("missing_hp:", StringComparison.Ordinal))
                    {
                        double.TryParse(cb.Cond.Substring(11), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cb.Per);
                        cb.Cond = "missing_hp";
                    }
                    f.Conds.Add(cb);
                }
            }
            // parry was removed (skill redesign): parry chance an old save still carries counts as counter chance
            int pc = (int)StatId.ParryChance, cc = (int)StatId.CounterChance;
            f.Flat[cc] += f.Flat[pc]; f.Flat[pc] = 0; f.Pct[pc] = 0;
            f.Flat[(int)StatId.ParryReduction] = 0; f.Pct[(int)StatId.ParryReduction] = 0;
            if (f.HasLoop) _anyLoop = true;
            foreach (var tr in spec.Traits)
                switch (tr)
                {
                    case ContentDb.TraitTwin: f.Twin = true; break;
                    case ContentDb.TraitWide: f.Wide = true; break;
                    case ContentDb.TraitLingering: f.Lingering = true; break;
                    case ContentDb.TraitBulwark: f.Bulwark = true; break;
                    case ContentDb.TraitEager: f.Eager = true; break;
                }
            F.Add(f);
            var info = new BattleFighterInfo { Index = f.Index, Id = spec.Id, Name = spec.Name, Role = spec.Role, Side = side, Targetable = spec.Targetable };
            Res.Fighters.Add(info);
            return f;
        }

        private void InitHp(Fighter f)
        {
            f.MaxHp = Math.Max(1, f.S(StatId.Hp));
            if (f.Spec.StartHp >= 0)
            {
                if (f.Spec.StartHp > f.MaxHp) f.MaxHp = f.Spec.StartHp;
                f.Hp = f.Spec.StartHp;
            }
            else f.Hp = f.MaxHp;
            f.Shield = Math.Max(0, f.Spec.StartShield);
            if (f.Spec.StartRage > 0 && !f.Sidekick) f.Rage = Math.Min(T.RageOverfillMax, f.Spec.StartRage);
            f.StartHp = f.Hp;
            if (f.Hp < 0.5) { f.Hp = 0; f.Alive = false; }
        }

        private void Setup()
        {
            foreach (var s in _setup.Player) MakeFighter(s, BattleSide.Player);
            foreach (var s in _setup.Enemy) MakeFighter(s, BattleSide.Enemy);
            // Alien / mount power from the side's worm (GDD §4.1). The mount's own AlienPower (Hive Carrier weapons sit on the mount)
            // reaches the sidekicks too. Aliens are sidekicks without stats: their numbers are % of the worm's ATK / max HP (InitSidekick).
            for (int side = 0; side < 2; side++)
            {
                Fighter worm = FirstWorm((BattleSide)side);
                double ap = AlienPowerOf((BattleSide)side);
                foreach (var f in F)
                {
                    if ((int)f.Side != side) continue;
                    if (f.Role == CombatRole.Alien || f.Role == CombatRole.Summon) InitSidekick(f, ap);
                    else if (f.Role == CombatRole.Mount) InitMount(f, worm); // hero & mount kits: no mount stats, no HP shield
                }
            }
            foreach (var f in F) InitHp(f);
            SetupKits();
        }

        /// <summary>
        /// Aliens pass: a sidekick has no stats of its own (any HP / ATK / DEF it carries is ignored); its power is AtkPct % of its
        /// worm's ATK × (1 + alien power %), see <see cref="AtkOf"/>. Its charge starts at SidekickStartCharge.
        /// </summary>
        private void InitSidekick(Fighter f, double alienPower)
        {
            f.Sidekick = true;
            foreach (var k in new[] { (int)StatId.Atk, (int)StatId.Hp, (int)StatId.Def }) { f.Flat[k] = 0; f.Pct[k] = 0; }
            f.AlienPower = alienPower;
            f.Charge = T.SidekickStartCharge;
        }

        /// <summary>A fighter's ATK; a sidekick's is its worm's live ATK × (1 + alien power %) (its hits then take their % of it, ComputeDamage).</summary>
        private double AtkOf(Fighter f)
        {
            if (f.Rider)
            {
                var w = FirstWorm(f.Side);
                return w == null ? 0 : w.S(StatId.Atk) * Math.Max(0, 1 + f.MountPct / 100.0);
            }
            if (!f.Sidekick) return f.S(StatId.Atk);
            var worm = FirstWorm(f.Side);
            return worm == null ? 0 : worm.S(StatId.Atk) * Math.Max(0, 1 + f.AlienPower / 100.0);
        }

        private double AlienPowerOf(BattleSide side)
        {
            var worm = FirstWorm(side);
            double ap = worm != null ? worm.S(StatId.AlienPower) : 0;
            foreach (var f in F) if (f.Side == side && f.Role == CombatRole.Mount) ap += f.S(StatId.AlienPower);
            return ap;
        }

        /// <summary>A sidekick's heals, shields and rage are × (1 + alien power %) (never its chances, stacks or buffs); others' as written.</summary>
        private static double SupportScale(Fighter owner) => owner.Sidekick ? Math.Max(0, 1 + owner.AlienPower / 100.0) : 1;

        private Fighter FirstWorm(BattleSide side)
        {
            foreach (var f in F) if (f.Side == side && f.Role == CombatRole.Worm) return f;
            return null;
        }

        /// <summary>Whose rule skills apply to a fighter's actions: its own, or its worm's for a sidekick / mount.</summary>
        private Fighter RulesOwner(Fighter f)
        {
            if (f == null) return null;
            if (f.Sidekick || f.Role == CombatRole.Mount) return FirstWorm(f.Side) ?? f;
            return f;
        }

        private RuleSet RulesOf(Fighter f) => RulesOwner(f)?.Rules;

        private StatusRules SRules(StatusInst st)
        {
            var src = st != null && st.Source >= 0 && st.Source < F.Count ? F[st.Source] : null;
            var rs = RulesOf(src);
            return rs != null ? rs.Of(st.Id) : RuleSet.None;
        }

        /// <summary>A status's stack cap with its applier's rules (Hemorrhage +5; Heat at least its Supernova threshold).</summary>
        private int CapOf(StatusDef def, StatusRules r, bool deep = false)
        {
            int cap = (deep && r.CapMult > 1 ? (int)Math.Round(def.MaxStacks * r.CapMult) : def.MaxStacks) + (int)Math.Round(r.MaxDelta);
            int burst = BurstAtOf(def, r);
            if (burst > 0) cap = Math.Max(cap, burst);
            return Math.Max(1, cap);
        }

        private static int BurstAtOf(StatusDef def, StatusRules r) => def.BurstAtStacks <= 0 ? 0 : BurstWithDelta(r.BurstAt > 0 ? r.BurstAt : def.BurstAtStacks, r);

        // ---- main loop ------------------------------------------------------------------------

        public BattleResult Run()
        {
            Setup();
            foreach (var f in F) { Res.Fighters[f.Index].StartShield = Round0(f.Shield); Res.Fighters[f.Index].StartRage = Round0(f.Rage); }
            int cap = _setup.RoundCap > 0 ? _setup.RoundCap : T.RoundCap;
            CheckOver();
            if (!Over) ApplyOpeningStatuses(); // Plague
            if (!Over)
                foreach (var f in TurnOrder()) { if (Over) break; if (f.Alive) Fire(f, TriggerKind.OnBattleStart, null); }
            // Eager (aliens pass): a sidekick's first action comes at battle start (its charge then starts empty)
            if (!Over)
                foreach (var f in TurnOrder())
                {
                    if (Over) break;
                    var w = FirstWorm(f.Side);
                    if (!f.Alive || !f.Sidekick || !f.Eager) continue;
                    SidekickAct(f, false);
                    f.Charge = 0;
                }

            while (!Over && Round < cap)
            {
                Round++;
                if (_anyLoop) Snapshot();
                var order = TurnOrder();
                foreach (var f in order) { if (Over) break; if (f.Alive) Fire(f, TriggerKind.OnRoundStart, null); }
                foreach (var f in order) { if (Over) break; if (f.Alive) TakeTurn(f); if (!Over) RunExtraTurns(); }
                foreach (var f in order) { if (Over) break; if (f.Alive) Fire(f, TriggerKind.OnRoundEnd, null); }
                TickBuffs();
            }
            if (!Over)
            {
                Over = true;
                Res.Winner = BattleSide.Enemy;
                Res.EndReason = BattleEndReason.RoundCap;
            }
            foreach (var f in TurnOrder())
                if (f.Alive && f.Side == Res.Winner) Fire(f, TriggerKind.OnBattleWon, null);
            return BuildResult();
        }

        private BattleResult BuildResult()
        {
            Res.Rounds = Round;
            foreach (var f in F)
            {
                var info = Res.Fighters[f.Index];
                info.MaxHp = Round0(f.MaxHp);
                info.StartHp = Round0(f.StartHp);
                info.EndHp = Round0(f.Hp);
                info.EndShield = Round0(f.Shield);
                info.EndRage = Round0(f.Rage);
                info.Alive = f.Alive;
                info.DamageDealt = Round0(f.DamageDealt);
                info.SummonedBy = f.SummonedBy;
            }
            var worm = FirstWorm(BattleSide.Player);
            if (worm != null) { Res.WormHpLeft = worm.Alive ? worm.Hp : 0; Res.WormMaxHp = worm.MaxHp; }
            Res.Novas = 0;
            foreach (var le in Res.Log) if (le.Action == BattleAction.Supernova && le.Amount == 0 && le.Actor == le.Target) Res.Novas++;
            return Res;
        }

        private static long Round0(double v) => (long)Math.Round(v, MidpointRounding.AwayFromZero);

        private static int RolePriority(CombatRole r)
        {
            switch (r)
            {
                case CombatRole.Mount: return 0;
                case CombatRole.Alien: return 1;
                case CombatRole.Summon: return 2;
                case CombatRole.Worm: return 3;
                default: return 4;
            }
        }

        private double SideSpeed(BattleSide side)
        {
            double best = double.MinValue;
            foreach (var f in F) if (f.Side == side && f.Alive && f.Targetable) best = Math.Max(best, f.S(StatId.Speed));
            return best;
        }

        /// <summary>Opening side by Speed (player side on ties); within a side Mount → Aliens → Summons → Worm → enemies.</summary>
        private List<Fighter> TurnOrder()
        {
            bool playerFirst = SideSpeed(BattleSide.Player) >= SideSpeed(BattleSide.Enemy);
            // Slipstream (Rift Racer): in round 1 the worm's side acts before every enemy (not crowd control: enemies still act)
            if (Round <= 1)
            {
                var pm = MountOf(BattleSide.Player); var em = MountOf(BattleSide.Enemy);
                if (pm != null && pm.Rules.Slipstream && !(em != null && em.Rules.Slipstream)) playerFirst = true;
                else if (em != null && em.Rules.Slipstream && !(pm != null && pm.Rules.Slipstream)) playerFirst = false;
            }
            // Quantum Swift Tail: from round N the worm's side acts before every enemy
            var pw = FirstWorm(BattleSide.Player); var ew = FirstWorm(BattleSide.Enemy);
            bool pa = pw != null && pw.Rules.ActFirstRound > 0 && Round >= pw.Rules.ActFirstRound;
            bool ea = ew != null && ew.Rules.ActFirstRound > 0 && Round >= ew.Rules.ActFirstRound;
            if (pa && !ea) playerFirst = true; else if (ea && !pa) playerFirst = false;
            var first = playerFirst ? BattleSide.Player : BattleSide.Enemy;
            var order = new List<Fighter>(F.Count);
            AppendSide(order, first);
            AppendSide(order, first == BattleSide.Player ? BattleSide.Enemy : BattleSide.Player);
            return order;
        }

        private void AppendSide(List<Fighter> order, BattleSide side)
        {
            for (int p = 0; p <= 4; p++)
                foreach (var f in F)
                    if (f.Side == side && RolePriority(f.Role) == p) order.Add(f);
        }

        private void CheckOver()
        {
            if (Over) return;
            bool p = false, e = false;
            foreach (var f in F)
            {
                if (!f.Alive || !f.Targetable) continue;
                if (f.Side == BattleSide.Player) p = true; else e = true;
            }
            if (!p) { Over = true; Res.Winner = BattleSide.Enemy; Res.EndReason = BattleEndReason.PlayerDefeated; }
            else if (!e) { Over = true; Res.Winner = BattleSide.Player; Res.EndReason = BattleEndReason.EnemiesDefeated; }
        }

        // ---- turns ----------------------------------------------------------------------------

        private void TakeTurn(Fighter f)
        {
            if (f.Sidekick) { SidekickTurn(f); return; }
            if (f.Role == CombatRole.Mount) { MountTurn(f); return; }
            _acting = f;
            EndTurnScopedEffects(f);
            f.ComboThisTurn = false;
            double regen = f.S(StatId.Regen);
            if (regen > 0 && f.Hp < f.MaxHp) Heal(f, f, f.MaxHp * regen / 100.0 * (1 + f.S(StatId.HealPower) / 100.0), BattleAction.Regen, null, false);

            if (!TickStatuses(f)) return;
            if (f.Role == CombatRole.Worm) { AbilityTick(f, false); if (Over || !f.Alive) { if (f.Alive) EndTurn(f); return; } }

            bool skip = false, charmed = false;
            foreach (var st in f.Statuses)
            {
                if (!T.Statuses.TryGetValue(st.Id, out var def)) continue;
                if (def.SkipsTurn) skip = true;
                if (def.Charms) charmed = true;
            }
            if (!skip && f.Rules.LoseTurnEvery > 0 && Round % f.Rules.LoseTurnEvery == 0) skip = true; // Borrowed Time's cost
            if (f.SkipNextTurn) { f.SkipNextTurn = false; skip = true; } // Cryo pod: the saved worm sits out its next turn
            if (skip)
            {
                Log(BattleAction.SkipTurn, f, f, 0);
                // Time Debt: each enemy turn skipped feeds the other side
                for (int i = 0, n = F.Count; i < n; i++) // a trigger may summon (F grows): iterate the fighters present now
                {
                    var o = F[i];
                    if (Over) break;
                    if (o.Side != f.Side && o.Alive && (o.Role == CombatRole.Worm || o.Role == CombatRole.Mount)) Fire(o, TriggerKind.OnSkipTurn, f);
                }
                if (f.Alive) EndTurn(f);
                return;
            }

            Act(f, charmed);
            if (Over || !f.Alive) { if (f.Alive) EndTurn(f); return; }
            if (f.Rules.ExtraTurn) // Borrowed Time: a second turn each round
            {
                Act(f, charmed);
                if (Over || !f.Alive) { if (f.Alive) EndTurn(f); return; }
            }
            EndTurn(f);
        }

        /// <summary>Buffs and windows that last "until your next turn" end when the owner's next turn starts.</summary>
        private void EndTurnScopedEffects(Fighter f)
        {
            f.CounterWindow = false;
            for (int i = f.Buffs.Count - 1; i >= 0; i--)
            {
                var b = f.Buffs[i];
                if (b.ThroughTurn && b.Rounds == -1) b.Rounds = -2; // this is the turn it lasts through
                if (!b.UntilTurn) continue;
                if (b.TurnsLeft > 1) { b.TurnsLeft--; continue; } // until_turn:N (Grandmaster Mask)
                RemoveBuff(f, i);
            }
        }

        /// <summary>The acting part of a turn: OnTurnStart, the basic attack, a full-rage attack, the combo chain, OnTurnEnd.</summary>
        private void Act(Fighter f, bool charmed)
        {
            f.Turns++;
            f.ComboStrikes = 0;
            f.Strikes = 0;
            f.ExtraHitsTurn = 0;
            f.PileTarget = -1; f.PileCount = 0;
            f.CritThisTurn = false;
            Fire(f, TriggerKind.OnTurnStart, null);
            if (!f.Alive || Over) return;
            ArcVolley(f); // Storm Coil
            if (!f.Alive || Over) return;
            if (f.Role == CombatRole.Worm) { AbilityTick(f, true); if (!f.Alive || Over) return; }

            Fighter target = charmed ? CharmTarget(f) : DefaultTarget(f);
            if (target == null)
            {
                if (charmed) Log(BattleAction.SkipTurn, f, f, 0);
                return;
            }
            var rules = f.Rules;
            if (!rules.NoTurnAttack)
            {
                double aoe = charmed ? 0 : TurnAoePct(f);
                if (aoe > 0)
                {
                    // Thunderhead: the basic attack hits every enemy at a fraction of its damage (parts: Quantum Strike Tail's turn,
                    // Last Stand Plate below 30% HP; area basic attacks use the highest %)
                    var all = new List<Fighter>();
                    foreach (var o in F) if (o.Side != f.Side && o.Alive && o.Targetable) all.Add(o);
                    foreach (var o in all) { if (Over || !f.Alive) break; Attack(f, o, AttackKind.Basic, 0, aoe / 100.0); }
                }
                else Attack(f, target, AttackKind.Basic, 0);
                if (Over || !f.Alive) return;
                // Afterburner Tail: the turn's first attack strikes N times in a row (combo strikes)
                for (int k = 1; k < f.ForcedStrikes && !Over && f.Alive; k++)
                {
                    var ct = charmed ? (target.Alive ? target : null) : DefaultTarget(f);
                    if (ct == null) break;
                    f.ComboStrikes++;
                    f.ComboThisTurn = true;
                    double cd = Attack(f, ct, AttackKind.Combo, 0);
                    Fire(f, TriggerKind.OnCombo, ct, StatusId.None, cd);
                }
                f.ForcedStrikes = 0;
                if (Over || !f.Alive) return;
            }

            GainRage(f, T.RagePerAction);
            double cost = RageCost(f); // parts v2: Rage Turbine / Quantum Fury Shell need less rage (floor 50)
            if (f.Rage >= cost)
            {
                f.Rage -= cost;
                bool twice = TakeBank(f); // Overcharge: a full bank fires this rage attack twice
                var rt = charmed ? (target.Alive ? target : null) : RageTarget(f);
                var stagger = f.Status(StatusId.Stagger);
                if (stagger != null)
                {
                    // Thermal Shock on a boss: the staggered fighter loses this rage attack
                    f.Statuses.Remove(stagger);
                    var e = Log(BattleAction.Staggered, f, f, 0);
                    if (e != null) e.Status = StatusId.Stagger;
                }
                else if (rt != null)
                {
                    for (int k = 0; k < (twice ? 2 : 1); k++)
                    {
                        if (k > 0) { rt = charmed ? (rt.Alive ? rt : null) : RageTarget(f); if (rt == null || Over || !f.Alive) break; }
                        RageAttack(f, rt);
                    }
                }
            }

            if (!rules.NoTurnAttack && !Over && f.Alive) ComboChain(f, target, charmed, 0);
            if (!Over && f.Alive) Fire(f, TriggerKind.OnTurnEnd, DefaultTarget(f));
            f.Strikes = 0;
        }

        /// <summary>
        /// The combo chain: each link rolls the (decaying) combo chance for another strike (at most MaxChain). Endless Chain: no decay,
        /// each strike deals ComboFalloff % less than the last, combo chance capped. Counter-combos (Perfect Form) never get countered.
        /// </summary>
        private void ComboChain(Fighter f, Fighter target, bool charmed, int counterDepth)
        {
            var rules = f.Rules;
            double cap = rules.ComboCap > 0 ? Math.Min(95, rules.ComboCap) : 95;
            // Flicker Treads: a share of the dodge chance also counts as combo chance (read before conversions, never chained)
            double chance = f.S(StatId.ComboChance) + (rules.DodgeAsComboPct > 0 ? f.S(StatId.Dodge) * rules.DodgeAsComboPct / 100.0 : 0);
            double p = CombatMath.Clamp(ShutDown(f) ? 0 : chance, 0, cap) / 100.0;
            int links = 0, carried = 0;
            double mult = 1;
            bool forced = false;
            while (!Over && f.Alive && (forced || (p > 0 && links < T.MaxChain && R.Chance(p))))
            {
                forced = false;
                // Chainlash Whip: combo strikes after the first go for the lowest-HP enemy (awakened: double damage)
                bool lowest = rules.ComboTargetLowest && !charmed && f.ComboStrikes > 0;
                var ct = charmed ? (target.Alive ? target : null) : lowest ? LowestEnemy(f) : DefaultTarget(f);
                if (ct == null) break;
                if (rules.ComboFalloffPct != 0) mult *= Math.Max(0, 1 - rules.ComboFalloffPct / 100.0); // negative falloff: strikes ramp up
                f.ComboStrikes++;
                f.ComboThisTurn = true;
                double dealt = Attack(f, ct, AttackKind.Combo, counterDepth, mult * (lowest ? 1 + rules.ComboLowestBonus / 100.0 : 1));
                Fire(f, TriggerKind.OnCombo, ct, StatusId.None, dealt);
                // Chainlash Whip: a combo strike that kills carries the chain on (no decay, no link used; awakened: +1 strike)
                if (rules.ComboKillCarry && !ct.Alive && carried < T.MaxChain)
                {
                    carried++;
                    if (rules.ComboKillExtra > 0) forced = true;
                    continue;
                }
                if (!rules.ComboNoDecay) p *= T.ChainDecay;
                links++;
            }
        }

        /// <summary>Winter's Grip: a fighter carrying Chill from an applier with the shutdown rule can't crit, combo or counter.</summary>
        private bool ShutDown(Fighter f)
        {
            var st = f.Status(StatusId.Chill);
            return st != null && SRules(st).Shutdown;
        }

        /// <summary>A sidekick's turn: it charges Speed × (1 + haste %); a full charge (SidekickActAt) is spent on one action.</summary>
        private void SidekickTurn(Fighter f)
        {
            var worm = FirstWorm(f.Side);
            double haste = worm != null ? Math.Max(-90, worm.S(StatId.SidekickHaste)) : 0;
            f.Charge += Math.Max(0, f.S(StatId.Speed)) * (1 + haste / 100.0);
            double at = ActAt(f); // parts v2: Swarmcaller Antenna (floor 50)
            if (f.Charge < at) return;
            f.Charge = Math.Min(at, f.Charge - at); // at most one own action per turn
            SidekickAct(f, false);
        }

        /// <summary>
        /// A sidekick acts: a SidekickAct marker, its ability (OnAct triggers), Hivemind's tuned signature, then OnSidekickAct on its
        /// side's worm and mount.
        /// </summary>
        private void SidekickAct(Fighter f, bool extra)
        {
            if (!f.Alive || Over) return;
            f.ActCount++;
            string ability = null;
            foreach (var sk in f.Skills)
                foreach (var t in sk.Triggers)
                    if (ability == null && t.When == TriggerKind.OnAct) ability = sk.Id;
            var e = Log(BattleAction.SidekickAct, f, f, 0);
            if (e != null) { e.SkillId = ability; e.Stacks = extra ? 1 : 0; }
            f.SidekickHitThisAct = false; f.SidekickHitTarget = -1;
            Fire(f, TriggerKind.OnAct, null);
            if (Over) return;
            var worm = FirstWorm(f.Side);
            // Hivemind: every sidekick is tuned to a signature; a drone (aliens pass) always triggers the start signature once.
            // Launch Bay drones trigger none (1★–A3) or every owned signature (A5).
            if (worm != null && worm.Alive && f.CarryOnAct) Carry(f, DefaultTarget(f), 1);
            else if (worm != null && worm.Alive && !f.NoSig && (worm.Rules.Hivemind || f.Role == CombatRole.Summon)) HiveSignature(f, worm);
            if (Over) return;
            // Tamed (Beast Tamer 5★): a sidekick action that hit an enemy carries the signatures (once per action)
            if (worm != null && worm.Alive && f.SidekickHitThisAct && f.SidekickHitTarget >= 0 && F[f.SidekickHitTarget].Alive)
                Fire(worm, TriggerKind.OnSidekickHit, F[f.SidekickHitTarget]);
            if (Over) return;
            for (int i = 0, n = F.Count; i < n; i++) // a trigger may summon (F grows): iterate the fighters present now
            {
                var o = F[i];
                if (Over) break;
                if (o.Side == f.Side && o.Alive && (o.Role == CombatRole.Worm || o.Role == CombatRole.Mount)) Fire(o, TriggerKind.OnSidekickAct, null);
            }
            if (Over || worm == null || !worm.Alive) return;
            // Hatchery Tail: Drone zaps apply stacks of a random status the side owns
            if (f.Role == CombatRole.Summon && worm.Rules.DroneStatus > 0 && worm.Owned != null && worm.Owned.Count > 0)
            {
                var front = DefaultTarget(f);
                if (front != null) ApplyStatus(f, front, worm.Owned[R.Range(0, worm.Owned.Count)], worm.Rules.DroneStatus, 0, null);
            }
            // Swarmcaller Antenna: the side's first N sidekick actions each round happen twice
            if (!_kickRepeat && worm.Rules.SidekickTwice > 0 && !Over && f.Alive)
            {
                if (worm.KickActRound != Round) { worm.KickActRound = Round; worm.KickActCount = 0; }
                if (worm.KickActCount < worm.Rules.SidekickTwice && ReserveExtra(f.Side))
                {
                    worm.KickActCount++;
                    _kickRepeat = true;
                    try { SidekickAct(f, true); } finally { _kickRepeat = false; }
                }
            }
        }

        private bool _kickRepeat;

        /// <summary>The rage attack's target: the front enemy (Quantum Fury Tail: the lowest-HP enemy).</summary>
        private Fighter RageTarget(Fighter f) => f.Rules.RageTargetLowest ? LowestEnemy(f) : DefaultTarget(f);

        private Fighter LowestEnemy(Fighter f)
        {
            Fighter best = null;
            foreach (var o in F)
                if (o.Side != f.Side && o.Alive && o.Targetable && (best == null || o.Hp / o.MaxHp < best.Hp / best.MaxHp)) best = o;
            return best;
        }

        private SkillTrigger _hiveArc, _hiveStrike;
        private SkillTrigger HiveArc => _hiveArc ?? (_hiveArc = new SkillTrigger { When = TriggerKind.OnAct, Effect = EffectKind.Damage, Value = T.HiveArcPct, Target = TargetKind.OtherEnemies, Stacks = 1 });
        private SkillTrigger HiveStrike => _hiveStrike ?? (_hiveStrike = new SkillTrigger { When = TriggerKind.OnAct, Effect = EffectKind.Damage, Value = T.HiveStrikePct, Target = TargetKind.Enemy });
        private readonly int[] _extraRound = { -1, -1 }, _extraCount = new int[2];

        /// <summary>Extra sidekick actions per round on a side are capped (Combat.MaxExtraSidekickActions): true if one more may happen.</summary>
        private bool ReserveExtra(BattleSide side)
        {
            int i = (int)side;
            if (_extraRound[i] != Round) { _extraRound[i] = Round; _extraCount[i] = 0; }
            if (_extraCount[i] >= T.MaxExtraSidekickActions) return false;
            _extraCount[i]++;
            return true;
        }

        /// <summary>
        /// Hivemind: each sidekick is tuned to one of the worm's signatures, round-robin in the order the run found them; its action
        /// also triggers that signature once (Ignite / Irradiate / Lacerate / Chill: one stack; Arc: one HiveArcPct % jump; Flurry /
        /// Riposte / Echo: one HiveStrikePct % strike; Command: it acts again, once per round). Without Hivemind a drone triggers the
        /// start signature (the first one) the same way (aliens pass).
        /// </summary>
        private void HiveSignature(Fighter kick, Fighter worm)
        {
            if (worm.Signatures.Count == 0) return;
            int ord = 0;
            if (worm.Rules.Hivemind)
                foreach (var o in F) { if (o == kick) break; if (o.Side == kick.Side && o.Sidekick) ord++; }
            var sig = worm.Signatures[ord % worm.Signatures.Count];
            var front = DefaultTarget(kick);
            if (front == null) return;
            switch (sig.Family)
            {
                case SkillFamily.Fire:
                case SkillFamily.Poison:
                case SkillFamily.Bleed:
                case SkillFamily.Ice:
                {
                    StatusId st = StatusId.None;
                    foreach (var t in sig.Triggers) if (t.Effect == EffectKind.ApplyStatus) { st = t.Status; break; }
                    if (st != StatusId.None) ApplyStatus(kick, front, st, 1, 0, sig.Id);
                    break;
                }
                case SkillFamily.Lightning:
                    Execute(kick, sig, HiveArc, front, 0);
                    break;
                case SkillFamily.AlienSynergy:
                    if (kick.HiveRound != Round && ReserveExtra(kick.Side)) { kick.HiveRound = Round; SidekickAct(kick, true); }
                    break;
                default:
                    Execute(kick, sig, HiveStrike, front, 0);
                    break;
            }
        }

        private void EndTurn(Fighter f)
        {
            for (int i = f.Statuses.Count - 1; i >= 0; i--)
            {
                var st = f.Statuses[i];
                if (T.Statuses.TryGetValue(st.Id, out var sdef) && sdef.Permanent) continue; // Heat, Radiation, Bleed stay
                var r = SRules(st);
                if (r.NoExpireAt > 0 && st.Stacks >= r.NoExpireAt) continue; // Permafrost, Capacitor
                if (r.NoExpireShielded) { var ap = st.Source >= 0 && st.Source < F.Count ? RulesOwner(F[st.Source]) : null; if (ap != null && ap.Alive && ap.Shield > 0) continue; } // Rimeguard Plate
                if (st.Fresh) { st.Fresh = false; continue; } // applied during this very turn: it lasts into the next one
                st.Turns--;
                if (st.Turns <= 0)
                {
                    f.Statuses.RemoveAt(i);
                    var e = Log(BattleAction.StatusExpired, f, f, 0);
                    if (e != null) e.Status = st.Id;
                    StatusLost(f, st.Id, st.Stacks);
                    if (Over) return;
                }
            }
            if (!f.ComboThisTurn) // Battle Rhythm: a turn without a combo resets it
                for (int i = f.Buffs.Count - 1; i >= 0; i--)
                    if (f.Buffs[i].ResetNoCombo) RemoveBuff(f, i);
            for (int i = f.Buffs.Count - 1; i >= 0; i--)
                if (f.Buffs[i].ThroughTurn && f.Buffs[i].Rounds == -2) RemoveBuff(f, i); // the turn it was waiting for is over
            if (_acting == f) _acting = null;
        }

        private void RemoveBuff(Fighter f, int i)
        {
            var b = f.Buffs[i];
            f.BFlat[(int)b.Stat] -= b.Flat;
            f.BPct[(int)b.Stat] -= b.Percent;
            f.Buffs.RemoveAt(i);
        }

        private void TickBuffs()
        {
            foreach (var f in F)
            {
                for (int i = f.Buffs.Count - 1; i >= 0; i--)
                {
                    var b = f.Buffs[i];
                    if (b.Rounds < 0) continue;
                    b.Rounds--;
                    if (b.Rounds <= 0) RemoveBuff(f, i);
                }
            }
        }

        private Fighter DefaultTarget(Fighter f)
        {
            foreach (var o in F) if (o.Side != f.Side && o.Alive && o.Targetable) return o;
            return null;
        }

        private Fighter CharmTarget(Fighter f)
        {
            var options = new List<Fighter>();
            foreach (var o in F) if (o.Side == f.Side && o != f && o.Alive && o.Targetable) options.Add(o);
            return options.Count == 0 ? null : options[R.Range(0, options.Count)];
        }

        // ---- statuses over time ---------------------------------------------------------------

        /// <summary>DoT ticks at the start of the fighter's turn (Time Dilation: twice; its decay afterwards). False if it died.</summary>
        private bool TickStatuses(Fighter f)
        {
            for (int i = 0; i < f.Statuses.Count; i++)
            {
                var st = f.Statuses[i];
                if (!T.Statuses.TryGetValue(st.Id, out var def) || def.DotAtkPct <= 0) continue;
                var src = st.Source >= 0 && st.Source < F.Count ? F[st.Source] : null;
                var rs = RulesOf(src);
                int ticks = rs != null && rs.DotTicks > 1 ? rs.DotTicks : 1;
                // Bloodtide Fin: at max (awakened: 5+) stacks the status ticks twice
                var sr = SRules(st);
                if (sr.DotTwiceAt >= 0 && T.Statuses.TryGetValue(st.Id, out var sdef2) && st.Stacks >= (sr.DotTwiceAt == 0 ? CapOf(sdef2, sr) : sr.DotTwiceAt)) ticks = Math.Max(ticks, 2);
                for (int k = 0; k < ticks; k++)
                {
                    if (!f.Statuses.Contains(st) || !TickOk(f, st.Id)) break; // at most 3 ticks per enemy per round
                    TickOne(f, st, def, 1, null);
                    if (!f.Alive || Over) return false;
                }
            }
            // Time Dilation's cost: permanent stacks of its owner's statuses decay each round
            for (int i = f.Statuses.Count - 1; i >= 0; i--)
            {
                var st = f.Statuses[i];
                if (!T.Statuses.TryGetValue(st.Id, out var def) || !def.Permanent) continue;
                var src = st.Source >= 0 && st.Source < F.Count ? F[st.Source] : null;
                var rs = RulesOf(src);
                if (rs != null && rs.Decay > 0) RemoveStacks(src, f, st.Id, rs.Decay, null);
            }
            return true;
        }

        /// <summary>One DoT tick: stacks × power × the status's DoT %, its applier's rules (damage %, can crit, shields).</summary>
        private void TickOne(Fighter f, StatusInst st, StatusDef def, double scale, string skillId)
        {
            var r = SRules(st);
            Fighter src = st.Source >= 0 && st.Source < F.Count ? F[st.Source] : null;
            double dmg = st.Power * def.DotAtkPct / 100.0 * st.Stacks * (1 + r.DotPct / 100.0) * scale
                * CombatMath.DamageReductionFactor(f.S(StatId.DamageReduction), T) * TakenMultiplier(f);
            bool crit = false;
            var srs = RulesOf(src);
            if ((r.DotCrit || (srs != null && srs.StatusCrit)) && src != null) // Dead Reckoning: every status damage can crit
            {
                var cs = CritSource(src);
                crit = R.Chance(CombatMath.CritProbability(cs.S(StatId.CritChance), T));
                if (crit) dmg *= CombatMath.CritMultiplier(T.BaseCritDamage + cs.S(StatId.CritDamage), f.S(StatId.CritResist));
            }
            double dealt = DealDamage(src, f, dmg, def.IgnoresShield && !r.KeepsShields, BattleAction.StatusTick, crit, st.Id, skillId);
            StatusDamage(src, dealt);
            // parts v2 (Decay Stinger): the applier's per-tick triggers
            var tickOwner = src != null ? (src.Sidekick || src.Rider ? FirstWorm(src.Side) ?? src : src) : null;
            if (tickOwner != null && tickOwner.Alive && !Over && f.Alive) Fire(tickOwner, TriggerKind.OnStatusTick, f, st.Id, dealt);
        }

        // ---- attacks --------------------------------------------------------------------------

        private double StylePct(AttackStyle style, AttackKind kind)
        {
            if (!T.Styles.TryGetValue(style, out var s)) s = new AttackStyleDef();
            switch (kind)
            {
                case AttackKind.Rage: return s.Rage;
                case AttackKind.Combo: return s.Combo;
                case AttackKind.Counter: return s.Counter;
                default: return s.Basic;
            }
        }

        private static BattleAction ActionFor(AttackKind kind)
        {
            switch (kind)
            {
                case AttackKind.Rage: return BattleAction.Rage;
                case AttackKind.Combo: return BattleAction.Combo;
                case AttackKind.Counter: return BattleAction.Counter;
                case AttackKind.Skill:
                case AttackKind.Arc: return BattleAction.Skill;
                default: return BattleAction.Basic;
            }
        }

        /// <summary>Crit roll (Last Light: below its HP threshold every hit crits; Winter's Grip: a chilled fighter can't crit).</summary>
        private bool RollCrit(Fighter att, Fighter def = null, bool extra = false, bool attack = false)
        {
            var cs = CritSource(att);
            double chance = cs.S(StatId.CritChance);
            if (extra && att.Role == CombatRole.Worm) chance += att.Rules.ExtraCritPct; // Sharp Noodle: extra hits crit more
            if (def != null && (att.Role == CombatRole.Worm || att.Rider) && _mark[(int)att.Side] == def.Index)
            {
                var m = MountOf(att.Side);
                if (m != null) chance += m.Rules.SpotlightPct; // Spotlight: hits on the mark
            }
            bool crit = R.Chance(CombatMath.CritProbability(chance, T));
            if (ShutDown(att)) return false;
            var rs = RulesOf(att);
            if (rs != null && rs.LastLightPct > 0 && !att.Sidekick && att.HpPct < rs.LastLightPct) return true;
            if (attack && SureCrit(att, def)) return true; // parts v2: sure crits on attacks
            return crit;
        }

        /// <summary>Hit order: crit roll → block → dodge roll → damage; then on-hit triggers and the counter chain. Returns the damage dealt.</summary>
        private double Attack(Fighter att, Fighter def, AttackKind kind, int counterDepth, double mult = 1)
        {
            if (!att.Alive || !def.Alive || Over) return 0;
            Fire(att, TriggerKind.OnAttack, def);
            if (!att.Alive || !def.Alive || Over) return 0;
            att.Strikes++;
            // an extra hit (hero & mount kits): a combo strike, a counter, an Echo, a mount-made attack
            bool extra = kind == AttackKind.Combo || kind == AttackKind.Counter || att.ExtraDepth > 0;

            bool crit = RollCrit(att, def, extra, true);
            var prevKind = att.CurKind;
            att.CurKind = kind;
            try
            {
            if (kind == AttackKind.Rage) att.RageCrit = false; // set once the rage attack lands
            if (def.Guards.Count > 0)
            {
                // a blocking sidekick intercepts the hit: nothing lands, no on-hit or when-hit triggers
                var blocker = F[def.Guards[0]];
                def.Guards.RemoveAt(0);
                double stopped = ComputeDamage(att, def, StylePct(att.Style, kind), 0, crit, kind, false) * mult;
                var b = Log(BattleAction.Block, blocker, def, stopped);
                if (b != null) { b.Crit = crit; b.Stacks = def.Guards.Count; }
                Fire(def, TriggerKind.OnBlock, att, StatusId.None, stopped);
                StillnessHit(def);
                return 0;
            }
            // parts v2: forced misses (Quantum Swift Legs, Flicker Treads, Ashwalkers; at most 1 fully avoided hit per round)
            bool forcedMiss = ForcedMiss(def);
            double dodgeP = forcedMiss ? 0 : DodgeChance(att, def);
            if (forcedMiss || (dodgeP > 0 && R.Chance(dodgeP)))
            {
                def.ConsecutiveDodges++;
                var e = Log(ActionFor(kind), att, def, 0);
                if (e != null) e.Dodge = true;
                EndBuffs(def, false, true, false);
                bool prevForced = _forcedMiss;
                _forcedMiss = forcedMiss;
                _dodgeDepth++;
                try { Fire(def, TriggerKind.OnDodge, att); }
                finally { _dodgeDepth--; _forcedMiss = prevForced; }
                StillnessHit(def);
                EndBuffs(att, false, false, true);
                return 0;
            }
            def.ConsecutiveDodges = 0;

            double defIgnore = Math.Max(att.NextDefIgnore, crit ? att.Rules.CritDefIgnorePct : 0);
            att.NextDefIgnore = 0;
            double dmg = ComputeDamage(att, def, StylePct(att.Style, ComboFull(att, kind) ? AttackKind.Basic : kind), 0, crit, kind, false, defIgnore) * mult;
            if (att.NextHitBonus > 0) { dmg *= 1 + att.NextHitBonus / 100.0; att.NextHitBonus = 0; } // parts v2: "your next hit deals double damage"
            if (kind == AttackKind.Counter && att.CounterCharge > 0) { dmg *= 1 + att.CounterCharge / 100.0; att.CounterCharge = 0; } // Grudge
            if (att.Role == CombatRole.Worm)
            {
                // Pile-On (Shadow Noodle): each hit on the same enemy in one turn deals more than the last
                if (att.Rules.PileOnPct > 0 && _acting == att)
                {
                    if (att.PileTarget == def.Index) att.PileCount++; else { att.PileTarget = def.Index; att.PileCount = 0; }
                    dmg *= 1 + Math.Min(att.Rules.PileOnMax > 0 ? att.Rules.PileOnMax : double.MaxValue, att.Rules.PileOnPct * att.PileCount) / 100.0;
                }
                // Slipstream (Rift Racer 3★+): the worm's first attack of the battle hits harder
                if (!att.Attacked) { var sm = MountOf(att.Side); if (sm != null && sm.Rules.SlipstreamPct > 0) dmg *= 1 + sm.Rules.SlipstreamPct / 100.0; }
            }
            if (def.GuardHits > 0 && def.GuardPct > 0)
            {
                // a guard (aliens pass) softens the hit; it still lands, so on-hit / when-hit triggers and counters fire
                // Broodguard Legs: a sidekick's guard stops the whole hit (a fully avoided hit: 1 per round, else its own %)
                bool kickGuard = def.GuardBy >= 0 && def.GuardBy < F.Count && F[def.GuardBy].Sidekick;
                double gpct = kickGuard && def.Rules.GuardFull && AvoidOk(def) ? 100 : Math.Min(100, def.GuardPct);
                double prevented = dmg * gpct / 100.0;
                if (kickGuard && def.Rules.GuardReflectPct > 0 && prevented > 0 && att.Alive && !Over)
                    DealDamage(def, att, prevented * def.Rules.GuardReflectPct / 100.0 * CombatMath.DamageReductionFactor(att.S(StatId.DamageReduction), T) * TakenMultiplier(att),
                        false, BattleAction.Skill, false, StatusId.None, null);
                dmg -= prevented;
                def.GuardHits--;
                var gl = Log(BattleAction.Guarded, def.GuardBy >= 0 && def.GuardBy < F.Count ? F[def.GuardBy] : def, def, prevented);
                if (gl != null) gl.Stacks = def.GuardHits;
                if (def.GuardHits <= 0) { def.GuardPct = 0; def.GuardBy = -1; }
                Fire(def, TriggerKind.OnBlock, att, StatusId.None, prevented);
                if (Over || !def.Alive || !att.Alive) return 0;
            }
            else dmg = DroneGuard(def, dmg); // Launch Bay drones (5★+) guard one hit, then break
            double dealt = DealDamage(att, def, dmg, false, ActionFor(kind), crit, StatusId.None, null);
            double absorbed = _lastAbsorbed;
            bool broke = _lastBroke;
            MarkFirstHit(att, def);
            if (crit) { att.CritThisTurn = true; EndBuffs(att, true, false, false); }
            def.LastTakenCrit = crit; def.LastTakenKind = kind; def.LastTakenDealt = dealt;
            if (def.Role == CombatRole.Worm)
            {
                def.LastAttacker = att.Index;
                if (!def.HitBy.Contains(att.Index)) def.HitBy.Add(att.Index);
            }
            // Payback Coil (Aegis Walker 5★+): the worm's counterattacks store a share of their damage
            if (kind == AttackKind.Counter && att.Role == CombatRole.Worm && dealt > 0)
            {
                var cm = MountOf(att.Side);
                if (cm != null && cm.Rules.CoilCounterPct > 0) StoreCoil(att, dealt * cm.Rules.CoilCounterPct / 100.0);
            }
            att.LastKind = kind; att.LastTarget = def.Index; att.Attacked = true;
            if (kind == AttackKind.Rage) att.RageCrit = crit;
            if (dealt > att.BestDealt) { att.BestDealt = dealt; att.BestKind = kind; att.BestTarget = def.Index; }
            double ls = att.S(StatId.LifeSteal);
            // untargetable fighters (aliens, mount) are never hurt: their lifesteal feeds the side's worm (Vampiric aliens)
            var leech = att.Targetable ? att : (FirstWorm(att.Side) ?? att);
            if (dealt > 0 && ls > 0 && att.Alive) Heal(att, leech, dealt * ls / 100.0, BattleAction.LifeSteal, null, false);
            if (def.Alive) GainRage(def, T.RagePerHitTaken);
            if (def.HitsTakenRoundNo != Round) { def.HitsTakenRoundNo = Round; def.HitsTakenRound = 0; }
            def.HitsTakenRound++;
            def.HitHistory.Add(dealt);
            def.HitFrom.Add(att.Index);
            if (def.HitHistory.Count > 3) { def.HitHistory.RemoveAt(0); def.HitFrom.RemoveAt(0); }
            att.HitsLanded++;

            var ar = RulesOf(att);
            bool procs = ar == null || ar.ProcCap <= 0 || att.Strikes <= ar.ProcCap; // Endless Chain: no on-hit effects past the cap
            bool wormExtra = extra && att.Role == CombatRole.Worm;
            if (procs)
            {
                if (wormExtra) _extraHitDepth++; // Double Tap: the statuses this extra hit applies count twice
                try
                {
                    Fire(att, TriggerKind.OnHit, def, StatusId.None, dealt);
                    if (crit) Fire(att, TriggerKind.OnCrit, def, StatusId.None, dealt);
                }
                finally { if (wormExtra) _extraHitDepth--; }
                RunArmed(att, def, dealt); // parts v2: riders armed for the next hit
                // Seasoning / Turning Sand: the next hit carries the signatures' on-hit N times
                if (att.PendingCarry > 0 && def.Alive && !Over) { int pc = att.PendingCarry; att.PendingCarry = 0; Carry(att, def, pc); }
            }
            if (wormExtra && !Over && att.Alive)
            {
                if (_acting == att) att.ExtraHitsTurn++;
                Fire(att, TriggerKind.OnExtraHit, def, StatusId.None, dealt);
            }
            if (absorbed > 0)
            {
                Fire(def, TriggerKind.OnShieldHit, att, StatusId.None, absorbed);
                if (broke && !Over && def.Alive) Fire(def, TriggerKind.OnShieldBreak, att, StatusId.None, absorbed);
                if (def.Rules.AbilityShieldHits > 0 && !Over && def.Alive) AbilityCount(def, 1, def.Rules.AbilityShieldHits); // Starfall's count
            }
            StillnessHit(def); // Mirror Palm's count: every enemy attack on the worm
            Fire(def, TriggerKind.OnDamaged, att, StatusId.None, dealt);

            EndBuffs(att, false, false, true);
            Repeat(att, def, kind, mult);
            if (Over || !def.Alive || !att.Alive || counterDepth >= T.MaxChain) return dealt;
            var dr = def.Rules;
            bool window = def.CounterWindow || def.CounterWindowUntil >= Round;
            bool always = counterDepth == 0 && (dr.CounterAll || window || def.ForceCounterHit) && !ShutDown(def);
            def.ForceCounterHit = false;
            double chance = ShutDown(def) ? 0 : def.S(StatId.CounterChance) + (dr.ComboAsCounter ? def.S(StatId.ComboChance) : 0);
            if (dr.FirstHitCounterPct > 0 && def.HitsTakenRoundNo == Round && def.HitsTakenRound == 1) chance += dr.FirstHitCounterPct; // Feint Mask
            double cp = CombatMath.Clamp(chance, 0, 95) / 100.0 * CombatMath.PowInt(T.ChainDecay, counterDepth);
            if (always || (cp > 0 && R.Chance(cp)))
            {
                var ct = att.Targetable ? att : DefaultTarget(def);
                if (ct != null)
                {
                    // Backlash Hammer: at full rage the counter is the rage attack (awakened: +50%)
                    double rc = RageCost(def);
                    if (dr.RageCounter && counterDepth == 0 && def.Role == CombatRole.Worm && def.Rage >= rc)
                    {
                        def.Rage -= rc;
                        RageAttack(def, ct, 1 + dr.RageCounterPct / 100.0, true);
                    }
                    else
                    {
                        double cd = Attack(def, ct, AttackKind.Counter, counterDepth + 1);
                        Fire(def, TriggerKind.OnCounter, ct, StatusId.None, cd);
                    }
                    // Rampart Carapace: shielded (awakened: always) counterattacks hit every enemy
                    if ((dr.CounterAoe || (dr.CounterAoeShielded && def.Shield > 0)) && !Over && def.Alive)
                        foreach (var o in new List<Fighter>(F))
                        {
                            if (Over || !def.Alive) break;
                            if (o == ct || o.Side == def.Side || !o.Alive || !o.Targetable) continue;
                            double od = Attack(def, o, AttackKind.Counter, T.MaxChain);
                            Fire(def, TriggerKind.OnCounter, o, StatusId.None, od);
                        }
                    // Perfect Form: a counterattack can combo (those strikes are never countered back)
                    if (dr.CounterCombo && !Over && def.Alive && ct.Alive) ComboChain(def, ct, false, T.MaxChain);
                }
            }
            return dealt;
            }
            finally { att.CurKind = prevKind; }
        }

        /// <summary>Quantum Swift Blaster: an attack made twice (once; the repeat is an extra hit and never repeats itself).</summary>
        private void Repeat(Fighter att, Fighter def, AttackKind kind, double mult)
        {
            if (!att.RepeatPending || Over || !att.Alive) return;
            att.RepeatPending = false;
            var tg = def.Alive ? def : DefaultTarget(att);
            if (tg == null) return;
            att.ExtraDepth++;
            try { Attack(att, tg, kind == AttackKind.Skill || kind == AttackKind.Arc ? AttackKind.Basic : kind, T.MaxChain, mult * att.RepeatPct / 100.0); }
            finally { att.ExtraDepth--; att.RepeatPending = false; }
        }

        /// <summary>Afterburner Tail: on its rhythm turns combo strikes deal full attack damage (the basic multiplier).</summary>
        private bool ComboFull(Fighter att, AttackKind kind) =>
            kind == AttackKind.Combo && att.Rules.ComboFullEvery > 0 && att.Turns > 0 && att.Turns % att.Rules.ComboFullEvery == 0;

        /// <summary>
        /// Hit damage. A sidekick's hit (aliens pass) is atkPct % (× its AtkPct %) of a full-strength hit at its worm's ATK × alien power:
        /// "15% ATK" is 15% of what that hit would deal through the target's DEF, never floored away by it.
        /// </summary>
        private double ComputeDamage(Fighter att, Fighter def, double atkPct, double bonusPct, bool crit, AttackKind kind, bool trueDamage, double defIgnorePct = 0)
        {
            // a mount has no stats: its crits (the worm's crit chance, or always-crit shots) deal the base crit damage only
            double critDmg = att.Rider ? 0 : att.S(StatId.CritDamage);
            // Deadeye Visor: crit chance above 100% becomes crit damage (read before conversions)
            if (!att.Rider && att.Rules.CritOverflow > 0) critDmg += Math.Max(0, att.S(StatId.CritChance) - 100) * att.Rules.CritOverflow;
            double critMult = CombatMath.CritMultiplier(T.BaseCritDamage + critDmg, def.S(StatId.CritResist));
            double share = 1;
            if (att.Sidekick || att.Rider) { share = atkPct / 100.0 * att.AtkPct / 100.0; atkPct = 100; } // the mount's "X% ATK" like a sidekick's
            double defStat = def.S(StatId.Def) * (defIgnorePct > 0 ? Math.Max(0, 1 - defIgnorePct / 100.0) : 1);
            return CombatMath.HitDamage(AtkOf(att), atkPct, bonusPct, defStat, def.S(StatId.DamageReduction), trueDamage,
                crit, critMult, CondMultiplier(att, def, kind), TakenMultiplier(def), T) * DealtMultiplier(att) * share * DefendMultiplier(RulesOwner(att), def);
        }

        /// <summary>Damage the fighter deals, from its statuses (Chill: −3% per stack, scaled by the chiller's ice damage).</summary>
        private double DealtMultiplier(Fighter att)
        {
            double sum = att.S(StatId.DamageDealt); // parts v2 (Quantum Guard Helm's opener on enemies)
            foreach (var st in att.Statuses)
                if (T.Statuses.TryGetValue(st.Id, out var sd) && sd.DamageDealtPctPerStack != 0)
                    sum += sd.DamageDealtPctPerStack * st.Stacks * (AtkBased(sd) || st.Power <= 0 ? 1 : st.Power);
            return Math.Max(0, 1 + sum / 100.0);
        }

        /// <summary>Statuses whose power is the applier's element-scaled ATK (DoTs, Supernova); others store an element multiplier.</summary>
        private static bool AtkBased(StatusDef d) => d.DotAtkPct > 0 || d.BurstAtStacks > 0;

        private double CondMultiplier(Fighter att, Fighter def, AttackKind kind)
        {
            double sum = 0;
            if (def.Role == CombatRole.Boss) sum += att.S(StatId.BossDamage);
            double ratio = def.MaxHp > 0 ? def.Hp / def.MaxHp * 100.0 : 0;
            foreach (var c in att.Conds)
            {
                if (c.If != null && !Check(att, def, c.If)) continue;
                double v = c.Value;
                bool ok;
                switch (c.Cond)
                {
                    case "vs_boss": ok = def.Role == CombatRole.Boss; break;
                    case "vs_elite": ok = def.Role == CombatRole.Elite || def.Role == CombatRole.Boss; break;
                    case "vs_high_hp": ok = ratio > T.HighHpCondPct; break;
                    case "vs_low_hp": ok = ratio < T.LowHpCondPct; break;
                    case "vs_shielded": ok = def.Shield > 0; break;
                    case "vs_status": ok = def.Status(c.Status) != null; break;
                    case "per_stack": { var st = def.Status(c.Status); ok = st != null; v = ok ? c.Value * st.Stacks : 0; break; }
                    case "status_count": { int n = CountStatuses(def); ok = n > 0; v = c.Value * n; break; }
                    case "basic": ok = kind == AttackKind.Basic; break;
                    case "combo": ok = kind == AttackKind.Combo; break;
                    case "counter": ok = kind == AttackKind.Counter; break;
                    case "rage": ok = kind == AttackKind.Rage; break;
                    case "skill": ok = kind == AttackKind.Skill || kind == AttackKind.Arc; break;
                    case "arc": ok = kind == AttackKind.Arc; break;
                    case "if": ok = true; break;
                    // parts v2: Last Stand Plate (+Value % per N % HP missing), Foresight Visor (Echo hits)
                    case "missing_hp": { ok = c.Per > 0; v = ok ? c.Value * Math.Floor(Math.Max(0, 100 - att.HpPct) / c.Per) : 0; break; }
                    case "echo": ok = att.EchoDepth > 0; break;
                    default: ok = false; break;
                }
                if (ok) sum += v;
            }
            return Math.Max(0, 1 + sum / 100.0);
        }

        private double TakenMultiplier(Fighter def)
        {
            double sum = def.S(StatId.DamageTaken);
            foreach (var st in def.Statuses)
                if (T.Statuses.TryGetValue(st.Id, out var sd) && sd.DamageTakenPctPerStack != 0)
                {
                    double v = sd.DamageTakenPctPerStack * st.Stacks * (!AtkBased(sd) && sd.HasElement && st.Power > 0 ? st.Power : 1);
                    // Superconductor: Shock on a chilled or frozen fighter counts double
                    if (st.Id == StatusId.Shock && SRules(st).DoubleVsChill && (def.Status(StatusId.Chill) != null || def.Status(StatusId.Freeze) != null)) v *= 2;
                    sum += v;
                }
            return Math.Max(0, 1 + sum / 100.0);
        }

        private int CountStatuses(Fighter f)
        {
            int n = 0;
            if (f.Alive) { foreach (var st in f.Statuses) if (st.Stacks > 0) n++; }
            else if (f.DeathStatuses != null) n = f.DeathStatuses.Count;
            return n;
        }

        private double DealDamage(Fighter src, Fighter target, double amount, bool ignoreShield, BattleAction action, bool crit, StatusId status, string skillId)
        {
            _lastAbsorbed = 0;
            _lastBroke = false;
            if (!target.Alive) return 0;
            amount = Math.Max(0, amount);
            // Quantum Guard Shell: no single hit takes more than N % of max HP (applied last, after every damage-taken modifier)
            if (target.Rules.HitCapPct > 0 && action != BattleAction.StatusTick) amount = Math.Min(amount, target.MaxHp * target.Rules.HitCapPct / 100.0);
            double absorbed = 0;
            if (!ignoreShield && target.Shield > 0) { absorbed = Math.Min(target.Shield, amount); target.Shield -= absorbed; _lastBroke = target.Shield <= 0.5; }
            double hpLoss = Math.Min(target.Hp, amount - absorbed);
            double hpBefore = target.Hp;
            target.Hp -= hpLoss;
            double dealt = absorbed + hpLoss;
            if (src != null) src.LastHitCrit = crit;
            if (src != null) src.DamageDealt += dealt;
            if (src != null && src.Side == BattleSide.Player && target.Side == BattleSide.Enemy) Res.PlayerDamageDealt += (long)Math.Round(dealt);
            if (target.Side == BattleSide.Player) Res.PlayerDamageTaken += (long)Math.Round(dealt);
            var e = Log(action, src, target, dealt);
            if (e != null) { e.Crit = crit; e.Status = status; e.SkillId = skillId; }

            // Payback Coil (Aegis Walker): a share of every damage the worm takes, shielded hits included; Supernova recoil counts more
            if (target.Role == CombatRole.Worm && dealt > 0 && src != null && src.Side != target.Side)
            {
                var cm = MountOf(target.Side);
                if (cm != null && cm.Rules.CoilPct > 0)
                    StoreCoil(target, dealt * cm.Rules.CoilPct / 100.0 * (action == BattleAction.Supernova && cm.Rules.CoilRecoilX > 0 ? cm.Rules.CoilRecoilX : 1));
            }
            if (target.Hp < 0.5 && target.HasFatal && !Over)
            {
                // a fatal hit: OnFatal effects (Last Word, Sacrifice Protocol, Loop, kit saves) may save the fighter — one save per battle
                target.Hp = 0;
                double prevFatal = _fatalHpBefore;
                _fatalHpBefore = hpBefore;
                Fire(target, TriggerKind.OnFatal, src, StatusId.None, amount);
                _fatalHpBefore = prevFatal;
            }
            if (target.Hp < 0.5)
            {
                target.Hp = 0;
                target.Alive = false;
                target.DeathStatuses = new List<StatusInst>(target.Statuses);
                target.Statuses.Clear();
                Log(BattleAction.Death, src, target, 0);
                CheckOver();
                var prevKill = _killAction;
                _killAction = action;
                if (src != null && src.Alive) Fire(src, TriggerKind.OnKill, target);
                _killAction = prevKill;
                // Index loop over the fighters present at the death: an OnEnemyDeath trigger may summon (F grows), which would break
                // a foreach ("Collection was modified"); fighters summoned by these triggers do not react to this death.
                for (int i = 0, n = F.Count; i < n; i++) { var o = F[i]; if (o.Alive && o.Side != target.Side && !Over) Fire(o, TriggerKind.OnEnemyDeath, target); }
                MarkDied(target); // Spotlight moves on; Contagion carries the statuses over
                // Overkill: damage past the killed fighter's HP carries over to the next enemy
                var ors = RulesOf(src);
                double excess = amount - absorbed - hpLoss;
                // Quantum Mend Drill: overkill damage heals the killer
                var ko = RulesOwner(src);
                if (ko != null && ko.Rules.OverkillHealPct > 0 && excess >= 1 && ko.Alive && ko.Side != target.Side)
                    Heal(ko, ko, excess * ko.Rules.OverkillHealPct / 100.0, BattleAction.Heal, null, false);
                if (ors != null && ors.Overkill && excess >= 1 && !Over && src.Side != target.Side)
                {
                    var next = DefaultTarget(src);
                    if (next != null) DealDamage(src, next, excess, false, BattleAction.Skill, false, StatusId.None, ors.OverkillSkill);
                }
            }
            else if (!target.LowHpFired && target.Hp < target.MaxHp * T.LowHpTriggerPct / 100.0)
            {
                target.LowHpFired = true;
                Fire(target, TriggerKind.OnLowHp, src);
            }
            if (target.Alive && !Over && target.Role != CombatRole.Boss) StatusExecute(target);
            if (absorbed > 0) ShieldParts(target, src); // siphon / ice-wall parts of the shield; a broken ice wall bursts
            _lastAbsorbed = absorbed;
            return dealt;
        }

        private bool _lastBroke;

        /// <summary>Decay Stinger: a non-boss enemy carrying N+ of the rule's status dies below the rule's HP % (the highest threshold applies).</summary>
        private void StatusExecute(Fighter target)
        {
            var w = FirstWorm(target.Side == BattleSide.Player ? BattleSide.Enemy : BattleSide.Player);
            if (w == null || !w.Alive) return;
            var r = w.Rules;
            if (r.ExecuteStatus == StatusId.None || r.ExecuteBelowPct <= 0 || target.HpPct >= r.ExecuteBelowPct) return;
            var st = target.Status(r.ExecuteStatus);
            if (st == null || st.Stacks < r.ExecuteAt) return;
            target.Shield = 0;
            DealDamage(w, target, target.Hp + 1, true, BattleAction.Execute, false, StatusId.None, null);
        }

        private void Heal(Fighter src, Fighter target, double amount, BattleAction action, string skillId, bool overhealToShield, bool siphon = false)
        {
            if (!target.Alive || amount <= 0) return;
            var tr = RulesOf(target);
            if (tr != null && tr.LastLightPct > 0 && !target.Sidekick && target.HpPct < tr.LastLightPct) return; // Last Light: no healing
            foreach (var st in target.Statuses)
                if (T.Statuses.TryGetValue(st.Id, out var sd) && sd.HealReductionPct > 0) amount *= 1 - Math.Min(100, sd.HealReductionPct) / 100.0;
            amount *= Math.Max(0, 1 + target.S(StatId.HealReceived) / 100.0);
            double healed = Math.Min(Math.Max(0, target.MaxHp - target.Hp), amount);
            target.Hp += healed;
            double extra = overhealToShield ? amount - healed : 0;
            if (extra > 0 && siphon)
            {
                // Siphon Line (5★+): overheal becomes a shield up to the mount's cap (% max HP)
                var sm = MountOf(target.Side);
                double cap = sm != null ? sm.Rules.SiphonCapPct / 100.0 * target.MaxHp : 0;
                extra = Math.Max(0, Math.Min(extra, cap - target.SiphonShield));
                target.SiphonShield += extra;
            }
            // Quantum Mend Helm: healing past full HP becomes a shield, up to the caps of every source (they add up)
            if (!overhealToShield && amount - healed > 0 && target.Rules.OverhealCapPct > 0)
            {
                double room = target.MaxHp * target.Rules.OverhealCapPct / 100.0 - target.OverhealShield;
                double add = Math.Max(0, Math.Min(amount - healed, room));
                target.OverhealShield += add;
                extra += add;
            }
            if (extra > 0) target.Shield += extra;
            // Guiding Star (Star Knight 7★): every 10% max HP healed counts as one hit absorbed by the shield
            if (healed > 0 && target.Rules.HealsCountPct > 0 && target.Rules.AbilityShieldHits > 0 && target.MaxHp > 0)
            {
                target.HealAcc += healed;
                double step = target.MaxHp * target.Rules.HealsCountPct / 100.0;
                int n = 0;
                while (target.HealAcc >= step) { target.HealAcc -= step; n++; }
                if (n > 0 && !Over)
                {
                    var e0 = Log(action, src, target, healed + extra);
                    if (e0 != null) e0.SkillId = skillId;
                    AbilityCount(target, n, target.Rules.AbilityShieldHits);
                    return;
                }
            }
            if (healed > 0 || extra > 0)
            {
                var e = Log(action, src, target, healed + extra);
                if (e != null) e.SkillId = skillId;
            }
        }

        private void GainRage(Fighter f, double baseAmount)
        {
            AddRage(f, baseAmount * (1 + f.S(StatId.RageGain) / 100.0));
        }

        // ---- skills ---------------------------------------------------------------------------

        private static bool IsBattleEffect(EffectKind k)
        {
            switch (k)
            {
                case EffectKind.Damage:
                case EffectKind.TrueDamage:
                case EffectKind.ApplyStatus:
                case EffectKind.Heal:
                case EffectKind.Shield:
                case EffectKind.GainRage:
                case EffectKind.BuffStat:
                case EffectKind.Summon:
                case EffectKind.RemoveStatus:
                case EffectKind.ConsumeStatus:
                case EffectKind.Strike:
                case EffectKind.AllyAction:
                case EffectKind.Block:
                case EffectKind.BlockStatus:
                case EffectKind.ForceSupernova:
                case EffectKind.MultiplyStatus:
                case EffectKind.TransferStatus:
                case EffectKind.TickStatus:
                case EffectKind.Execute:
                case EffectKind.Reflect:
                case EffectKind.CheatDeath:
                case EffectKind.FreeRageAttack:
                case EffectKind.Charge:
                case EffectKind.LoseHp:
                case EffectKind.StackAll:
                case EffectKind.Sacrifice:
                case EffectKind.Echo:
                case EffectKind.Rewind:
                case EffectKind.Loop:
                case EffectKind.Guard:
                case EffectKind.CarrySignatures:
                case EffectKind.AdvanceTimer:
                case EffectKind.MountFire:
                case EffectKind.FireAbility:
                case EffectKind.RiderAttack:
                case EffectKind.Whistle:
                case EffectKind.SpreadStatus:
                case EffectKind.ChargeAllies:
                case EffectKind.GainRunCoins:
                // parts v2
                case EffectKind.ExtraTurn:
                case EffectKind.SureCrit:
                case EffectKind.ForceMiss:
                case EffectKind.Empower:
                case EffectKind.TurnFlag:
                case EffectKind.ForceCombo:
                case EffectKind.Repeat:
                case EffectKind.MultiplyShield:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True if a battle effect kind is executed by the simulator (tests: every content effect does something).</summary>
        internal static bool IsBattleEffectKind(EffectKind k) => IsBattleEffect(k);

        /// <summary>The first ';'-separated token of a Param (the status filter of OnStatusApplied / OnStatusConsumed).</summary>
        internal static string ParamHead(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            int i = p.IndexOf(';');
            return i < 0 ? p : p.Substring(0, i);
        }

        internal static bool HasToken(string p, string token)
        {
            if (string.IsNullOrEmpty(p)) return false;
            foreach (var s in p.Split(';')) if (s.Trim() == token) return true;
            return false;
        }

        /// <summary>OnStackThreshold's Param "Status:N".</summary>
        internal static bool ParseThreshold(string p, out StatusId status, out int n)
        {
            status = StatusId.None; n = 0;
            var head = ParamHead(p);
            if (string.IsNullOrEmpty(head)) return false;
            var a = head.Split(':');
            return a.Length == 2 && Enum.TryParse(a[0], out status) && int.TryParse(a[1], out n) && n > 0;
        }

        private bool Check(Fighter owner, Fighter ctx, string cond)
        {
            foreach (var t in BattleConditions.Parse(cond))
            {
                bool ok;
                switch (t.Kind)
                {
                    case BattleConditions.Kind.Has: { var st = ctx?.Status(t.Status); ok = st != null && st.Stacks >= t.N; break; }
                    case BattleConditions.Kind.Lacks: ok = ctx == null || ctx.Status(t.Status) == null; break;
                    case BattleConditions.Kind.Max:
                    {
                        var st = ctx?.Status(t.Status);
                        ok = st != null && T.Statuses.TryGetValue(t.Status, out var sd) && st.Stacks >= CapOf(sd, SRules(st));
                        break;
                    }
                    case BattleConditions.Kind.Unblocked: ok = ctx != null && ctx.StatusCd[(int)t.Status] <= Round; break;
                    case BattleConditions.Kind.HpBelow: ok = ctx != null && ctx.HpPct < t.N; break;
                    case BattleConditions.Kind.Boss: ok = ctx != null && ctx.Role == CombatRole.Boss; break;
                    case BattleConditions.Kind.NotBoss: ok = ctx != null && ctx.Role != CombatRole.Boss; break;
                    case BattleConditions.Kind.Statuses: ok = ctx != null && CountStatuses(ctx) >= t.N; break;
                    case BattleConditions.Kind.SelfHpBelow: ok = owner.HpPct < t.N; break;
                    case BattleConditions.Kind.SelfShielded: ok = owner.Shield > 0; break;
                    case BattleConditions.Kind.Sidekicks:
                    {
                        ok = false;
                        foreach (var o in F) if (o.Side == owner.Side && o.Alive && o.Sidekick) { ok = true; break; }
                        break;
                    }
                    case BattleConditions.Kind.OneEnemy:
                    {
                        int n = 0;
                        foreach (var o in F) if (o.Side != owner.Side && o.Alive && o.Targetable) n++;
                        ok = n == 1;
                        break;
                    }
                    case BattleConditions.Kind.FirstTurn: ok = owner.Turns == 1; break;
                    case BattleConditions.Kind.FirstHit: ok = owner.HitsLanded == 1; break;
                    case BattleConditions.Kind.FirstHitTaken: ok = owner.HitsTakenRoundNo == Round && owner.HitsTakenRound == 1; break;
                    case BattleConditions.Kind.ComboStrikes: ok = owner.ComboStrikes == (int)t.N; break;
                    case BattleConditions.Kind.RageCrit: ok = owner.RageCrit; break;
                    case BattleConditions.Kind.WormRage: { var w = owner.Role == CombatRole.Worm ? owner : FirstWorm(owner.Side); ok = w != null && w.Rage >= t.N - 1e-6; break; }
                    case BattleConditions.Kind.CritKill: ok = owner.LastHitCrit; break;
                    case BattleConditions.Kind.ExtraHits: ok = owner.ExtraHitsTurn >= (int)t.N; break;
                    case BattleConditions.Kind.SiphonFull: { var w = owner.Role == CombatRole.Worm ? owner : FirstWorm(owner.Side); ok = w != null && SiphonFull(w); break; }
                    // parts v2
                    case BattleConditions.Kind.AttackerSlower: ok = ctx != null && ctx.S(StatId.Speed) < owner.S(StatId.Speed); break;
                    case BattleConditions.Kind.AmountPct: ok = owner.MaxHp > 0 && _checkAmount > owner.MaxHp * t.N / 100.0; break;
                    case BattleConditions.Kind.CritIn: ok = owner.LastTakenCrit; break;
                    case BattleConditions.Kind.RageIn: ok = owner.LastTakenKind == AttackKind.Rage; break;
                    case BattleConditions.Kind.NovaKill: ok = _killAction == BattleAction.Supernova; break;
                    case BattleConditions.Kind.NovaRage: ok = owner.RageDepth > 0; break;
                    case BattleConditions.Kind.WithinHeat:
                    {
                        var st = ctx?.Status(StatusId.Heat);
                        ok = st != null && T.Statuses.TryGetValue(StatusId.Heat, out var hd) && BurstAtOf(hd, SRules(st)) - st.Stacks <= t.N;
                        break;
                    }
                    case BattleConditions.Kind.RageWithin: ok = owner.Rage >= RageCost(owner) - t.N - 1e-6; break;
                    case BattleConditions.Kind.TurnEvery: ok = owner.Turns > 0 && owner.Turns % (int)t.N == 0; break;
                    case BattleConditions.Kind.NoCritTurn: ok = !owner.CritThisTurn; break;
                    case BattleConditions.Kind.Forced: ok = _forcedMiss; break;
                    case BattleConditions.Kind.ComboMin: ok = owner.ComboStrikes >= (int)t.N; break;
                    case BattleConditions.Kind.HpAbove: ok = ctx != null && ctx.HpPct > t.N; break;
                    case BattleConditions.Kind.BasicHit: ok = owner.CurKind == AttackKind.Basic; break;
                    case BattleConditions.Kind.HitCrit: ok = owner.LastHitCrit; break;
                    case BattleConditions.Kind.InExtraTurn: ok = owner.InExtraTurn; break;
                    default: ok = false; break;
                }
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// Fires the owner's triggers of a kind; a worm's events also reach its mount's Rider triggers (hero &amp; mount kits: the mount
        /// listens to its worm's status applications, crits, rage attacks, fatal hits …; their gates read the worm).
        /// </summary>
        private void Fire(Fighter owner, TriggerKind kind, Fighter ctx, StatusId ctxStatus = StatusId.None, double amount = 0)
        {
            FireOn(owner, owner, false, kind, ctx, ctxStatus, amount);
            if (owner.Role == CombatRole.Worm && !_noRider && owner.Alive)
            {
                var m = MountOf(owner.Side);
                if (m != null && m.Alive && (!Over || kind == TriggerKind.OnBattleWon || kind == TriggerKind.OnKill)) FireOn(m, owner, true, kind, ctx, ctxStatus, amount);
            }
        }

        private static bool IsSave(EffectKind k) => k == EffectKind.CheatDeath || k == EffectKind.Sacrifice || k == EffectKind.Loop;

        private void FireOn(Fighter owner, Fighter gate, bool rider, TriggerKind kind, Fighter ctx, StatusId ctxStatus, double amount)
        {
            if (!owner.Alive) return;
            // the battle's last kill still pays its run coins (Plunder): only GainRunCoins fires after the end
            bool afterEnd = Over && kind != TriggerKind.OnBattleWon;
            if (afterEnd && kind != TriggerKind.OnKill) return;
            if (_depth >= 4) return;
            int thrPrev = _thrPrev;
            _depth++;
            try
            {
                for (int si = 0; si < owner.Skills.Count; si++)
                {
                    var skill = owner.Skills[si];
                    for (int ti = 0; ti < skill.Triggers.Count; ti++)
                    {
                        var t = skill.Triggers[ti];
                        if (t.When != kind || !IsBattleEffect(t.Effect) || t.Rider != rider) continue;
                        if (afterEnd && t.Effect != EffectKind.GainRunCoins) continue;
                        // one fatal-save per battle across every source (Last Word, Sacrifice, Loop, Last Constellation, Borrowed Sand, Cryo Pod)
                        if (kind == TriggerKind.OnFatal && IsSave(t.Effect) && gate.FatalSaved) continue;
                        // the status filter (a status name as the Param's head; kit tokens such as "round_cap:30" are no filter)
                        if ((kind == TriggerKind.OnStatusApplied || kind == TriggerKind.OnStatusConsumed || kind == TriggerKind.OnStatusTick)
                            && !string.IsNullOrEmpty(t.Param) && ParamHead(t.Param).IndexOf(':') < 0 && ParamHead(t.Param) != ctxStatus.ToString()) continue;
                        if (kind == TriggerKind.OnStackThreshold)
                        {
                            if (!ParseThreshold(t.Param, out var ts, out var tn) || ts != ctxStatus || !(thrPrev < tn && amount >= tn)) continue;
                        }
                        if (Round < owner.Ready[si][ti]) continue;
                        if (t.MaxPerBattle > 0 && owner.Fired[si][ti] >= t.MaxPerBattle) continue;
                        _checkAmount = amount;
                        if (!string.IsNullOrEmpty(t.If) && !Check(gate, ctx, t.If)) continue;
                        // parts v2: every Nth event per context fighter ("your 3rd counter on one enemy")
                        int everyTarget = (int)ParamNumber(t.Param, "every_target");
                        if (everyTarget > 0 && !EveryTarget(owner, t, ctx, everyTarget)) continue;
                        int times = 1;
                        if (t.EveryN > 0)
                        {
                            int units = kind == TriggerKind.OnStatusApplied ? Math.Max(1, (int)amount) : 1;
                            int before = owner.Counts[si][ti], after = before + units;
                            owner.Counts[si][ti] = after;
                            times = Math.Min(3, after / t.EveryN - before / t.EveryN);
                            if (times <= 0) continue;
                        }
                        double chance = t.Chance;
                        if (t.Effect == EffectKind.ApplyStatus) chance *= 1 + owner.S(StatId.StatusChance) / 100.0;
                        if (chance < 100 && !R.Chance(chance / 100.0)) continue;
                        if (!TurnCapOk(owner, skill, ti, t)) continue;
                        int rndCap = (int)ParamNumber(t.Param, "rnd_cap"); // parts v2: "once per round"
                        if (rndCap > 0 && RoundCapped(owner, "rnd:" + skill.Id + ":" + ti, rndCap, 1) <= 0) continue;
                        if (t.CooldownRounds > 0) owner.Ready[si][ti] = Round + t.CooldownRounds;
                        if (HasToken(t.Param, "arm")) { owner.Armed.Add(new KeyValuePair<SkillDef, SkillTrigger>(skill, t)); owner.Fired[si][ti]++; continue; } // runs on the next hit
                        owner.Fired[si][ti]++;
                        _firedCount++;
                        for (int n = 0; n < times; n++)
                        {
                            Execute(owner, skill, t, ctx, amount);
                            if (!owner.Alive || (Over && kind != TriggerKind.OnBattleWon && !afterEnd)) return;
                        }
                    }
                }
            }
            finally { _depth--; }
        }

        private List<Fighter> Targets(Fighter owner, TargetKind kind, Fighter ctx, int count = 1)
        {
            var list = new List<Fighter>();
            switch (kind)
            {
                case TargetKind.Self:
                    list.Add(owner);
                    break;
                case TargetKind.Enemy:
                    if (ctx != null && ctx.Alive && ctx.Side != owner.Side && ctx.Targetable) list.Add(ctx);
                    else { var d = DefaultTarget(owner); if (d != null) list.Add(d); }
                    break;
                case TargetKind.AllEnemies:
                    foreach (var o in F) if (o.Side != owner.Side && o.Alive && o.Targetable) list.Add(o);
                    break;
                case TargetKind.AllOtherEnemies:
                    foreach (var o in F) if (o.Side != owner.Side && o.Alive && o.Targetable && o != ctx) list.Add(o);
                    break;
                case TargetKind.RandomEnemy:
                {
                    var opts = new List<Fighter>();
                    foreach (var o in F) if (o.Side != owner.Side && o.Alive && o.Targetable) opts.Add(o);
                    if (opts.Count > 0) list.Add(opts[R.Range(0, opts.Count)]);
                    break;
                }
                case TargetKind.LowestHpEnemy:
                {
                    Fighter best = null;
                    foreach (var o in F)
                        if (o.Side != owner.Side && o.Alive && o.Targetable && (best == null || o.Hp / o.MaxHp < best.Hp / best.MaxHp)) best = o;
                    if (best != null) list.Add(best);
                    break;
                }
                case TargetKind.Worm:
                {
                    var w = FirstWorm(owner.Side);
                    list.Add(w != null && w.Alive ? w : owner);
                    break;
                }
                case TargetKind.AllAllies:
                    foreach (var o in F) if (o.Side == owner.Side && o.Alive) list.Add(o);
                    break;
                case TargetKind.SecondEnemy:
                {
                    // the enemy behind the context (or front) enemy
                    var line = new List<Fighter>();
                    foreach (var o in F) if (o.Side != owner.Side && o.Alive && o.Targetable) line.Add(o);
                    var basis = ctx != null && line.Contains(ctx) ? ctx : (line.Count > 0 ? line[0] : null);
                    int at = basis != null ? line.IndexOf(basis) : -1;
                    if (at >= 0 && at + 1 < line.Count) list.Add(line[at + 1]);
                    break;
                }
                case TargetKind.LastAttacker:
                {
                    var w = FirstWorm(owner.Side);
                    var la = w != null && w.LastAttacker >= 0 && w.LastAttacker < F.Count ? F[w.LastAttacker] : null;
                    if (la != null && la.Alive && la.Targetable && la.Side != owner.Side) list.Add(la);
                    else { var d = DefaultTarget(owner); if (d != null) list.Add(d); }
                    break;
                }
                case TargetKind.Attackers:
                {
                    var w = FirstWorm(owner.Side);
                    if (w != null) foreach (var i in w.HitBy) if (i >= 0 && i < F.Count && F[i].Alive && F[i].Targetable && F[i].Side != owner.Side) list.Add(F[i]);
                    if (list.Count == 0) { var d = DefaultTarget(owner); if (d != null) list.Add(d); }
                    break;
                }
                case TargetKind.FastestNonBoss:
                {
                    Fighter best = null;
                    foreach (var o in F)
                        if (o.Side != owner.Side && o.Alive && o.Targetable && o.Role != CombatRole.Boss && (best == null || o.S(StatId.Speed) > best.S(StatId.Speed))) best = o;
                    if (best != null) list.Add(best);
                    break;
                }
                case TargetKind.OtherEnemies:
                {
                    var opts = new List<Fighter>();
                    foreach (var o in F) if (o.Side != owner.Side && o.Alive && o.Targetable && o != ctx) opts.Add(o);
                    var rs = RulesOf(owner);
                    for (int n = Math.Max(1, count); n > 0 && opts.Count > 0; n--)
                    {
                        // Superconductor: Arcs jump to chilled or frozen enemies first
                        var pool = opts;
                        if (rs != null && rs.ArcSeekChill)
                        {
                            var cold = new List<Fighter>();
                            foreach (var o in opts) if (o.Status(StatusId.Chill) != null || o.Status(StatusId.Freeze) != null) cold.Add(o);
                            if (cold.Count > 0) pool = cold;
                        }
                        var pick = pool[R.Range(0, pool.Count)];
                        list.Add(pick);
                        opts.Remove(pick);
                    }
                    break;
                }
            }
            return list;
        }

        private static StatId ElementStat(SkillFamily family)
        {
            switch (family)
            {
                case SkillFamily.Fire: return StatId.FireDamage;
                case SkillFamily.Ice: return StatId.IceDamage;
                case SkillFamily.Lightning: return StatId.LightningDamage;
                case SkillFamily.Poison: return StatId.PoisonDamage;
                case SkillFamily.Bleed: return StatId.BleedDamage;
                default: return StatId.Atk;
            }
        }

        private BattleLogEntry Mark(Fighter owner, Fighter target, SkillDef skill, double amount = 0)
        {
            var e = Log(BattleAction.Buff, owner, target, amount);
            if (e != null) e.SkillId = skill.Id;
            return e;
        }

        private void Execute(Fighter owner, SkillDef skill, SkillTrigger t, Fighter ctx, double amount)
        {
            var picked = PickTargets(owner, t.Param, ctx);
            List<Fighter> targets;
            if (picked != null && (t.Target == TargetKind.Enemy || t.Target == TargetKind.SecondEnemy)) targets = picked;
            else
            {
                if (picked != null) ctx = picked.Count > 0 ? picked[0] : null; // "the most irradiated enemy gives …" : others relative to it
                if (picked != null && ctx == null) return;
                targets = Targets(owner, t.Target, ctx, HasToken(t.Param, "noarc") || t.Target != TargetKind.OtherEnemies ? t.Stacks : t.Stacks);
            }
            FilterTargets(targets, t.Param);
            int perTarget = (int)ParamNumber(t.Param, "per_target");
            if (perTarget > 0) { targets.RemoveAll(tg => tg != null && !TakePerTarget(owner, t, tg, perTarget)); if (targets.Count == 0) return; } // once per enemy
            if (ExecutePart(owner, skill, t, ctx, amount, targets)) return;
            switch (t.Effect)
            {
                case EffectKind.Damage:
                case EffectKind.TrueDamage:
                {
                    var el = ElementStat(skill.Family);
                    double bonus = el == StatId.Atk ? 0 : owner.S(el);
                    bool isTrue = t.Effect == EffectKind.TrueDamage;
                    bool arc = t.Target == TargetKind.OtherEnemies && !HasToken(t.Param, "noarc");
                    double extra = 0;
                    var ro = RulesOwner(owner);
                    if (arc)
                    {
                        bonus += ro != null ? ro.S(StatId.ArcDamage) : 0;
                        // Ground Loop: with no other enemy left the jump strikes the context enemy itself
                        if (targets.Count == 0 && ro != null && ro.Rules.ArcLoopPct > 0 && ctx != null && ctx.Alive && ctx.Side != owner.Side && ctx.Targetable)
                        {
                            targets.Add(ctx);
                            extra = ro.Rules.ArcLoopPct;
                        }
                    }
                    // Drain Hose A5: with a full siphon shield the shot hits every enemy
                    if (HasToken(t.Param, "wide_siphon")) { var sw = FirstWorm(owner.Side); if (sw != null && SiphonFull(sw)) targets = Targets(owner, TargetKind.AllEnemies, ctx); }
                    bool forceCrit = HasToken(t.Param, "crit"), onCrit = HasToken(t.Param, "oncrit"), coil = HasToken(t.Param, "coil");
                    // Wide (aliens pass): a sidekick's hit also strikes one random other enemy at WidePct %
                    if (owner.Sidekick && owner.Wide && t.Target != TargetKind.AllEnemies)
                    {
                        var others = new List<Fighter>();
                        foreach (var o in F) if (o.Side != owner.Side && o.Alive && o.Targetable && !targets.Contains(o)) others.Add(o);
                        if (others.Count > 0) targets.Add(others[R.Range(0, others.Count)]);
                        else targets.Add(null);
                    }
                    // Payback Cannon A5 ("coil_split"): the targets share the stored damage
                    double coilEach = coil ? owner.Coil : 0;
                    if (coil && HasToken(t.Param, "coil_split"))
                    {
                        int n = 0;
                        foreach (var tg in targets) if (tg != null && tg.Alive && tg.Side != owner.Side) n++;
                        if (n > 1) coilEach /= n;
                    }
                    for (int ti = 0; ti < targets.Count; ti++)
                    {
                        var tg = targets[ti];
                        if (Over || !owner.Alive) break;
                        if (tg == null || !tg.Alive || tg.Side == owner.Side) continue;
                        double wide = owner.Sidekick && owner.Wide && ti == targets.Count - 1 && t.Target != TargetKind.AllEnemies ? C.AlienTuning.WidePct / 100.0 : 1;
                        SkillHit(owner, skill, tg, t.Value, bonus, isTrue, arc, (1 + extra / 100.0) * wide * _abilityMult, ro, forceCrit, onCrit, coilEach);
                    }
                    if (coil && owner.Coil > 0) owner.CoilUsed = true; // Payback Cannon empties the coil after the shot
                    // Zip Strike (aliens pass, Param "combo"): the sidekick's strike rolls the worm's combo chance for more strikes
                    // (decaying like a combo chain) and each fires the worm's combo payoffs
                    if (t.Param == "combo" && owner.Sidekick && !Over)
                    {
                        var worm = FirstWorm(owner.Side);
                        if (worm != null && worm.Alive)
                        {
                            double p = CombatMath.Clamp(ShutDown(worm) ? 0 : worm.S(StatId.ComboChance), 0, 95) / 100.0;
                            for (int links = 0; !Over && owner.Alive && worm.Alive && p > 0 && links < T.MaxChain && R.Chance(p); links++)
                            {
                                var ct = DefaultTarget(owner);
                                if (ct == null) break;
                                double dealt = SkillHit(owner, skill, ct, t.Value, bonus, isTrue, false, 1, ro);
                                if (!Over && worm.Alive) Fire(worm, TriggerKind.OnCombo, ct, StatusId.None, dealt);
                                if (!worm.Rules.ComboNoDecay) p *= T.ChainDecay;
                            }
                        }
                    }
                    break;
                }
                case EffectKind.ApplyStatus:
                {
                    bool perStatus = HasToken(t.Param, "per_status"), perUnit = HasToken(t.Param, "per_unit");
                    int cd = (int)ParamNumber(t.Param, "cd");
                    var status = t.Status;
                    if (status == StatusId.None && HasToken(t.Param, "owned"))
                    {
                        // a random status the run applies (Flask, Scan Pulse; never crowd control); none owned → nothing
                        var w = FirstWorm(owner.Side);
                        if (w == null || w.Owned == null || w.Owned.Count == 0) break;
                        status = w.Owned[R.Range(0, w.Owned.Count)];
                    }
                    double bossRage = ParamNumber(t.Param, "boss_rage");
                    // Twin (aliens pass): +1 stack on every 2nd action of the sidekick
                    int twin = owner.Sidekick && owner.Twin && owner.ActCount > 0 && owner.ActCount % Math.Max(1, C.AlienTuning.TwinEvery) == 0 ? 1 : 0;
                    foreach (var tg in targets)
                    {
                        if (Over) break;
                        int n = Math.Max(1, t.Stacks) + twin;
                        if (t.Target == TargetKind.OtherEnemies || t.Target == TargetKind.AllOtherEnemies) n = Math.Max(1, t.Stacks) + twin;
                        if (perStatus) n *= CountStatuses(tg);       // Fallout Aura: per different status the target has
                        if (perUnit) n *= Math.Max(0, (int)Math.Round(amount)); // Fallout Wounds: per stack consumed
                        if (HasScale(t.Param)) n = (int)Math.Round(Scale(owner, ctx, t.Param, n, amount)); // parts v2 scaled stacks
                        if (HasToken(t.Param, "spread")) n = SpreadStacks(owner, status, n);
                        if (n <= 0) continue;
                        if (bossRage > 0 && tg.Role == CombatRole.Boss)
                        {
                            // Time Debt: bosses are never skipped, they lose rage instead
                            AddRage(tg, -bossRage, true);
                            var be = Log(BattleAction.GainRage, owner, tg, -bossRage);
                            if (be != null) be.SkillId = skill.Id;
                            continue;
                        }
                        ApplyStatus(owner, tg, status, n, t.Duration, skill.Id);
                        // "cd:N": the target can't get it again for N rounds (Chronoslug's Delay, like Freeze's re-apply cooldown)
                        if (cd > 0 && tg.Status(status) != null && tg.StatusCd[(int)status] < Round + cd) tg.StatusCd[(int)status] = Round + cd;
                    }
                    break;
                }
                case EffectKind.Heal:
                {
                    bool ofAmount = HasToken(t.Param, "of_amount"), perUnitHeal = HasToken(t.Param, "per_unit"), siphon = HasToken(t.Param, "siphon");
                    bool overheal = t.Param == "overheal_shield" || HasToken(t.Param, "overheal_shield");
                    double roundCap = ParamNumber(t.Param, "round_cap");
                    var hp = RulesOwner(owner) ?? owner; // a mount heals with its worm's heal power
                    foreach (var tg in targets)
                    {
                        if (tg.Sidekick) continue; // sidekicks have no HP to heal
                        double pct = t.Value * (perUnitHeal ? Math.Max(0, amount) : 1);
                        if (HasScale(t.Param)) pct = Scale(owner, ctx, t.Param, pct, amount); // parts v2 scaled heals
                        if (roundCap > 0) pct = RoundCapped(owner, skill.Id, roundCap, pct); // Taste Test: at most N% max HP per round
                        if (pct <= 0) continue;
                        double basis = ofAmount ? Math.Max(0, amount) : tg.MaxHp; // Siphon Line: a share of the status damage
                        Heal(owner, tg, basis * pct / 100.0 * SupportScale(owner) * (1 + hp.S(StatId.HealPower) / 100.0), BattleAction.Heal, skill.Id, overheal, siphon);
                    }
                    break;
                }
                case EffectKind.Shield:
                    foreach (var tg in targets)
                    {
                        if (!tg.Alive || tg.Sidekick) continue;
                        // "of_amount": a share of the trigger's amount (Rebound Plating: of the counter's damage), else of max HP
                        double basis = HasToken(t.Param, "of_amount") ? Math.Max(0, amount) : HasToken(t.Param, "of_consume") ? Math.Max(0, owner.LastConsumeDealt) : tg.MaxHp;
                        var sp = owner.Rider ? (RulesOwner(owner) ?? owner) : owner; // a mount shields with its worm's shield power
                        double sv = HasScale(t.Param) ? Scale(owner, ctx, t.Param, t.Value, amount) : t.Value;
                        double amt = basis * sv / 100.0 * SupportScale(owner) * (1 + sp.S(StatId.ShieldPower) / 100.0)
                            * (owner.Sidekick && owner.Bulwark ? 1 + C.AlienTuning.BulwarkPct / 100.0 : 1);
                        if (amt <= 0) continue;
                        tg.Shield += amt;
                        double wall = ParamNumber(t.Param, "wall");
                        if (wall > 0)
                        {
                            // Ice Wall (Frost Frigate 5★+): this part of the shield bursts when it breaks
                            tg.WallShield += amt; tg.WallPct = wall; tg.WallAll = HasToken(t.Param, "wall_all"); tg.WallOwner = owner.Index; tg.WallSkill = skill.Id;
                        }
                        var e = Log(BattleAction.Shield, owner, tg, amt);
                        if (e != null) e.SkillId = skill.Id;
                    }
                    break;
                case EffectKind.GainRage:
                {
                    bool fill = HasToken(t.Param, "fill"), bosses = HasToken(t.Param, "bosses"), debt = HasToken(t.Param, "debt");
                    double roundCap = ParamNumber(t.Param, "round_cap");
                    foreach (var tg in targets)
                    {
                        if (!tg.Alive || tg.Sidekick || tg.Role == CombatRole.Mount) continue; // sidekicks and mounts have no rage
                        if (bosses && tg.Role != CombatRole.Boss) continue;
                        double rage = fill ? Math.Max(0, T.RageMax - tg.Rage) : (HasScale(t.Param) ? Scale(owner, ctx, t.Param, t.Value, amount) : t.Value) * SupportScale(owner);
                        if (roundCap > 0 && rage > 0) rage = RoundCapped(owner, skill.Id, roundCap, rage); // Ignition, Pressure Valve
                        if (rage == 0) continue;
                        AddRage(tg, rage, debt);
                        var e = Log(BattleAction.GainRage, owner, tg, rage);
                        if (e != null) e.SkillId = skill.Id;
                    }
                    break;
                }
                case EffectKind.BuffStat:
                    if (t.Stat == null) break;
                    foreach (var tg in targets)
                    {
                        if (!tg.Alive) continue;
                        int untilN = (int)ParamNumber(t.Param, "until_turn");
                        bool untilTurn = HasToken(t.Param, "until_turn") || untilN > 0, reset = HasToken(t.Param, "reset_no_combo"), through = HasToken(t.Param, "next_turn");
                        bool endCrit = HasToken(t.Param, "until_crit"), endDodge = HasToken(t.Param, "until_dodge"), endAttack = HasToken(t.Param, "next_attack");
                        // Lingering (aliens pass): a sidekick's timed buff lasts +1 round
                        int rounds = t.Duration > 0 ? t.Duration + (owner.Sidekick && owner.Lingering ? C.AlienTuning.LingeringRounds : 0) : -1;
                        var b = new BuffInst
                        {
                            Stat = t.Stat.Stat, Flat = t.Stat.Flat, Percent = t.Stat.Percent, Rounds = rounds,
                            Sidekick = owner.Sidekick ? owner.Index : -1, UntilTurn = untilTurn, ResetNoCombo = reset, ThroughTurn = through, SkillId = skill.Id,
                        };
                        if (untilTurn || through || endCrit || endDodge || endAttack) b.Rounds = -1;
                        b.EndOnCrit = endCrit; b.EndOnDodge = endDodge; b.EndAfterAttack = endAttack; b.TurnsLeft = untilN;
                        int stackMax = (int)ParamNumber(t.Param, "stack");
                        if (stackMax > 0)
                        {
                            // "stack:N" (Nebulisk's Starlight): the same source's buff stacks up to N times, then holds
                            int have = 0;
                            foreach (var old in tg.Buffs) if (old.Stat == b.Stat && old.SkillId != null && old.Sidekick == b.Sidekick && (owner.Sidekick || old.SkillId == skill.Id)) have++;
                            if (have >= stackMax) continue;
                        }
                        else if (owner.Sidekick || untilTurn || through)
                        {
                            // a sidekick's buff, or one that lasts until the next turn, refreshes rather than stacking with itself
                            for (int bi = tg.Buffs.Count - 1; bi >= 0; bi--)
                            {
                                var old = tg.Buffs[bi];
                                if (old.Stat != b.Stat) continue;
                                if (owner.Sidekick ? old.Sidekick != owner.Index : old.SkillId != skill.Id || !(old.UntilTurn || old.ThroughTurn)) continue;
                                RemoveBuff(tg, bi);
                            }
                        }
                        tg.Buffs.Add(b);
                        tg.BFlat[(int)b.Stat] += b.Flat;
                        tg.BPct[(int)b.Stat] += b.Percent;
                        var e = Log(BattleAction.Buff, owner, tg, (long)Math.Round(b.Percent != 0 ? b.Percent : b.Flat));
                        if (e != null) e.SkillId = skill.Id;
                    }
                    break;
                case EffectKind.Summon:
                    Summon(owner, skill, t);
                    break;
                case EffectKind.RemoveStatus:
                    foreach (var tg in targets)
                    {
                        if (HasToken(t.Param, "all")) // Quantum Mend Tail: remove every status
                        {
                            var ids = new List<StatusId>();
                            foreach (var st in tg.Statuses) ids.Add(st.Id);
                            foreach (var id in ids) RemoveStacks(owner, tg, id, 0, skill.Id);
                            continue;
                        }
                        var rs = t.Status == StatusId.None && HasToken(t.Param, "largest") ? Largest(tg) : t.Status; // Coolant Loop
                        if (rs == StatusId.None) continue;
                        int removed = RemoveStacks(owner, tg, rs, t.Stacks, skill.Id);
                        if (removed > 0 && HasToken(t.Param, "to_front") && OwnableStatus(rs) && !Over)
                        {
                            var front = DefaultTarget(tg); // Coolant Loop A5: the removed stacks go onto the front enemy
                            var w = FirstWorm(owner.Side);
                            if (front != null && w != null) ApplyStatus(w, front, rs, removed, 0, skill.Id);
                        }
                    }
                    break;
                case EffectKind.ConsumeStatus:
                    foreach (var tg in targets) { if (Over || !owner.Alive) break; Consume(owner, skill, t, tg); }
                    break;
                case EffectKind.Strike:
                {
                    var tg = targets.Count > 0 ? targets[0] : null;
                    if (tg == null || !tg.Targetable) tg = DefaultTarget(owner);
                    if (tg == null) break;
                    bool counter = t.Param == "counter";
                    Mark(owner, owner, skill); // marks the extra strike as the skill's
                    if (!counter) { owner.ComboStrikes++; owner.ComboThisTurn = true; } // Flurry's strike is a combo strike
                    double dealt = Attack(owner, tg, counter ? AttackKind.Counter : AttackKind.Combo, counter ? 1 : 0);
                    Fire(owner, counter ? TriggerKind.OnCounter : TriggerKind.OnCombo, tg, StatusId.None, dealt);
                    // Flicker Treads: the strike can combo
                    if (HasToken(t.Param, "chain") && !Over && owner.Alive && tg.Alive) ComboChain(owner, tg, false, 0);
                    break;
                }
                case EffectKind.AllyAction:
                {
                    // Stacks extra sidekick actions, round-robin over the side's sidekicks (a lone sidekick acts several times); "all": each acts;
                    // "others" (Tidecaller): another sidekick than the owner, rotating with the owner's actions. Capped per round (ReserveExtra).
                    var kicks = new List<Fighter>();
                    bool others = HasToken(t.Param, "others"), drones = HasToken(t.Param, "drones");
                    foreach (var o in F) if (o.Side == owner.Side && o.Alive && o.Sidekick && !(others && o == owner) && (!drones || o.Role == CombatRole.Summon)) kicks.Add(o);
                    if (kicks.Count == 0) break;
                    bool all = HasToken(t.Param, "all") || drones;
                    double joinCharge = ParamNumber(t.Param, "charge"); // Symbiote: each ally that joins loses 50 charge
                    int start = all ? 0 : others ? owner.ActCount % kicks.Count : HasToken(t.Param, "random") ? R.Range(0, kicks.Count) : Round % kicks.Count;
                    int count = all ? kicks.Count * Math.Max(1, t.Stacks) : Math.Max(1, t.Stacks);
                    for (int n = 0; n < count; n++)
                    {
                        if (Over || !owner.Alive) break;
                        var a = kicks[(start + n) % kicks.Count];
                        if (!a.Alive) continue;
                        if (!ReserveExtra(owner.Side)) break;
                        Mark(owner, a, skill); // the extra action, credited to the skill
                        SidekickAct(a, true);
                        if (joinCharge != 0) a.Charge = Math.Max(0, a.Charge + joinCharge);
                    }
                    break;
                }
                case EffectKind.Guard:
                    // aliens pass: one guard per fighter, a new one refreshes it (Bulwark +20%, Lingering +1 hit); hits still land
                    foreach (var tg in targets)
                    {
                        if (!tg.Alive || !tg.Targetable || t.Value <= 0) continue;
                        var at = C.AlienTuning;
                        // parts v2: "kick" = a sidekick of the owner's side raises it (Broodguard Legs; none → no guard), "hits:N"
                        Fighter by = owner;
                        if (HasToken(t.Param, "kick")) { by = null; foreach (var o in F) if (o.Side == owner.Side && o.Alive && o.Sidekick) { by = o; break; } }
                        if (by == null) continue;
                        tg.GuardPct = Math.Min(at.GuardCapPct, t.Value * (owner.Sidekick && owner.Bulwark ? 1 + at.BulwarkPct / 100.0 : 1));
                        tg.GuardHits = Math.Max(1, (int)ParamNumber(t.Param, "hits")) + (owner.Sidekick && owner.Lingering ? 1 : 0);
                        tg.GuardBy = by.Index;
                        var e = Log(BattleAction.Guard, owner, tg, tg.GuardPct);
                        if (e != null) { e.SkillId = skill.Id; e.Stacks = tg.GuardHits; }
                    }
                    break;
                case EffectKind.Block:
                {
                    // a sidekick guards; a worm's Block (Bodyguard) is held by the side's sidekicks, round-robin
                    var kicks = new List<Fighter>();
                    if (owner.Sidekick) kicks.Add(owner);
                    else foreach (var o in F) if (o.Side == owner.Side && o.Alive && o.Sidekick) kicks.Add(o);
                    if (kicks.Count == 0) break;
                    foreach (var tg in targets)
                    {
                        if (!tg.Alive || !tg.Targetable) continue;
                        for (int n = 0; n < Math.Max(1, t.Stacks) && tg.Guards.Count < T.MaxGuards; n++) tg.Guards.Add(kicks[n % kicks.Count].Index);
                        var e = Log(BattleAction.Guard, kicks[0], tg, 0);
                        if (e != null) { e.SkillId = skill.Id; e.Stacks = tg.Guards.Count; }
                    }
                    break;
                }
                case EffectKind.BlockStatus:
                    foreach (var tg in targets)
                    {
                        if (!tg.Alive || t.Status == StatusId.None) continue;
                        int until = Round + Math.Max(1, t.Duration);
                        if (tg.StatusCd[(int)t.Status] < until) tg.StatusCd[(int)t.Status] = until;
                        var e = Log(BattleAction.StatusBlocked, owner, tg, 0);
                        if (e != null) { e.Status = t.Status; e.Stacks = Math.Max(1, t.Duration); e.SkillId = skill.Id; }
                    }
                    break;
                case EffectKind.ForceSupernova:
                {
                    int at = (int)ParamNumber(t.Param, "at"); // "at:N" (Solarch): only from N Heat stacks
                    foreach (var tg in targets)
                    {
                        if (Over) break;
                        var st = tg.Status(StatusId.Heat);
                        if (st == null || !tg.Alive || !T.Statuses.TryGetValue(StatusId.Heat, out var hd) || st.Stacks < at) continue;
                        Supernova(tg, st, hd, t.Value, skill.Id);
                    }
                    break;
                }
                case EffectKind.MultiplyStatus:
                {
                    double cap = 0;
                    if (!string.IsNullOrEmpty(t.Param))
                        foreach (var tok in t.Param.Split(';'))
                            if (tok.StartsWith("cap:", StringComparison.Ordinal))
                                double.TryParse(tok.Substring(4), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cap);
                    foreach (var tg in targets)
                    {
                        if (Over) break;
                        var st = tg.Status(t.Status);
                        if (st == null || !tg.Alive) continue;
                        int add = (int)Math.Floor(st.Stacks * t.Value / 100.0) - st.Stacks;
                        if (cap > 0) add = Math.Min(add, (int)cap);
                        if (add > 0) ApplyStatus(owner, tg, t.Status, add, 0, skill.Id);
                    }
                    break;
                }
                case EffectKind.TransferStatus:
                {
                    var st = ctx?.Status(t.Status);
                    if (st == null) break;
                    int n = SpreadStacks(owner, t.Status, (int)Math.Floor(st.Stacks * t.Value / 100.0));
                    if (n <= 0) break;
                    if (HasToken(t.Param, "next")) { var nx = DefaultTarget(owner); targets = nx != null ? new List<Fighter> { nx } : new List<Fighter>(); } // Cinder Crown
                    foreach (var tg in targets) { if (Over) break; if (tg != ctx) ApplyStatus(owner, tg, t.Status, n, 0, skill.Id); }
                    break;
                }
                case EffectKind.TickStatus:
                    foreach (var tg in targets)
                    {
                        if (Over || !tg.Alive) continue;
                        // one "tick now" execution per skill, target and round (Gamma Burst); every instant tick counts toward the 3 ticks per
                        // enemy per round from every source (parts v2; Stacks = ticks now: "ticks twice")
                        string tkey = "tick:" + skill.Id + ":" + tg.Index + ":" + (int)t.Status;
                        if (owner.CapRound.TryGetValue(tkey, out int tr) && tr == Round) continue;
                        owner.CapRound[tkey] = Round;
                        for (int k = 0; k < Math.Max(1, t.Stacks) && !Over && tg.Alive; k++)
                        {
                            var st = tg.Status(t.Status);
                            if (st == null || !T.Statuses.TryGetValue(t.Status, out var sd) || sd.DotAtkPct <= 0 || !TickOk(tg, t.Status)) break;
                            TickOne(tg, st, sd, (t.Value > 0 ? t.Value : 100) / 100.0, skill.Id);
                        }
                    }
                    break;
                case EffectKind.Execute:
                    foreach (var tg in targets)
                    {
                        if (Over || !tg.Alive || tg.Side == owner.Side || tg.Role == CombatRole.Boss) continue; // never on bosses
                        if (t.Value > 0 && tg.HpPct >= t.Value) continue;
                        tg.Shield = 0;
                        DealDamage(owner, tg, tg.Hp + 1, true, BattleAction.Execute, false, StatusId.None, skill.Id);
                    }
                    break;
                case EffectKind.Reflect:
                {
                    // parts v2 bases: "maxhp" (Value % of the owner's max HP), "of_taken" (the hit the owner just took)
                    double from = HasToken(t.Param, "maxhp") ? owner.MaxHp : HasToken(t.Param, "of_taken") ? owner.LastTakenDealt : amount;
                    double basis = Math.Max(0, from) * t.Value / 100.0;
                    if (basis <= 0) break;
                    foreach (var tg in targets)
                    {
                        if (Over || !tg.Alive || tg.Side == owner.Side) continue;
                        double d = basis * CombatMath.DamageReductionFactor(tg.S(StatId.DamageReduction), T) * TakenMultiplier(tg);
                        DealDamage(owner, tg, d, false, BattleAction.Skill, false, StatusId.None, skill.Id);
                    }
                    break;
                }
                case EffectKind.CheatDeath:
                {
                    var saved = owner.Rider ? (FirstWorm(owner.Side) ?? owner) : owner; // Cryo Pod (a mount's) saves its worm
                    if (saved.FatalSaved) break;
                    saved.FatalSaved = true;
                    if (HasToken(t.Param, "undo")) saved.Hp = Math.Max(1, Math.Min(saved.MaxHp, _fatalHpBefore)); // Borrowed Sand: the hit is undone
                    if (saved.Hp < 1) saved.Hp = 1;
                    {
                        var e = Log(BattleAction.CheatDeath, owner, saved, 0);
                        if (e != null) e.SkillId = skill.Id;
                    }
                    if (HasToken(t.Param, "counter_window")) saved.CounterWindow = true; // Last Word: counter every hit until your next turn
                    if (HasToken(t.Param, "stasis"))
                    {
                        // Cryo Pod: the worm skips its next turn and heals
                        saved.SkipNextTurn = true;
                        Heal(owner, saved, saved.MaxHp * t.Value / 100.0, BattleAction.Heal, skill.Id, false);
                    }
                    double ab = ParamNumber(t.Param, "ability");
                    if (ab > 0 && !Over) FireAbility(saved, ab / 100.0); // Last Constellation: Starfall at double damage
                    break;
                }
                case EffectKind.FreeRageAttack:
                {
                    var actor = HasToken(t.Param, "worm") ? FirstWorm(owner.Side) : owner; // Eruption A5: the worm attacks
                    if (actor == null || !actor.Alive) break;
                    var tg = targets.Count > 0 ? targets[0] : DefaultTarget(actor);
                    if (tg == null || !tg.Targetable) tg = DefaultTarget(actor);
                    if (tg == null) break;
                    // parts v2: "spend" = it is the worm's own rage attack fired now (Quantum Fury Legs: the rage is spent); free rage
                    // attacks from every source are capped at 2 per turn
                    if (HasToken(t.Param, "spend")) actor.Rage = Math.Max(0, actor.Rage - RageCost(actor));
                    else if (!FreeRageOk(actor)) break;
                    Mark(owner, actor, skill);
                    if (HasToken(t.Param, "worm") && actor != owner) { double dealt = Attack(actor, tg, AttackKind.Rage, 0); Fire(actor, TriggerKind.OnRageAttack, tg, StatusId.None, dealt); }
                    else RageAttack(actor, actor.Rules.RageTargetLowest && targets.Count == 0 ? RageTarget(actor) : tg);
                    break;
                }
                case EffectKind.Charge:
                    if (owner.Rider)
                    {
                        // Lance Sight: each worm crit charges the next shot (double on enemies below the threshold), capped
                        double v = t.Value;
                        double below = ParamNumber(t.Param, "double_below");
                        if (below > 0 && ctx != null && ctx.HpPct < below) v *= 2;
                        double cap = ParamNumber(t.Param, "cap");
                        owner.ShotCharge = cap > 0 ? Math.Min(cap, owner.ShotCharge + v) : owner.ShotCharge + v;
                        var ce = Log(BattleAction.KitCounter, owner, owner, owner.ShotCharge);
                        if (ce != null) { ce.SkillId = skill.Id; ce.Stacks = (int)cap; }
                        break;
                    }
                    if (HasToken(t.Param, "rage"))
                    {
                        // Backlash Hammer: each counter charges the next rage attack (capped)
                        double rcap = ParamNumber(t.Param, "cap");
                        owner.RageCharge = rcap > 0 ? Math.Min(rcap, owner.RageCharge + t.Value) : owner.RageCharge + t.Value;
                        Mark(owner, owner, skill, owner.RageCharge);
                        break;
                    }
                    owner.CounterCharge += t.Value;
                    Mark(owner, owner, skill, owner.CounterCharge);
                    break;
                case EffectKind.LoseHp:
                {
                    double loss = Math.Min(owner.Hp - 1, owner.MaxHp * t.Value / 100.0);
                    if (loss <= 0) break;
                    owner.Hp -= loss;
                    var e = Log(BattleAction.Skill, owner, owner, loss);
                    if (e != null) e.SkillId = skill.Id;
                    break;
                }
                case EffectKind.StackAll:
                    foreach (var tg in targets)
                    {
                        if (Over || !tg.Alive) continue;
                        var ids = new List<StatusId>();
                        foreach (var st in tg.Statuses)
                            if (T.Statuses.TryGetValue(st.Id, out var sd) && sd.MaxStacks > 1 && tg.StackAllRound[(int)st.Id] != Round) ids.Add(st.Id);
                        foreach (var id in ids) { tg.StackAllRound[(int)id] = Round; if (!Over) ApplyStatus(owner, tg, id, Math.Max(1, t.Stacks), 0, skill.Id); }
                    }
                    break;
                case EffectKind.Sacrifice:
                {
                    Fighter kick = null;
                    foreach (var o in F) if (o.Side == owner.Side && o.Alive && o.Sidekick) { kick = o; break; }
                    if (kick == null || owner.FatalSaved) break;
                    owner.FatalSaved = true;
                    kick.Alive = false;
                    var e = Log(BattleAction.Sacrifice, kick, owner, 0);
                    if (e != null) e.SkillId = skill.Id;
                    if (owner.Hp < 1) owner.Hp = 1;
                    Heal(owner, owner, owner.MaxHp * t.Value / 100.0, BattleAction.Heal, skill.Id, false);
                    break;
                }
                case EffectKind.Echo:
                {
                    if (owner.Sidekick) { SidekickEcho(owner, skill, t); break; }
                    if (!owner.Attacked || owner.EchoDepth >= MaxEchoDepth) break; // Echoes chain at most 2 deep (parts v2)
                    var rs = owner.Rules;
                    var kind = rs.EchoStrongest ? owner.BestKind : owner.LastKind;
                    int ti = rs.EchoStrongest ? owner.BestTarget : owner.LastTarget;
                    double emult = ParamNumber(t.Param, "mult"); // Timeslip Boots: an Echo at 40%; Oracle Visor: at double damage
                    double mult = Math.Max(0, 1 + owner.S(StatId.EchoDamage) / 100.0) * (emult > 0 ? emult / 100.0 : 1);
                    for (int n = 0; n < Math.Max(1, t.Stacks); n++)
                    {
                        if (Over || !owner.Alive) break;
                        var tg = ti >= 0 && ti < F.Count && F[ti].Alive && F[ti].Side != owner.Side ? F[ti] : DefaultTarget(owner);
                        if (tg == null) break;
                        var e = Log(BattleAction.Echo, owner, tg, 0);
                        if (e != null) e.SkillId = skill.Id;
                        owner.ExtraDepth++; // an Echo is an extra hit
                        owner.EchoDepth++;
                        try
                        {
                            double ed = Attack(owner, tg, kind == AttackKind.Skill || kind == AttackKind.Arc ? AttackKind.Basic : kind, 0, mult);
                            if (!Over && owner.Alive) Fire(owner, TriggerKind.OnEcho, tg, StatusId.None, ed);
                        }
                        finally { owner.ExtraDepth--; owner.EchoDepth--; }
                    }
                    break;
                }
                case EffectKind.Rewind:
                {
                    double sum = 0;
                    int n = Math.Max(1, t.Stacks);
                    var from = new List<int>();
                    for (int i = owner.HitHistory.Count - 1; i >= 0 && n > 0; i--, n--) { sum += owner.HitHistory[i]; if (i < owner.HitFrom.Count) from.Add(owner.HitFrom[i]); }
                    double back = Math.Min(sum, owner.MaxHp - owner.Hp);
                    if (back <= 0) break;
                    var undone = new List<KeyValuePair<int, double>>();
                    for (int i = owner.HitHistory.Count - 1, k = Math.Max(1, t.Stacks); i >= 0 && k > 0; i--, k--)
                        undone.Add(new KeyValuePair<int, double>(i < owner.HitFrom.Count ? owner.HitFrom[i] : -1, owner.HitHistory[i]));
                    owner.Hp += back;
                    var e = Log(BattleAction.Heal, owner, owner, back);
                    if (e != null) e.SkillId = skill.Id;
                    // the undone hits leave the history (they can't be undone twice)
                    int drop = Math.Min(owner.HitHistory.Count, Math.Max(1, t.Stacks));
                    owner.HitHistory.RemoveRange(owner.HitHistory.Count - drop, drop);
                    int dropFrom = Math.Min(drop, owner.HitFrom.Count);
                    owner.HitFrom.RemoveRange(owner.HitFrom.Count - dropFrom, dropFrom);
                    double reflect = ParamNumber(t.Param, "reflect");
                    if (reflect > 0) // Hour of Need: each undone hit strikes its attacker back
                        foreach (var ai in from)
                        {
                            if (Over || !owner.Alive) break;
                            var at = ai >= 0 && ai < F.Count ? F[ai] : null;
                            if (at == null || !at.Alive || !at.Targetable || at.Side == owner.Side) continue;
                            SkillHit(owner, skill, at, reflect, 0, false, false, 1, owner);
                        }
                    // parts v2: Hindsight Shell deals undone hits to their attackers (stacks with Hour of Need); OnUndo per undone hit
                    foreach (var u in undone)
                    {
                        if (Over || !owner.Alive) break;
                        var at = u.Key >= 0 && u.Key < F.Count ? F[u.Key] : null;
                        if (owner.Rules.UndoReflectPct > 0 && at != null && at.Alive && at.Targetable && at.Side != owner.Side)
                            DealDamage(owner, at, u.Value * owner.Rules.UndoReflectPct / 100.0 * CombatMath.DamageReductionFactor(at.S(StatId.DamageReduction), T) * TakenMultiplier(at),
                                false, BattleAction.Skill, false, StatusId.None, skill.Id);
                        if (!Over && owner.Alive) Fire(owner, TriggerKind.OnUndo, at, StatusId.None, u.Value);
                    }
                    break;
                }
                case EffectKind.Loop:
                    if (owner.FatalSaved) break;
                    owner.FatalSaved = true;
                    Restore(owner, skill);
                    break;
                default:
                    ExecuteKit(owner, skill, t, ctx, amount, targets);
                    break;
            }
        }

        /// <summary>A skill's damage hit on one target (Damage / TrueDamage): crit roll, lifesteal (a sidekick's heals its worm), triggers.</summary>
        private double SkillHit(Fighter owner, SkillDef skill, Fighter tg, double pct, double bonus, bool isTrue, bool arc, double mult, Fighter ro,
            bool forceCrit = false, bool onCrit = false, double flat = 0)
        {
            bool crit;
            if (arc && ro != null && ro.Rules.ArcCrit && !forceCrit)
            {
                // Arcwright Drill: every Arc jump of the side rolls the worm's crit (awakened: +20% crit chance)
                crit = R.Chance(CombatMath.CritProbability(ro.S(StatId.CritChance) + ro.Rules.ArcCritBonus, T)) && !ShutDown(ro);
                ro.LastHitCrit = crit;
            }
            else crit = forceCrit || RollCrit(owner, tg);
            // Lance Sight: the charge adds its % ATK to the shot's main hit (a pierce for half gets half)
            if (owner.Rider && owner.ShotCharge > 0 && owner.ShotBasePct > 0) mult *= 1 + owner.ShotCharge / owner.ShotBasePct;
            double dmg = ComputeDamage(owner, tg, pct, bonus, crit, arc ? AttackKind.Arc : AttackKind.Skill, isTrue) * mult;
            // Payback Cannon: + the stored damage (through the target's damage reduction)
            if (flat > 0) dmg += flat * CombatMath.DamageReductionFactor(tg.S(StatId.DamageReduction), T) * TakenMultiplier(tg);
            double dealt = DealDamage(owner, tg, dmg, false, BattleAction.Skill, crit, StatusId.None, skill.Id);
            if (owner.Sidekick && dealt > 0) { owner.SidekickHitThisAct = true; owner.SidekickHitTarget = tg.Index; }
            if (crit && !Over)
            {
                // crit link: a mount's crits are its worm's (its on-crit skills fire); Broadside's always-crits fire them too
                if (owner.Rider)
                {
                    var w = FirstWorm(owner.Side);
                    if (w != null && w.Alive) { _noRider = true; try { Fire(w, TriggerKind.OnCrit, tg, StatusId.None, dealt); } finally { _noRider = false; } }
                }
                else if (onCrit && owner.Alive) Fire(owner, TriggerKind.OnCrit, tg, StatusId.None, dealt);
            }
            // a sidekick's lifesteal (Vampiric) heals its worm: the sidekick itself is never hurt
            double ls = owner.Sidekick ? owner.S(StatId.LifeSteal) : 0;
            var lw = ls > 0 && dealt > 0 ? FirstWorm(owner.Side) : null;
            if (lw != null && lw.Alive) Heal(owner, lw, dealt * ls / 100.0, BattleAction.LifeSteal, skill.Id, false);
            if (tg.Alive) Fire(tg, TriggerKind.OnDamaged, owner, StatusId.None, dealt);
            if (arc && ro != null && !Over) Fire(ro, TriggerKind.OnArcJump, tg, StatusId.None, dealt);
            return dealt;
        }

        /// <summary>
        /// Void Echo (aliens pass): a sidekick repeats its worm's last attack (Paradox: its strongest) at Value % × (1 + alien power %) ×
        /// (1 + the worm's Echo damage %). The worm attacks (its on-hit triggers fire); the damage is credited to the sidekick.
        /// </summary>
        private void SidekickEcho(Fighter owner, SkillDef skill, SkillTrigger t)
        {
            var worm = FirstWorm(owner.Side);
            if (worm == null || !worm.Alive || !worm.Attacked) return;
            var kind = worm.Rules.EchoStrongest ? worm.BestKind : worm.LastKind;
            int ti = worm.Rules.EchoStrongest ? worm.BestTarget : worm.LastTarget;
            double mult = t.Value / 100.0 * Math.Max(0, 1 + owner.AlienPower / 100.0) * Math.Max(0, 1 + worm.S(StatId.EchoDamage) / 100.0);
            var tg = ti >= 0 && ti < F.Count && F[ti].Alive && F[ti].Side != worm.Side ? F[ti] : DefaultTarget(worm);
            if (tg == null) return;
            var e = Log(BattleAction.Echo, owner, tg, 0);
            if (e != null) e.SkillId = skill.Id;
            double dealt = Attack(worm, tg, kind == AttackKind.Skill || kind == AttackKind.Arc ? AttackKind.Basic : kind, 0, mult);
            if (dealt <= 0) return;
            worm.DamageDealt -= dealt;
            owner.DamageDealt += dealt;
            double ls = owner.S(StatId.LifeSteal);
            if (ls > 0 && worm.Alive) Heal(owner, worm, dealt * ls / 100.0, BattleAction.LifeSteal, skill.Id, false);
        }

        /// <summary>The number of a "name:N" token in a Param (0 when absent).</summary>
        internal static double ParamNumber(string p, string name)
        {
            if (string.IsNullOrEmpty(p)) return 0;
            foreach (var raw in p.Split(';'))
            {
                var tok = raw.Trim();
                if (tok.Length > name.Length + 1 && tok.StartsWith(name, StringComparison.Ordinal) && tok[name.Length] == ':'
                    && double.TryParse(tok.Substring(name.Length + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    return v;
            }
            return 0;
        }

        /// <summary>Removes up to <paramref name="stacks"/> (0 = all) stacks of a status (cooling, cleansing). Returns the stacks removed.</summary>
        private int RemoveStacks(Fighter src, Fighter tg, StatusId id, int stacks, string skillId)
        {
            var st = tg.Status(id);
            if (st == null || !tg.Alive) return 0;
            int n = stacks <= 0 ? st.Stacks : Math.Min(stacks, st.Stacks);
            st.Stacks -= n;
            if (st.Stacks <= 0) tg.Statuses.Remove(st);
            var e = Log(BattleAction.StatusRemoved, src, tg, n);
            if (e != null) { e.Status = id; e.Stacks = Math.Max(0, st.Stacks); e.SkillId = skillId; }
            StatusLost(tg, id, n);
            return n;
        }

        /// <summary>
        /// Consumes stacks of a status on the target (all, or Param "take:N"): "dmg" % ATK skill damage per stack (element bonus of the
        /// skill's family; the original form: Value), "heal" heals the owner's worm that % of it (× the consume-heal rule, Crimson
        /// Pact), "buff:Stat:X" X per stack for the rest of the battle (at most "cap" per battle), "refund_max" rage when the stacks
        /// were at their cap. Then OnStatusConsumed fires (Fallout Wounds).
        /// </summary>
        private void Consume(Fighter owner, SkillDef skill, SkillTrigger t, Fighter tg)
        {
            if (!tg.Alive || tg.Side == owner.Side) return;
            var spec = ConsumeSpec.Of(t);
            var status = t.Status == StatusId.None && spec.Largest ? Largest(tg) : t.Status; // Feast eats the largest status
            if (status == StatusId.None) return;
            var before = tg.Status(status);
            if (before == null) return;
            bool atMax = T.Statuses.TryGetValue(status, out var sdef) && before.Stacks >= CapOf(sdef, SRules(before));
            int n = RemoveStacks(owner, tg, status, spec.Take, skill.Id);
            if (n <= 0) return;
            var worm = owner.Targetable ? owner : FirstWorm(owner.Side);
            if (spec.HealAtk > 0 && worm != null && worm.Alive)
            {
                // Thorn Harvest (aliens pass): heal the worm HealAtk % of ATK (a sidekick's: its worm's × alien power) per stack consumed
                var hr = RulesOf(owner);
                double pct = spec.HealAtk * n * (1 + (hr != null ? hr.Of(status).ConsumeHealPct : 0) / 100.0);
                Heal(owner, worm, AtkOf(owner) * pct / 100.0, BattleAction.Heal, skill.Id, false);
            }
            if (spec.Dmg > 0)
            {
                var el = ElementStat(skill.Family);
                bool crit = RollCrit(owner);
                double dmg = ComputeDamage(owner, tg, spec.Dmg * n, el == StatId.Atk ? 0 : owner.S(el), crit, AttackKind.Skill, false) * _abilityMult;
                double dealt = DealDamage(owner, tg, dmg, false, BattleAction.Skill, crit, StatusId.None, skill.Id);
                var rules = RulesOf(owner);
                double healPct = spec.Heal * (1 + (rules != null ? rules.Of(status).ConsumeHealPct : 0) / 100.0);
                if (healPct > 0 && dealt > 0 && worm != null && worm.Alive) Heal(owner, worm, dealt * healPct / 100.0, BattleAction.Heal, skill.Id, false);
                // Banquet (Chef 10★): a full Feast splashes every other enemy for a share of its damage
                if (spec.Splash > 0 && spec.Take > 0 && n >= spec.Take && !Over)
                    foreach (var o in new List<Fighter>(F))
                    {
                        if (Over) break;
                        if (o == tg || o.Side == owner.Side || !o.Alive || !o.Targetable) continue;
                        dealt += DealDamage(owner, o, ComputeDamage(owner, o, spec.Dmg * n * spec.Splash / 100.0, el == StatId.Atk ? 0 : owner.S(el), crit, AttackKind.Skill, false) * _abilityMult,
                            false, BattleAction.Skill, crit, StatusId.None, skill.Id);
                    }
                StatusDamage(owner, dealt); // consumes are status damage (Siphon Line)
                var cw = owner.Sidekick && worm != null ? worm : owner;
                cw.LastConsumeDealt = dealt; // Bloodtide Fin: a shield of the consume's damage
            }
            if (spec.Buff && spec.BuffValue > 0 && owner.Alive)
            {
                double add = spec.BuffValue * n;
                owner.ConsumeBuffUsed.TryGetValue(skill.Id, out var used);
                if (spec.Cap > 0) add = Math.Min(add, spec.Cap - used);
                if (add > 0)
                {
                    owner.ConsumeBuffUsed[skill.Id] = used + add;
                    bool point = StatMod.IsPointStat(spec.BuffStat);
                    var b = new BuffInst { Stat = spec.BuffStat, Flat = point ? add : 0, Percent = point ? 0 : add, Rounds = -1, SkillId = skill.Id };
                    owner.Buffs.Add(b);
                    owner.BFlat[(int)b.Stat] += b.Flat;
                    owner.BPct[(int)b.Stat] += b.Percent;
                    var e = Log(BattleAction.Buff, owner, owner, add);
                    if (e != null) e.SkillId = skill.Id;
                }
            }
            if (spec.RefundMax > 0 && atMax && worm != null && worm.Alive)
            {
                AddRage(worm, spec.RefundMax);
                var e = Log(BattleAction.GainRage, owner, worm, spec.RefundMax);
                if (e != null) e.SkillId = skill.Id;
            }
            // a sidekick's consume counts as its worm's (Fallout Wounds and other consume payoffs fire)
            var consumer = owner.Sidekick && worm != null ? worm : owner;
            if (!Over && consumer.Alive) Fire(consumer, TriggerKind.OnStatusConsumed, tg, status, n);
        }

        /// <summary>The "heal:N" part of a consume trigger's Param (0 when absent).</summary>
        internal static double ConsumeHealPct(SkillTrigger t) => ConsumeSpec.Of(t).Heal;

        /// <summary>
        /// Supernova (Heat at its threshold): the fighter explodes. Every targetable fighter of its side (itself included) takes
        /// BurstAtkPct % of the heater's element-scaled ATK × (1 + heater's SupernovaDamage % + <paramref name="bonusPct"/>), DR and
        /// damage-taken statuses apply; the heater's side's worm takes BurstRecoilPct % of its max HP × (1 − SupernovaGuard %) unless
        /// the heater's rules say it never hurts (Controlled Burn); the Heat is gone. The heater's OnSupernova triggers fire last.
        /// </summary>
        private void Supernova(Fighter x, StatusInst st, StatusDef def, double bonusPct = 0, string skillId = null)
        {
            x.Statuses.Remove(st);
            Fighter heater = st.Source >= 0 && st.Source < F.Count ? F[st.Source] : null;
            var rules = SRules(st);
            var mark = Log(BattleAction.Supernova, x, x, 0);
            if (mark != null) { mark.Status = StatusId.Heat; mark.Stacks = st.Stacks; mark.SkillId = skillId; }
            StatusLost(x, StatusId.Heat, st.Stacks);
            if (Over) return;
            // Ashwalkers: Supernovas a dodge set off deal no recoil (awakened: double damage)
            var hro = RulesOwner(heater);
            bool dodgeNova = _dodgeDepth > 0 && hro != null && hro.Rules.DodgeNova;
            if (dodgeNova) bonusPct += hro.Rules.DodgeNovaBonus;
            double dmg = st.Power * def.BurstAtkPct / 100.0 * (1 + ((heater != null ? Math.Max(0, heater.S(StatId.SupernovaDamage)) : 0) + bonusPct) / 100.0);
            var hrs = RulesOf(heater);
            bool canCrit = hrs != null && hrs.StatusCrit; // Dead Reckoning: Supernovas can crit
            var hit = new List<Fighter>();
            foreach (var o in F) if (o.Side == x.Side && o.Alive && o.Targetable) hit.Add(o);
            double total = 0;
            foreach (var o in hit)
            {
                if (Over) return;
                double d = dmg * CombatMath.DamageReductionFactor(o.S(StatId.DamageReduction), T) * TakenMultiplier(o);
                bool crit = false;
                if (canCrit)
                {
                    var cs = CritSource(heater);
                    crit = R.Chance(CombatMath.CritProbability(cs.S(StatId.CritChance), T));
                    if (crit) d *= CombatMath.CritMultiplier(T.BaseCritDamage + cs.S(StatId.CritDamage), o.S(StatId.CritResist));
                }
                total += DealDamage(heater, o, d, false, BattleAction.Supernova, crit, StatusId.Heat, null);
            }
            if (Over) return;
            StatusDamage(heater, total);
            if (Over) return;
            var side = heater != null ? heater.Side : (x.Side == BattleSide.Player ? BattleSide.Enemy : BattleSide.Player);
            var worm = FirstWorm(side);
            if (worm != null && worm.Alive && worm.Side != x.Side && !rules.NoRecoil && !dodgeNova)
            {
                double guard = CombatMath.Clamp(worm.S(StatId.SupernovaGuard), 0, 100) / 100.0;
                double recoil = worm.MaxHp * def.BurstRecoilPct / 100.0 * (1 - guard);
                if (recoil > 0) DealDamage(x, worm, recoil, false, BattleAction.Supernova, false, StatusId.Heat, null);
            }
            if (!Over && heater != null && heater.Alive) Fire(heater, TriggerKind.OnSupernova, x);
        }

        private void ApplyStatus(Fighter src, Fighter tg, StatusId id, int stacks, int duration, string skillId)
        {
            if (!tg.Alive || id == StatusId.None) return;
            if (!T.Statuses.TryGetValue(id, out var def)) return;
            if (tg.StatusCd[(int)id] > Round) return;
            // aliens pass: a sidekick's statuses are the shared ones with its worm as the source (the worm's power and rules, the
            // worm's status triggers); the log still names the sidekick
            var actor = src;
            if (src != null && (src.Sidekick || src.Rider)) // rider link: a mount's statuses are its worm's too
            {
                var w = FirstWorm(src.Side);
                if (w != null) src = w;
            }
            if (_extraHitDepth > 0 && src != null && src.Role == CombatRole.Worm && src.Rules.DoubleTap) stacks *= 2; // Double Tap
            double resist = CombatMath.Clamp(CombatMath.SoftCap(tg.S(StatId.StatusResist), T.StatusResistKnee), 0, 90) / 100.0;
            if (resist > 0 && R.Chance(resist))
            {
                var r = Log(BattleAction.StatusResisted, actor, tg, 0);
                if (r != null) { r.Status = id; r.SkillId = skillId; }
                return;
            }
            int dur = duration > 0 ? duration : def.DefaultDuration;
            // Lingering (aliens pass): a sidekick's Chill or Shock lasts +1 round (never Freeze / Delay)
            if (actor != null && actor.Sidekick && actor.Lingering && (id == StatusId.Chill || id == StatusId.Shock)) dur += C.AlienTuning.LingeringRounds;
            double power = 0;
            if (AtkBased(def))
            {
                double elem = def.HasElement ? src.S(def.ElementStat) : 0;
                power = AtkOf(src) * (1 + elem / 100.0);
            }
            // element statuses without a DoT (Shock, Freeze): the applier's element damage amplifies their damage-taken bonus
            // (Lightning / Ice damage did nothing for a fighter that only shocks or freezes)
            else if (def.HasElement) power = 1 + src.S(def.ElementStat) / 100.0;
            var srcRules = RulesOf(src);
            var sr = srcRules != null ? srcRules.Of(id) : RuleSet.None;
            int cap = CapOf(def, sr, true); // Deep Wells doubles the cap for applying; conditions read the base cap
            var st = tg.Status(id);
            int prev = st != null ? st.Stacks : 0;
            if (st == null)
            {
                st = new StatusInst { Id = id, Stacks = Math.Min(stacks, cap), Turns = dur, Power = power, Source = src.Index };
                tg.Statuses.Add(st);
            }
            else
            {
                // Adds stacks up to the cap; any reapplication (including at max stacks) refreshes the duration.
                st.Stacks = Math.Max(st.Stacks, Math.Min(cap, st.Stacks + stacks));
                st.Turns = Math.Max(st.Turns, dur);
                if (power >= st.Power) { st.Power = power; st.Source = src.Index; }
            }
            if (def.ApplyCooldownRounds > 0) tg.StatusCd[(int)id] = Round + def.ApplyCooldownRounds;
            if (tg == _acting) st.Fresh = true;
            var e = Log(BattleAction.StatusApplied, actor, tg, st.Stacks);
            if (e != null) { e.Status = id; e.Stacks = st.Stacks; e.SkillId = skillId; }
            if (def.Clears != StatusId.None) RemoveStacks(src, tg, def.Clears, 0, skillId); // CC pass: any Freeze resets the target's Chill to 0
            if (def.Cools != StatusId.None && !sr.NoCool) RemoveStacks(src, tg, def.Cools, stacks, skillId); // Chill cools Heat (Cold Fusion: not)
            int now = st.Stacks;
            if (now > prev) CountPlague(src, tg, id, now - prev, skillId);
            if (now > prev && tg.Statuses.Contains(st))
            {
                int saved = _thrPrev;
                _thrPrev = prev;
                Fire(src, TriggerKind.OnStackThreshold, tg, id, now);
                _thrPrev = saved;
            }
            int burst = BurstAtOf(def, SRules(st));
            if (burst > 0 && st.Stacks >= burst && tg.Statuses.Contains(st) && tg.Alive) Supernova(tg, st, def);
            Fire(src, TriggerKind.OnStatusApplied, tg, id, stacks);
        }

        /// <summary>
        /// Plague (lab Mythic): stacks the player side adds to enemies while its worm owns Plague are counted per status
        /// (BattleResult.StacksInflicted; the run adds them up). Plague's own opening stacks don't count.
        /// </summary>
        private void CountPlague(Fighter src, Fighter tg, StatusId id, int added, string skillId)
        {
            if (src == null || src.Side != BattleSide.Player || tg.Side != BattleSide.Enemy) return;
            var rs = RulesOf(src);
            if (rs == null || rs.PlagueSkill == null || skillId == rs.PlagueSkill) return;
            var map = Res.StacksInflicted ?? (Res.StacksInflicted = new Dictionary<string, int>());
            string key = id.ToString();
            map[key] = (map.TryGetValue(key, out var n) ? n : 0) + added;
        }

        /// <summary>
        /// Plague's opening stacks (CombatantSpec.OpeningStatuses on the player side): every enemy present at battle start gains
        /// them, with the fighter as their source, before any battle-start trigger.
        /// </summary>
        private void ApplyOpeningStatuses()
        {
            for (int i = 0, n = F.Count; i < n; i++)
            {
                var f = F[i];
                var open = f.Spec.OpeningStatuses;
                if (f.Side != BattleSide.Player || !f.Alive || open == null || open.Count == 0) continue;
                var ids = new List<StatusId>();
                foreach (var kv in open) if (kv.Value > 0 && Enum.TryParse(kv.Key, out StatusId sid) && sid != StatusId.None) ids.Add(sid);
                ids.Sort();
                string skill = RulesOf(f)?.PlagueSkill;
                foreach (var sid in ids)
                    for (int e = 0; e < n; e++)
                    {
                        if (Over) return;
                        var tg = F[e];
                        if (tg.Side == BattleSide.Enemy && tg.Alive && tg.Targetable) ApplyStatus(f, tg, sid, open[sid.ToString()], 0, skill);
                    }
            }
        }

        /// <summary>
        /// Enemy summons only (bosses calling their guard): Param = enemy def id, built on the owner's threat scale. Player-side
        /// drones were removed (dead-effects pass); content validation keeps every Summon on an enemy skill with an enemy id.
        /// </summary>
        private void Summon(Fighter owner, SkillDef skill, SkillTrigger t)
        {
            var sk = C.Sidekick(t.Param);
            if (owner.Side == BattleSide.Player && sk != null) { SummonSidekicks(owner, skill, t, sk); return; }
            if (owner.Side == BattleSide.Enemy && sk != null) return;
            if (owner.Side != BattleSide.Enemy || t.Param == null || !C.Enemies.ContainsKey(t.Param)) return;
            int count = 0;
            foreach (var f in F) if (f.Side == owner.Side && f.Alive) count++;
            if (count >= T.MaxFightersPerSide) return;
            var spec = BattleSimulator.BuildEnemy(C, t.Param, owner.Spec.ThreatScale);
            var nf = MakeFighter(spec, owner.Side);
            nf.SummonedBy = owner.Index;
            InitHp(nf);
            Res.Fighters[nf.Index].StartShield = Round0(nf.Shield);
            Res.Fighters[nf.Index].StartRage = Round0(nf.Rage);
            var e = Log(BattleAction.Summon, owner, nf, 0);
            if (e != null) e.SkillId = skill.Id;
        }

        /// <summary>
        /// Player-side summon: Stacks battle-scoped sidekicks of a SidekickDef (drones), each with power = Value % of the worm's ATK
        /// × (1 + the side's alien power %) for every summoner (mount, sidekick, worm; aliens pass), untargetable, charging by the def's
        /// Speed. Param ";max:N": at most N of them alive from this summoner (Swarmheart). A Wide summoner's drones are Wide.
        /// </summary>
        private void SummonSidekicks(Fighter owner, SkillDef skill, SkillTrigger t, SidekickDef sk)
        {
            int max = (int)ParamNumber(t.Param, "max");
            for (int n = 0; n < Math.Max(1, t.Stacks); n++)
            {
                int count = 0, mine = 0;
                foreach (var f in F)
                {
                    if (f.Side != owner.Side || !f.Alive) continue;
                    count++;
                    if (f.SummonedBy == owner.Index && f.Role == CombatRole.Summon) mine++;
                }
                if (count >= T.MaxFightersPerSide || (max > 0 && mine >= max) || !DroneRoom(owner.Side)) return; // at most 5 Drones a side
                var spec = new CombatantSpec
                {
                    Id = sk.Id, Name = sk.Name, Role = CombatRole.Summon, Targetable = false, Style = AttackStyle.Blaster,
                };
                spec.Stats.Add(new StatMod(StatId.Speed, sk.Speed));
                if (!string.IsNullOrEmpty(sk.AbilitySkillId)) spec.SkillIds.Add(sk.AbilitySkillId);
                if (owner.Sidekick && owner.Wide) spec.Traits.Add(ContentDb.TraitWide);
                var nf = MakeFighter(spec, owner.Side);
                nf.SummonedBy = owner.Index;
                InitSidekick(nf, AlienPowerOf(owner.Side));
                nf.AtkPct = t.Value;
                // Launch Bay (Hive Carrier): no signature (1★–A3) / every signature (A5), guards one hit (5★+)
                nf.NoSig = HasToken(t.Param, "nosig");
                nf.CarryOnAct = HasToken(t.Param, "carry");
                nf.DroneGuardPct = ParamNumber(t.Param, "guard");
                var dw = FirstWorm(owner.Side);
                if (dw != null && dw.Rules.DroneGuardPct > 0 && dw.Rules.DroneGuardPct > nf.DroneGuardPct) nf.DroneGuardPct = dw.Rules.DroneGuardPct; // Hatchery Tail
                nf.DroneGuardHits = dw != null && dw.Rules.DroneGuardHits > 1 ? dw.Rules.DroneGuardHits : 1;
                InitHp(nf);
                var e = Log(BattleAction.Summon, owner, nf, 0);
                if (e != null) e.SkillId = skill.Id;
            }
        }

        // ---- Loop (rewind) ----------------------------------------------------------------------

        /// <summary>Keeps the last three round-start snapshots (only when a fighter owns a Loop effect).</summary>
        private void Snapshot()
        {
            int n = F.Count;
            var s = new BattleSnap
            {
                Round = Round, Hp = new double[n], Shield = new double[n], Rage = new double[n], Charge = new double[n], Alive = new bool[n],
                Statuses = new List<StatusInst>[n],
            };
            for (int i = 0; i < n; i++)
            {
                var f = F[i];
                s.Hp[i] = f.Hp; s.Shield[i] = f.Shield; s.Rage[i] = f.Rage; s.Charge[i] = f.Charge; s.Alive[i] = f.Alive;
                var l = new List<StatusInst>(f.Statuses.Count);
                foreach (var st in f.Statuses) l.Add(st.Copy());
                s.Statuses[i] = l;
            }
            _snaps.Add(s);
            if (_snaps.Count > 3) _snaps.RemoveAt(0);
        }

        /// <summary>
        /// Loop: every fighter goes back to the start of the round before this one (two rounds back counting this one: HP, shields,
        /// rage, statuses, sidekick charge, the fallen alive again; fighters summoned since leave). The round counter runs on.
        /// </summary>
        private void Restore(Fighter owner, SkillDef skill)
        {
            if (_snaps.Count == 0) { if (owner.Hp < 1) owner.Hp = 1; return; }
            var s = _snaps.Count >= 2 ? _snaps[_snaps.Count - 2] : _snaps[0];
            for (int i = 0; i < F.Count; i++)
            {
                var f = F[i];
                if (i >= s.Hp.Length) { f.Alive = false; f.Statuses.Clear(); continue; }
                f.Hp = s.Hp[i]; f.Shield = s.Shield[i]; f.Rage = s.Rage[i]; f.Charge = s.Charge[i]; f.Alive = s.Alive[i];
                f.Statuses.Clear();
                foreach (var st in s.Statuses[i]) f.Statuses.Add(st.Copy());
                if (f.Alive) f.DeathStatuses = null;
            }
            if (owner.Hp < 1) owner.Hp = Math.Max(1, owner.Hp);
            var e = Log(BattleAction.Loop, owner, owner, s.Round);
            if (e != null) e.SkillId = skill.Id;
            // one entry per other fighter with its restored HP / shield / rage, so playback can rewind the board too
            foreach (var f in F)
            {
                if (f == owner || f.Sidekick || f.Role == CombatRole.Mount) continue;
                var x = Log(BattleAction.Loop, owner, f, s.Round);
                if (x != null) { x.SkillId = skill.Id; x.Stacks = f.Alive ? 1 : 0; }
            }
        }

        // ---- log ------------------------------------------------------------------------------

        private BattleLogEntry Log(BattleAction action, Fighter actor, Fighter target, double amount)
        {
            if (Res.Log.Count >= T.LogCap) { Res.LogTruncated = true; return null; }
            var e = new BattleLogEntry
            {
                Round = Round, Actor = actor != null ? actor.Index : -1, Action = action, Target = target != null ? target.Index : -1,
                Amount = (long)Math.Round(amount, MidpointRounding.AwayFromZero),
                TargetHp = target != null ? (long)Math.Round(target.Hp, MidpointRounding.AwayFromZero) : 0,
                TargetShield = target != null ? (long)Math.Round(target.Shield, MidpointRounding.AwayFromZero) : 0,
                TargetRage = target != null ? (long)Math.Round(target.Rage, MidpointRounding.AwayFromZero) : 0,
                ActorShield = actor != null ? (long)Math.Round(actor.Shield, MidpointRounding.AwayFromZero) : 0,
                ActorRage = actor != null ? (long)Math.Round(actor.Rage, MidpointRounding.AwayFromZero) : 0,
            };
            Res.Log.Add(e);
            return e;
        }
    }
}
