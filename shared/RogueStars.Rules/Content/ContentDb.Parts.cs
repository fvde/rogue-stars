using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>What kind of body part a definition is. Arm parts fit either arm slot.</summary>
    public enum PartKind { Head, Body, Tail, Legs, Arm }

    /// <summary>The six equipment slots of the worm.</summary>
    public enum PartSlot { Head, Body, Tail, Legs, LeftArm, RightArm }

    /// <summary>
    /// The 14-step part rarity ladder raised by merging (docs/design/parts.md). Mythic S and Mythic SS are rungs of the
    /// ladder (each unlocks a part skill); awakening is separate (<see cref="PartsConfig.AwakenCost"/>).
    /// </summary>
    public enum PartRarity
    {
        Common, Uncommon, Rare, Epic, Legendary,
        Mythic, Mythic1, Mythic2,
        MythicS, MythicS1, MythicS2,
        MythicSS, MythicSS1, MythicSS2,
    }

    [Serializable]
    public sealed class PartDef
    {
        public string Id;
        public string Name;
        public PartKind Kind;
        /// <summary>Attack style; only meaningful for arm parts.</summary>
        public AttackStyle Style;
        /// <summary>Family this part favours (its skills are built around it).</summary>
        public SkillFamily Family;
        /// <summary>Stats at Common, level 1 (scaled by rarity multiplier and Forge bonus).</summary>
        public List<StatMod> BaseStats = new List<StatMod>();
        /// <summary>Stats per level above 1 (scaled by rarity multiplier, not by Forge).</summary>
        public List<StatMod> StatsPerLevel = new List<StatMod>();
        /// <summary>
        /// The part's five skills (Source = Part); skill i is active from PartsConfig.SkillUnlockRarities[i]
        /// (Epic, Legendary, Mythic, Mythic S, Mythic SS).
        /// </summary>
        public List<string> SkillIds = new List<string>();

        /// <summary>Awakened version (Mythic+ parts, once): new name, stronger stats, five redesigned skills (same unlock rarities).</summary>
        public string AwakenedName;
        public List<StatMod> AwakenedBaseStats = new List<StatMod>();
        public List<StatMod> AwakenedStatsPerLevel = new List<StatMod>();
        public List<string> AwakenedSkillIds = new List<string>();
    }

    /// <summary>
    /// Merge recipe to reach a rarity step: the target part (one step below) plus materials.
    /// <see cref="SameDefCount"/> copies of the target's own def at <see cref="SameDefRarity"/> or higher, plus other parts
    /// (any def, any kind, each at <see cref="FodderMinRarity"/> or higher) worth at least <see cref="FodderPoints"/>
    /// fodder points (<see cref="PartRarityDef.FodderValue"/>; no superfluous material).
    /// </summary>
    [Serializable]
    public sealed class PartMergeRecipe
    {
        public int SameDefCount;
        public PartRarity SameDefRarity;
        public long FodderPoints;
        public PartRarity FodderMinRarity;
        /// <summary>Ore paid for the merge (Mythic and above; grows ~×1.8 per step).</summary>
        public long Ore;
    }

    [Serializable]
    public sealed class PartRarityDef
    {
        public PartRarity Rarity;
        public string Name;
        public double StatMultiplier;
        public int BaseLevelCap;
        /// <summary>How many Advancement Plate cap raises this rarity allows.</summary>
        public int MaxCapRaises;
        /// <summary>Rune slots granted by rarity alone (the Forge adds more, up to PartsConfig.MaxRuneSlots).</summary>
        public int RuneSlots;
        /// <summary>Ore granted on dismantle (in addition to the 100% level refund).</summary>
        public long DismantleOre;
        /// <summary>Recipe to reach this rarity from the previous step (null for Common).</summary>
        public PartMergeRecipe Merge;
        /// <summary>
        /// Fodder points this part is worth as merge material: everything that went into it (Common 1, ×3 per step up to
        /// Legendary = 81, then the cumulative recipe cost).
        /// </summary>
        public long FodderValue;
    }

    [Serializable]
    public sealed class ForgeStep
    {
        public long Diamonds;
        public long GearCores;
        /// <summary>Percent added to the part's base stats only (cumulative over steps).</summary>
        public double BonusPercent;
    }

    [Serializable]
    public sealed class RuneDef
    {
        public string Id;
        public string Name;
        public StatId Stat;
        /// <summary>True: value is a stat percent; false: flat.</summary>
        public bool IsPercent;
        /// <summary>Value at Common rarity; multiplied by PartsConfig.RuneRarityMultipliers.</summary>
        public double BaseValue;
    }

    [Serializable]
    public sealed class PartsConfig
    {
        public Dictionary<string, PartDef> Defs = new Dictionary<string, PartDef>();
        /// <summary>Indexed by (int)PartRarity.</summary>
        public List<PartRarityDef> Rarities = new List<PartRarityDef>();
        /// <summary>LevelOreCosts[L] / LevelGearCoreCosts[L]: cost from level L to L+1 (index 0 unused).</summary>
        public List<long> LevelOreCosts = new List<long>();
        public List<long> LevelGearCoreCosts = new List<long>();
        public int CapRaiseLevels = 5;
        /// <summary>Advancement Plates for the n-th cap raise (index = raises already done).</summary>
        public List<long> CapRaisePlates = new List<long>();
        public int MaxRuneSlots = 5;
        public PartRarity ForgeMinRarity = PartRarity.Epic;
        public List<ForgeStep> Forge = new List<ForgeStep>();
        /// <summary>Rarity at which each of a part's five skills becomes active (index = skill index).</summary>
        public List<PartRarity> SkillUnlockRarities = new List<PartRarity>();
        /// <summary>Awakening: Mythic or higher, once per part, for AwakenCost Awaken Cores.</summary>
        public PartRarity AwakenMinRarity = PartRarity.Mythic;
        public long AwakenCost = 50;
        /// <summary>Scale on the numbers of part skills (normal / awakened): the balance knob for part-skill power.</summary>
        public double SkillPowerScale = 0.5, AwakenedSkillPowerScale = 0.7;
        /// <summary>One Legendary part in fodder points (the unit of the recipe table in docs/design/parts.md).</summary>
        public long LegendaryFodder = 81;
        /// <summary>Quick-merge never produces parts above this rarity.</summary>
        public PartRarity QuickMergeMaxTarget = PartRarity.Legendary;

        public Dictionary<string, RuneDef> Runes = new Dictionary<string, RuneDef>();
        /// <summary>Indexed by (int)Rarity (Common..Mythic).</summary>
        public List<double> RuneRarityMultipliers = new List<double>();
        public Rarity RuneMaxRarity = Rarity.Mythic;
        public int RuneMergeCount = 3;

        public PartRarityDef RarityDef(int r) => Rarities[Math.Max(0, Math.Min(Rarities.Count - 1, r))];
        public int MaxLevel => LevelOreCosts.Count;
    }

    public sealed partial class ContentDb
    {
        /// <summary>Parts, rarity ladder, merge recipes, runes, Forge and awakening (Progression).</summary>
        public readonly PartsConfig PartsConfig = new PartsConfig();

        partial void BuildParts()
        {
            var p = PartsConfig;

            // ---- rarity ladder (docs/design/parts.md) ---------------------------------------------
            // Below Mythic every step takes any surplus of the step's rarity (3 → 1, any def and kind), so every drop ends
            // up as Legendary fodder. Mythic and above take Legendary+ fodder in Legendary-equivalents (LE = 81 points)
            // growing ~1.7× per step; Mythic, S, SS and SS+2 also need copies of the part's own def (Legendary or higher).
            long le = p.LegendaryFodder;
            // name, stat mult, base cap, cap raises, rune slots, dismantle ore, recipe
            AddPartRarity(PartRarity.Common, "Common", 1.0, 10, 0, 0, 50, null);
            AddPartRarity(PartRarity.Uncommon, "Uncommon", 1.3, 15, 0, 0, 100, PartRecipe(0, PartRarity.Common, 2, PartRarity.Common));
            AddPartRarity(PartRarity.Rare, "Rare", 1.7, 20, 1, 1, 250, PartRecipe(0, PartRarity.Uncommon, 6, PartRarity.Uncommon));
            AddPartRarity(PartRarity.Epic, "Epic", 2.4, 30, 1, 1, 600, PartRecipe(0, PartRarity.Rare, 18, PartRarity.Rare));
            AddPartRarity(PartRarity.Legendary, "Legendary", 3.6, 50, 2, 2, 2000, PartRecipe(0, PartRarity.Epic, 54, PartRarity.Epic));
            AddPartRarity(PartRarity.Mythic, "Mythic", 5.6, 70, 3, 3, 6000, PartRecipe(1, PartRarity.Legendary, 2 * le, PartRarity.Legendary));
            AddPartRarity(PartRarity.Mythic1, "Mythic+1", 6.4, 80, 3, 3, 9000, PartRecipe(0, PartRarity.Legendary, 8 * le, PartRarity.Legendary));
            AddPartRarity(PartRarity.Mythic2, "Mythic+2", 7.3, 90, 3, 3, 13000, PartRecipe(0, PartRarity.Legendary, 14 * le, PartRarity.Legendary));
            AddPartRarity(PartRarity.MythicS, "Mythic S", 8.8, 100, 4, 4, 20000, PartRecipe(2, PartRarity.Legendary, 24 * le, PartRarity.Legendary));
            AddPartRarity(PartRarity.MythicS1, "Mythic S+1", 9.8, 110, 4, 4, 28000, PartRecipe(0, PartRarity.Legendary, 40 * le, PartRarity.Legendary));
            AddPartRarity(PartRarity.MythicS2, "Mythic S+2", 11.0, 120, 5, 4, 38000, PartRecipe(0, PartRarity.Legendary, 70 * le, PartRarity.Legendary));
            AddPartRarity(PartRarity.MythicSS, "Mythic SS", 13.0, 130, 5, 5, 55000, PartRecipe(3, PartRarity.Legendary, 120 * le, PartRarity.Legendary));
            AddPartRarity(PartRarity.MythicSS1, "Mythic SS+1", 14.6, 140, 6, 5, 75000, PartRecipe(0, PartRarity.Legendary, 200 * le, PartRarity.Legendary));
            AddPartRarity(PartRarity.MythicSS2, "Mythic SS+2", 16.5, 150, 6, 5, 100000, PartRecipe(1, PartRarity.Legendary, 340 * le, PartRarity.Legendary));
            // Ore per merge from Mythic on: 4k, ×1.8 per step (Mythic SS+2 ≈ 440k).
            for (int r = (int)PartRarity.Mythic; r < p.Rarities.Count; r++)
                p.Rarities[r].Merge.Ore = (long)(Math.Round(4000 * Math.Pow(1.8, r - (int)PartRarity.Mythic) / 1000.0) * 1000);
            // Fodder value = everything that went into a part (Common 1; Legendary 81; then the cumulative recipe cost).
            for (int r = 0; r < p.Rarities.Count; r++)
            {
                var rd = p.Rarities[r];
                if (rd.Merge == null) { rd.FodderValue = 1; continue; }
                rd.FodderValue = p.Rarities[r - 1].FodderValue + rd.Merge.SameDefCount * p.Rarities[(int)rd.Merge.SameDefRarity].FodderValue + rd.Merge.FodderPoints;
            }

            // ---- levels, cap raises ----------------------------------------------------------------
            p.CapRaiseLevels = 5;
            p.CapRaisePlates.AddRange(new long[] { 60, 160, 320, 600, 1000, 1600 });
            int maxLevel = p.Rarities[p.Rarities.Count - 1].BaseLevelCap + 6 * p.CapRaiseLevels; // top base cap + all raises
            p.LevelOreCosts.Add(0); p.LevelGearCoreCosts.Add(0);
            for (int l = 1; l < maxLevel; l++)
            {
                p.LevelOreCosts.Add((long)Math.Round(30 * Math.Pow(l, 1.4)));
                p.LevelGearCoreCosts.Add(l < 10 ? 0 : (long)Math.Round(2 * Math.Pow(l - 9, 1.25)));
            }

            // ---- forge, skills, awakening ----------------------------------------------------------
            p.MaxRuneSlots = 5;
            p.ForgeMinRarity = PartRarity.Epic;
            p.Forge.Add(new ForgeStep { Diamonds = 300, GearCores = 400, BonusPercent = 10 });
            p.Forge.Add(new ForgeStep { Diamonds = 900, GearCores = 1200, BonusPercent = 15 });
            p.SkillUnlockRarities.AddRange(new[] { PartRarity.Epic, PartRarity.Legendary, PartRarity.Mythic, PartRarity.MythicS, PartRarity.MythicSS });
            p.AwakenMinRarity = PartRarity.Mythic;
            p.AwakenCost = 50;
            p.QuickMergeMaxTarget = PartRarity.Legendary;

            // ---- runes -----------------------------------------------------------------------------
            p.RuneRarityMultipliers.AddRange(new double[] { 1, 2.2, 4, 6.5, 10 });
            p.RuneMergeCount = 3;
            AddRuneDef("rune_vigor", "Vigor Rune", StatId.Hp, true, 2);
            AddRuneDef("rune_fury", "Fury Rune", StatId.Atk, true, 2);
            AddRuneDef("rune_bulwark", "Bulwark Rune", StatId.Def, true, 2.5);
            AddRuneDef("rune_precision", "Precision Rune", StatId.CritChance, false, 1);
            AddRuneDef("rune_ruin", "Ruin Rune", StatId.CritDamage, false, 4);
            AddRuneDef("rune_haste", "Haste Rune", StatId.Speed, false, 3);
            AddRuneDef("rune_echo", "Echo Rune", StatId.ComboChance, false, 1);
            AddRuneDef("rune_riposte", "Riposte Rune", StatId.CounterChance, false, 1);
            AddRuneDef("rune_mist", "Mist Rune", StatId.Dodge, false, 0.8);
            AddRuneDef("rune_leech", "Leech Rune", StatId.LifeSteal, false, 0.8);
            AddRuneDef("rune_ember", "Ember Rune", StatId.FireDamage, false, 2);
            AddRuneDef("rune_volt", "Volt Rune", StatId.LightningDamage, false, 2);

            // ---- part defs (30): id, name, kind, arm style, family, awakened name ---------------------------------------------------------------------
            AddPartDef("head_scanner_visor", "Scanner Visor", PartKind.Head, AttackStyle.Slam, SkillFamily.Crit, "Omniscope Visor");
            AddPartDef("head_ember_crown", "Ember Crown", PartKind.Head, AttackStyle.Slam, SkillFamily.Fire, "Sunfire Diadem");
            AddPartDef("head_frost_helm", "Frost Helm", PartKind.Head, AttackStyle.Slam, SkillFamily.Ice, "Winter King's Helm");
            AddPartDef("head_hive_antenna", "Hive Antenna", PartKind.Head, AttackStyle.Slam, SkillFamily.AlienSynergy, "Overmind Antenna");
            AddPartDef("head_duelist_mask", "Duelist Mask", PartKind.Head, AttackStyle.Slam, SkillFamily.Counter, "Grandmaster Mask");
            AddPartDef("body_plated_shell", "Plated Shell", PartKind.Body, AttackStyle.Slam, SkillFamily.Counter, "Bastion Carapace");
            AddPartDef("body_reactor_core", "Reactor Core", PartKind.Body, AttackStyle.Slam, SkillFamily.Lightning, "Singularity Core");
            AddPartDef("body_toxic_tank", "Toxic Tank", PartKind.Body, AttackStyle.Slam, SkillFamily.Poison, "Plague Reactor");
            AddPartDef("body_glacier_plate", "Glacier Plate", PartKind.Body, AttackStyle.Slam, SkillFamily.Ice, "Permafrost Aegis");
            AddPartDef("body_spiked_mail", "Spiked Mail", PartKind.Body, AttackStyle.Slam, SkillFamily.Bleed, "Thornstorm Mail");
            AddPartDef("tail_rocket_booster", "Rocket Booster", PartKind.Tail, AttackStyle.Slam, SkillFamily.Combo, "Hyperdrive Thruster");
            AddPartDef("tail_stinger", "Stinger Tail", PartKind.Tail, AttackStyle.Slam, SkillFamily.Poison, "Widowmaker Stinger");
            AddPartDef("tail_coil_spring", "Coil Spring", PartKind.Tail, AttackStyle.Slam, SkillFamily.Lightning, "Tesla Coilwhip");
            AddPartDef("tail_razor_fin", "Razor Fin", PartKind.Tail, AttackStyle.Slam, SkillFamily.Bleed, "Crimson Scythe Fin");
            AddPartDef("tail_comet_tail", "Comet Tail", PartKind.Tail, AttackStyle.Slam, SkillFamily.Fire, "Meteor Wake");
            AddPartDef("legs_hover_treads", "Hover Treads", PartKind.Legs, AttackStyle.Slam, SkillFamily.Combo, "Phantom Treads");
            AddPartDef("legs_spider_legs", "Spider Legs", PartKind.Legs, AttackStyle.Slam, SkillFamily.Crit, "Arachnid Executor");
            AddPartDef("legs_magma_stompers", "Magma Stompers", PartKind.Legs, AttackStyle.Slam, SkillFamily.Fire, "Volcano Striders");
            AddPartDef("legs_grav_boots", "Grav Boots", PartKind.Legs, AttackStyle.Slam, SkillFamily.Counter, "Event Horizon Boots");
            AddPartDef("legs_cryo_skates", "Cryo Skates", PartKind.Legs, AttackStyle.Slam, SkillFamily.Ice, "Glacial Phase Skates");
            AddPartDef("arm_slam_hammer", "Slam Hammer", PartKind.Arm, AttackStyle.Slam, SkillFamily.Counter, "Worldbreaker Maul");
            AddPartDef("arm_mantis_claw", "Mantis Claw", PartKind.Arm, AttackStyle.Claw, SkillFamily.Bleed, "Reaper Mantis");
            AddPartDef("arm_plasma_blaster", "Plasma Blaster", PartKind.Arm, AttackStyle.Blaster, SkillFamily.Fire, "Nova Lance");
            AddPartDef("arm_drill_lance", "Drill Lance", PartKind.Arm, AttackStyle.Drill, SkillFamily.Crit, "Starpiercer");
            AddPartDef("arm_tesla_whip", "Tesla Whip", PartKind.Arm, AttackStyle.Whip, SkillFamily.Lightning, "Thunderlord Lash");
            AddPartDef("arm_rail_cannon", "Rail Cannon", PartKind.Arm, AttackStyle.Cannon, SkillFamily.Crit, "Annihilator Rail");
            AddPartDef("arm_venom_claw", "Venom Claw", PartKind.Arm, AttackStyle.Claw, SkillFamily.Poison, "Viper Queen's Talon");
            AddPartDef("arm_frost_cannon", "Frost Cannon", PartKind.Arm, AttackStyle.Cannon, SkillFamily.Ice, "Absolute Zero Cannon");
            AddPartDef("arm_ripper_drill", "Ripper Drill", PartKind.Arm, AttackStyle.Drill, SkillFamily.Combo, "Maelstrom Drill");
            AddPartDef("arm_hive_whip", "Hive Whip", PartKind.Arm, AttackStyle.Whip, SkillFamily.AlienSynergy, "Swarmmother Lash");
        }

        private static PartMergeRecipe PartRecipe(int same, PartRarity sameMinRarity, long fodderPoints, PartRarity fodderMinRarity) =>
            new PartMergeRecipe { SameDefCount = same, SameDefRarity = sameMinRarity, FodderPoints = fodderPoints, FodderMinRarity = fodderMinRarity };

        private void AddPartRarity(PartRarity r, string name, double mult, int cap, int raises, int slots, long ore, PartMergeRecipe merge)
        {
            if ((int)r != PartsConfig.Rarities.Count) throw new InvalidOperationException("rarities must be added in order");
            PartsConfig.Rarities.Add(new PartRarityDef { Rarity = r, Name = name, StatMultiplier = mult, BaseLevelCap = cap, MaxCapRaises = raises, RuneSlots = slots, DismantleOre = ore, Merge = merge });
        }

        private void AddRuneDef(string id, string name, StatId stat, bool percent, double value) =>
            PartsConfig.Runes[id] = new RuneDef { Id = id, Name = name, Stat = stat, IsPercent = percent, BaseValue = value };

        private void AddPartDef(string id, string name, PartKind kind, AttackStyle style, SkillFamily family, string awakenedName)
        {
            var d = new PartDef { Id = id, Name = name, Kind = kind, Style = style, Family = family };
            switch (kind)
            {
                case PartKind.Head:
                    d.BaseStats.Add(new StatMod(StatId.Hp, 60)); d.BaseStats.Add(new StatMod(StatId.CritChance, 1));
                    d.StatsPerLevel.Add(new StatMod(StatId.Hp, 8));
                    break;
                case PartKind.Body:
                    d.BaseStats.Add(new StatMod(StatId.Hp, 120)); d.BaseStats.Add(new StatMod(StatId.Def, 8));
                    d.StatsPerLevel.Add(new StatMod(StatId.Hp, 14)); d.StatsPerLevel.Add(new StatMod(StatId.Def, 1));
                    break;
                case PartKind.Tail:
                    d.BaseStats.Add(new StatMod(StatId.Hp, 50)); d.BaseStats.Add(new StatMod(StatId.Atk, 6)); d.BaseStats.Add(new StatMod(StatId.Speed, 2));
                    d.StatsPerLevel.Add(new StatMod(StatId.Hp, 6)); d.StatsPerLevel.Add(new StatMod(StatId.Atk, 0.8));
                    break;
                case PartKind.Legs:
                    d.BaseStats.Add(new StatMod(StatId.Def, 10)); d.BaseStats.Add(new StatMod(StatId.Speed, 4)); d.BaseStats.Add(new StatMod(StatId.Dodge, 1));
                    d.StatsPerLevel.Add(new StatMod(StatId.Def, 1.2));
                    break;
                default:
                    d.BaseStats.Add(new StatMod(StatId.Atk, 15));
                    d.StatsPerLevel.Add(new StatMod(StatId.Atk, 2));
                    switch (style)
                    {
                        case AttackStyle.Slam: d.BaseStats.Add(new StatMod(StatId.CounterChance, 2)); break;
                        case AttackStyle.Claw: d.BaseStats.Add(new StatMod(StatId.CritChance, 1)); break;
                        case AttackStyle.Blaster: d.BaseStats.Add(new StatMod(StatId.Atk, 2)); break;
                        case AttackStyle.Drill: d.BaseStats.Add(new StatMod(StatId.CritDamage, 5)); break;
                        case AttackStyle.Whip: d.BaseStats.Add(new StatMod(StatId.ComboChance, 2)); break;
                        case AttackStyle.Cannon: d.BaseStats.Add(new StatMod(StatId.Atk, 4)); break;
                    }
                    break;
            }

            // Awakened version: ×1.4 stats plus the family's signature stat (PartAwakenedSignatureStat).
            d.AwakenedName = awakenedName;
            foreach (var m in d.BaseStats) d.AwakenedBaseStats.Add(new StatMod(m.Stat, Math.Round(m.Flat * 1.4, 2), Math.Round(m.Percent * 1.4, 2)));
            foreach (var m in d.StatsPerLevel) d.AwakenedStatsPerLevel.Add(new StatMod(m.Stat, Math.Round(m.Flat * 1.4, 2), Math.Round(m.Percent * 1.4, 2)));
            d.AwakenedBaseStats.Add(PartAwakenedSignatureStat(family));
            BuildPartSkills(d);
            PartsConfig.Defs[id] = d;
        }

        /// <summary>
        /// Builds a non-draftable meta skill for a family at a given power (1 = baseline). Shared by part,
        /// hero and mount content so each family's identity reads the same everywhere.
        /// </summary>
        internal static SkillDef ProgFamilySkill(string id, string name, SkillFamily family, Rarity rarity, SkillSource source, double power, string upgradesFrom)
        {
            var s = new SkillDef { Id = id, Name = name, Family = family, Rarity = rarity, Source = source, Draftable = false, UpgradesFrom = upgradesFrom };
            double v = power;
            switch (family)
            {
                case SkillFamily.Fire:
                    s.Stats.Add(new StatMod(StatId.FireDamage, 0, Math.Round(5 * v, 1)));
                    s.Triggers.Add(new SkillTrigger { When = TriggerKind.OnHit, Chance = Math.Min(100, Math.Round(15 * v)), Effect = EffectKind.ApplyStatus, Status = StatusId.Burn, Stacks = 1 + (int)(v / 1.5), Duration = 3 });
                    s.Description = "Hits can set the enemy on fire.";
                    break;
                case SkillFamily.Ice:
                    s.Stats.Add(new StatMod(StatId.IceDamage, 0, Math.Round(5 * v, 1)));
                    s.Triggers.Add(new SkillTrigger { When = TriggerKind.OnHit, Chance = Math.Min(100, Math.Round(10 * v)), Effect = EffectKind.ApplyStatus, Status = StatusId.Freeze, Stacks = 1, Duration = 1 + (int)(v / 2) });
                    s.Description = "Hits can freeze the enemy.";
                    break;
                case SkillFamily.Lightning:
                    s.Stats.Add(new StatMod(StatId.LightningDamage, 0, Math.Round(5 * v, 1)));
                    s.Triggers.Add(new SkillTrigger { When = TriggerKind.OnAttack, Chance = Math.Min(100, Math.Round(20 * v)), Effect = EffectKind.Damage, Target = TargetKind.RandomEnemy, Value = Math.Round(30 * v) });
                    s.Description = "Attacks can arc lightning to a random enemy.";
                    break;
                case SkillFamily.Poison:
                    s.Stats.Add(new StatMod(StatId.PoisonDamage, 0, Math.Round(5 * v, 1)));
                    s.Triggers.Add(new SkillTrigger { When = TriggerKind.OnHit, Chance = Math.Min(100, Math.Round(20 * v)), Effect = EffectKind.ApplyStatus, Status = StatusId.Poison, Stacks = 1 + (int)v, Duration = 4 });
                    s.Description = "Hits can poison the enemy.";
                    break;
                case SkillFamily.Bleed:
                    s.Stats.Add(new StatMod(StatId.BleedDamage, 0, Math.Round(5 * v, 1)));
                    s.Triggers.Add(new SkillTrigger { When = TriggerKind.OnCrit, Chance = Math.Min(100, Math.Round(40 * v)), Effect = EffectKind.ApplyStatus, Status = StatusId.Bleed, Stacks = 1 + (int)v, Duration = 3 });
                    s.Description = "Critical hits make the enemy bleed.";
                    break;
                case SkillFamily.Crit:
                    s.Stats.Add(new StatMod(StatId.CritChance, Math.Round(3 * v, 1)));
                    s.Stats.Add(new StatMod(StatId.CritDamage, Math.Round(10 * v, 1)));
                    s.Triggers.Add(new SkillTrigger { When = TriggerKind.OnCrit, Chance = Math.Min(100, Math.Round(15 * v)), Effect = EffectKind.GainRage, Target = TargetKind.Self, Value = Math.Round(10 * v) });
                    s.Description = "More crits; crits can build rage.";
                    break;
                case SkillFamily.Combo:
                    s.Stats.Add(new StatMod(StatId.ComboChance, Math.Round(3 * v, 1)));
                    s.Triggers.Add(new SkillTrigger { When = TriggerKind.OnCombo, Chance = Math.Min(100, Math.Round(25 * v)), Effect = EffectKind.Damage, Value = Math.Round(25 * v) });
                    s.Description = "More combos; combos hit harder.";
                    break;
                case SkillFamily.Counter:
                    s.Stats.Add(new StatMod(StatId.CounterChance, Math.Round(3 * v, 1)));
                    s.Triggers.Add(new SkillTrigger { When = TriggerKind.OnCounter, Chance = Math.Min(100, Math.Round(30 * v)), Effect = EffectKind.Shield, Target = TargetKind.Self, Value = Math.Round(3 * v, 1) });
                    s.Description = "More counters; counters grant a shield.";
                    break;
                case SkillFamily.AlienSynergy:
                    s.Stats.Add(new StatMod(StatId.AlienPower, 0, Math.Round(6 * v, 1)));
                    s.Triggers.Add(new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.BuffStat, Target = TargetKind.AllAllies, Stat = new StatMod(StatId.Atk, 0, Math.Round(5 * v, 1)), Duration = 3 });
                    s.Description = "Aliens are stronger and start battles empowered.";
                    break;
                default:
                    s.Stats.Add(new StatMod(StatId.Atk, 0, Math.Round(4 * v, 1)));
                    s.Description = "More attack.";
                    break;
            }
            return s;
        }
    }
}
