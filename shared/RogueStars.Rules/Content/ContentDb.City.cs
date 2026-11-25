using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    public enum BuildingCategory { Production, Boon, Housing, Utility }

    /// <summary>What a building (or one of its star unlocks) contributes.</summary>
    public enum BoonKind
    {
        None,
        StaminaCap,          // + max stamina (whole number)
        StaminaRegenPct,     // stamina tick speed-up percent
        DraftRerolls,        // + draft rerolls per run (whole number)
        SquadSlots,          // + alien squad slots (Launch Pod star unlocks only)
        BreedingSlots,       // + parallel breeding jobs (whole number)
        BreedingSpeedPct,    // breeding duration speed-up percent
        BreedingQualityPct,  // gene quality points added to children
        ResearchSpeedPct,    // laboratory research speed-up percent (read by Laboratory)
        ResearchSlots,       // laboratory research slots (Laboratory)
        ProductionPct,       // + % production in every production building (Command Spire)
        WorkerSlots,         // + worker slots of this building (star unlocks)
        RunXpPct,            // + % XP in runs (Observatory)
        RunStartCoins,       // run coins at the start of every run (whole number)
        SkipNegativeEvents,  // bad event outcomes skipped per run (whole number)
        AwakeningSlowPct,    // the boss awakening meter fills this % slower
        AlienDamagePct,      // + % alien damage in runs (Barracks Dome, Launch Pod)
    }

    /// <summary>Which currencies a building is paid in (build and level-ups).</summary>
    public enum BuildingCostType { Both, Biomass, Ore }

    /// <summary>Placement skill kinds (City design §5). Distances are straight steps between the closest footprint tiles.</summary>
    public enum PlacementKind
    {
        None,
        /// <summary>Side contact with buildings of <see cref="PlacementSkillDef.Target"/>: +Pct per match up to MaxPct.</summary>
        Neighbours,
        /// <summary>Different categories touching it: +Pct per category up to MaxPct.</summary>
        MeltingPot,
        /// <summary>In a straight line of 3 side-touching buildings of Target (itself included).</summary>
        RowOfThree,
        /// <summary>Buildings that touch only its corner: +Pct each up to MaxPct.</summary>
        Diagonal,
        /// <summary>A Target building directly opposite across a 1-wide street.</summary>
        AcrossStreet,
        /// <summary>Within Count tiles of the fireplace.</summary>
        Hearth,
        /// <summary>Count+ tiles away from the fireplace.</summary>
        Outskirts,
        /// <summary>Count+ empty land tiles (no building, street, feature or plaza) touch it.</summary>
        OpenGround,
        /// <summary>A footprint tile touches the land edge (undug or void).</summary>
        Cliffside,
        /// <summary>Gives +Pct to every building touching it (counts toward their cap).</summary>
        Aura,
        /// <summary>Special: touches the crash-site plaza.</summary>
        TouchPlaza,
    }

    /// <summary>"Do X to get +Y%": a multiplier on what the building does, or a flat whole-number bonus (Flat &gt; 0).</summary>
    [Serializable]
    public sealed class PlacementSkillDef
    {
        public PlacementKind Kind;
        /// <summary>Neighbour / across / row target: a blueprint id, "tag:&lt;tag&gt;" or "cat:&lt;Category&gt;"; null = any building.</summary>
        public string Target;
        /// <summary>Distance (Hearth / Outskirts) or tile count (Open Ground).</summary>
        public int Count;
        public double Pct;
        public double MaxPct;
        /// <summary>Flat whole-number bonus added to the main boon instead of a percentage (Scout Post, War Academy).</summary>
        public double Flat;
        /// <summary>Player-facing rule text.</summary>
        public string Text;
    }

    /// <summary>
    /// Building value = Base × (1 + level ladder + star ladder) (City design §6, "3× from placement to max"). Placement = the
    /// building's placement skill multiplies this boon.
    /// </summary>
    [Serializable]
    public sealed class BuildingBoon
    {
        public BoonKind Kind;
        /// <summary>Value when first placed (level 1, 1★).</summary>
        public double Base;
        public bool GrowsWithLevel = true;
        public bool GrowsWithStars = true;
        /// <summary>The boon the building's placement skill boosts.</summary>
        public bool Placement;
        /// <summary>Whole-number boons round down per copy (rerolls, skips, slots, stamina, run coins).</summary>
        public bool Whole;
    }

    /// <summary>Something unlocked when the blueprint reaches Stars (worker / squad / breeding / research slots).</summary>
    [Serializable]
    public sealed class StarMilestone
    {
        public int Stars;
        public BoonKind Kind;
        public double Value;
        public string Description;
    }

    /// <summary>
    /// Pull buildings (City design §6, "Pull buildings: chances you pop"): the building makes chances that pile up on it; popping one
    /// gives 1..X pulls (tokens) of <see cref="TokenCurrency"/>. Levels speed chances up (ladder) and raise storage; stars 2–4 add
    /// +StarSpeedPct each; stars set X and the odds (<see cref="CityTuningDef.PopOdds"/>). Aliens' work bonus does not apply.
    /// </summary>
    [Serializable]
    public sealed class PullChanceDef
    {
        public string TokenCurrency;
        /// <summary>Player-facing name of one chance ("Research Scroll").</summary>
        public string ChanceName;
        public double BasePerDay = 0.5;
        public int BaseStorage = 3;
        public double StarSpeedPct = 10;
        public int StarSpeedFrom = 2, StarSpeedTo = 4;
    }

    [Serializable]
    public sealed class BlueprintDef
    {
        public string Id;
        public string Name;
        public string Description;
        public Rarity Rarity;
        public BuildingCategory Category;
        /// <summary>Footprint (unrotated), from the rarity (CityTuningDef.Footprint).</summary>
        public int Width = 1;
        public int Height = 1;
        /// <summary>Copies a player may own of this building (CityTuningDef.Copies by rarity unless set).</summary>
        public int Copies = 1;
        public BuildingCostType CostType = BuildingCostType.Both;
        /// <summary>Production buildings: produced currency and rate per hour when placed (level 1, 1★).</summary>
        public string ProduceCurrency;
        public double BaseRatePerHour;
        /// <summary>Pull buildings: the chances it makes (ProduceCurrency is null).</summary>
        public PullChanceDef Pull;
        public List<BuildingBoon> Boons = new List<BuildingBoon>();
        public List<StarMilestone> Milestones = new List<StarMilestone>();
        /// <summary>Worker slots at 1★ (production: by rarity; others 1). Launch Pod: these are the squad slots.</summary>
        public int WorkerSlots = 1;
        /// <summary>Null = no placement skill.</summary>
        public PlacementSkillDef Placement;
        /// <summary>Tags placement skills can target ("farm").</summary>
        public List<string> Tags = new List<string>();
    }

    /// <summary>A terrain feature tile: an obstacle nothing can be built or paved on.</summary>
    [Serializable]
    public sealed class CityFeatureDef
    {
        public string Kind; // "crystal", "spring", "ruin"
        public int X, Y;
    }

    /// <summary>A diggable patch of land (City design §3). Dug in any order once it touches the player's land.</summary>
    [Serializable]
    public sealed class CityPatchDef
    {
        public string Id;
        public string Name;
        /// <summary>Cells (y × GridWidth + x).</summary>
        public List<int> Cells = new List<int>();
        public List<CityFeatureDef> Features = new List<CityFeatureDef>();
    }

    /// <summary>Cost of the n-th dig (by dig number, not by patch).</summary>
    [Serializable]
    public sealed class CityDigDef
    {
        /// <summary>Design target day for an active free player (docs, sim).</summary>
        public int TargetDay;
        public long Biomass, Ore, GearCores;
        public long Ms;
    }

    [Serializable]
    public sealed class BuildingRarityTuning
    {
        /// <summary>Build cost of a new copy (the "both" split; single-currency buildings convert, see CityTuningDef).</summary>
        public long BuildOre, BuildBiomass, BuildMs;
        /// <summary>First level-up (1 → 2), "both" split.</summary>
        public long UpgradeOre, UpgradeBiomass, UpgradeMs;
        /// <summary>Worker slots of production buildings at 1★.</summary>
        public int WorkerSlots;
        public int Copies;
        public int Width, Height;
    }

    [Serializable]
    public sealed class CityTuningDef
    {
        public int GridWidth = 20, GridHeight = 20;
        /// <summary>Crash-site plaza (fixed 3×3) and its fireplace.</summary>
        public int PlazaX = 8, PlazaY = 8, PlazaSize = 3;
        public int FireX => PlazaX + PlazaSize / 2;
        public int FireY => PlazaY + PlazaSize / 2;
        /// <summary>Start land (plaza included), cells y × GridWidth + x.</summary>
        public List<int> StartCells = new List<int>();
        public int MaxLevel = 10;
        /// <summary>Level and star ladders: +StepPct per step, +MilestonePct at the milestone steps (7 × 10 + 2 × 15 = 100%).</summary>
        public double LadderStepPct = 10, LadderMilestonePct = 15;
        public int[] LadderMilestones = { 5, 10 };
        /// <summary>Single-currency buildings: Biomass only = BiomassOnlyMult × the Biomass part, Ore only = OreOnlyMult × the Ore part.</summary>
        public double BiomassOnlyMult = 3, OreOnlyMult = 1.5;
        /// <summary>Level-up cost multiplier of the first cost, per level-up (index 0 = level 1 → 2 … index 8 = 9 → 10).</summary>
        public double[] LevelCostMult = { 1, 2, 3, 5, 9, 15, 25, 45, 80 };
        /// <summary>Level-up time growth per level (time × TimeGrowth^(level−1)).</summary>
        public double TimeGrowth = 1.25;
        /// <summary>Storage of production buildings in hours of their current rate (before workers / placement).</summary>
        public double StorageHours = 8;
        /// <summary>Production priority: prioritized currency +Boost%, every other currency −Penalty%.</summary>
        public double PriorityBoostPct = 30, PriorityPenaltyPct = 10;
        public int MaxSquadSlots = 3;
        /// <summary>Own placement skill + auras, capped (percent).</summary>
        public double PlacementCapPct = 30;
        /// <summary>Pop odds by stars (index 1..10): chance in percent of 1, 2 and 3 pulls.</summary>
        public double[][] PopOdds = new double[0][];
        public BuildingRarityTuning[] Rarity = new BuildingRarityTuning[0];
        public List<CityDigDef> Digs = new List<CityDigDef>();
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
        public const string LaunchPodId = "bp_launch_pod";
        public const string LaboratoryId = "bp_laboratory";

        public readonly Dictionary<string, BlueprintDef> Blueprints = new Dictionary<string, BlueprintDef>();
        /// <summary>Diggable patches in content order.</summary>
        public readonly List<CityPatchDef> CityPatches = new List<CityPatchDef>();
        public readonly CityTuningDef CityTuning = new CityTuningDef();
        public readonly TimerTuningDef TimerTuning = new TimerTuningDef();

        public BlueprintDef Blueprint(string id) => id != null && Blueprints.TryGetValue(id, out var b) ? b : null;

        public CityPatchDef CityPatch(string id)
        {
            foreach (var p in CityPatches) if (p.Id == id) return p;
            return null;
        }

        /// <summary>
        /// The land (City design §3): 20×20 grid, '0' = start land (the 3×3 plaza in the middle and 21 tiles around it),
        /// 'A'..'T' = the 20 diggable patches (7–16 tiles), '.' = void. 240 land tiles in all.
        /// </summary>
        private static readonly string[] CityMap =
        {
            "....................",
            "...TTTTTTTTTTTT.....",
            "...TTTLLLLOOOOT.....",
            "..QQQLLLLLLLOOO.....",
            ".QQQIIIIGGGGOOO.....",
            ".QQQIIDDGGGGGOORR...",
            ".QQQIDDDDD0BBBJRRR..",
            ".QNIIAD00000BEJJRR..",
            ".NNIAA000000BEJJRR..",
            "..NNAA000000BEJJRR..",
            "..NNAA000000BEJMMR..",
            "..NNFFC0000EEEJMMR..",
            "..NNFFCCC00EHHJMMR..",
            ".SSSSFFCCCCHHHMM....",
            ".SSSSKFFFHHHHMM.....",
            "..SSSKKKKKKKKM......",
            "..SSSSPPKPPPPP......",
            ".......PPPPP........",
            "....................",
            "....................",
        };

        private static readonly string[] CityPatchNames =
        {
            "Landing Scrub", "Ember Flats", "Saucer Trail", "Moss Hollow", "Shard Steps", "Dust Basin", "North Terrace",
            "Spore Marsh", "Pebble Run", "Crater Rim", "Fern Gully", "Glass Dunes", "Signal Ridge", "Fungal Deep",
            "Crystal Field", "Old Quarry", "Starfall Plateau", "Molten Shelf", "Lantern Bluff", "Far Ridge",
        };

        partial void BuildCity()
        {
            const long Min = 60_000L, Hour = 60 * Min;
            var t = CityTuning;
            t.Rarity = new[]
            {
                new BuildingRarityTuning { BuildOre = 40,    BuildBiomass = 60,    BuildMs = 1 * Min,   UpgradeOre = 30,   UpgradeBiomass = 50,   UpgradeMs = 2 * Min,  WorkerSlots = 1, Copies = 3, Width = 1, Height = 1 },
                new BuildingRarityTuning { BuildOre = 200,   BuildBiomass = 300,   BuildMs = 10 * Min,  UpgradeOre = 120,  UpgradeBiomass = 200,  UpgradeMs = 8 * Min,  WorkerSlots = 2, Copies = 3, Width = 2, Height = 1 },
                new BuildingRarityTuning { BuildOre = 800,   BuildBiomass = 1200,  BuildMs = 1 * Hour,  UpgradeOre = 500,  UpgradeBiomass = 800,  UpgradeMs = 30 * Min, WorkerSlots = 2, Copies = 2, Width = 2, Height = 2 },
                new BuildingRarityTuning { BuildOre = 3000,  BuildBiomass = 4500,  BuildMs = 4 * Hour,  UpgradeOre = 1800, UpgradeBiomass = 2800, UpgradeMs = 2 * Hour, WorkerSlots = 2, Copies = 2, Width = 3, Height = 2 },
                new BuildingRarityTuning { BuildOre = 10000, BuildBiomass = 15000, BuildMs = 12 * Hour, UpgradeOre = 6000, UpgradeBiomass = 9000, UpgradeMs = 6 * Hour, WorkerSlots = 3, Copies = 1, Width = 3, Height = 3 },
            };
            t.PopOdds = new[]
            {
                new double[] { 100, 0, 0 }, // (unused: stars start at 1)
                new double[] { 100, 0, 0 }, new double[] { 100, 0, 0 }, new double[] { 100, 0, 0 }, new double[] { 100, 0, 0 },
                new double[] { 90, 10, 0 }, new double[] { 85, 15, 0 }, new double[] { 80, 20, 0 }, new double[] { 75, 25, 0 },
                new double[] { 70, 30, 0 }, new double[] { 70, 20, 10 },
            };

            // ---- excavation curve (City design §4, dig table): dig number → cost and timer ----
            void Dig(int day, long bio, long ore, long cores, long ms) => t.Digs.Add(new CityDigDef { TargetDay = day, Biomass = bio, Ore = ore, GearCores = cores, Ms = ms });
            Dig(1, 2500, 750, 0, 1 * Min);
            Dig(2, 4000, 1200, 0, 5 * Min);
            Dig(3, 6000, 1500, 0, 15 * Min);
            Dig(5, 8000, 2500, 0, 30 * Min);
            Dig(7, 12000, 3500, 0, 1 * Hour);
            Dig(10, 15000, 5000, 0, 2 * Hour);
            Dig(14, 20000, 7000, 0, 3 * Hour);
            Dig(19, 35000, 10000, 0, 4 * Hour);
            Dig(25, 45000, 12000, 0, 6 * Hour);
            Dig(32, 60000, 15000, 500, 8 * Hour);
            Dig(40, 75000, 25000, 1000, 8 * Hour);
            Dig(50, 100000, 30000, 1500, 10 * Hour);
            Dig(61, 120000, 45000, 2000, 12 * Hour);
            Dig(73, 150000, 50000, 3000, 12 * Hour);
            Dig(86, 200000, 70000, 4000, 16 * Hour);
            Dig(100, 300000, 80000, 5000, 16 * Hour);
            Dig(116, 350000, 100000, 6000, 20 * Hour);
            Dig(134, 400000, 120000, 8000, 20 * Hour);
            Dig(155, 500000, 150000, 10000, 24 * Hour);
            Dig(178, 600000, 150000, 12000, 24 * Hour);

            BuildCityMap();

            // ---- production (11) ----
            var farm = CityProduction("bp_biomass_farm", "Biomass Farm", Rarity.Common, BuildingCostType.Biomass, Currencies.Biomass, 30,
                Skill(PlacementKind.Neighbours, 10, 20, "Fields: +10% output per touching farm (max +20%).", "tag:farm"));
            farm.Tags.Add("farm");
            var grain = CityProduction("bp_grain_farm", "Grain Farm", Rarity.Common, BuildingCostType.Biomass, Currencies.Grain, 40,
                Skill(PlacementKind.OpenGround, 20, 20, "+20% output with 2+ empty land tiles touching.", null, 2));
            grain.Tags.Add("farm");
            CityProduction("bp_ore_mine", "Ore Mine", Rarity.Common, BuildingCostType.Ore, Currencies.Ore, 40,
                Skill(PlacementKind.Neighbours, 10, 20, "+10% output per touching Ore Mine (max +20%).", "bp_ore_mine"));
            CityProduction("bp_fuel_refinery", "Fuel Refinery", Rarity.Rare, BuildingCostType.Ore, Currencies.Fuel, 15,
                Skill(PlacementKind.Outskirts, 20, 20, "Outskirts: +20% output if 6+ tiles from the fireplace.", null, 6));
            CityProduction("bp_gear_works", "Gear Works", Rarity.Rare, BuildingCostType.Ore, Currencies.GearCores, 2,
                Skill(PlacementKind.Neighbours, 10, 30, "+10% output per touching Ore Mine (max +30%).", "bp_ore_mine"));
            CityProduction("bp_training_grounds", "Training Grounds", Rarity.Rare, BuildingCostType.Biomass, Currencies.HeroXp, 30,
                Skill(PlacementKind.AcrossStreet, 20, 20, "+20% output facing a War Academy across a street.", "bp_war_academy"));
            CityProduction("bp_plate_foundry", "Plate Foundry", Rarity.Epic, BuildingCostType.Ore, Currencies.AdvancementPlates, 0.5,
                Skill(PlacementKind.RowOfThree, 20, 20, "+20% output in a row of 3 production buildings.", "cat:Production"));
            CityProduction("bp_diamond_mine", "Diamond Mine", Rarity.Epic, BuildingCostType.Ore, Currencies.Diamonds, 1,
                Skill(PlacementKind.Cliffside, 30, 30, "+30% output on the land edge."));
            CityPullBuilding("bp_token_relay", "Blueprint Relay", BuildingCostType.Both, Currencies.BlueprintTokens, "Research Scroll",
                "Makes Research Scrolls: sealed blueprint scrolls from the archive. Pop one for 1 to X Blueprint pulls.",
                Skill(PlacementKind.AcrossStreet, 30, 30, "+30% scroll speed facing a Signal Beacon across a street.", "bp_signal_beacon"));
            CityPullBuilding("bp_signal_beacon", "Alien Signal Beacon", BuildingCostType.Both, Currencies.AlienTokens, "Incoming Transmission",
                "Makes Incoming Transmissions: alien signals caught by the dish. Pop one for 1 to X Alien pulls.",
                Skill(PlacementKind.Cliffside, 30, 30, "+30% transmission speed on the land edge."));
            CityPullBuilding("bp_part_fabricator", "Part Fabricator", BuildingCostType.Ore, Currencies.PartTokens, "Meteor Drop",
                "Makes Meteor Drops: meteors that fall next to the fabricator and hold salvage. Pop one for 1 to X Part pulls.",
                Skill(PlacementKind.Neighbours, 15, 30, "+15% meteor speed per touching Gear Works (max +30%).", "bp_gear_works"));

            // ---- housing (3) ----
            var pod = CityDef(LaunchPodId, "Launch Pod", Rarity.Common, BuildingCategory.Housing, BuildingCostType.Biomass,
                "Your expedition squad lives here: the aliens you put in the Launch Pod join your runs. More stars open more seats.", null);
            pod.Copies = 1;
            pod.WorkerSlots = 1; // = squad slots
            pod.Milestones.Add(new StarMilestone { Stars = 5, Kind = BoonKind.SquadSlots, Value = 1, Description = "+1 squad slot" });
            pod.Milestones.Add(new StarMilestone { Stars = 10, Kind = BoonKind.SquadSlots, Value = 1, Description = "+1 squad slot" });
            pod.Boons.Add(new BuildingBoon { Kind = BoonKind.AlienDamagePct, Base = 10, GrowsWithStars = false });
            var dome = CityDef("bp_barracks_dome", "Barracks Dome", Rarity.Legendary, BuildingCategory.Housing, BuildingCostType.Both,
                "A fortified alien dome: stronger aliens in runs.",
                Skill(PlacementKind.Aura, 10, 10, "Aura: aliens working in touching buildings get +10% work power."));
            dome.Boons.Add(new BuildingBoon { Kind = BoonKind.AlienDamagePct, Base = 5 });
            var spire = CityDef("bp_command_spire", "Command Spire", Rarity.Mythic, BuildingCategory.Housing, BuildingCostType.Both,
                "The worm's seat of rule: more production in every building.",
                Skill(PlacementKind.TouchPlaza, 30, 30, "Touching the plaza: +30% on its production bonus."));
            spire.Boons.Add(new BuildingBoon { Kind = BoonKind.ProductionPct, Base = 5, Placement = true });

            // ---- utility (5) ----
            var pen = CityDef("bp_breeding_pen", "Breeding Pen", Rarity.Rare, BuildingCategory.Utility, BuildingCostType.Biomass,
                "Breed two aliens into a new generation.",
                Skill(PlacementKind.Neighbours, 20, 20, "Breeding 20% faster touching a Housing building.", "cat:Housing"));
            pen.Boons.Add(new BuildingBoon { Kind = BoonKind.BreedingSlots, Base = 1, GrowsWithLevel = false, GrowsWithStars = false, Whole = true });
            pen.Boons.Add(new BuildingBoon { Kind = BoonKind.BreedingSpeedPct, Base = 10, Placement = true });
            pen.Milestones.Add(new StarMilestone { Stars = 5, Kind = BoonKind.BreedingSlots, Value = 1, Description = "+1 breeding slot" });
            pen.Milestones.Add(new StarMilestone { Stars = 10, Kind = BoonKind.BreedingSlots, Value = 1, Description = "+1 breeding slot" });
            var geneLab = CityDef("bp_gene_lab", "Gene Lab", Rarity.Epic, BuildingCategory.Utility, BuildingCostType.Biomass,
                "Better newborns: higher gene quality and a chance of better traits.",
                Skill(PlacementKind.Neighbours, 20, 20, "+20% gene quality touching a Breeding Pen.", "bp_breeding_pen"));
            geneLab.Boons.Add(new BuildingBoon { Kind = BoonKind.BreedingQualityPct, Base = 5, Placement = true });
            var lab = CityDef(LaboratoryId, "Laboratory", Rarity.Mythic, BuildingCategory.Utility, BuildingCostType.Both,
                "Unlocks skill research: research stays locked until this building is placed. Stars add research slots; levels speed research up.", null);
            lab.Boons.Add(new BuildingBoon { Kind = BoonKind.ResearchSlots, Base = 1, GrowsWithLevel = false, GrowsWithStars = false, Whole = true });
            lab.Boons.Add(new BuildingBoon { Kind = BoonKind.ResearchSpeedPct, Base = 10, GrowsWithStars = false });
            lab.Milestones.Add(new StarMilestone { Stars = 5, Kind = BoonKind.ResearchSlots, Value = 1, Description = "+1 research slot" });
            lab.Milestones.Add(new StarMilestone { Stars = 10, Kind = BoonKind.ResearchSlots, Value = 1, Description = "+1 research slot" });
            var obs = CityDef("bp_observatory", "Observatory", Rarity.Rare, BuildingCategory.Utility, BuildingCostType.Both,
                "Charts the stars: more XP in runs.",
                Skill(PlacementKind.Cliffside, 20, 20, "+20% run XP on the land edge."));
            obs.Boons.Add(new BuildingBoon { Kind = BoonKind.RunXpPct, Base = 10, Placement = true });
            BuildShardReactor(); // Mythic: excess shards → shards of a rarer item of the same type (ContentDb.Reactor.cs)

            // ---- boons (6) ----
            var well = CityDef("bp_stamina_well", "Stamina Well", Rarity.Rare, BuildingCategory.Boon, BuildingCostType.Biomass,
                "Raises max Stamina.",
                Skill(PlacementKind.Hearth, 20, 20, "Hearth: +20% Stamina within 3 tiles of the fireplace.", null, 3));
            well.Boons.Add(new BuildingBoon { Kind = BoonKind.StaminaCap, Base = 5, Placement = true, Whole = true });
            var regen = CityDef("bp_regen_spire", "Regen Spire", Rarity.Epic, BuildingCategory.Boon, BuildingCostType.Biomass,
                "Stamina regenerates faster.",
                Skill(PlacementKind.OpenGround, 20, 20, "+20% regen with 3+ empty land tiles touching.", null, 3));
            regen.Boons.Add(new BuildingBoon { Kind = BoonKind.StaminaRegenPct, Base = 10, Placement = true });
            var depot = CityDef("bp_supply_depot", "Supply Depot", Rarity.Common, BuildingCategory.Boon, BuildingCostType.Both,
                "Start every run with extra run coins.",
                Skill(PlacementKind.Hearth, 20, 20, "Hearth: +20% run coins within 3 tiles of the fireplace.", null, 3));
            depot.Boons.Add(new BuildingBoon { Kind = BoonKind.RunStartCoins, Base = 20, Placement = true, Whole = true });
            var scout = CityDef("bp_scout_post", "Scout Post", Rarity.Legendary, BuildingCategory.Boon, BuildingCostType.Both,
                "Scouts warn you: skip bad event outcomes each run.",
                Skill(PlacementKind.Outskirts, 0, 0, "Outskirts: +1 skip per run if 6+ tiles from the fireplace.", null, 6, 1));
            scout.Boons.Add(new BuildingBoon { Kind = BoonKind.SkipNegativeEvents, Base = 1, Placement = true, Whole = true });
            var academy = CityDef("bp_war_academy", "War Academy", Rarity.Legendary, BuildingCategory.Boon, BuildingCostType.Ore,
                "Extra draft rerolls every run.",
                Skill(PlacementKind.AcrossStreet, 0, 0, "+1 reroll facing Training Grounds across a street.", "bp_training_grounds", 0, 1));
            academy.Boons.Add(new BuildingBoon { Kind = BoonKind.DraftRerolls, Base = 1, Placement = true, Whole = true });
            // Mythic (owner, 2026-11-25: too strong stacked): one copy, 3×3; 10% → 30% at max, ≤ 39% with the +30% placement cap.
            var stasis = CityDef("bp_stasis_beacon", "Stasis Beacon", Rarity.Mythic, BuildingCategory.Boon, BuildingCostType.Both,
                "Slows the boss awakening meter.",
                Skill(PlacementKind.Outskirts, 30, 30, "Outskirts: +30% if 6+ tiles from the fireplace.", null, 6));
            stasis.Boons.Add(new BuildingBoon { Kind = BoonKind.AwakeningSlowPct, Base = 10, Placement = true });
        }

        /// <summary>Parses <see cref="CityMap"/> into the start land, the 20 patches and their terrain features.</summary>
        private void BuildCityMap()
        {
            var t = CityTuning;
            t.GridHeight = CityMap.Length;
            t.GridWidth = CityMap[0].Length;
            for (int i = 0; i < 20; i++)
                CityPatches.Add(new CityPatchDef { Id = "patch_" + (char)('a' + i), Name = CityPatchNames[i] });
            for (int y = 0; y < t.GridHeight; y++)
                for (int x = 0; x < t.GridWidth; x++)
                {
                    char ch = CityMap[y][x];
                    int cell = y * t.GridWidth + x;
                    if (ch == '0') t.StartCells.Add(cell);
                    else if (ch >= 'A' && ch <= 'T') CityPatches[ch - 'A'].Cells.Add(cell);
                }
            // 10 feature tiles: 4 Crystal Outcrops, 4 Springs, 2 Old Ruins (obstacles only).
            void F(char patch, string kind, int x, int y) => CityPatches[patch - 'A'].Features.Add(new CityFeatureDef { Kind = kind, X = x, Y = y });
            F('F', "crystal", 6, 13); F('K', "crystal", 8, 15); F('O', "crystal", 13, 3); F('R', "crystal", 16, 8);
            F('H', "spring", 12, 13); F('I', "spring", 4, 5); F('M', "spring", 15, 12); F('Q', "spring", 2, 5);
            F('P', "ruin", 9, 17); F('T', "ruin", 8, 1);
        }

        // ---- content helpers (city) ----------------------------------------------------------

        private static PlacementSkillDef Skill(PlacementKind kind, double pct, double max, string text, string target = null, int count = 0, double flat = 0) =>
            new PlacementSkillDef { Kind = kind, Pct = pct, MaxPct = max, Text = text, Target = target, Count = count, Flat = flat };

        private BlueprintDef CityDef(string id, string name, Rarity rarity, BuildingCategory cat, BuildingCostType cost, string description, PlacementSkillDef placement)
        {
            var r = CityTuning.Rarity[(int)rarity];
            var d = new BlueprintDef
            {
                Id = id, Name = name, Rarity = rarity, Category = cat, CostType = cost, Description = description, Placement = placement,
                Width = r.Width, Height = r.Height, Copies = r.Copies, WorkerSlots = 1,
            };
            Blueprints.Add(id, d);
            return d;
        }

        private BlueprintDef CityProduction(string id, string name, Rarity rarity, BuildingCostType cost, string currency, double ratePerHour, PlacementSkillDef placement)
        {
            var d = CityDef(id, name, rarity, BuildingCategory.Production, cost, "Produces " + currency + " and stores 8 hours of it.", placement);
            d.ProduceCurrency = currency;
            d.BaseRatePerHour = ratePerHour;
            d.WorkerSlots = CityTuning.Rarity[(int)rarity].WorkerSlots;
            d.Milestones.Add(new StarMilestone { Stars = 5, Kind = BoonKind.WorkerSlots, Value = 1, Description = "+1 worker slot" });
            return d;
        }

        private void CityPullBuilding(string id, string name, BuildingCostType cost, string token, string chanceName, string description, PlacementSkillDef placement)
        {
            var d = CityDef(id, name, Rarity.Legendary, BuildingCategory.Production, cost, description, placement);
            d.Pull = new PullChanceDef { TokenCurrency = token, ChanceName = chanceName };
            d.WorkerSlots = CityTuning.Rarity[(int)Rarity.Legendary].WorkerSlots;
            d.Milestones.Add(new StarMilestone { Stars = 5, Kind = BoonKind.WorkerSlots, Value = 1, Description = "+1 worker slot" });
        }
    }
}
