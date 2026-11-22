using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Stub. Owned by workstream Meta; replace with the real implementation.</summary>
    public static class OnboardingSystem
    {
        public static void Register(GameEngine e) { }
    }

    /// <summary>
    /// Feature gating used by every system ("is the Breeding Pen unlocked?").
    /// Stub returns true; workstream Meta implements real, content-defined unlock rules.
    /// </summary>
    public static class Features
    {
        public const string Laboratory = "laboratory";
        public const string Aliens = "aliens";
        public const string AlienSquad = "alien_squad";
        public const string Blueprints = "blueprints";
        public const string Heroes = "heroes";
        public const string Mounts = "mounts";
        public const string Breeding = "breeding";
        public const string Arena = "arena";
        public const string Guilds = "guilds";
        public const string Invasions = "invasions";
        public const string Overcharged = "overcharged";
        public const string Burrow = "burrow";
        public const string DarkSide = "dark_side";
        public const string PartPulls = "part_pulls";
        public const string AlienPulls = "alien_pulls";
        public const string Forge = "forge";
        public const string Runes = "runes";

        public static bool IsUnlocked(GameState state, ContentDb content, string featureId) => true;
    }
}
