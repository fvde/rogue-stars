using System;
using System.Collections.Generic;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        /// <summary>Owned mounts and the active one. Owned by Progression/MountSystem.</summary>
        public MountState Mounts = new MountState();
    }
}

namespace RogueStars.Rules.Systems
{
    [Serializable]
    public sealed class MountEntry
    {
        public int Stars = 1;
        public int Awaken;
        public int Level = 1;
    }

    [Serializable]
    public sealed class MountState
    {
        /// <summary>Owned mounts by id. Shards live in the wallet (Currencies.MountShards(id)), cores in Currencies.MountCores.</summary>
        public Dictionary<string, MountEntry> Owned = new Dictionary<string, MountEntry>();
        public string Active = "";
    }
}
