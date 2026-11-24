using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>
    /// Part skills (Progression): every part def has five skills (Source = Part) that unlock at Epic, Legendary, Mythic,
    /// Mythic S and Mythic SS, and five awakened redesigns with the same unlock pattern. Skills are built from the part's
    /// family (element status, crit, combo, counter, alien squad) and its kind (head = precision, body = defence,
    /// tail = momentum, legs = evasion, arm = attack style). Awakened skills pair the family with a "twin" family and
    /// use different triggers (status chains, turn-start pulses, opening barrages, overheal shields, last stands) so an
    /// awakened part can open a new build. Roles: docs/design/parts.md.
    /// </summary>
    public sealed partial class ContentDb
    {
        /// <summary>Second family of an awakened part (its redesigned skills mix both).</summary>
        public static SkillFamily PartTwinFamily(SkillFamily f)
        {
            switch (f)
            {
                case SkillFamily.Fire: return SkillFamily.Crit;
                case SkillFamily.Ice: return SkillFamily.Counter;
                case SkillFamily.Lightning: return SkillFamily.Combo;
                case SkillFamily.Poison: return SkillFamily.AlienSynergy;
                case SkillFamily.Bleed: return SkillFamily.Counter;
                case SkillFamily.Crit: return SkillFamily.Lightning;
                case SkillFamily.Combo: return SkillFamily.Fire;
                case SkillFamily.Counter: return SkillFamily.Poison;
                case SkillFamily.AlienSynergy: return SkillFamily.Bleed;
                default: return SkillFamily.Crit;
            }
        }

        private static StatusId PartFamilyStatus(SkillFamily f)
        {
            switch (f)
            {
                case SkillFamily.Fire: return StatusId.Heat;
                case SkillFamily.Ice: return StatusId.Freeze;
                case SkillFamily.Lightning: return StatusId.Shock;
                case SkillFamily.Poison: return StatusId.Poison;
                case SkillFamily.Bleed: return StatusId.Bleed;
                default: return StatusId.None;
            }
        }

        private static StatId PartFamilyElementStat(SkillFamily f)
        {
            switch (f)
            {
                case SkillFamily.Fire: return StatId.FireDamage;
                case SkillFamily.Ice: return StatId.IceDamage;
                case SkillFamily.Lightning: return StatId.LightningDamage;
                case SkillFamily.Poison: return StatId.PoisonDamage;
                default: return StatId.BleedDamage;
            }
        }

        /// <summary>Extra base stat of an awakened part: the family's signature stat.</summary>
        private static StatMod PartAwakenedSignatureStat(SkillFamily f)
        {
            switch (f)
            {
                case SkillFamily.Crit: return new StatMod(StatId.CritDamage, 12);
                case SkillFamily.Combo: return new StatMod(StatId.ComboChance, 3);
                case SkillFamily.Counter: return new StatMod(StatId.CounterChance, 3);
                case SkillFamily.AlienSynergy: return new StatMod(StatId.AlienPower, 8);
                default: return new StatMod(PartFamilyElementStat(f), 8);
            }
        }

        /// <summary>The family's stat package scaled by power (passive part of most part skills).</summary>
        private static void PartFamilyStats(SkillDef s, SkillFamily f, double p)
        {
            switch (f)
            {
                case SkillFamily.Crit: s.Stats.Add(new StatMod(StatId.CritChance, R(2 * p))); s.Stats.Add(new StatMod(StatId.CritDamage, R(6 * p))); break;
                case SkillFamily.Combo: s.Stats.Add(new StatMod(StatId.ComboChance, R(2 * p))); break;
                case SkillFamily.Counter: s.Stats.Add(new StatMod(StatId.CounterChance, R(2 * p))); s.Stats.Add(new StatMod(StatId.ParryChance, R(1 * p))); break;
                case SkillFamily.AlienSynergy: s.Stats.Add(new StatMod(StatId.AlienPower, R(5 * p))); break;
                default: s.Stats.Add(new StatMod(PartFamilyElementStat(f), R(5 * p))); break;
            }
        }

        /// <summary>The family's effect when a part skill fires (status, arc, shield, rage, squad buff).</summary>
        private static List<SkillTrigger> PartPayload(SkillFamily f, TriggerKind when, double chance, double p, TargetKind target, int cooldown = 0)
        {
            var list = new List<SkillTrigger>();
            SkillTrigger T(EffectKind e) { var t = new SkillTrigger { When = when, Chance = Math.Min(100, Math.Round(chance)), Effect = e, Target = target, CooldownRounds = cooldown }; list.Add(t); return t; }
            switch (f)
            {
                case SkillFamily.Fire: { var t = T(EffectKind.ApplyStatus); t.Status = StatusId.Heat; t.Stacks = 1 + (int)(p / 1.2); t.Duration = 3; break; }
                case SkillFamily.Ice:
                {
                    var d = T(EffectKind.Damage); d.Value = R(20 * p);
                    var t = T(EffectKind.ApplyStatus); t.Status = StatusId.Freeze; t.Stacks = 1; t.Duration = 1; t.Chance = Math.Min(100, Math.Round(chance * 0.5));
                    break;
                }
                case SkillFamily.Lightning:
                {
                    var d = T(EffectKind.Damage); d.Value = R(28 * p); if (target == TargetKind.Enemy) d.Target = TargetKind.RandomEnemy;
                    var t = T(EffectKind.ApplyStatus); t.Status = StatusId.Shock; t.Stacks = 1; t.Duration = 2;
                    break;
                }
                case SkillFamily.Poison: { var t = T(EffectKind.ApplyStatus); t.Status = StatusId.Poison; t.Stacks = 1 + (int)p; t.Duration = 4; break; }
                case SkillFamily.Bleed: { var t = T(EffectKind.ApplyStatus); t.Status = StatusId.Bleed; t.Stacks = 1 + (int)(p / 1.2); t.Duration = 3; break; }
                case SkillFamily.Crit: { var t = T(EffectKind.BuffStat); t.Target = TargetKind.Self; t.Stat = new StatMod(StatId.CritDamage, R(12 * p)); t.Duration = 2; var r = T(EffectKind.GainRage); r.Target = TargetKind.Self; r.Value = R(6 * p); break; }
                case SkillFamily.Combo: { var d = T(EffectKind.Damage); d.Value = R(22 * p); var b = T(EffectKind.BuffStat); b.Target = TargetKind.Self; b.Stat = new StatMod(StatId.ComboChance, R(4 * p)); b.Duration = 2; break; }
                case SkillFamily.Counter: { var t = T(EffectKind.Shield); t.Target = TargetKind.Self; t.Value = R(3 * p); var b = T(EffectKind.BuffStat); b.Target = TargetKind.Self; b.Stat = new StatMod(StatId.CounterChance, R(5 * p)); b.Duration = 2; break; }
                case SkillFamily.AlienSynergy: { var t = T(EffectKind.BuffStat); t.Target = TargetKind.AllAllies; t.Stat = new StatMod(StatId.Atk, 0, R(6 * p)); t.Duration = 2; var r = T(EffectKind.GainRage); r.Target = TargetKind.AllAllies; r.Value = R(8 * p); break; }
                default: { var d = T(EffectKind.Damage); d.Value = R(25 * p); break; }
            }
            return list;
        }

        private static double R(double v) => Math.Round(v, 1);

        /// <summary>Kind trigger: when a part of this kind (arms: this attack style) fires its technique, and how often.</summary>
        private static void PartKindTrigger(PartDef d, out TriggerKind when, out double chance, out int cooldown, out string what)
        {
            cooldown = 0;
            switch (d.Kind)
            {
                case PartKind.Head: when = TriggerKind.OnCrit; chance = 45; what = "Critical hits"; return;
                case PartKind.Body: when = TriggerKind.OnDamaged; chance = 30; cooldown = 1; what = "Taking damage"; return;
                case PartKind.Tail: when = TriggerKind.OnCombo; chance = 50; what = "Combos"; return;
                case PartKind.Legs: when = TriggerKind.OnDodge; chance = 100; what = "Dodging"; return;
            }
            switch (d.Style)
            {
                case AttackStyle.Slam: when = TriggerKind.OnCounter; chance = 70; what = "Counters"; return;
                case AttackStyle.Claw: when = TriggerKind.OnCrit; chance = 55; what = "Critical hits"; return;
                case AttackStyle.Drill: when = TriggerKind.OnHit; chance = 30; cooldown = 1; what = "Hits"; return;
                case AttackStyle.Whip: when = TriggerKind.OnCombo; chance = 60; what = "Combos"; return;
                default: when = TriggerKind.OnRageAttack; chance = 100; what = "Rage attacks"; return; // Blaster, Cannon
            }
        }

        /// <summary>Kind stat added to the technique skill (head precision, body toughness, tail momentum, legs evasion, arm style).</summary>
        private static StatMod PartKindStat(PartDef d, double p)
        {
            switch (d.Kind)
            {
                case PartKind.Head: return new StatMod(StatId.CritChance, R(3 * p));
                case PartKind.Body: return new StatMod(StatId.DamageReduction, R(3 * p));
                case PartKind.Tail: return new StatMod(StatId.ComboChance, R(3 * p));
                case PartKind.Legs: return new StatMod(StatId.Dodge, R(2.5 * p));
            }
            switch (d.Style)
            {
                case AttackStyle.Slam: return new StatMod(StatId.CounterChance, R(3 * p));
                case AttackStyle.Claw: return new StatMod(StatId.CritChance, R(3 * p));
                case AttackStyle.Drill: return new StatMod(StatId.DodgeIgnore, R(5 * p));
                case AttackStyle.Whip: return new StatMod(StatId.ComboChance, R(3 * p));
                case AttackStyle.Cannon: return new StatMod(StatId.BossDamage, R(8 * p));
                default: return new StatMod(StatId.RageGain, R(8 * p)); // Blaster
            }
        }

        private static string FamilyWord(SkillFamily f)
        {
            switch (f)
            {
                case SkillFamily.Fire: return "Ember";
                case SkillFamily.Ice: return "Frost";
                case SkillFamily.Lightning: return "Storm";
                case SkillFamily.Poison: return "Isotope";
                case SkillFamily.Bleed: return "Razor";
                case SkillFamily.Crit: return "Precision";
                case SkillFamily.Combo: return "Flurry";
                case SkillFamily.Counter: return "Riposte";
                case SkillFamily.AlienSynergy: return "Hive";
                default: return "Core";
            }
        }

        private static string KindWord(PartDef d)
        {
            switch (d.Kind)
            {
                case PartKind.Head: return "Gaze";
                case PartKind.Body: return "Hide";
                case PartKind.Tail: return "Wake";
                case PartKind.Legs: return "Step";
                default: return d.Style.ToString();
            }
        }

        private static string FamilyEffectText(SkillFamily f)
        {
            switch (f)
            {
                case SkillFamily.Fire: return "set enemies on fire";
                case SkillFamily.Ice: return "strike with frost and may freeze";
                case SkillFamily.Lightning: return "arc lightning and shock";
                case SkillFamily.Poison: return "irradiate";
                case SkillFamily.Bleed: return "cause bleeding";
                case SkillFamily.Crit: return "sharpen your crits and build rage";
                case SkillFamily.Combo: return "strike again and raise combo chance";
                case SkillFamily.Counter: return "raise a shield and counter more";
                case SkillFamily.AlienSynergy: return "rally your aliens";
                default: return "deal damage";
            }
        }

        private SkillDef NewPartSkill(PartDef d, int index, bool awakened, string name, Rarity rarity, SkillFamily family, string desc)
        {
            var s = new SkillDef
            {
                Id = d.Id + (awakened ? "_aw" : "_sk") + (index + 1), Name = name, Family = family, Rarity = rarity,
                Source = SkillSource.Part, Draftable = false, Description = desc,
            };
            AddSkill(s);
            (awakened ? d.AwakenedSkillIds : d.SkillIds).Add(s.Id);
            return s;
        }

        /// <summary>Builds the five skills and the five awakened skills of a part def (see class summary).</summary>
        private void BuildPartSkills(PartDef d)
        {
            var f = d.Family; var twin = PartTwinFamily(f);
            var status = PartFamilyStatus(f);
            string fw = FamilyWord(f), kw = KindWord(d);
            PartKindTrigger(d, out var kindWhen, out var kindChance, out var kindCd, out var kindWhat);

            // 1 (Epic) core: family stats + a light family proc on hit.
            var core = NewPartSkill(d, 0, false, fw + " Core", Rarity.Epic, f, "Family stats; hits may " + FamilyEffectText(f) + ".");
            PartFamilyStats(core, f, 0.8);
            core.Triggers.AddRange(PartPayload(f, TriggerKind.OnHit, 12, 0.7, TargetKind.Enemy, 1));

            // 2 (Legendary) technique: the kind's trigger fires the family payload; kind stat.
            var tech = NewPartSkill(d, 1, false, fw + " " + kw, Rarity.Legendary, f, kindWhat + " " + FamilyEffectText(f) + ".");
            tech.Stats.Add(PartKindStat(d, 1));
            tech.Triggers.AddRange(PartPayload(f, kindWhen, kindChance, 1.1, TargetKind.Enemy, kindCd));

            // 3 (Mythic) synergy: the build hook — bonus damage against the family's condition.
            var syn = NewPartSkill(d, 2, false, fw + " Hunter", Rarity.Mythic, f, "");
            switch (f)
            {
                case SkillFamily.Crit: syn.Triggers.Add(Cond("vs_low_hp", 22)); syn.Stats.Add(new StatMod(StatId.CritDamage, 15)); syn.Description = "+{22}% damage vs enemies under 40% HP, +{15} crit damage."; break;
                case SkillFamily.Combo: syn.Triggers.Add(Cond("combo", 28)); syn.Stats.Add(new StatMod(StatId.ComboChance, 4)); syn.Description = "Combos deal +{28}%; +{4} combo chance."; break;
                case SkillFamily.Counter: syn.Triggers.Add(Cond("counter", 32)); syn.Stats.Add(new StatMod(StatId.ParryChance, 5)); syn.Description = "Counters deal +{32}%; +{5} parry."; break;
                case SkillFamily.AlienSynergy:
                    syn.Stats.Add(new StatMod(StatId.AlienPower, 18));
                    syn.Triggers.Add(new SkillTrigger { When = TriggerKind.OnSidekickAct, Effect = EffectKind.GainRage, Target = TargetKind.Worm, Value = 10 });
                    syn.Description = "Sidekicks +{18}% power; each sidekick action gives the worm {10} rage."; break;
                default:
                    syn.Triggers.Add(Cond("vs_status:" + status, 22)); syn.Stats.Add(new StatMod(StatId.StatusChance, 8));
                    syn.Description = "+{22}% damage vs enemies with " + status + "; +{8} status chance."; break;
            }

            // 4 (Mythic S) signature: a kind-shaped, battle-defining effect.
            var sig = NewPartSkill(d, 3, false, kw + " of the " + fw + " Star", Rarity.Mythic, f, "");
            PartFamilyStats(sig, f, 1.2);
            switch (d.Kind)
            {
                case PartKind.Head:
                    sig.Triggers.AddRange(PartPayload(f, TriggerKind.OnBattleStart, 100, 1.6, TargetKind.AllEnemies));
                    sig.Description = "Battle start: " + FamilyEffectText(f) + " on every enemy."; break;
                case PartKind.Body:
                    sig.Triggers.Add(new SkillTrigger { When = TriggerKind.OnLowHp, Effect = EffectKind.Shield, Target = TargetKind.Self, Value = 25 });
                    sig.Triggers.AddRange(PartPayload(f, TriggerKind.OnLowHp, 100, 2.0, TargetKind.AllEnemies));
                    sig.Description = "Below 30% HP: a {25}% shield, then " + FamilyEffectText(f) + " on every enemy."; break;
                case PartKind.Tail:
                    sig.Triggers.Add(new SkillTrigger { When = TriggerKind.OnRoundStart, Effect = EffectKind.Damage, Target = TargetKind.AllEnemies, Value = 30, CooldownRounds = 1 });
                    sig.Triggers.AddRange(PartPayload(f, TriggerKind.OnRoundStart, 100, 1.2, TargetKind.AllEnemies, 1));
                    sig.Description = "Every other round: a {30}% ATK sweep that " + FamilyEffectText(f) + "."; break;
                case PartKind.Legs:
                    sig.Triggers.Add(new SkillTrigger { When = TriggerKind.OnDodge, Effect = EffectKind.TrueDamage, Target = TargetKind.Enemy, Value = 55 });
                    sig.Stats.Add(new StatMod(StatId.Dodge, 4));
                    sig.Description = "Dodges strike back for {55}% ATK true damage; +{4} dodge."; break;
                default:
                    sig.Triggers.Add(new SkillTrigger { When = TriggerKind.OnRageAttack, Effect = EffectKind.Damage, Target = TargetKind.AllEnemies, Value = 70 });
                    sig.Triggers.AddRange(PartPayload(f, TriggerKind.OnRageAttack, 100, 1.4, TargetKind.AllEnemies));
                    sig.Description = "Rage attacks hit every enemy for {70}% ATK and " + FamilyEffectText(f) + "."; break;
            }

            // 5 (Mythic SS) ultimate: the family's capstone.
            var ult = NewPartSkill(d, 4, false, fw + " Apex", Rarity.Mythic, f, "");
            switch (f)
            {
                case SkillFamily.Fire:
                    ult.Triggers.Add(St(TriggerKind.OnKill, 100, StatusId.Heat, 4, TargetKind.AllEnemies, 3));
                    ult.Triggers.Add(new SkillTrigger { When = TriggerKind.OnKill, Effect = EffectKind.Heal, Target = TargetKind.Self, Value = 6 });
                    ult.Stats.Add(new StatMod(StatId.FireDamage, 25)); ult.Description = "Kills spread 4 Heat to all enemies and heal {6}%; +{25}% fire damage."; break;
                case SkillFamily.Ice:
                    ult.Triggers.Add(St(TriggerKind.OnBattleStart, 100, StatusId.Freeze, 1, TargetKind.AllEnemies, 1));
                    ult.Triggers.Add(Cond("vs_status:Freeze", 30)); ult.Description = "Battle start: freeze every enemy; +{30}% damage vs frozen."; break;
                case SkillFamily.Lightning:
                    ult.Triggers.Add(St(TriggerKind.OnRoundStart, 25, StatusId.Stun, 1, TargetKind.RandomEnemy, 1));
                    ult.Triggers.Add(new SkillTrigger { When = TriggerKind.OnRoundStart, Effect = EffectKind.Damage, Target = TargetKind.AllEnemies, Value = 25 });
                    ult.Description = "Every round: {25}% ATK lightning on all enemies, 25% chance to stun one."; break;
                case SkillFamily.Poison:
                    ult.Triggers.Add(St(TriggerKind.OnRoundEnd, 100, StatusId.Poison, 2, TargetKind.AllEnemies, 4));
                    ult.Stats.Add(new StatMod(StatId.Regen, 2)); ult.Description = "Every round end: 2 Radiation on all enemies; regenerate {2}% per turn."; break;
                case SkillFamily.Bleed:
                    ult.Triggers.Add(St(TriggerKind.OnCrit, 100, StatusId.Bleed, 2, TargetKind.AllEnemies, 3));
                    ult.Stats.Add(new StatMod(StatId.LifeSteal, 8)); ult.Description = "Crits make every enemy bleed; +{8}% lifesteal."; break;
                case SkillFamily.Crit:
                    ult.Triggers.Add(new SkillTrigger { When = TriggerKind.OnCrit, Effect = EffectKind.TrueDamage, Target = TargetKind.Enemy, Value = 45 });
                    ult.Stats.Add(new StatMod(StatId.CritDamage, 30)); ult.Description = "Crits add a {45}% ATK true-damage strike; +{30} crit damage."; break;
                case SkillFamily.Combo:
                    ult.Triggers.Add(new SkillTrigger { When = TriggerKind.OnCombo, Effect = EffectKind.GainRage, Target = TargetKind.Self, Value = 12 });
                    ult.Triggers.Add(new SkillTrigger { When = TriggerKind.OnCombo, Effect = EffectKind.BuffStat, Target = TargetKind.Self, Stat = new StatMod(StatId.Atk, 0, 3) });
                    ult.Description = "Each combo: +{12} rage and +{3}% ATK for the battle."; break;
                case SkillFamily.Counter:
                    ult.Triggers.Add(new SkillTrigger { When = TriggerKind.OnParry, Effect = EffectKind.TrueDamage, Target = TargetKind.Enemy, Value = 110 });
                    ult.Stats.Add(new StatMod(StatId.ParryChance, 10)); ult.Description = "Parries reflect {110}% ATK true damage; +{10} parry."; break;
                default:
                    // the squad opens every battle: two sidekick actions (sidekick pass; was squad rage, which sidekicks do not use)
                    ult.Triggers.Add(new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.AllyAction, Target = TargetKind.Self, Stacks = 2 });
                    ult.Stats.Add(new StatMod(StatId.AlienPower, 25)); ult.Description = "Battle start: 2 sidekick actions; sidekicks +{25}% power."; break;
            }

            BuildAwakenedPartSkills(d, twin);
            foreach (var id in d.SkillIds) ScalePartSkill(Skills[id], PartsConfig.SkillPowerScale);
            foreach (var id in d.AwakenedSkillIds) ScalePartSkill(Skills[id], PartsConfig.AwakenedSkillPowerScale);
        }

        /// <summary>Scales a part skill's amounts (stats, damage/heal/shield/rage values, buffs, conditional bonuses); chances and stacks stay.</summary>
        private static void ScalePartSkill(SkillDef s, double k)
        {
            // Descriptions mark scaled amounts as {n}.
            s.Description = System.Text.RegularExpressions.Regex.Replace(s.Description ?? "", @"\{(\d+(?:\.\d+)?)\}",
                m => R(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * k).ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var m in s.Stats) { m.Flat = R(m.Flat * k); m.Percent = R(m.Percent * k); }
            foreach (var t in s.Triggers)
            {
                t.Value = R(t.Value * k);
                if (t.Stat != null) { t.Stat.Flat = R(t.Stat.Flat * k); t.Stat.Percent = R(t.Stat.Percent * k); }
            }
        }

        private void BuildAwakenedPartSkills(PartDef d, SkillFamily twin)
        {
            var f = d.Family;
            var status = PartFamilyStatus(f); var twinStatus = PartFamilyStatus(twin);
            string fw = FamilyWord(f), tw = FamilyWord(twin), kw = KindWord(d);
            PartKindTrigger(d, out var kindWhen, out var kindChance, out var kindCd, out var kindWhat);
            // The status this awakened part builds around (the family's own, else the twin's element).
            var chainStatus = status != StatusId.None ? status : twinStatus;

            // 1A core: both families' stats; the twin's trigger now delivers the family payload.
            var core = NewPartSkill(d, 0, true, tw + "-" + fw + " Core", Rarity.Epic, f, "Stats of both families; " + kindWhat.ToLowerInvariant() + " also " + FamilyEffectText(twin) + ".");
            PartFamilyStats(core, f, 1.1); PartFamilyStats(core, twin, 0.7);
            core.Triggers.AddRange(PartPayload(f, TriggerKind.OnHit, 15, 1.0, TargetKind.Enemy, 1));
            core.Triggers.AddRange(PartPayload(twin, kindWhen, kindChance * 0.6, 0.8, TargetKind.Enemy, Math.Max(1, kindCd)));

            // 2A technique: a turn-start pulse (no longer waits for a hit) on a random enemy.
            var tech = NewPartSkill(d, 1, true, "Pulsing " + fw + " " + kw, Rarity.Legendary, f, "Every second turn: " + FamilyEffectText(f) + " and " + FamilyEffectText(twin) + " on a random enemy.");
            tech.Stats.Add(PartKindStat(d, 1.4));
            tech.Triggers.AddRange(PartPayload(f, TriggerKind.OnTurnStart, 100, 1.4, TargetKind.RandomEnemy, 1));
            tech.Triggers.AddRange(PartPayload(twin, TriggerKind.OnTurnStart, 60, 1.0, TargetKind.RandomEnemy, 1));

            // 3A synergy: a status chain — every application of the build status pays out (builds around status counts).
            var syn = NewPartSkill(d, 2, true, chainStatus + " Resonance", Rarity.Mythic, f, "");
            if (chainStatus != StatusId.None)
            {
                syn.Triggers.Add(new SkillTrigger { When = TriggerKind.OnStatusApplied, Param = chainStatus.ToString(), Effect = EffectKind.Damage, Target = TargetKind.Enemy, Value = 18, Chance = 60 });
                syn.Triggers.Add(new SkillTrigger { When = TriggerKind.OnStatusApplied, Param = chainStatus.ToString(), Effect = EffectKind.GainRage, Target = TargetKind.Self, Value = 4 });
                syn.Triggers.Add(Cond("vs_status:" + chainStatus, 25));
                syn.Stats.Add(new StatMod(StatId.StatusChance, 12));
                syn.Description = "Whenever you apply " + chainStatus + ": 60% chance of an {18}% ATK strike and +{4} rage; +{25}% damage vs " + chainStatus + "; +{12} status chance.";
            }
            else
            {
                syn.Triggers.Add(Cond("skill", 30));
                syn.Stats.Add(new StatMod(StatId.RageGain, 15));
                syn.Description = "Skill damage +{30}%; +{15} rage gain.";
            }

            // 4A signature: a new battle opener — barrages, overheal shields or evasive counters by kind.
            var sig = NewPartSkill(d, 3, true, "Awakened " + kw, Rarity.Mythic, f, "");
            PartFamilyStats(sig, twin, 1.4);
            switch (d.Kind)
            {
                case PartKind.Body:
                    sig.Triggers.Add(new SkillTrigger { When = TriggerKind.OnTurnStart, Effect = EffectKind.Heal, Target = TargetKind.Self, Value = 4, Param = "overheal_shield" });
                    sig.Triggers.AddRange(PartPayload(f, TriggerKind.OnDamaged, 40, 1.5, TargetKind.Enemy, 1));
                    sig.Description = "Heal {4}% every turn; overheal becomes a shield. Taking damage may " + FamilyEffectText(f) + "."; break;
                case PartKind.Legs:
                    sig.Triggers.Add(new SkillTrigger { When = TriggerKind.OnDodge, Effect = EffectKind.Shield, Target = TargetKind.Self, Value = 5 });
                    sig.Triggers.AddRange(PartPayload(twin, TriggerKind.OnDodge, 100, 1.5, TargetKind.AllEnemies));
                    sig.Stats.Add(new StatMod(StatId.Dodge, 6));
                    sig.Description = "Dodges grant a {5}% shield and " + FamilyEffectText(twin) + " on all enemies; +{6} dodge."; break;
                case PartKind.Tail:
                    sig.Triggers.Add(new SkillTrigger { When = TriggerKind.OnRoundStart, Effect = EffectKind.BuffStat, Target = TargetKind.Self, Stat = new StatMod(StatId.Speed, 4), Duration = 0 });
                    sig.Triggers.AddRange(PartPayload(twin, TriggerKind.OnRoundStart, 100, 1.2, TargetKind.AllEnemies, 1));
                    sig.Description = "Gain speed every round; every other round " + FamilyEffectText(twin) + " on all enemies."; break;
                default:
                    // (was a drone summon: drones are gone) an opening barrage on every enemy
                    sig.Triggers.Add(new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.Damage, Target = TargetKind.AllEnemies, Value = 45 });
                    sig.Triggers.AddRange(PartPayload(f, TriggerKind.OnBattleStart, 100, 1.8, TargetKind.AllEnemies));
                    sig.Description = "Battle start: a {45}% ATK " + fw.ToLowerInvariant() + " barrage that " + FamilyEffectText(f) + " on every enemy."; break;
            }

            // 5A ultimate: a last stand that turns a losing battle (once, below 30% HP) plus a permanent skill-damage hook.
            var ult = NewPartSkill(d, 4, true, fw + " Ascension", Rarity.Mythic, f,
                "Below 30% HP (once): heal {30}%, {150}% ATK true damage to all enemies, " + FamilyEffectText(f) + " on all; skill damage +{25}%.");
            ult.Triggers.Add(new SkillTrigger { When = TriggerKind.OnLowHp, Effect = EffectKind.Heal, Target = TargetKind.Self, Value = 30 });
            ult.Triggers.Add(new SkillTrigger { When = TriggerKind.OnLowHp, Effect = EffectKind.TrueDamage, Target = TargetKind.AllEnemies, Value = 150 });
            ult.Triggers.AddRange(PartPayload(f, TriggerKind.OnLowHp, 100, 3.0, TargetKind.AllEnemies));
            ult.Triggers.Add(Cond("skill", 25));
        }

        private static SkillTrigger Cond(string condition, double pct) =>
            new SkillTrigger { When = TriggerKind.Passive, Effect = EffectKind.BuffStat, Param = "cond:" + condition, Value = pct };

        private static SkillTrigger St(TriggerKind when, double chance, StatusId status, int stacks, TargetKind target, int duration) =>
            new SkillTrigger { When = when, Chance = chance, Effect = EffectKind.ApplyStatus, Status = status, Stacks = stacks, Target = target, Duration = duration };
    }
}
