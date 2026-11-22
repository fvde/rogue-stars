using System;
using System.Collections.Generic;

namespace RogueStars.Rules.Core
{
    /// <summary>
    /// Timed economy/gameplay modifier from invasions, planetary events, passes or buildings.
    /// Target keys: "production:&lt;currency&gt;", "run_reward:&lt;currency&gt;", "run_reward:*", "stamina_regen",
    /// "breeding_speed", "build_speed", "enemy_atk", ... Multipliers combine multiplicatively.
    /// </summary>
    [Serializable]
    public sealed class Modifier
    {
        public string Id;
        public string Source;        // e.g. "invasion:slime_swarm", "event:diamond_rush", "pass:monthly"
        public string Target;
        public double Multiplier = 1;
        public long StartMs;
        public long EndMs;           // 0 = permanent
        public string Description;

        public bool ActiveAt(long nowMs) => nowMs >= StartMs && (EndMs == 0 || nowMs < EndMs);
    }

    public static class ModifierMath
    {
        /// <summary>Product of all active multipliers for a target (exact key and the "prefix:*" wildcard).</summary>
        public static double Multiplier(IEnumerable<Modifier> modifiers, string target, long nowMs)
        {
            double m = 1;
            if (modifiers == null) return m;
            int colon = target.IndexOf(':');
            string wildcard = colon >= 0 ? target.Substring(0, colon) + ":*" : null;
            foreach (var mod in modifiers)
            {
                if (!mod.ActiveAt(nowMs)) continue;
                if (mod.Target == target || (wildcard != null && mod.Target == wildcard)) m *= mod.Multiplier;
            }
            return m;
        }

        public static void RemoveExpired(List<Modifier> modifiers, long nowMs)
        {
            modifiers.RemoveAll(m => m.EndMs != 0 && nowMs >= m.EndMs);
        }
    }
}
