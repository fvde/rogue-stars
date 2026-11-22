using System;
using System.Collections.Generic;

namespace RogueStars.Rules.Core
{
    /// <summary>
    /// Deterministic random number generator (SplitMix64 seeding + xoshiro256**).
    /// Identical sequences on client and server for the same seed; never use System.Random in rules.
    /// </summary>
    [Serializable]
    public sealed class Rng
    {
        public ulong S0, S1, S2, S3;

        public Rng() : this(0x9E3779B97F4A7C15UL) { }

        public Rng(ulong seed)
        {
            ulong x = seed;
            S0 = SplitMix(ref x); S1 = SplitMix(ref x); S2 = SplitMix(ref x); S3 = SplitMix(ref x);
        }

        public static Rng FromSeed(long seed) => new Rng(unchecked((ulong)seed));

        /// <summary>Derive an independent stream for a sub-purpose (e.g. one battle) without consuming this one.</summary>
        public Rng Fork(string purpose)
        {
            ulong h = 1469598103934665603UL;
            foreach (char c in purpose) { h ^= c; h *= 1099511628211UL; }
            return new Rng(S0 ^ h ^ RotL(S2, 17));
        }

        private static ulong SplitMix(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static ulong RotL(ulong x, int k) => (x << k) | (x >> (64 - k));

        public ulong NextULong()
        {
            ulong result = RotL(S1 * 5, 7) * 9;
            ulong t = S1 << 17;
            S2 ^= S0; S3 ^= S1; S1 ^= S2; S0 ^= S3; S2 ^= t; S3 = RotL(S3, 45);
            return result;
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            ulong span = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextULong() % span);
        }

        /// <summary>True with the given probability (0..1).</summary>
        public bool Chance(double probability) => probability > 0 && NextDouble() < probability;

        public T Pick<T>(IList<T> items) => items[Range(0, items.Count)];

        /// <summary>Pick an index by non-negative weights; returns -1 if all weights are zero.</summary>
        public int PickWeighted(IList<double> weights)
        {
            double total = 0;
            for (int i = 0; i < weights.Count; i++) total += Math.Max(0, weights[i]);
            if (total <= 0) return -1;
            double roll = NextDouble() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= Math.Max(0, weights[i]);
                if (roll < 0) return i;
            }
            return weights.Count - 1;
        }

        public void Shuffle<T>(IList<T> items)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                T tmp = items[i]; items[i] = items[j]; items[j] = tmp;
            }
        }
    }
}
