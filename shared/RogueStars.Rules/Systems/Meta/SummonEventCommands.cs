using System;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    public enum SummonPayment { Tickets, Diamonds }

    /// <summary>
    /// 1 or 10 pulls in the current event of a type, paid with its tickets or Diamonds. EventId (optional) must be the
    /// current event's id (a client still showing an ended event gets "event ended"). Payload: <see cref="SummonPullResult"/>.
    /// </summary>
    [Serializable]
    public sealed class SummonPull : GameCommand
    {
        public SummonEventType EventType;
        public int Count = 1;
        public SummonPayment Payment;
        public string EventId;
    }

    /// <summary>Claim one reached target of a task line (index into the line's targets; -1 = every reached one of the line).</summary>
    [Serializable]
    public sealed class SummonTaskClaim : GameCommand { public SummonEventType EventType; public string LineId; public int Index = -1; }

    /// <summary>Claim every reached task target of the current event. Payload: number of claims.</summary>
    [Serializable]
    public sealed class SummonTaskClaimAll : GameCommand { public SummonEventType EventType; }

    /// <summary>Claim a reached pull-count milestone of the current event.</summary>
    [Serializable]
    public sealed class SummonMilestoneClaim : GameCommand { public SummonEventType EventType; public int Pulls; }

    /// <summary>Buy an exchange shop item with the type's exchange tokens (limited per event).</summary>
    [Serializable]
    public sealed class SummonExchangeBuy : GameCommand { public SummonEventType EventType; public string ItemId; }

    /// <summary>Claim an unlocked day of the event pass (free track, or premium when bought for this event).</summary>
    [Serializable]
    public sealed class SummonPassClaim : GameCommand { public SummonEventType EventType; public int Day; public bool Premium; }

    /// <summary>Claim every unlocked, unclaimed day of the event pass (premium too when bought). Payload: number of claims.</summary>
    [Serializable]
    public sealed class SummonPassClaimAll : GameCommand { public SummonEventType EventType; }
}
