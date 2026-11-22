using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    public enum BuildingCategory { Production, Boon, Housing, Utility }

    /// <summary>What a building (or one of its star milestones) contributes.</summary>
    public enum BoonKind
    {
        None,
        StaminaCap,          // + max stamina
        StaminaRegenPct,     // stamina tick speed-up percent
        DraftRerolls,        // + draft rerolls per run
        SquadSlots,          // + alien squad slots (capped by CityTuning.MaxSquadSlots)
        RunSkill,            // building boon skill(s) added to CombatLoadout.MetaSkillIds
        BreedingSlots,       // + parallel breeding jobs
        BreedingSpeedPct,    // breeding duration speed-up percent
        BreedingQualityPct,  // gene quality points added to children (+ mutation/inherit chance)
        ResearchSpeedPct,    // laboratory research speed-up percent (read by Laboratory)
        BuildSpeedPct,       // construction/upgrade speed-up percent
        ProductionPct,       // + % production (milestone: this building; boon: every building)
        StoragePct,          // + % storage cap of this building (milestones)
        WorkerSlots,         // + worker slots of this building (milestones)
    }

    [Serializable]
    public sealed class SkillTier
    {
        public int MinLevel;
        public string SkillId;
        public SkillTier() { }
        public SkillTier(int minLevel, string skillId) { MinLevel = minLevel; SkillId = skillId; }
    }

    /// <summary>Level-scaled boon: value = Base + PerLevel × (level − MinLevel) once level ≥ MinLevel.</summary>
    [Serializable]
    public sealed class BuildingBoon
    {
        public BoonKind Kind;
        public double Base;
        public double PerLevel;
        public int MinLevel = 1;
        /// <summary>If true the value is multiplied by (1 + BoonPerStarPct × (stars − 1) / 100).</summary>
        public bool StarScaled;
        /// <summary>For RunSkill: the highest tier with MinLevel ≤ level is active.</summary>
        public List<SkillTier> SkillTiers = new List<SkillTier>();
    }

    /// <summary>Effect unlocked when the blueprint reaches Stars.</summary>
    [Serializable]
    public sealed class StarMilestone
    {
        public int Stars;
        public BoonKind Kind;
        public double Value;
        /// <summary>For RunSkill milestones: skill added to MetaSkillIds.</summary>
        public string SkillId;
        /// <summary>Optional skill id this milestone's skill replaces (upgrade).</summary>
        public string ReplacesSkillId;
        public string Description;
    }

    [Serializable]
    public sealed class BlueprintDef
    {
        public string Id;
        public string Name;
        public string Description;
        public Rarity Rarity;
        public BuildingCategory Category;
        public int Width = 1;
        public int Height = 1;
        /// <summary>Production buildings: produced currency, base rate per hour and storage at level 1 / star 1.</summary>
        public string ProduceCurrency;
        public double BaseRatePerHour;
        public double BaseStorage;
        /// <summary>Multiplier on the rarity build/upgrade costs.</summary>
        public double CostMult = 1;
        public List<BuildingBoon> Boons = new List<BuildingBoon>();
        public List<StarMilestone> Milestones = new List<StarMilestone>();
        /// <summary>Collection bonus: + CollectionPctPerStar % of CollectionStat per star while owned.</summary>
        public StatId CollectionStat = StatId.Hp;
        public double CollectionPctPerStar;
    }

    [Serializable]
    public sealed class CityAreaDef
    {
        public string Id;
        public string Name;
        /// <summary>Unlock order; areas unlock one by one in this order.</summary>
        public int Order;
        public int X, Y, Width, Height;
        public List<Cost> UnlockCost = new List<Cost>();
        /// <summary>Optional counter requirement (state.Counter(key) ≥ value).</summary>
        public string RequiresCounter;
        public long RequiresCounterValue;
    }

    [Serializable]
    public sealed class BuildingRarityTuning
    {
        public long BuildOre, BuildBiomass, BuildMs;
        public long UpgradeOre, UpgradeBiomass, UpgradeMs;
        public int WorkerSlots;
        public double CollectionPctPerStar;
    }

    [Serializable]
    public sealed class CityTuningDef
    {
        public int GridWidth = 12, GridHeight = 12;
        /// <summary>Upgrade cost to go from level L to L+1 = base × CostGrowth^(L−1); time × TimeGrowth^(L−1).</summary>
        public double CostGrowth = 1.35, TimeGrowth = 1.22;
        /// <summary>Max building level = MaxLevelBase + MaxLevelPerStar × stars.</summary>
        public int MaxLevelBase = 8, MaxLevelPerStar = 2;
        /// <summary>Production/storage growth per level above 1 and per star above 1 (percent).</summary>
        public double LevelProductionPct = 15, LevelStoragePct = 12, StarProductionPct = 5, StarStoragePct = 5;
        /// <summary>Star scaling for StarScaled boons (percent per star above 1).</summary>
        public double BoonPerStarPct = 6;
        /// <summary>Production priority: prioritized currency +Boost%, every other currency −Penalty%.</summary>
        public double PriorityBoostPct = 30, PriorityPenaltyPct = 10;
        public int MaxSquadSlots = 3;
        /// <summary>Shards needed to go from star s to s+1 (index 0 = 1→2 ... index 8 = 9→10).</summary>
        public long[] BlueprintStarCosts = { 1, 2, 3, 5, 8, 12, 18, 26, 36 };
        /// <summary>Shards per duplicate blueprint copy, by rarity.</summary>
        public long[] BlueprintShardsPerDuplicate = { 1, 1, 1, 1, 1 };
        public BuildingRarityTuning[] Rarity = new BuildingRarityTuning[0];
    }

    /// <summary>Published timer speed-up rules.</summary>
    [Serializable]
    public sealed class TimerTuningDef
    {
        /// <summary>Diamond cost = max(1, ceil(remaining minutes × DiamondsPerMinute)).</summary>
        public double DiamondsPerMinute = 0.2;
        public double AdReductionPct = 15;
        public int MaxAdsPerTimer = 3;
        /// <summary>Daily free speed-ups (0 disables) and the time each removes (0 = completes the timer).</summary>
        public int FreeSpeedUpsPerDay = 1;
        public long FreeSpeedUpMs = 30 * 60_000L;
        /// <summary>Meta ad placement consumed by ad speed-ups (shared daily cap; instant with No Ads).</summary>
        public string AdPlacementId = "timer_speedup";
    }

    public sealed partial class ContentDb
    {
        public readonly Dictionary<string, BlueprintDef> Blueprints = new Dictionary<string, BlueprintDef>();
        /// <summary>City areas in unlock order.</summary>
        public readonly List<CityAreaDef> CityAreas = new List<CityAreaDef>();
        public readonly CityTuningDef CityTuning = new CityTuningDef();
        public readonly TimerTuningDef TimerTuning = new TimerTuningDef();

        public BlueprintDef Blueprint(string id) => id != null && Blueprints.TryGetValue(id, out var b) ? b : null;

        public CityAreaDef CityArea(string id)
        {
            foreach (var a in CityAreas) if (a.Id == id) return a;
            return null;
        }

        partial void BuildCity()
        {
            const long Min = 60_000L, Hour = 60 * Min;
            CityTuning.Rarity = new[]
            {
                new BuildingRarityTuning { BuildOre = 40,    BuildBiomass = 60,    BuildMs = 1 * Min,   UpgradeOre = 30,   UpgradeBiomass = 50,    UpgradeMs = 2 * Min,  WorkerSlots = 1, CollectionPctPerStar = 0.3 },
                new BuildingRarityTuning { BuildOre = 200,   BuildBiomass = 300,   BuildMs = 10 * Min,  UpgradeOre = 120,  UpgradeBiomass = 200,   UpgradeMs = 8 * Min,  WorkerSlots = 2, CollectionPctPerStar = 0.5 },
                new BuildingRarityTuning { BuildOre = 800,   BuildBiomass = 1200,  BuildMs = 1 * Hour,  UpgradeOre = 500,  UpgradeBiomass = 800,   UpgradeMs = 30 * Min, WorkerSlots = 2, CollectionPctPerStar = 0.8 },
                new BuildingRarityTuning { BuildOre = 3000,  BuildBiomass = 4500,  BuildMs = 4 * Hour,  UpgradeOre = 1800, UpgradeBiomass = 2800,  UpgradeMs = 2 * Hour, WorkerSlots = 3, CollectionPctPerStar = 1.2 },
                new BuildingRarityTuning { BuildOre = 10000, BuildBiomass = 15000, BuildMs = 12 * Hour, UpgradeOre = 6000, UpgradeBiomass = 9000,  UpgradeMs = 6 * Hour, WorkerSlots = 3, CollectionPctPerStar = 2.0 },
            };

            // ---- areas: 12×12 grid, 4×4 core unlocked at start, then the ring one by one ----
            CityAreaAdd("area_core", "Landing Site", 0, 4, 4, null, 0);
            CityAreaAdd("area_north", "Northern Flats", 1, 4, 0, null, 0, CityCost(Currencies.Biomass, 300), CityCost(Currencies.Ore, 150));
            CityAreaAdd("area_east", "Crater Rim", 2, 8, 4, "city_buildings_built", 3, CityCost(Currencies.Biomass, 1200), CityCost(Currencies.Ore, 800));
            CityAreaAdd("area_south", "Spore Marsh", 3, 4, 8, "city_buildings_built", 5, CityCost(Currencies.Biomass, 4000), CityCost(Currencies.Ore, 2500));
            CityAreaAdd("area_west", "Glass Dunes", 4, 0, 4, "city_buildings_built", 7, CityCost(Currencies.Biomass, 10000), CityCost(Currencies.Ore, 7000));
            CityAreaAdd("area_northeast", "Signal Ridge", 5, 8, 0, "city_buildings_built", 9, CityCost(Currencies.Biomass, 25000), CityCost(Currencies.Ore, 18000));
            CityAreaAdd("area_southeast", "Molten Shelf", 6, 8, 8, "city_buildings_built", 12, CityCost(Currencies.Biomass, 60000), CityCost(Currencies.Ore, 45000), CityCost(Currencies.Diamonds, 100));
            CityAreaAdd("area_southwest", "Fungal Deep", 7, 0, 8, "city_buildings_built", 15, CityCost(Currencies.Biomass, 140000), CityCost(Currencies.Ore, 100000), CityCost(Currencies.Diamonds, 200));
            CityAreaAdd("area_northwest", "Starfall Plateau", 8, 0, 0, "city_buildings_built", 18, CityCost(Currencies.Biomass, 300000), CityCost(Currencies.Ore, 220000), CityCost(Currencies.Diamonds, 400));

            // ---- production ----
            CityProduction("bp_biomass_farm", "Biomass Farm", Rarity.Common, 1, 1, Currencies.Biomass, 60, StatId.Hp);
            CityProduction("bp_grain_farm", "Grain Farm", Rarity.Common, 1, 1, Currencies.Grain, 40, StatId.Hp);
            CityProduction("bp_ore_mine", "Ore Mine", Rarity.Common, 1, 1, Currencies.Ore, 40, StatId.Def);
            CityProduction("bp_spore_vats", "Spore Vats", Rarity.Rare, 2, 1, Currencies.Biomass, 150, StatId.Regen);
            CityProduction("bp_fuel_refinery", "Fuel Refinery", Rarity.Rare, 2, 1, Currencies.Fuel, 25, StatId.MountPower);
            CityProduction("bp_gear_works", "Gear Works", Rarity.Rare, 2, 1, Currencies.GearCores, 6, StatId.Atk);
            CityProduction("bp_training_grounds", "Training Grounds", Rarity.Rare, 2, 1, Currencies.HeroXp, 30, StatId.CritDamage);
            CityProduction("bp_plate_foundry", "Plate Foundry", Rarity.Epic, 2, 2, Currencies.AdvancementPlates, 1.5, StatId.Def);
            CityProduction("bp_diamond_mine", "Diamond Mine", Rarity.Epic, 2, 2, Currencies.Diamonds, 2, StatId.CritChance);
            CityProduction("bp_token_relay", "Blueprint Relay", Rarity.Legendary, 2, 2, Currencies.BlueprintTokens, 0.05, StatId.LootLuck);
            CityProduction("bp_signal_beacon", "Alien Signal Beacon", Rarity.Legendary, 2, 2, Currencies.AlienTokens, 0.05, StatId.AlienPower);

            // ---- housing (squad slots: first housing = slot 1; 2 and 3 via upgrades / rare blueprints) ----
            var pod = CityDef("bp_housing_pod", "Housing Pod", Rarity.Common, BuildingCategory.Housing, 1, 1, StatId.Hp,
                "Homes for your aliens. Unlocks the first squad slot; a second at level 12.");
            pod.Boons.Add(new BuildingBoon { Kind = BoonKind.SquadSlots, Base = 1 });
            pod.Boons.Add(new BuildingBoon { Kind = BoonKind.SquadSlots, Base = 1, MinLevel = 12 });
            var dome = CityDef("bp_barracks_dome", "Barracks Dome", Rarity.Legendary, BuildingCategory.Housing, 2, 2, StatId.AlienPower,
                "A fortified alien dome. +1 squad slot; boosts alien power.");
            dome.Boons.Add(new BuildingBoon { Kind = BoonKind.SquadSlots, Base = 1 });
            dome.Milestones.Add(new StarMilestone { Stars = 5, Kind = BoonKind.DraftRerolls, Value = 1, Description = "+1 draft reroll" });
            var command = CityDef("bp_command_spire", "Command Spire", Rarity.Mythic, BuildingCategory.Housing, 2, 2, StatId.Atk,
                "The worm's seat of rule. +1 squad slot and +5% production in every building.");
            command.Boons.Add(new BuildingBoon { Kind = BoonKind.SquadSlots, Base = 1 });
            command.Boons.Add(new BuildingBoon { Kind = BoonKind.ProductionPct, Base = 5, PerLevel = 0.5, StarScaled = true });

            // ---- utility ----
            var pen = CityDef("bp_breeding_pen", "Breeding Pen", Rarity.Rare, BuildingCategory.Utility, 2, 1, StatId.Hp,
                "Breed two aliens into a new generation. Levels speed breeding and add a second slot.");
            pen.Boons.Add(new BuildingBoon { Kind = BoonKind.BreedingSlots, Base = 1 });
            pen.Boons.Add(new BuildingBoon { Kind = BoonKind.BreedingSlots, Base = 1, MinLevel = 10 });
            pen.Boons.Add(new BuildingBoon { Kind = BoonKind.BreedingSpeedPct, Base = 0, PerLevel = 2, StarScaled = true });
            pen.Milestones.Add(new StarMilestone { Stars = 7, Kind = BoonKind.BreedingSlots, Value = 1, Description = "+1 breeding slot" });
            var geneLab = CityDef("bp_gene_lab", "Gene Lab", Rarity.Epic, BuildingCategory.Utility, 2, 2, StatId.StatusResist,
                "Improves the stats and traits of newborn aliens.");
            geneLab.Boons.Add(new BuildingBoon { Kind = BoonKind.BreedingQualityPct, Base = 2, PerLevel = 0.4, StarScaled = true });
            geneLab.Boons.Add(new BuildingBoon { Kind = BoonKind.BreedingSpeedPct, Base = 5, PerLevel = 1 });
            var lab = CityDef("bp_laboratory", "Laboratory", Rarity.Common, BuildingCategory.Utility, 1, 1, StatId.XpGain,
                "Speeds up Laboratory research.");
            lab.Boons.Add(new BuildingBoon { Kind = BoonKind.ResearchSpeedPct, Base = 5, PerLevel = 1.5, StarScaled = true });
            var crane = CityDef("bp_construction_yard", "Construction Yard", Rarity.Rare, BuildingCategory.Utility, 2, 1, StatId.Def,
                "Faster construction and upgrades.");
            crane.Boons.Add(new BuildingBoon { Kind = BoonKind.BuildSpeedPct, Base = 5, PerLevel = 1, StarScaled = true });
            var obs = CityDef("bp_observatory", "Observatory", Rarity.Rare, BuildingCategory.Utility, 1, 1, StatId.XpGain,
                "Charts the stars: more XP in runs and +2% production everywhere.");
            obs.Boons.Add(new BuildingBoon { Kind = BoonKind.ProductionPct, Base = 2, PerLevel = 0.25 });
            CityRunSkillTiers(obs, "Star Charts", "+{0}% XP in runs.", Rarity.Rare,
                v => new SkillDef { Stats = { new StatMod(StatId.XpGain, 0, v) } }, 5, 8, 12, 16, 20);

            // ---- boons ----
            var well = CityDef("bp_stamina_well", "Stamina Well", Rarity.Rare, BuildingCategory.Boon, 1, 1, StatId.Hp,
                "Raises max Stamina.");
            well.Boons.Add(new BuildingBoon { Kind = BoonKind.StaminaCap, Base = 5, PerLevel = 1 });
            well.Milestones.Add(new StarMilestone { Stars = 5, Kind = BoonKind.StaminaCap, Value = 5, Description = "+5 max Stamina" });
            well.Milestones.Add(new StarMilestone { Stars = 10, Kind = BoonKind.StaminaCap, Value = 10, Description = "+10 max Stamina" });
            var spire = CityDef("bp_regen_spire", "Regen Spire", Rarity.Epic, BuildingCategory.Boon, 1, 1, StatId.Regen,
                "Stamina regenerates faster.");
            spire.Boons.Add(new BuildingBoon { Kind = BoonKind.StaminaRegenPct, Base = 5, PerLevel = 0.8, StarScaled = true });
            var depot = CityDef("bp_supply_depot", "Supply Depot", Rarity.Common, BuildingCategory.Boon, 1, 1, StatId.Def,
                "Start every run with extra run coins.");
            CityRunSkillTiers(depot, "Supply Drop", "Start runs with {0} run coins.", Rarity.Common,
                v => CityTrig(TriggerKind.OnRunStart, EffectKind.GainRunCoins, v), 20, 35, 55, 80, 110);
            var scout = CityDef("bp_scout_post", "Scout Post", Rarity.Rare, BuildingCategory.Boon, 1, 1, StatId.Dodge,
                "Scouts warn you: skip negative event outcomes each run.");
            CityRunSkillTiers(scout, "Forewarned", "Skip {0} negative event outcome(s) per run.", Rarity.Rare,
                v => CityTrig(TriggerKind.OnRunStart, EffectKind.SkipNegativeEvent, v), 1, 1, 2, 2, 3);
            var salvage = CityDef("bp_salvage_yard", "Salvage Yard", Rarity.Rare, BuildingCategory.Boon, 2, 1, StatId.LootLuck,
                "More pending loot in runs.");
            CityRunSkillTiers(salvage, "Salvage Rights", "+{0}% run loot.", Rarity.Rare,
                v => CityTrig(TriggerKind.OnRunStart, EffectKind.ExtraLoot, v), 5, 8, 12, 16, 20);
            var academy = CityDef("bp_war_academy", "War Academy", Rarity.Rare, BuildingCategory.Boon, 2, 1, StatId.Atk,
                "Extra draft rerolls every run.");
            academy.Boons.Add(new BuildingBoon { Kind = BoonKind.DraftRerolls, Base = 1 });
            academy.Boons.Add(new BuildingBoon { Kind = BoonKind.DraftRerolls, Base = 1, MinLevel = 10 });
            academy.Milestones.Add(new StarMilestone { Stars = 7, Kind = BoonKind.DraftRerolls, Value = 1, Description = "+1 draft reroll" });
            var dice = CityDef("bp_dice_tower", "Dice Tower", Rarity.Epic, BuildingCategory.Boon, 1, 1, StatId.Speed,
                "Chance for an extra die on each roll.");
            CityRunSkillTiers(dice, "Loaded Fortune", "{0}% chance to roll an extra die.", Rarity.Epic,
                v => { var s = CityTrig(TriggerKind.OnRoll, EffectKind.ExtraDice, 1); s.Triggers[0].Chance = v; return s; }, 8, 11, 14, 17, 20);
            var stasis = CityDef("bp_stasis_beacon", "Stasis Beacon", Rarity.Legendary, BuildingCategory.Boon, 2, 2, StatId.BossDamage,
                "Slows the boss awakening meter.");
            CityRunSkillTiers(stasis, "Stasis Field", "Boss awakening meter starts {0} points lower.", Rarity.Legendary,
                v => CityTrig(TriggerKind.OnRunStart, EffectKind.SlowAwakening, v), 5, 8, 11, 14, 18);

            // Extremely rare Epic+ start-skill buildings: 1–3 skills at run start.
            AddSkill(CityBuildingSkill("bsk_ember_crown", "Ember Crown", "Attacks have a 25% chance to Burn.", Rarity.Epic, SkillFamily.Fire,
                new SkillTrigger { When = TriggerKind.OnAttack, Chance = 25, Effect = EffectKind.ApplyStatus, Status = StatusId.Burn, Stacks = 1, Duration = 3 }));
            AddSkill(CityBuildingSkill("bsk_frost_sigil", "Frost Sigil", "Getting hit has a 15% chance to Freeze the attacker.", Rarity.Legendary, SkillFamily.Ice,
                new SkillTrigger { When = TriggerKind.OnDamaged, Chance = 15, Effect = EffectKind.ApplyStatus, Status = StatusId.Freeze, Stacks = 1, Duration = 1 }));
            AddSkill(CityBuildingSkill("bsk_storm_mantle", "Storm Mantle", "Crits deal 40% ATK lightning damage.", Rarity.Legendary, SkillFamily.Lightning,
                new SkillTrigger { When = TriggerKind.OnCrit, Chance = 100, Effect = EffectKind.Damage, Value = 40 }));
            AddSkill(CityBuildingSkill("bsk_aegis_core", "Aegis Core", "Start every battle with a 12% max HP shield.", Rarity.Epic, SkillFamily.None,
                new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.Shield, Target = TargetKind.Worm, Value = 12 }));
            AddSkill(CityBuildingSkill("bsk_aegis_core_plus", "Aegis Core+", "Start every battle with a 20% max HP shield.", Rarity.Legendary, SkillFamily.None,
                new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.Shield, Target = TargetKind.Worm, Value = 20 }));

            var forge = CityDef("bp_star_forge", "Star Forge", Rarity.Epic, BuildingCategory.Boon, 2, 2, StatId.Atk,
                "Start every run with Aegis Core (upgraded at 7 stars).");
            forge.Boons.Add(new BuildingBoon { Kind = BoonKind.RunSkill, SkillTiers = { new SkillTier(1, "bsk_aegis_core") } });
            forge.Milestones.Add(new StarMilestone { Stars = 7, Kind = BoonKind.RunSkill, SkillId = "bsk_aegis_core_plus", ReplacesSkillId = "bsk_aegis_core", Description = "Upgrades Aegis Core" });
            var reliquary = CityDef("bp_ancient_reliquary", "Ancient Reliquary", Rarity.Mythic, BuildingCategory.Boon, 2, 2, StatId.CritDamage,
                "Start every run with Ember Crown; Storm Mantle at 5 stars; Frost Sigil at 10 stars.");
            reliquary.Boons.Add(new BuildingBoon { Kind = BoonKind.RunSkill, SkillTiers = { new SkillTier(1, "bsk_ember_crown") } });
            reliquary.Milestones.Add(new StarMilestone { Stars = 5, Kind = BoonKind.RunSkill, SkillId = "bsk_storm_mantle", Description = "Second start skill" });
            reliquary.Milestones.Add(new StarMilestone { Stars = 10, Kind = BoonKind.RunSkill, SkillId = "bsk_frost_sigil", Description = "Third start skill" });
        }

        // ---- content helpers (city) ----------------------------------------------------------

        private static Cost CityCost(string currency, long amount) => new Cost(currency, amount);

        private void CityAreaAdd(string id, string name, int order, int x, int y, string counter, long counterValue, params Cost[] cost)
        {
            var a = new CityAreaDef { Id = id, Name = name, Order = order, X = x, Y = y, Width = 4, Height = 4, RequiresCounter = counter, RequiresCounterValue = counterValue };
            a.UnlockCost.AddRange(cost);
            CityAreas.Add(a);
        }

        private BlueprintDef CityDef(string id, string name, Rarity rarity, BuildingCategory cat, int w, int h, StatId collectionStat, string description)
        {
            var d = new BlueprintDef
            {
                Id = id, Name = name, Rarity = rarity, Category = cat, Width = w, Height = h, Description = description,
                CollectionStat = collectionStat, CollectionPctPerStar = CityTuning.Rarity[(int)rarity].CollectionPctPerStar,
            };
            Blueprints.Add(id, d);
            return d;
        }

        private void CityProduction(string id, string name, Rarity rarity, int w, int h, string currency, double ratePerHour, StatId collectionStat)
        {
            var d = CityDef(id, name, rarity, BuildingCategory.Production, w, h, collectionStat, "Produces " + currency + " over time.");
            d.ProduceCurrency = currency;
            d.BaseRatePerHour = ratePerHour;
            d.BaseStorage = ratePerHour * 8; // 8 hours at level 1
            d.Milestones.Add(new StarMilestone { Stars = 3, Kind = BoonKind.ProductionPct, Value = 10, Description = "+10% production" });
            d.Milestones.Add(new StarMilestone { Stars = 5, Kind = BoonKind.WorkerSlots, Value = 1, Description = "+1 worker slot" });
            d.Milestones.Add(new StarMilestone { Stars = 7, Kind = BoonKind.StoragePct, Value = 50, Description = "+50% storage" });
            d.Milestones.Add(new StarMilestone { Stars = 10, Kind = BoonKind.ProductionPct, Value = 30, Description = "+30% production" });
        }

        /// <summary>Creates one building skill per level tier (levels 1/5/10/15/20); each tier upgrades the previous.</summary>
        private void CityRunSkillTiers(BlueprintDef bp, string name, string descFormat, Rarity rarity, Func<double, SkillDef> make, params double[] values)
        {
            int[] levels = { 1, 5, 10, 15, 20 };
            var boon = new BuildingBoon { Kind = BoonKind.RunSkill };
            for (int i = 0; i < values.Length && i < levels.Length; i++)
            {
                var s = make(values[i]);
                s.Id = bp.Id + "_t" + (i + 1);
                s.Name = name + " " + (i + 1);
                s.Description = string.Format(descFormat, values[i]);
                s.Rarity = rarity;
                s.Source = SkillSource.Building;
                s.Draftable = false;
                if (i > 0) s.UpgradesFrom = bp.Id + "_t" + i;
                AddSkill(s);
                boon.SkillTiers.Add(new SkillTier(levels[i], s.Id));
            }
            bp.Boons.Add(boon);
        }

        private static SkillDef CityTrig(TriggerKind when, EffectKind effect, double value)
        {
            var s = new SkillDef();
            s.Triggers.Add(new SkillTrigger { When = when, Effect = effect, Value = value, Target = TargetKind.Self });
            return s;
        }

        private static SkillDef CityBuildingSkill(string id, string name, string desc, Rarity rarity, SkillFamily family, SkillTrigger trigger)
        {
            var s = new SkillDef { Id = id, Name = name, Description = desc, Rarity = rarity, Family = family, Source = SkillSource.Building, Draftable = false };
            s.Triggers.Add(trigger);
            return s;
        }
    }
}
