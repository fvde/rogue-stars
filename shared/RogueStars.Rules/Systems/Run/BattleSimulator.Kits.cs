using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    // ------------------------------------------------------------------------------------------
    // Hero & mount kits (docs/design/heroes-mounts.md, engine requirements X1–X18): hero Ability timers and counters, the mount
    // as a vehicle on a reload (no stats, % of the worm's ATK, rider link incl. crits), signature carry, extra hits, rage bank,
    // status loss / status damage events, mark and contagion, payback coil, siphon and ice-wall shields, shared caps.
    // ------------------------------------------------------------------------------------------

    internal sealed partial class StatusRules
    {
        /// <summary>Deep Wells: the stack cap × this (Bleed, Chill, Shock); conditions and "at max" skills keep reading the base cap.</summary>
        public double CapMult;
    }

    internal sealed partial class RuleSet
    {
        // hero (on the worm)
        public int AbilityEvery, AbilityTurns, AbilityShieldHits, AbilityStillness, RageBank;
        public string AbilitySkill;
        public double HealsCountPct, ExtraCritPct, PileOnPct, PileOnMax, ElementPoolPct;
        public bool StatusCrit, DoubleTap;
        // mount (on the mount fighter)
        public int Reload;
        public bool NeedsAttack, Slipstream, CoilWindow;
        public double SpotlightPct, ContagionPct, SlipstreamPct, CoilPct, CoilCapPct, CoilRecoilX, CoilCounterPct, SiphonCapPct;
        public string MountSkill, PassiveSkill;

        /// <summary>Kit rules (Passive Rule triggers on hero kit skills and mount kit skills).</summary>
        public void AddKitRule(string name, double arg, double value, string skillId)
        {
            switch (name)
            {
                case "ability_every": AbilityEvery = (int)arg; AbilitySkill = skillId; break;
                case "ability_turns": AbilityTurns = (int)arg; AbilitySkill = skillId; break;
                case "ability_shield_hits": AbilityShieldHits = (int)arg; AbilitySkill = skillId; break;
                case "ability_stillness": AbilityStillness = (int)arg; AbilitySkill = skillId; break;
                case "rage_bank": RageBank = (int)(arg > 0 ? arg : 100); AbilitySkill = AbilitySkill ?? skillId; break;
                case "heals_count": HealsCountPct = arg > 0 ? arg : value; break;
                case "status_crit": StatusCrit = true; break;
                case "double_tap": DoubleTap = true; break;
                case "extra_crit": ExtraCritPct += value; break;
                case "pile_on": PileOnPct = value; PileOnMax = arg; break;
                case "element_pool": ElementPoolPct = value; break;
                case "reload": Reload = (int)arg; MountSkill = skillId; break;
                case "needs_attack": NeedsAttack = true; break;
                case "spotlight": SpotlightPct = value; PassiveSkill = skillId; break;
                case "contagion": ContagionPct = value; PassiveSkill = skillId; break;
                case "slipstream": Slipstream = true; SlipstreamPct = value; break;
                case "coil": CoilPct = value; PassiveSkill = skillId; break;
                case "coil_cap": CoilCapPct = value; break;
                case "coil_recoil": CoilRecoilX = arg; break;
                case "coil_counter": CoilCounterPct = value; break;
                case "coil_window": CoilWindow = true; break;
                case "siphon_cap": SiphonCapPct = value; break;
                default: AddPartRule(name, arg, value, skillId); break;
            }
        }
    }

    internal sealed partial class Fighter
    {
        /// <summary>The side's mount: a vehicle without stats (ATK = its worm's × (1 + mount damage %)), its crits are the worm's.</summary>
        public bool Rider;
        public double MountPct;
        /// <summary>Rounds until the mount's next shot (fires at 1 → 0), and the round its timer was last advanced.</summary>
        public int MountLeft = 1, MountAdvRound = -1;
        /// <summary>Hero Ability timer (rounds or turns left) / counter, the round the timer was last advanced.</summary>
        public int AbilityLeft, AbilityCount, AbilityAdvRound = -1;
        public double HealAcc;
        /// <summary>The rage bank above 100 (Sparkplug's Overcharge) and the round it last doubled a rage attack.</summary>
        public double Bank;
        public int BankRound = -1;
        /// <summary>Signature carries armed for the next hit (Seasoning ×3, Turning Sand ×2).</summary>
        public int PendingCarry;
        /// <summary>An attack made outside the normal turn flow (Echo, Rift Dash, Echo Shot, Alpha Howl) is an extra hit.</summary>
        public int ExtraDepth;
        public int ExtraHitsTurn, PileTarget = -1, PileCount;
        public bool SkipNextTurn, FatalSaved, LastHitCrit;
        /// <summary>Mount charges: Star Lancer's lance (+% on the next shot), Aegis Walker's coil (stored damage).</summary>
        public double ShotCharge, Coil, ShotBasePct;
        public bool CoilUsed;
        /// <summary>Who hit this worm: the last attacker, and every attacker since the mount's last shot.</summary>
        public int LastAttacker = -1;
        public readonly List<int> HitBy = new List<int>();
        /// <summary>Attackers of the last few hits taken (parallel to HitHistory; Rewind's strike-back).</summary>
        public readonly List<int> HitFrom = new List<int>();
        /// <summary>Siphon Hauler's overheal shield and Frost Frigate's ice wall (parts of Shield).</summary>
        public double SiphonShield, WallShield, WallPct;
        public bool WallAll;
        public int WallOwner = -1;
        public string WallSkill;
        /// <summary>Drones of the Hive Carrier's Launch Bay: guard one hit (−GuardDronePct %), trigger no / every signature.</summary>
        public double DroneGuardPct;
        public bool NoSig, CarryOnAct;
        /// <summary>Statuses the side's run applies (random owned status: Flask, Scan Pulse; never crowd control).</summary>
        public List<StatusId> Owned;
        /// <summary>Per-round caps (Ignition, Pressure Valve, Taste Test) and per-turn caps (Wingman), keyed by skill / trigger.</summary>
        public readonly Dictionary<string, double> CapUsed = new Dictionary<string, double>();
        public readonly Dictionary<string, int> CapRound = new Dictionary<string, int>();
        public bool SidekickHitThisAct;
        public int SidekickHitTarget = -1;
    }

    internal sealed partial class BattleSim
    {
        private readonly int[] _mark = { -1, -1 };
        private readonly int[] _bonusVolleyRound = { -1, -1 };
        private int _extraHitDepth, _carryDepth, _firedCount;
        private double _abilityMult = 1, _fatalHpBefore;
        private bool _noRider;

        private Fighter MountOf(BattleSide side)
        {
            foreach (var f in F) if (f.Side == side && f.Role == CombatRole.Mount) return f;
            return null;
        }

        /// <summary>Crit chance / damage of a fighter's hits: the mount rides on its worm's.</summary>
        private Fighter CritSource(Fighter f) => f != null && f.Rider ? FirstWorm(f.Side) ?? f : f;

        // ---- setup ----------------------------------------------------------------------------

        /// <summary>The mount has no stats: its ATK is its worm's × (1 + the worm's mount damage % + its own), never attacked.</summary>
        private void InitMount(Fighter f, Fighter worm)
        {
            f.Rider = true;
            f.MountPct = (worm != null ? worm.S(StatId.MountPower) : 0) + f.S(StatId.MountPower);
            foreach (var k in new[] { (int)StatId.Atk, (int)StatId.Hp, (int)StatId.Def }) { f.Flat[k] = 0; f.Pct[k] = 0; }
            f.MountLeft = 1;
        }

        private void SetupKits()
        {
            for (int side = 0; side < 2; side++)
            {
                var worm = FirstWorm((BattleSide)side);
                if (worm == null) continue;
                var r = worm.Rules;
                worm.AbilityLeft = r.AbilityEvery > 0 ? r.AbilityEvery : r.AbilityTurns;
                // the Ability's skill for the log: the one with the OnAbility effects (Mirror Palm), else the rule's (Overcharge)
                foreach (var sk in worm.Skills)
                {
                    bool ab = false;
                    foreach (var t in sk.Triggers) if (t.When == TriggerKind.OnAbility) { ab = true; break; }
                    if (ab) { r.AbilitySkill = sk.Id; break; }
                }
                // the HUD's starting point: a timer's rounds / turns left, a counter's 0 / max, the empty bank
                if (r.AbilityEvery > 0 || r.AbilityTurns > 0) CounterLog(worm, worm.AbilityLeft, r.AbilityEvery > 0 ? r.AbilityEvery : r.AbilityTurns, r.AbilitySkill);
                else if (r.AbilityShieldHits > 0 || r.AbilityStillness > 0) CounterLog(worm, 0, r.AbilityShieldHits > 0 ? r.AbilityShieldHits : r.AbilityStillness, r.AbilitySkill);
                else if (r.RageBank > 0) CounterLog(worm, 0, r.RageBank, r.AbilitySkill);
                if (r.ElementPoolPct > 0) PoolElements(worm, r.ElementPoolPct);
                var mount = MountOf((BattleSide)side);
                if (mount != null)
                {
                    foreach (var sk in mount.Skills)
                        foreach (var t in sk.Triggers)
                            if (t.Rider && t.When == TriggerKind.OnFatal) worm.HasFatal = true;
                    if (mount.Rules.SpotlightPct > 0 || mount.Rules.ContagionPct > 0) MarkHealthiest(mount);
                }
                // statuses the side's run applies (Flask, Scan Pulse pick one at random; never crowd control or Stagger)
                var owned = new SortedSet<StatusId>();
                foreach (var f in F)
                {
                    if (f.Side != (BattleSide)side) continue;
                    foreach (var sk in f.Skills)
                        foreach (var t in sk.Triggers)
                            if (t.Effect == EffectKind.ApplyStatus && t.Status != StatusId.None && OwnableStatus(t.Status)) owned.Add(t.Status);
                }
                worm.Owned = new List<StatusId>(owned);
            }
        }

        private bool OwnableStatus(StatusId id) =>
            id != StatusId.Stagger && id != StatusId.Burn && T.Statuses.TryGetValue(id, out var d) && !d.SkipsTurn && !d.Charms;

        /// <summary>Conduit: the highest element damage bonus also counts for the other elements at Value % (shown as a derived bonus).</summary>
        private static void PoolElements(Fighter f, double pct)
        {
            var els = new[] { StatId.FireDamage, StatId.IceDamage, StatId.LightningDamage, StatId.PoisonDamage, StatId.BleedDamage };
            double best = 0;
            foreach (var e in els) best = Math.Max(best, f.S(e));
            double floor = best * pct / 100.0;
            foreach (var e in els)
            {
                double have = f.S(e);
                if (have >= floor) continue;
                int i = (int)e;
                double mult = 1 + (f.Pct[i] + f.BPct[i]) / 100.0;
                if (mult > 0) f.Flat[i] += (floor - have) / mult;
            }
        }

        // ---- mount: a vehicle on a reload ----------------------------------------------------------

        private void MountTurn(Fighter m)
        {
            if (m.Rules.Reload <= 0) return;
            m.MountLeft--;
            if (m.MountLeft > 0) return;
            // reload first, so a kill during the shot can bring the next one sooner (Lance Sight A5, Slipstream, Patience)
            m.MountLeft = m.Rules.Reload;
            if (!Volley(m, false)) m.MountLeft = 1; // a held shot (Echo Shot before the worm attacked) stays ready
        }

        /// <summary>The mount fires its active (OnVolley); false when it holds the shot (nothing to repeat yet).</summary>
        private bool Volley(Fighter m, bool extra)
        {
            if (Over || m == null || !m.Alive) return false;
            var worm = FirstWorm(m.Side);
            if (m.Rules.NeedsAttack && (worm == null || !worm.Attacked)) return false;
            var front = DefaultTarget(m);
            if (front == null) return false;
            var ctx = _mark[(int)m.Side] >= 0 && F[_mark[(int)m.Side]].Alive ? F[_mark[(int)m.Side]] : front;
            var e = Log(BattleAction.Volley, m, ctx, extra ? 1 : 0);
            if (e != null) { e.SkillId = m.Rules.MountSkill; e.Stacks = m.Rules.Reload; }
            m.ShotBasePct = 0; // the main hit's % ATK (Lance Sight's charge is added to it)
            foreach (var sk in m.Skills)
                foreach (var t in sk.Triggers)
                    if (m.ShotBasePct <= 0 && t.When == TriggerKind.OnVolley && t.Effect == EffectKind.Damage) m.ShotBasePct = t.Value;
            Fire(m, TriggerKind.OnVolley, ctx);
            m.ShotCharge = 0;
            if (m.CoilUsed) { m.CoilUsed = false; m.Coil = 0; CoilLog(m); }
            if (worm != null) worm.HitBy.Clear();
            if (!Over && worm != null && worm.Alive) Fire(worm, TriggerKind.OnMountFire, front);
            return true;
        }

        /// <summary>Short Circuit, Whistle: the side's mount fires now (an extra shot; at most one per side per round).</summary>
        private bool BonusVolley(BattleSide side)
        {
            var m = MountOf(side);
            if (m == null || m.Rules.Reload <= 0 || _bonusVolleyRound[(int)side] == Round) return false;
            _bonusVolleyRound[(int)side] = Round;
            return Volley(m, true);
        }

        // ---- hero ability: timers and counters -------------------------------------------------

        /// <summary>A round timer ticks at the worm's turn (before a skipped turn is lost); a turn timer at its acting turn.</summary>
        private void AbilityTick(Fighter w, bool turnTimer)
        {
            var r = w.Rules;
            int every = turnTimer ? r.AbilityTurns : r.AbilityEvery;
            if (every <= 0 || (turnTimer ? r.AbilityEvery > 0 : false)) return;
            w.AbilityLeft--;
            if (w.AbilityLeft > 0) { CounterLog(w, w.AbilityLeft, every, r.AbilitySkill); return; } // "Feast in 2"
            w.AbilityLeft = every;
            FireAbility(w, 1);
            CounterLog(w, w.AbilityLeft, every, r.AbilitySkill);
        }

        private void FireAbility(Fighter w, double mult)
        {
            if (Over || !w.Alive) return;
            var e = Log(BattleAction.Ability, w, w, 0);
            if (e != null) e.SkillId = w.Rules.AbilitySkill;
            double prev = _abilityMult;
            _abilityMult = mult;
            try { Fire(w, TriggerKind.OnAbility, DefaultTarget(w)); }
            finally { _abilityMult = prev; }
        }

        /// <summary>A counter ability (Starfall: hits absorbed by shields; Stillness): +n, fires and resets at its max.</summary>
        private void AbilityCount(Fighter w, int n, int max)
        {
            if (max <= 0 || n <= 0 || !w.Alive) return;
            w.AbilityCount += n;
            bool fire = w.AbilityCount >= max;
            if (fire) w.AbilityCount -= max;
            CounterLog(w, fire ? max : w.AbilityCount, max, w.Rules.AbilitySkill);
            if (fire) { FireAbility(w, 1); CounterLog(w, w.AbilityCount, max, w.Rules.AbilitySkill); }
        }

        private void CounterLog(Fighter f, double value, int max, string skillId)
        {
            var e = Log(BattleAction.KitCounter, f, f, value);
            if (e != null) { e.Stacks = max; e.SkillId = skillId; }
        }

        private void CoilLog(Fighter m)
        {
            var e = Log(BattleAction.KitCounter, m, m, m.Coil);
            if (e != null) { e.Stacks = (int)Math.Round(CoilCap(m)); e.SkillId = m.Rules.PassiveSkill; }
        }

        /// <summary>Stillness: every enemy attack on the worm adds one, dodged or blocked too; at the max Mirror Palm fires and it resets.</summary>
        private void StillnessHit(Fighter def)
        {
            if (def.Role == CombatRole.Worm && def.Rules.AbilityStillness > 0 && !Over && def.Alive) AbilityCount(def, 1, def.Rules.AbilityStillness);
        }

        /// <summary>Patience, Slipstream, Lance Sight: a timer comes 1 round sooner (each timer at most once per round).</summary>
        private void AdvanceTimers(Fighter owner, string which)
        {
            var worm = FirstWorm(owner.Side);
            var mount = MountOf(owner.Side);
            bool all = HasToken(which, "all");
            if ((all || HasToken(which, "ability")) && worm != null && worm.AbilityAdvRound != Round
                && (worm.Rules.AbilityEvery > 0 || worm.Rules.AbilityTurns > 0) && worm.AbilityLeft > 1)
            {
                worm.AbilityAdvRound = Round;
                worm.AbilityLeft--;
                CounterLog(worm, worm.AbilityLeft, worm.Rules.AbilityEvery > 0 ? worm.Rules.AbilityEvery : worm.Rules.AbilityTurns, worm.Rules.AbilitySkill);
            }
            if ((all || HasToken(which, "mount")) && mount != null && mount.MountAdvRound != Round && mount.MountLeft > 1)
            {
                mount.MountAdvRound = Round;
                mount.MountLeft--;
            }
            if (HasToken(which, "reload") && mount != null && mount.MountAdvRound != Round)
            {
                mount.MountAdvRound = Round;
                mount.MountLeft = 1;
            }
        }

        // ---- rage: bank above 100 ----------------------------------------------------------------

        /// <summary>Adds rage (may be negative: Time Debt). With a rage bank (Overcharge) rage past 100 goes to the bank (max its size).</summary>
        private void AddRage(Fighter f, double amount, bool debt = false)
        {
            if (f.Sidekick) return;
            double next = f.Rage + amount;
            int bank = f.Rules.RageBank;
            if (bank > 0 && next > T.RageMax)
            {
                double before = f.Bank;
                f.Bank = Math.Min(bank, f.Bank + next - T.RageMax);
                next = T.RageMax;
                if (Math.Round(f.Bank) != Math.Round(before)) CounterLog(f, f.Bank, bank, f.Rules.AbilitySkill);
            }
            f.Rage = Math.Max(debt ? -T.RageMax : Math.Min(0, f.Rage), Math.Min(T.RageOverfillMax, next));
        }

        /// <summary>A full bank makes this rage attack fire twice (once per round); the bank empties.</summary>
        private bool TakeBank(Fighter f)
        {
            int bank = f.Rules.RageBank;
            if (bank <= 0 || f.Bank < bank - 1e-6 || f.BankRound == Round) return false;
            f.BankRound = Round;
            f.Bank = 0;
            CounterLog(f, 0, bank, f.Rules.AbilitySkill);
            return true;
        }

        // ---- per-round / per-turn caps ----------------------------------------------------------------

        /// <summary>How much of <paramref name="want"/> fits under a per-round cap (key = skill id); records the use.</summary>
        private double RoundCapped(Fighter f, string key, double cap, double want)
        {
            if (cap <= 0) return want;
            if (!f.CapRound.TryGetValue(key, out int r) || r != Round) { f.CapRound[key] = Round; f.CapUsed[key] = 0; }
            double used = f.CapUsed[key];
            double give = Math.Max(0, Math.Min(want, cap - used));
            f.CapUsed[key] = used + give;
            return give;
        }

        /// <summary>"turn_cap:N" (Wingman): a trigger fires at most N times per fighter turn.</summary>
        private bool TurnCapOk(Fighter owner, SkillDef skill, int ti, SkillTrigger t)
        {
            int cap = (int)ParamNumber(t.Param, "turn_cap");
            if (cap <= 0) return true;
            string key = "turn:" + skill.Id + ":" + ti;
            int turnKey = Round * 1000 + (_acting != null ? _acting.Index : 999);
            if (!owner.CapRound.TryGetValue(key, out int r) || r != turnKey) { owner.CapRound[key] = turnKey; owner.CapUsed[key] = 0; }
            if (owner.CapUsed[key] >= cap) return false;
            owner.CapUsed[key] += 1;
            return true;
        }

        // ---- signature carry (X5) ------------------------------------------------------------------

        /// <summary>
        /// Every owned signature's on-hit runs once on the target, <paramref name="times"/> times (the worm's statuses and power):
        /// Ignite / Irradiate / Lacerate / Chill apply their (awakened) stacks, Arc jumps; Flurry, Riposte and Echo strike for
        /// HiveStrikePct % ATK, Command gives one sidekick an extra action (Hivemind's tuning, once per round).
        /// </summary>
        private void Carry(Fighter carrier, Fighter tg, int times)
        {
            var worm = carrier.Role == CombatRole.Worm ? carrier : FirstWorm(carrier.Side);
            if (worm == null || !worm.Alive || tg == null || !tg.Alive || tg.Side == worm.Side || worm.Signatures.Count == 0 || _carryDepth >= 2) return;
            _carryDepth++;
            try
            {
                for (int n = 0; n < Math.Max(1, times); n++)
                    foreach (var sig in worm.Signatures)
                    {
                        if (Over || !worm.Alive || !tg.Alive) return;
                        bool any = false;
                        foreach (var t in sig.Triggers)
                        {
                            if (t.When != TriggerKind.OnHit) continue;
                            any = true;
                            double chance = t.Chance * (t.Effect == EffectKind.ApplyStatus ? 1 + worm.S(StatId.StatusChance) / 100.0 : 1);
                            if (chance < 100 && !R.Chance(chance / 100.0)) continue;
                            if (t.Effect == EffectKind.Strike) Execute(worm, sig, HiveStrike, tg, 0);
                            else Execute(worm, sig, t, tg, 0);
                        }
                        if (any) continue;
                        if (sig.Family == SkillFamily.AlienSynergy)
                        {
                            Fighter kick = null;
                            foreach (var o in F) if (o.Side == worm.Side && o.Alive && o.Sidekick && o.HiveRound != Round) { kick = o; break; }
                            if (kick != null && ReserveExtra(worm.Side)) { kick.HiveRound = Round; SidekickAct(kick, true); }
                        }
                        else Execute(worm, sig, HiveStrike, tg, 0);
                    }
            }
            finally { _carryDepth--; }
        }

        // ---- marks and contagion (X15) -----------------------------------------------------------

        private void MarkHealthiest(Fighter mount)
        {
            Fighter best = null;
            foreach (var o in F)
                if (o.Side != mount.Side && o.Alive && o.Targetable && (best == null || o.Hp > best.Hp)) best = o;
            _mark[(int)mount.Side] = best != null ? best.Index : -1;
            if (best != null)
            {
                var e = Log(BattleAction.Mark, mount, best, 0);
                if (e != null) e.SkillId = mount.Rules.PassiveSkill;
            }
        }

        /// <summary>The marked enemy died: a new mark (the healthiest), and Contagion moves a share of its statuses there.</summary>
        private void MarkDied(Fighter dead)
        {
            for (int side = 0; side < 2; side++)
            {
                if (_mark[side] != dead.Index) continue;
                var mount = MountOf((BattleSide)side);
                if (mount == null) { _mark[side] = -1; continue; }
                MarkHealthiest(mount);
                double pct = mount.Rules.ContagionPct;
                var worm = FirstWorm((BattleSide)side);
                if (pct <= 0 || _mark[side] < 0 || worm == null || dead.DeathStatuses == null || Over) continue;
                var next = F[_mark[side]];
                foreach (var st in new List<StatusInst>(dead.DeathStatuses))
                {
                    if (Over || !next.Alive) break;
                    if (!OwnableStatus(st.Id)) continue; // never Freeze, Stun, Delay or Charm
                    int n = (int)Math.Floor(st.Stacks * pct / 100.0);
                    if (n > 0) ApplyStatus(worm, next, st.Id, n, 0, mount.Rules.PassiveSkill);
                }
            }
        }

        // ---- the largest status (Feast, Mirror Palm, Coolant Loop) -------------------------------

        private StatusId Largest(Fighter f)
        {
            StatusInst best = null;
            foreach (var st in f.Statuses)
                if (st.Stacks > 0 && OwnableStatus(st.Id) && (best == null || st.Stacks > best.Stacks || (st.Stacks == best.Stacks && st.Id < best.Id))) best = st;
            return best != null ? best.Id : StatusId.None;
        }

        // ---- status loss and status damage (X1, Siphon) ------------------------------------------------

        /// <summary>A fighter lost status stacks (any cause): OnStatusLost on the other side's worm (and its mount's rider triggers).</summary>
        private void StatusLost(Fighter f, StatusId id, int n)
        {
            if (n <= 0 || Over) return;
            var other = FirstWorm(f.Side == BattleSide.Player ? BattleSide.Enemy : BattleSide.Player);
            if (other != null && other.Alive) Fire(other, TriggerKind.OnStatusLost, f, id, n);
        }

        private void StatusDamage(Fighter src, double dealt)
        {
            if (src == null || dealt <= 0 || Over) return;
            var owner = src.Sidekick || src.Rider ? FirstWorm(src.Side) ?? src : src;
            if (owner.Alive) Fire(owner, TriggerKind.OnStatusDamage, null, StatusId.None, dealt);
        }

        // ---- payback coil (X16), siphon and ice-wall shields -------------------------------------------

        private double CoilCap(Fighter m) => m.Rules.CoilCapPct / 100.0 * AtkOf(m);

        private void StoreCoil(Fighter worm, double amount)
        {
            var m = MountOf(worm.Side);
            if (m == null || m.Rules.CoilPct <= 0 || amount <= 0) return;
            double cap = CoilCap(m);
            double before = m.Coil;
            m.Coil = Math.Min(cap, m.Coil + amount);
            if (Math.Round(m.Coil) != Math.Round(before)) CoilLog(m);
            if (m.Rules.CoilWindow && before < cap - 0.5 && m.Coil >= cap - 0.5 && worm.Alive) worm.CounterWindow = true; // A5: counter every hit
        }

        /// <summary>After a shield absorbed: the siphon / wall parts can't exceed what is left; a broken ice wall bursts.</summary>
        private void ShieldParts(Fighter tg, Fighter src)
        {
            if (tg.SiphonShield > tg.Shield) tg.SiphonShield = tg.Shield;
            if (tg.WallShield <= 0) return;
            if (tg.Shield < tg.WallShield) tg.WallShield = tg.Shield;
            if (tg.WallShield >= 0.5) return;
            tg.WallShield = 0;
            var m = tg.WallOwner >= 0 && tg.WallOwner < F.Count ? F[tg.WallOwner] : null;
            if (m == null || Over || tg.WallPct <= 0) return;
            var skill = C.Skill(tg.WallSkill) ?? m.Skills[0];
            var hit = new List<Fighter>();
            if (tg.WallAll) { foreach (var o in F) if (o.Side != tg.Side && o.Alive && o.Targetable) hit.Add(o); }
            else
            {
                var at = src != null && src.Alive && src.Side != tg.Side && src.Targetable ? src : DefaultTarget(tg);
                if (at != null) hit.Add(at);
            }
            foreach (var o in hit)
            {
                if (Over || !o.Alive) continue;
                SkillHit(m, skill, o, tg.WallPct, 0, false, false, 1, RulesOwner(m));
                if (tg.WallAll && !Over && o.Alive) Carry(m, o, 1);
            }
        }

        // ---- drones (Launch Bay) --------------------------------------------------------------------

        /// <summary>A guarding drone softens a hit on the worm by its % and breaks (Hive Carrier 5★+).</summary>
        private double DroneGuard(Fighter def, double dmg)
        {
            if (def.Role != CombatRole.Worm) return dmg;
            foreach (var o in F)
            {
                if (o.Side != def.Side || !o.Alive || o.DroneGuardPct <= 0) continue;
                double prevented = dmg * Math.Min(100, o.DroneGuardPct) / 100.0;
                // parts v2: Swarmqueen Tail's Drones guard 2 hits before they break
                if (o.DroneGuardHits > 1) { o.DroneGuardHits--; var g2 = Log(BattleAction.Guarded, o, def, prevented); if (g2 != null) g2.Stacks = o.DroneGuardHits; return dmg - prevented; }
                o.Alive = false;
                var g = Log(BattleAction.Guarded, o, def, prevented);
                if (g != null) g.Stacks = 0;
                Log(BattleAction.Death, o, o, 0);
                return dmg - prevented;
            }
            return dmg;
        }

        private bool SiphonFull(Fighter w)
        {
            var m = MountOf(w.Side);
            double cap = m != null ? m.Rules.SiphonCapPct / 100.0 * w.MaxHp : 0;
            return cap > 0 && w.SiphonShield >= cap - 0.5;
        }

        // ---- kit effects ----------------------------------------------------------------------------

        private void ExecuteKit(Fighter owner, SkillDef skill, SkillTrigger t, Fighter ctx, double amount, List<Fighter> targets)
        {
            switch (t.Effect)
            {
                case EffectKind.CarrySignatures:
                {
                    var worm = owner.Role == CombatRole.Worm ? owner : FirstWorm(owner.Side);
                    if (worm == null || !worm.Alive) break;
                    if (HasToken(t.Param, "next_hit")) { worm.PendingCarry = Math.Max(worm.PendingCarry, Math.Max(1, t.Stacks)); break; }
                    foreach (var tg in targets) { if (Over) break; Carry(owner, tg, Math.Max(1, t.Stacks)); }
                    break;
                }
                case EffectKind.AdvanceTimer:
                    AdvanceTimers(owner, string.IsNullOrEmpty(t.Param) ? "all" : t.Param);
                    break;
                case EffectKind.MountFire:
                    BonusVolley(owner.Side);
                    break;
                case EffectKind.FireAbility:
                {
                    var worm = owner.Role == CombatRole.Worm ? owner : FirstWorm(owner.Side);
                    if (worm != null) FireAbility(worm, t.Value > 0 ? t.Value / 100.0 : 1);
                    break;
                }
                case EffectKind.RiderAttack:
                    RiderAttack(owner, skill, t);
                    break;
                case EffectKind.Whistle:
                    Whistle(owner, skill, t);
                    break;
                case EffectKind.SpreadStatus:
                    Spread(owner, skill, t, ctx);
                    break;
                case EffectKind.ChargeAllies:
                    foreach (var o in F)
                        if (o.Side == owner.Side && o.Alive && o.Sidekick) o.Charge = Math.Min(ActAt(o), o.Charge + t.Value);
                    break;
                case EffectKind.GainRunCoins:
                    // in-battle coins (Plunder); on OnBattleWon they are the board's (RunSystem pays Prospector after the battle)
                    if (owner.Side == BattleSide.Player && t.Value > 0 && t.When != TriggerKind.OnBattleWon)
                    {
                        Res.RunCoins += (long)Math.Round(t.Value);
                        var e = Log(BattleAction.Buff, owner, owner, t.Value);
                        if (e != null) e.SkillId = skill.Id;
                    }
                    break;
            }
        }

        /// <summary>
        /// Rift Dash / Echo Shot: the worm makes Stacks extra attacks at Value % ("echo": it repeats its last attack, Paradox: its
        /// strongest; "as_echo": counts as an Echo with Echo damage; "combo": it can combo). The damage is credited to the mount.
        /// </summary>
        private void RiderAttack(Fighter owner, SkillDef skill, SkillTrigger t)
        {
            var worm = owner.Role == CombatRole.Worm ? owner : FirstWorm(owner.Side);
            if (worm == null || !worm.Alive) return;
            bool echo = HasToken(t.Param, "echo"), asEcho = HasToken(t.Param, "as_echo"), combo = HasToken(t.Param, "combo");
            double before = worm.DamageDealt;
            worm.ExtraDepth++;
            try
            {
                for (int n = 0; n < Math.Max(1, t.Stacks); n++)
                {
                    if (Over || !worm.Alive) break;
                    var kind = AttackKind.Basic;
                    Fighter tg = null;
                    if (echo && worm.Attacked)
                    {
                        bool strongest = asEcho && worm.Rules.EchoStrongest;
                        kind = strongest ? worm.BestKind : worm.LastKind;
                        int ti = strongest ? worm.BestTarget : worm.LastTarget;
                        tg = ti >= 0 && ti < F.Count && F[ti].Alive && F[ti].Side != worm.Side ? F[ti] : null;
                        if (kind == AttackKind.Skill || kind == AttackKind.Arc) kind = AttackKind.Basic;
                    }
                    if (tg == null) tg = DefaultTarget(worm);
                    if (tg == null) break;
                    double mult = t.Value / 100.0 * (asEcho ? Math.Max(0, 1 + worm.S(StatId.EchoDamage) / 100.0) : 1);
                    var e = Log(asEcho ? BattleAction.Echo : BattleAction.Buff, owner, tg, 0);
                    if (e != null) e.SkillId = skill.Id;
                    Attack(worm, tg, kind, 0, mult);
                    if (combo && !Over && worm.Alive && tg.Alive) ComboChain(worm, tg, false, 0);
                }
            }
            finally { worm.ExtraDepth--; }
            if (owner != worm)
            {
                double dealt = worm.DamageDealt - before;
                if (dealt > 0) { worm.DamageDealt -= dealt; owner.DamageDealt += dealt; }
            }
        }

        /// <summary>Whistle (Beast Tamer): every sidekick acts, fastest first, then the mount fires; Pack Guard shields, Alpha Howl follows.</summary>
        private void Whistle(Fighter owner, SkillDef skill, SkillTrigger t)
        {
            var worm = owner.Role == CombatRole.Worm ? owner : FirstWorm(owner.Side);
            if (worm == null || !worm.Alive) return;
            double follow = ParamNumber(t.Param, "follow");
            var kicks = new List<Fighter>();
            foreach (var o in F) if (o.Side == owner.Side && o.Alive && o.Sidekick) kicks.Add(o);
            kicks.Sort((a, b) => { int c = b.S(StatId.Speed).CompareTo(a.S(StatId.Speed)); return c != 0 ? c : a.Index.CompareTo(b.Index); });
            int acted = 0;
            void Follow()
            {
                if (follow <= 0 || Over || !worm.Alive) return;
                var tg = DefaultTarget(worm);
                if (tg == null) return;
                worm.ExtraDepth++;
                try { Attack(worm, tg, AttackKind.Basic, 0, follow / 100.0); }
                finally { worm.ExtraDepth--; }
            }
            foreach (var k in kicks)
            {
                if (Over || !worm.Alive) break;
                if (!k.Alive || !ReserveExtra(owner.Side)) continue;
                Mark(owner, k, skill);
                SidekickAct(k, true);
                acted++;
                Follow();
            }
            if (!Over && BonusVolley(owner.Side)) { acted++; Follow(); }
            if (t.Value > 0 && acted > 0 && !Over && worm.Alive)
            {
                double amt = worm.MaxHp * t.Value * acted / 100.0 * (1 + worm.S(StatId.ShieldPower) / 100.0);
                worm.Shield += amt;
                var e = Log(BattleAction.Shield, worm, worm, amt);
                if (e != null) e.SkillId = skill.Id;
            }
        }

        /// <summary>Mirror Palm: every other enemy gets Value % of the context enemy's largest status (capped); Nebula Mind hits per stack copied.</summary>
        private void Spread(Fighter owner, SkillDef skill, SkillTrigger t, Fighter ctx)
        {
            var worm = owner.Role == CombatRole.Worm ? owner : FirstWorm(owner.Side);
            var front = ctx != null && ctx.Alive && ctx.Side != owner.Side && ctx.Targetable ? ctx : DefaultTarget(owner);
            if (worm == null || front == null) return;
            // parts v2: a named status (Isotope Visor's Radiation, Firebrand Blaster's Heat) instead of the largest; "behind": only the
            // enemy behind the context one; the applier's spread rule multiplies the copy
            var id = t.Status != StatusId.None ? t.Status : Largest(front);
            var st = id != StatusId.None ? front.Status(id) : null;
            int copied = 0;
            List<Fighter> only = HasToken(t.Param, "behind") ? Targets(owner, TargetKind.SecondEnemy, front) : null;
            if (st != null)
            {
                int n = (int)Math.Floor(st.Stacks * t.Value / 100.0);
                double cap = ParamNumber(t.Param, "cap");
                if (cap > 0) n = Math.Min(n, (int)cap);
                if (t.Status != StatusId.None) n = SpreadStacks(worm, id, n);
                if (n > 0)
                    foreach (var o in new List<Fighter>(F))
                    {
                        if (Over) break;
                        if (o == front || o.Side == owner.Side || !o.Alive || !o.Targetable || (only != null && !only.Contains(o))) continue;
                        var had = o.Status(id);
                        int prev = had != null ? had.Stacks : 0;
                        ApplyStatus(worm, o, id, n, 0, skill.Id);
                        var now = o.Status(id);
                        copied += Math.Max(0, (now != null ? now.Stacks : 0) - prev);
                    }
            }
            double per = ParamNumber(t.Param, "dmg_per");
            if (per > 0 && copied > 0 && !Over)
            {
                double pct = per * copied;
                double dcap = ParamNumber(t.Param, "dmg_cap");
                if (dcap > 0) pct = Math.Min(pct, dcap);
                foreach (var o in new List<Fighter>(F))
                {
                    if (Over || !worm.Alive) break;
                    if (o.Side == owner.Side || !o.Alive || !o.Targetable) continue;
                    SkillHit(owner, skill, o, pct, 0, false, false, _abilityMult, worm);
                }
            }
        }
    }
}
