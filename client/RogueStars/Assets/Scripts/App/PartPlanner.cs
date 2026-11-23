using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.App
{
    /// <summary>
    /// Client-side part locks (never auto-selected as merge/replicator material; using one needs a confirmation).
    /// Kept per player in PlayerPrefs; the rules have no part lock (equipped, awakened and forged parts are refused by
    /// the rules themselves).
    /// </summary>
    public static class PartLocks
    {
        private static readonly Dictionary<string, HashSet<string>> Cache = new Dictionary<string, HashSet<string>>();
        /// <summary>False in tests: keep locks in memory only.</summary>
        public static bool Persist = true;

        private static string Key(string playerId) => "rs_part_locks_" + (playerId ?? "none");

        public static HashSet<string> For(string playerId)
        {
            string key = Key(playerId);
            if (Cache.TryGetValue(key, out var set)) return set;
            set = new HashSet<string>();
            if (Persist)
            {
                string raw = UnityEngine.PlayerPrefs.GetString(key, "");
                foreach (var uid in raw.Split(',')) if (uid.Length > 0) set.Add(uid);
            }
            Cache[key] = set;
            return set;
        }

        public static bool IsLocked(string playerId, string uid) => uid != null && For(playerId).Contains(uid);

        public static void Set(string playerId, string uid, bool locked)
        {
            var set = For(playerId);
            if (locked) set.Add(uid); else set.Remove(uid);
            if (!Persist) return;
            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            UnityEngine.PlayerPrefs.SetString(Key(playerId), string.Join(",", list));
            UnityEngine.PlayerPrefs.Save();
        }

        public static void ClearAll() => Cache.Clear();
    }

    /// <summary>A merge / replicator material proposal (auto-selected or picked by hand), checked against the rules.</summary>
    public sealed class MergePlan
    {
        /// <summary>True for a Replicator plan (no copies, Legendary+ fodder, Legendary copy as result).</summary>
        public bool Replicate;
        /// <summary>Rarity the target reaches (merge) or -1 (replicate / max).</summary>
        public int NextRarity = -1;
        public PartMergeRecipe Recipe;
        public int CopiesNeeded;
        public PartRarity CopyMinRarity;
        /// <summary>Eligible own-def copies the player has (not equipped / awakened / forged).</summary>
        public int CopiesOwned;
        public long FodderNeeded;
        public PartRarity FodderMinRarity;
        public long Ore;
        public readonly List<string> Copies = new List<string>();
        public readonly List<string> Fodder = new List<string>();
        public long FodderPoints;
        /// <summary>Selected materials that are locked / Mythic+ / reserved copies: merging needs a confirmation.</summary>
        public readonly List<string> Protected = new List<string>();
        /// <summary>Why the plan cannot be executed (null = valid).</summary>
        public string Error;

        public bool Valid => Error == null;
        public bool NeedsConfirmation => Protected.Count > 0;

        public List<string> Materials
        {
            get { var l = new List<string>(Copies); l.AddRange(Fodder); return l; }
        }
    }

    /// <summary>Before/after of a part's one-time awakening.</summary>
    public sealed class AwakenPreview
    {
        public string NameBefore, NameAfter;
        public StatBlock StatsBefore, StatsAfter;
        public List<PartSkillLine> SkillsBefore, SkillsAfter;
        public long Cost, Have;
        /// <summary>Why the part cannot be awakened now (null = it can).</summary>
        public string BlockReason;
    }

    /// <summary>One of a part's five skills with its unlock rarity.</summary>
    public sealed class PartSkillLine
    {
        public string SkillId;
        public string Name;
        public string Description;
        public PartRarity UnlockRarity;
        public bool Unlocked;
    }

    /// <summary>
    /// Merge picker logic for the Parts screen: material candidates, fodder points, best-fodder auto selection (no
    /// superfluous material, protected parts only as a fallback that needs a confirmation), Replicator plans, the
    /// skills of a part by rarity and the awakening preview. Pure functions of the rules state; the rules
    /// (<see cref="PartsSystem.ValidateMerge"/>/<see cref="PartsSystem.ValidateReplicate"/>) have the final word.
    /// </summary>
    public static class PartPlanner
    {
        /// <summary>Parts that can be materials for <paramref name="target"/> at all (rules: unequipped, not awakened or forged).</summary>
        public static List<PartItem> Candidates(GameState s, PartItem target)
        {
            var list = new List<PartItem>();
            foreach (var uid in PartsSystem.SortedPartUids(s))
            {
                var p = s.Parts.Items[uid];
                if (target != null && p.Uid == target.Uid) continue;
                if (PartsSystem.IsMergeMaterial(s, p)) list.Add(p);
            }
            return list;
        }

        /// <summary>
        /// Why a part should not be consumed without asking (null = free fodder): locked by the player, Mythic or higher
        /// (a build target of its own), or a Legendary+ own-def copy of an equipped part (kept for its Mythic S / SS steps).
        /// </summary>
        public static string ProtectReason(GameState s, ContentDb c, PartItem p, ISet<string> locks)
        {
            if (locks != null && locks.Contains(p.Uid)) return "locked";
            if (p.Rarity >= (int)PartRarity.Mythic) return c.PartsConfig.RarityDef(p.Rarity).Name;
            if (p.Rarity >= (int)PartRarity.Legendary)
                foreach (var kv in s.Parts.Equipped)
                {
                    var eq = PartsSystem.Get(s, kv.Value);
                    if (eq != null && eq.Uid != p.Uid && eq.DefId == p.DefId && eq.Rarity + 1 < c.PartsConfig.Rarities.Count) return "copy for equipped " + Fmt.PartName(c, eq);
                }
            return null;
        }

        /// <summary>Fodder points as Legendary-equivalents ("2 LE").</summary>
        public static string Le(ContentDb c, long points)
        {
            double le = points / (double)Math.Max(1, c.PartsConfig.LegendaryFodder);
            return (le >= 10 || Math.Abs(le - Math.Round(le)) < 1e-9 ? Math.Round(le).ToString("0") : le.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) + " LE";
        }

        private static int Compare(GameState s, ContentDb c, PartItem a, PartItem b, string avoidDef)
        {
            long va = PartsSystem.FodderValue(c, a), vb = PartsSystem.FodderValue(c, b);
            if (va != vb) return va.CompareTo(vb);
            int da = a.DefId == avoidDef ? 1 : 0, db = b.DefId == avoidDef ? 1 : 0;
            if (da != db) return da.CompareTo(db);
            if (a.Level != b.Level) return a.Level.CompareTo(b.Level);
            return string.CompareOrdinal(a.Uid, b.Uid);
        }

        /// <summary>
        /// Best fodder from <paramref name="pool"/> (sorted cheapest first) worth at least <paramref name="need"/> points with no
        /// superfluous part: greedy cheapest-first then pruned from the small end, or the single cheapest part that covers the
        /// need, whichever wastes fewer points (ties: fewer parts). Null if the pool is short.
        /// </summary>
        public static List<PartItem> PickFodder(ContentDb c, List<PartItem> pool, long need)
        {
            if (need <= 0) return new List<PartItem>();
            List<PartItem> greedy = null;
            long sum = 0;
            var acc = new List<PartItem>();
            foreach (var p in pool)
            {
                acc.Add(p); sum += PartsSystem.FodderValue(c, p);
                if (sum >= need) break;
            }
            if (sum >= need)
            {
                while (acc.Count > 1 && sum - PartsSystem.FodderValue(c, acc[0]) >= need) { sum -= PartsSystem.FodderValue(c, acc[0]); acc.RemoveAt(0); }
                greedy = acc;
            }
            PartItem single = null;
            foreach (var p in pool) if (PartsSystem.FodderValue(c, p) >= need) { single = p; break; }
            if (greedy == null && single == null) return null;
            if (greedy == null) return new List<PartItem> { single };
            if (single == null) return greedy;
            long gs = 0;
            foreach (var p in greedy) gs += PartsSystem.FodderValue(c, p);
            long ss = PartsSystem.FodderValue(c, single);
            return ss < gs || (ss == gs && greedy.Count > 1) ? new List<PartItem> { single } : greedy;
        }

        /// <summary>Auto-selected materials for merging <paramref name="target"/> to the next rarity.</summary>
        public static MergePlan AutoMerge(GameState s, ContentDb c, PartItem target, ISet<string> locks)
        {
            var plan = NewMergePlan(s, c, target);
            if (plan.Recipe == null) return plan;
            var cands = Candidates(s, target);
            // copies: cheapest eligible own-def parts, unlocked first
            var copies = new List<PartItem>();
            foreach (var p in cands) if (p.DefId == target.DefId && p.Rarity >= (int)plan.CopyMinRarity) copies.Add(p);
            plan.CopiesOwned = copies.Count;
            copies.Sort((a, b) =>
            {
                bool la = locks != null && locks.Contains(a.Uid), lb = locks != null && locks.Contains(b.Uid);
                if (la != lb) return la ? 1 : -1;
                return Compare(s, c, a, b, null);
            });
            var used = new HashSet<string>();
            for (int i = 0; i < plan.CopiesNeeded && i < copies.Count; i++)
            {
                plan.Copies.Add(copies[i].Uid); used.Add(copies[i].Uid);
                if (locks != null && locks.Contains(copies[i].Uid)) plan.Protected.Add(copies[i].Uid);
            }
            if (plan.Copies.Count < plan.CopiesNeeded)
            {
                plan.Error = "needs " + plan.CopiesNeeded + "× own-def copy (" + c.PartsConfig.RarityDef((int)plan.CopyMinRarity).Name + "+), have " + plan.CopiesOwned;
                return plan;
            }
            FillFodder(s, c, plan, cands, used, target.DefId, locks, null);
            Check(s, c, plan, target);
            return plan;
        }

        /// <summary>Auto-selected Replicator materials for a Legendary copy of <paramref name="target"/>'s def.</summary>
        public static MergePlan AutoReplicate(GameState s, ContentDb c, PartItem target, ISet<string> locks)
        {
            var plan = NewReplicatePlan(c, target);
            if (plan.Error != null) return plan;
            FillFodder(s, c, plan, Candidates(s, target), new HashSet<string>(), target.DefId, locks, target.DefId);
            Check(s, c, plan, target);
            return plan;
        }

        /// <summary>A hand-picked material set split like the rules (cheapest own-def copies first), checked by the rules.</summary>
        public static MergePlan Evaluate(GameState s, ContentDb c, PartItem target, IList<string> materials, ISet<string> locks, bool replicate)
        {
            var plan = replicate ? NewReplicatePlan(c, target) : NewMergePlan(s, c, target);
            var mats = new List<PartItem>();
            foreach (var uid in materials) { var p = PartsSystem.Get(s, uid); if (p != null) mats.Add(p); }
            if (!replicate && plan.Recipe != null)
            {
                foreach (var p in Candidates(s, target)) if (p.DefId == target.DefId && p.Rarity >= (int)plan.CopyMinRarity) plan.CopiesOwned++;
                var copies = mats.FindAll(p => p.DefId == target.DefId && p.Rarity >= (int)plan.CopyMinRarity);
                copies.Sort((a, b) => a.Rarity != b.Rarity ? a.Rarity.CompareTo(b.Rarity) : string.CompareOrdinal(a.Uid, b.Uid));
                for (int i = 0; i < plan.CopiesNeeded && i < copies.Count; i++) plan.Copies.Add(copies[i].Uid);
            }
            foreach (var p in mats)
            {
                if (plan.Copies.Contains(p.Uid)) { if (locks != null && locks.Contains(p.Uid)) plan.Protected.Add(p.Uid); continue; }
                plan.Fodder.Add(p.Uid);
                plan.FodderPoints += PartsSystem.FodderValue(c, p);
                if (ProtectReason(s, c, p, locks) != null) plan.Protected.Add(p.Uid);
            }
            if (plan.Error == null) Check(s, c, plan, target);
            return plan;
        }

        private static MergePlan NewMergePlan(GameState s, ContentDb c, PartItem target)
        {
            var cfg = c.PartsConfig;
            var plan = new MergePlan();
            if (target.Rarity + 1 >= cfg.Rarities.Count) { plan.Error = "max rarity"; return plan; }
            plan.NextRarity = target.Rarity + 1;
            plan.Recipe = cfg.Rarities[plan.NextRarity].Merge;
            if (plan.Recipe == null) { plan.Error = "no recipe"; return plan; }
            plan.CopiesNeeded = plan.Recipe.SameDefCount;
            plan.CopyMinRarity = plan.Recipe.SameDefRarity;
            plan.FodderNeeded = plan.Recipe.FodderPoints;
            plan.FodderMinRarity = plan.Recipe.FodderMinRarity;
            plan.Ore = plan.Recipe.Ore;
            return plan;
        }

        private static MergePlan NewReplicatePlan(ContentDb c, PartItem target)
        {
            var cfg = c.PartsConfig;
            var plan = new MergePlan
            {
                Replicate = true, FodderNeeded = cfg.ReplicateFodderPoints, FodderMinRarity = PartRarity.Legendary, Ore = cfg.ReplicateOre,
            };
            if (target.Rarity < (int)cfg.ReplicateMinTargetRarity) plan.Error = "Replicator copies " + cfg.RarityDef((int)cfg.ReplicateMinTargetRarity).Name + "+ parts only";
            return plan;
        }

        private static void FillFodder(GameState s, ContentDb c, MergePlan plan, List<PartItem> cands, HashSet<string> used, string targetDef, ISet<string> locks, string excludeDef)
        {
            var free = new List<PartItem>();
            var all = new List<PartItem>();
            foreach (var p in cands)
            {
                if (used.Contains(p.Uid) || p.Rarity < (int)plan.FodderMinRarity || p.DefId == excludeDef) continue;
                all.Add(p);
                if (ProtectReason(s, c, p, locks) == null) free.Add(p);
            }
            Comparison<PartItem> cmp = (a, b) => Compare(s, c, a, b, targetDef);
            free.Sort(cmp); all.Sort(cmp);
            var pick = PickFodder(c, free, plan.FodderNeeded);
            if (pick == null)
            {
                pick = PickFodder(c, all, plan.FodderNeeded);
                if (pick != null)
                    foreach (var p in pick) if (ProtectReason(s, c, p, locks) != null) plan.Protected.Add(p.Uid);
            }
            if (pick == null)
            {
                long have = 0;
                foreach (var p in all) have += PartsSystem.FodderValue(c, p);
                plan.Error = "not enough fodder: " + have + "/" + plan.FodderNeeded + " pts (" + c.PartsConfig.RarityDef((int)plan.FodderMinRarity).Name + "+)";
                return;
            }
            foreach (var p in pick) { plan.Fodder.Add(p.Uid); plan.FodderPoints += PartsSystem.FodderValue(c, p); }
        }

        private static void Check(GameState s, ContentDb c, MergePlan plan, PartItem target)
        {
            if (plan.Error != null) return;
            try
            {
                if (plan.Replicate) PartsSystem.ValidateReplicate(s, c, target.Uid, plan.Materials);
                else PartsSystem.ValidateMerge(s, c, target.Uid, plan.Materials);
            }
            catch (RuleException ex) { plan.Error = ex.Message; }
        }

        // ---- skills / awakening --------------------------------------------------------------------

        /// <summary>The part's five skills (normal or awakened set) with unlock rarity and state at the part's rarity.</summary>
        public static List<PartSkillLine> SkillLines(ContentDb c, PartItem p, bool awakened)
        {
            var cfg = c.PartsConfig;
            var def = PartsSystem.Def(c, p);
            var ids = awakened ? def.AwakenedSkillIds : def.SkillIds;
            var list = new List<PartSkillLine>();
            for (int i = 0; i < ids.Count && i < cfg.SkillUnlockRarities.Count; i++)
            {
                var sk = c.Skill(ids[i]);
                list.Add(new PartSkillLine
                {
                    SkillId = ids[i], Name = sk?.Name ?? ids[i], Description = sk?.Description ?? "",
                    UnlockRarity = cfg.SkillUnlockRarities[i], Unlocked = p.Rarity >= (int)cfg.SkillUnlockRarities[i],
                });
            }
            return list;
        }

        public static PartItem CloneItem(PartItem p) => new PartItem
        {
            Uid = p.Uid, DefId = p.DefId, Rarity = p.Rarity, Level = p.Level, CapRaises = p.CapRaises, ForgedSlots = p.ForgedSlots,
            Awaken = p.Awaken, Runes = new List<string>(p.Runes), SpentOre = p.SpentOre, SpentGearCores = p.SpentGearCores, SpentPlates = p.SpentPlates,
        };

        /// <summary>Name, stats and skills before and after awakening (the rules' PartStats/DisplayName on an awakened copy).</summary>
        public static AwakenPreview Awaken(GameState s, ContentDb c, PartItem p)
        {
            var cfg = c.PartsConfig;
            var after = CloneItem(p);
            after.Awaken = 1;
            var before = CloneItem(p);
            before.Awaken = 0;
            var pv = new AwakenPreview
            {
                NameBefore = PartsSystem.DisplayName(c, before), NameAfter = PartsSystem.DisplayName(c, after),
                StatsBefore = PartsSystem.PartStats(s, c, before), StatsAfter = PartsSystem.PartStats(s, c, after),
                SkillsBefore = SkillLines(c, p, false), SkillsAfter = SkillLines(c, p, true),
                Cost = cfg.AwakenCost, Have = s.Wallet.Get(ProgressionCurrencies.PartAwakenCores),
            };
            if (p.Awaken > 0) pv.BlockReason = "already awakened";
            else if (p.Rarity < (int)cfg.AwakenMinRarity) pv.BlockReason = "needs " + cfg.RarityDef((int)cfg.AwakenMinRarity).Name + " or higher";
            else if (pv.Have < pv.Cost) pv.BlockReason = "needs " + pv.Cost + " Awaken Cores (have " + pv.Have + ")";
            return pv;
        }
    }
}
