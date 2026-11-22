using System;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        public ArenaState Arena = new ArenaState();
    }
}

namespace RogueStars.Rules.Systems
{
    [Serializable]
    public sealed class ArenaState
    {
        /// <summary>League index into content Arena.Leagues (0 = lowest).</summary>
        public int League;
        public long Points;
        public long BestPoints;
        public int WeekWins;
        public int WeekLosses;
        /// <summary>Fights started (ticket spent) whose result the server has not reported yet.</summary>
        public int PendingFights;
        public string PendingOpponentId;
        public int WeeksPlayed;
    }

    public enum LeagueOutcome { Stay, Promote, Demote }

    /// <summary>Async PvP snapshot: the player's full combat loadout at a point in time.</summary>
    [Serializable]
    public sealed class ArenaSnapshot
    {
        public string PlayerId;
        public string DisplayName;
        public int League;
        public long Points;
        public long Power;
        public long CreatedMs;
        public Core.CombatLoadout Loadout = new Core.CombatLoadout();
    }

    /// <summary>One row of a weekly 150-player group (server input / output of ResolveGroup).</summary>
    [Serializable]
    public sealed class ArenaStanding
    {
        public string PlayerId;
        public long Points;
        public int Rank;
        public LeagueOutcome Outcome;
    }
}
