using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Cross-runtime determinism probe for local-first battles: a fixed set of battles built from content (every hero, mount,
    /// worm-usable skill and enemy shows up in some battle, threats across the curve), simulated and fingerprinted with
    /// <see cref="StateHash"/> (full result, log included). The .NET test suite (server runtime) and the Unity EditMode suite
    /// (Mono, the client runtime) both compare <see cref="Fingerprint"/> with the same golden value
    /// (shared/RogueStars.Rules.Tests/Golden/battle-determinism.txt): the client's battle and the server's re-simulation
    /// of the same inputs must agree bit for bit.
    /// </summary>
    public static class BattleDeterminism
    {
        public const int DefaultCount = 48;

        /// <summary>The probe battles (setup + seed), built only from content in ordinal id order.</summary>
        public static List<KeyValuePair<BattleSetup, long>> Scenarios(ContentDb content, int count = DefaultCount)
        {
            content = content ?? ContentDb.Default;
            var skills = new List<string>();
            foreach (var kv in content.Skills)
            {
                var s = kv.Value;
                if (s.Source == SkillSource.Run || s.Source == SkillSource.Laboratory || s.Source == SkillSource.Hero || s.Source == SkillSource.Part)
                    skills.Add(kv.Key);
            }
            skills.Sort(string.CompareOrdinal);
            var mountSkills = new List<string>();
            foreach (var kv in content.Skills) if (kv.Value.Source == SkillSource.Mount) mountSkills.Add(kv.Key);
            mountSkills.Sort(string.CompareOrdinal);
            var alienSkills = new List<string>();
            foreach (var kv in content.Skills) if (kv.Value.Source == SkillSource.Alien) alienSkills.Add(kv.Key);
            alienSkills.Sort(string.CompareOrdinal);
            var enemies = new List<string>(content.Enemies.Keys);
            enemies.Sort(string.CompareOrdinal);

            var list = new List<KeyValuePair<BattleSetup, long>>();
            for (int i = 0; i < count; i++)
            {
                int threat = (i * 7) % 40;
                double scale = content.ThreatMultiplier(threat);
                var loadout = new CombatLoadout { DisplayName = "Probe" };
                loadout.WormStats.Add(new StatMod(StatId.Hp, 900 * scale));
                loadout.WormStats.Add(new StatMod(StatId.Atk, 90 * scale));
                loadout.WormStats.Add(new StatMod(StatId.Def, 12 * scale));
                loadout.WormStats.Add(new StatMod(StatId.Speed, 100 + i));
                loadout.WormStats.Add(new StatMod(StatId.CritChance, i % 30));
                loadout.WormStats.Add(new StatMod(StatId.Dodge, (i * 3) % 25));
                if (mountSkills.Count > 0 && i % 3 != 2)
                {
                    loadout.MountId = "probe_mount";
                    loadout.MountStats.Add(new StatMod(StatId.Atk, 40 * scale));
                    loadout.MetaSkillIds.Add(mountSkills[i % mountSkills.Count]);
                }
                if (alienSkills.Count > 0 && i % 2 == 0)
                {
                    var a = new CombatAlien { AlienTypeId = "probe_alien", Name = "Probe Alien" };
                    a.Stats.Add(new StatMod(StatId.Atk, 30 * scale)).Add(new StatMod(StatId.Speed, 50 + i));
                    a.SkillIds.Add(alienSkills[(i / 2) % alienSkills.Count]);
                    loadout.Aliens.Add(a);
                }
                // every worm skill appears: skill k goes into battle k % count, up to ~8 per battle (wrapping)
                var run = new List<string>();
                int per = Math.Max(10, (skills.Count + count - 1) / count);
                for (int k = i; k < skills.Count && run.Count < per; k += count) run.Add(skills[k]);
                if (run.Count < 4) for (int k = 0; k < 4 && skills.Count > 0; k++) run.Add(skills[(i * 13 + k * 31) % skills.Count]);
                var setup = new BattleSetup { RoundCap = i % 4 == 0 ? content.Combat.BossRoundCap : content.Combat.RoundCap };
                setup.Player.AddRange(BattleSimulator.BuildPlayerSide(loadout, content, run));
                int n = 1 + i % 3;
                for (int e = 0; e < n && enemies.Count > 0; e++)
                    setup.Enemy.Add(BattleSimulator.BuildEnemy(content, enemies[(i * 5 + e * 11) % enemies.Count], scale, 10 * e, 5 * (i % 5)));
                list.Add(new KeyValuePair<BattleSetup, long>(setup, 1000 + i * 7919L));
            }
            return list;
        }

        /// <summary>Fingerprint of every probe battle's full result (log included), combined in order.</summary>
        public static ulong Fingerprint(ContentDb content, int count = DefaultCount) => StateHash.Of(Results(content, count));

        /// <summary>Per-battle fingerprints (diagnostics: which battle diverged).</summary>
        public static List<string> BattleHashes(ContentDb content, int count = DefaultCount)
        {
            var res = new List<string>();
            foreach (var r in Results(content, count)) res.Add(StateHash.Hex(r));
            return res;
        }

        private static List<BattleResult> Results(ContentDb content, int count)
        {
            var res = new List<BattleResult>();
            foreach (var s in Scenarios(content, count))
                res.Add(BattleSimulator.Simulate(s.Key, content, Rng.FromSeed(s.Value)));
            return res;
        }
    }
}
