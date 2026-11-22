using System;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Spend an arena ticket to fight an opponent snapshot chosen by server matchmaking.</summary>
    [Serializable]
    public sealed class ArenaStartFight : GameCommand { public string OpponentId; }

    /// <summary>Server reports the result of a started arena fight (server runs BattleSimulator on both snapshots).</summary>
    [Serializable, ServerOnly]
    public sealed class ArenaFightResult : GameCommand { public bool Won; public long OpponentPoints; }

    /// <summary>Server closes the weekly league: rank rewards, promotion/demotion, points reset.</summary>
    [Serializable, ServerOnly]
    public sealed class ArenaWeekEnd : GameCommand { public int Rank; public int GroupSize; }
}
