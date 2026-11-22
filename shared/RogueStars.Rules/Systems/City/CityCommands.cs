using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    // ---- City ---------------------------------------------------------------------------------

    /// <summary>Unlock the next city area (content requirements + cost).</summary>
    [Serializable] public sealed class CityUnlockArea : GameCommand { public string AreaId; }

    /// <summary>Place an owned blueprint's building at (X,Y). First placement starts the construction timer; re-placing a stored building is instant.</summary>
    [Serializable] public sealed class CityBuild : GameCommand { public string BlueprintId; public int X; public int Y; }

    /// <summary>Move a placed building to (X,Y).</summary>
    [Serializable] public sealed class CityMove : GameCommand { public string BlueprintId; public int X; public int Y; }

    /// <summary>Remove a building from the grid (keeps level and stars; collects storage; unassigns workers). Not allowed while a timer runs.</summary>
    [Serializable] public sealed class CityDemolish : GameCommand { public string BlueprintId; }

    /// <summary>Start an upgrade (Ore + Biomass + timer).</summary>
    [Serializable] public sealed class CityUpgrade : GameCommand { public string BlueprintId; }

    /// <summary>Collect stored production; BlueprintId null/empty = collect all buildings. Payload: Dictionary currency → amount.</summary>
    [Serializable] public sealed class CityCollect : GameCommand { public string BlueprintId; }

    /// <summary>Set the production priority currency (null/empty clears it).</summary>
    [Serializable] public sealed class CitySetPriority : GameCommand { public string Currency; }

    /// <summary>Spend blueprint shards to raise stars (1..10).</summary>
    [Serializable] public sealed class BlueprintStarUp : GameCommand { public string BlueprintId; }

    // ---- Timers -------------------------------------------------------------------------------

    public enum SpeedUpMethod { Diamonds = 0, Ad = 1, Free = 2 }

    /// <summary>Speed up any timer. Diamonds: completes it; Ad: −15% of remaining (max 3 per timer); Free: daily free speed-up.</summary>
    /// AdToken: proof of the watched rewarded ad (not needed with No Ads).
    [Serializable] public sealed class TimerSpeedUp : GameCommand { public string TimerId; public SpeedUpMethod Method; public string AdToken; }

    // ---- Aliens -------------------------------------------------------------------------------

    [Serializable] public sealed class AlienLevelUp : GameCommand { public string AlienTypeId; }

    [Serializable] public sealed class AlienStarUp : GameCommand { public string AlienTypeId; }

    /// <summary>Assign an alien to work in a building (BlueprintId null/empty = unassign). Moving between buildings is allowed.</summary>
    [Serializable] public sealed class AlienAssign : GameCommand { public string AlienTypeId; public string BlueprintId; }

    /// <summary>Replace the squad (ordered, distinct, owned, at most CitySystem.SquadSlots).</summary>
    [Serializable] public sealed class AlienSetSquad : GameCommand { public List<string> AlienTypeIds = new List<string>(); }

    // ---- Breeding -----------------------------------------------------------------------------

    [Serializable] public sealed class BreedStart : GameCommand { public string ParentA; public string ParentB; }

    /// <summary>Collect a finished breeding job; the child becomes a pending choice. Payload: child id.</summary>
    [Serializable] public sealed class BreedCollect : GameCommand { public string JobId; }

    public enum BreedKeep { Replace = 0, Discard = 1 }

    /// <summary>Resolve a pending child: replace the current alien of that type, or discard it for shards/Grain.</summary>
    [Serializable] public sealed class BreedResolve : GameCommand { public string ChildId; public BreedKeep Keep; }
}
