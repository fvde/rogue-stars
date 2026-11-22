using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        /// <summary>Worm level and talent road. Owned by Progression/WormSystem.</summary>
        public WormState Worm = new WormState();
    }
}

namespace RogueStars.Rules.Systems
{
    [Serializable]
    public sealed class WormState
    {
        public int Level = 1;
        /// <summary>Number of fully completed (ranked-up) talent tiers; the current tier index equals this.</summary>
        public int TalentTiersDone;
        /// <summary>Ranks bought in the current tier per button key ("hp", "atk", "def").</summary>
        public Dictionary<string, int> TalentRanks = new Dictionary<string, int>();
    }
}
