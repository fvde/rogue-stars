using System;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        /// <summary>Daily economy bookkeeping (stamina refills). Owned by Progression/EconomySystem.</summary>
        public EconomyState Economy = new EconomyState();
    }
}

namespace RogueStars.Rules.Systems
{
    [Serializable]
    public sealed class EconomyState
    {
        /// <summary>Day index (see EconomySystem.DayIndex) the daily counters below belong to.</summary>
        public long DayIndex = long.MinValue;
        public int StaminaDiamondRefillsToday;
        public int StaminaAdRefillsToday;
        public int StaminaFreeRefillsToday;
    }
}
