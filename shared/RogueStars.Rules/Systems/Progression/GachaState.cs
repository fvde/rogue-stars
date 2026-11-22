using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        /// <summary>Hidden pity per pull type. Owned by Progression/GachaSystem. Never show pity in the UI.</summary>
        public GachaState Gacha = new GachaState();
    }
}

namespace RogueStars.Rules.Systems
{
    /// <summary>Hidden pity counters of one pull type, shared across all its banners and carried over.</summary>
    [Serializable]
    public sealed class GachaPityState
    {
        public int SinceMythic;
        /// <summary>Pulls since the last Legendary-or-better result.</summary>
        public int SinceLegendary;
        public long TotalPulls;
        public long Mythics;
        /// <summary>How many Mythics came from the hard pity (analytics / tests).</summary>
        public long MythicPityTriggers;
    }

    [Serializable]
    public sealed class GachaState
    {
        public Dictionary<GachaType, GachaPityState> Pity = new Dictionary<GachaType, GachaPityState>();

        public GachaPityState For(GachaType t)
        {
            if (!Pity.TryGetValue(t, out var p)) { p = new GachaPityState(); Pity[t] = p; }
            return p;
        }
    }
}
