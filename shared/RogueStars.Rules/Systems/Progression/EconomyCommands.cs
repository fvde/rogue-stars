using System;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    public enum StaminaRefillMethod { Diamonds, Ad, DailyFree }

    /// <summary>
    /// Buy one stamina refill. Ad: the client sets AdWatched after a completed rewarded ad (not needed with
    /// the No Ads entitlement). Payload: <see cref="StaminaRefillResult"/>.
    /// </summary>
    [Serializable]
    public sealed class BuyStamina : GameCommand
    {
        public StaminaRefillMethod Method;
        public bool AdWatched;
    }

    [Serializable]
    public sealed class StaminaRefillResult
    {
        public long Gained;
        public long Stamina;
        public long DiamondsSpent;
    }

    /// <summary>Snapshot of today's stamina refill options for the UI.</summary>
    [Serializable]
    public sealed class StaminaRefillInfo
    {
        public long Amount;
        /// <summary>Price of the next Diamond refill, or -1 when the daily limit is reached.</summary>
        public long NextDiamondPrice;
        public int DiamondRefillsLeft;
        public int AdRefillsLeft;
        public int FreeRefillsLeft;
        public bool AdsInstant;
    }
}
