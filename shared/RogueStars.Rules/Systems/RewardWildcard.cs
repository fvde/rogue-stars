using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Item rewards with <c>Id = "*"</c> mean "a random def of <see cref="Reward.Rarity"/>" (run drops). The owning
    /// granters (Part/Rune: Progression, Blueprint/Alien: City) resolve them here, one pick per copy, with the
    /// persistent ctx.Rng over ordinal-sorted content ids (deterministic).
    /// For parts the rarity is a <see cref="Rarity"/> tier (Common..Mythic) mapped to the part ladder like the
    /// Part Signal does; runes, blueprints and aliens use the rarity index directly.
    /// </summary>
    public static class RewardWildcard
    {
        public const string Any = "*";

        public static bool IsWildcard(Reward r) => r != null && r.Id == Any;

        /// <summary>Random id among <paramref name="defs"/> whose rarity matches; falls back to the closest lower rarity.</summary>
        public static string Pick<T>(Rng rng, Dictionary<string, T> defs, Func<T, int> rarityOf, int rarity)
        {
            for (int r = rarity; r >= 0; r--)
            {
                var ids = new List<string>();
                foreach (var kv in defs) if (rarityOf(kv.Value) == r) ids.Add(kv.Key);
                if (ids.Count == 0) continue;
                ids.Sort(StringComparer.Ordinal);
                return ids[rng.Range(0, ids.Count)];
            }
            var all = new List<string>(defs.Keys);
            GameEngine.Require(all.Count > 0, "no content for wildcard reward");
            all.Sort(StringComparer.Ordinal);
            return all[rng.Range(0, all.Count)];
        }

        /// <summary>Random id among all <paramref name="defs"/> (rarity is carried by the item itself, e.g. parts/runes).</summary>
        public static string PickAny<T>(Rng rng, Dictionary<string, T> defs) => Pick(rng, defs, _ => 0, 0);

        /// <summary>Part ladder rarity for a gacha/drop tier (same mapping as the Part Signal pools).</summary>
        public static int PartRarityForTier(int tier)
        {
            switch ((Rarity)Math.Max(0, Math.Min(4, tier)))
            {
                case Rarity.Rare: return (int)PartRarity.Rare;
                case Rarity.Epic: return (int)PartRarity.Epic;
                case Rarity.Legendary: return (int)PartRarity.Legendary;
                case Rarity.Mythic: return (int)PartRarity.Mythic;
                default: return (int)PartRarity.Common;
            }
        }
    }
}
