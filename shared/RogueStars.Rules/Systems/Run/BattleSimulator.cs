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

        /// <summary>A sidekick's ability power from its HP / ATK / DEF (before alien power): it is never hit, so all three feed its ability.</summary>
        public static double SidekickPower(double atk, double hp, double def, BattleTuning t) =>
            Math.Max(0, atk) + Math.Max(0, hp) * t.SidekickHpToPower + Math.Max(0, def) * t.SidekickDefToPower;

        /// <summary>Ability power of an alien's combat stats (UI: the alien screens), before the loadout's alien power.</summary>
        public static double SidekickPower(StatBlock stats, ContentDb content) =>
            SidekickPower(stats.Value(StatId.Atk), stats.Value(StatId.Hp), stats.Value(StatId.Def), (content ?? ContentDb.Default).Combat);

        /// <summary>
        /// How a sidekick's heals / shields / rage / buffs scale with its power before alien power: (power / ref)^exp, at most the cap
        /// (alien power then multiplies them in full, like it multiplies damage).
        /// </summary>
        public static double SidekickSupportScale(double power, BattleTuning t) =>
            Math.Min(t.SidekickSupportCap, Math.Pow(Math.Max(0, power) / Math.Max(1, t.SidekickPowerRef), t.SidekickSupportExp));

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
            return block;
        }
    }

    // ------------------------------------------------------------------------------------------
    // Internal simulation state (never serialized)
    // ------------------------------------------------------------------------------------------

    internal enum AttackKind { Basic, Rage, Combo, Counter, Skill }

    internal sealed class StatusInst
    {
        public StatusId Id;
        public int Stacks;
        public int Turns;
        public double Power;
        public int Source;
    }

    internal sealed class BuffInst
    {
        public StatId Stat;
        public double Flat, Percent;
        public int Rounds; // -1 = rest of battle
        /// <summary>A sidekick's buff (its index, else -1): the same sidekick's next action refreshes it instead of stacking.</summary>
        public int Sidekick = -1;
    }

    internal sealed class CondBonus
    {
        public string Cond;
        public StatusId Status;
        public double Value;
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
        public readonly List<StatusInst> Statuses = new List<StatusInst>();
        public readonly List<BuffInst> Buffs = new List<BuffInst>();
        public readonly int[] StatusCd = new int[16];
        public readonly List<CondBonus> Conds = new List<CondBonus>();
        public double DamageDealt;
        public int SummonedBy = -1;
        /// <summary>Sidekick (alien, player-side summon): never hit, charges by Speed and acts with its ability.</summary>
        public bool Sidekick;
        public double Charge;
        /// <summary>The side's alien power (%) a sidekick got at its start: it multiplies its support abilities in full.</summary>
        public double AlienPower;
        /// <summary>Indices of the sidekicks guarding this fighter, oldest first (Block): each intercepts one hit.</summary>
        public readonly List<int> Guards = new List<int>();

        public double S(StatId s)
        {
            int i = (int)s;
            return (Flat[i] + BFlat[i]) * (1 + (Pct[i] + BPct[i]) / 100.0);
        }

        public StatusInst Status(StatusId id)
        {
            for (int i = 0; i < Statuses.Count; i++) if (Statuses[i].Id == id) return Statuses[i];
            return null;
        }
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
                foreach (var m in s.Stats) { f.Flat[(int)m.Stat] += m.Flat; f.Pct[(int)m.Stat] += m.Percent; }
                foreach (var t in s.Triggers)
                {
                    if (t.When != TriggerKind.Passive || t.Param == null || !t.Param.StartsWith("cond:", StringComparison.Ordinal)) continue;
                    var cb = new CondBonus { Cond = t.Param.Substring(5), Value = t.Value };
                    if (cb.Cond.StartsWith("vs_status:", StringComparison.Ordinal))
                    {
                        Enum.TryParse(cb.Cond.Substring(10), out cb.Status);
                        cb.Cond = "vs_status";
                    }
                    f.Conds.Add(cb);
                }
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
            f.StartHp = f.Hp;
            if (f.Hp < 0.5) { f.Hp = 0; f.Alive = false; }
        }

        private void Setup()
        {
            foreach (var s in _setup.Player) MakeFighter(s, BattleSide.Player);
            foreach (var s in _setup.Enemy) MakeFighter(s, BattleSide.Enemy);
            // Alien / mount power from the side's worm (GDD §4.1). The mount's own AlienPower (Hive Carrier weapons sit on the mount)
            // reaches the sidekicks too. Aliens are sidekicks: their HP / ATK / DEF become ability power (InitSidekick).
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
        /// A sidekick's HP, ATK and DEF fold into its ability power (its ATK in battle): ATK + HP × HpToPower + DEF × DefToPower, then
        /// × (1 + alien power %). Its charge starts at SidekickStartCharge.
        /// </summary>
        private void InitSidekick(Fighter f, double alienPower)
        {
            f.Sidekick = true;
            double power = BattleSimulator.SidekickPower(f.S(StatId.Atk), f.S(StatId.Hp), f.S(StatId.Def), T);
            f.Flat[(int)StatId.Atk] = power;
            f.Pct[(int)StatId.Atk] = alienPower;
            f.AlienPower = alienPower;
            f.Charge = T.SidekickStartCharge;
        }

        private double AlienPowerOf(BattleSide side)
        {
            var worm = FirstWorm(side);
            double ap = worm != null ? worm.S(StatId.AlienPower) : 0;
            foreach (var f in F) if (f.Side == side && f.Role == CombatRole.Mount) ap += f.S(StatId.AlienPower);
            return ap;
        }

        /// <summary>Support abilities (heal, shield, rage, buffs) of a sidekick scale with its power; everyone else's are as written.</summary>
        private double SupportScale(Fighter owner)
        {
            if (!owner.Sidekick) return 1;
            double ap = 1 + owner.AlienPower / 100.0;
            return BattleSimulator.SidekickSupportScale(owner.S(StatId.Atk) / Math.Max(0.01, ap), T) * Math.Max(0, ap);
        }

        private Fighter FirstWorm(BattleSide side)
        {
            foreach (var f in F) if (f.Side == side && f.Role == CombatRole.Worm) return f;
            return null;
        }

        // ---- main loop ------------------------------------------------------------------------

        public BattleResult Run()
        {
            Setup();
            foreach (var f in F) { Res.Fighters[f.Index].StartShield = Round0(f.Shield); Res.Fighters[f.Index].StartRage = Round0(f.Rage); }
            int cap = _setup.RoundCap > 0 ? _setup.RoundCap : T.RoundCap;
            CheckOver();
            if (!Over)
                foreach (var f in TurnOrder()) { if (Over) break; if (f.Alive) Fire(f, TriggerKind.OnBattleStart, null); }

            while (!Over && Round < cap)
            {
                Round++;
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
            double regen = f.S(StatId.Regen);
            if (regen > 0 && f.Hp < f.MaxHp) Heal(f, f, f.MaxHp * regen / 100.0 * (1 + f.S(StatId.HealPower) / 100.0), BattleAction.Regen, null, false);

            for (int i = 0; i < f.Statuses.Count; i++)
            {
                var st = f.Statuses[i];
                if (!T.Statuses.TryGetValue(st.Id, out var def) || def.DotAtkPct <= 0) continue;
                double dmg = st.Power * def.DotAtkPct / 100.0 * st.Stacks * CombatMath.DamageReductionFactor(f.S(StatId.DamageReduction), T) * TakenMultiplier(f);
                Fighter src = st.Source >= 0 && st.Source < F.Count ? F[st.Source] : null;
                DealDamage(src, f, dmg, def.IgnoresShield, BattleAction.StatusTick, false, false, st.Id, null);
                if (!f.Alive || Over) return;
            }

            bool skip = false, charmed = false;
            foreach (var st in f.Statuses)
            {
                if (!T.Statuses.TryGetValue(st.Id, out var def)) continue;
                if (def.SkipsTurn) skip = true;
                if (def.Charms) charmed = true;
            }
            if (skip)
            {
                Log(BattleAction.SkipTurn, f, f, 0);
                EndTurn(f);
                return;
            }

            Fire(f, TriggerKind.OnTurnStart, null);
            if (!f.Alive || Over) return;

            Fighter target = charmed ? CharmTarget(f) : DefaultTarget(f);
            if (target == null)
            {
                if (charmed) Log(BattleAction.SkipTurn, f, f, 0);
                EndTurn(f);
                return;
            }
            Attack(f, target, AttackKind.Basic, 0);
            if (Over || !f.Alive) { if (f.Alive) EndTurn(f); return; }

            GainRage(f, T.RagePerAction);
            if (f.Rage >= T.RageMax)
            {
                f.Rage -= T.RageMax;
                var rt = charmed ? (target.Alive ? target : null) : DefaultTarget(f);
                if (rt != null)
                {
                    Attack(f, rt, AttackKind.Rage, 0);
                    Fire(f, TriggerKind.OnRageAttack, rt);
                }
            }

            double p = CombatMath.Clamp(f.S(StatId.ComboChance), 0, 95) / 100.0;
            int links = 0;
            while (!Over && f.Alive && p > 0 && links < T.MaxChain && R.Chance(p))
            {
                var ct = charmed ? (target.Alive ? target : null) : DefaultTarget(f);
                if (ct == null) break;
                Attack(f, ct, AttackKind.Combo, 0);
                Fire(f, TriggerKind.OnCombo, ct);
                p *= T.ChainDecay;
                links++;
            }
            if (f.Alive) EndTurn(f);
        }

        /// <summary>A sidekick's turn: it charges Speed × (1 + haste %); a full charge (SidekickActAt) is spent on one action.</summary>
        private void SidekickTurn(Fighter f)
        {
            var worm = FirstWorm(f.Side);
            double haste = worm != null ? Math.Max(-90, worm.S(StatId.SidekickHaste)) : 0;
            f.Charge += Math.Max(0, f.S(StatId.Speed)) * (1 + haste / 100.0);
            if (f.Charge < T.SidekickActAt) return;
            f.Charge = Math.Min(T.SidekickActAt, f.Charge - T.SidekickActAt); // at most one own action per turn
            SidekickAct(f, false);
        }

        /// <summary>
        /// A sidekick acts: a SidekickAct marker, its ability (OnAct triggers), then OnSidekickAct on its side's worm and mount.
        /// </summary>
        private void SidekickAct(Fighter f, bool extra)
        {
            if (!f.Alive || Over) return;
            string ability = null;
            foreach (var sk in f.Skills)
                foreach (var t in sk.Triggers)
                    if (ability == null && t.When == TriggerKind.OnAct) ability = sk.Id;
            var e = Log(BattleAction.SidekickAct, f, f, 0);
            if (e != null) { e.SkillId = ability; e.Stacks = extra ? 1 : 0; }
            Fire(f, TriggerKind.OnAct, null);
            if (Over) return;
            foreach (var o in F)
            {
                if (Over) break;
                if (o.Side == f.Side && o.Alive && (o.Role == CombatRole.Worm || o.Role == CombatRole.Mount)) Fire(o, TriggerKind.OnSidekickAct, null);
            }
        }

        private void EndTurn(Fighter f)
        {
            for (int i = f.Statuses.Count - 1; i >= 0; i--)
            {
                var st = f.Statuses[i];
                if (T.Statuses.TryGetValue(st.Id, out var sdef) && sdef.Permanent) continue; // Heat, Radiation, Bleed stay
                st.Turns--;
                if (st.Turns <= 0)
                {
                    f.Statuses.RemoveAt(i);
                    var e = Log(BattleAction.StatusExpired, f, f, 0);
                    if (e != null) e.Status = st.Id;
                }
            }
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
                    if (b.Rounds <= 0)
                    {
                        f.BFlat[(int)b.Stat] -= b.Flat;
                        f.BPct[(int)b.Stat] -= b.Percent;
                        f.Buffs.RemoveAt(i);
                    }
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
                case AttackKind.Skill: return BattleAction.Skill;
                default: return BattleAction.Basic;
            }
        }

        /// <summary>Hit order: crit roll → dodge roll → damage → parry; then on-hit triggers and counter chain.</summary>
        private void Attack(Fighter att, Fighter def, AttackKind kind, int counterDepth)
        {
            if (!att.Alive || !def.Alive || Over) return;
            Fire(att, TriggerKind.OnAttack, def);
            if (!att.Alive || !def.Alive || Over) return;

            bool crit = R.Chance(CombatMath.CritProbability(att.S(StatId.CritChance), T));
            if (def.Guards.Count > 0)
            {
                // a blocking sidekick intercepts the hit: nothing lands, no on-hit or when-hit triggers
                var blocker = F[def.Guards[0]];
                def.Guards.RemoveAt(0);
                double stopped = ComputeDamage(att, def, StylePct(att.Style, kind), 0, crit, kind, false);
                var b = Log(BattleAction.Block, blocker, def, stopped);
                if (b != null) { b.Crit = crit; b.Stacks = def.Guards.Count; }
                return;
            }
            double dodgeP = CombatMath.DodgeProbability(def.S(StatId.Dodge), att.S(StatId.DodgeIgnore), def.ConsecutiveDodges, T);
            if (dodgeP > 0 && R.Chance(dodgeP))
            {
                def.ConsecutiveDodges++;
                var e = Log(ActionFor(kind), att, def, 0);
                if (e != null) e.Dodge = true;
                Fire(def, TriggerKind.OnDodge, att);
                return;
            }
            def.ConsecutiveDodges = 0;

            double dmg = ComputeDamage(att, def, StylePct(att.Style, kind), 0, crit, kind, false);
            bool parried = false;
            double parryP = CombatMath.Clamp(CombatMath.SoftCap(def.S(StatId.ParryChance), T.ParryKnee), 0, T.MaxParry) / 100.0;
            if (parryP > 0 && R.Chance(parryP))
            {
                parried = true;
                dmg *= 1 - Math.Min(T.MaxParryReduction, T.BaseParryReduction + def.S(StatId.ParryReduction)) / 100.0;
            }
            double dealt = DealDamage(att, def, dmg, false, ActionFor(kind), crit, parried, StatusId.None, null);
            double ls = att.S(StatId.LifeSteal);
            // untargetable fighters (aliens, mount) are never hurt: their lifesteal feeds the side's worm (Vampiric aliens)
            var leech = att.Targetable ? att : (FirstWorm(att.Side) ?? att);
            if (dealt > 0 && ls > 0 && att.Alive) Heal(att, leech, dealt * ls / 100.0, BattleAction.LifeSteal, null, false);
            if (def.Alive) GainRage(def, T.RagePerHitTaken);

            Fire(att, TriggerKind.OnHit, def);
            if (crit) Fire(att, TriggerKind.OnCrit, def);
            if (parried) Fire(def, TriggerKind.OnParry, att);
            Fire(def, TriggerKind.OnDamaged, att);

            if (Over || !def.Alive || !att.Alive || counterDepth >= T.MaxChain) return;
            double cp = CombatMath.Clamp(def.S(StatId.CounterChance), 0, 95) / 100.0 * Math.Pow(T.ChainDecay, counterDepth);
            if (cp > 0 && R.Chance(cp))
            {
                var ct = att.Targetable ? att : DefaultTarget(def);
                if (ct != null)
                {
                    Attack(def, ct, AttackKind.Counter, counterDepth + 1);
                    Fire(def, TriggerKind.OnCounter, ct);
                }
            }
        }

        private double ComputeDamage(Fighter att, Fighter def, double atkPct, double bonusPct, bool crit, AttackKind kind, bool trueDamage)
        {
            double critMult = CombatMath.CritMultiplier(T.BaseCritDamage + att.S(StatId.CritDamage), def.S(StatId.CritResist));
            return CombatMath.HitDamage(att.S(StatId.Atk), atkPct, bonusPct, def.S(StatId.Def), def.S(StatId.DamageReduction), trueDamage,
                crit, critMult, CondMultiplier(att, def, kind), TakenMultiplier(def), T) * DealtMultiplier(att);
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
                bool ok;
                switch (c.Cond)
                {
                    case "vs_boss": ok = def.Role == CombatRole.Boss; break;
                    case "vs_elite": ok = def.Role == CombatRole.Elite || def.Role == CombatRole.Boss; break;
                    case "vs_high_hp": ok = ratio > T.HighHpCondPct; break;
                    case "vs_low_hp": ok = ratio < T.LowHpCondPct; break;
                    case "vs_shielded": ok = def.Shield > 0; break;
                    case "vs_status": ok = def.Status(c.Status) != null; break;
                    case "basic": ok = kind == AttackKind.Basic; break;
                    case "combo": ok = kind == AttackKind.Combo; break;
                    case "counter": ok = kind == AttackKind.Counter; break;
                    case "rage": ok = kind == AttackKind.Rage; break;
                    case "skill": ok = kind == AttackKind.Skill; break;
                    default: ok = false; break;
                }
                if (ok) sum += c.Value;
            }
            return Math.Max(0, 1 + sum / 100.0);
        }

        private double TakenMultiplier(Fighter def)
        {
            double sum = 0;
            foreach (var st in def.Statuses)
                if (T.Statuses.TryGetValue(st.Id, out var sd) && sd.DamageTakenPctPerStack != 0)
                    sum += sd.DamageTakenPctPerStack * st.Stacks * (!AtkBased(sd) && sd.HasElement && st.Power > 0 ? st.Power : 1);
            return Math.Max(0, 1 + sum / 100.0);
        }

        private double DealDamage(Fighter src, Fighter target, double amount, bool ignoreShield, BattleAction action, bool crit, bool parried, StatusId status, string skillId)
        {
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
            if (e != null) { e.Crit = crit; e.Parry = parried; e.Status = status; e.SkillId = skillId; }

            if (target.Hp < 0.5)
            {
                target.Hp = 0;
                target.Alive = false;
                target.Statuses.Clear();
                Log(BattleAction.Death, src, target, 0);
                CheckOver();
                if (src != null && src.Alive) Fire(src, TriggerKind.OnKill, target);
            }
            else if (!target.LowHpFired && target.Hp < target.MaxHp * T.LowHpTriggerPct / 100.0)
            {
                target.LowHpFired = true;
                Fire(target, TriggerKind.OnLowHp, src);
            }
            return dealt;
        }

        private void Heal(Fighter src, Fighter target, double amount, BattleAction action, string skillId, bool overhealToShield)
        {
            if (!target.Alive || amount <= 0) return;
            foreach (var st in target.Statuses)
                if (T.Statuses.TryGetValue(st.Id, out var sd) && sd.HealReductionPct > 0) amount *= 1 - Math.Min(100, sd.HealReductionPct) / 100.0;
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
                    return true;
                default:
                    return false;
            }
        }

        private void Fire(Fighter owner, TriggerKind kind, Fighter ctx, StatusId ctxStatus = StatusId.None)
        {
            if (!owner.Alive) return;
            if (Over && kind != TriggerKind.OnBattleWon) return;
            if (_depth >= 4) return;
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
                        if (kind == TriggerKind.OnStatusApplied && !string.IsNullOrEmpty(t.Param) && t.Param != ctxStatus.ToString()) continue;
                        if (Round < owner.Ready[si][ti]) continue;
                        double chance = t.Chance;
                        if (t.Effect == EffectKind.ApplyStatus) chance *= 1 + owner.S(StatId.StatusChance) / 100.0;
                        if (chance < 100 && !R.Chance(chance / 100.0)) continue;
                        if (t.CooldownRounds > 0) owner.Ready[si][ti] = Round + t.CooldownRounds;
                        Execute(owner, skill, t, ctx);
                        if (!owner.Alive || (Over && kind != TriggerKind.OnBattleWon)) return;
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
                    for (int n = Math.Max(1, count); n > 0 && opts.Count > 0; n--)
                    {
                        int k = R.Range(0, opts.Count);
                        list.Add(opts[k]);
                        opts.RemoveAt(k);
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

        private void Execute(Fighter owner, SkillDef skill, SkillTrigger t, Fighter ctx)
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
                    foreach (var tg in targets)
                    {
                        if (Over || !owner.Alive) break;
                        if (!tg.Alive || tg.Side == owner.Side) continue;
                        bool crit = R.Chance(CombatMath.CritProbability(owner.S(StatId.CritChance), T));
                        double dmg = ComputeDamage(owner, tg, t.Value, bonus, crit, AttackKind.Skill, isTrue);
                        double dealt = DealDamage(owner, tg, dmg, false, BattleAction.Skill, crit, false, StatusId.None, skill.Id);
                        // a sidekick's lifesteal (Vampiric) heals its worm: the sidekick itself is never hurt
                        double ls = owner.Sidekick ? owner.S(StatId.LifeSteal) : 0;
                        var lw = ls > 0 && dealt > 0 ? FirstWorm(owner.Side) : null;
                        if (lw != null && lw.Alive) Heal(owner, lw, dealt * ls / 100.0, BattleAction.LifeSteal, skill.Id, false);
                        if (tg.Alive) Fire(tg, TriggerKind.OnDamaged, owner);
                    }
                    break;
                }
                case EffectKind.ApplyStatus:
                    foreach (var tg in targets) { if (Over) break; ApplyStatus(owner, tg, t.Status, Math.Max(1, t.Stacks), t.Duration, skill.Id); }
                    break;
                case EffectKind.Heal:
                    foreach (var tg in targets)
                        if (!tg.Sidekick) // sidekicks have no HP to heal
                            Heal(owner, tg, tg.MaxHp * t.Value / 100.0 * SupportScale(owner) * (1 + owner.S(StatId.HealPower) / 100.0), BattleAction.Heal, skill.Id, t.Param == "overheal_shield");
                    break;
                case EffectKind.Shield:
                    foreach (var tg in targets)
                    {
                        if (!tg.Alive || tg.Sidekick) continue;
                        double amt = tg.MaxHp * t.Value / 100.0 * SupportScale(owner) * (1 + owner.S(StatId.ShieldPower) / 100.0);
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
                        double k = SupportScale(owner);
                        var b = new BuffInst { Stat = t.Stat.Stat, Flat = t.Stat.Flat * k, Percent = t.Stat.Percent * k, Rounds = t.Duration > 0 ? t.Duration : -1, Sidekick = owner.Sidekick ? owner.Index : -1 };
                        if (owner.Sidekick)
                        {
                            // a sidekick's buff refreshes (its next action renews it) rather than stacking with itself
                            for (int bi = tg.Buffs.Count - 1; bi >= 0; bi--)
                            {
                                var old = tg.Buffs[bi];
                                if (old.Sidekick != owner.Index || old.Stat != b.Stat) continue;
                                tg.BFlat[(int)old.Stat] -= old.Flat;
                                tg.BPct[(int)old.Stat] -= old.Percent;
                                tg.Buffs.RemoveAt(bi);
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
                    var e = Log(BattleAction.Buff, owner, owner, 0); // marks the extra strike as the skill's
                    if (e != null) e.SkillId = skill.Id;
                    Attack(owner, tg, counter ? AttackKind.Counter : AttackKind.Combo, counter ? 1 : 0);
                    Fire(owner, counter ? TriggerKind.OnCounter : TriggerKind.OnCombo, tg);
                    break;
                }
                case EffectKind.AllyAction:
                {
                    // Stacks extra sidekick actions, round-robin over the side's sidekicks (a lone sidekick acts several times)
                    var kicks = new List<Fighter>();
                    foreach (var o in F) if (o.Side == owner.Side && o.Alive && o.Sidekick) kicks.Add(o);
                    if (kicks.Count == 0) break;
                    int start = Round % kicks.Count;
                    for (int n = 0; n < Math.Max(1, t.Stacks); n++)
                    {
                        if (Over || !owner.Alive) break;
                        var a = kicks[(start + n) % kicks.Count];
                        if (!a.Alive) continue;
                        var e = Log(BattleAction.Buff, owner, a, 0); // the extra action, credited to the skill
                        if (e != null) e.SkillId = skill.Id;
                        SidekickAct(a, true);
                    }
                    break;
                }
                case EffectKind.Block:
                    foreach (var tg in targets)
                    {
                        if (!tg.Alive || !tg.Targetable) continue;
                        for (int n = 0; n < Math.Max(1, t.Stacks) && tg.Guards.Count < T.MaxGuards; n++) tg.Guards.Add(owner.Index);
                        var e = Log(BattleAction.Guard, owner, tg, 0);
                        if (e != null) { e.SkillId = skill.Id; e.Stacks = tg.Guards.Count; }
                    }
                    break;
            }
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
        /// Consumes every stack of a status on the target: Value % ATK skill damage per stack (element bonus of the skill's family),
        /// and Param "heal:N" heals the owner's worm for N% of the damage dealt (Bleed's lifesteal).
        /// </summary>
        private void Consume(Fighter owner, SkillDef skill, SkillTrigger t, Fighter tg)
        {
            if (!tg.Alive || tg.Side == owner.Side) return;
            int n = RemoveStacks(owner, tg, t.Status, 0, skill.Id);
            if (n <= 0) return;
            var el = ElementStat(skill.Family);
            bool crit = R.Chance(CombatMath.CritProbability(owner.S(StatId.CritChance), T));
            double dmg = ComputeDamage(owner, tg, t.Value * n, el == StatId.Atk ? 0 : owner.S(el), crit, AttackKind.Skill, false);
            double dealt = DealDamage(owner, tg, dmg, false, BattleAction.Skill, crit, false, StatusId.None, skill.Id);
            var worm = owner.Targetable ? owner : FirstWorm(owner.Side);
            double healPct = ConsumeHealPct(t);
            if (healPct > 0 && dealt > 0 && worm != null && worm.Alive) Heal(owner, worm, dealt * healPct / 100.0, BattleAction.Heal, skill.Id, false);
        }

        /// <summary>The "heal:N" part of a consume trigger's Param (0 when absent).</summary>
        internal static double ConsumeHealPct(SkillTrigger t)
        {
            double v = 0;
            if (t.Param != null && t.Param.StartsWith("heal:", StringComparison.Ordinal))
                double.TryParse(t.Param.Substring(5), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
            return v;
        }

        /// <summary>
        /// Supernova (Heat at its threshold): the fighter explodes. Every targetable fighter of its side (itself included) takes
        /// BurstAtkPct % of the heater's element-scaled ATK × (1 + heater's SupernovaDamage %), DR and damage-taken statuses apply;
        /// the heater's side's worm takes BurstRecoilPct % of its max HP × (1 − SupernovaGuard %); the Heat is gone. The heater's
        /// OnSupernova triggers fire last.
        /// </summary>
        private void Supernova(Fighter x, StatusInst st, StatusDef def)
        {
            x.Statuses.Remove(st);
            Fighter heater = st.Source >= 0 && st.Source < F.Count ? F[st.Source] : null;
            var mark = Log(BattleAction.Supernova, x, x, 0);
            if (mark != null) { mark.Status = StatusId.Heat; mark.Stacks = st.Stacks; }
            double dmg = st.Power * def.BurstAtkPct / 100.0 * (1 + (heater != null ? Math.Max(0, heater.S(StatId.SupernovaDamage)) : 0) / 100.0);
            var hit = new List<Fighter>();
            foreach (var o in F) if (o.Side == x.Side && o.Alive && o.Targetable) hit.Add(o);
            foreach (var o in hit)
            {
                if (Over) return;
                double d = dmg * CombatMath.DamageReductionFactor(o.S(StatId.DamageReduction), T) * TakenMultiplier(o);
                DealDamage(heater, o, d, false, BattleAction.Supernova, false, false, StatusId.Heat, null);
            }
            if (Over) return;
            var side = heater != null ? heater.Side : (x.Side == BattleSide.Player ? BattleSide.Enemy : BattleSide.Player);
            var worm = FirstWorm(side);
            if (worm != null && worm.Alive && worm.Side != x.Side)
            {
                double guard = CombatMath.Clamp(worm.S(StatId.SupernovaGuard), 0, 100) / 100.0;
                double recoil = worm.MaxHp * def.BurstRecoilPct / 100.0 * (1 - guard);
                if (recoil > 0) DealDamage(x, worm, recoil, false, BattleAction.Supernova, false, false, StatusId.Heat, null);
            }
            if (!Over && heater != null && heater.Alive) Fire(heater, TriggerKind.OnSupernova, x);
        }

        private void ApplyStatus(Fighter src, Fighter tg, StatusId id, int stacks, int duration, string skillId)
        {
            if (!tg.Alive || id == StatusId.None) return;
            if (!T.Statuses.TryGetValue(id, out var def)) return;
            if (tg.StatusCd[(int)id] > Round) return;
            double resist = CombatMath.Clamp(CombatMath.SoftCap(tg.S(StatId.StatusResist), T.StatusResistKnee), 0, 90) / 100.0;
            if (resist > 0 && R.Chance(resist))
            {
                var r = Log(BattleAction.StatusResisted, src, tg, 0);
                if (r != null) { r.Status = id; r.SkillId = skillId; }
                return;
            }
            int dur = duration > 0 ? duration : def.DefaultDuration;
            double power = 0;
            if (AtkBased(def))
            {
                double elem = def.HasElement ? src.S(def.ElementStat) : 0;
                power = src.S(StatId.Atk) * (1 + elem / 100.0);
            }
            // element statuses without a DoT (Shock, Freeze): the applier's element damage amplifies their damage-taken bonus
            // (Lightning / Ice damage did nothing for a fighter that only shocks or freezes)
            else if (def.HasElement) power = 1 + src.S(def.ElementStat) / 100.0;
            var st = tg.Status(id);
            if (st == null)
            {
                st = new StatusInst { Id = id, Stacks = Math.Min(stacks, def.MaxStacks), Turns = dur, Power = power, Source = src.Index };
                tg.Statuses.Add(st);
            }
            else
            {
                // Adds stacks up to the cap; any reapplication (including at max stacks) refreshes the duration.
                st.Stacks = Math.Min(def.MaxStacks, st.Stacks + stacks);
                st.Turns = Math.Max(st.Turns, dur);
                if (power >= st.Power) { st.Power = power; st.Source = src.Index; }
            }
            if (def.ApplyCooldownRounds > 0) tg.StatusCd[(int)id] = Round + def.ApplyCooldownRounds;
            var e = Log(BattleAction.StatusApplied, src, tg, st.Stacks);
            if (e != null) { e.Status = id; e.Stacks = st.Stacks; e.SkillId = skillId; }
            if (def.Cools != StatusId.None) RemoveStacks(src, tg, def.Cools, stacks, skillId); // Chill cools Heat
            if (def.BurstAtStacks > 0 && st.Stacks >= def.BurstAtStacks && tg.Statuses.Contains(st)) Supernova(tg, st, def);
            Fire(src, TriggerKind.OnStatusApplied, tg, id);
        }

        /// <summary>
        /// Enemy summons only (bosses calling their guard): Param = enemy def id, built on the owner's threat scale. Player-side
        /// drones were removed (dead-effects pass); content validation keeps every Summon on an enemy skill with an enemy id.
        /// </summary>
        private void Summon(Fighter owner, SkillDef skill, SkillTrigger t)
        {
            var sk = C.Sidekick(t.Param);
            if (owner.Side == BattleSide.Player && sk != null) { SummonSidekicks(owner, skill, t, sk); return; }
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
        /// Player-side summon (sidekick pass): Stacks battle-scoped sidekicks of a SidekickDef (drones), each with ability power =
        /// Value % of the summoner's ATK × (1 + the side's alien power %), untargetable, charging by the def's Speed.
        /// </summary>
        private void SummonSidekicks(Fighter owner, SkillDef skill, SkillTrigger t, SidekickDef sk)
        {
            for (int n = 0; n < Math.Max(1, t.Stacks); n++)
            {
                int count = 0;
                foreach (var f in F) if (f.Side == owner.Side && f.Alive) count++;
                if (count >= T.MaxFightersPerSide) return;
                var spec = new CombatantSpec
                {
                    Id = sk.Id, Name = sk.Name, Role = CombatRole.Summon, Targetable = false, Style = AttackStyle.Blaster,
                };
                spec.Stats.Add(new StatMod(StatId.Atk, owner.S(StatId.Atk) * t.Value / 100.0)).Add(new StatMod(StatId.Speed, sk.Speed));
                if (!string.IsNullOrEmpty(sk.AbilitySkillId)) spec.SkillIds.Add(sk.AbilitySkillId);
                var nf = MakeFighter(spec, owner.Side);
                nf.SummonedBy = owner.Index;
                InitSidekick(nf, AlienPowerOf(owner.Side));
                InitHp(nf);
                var e = Log(BattleAction.Summon, owner, nf, 0);
                if (e != null) e.SkillId = skill.Id;
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
