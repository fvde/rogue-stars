using System;
using System.Collections.Generic;

namespace RogueStars.Rules.Content
{
    [Serializable]
    public sealed class GuildConfig
    {
        public int MaxMembers = 30;
        public int MaxOfficers = 4;
        /// <summary>Guild XP thresholds per guild level (server computes guild level from member activity).</summary>
        public List<long> LevelXp = new List<long> { 0, 1_000, 3_000, 7_000, 15_000, 30_000, 60_000, 100_000, 160_000, 250_000 };
        /// <summary>Guild XP a member's contribution score adds (server aggregates).</summary>
        public double GuildXpPerContributionScore = 1.0;
        /// <summary>Re-joining a guild is blocked for this long after leaving.</summary>
        public long RejoinCooldownMs = 12 * 3_600_000L;
    }

    public sealed partial class ContentDb
    {
        public readonly GuildConfig Guild = new GuildConfig();

        partial void BuildGuild()
        {
            // Guild shop items are ShopItemDefs with Section "guild" (see ContentDb.Shop.cs).
        }
    }
}
