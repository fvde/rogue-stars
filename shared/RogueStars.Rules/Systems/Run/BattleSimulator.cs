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
                Style = loadout.ArmStyles.Count > 0 ? loadout.ArmStyles[0] : AttackStyle.Slam,
            };
            if (wormExtraStats != null) worm.Stats.AddAll(wormExtraStats);
            CombatantSpec mount = null;
            if (hasMount)
            {
                mount = new CombatantSpec
                {
                    Id = string.IsNullOrEmpty(loadout.MountId) ? "mount" : loadout.MountId, Name = "Mount", Role = CombatRole.Mount,
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
            foreach (var id in loadout.MetaSkillIds)
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

    internal sealed class BuffInst
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
        /// <summary>Gate (SkillTrigger.If), checked with the hit fighter as context; null = none.</summary>
        public string If;
    }

    /// <summary>
    /// How a status behaves when its applier owns rule skills (R6: rules live on the applier and are read from StatusInst.Source;
    /// a sidekick's or mount's statuses use its worm's rules).
    /// </summary>
    internal sealed class StatusRules
    {
        public double MaxDelta, DotPct, ConsumeHealPct;
        public int BurstAt, NoExpireAt;
        public bool NoRecoil, KeepsShields, DotCrit, NoCool, Shutdown, DoubleVsChill;
    }

    /// <summary>A fighter's rule skills (Passive <c>Rule</c> triggers), folded once at battle start.</summary>
    internal sealed class RuleSet
    {
        public static readonly StatusRules None = new StatusRules();
        public readonly StatusRules[] S = new StatusRules[32];
        public bool ArcSeekChill, ComboNoDecay, ComboAsCounter, CounterCombo, NoTurnAttack, CounterAll, Hivemind, Symbiote,
            Overkill, EchoStrongest, ExtraTurn;
        public double ArcLoopPct, BasicAoePct, ComboFalloffPct, ComboCap, LastLightPct;
        public int ProcCap, LoseTurnEvery, DotTicks, Decay;
        public string OverkillSkill;

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
                case "symbiote": Symbiote = true; break;
                case "overkill": Overkill = true; OverkillSkill = skillId; break;
                case "last_light": LastLightPct = arg; break;
                case "echo_strongest": EchoStrongest = true; break;
                case "extra_turn": ExtraTurn = true; break;
                case "lose_turn_every": LoseTurnEvery = (int)arg; break;
                case "dot_ticks": DotTicks = (int)arg; break;
                case "decay": Decay = (int)arg; break;
            }
        }
    }

    /// <summary>A ConsumeStatus trigger's Param, parsed ("dmg:$;heal:50;take:2;buff:Atk:$;cap:60;refund_max:50;heal_atk:$"; $ = Value).</summary>
    internal sealed class ConsumeSpec
    {
        /// <summary>HealAtk (aliens pass, Thorn Harvest): heal the worm this % of ATK per stack consumed.</summary>
        public double Dmg, Heal, BuffValue, Cap, RefundMax, HealAtk;
        public int Take;
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
                    if (a.Length < 2) continue;
                    switch (a[0])
                    {
                        case "dmg": c.Dmg = V(a[1]); dmgToken = true; break;
                        case "heal": c.Heal = V(a[1]); break;
                        case "heal_atk": c.HealAtk = V(a[1]); break;
                        case "take": c.Take = (int)V(a[1]); break;
                        case "cap": c.Cap = V(a[1]); break;
                        case "refund_max": c.RefundMax = V(a[1]); break;
                        case "buff":
                            if (a.Length >= 3 && Enum.TryParse(a[1], out StatId st)) { c.Buff = true; c.BuffStat = st; c.BuffValue = V(a[2]); }
                            break;
                    }
                }
            if (!dmgToken && !dollar) c.Dmg = t.Value; // the original form: Value = % ATK per stack, "heal:N"
            return c;
        }
    }

    internal sealed class Fighter
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

    internal sealed class BattleSim
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
                double ap = AlienPowerOf((BattleSide)side), mp = worm != null ? worm.S(StatId.MountPower) : 0;
                foreach (var f in F)
                {
                    if ((int)f.Side != side) continue;
                    if (f.Role == CombatRole.Alien || f.Role == CombatRole.Summon) InitSidekick(f, ap);
                    else if (f.Role == CombatRole.Mount) f.Pct[(int)StatId.Atk] += mp;
                }
            }
            foreach (var f in F) InitHp(f);
            // The mount absorbs part of the damage: its HP stat becomes a battle-start shield on the worm.
            foreach (var f in F)
            {
                if (f.Role != CombatRole.Mount) continue;
                var worm = FirstWorm(f.Side);
                if (worm != null && worm.Alive) worm.Shield += Math.Max(0, f.S(StatId.Hp));
            }
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
        private int CapOf(StatusDef def, StatusRules r)
        {
            int cap = def.MaxStacks + (int)Math.Round(r.MaxDelta);
            int burst = BurstAtOf(def, r);
            if (burst > 0) cap = Math.Max(cap, burst);
            return Math.Max(1, cap);
        }

        private static int BurstAtOf(StatusDef def, StatusRules r) => def.BurstAtStacks <= 0 ? 0 : (r.BurstAt > 0 ? r.BurstAt : def.BurstAtStacks);

        // ---- main loop ------------------------------------------------------------------------

        public BattleResult Run()
        {
            Setup();
            foreach (var f in F) { Res.Fighters[f.Index].StartShield = Round0(f.Shield); Res.Fighters[f.Index].StartRage = Round0(f.Rage); }
            int cap = _setup.RoundCap > 0 ? _setup.RoundCap : T.RoundCap;
            CheckOver();
            if (!Over)
                foreach (var f in TurnOrder()) { if (Over) break; if (f.Alive) Fire(f, TriggerKind.OnBattleStart, null); }
            // Eager (aliens pass): a sidekick's first action comes at battle start (its charge then starts empty)
            if (!Over)
                foreach (var f in TurnOrder())
                {
                    if (Over) break;
                    var w = FirstWorm(f.Side);
                    if (!f.Alive || !f.Sidekick || !f.Eager || (w != null && w.Rules.Symbiote)) continue;
                    SidekickAct(f, false);
                    f.Charge = 0;
                }

            while (!Over && Round < cap)
            {
                Round++;
                if (_anyLoop) Snapshot();
                var order = TurnOrder();
                foreach (var f in order) { if (Over) break; if (f.Alive) Fire(f, TriggerKind.OnRoundStart, null); }
                foreach (var f in order) { if (Over) break; if (f.Alive) TakeTurn(f); }
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
            _acting = f;
            EndTurnScopedEffects(f);
            f.ComboThisTurn = false;
            double regen = f.S(StatId.Regen);
            if (regen > 0 && f.Hp < f.MaxHp) Heal(f, f, f.MaxHp * regen / 100.0 * (1 + f.S(StatId.HealPower) / 100.0), BattleAction.Regen, null, false);

            if (!TickStatuses(f)) return;

            bool skip = false, charmed = false;
            foreach (var st in f.Statuses)
            {
                if (!T.Statuses.TryGetValue(st.Id, out var def)) continue;
                if (def.SkipsTurn) skip = true;
                if (def.Charms) charmed = true;
            }
            if (!skip && f.Rules.LoseTurnEvery > 0 && Round % f.Rules.LoseTurnEvery == 0) skip = true; // Borrowed Time's cost
            if (skip)
            {
                Log(BattleAction.SkipTurn, f, f, 0);
                EndTurn(f);
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
                RemoveBuff(f, i);
            }
        }

        /// <summary>The acting part of a turn: OnTurnStart, the basic attack, a full-rage attack, the combo chain, OnTurnEnd.</summary>
        private void Act(Fighter f, bool charmed)
        {
            f.Turns++;
            f.ComboStrikes = 0;
            f.Strikes = 0;
            Fire(f, TriggerKind.OnTurnStart, null);
            if (!f.Alive || Over) return;

            Fighter target = charmed ? CharmTarget(f) : DefaultTarget(f);
            if (target == null)
            {
                if (charmed) Log(BattleAction.SkipTurn, f, f, 0);
                return;
            }
            var rules = f.Rules;
            if (!rules.NoTurnAttack)
            {
                if (rules.BasicAoePct > 0 && !charmed)
                {
                    // Thunderhead: the basic attack hits every enemy at a fraction of its damage
                    var all = new List<Fighter>();
                    foreach (var o in F) if (o.Side != f.Side && o.Alive && o.Targetable) all.Add(o);
                    foreach (var o in all) { if (Over || !f.Alive) break; Attack(f, o, AttackKind.Basic, 0, rules.BasicAoePct / 100.0); }
                }
                else Attack(f, target, AttackKind.Basic, 0);
                if (Over || !f.Alive) return;
            }

            GainRage(f, T.RagePerAction);
            if (f.Rage >= T.RageMax)
            {
                f.Rage -= T.RageMax;
                var rt = charmed ? (target.Alive ? target : null) : DefaultTarget(f);
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
                    double dealt = Attack(f, rt, AttackKind.Rage, 0);
                    Fire(f, TriggerKind.OnRageAttack, rt, StatusId.None, dealt);
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
            double p = CombatMath.Clamp(ShutDown(f) ? 0 : f.S(StatId.ComboChance), 0, cap) / 100.0;
            int links = 0;
            double mult = 1;
            while (!Over && f.Alive && p > 0 && links < T.MaxChain && R.Chance(p))
            {
                var ct = charmed ? (target.Alive ? target : null) : DefaultTarget(f);
                if (ct == null) break;
                if (rules.ComboFalloffPct > 0) mult *= Math.Max(0, 1 - rules.ComboFalloffPct / 100.0);
                f.ComboStrikes++;
                f.ComboThisTurn = true;
                double dealt = Attack(f, ct, AttackKind.Combo, counterDepth, mult);
                Fire(f, TriggerKind.OnCombo, ct, StatusId.None, dealt);
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
            if (worm != null && worm.Rules.Symbiote) return; // Symbiote: sidekicks act only with the worm's rage attacks
            double haste = worm != null ? Math.Max(-90, worm.S(StatId.SidekickHaste)) : 0;
            f.Charge += Math.Max(0, f.S(StatId.Speed)) * (1 + haste / 100.0);
            if (f.Charge < T.SidekickActAt) return;
            f.Charge = Math.Min(T.SidekickActAt, f.Charge - T.SidekickActAt); // at most one own action per turn
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
            Fire(f, TriggerKind.OnAct, null);
            if (Over) return;
            var worm = FirstWorm(f.Side);
            // Hivemind: every sidekick is tuned to a signature; a drone (aliens pass) always triggers the start signature once
            if (worm != null && worm.Alive && (worm.Rules.Hivemind || f.Role == CombatRole.Summon)) HiveSignature(f, worm);
            if (Over) return;
            foreach (var o in F)
            {
                if (Over) break;
                if (o.Side == f.Side && o.Alive && (o.Role == CombatRole.Worm || o.Role == CombatRole.Mount)) Fire(o, TriggerKind.OnSidekickAct, null);
            }
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
                if (st.Fresh) { st.Fresh = false; continue; } // applied during this very turn: it lasts into the next one
                st.Turns--;
                if (st.Turns <= 0)
                {
                    f.Statuses.RemoveAt(i);
                    var e = Log(BattleAction.StatusExpired, f, f, 0);
                    if (e != null) e.Status = st.Id;
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
                for (int k = 0; k < ticks; k++)
                {
                    if (!f.Statuses.Contains(st)) break;
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
            if (r.DotCrit && src != null)
            {
                crit = R.Chance(CombatMath.CritProbability(src.S(StatId.CritChance), T));
                if (crit) dmg *= CombatMath.CritMultiplier(T.BaseCritDamage + src.S(StatId.CritDamage), f.S(StatId.CritResist));
            }
            DealDamage(src, f, dmg, def.IgnoresShield && !r.KeepsShields, BattleAction.StatusTick, crit, st.Id, skillId);
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
        private bool RollCrit(Fighter att)
        {
            bool crit = R.Chance(CombatMath.CritProbability(att.S(StatId.CritChance), T));
            if (ShutDown(att)) return false;
            var rs = RulesOf(att);
            if (rs != null && rs.LastLightPct > 0 && !att.Sidekick && att.HpPct < rs.LastLightPct) return true;
            return crit;
        }

        /// <summary>Hit order: crit roll → block → dodge roll → damage; then on-hit triggers and the counter chain. Returns the damage dealt.</summary>
        private double Attack(Fighter att, Fighter def, AttackKind kind, int counterDepth, double mult = 1)
        {
            if (!att.Alive || !def.Alive || Over) return 0;
            Fire(att, TriggerKind.OnAttack, def);
            if (!att.Alive || !def.Alive || Over) return 0;
            att.Strikes++;

            bool crit = RollCrit(att);
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
                return 0;
            }
            double dodgeP = CombatMath.DodgeProbability(def.S(StatId.Dodge), att.S(StatId.DodgeIgnore), def.ConsecutiveDodges, T);
            if (dodgeP > 0 && R.Chance(dodgeP))
            {
                def.ConsecutiveDodges++;
                var e = Log(ActionFor(kind), att, def, 0);
                if (e != null) e.Dodge = true;
                Fire(def, TriggerKind.OnDodge, att);
                return 0;
            }
            def.ConsecutiveDodges = 0;

            double dmg = ComputeDamage(att, def, StylePct(att.Style, kind), 0, crit, kind, false) * mult;
            if (kind == AttackKind.Counter && att.CounterCharge > 0) { dmg *= 1 + att.CounterCharge / 100.0; att.CounterCharge = 0; } // Grudge
            if (def.GuardHits > 0 && def.GuardPct > 0)
            {
                // a guard (aliens pass) softens the hit; it still lands, so on-hit / when-hit triggers and counters fire
                double prevented = dmg * Math.Min(100, def.GuardPct) / 100.0;
                dmg -= prevented;
                def.GuardHits--;
                var gl = Log(BattleAction.Guarded, def.GuardBy >= 0 && def.GuardBy < F.Count ? F[def.GuardBy] : def, def, prevented);
                if (gl != null) gl.Stacks = def.GuardHits;
                if (def.GuardHits <= 0) { def.GuardPct = 0; def.GuardBy = -1; }
                Fire(def, TriggerKind.OnBlock, att, StatusId.None, prevented);
                if (Over || !def.Alive || !att.Alive) return 0;
            }
            double dealt = DealDamage(att, def, dmg, false, ActionFor(kind), crit, StatusId.None, null);
            double absorbed = _lastAbsorbed;
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
            if (def.HitHistory.Count > 3) def.HitHistory.RemoveAt(0);
            att.HitsLanded++;

            var ar = RulesOf(att);
            bool procs = ar == null || ar.ProcCap <= 0 || att.Strikes <= ar.ProcCap; // Endless Chain: no on-hit effects past the cap
            if (procs)
            {
                Fire(att, TriggerKind.OnHit, def, StatusId.None, dealt);
                if (crit) Fire(att, TriggerKind.OnCrit, def, StatusId.None, dealt);
            }
            if (absorbed > 0) Fire(def, TriggerKind.OnShieldHit, att, StatusId.None, absorbed);
            Fire(def, TriggerKind.OnDamaged, att, StatusId.None, dealt);

            if (Over || !def.Alive || !att.Alive || counterDepth >= T.MaxChain) return dealt;
            var dr = def.Rules;
            bool always = counterDepth == 0 && (dr.CounterAll || def.CounterWindow) && !ShutDown(def);
            double chance = ShutDown(def) ? 0 : def.S(StatId.CounterChance) + (dr.ComboAsCounter ? def.S(StatId.ComboChance) : 0);
            double cp = CombatMath.Clamp(chance, 0, 95) / 100.0 * Math.Pow(T.ChainDecay, counterDepth);
            if (always || (cp > 0 && R.Chance(cp)))
            {
                var ct = att.Targetable ? att : DefaultTarget(def);
                if (ct != null)
                {
                    double cd = Attack(def, ct, AttackKind.Counter, counterDepth + 1);
                    Fire(def, TriggerKind.OnCounter, ct, StatusId.None, cd);
                    // Perfect Form: a counterattack can combo (those strikes are never countered back)
                    if (dr.CounterCombo && !Over && def.Alive && ct.Alive) ComboChain(def, ct, false, T.MaxChain);
                }
            }
            return dealt;
        }

        /// <summary>
        /// Hit damage. A sidekick's hit (aliens pass) is atkPct % (× its AtkPct %) of a full-strength hit at its worm's ATK × alien power:
        /// "15% ATK" is 15% of what that hit would deal through the target's DEF, never floored away by it.
        /// </summary>
        private double ComputeDamage(Fighter att, Fighter def, double atkPct, double bonusPct, bool crit, AttackKind kind, bool trueDamage)
        {
            double critMult = CombatMath.CritMultiplier(T.BaseCritDamage + att.S(StatId.CritDamage), def.S(StatId.CritResist));
            double share = 1;
            if (att.Sidekick) { share = atkPct / 100.0 * att.AtkPct / 100.0; atkPct = 100; }
            return CombatMath.HitDamage(AtkOf(att), atkPct, bonusPct, def.S(StatId.Def), def.S(StatId.DamageReduction), trueDamage,
                crit, critMult, CondMultiplier(att, def, kind), TakenMultiplier(def), T) * DealtMultiplier(att) * share;
        }

        /// <summary>Damage the fighter deals, from its statuses (Chill: −3% per stack, scaled by the chiller's ice damage).</summary>
        private double DealtMultiplier(Fighter att)
        {
            double sum = 0;
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
            if (!target.Alive) return 0;
            amount = Math.Max(0, amount);
            double absorbed = 0;
            if (!ignoreShield && target.Shield > 0) { absorbed = Math.Min(target.Shield, amount); target.Shield -= absorbed; }
            double hpLoss = Math.Min(target.Hp, amount - absorbed);
            target.Hp -= hpLoss;
            double dealt = absorbed + hpLoss;
            if (src != null) src.DamageDealt += dealt;
            if (src != null && src.Side == BattleSide.Player && target.Side == BattleSide.Enemy) Res.PlayerDamageDealt += (long)Math.Round(dealt);
            if (target.Side == BattleSide.Player) Res.PlayerDamageTaken += (long)Math.Round(dealt);
            var e = Log(action, src, target, dealt);
            if (e != null) { e.Crit = crit; e.Status = status; e.SkillId = skillId; }

            if (target.Hp < 0.5 && target.HasFatal && !Over)
            {
                // a fatal hit: OnFatal effects (Last Word, Sacrifice Protocol, Loop) may save the fighter
                target.Hp = 0;
                Fire(target, TriggerKind.OnFatal, src, StatusId.None, amount);
            }
            if (target.Hp < 0.5)
            {
                target.Hp = 0;
                target.Alive = false;
                target.DeathStatuses = new List<StatusInst>(target.Statuses);
                target.Statuses.Clear();
                Log(BattleAction.Death, src, target, 0);
                CheckOver();
                if (src != null && src.Alive) Fire(src, TriggerKind.OnKill, target);
                foreach (var o in F) if (o.Alive && o.Side != target.Side && !Over) Fire(o, TriggerKind.OnEnemyDeath, target);
                // Overkill: damage past the killed fighter's HP carries over to the next enemy
                var ors = RulesOf(src);
                double excess = amount - absorbed - hpLoss;
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
            _lastAbsorbed = absorbed;
            return dealt;
        }

        private void Heal(Fighter src, Fighter target, double amount, BattleAction action, string skillId, bool overhealToShield)
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
            if (extra > 0) target.Shield += extra;
            if (healed > 0 || extra > 0)
            {
                var e = Log(action, src, target, healed + extra);
                if (e != null) e.SkillId = skillId;
            }
        }

        private void GainRage(Fighter f, double baseAmount)
        {
            f.Rage = Math.Min(T.RageOverfillMax, f.Rage + baseAmount * (1 + f.S(StatId.RageGain) / 100.0));
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
                    default: ok = false; break;
                }
                if (!ok) return false;
            }
            return true;
        }

        private void Fire(Fighter owner, TriggerKind kind, Fighter ctx, StatusId ctxStatus = StatusId.None, double amount = 0)
        {
            if (!owner.Alive) return;
            if (Over && kind != TriggerKind.OnBattleWon) return;
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
                        if (t.When != kind || !IsBattleEffect(t.Effect)) continue;
                        if ((kind == TriggerKind.OnStatusApplied || kind == TriggerKind.OnStatusConsumed)
                            && !string.IsNullOrEmpty(t.Param) && ParamHead(t.Param) != ctxStatus.ToString()) continue;
                        if (kind == TriggerKind.OnStackThreshold)
                        {
                            if (!ParseThreshold(t.Param, out var ts, out var tn) || ts != ctxStatus || !(thrPrev < tn && amount >= tn)) continue;
                        }
                        if (Round < owner.Ready[si][ti]) continue;
                        if (t.MaxPerBattle > 0 && owner.Fired[si][ti] >= t.MaxPerBattle) continue;
                        if (!string.IsNullOrEmpty(t.If) && !Check(owner, ctx, t.If)) continue;
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
                        if (t.CooldownRounds > 0) owner.Ready[si][ti] = Round + t.CooldownRounds;
                        owner.Fired[si][ti]++;
                        for (int n = 0; n < times; n++)
                        {
                            Execute(owner, skill, t, ctx, amount);
                            if (!owner.Alive || (Over && kind != TriggerKind.OnBattleWon)) return;
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
            var targets = Targets(owner, t.Target, ctx, t.Stacks);
            switch (t.Effect)
            {
                case EffectKind.Damage:
                case EffectKind.TrueDamage:
                {
                    var el = ElementStat(skill.Family);
                    double bonus = el == StatId.Atk ? 0 : owner.S(el);
                    bool isTrue = t.Effect == EffectKind.TrueDamage;
                    bool arc = t.Target == TargetKind.OtherEnemies;
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
                    // Wide (aliens pass): a sidekick's hit also strikes one random other enemy at WidePct %
                    if (owner.Sidekick && owner.Wide && t.Target != TargetKind.AllEnemies)
                    {
                        var others = new List<Fighter>();
                        foreach (var o in F) if (o.Side != owner.Side && o.Alive && o.Targetable && !targets.Contains(o)) others.Add(o);
                        if (others.Count > 0) targets.Add(others[R.Range(0, others.Count)]);
                        else targets.Add(null);
                    }
                    for (int ti = 0; ti < targets.Count; ti++)
                    {
                        var tg = targets[ti];
                        if (Over || !owner.Alive) break;
                        if (tg == null || !tg.Alive || tg.Side == owner.Side) continue;
                        double wide = owner.Sidekick && owner.Wide && ti == targets.Count - 1 && t.Target != TargetKind.AllEnemies ? C.AlienTuning.WidePct / 100.0 : 1;
                        SkillHit(owner, skill, tg, t.Value, bonus, isTrue, arc, (1 + extra / 100.0) * wide, ro);
                    }
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
                    // Twin (aliens pass): +1 stack on every 2nd action of the sidekick
                    int twin = owner.Sidekick && owner.Twin && owner.ActCount > 0 && owner.ActCount % Math.Max(1, C.AlienTuning.TwinEvery) == 0 ? 1 : 0;
                    foreach (var tg in targets)
                    {
                        if (Over) break;
                        int n = Math.Max(1, t.Stacks) + twin;
                        if (perStatus) n *= CountStatuses(tg);       // Fallout Aura: per different status the target has
                        if (perUnit) n *= Math.Max(0, (int)Math.Round(amount)); // Fallout Wounds: per stack consumed
                        if (n <= 0) continue;
                        ApplyStatus(owner, tg, t.Status, n, t.Duration, skill.Id);
                        // "cd:N": the target can't get it again for N rounds (Chronoslug's Delay, like Freeze's re-apply cooldown)
                        if (cd > 0 && tg.Status(t.Status) != null && tg.StatusCd[(int)t.Status] < Round + cd) tg.StatusCd[(int)t.Status] = Round + cd;
                    }
                    break;
                }
                case EffectKind.Heal:
                    foreach (var tg in targets)
                        if (!tg.Sidekick) // sidekicks have no HP to heal
                            Heal(owner, tg, tg.MaxHp * t.Value / 100.0 * SupportScale(owner) * (1 + owner.S(StatId.HealPower) / 100.0), BattleAction.Heal, skill.Id, t.Param == "overheal_shield");
                    break;
                case EffectKind.Shield:
                    foreach (var tg in targets)
                    {
                        if (!tg.Alive || tg.Sidekick) continue;
                        // "of_amount": a share of the trigger's amount (Rebound Plating: of the counter's damage), else of max HP
                        double basis = HasToken(t.Param, "of_amount") ? Math.Max(0, amount) : tg.MaxHp;
                        double amt = basis * t.Value / 100.0 * SupportScale(owner) * (1 + owner.S(StatId.ShieldPower) / 100.0)
                            * (owner.Sidekick && owner.Bulwark ? 1 + C.AlienTuning.BulwarkPct / 100.0 : 1);
                        if (amt <= 0) continue;
                        tg.Shield += amt;
                        var e = Log(BattleAction.Shield, owner, tg, amt);
                        if (e != null) e.SkillId = skill.Id;
                    }
                    break;
                case EffectKind.GainRage:
                    foreach (var tg in targets)
                    {
                        if (!tg.Alive || tg.Sidekick) continue; // sidekicks have no rage
                        double rage = t.Value * SupportScale(owner);
                        tg.Rage = Math.Min(T.RageOverfillMax, tg.Rage + rage);
                        var e = Log(BattleAction.GainRage, owner, tg, rage);
                        if (e != null) e.SkillId = skill.Id;
                    }
                    break;
                case EffectKind.BuffStat:
                    if (t.Stat == null) break;
                    foreach (var tg in targets)
                    {
                        if (!tg.Alive) continue;
                        bool untilTurn = HasToken(t.Param, "until_turn"), reset = HasToken(t.Param, "reset_no_combo"), through = HasToken(t.Param, "next_turn");
                        // Lingering (aliens pass): a sidekick's timed buff lasts +1 round
                        int rounds = t.Duration > 0 ? t.Duration + (owner.Sidekick && owner.Lingering ? C.AlienTuning.LingeringRounds : 0) : -1;
                        var b = new BuffInst
                        {
                            Stat = t.Stat.Stat, Flat = t.Stat.Flat, Percent = t.Stat.Percent, Rounds = rounds,
                            Sidekick = owner.Sidekick ? owner.Index : -1, UntilTurn = untilTurn, ResetNoCombo = reset, ThroughTurn = through, SkillId = skill.Id,
                        };
                        if (untilTurn || through) b.Rounds = -1;
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
                    foreach (var tg in targets) RemoveStacks(owner, tg, t.Status, t.Stacks, skill.Id);
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
                    break;
                }
                case EffectKind.AllyAction:
                {
                    // Stacks extra sidekick actions, round-robin over the side's sidekicks (a lone sidekick acts several times); "all": each acts;
                    // "others" (Tidecaller): another sidekick than the owner, rotating with the owner's actions. Capped per round (ReserveExtra).
                    var kicks = new List<Fighter>();
                    bool others = t.Param == "others";
                    foreach (var o in F) if (o.Side == owner.Side && o.Alive && o.Sidekick && !(others && o == owner)) kicks.Add(o);
                    if (kicks.Count == 0) break;
                    bool all = t.Param == "all";
                    int start = all ? 0 : others ? owner.ActCount % kicks.Count : Round % kicks.Count;
                    int count = all ? kicks.Count : Math.Max(1, t.Stacks);
                    for (int n = 0; n < count; n++)
                    {
                        if (Over || !owner.Alive) break;
                        var a = kicks[(start + n) % kicks.Count];
                        if (!a.Alive) continue;
                        if (!ReserveExtra(owner.Side)) break;
                        Mark(owner, a, skill); // the extra action, credited to the skill
                        SidekickAct(a, true);
                    }
                    break;
                }
                case EffectKind.Guard:
                    // aliens pass: one guard per fighter, a new one refreshes it (Bulwark +20%, Lingering +1 hit); hits still land
                    foreach (var tg in targets)
                    {
                        if (!tg.Alive || !tg.Targetable || t.Value <= 0) continue;
                        var at = C.AlienTuning;
                        tg.GuardPct = Math.Min(at.GuardCapPct, t.Value * (owner.Sidekick && owner.Bulwark ? 1 + at.BulwarkPct / 100.0 : 1));
                        tg.GuardHits = 1 + (owner.Sidekick && owner.Lingering ? 1 : 0);
                        tg.GuardBy = owner.Index;
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
                    int n = (int)Math.Floor(st.Stacks * t.Value / 100.0);
                    if (n <= 0) break;
                    foreach (var tg in targets) { if (Over) break; if (tg != ctx) ApplyStatus(owner, tg, t.Status, n, 0, skill.Id); }
                    break;
                }
                case EffectKind.TickStatus:
                    foreach (var tg in targets)
                    {
                        if (Over || !tg.Alive) continue;
                        var st = tg.Status(t.Status);
                        if (st == null || tg.TickRound[(int)t.Status] == Round || !T.Statuses.TryGetValue(t.Status, out var sd) || sd.DotAtkPct <= 0) continue;
                        tg.TickRound[(int)t.Status] = Round; // one instant tick per enemy per round
                        TickOne(tg, st, sd, t.Value / 100.0, skill.Id);
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
                    double basis = Math.Max(0, amount) * t.Value / 100.0;
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
                    if (owner.Hp < 1) owner.Hp = 1;
                    {
                        var e = Log(BattleAction.CheatDeath, owner, owner, 0);
                        if (e != null) e.SkillId = skill.Id;
                    }
                    if (HasToken(t.Param, "counter_window")) owner.CounterWindow = true; // Last Word: counter every hit until your next turn
                    break;
                case EffectKind.FreeRageAttack:
                {
                    var tg = targets.Count > 0 ? targets[0] : DefaultTarget(owner);
                    if (tg == null || !tg.Targetable) tg = DefaultTarget(owner);
                    if (tg == null) break;
                    Mark(owner, owner, skill);
                    double dealt = Attack(owner, tg, AttackKind.Rage, 0);
                    Fire(owner, TriggerKind.OnRageAttack, tg, StatusId.None, dealt);
                    break;
                }
                case EffectKind.Charge:
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
                    if (kick == null) break;
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
                    if (!owner.Attacked) break;
                    var rs = owner.Rules;
                    var kind = rs.EchoStrongest ? owner.BestKind : owner.LastKind;
                    int ti = rs.EchoStrongest ? owner.BestTarget : owner.LastTarget;
                    double mult = Math.Max(0, 1 + owner.S(StatId.EchoDamage) / 100.0);
                    for (int n = 0; n < Math.Max(1, t.Stacks); n++)
                    {
                        if (Over || !owner.Alive) break;
                        var tg = ti >= 0 && ti < F.Count && F[ti].Alive && F[ti].Side != owner.Side ? F[ti] : DefaultTarget(owner);
                        if (tg == null) break;
                        var e = Log(BattleAction.Echo, owner, tg, 0);
                        if (e != null) e.SkillId = skill.Id;
                        Attack(owner, tg, kind == AttackKind.Skill || kind == AttackKind.Arc ? AttackKind.Basic : kind, 0, mult);
                    }
                    break;
                }
                case EffectKind.Rewind:
                {
                    double sum = 0;
                    int n = Math.Max(1, t.Stacks);
                    for (int i = owner.HitHistory.Count - 1; i >= 0 && n > 0; i--, n--) sum += owner.HitHistory[i];
                    double back = Math.Min(sum, owner.MaxHp - owner.Hp);
                    if (back <= 0) break;
                    owner.Hp += back;
                    var e = Log(BattleAction.Heal, owner, owner, back);
                    if (e != null) e.SkillId = skill.Id;
                    break;
                }
                case EffectKind.Loop:
                    Restore(owner, skill);
                    break;
            }
        }

        /// <summary>A skill's damage hit on one target (Damage / TrueDamage): crit roll, lifesteal (a sidekick's heals its worm), triggers.</summary>
        private double SkillHit(Fighter owner, SkillDef skill, Fighter tg, double pct, double bonus, bool isTrue, bool arc, double mult, Fighter ro)
        {
            bool crit = RollCrit(owner);
            double dmg = ComputeDamage(owner, tg, pct, bonus, crit, arc ? AttackKind.Arc : AttackKind.Skill, isTrue) * mult;
            double dealt = DealDamage(owner, tg, dmg, false, BattleAction.Skill, crit, StatusId.None, skill.Id);
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
            var before = tg.Status(t.Status);
            if (before == null) return;
            bool atMax = T.Statuses.TryGetValue(t.Status, out var sdef) && before.Stacks >= CapOf(sdef, SRules(before));
            int n = RemoveStacks(owner, tg, t.Status, spec.Take, skill.Id);
            if (n <= 0) return;
            var worm = owner.Targetable ? owner : FirstWorm(owner.Side);
            if (spec.HealAtk > 0 && worm != null && worm.Alive)
            {
                // Thorn Harvest (aliens pass): heal the worm HealAtk % of ATK (a sidekick's: its worm's × alien power) per stack consumed
                var hr = RulesOf(owner);
                double pct = spec.HealAtk * n * (1 + (hr != null ? hr.Of(t.Status).ConsumeHealPct : 0) / 100.0);
                Heal(owner, worm, AtkOf(owner) * pct / 100.0, BattleAction.Heal, skill.Id, false);
            }
            if (spec.Dmg > 0)
            {
                var el = ElementStat(skill.Family);
                bool crit = RollCrit(owner);
                double dmg = ComputeDamage(owner, tg, spec.Dmg * n, el == StatId.Atk ? 0 : owner.S(el), crit, AttackKind.Skill, false);
                double dealt = DealDamage(owner, tg, dmg, false, BattleAction.Skill, crit, StatusId.None, skill.Id);
                var rules = RulesOf(owner);
                double healPct = spec.Heal * (1 + (rules != null ? rules.Of(t.Status).ConsumeHealPct : 0) / 100.0);
                if (healPct > 0 && dealt > 0 && worm != null && worm.Alive) Heal(owner, worm, dealt * healPct / 100.0, BattleAction.Heal, skill.Id, false);
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
                worm.Rage = Math.Min(T.RageOverfillMax, worm.Rage + spec.RefundMax);
                var e = Log(BattleAction.GainRage, owner, worm, spec.RefundMax);
                if (e != null) e.SkillId = skill.Id;
            }
            // a sidekick's consume counts as its worm's (Fallout Wounds and other consume payoffs fire)
            var consumer = owner.Sidekick && worm != null ? worm : owner;
            if (!Over && consumer.Alive) Fire(consumer, TriggerKind.OnStatusConsumed, tg, t.Status, n);
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
            double dmg = st.Power * def.BurstAtkPct / 100.0 * (1 + ((heater != null ? Math.Max(0, heater.S(StatId.SupernovaDamage)) : 0) + bonusPct) / 100.0);
            var hit = new List<Fighter>();
            foreach (var o in F) if (o.Side == x.Side && o.Alive && o.Targetable) hit.Add(o);
            foreach (var o in hit)
            {
                if (Over) return;
                double d = dmg * CombatMath.DamageReductionFactor(o.S(StatId.DamageReduction), T) * TakenMultiplier(o);
                DealDamage(heater, o, d, false, BattleAction.Supernova, false, StatusId.Heat, null);
            }
            if (Over) return;
            var side = heater != null ? heater.Side : (x.Side == BattleSide.Player ? BattleSide.Enemy : BattleSide.Player);
            var worm = FirstWorm(side);
            if (worm != null && worm.Alive && worm.Side != x.Side && !rules.NoRecoil)
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
            if (src != null && src.Sidekick)
            {
                var w = FirstWorm(src.Side);
                if (w != null) src = w;
            }
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
            int cap = CapOf(def, sr);
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
                if (count >= T.MaxFightersPerSide || (max > 0 && mine >= max)) return;
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
