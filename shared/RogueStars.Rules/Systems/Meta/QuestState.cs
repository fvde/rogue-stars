using System;
using System.Collections.Generic;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        public QuestState Quests = new QuestState();
    }
}

namespace RogueStars.Rules.Systems
{
    [Serializable]
    public sealed class QuestState
    {
        public long DailyIndex = long.MinValue;
        public long WeeklyIndex = long.MinValue;
        /// <summary>Counter values at the start of the current day/week (progress = counter − baseline).</summary>
        public Dictionary<string, long> DailyBaseline = new Dictionary<string, long>();
        public Dictionary<string, long> WeeklyBaseline = new Dictionary<string, long>();
        /// <summary>Claimed quest ids (daily/weekly ids are cleared at their reset).</summary>
        public HashSet<string> Claimed = new HashSet<string>();
        public long DailyPoints;
        public long WeeklyPoints;
        /// <summary>Claimed point chests: "Daily:20", "Weekly:300" (cleared at reset).</summary>
        public HashSet<string> ChestsClaimed = new HashSet<string>();

        /// <summary>Repeating achievements: milestones claimed so far per achievement id (milestone n is at n × QuestDef.Every).</summary>
        public Dictionary<string, long> AchievementClaims = new Dictionary<string, long>();
        /// <summary>The random pull tokens (currency ids) the last point chest paid: the client's chest-open animation reads them.</summary>
        public List<string> LastChestTokens = new List<string>();

        // Daily login (7-day cycle without streak loss)
        public int LoginCycleIndex;
        public long LastLoginDay = long.MinValue;

        // New-player event: claimed day numbers (1-based)
        public HashSet<int> NewPlayerDaysClaimed = new HashSet<int>();

        // Returning-player gift
        public long LastReturningGiftMs;
    }
}
