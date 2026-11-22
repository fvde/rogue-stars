using System;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Claim a completed quest/achievement (rewards, track points, season XP).</summary>
    [Serializable]
    public sealed class QuestClaim : GameCommand { public string QuestId; }

    /// <summary>Claim every completed, unclaimed quest.</summary>
    [Serializable]
    public sealed class QuestClaimAll : GameCommand { }

    /// <summary>Open a point-track chest once its point threshold is reached.</summary>
    [Serializable]
    public sealed class QuestClaimChest : GameCommand { public PointTrack Track; public long Points; }

    /// <summary>Claim today's daily login reward (next day of the 7-day cycle).</summary>
    [Serializable]
    public sealed class LoginClaim : GameCommand { }

    /// <summary>Claim a new-player event day reward (1-based day).</summary>
    [Serializable]
    public sealed class NewPlayerClaim : GameCommand { public int Day; }
}
