using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Onboarding: content-defined feature unlocks (GDD §16), the new-player starter kit and per-feature unlock
    /// grants (first alien, first housing...), unlock celebrations (opt-in, never forced), tutorial step flags and
    /// the daily lobby popup cap.
    /// Events: "feature_unlocked"(featureId), "tutorial_step"(step).
    /// </summary>
    public static class OnboardingSystem
    {
        public const string TutorialFlagPrefix = "tut:";

        public static void Register(GameEngine e)
        {
            e.RegisterSettler(CheckUnlocks);
            e.RegisterNewPlayer(ctx =>
            {
                ctx.State.Onboarding.Version = ctx.Content.Onboarding.Version;
                ctx.GrantAll(ctx.Content.Onboarding.StarterKit);
            });
            e.Register<TutorialAdvance>(HandleTutorial);
            e.Register<FeatureCelebrate>(HandleCelebrate);
            e.Register<LobbyPopupShown>(HandlePopup);
        }

        /// <summary>
        /// Records newly met feature unlocks (sticky) and emits "feature_unlocked". Runs as a settler before every
        /// command; other systems may also call it right after progress that can unlock features. A feature unlocks the
        /// moment its level is cleared (GDD §16: player level only, no calendar gate, no daily limit); a group unlocks as a
        /// whole once every member's requirements are met. Never re-locks.
        /// </summary>
        public static void CheckUnlocks(CommandContext ctx)
        {
            var s = ctx.State;
            var c = ctx.Content;
            if (s.Onboarding.Version < c.Onboarding.Version) Migrate(ctx);
            foreach (var group in Features.Groups(c))
            {
                if (Features.GroupUnlocked(s, group)) continue;
                if (!GroupRequirementsMet(s, c, group, ctx.NowMs)) continue;
                foreach (var f in group)
                {
                    if (s.Onboarding.Unlocked.ContainsKey(f.Id)) continue;
                    Record(ctx, f);
                    ctx.Emit("feature_unlocked", f.Id);
                }
            }
            CitySystem.EnsureCrashSitePod(ctx); // the Aliens unlock's Launch Pod stands on the crash site (old saves get it here too)
        }

        private static void Record(CommandContext ctx, FeatureDef f)
        {
            ctx.State.Onboarding.Unlocked[f.Id] = ctx.NowMs;
            ctx.GrantAll(f.UnlockGrants);
            RewardGate.Release(ctx, f.Id); // the stash kept for this feature's use (shown by the unlock moment)
        }

        /// <summary>
        /// A save from an older onboarding content version: features added since (<see cref="FeatureDef.Since"/>) whose level
        /// the save already cleared describe content it already had (shops, minibosses, auto roll, packages...). They are
        /// recorded and celebrated at once, gift included, without an unlock moment each (their "?" replays the intro).
        /// Everything the save had unlocked stays unlocked. Runs once per save.
        /// </summary>
        public static void Migrate(CommandContext ctx)
        {
            var s = ctx.State;
            var c = ctx.Content;
            int from = Math.Max(1, s.Onboarding.Version); // 0 = a save from before versions: the original features (Since 1)
            foreach (var f in c.FeatureOrder)
            {
                if (f.Since <= from || s.Onboarding.Unlocked.ContainsKey(f.Id)) continue;
                if (!Requirements.IsMet(s, c, f.Requires, ctx.NowMs)) continue;
                Record(ctx, f);
                if (s.Onboarding.Celebrated.Add(f.Id)) ctx.GrantAll(f.UnlockRewards);
            }
            s.Onboarding.Version = c.Onboarding.Version;
        }

        /// <summary>Every member's own requirements are met at <paramref name="nowMs"/>.</summary>
        public static bool GroupRequirementsMet(GameState s, ContentDb c, List<FeatureDef> group, long nowMs)
        {
            foreach (var f in group)
                if (!s.Onboarding.Unlocked.ContainsKey(f.Id) && !Requirements.IsMet(s, c, f.Requires, nowMs)) return false;
            return true;
        }

        /// <summary>Unlocked features whose celebration has not been opened yet (UI badge, not a forced popup).</summary>
        public static List<string> PendingCelebrations(GameState state, ContentDb content)
        {
            var list = new List<string>();
            foreach (var f in content.FeatureOrder)
                if (state.Onboarding.Unlocked.ContainsKey(f.Id) && !state.Onboarding.Celebrated.Contains(f.Id)) list.Add(f.Id);
            return list;
        }

        public static bool IsStepDone(GameState state, string step) => state.Onboarding.TutorialDone.Contains(step);

        /// <summary>True while today's lobby popup cap is not reached.</summary>
        public static bool CanShowLobbyPopup(GameState state, ContentDb content, long nowMs)
        {
            long day = MetaTime.DayIndex(content, nowMs);
            int shown = state.Onboarding.PopupDay == day ? state.Onboarding.PopupsShownToday : 0;
            return shown < content.Onboarding.MaxLobbyPopupsPerDay;
        }

        private static CommandResult HandleTutorial(TutorialAdvance cmd, CommandContext ctx)
        {
            var s = ctx.State;
            GameEngine.Require(cmd.Step != null && ctx.Content.TutorialSteps.TryGetValue(cmd.Step, out _), "unknown tutorial step");
            var def = ctx.Content.TutorialSteps[cmd.Step];
            GameEngine.Require(!s.Onboarding.TutorialDone.Contains(def.Id), "tutorial step already done");
            GameEngine.Require(def.After == null || s.Onboarding.TutorialDone.Contains(def.After), "previous tutorial step not done");
            GameEngine.Require(def.FeatureId == null || Features.IsUnlocked(s, ctx.Content, def.FeatureId), "feature locked");
            s.Onboarding.TutorialDone.Add(def.Id);
            s.Flags.Add(TutorialFlagPrefix + def.Id);
            s.Count("tutorial_steps");
            ctx.GrantAll(def.Rewards);
            ctx.Emit("tutorial_step", def.Id);
            return CommandResult.Success();
        }

        private static CommandResult HandleCelebrate(FeatureCelebrate cmd, CommandContext ctx)
        {
            var s = ctx.State;
            var def = ctx.Content.Feature(cmd.FeatureId);
            GameEngine.Require(def != null, "unknown feature");
            GameEngine.Require(s.Onboarding.Unlocked.ContainsKey(def.Id), "feature locked");
            GameEngine.Require(!s.Onboarding.Celebrated.Contains(def.Id), "already celebrated");
            s.Onboarding.Celebrated.Add(def.Id);
            ctx.GrantAll(def.UnlockRewards);
            return CommandResult.Success();
        }

        private static CommandResult HandlePopup(LobbyPopupShown cmd, CommandContext ctx)
        {
            var s = ctx.State;
            GameEngine.Require(CanShowLobbyPopup(s, ctx.Content, ctx.NowMs), "lobby popup cap reached");
            long day = MetaTime.DayIndex(ctx.Content, ctx.NowMs);
            if (s.Onboarding.PopupDay != day) { s.Onboarding.PopupDay = day; s.Onboarding.PopupsShownToday = 0; }
            s.Onboarding.PopupsShownToday++;
            return CommandResult.Success();
        }
    }

    /// <summary>Unlock state of one feature (requirement text/progress).</summary>
    public sealed class FeatureStatus
    {
        public string Id;
        public string Name;
        public int Order;
        public bool Unlocked;
        /// <summary>Main level that unlocks it.</summary>
        public long Level;
        public string RequirementText;
        public List<RequirementProgress> Progress = new List<RequirementProgress>();
    }

    /// <summary>
    /// The single next locked unlock (lowest onboarding order), for the hub teaser (<see cref="Features.Next"/>). Everything
    /// after it stays hidden.
    /// </summary>
    public sealed class NextUnlock
    {
        public string GroupId;
        /// <summary>Member feature ids in onboarding order.</summary>
        public List<string> Ids = new List<string>();
        /// <summary>Member names joined with " + ".</summary>
        public string Name;
        /// <summary>One line of what it does (the first member's description).</summary>
        public string Description;
        /// <summary>Main level to clear (0 = no level gate).</summary>
        public long Level;
        public long CurrentLevel;
        public bool LevelMet;
        /// <summary>Main levels still to clear (0 when the level is met).</summary>
        public long LevelsToGo;
        /// <summary>"clear level 12", "unlocking now".</summary>
        public string Text;
        public List<RequirementProgress> Progress = new List<RequirementProgress>();
    }

    /// <summary>
    /// Feature gating used by every system ("is the Breeding Pen unlocked?"). Content-defined rules
    /// (<see cref="ContentDb.FeatureDefs"/>); unlocks are sticky once recorded by the onboarding settler.
    /// Unknown feature ids are treated as ungated (unlocked). The flag <see cref="DevUnlockAllFlag"/> unlocks everything (dev/tests).
    /// </summary>
    public static class Features
    {
        public const string Parts = "parts";
        public const string BiomassFarm = "biomass_farm";
        public const string WormLevel = "worm_level";
        public const string GrainFarm = "grain_farm";
        public const string OreMine = "ore_mine";
        public const string SeasonPass = "season_pass";
        public const string Funds = "funds";
        public const string Laboratory = "laboratory";
        public const string Aliens = "aliens";
        public const string AlienSquad = "alien_squad";
        /// <summary>The Blueprint Signal comes with the City unlock: the same feature id.</summary>
        public const string Blueprints = BiomassFarm;
        public const string Heroes = "heroes";
        public const string Mounts = "mounts";
        public const string Breeding = "breeding";
        public const string Arena = "arena";
        public const string Guilds = "guilds";
        public const string Invasions = "invasions";
        public const string Overcharged = "overcharged";
        public const string Burrow = "burrow";
        public const string DarkSide = "dark_side";
        /// <summary>The Part Signal comes with the Parts unlock (owner, 2026-11-25): the same feature id.</summary>
        public const string PartPulls = Parts;
        /// <summary>The Alien Signal comes with the Aliens unlock: the same feature id.</summary>
        public const string AlienPulls = Aliens;
        public const string Forge = "forge";
        public const string Runes = "runes";

        // Level unlocks (content version 2): run content, the auto roll and the Laboratory's signature packages.
        /// <summary>Run content: the miniboss lair and the awakening clock's minibosses (Mythic choices).</summary>
        public const string RunMiniboss = "run_miniboss";
        /// <summary>Run content: two dice (portal levels 6–10) and forks in the path.</summary>
        public const string RunTwoDice = "run_two_dice";
        /// <summary>Run content: campfires (fixed tile and pool tile).</summary>
        public const string RunCampfire = "run_campfire";
        /// <summary>Run content: the run-start signature choice (CampaignDef.SignatureChoiceFromLevel; before it runs have no signature).</summary>
        public const string RunSignatures = "run_signatures";
        /// <summary>Run content: Alien Arcade GAME tiles (MinigameTuning.MinMainLevel).</summary>
        public const string RunMinigames = "run_minigames";
        /// <summary>Hold the dice to auto-roll (client convenience; auto never answers a decision).</summary>
        public const string AutoRoll = "auto_roll";
        /// <summary>Laboratory signature packages (each package node's RequiresFeature). The first (Combo) comes with the Laboratory.</summary>
        public const string PackageIce = "package_ice";
        public const string PackageLightning = "package_lightning";
        public const string PackageCounter = "package_counter";
        public const string PackageAlien = "package_alien";
        public const string PackageTime = "package_time";

        /// <summary>State flag that unlocks every feature (dev cheats, other workstreams' tests).</summary>
        public const string DevUnlockAllFlag = "dev_unlock_all";

        /// <summary>
        /// Unlocked = recorded by the onboarding settler (<see cref="OnboardingSystem.CheckUnlocks"/>), the dev flag, or an
        /// ungated id. Requirements being met is not enough (the settler records them before the next command).
        /// </summary>
        public static bool IsUnlocked(GameState state, ContentDb content, string featureId)
        {
            if (state.Flags.Contains(DevUnlockAllFlag)) return true;
            if (state.Onboarding.Unlocked.ContainsKey(featureId)) return true;
            return content.Feature(featureId) == null;
        }

        /// <summary>Throws RuleException("feature locked") unless the feature is unlocked (gate for command handlers).</summary>
        public static void Require(GameState state, ContentDb content, string featureId) =>
            GameEngine.Require(IsUnlocked(state, content, featureId), "feature locked");

        /// <summary>All features in onboarding order with unlock state and requirement text/progress.</summary>
        public static List<FeatureStatus> List(GameState state, ContentDb content, long nowMs = 0)
        {
            var list = new List<FeatureStatus>();
            foreach (var f in content.FeatureOrder)
            {
                list.Add(new FeatureStatus
                {
                    Id = f.Id, Name = f.Name, Order = f.Order, Level = UnlockLevel(f),
                    Unlocked = IsUnlocked(state, content, f.Id),
                    RequirementText = Requirements.Describe(content, f.Requires),
                    Progress = Requirements.Progress(state, content, f.Requires, nowMs),
                });
            }
            return list;
        }

        /// <summary>
        /// Unlock groups in onboarding order (each group's members in order). Cached per content (the onboarding settler
        /// runs on every command) and shared: do not modify the lists.
        /// </summary>
        public static List<List<FeatureDef>> Groups(ContentDb content)
        {
            if (GroupCache.TryGetValue(content, out var cached) && cached.Count == content.FeatureOrder.Count) return cached.Groups;
            var groups = BuildGroups(content);
            GroupCache.AddOrUpdate(content, new CachedGroups { Count = content.FeatureOrder.Count, Groups = groups });
            return groups;
        }

        private sealed class CachedGroups
        {
            public int Count;
            public List<List<FeatureDef>> Groups;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDb, CachedGroups> GroupCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ContentDb, CachedGroups>();

        private static List<List<FeatureDef>> BuildGroups(ContentDb content)
        {
            var groups = new List<List<FeatureDef>>();
            var index = new Dictionary<string, List<FeatureDef>>();
            foreach (var f in content.FeatureOrder)
            {
                if (!index.TryGetValue(f.GroupId, out var g)) { g = new List<FeatureDef>(); index[f.GroupId] = g; groups.Add(g); }
                g.Add(f);
            }
            return groups;
        }

        /// <summary>Main level a feature needs (0 = none).</summary>
        public static long UnlockLevel(FeatureDef def)
        {
            long level = 0;
            foreach (var r in def.Requires.Items) if (r.Kind == RequirementKind.MainLevel) level = Math.Max(level, r.Target);
            return level;
        }

        /// <summary>A member unlocks at a main level (false for building features that unlock with their blueprint).</summary>
        public static bool HasLevelGate(List<FeatureDef> group)
        {
            foreach (var f in group) if (UnlockLevel(f) > 0) return true;
            return false;
        }

        public static bool GroupUnlocked(GameState state, List<FeatureDef> group)
        {
            foreach (var f in group) if (!state.Onboarding.Unlocked.ContainsKey(f.Id)) return false;
            return true;
        }

        /// <summary>
        /// The single next locked unlock (lowest onboarding order) with its level progress and teaser text; null when
        /// everything is unlocked (or the dev flag is set).
        /// </summary>
        public static NextUnlock Next(GameState state, ContentDb content, long nowMs)
        {
            if (state.Flags.Contains(DevUnlockAllFlag)) return null;
            foreach (var group in Groups(content))
            {
                if (GroupUnlocked(state, group)) continue;
                if (!HasLevelGate(group)) continue; // a building's feature (unlocks with its blueprint): nothing to tease by level
                var n = new NextUnlock { GroupId = group[0].GroupId, Description = group[0].Description };
                var names = new List<string>();
                bool otherMet = true;
                foreach (var f in group)
                {
                    n.Ids.Add(f.Id);
                    names.Add(f.Name);
                    foreach (var p in Requirements.Progress(state, content, f.Requires, nowMs))
                    {
                        if (p.Requirement.Kind == RequirementKind.MainLevel) { n.Level = Math.Max(n.Level, p.Target); n.CurrentLevel = p.Current; }
                        else if (!p.Met) otherMet = false;
                        if (!n.Progress.Exists(x => x.Text == p.Text)) n.Progress.Add(p);
                    }
                }
                n.Name = string.Join(" + ", names);
                n.LevelMet = n.CurrentLevel >= n.Level;
                n.LevelsToGo = Math.Max(0, n.Level - n.CurrentLevel);
                n.Text = TeaserText(n, otherMet);
                return n;
            }
            return null;
        }

        private static string TeaserText(NextUnlock n, bool otherMet)
        {
            var parts = new List<string>();
            if (!n.LevelMet && n.Level > 0) parts.Add("clear level " + n.Level);
            if (!otherMet)
                foreach (var p in n.Progress)
                    if (!p.Met && p.Requirement.Kind != RequirementKind.MainLevel) parts.Add(p.Text);
            return parts.Count == 0 ? "unlocking now" : string.Join(" · ", parts);
        }

        /// <summary>Only the locked features (with their requirement).</summary>
        public static List<FeatureStatus> Locked(GameState state, ContentDb content, long nowMs = 0)
        {
            var all = List(state, content, nowMs);
            all.RemoveAll(f => f.Unlocked);
            return all;
        }
    }
}
