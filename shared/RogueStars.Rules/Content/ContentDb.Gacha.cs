using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>The three pull types (signals).</summary>
    public enum GachaType { Blueprint, Part, Alien }

    /// <summary>One possible pull outcome inside a rarity tier.</summary>
    [Serializable]
    public sealed class GachaEntry
    {
        public string Kind;
        public string Id;
        public long Amount = 1;
        /// <summary>Reward.Rarity passed to the granter (PartRarity index for parts, Core.Rarity otherwise).</summary>
        public int Rarity;
        public double Weight = 1;

        public GachaEntry() { }
        public GachaEntry(string kind, string id, long amount, int rarity, double weight) { Kind = kind; Id = id; Amount = amount; Rarity = rarity; Weight = weight; }
    }

    [Serializable]
    public sealed class GachaTierDef
    {
        public Rarity Tier;
        /// <summary>Base weight of the tier; published odds = weight / sum of weights.</summary>
        public double Weight;
        public List<GachaEntry> Pool = new List<GachaEntry>();
    }

    [Serializable]
    public sealed class GachaTypeDef
    {
        public GachaType Type;
        public string Name;
        public string TokenCurrency;
        /// <summary>Meta ad placement for the daily extra free single pull (GachaPayment.Ad).</summary>
        public string AdPlacementId;
        public long TokenCost1 = 1;
        public long TokenCost10 = 10;
        public long DiamondCost1;
        public long DiamondCost10;
        /// <summary>Ordered Common → Mythic.</summary>
        public List<GachaTierDef> Tiers = new List<GachaTierDef>();
        /// <summary>Hidden: the N-th pull without a Mythic is a Mythic.</summary>
        public int MythicHardPity = 50;
        /// <summary>Hidden: after this many pulls without Legendary+, the Legendary weight grows by SoftPityStep per pull.</summary>
        public int LegendarySoftPityStart = 25;
        public double LegendarySoftPityStep = 2.5;
        /// <summary>Hidden: the N-th pull without Legendary+ is at least Legendary.</summary>
        public int LegendaryHardPity = 40;
        /// <summary>A 10-pull always contains at least one result of this tier or better.</summary>
        public Rarity TenPullMinTier = Rarity.Epic;
    }

    /// <summary>A featured mythic item (themed banner).</summary>
    [Serializable]
    public sealed class BannerTheme
    {
        public string Id;
        public string Name;
        public string FeaturedKind;
        public string FeaturedId;
        public long FeaturedAmount = 1;
        public int FeaturedRarity;
        /// <summary>Share of Mythic outcomes that are the featured item (0..1).</summary>
        public double FeaturedShare = 0.5;
    }

    /// <summary>Endless weekly rotation of themes for one pull type: theme index = period mod theme count.</summary>
    [Serializable]
    public sealed class BannerRotation
    {
        public GachaType Type;
        public long EpochMs;
        public long DurationMs;
        public List<BannerTheme> Themes = new List<BannerTheme>();
    }

    /// <summary>An explicitly scheduled banner (live ops); overrides the rotation while active.</summary>
    [Serializable]
    public sealed class BannerDef
    {
        public string Id;
        public GachaType Type;
        public BannerTheme Theme;
        public long StartMs;
        public long EndMs;

        public bool ActiveAt(long nowMs) => nowMs >= StartMs && nowMs < EndMs;
    }

    [Serializable]
    public sealed class GachaConfig
    {
        public Dictionary<GachaType, GachaTypeDef> Types = new Dictionary<GachaType, GachaTypeDef>();
        public List<BannerRotation> Rotations = new List<BannerRotation>();
        public List<BannerDef> ScheduledBanners = new List<BannerDef>();
    }

    public sealed partial class ContentDb
    {
        /// <summary>Pull types, pools, hidden pity, banner rotations (Progression).</summary>
        public readonly GachaConfig GachaConfig = new GachaConfig();

        /// <summary>Ids of a City-owned content dictionary with the given rarity, ordinal order (pool order is part of determinism).</summary>
        private static List<string> GachaIdsOfRarity<T>(Dictionary<string, T> defs, Func<T, Rarity> rarity, Rarity r)
        {
            var ids = new List<string>();
            foreach (var id in GachaSortedKeys(defs)) if (rarity(defs[id]) == r) ids.Add(id);
            return ids;
        }

        partial void BuildGacha()
        {
            var g = GachaConfig;
            double[] tierWeights = { 60, 28, 9.5, 2.0, 0.5 };
            int[] partRarityForTier = { (int)PartRarity.Common, (int)PartRarity.Rare, (int)PartRarity.Epic, (int)PartRarity.Legendary, (int)PartRarity.Mythic };
            long[] heroShardsForTier = { 2, 5, 10, 25, 60 };

            var part = NewGachaType(GachaType.Part, "Part Signal", Currencies.PartTokens);
            var bp = NewGachaType(GachaType.Blueprint, "Blueprint Signal", Currencies.BlueprintTokens);
            var alien = NewGachaType(GachaType.Alien, "Alien Signal", Currencies.AlienTokens);
            for (int t = 0; t < 5; t++)
            {
                var pt = new GachaTierDef { Tier = (Rarity)t, Weight = tierWeights[t] };
                foreach (var id in GachaSortedKeys(PartsConfig.Defs)) pt.Pool.Add(new GachaEntry(RewardKinds.Part, id, 1, partRarityForTier[t], 2));
                if (t < 4) foreach (var id in GachaSortedKeys(PartsConfig.Runes)) pt.Pool.Add(new GachaEntry(RewardKinds.Rune, id, 1, t, 2));
                part.Tiers.Add(pt);

                // Every City blueprint and alien type is pullable in the tier of its rarity (City owns the granters).
                var bt = new GachaTierDef { Tier = (Rarity)t, Weight = tierWeights[t] };
                foreach (var id in GachaIdsOfRarity(Blueprints, d => d.Rarity, (Rarity)t)) bt.Pool.Add(new GachaEntry(RewardKinds.Blueprint, id, 1, t, 1));
                bp.Tiers.Add(bt);

                var at = new GachaTierDef { Tier = (Rarity)t, Weight = tierWeights[t] };
                var alienIds = GachaIdsOfRarity(AlienTypes, d => d.Rarity, (Rarity)t);
                foreach (var id in alienIds) at.Pool.Add(new GachaEntry(RewardKinds.Alien, id, 1, t, 2));
                // Hero shards: ~40% of alien-signal outcomes, spread over all heroes.
                var heroes = GachaSortedKeys(HeroConfig.Defs);
                double heroWeight = 2.0 * Math.Max(1, alienIds.Count) * (40.0 / 60.0) / heroes.Count;
                foreach (var id in heroes) at.Pool.Add(new GachaEntry(RewardKinds.HeroShards, id, heroShardsForTier[t], t, heroWeight));
                alien.Tiers.Add(at);
            }

            const long epoch = 1_799_020_800_000; // Monday 2027-01-04 00:00 UTC
            const long week = 7L * 86_400_000;
            int mythic = (int)Rarity.Mythic, mythicPart = (int)PartRarity.Mythic;
            g.Rotations.Add(new BannerRotation
            {
                Type = GachaType.Part, EpochMs = epoch, DurationMs = week,
                Themes =
                {
                    GachaThemeOf("part_inferno_arsenal", "Inferno Arsenal", RewardKinds.Part, "arm_plasma_blaster", 1, mythicPart),
                    GachaThemeOf("part_glacier_vault", "Glacier Vault", RewardKinds.Part, "arm_frost_cannon", 1, mythicPart),
                    GachaThemeOf("part_storm_works", "Storm Works", RewardKinds.Part, "arm_tesla_whip", 1, mythicPart),
                    GachaThemeOf("part_shadow_forge", "Shadow Forge", RewardKinds.Part, "arm_mantis_claw", 1, mythicPart),
                },
            });
            g.Rotations.Add(new BannerRotation
            {
                Type = GachaType.Blueprint, EpochMs = epoch, DurationMs = week,
                Themes =
                {
                    GachaThemeOf("bp_command_spire_rising", "Command Spire Rising", RewardKinds.Blueprint, "bp_command_spire", 1, mythic),
                    GachaThemeOf("bp_reliquary_week", "Reliquary Week", RewardKinds.Blueprint, "bp_ancient_reliquary", 1, mythic),
                },
            });
            g.Rotations.Add(new BannerRotation
            {
                Type = GachaType.Alien, EpochMs = epoch, DurationMs = week,
                Themes =
                {
                    GachaThemeOf("alien_nebula_court", "Nebula Court", RewardKinds.Alien, "alien_nebulisk", 1, mythic),
                    GachaThemeOf("alien_knights_call", "Knight's Call", RewardKinds.HeroShards, "hero_star_knight", 80, mythic),
                    GachaThemeOf("alien_chefs_banquet", "Chef's Banquet", RewardKinds.HeroShards, "hero_chef", 80, mythic),
                    GachaThemeOf("alien_noodle_dojo", "Noodle Dojo", RewardKinds.HeroShards, "hero_noodle_ninja", 80, mythic),
                },
            });
        }

        private GachaTypeDef NewGachaType(GachaType type, string name, string token)
        {
            var d = new GachaTypeDef
            {
                Type = type, Name = name, TokenCurrency = token, TokenCost1 = 1, TokenCost10 = 10,
                AdPlacementId = "free_pull_" + type.ToString().ToLowerInvariant(),
                DiamondCost1 = 150, DiamondCost10 = 1350,
                MythicHardPity = 50, LegendarySoftPityStart = 25, LegendarySoftPityStep = 2.5, LegendaryHardPity = 40,
                TenPullMinTier = Rarity.Epic,
            };
            GachaConfig.Types[type] = d;
            return d;
        }

        private static BannerTheme GachaThemeOf(string id, string name, string kind, string featuredId, long amount, int rarity) =>
            new BannerTheme { Id = id, Name = name, FeaturedKind = kind, FeaturedId = featuredId, FeaturedAmount = amount, FeaturedRarity = rarity, FeaturedShare = 0.5 };

        private static List<string> GachaSortedKeys<T>(Dictionary<string, T> d)
        {
            var k = new List<string>(d.Keys);
            k.Sort(StringComparer.Ordinal);
            return k;
        }
    }
}
