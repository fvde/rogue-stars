using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        /// <summary>Part and rune inventory, equipment. Owned by Progression/PartsSystem.</summary>
        public PartsState Parts = new PartsState();
    }
}

namespace RogueStars.Rules.Systems
{
    /// <summary>One owned part instance.</summary>
    [Serializable]
    public sealed class PartItem
    {
        public string Uid;
        public string DefId;
        /// <summary>(int)PartRarity: the def's rarity; a Mythic's ladder step (Mythic … Mythic SS).</summary>
        public int Rarity;
        public int Level = 1;
        /// <summary>Forge slot unlocks done.</summary>
        public int ForgedSlots;
        /// <summary>1 once awakened (Mythic+, one time): awakened name, stats and skills; 0 otherwise.</summary>
        public int Awaken;
        /// <summary>A locked part is never fed for Metal or used as ladder / Replicator material.</summary>
        public bool Locked;
        /// <summary>Socketed rune uid per slot; "" = empty. Length grows with available slots.</summary>
        public List<string> Runes = new List<string>();
        /// <summary>Level materials invested (refunded 100% on level-down, feeding, or use as ladder / Replicator material).</summary>
        public long SpentOre;
        public long SpentGearCores;
    }

    /// <summary>One owned rune instance.</summary>
    [Serializable]
    public sealed class RuneItem
    {
        public string Uid;
        public string DefId;
        /// <summary>(int)Core.Rarity.</summary>
        public int Rarity;
        public bool Locked;
        /// <summary>Uid of the part the rune is socketed in, "" when in the inventory.</summary>
        public string SocketedIn = "";
    }

    [Serializable]
    public sealed class PartsState
    {
        public Dictionary<string, PartItem> Items = new Dictionary<string, PartItem>();
        public Dictionary<string, RuneItem> Runes = new Dictionary<string, RuneItem>();
        public Dictionary<PartSlot, string> Equipped = new Dictionary<PartSlot, string>();
        /// <summary>Counter for deterministic uids ("p00000001", "r00000001").</summary>
        public long NextUid = 1;
        /// <summary>
        /// Parts model of the save (PartsSystem.ModelVersion): 2 = parts v2. Older saves are reset to the starter set on load
        /// (pre-release, no migration: the old ladder's items do not exist any more).
        /// </summary>
        public int Model;
    }
}
