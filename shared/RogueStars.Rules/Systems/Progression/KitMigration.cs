using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Old saves (hero &amp; mount kits): hero traits and mount weapons used to be ProgFamilySkill steps (<c>hero_x_a1 … _b2</c>,
    /// <c>mount_x_w1 … _w4</c>, <c>_s1</c>, <c>_s2</c>) and mounts had stats. A player's stars, awakening and levels are kept and the
    /// kits come from them, so a saved GameState needs nothing. Saved loadouts (an active run's, arena snapshots) still list the old
    /// skill ids: they are read as the new kit at the step the old ids show (the highest old step present), and old mount stats are
    /// ignored (a mount's power is mount damage).
    /// </summary>
    public static class KitMigration
    {
        private static readonly string[] HeroSuffix = { "_a1", "_a2", "_b1", "_a3", "_b2" };
        private static readonly int[] HeroStar = { 1, 3, 5, 7, 10 };
        private static readonly string[] MountSuffix = { "_w1", "_w2", "_w3", "_s1", "_s2", "_w4" };
        private static readonly int[][] MountStep = { new[] { 1, 0 }, new[] { 2, 0 }, new[] { 4, 0 }, new[] { 5, 0 }, new[] { 5, 3 }, new[] { 5, 5 } };

        /// <summary>The loadout's meta skill ids with old hero / mount step ids replaced by the kits at those steps (a new list).</summary>
        public static List<string> MetaSkillIds(ContentDb c, CombatLoadout lo)
        {
            var ids = lo != null ? lo.MetaSkillIds : new List<string>();
            var outIds = new List<string>(ids.Count);
            int heroStar = 0, mountStar = 0, mountAwaken = 0;
            string hero = null, mount = null;
            foreach (var id in ids)
            {
                if (id == null) continue;
                if (c.Skill(id) == null && LegacyHero(c, id, out var h, out int star))
                {
                    hero = h; heroStar = Math.Max(heroStar, star);
                    continue;
                }
                if (c.Skill(id) == null && LegacyMount(c, id, out var m, out int ms, out int ma))
                {
                    mount = m;
                    if (ms > mountStar || (ms == mountStar && ma > mountAwaken)) { mountStar = ms; mountAwaken = ma; }
                    continue;
                }
                outIds.Add(id);
            }
            if (hero != null)
                foreach (var sk in HeroSystem.TraitSkills(c, hero, heroStar)) if (!outIds.Contains(sk)) outIds.Add(sk);
            if (mount != null)
                foreach (var sk in MountSystem.WeaponSkills(c, mount, mountStar + mountAwaken)) if (!outIds.Contains(sk)) outIds.Add(sk);
            return outIds;
        }

        /// <summary>True if the loadout still lists old hero / mount step ids.</summary>
        public static bool HasLegacy(ContentDb c, CombatLoadout lo)
        {
            if (lo == null) return false;
            foreach (var id in lo.MetaSkillIds)
                if (id != null && c.Skill(id) == null && (LegacyHero(c, id, out _, out _) || LegacyMount(c, id, out _, out _, out _))) return true;
            return false;
        }

        /// <summary>Rewrites an old loadout in place (an active run's on load, an arena snapshot).</summary>
        public static void Migrate(ContentDb c, CombatLoadout lo)
        {
            if (!HasLegacy(c, lo)) return;
            var ids = MetaSkillIds(c, lo);
            lo.MetaSkillIds.Clear();
            lo.MetaSkillIds.AddRange(ids);
            lo.MountStats.Flat.Remove(StatId.Atk); lo.MountStats.Flat.Remove(StatId.Hp); lo.MountStats.Flat.Remove(StatId.DamageReduction);
            lo.MountStats.Percent.Remove(StatId.Atk); lo.MountStats.Percent.Remove(StatId.Hp); lo.MountStats.Percent.Remove(StatId.DamageReduction);
        }

        internal static bool LegacyHero(ContentDb c, string id, out string hero, out int star)
        {
            hero = null; star = 0;
            for (int i = 0; i < HeroSuffix.Length; i++)
            {
                if (!id.EndsWith(HeroSuffix[i], StringComparison.Ordinal)) continue;
                string h = id.Substring(0, id.Length - HeroSuffix[i].Length);
                if (!c.HeroConfig.Defs.ContainsKey(h)) continue;
                hero = h; star = HeroStar[i];
                return true;
            }
            return false;
        }

        internal static bool LegacyMount(ContentDb c, string id, out string mount, out int star, out int awaken)
        {
            mount = null; star = 0; awaken = 0;
            for (int i = 0; i < MountSuffix.Length; i++)
            {
                if (!id.EndsWith(MountSuffix[i], StringComparison.Ordinal)) continue;
                string m = id.Substring(0, id.Length - MountSuffix[i].Length);
                if (!c.MountConfig.Defs.ContainsKey(m)) continue;
                mount = m; star = MountStep[i][0]; awaken = MountStep[i][1];
                return true;
            }
            return false;
        }
    }
}
