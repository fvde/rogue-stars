using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    /// <summary>
    /// Laboratory tree (Run workstream): 36 nodes in four branches (offense, defense, elements, command). Skill nodes put
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
            LabPrereq[] Pkg(string id) => new[] { new LabPrereq(id, 1) };

            // ---- signature packages (signatures pass): each unlocks a synergy for every run: its signature, its 2 Common +
            // 2 Legendary run skills and its Mythics. Fire, Radiation and Bleed are open from the start. A chain: Combo comes with the
            // Laboratory, each next one needs the previous package, a stat node at level 2 and its own level unlock (GDD §16:
            // package_ice … package_time; the Alien package's level comes after Aliens, Command needs a squad). That synergy's lab
            // skill nodes (Chain Strikes, Frost Nova, Overload, Knockout, Bulwark, Pack Tactics) need its package.
            LabPackage("pkg_combo", "Combo Package", SkillFamily.Combo, "sig_combo", 1, null);
            LabPackage("pkg_ice", "Ice Package", SkillFamily.Ice, "sig_ice", 2, new[] { new LabPrereq("pkg_combo", 1), new LabPrereq("lab_atk_1", 2) })
                .RequiresFeature = Features.PackageIce;
            LabPackage("pkg_lightning", "Lightning Package", SkillFamily.Lightning, "sig_lightning", 3, new[] { new LabPrereq("pkg_ice", 1), new LabPrereq("lab_hp_1", 2) })
                .RequiresFeature = Features.PackageLightning;
            LabPackage("pkg_counter", "Counter Package", SkillFamily.Counter, "sig_counter", 4, new[] { new LabPrereq("pkg_lightning", 1), new LabPrereq("lab_def_1", 2) })
                .RequiresFeature = Features.PackageCounter;
            LabPackage("pkg_alien", "Alien Package", SkillFamily.AlienSynergy, "sig_alien", 5, new[] { new LabPrereq("pkg_counter", 1), new LabPrereq("lab_alien_power_1", 2) })
                .RequiresFeature = Features.PackageAlien;
            // skill redesign (R13): Time comes last in the chain (after Alien), with a defense stat node at level 2
            LabPackage("pkg_time", "Time Package", SkillFamily.Time, "sig_time", 6, new[] { new LabPrereq("pkg_alien", 1), new LabPrereq("lab_def_1", 2) })
                .RequiresFeature = Features.PackageTime;

            // ---- offense ----
            RunLab("res_eagle_eye", "Eagle Eye", true, SkillFamily.None, Rarity.Rare, Off, 1, null, false, (b, lv) =>
                b.Desc("+crit chance and crit damage.").Stat(StatId.CritChance, 8 + 2 * lv).Stat(StatId.CritDamage, 10 + 5 * lv));
            LabStat("lab_atk_1", "Servo Muscles", Off, 1, StatId.Atk, true, 1, null, false);
            RunLab("res_chain_strikes", "Chain Strikes", true, SkillFamily.Combo, Rarity.Rare, Off, 2, Pkg("pkg_combo"), true, (b, lv) =>
                b.Desc("+combo chance.").Stat(StatId.ComboChance, 5 + 5 * lv));
            LabStat("lab_crit_chance", "Targeting Lenses", Off, 2, StatId.CritChance, false, 1, Any("res_eagle_eye", "lab_atk_1"), false);
            LabStat("lab_rage_gain", "Adrenal Pump", Off, 2, StatId.RageGain, false, 2, Any("res_eagle_eye", "lab_atk_1"), false);
            RunLab("res_bloodletting", "Bloodletting", false, SkillFamily.Bleed, Rarity.Rare, Off, 3, Any("res_chain_strikes", "lab_crit_chance", "lab_rage_gain"), false, (b, lv) =>
                b.Desc("Rage attacks cut deep and bleed.").On(TriggerKind.OnRageAttack, EffectKind.Damage, 40 + 10 * lv, TargetKind.Enemy)
                 .Status(TriggerKind.OnRageAttack, 100, StatusId.Bleed, 2));
            LabStat("lab_crit_damage", "Fracture Analysis", Off, 3, StatId.CritDamage, false, 2, Any("res_chain_strikes", "lab_crit_chance", "lab_rage_gain"), false);
            LabStat("lab_combo", "Reflex Loop", Off, 3, StatId.ComboChance, false, 1, Any("res_chain_strikes", "lab_crit_chance", "lab_rage_gain"), false);
            LabStat("lab_atk_2", "Hyper Servos", Off, 4, StatId.Atk, true, 1, Any("res_bloodletting", "lab_crit_damage", "lab_combo"), false);
            LabStat("lab_boss_damage", "Titan Studies", Off, 5, StatId.BossDamage, false, 2,
                new[] { new LabPrereq("lab_atk_2", 3), new LabPrereq("lab_elemental_mastery", 2) }, true);
            // Mythic unlock (owner, 2026-11-26): a capstone beside Titan Studies. Shatter's battle-long stacks (owner: reset every
            // battle, max 15) count the worm's hits and its aliens' hits on a frozen enemy; offered only with a Freeze source.
            LabMythic("res_shatter", "Shatter", SkillFamily.Ice, Off, 5,
                new[] { new LabPrereq("lab_atk_2", 3), new LabPrereq("res_frost_nova", 3) }, b =>
                b.Needs("freeze").Desc("Every blow on the ice makes you stronger.")
                 .Add(Tg(TriggerKind.OnHit, EffectKind.BuffStat, target: TargetKind.Self, @if: "has:Freeze", param: "stack:15", stat: new StatMod(StatId.Atk, 0, 10)))
                 .Add(Tg(TriggerKind.OnSidekickHit, EffectKind.BuffStat, target: TargetKind.Self, @if: "has:Freeze", param: "stack:15", stat: new StatMod(StatId.Atk, 0, 10)))
                 .Text("Each hit by you or an alien on a frozen enemy: +{b0}% ATK this battle (max 15 stacks)."));

            // ---- defense ----
            RunLab("res_second_wind", "Second Wind", true, SkillFamily.None, Rarity.Rare, Def, 1, null, false, (b, lv) =>
                b.Desc("Below 30% HP: heal (overheal becomes a shield).").On(TriggerKind.OnLowHp, EffectKind.Heal, 20 + 5 * lv, TargetKind.Self, 100, 0, "overheal_shield"));
            LabStat("lab_hp_1", "Dense Tissue", Def, 1, StatId.Hp, true, 1, null, false);
            // CC pass (owner): Retaliation → Knockout (ids kept): only the stun, 10% → 15% → 20% in-run at research I, research V
            // 14% → 21% → 28%; one of the two Commons allowed to crowd-control (ContentValidationTests)
            RunLab("res_retaliation", "Knockout", false, SkillFamily.Counter, Rarity.Rare, Def, 2, Pkg("pkg_counter"), true, (b, lv) =>
                b.Desc("Counterattacks may stun.")
                 .Add(Tg(TriggerKind.OnCounter, EffectKind.ApplyStatus, status: StatusId.Stun, chance: 9 + lv, step: (9 + lv) / 2.0))
                 .Text("Counterattacks: {c0}% chance to stun."));
            LabStat("lab_def_1", "Chitin Plating", Def, 2, StatId.Def, true, 1, Any("res_second_wind", "lab_hp_1"), false);
            RunLab("res_bulwark", "Bulwark Protocol", false, SkillFamily.Counter, Rarity.Epic, Def, 3, Pkg("pkg_counter"), true, (b, lv) =>
                b.Desc("Battle start: a shield; counters may refresh it.").On(TriggerKind.OnBattleStart, EffectKind.Shield, 6 + 4 * lv, TargetKind.Self)
                 .On(TriggerKind.OnCounter, EffectKind.Shield, 2 * lv, TargetKind.Self, 50)); // parry removed: was on parry
            LabStat("lab_dodge", "Blink Glands", Def, 3, StatId.Dodge, false, 1, Any("res_retaliation", "lab_def_1"), false);
            LabStat("lab_counter", "Spring Joints", Def, 3, StatId.CounterChance, false, 1, Any("res_retaliation", "lab_def_1"), false);
            LabStat("lab_shield_power", "Barrier Theory", Def, 3, StatId.ShieldPower, false, 2, Any("res_retaliation", "lab_def_1"), false);
            LabStat("lab_hp_2", "Regenerative Mesh", Def, 4, StatId.Hp, true, 1, Any("res_bulwark", "lab_dodge", "lab_counter", "lab_shield_power"), false);
            LabStat("lab_def_2", "Neutron Shell", Def, 4, StatId.Def, true, 1, Any("res_bulwark", "lab_dodge", "lab_counter", "lab_shield_power"), false);
            LabStat("lab_damage_reduction", "Impact Dampers", Def, 5, StatId.DamageReduction, false, 1,
                new[] { new LabPrereq("lab_hp_2", 3), new LabPrereq("lab_alien_power_2", 2) }, true);

            // ---- elements ----
            RunLab("res_burning_soul", "Burning Soul", false, SkillFamily.Fire, Rarity.Epic, Ele, 1, null, false, (b, lv) =>
                b.Desc("Battle start: heat all enemies.").Status(TriggerKind.OnBattleStart, 100, StatusId.Heat, 1 + lv, TargetKind.AllEnemies));
            // CC pass (owner): a critical rage attack casts Frost Nova; research and in-run levels raise its damage, never the freeze
            RunLab("res_frost_nova", "Frost Nova", false, SkillFamily.Ice, Rarity.Epic, Ele, 1, Pkg("pkg_ice"), true, (b, lv) =>
                b.Desc("A critical rage attack casts Frost Nova.")
                 .Add(Tg(TriggerKind.OnRageAttack, EffectKind.ApplyStatus, target: TargetKind.AllEnemies, @if: "rage_crit", status: StatusId.Freeze, grow: GrowKind.Fixed))
                 .Add(Tg(TriggerKind.OnRageAttack, EffectKind.Damage, 50 + 10 * lv, TargetKind.AllEnemies, "rage_crit"))
                 .Text("If your rage attack crits, cast Frost Nova: Freeze all enemies and deal {v1}% ATK to each."));
            RunLab("res_overload", "Overload", false, SkillFamily.Lightning, Rarity.Epic, Ele, 2, Pkg("pkg_lightning"), true, (b, lv) =>
                b.Desc("Crits shock all enemies.").Status(TriggerKind.OnCrit, 40 + 10 * lv, StatusId.Shock, 1, TargetKind.AllEnemies));
            LabElementStat("lab_thermal_mastery", "Thermal Mastery", 2, new[] { StatId.FireDamage, StatId.IceDamage }, Any("res_burning_soul", "res_frost_nova"));
            LabElementStat("lab_volatile_mastery", "Volatile Mastery", 2, new[] { StatId.LightningDamage, StatId.PoisonDamage, StatId.BleedDamage }, Any("res_burning_soul", "res_frost_nova"));
            RunLab("res_toxic_cloud", "Fallout Cloud", false, SkillFamily.Poison, Rarity.Rare, Ele, 3, Any("res_overload", "lab_thermal_mastery", "lab_volatile_mastery"), false, (b, lv) =>
                b.Desc("Dodging releases radiation.").Status(TriggerKind.OnDodge, 100, StatusId.Poison, lv, TargetKind.AllEnemies).Stat(StatId.Dodge, 3 * lv));
            LabStat("lab_status_chance", "Catalyst Glands", Ele, 3, StatId.StatusChance, false, 2, Any("res_overload", "lab_thermal_mastery", "lab_volatile_mastery"), false);
            LabElementStat("lab_elemental_mastery", "Elemental Mastery", 4, new[] { StatId.FireDamage, StatId.IceDamage, StatId.LightningDamage, StatId.PoisonDamage, StatId.BleedDamage },
                Any("res_toxic_cloud", "lab_status_chance"));
            RunLab("res_elemental_storm", "Elemental Storm", false, SkillFamily.None, Rarity.Legendary, Ele, 5,
                new[] { new LabPrereq("lab_elemental_mastery", 3), new LabPrereq("lab_crit_damage", 3) }, true, (b, lv) =>
                b.Desc("Each round: may heat, shock and irradiate all enemies.")
                 .Status(TriggerKind.OnRoundStart, 30 + 10 * lv, StatusId.Heat, 1, TargetKind.AllEnemies)
                 .Status(TriggerKind.OnRoundStart, 30 + 10 * lv, StatusId.Shock, 1, TargetKind.AllEnemies)
                 .Status(TriggerKind.OnRoundStart, 30 + 10 * lv, StatusId.Poison, 1 + lv / 2, TargetKind.AllEnemies));
            // Mythic unlock (owner, 2026-11-26): a capstone beside Elemental Storm. Plague counts the stacks of each status the player
            // side inflicts while the run owns it (ActiveRun.PlagueStacks, run-scoped); every 20 of one status give every enemy of
            // the run's later battles one opening stack of it (RunSystem.PlagueOpening).
            LabMythic("res_plague", "Plague", SkillFamily.None, Ele, 5,
                new[] { new LabPrereq("lab_status_chance", 3), new LabPrereq("res_toxic_cloud", 3) }, b =>
                b.Desc("Every affliction spreads to the next fight.")
                 .Add(Tg(TriggerKind.Passive, EffectKind.Rule, 20, TargetKind.Self, param: "plague"))
                 .Text("Every {v0} stacks of a status you inflict: later enemies of this run begin with 1 more stack of it."));

            // ---- command (squad, mount, loot) ----
            RunLab("res_pack_tactics", "Pack Tactics", false, SkillFamily.AlienSynergy, Rarity.Epic, Cmd, 1, Pkg("pkg_alien"), true, (b, lv) =>
                b.Desc("Sidekicks +power; each sidekick action gives the worm rage.").Stat(StatId.AlienPower, 15 * lv)
                 .On(TriggerKind.OnSidekickAct, EffectKind.GainRage, 5 + 5 * lv, TargetKind.Worm));
            LabStat("lab_alien_power_1", "Hive Linguistics", Cmd, 1, StatId.AlienPower, false, 1, null, false);
            // Treasure Sense (loot %: it only touched the ~1 account-loot item a run carries) and Drone Bay (a drone summon) were
            // removed in the skill tuning pass; Lucky Dice moved up to row 2 so the branch keeps its shape.
            LabStat("lab_loot_luck", "Salvage Algorithms", Cmd, 2, StatId.LootLuck, false, 2, Any("res_pack_tactics", "lab_alien_power_1"), false);
            RunLab("res_lucky_dice", "Lucky Dice", false, SkillFamily.None, Rarity.Legendary, Cmd, 2, Any("res_pack_tactics", "lab_alien_power_1"), false, (b, lv) =>
                b.Desc("Rolls may grant an extra die next roll.").Board(TriggerKind.OnRoll, EffectKind.ExtraDice, 1, null, 10 + 10 * lv));
            LabStat("lab_mount_power", "Hangar Tuning", Cmd, 3, StatId.MountPower, false, 1, Any("res_lucky_dice", "lab_loot_luck"), false);
            LabStat("lab_alien_power_2", "Symbiont Bond", Cmd, 4, StatId.AlienPower, false, 1, Any("lab_mount_power"), false);
        }

        /// <summary>
        /// Package node (one level): unlocks <paramref name="family"/> for the run pool; its level 1 "skill" is the signature.
        /// Description: "Ice package: Chill + 7 skills".
        /// </summary>
        private LabResearchDef LabPackage(string id, string name, SkillFamily family, string signatureId, int tier, LabPrereq[] requires)
        {
            var def = NewLabNode(id, name, LabNodeKind.Package, "packages", tier, requires, true, false);
            def.Family = family;
            def.Levels.Add(new LabLevelDef { SkillId = signatureId });
            int n = 0;
            foreach (var s in Skills.Values) if (s.Family == family && s.Source == SkillSource.Run && s.Draftable) n++;
            def.Description = SkillText.FamilyName(family) + " package: " + Skills[signatureId].Name + " + " + n + " skills.";
            return def;
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

        /// <summary>
        /// Mythic node (owner, 2026-11-26): one research level puts a Mythic (Laboratory source) into the run's Mythic choice pool
        /// (RunDraft.IsMythicEligible via CombatLoadout.DraftPoolSkillIds); Mythics never level, so the node has one level, like a
        /// package. Capstone placement: two prerequisites at level 3 (all).
        /// </summary>
        private void LabMythic(string researchId, string name, SkillFamily family, string branch, int tier, LabPrereq[] requires, Action<RunSkillBuilder> fill)
        {
            var def = NewLabNode(researchId, name, LabNodeKind.Skill, branch, tier, requires, true, false);
            string sid = "lab_" + researchId.Substring(4);
            var b = RunSkill(sid, name, family, Rarity.Mythic, SkillSource.Laboratory);
            fill(b);
            def.Levels.Add(new LabLevelDef { SkillId = sid });
            def.Description = "Mythic: " + Skills[sid].Description;
        }

        /// <summary>Stat node: level n gives n × <paramref name="step"/> (percent or flat points) of one stat.</summary>
        private void LabStat(string id, string name, string branch, int tier, StatId stat, bool percent, double step, LabPrereq[] requires, bool requireAll)
        {
            var def = NewLabNode(id, name, LabNodeKind.Stat, branch, tier, requires, requireAll, false);
            for (int lv = 1; lv <= LabLevels; lv++)
                def.Levels.Add(new LabLevelDef { Stats = { percent ? new StatMod(stat, 0, step * lv) : new StatMod(stat, step * lv) } });
            string unit = percent || SkillText.IsPointStat(stat) ? "%" : "";
            def.Description = "+" + step + unit + " " + SkillText.StatName(stat) + " per level (max +" + step * LabLevels + unit + ").";
        }

        /// <summary>Element stat node: +2% per level to several element damage stats.</summary>
        private void LabElementStat(string id, string name, int tier, StatId[] stats, LabPrereq[] requires)
        {
            var def = NewLabNode(id, name, LabNodeKind.Stat, "elements", tier, requires, false, false);
            for (int lv = 1; lv <= LabLevels; lv++)
            {
                var level = new LabLevelDef();
                foreach (var st in stats) level.Stats.Add(new StatMod(st, 2 * lv)); // points (a % of a zero base would be nothing)
                def.Levels.Add(level);
            }
            def.Description = "+2% " + string.Join("/", Array.ConvertAll(stats, x => x.ToString())) + " per level (max +10%).";
        }
    }
}
