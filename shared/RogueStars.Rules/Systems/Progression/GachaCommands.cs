using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Ad: the daily extra free single pull of a rewarded ad (Meta placement GachaTypeDef.AdPlacementId). Free: one of the free 10×
    /// pulls of the type (GachaState.FreeTenPulls, Count must be 10), a normal 10× with no guarantee.
    /// </summary>
    public enum GachaPayment { Tokens, Diamonds, Ad, Free }

    /// <summary>
    /// Pull 1 or 10 times on a pull type. BannerId: empty = standard pool; otherwise must be a banner of
    /// this type active now (see GachaSystem.ActiveBanner). Payload: <see cref="GachaPullResult"/>.
    /// </summary>
    [Serializable]
    public sealed class GachaPull : GameCommand
    {
        public GachaType PullType;
        public int Count = 1;
        public GachaPayment Payment;
        public string BannerId;
        /// <summary>Proof of the watched rewarded ad for Payment = Ad (not needed with No Ads).</summary>
        public string AdToken;
    }

    [Serializable]
    public sealed class GachaPullItem
    {
        public string Kind;
        public string Id;
        public long Amount;
        public int Rarity;
        /// <summary>Rarity tier rolled (Common..Mythic).</summary>
        public Rarity Tier;
        /// <summary>True when this is the banner's featured item.</summary>
        public bool Featured;
        /// <summary>Blueprint / alien results: the batch's shards and what it did ("new" = unlocked the item, "shards").</summary>
        public long Shards;
        public string Note = "";
    }

    /// <summary>The visible pity counters ("Mythic 37 / 100", "Legendary 12 / 40"); a pity of 0 = none.</summary>
    [Serializable]
    public sealed class GachaPityStatus
    {
        public int Mythic;
        public int MythicPity;
        public int Legendary;
        public int LegendaryPity;
    }

    [Serializable]
    public sealed class GachaPullResult
    {
        public GachaType Type;
        public string BannerId;
        public List<GachaPullItem> Items = new List<GachaPullItem>();
        /// <summary>The visible counters after these pulls.</summary>
        public GachaPityStatus Pity = new GachaPityStatus();
    }

    /// <summary>A resolved banner (from the rotation or the live-ops schedule).</summary>
    [Serializable]
    public sealed class ActiveBannerInfo
    {
        /// <summary>Unique per occurrence: "&lt;themeId&gt;@&lt;period&gt;" for rotations, the BannerDef id otherwise.</summary>
        public string Id;
        public GachaType Type;
        public BannerTheme Theme;
        public long StartMs;
        public long EndMs;
    }

    /// <summary>Published odds: the flat rates of every pull, the fixed pity values and the 10-pull rule.</summary>
    [Serializable]
    public sealed class GachaOdds
    {
        public GachaType Type;
        public string BannerId;
        public List<GachaOddsTier> Tiers = new List<GachaOddsTier>();
        /// <summary>Rule shown to players: a 10-pull contains at least one result of TenPullMinTier or better (when TenPullGuarantee).</summary>
        public bool TenPullGuarantee;
        public Rarity TenPullMinTier;
        /// <summary>The fixed pity: the MythicPity-th pull without a Mythic is a Mythic; LegendaryPity likewise (0 = none).</summary>
        public int MythicPity;
        public int LegendaryPity;
        /// <summary>Shards one won blueprint / alien pays.</summary>
        public long BatchShards;
    }

    [Serializable]
    public sealed class GachaOddsTier
    {
        public Rarity Tier;
        /// <summary>Percent (0..100).</summary>
        public double Percent;
        /// <summary>
        /// Percent of this tier's results that are a featured item (80 on a banner with a featured item of this tier, else 0); the rest
        /// ("off banner") are the other items of the tier.
        /// </summary>
        public double FeaturedPercent;
        public List<GachaOddsItem> Items = new List<GachaOddsItem>();
    }

    [Serializable]
    public sealed class GachaOddsItem
    {
        public string Kind;
        public string Id;
        public long Amount;
        public int Rarity;
        public bool Featured;
        /// <summary>Percent of all pulls (0..100).</summary>
        public double Percent;
    }
}
