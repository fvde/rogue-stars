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
            e.RegisterNewPlayer(ctx => ctx.GrantAll(ctx.Content.Onboarding.StarterKit));
            e.Register<TutorialAdvance>(HandleTutorial);
            e.Register<FeatureCelebrate>(HandleCelebrate);
            e.Register<LobbyPopupShown>(HandlePopup);
        }

        /// <summary>
        /// Records newly met feature unlocks (sticky) and emits "feature_unlocked". Runs as a settler before every
        /// command; other systems may also call it right after progress that can unlock features.
        /// Groups unlock as a whole once every member's requirements are met. Day-1 groups (no account-day gate) are
        /// never limited; every later group needs a free daily slot: at most one such group per reset day (the lowest in
        /// onboarding order), so a player who comes back after a break catches up one group per day.
        /// </summary>
        public static void CheckUnlocks(CommandContext ctx)
        {
            var s = ctx.State;
            var c = ctx.Content;
            bool usedToday = UnlockedToday(s, c, ctx.NowMs);
            foreach (var group in Features.Groups(c))
            {
                if (Features.GroupUnlocked(s, group)) continue;
                if (!GroupRequirementsMet(s, c, group, ctx.NowMs)) continue;
                bool limited = Features.GroupDay(group) > 1;
                if (limited && usedToday) return;
                foreach (var f in group)
                {
                    if (s.Onboarding.Unlocked.ContainsKey(f.Id)) continue;
                    s.Onboarding.Unlocked[f.Id] = ctx.NowMs;
                    ctx.Emit("feature_unlocked", f.Id);
                    ctx.GrantAll(f.UnlockGrants);
                    RewardGate.Release(ctx, f.Id); // the stash kept for this feature's use (shown by the unlock moment)
                }
                if (limited) usedToday = true;
            }
        }

        /// <summary>Every member's own requirements are met at <paramref name="nowMs"/> (ignores the daily limit).</summary>
        public static bool GroupRequirementsMet(GameState s, ContentDb c, List<FeatureDef> group, long nowMs)
        {
            foreach (var f in group)
                if (!s.Onboarding.Unlocked.ContainsKey(f.Id) && !Requirements.IsMet(s, c, f.Requires, nowMs)) return false;
            return true;
        }

        /// <summary>
        /// True when today's daily unlock slot is used: a day-gated feature (earliest day ≥ 2) was recorded on the reset
        /// day of <paramref name="nowMs"/>.
        /// </summary>
        public static bool UnlockedToday(GameState s, ContentDb c, long nowMs)
        {
            long today = MetaTime.DayIndex(c, nowMs);
            foreach (var kv in s.Onboarding.Unlocked)
            {
                if (MetaTime.DayIndex(c, kv.Value) != today) continue;
                var def = c.Feature(kv.Key);
                if (def != null && Features.UnlockDay(def) > 1) return true;
            }
            return false;
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
        public string RequirementText;
        public List<RequirementProgress> Progress = new List<RequirementProgress>();
    }

    /// <summary>
    /// The single next locked unlock group, for the hub teaser (<see cref="Features.Next"/>). Everything after it stays
    /// hidden.
    /// </summary>
    public sealed class NextUnlock
    {
        public string GroupId;
        /// <summary>Member feature ids in onboarding order.</summary>
        public List<string> Ids = new List<string>();
        /// <summary>Member names joined with " + ".</summary>
        public string Name;
        /// <summary>Main level to clear (0 = no level gate).</summary>
        public long Level;
        public long CurrentLevel;
        public bool LevelMet;
        /// <summary>Earliest account day (1 = creation day).</summary>
        public int Day;
        /// <summary>The account's current day (1 = creation day).</summary>
        public int CurrentDay;
        /// <summary>Whole days until the group can unlock: the day gate, or tomorrow when today's unlock is used up. 0 = today.</summary>
        public int DaysToWait;
        /// <summary>Requirements met but today's one unlock is already used.</summary>
        public bool WaitingForDailyLimit;
        /// <summary>"clear level 12", "clear level 12 · unlocks tomorrow", "unlocks tomorrow", "unlocks in 3 days".</summary>
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
        /// <summary>The shop (offers, packs, daily gift). Client navigation gate; purchases themselves are not gated.</summary>
        public const string Shop = "shop";

        /// <summary>State flag that unlocks every feature (dev cheats, other workstreams' tests).</summary>
        public const string DevUnlockAllFlag = "dev_unlock_all";

        /// <summary>
        /// Unlocked = recorded by the onboarding settler (<see cref="OnboardingSystem.CheckUnlocks"/>, which applies the day
        /// gate and the one-per-day limit), the dev flag, or an ungated id. Requirements being met is not enough.
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
                    Id = f.Id, Name = f.Name, Order = f.Order,
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

        /// <summary>Earliest account day of a feature (1 = creation day): its AccountAgeDays requirement + 1.</summary>
        public static int UnlockDay(FeatureDef def)
        {
            long age = 0;
            foreach (var r in def.Requires.Items) if (r.Kind == RequirementKind.AccountAgeDays) age = Math.Max(age, r.Target);
            return (int)age + 1;
        }

        /// <summary>Earliest account day of a group (its latest member).</summary>
        public static int GroupDay(List<FeatureDef> group)
        {
            int day = 1;
            foreach (var f in group) day = Math.Max(day, UnlockDay(f));
            return day;
        }

        /// <summary>Main level a feature needs (0 = none).</summary>
        public static long UnlockLevel(FeatureDef def)
        {
            long level = 0;
            foreach (var r in def.Requires.Items) if (r.Kind == RequirementKind.MainLevel) level = Math.Max(level, r.Target);
            return level;
        }

        public static bool GroupUnlocked(GameState state, List<FeatureDef> group)
        {
            foreach (var f in group) if (!state.Onboarding.Unlocked.ContainsKey(f.Id)) return false;
            return true;
        }

        /// <summary>
        /// The single next locked unlock group (lowest onboarding order) with its level/day progress and teaser text;
        /// null when everything is unlocked (or the dev flag is set).
        /// </summary>
        public static NextUnlock Next(GameState state, ContentDb content, long nowMs)
        {
            if (state.Flags.Contains(DevUnlockAllFlag)) return null;
            foreach (var group in Groups(content))
            {
                if (GroupUnlocked(state, group)) continue;
                var n = new NextUnlock { GroupId = group[0].GroupId };
                var names = new List<string>();
                long age = Requirements.AccountAgeDays(state, content, nowMs);
                n.CurrentDay = (int)age + 1;
                n.Day = 1;
                bool otherMet = true;
                foreach (var f in group)
                {
                    n.Ids.Add(f.Id);
                    names.Add(f.Name);
                    foreach (var p in Requirements.Progress(state, content, f.Requires, nowMs))
                    {
                        if (p.Requirement.Kind == RequirementKind.MainLevel) { n.Level = Math.Max(n.Level, p.Target); n.CurrentLevel = p.Current; }
                        else if (p.Requirement.Kind == RequirementKind.AccountAgeDays) n.Day = (int)Math.Max(n.Day, p.Target + 1);
                        else if (!p.Met) otherMet = false;
                        if (!n.Progress.Exists(x => x.Text == p.Text)) n.Progress.Add(p);
                    }
                }
                n.Name = string.Join(" + ", names);
                n.LevelMet = n.CurrentLevel >= n.Level;
                int dayWait = Math.Max(0, n.Day - n.CurrentDay);
                bool usedToday = GroupDay(group) > 1 && OnboardingSystem.UnlockedToday(state, content, nowMs);
                n.DaysToWait = Math.Max(dayWait, usedToday ? 1 : 0);
                n.WaitingForDailyLimit = usedToday && dayWait == 0 && n.LevelMet && otherMet;
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
                    if (!p.Met && p.Requirement.Kind != RequirementKind.MainLevel && p.Requirement.Kind != RequirementKind.AccountAgeDays) parts.Add(p.Text);
            if (n.DaysToWait == 1) parts.Add("unlocks tomorrow");
            else if (n.DaysToWait > 1) parts.Add("unlocks in " + n.DaysToWait + " days");
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
