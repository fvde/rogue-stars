using System;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Server sets the player's guild membership after a cross-player action (create/join/leave/kick/promote).
    /// GuildId null = left the guild.
    /// </summary>
    [Serializable, ServerOnly]
    public sealed class GuildSetMembership : GameCommand { public string GuildId; public string Role; }

    /// <summary>Buy a guild shop item with guild_marks (weekly limits).</summary>
    [Serializable]
    public sealed class GuildShopBuy : GameCommand { public string ItemId; }
}
