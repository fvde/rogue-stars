using System;
using System.Collections.Generic;

namespace RogueStars.Rules.Core
{
    /// <summary>
    /// Complete persistent state of one player. Partial: every system adds its own fields in
    /// Systems/&lt;System&gt;/&lt;System&gt;State.cs. Keep fields public and serializable (no properties with logic).
    /// </summary>
    [Serializable]
    public sealed partial class GameState
    {
        public string PlayerId;
        public string DisplayName;
        /// <summary>Incremented on every successful command; used for optimistic concurrency.</summary>
        public long Version;
        public long CreatedMs;
        public long LastCommandMs;
        public Wallet Wallet = new Wallet();
        public List<Modifier> Modifiers = new List<Modifier>();
        /// <summary>Persistent RNG stream for authoritative rolls (pulls, breeding, loot).</summary>
        public Rng Rng = new Rng();
        /// <summary>Generic counters for quests/achievements/analytics ("runs_completed", "pulls_part", ...).</summary>
        public Dictionary<string, long> Counters = new Dictionary<string, long>();
        /// <summary>Generic one-off flags (tutorial steps, claimed one-time rewards...).</summary>
        public HashSet<string> Flags = new HashSet<string>();

        public long Counter(string key) => Counters.TryGetValue(key, out var v) ? v : 0;
        public void Count(string key, long amount = 1) => Counters[key] = Counter(key) + amount;
    }
}
