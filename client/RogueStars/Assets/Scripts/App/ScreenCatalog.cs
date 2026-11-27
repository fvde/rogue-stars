using System;
using System.Collections.Generic;
using RogueStars.Client.Screens;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.App
{
    public sealed class ScreenEntry
    {
        public string Id;
        public string Title;
        /// <summary>Meta feature gate (Features.*), or null when always available.</summary>
        public string FeatureId;
        /// <summary>Extra visibility condition besides the feature gate (null = none), e.g. the journal once someone was met.</summary>
        public Func<GameState, bool> Visible;
        /// <summary>Owning rules workstream (City, Meta, Progression, Run, Client).</summary>
        public string Workstream;
        public Func<UiScreen> Create;
        /// <summary>True until the owning workstream's rules are merged into this branch.</summary>
        public bool Placeholder;
    }

    /// <summary>
    /// Every navigable screen with its feature gate. The hub places each one (HubLayout: tabs, side columns, "More") once its
    /// feature is unlocked; the smoke test instantiates all of it. To add a screen: implement a UiScreen and add an entry
    /// here (Todo(...) makes a placeholder); it shows up under "More" unless HubLayout gives it a place.
    /// </summary>
    public static class ScreenCatalog
    {
        public static readonly List<ScreenEntry> All = new List<ScreenEntry>
        {
            new ScreenEntry { Id = "portals", Title = "Portals", Workstream = "Run", Create = () => new PortalsScreen() },
            new ScreenEntry { Id = "worm", Title = "Worm", FeatureId = Features.Parts, Workstream = "Progression", Create = () => new WormScreen() },
            new ScreenEntry { Id = "parts", Title = "Parts", FeatureId = Features.Parts, Workstream = "Progression", Create = () => new PartsScreen() },
            // "signals": the former pull screen's id, kept as an alias of the Shop (deep links, old builds' routes).
            new ScreenEntry { Id = "signals", Title = "Shop", Workstream = "Progression", Create = () => new ShopScreen() },
            new ScreenEntry { Id = "laboratory", Title = "Laboratory", FeatureId = Features.Laboratory, Workstream = "Run", Create = () => new LaboratoryScreen() },
            new ScreenEntry { Id = "heroes", Title = "Heroes", FeatureId = Features.Heroes, Workstream = "Progression", Create = () => new HeroesScreen() },
            new ScreenEntry { Id = "mounts", Title = "Mounts", FeatureId = Features.Mounts, Workstream = "Progression", Create = () => new MountsScreen() },
            // Summon events (heroes, then mounts): the hub's left column ("Summon").
            new ScreenEntry { Id = "summon", Title = "Summon Events", FeatureId = Features.Heroes, Workstream = "Meta", Create = () => new SummonEventScreen() },
            new ScreenEntry { Id = "city", Title = "City", FeatureId = Features.AlienSquad, Workstream = "City", Create = () => new CityScreen() },
            new ScreenEntry { Id = "aliens", Title = "Aliens", FeatureId = Features.Aliens, Workstream = "City", Create = () => new AliensScreen() },
            new ScreenEntry { Id = "breeding", Title = "Breeding Pen", FeatureId = Features.Breeding, Workstream = "City", Create = () => new BreedingScreen() },
            new ScreenEntry { Id = "quests", Title = "Daily Quests", FeatureId = Features.Parts, Workstream = "Meta", Create = () => new QuestsScreen() },
            // The Shop: pulls (Aliens / Blueprints / Parts) and offers in one screen, the hub's "Shop" tab. Its
            // offers need no unlock (owner, 2026-11-27); each pull section comes with its own feature (ShopScreen.PullSections).
            new ScreenEntry { Id = "shop", Title = "Shop", Workstream = "Meta", Create = () => new ShopScreen() },
            // The Passes page: Shop → "Passes" and the hub's Pass icon (daily gift, Monthly Pass, Forever products, season pass, funds).
            // No unlock, like the Shop's offers.
            new ScreenEntry { Id = "passes", Title = "Passes", Workstream = "Meta", Create = () => new PassesScreen() },
            new ScreenEntry { Id = "mail", Title = "Mail", Workstream = "Meta", Create = () => new MailScreen() },
            new ScreenEntry { Id = "observatory", Title = "Observatory", FeatureId = Features.Invasions, Workstream = "Meta", Create = () => new ObservatoryScreen() },
            new ScreenEntry { Id = "arena", Title = "Arena", FeatureId = Features.Arena, Workstream = "Meta", Create = () => new ArenaScreen() },
            new ScreenEntry { Id = "guild", Title = "Guild", FeatureId = Features.Guilds, Workstream = "Meta", Create = () => new GuildScreen() },
            // Run Quests: the journal of the story characters met on the board ("More"; docs/design/run-quests/recipe.md). No
            // feature gate (Run Quests are part of the game from the start): it shows up once the player met someone.
            new ScreenEntry { Id = "journal", Title = "Journal", Workstream = "Run", Visible = s => s.RunQuests.Encounters > 0, Create = () => new JournalScreen() },
            new ScreenEntry { Id = "settings", Title = "Settings", Workstream = "Client", Create = () => new SettingsScreen() },
        };

        public static ScreenEntry Find(string id) => All.Find(e => e.Id == id);

        private static ScreenEntry Todo(string id, string title, string feature, string workstream, string description) => new ScreenEntry
        {
            Id = id, Title = title, FeatureId = feature, Workstream = workstream, Placeholder = true,
            Create = () => new PlaceholderScreen(title, workstream, description),
        };
    }
}
