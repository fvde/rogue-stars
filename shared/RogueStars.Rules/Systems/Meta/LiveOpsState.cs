using System;
using System.Collections.Generic;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        public LiveOpsState LiveOps = new LiveOpsState();
    }
}

namespace RogueStars.Rules.Systems
{
    [Serializable]
    public sealed class LiveOpsState
    {
        /// <summary>Last reset-day on which invasion attempts were topped up.</summary>
        public long AttemptDay = -1;
        /// <summary>Occurrence id → this player's contribution to that invasion occurrence.</summary>
        public Dictionary<string, InvasionContribution> Contributions = new Dictionary<string, InvasionContribution>();
        /// <summary>Occurrence ids whose outcome was applied (idempotency; trimmed to the newest 50).</summary>
        public List<string> AppliedOutcomes = new List<string>();
        /// <summary>Server-pushed surprise invasions (in addition to the content calendar).</summary>
        public List<ServerInvasion> ServerInvasions = new List<ServerInvasion>();
        /// <summary>Planetary event modifiers are materialised for every occurrence overlapping (this, now]; 0 = never.</summary>
        public long ScheduleSettledMs;
    }

    [Serializable]
    public sealed class InvasionContribution
    {
        public string InvasionId;
        public long StartMs;
        public long EndMs;
        public int Attempts;
        /// <summary>Attempts started whose battle result the server has not reported yet.</summary>
        public int PendingAttempts;
        public long Damage;
        public long Score;
    }

    [Serializable]
    public sealed class ServerInvasion
    {
        public string OccurrenceId;
        public string InvasionId;
        public long StartMs;
        public long EndMs;
    }

    /// <summary>One live event occurrence (query result).</summary>
    [Serializable]
    public sealed class ActiveLiveEvent
    {
        /// <summary>"&lt;eventId&gt;@&lt;startMs&gt;": unique per occurrence; used by attempts and outcomes.</summary>
        public string OccurrenceId;
        public string EventId;
        public Content.LiveOpsKind Kind;
        public string RefId;
        public string Name;
        public long StartMs;
        public long EndMs;
        public bool Surprise;
    }
}
