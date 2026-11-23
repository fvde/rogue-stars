using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>
    /// Laboratory tree (Run workstream): 38 nodes in four branches (offense, defense, elements, command). Skill nodes put
    /// skills into the run draft pool and upgrade them; stat nodes give +1…+10% style bonuses to the loadout. Stat and skill
    /// nodes share the branches, so the player chooses between raw stats and new skills; deeper rows need a node of the row
    /// above at level 2 (any), capstones need two nodes (all). Pacing: <see cref="LabTuningDef"/> (Data + exponential timers).
    /// </summary>
    public sealed partial class ContentDb
    {
        public readonly Dictionary<string, LabResearchDef> LabResearch = new Dictionary<string, LabResearchDef>();
        public readonly LabTuningDef LabTuning = new LabTuningDef();

        public LabResearchDef LabNode(string id) => id != null && LabResearch.TryGetValue(id, out var d) ? d : null;

        private void RunBuildLabSkills()
        {
            const string Off = "offense", Def = "defense", Ele = "elements", Cmd = "command";
            LabPrereq[] Any(params string[] ids) { var l = new LabPrereq[ids.Length]; for (int i = 0; i < ids.Length; i++) l[i] = new LabPrereq(ids[i], 2); return l; }

            // ---- offense ----
            RunLab("res_eagle_eye", "Eagle Eye", true, SkillFamily.Crit, Rarity.Rare, Off, 1, null, false, (b, lv) =>
                b.Desc("+crit chance and crit damage.").Stat(StatId.CritChance, 4 + 4 * lv).Stat(StatId.CritDamage, 10 * lv));
            LabStat("lab_atk_1", "Servo Muscles", Off, 1, StatId.Atk, true, 1, null, false);
            RunLab("res_chain_strikes", "Chain Strikes", true, SkillFamily.Combo, Rarity.Rare, Off, 2, Any("res_eagle_eye", "lab_atk_1"), false, (b, lv) =>
                b.Desc("+combo chance.").Stat(StatId.ComboChance, 5 + 5 * lv));
            LabStat("lab_crit_chance", "Targeting Lenses", Off, 2, StatId.CritChance, false, 1, Any("res_eagle_eye", "lab_atk_1"), false);
            LabStat("lab_rage_gain", "Adrenal Pump", Off, 2, StatId.RageGain, false, 2, Any("res_eagle_eye", "lab_atk_1"), false);
            RunLab("res_bloodletting", "Bloodletting", false, SkillFamily.Bleed, Rarity.Rare, Off, 3, Any("res_chain_strikes", "lab_crit_chance", "lab_rage_gain"), false, (b, lv) =>
                b.Desc("Rage attacks cause heavy bleed.").Status(TriggerKind.OnRageAttack, 100, StatusId.Bleed, 1 + lv));
            LabStat("lab_crit_damage", "Fracture Analysis", Off, 3, StatId.CritDamage, false, 2, Any("res_chain_strikes", "lab_crit_chance", "lab_rage_gain"), false);
            LabStat("lab_combo", "Reflex Loop", Off, 3, StatId.ComboChance, false, 1, Any("res_chain_strikes", "lab_crit_chance", "lab_rage_gain"), false);
            LabStat("lab_atk_2", "Hyper Servos", Off, 4, StatId.Atk, true, 1, Any("res_bloodletting", "lab_crit_damage", "lab_combo"), false);
            LabStat("lab_boss_damage", "Titan Studies", Off, 5, StatId.BossDamage, false, 2,
                new[] { new LabPrereq("lab_atk_2", 3), new LabPrereq("lab_elemental_mastery", 2) }, true);

            // ---- defense ----
            RunLab("res_second_wind", "Second Wind", true, SkillFamily.None, Rarity.Rare, Def, 1, null, false, (b, lv) =>
                b.Desc("Below 30% HP: heal.").On(TriggerKind.OnLowHp, EffectKind.Heal, 15 + 10 * lv, TargetKind.Self));
            LabStat("lab_hp_1", "Dense Tissue", Def, 1, StatId.Hp, true, 1, null, false);
            RunLab("res_retaliation", "Retaliation", false, SkillFamily.Counter, Rarity.Rare, Def, 2, Any("res_second_wind", "lab_hp_1"), false, (b, lv) =>
                b.Desc("+counter chance, counters may stun.").Stat(StatId.CounterChance, 6 * lv).Status(TriggerKind.OnCounter, 10 * lv, StatusId.Stun));
            LabStat("lab_def_1", "Chitin Plating", Def, 2, StatId.Def, true, 1, Any("res_second_wind", "lab_hp_1"), false);
            RunLab("res_bulwark", "Bulwark Protocol", false, SkillFamily.Counter, Rarity.Epic, Def, 3, Any("res_retaliation", "lab_def_1"), false, (b, lv) =>
                b.Desc("Battle start: a shield; parries may refresh it.").On(TriggerKind.OnBattleStart, EffectKind.Shield, 6 + 4 * lv, TargetKind.Self)
                 .On(TriggerKind.OnParry, EffectKind.Shield, 2 * lv, TargetKind.Self, 50));
            LabStat("lab_dodge", "Blink Glands", Def, 3, StatId.Dodge, false, 1, Any("res_retaliation", "lab_def_1"), false);
            LabStat("lab_counter", "Spring Joints", Def, 3, StatId.CounterChance, false, 1, Any("res_retaliation", "lab_def_1"), false);
            LabStat("lab_shield_power", "Barrier Theory", Def, 3, StatId.ShieldPower, false, 2, Any("res_retaliation", "lab_def_1"), false);
            LabStat("lab_hp_2", "Regenerative Mesh", Def, 4, StatId.Hp, true, 1, Any("res_bulwark", "lab_dodge", "lab_counter", "lab_shield_power"), false);
            LabStat("lab_def_2", "Neutron Shell", Def, 4, StatId.Def, true, 1, Any("res_bulwark", "lab_dodge", "lab_counter", "lab_shield_power"), false);
            LabStat("lab_damage_reduction", "Impact Dampers", Def, 5, StatId.DamageReduction, false, 1,
                new[] { new LabPrereq("lab_hp_2", 3), new LabPrereq("lab_alien_power_2", 2) }, true);

            // ---- elements ----
            RunLab("res_burning_soul", "Burning Soul", false, SkillFamily.Fire, Rarity.Epic, Ele, 1, null, false, (b, lv) =>
                b.Desc("Battle start: burn all enemies.").Status(TriggerKind.OnBattleStart, 100, StatusId.Burn, 1 + lv, TargetKind.AllEnemies));
            RunLab("res_frost_nova", "Frost Nova", false, SkillFamily.Ice, Rarity.Epic, Ele, 1, null, false, (b, lv) =>
                b.Desc("Rage attacks may freeze all enemies.").Status(TriggerKind.OnRageAttack, 25 + 15 * lv, StatusId.Freeze, 1, TargetKind.AllEnemies));
            RunLab("res_overload", "Overload", false, SkillFamily.Lightning, Rarity.Epic, Ele, 2, Any("res_burning_soul", "res_frost_nova"), false, (b, lv) =>
                b.Desc("Crits shock all enemies.").Status(TriggerKind.OnCrit, 50 + 25 * lv, StatusId.Shock, 1, TargetKind.AllEnemies));
            LabElementStat("lab_thermal_mastery", "Thermal Mastery", 2, new[] { StatId.FireDamage, StatId.IceDamage }, Any("res_burning_soul", "res_frost_nova"));
            LabElementStat("lab_volatile_mastery", "Volatile Mastery", 2, new[] { StatId.LightningDamage, StatId.PoisonDamage, StatId.BleedDamage }, Any("res_burning_soul", "res_frost_nova"));
            RunLab("res_toxic_cloud", "Toxic Cloud", false, SkillFamily.Poison, Rarity.Rare, Ele, 3, Any("res_overload", "lab_thermal_mastery", "lab_volatile_mastery"), false, (b, lv) =>
                b.Desc("Dodging releases poison.").Status(TriggerKind.OnDodge, 100, StatusId.Poison, 2 * lv, TargetKind.AllEnemies).Stat(StatId.Dodge, 3 * lv));
            LabStat("lab_status_chance", "Catalyst Glands", Ele, 3, StatId.StatusChance, false, 2, Any("res_overload", "lab_thermal_mastery", "lab_volatile_mastery"), false);
            LabElementStat("lab_elemental_mastery", "Elemental Mastery", 4, new[] { StatId.FireDamage, StatId.IceDamage, StatId.LightningDamage, StatId.PoisonDamage, StatId.BleedDamage },
                Any("res_toxic_cloud", "lab_status_chance"));
            RunLab("res_elemental_storm", "Elemental Storm", false, SkillFamily.None, Rarity.Legendary, Ele, 5,
                new[] { new LabPrereq("lab_elemental_mastery", 3), new LabPrereq("lab_crit_damage", 3) }, true, (b, lv) =>
                b.Desc("Every other round: burn, shock and poison all enemies.")
                 .Status(TriggerKind.OnRoundStart, 30 + 10 * lv, StatusId.Burn, 1, TargetKind.AllEnemies)
                 .Status(TriggerKind.OnRoundStart, 30 + 10 * lv, StatusId.Shock, 1, TargetKind.AllEnemies)
                 .Status(TriggerKind.OnRoundStart, 30 + 10 * lv, StatusId.Poison, 1 + lv / 2, TargetKind.AllEnemies));

            // ---- command (squad, mount, loot) ----
            RunLab("res_pack_tactics", "Pack Tactics", false, SkillFamily.AlienSynergy, Rarity.Epic, Cmd, 1, null, false, (b, lv) =>
                b.Desc("Aliens +AlienPower; allies gain rage at battle start.").Stat(StatId.AlienPower, 15 * lv)
                 .On(TriggerKind.OnBattleStart, EffectKind.GainRage, 25 * lv, TargetKind.AllAllies));
            LabStat("lab_alien_power_1", "Hive Linguistics", Cmd, 1, StatId.AlienPower, true, 1, null, false);
            RunLab("res_treasure_sense", "Treasure Sense", false, SkillFamily.None, Rarity.Epic, Cmd, 2, Any("res_pack_tactics", "lab_alien_power_1"), false, (b, lv) =>
                b.Desc("More pending loot on completion.").Board(TriggerKind.OnRunStart, EffectKind.ExtraLoot, 10 * lv));
            LabStat("lab_loot_luck", "Salvage Algorithms", Cmd, 2, StatId.LootLuck, false, 2, Any("res_pack_tactics", "lab_alien_power_1"), false);
            RunLab("res_lucky_dice", "Lucky Dice", false, SkillFamily.None, Rarity.Legendary, Cmd, 3, Any("res_treasure_sense", "lab_loot_luck"), false, (b, lv) =>
                b.Desc("Rolls may grant an extra die next roll.").Board(TriggerKind.OnRoll, EffectKind.ExtraDice, 1, null, 10 + 10 * lv));
            LabStat("lab_mount_power", "Hangar Tuning", Cmd, 3, StatId.MountPower, true, 1, Any("res_treasure_sense", "lab_loot_luck"), false);
            LabStat("lab_alien_power_2", "Symbiont Bond", Cmd, 4, StatId.AlienPower, true, 1, Any("res_lucky_dice", "lab_mount_power"), false);
            RunLab("res_drone_bay", "Drone Bay", false, SkillFamily.AlienSynergy, Rarity.Legendary, Cmd, 4, Any("res_lucky_dice", "lab_mount_power"), false, (b, lv) =>
                b.Desc("Battle start: launch a combat drone.").On(TriggerKind.OnBattleStart, EffectKind.Summon, 20 + 8 * lv, TargetKind.Self, 100, 0, "drone"));
        }

        /// <summary>Research levels per laboratory node.</summary>
        public const int LabLevels = 5;

        private LabResearchDef NewLabNode(string id, string name, LabNodeKind kind, string branch, int tier, LabPrereq[] requires, bool requireAll, bool isDefault)
        {
            var def = new LabResearchDef { Id = id, Name = name, Kind = kind, Branch = branch, Tier = tier, RequireAll = requireAll, DefaultResearched = isDefault };
            if (requires != null) def.Requires.AddRange(requires);
            LabResearch[def.Id] = def;
            return def;
        }

        /// <summary>Skill node: level 1 unlocks the skill into the draft pool, each level swaps in an upgraded version.</summary>
        private void RunLab(string researchId, string name, bool isDefault, SkillFamily family, Rarity rarity, string branch, int tier,
            LabPrereq[] requires, bool requireAll, Action<RunSkillBuilder, int> fill)
        {
            var def = NewLabNode(researchId, name, LabNodeKind.Skill, branch, tier, requires, requireAll, isDefault);
            string baseId = "lab_" + researchId.Substring(4);
            string[] numerals = { "", " II", " III", " IV", " V" };
            for (int lv = 1; lv <= LabLevels; lv++)
            {
                string sid = lv == 1 ? baseId : baseId + "_" + lv;
                // the tier is fixed per skill (owner decision): research levels raise its numbers, never its tier
                var b = RunSkill(sid, name + numerals[lv - 1], family, rarity, SkillSource.Laboratory);
                fill(b, lv);
                def.Levels.Add(new LabLevelDef { SkillId = sid });
            }
            def.Description = Skills[baseId].Description;
        }

        /// <summary>Stat node: level n gives n × <paramref name="step"/> (percent or flat points) of one stat.</summary>
        private void LabStat(string id, string name, string branch, int tier, StatId stat, bool percent, double step, LabPrereq[] requires, bool requireAll)
        {
            var def = NewLabNode(id, name, LabNodeKind.Stat, branch, tier, requires, requireAll, false);
            for (int lv = 1; lv <= LabLevels; lv++)
                def.Levels.Add(new LabLevelDef { Stats = { percent ? new StatMod(stat, 0, step * lv) : new StatMod(stat, step * lv) } });
            def.Description = "+" + step + (percent ? "% " : " ") + stat + " per level (max +" + step * LabLevels + ").";
        }

        /// <summary>Element stat node: +2% per level to several element damage stats.</summary>
        private void LabElementStat(string id, string name, int tier, StatId[] stats, LabPrereq[] requires)
        {
            var def = NewLabNode(id, name, LabNodeKind.Stat, "elements", tier, requires, false, false);
            for (int lv = 1; lv <= LabLevels; lv++)
            {
                var level = new LabLevelDef();
                foreach (var st in stats) level.Stats.Add(new StatMod(st, 0, 2 * lv));
                def.Levels.Add(level);
            }
            def.Description = "+2% " + string.Join("/", Array.ConvertAll(stats, x => x.ToString())) + " per level (max +10%).";
        }
    }
}
