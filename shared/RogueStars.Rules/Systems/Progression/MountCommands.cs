using System;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Unlock a mount at 1★ with ContentDb.ShardStars.UnlockShards (50) of its shards (like HeroUnlock).</summary>
    [Serializable] public sealed class MountUnlock : GameCommand { public string MountId; }
    /// <summary>Raise a mount one star (max 10) with its mount shards (the shared table; shards only).</summary>
    [Serializable] public sealed class MountStarUp : GameCommand { public string MountId; }
    /// <summary>Level a mount with Fuel (cap grows with stars).</summary>
    [Serializable] public sealed class MountLevelUp : GameCommand { public string MountId; public int Levels = 1; }
    /// <summary>Make a mount the active one; empty MountId dismounts.</summary>
    [Serializable] public sealed class MountEquip : GameCommand { public string MountId; }
}
