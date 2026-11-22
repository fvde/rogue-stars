using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Single place that wires every system into the engine. Each system owns its own folder.</summary>
    public static class SystemsRegistry
    {
        public static void RegisterAll(GameEngine e)
        {
            // Workstream Run: run, board, combat, skills, laboratory
            RunSystem.Register(e);
            LaboratorySystem.Register(e);
            // Workstream Progression: worm, parts, heroes, mounts, gacha, economy
            EconomySystem.Register(e);
            WormSystem.Register(e);
            PartsSystem.Register(e);
            HeroSystem.Register(e);
            MountSystem.Register(e);
            GachaSystem.Register(e);
            // Workstream City: city, blueprints, aliens, breeding, timers
            CitySystem.Register(e);
            AlienSystem.Register(e);
            BreedingSystem.Register(e);
            TimerSystem.Register(e);
            // Workstream Meta: onboarding, liveops, quests, shop, mail, arena, guild
            OnboardingSystem.Register(e);
            LiveOpsSystem.Register(e);
            QuestSystem.Register(e);
            ShopSystem.Register(e);
            MailSystem.Register(e);
            ArenaSystem.Register(e);
            GuildSystem.Register(e);
        }
    }
}
