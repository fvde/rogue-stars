using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{

    /// <summary>Equip a part. Arm parts go to LeftArm or RightArm; replaces what was in the slot.</summary>
    [Serializable] public sealed class PartEquip : GameCommand { public string Uid; public PartSlot Slot; }
    [Serializable] public sealed class PartUnequip : GameCommand { public PartSlot Slot; }
    /// <summary>Level a part with Ore (+ Gear Cores at higher levels) up to its cap.</summary>
    [Serializable] public sealed class PartLevelUp : GameCommand { public string Uid; public int Levels = 1; }
    /// <summary>Reset a part to level 1, refunding 100% of the Ore and Gear Cores spent.</summary>
    [Serializable] public sealed class PartLevelDown : GameCommand { public string Uid; }
    /// <summary>Raise the level cap with Advancement Plates (limited raises per rarity).</summary>
    [Serializable] public sealed class PartRaiseCap : GameCommand { public string Uid; }
    /// <summary>Merge materials into the target, raising it one rarity step (recipe from content).</summary>
    [Serializable] public sealed class PartMerge : GameCommand { public string TargetUid; public List<string> MaterialUids = new List<string>(); }
    /// <summary>Automatically perform every possible merge up to PartsConfig.QuickMergeMaxTarget. Payload: merge count.</summary>
    [Serializable] public sealed class PartQuickMerge : GameCommand { }
    /// <summary>Dismantle parts: 100% level/cap material refund + dismantle Ore; runes return to the inventory.</summary>
    [Serializable] public sealed class PartDismantle : GameCommand { public List<string> Uids = new List<string>(); }
    [Serializable] public sealed class RuneSocket : GameCommand { public string RuneUid; public string PartUid; public int SlotIndex; }
    [Serializable] public sealed class RuneUnsocket : GameCommand { public string PartUid; public int SlotIndex; }
    /// <summary>Merge RuneMergeCount runes of the same def and rarity into one of the next rarity. Payload: new rune uid.</summary>
    [Serializable] public sealed class RuneMerge : GameCommand { public List<string> RuneUids = new List<string>(); }
    [Serializable] public sealed class RuneLock : GameCommand { public string RuneUid; public bool Locked = true; }
    /// <summary>Forge: paid rune slot unlock that also adds % to the part's base stats.</summary>
    [Serializable] public sealed class PartForgeUnlock : GameCommand { public string Uid; }
    /// <summary>
    /// Replicator: consume Legendary+ parts (any def, worth PartsConfig.ReplicateFodderPoints) + Ore to create a Legendary
    /// copy of the def of the Mythic+ part TargetUid (merge copy for Mythic S / SS).
    /// </summary>
    [Serializable] public sealed class PartReplicate : GameCommand { public string TargetUid; public List<string> MaterialUids = new List<string>(); }

    /// <summary>Awaken a Mythic+ part (once) for PartsConfig.AwakenCost Awaken Cores.</summary>
    [Serializable] public sealed class PartAwaken : GameCommand { public string Uid; }
}
