using System;
using System.Collections.Generic;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        /// <summary>Unlocked heroes and the equipped one. Owned by Progression/HeroSystem.</summary>
        public HeroState Heroes = new HeroState();
    }
}

namespace RogueStars.Rules.Systems
{
    [Serializable]
    public sealed class HeroEntry
    {
        public int Stars = 1;
        public int Level = 1;
    }

    [Serializable]
    public sealed class HeroState
    {
        /// <summary>Unlocked heroes by id. Shards live in the wallet (Currencies.HeroShards(id)).</summary>
        public Dictionary<string, HeroEntry> Owned = new Dictionary<string, HeroEntry>();
        public string Equipped = "";
    }
}
