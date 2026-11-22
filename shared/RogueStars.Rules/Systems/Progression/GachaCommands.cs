using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    public enum GachaPayment { Tokens, Diamonds }

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
    }

    [Serializable]
    public sealed class GachaPullResult
    {
        public GachaType Type;
        public string BannerId;
        public List<GachaPullItem> Items = new List<GachaPullItem>();
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

    /// <summary>Published odds (base rates; hidden pity is intentionally not part of it).</summary>
    [Serializable]
    public sealed class GachaOdds
    {
        public GachaType Type;
        public string BannerId;
        public List<GachaOddsTier> Tiers = new List<GachaOddsTier>();
        /// <summary>Guarantee shown to players: a 10-pull contains at least one result of this tier or better.</summary>
        public Rarity TenPullMinTier;
    }

    [Serializable]
    public sealed class GachaOddsTier
    {
        public Rarity Tier;
        /// <summary>Percent (0..100).</summary>
        public double Percent;
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
