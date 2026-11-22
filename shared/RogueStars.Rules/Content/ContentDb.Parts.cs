using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>What kind of body part a definition is. Arm parts fit either arm slot.</summary>
    public enum PartKind { Head, Body, Tail, Legs, Arm }

    /// <summary>The six equipment slots of the worm.</summary>
    public enum PartSlot { Head, Body, Tail, Legs, LeftArm, RightArm }

    /// <summary>The 15-step part rarity ladder raised by merging.</summary>
    public enum PartRarity
    {
        Common, Uncommon, Rare, RarePlus, Epic, Epic1, Epic2,
        Legendary, Legendary1, Legendary2, Legendary3,
        Mythic, Mythic1, Mythic2, Mythic3,
    }

    [Serializable]
    public sealed class PartDef
    {
        public string Id;
        public string Name;
        public PartKind Kind;
        /// <summary>Attack style; only meaningful for arm parts.</summary>
        public AttackStyle Style;
        /// <summary>Family this part favours (its signature/awakening skills belong to it).</summary>
        public SkillFamily Family;
        /// <summary>Stats at Common, level 1 (scaled by rarity multiplier and Forge bonus).</summary>
        public List<StatMod> BaseStats = new List<StatMod>();
        /// <summary>Stats per level above 1 (scaled by rarity multiplier, not by Forge).</summary>
        public List<StatMod> StatsPerLevel = new List<StatMod>();
        /// <summary>Signature skill active at PartsConfig.SignatureRarity, upgraded at Signature2Rarity.</summary>
        public string SignatureSkillId;
        public string Signature2SkillId;
        /// <summary>S and SS awakening skills (SS replaces S).</summary>
        public string AwakenSSkillId;
        public string AwakenSSSkillId;
    }

    /// <summary>Merge recipe to reach a rarity step: the target part (one step below) plus materials.</summary>
    [Serializable]
    public sealed class PartMergeRecipe
    {
        /// <summary>Extra copies of the same part def needed, at exactly SameDefRarity.</summary>
        public int SameDefCount;
        public PartRarity SameDefRarity;
        /// <summary>Any parts of the same kind (any def) needed, at exactly FodderRarity.</summary>
        public int FodderCount;
        public PartRarity FodderRarity;
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
        public int MaxRuneSlots = 4;
        public PartRarity ForgeMinRarity = PartRarity.Epic;
        public List<ForgeStep> Forge = new List<ForgeStep>();
        public PartRarity AwakenMinRarity = PartRarity.Legendary;
        public PartRarity SSMinRarity = PartRarity.Mythic;
        /// <summary>Part Awaken Cores for S (index 0) and SS (index 1).</summary>
        public List<long> AwakenCores = new List<long>();
        /// <summary>All-stat percent bonus of the part at awaken level 0/1/2.</summary>
        public List<double> AwakenStatPercent = new List<double>();
        public PartRarity SignatureRarity = PartRarity.Mythic;
        public PartRarity Signature2Rarity = PartRarity.Mythic3;
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

            // ---- rarity ladder -------------------------------------------------------------------
            // name, stat mult, base cap, cap raises, rune slots, dismantle ore, recipe (same n @r, fodder n @r)
            AddPartRarity(PartRarity.Common, "Common", 1.0, 10, 0, 0, 50, null);
            AddPartRarity(PartRarity.Uncommon, "Uncommon", 1.25, 15, 0, 0, 100, PartRecipe(2, PartRarity.Common, 0, PartRarity.Common));
            AddPartRarity(PartRarity.Rare, "Rare", 1.6, 20, 1, 0, 200, PartRecipe(2, PartRarity.Uncommon, 0, PartRarity.Uncommon));
            AddPartRarity(PartRarity.RarePlus, "Rare+", 1.9, 25, 1, 1, 350, PartRecipe(1, PartRarity.Rare, 1, PartRarity.Rare));
            AddPartRarity(PartRarity.Epic, "Epic", 2.4, 30, 2, 1, 600, PartRecipe(2, PartRarity.RarePlus, 0, PartRarity.RarePlus));
            AddPartRarity(PartRarity.Epic1, "Epic+1", 2.8, 35, 2, 1, 900, PartRecipe(0, PartRarity.Epic, 2, PartRarity.Epic));
            AddPartRarity(PartRarity.Epic2, "Epic+2", 3.2, 40, 2, 1, 1300, PartRecipe(1, PartRarity.Epic, 1, PartRarity.Epic));
            AddPartRarity(PartRarity.Legendary, "Legendary", 4.0, 50, 3, 2, 2000, PartRecipe(2, PartRarity.Epic2, 0, PartRarity.Epic2));
            AddPartRarity(PartRarity.Legendary1, "Legendary+1", 4.6, 55, 3, 2, 2800, PartRecipe(0, PartRarity.Legendary, 1, PartRarity.Legendary));
            AddPartRarity(PartRarity.Legendary2, "Legendary+2", 5.2, 60, 3, 2, 3800, PartRecipe(0, PartRarity.Legendary, 2, PartRarity.Legendary));
            AddPartRarity(PartRarity.Legendary3, "Legendary+3", 5.9, 65, 4, 2, 5000, PartRecipe(1, PartRarity.Legendary, 1, PartRarity.Legendary));
            AddPartRarity(PartRarity.Mythic, "Mythic", 7.0, 80, 4, 3, 7000, PartRecipe(2, PartRarity.Legendary, 0, PartRarity.Legendary));
            AddPartRarity(PartRarity.Mythic1, "Mythic+1", 7.8, 85, 5, 3, 9000, PartRecipe(0, PartRarity.Mythic, 1, PartRarity.Mythic));
            AddPartRarity(PartRarity.Mythic2, "Mythic+2", 8.7, 90, 5, 3, 11000, PartRecipe(0, PartRarity.Mythic, 2, PartRarity.Mythic));
            AddPartRarity(PartRarity.Mythic3, "Mythic+3", 9.8, 100, 6, 3, 14000, PartRecipe(1, PartRarity.Mythic, 1, PartRarity.Mythic));

            // ---- levels, cap raises ----------------------------------------------------------------
            p.CapRaiseLevels = 5;
            p.CapRaisePlates.AddRange(new long[] { 5, 10, 20, 35, 55, 80 });
            int maxLevel = 100 + 6 * p.CapRaiseLevels; // Mythic+3 base cap + all raises
            p.LevelOreCosts.Add(0); p.LevelGearCoreCosts.Add(0);
            for (int l = 1; l < maxLevel; l++)
            {
                p.LevelOreCosts.Add((long)Math.Round(30 * Math.Pow(l, 1.4)));
                p.LevelGearCoreCosts.Add(l < 20 ? 0 : 1 + (l - 20) / 8);
            }

            // ---- forge, awakening, signatures ------------------------------------------------------
            p.MaxRuneSlots = 4;
            p.ForgeMinRarity = PartRarity.Epic;
            p.Forge.Add(new ForgeStep { Diamonds = 300, GearCores = 40, BonusPercent = 10 });
            p.Forge.Add(new ForgeStep { Diamonds = 900, GearCores = 120, BonusPercent = 15 });
            p.AwakenMinRarity = PartRarity.Legendary;
            p.SSMinRarity = PartRarity.Mythic;
            p.AwakenCores.AddRange(new long[] { 20, 60 });
            p.AwakenStatPercent.AddRange(new double[] { 0, 15, 35 });
            p.SignatureRarity = PartRarity.Mythic;
            p.Signature2Rarity = PartRarity.Mythic3;
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

            // ---- part defs (30) ---------------------------------------------------------------------
            AddPartDef("head_scanner_visor", "Scanner Visor", PartKind.Head, AttackStyle.Slam, SkillFamily.Crit);
            AddPartDef("head_ember_crown", "Ember Crown", PartKind.Head, AttackStyle.Slam, SkillFamily.Fire);
            AddPartDef("head_frost_helm", "Frost Helm", PartKind.Head, AttackStyle.Slam, SkillFamily.Ice);
            AddPartDef("head_hive_antenna", "Hive Antenna", PartKind.Head, AttackStyle.Slam, SkillFamily.AlienSynergy);
            AddPartDef("head_duelist_mask", "Duelist Mask", PartKind.Head, AttackStyle.Slam, SkillFamily.Counter);
            AddPartDef("body_plated_shell", "Plated Shell", PartKind.Body, AttackStyle.Slam, SkillFamily.Counter);
            AddPartDef("body_reactor_core", "Reactor Core", PartKind.Body, AttackStyle.Slam, SkillFamily.Lightning);
            AddPartDef("body_toxic_tank", "Toxic Tank", PartKind.Body, AttackStyle.Slam, SkillFamily.Poison);
            AddPartDef("body_glacier_plate", "Glacier Plate", PartKind.Body, AttackStyle.Slam, SkillFamily.Ice);
            AddPartDef("body_spiked_mail", "Spiked Mail", PartKind.Body, AttackStyle.Slam, SkillFamily.Bleed);
            AddPartDef("tail_rocket_booster", "Rocket Booster", PartKind.Tail, AttackStyle.Slam, SkillFamily.Combo);
            AddPartDef("tail_stinger", "Stinger Tail", PartKind.Tail, AttackStyle.Slam, SkillFamily.Poison);
            AddPartDef("tail_coil_spring", "Coil Spring", PartKind.Tail, AttackStyle.Slam, SkillFamily.Lightning);
            AddPartDef("tail_razor_fin", "Razor Fin", PartKind.Tail, AttackStyle.Slam, SkillFamily.Bleed);
            AddPartDef("tail_comet_tail", "Comet Tail", PartKind.Tail, AttackStyle.Slam, SkillFamily.Fire);
            AddPartDef("legs_hover_treads", "Hover Treads", PartKind.Legs, AttackStyle.Slam, SkillFamily.Combo);
            AddPartDef("legs_spider_legs", "Spider Legs", PartKind.Legs, AttackStyle.Slam, SkillFamily.Crit);
            AddPartDef("legs_magma_stompers", "Magma Stompers", PartKind.Legs, AttackStyle.Slam, SkillFamily.Fire);
            AddPartDef("legs_grav_boots", "Grav Boots", PartKind.Legs, AttackStyle.Slam, SkillFamily.Counter);
            AddPartDef("legs_cryo_skates", "Cryo Skates", PartKind.Legs, AttackStyle.Slam, SkillFamily.Ice);
            AddPartDef("arm_slam_hammer", "Slam Hammer", PartKind.Arm, AttackStyle.Slam, SkillFamily.Counter);
            AddPartDef("arm_mantis_claw", "Mantis Claw", PartKind.Arm, AttackStyle.Claw, SkillFamily.Bleed);
            AddPartDef("arm_plasma_blaster", "Plasma Blaster", PartKind.Arm, AttackStyle.Blaster, SkillFamily.Fire);
            AddPartDef("arm_drill_lance", "Drill Lance", PartKind.Arm, AttackStyle.Drill, SkillFamily.Crit);
            AddPartDef("arm_tesla_whip", "Tesla Whip", PartKind.Arm, AttackStyle.Whip, SkillFamily.Lightning);
            AddPartDef("arm_rail_cannon", "Rail Cannon", PartKind.Arm, AttackStyle.Cannon, SkillFamily.Crit);
            AddPartDef("arm_venom_claw", "Venom Claw", PartKind.Arm, AttackStyle.Claw, SkillFamily.Poison);
            AddPartDef("arm_frost_cannon", "Frost Cannon", PartKind.Arm, AttackStyle.Cannon, SkillFamily.Ice);
            AddPartDef("arm_ripper_drill", "Ripper Drill", PartKind.Arm, AttackStyle.Drill, SkillFamily.Combo);
            AddPartDef("arm_hive_whip", "Hive Whip", PartKind.Arm, AttackStyle.Whip, SkillFamily.AlienSynergy);
        }

        private static PartMergeRecipe PartRecipe(int same, PartRarity sameR, int fodder, PartRarity fodderR) =>
            new PartMergeRecipe { SameDefCount = same, SameDefRarity = sameR, FodderCount = fodder, FodderRarity = fodderR };

        private void AddPartRarity(PartRarity r, string name, double mult, int cap, int raises, int slots, long ore, PartMergeRecipe merge)
        {
            if ((int)r != PartsConfig.Rarities.Count) throw new InvalidOperationException("rarities must be added in order");
            PartsConfig.Rarities.Add(new PartRarityDef { Rarity = r, Name = name, StatMultiplier = mult, BaseLevelCap = cap, MaxCapRaises = raises, RuneSlots = slots, DismantleOre = ore, Merge = merge });
        }

        private void AddRuneDef(string id, string name, StatId stat, bool percent, double value) =>
            PartsConfig.Runes[id] = new RuneDef { Id = id, Name = name, Stat = stat, IsPercent = percent, BaseValue = value };

        private void AddPartDef(string id, string name, PartKind kind, AttackStyle style, SkillFamily family)
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

            d.SignatureSkillId = id + "_sig";
            d.Signature2SkillId = id + "_sig2";
            d.AwakenSSkillId = id + "_s";
            d.AwakenSSSkillId = id + "_ss";
            AddSkill(ProgFamilySkill(d.SignatureSkillId, name + " Signature", family, Rarity.Legendary, SkillSource.Part, 1.0, null));
            AddSkill(ProgFamilySkill(d.Signature2SkillId, name + " Signature II", family, Rarity.Mythic, SkillSource.Part, 1.8, d.SignatureSkillId));
            AddSkill(ProgFamilySkill(d.AwakenSSkillId, name + " [S]", family, Rarity.Legendary, SkillSource.Part, 1.3, null));
            AddSkill(ProgFamilySkill(d.AwakenSSSkillId, name + " [SS]", family, Rarity.Mythic, SkillSource.Part, 2.4, d.AwakenSSkillId));
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
