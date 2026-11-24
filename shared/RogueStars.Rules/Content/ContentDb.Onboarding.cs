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
        /// <summary>
        /// Account at least Target days old, counted in daily resets (GameClock.DayIndex): 0 on the creation day,
        /// 1 from the first reset after it ("day 2"), ...
        /// </summary>
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

    /// <summary>
    /// A gated feature (GDD §16). The client hides locked features; only the next one is teased
    /// (<see cref="RogueStars.Rules.Systems.Features.Next"/>).
    /// </summary>
    [Serializable]
    public sealed class FeatureDef
    {
        public string Id;
        public string Name;
        /// <summary>One line of what the feature does (unlock moment).</summary>
        public string Description;
        /// <summary>
        /// Unlock group: features with the same group unlock together (one celebration day). Null = its own group
        /// (the id). Members share their requirements.
        /// </summary>
        public string Group;
        public string GroupId => string.IsNullOrEmpty(Group) ? Id : Group;
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
        private static RequirementDef ReqFeature(string id) => new RequirementDef(RequirementKind.Feature, id, 1);
        /// <summary>Earliest account day (1 = the creation day): AccountAgeDays ≥ day − 1.</summary>
        private static RequirementDef ReqDay(int day) => new RequirementDef(RequirementKind.AccountAgeDays, null, day - 1);

        partial void BuildOnboarding()
        {
            int order = 0;
            void F(string id, string name, int day, long level, long diamonds, string tutorial, string description, string group = null)
            {
                var req = RequirementSet.All(ReqMain(level));
                if (day > 1) req.Items.Add(ReqDay(day));
                var f = new FeatureDef { Id = id, Name = name, Order = ++order, Requires = req, TutorialStep = tutorial, Description = description, Group = group };
                if (diamonds > 0) f.UnlockRewards.Add(Reward.Cur(Currencies.Diamonds, diamonds));
                AddFeature(f);
            }

            // GDD §16 onboarding v2: a three-week journey. Gate = main level cleared AND earliest account day (D1 = the
            // creation day, counted in reset days); after day 1 at most one unlock group per day (OnboardingSystem).
            // Main level numbers are global (portal 1 = levels 1..10, portal 2 = 11..20, ...).
            // Week 1: the worm and the run.
            F(Features.Parts, "Parts", 1, 1, 20, "equip_part", "Bolt parts onto your worm: every slot adds stats, rarer parts add skills.");
            F(Features.Quests, "Daily Goals", 1, 5, 0, null, "Daily and weekly goals with point chests, plus a 7-day login calendar.");
            F(Features.BiomassFarm, "City: Biomass Farm", 2, 8, 20, "build_biomass_farm", "Your first plot: the Biomass Farm grows Biomass while you are away.");
            // Approved as D3 L10; L6 keeps the day-3 unlock for ~1 h/day and binge players (they reach L9–10 by day 3) but avoids a
            // 9-day wall for casual players, who cannot clear level 10 without worm levels (sim: casual stuck at L9 day 10–22).
            F(Features.WormLevel, "Worm Level & Talents", 3, 6, 20, "level_worm", "Feed Biomass to level the worm and walk the talent road for permanent perks.");
            F(Features.PartPulls, "Part Signal", 4, 12, 30, null, "Tune in to the Part Signal for new parts. Your first pulls are on the house.");
            F(Features.Shop, "Shop", 4, 12, 0, null, "Offers, Diamond packs and the daily free gift.", group: Features.PartPulls);
            F(Features.Laboratory, "Laboratory", 5, 14, 30, "research_skill", "Research makes your worm permanently stronger or adds new skills to every run.");
            F(Features.GrainFarm, "Grain Farm", 6, 16, 20, null, "Grain feeds the colony: aliens and buildings run on it.");
            F(Features.Aliens, "Aliens", 7, 18, 50, "meet_alien", "Your first alien arrives. Aliens fight in your squad and staff buildings.");
            F(Features.AlienPulls, "Alien Signal", 7, 18, 30, null, "Call more aliens from across the galaxy.", group: Features.Aliens);
            // Week 2: the colony grows.
            F(Features.AlienSquad, "Housing (Squad Slot 1)", 8, 20, 30, null, "Build Housing to open your first squad slot: an alien fights beside the worm.");
            F(Features.Blueprints, "Blueprint Signal", 9, 22, 50, null, "Pull new building blueprints for the City.");
            F(Features.Heroes, "Heroes", 10, 24, 50, "hero_summon", "Hero summon events open: pull for hero shards and copies. A hero adds its kit to every run.");
            F(Features.OreMine, "Ore Mine", 11, 26, 20, null, "Ore powers part merges, the Replicator and upgrades.");
            F(Features.Runes, "Runes", 11, 26, 30, null, "Socket runes into parts for extra stats.", group: Features.OreMine);
            F(Features.Mounts, "Mounts", 12, 28, 50, null, "Ride into battle: mounts add stats, a shield and their own attacks. Summon events bring more mounts.");
            F(Features.Funds, "Funds", 13, 29, 0, null, "Growth funds pay out Diamonds as you clear main levels.");
            F(Features.Breeding, "Breeding Pen", 14, 30, 50, null, "Breed two aliens for a child with better genes.");
            F(Features.SeasonPass, "Season Pass", 14, 30, 0, null, "Earn pass points from daily goals for a track of rewards every season.", group: Features.Breeding);
            // Week 3: the wider galaxy.
            F(Features.Arena, "Arena", 15, 32, 50, null, "Fight other players' worms for league points and Arena Medals.");
            F(Features.Overcharged, "Overcharged Levels", 16, 34, 50, null, "Replay cleared levels with harder modifiers for bigger rewards.");
            F(Features.Guilds, "Guilds", 17, 36, 50, null, "Join a guild: chat, a guild shop and invasions together.");
            F(Features.Invasions, "Invasions", 18, 38, 50, null, "Invaders attack the planet: deal damage with your guild for rewards.");
            F(Features.Burrow, "Burrow", 19, 40, 50, null, "Dig ever deeper with Burrow keys: cash out or risk it all.");
            F(Features.Forge, "Forge", 20, 42, 50, null, "Forge extra slots into your best parts.");
            F(Features.DarkSide, "Dark Side of the Planet", 21, 45, 100, null, "An endless dark campaign that pays in laboratory Data.");

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
            // The former starting wallet, each part with the unlock that gives it a use (rewards follow use, GDD §13).
            G(Features.Parts, Reward.Cur(Currencies.Ore, 500));
            G(Features.BiomassFarm, Reward.Cur(Currencies.Biomass, 500));
            G(Features.PartPulls, Reward.Cur(Currencies.PartTokens, 10)); // "your first pulls are on the house"
            G(Features.Aliens, Reward.Cur(Currencies.Grain, 300));
            G(Features.AlienPulls, Reward.Cur(Currencies.AlienTokens, 5));
            G(Features.Blueprints, Reward.Cur(Currencies.BlueprintTokens, 5));
            G(Features.GrainFarm, new Reward(RewardKinds.Blueprint, "bp_grain_farm", 1));
            G(Features.Laboratory, new Reward(RewardKinds.Blueprint, "bp_laboratory", 1));
            G(Features.Laboratory, Reward.Cur(Currencies.Data, 60)); // the first researches (later: daily chests, Dark Side)
            G(Features.Aliens, new Reward(RewardKinds.Alien, "alien_blorp", 1));
            G(Features.AlienSquad, new Reward(RewardKinds.Blueprint, "bp_housing_pod", 1));
            G(Features.OreMine, new Reward(RewardKinds.Blueprint, "bp_ore_mine", 1));
            G(Features.Heroes, Reward.Cur(Currencies.HeroTickets, 10)); // the first hero summon event's 10× pull (no starter hero: heroes only come from events)
            G(Features.Mounts, Reward.Cur(Currencies.MountTickets, 10)); // the first mount summon event's 10× pull
            G(Features.Runes, new Reward(RewardKinds.Rune, "rune_vigor", 1, 0));
            G(Features.Breeding, new Reward(RewardKinds.Blueprint, "bp_breeding_pen", 1));

            T("wake_up", "The worm wakes", null, null);
            T("first_roll", "Roll the dice", "wake_up", null);
            T("first_battle", "Win a battle", "first_roll", null);
            T("first_run", "Finish your first run", "first_battle", null, 20);
            T("equip_part", "Equip a part", "first_run", Features.Parts, 10);
            T("build_biomass_farm", "Build the Biomass Farm", "equip_part", Features.BiomassFarm, 10);
            T("level_worm", "Level up the worm", "equip_part", Features.WormLevel, 10); // casual players can reach it before the City
            T("research_skill", "Research a skill", "level_worm", Features.Laboratory, 10);
            T("meet_alien", "Meet your first alien", "research_skill", Features.Aliens, 10);
            T("hero_summon", "Pull in the hero summon event", null, Features.Heroes, 10);
        }
    }
}
