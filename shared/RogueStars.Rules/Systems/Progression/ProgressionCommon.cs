using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Currency ids introduced by the Progression workstream (in addition to Core.Currencies).</summary>
    public static class ProgressionCurrencies
    {
        /// <summary>Endgame material for S / SS part awakening.</summary>
        public const string PartAwakenCores = "part_awaken_cores";
    }

    /// <summary>Well-known state flags read by Progression (set by other workstreams).</summary>
    public static class ProgressionFlags
    {
        /// <summary>Permanent "No Ads" purchase (set by Meta). Ad placements complete instantly.</summary>
        public const string NoAds = "ent_no_ads";
    }

    /// <summary>Small shared helpers for the Progression systems.</summary>
    public static class ProgressionUtil
    {
        /// <summary>Spend costs or throw a RuleException ("not enough ...").</summary>
        public static void Spend(GameState s, IList<Cost> costs)
        {
            if (!s.Wallet.TrySpend(costs))
            {
                string what = "";
                foreach (var c in costs) if (!s.Wallet.CanAfford(c.Currency, c.Amount)) { what = c.Currency; break; }
                throw new RuleException("not enough " + what);
            }
        }

        public static void Spend(GameState s, string currency, long amount)
        {
            GameEngine.Require(s.Wallet.TrySpend(currency, amount), "not enough " + currency);
        }

        /// <summary>Set a counter to max(current, value) (for "highest reached" style counters).</summary>
        public static void CountMax(GameState s, string key, long value)
        {
            if (value > s.Counter(key)) s.Counters[key] = value;
        }

        public static StatBlock Scaled(IEnumerable<StatMod> mods, double factor)
        {
            var b = new StatBlock();
            if (mods == null) return b;
            foreach (var m in mods) b.Add(new StatMod(m.Stat, m.Flat * factor, m.Percent * factor));
            return b;
        }

        /// <summary>Apply an ordered skill-step list: add each unlocked skill, removing the one it replaces.</summary>
        public static List<string> ResolveSkillSteps(IEnumerable<SkillStep> steps, Func<SkillStep, bool> unlocked)
        {
            var list = new List<string>();
            foreach (var st in steps)
            {
                if (!unlocked(st) || list.Contains(st.SkillId)) continue;
                int at = string.IsNullOrEmpty(st.Replaces) ? -1 : list.IndexOf(st.Replaces);
                if (at >= 0) list[at] = st.SkillId; // upgrade keeps its position
                else list.Add(st.SkillId);
            }
            return list;
        }
    }

    /// <summary>A skill that unlocks at a milestone (star / awakening) and optionally replaces a previous one.</summary>
    [Serializable]
    public sealed class SkillStep
    {
        public int Star;
        /// <summary>Awakening level required (mounts); 0 = none.</summary>
        public int Awaken;
        public string SkillId;
        public string Replaces;

        public SkillStep() { }
        public SkillStep(int star, int awaken, string skillId, string replaces = null) { Star = star; Awaken = awaken; SkillId = skillId; Replaces = replaces; }
    }
}
