using System;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Spend one invasion attempt on an active invasion occurrence. The server then runs the battle.</summary>
    [Serializable]
    public sealed class InvasionStartAttempt : GameCommand { public string OccurrenceId; }

    /// <summary>Server reports the damage of a started attempt (from BattleSimulator); adds contribution score.</summary>
    [Serializable, ServerOnly]
    public sealed class InvasionRecordDamage : GameCommand { public string OccurrenceId; public long Damage; }

    /// <summary>Server applies the group outcome of an invasion occurrence to this player (modifiers + rank rewards).</summary>
    [Serializable, ServerOnly]
    public sealed class LiveOpsApplyOutcome : GameCommand
    {
        public string InvasionId;
        public string OccurrenceId;
        public bool Repelled;
        /// <summary>1-based contribution rank in the player's group; 0 = did not take part.</summary>
        public int ContributionRank;
    }

    /// <summary>Server starts a surprise invasion for this player (e.g. ops-triggered), on top of the calendar.</summary>
    [Serializable, ServerOnly]
    public sealed class LiveOpsStartSurpriseInvasion : GameCommand { public string InvasionId; public long StartMs; public long DurationMs; }
}
