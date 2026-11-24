using System;

namespace RogueStars.Rules.Content
{
    /// <summary>
    /// The one star system of every pulled item (docs/design/pull-system.md): heroes, mounts, aliens and blueprints of every rarity
    /// are shard based and go to 10★ on the same table. Winning an item in a pull (a "batch") pays <see cref="BatchShards"/> of its
    /// shards; the first batch of an item the player does not own unlocks it (the unlock costs exactly one batch). Parts keep their
    /// own merge ladder (docs/design/parts.md).
    /// </summary>
    [Serializable]
    public sealed class ShardStarConfig
    {
        public int MaxStars = 10;
        /// <summary>Shards one won item pays (pulls never pay smaller amounts).</summary>
        public long BatchShards = 50;
        /// <summary>StepCosts[s] = shards to reach star s (index 1 = unlock at 1★, index 0 unused): 1 000 shards from nothing to 10★.</summary>
        public long[] StepCosts = { 0, 50, 50, 50, 100, 100, 100, 100, 150, 150, 150 };

        /// <summary>Shards that unlock an item (1★).</summary>
        public long UnlockShards => StepCosts[1];

        /// <summary>Shards to go from <paramref name="stars"/> to the next star (0 = unlock), or -1 at max.</summary>
        public long NextCost(int stars) => stars < 0 ? StepCosts[1] : stars >= MaxStars || stars + 1 >= StepCosts.Length ? -1 : StepCosts[stars + 1];

        /// <summary>Shards from nothing to <paramref name="stars"/> (0 → 0).</summary>
        public long TotalTo(int stars)
        {
            long t = 0;
            for (int i = 1; i <= stars && i < StepCosts.Length; i++) t += StepCosts[i];
            return t;
        }

        /// <summary>Shards still needed from <paramref name="stars"/> (0 = not owned) to max stars.</summary>
        public long Remaining(int stars) => TotalTo(MaxStars) - TotalTo(Math.Max(0, stars));
    }

    public sealed partial class ContentDb
    {
        /// <summary>The shared star table of heroes, mounts, aliens and blueprints (Progression + City).</summary>
        public readonly ShardStarConfig ShardStars = new ShardStarConfig();
    }
}
