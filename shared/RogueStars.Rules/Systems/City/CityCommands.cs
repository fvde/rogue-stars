using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    // ---- City ---------------------------------------------------------------------------------
    // Layout edits are applied at once, one command per edit (no draft / save); each runs CityLayout.Validate on the resulting layout.
    // Undo is client-side: send the reverse command (Place ↔ Store, Move back, Streets with Adds/Removes swapped).

    /// <summary>
    /// Place a building at (X,Y). Uid set: a stored copy of BlueprintId (free, instant). Uid empty: a new copy (pays the build cost,
    /// starts construction; copies capped by rarity). Payload: the building's uid.
    /// </summary>
    [Serializable] public sealed class CityPlace : GameCommand { public string BlueprintId; public string Uid; public int X; public int Y; public bool Rotated; }

    /// <summary>Move a placed building (free, instant; keeps level, timers, workers and storage).</summary>
    [Serializable] public sealed class CityMove : GameCommand { public string Uid; public int X; public int Y; public bool Rotated; }

    /// <summary>
    /// Put a placed building back into storage (level and stars kept, storage collected, workers idle). Not while upgrading. A new copy
    /// still under its first construction is cancelled instead (build cost refunded, copy removed).
    /// </summary>
    [Serializable] public sealed class CityStore : GameCommand { public string Uid; }

    /// <summary>One street stroke: cells (y × GridWidth + x, see CityLayout.Cell) to pave and to erase. Streets are free.</summary>
    [Serializable] public sealed class CityStreets : GameCommand { public List<int> Adds = new List<int>(); public List<int> Removes = new List<int>(); }

    /// <summary>Dig a patch that touches your land (cost by dig number, one dig at a time, timer takes speed-ups).</summary>
    [Serializable] public sealed class CityDig : GameCommand { public string PatchId; }

    /// <summary>Start a level-up of a building copy (cost by the blueprint's cost type + timer).</summary>
    [Serializable] public sealed class CityUpgrade : GameCommand { public string Uid; }

    /// <summary>Collect stored production; Uid null/empty = collect every building. Payload: Dictionary currency → amount.</summary>
    [Serializable] public sealed class CityCollect : GameCommand { public string Uid; }

    /// <summary>
    /// Pop chances of pull buildings: Uid + All false = one chance; Uid + All true = every chance on it; Uid empty = every chance on
    /// every pull building. Payload: List&lt;CityPopResult&gt; (one per pop, 1..3 pulls each).
    /// </summary>
    [Serializable] public sealed class CityPop : GameCommand { public string Uid; public bool All; }

    /// <summary>Set the production priority currency (null/empty clears it).</summary>
    [Serializable] public sealed class CitySetPriority : GameCommand { public string Currency; }

    /// <summary>Spend blueprint shards to raise stars (1..10; shared by every copy).</summary>
    [Serializable] public sealed class BlueprintStarUp : GameCommand { public string BlueprintId; }

    // ---- Timers -------------------------------------------------------------------------------

    public enum SpeedUpMethod { Diamonds = 0, Ad = 1, Free = 2 }

    /// <summary>Speed up any timer. Diamonds: completes it; Ad: −15% of remaining (max 3 per timer); Free: daily free speed-up.</summary>
    /// AdToken: proof of the watched rewarded ad (not needed with No Ads).
    [Serializable] public sealed class TimerSpeedUp : GameCommand { public string TimerId; public SpeedUpMethod Method; public string AdToken; }

    // ---- Aliens -------------------------------------------------------------------------------

    [Serializable] public sealed class AlienStarUp : GameCommand { public string AlienTypeId; }

    /// <summary>
    /// Assign an alien to work in a building copy (BuildingUid null/empty = idle). Moving between buildings is allowed. Assigning to the
    /// Launch Pod puts the alien in the squad.
    /// </summary>
    [Serializable] public sealed class AlienAssign : GameCommand { public string AlienTypeId; public string BuildingUid; }

    /// <summary>Replace the squad (ordered, distinct, owned, at most CitySystem.SquadSlots): these aliens move into the Launch Pod.</summary>
    [Serializable] public sealed class AlienSetSquad : GameCommand { public List<string> AlienTypeIds = new List<string>(); }

    // ---- Breeding -----------------------------------------------------------------------------

    [Serializable] public sealed class BreedStart : GameCommand { public string ParentA; public string ParentB; }

    /// <summary>Collect a finished breeding job; the child becomes a pending choice. Payload: child id.</summary>
    [Serializable] public sealed class BreedCollect : GameCommand { public string JobId; }

    public enum BreedKeep { Replace = 0, Discard = 1 }

    /// <summary>Resolve a pending child: replace the current alien of that type, or discard it for shards/Grain.</summary>
    [Serializable] public sealed class BreedResolve : GameCommand { public string ChildId; public BreedKeep Keep; }
}
