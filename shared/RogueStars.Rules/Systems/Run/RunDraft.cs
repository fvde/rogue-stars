using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Level-up draft (GDD §5): choose 1 of N. Pool = draftable Run skills + Laboratory skills toggled on
    /// (CombatLoadout.DraftPoolSkillIds). Gating: upgrade chains (UpgradesFrom must be owned), prerequisites,
    /// family count. One synergy slot favours owned families; a hidden pity guarantees a Legendary+ offer.
    /// </summary>
    public static class RunDraft
    {
        /// <summary>All skill ids in ordinal order (deterministic iteration over content).</summary>
        public static List<string> SortedSkillIds(ContentDb content)
        {
            var sorted = Sorted(content);
            var ids = new List<string>(sorted.Length);
            foreach (var s in sorted) ids.Add(s.Id);
            return ids;
        }

        /// <summary>
        /// Skills in ordinal id order, cached per content (drafts and shops run on every run step; sorting a thousand ids
        /// each time dominated bot simulations). Rebuilt when skills are added (tests add skills to their content).
        /// </summary>
        private static SkillDef[] Sorted(ContentDb content)
        {
            if (SortedCache.TryGetValue(content, out var cached) && cached.Length == content.Skills.Count) return cached;
            var ids = new List<string>(content.Skills.Keys);
            ids.Sort(StringComparer.Ordinal);
            var defs = new SkillDef[ids.Count];
            for (int i = 0; i < ids.Count; i++) defs[i] = content.Skills[ids[i]];
            SortedCache.AddOrUpdate(content, defs);
            return defs;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDb, SkillDef[]> SortedCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ContentDb, SkillDef[]>();

        public static int FamilyCount(ContentDb content, ActiveRun run, SkillFamily family)
        {
            if (family == SkillFamily.None) return 0;
            int n = 0;
            foreach (var id in run.Skills) { var s = content.Skill(id); if (s != null && s.Family == family) n++; }
            return n;
        }

        /// <summary>Level of a skill in this run: 0 = not owned, 1 = owned, 2+ = levelled up (RunTuning.MaxSkillLevel).</summary>
        public static int Level(ActiveRun run, string skillId)
        {
            if (skillId == null || !run.Skills.Contains(skillId)) return 0;
            return run.SkillLevels.TryGetValue(skillId, out var l) && l > 1 ? l : 1;
        }

        /// <summary>True if the owned skill can be offered again as a level-up: below the max level and not the lower tier of an upgrade chain (its next tier is the upgrade).</summary>
        public static bool CanLevelUp(ContentDb content, ActiveRun run, SkillDef s)
        {
            if (s == null || !run.Skills.Contains(s.Id)) return false;
            return Level(run, s.Id) < content.RunTuning.MaxSkillLevel && !HasUpgrade(content, s.Id);
        }

        /// <summary>True if another skill upgrades from this one (an upgrade chain's lower tier).</summary>
        public static bool HasUpgrade(ContentDb content, string skillId)
        {
            if (!UpgradedCache.TryGetValue(content, out var set) || set.Version != content.Skills.Count)
            {
                set = new UpgradedSet { Version = content.Skills.Count };
                foreach (var s in Sorted(content)) if (s.UpgradesFrom != null) set.Add(s.UpgradesFrom);
                UpgradedCache.AddOrUpdate(content, set);
            }
            return set.Contains(skillId);
        }

        private sealed class UpgradedSet : HashSet<string> { public int Version; }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDb, UpgradedSet> UpgradedCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ContentDb, UpgradedSet>();

        private static bool InPool(ActiveRun run, SkillDef s) =>
            s != null && s.Draftable && (s.Source == SkillSource.Run || run.Loadout.DraftPoolSkillIds.Contains(s.Id));

        /// <summary>
        /// True if the skill may currently be offered to this run: a new skill whose chain, prerequisites and family gate are
        /// met, or an owned skill that can still level up (<see cref="CanLevelUp"/>).
        /// </summary>
        public static bool IsEligible(ContentDb content, ActiveRun run, SkillDef s)
        {
            if (!InPool(run, s)) return false;
            if (run.Skills.Contains(s.Id)) return CanLevelUp(content, run, s);
            if (run.ReplacedSkills.Contains(s.Id)) return false;
            if (s.UpgradesFrom != null && !run.Skills.Contains(s.UpgradesFrom)) return false;
            foreach (var r in s.Requires) if (!run.Skills.Contains(r)) return false;
            if (s.RequiresFamilyCount > 0 && FamilyCount(content, run, s.Family) < s.RequiresFamilyCount) return false;
            return true;
        }

        /// <summary>
        /// Mythic choice pool (after a beaten miniboss): every Mythic skill of the pool the run does not own (family gates and
        /// prerequisites waived — the miniboss is the gate) plus owned mythics that can still level up.
        /// </summary>
        public static bool IsMythicEligible(ContentDb content, ActiveRun run, SkillDef s)
        {
            if (!InPool(run, s) || s.Rarity != Rarity.Mythic) return false;
            if (run.Skills.Contains(s.Id)) return CanLevelUp(content, run, s);
            if (run.ReplacedSkills.Contains(s.Id)) return false;
            return s.UpgradesFrom == null || run.Skills.Contains(s.UpgradesFrom);
        }

        /// <summary>Offer weight of a skill: its rarity weight, lower for a level-up of an owned skill.</summary>
        public static double OfferWeight(ContentDb content, ActiveRun run, SkillDef s, double rarityWeight) =>
            run.Skills.Contains(s.Id) ? rarityWeight * content.RunTuning.LevelUpOfferWeight : rarityWeight;

        /// <summary>Mythic choice (RunTuning.MythicChoices skills, owned families favoured). Empty if no mythic is left.</summary>
        public static List<string> GenerateMythic(ContentDb content, ActiveRun run, Rng rng)
        {
            var t = content.RunTuning;
            var pool = new List<SkillDef>();
            foreach (var s in Sorted(content)) if (IsMythicEligible(content, run, s)) pool.Add(s);
            var offer = new List<string>();
            while (offer.Count < t.MythicChoices)
            {
                var cands = new List<SkillDef>(); var w = new List<double>();
                foreach (var s in pool)
                {
                    if (offer.Contains(s.Id)) continue;
                    cands.Add(s);
                    w.Add(OfferWeight(content, run, s, 1 + t.SynergyWeightPerOwned * FamilyCount(content, run, s.Family)));
                }
                var pick = PickWeighted(cands, w, rng);
                if (pick == null) break;
                offer.Add(pick.Id);
            }
            return offer;
        }

        public static List<SkillDef> Eligible(ContentDb content, ActiveRun run)
        {
            var list = new List<SkillDef>();
            foreach (var s in Sorted(content))
                if (IsEligible(content, run, s)) list.Add(s);
            return list;
        }

        private static double RarityWeight(ContentDb content, Rarity r) =>
            content.RunTuning.DraftRarityWeights.TryGetValue(r, out var w) ? w : 1;

        private static SkillDef PickWeighted(List<SkillDef> pool, List<double> weights, Rng rng)
        {
            int idx = rng.PickWeighted(weights);
            return idx < 0 ? null : pool[idx];
        }

        /// <summary>Generate a fresh offer (skill ids). Empty if nothing is eligible.</summary>
        public static List<string> Generate(ContentDb content, ActiveRun run, Rng rng)
        {
            var t = content.RunTuning;
            var pool = Eligible(content, run);
            var offer = new List<string>();

            // Hidden legendary pity.
            if (run.DraftsWithoutLegendary >= t.LegendaryPity)
            {
                var legends = new List<SkillDef>(); var w = new List<double>();
                foreach (var s in pool) if (s.Rarity >= Rarity.Legendary && !run.Skills.Contains(s.Id)) { legends.Add(s); w.Add(RarityWeight(content, s.Rarity)); }
                var pick = PickWeighted(legends, w, rng);
                if (pick != null) offer.Add(pick.Id);
            }

            // Synergy slot: favour families the worm already owns.
            {
                var cands = new List<SkillDef>(); var w = new List<double>();
                foreach (var s in pool)
                {
                    if (offer.Contains(s.Id)) continue;
                    int owned = FamilyCount(content, run, s.Family);
                    if (owned <= 0) continue;
                    cands.Add(s);
                    w.Add(OfferWeight(content, run, s, RarityWeight(content, s.Rarity)) * (1 + t.SynergyWeightPerOwned * owned));
                }
                var pick = PickWeighted(cands, w, rng);
                if (pick != null && offer.Count < t.DraftChoices) offer.Add(pick.Id);
            }

            while (offer.Count < t.DraftChoices)
            {
                var cands = new List<SkillDef>(); var w = new List<double>();
                foreach (var s in pool) if (!offer.Contains(s.Id)) { cands.Add(s); w.Add(OfferWeight(content, run, s, RarityWeight(content, s.Rarity))); }
                var pick = PickWeighted(cands, w, rng);
                if (pick == null) break;
                offer.Add(pick.Id);
            }
            return offer;
        }

        /// <summary>Default auto-mode pick: highest rarity, then synergy with owned families, then upgrades (a new skill before a level-up).</summary>
        public static string BestPick(ContentDb content, ActiveRun run)
        {
            string best = null; double bestScore = double.MinValue;
            foreach (var id in run.DraftOffer)
            {
                double score = PickScore(content, run, content.Skill(id));
                if (score > bestScore) { bestScore = score; best = id; }
            }
            return best;
        }

        /// <summary>How much the default (farming) choice wants a skill: rarity, family synergy, chain upgrades; level-ups a little less.</summary>
        public static double PickScore(ContentDb content, ActiveRun run, SkillDef s)
        {
            if (s == null) return double.MinValue;
            double score = (int)s.Rarity * 10 + FamilyCount(content, run, s.Family) * 4 + (s.UpgradesFrom != null ? 6 : 0);
            if (run.Skills.Contains(s.Id)) score -= 2 + Level(run, s.Id);
            return score;
        }
    }
}
