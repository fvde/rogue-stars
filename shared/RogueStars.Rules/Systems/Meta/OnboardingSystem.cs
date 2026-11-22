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
            e.RegisterNewPlayer(ctx => ctx.GrantAll(ctx.Content.Onboarding.StarterKit));
            e.Register<TutorialAdvance>(HandleTutorial);
            e.Register<FeatureCelebrate>(HandleCelebrate);
            e.Register<LobbyPopupShown>(HandlePopup);
        }

        /// <summary>
        /// Records newly met feature unlocks (sticky) and emits "feature_unlocked". Runs as a settler before
        /// every command; other systems may also call it right after progress that can unlock features.
        /// </summary>
        public static void CheckUnlocks(CommandContext ctx)
        {
            var s = ctx.State;
            foreach (var f in ctx.Content.FeatureOrder)
            {
                if (s.Onboarding.Unlocked.ContainsKey(f.Id)) continue;
                if (!Requirements.IsMet(s, ctx.Content, f.Requires, ctx.NowMs)) continue;
                s.Onboarding.Unlocked[f.Id] = ctx.NowMs;
                ctx.Emit("feature_unlocked", f.Id);
                ctx.GrantAll(f.UnlockGrants);
            }
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

    /// <summary>Unlock state of one feature for the "locked features are visible" UI.</summary>
    public sealed class FeatureStatus
    {
        public string Id;
        public string Name;
        public int Order;
        public bool Unlocked;
        public string RequirementText;
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
        public const string Quests = "quests";
        public const string BiomassFarm = "biomass_farm";
        public const string WormLevel = "worm_level";
        public const string GrainFarm = "grain_farm";
        public const string OreMine = "ore_mine";
        public const string SeasonPass = "season_pass";
        public const string Funds = "funds";
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

        /// <summary>State flag that unlocks every feature (dev cheats, other workstreams' tests).</summary>
        public const string DevUnlockAllFlag = "dev_unlock_all";

        public static bool IsUnlocked(GameState state, ContentDb content, string featureId)
        {
            if (state.Flags.Contains(DevUnlockAllFlag)) return true;
            if (state.Onboarding.Unlocked.ContainsKey(featureId)) return true;
            var def = content.Feature(featureId);
            if (def == null) return true;
            return Requirements.IsMet(state, content, def.Requires);
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
                    Id = f.Id, Name = f.Name, Order = f.Order,
                    Unlocked = IsUnlocked(state, content, f.Id),
                    RequirementText = Requirements.Describe(content, f.Requires),
                    Progress = Requirements.Progress(state, content, f.Requires, nowMs),
                });
            }
            return list;
        }

        /// <summary>Only the locked features (visible with their requirement).</summary>
        public static List<FeatureStatus> Locked(GameState state, ContentDb content, long nowMs = 0)
        {
            var all = List(state, content, nowMs);
            all.RemoveAll(f => f.Unlocked);
            return all;
        }
    }
}
