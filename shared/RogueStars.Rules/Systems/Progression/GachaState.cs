using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        /// <summary>Visible pity counters per pull type (docs/design/pull-system.md). Owned by Progression/GachaSystem.</summary>
        public GachaState Gacha = new GachaState();
    }
}

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// The visible pity counters of one pull type ("Mythic x / 100", "Legendary x / 40"), shared across all its banners and carried
    /// over. Fixed: no soft pity, nothing hidden.
    /// </summary>
    [Serializable]
    public sealed class GachaPityState
    {
        /// <summary>Pulls since the last Mythic result.</summary>
        public int SinceMythic;
        /// <summary>Pulls since the last Legendary-or-better result.</summary>
        public int SinceLegendary;
        public long TotalPulls;
        public long Mythics;
        /// <summary>How many Mythics came from the pity (analytics / tests).</summary>
        public long MythicPityTriggers;
        /// <summary>How many Legendaries came from the Legendary pity (analytics / tests).</summary>
        public long LegendaryPityTriggers;
    }

    [Serializable]
    public sealed class GachaState
    {
        public Dictionary<GachaType, GachaPityState> Pity = new Dictionary<GachaType, GachaPityState>();
        /// <summary>Save migration applied (PullSystemMigration.Version): 1 = the pull system (shard batches, 10★ table, visible pity).</summary>
        public int Migrated;

        public GachaPityState For(GachaType t)
        {
            if (!Pity.TryGetValue(t, out var p)) { p = new GachaPityState(); Pity[t] = p; }
            return p;
        }
    }
}
