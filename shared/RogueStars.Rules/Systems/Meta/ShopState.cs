using System;
using System.Collections.Generic;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        public ShopState Shop = new ShopState();
    }
}

namespace RogueStars.Rules.Systems
{
    [Serializable]
    public sealed class ShopState
    {
        /// <summary>Lifetime completed purchases per product id (also free rolling-step claims).</summary>
        public Dictionary<string, int> PurchaseCounts = new Dictionary<string, int>();
        /// <summary>Flash offers: purchases per "productId@occurrenceStart".</summary>
        public Dictionary<string, int> OccurrencePurchases = new Dictionary<string, int>();
        public List<PendingPurchase> Pending = new List<PendingPurchase>();
        /// <summary>Processed receipt ids (replay protection; newest 200).</summary>
        public List<string> Receipts = new List<string>();
        /// <summary>Product id → daily-claim entitlement (gift packs, monthly pass, Daily Diamonds).</summary>
        public Dictionary<string, DailyGrant> DailyGrants = new Dictionary<string, DailyGrant>();
        /// <summary>Rolling chain id → steps taken so far (current step = taken % steps when looping).</summary>
        public Dictionary<string, int> RollingTaken = new Dictionary<string, int>();
        /// <summary>Shop item id → purchases in the current limit period.</summary>
        public Dictionary<string, PeriodCount> ItemBuys = new Dictionary<string, PeriodCount>();
        /// <summary>Products with a daily limit (ProductDef.DailyLimit) → purchases on the reset day stored in Period.</summary>
        public Dictionary<string, PeriodCount> ProductDailyBuys = new Dictionary<string, PeriodCount>();
        public long AdDay = -1;
        public Dictionary<string, int> AdViews = new Dictionary<string, int>();

        // Season pass
        public long SeasonIndex = long.MinValue;
        public long SeasonQuestXp;
        public Dictionary<string, long> SeasonBaseline = new Dictionary<string, long>();
        /// <summary>Season index the premium track was bought for (long.MinValue = none).</summary>
        public long PremiumSeason = long.MinValue;
        /// <summary>"f:&lt;level&gt;" / "p:&lt;level&gt;" claimed this season.</summary>
        public HashSet<string> SeasonClaims = new HashSet<string>();

        // Funds
        public HashSet<string> FundsOwned = new HashSet<string>();
        /// <summary>"&lt;fundId&gt;:&lt;stepIndex&gt;" claimed.</summary>
        public HashSet<string> FundClaims = new HashSet<string>();
    }

    [Serializable]
    public sealed class PendingPurchase
    {
        public string ProductId;
        public long StartedMs;
    }

    [Serializable]
    public sealed class DailyGrant
    {
        public string ProductId;
        /// <summary>First reset-day that is no longer covered (long.MaxValue = forever).</summary>
        public long EndDay;
        public long LastClaimDay = long.MinValue;
    }

    [Serializable]
    public sealed class PeriodCount
    {
        public long Period;
        public int Count;
    }
}
