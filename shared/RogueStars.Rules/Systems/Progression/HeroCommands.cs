using System;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Unlock a hero at 1★ by spending ContentDb.ShardStars.UnlockShards (50) of its shards.</summary>
    [Serializable] public sealed class HeroUnlock : GameCommand { public string HeroId; }
    /// <summary>Raise a hero one star with its shards (the shared table up to 10★).</summary>
    [Serializable] public sealed class HeroStarUp : GameCommand { public string HeroId; }
    /// <summary>Level a hero with Hero XP (max level grows with stars).</summary>
    [Serializable] public sealed class HeroLevelUp : GameCommand { public string HeroId; public int Levels = 1; }
    /// <summary>Equip a hero persona; empty HeroId unequips.</summary>
    [Serializable] public sealed class HeroEquip : GameCommand { public string HeroId; }
}
