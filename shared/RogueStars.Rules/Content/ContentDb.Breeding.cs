using System;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    [Serializable]
    public sealed class BreedingTuningDef
    {
        /// <summary>Grain cost by the higher parent rarity.</summary>
        public long[] GrainCost = { 200, 500, 1200, 3000, 8000 };
        /// <summary>Breeding duration in hours by the higher parent rarity.</summary>
        public double[] DurationHours = { 2, 4, 8, 12, 24 };
        /// <summary>Child gene = lerp(parentA, parentB, u) ± GeneVariance + quality bonus, clamped to AlienTuning gene bounds.</summary>
        public double GeneVariance = 8;
        /// <summary>Chance each parent trait is inherited (+ InheritPerQuality per quality point, percent points).</summary>
        public double TraitInheritChance = 0.5;
        public double InheritPerQuality = 0.01;
        /// <summary>Chance of a mutation (new random trait) (+ MutationPerQuality per quality point).</summary>
        public double MutationChance = 0.08;
        public double MutationPerQuality = 0.005;
        /// <summary>Discarding a child: shards of its type by rarity and this percent of the Grain cost back.</summary>
        public long[] DiscardShards = { 1, 1, 2, 2, 3 };
        public double DiscardGrainRefundPct = 30;
        /// <summary>Max born children waiting for a keep/discard choice.</summary>
        public int MaxPending = 3;
    }

    public sealed partial class ContentDb
    {
        public readonly BreedingTuningDef BreedingTuning = new BreedingTuningDef();

        partial void BuildBreeding()
        {
            // Tuning values are the defaults in BreedingTuningDef; the Breeding Pen / Gene Lab buildings
            // (ContentDb.City.cs) provide slots, speed and quality.
        }
    }
}
