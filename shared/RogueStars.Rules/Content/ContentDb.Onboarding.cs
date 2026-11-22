using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    /// <summary>What a requirement measures. See <see cref="Requirements"/> for how each kind is read.</summary>
    public enum RequirementKind
    {
        /// <summary>Highest main level cleared ≥ Target (counter <c>main_level_max</c>).</summary>
        MainLevel,
        /// <summary>Worm level ≥ Target (counter <c>worm_level</c>).</summary>
        WormLevel,
        /// <summary>state.Counter(Key) ≥ Target.</summary>
        Counter,
        /// <summary>state.Flags contains Key (owned item / entitlement / tutorial flag).</summary>
        Flag,
        /// <summary>Wallet balance of Key ≥ Target (owned items stored as currencies, e.g. shards).</summary>
        Currency,
        /// <summary>Feature Key unlocked.</summary>
        Feature,
        /// <summary>Account at least Target days old.</summary>
        AccountAgeDays,
    }

    [Serializable]
    public sealed class RequirementDef
    {
        public RequirementKind Kind;
        public string Key;
        public long Target;
        /// <summary>Optional display text; generated when empty.</summary>
        public string Text;

        public RequirementDef() { }
        public RequirementDef(RequirementKind kind, string key, long target, string text = null) { Kind = kind; Key = key; Target = target; Text = text; }
    }

    /// <summary>All (default) or any of the listed requirements. Empty = always met.</summary>
    [Serializable]
    public sealed class RequirementSet
    {
        public bool Any;
        public List<RequirementDef> Items = new List<RequirementDef>();

        public static RequirementSet All(params RequirementDef[] items) { var s = new RequirementSet(); s.Items.AddRange(items); return s; }
        public static RequirementSet AnyOf(params RequirementDef[] items) { var s = All(items); s.Any = true; return s; }
    }

    /// <summary>A gated feature (GDD §16). Locked features stay visible with their requirement text.</summary>
    [Serializable]
    public sealed class FeatureDef
    {
        public string Id;
        public string Name;
        /// <summary>Position in the onboarding order (GDD §16).</summary>
        public int Order;
        public RequirementSet Requires = new RequirementSet();
        /// <summary>One-time gift claimed with the unlock celebration (FeatureCelebrate).</summary>
        public List<Reward> UnlockRewards = new List<Reward>();
        /// <summary>Optional tutorial step the UI should offer after unlocking.</summary>
        public string TutorialStep;
        /// <summary>
        /// Granted automatically the moment the unlock is recorded (the feature's starter items, e.g. the first
        /// alien when Aliens unlock). Unlike <see cref="UnlockRewards"/> it does not wait for the celebration.
        /// </summary>
        public List<Reward> UnlockGrants = new List<Reward>();
    }

    [Serializable]
    public sealed class TutorialStepDef
    {
        public string Id;
        public string Name;
        /// <summary>Step that must be completed first (optional).</summary>
        public string After;
        /// <summary>Feature that must be unlocked before this step can complete (optional).</summary>
        public string FeatureId;
        public List<Reward> Rewards = new List<Reward>();
    }

    [Serializable]
    public sealed class OnboardingConfig
    {
        /// <summary>Max lobby popups (offers, celebrations, news) shown per day.</summary>
        public int MaxLobbyPopupsPerDay = 3;
        /// <summary>
        /// Items every new player starts with (first parts, first blueprint). Starting currencies live in
        /// EconomyConfig.StartingWallet (Progression). Granted through the owners' granters.
        /// </summary>
        public List<Reward> StarterKit = new List<Reward>();
    }

    /// <summary>Daily/weekly reset anchors shared by quests, shop limits, ads, arena and guild.</summary>
    [Serializable]
    public sealed class ResetConfig
    {
        /// <summary>Daily reset at 00:00 UTC + offset.</summary>
        public long DailyResetOffsetMs;
        /// <summary>Any Monday 00:00 UTC (default 1970-01-05).</summary>
        public long WeeklyResetAnchorMs = 4 * MetaTime.Day;
    }

    public sealed partial class ContentDb
    {
        public readonly Dictionary<string, FeatureDef> FeatureDefs = new Dictionary<string, FeatureDef>();
        /// <summary>Features sorted by <see cref="FeatureDef.Order"/>.</summary>
        public readonly List<FeatureDef> FeatureOrder = new List<FeatureDef>();
        public readonly Dictionary<string, TutorialStepDef> TutorialSteps = new Dictionary<string, TutorialStepDef>();
        public readonly OnboardingConfig Onboarding = new OnboardingConfig();
        public readonly ResetConfig Resets = new ResetConfig();

        public FeatureDef Feature(string id) => id != null && FeatureDefs.TryGetValue(id, out var f) ? f : null;

        public void AddFeature(FeatureDef f)
        {
            if (FeatureDefs.ContainsKey(f.Id)) throw new InvalidOperationException("duplicate feature " + f.Id);
            FeatureDefs[f.Id] = f;
            FeatureOrder.Add(f);
            FeatureOrder.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
        }

        private static RequirementDef ReqMain(long level) => new RequirementDef(RequirementKind.MainLevel, null, level);
        private static RequirementDef ReqWorm(long level) => new RequirementDef(RequirementKind.WormLevel, null, level);
        private static RequirementDef ReqFeature(string id) => new RequirementDef(RequirementKind.Feature, id, 1);

        partial void BuildOnboarding()
        {
            int order = 0;
            void F(string id, string name, RequirementSet req, long diamonds = 0, string tutorial = null)
            {
                var f = new FeatureDef { Id = id, Name = name, Order = ++order, Requires = req, TutorialStep = tutorial };
                if (diamonds > 0) f.UnlockRewards.Add(Reward.Cur(Currencies.Diamonds, diamonds));
                AddFeature(f);
            }

            // GDD §16 order. Main level numbers are global (portal 1 = levels 1..10, portal 2 = 11..20, ...).
            F(Features.Parts, "Parts", RequirementSet.All(ReqMain(1)), 20, "equip_part");
            F(Features.Quests, "Quests", RequirementSet.All(ReqMain(1)));
            F(Features.BiomassFarm, "Biomass Farm", RequirementSet.All(ReqMain(2)), 20, "build_biomass_farm");
            F(Features.WormLevel, "Worm Level", RequirementSet.All(ReqMain(3)), 20, "level_worm");
            F(Features.PartPulls, "Part Signal", RequirementSet.All(ReqMain(3)), 30);
            F(Features.SeasonPass, "Season Pass", RequirementSet.All(ReqMain(3)));
            F(Features.Laboratory, "Laboratory", RequirementSet.All(ReqMain(4)), 30, "research_skill");
            F(Features.GrainFarm, "Grain Farm", RequirementSet.All(ReqMain(5)), 20);
            F(Features.Aliens, "Aliens", RequirementSet.All(ReqMain(6)), 50, "meet_alien");
            F(Features.AlienPulls, "Alien Signal", RequirementSet.All(ReqFeature(Features.Aliens)), 30);
            F(Features.AlienSquad, "Housing (Squad Slot 1)", RequirementSet.All(ReqMain(7)), 30);
            F(Features.Blueprints, "Blueprint Signal", RequirementSet.All(ReqMain(8)), 50);
            F(Features.OreMine, "Ore Mine", RequirementSet.All(ReqMain(9)), 20);
            F(Features.Heroes, "Heroes", RequirementSet.All(ReqMain(10)), 50, "equip_hero");
            F(Features.Runes, "Runes", RequirementSet.All(ReqMain(11)), 30);
            F(Features.Funds, "Funds", RequirementSet.All(ReqMain(11)));
            F(Features.Mounts, "Mounts", RequirementSet.All(ReqMain(12)), 50);
            F(Features.Breeding, "Breeding Pen", RequirementSet.All(ReqMain(14), ReqFeature(Features.Aliens)), 50);
            F(Features.Arena, "Arena", RequirementSet.All(ReqMain(16)), 50);
            F(Features.Guilds, "Guilds", RequirementSet.All(ReqMain(18)), 50);
            F(Features.Invasions, "Invasions", RequirementSet.All(ReqMain(20)), 50);
            F(Features.Overcharged, "Overcharged Levels", RequirementSet.All(ReqMain(22)), 50);
            F(Features.Forge, "Forge", RequirementSet.AnyOf(ReqWorm(25), ReqMain(28)), 50);
            F(Features.Burrow, "Burrow", RequirementSet.All(ReqMain(25), ReqWorm(15)), 50);
            F(Features.DarkSide, "Dark Side of the Planet", RequirementSet.All(ReqMain(40)), 100);

            void T(string id, string name, string after, string feature, long diamonds = 0)
            {
                var t = new TutorialStepDef { Id = id, Name = name, After = after, FeatureId = feature };
                if (diamonds > 0) t.Rewards.Add(Reward.Cur(Currencies.Diamonds, diamonds));
                TutorialSteps[id] = t;
            }
            // Starter kit (new player) and items granted when a feature unlocks (GDD §16 onboarding order).
            var kit = Onboarding.StarterKit;
            kit.Add(new Reward(RewardKinds.Part, "arm_slam_hammer", 1, (int)PartRarity.Common));
            kit.Add(new Reward(RewardKinds.Part, "head_scanner_visor", 1, (int)PartRarity.Common));
            kit.Add(new Reward(RewardKinds.Part, "body_plated_shell", 1, (int)PartRarity.Common));
            kit.Add(new Reward(RewardKinds.Blueprint, "bp_biomass_farm", 1));
            void G(string featureId, Reward r) => FeatureDefs[featureId].UnlockGrants.Add(r);
            G(Features.GrainFarm, new Reward(RewardKinds.Blueprint, "bp_grain_farm", 1));
            G(Features.Laboratory, new Reward(RewardKinds.Blueprint, "bp_laboratory", 1));
            G(Features.Aliens, new Reward(RewardKinds.Alien, "alien_blorp", 1));
            G(Features.AlienSquad, new Reward(RewardKinds.Blueprint, "bp_housing_pod", 1));
            G(Features.OreMine, new Reward(RewardKinds.Blueprint, "bp_ore_mine", 1));
            G(Features.Heroes, new Reward(RewardKinds.HeroShards, "hero_star_knight", 40)); // enough to unlock the first hero
            G(Features.Runes, new Reward(RewardKinds.Rune, "rune_vigor", 1, 0));
            G(Features.Mounts, new Reward(RewardKinds.Mount, "mount_scout_saucer", 1));
            G(Features.Breeding, new Reward(RewardKinds.Blueprint, "bp_breeding_pen", 1));

            T("wake_up", "The worm wakes", null, null);
            T("first_roll", "Roll the dice", "wake_up", null);
            T("first_battle", "Win a battle", "first_roll", null);
            T("first_run", "Finish your first run", "first_battle", null, 20);
            T("equip_part", "Equip a part", "first_run", Features.Parts, 10);
            T("build_biomass_farm", "Build the Biomass Farm", "equip_part", Features.BiomassFarm, 10);
            T("level_worm", "Level up the worm", "build_biomass_farm", Features.WormLevel, 10);
            T("research_skill", "Research a skill", "level_worm", Features.Laboratory, 10);
            T("meet_alien", "Meet your first alien", "research_skill", Features.Aliens, 10);
            T("equip_hero", "Choose a hero persona", null, Features.Heroes, 10);
        }
    }
}
