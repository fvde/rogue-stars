using System;
using System.Collections.Generic;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        public GuildState Guild = new GuildState();
    }
}

namespace RogueStars.Rules.Systems
{
    /// <summary>The player's side of guild membership. Guild storage, roster and chat live on the server.</summary>
    [Serializable]
    public sealed class GuildState
    {
        /// <summary>Null when not in a guild.</summary>
        public string GuildId;
        /// <summary>See <see cref="GuildRoles"/>.</summary>
        public string Role;
        public long JoinedMs;
        public long LeftMs;
        /// <summary>Week index of <see cref="WeeklyInvasionContribution"/>.</summary>
        public long ContributionWeek = long.MinValue;
        public long WeeklyInvasionContribution;
        public long TotalInvasionContribution;
        public Dictionary<string, PeriodCount> ShopBuys = new Dictionary<string, PeriodCount>();
    }

    public static class GuildRoles
    {
        public const string Leader = "leader";
        public const string Officer = "officer";
        public const string Member = "member";

        public static bool IsValid(string role) => role == Leader || role == Officer || role == Member;
    }
}
