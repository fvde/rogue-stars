using System;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Raise the worm level with Biomass.</summary>
    [Serializable]
    public sealed class WormLevelUp : GameCommand
    {
        public int Levels = 1;
    }

    /// <summary>Buy ranks of one button ("hp", "atk", "def") in the current talent tier.</summary>
    [Serializable]
    public sealed class WormTalentUpgrade : GameCommand
    {
        public string Button;
        public int Ranks = 1;
    }

    /// <summary>Complete the current talent tier (all buttons capped) and gain its perk.</summary>
    [Serializable]
    public sealed class WormTalentTierUp : GameCommand { }
}
