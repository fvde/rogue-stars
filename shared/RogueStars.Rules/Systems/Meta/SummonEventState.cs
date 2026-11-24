using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        /// <summary>Hero / mount summon events: the current event of each type and the Luck carried between events. Owned by Meta/SummonEventSystem.</summary>
        public SummonState Summons = new SummonState();
    }
}

namespace RogueStars.Rules.Systems
{
    [Serializable]
    public sealed class SummonState
    {
        /// <summary>Save migration applied (SummonEventSystem.MigrationVersion): 1 = summon events introduced.</summary>
        public int Migrated;
        /// <summary>The player's current (or last) event of each type.</summary>
        public Dictionary<SummonEventType, SummonEventProgress> Events = new Dictionary<SummonEventType, SummonEventProgress>();
        /// <summary>
        /// Pity per event type, kept across events: SinceMythic is the visible Luck (pulls since the last featured batch, max 100).
        /// Events have no Legendary pity.
        /// </summary>
        public Dictionary<SummonEventType, GachaPityState> Luck = new Dictionary<SummonEventType, GachaPityState>();

        public GachaPityState LuckFor(SummonEventType t)
        {
            if (!Luck.TryGetValue(t, out var p)) { p = new GachaPityState(); Luck[t] = p; }
            return p;
        }
    }

    /// <summary>This player's progress in one event occurrence (reset when the next event of the type starts).</summary>
    [Serializable]
    public sealed class SummonEventProgress
    {
        /// <summary>"hero@12" (global occurrence 12) or "hero@welcome" (the first event, started by the feature unlock).</summary>
        public string EventId;
        public string FeaturedId;
        public long StartMs;
        public long EndMs;
        /// <summary>When this player joined (the event start, or the first command after it); task progress counts from here.</summary>
        public long JoinedMs;
        /// <summary>Pulls made in this event (milestones).</summary>
        public int Pulls;
        /// <summary>Task counters at the join (progress = counter − baseline).</summary>
        public Dictionary<string, long> Baseline = new Dictionary<string, long>();
        /// <summary>Claimed rewards: "task:&lt;line&gt;:&lt;index&gt;", "ms:&lt;pulls&gt;", "pass:f:&lt;day&gt;", "pass:p:&lt;day&gt;".</summary>
        public HashSet<string> Claimed = new HashSet<string>();
        /// <summary>Exchange item id → bought in this event.</summary>
        public Dictionary<string, int> ExchangeBuys = new Dictionary<string, int>();
        /// <summary>The premium pass of this event was bought.</summary>
        public bool PassPremium;
    }

    /// <summary>A resolved event occurrence (query result).</summary>
    [Serializable]
    public sealed class SummonEventInfo
    {
        public SummonEventType Type;
        public string Id;
        public string FeaturedId;
        public long StartMs;
        public long EndMs;
        /// <summary>The player's first event (started by the feature unlock).</summary>
        public bool Welcome;
    }

    /// <summary>One pull result. Note: what a featured batch did ("new" = unlocked, "shards", "overflow" at 10★), empty for filler.</summary>
    [Serializable]
    public sealed class SummonPullItem
    {
        public string Kind;
        public string Id;
        public long Amount;
        public Rarity Tier;
        public bool Featured;
        public string Note = "";
        /// <summary>Star count (heroes / mounts) after a featured batch.</summary>
        public int Stars;
        /// <summary>Shards a featured batch paid (50), 0 for filler and overflow.</summary>
        public long Shards;
    }

    [Serializable]
    public sealed class SummonPullResult
    {
        public SummonEventType Type;
        public string EventId;
        public List<SummonPullItem> Items = new List<SummonPullItem>();
        /// <summary>Exchange tokens earned by these pulls.</summary>
        public long Tokens;
        public int Luck;
        public int LuckMax;
        /// <summary>The pulls filled the Luck meter (a guaranteed featured batch).</summary>
        public bool LuckTriggered;
    }

    /// <summary>One task line's state for display.</summary>
    [Serializable]
    public sealed class SummonTaskStatus
    {
        public string LineId;
        public int Index;
        public long Target;
        public long Progress;
        public string Text;
        public Reward Reward;
        public bool Claimable;
        /// <summary>Every target of the line is claimed.</summary>
        public bool Done;
    }
}
