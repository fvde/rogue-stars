using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Progress of one requirement (for "locked feature" UI).</summary>
    public sealed class RequirementProgress
    {
        public RequirementDef Requirement;
        public long Current;
        public long Target;
        public bool Met;
        public string Text;
    }

    /// <summary>
    /// Requirement engine used by feature gates, shop visibility and onboarding. Requirements read
    /// counters/flags/wallet by default; other workstreams' real state can be plugged in after merge with
    /// <see cref="SetProgressProvider"/> (e.g. MainLevel → Run's cleared-level state).
    /// </summary>
    public static class Requirements
    {
        /// <summary>Counter holding the highest main portal level number cleared (Run keeps it as a max, not a sum).</summary>
        public const string MainLevelCounter = "main_level_max";
        /// <summary>Counter holding the current worm level (Progression keeps it equal to the level).</summary>
        public const string WormLevelCounter = "worm_level";

        private static readonly Dictionary<RequirementKind, Func<GameState, ContentDb, RequirementDef, long>> Providers =
            new Dictionary<RequirementKind, Func<GameState, ContentDb, RequirementDef, long>>();
        private static readonly object Gate = new object();

        /// <summary>
        /// Override how the current value of a requirement kind is read (wiring hook for the lead).
        /// Pass null to restore the default counter/flag based reading. Provider must be pure.
        /// </summary>
        public static void SetProgressProvider(RequirementKind kind, Func<GameState, ContentDb, RequirementDef, long> provider)
        {
            lock (Gate)
            {
                if (provider == null) Providers.Remove(kind);
                else Providers[kind] = provider;
            }
        }

        /// <summary>Current value of the requirement's measured quantity.</summary>
        public static long Current(GameState state, ContentDb content, RequirementDef req) => Current(state, content, req, 0);

        private static long Current(GameState state, ContentDb content, RequirementDef req, int depth)
        {
            Func<GameState, ContentDb, RequirementDef, long> provider;
            lock (Gate) Providers.TryGetValue(req.Kind, out provider);
            if (provider != null) return provider(state, content, req);
            switch (req.Kind)
            {
                case RequirementKind.MainLevel: return state.Counter(MainLevelCounter);
                case RequirementKind.WormLevel: return state.Counter(WormLevelCounter);
                case RequirementKind.Counter: return state.Counter(req.Key);
                case RequirementKind.Flag: return state.Flags.Contains(req.Key) ? 1 : 0;
                case RequirementKind.Currency: return state.Wallet.Get(req.Key);
                case RequirementKind.Feature:
                    if (depth > 8) return 0; // guard against cyclic content
                    return FeatureUnlockedInternal(state, content, req.Key, depth + 1) ? 1 : 0;
                case RequirementKind.AccountAgeDays: return 0; // needs time: see Current(state, content, req, now)
                default: return 0;
            }
        }

        private static bool FeatureUnlockedInternal(GameState state, ContentDb content, string featureId, int depth)
        {
            if (state.Flags.Contains(Features.DevUnlockAllFlag)) return true;
            if (state.Onboarding.Unlocked.ContainsKey(featureId)) return true;
            var def = content.Feature(featureId);
            if (def == null) return true;
            return IsMet(state, content, def.Requires, 0, depth);
        }

        private static long Target(RequirementDef req)
        {
            if (req.Kind == RequirementKind.Flag || req.Kind == RequirementKind.Feature) return 1;
            return req.Target;
        }

        public static bool IsMet(GameState state, ContentDb content, RequirementDef req, long nowMs = 0) => IsMet(state, content, req, nowMs, 0);

        private static bool IsMet(GameState state, ContentDb content, RequirementDef req, long nowMs, int depth)
        {
            if (req == null) return true;
            if (req.Kind == RequirementKind.AccountAgeDays) return nowMs - state.CreatedMs >= req.Target * MetaTime.Day;
            return Current(state, content, req, depth) >= Target(req);
        }

        /// <summary>All (or any, when <see cref="RequirementSet.Any"/>) requirements met. Null/empty set = met.</summary>
        public static bool IsMet(GameState state, ContentDb content, RequirementSet set, long nowMs = 0) => IsMet(state, content, set, nowMs, 0);

        private static bool IsMet(GameState state, ContentDb content, RequirementSet set, long nowMs, int depth)
        {
            if (set == null || set.Items.Count == 0) return true;
            foreach (var r in set.Items)
            {
                bool met = IsMet(state, content, r, nowMs, depth);
                if (set.Any && met) return true;
                if (!set.Any && !met) return false;
            }
            return !set.Any;
        }

        /// <summary>Per-requirement progress for UI ("Clear main level 5 (3/5)").</summary>
        public static List<RequirementProgress> Progress(GameState state, ContentDb content, RequirementSet set, long nowMs = 0)
        {
            var list = new List<RequirementProgress>();
            if (set == null) return list;
            foreach (var r in set.Items)
            {
                long cur = r.Kind == RequirementKind.AccountAgeDays
                    ? Math.Max(0, (nowMs - state.CreatedMs) / MetaTime.Day)
                    : Current(state, content, r);
                list.Add(new RequirementProgress { Requirement = r, Current = cur, Target = Target(r), Met = IsMet(state, content, r, nowMs), Text = Describe(content, r) });
            }
            return list;
        }

        /// <summary>Human-readable text of one requirement (content Text wins).</summary>
        public static string Describe(ContentDb content, RequirementDef r)
        {
            if (r == null) return "";
            if (!string.IsNullOrEmpty(r.Text)) return r.Text;
            switch (r.Kind)
            {
                case RequirementKind.MainLevel: return "Clear main level " + r.Target;
                case RequirementKind.WormLevel: return "Reach worm level " + r.Target;
                case RequirementKind.Counter: return r.Key + " " + r.Target;
                case RequirementKind.Flag: return "Requires " + r.Key;
                case RequirementKind.Currency: return "Own " + r.Target + " " + r.Key;
                case RequirementKind.Feature:
                    var f = content?.Feature(r.Key);
                    return "Unlock " + (f != null ? f.Name : r.Key);
                case RequirementKind.AccountAgeDays: return "Play for " + r.Target + " days";
                default: return r.Kind.ToString();
            }
        }

        /// <summary>Text of a whole set: "A and B" / "A or B".</summary>
        public static string Describe(ContentDb content, RequirementSet set)
        {
            if (set == null || set.Items.Count == 0) return "";
            var parts = new List<string>();
            foreach (var r in set.Items) parts.Add(Describe(content, r));
            return string.Join(set.Any ? " or " : " and ", parts);
        }
    }
}
