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
        /// <summary>Visible, fixed pity ("Mythic x / 100"): the N-th pull without a Mythic is a Mythic (0 = none).</summary>
        public int MythicPity = 100;
        /// <summary>
        /// Visible, fixed pity ("Legendary x / 40"): the N-th pull without a Legendary-or-better result is at least Legendary (0 = none).
        /// That pull still rolls Mythic at the flat Mythic rate; every lower result becomes Legendary.
        /// </summary>
        public int LegendaryPity = 40;
        /// <summary>Visible 10-pull rule: a 10-pull contains at least one result of <see cref="TenPullMinTier"/> or better (false = none).</summary>
        public bool TenPullGuarantee = true;
        public Rarity TenPullMinTier = Rarity.Epic;
    }

    /// <summary>One featured item of a banner and the tier it is featured in (Legendary or Mythic).</summary>
    [Serializable]
    public sealed class BannerFeature
    {
        public string Kind;
        public string Id;
        public long Amount = 1;
        /// <summary>Reward.Rarity passed to the granter.</summary>
        public int Rarity;
        public Rarity Tier = Core.Rarity.Mythic;
    }

    /// <summary>
    /// A themed banner: its headline featured item (FeaturedKind / Id, in FeaturedTier) and optionally more featured items
    /// (<see cref="MoreFeatured"/>). Featured items are Legendary or Mythic: when a pull on the banner hits a tier that has featured
    /// items, FeaturedShare (80%) of those results are a featured item (split evenly), the rest another item of that tier (owner,
    /// 2026-11-25). The tier odds and the pity do not change.
    /// </summary>
    [Serializable]
    public sealed class BannerTheme
    {
        public string Id;
        public string Name;
        public string FeaturedKind;
        public string FeaturedId;
        public long FeaturedAmount = 1;
        public int FeaturedRarity;
        /// <summary>Tier the headline item is featured in (Mythic unless set).</summary>
        public Rarity FeaturedTier = Rarity.Mythic;
        /// <summary>Share of a featured tier's results that are a featured item (0..1).</summary>
        public double FeaturedShare = 0.8;
        /// <summary>Further featured items (e.g. the core blueprint banner's Legendary pull buildings).</summary>
        public List<BannerFeature> MoreFeatured = new List<BannerFeature>();

        /// <summary>The featured items of a tier (headline first), empty when the tier has none.</summary>
        public List<BannerFeature> FeaturedIn(Rarity tier)
        {
            var list = new List<BannerFeature>();
            if (!string.IsNullOrEmpty(FeaturedId) && FeaturedTier == tier)
                list.Add(new BannerFeature { Kind = FeaturedKind, Id = FeaturedId, Amount = FeaturedAmount, Rarity = FeaturedRarity, Tier = FeaturedTier });
            foreach (var f in MoreFeatured) if (f.Tier == tier) list.Add(f);
            return list;
        }

        public bool IsFeatured(string kind, string id)
        {
            if (FeaturedKind == kind && FeaturedId == id) return true;
            foreach (var f in MoreFeatured) if (f.Kind == kind && f.Id == id) return true;
            return false;
        }
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
        /// <summary>Pull types, pools, visible pity, banner rotations (Progression; docs/design/pull-system.md).</summary>
        public readonly GachaConfig GachaConfig = new GachaConfig();
        /// <summary>Theme id of the recurring core blueprint banner (Laboratory + the three pull buildings).</summary>
        public const string CoreBlueprintBannerId = "bp_core";

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
            // Pull system (docs/design/pull-system.md): Mythic a flat 1.2% with a visible pity at 100, Legendary a flat 3% with a
            // visible pity at 40 (rate × pity = 1.2 for both); the 1.7 points come out of Common. No soft or hidden pity.
            double[] tierWeights = { 58.3, 28, 9.5, 3.0, 1.2 };
            int[] partRarityForTier = { (int)PartRarity.Common, (int)PartRarity.Rare, (int)PartRarity.Epic, (int)PartRarity.Legendary, (int)PartRarity.Mythic };
            // Heroes and mounts are not in the Signals: summon events are the way to get them (ContentDb.SummonEvents,
            // docs/design/summon-events.md). Their old share of the outcomes is supplies: Ore / Grain in the low tiers (usable from
            // the Signal's unlock on), the type's summon tickets in Epic and Legendary. The Mythic tier holds Mythic items only
            // (pull system: a Mythic result is always a Mythic batch).
            long[] supplyForTier = { 300, 800, 1, 3, 0 };

            var part = NewGachaType(GachaType.Part, "Parts pull", Currencies.PartTokens);
            var bp = NewGachaType(GachaType.Blueprint, "Blueprints pull", Currencies.BlueprintTokens);
            var alien = NewGachaType(GachaType.Alien, "Aliens pull", Currencies.AlienTokens);
            for (int t = 0; t < 5; t++)
            {
                var pt = new GachaTierDef { Tier = (Rarity)t, Weight = tierWeights[t] };
                // parts v2: each tier draws uniformly from that rarity's item set (30 standard items per rarity, the 30 Mythics)
                foreach (var id in GachaSortedKeys(PartsConfig.Defs))
                    if ((int)PartsConfig.Defs[id].Rarity == partRarityForTier[t]) pt.Pool.Add(new GachaEntry(RewardKinds.Part, id, 1, partRarityForTier[t], 2));
                part.Tiers.Add(pt);

                // Every City blueprint and alien type is pullable in the tier of its rarity (City owns the granters); one result
                // is one batch of ShardStars.BatchShards shards (the first unlocks the item).
                var bt = new GachaTierDef { Tier = (Rarity)t, Weight = tierWeights[t] };
                var bpIds = GachaIdsOfRarity(Blueprints, d => d.Rarity, (Rarity)t);
                foreach (var id in bpIds) bt.Pool.Add(new GachaEntry(RewardKinds.Blueprint, id, 1, t, 1));
                if (supplyForTier[t] > 0)
                    bt.Pool.Add(new GachaEntry(RewardKinds.Currency, t < 2 ? Currencies.Ore : Currencies.MountTickets, supplyForTier[t], t, Math.Max(1, bpIds.Count) / 3.0));
                bp.Tiers.Add(bt);

                var at = new GachaTierDef { Tier = (Rarity)t, Weight = tierWeights[t] };
                var alienIds = GachaIdsOfRarity(AlienTypes, d => d.Rarity, (Rarity)t);
                foreach (var id in alienIds) at.Pool.Add(new GachaEntry(RewardKinds.Alien, id, 1, t, 2));
                if (supplyForTier[t] > 0)
                    at.Pool.Add(new GachaEntry(RewardKinds.Currency, t < 2 ? Currencies.Grain : Currencies.HeroTickets, supplyForTier[t], t, 2.0 * Math.Max(1, alienIds.Count) * (40.0 / 60.0)));
                alien.Tiers.Add(at);
            }

            const long epoch = 1_799_020_800_000; // Monday 2027-01-04 00:00 UTC
            const long week = 7L * 86_400_000;
            int mythic = (int)Rarity.Mythic, mythicPart = (int)PartRarity.Mythic;
            // Parts v2: all 30 Mythics at launch in 10 Part banners of 3 (one per synergy, its 3 Mythics in 3 different slots), one a
            // week (10-week cycle); 80% of Mythic results split between the 3 featured, 20% over the other 27.
            var partBanners = new BannerRotation { Type = GachaType.Part, EpochMs = epoch, DurationMs = week };
            foreach (var (bannerId, bannerName, synergy) in PartBanners)
            {
                var ids = new List<string>();
                foreach (var m in PartMythicData) if (m.Synergy == synergy) ids.Add(m.Id);
                var theme = GachaThemeOf(bannerId, bannerName, RewardKinds.Part, ids[0], 1, mythicPart);
                for (int i = 1; i < ids.Count; i++)
                    theme.MoreFeatured.Add(new BannerFeature { Kind = RewardKinds.Part, Id = ids[i], Amount = 1, Rarity = mythicPart, Tier = Rarity.Mythic });
                partBanners.Themes.Add(theme);
            }
            g.Rotations.Add(partBanners);
            // Blueprint banners (owner, 2026-11-25; docs/design/pull-system.md "Banner schedule"): weekly from Monday 00:00 UTC, a 6-week
            // cycle where the core blueprint banner returns every other week (Laboratory featured in Mythic, the three pull buildings
            // in Legendary; nothing else rotates into it) between the themed Mythic banners.
            var core = GachaThemeOf(CoreBlueprintBannerId, "Core Blueprints", RewardKinds.Blueprint, LaboratoryId, 1, mythic);
            foreach (var id in new[] { "bp_token_relay", "bp_signal_beacon", "bp_part_fabricator" })
                core.MoreFeatured.Add(new BannerFeature { Kind = RewardKinds.Blueprint, Id = id, Amount = 1, Rarity = (int)Rarity.Legendary, Tier = Rarity.Legendary });
            g.Rotations.Add(new BannerRotation
            {
                Type = GachaType.Blueprint, EpochMs = epoch, DurationMs = week,
                Themes =
                {
                    core,
                    GachaThemeOf("bp_command_spire_rising", "Command Spire Rising", RewardKinds.Blueprint, "bp_command_spire", 1, mythic),
                    core,
                    GachaThemeOf("bp_reactor_ignition", "Reactor Ignition", RewardKinds.Blueprint, ShardReactorId, 1, mythic),
                    core,
                    GachaThemeOf("bp_stasis_week", "Stasis Beacon Week", RewardKinds.Blueprint, "bp_stasis_beacon", 1, mythic),
                },
            });
            g.Rotations.Add(new BannerRotation
            {
                Type = GachaType.Alien, EpochMs = epoch, DurationMs = week,
                Themes =
                {
                    GachaThemeOf("alien_nebula_court", "Nebula Court", RewardKinds.Alien, "alien_nebulisk", 1, mythic),
                    GachaThemeOf("alien_swarm_season", "Swarm Season", RewardKinds.Alien, "alien_swarmheart", 1, mythic),
                },
            });
        }

        /// <summary>The 10 Part banners (parts v2, owner rev 3f): id, name, synergy (its 3 Mythics are featured).</summary>
        public static readonly (string Id, string Name, string Synergy)[] PartBanners =
        {
            ("part_inferno_arsenal", "Inferno Arsenal", "Fire"), ("part_glacier_vault", "Glacier Vault", "Ice"),
            ("part_storm_works", "Storm Works", "Lightning"), ("part_fallout_foundry", "Fallout Foundry", "Radiation"),
            ("part_shadow_forge", "Shadow Forge", "Bleed"), ("part_whirlwind_workshop", "Whirlwind Workshop", "Combo"),
            ("part_bulwark_armory", "Bulwark Armory", "Counter"), ("part_hive_hangar", "Hive Hangar", "Alien"),
            ("part_hourglass_depot", "Hourglass Depot", "Time"), ("part_proving_grounds", "Proving Grounds", "General"),
        };

        private GachaTypeDef NewGachaType(GachaType type, string name, string token)
        {
            var d = new GachaTypeDef
            {
                Type = type, Name = name, TokenCurrency = token, TokenCost1 = 1, TokenCost10 = 10,
                AdPlacementId = "free_pull_" + type.ToString().ToLowerInvariant(),
                DiamondCost1 = 150, DiamondCost10 = 1350,
                MythicPity = 100, LegendaryPity = 40, TenPullGuarantee = true, TenPullMinTier = Rarity.Epic,
            };
            GachaConfig.Types[type] = d;
            return d;
        }

        private static BannerTheme GachaThemeOf(string id, string name, string kind, string featuredId, long amount, int rarity) =>
            new BannerTheme { Id = id, Name = name, FeaturedKind = kind, FeaturedId = featuredId, FeaturedAmount = amount, FeaturedRarity = rarity, FeaturedTier = Rarity.Mythic, FeaturedShare = 0.8 };

        private static List<string> GachaSortedKeys<T>(Dictionary<string, T> d)
        {
            var k = new List<string>(d.Keys);
            k.Sort(StringComparer.Ordinal);
            return k;
        }
    }
}
