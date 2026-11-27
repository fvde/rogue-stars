using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{

    /// <summary>Equip a part. Arm parts fit either arm (LeftArm or RightArm); replaces what was in the slot.</summary>
    [Serializable] public sealed class PartEquip : GameCommand { public string Uid; public PartSlot Slot; }
    [Serializable] public sealed class PartUnequip : GameCommand { public PartSlot Slot; }
    /// <summary>Level a part with Ore (+ Gear Cores from level 10) up to its cap.</summary>
    [Serializable] public sealed class PartLevelUp : GameCommand { public string Uid; public int Levels = 1; }
    /// <summary>Reset a part to level 1, refunding 100% of the Ore and Gear Cores spent.</summary>
    [Serializable] public sealed class PartLevelDown : GameCommand { public string Uid; }
    /// <summary>Lock / unlock a part: a locked part is never fed for Metal or used as ladder / Replicator material.</summary>
    [Serializable] public sealed class PartLock : GameCommand { public string Uid; public bool Locked = true; }
    /// <summary>
    /// Feed spare standard parts (Common … Legendary) for Metal: Common 1, Rare 2, Epic 6, Legendary 20 each; their level Ore,
    /// Gear Cores and runes come back. Equipped, locked and forged parts and Mythics are never fed. Payload: Metal gained.
    /// </summary>
    [Serializable] public sealed class PartFeed : GameCommand { public List<string> Uids = new List<string>(); }
    /// <summary>
    /// Climb a Mythic one ladder step (Mythic → +1 → +2 → S → S+1 → S+2 → SS): exactly the step's copies of the same Mythic and other
    /// Mythics of the same slot (both at the base step, not awakened; arms share one pool), plus the step's Metal and Ore.
    /// Payload: the new rarity.
    /// </summary>
    [Serializable] public sealed class PartClimb : GameCommand { public string TargetUid; public List<string> MaterialUids = new List<string>(); }
    /// <summary>
    /// Replicator: PartsConfig.ReplicateMythics spare Mythics (any slot, base step, not awakened) + ReplicateOre → one base-step copy of
    /// the Mythic TargetUid (a Mythic the player owns). Payload: the new part uid.
    /// </summary>
    [Serializable] public sealed class PartReplicate : GameCommand { public string TargetUid; public List<string> MaterialUids = new List<string>(); }
    [Serializable] public sealed class RuneSocket : GameCommand { public string RuneUid; public string PartUid; public int SlotIndex; }
    [Serializable] public sealed class RuneUnsocket : GameCommand { public string PartUid; public int SlotIndex; }
    /// <summary>Merge RuneMergeCount runes of the same def and rarity into one of the next rarity. Payload: new rune uid.</summary>
    [Serializable] public sealed class RuneMerge : GameCommand { public List<string> RuneUids = new List<string>(); }
    [Serializable] public sealed class RuneLock : GameCommand { public string RuneUid; public bool Locked = true; }
    /// <summary>Forge: paid rune slot unlock that also adds % to the part's base stats (Epic+).</summary>
    [Serializable] public sealed class PartForgeUnlock : GameCommand { public string Uid; }

    /// <summary>Awaken a Mythic+ part (once) for PartsConfig.AwakenCost Awaken Cores.</summary>
    [Serializable] public sealed class PartAwaken : GameCommand { public string Uid; }
}
