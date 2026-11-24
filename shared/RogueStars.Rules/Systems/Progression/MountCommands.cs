using System;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Unlock a mount at 1★ with MountConfig.UnlockShards of its shards (like HeroUnlock; summon events pay shards).</summary>
    [Serializable] public sealed class MountUnlock : GameCommand { public string MountId; }
    /// <summary>Raise a mount one star (max 5) with its mount shards.</summary>
    [Serializable] public sealed class MountStarUp : GameCommand { public string MountId; }
    /// <summary>Awaken a 5★ mount one level (max 5) with mount shards + Mount Cores.</summary>
    [Serializable] public sealed class MountAwaken : GameCommand { public string MountId; }
    /// <summary>Level a mount with Fuel (cap grows with stars and awakening).</summary>
    [Serializable] public sealed class MountLevelUp : GameCommand { public string MountId; public int Levels = 1; }
    /// <summary>Make a mount the active one; empty MountId dismounts.</summary>
    [Serializable] public sealed class MountEquip : GameCommand { public string MountId; }
}
