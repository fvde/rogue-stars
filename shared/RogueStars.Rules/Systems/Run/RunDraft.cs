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
            foreach (var id in run.Skills) { var s = content.Skill(id); if (s != null && s.Family == family && !s.Signature) n++; } // the signature itself does not count
            return n;
        }

        /// <summary>Level of a skill in this run: 0 = not owned, 1 = owned, 2+ = levelled up (cap by tier, RunTuning.MaxSkillLevelOf).</summary>
        public static int Level(ActiveRun run, string skillId)
        {
            if (skillId == null || !run.Skills.Contains(skillId)) return 0;
            return run.SkillLevels.TryGetValue(skillId, out var l) && l > 1 ? l : 1;
        }

        /// <summary>True if the owned skill can be offered again as a level-up: below the max level and not the lower tier of an upgrade chain (its next tier is the upgrade).</summary>
        public static bool CanLevelUp(ContentDb content, ActiveRun run, SkillDef s)
        {
            if (s == null || !run.Skills.Contains(s.Id) || s.Signature) return false; // a signature levels by awakening only
            return Level(run, s.Id) < content.RunTuning.MaxSkillLevelOf(s.Rarity) && !HasUpgrade(content, s.Id) && SkillLeveling.Grows(content, s);
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
            s != null && s.Draftable && !s.Signature
            && (s.Source == SkillSource.Run ? FamilyUnlocked(run, s.Family) : run.Loadout.DraftPoolSkillIds.Contains(s.Id));

        /// <summary>
        /// The run pool's synergy lock (signatures pass): run skills of a synergy are in the pool only when it is a starter synergy or
        /// its Laboratory package is researched (CombatLoadout.UnlockedFamilies; generic skills always; an old run without the list: all).
        /// Laboratory skills are gated by their research instead.
        /// </summary>
        public static bool FamilyUnlocked(ActiveRun run, SkillFamily family) =>
            family == SkillFamily.None || run.Loadout.UnlockedFamilies.Count == 0 || run.Loadout.UnlockedFamilies.Contains(family);

        /// <summary>The synergy of the run's first signature (None without one).</summary>
        public static SkillFamily SignatureFamily(ContentDb content, ActiveRun run)
        {
            var f = SignatureFamilies(content, run);
            return f.Count > 0 ? f[0] : SkillFamily.None;
        }

        /// <summary>The synergies of all the run's signatures, in the order found (several signatures per run).</summary>
        public static List<SkillFamily> SignatureFamilies(ContentDb content, ActiveRun run)
        {
            var list = new List<SkillFamily>();
            foreach (var id in RunSystem.Signatures(run))
            {
                var s = content.Skill(id);
                if (s != null && !list.Contains(s.Family)) list.Add(s.Family);
            }
            return list;
        }

        /// <summary>
        /// True if the skill may currently be offered to this run: a new skill whose chain, prerequisites and family gate are
        /// met, or an owned skill that can still level up (<see cref="CanLevelUp"/>).
        /// </summary>
        public static bool IsEligible(ContentDb content, ActiveRun run, SkillDef s)
        {
            if (!InPool(run, s) || s.Rarity == Rarity.Mythic) return false; // Mythics come only from miniboss choices (GenerateMythic)
            if (run.Skills.Contains(s.Id)) return CanLevelUp(content, run, s);
            if (run.ReplacedSkills.Contains(s.Id)) return false;
            if (!HasSource(content, run, s.Needs)) return false;
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
            if (!HasSource(content, run, s.Needs)) return false;
            return s.UpgradesFrom == null || run.Skills.Contains(s.UpgradesFrom);
        }

        /// <summary>
        /// Offer gate (skill redesign, SkillDef.Needs): true if the run owns a source for the skill. Sources are read from every active
        /// skill (run skills incl. signatures, meta skills: hero, parts, mount, buildings) and the squad's alien skills: "arc" (an Arc
        /// jump), "applies:Status" / "freeze", "consume:Status", "echo", "shield" (a shield effect or a mount's battle-start shield),
        /// "aliens" (a squad or a sidekick summon), "blocks" (a Block effect: a guarding alien or Bodyguard). Null: always.
        /// </summary>
        public static bool HasSource(ContentDb content, ActiveRun run, string needs)
        {
            if (string.IsNullOrEmpty(needs)) return true;
            var lo = run.Loadout;
            if (needs == "aliens" && lo != null && lo.Aliens.Count > 0) return true;
            // hero & mount kits: a mount no longer shields by itself (its kit's shield effects count like any other)
            string kind = needs, arg = null;
            int colon = needs.IndexOf(':');
            if (colon >= 0) { kind = needs.Substring(0, colon); arg = needs.Substring(colon + 1); }
            if (kind == "freeze") { kind = "applies"; arg = "Freeze"; }
            StatusId st = StatusId.None;
            if (arg != null) Enum.TryParse(arg, out st);
            bool Provides(SkillDef d)
            {
                if (d == null) return false;
                foreach (var t in d.Triggers)
                {
                    switch (kind)
                    {
                        case "arc": if (t.Effect == EffectKind.Damage && t.Target == TargetKind.OtherEnemies) return true; break;
                        case "applies": if (t.Effect == EffectKind.ApplyStatus && t.Status == st) return true; break;
                        case "consume": if (t.Effect == EffectKind.ConsumeStatus && t.Status == st) return true; break;
                        case "echo": if (t.Effect == EffectKind.Echo) return true; break;
                        case "shield": if (t.Effect == EffectKind.Shield) return true; break;
                        case "aliens": if (t.Effect == EffectKind.Summon && content.Sidekick(t.Param) != null) return true; break;
                        case "blocks": if (t.Effect == EffectKind.Block || t.Effect == EffectKind.Guard) return true; break;
                    }
                }
                return false;
            }
            foreach (var id in run.Skills) if (Provides(content.Skill(id))) return true;
            if (lo != null)
            {
                foreach (var id in KitMigration.MetaSkillIds(content, lo)) if (Provides(content.Skill(id))) return true;
                foreach (var a in lo.Aliens) foreach (var id in a.SkillIds) if (Provides(content.Skill(id))) return true;
            }
            return false;
        }

        /// <summary>Offer weight of a skill: its rarity weight, lower for a level-up of an owned skill.</summary>
        public static double OfferWeight(ContentDb content, ActiveRun run, SkillDef s, double rarityWeight)
        {
            double w = run.Skills.Contains(s.Id) ? rarityWeight * content.RunTuning.LevelUpOfferWeight : rarityWeight;
            return s.Family != SkillFamily.None && SignatureFamilies(content, run).Contains(s.Family) ? w * content.RunTuning.SignatureFamilyWeight : w; // builds form around every signature
        }

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

            // Synergy slot: favour families the worm already owns. With several signatures the slot cycles between their synergies
            // (draft by draft); a signature synergy is offered there from the first draft.
            {
                var sigFams = SignatureFamilies(content, run);
                SkillFamily cycled = SkillFamily.None;
                if (sigFams.Count > 1)
                {
                    int drafts = 0;
                    foreach (var g in run.Gains) if (g.Source == "draft") drafts++;
                    cycled = sigFams[drafts % sigFams.Count];
                    bool any = false;
                    foreach (var s in pool) if (s.Family == cycled && !offer.Contains(s.Id)) { any = true; break; }
                    if (!any) cycled = SkillFamily.None;
                }
                var cands = new List<SkillDef>(); var w = new List<double>();
                foreach (var s in pool)
                {
                    if (offer.Contains(s.Id)) continue;
                    if (cycled != SkillFamily.None && s.Family != cycled) continue;
                    int owned = FamilyCount(content, run, s.Family);
                    if (owned <= 0 && (s.Family == SkillFamily.None || !sigFams.Contains(s.Family))) continue;
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
            // a found signature (a miniboss offer) is worth a Mythic
            double score = (s.Signature ? (int)Rarity.Mythic : (int)s.Rarity) * 10 + FamilyCount(content, run, s.Family) * 4 + (s.UpgradesFrom != null ? 6 : 0);
            if (run.Skills.Contains(s.Id)) score -= 2 + Level(run, s.Id);
            return score;
        }
    }
}
