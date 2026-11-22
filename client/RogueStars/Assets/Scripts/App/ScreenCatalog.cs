using System;
using System.Collections.Generic;
using RogueStars.Client.Screens;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.App
{
    public sealed class ScreenEntry
    {
        public string Id;
        public string Title;
        /// <summary>Meta feature gate (Features.*), or null when always available.</summary>
        public string FeatureId;
        /// <summary>Owning rules workstream (City, Meta, Progression, Run, Client).</summary>
        public string Workstream;
        public Func<UiScreen> Create;
        /// <summary>True until the owning workstream's rules are merged into this branch.</summary>
        public bool Placeholder;
    }

    /// <summary>
    /// Every navigable screen. The hub builds its navigation from this list and the smoke test instantiates all
    /// of it. To add a screen after a rules merge: implement a UiScreen and replace its placeholder entry here.
    /// </summary>
    public static class ScreenCatalog
    {
        public static readonly List<ScreenEntry> All = new List<ScreenEntry>
        {
            new ScreenEntry { Id = "city", Title = "City", Workstream = "City", Create = () => new CityScreen() },
            new ScreenEntry { Id = "aliens", Title = "Aliens", FeatureId = Features.Aliens, Workstream = "City", Create = () => new AliensScreen() },
            new ScreenEntry { Id = "breeding", Title = "Breeding Pen", FeatureId = Features.Breeding, Workstream = "City", Create = () => new BreedingScreen() },
            new ScreenEntry { Id = "observatory", Title = "Observatory", Workstream = "Meta", Create = () => new ObservatoryScreen() },
            new ScreenEntry { Id = "quests", Title = "Quests", FeatureId = Features.Quests, Workstream = "Meta", Create = () => new QuestsScreen() },
            new ScreenEntry { Id = "shop", Title = "Shop", Workstream = "Meta", Create = () => new ShopScreen() },
            new ScreenEntry { Id = "mail", Title = "Mail", Workstream = "Meta", Create = () => new MailScreen() },
            new ScreenEntry { Id = "arena", Title = "Arena", FeatureId = Features.Arena, Workstream = "Meta", Create = () => new ArenaScreen() },
            new ScreenEntry { Id = "guild", Title = "Guild", FeatureId = Features.Guilds, Workstream = "Meta", Create = () => new GuildScreen() },
            new ScreenEntry { Id = "settings", Title = "Settings", Workstream = "Client", Create = () => new SettingsScreen() },

            // TODO(merge Run): replace with PortalsScreen / RunScreen / LaboratoryScreen once Systems/Run lands.
            Todo("portals", "Portals", null, "Run", "Pick a portal level and start a run (stamina). Board, dice, battles and drafts come with the Run workstream."),
            Todo("laboratory", "Laboratory", Features.Laboratory, "Run", "Research skills into the run draft pool and toggle them."),
            // TODO(merge Progression): replace with the real screens once Systems/Progression lands.
            Todo("worm", "Worm", Features.WormLevel, "Progression", "Worm level (Biomass) and the talent road."),
            Todo("parts", "Parts", Features.Parts, "Progression", "Six body slots, merge ladder, levels, runes and the Forge."),
            Todo("heroes", "Heroes", Features.Heroes, "Progression", "Hero personas: shards, stars 1–10, traits."),
            Todo("mounts", "Mounts", Features.Mounts, "Progression", "Spaceship mounts: stars, awakening, Fuel levels."),
            Todo("signals", "Signals", Features.Blueprints, "Progression", "Blueprint / Part / Alien pulls with pity and mythic banners."),
        };

        public static ScreenEntry Find(string id) => All.Find(e => e.Id == id);

        private static ScreenEntry Todo(string id, string title, string feature, string workstream, string description) => new ScreenEntry
        {
            Id = id, Title = title, FeatureId = feature, Workstream = workstream, Placeholder = true,
            Create = () => new PlaceholderScreen(title, workstream, description),
        };
    }
}
