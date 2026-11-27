using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>
    /// Part skills (parts v2, docs/design/parts.md, docs/design/parts-v2/data.py): Epic parts have 1 skill, Legendary 2, every
    /// Mythic 5 (3 active at Mythic, the 4th at Mythic S, the 5th at Mythic SS) and an awakened version of each. Each skill is a
    /// SkillDef (Source = Part, not draftable) whose card text is the approved text (ContentDb.PartData.g.cs, generated from data.py);
    /// its numbers match the text (PartsV2SkillTests). Slot trigger languages: head = openers and crits, body = when hit / low HP,
    /// tail = rhythm, legs = dodges and speed, arm = attacks.
    /// </summary>
    public sealed partial class ContentDb
    {
        private const TriggerKind Passive = TriggerKind.Passive;

        // ---- trigger helpers ---------------------------------------------------------------------

        private static SkillTrigger PT(TriggerKind when, EffectKind eff, double value = 0, TargetKind tg = TargetKind.Self, StatusId st = StatusId.None,
            int stacks = 1, string param = null, string iff = null, int every = 0, int maxBattle = 0, int dur = 0, double chance = 100) =>
            new SkillTrigger
            {
                When = when, Effect = eff, Value = value, Target = tg, Status = st, Stacks = stacks, Param = param, If = iff, EveryN = every,
                MaxPerBattle = maxBattle, Duration = dur, Chance = chance,
            };

        /// <summary>A status on enemies (Target Enemy = the context enemy, else the front one).</summary>
        private static SkillTrigger PApply(TriggerKind when, StatusId st, int stacks, TargetKind tg = TargetKind.Enemy, string param = null, string iff = null,
            int every = 0, int dur = 0, double chance = 100, int maxBattle = 0) =>
            PT(when, EffectKind.ApplyStatus, 0, tg, st, stacks, param, iff, every, maxBattle, dur, chance);

        /// <summary>Crowd control once per enemy per battle; bosses lose 20 rage instead.</summary>
        private static SkillTrigger PCc(TriggerKind when, StatusId st, TargetKind tg = TargetKind.Enemy, string param = null, string iff = null, int every = 0, double chance = 100)
            => PApply(when, st, 1, tg, "per_target:1;boss_rage:20" + (param != null ? ";" + param : ""), iff, every, 1, chance);

        private static SkillTrigger PBuff(TriggerKind when, StatId stat, double v, string param = null, TargetKind tg = TargetKind.Self, string iff = null, int every = 0, int dur = 0)
            => new SkillTrigger { When = when, Effect = EffectKind.BuffStat, Stat = StatMod.Bonus(stat, v), Target = tg, Param = param, If = iff, EveryN = every, Duration = dur };

        private static SkillTrigger PRule(string param, double v = 0, StatusId st = StatusId.None, int dur = 0) =>
            new SkillTrigger { When = Passive, Effect = EffectKind.Rule, Target = TargetKind.Self, Param = param, Value = v, Status = st, Duration = dur };

        private static SkillTrigger PCond(string cond, double v, string iff = null) =>
            new SkillTrigger { When = Passive, Effect = EffectKind.Damage, Target = TargetKind.Self, Param = "cond:" + cond, Value = v, If = iff };

        private void PartSkill(string id, string name, SkillFamily family, Rarity rarity, string text, params SkillTrigger[] triggers)
        {
            var s = new SkillDef
            {
                Id = id, Name = name, Family = family, Rarity = rarity, Source = SkillSource.Part, Draftable = false,
                Description = text, Text = text,
            };
            s.Triggers.AddRange(triggers);
            AddSkill(s);
        }

        // ---- build ---------------------------------------------------------------------------------

        private void BuildPartSkills()
        {
            foreach (var d in PartsConfig.Defs.Values)
            {
                if (d.Rarity == PartRarity.Epic || d.Rarity == PartRarity.Legendary)
                    for (int i = 0; i < d.SkillIds.Count; i++)
                    {
                        string key = d.Rarity + "|" + d.Kind + "|" + d.Role + "|" + i;
                        PartSkill(d.SkillIds[i], d.Name, d.Family, d.Rarity == PartRarity.Epic ? Rarity.Epic : Rarity.Legendary, PartStdTexts[key], StdSkill(d.Rarity, d.Kind, d.Role, i));
                    }
            }
            foreach (var m in PartMythicData)
            {
                var d = PartsConfig.Defs[m.Id];
                for (int i = 0; i < 5; i++)
                {
                    PartSkill(d.SkillIds[i], m.Name + " " + Ladder[i], d.Family, Rarity.Mythic, m.Skills[i], MythicSkill(m.Id, i, false));
                    PartSkill(d.AwakenedSkillIds[i], m.AwakenedName + " " + Ladder[i], d.Family, Rarity.Mythic, m.Awakened[i], MythicSkill(m.Id, i, true));
                }
            }
        }

        private static readonly string[] Ladder = { "I", "II", "III", "S", "SS" };

        // ---- standard parts: Epic (1 skill) and Legendary (2 skills) ---------------------------------------

        private static SkillTrigger[] StdSkill(PartRarity r, PartKind k, PartRole role, int i)
        {
            const TriggerKind Start = TriggerKind.OnBattleStart, Hit = TriggerKind.OnHit, Hurt = TriggerKind.OnDamaged, Dodge = TriggerKind.OnDodge,
                Kill = TriggerKind.OnKill, Crit = TriggerKind.OnCrit, Rage = TriggerKind.OnRageAttack, TurnS = TriggerKind.OnTurnStart,
                TurnE = TriggerKind.OnTurnEnd, RoundS = TriggerKind.OnRoundStart, Atk = TriggerKind.OnAttack;
            bool epic = r == PartRarity.Epic;
            string key = (epic ? "E" : "L" + i) + k + role;
            switch (key)
            {
                // HEAD: openers and precision
                case "EHeadGuard": return new[] { PT(Start, EffectKind.Shield, 10) };
                case "EHeadStrike": return new[] { PT(Start, EffectKind.Empower, 100) };
                case "EHeadSwift": return new[] { PBuff(Start, StatId.Speed, 20, dur: 2) };
                case "EHeadKeen": return new[] { PRule("first_hit_crit") };
                case "EHeadMend": return new[] { PT(Kill, EffectKind.Heal, 5) };
                case "EHeadFury": return new[] { PT(Start, EffectKind.GainRage, 30) };
                case "L0HeadGuard": return new[] { PBuff(Start, StatId.DamageDealt, -10, tg: TargetKind.AllEnemies, dur: 3) };
                case "L1HeadGuard": return new[] { PT(Kill, EffectKind.Shield, 8) };
                case "L0HeadStrike": return new[] { PT(Start, EffectKind.Damage, 50, TargetKind.AllEnemies) };
                case "L1HeadStrike": return new[] { PT(Kill, EffectKind.Empower, 100) };
                case "L0HeadSwift": return new[] { PT(TurnE, EffectKind.ExtraTurn, iff: "first_turn", maxBattle: 1) };
                case "L1HeadSwift": return new[] { PRule("slower_taken", 15) };
                case "L0HeadKeen": return new[] { PT(Start, EffectKind.SureCrit, stacks: 3) };
                case "L1HeadKeen": return new[] { PRule("crit_def_ignore", 50) };
                case "L0HeadMend": return new[] { PRule("overheal_cap", 15) };
                case "L1HeadMend": return new[] { PT(Crit, EffectKind.Heal, 2) };
                case "L0HeadFury": return new[] { PT(Start, EffectKind.Empower, 100, param: "rage") };
                case "L1HeadFury": return new[] { PT(Kill, EffectKind.GainRage, 25) };
                // BODY: defence when hit and at low HP
                case "EBodyGuard": return new[] { PRule("first_taken", 20) };
                case "EBodyStrike": return new[] { PBuff(Hurt, StatId.Atk, 4, "stack:5") };
                case "EBodySwift": return new[] { PBuff(Hurt, StatId.Dodge, 30, "until_turn", iff: "self_hp_below:50") };
                case "EBodyKeen": return new[] { PBuff(Hurt, StatId.CritChance, 15, "next_attack") };
                case "EBodyMend": return new[] { PT(Hurt, EffectKind.Heal, 2, param: "rnd_cap:1") };
                case "EBodyFury": return new[] { PT(Hurt, EffectKind.GainRage, 10) };
                case "L0BodyGuard": return new[] { PRule("hit_cap", 20) };
                case "L1BodyGuard": return new[] { PT(Hurt, EffectKind.Shield, 30, iff: "self_hp_below:30", maxBattle: 1) };
                case "L0BodyStrike": return new[] { PT(Hurt, EffectKind.Reflect, 30, TargetKind.AllOtherEnemies, param: "arm") };
                case "L1BodyStrike": return new[] { PT(Hurt, EffectKind.Damage, 150, TargetKind.AllEnemies, iff: "self_hp_below:30", maxBattle: 1) };
                case "L0BodySwift": return new[] { PT(Hurt, EffectKind.ExtraTurn, iff: "self_hp_below:30", maxBattle: 1) };
                case "L1BodySwift": return new[] { PBuff(Hurt, StatId.Speed, 3, "stack:5") };
                case "L0BodyKeen": return new[] { PT(Crit, EffectKind.Shield, 5, param: "of_amount") };
                case "L1BodyKeen": return new[] { PT(Hurt, EffectKind.SureCrit, iff: "crit_in") };
                case "L0BodyMend": return new[] { PT(Hurt, EffectKind.Heal, 50, param: "of_amount", iff: "amount_pct:15") };
                case "L1BodyMend": return new[] { PT(Hurt, EffectKind.RemoveStatus, 0, TargetKind.Self, stacks: 1, param: "largest") };
                case "L0BodyFury": return new[] { PRule("rage_cost_low:50", 20) };
                case "L1BodyFury": return new[] { PT(Hurt, EffectKind.GainRage, 25, iff: "crit_in") };
                // TAIL: rhythm
                case "ETailGuard": return new[] { PT(TurnS, EffectKind.Shield, 8, every: 3) };
                case "ETailStrike": return new[] { PT(TurnS, EffectKind.Damage, 80, TargetKind.LowestHpEnemy, every: 3) };
                case "ETailSwift": return new[] { PBuff(RoundS, StatId.Speed, 2, "stack:10") };
                case "ETailKeen": return new[] { PT(TurnS, EffectKind.SureCrit, param: "turn", every: 3) };
                case "ETailMend": return new[] { PT(TurnE, EffectKind.Heal, 4, iff: "self_hp_below:50") };
                case "ETailFury": return new[] { PT(TurnS, EffectKind.GainRage, 25, every: 4) };
                case "L0TailGuard": return new[] { PT(TriggerKind.OnRoundEnd, EffectKind.Shield, 3, iff: "self_shielded") };
                case "L1TailGuard": return new[] { PT(RoundS, EffectKind.Guard, 50, param: "hits:2", every: 4) };
                case "L0TailStrike": return new[] { PBuff(TurnS, StatId.Atk, 3, "stack:10") };
                case "L1TailStrike": return new[] { PT(TurnS, EffectKind.TurnFlag, param: "aoe", every: 4) };
                case "L0TailSwift": return new[] { PT(TurnE, EffectKind.ExtraTurn, every: 5) };
                case "L1TailSwift": return new[] { PRule("act_first:3") };
                case "L0TailKeen": return new[] { PBuff(TurnE, StatId.CritChance, 10, "until_crit", iff: "no_crit_turn") };
                case "L1TailKeen": return new[] { PT(TurnS, EffectKind.Damage, 100, TargetKind.Enemy, param: "crit", every: 4) };
                case "L0TailMend": return new[] { PT(RoundS, EffectKind.Heal, 1, param: "per_round;max:6") };
                case "L1TailMend": return new[] { PT(TurnS, EffectKind.RemoveStatus, 0, TargetKind.Self, stacks: 0, param: "all", every: 4) };
                case "L0TailFury": return new[] { PT(RoundS, EffectKind.GainRage, 5, param: "per_enemy") };
                case "L1TailFury": return new[] { PRule("rage_target_lowest") };
                // LEGS: evasion
                case "ELegsGuard": return new[] { PT(Dodge, EffectKind.Shield, 4) };
                case "ELegsStrike": return new[] { PT(Dodge, EffectKind.Damage, 40, TargetKind.Enemy) };
                case "ELegsSwift": return new[] { PBuff(Start, StatId.Dodge, 20, dur: 1) };
                case "ELegsKeen": return new[] { PT(Dodge, EffectKind.SureCrit) };
                case "ELegsMend": return new[] { PT(Dodge, EffectKind.Heal, 3) };
                case "ELegsFury": return new[] { PT(Dodge, EffectKind.GainRage, 10) };
                case "L0LegsGuard": return new[] { PT(Dodge, EffectKind.Guard, 50, param: "hits:1") };
                case "L1LegsGuard": return new[] { PBuff(Hurt, StatId.Dodge, 3, "until_dodge;stack:5") };
                case "L0LegsStrike": return new[] { PT(Dodge, EffectKind.Empower, 100) };
                case "L1LegsStrike": return new[] { PT(Dodge, EffectKind.Damage, 20, TargetKind.AllEnemies) };
                case "L0LegsSwift": return new[] { PBuff(Dodge, StatId.Speed, 5, "stack:5") };
                case "L1LegsSwift": return new[] { PT(Start, EffectKind.ForceMiss, stacks: 1) };
                case "L0LegsKeen": return new[] { PBuff(Dodge, StatId.CritDamage, 10, "stack:5") };
                case "L1LegsKeen": return new[] { PT(Dodge, EffectKind.Damage, 50, TargetKind.Enemy, param: "crit") };
                case "L0LegsMend": return new[] { PBuff(Dodge, StatId.Regen, 1, "stack:5") };
                case "L1LegsMend": return new[] { PBuff(Dodge, StatId.HealReceived, 100, "until_turn") };
                case "L0LegsFury": return new[] { PBuff(Rage, StatId.Dodge, 20, "until_turn") };
                case "L1LegsFury": return new[] { PT(Dodge, EffectKind.FreeRageAttack, 0, TargetKind.Enemy, param: "spend", iff: "rage_within:25") };
                // ARM: attacks
                case "EArmGuard": return new[] { PT(Rage, EffectKind.Shield, 15) };
                case "EArmStrike": return new[] { PT(Atk, EffectKind.Empower, 50, every: 3) };
                case "EArmSwift": return new[] { PBuff(Atk, StatId.Speed, 3, "stack:10") };
                case "EArmKeen": return new[] { PT(Crit, EffectKind.Damage, 30, TargetKind.OtherEnemies, stacks: 1, param: "noarc") };
                case "EArmMend": return new[] { PT(Rage, EffectKind.Heal, 15, param: "of_amount") };
                case "EArmFury": return new[] { PT(Rage, EffectKind.GainRage, 20) };
                case "L0ArmGuard": return new[] { PBuff(Rage, StatId.DamageTaken, -30, "until_turn") };
                case "L1ArmGuard": return new[] { PT(Hit, EffectKind.Shield, 5, param: "of_amount") };
                case "L0ArmStrike": return new[] { PT(Hit, EffectKind.Damage, 30, TargetKind.SecondEnemy) };
                case "L1ArmStrike": return new[] { PT(Atk, EffectKind.Empower, 100, param: "def", every: 4) };
                case "L0ArmSwift": return new[] { PT(Kill, EffectKind.ExtraTurn, maxBattle: 1) };
                case "L1ArmSwift": return new[] { PT(Atk, EffectKind.Repeat, 100, every: 4) };
                case "L0ArmKeen": return new[] { PApply(Crit, StatusId.Exposed, 3) };
                case "L1ArmKeen": return new[] { PT(Kill, EffectKind.Strike, 0, TargetKind.Enemy, iff: "crit_kill") };
                case "L0ArmMend": return new[] { PT(Hit, EffectKind.Heal, 20, param: "of_amount", iff: "hp_below:30") };
                case "L1ArmMend": return new[] { PRule("overkill_heal", 50) };
                case "L0ArmFury": return new[] { PT(Kill, EffectKind.GainRage, 50, iff: "in_rage") };
                case "L1ArmFury": return new[] { PT(Crit, EffectKind.GainRage, 5) };
            }
            throw new InvalidOperationException("no standard part skill " + key);
        }

        // ---- Mythics: 5 skills + awakened versions -----------------------------------------------------

        private static SkillTrigger[] MythicSkill(string id, int i, bool aw)
        {
            const TriggerKind Start = TriggerKind.OnBattleStart, Hit = TriggerKind.OnHit, Hurt = TriggerKind.OnDamaged, Dodge = TriggerKind.OnDodge,
                Kill = TriggerKind.OnKill, Crit = TriggerKind.OnCrit, Rage = TriggerKind.OnRageAttack, TurnS = TriggerKind.OnTurnStart,
                TurnE = TriggerKind.OnTurnEnd, RoundS = TriggerKind.OnRoundStart, Nova = TriggerKind.OnSupernova, Arc = TriggerKind.OnArcJump,
                Counter = TriggerKind.OnCounter, Combo = TriggerKind.OnCombo, Echo = TriggerKind.OnEcho, Undo = TriggerKind.OnUndo,
                Break = TriggerKind.OnShieldBreak, Consumed = TriggerKind.OnStatusConsumed, Block = TriggerKind.OnBlock;
            const StatusId Heat = StatusId.Heat, Chill = StatusId.Chill, Shock = StatusId.Shock, Rad = StatusId.Poison, Bleed = StatusId.Bleed;
            const TargetKind Enemy = TargetKind.Enemy, All = TargetKind.AllEnemies, Others = TargetKind.AllOtherEnemies, Self = TargetKind.Self;
            int A(int b, int a) => aw ? a : b;
            double V(double b, double a) => aw ? a : b;
            switch (id + "_" + (i + 1))
            {
                // ---- FIRE ----
                case "myth_cinder_crown_1": return new[] { aw ? PApply(Start, Heat, 5, param: "pick_front:2") : PApply(Start, Heat, 6) };
                case "myth_cinder_crown_2":
                    return new[] { PT(TriggerKind.OnEnemyDeath, EffectKind.TransferStatus, 100, aw ? Others : Enemy, Heat, param: aw ? null : "next", iff: "has:Heat") };
                case "myth_cinder_crown_3": return new[] { PT(Nova, EffectKind.Shield, V(10, 20)) };
                case "myth_cinder_crown_4": return new[] { PRule("burst_delta", V(2, 4), Heat) };
                case "myth_cinder_crown_5":
                    return new[] { PT(Kill, EffectKind.ForceSupernova, 0, Enemy, param: (aw ? "pick_has:Heat:1;turn_cap:2" : "pick_has:Heat:3;turn_cap:1"), iff: "nova_kill") };
                case "myth_ashwalkers_1": return new[] { PApply(Dodge, Heat, A(2, 4)) };
                case "myth_ashwalkers_2": return new[] { PT(Dodge, EffectKind.ForceSupernova, 0, Enemy, iff: aw ? "within_heat:5" : "within_heat:2") };
                case "myth_ashwalkers_3": return new[] { PBuff(Nova, StatId.Dodge, V(5, 10), aw ? "stack:3" : "stack:4") };
                case "myth_ashwalkers_4": return new[] { PRule("dodge_nova", V(0, 100)) };
                case "myth_ashwalkers_5": return new[] { PRule(aw ? "nth_miss:3" : "nth_miss:4"), PApply(Dodge, Heat, A(3, 5), iff: "forced") };
                case "myth_firebrand_blaster_1": return new[] { PApply(Rage, Heat, A(4, 8)) };
                case "myth_firebrand_blaster_2": return new[] { PT(Nova, EffectKind.GainRage, V(40, 60)) };
                case "myth_firebrand_blaster_3": return new[] { PT(Rage, EffectKind.SpreadStatus, 100, Enemy, Heat, param: aw ? null : "behind") };
                case "myth_firebrand_blaster_4": return new[] { PCc(Nova, StatusId.Stun, All, iff: aw ? null : "nova_rage") };
                case "myth_firebrand_blaster_5": return new[] { PT(Nova, EffectKind.FreeRageAttack, 0, Enemy, param: aw ? "turn_cap:2" : "turn_cap:1", iff: "nova_rage") };
                // ---- ICE ----
                case "myth_rimeguard_plate_1": return new[] { PT(RoundS, EffectKind.Shield, V(2, 4), param: "per_status_enemy:Chill") };
                case "myth_rimeguard_plate_2": return new[] { PApply(Break, Chill, A(3, 6), All) };
                case "myth_rimeguard_plate_3": return new[] { PT(TriggerKind.OnStatusApplied, EffectKind.Shield, V(2, 5), param: "Chill;turn_cap:1") };
                case "myth_rimeguard_plate_4": return new[] { aw ? PCc(Break, StatusId.Freeze, All, "tg_has:Chill") : PCc(Break, StatusId.Freeze) };
                case "myth_rimeguard_plate_5":
                    return aw
                        ? new[] { PRule("no_expire_shielded", 0, Chill), PApply(RoundS, Chill, 1, All, "tg_has:Chill", "self_shielded") }
                        : new[] { PRule("no_expire_shielded", 0, Chill) };
                case "myth_frostglide_skates_1": return new[] { PApply(RoundS, Chill, 1, aw ? All : Enemy, "per_stat:Dodge:10") };
                case "myth_frostglide_skates_2": return new[] { PRule("dodge_per_stack", V(2, 3), Chill) };
                case "myth_frostglide_skates_3": return new[] { PT(Dodge, EffectKind.Damage, V(80, 120), Enemy, iff: aw ? "has:Chill:3" : "has:Chill:5") };
                case "myth_frostglide_skates_4": return new[] { PCc(Dodge, StatusId.Freeze, Enemy, "pick_most:Chill", every: A(3, 2)) };
                case "myth_frostglide_skates_5": return new[] { PRule("dodge_cap_vs", V(5, 10), Chill) };
                case "myth_shatter_cannon_1": return new[] { PApply(Rage, Chill, A(3, 5), All) };
                case "myth_shatter_cannon_2": return new[] { PT(Rage, EffectKind.ConsumeStatus, V(15, 25), Enemy, Chill) };
                case "myth_shatter_cannon_3": return new[] { PApply(RoundS, Chill, A(1, 2), All, aw ? "tg_has:Chill:3" : "tg_has:Chill:5") };
                case "myth_shatter_cannon_4": return new[] { aw ? PCc(Rage, StatusId.Freeze, All, "tg_has:Chill") : PCc(Rage, StatusId.Freeze, Enemy, "pick_has:Chill:1") };
                case "myth_shatter_cannon_5": return new[] { PRule("rage_area_vs", 0, aw ? Chill : StatusId.Freeze) };
                // ---- LIGHTNING ----
                case "myth_dynamo_hull_1": return new[] { PApply(Hurt, Shock, A(1, 2)) };
                case "myth_dynamo_hull_2": return new[] { PT(Hurt, EffectKind.Damage, V(40, 60), TargetKind.OtherEnemies, stacks: A(1, 2)) };
                case "myth_dynamo_hull_3": return new[] { PT(Arc, EffectKind.Shield, 2, param: aw ? "turn_cap:3" : "turn_cap:1") };
                case "myth_dynamo_hull_4": return new[] { PCc(Hurt, StatusId.Stun, Enemy, iff: aw ? "has:Shock:3" : "has:Shock:4") };
                case "myth_dynamo_hull_5": return new[] { PT(Arc, EffectKind.ConsumeStatus, 30, All, Shock, every: A(6, 4)) };
                case "myth_storm_coil_1": return new[] { PRule("arc_volley:3", V(40, 60)) };
                case "myth_storm_coil_2": return new[] { PBuff(Arc, StatId.ArcDamage, V(2, 3), "stack:20") };
                case "myth_storm_coil_3": return new[] { PApply(RoundS, Shock, 1, Enemy, aw ? "pick_least:Shock:2" : "pick_least:Shock:1") };
                case "myth_storm_coil_4": return new[] { PRule(aw ? "volley_stun:3" : "volley_stun:4", 0, StatusId.None, 1) };
                case "myth_storm_coil_5": return aw ? new[] { PRule("arc_volley:2"), PRule("arc_volley_first") } : new[] { PRule("arc_volley:2") };
                case "myth_arcwright_drill_1": return new[] { PT(Hit, EffectKind.Damage, 50, TargetKind.OtherEnemies, stacks: A(1, 2), iff: "basic_hit") };
                case "myth_arcwright_drill_2": return new[] { PRule("arc_crit", V(0, 20)) };
                case "myth_arcwright_drill_3": return new[] { PT(Arc, EffectKind.Heal, 2, param: "turn_cap:1", iff: aw ? "has:Shock" : "has:Shock:3") };
                case "myth_arcwright_drill_4": return new[] { PCc(Arc, StatusId.Stun, Enemy, every: A(5, 3)) };
                case "myth_arcwright_drill_5": return new[] { PApply(Arc, Shock, A(1, 2), iff: "hit_crit") };
                // ---- RADIATION ----
                case "myth_isotope_visor_1": return new[] { PApply(Hit, Rad, A(6, 10), param: "per_target:1") };
                case "myth_isotope_visor_2": return new[] { PT(Crit, EffectKind.SpreadStatus, V(20, 40), Enemy, Rad, param: aw ? "cap:20" : "cap:10") };
                case "myth_isotope_visor_3": return new[] { aw ? PApply(Hit, Rad, 2, Enemy, "behind:2") : PApply(Hit, Rad, 1, TargetKind.SecondEnemy) };
                case "myth_isotope_visor_4": return new[] { PApply(RoundS, Rad, A(5, 10), Others, "pick_most:Poison;spread") };
                case "myth_isotope_visor_5": return new[] { PRule("spread_mult", V(100, 200), Rad) };
                case "myth_hotcell_tank_1": return new[] { PApply(Hurt, Rad, A(1, 2), All) };
                case "myth_hotcell_tank_2": return new[] { PT(RoundS, EffectKind.Heal, 1, param: aw ? "per_total:Poison:10;max:10" : "per_total:Poison:20;max:5") };
                case "myth_hotcell_tank_3": return new[] { PRule(aw ? "taken_vs:10" : "taken_vs:20", V(15, 25), Rad) };
                case "myth_hotcell_tank_4":
                    return new[] { PT(Hurt, EffectKind.MultiplyStatus, 200, All, Rad, param: aw ? "cap:60" : "cap:40", iff: aw ? "self_hp_below:50" : "self_hp_below:30", maxBattle: 1) };
                case "myth_hotcell_tank_5": return new[] { PT(Hurt, EffectKind.TickStatus, 100, Enemy, Rad, stacks: A(1, 2), iff: "has:Poison") };
                case "myth_decay_stinger_1": return new[] { PApply(TurnS, Rad, A(5, 10), every: 2) };
                case "myth_decay_stinger_2": return new[] { PT(TurnS, EffectKind.TickStatus, 100, All, Rad, every: A(4, 3)) };
                case "myth_decay_stinger_3": return new[] { PT(Rage, EffectKind.TickStatus, V(100, 150), Enemy, Rad, stacks: 2) };
                case "myth_decay_stinger_4": return new[] { PRule(aw ? "execute_at:30" : "execute_at:40", V(20, 30), Rad) };
                case "myth_decay_stinger_5": return new[] { PApply(TriggerKind.OnStatusTick, Rad, A(1, 2), param: "Poison") };
                // ---- BLEED ----
                case "myth_bloodtide_fin_1": return new[] { PApply(TurnS, Bleed, A(2, 4), All, every: 3) };
                case "myth_bloodtide_fin_2": return new[] { PT(TurnE, EffectKind.ConsumeStatus, V(25, 30), Enemy, Bleed, iff: aw ? "has:Bleed:5" : "has:Bleed:8") };
                case "myth_bloodtide_fin_3": return new[] { PT(Consumed, EffectKind.Shield, V(20, 40), param: "Bleed;of_consume") };
                case "myth_bloodtide_fin_4": return new[] { PApply(Consumed, Bleed, 1, aw ? Others : TargetKind.OtherEnemies, "Bleed;half") };
                case "myth_bloodtide_fin_5": return new[] { PRule(aw ? "dot_twice_at:5" : "dot_twice_at:0", 0, Bleed) };
                case "myth_thornstep_legs_1": return new[] { PApply(Dodge, Bleed, A(3, 6), param: "arm") };
                case "myth_thornstep_legs_2": return new[] { aw ? PT(Dodge, EffectKind.TickStatus, 100, All, Bleed, param: "tg_has:Bleed") : PT(Dodge, EffectKind.TickStatus, 100, Enemy, Bleed, iff: "has:Bleed") };
                case "myth_thornstep_legs_3": return new[] { PT(Dodge, EffectKind.GainRage, V(-10, -20), Enemy, iff: "has:Bleed") };
                case "myth_thornstep_legs_4": return new[] { PApply(Dodge, Bleed, A(1, 2), Others, aw ? null : "tg_has:Bleed") };
                case "myth_thornstep_legs_5": return new[] { PT(Dodge, EffectKind.ConsumeStatus, V(30, 45), Enemy, Bleed) };
                case "myth_bloodletter_claw_1": return new[] { PApply(Crit, Bleed, A(3, 5)) };
                case "myth_bloodletter_claw_2": return new[] { PT(Crit, EffectKind.ConsumeStatus, V(20, 30), Enemy, Bleed, iff: aw ? "has:Bleed:7" : "max:Bleed") };
                case "myth_bloodletter_claw_3": return new[] { PT(Consumed, EffectKind.Heal, V(2, 3), param: "Bleed;per_unit") };
                case "myth_bloodletter_claw_4": return new[] { PT(TriggerKind.OnEnemyDeath, EffectKind.Heal, V(2, 4), param: "per_ctx_stack:Bleed", iff: "has:Bleed") };
                case "myth_bloodletter_claw_5": return new[] { PT(Crit, EffectKind.TickStatus, 100, All, Bleed, stacks: A(1, 2)) };
                // ---- COMBO ----
                case "myth_afterburner_tail_1": return new[] { PT(TurnS, EffectKind.ForceCombo, stacks: A(3, 5), every: 3) };
                case "myth_afterburner_tail_2": return new[] { PT(TurnE, EffectKind.GainRage, V(20, 30), iff: aw ? "combo_min:2" : "combo_min:3") };
                case "myth_afterburner_tail_3": return new[] { PT(Combo, EffectKind.Shield, V(2, 4), iff: "turn_every:3") };
                case "myth_afterburner_tail_4": return new[] { PT(TurnE, EffectKind.ExtraTurn, iff: "combo_min:4", maxBattle: A(1, 2)) };
                case "myth_afterburner_tail_5": return new[] { PRule(aw ? "combo_full_every:2" : "combo_full_every:3") };
                case "myth_flicker_treads_1": return new[] { PBuff(Dodge, StatId.ComboChance, V(20, 35), "next_turn") };
                case "myth_flicker_treads_2": return new[] { PBuff(Combo, StatId.Dodge, V(2, 4), "until_turn;stack:5") };
                case "myth_flicker_treads_3": return new[] { PT(Dodge, EffectKind.Strike, 0, Enemy, param: "chain", chance: V(50, 100)) };
                case "myth_flicker_treads_4": return new[] { PT(TurnE, EffectKind.ForceMiss, stacks: A(1, 2), iff: "combo_min:3") };
                case "myth_flicker_treads_5": return new[] { PRule("dodge_as_combo", V(50, 100)) };
                case "myth_chainlash_whip_1": return new[] { PT(Combo, EffectKind.Damage, V(30, 50), TargetKind.OtherEnemies, stacks: 1, param: "noarc") };
                case "myth_chainlash_whip_2": return new[] { PRule("combo_falloff", V(-10, -20)) };
                case "myth_chainlash_whip_3": return new[] { PRule("combo_kill_carry", V(0, 1)) };
                case "myth_chainlash_whip_4": return new[] { PRule("combo_target_lowest", V(0, 100)) };
                case "myth_chainlash_whip_5": return new[] { PT(Combo, EffectKind.FreeRageAttack, 0, Enemy, every: A(10, 6)) };
                // ---- COUNTER ----
                case "myth_feint_mask_1": return new[] { PRule("first_hit_counter", V(40, 70)) };
                case "myth_feint_mask_2": return new[] { PBuff(Counter, StatId.CritChance, -1000, aw ? "until_turn:2" : "until_turn", Enemy) };
                case "myth_feint_mask_3": return new[] { PT(Counter, EffectKind.GainRage, V(-10, -20), Enemy) };
                case "myth_feint_mask_4": return new[] { PT(Counter, EffectKind.Reflect, V(50, 100), Enemy, param: "of_taken", iff: "rage_in") };
                case "myth_feint_mask_5": return new[] { PCc(Counter, StatusId.Delay, Enemy, aw ? "every_target:2" : "every_target:3") };
                case "myth_rampart_carapace_1": return new[] { PT(Counter, EffectKind.MultiplyShield, 200, param: aw ? "cap:25" : "cap:15") };
                case "myth_rampart_carapace_2": return new[] { PT(TriggerKind.OnShieldHit, EffectKind.TurnFlag, param: aw ? "counter_hit;rnd_cap:2" : "counter_hit;rnd_cap:1") };
                case "myth_rampart_carapace_3": return new[] { PT(Counter, EffectKind.Reflect, V(5, 10), Enemy, param: "maxhp") };
                case "myth_rampart_carapace_4": return new[] { PT(Break, EffectKind.TurnFlag, param: "counter_window", dur: A(0, 2)) };
                case "myth_rampart_carapace_5": return new[] { PRule(aw ? "counter_aoe" : "counter_aoe_shielded") };
                case "myth_backlash_hammer_1": return new[] { PT(Counter, EffectKind.GainRage, V(15, 25)) };
                case "myth_backlash_hammer_2": return new[] { PT(Counter, EffectKind.Charge, V(10, 20), param: aw ? "rage;cap:100" : "rage;cap:50") };
                case "myth_backlash_hammer_3": return new[] { PBuff(Rage, StatId.CounterChance, V(25, 50), "until_turn") };
                case "myth_backlash_hammer_4": return new[] { PT(Counter, EffectKind.FreeRageAttack, 0, Enemy, every: A(3, 2)) };
                case "myth_backlash_hammer_5": return new[] { PRule("rage_counter", V(0, 50)) };
                // ---- ALIEN ----
                case "myth_swarmcaller_antenna_1": return new[] { PT(Start, EffectKind.AllyAction, 0, Self, stacks: A(1, 2), param: "all") };
                case "myth_swarmcaller_antenna_2": return new[] { PT(Crit, EffectKind.AllyAction, 0, Self, stacks: 1, param: aw ? "random;turn_cap:2" : "random;turn_cap:1") };
                case "myth_swarmcaller_antenna_3": return new[] { PT(TriggerKind.OnStatusApplied, EffectKind.ChargeAllies, V(10, 20), param: "turn_cap:1") };
                case "myth_swarmcaller_antenna_4": return new[] { PRule(aw ? "sidekick_twice:2" : "sidekick_twice:1") };
                case "myth_swarmcaller_antenna_5": return new[] { PRule("sidekick_act", V(25, 50)) };
                case "myth_broodguard_legs_1": return new[] { PT(Dodge, EffectKind.Guard, V(30, 50), TargetKind.Worm, param: "kick;hits:1") };
                case "myth_broodguard_legs_2": return new[] { PT(Block, EffectKind.Damage, V(50, 80), Enemy) };
                case "myth_broodguard_legs_3": return new[] { PT(Block, EffectKind.AllyAction, 0, Self, stacks: A(1, 2)) };
                case "myth_broodguard_legs_4": return new[] { PT(Dodge, EffectKind.ChargeAllies, V(20, 35)) };
                case "myth_broodguard_legs_5": return aw ? new[] { PRule("guard_full"), PRule("guard_reflect", 50) } : new[] { PRule("guard_full") };
                case "myth_hatchery_tail_1": return new[] { PT(TurnS, EffectKind.Summon, 25, Self, stacks: 1, param: aw ? "sk_drone;max:4" : "sk_drone;max:3", every: A(3, 2)) };
                case "myth_hatchery_tail_2": return new[] { PT(Rage, EffectKind.AllyAction, 0, Self, stacks: A(1, 2), param: "drones") };
                case "myth_hatchery_tail_3": return new[] { PRule(aw ? "drone_guard:2" : "drone_guard:1", V(30, 50)) };
                case "myth_hatchery_tail_4": return new[] { PRule(aw ? "drone_status:2" : "drone_status:1") };
                case "myth_hatchery_tail_5":
                    return aw
                        ? new[] { PT(Start, EffectKind.Summon, 25, Self, stacks: 4, param: "sk_drone;max:4"), PT(RoundS, EffectKind.Summon, 25, Self, stacks: 4, param: "sk_drone;max:4", every: 5) }
                        : new[] { PT(Start, EffectKind.Summon, 25, Self, stacks: 3, param: "sk_drone;max:3") };
                // ---- TIME ----
                case "myth_foresight_visor_1": return new[] { PT(TurnE, EffectKind.Echo, 0, Enemy, stacks: A(1, 2), iff: "first_turn", maxBattle: 1) };
                case "myth_foresight_visor_2": return new[] { PT(Rage, EffectKind.Echo, 0, Enemy, stacks: 1, param: aw ? "mult:200" : null) };
                case "myth_foresight_visor_3": return new[] { PCond("echo", 100, aw ? "hp_above:50" : "hp_above:70") };
                case "myth_foresight_visor_4": return new[] { PT(Echo, EffectKind.Echo, 0, Enemy, stacks: 1, chance: V(25, 50)) };
                case "myth_foresight_visor_5": return new[] { PT(Echo, EffectKind.Reflect, V(50, 80), Others) };
                case "myth_hindsight_shell_1": return new[] { PT(Hurt, EffectKind.Rewind, 0, Self, stacks: 1, maxBattle: A(1, 2)) };
                case "myth_hindsight_shell_2": return new[] { PT(Undo, EffectKind.GainRage, V(20, 40)) };
                case "myth_hindsight_shell_3": return new[] { PT(Undo, EffectKind.Shield, V(5, 10)) };
                case "myth_hindsight_shell_4": return new[] { PT(Hurt, EffectKind.Rewind, 0, Enemy, stacks: 1, param: aw ? "per_target:2" : "per_target:1", iff: "rage_in") };
                case "myth_hindsight_shell_5": return new[] { PRule("undo_reflect", V(100, 200)) };
                case "myth_timeslip_boots_1": return new[] { PBuff(Dodge, StatId.Speed, V(-10, -15), "stack:3", Enemy) };
                case "myth_timeslip_boots_2": return new[] { PT(Dodge, EffectKind.ExtraTurn, every: A(3, 2)) };
                case "myth_timeslip_boots_3": return new[] { PT(Dodge, EffectKind.Echo, 0, Enemy, stacks: 1, param: aw ? "mult:70" : "mult:40") };
                case "myth_timeslip_boots_4": return new[] { PCc(Dodge, StatusId.Delay, Enemy, chance: V(25, 50)) };
                case "myth_timeslip_boots_5": return new[] { PT(TurnE, EffectKind.Echo, 0, Enemy, stacks: A(1, 2), iff: "extra_turn") };
                // ---- GENERAL ----
                case "myth_deadeye_visor_1": return new[] { PRule(aw ? "nth_hit_crit:3" : "nth_hit_crit:4") };
                case "myth_deadeye_visor_2": return new[] { PBuff(Crit, StatId.CritChance, V(2, 3), "stack:15") };
                case "myth_deadeye_visor_3": return new[] { PT(Kill, EffectKind.GainRage, V(25, 40), iff: "crit_kill") };
                case "myth_deadeye_visor_4": return new[] { PT(Crit, EffectKind.Execute, V(30, 40), Enemy, iff: "not_boss") };
                case "myth_deadeye_visor_5": return new[] { PRule("crit_overflow", V(2, 3)) };
                case "myth_last_stand_plate_1": return new[] { PCond(aw ? "missing_hp:1" : "missing_hp:2", 1) };
                case "myth_last_stand_plate_2": return new[] { PT(Hurt, EffectKind.GainRage, 100, iff: aw ? "self_hp_below:50" : "self_hp_below:30", maxBattle: 1) };
                case "myth_last_stand_plate_3": return new[] { PT(Kill, EffectKind.Heal, V(15, 25), iff: "self_hp_below:50") };
                case "myth_last_stand_plate_4": return new[] { PT(Hurt, EffectKind.ExtraTurn, stacks: A(2, 3), iff: "self_hp_below:30", maxBattle: 1) };
                case "myth_last_stand_plate_5": return new[] { PRule(aw ? "low_hp_aoe:50" : "low_hp_aoe:30", 100) };
                case "myth_rage_turbine_1": return new[] { PT(TurnS, EffectKind.GainRage, V(10, 20)) };
                case "myth_rage_turbine_2": return new[] { PT(Rage, EffectKind.FreeRageAttack, 0, Enemy, every: A(3, 2)) };
                case "myth_rage_turbine_3": return new[] { PT(Rage, EffectKind.Heal, V(5, 10)) };
                case "myth_rage_turbine_4": return new[] { PRule("rage_aoe", V(60, 100)) };
                case "myth_rage_turbine_5": return new[] { PRule("rage_cost", V(25, 40)) };
            }
            throw new InvalidOperationException("no Mythic part skill " + id + " " + (i + 1));
        }
    }
}
