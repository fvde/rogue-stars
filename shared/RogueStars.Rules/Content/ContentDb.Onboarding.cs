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
        /// 1 from the first reset after it ("day 2"), ... Not used by feature gates (GDD §16: nothing unlocks by calendar day).
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

    /// <summary>Animation of one <see cref="FeatureIntroStep"/> (the client draws it; an unknown kind shows the art still).</summary>
    public static class IntroAnim
    {
        /// <summary>A hand pointer taps the art.</summary>
        public const string Tap = "tap";
        /// <summary>The art breathes (scale + glow).</summary>
        public const string Pulse = "pulse";
        /// <summary>The art slides in from the side into its place.</summary>
        public const string Slide = "slide";
        /// <summary>A number counts up next to the art.</summary>
        public const string Count = "count";
        /// <summary>The art turns (dice, signal dish).</summary>
        public const string Spin = "spin";
        /// <summary>The art hops.</summary>
        public const string Bounce = "bounce";
        /// <summary>A bar under the art fills up.</summary>
        public const string Fill = "fill";

        public static readonly string[] All = { Tap, Pulse, Slide, Count, Spin, Bounce, Fill };
    }

    /// <summary>One panel of a feature's intro (unlock screen and its "?" replay): a short line, an art key, an animation.</summary>
    [Serializable]
    public sealed class FeatureIntroStep
    {
        public string Text;
        /// <summary>ArtCatalog key (<c>Art/Intro/&lt;Art&gt;</c>); the client draws a fallback while the file is missing.</summary>
        public string Art;
        /// <summary>One of <see cref="IntroAnim"/>.</summary>
        public string Anim;

        public FeatureIntroStep() { }
        public FeatureIntroStep(string text, string art, string anim) { Text = text; Art = art; Anim = anim; }
    }

    /// <summary>
    /// A gated feature (GDD §16). Every feature unlocks at a main level cleared (the player level), never by calendar day.
    /// The client hides locked features; only the next one is teased (<see cref="RogueStars.Rules.Systems.Features.Next"/>).
    /// </summary>
    [Serializable]
    public sealed class FeatureDef
    {
        public string Id;
        public string Name;
        /// <summary>One line of what the feature does (unlock moment, hub teaser).</summary>
        public string Description;
        /// <summary>
        /// Unlock group: features with the same group unlock together. Null = its own group (the id). Members share their
        /// requirements. The level-unlock table gives every feature its own level, so no content uses groups today.
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
        /// <summary>
        /// Run content (new tiles or rules on the board): introduced after the level before the first level that has it
        /// (the level's own content fields carry it, ContentDb.Campaign).
        /// </summary>
        public bool RunContent;
        /// <summary>ArtCatalog key of the unlock screen's big art (<c>Art/Intro/&lt;Art&gt;</c>, drawn fallback).</summary>
        public string Art;
        /// <summary>The explainer (2–4 panels) of the unlock screen; the feature's "?" button replays it.</summary>
        public List<FeatureIntroStep> Intro = new List<FeatureIntroStep>();
        /// <summary>
        /// Content version that added the feature id (1 = the original features, 2 = level unlocks). A save from before
        /// version 2 gets the version-2 features its level already meets recorded and celebrated silently: it already had
        /// that content (OnboardingSystem.Migrate).
        /// </summary>
        public int Since = 1;
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
        /// <summary>Onboarding content version of new saves (see FeatureDef.Since, OnboardingState.Version).</summary>
        public int Version = 2;
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

        /// <summary>First main level whose board has the run content of <paramref name="featureId"/> (the gate is the level before).</summary>
        public int RunContentLevel(string featureId)
        {
            var f = Feature(featureId);
            return f == null ? 0 : (int)Features.UnlockLevel(f) + 1;
        }

        partial void BuildOnboarding()
        {
            int order = 0;
            FeatureDef F(string id, string name, long level, long diamonds, string tutorial, string description, int since = 1, bool run = false)
            {
                var f = new FeatureDef
                {
                    Id = id, Name = name, Order = ++order, Requires = RequirementSet.All(ReqMain(level)), TutorialStep = tutorial,
                    Description = description, Since = since, RunContent = run, Art = id,
                };
                if (diamonds > 0) f.UnlockRewards.Add(Reward.Cur(Currencies.Diamonds, diamonds));
                AddFeature(f);
                return f;
            }
            FeatureDef R(string id, string name, long level, string description) => F(id, name, level, 0, null, description, 2, true);

            // GDD §16 level unlocks: every feature unlocks at a main level cleared (the player level: Requirements.MainLevelCounter),
            // never by calendar day, one new thing per level up to level 20, then every 2 levels, then every 3–4. Systems come
            // before what depends on them and every reward kind waits for its use (RewardGate). Run content is introduced
            // after the level before the first board that has it. Main level numbers are global (portal 1 = levels 1..10, ...).
            F(Features.Parts, "Parts", 1, 20, "equip_part", "Bolt parts onto your worm: every slot adds stats, rarer parts add skills.");
            R(Features.RunShop, "Shops", Campaign.ShopsFromLevel - 1, "Shops on the board: spend run coins on skills and boosts.");
            F(Features.WormLevel, "Worm Level & Talents", 3, 20, "level_worm", "Feed Biomass to level the worm and walk the talent road for permanent perks.");
            R(Features.RunMiniboss, "Minibosses & Mythics", Campaign.MinibossesFromLevel - 1, "The waking boss sends minibosses. Beat one for a Mythic skill.");
            R(Features.RunTwoDice, "Two Dice & Forks", Campaign.ForksFromLevel - 1, "Roll two dice and choose your way where the path forks.");
            F(Features.BiomassFarm, "City: Biomass Farm", 6, 20, "build_biomass_farm", "Your first plot: the Biomass Farm grows Biomass while you are away.");
            F(Features.AutoRoll, "Auto Roll", 7, 0, null, "Hold the dice to roll on its own. It stops for every choice.", since: 2);
            F(Features.Quests, "Daily Goals", 8, 0, null, "Daily and weekly goals with point chests, plus a 7-day login calendar.");
            R(Features.RunCampfire, "Campfires", Campaign.CampfiresFromLevel - 1, "Campfires on the board: rest to heal or train a skill.");
            F(Features.PartPulls, "Part Signal", 10, 30, null, "Tune in to the Part Signal for new parts. Your first pulls are on the house.");
            R(Features.RunSignatures, "Signature Choice", Campaign.SignatureChoiceFromLevel - 1, "Pick your run's signature skill from three.");
            F(Features.Shop, "Shop", 12, 0, null, "Offers, Diamond packs and the daily free gift.");
            F(Features.Laboratory, "Laboratory", 13, 30, "research_skill", "Research makes your worm permanently stronger or adds new skills to every run.");
            R(Features.RunMinigames, "Alien Arcade", MinigameTuning.MinMainLevel - 1, "Game tiles on the board: a quick game for coins or a skill.");
            F(Features.Aliens, "Aliens", 15, 50, "meet_alien", "Your first alien arrives. Aliens fight in your squad and staff buildings.");
            F(Features.AlienSquad, "Housing (Squad Slot 1)", 16, 30, null, "Build Housing to open your first squad slot: an alien fights beside the worm.");
            F(Features.GrainFarm, "Grain Farm", 17, 20, null, "Grain feeds the colony: aliens and breeding run on it.");
            F(Features.Heroes, "Heroes", 18, 50, "hero_summon", "Hero summon events open: pull for hero shards and copies. A hero adds its kit to every run.");
            F(Features.AlienPulls, "Alien Signal", 19, 30, null, "Call more aliens from across the galaxy.");
            F(Features.Blueprints, "Blueprint Signal", 20, 50, null, "Pull new building blueprints for the City.");
            F(Features.OreMine, "Ore Mine", 22, 20, null, "Ore powers part merges, the Replicator and upgrades.");
            F(Features.PackageIce, "Ice Package", 24, 0, null, "A new Laboratory package: research it to add Ice skills to your runs.", since: 2);
            F(Features.Mounts, "Mounts", 26, 50, null, "Ride into battle: mounts add stats, a shield and their own attacks. Summon events bring more mounts.");
            F(Features.Runes, "Runes", 28, 30, null, "Socket runes into parts for extra stats.");
            F(Features.Funds, "Funds", 30, 0, null, "Growth funds pay out Diamonds as you clear main levels.");
            F(Features.Arena, "Arena", 32, 50, null, "Fight other players' worms for league points and Arena Medals.");
            F(Features.Breeding, "Breeding Pen", 34, 50, null, "Breed two aliens for a child with better genes.");
            F(Features.SeasonPass, "Season Pass", 36, 0, null, "Earn pass points from daily goals for a track of rewards every season.");
            F(Features.PackageLightning, "Lightning Package", 38, 0, null, "A new Laboratory package: research it to add Lightning skills to your runs.", since: 2);
            F(Features.Overcharged, "Overcharged Levels", 40, 50, null, "Replay cleared levels with harder modifiers for bigger rewards.");
            F(Features.Guilds, "Guilds", 42, 50, null, "Join a guild: chat, a guild shop and invasions together.");
            F(Features.Invasions, "Invasions", 44, 50, null, "Invaders attack the planet: deal damage with your guild for rewards.");
            F(Features.Burrow, "Burrow", 46, 50, null, "Dig ever deeper with Burrow keys: cash out or risk it all.");
            F(Features.PackageCounter, "Counter Package", 48, 0, null, "A new Laboratory package: research it to add Counter skills to your runs.", since: 2);
            F(Features.Forge, "Forge", 50, 50, null, "Forge extra slots into your best parts.");
            F(Features.DarkSide, "Dark Side of the Planet", 53, 100, null, "An endless dark campaign that pays in laboratory Data.");
            F(Features.PackageAlien, "Alien Package", 56, 0, null, "A new Laboratory package: research it to add Alien skills to your runs.", since: 2);
            F(Features.PackageTime, "Time Package", 60, 0, null, "A new Laboratory package: research it to add Time skills to your runs.", since: 2);
            BuildFeatureIntros();

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
            G(Features.Mounts, new Reward(RewardKinds.Mount, "mount_scout_saucer", 1));
            G(Features.Breeding, new Reward(RewardKinds.Blueprint, "bp_breeding_pen", 1));

            T("wake_up", "The worm wakes", null, null);
            T("first_roll", "Roll the dice", "wake_up", null);
            T("first_battle", "Win a battle", "first_roll", null);
            T("first_run", "Finish your first run", "first_battle", null, 20);
            T("equip_part", "Equip a part", "first_run", Features.Parts, 10);
            T("level_worm", "Level up the worm", "equip_part", Features.WormLevel, 10);
            T("build_biomass_farm", "Build the Biomass Farm", "equip_part", Features.BiomassFarm, 10);
            T("research_skill", "Research a skill", "level_worm", Features.Laboratory, 10);
            T("meet_alien", "Meet your first alien", "research_skill", Features.Aliens, 10);
            T("hero_summon", "Pull in the hero summon event", null, Features.Heroes, 10);
        }

        /// <summary>The unlock-screen explainers (2–4 short panels each, GDD §16). Every feature has one (tests enforce it).</summary>
        private void BuildFeatureIntros()
        {
            void I(string featureId, params string[] steps)
            {
                var f = FeatureDefs[featureId];
                for (int i = 0; i + 2 < steps.Length; i += 3) f.Intro.Add(new FeatureIntroStep(steps[i], steps[i + 1], steps[i + 2]));
            }
            const string Tap = IntroAnim.Tap, Pulse = IntroAnim.Pulse, Slide = IntroAnim.Slide, Count = IntroAnim.Count,
                Spin = IntroAnim.Spin, Bounce = IntroAnim.Bounce, Fill = IntroAnim.Fill;

            I(Features.Parts,
                "Your worm has slots: head, body and two arms.", "parts_slots", Pulse,
                "Put a part in a slot. Every part adds stats.", "part_equip", Slide,
                "Spend Ore to level parts up.", "ore", Count,
                "Rarer parts add skills to every run.", "part_rare", Bounce);
            I(Features.RunShop,
                "Shops now stand on the board.", "shop_tile", Bounce,
                "Land on one to spend your run coins.", "coins", Count,
                "Buy skills and boosts for this run.", "shop_buy", Tap);
            I(Features.WormLevel,
                "Feed Biomass to your worm to level it up.", "biomass", Count,
                "Every level adds HP and attack.", "worm_up", Bounce,
                "Levels open talents: perks that last forever.", "talents", Slide);
            I(Features.RunMiniboss,
                "As the boss wakes, it sends minibosses.", "miniboss", Bounce,
                "Beat a miniboss to open a Mythic chest.", "chest_mythic", Tap,
                "Pick one Mythic: it changes the whole run.", "mythic", Pulse);
            I(Features.RunTwoDice,
                "From now on you roll two dice.", "dice", Spin,
                "Your worm moves farther each roll.", "worm_move", Slide,
                "Where the path forks, tap the way to go.", "fork", Tap);
            I(Features.BiomassFarm,
                "Your city has its first plot.", "city", Pulse,
                "Build the Biomass Farm on it.", "farm_build", Tap,
                "It grows Biomass while you are away.", "biomass", Count);
            I(Features.AutoRoll,
                "Hold the dice to roll on its own.", "dice_hold", Tap,
                "Auto waits for every choice and fight.", "auto_pause", Pulse,
                "Tap anywhere to stop it.", "tap_stop", Tap);
            I(Features.Quests,
                "Every day brings new goals.", "goals", Slide,
                "Goals fill your point bar.", "points", Fill,
                "Open the chests on the bar.", "chest", Bounce,
                "Log in each day for the 7-day calendar.", "calendar", Pulse);
            I(Features.RunCampfire,
                "Campfires now burn on the board.", "campfire", Pulse,
                "Rest to heal, or train a skill.", "campfire_choice", Tap);
            I(Features.PartPulls,
                "Tune in to the Part Signal for new parts.", "signal", Spin,
                "Each pull costs one Part Token.", "token", Count,
                "Merge three equal parts into a rarer one.", "merge", Slide);
            I(Features.RunSignatures,
                "Every run has a signature skill.", "signature", Pulse,
                "Now you pick it: one of three.", "signature_pick", Tap,
                "Collect skills of its kind to awaken it.", "signature_awake", Fill);
            I(Features.Shop,
                "The Shop has offers and Diamond packs.", "shop", Bounce,
                "A free gift waits there every day.", "gift", Tap);
            I(Features.Laboratory,
                "Research makes your worm stronger for good.", "lab", Pulse,
                "Research costs Data and takes time.", "data", Fill,
                "Packages add new skills to your runs.", "package", Slide);
            I(Features.RunMinigames,
                "Game tiles now appear on the board.", "game_tile", Bounce,
                "Land on one to play a quick game.", "minigame", Tap,
                "Win a medal for coins or a skill.", "medal", Count);
            I(Features.Aliens,
                "Your first alien has arrived.", "alien", Bounce,
                "Aliens staff buildings and join your squad.", "alien_squad", Slide,
                "Aliens run on Grain.", "grain", Count);
            I(Features.AlienSquad,
                "Build Housing to open a squad slot.", "housing", Tap,
                "A squad alien fights beside your worm.", "alien_fight", Slide);
            I(Features.GrainFarm,
                "The Grain Farm grows Grain for your aliens.", "grain_farm", Pulse,
                "Collect it in the City.", "grain", Count);
            I(Features.Heroes,
                "Hero summon events are open.", "hero_event", Bounce,
                "Pull with Hero Tickets for heroes and shards.", "hero_pull", Spin,
                "Your hero adds its skills to every run.", "hero", Pulse);
            I(Features.AlienPulls,
                "Call more aliens with the Alien Signal.", "alien_signal", Spin,
                "Each pull costs one Alien Token.", "alien_token", Count);
            I(Features.Blueprints,
                "Pull blueprints for new buildings.", "blueprint_signal", Spin,
                "Build them in the City for goods and boosts.", "building", Slide);
            I(Features.OreMine,
                "The Ore Mine digs Ore while you are away.", "ore_mine", Pulse,
                "Ore levels up and merges your parts.", "ore", Count);
            I(Features.PackageIce,
                "A new package is ready in the Laboratory.", "package", Pulse,
                "Research it to add Ice skills to your runs.", "ice", Slide,
                "Ice chills enemies and makes them fragile.", "ice_skill", Bounce);
            I(Features.Mounts,
                "Mount summon events are open.", "mount_event", Bounce,
                "A mount adds stats, a shield and its own attacks.", "mount", Slide,
                "Level mounts with Fuel.", "fuel", Count);
            I(Features.Runes,
                "Runes fit into part sockets.", "rune", Slide,
                "Each rune adds extra stats.", "rune_stats", Count);
            I(Features.Funds,
                "Funds pay Diamonds as you clear levels.", "funds", Count,
                "Buy once, claim at every milestone.", "funds_claim", Tap);
            I(Features.Arena,
                "Fight other players' worms.", "arena", Bounce,
                "Wins bring league points and Arena Medals.", "arena_medal", Count,
                "Spend medals in the Arena shop.", "arena_shop", Tap);
            I(Features.Breeding,
                "Breed two aliens in the Breeding Pen.", "breeding", Pulse,
                "The child can get better genes.", "genes", Fill);
            I(Features.SeasonPass,
                "Daily goals give pass points.", "pass_points", Count,
                "Points climb a track of rewards each season.", "pass_track", Fill);
            I(Features.PackageLightning,
                "A new package is ready in the Laboratory.", "package", Pulse,
                "Research it to add Lightning skills to your runs.", "lightning", Slide,
                "Lightning arcs from enemy to enemy.", "lightning_skill", Bounce);
            I(Features.Overcharged,
                "Replay cleared levels Overcharged.", "overcharged", Pulse,
                "Tougher foes, bigger rewards.", "overcharged_reward", Bounce);
            I(Features.Guilds,
                "Join a guild or start your own.", "guild", Pulse,
                "Chat, a guild shop and invasions together.", "guild_shop", Slide);
            I(Features.Invasions,
                "Invaders attack the planet.", "invasion", Bounce,
                "Fight them from the Observatory.", "observatory", Tap,
                "Deal damage with your guild for rewards.", "invasion_reward", Count);
            I(Features.Burrow,
                "Dig down floor by floor with Burrow Keys.", "burrow", Slide,
                "Cash out between floors, or risk it all.", "burrow_cash", Tap);
            I(Features.PackageCounter,
                "A new package is ready in the Laboratory.", "package", Pulse,
                "Research it to add Counter skills to your runs.", "counter", Slide,
                "Counter skills strike back when you are hit.", "counter_skill", Bounce);
            I(Features.Forge,
                "Forge an extra slot into a part.", "forge", Pulse,
                "More slots, more power from your best parts.", "forge_slot", Bounce);
            I(Features.DarkSide,
                "The dark side of the planet is open.", "dark_side", Pulse,
                "Endless levels with extreme rules.", "dark_rules", Bounce,
                "Clears pay Data for the Laboratory.", "data", Count);
            I(Features.PackageAlien,
                "A new package is ready in the Laboratory.", "package", Pulse,
                "Research it to add Alien skills to your runs.", "alien_package", Slide,
                "Alien skills make your squad stronger.", "alien_fight", Bounce);
            I(Features.PackageTime,
                "A new package is ready in the Laboratory.", "package", Pulse,
                "Research it to add Time skills to your runs.", "time", Slide,
                "Time skills let you strike first.", "time_skill", Bounce);
        }
    }
}
