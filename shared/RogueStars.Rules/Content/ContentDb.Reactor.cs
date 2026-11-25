using System;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>
    /// The Shard Reactor (docs/design/pull-system.md, "Shard Reactor"): a Mythic building that turns excess shards into shards of a
    /// HIGHER rarity of the SAME type, of an item the player picks (alien shards → a higher-rarity alien's shards; blueprint shards → a
    /// higher-rarity blueprint's shards). One job per working alien (worker slots: 1, +1 at 5★, +1 at 10★ = up to 3 parallel jobs).
    /// A job converts Units × <see cref="OutputPerUnit"/> target shards; per target shard it eats
    /// <see cref="RatioByGap"/>[rarity gap] source shards, and per unit fuel (Grain for aliens, Ore for blueprints) and time by the
    /// target's rarity. Job speed is fixed; timers take speed-ups like any city timer.
    /// </summary>
    [Serializable]
    public sealed class ShardReactorDef
    {
        public string BlueprintId;
        /// <summary>Target shards one unit makes.</summary>
        public long OutputPerUnit = 10;
        /// <summary>Units one job may hold (1..MaxUnits).</summary>
        public int MaxUnits = 10;
        /// <summary>Source shards per target shard by rarity gap (index = target rarity − source rarity, 1..4): ×4 per tier.</summary>
        public long[] RatioByGap = { 0, 4, 16, 64, 256 };
        /// <summary>Grain per unit of an alien job, by target rarity (index: Common unused … Mythic).</summary>
        public long[] AlienFuelPerUnit = { 0, 50, 150, 400, 1000 };
        /// <summary>Ore per unit of a blueprint job, by target rarity.</summary>
        public long[] BlueprintFuelPerUnit = { 0, 300, 800, 2000, 5000 };
        /// <summary>Minutes per unit by target rarity (before the level speed-up).</summary>
        public long[] MinutesPerUnit = { 0, 20, 45, 90, 180 };
    }

    public sealed partial class ContentDb
    {
        public const string ShardReactorId = "bp_shard_reactor";
        public readonly ShardReactorDef ShardReactor = new ShardReactorDef { BlueprintId = ShardReactorId };

        /// <summary>The Shard Reactor building (called from BuildCity; City owns it). Job speed is fixed: levels and stars do not change it.</summary>
        private void BuildShardReactor()
        {
            var r = CityDef(ShardReactorId, "Shard Reactor", Rarity.Mythic, BuildingCategory.Utility, BuildingCostType.Both,
                "Fuses spare shards into shards of a rarer alien or blueprint you pick. One job per working alien.", null);
            r.WorkerSlots = 1;
            r.Milestones.Add(new StarMilestone { Stars = 5, Kind = BoonKind.WorkerSlots, Value = 1, Description = "+1 worker slot (2 jobs)" });
            r.Milestones.Add(new StarMilestone { Stars = 10, Kind = BoonKind.WorkerSlots, Value = 1, Description = "+1 worker slot (3 jobs)" });
        }
    }
}
