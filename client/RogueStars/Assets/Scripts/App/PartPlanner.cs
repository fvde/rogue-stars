using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.App
{
    /// <summary>
    /// Client-side part locks (never auto-selected as ladder / Replicator material or fed; using one needs a confirmation). Kept per
    /// player in PlayerPrefs; parts v2 also has the rules' lock (PartLock), which the Parts screen sets together with this one.
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

    /// <summary>
    /// A Mythic ladder step (or Replicator) proposal (auto-selected or picked by hand), checked against the rules (parts v2): copies
    /// of the same Mythic and other Mythics of the slot, Metal and Ore; the Replicator takes 3 spare Mythics and Ore.
    /// </summary>
    public sealed class MergePlan
    {
        /// <summary>True for a Replicator plan (3 spare Mythics of any slot → a copy of the target's Mythic).</summary>
        public bool Replicate;
        /// <summary>Rarity the target reaches (ladder step) or -1 (Replicator / top of the ladder).</summary>
        public int NextRarity = -1;
        public int CopiesNeeded;
        /// <summary>Base-step copies of the target's Mythic the player has (not equipped / awakened / locked / climbed).</summary>
        public int CopiesOwned;
        /// <summary>Other Mythics of the slot needed (Replicator: spare Mythics needed).</summary>
        public long FodderNeeded;
        /// <summary>Copies and other Mythics are always base-step Mythics.</summary>
        public PartRarity CopyMinRarity = PartRarity.Mythic, FodderMinRarity = PartRarity.Mythic;
        public long Ore, Metal;
        public readonly List<string> Copies = new List<string>();
        /// <summary>The other Mythics (Replicator: the spare Mythics).</summary>
        public readonly List<string> Fodder = new List<string>();
        /// <summary>How many other Mythics are selected.</summary>
        public long FodderPoints;
        /// <summary>Selected materials that are locked or copies of an equipped Mythic: climbing needs a confirmation.</summary>
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

    /// <summary>One of a part's skills with the rarity / ladder step it is active from.</summary>
    public sealed class PartSkillLine
    {
        public string SkillId;
        public string Name;
        public string Description;
        public PartRarity UnlockRarity;
        public bool Unlocked;
    }

    /// <summary>
    /// Ladder picker logic for the Parts screen (parts v2): material candidates, auto selection (protected Mythics only as a fallback
    /// that needs a confirmation), Replicator plans, a part's skills by rarity and the awakening preview. Pure functions of the rules
    /// state; the rules (<see cref="PartsSystem.ValidateClimb"/> / <see cref="PartsSystem.ValidateReplicate"/>) have the final word.
    /// </summary>
    public static class PartPlanner
    {
        /// <summary>Mythics that can be ladder or Replicator material (rules: base step, not awakened, equipped, locked or forged).</summary>
        public static List<PartItem> Candidates(GameState s, ContentDb c, PartItem target)
        {
            var list = new List<PartItem>();
            foreach (var uid in PartsSystem.SortedPartUids(s))
            {
                var p = s.Parts.Items[uid];
                if (target != null && p.Uid == target.Uid) continue;
                if (PartsSystem.IsMythicMaterial(s, c, p)) list.Add(p);
            }
            return list;
        }

        /// <summary>
        /// Why a Mythic should not be consumed without asking (null = free): locked by the player, or a copy of an equipped Mythic
        /// used as an other Mythic / Replicator input (it is worth more as that Mythic's own copy).
        /// </summary>
        public static string ProtectReason(GameState s, ContentDb c, PartItem p, ISet<string> locks, PartItem target = null)
        {
            if (locks != null && locks.Contains(p.Uid)) return "locked";
            if (target != null && p.DefId == target.DefId) return null; // a copy for this very step
            foreach (var kv in s.Parts.Equipped)
            {
                var eq = PartsSystem.Get(s, kv.Value);
                if (eq != null && eq.Uid != p.Uid && eq.DefId == p.DefId && PartsSystem.NextClimb(c, eq) != null) return "copy for equipped " + Fmt.PartName(c, eq);
            }
            return null;
        }

        private static int Compare(PartItem a, PartItem b) =>
            a.Level != b.Level ? a.Level.CompareTo(b.Level) : string.CompareOrdinal(a.Uid, b.Uid);

        /// <summary>Auto-selected materials for the target's next ladder step.</summary>
        public static MergePlan AutoMerge(GameState s, ContentDb c, PartItem target, ISet<string> locks)
        {
            var plan = NewClimbPlan(s, c, target);
            if (plan.Error != null) return plan;
            var cands = Candidates(s, c, target);
            var copies = cands.FindAll(p => p.DefId == target.DefId);
            plan.CopiesOwned = copies.Count;
            copies.Sort((a, b) =>
            {
                bool la = locks != null && locks.Contains(a.Uid), lb = locks != null && locks.Contains(b.Uid);
                return la != lb ? (la ? 1 : -1) : Compare(a, b);
            });
            for (int i = 0; i < plan.CopiesNeeded && i < copies.Count; i++)
            {
                plan.Copies.Add(copies[i].Uid);
                if (locks != null && locks.Contains(copies[i].Uid)) plan.Protected.Add(copies[i].Uid);
            }
            if (plan.Copies.Count < plan.CopiesNeeded)
            {
                plan.Error = "needs " + plan.CopiesNeeded + " cop" + (plan.CopiesNeeded == 1 ? "y" : "ies") + " of this Mythic, have " + plan.CopiesOwned;
                return plan;
            }
            var kind = PartsSystem.Def(c, target).Kind;
            FillOthers(s, c, plan, cands.FindAll(p => p.DefId != target.DefId && PartsSystem.Def(c, p).Kind == kind), locks, target, (int)plan.FodderNeeded);
            Check(s, c, plan, target);
            return plan;
        }

        /// <summary>Auto-selected Replicator materials (3 spare Mythics of any slot) for a copy of <paramref name="target"/>'s Mythic.</summary>
        public static MergePlan AutoReplicate(GameState s, ContentDb c, PartItem target, ISet<string> locks)
        {
            var plan = NewReplicatePlan(c, target);
            if (plan.Error != null) return plan;
            FillOthers(s, c, plan, Candidates(s, c, target).FindAll(p => p.DefId != target.DefId), locks, null, (int)plan.FodderNeeded);
            Check(s, c, plan, target);
            return plan;
        }

        /// <summary>A hand-picked material set split like the rules (copies = the same Mythic), checked by the rules.</summary>
        public static MergePlan Evaluate(GameState s, ContentDb c, PartItem target, IList<string> materials, ISet<string> locks, bool replicate)
        {
            var plan = replicate ? NewReplicatePlan(c, target) : NewClimbPlan(s, c, target);
            foreach (var p in Candidates(s, c, target)) if (!replicate && p.DefId == target.DefId) plan.CopiesOwned++;
            foreach (var uid in materials)
            {
                var p = PartsSystem.Get(s, uid);
                if (p == null) continue;
                if (!replicate && p.DefId == target.DefId) plan.Copies.Add(p.Uid);
                else { plan.Fodder.Add(p.Uid); plan.FodderPoints++; }
                if (ProtectReason(s, c, p, locks, replicate ? null : target) != null) plan.Protected.Add(p.Uid);
            }
            if (plan.Error == null) Check(s, c, plan, target);
            return plan;
        }

        private static MergePlan NewClimbPlan(GameState s, ContentDb c, PartItem target)
        {
            var plan = new MergePlan();
            if (!PartsSystem.Def(c, target).IsMythic) { plan.Error = "only Mythics climb"; return plan; }
            var step = PartsSystem.NextClimb(c, target);
            if (step == null) { plan.Error = "top of the ladder"; return plan; }
            plan.NextRarity = target.Rarity + 1;
            plan.CopiesNeeded = step.Copies;
            plan.FodderNeeded = step.Others;
            plan.Ore = step.Ore;
            plan.Metal = step.Metal;
            return plan;
        }

        private static MergePlan NewReplicatePlan(ContentDb c, PartItem target)
        {
            var cfg = c.PartsConfig;
            var plan = new MergePlan { Replicate = true, FodderNeeded = cfg.ReplicateMythics, Ore = cfg.ReplicateOre };
            if (!PartsSystem.Def(c, target).IsMythic) plan.Error = "the Replicator copies Mythics only";
            return plan;
        }

        private static void FillOthers(GameState s, ContentDb c, MergePlan plan, List<PartItem> pool, ISet<string> locks, PartItem target, int need)
        {
            var free = pool.FindAll(p => ProtectReason(s, c, p, locks, target) == null);
            var rest = pool.FindAll(p => ProtectReason(s, c, p, locks, target) != null);
            free.Sort(Compare); rest.Sort(Compare);
            foreach (var p in free) { if (plan.Fodder.Count >= need) break; plan.Fodder.Add(p.Uid); plan.FodderPoints++; }
            foreach (var p in rest) { if (plan.Fodder.Count >= need) break; plan.Fodder.Add(p.Uid); plan.FodderPoints++; plan.Protected.Add(p.Uid); }
            if (plan.Fodder.Count < need)
                plan.Error = (plan.Replicate ? "needs " + need + " spare Mythics" : "needs " + need + " other Mythics of this slot") + ", have " + pool.Count;
        }

        private static void Check(GameState s, ContentDb c, MergePlan plan, PartItem target)
        {
            if (plan.Error != null) return;
            try
            {
                if (plan.Replicate) PartsSystem.ValidateReplicate(s, c, target.Uid, plan.Materials);
                else PartsSystem.ValidateClimb(s, c, target.Uid, plan.Materials);
            }
            catch (RuleException ex) { plan.Error = ex.Message; }
        }

        // ---- skills / awakening --------------------------------------------------------------------

        /// <summary>Rarity a part's skill i is active from: Epic 1, Legendary 2, Mythic 1–3, Mythic S the 4th, Mythic SS the 5th.</summary>
        public static PartRarity SkillUnlock(PartDef def, int i)
        {
            if (!def.IsMythic) return def.Rarity;
            return i < 3 ? PartRarity.Mythic : i == 3 ? PartRarity.MythicS : PartRarity.MythicSS;
        }

        /// <summary>The part's skills (normal or awakened set) with the rarity / step they are active from and their state now.</summary>
        public static List<PartSkillLine> SkillLines(ContentDb c, PartItem p, bool awakened)
        {
            var def = PartsSystem.Def(c, p);
            var ids = awakened && def.AwakenedSkillIds.Count > 0 ? def.AwakenedSkillIds : def.SkillIds;
            int active = PartsSystem.SkillCount(c, p.Rarity);
            var list = new List<PartSkillLine>();
            for (int i = 0; i < ids.Count; i++)
            {
                var sk = c.Skill(ids[i]);
                list.Add(new PartSkillLine
                {
                    SkillId = ids[i], Name = sk?.Name ?? ids[i], Description = sk?.Description ?? "",
                    UnlockRarity = SkillUnlock(def, i), Unlocked = i < active,
                });
            }
            return list;
        }

        public static PartItem CloneItem(PartItem p) => new PartItem
        {
            Uid = p.Uid, DefId = p.DefId, Rarity = p.Rarity, Level = p.Level, ForgedSlots = p.ForgedSlots, Locked = p.Locked,
            Awaken = p.Awaken, Runes = new List<string>(p.Runes), SpentOre = p.SpentOre, SpentGearCores = p.SpentGearCores,
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
            else if (!PartsSystem.Def(c, p).IsMythic || p.Rarity < (int)cfg.AwakenMinRarity) pv.BlockReason = "needs a Mythic part";
            else if (pv.Have < pv.Cost) pv.BlockReason = "needs " + pv.Cost + " Awaken Cores (have " + pv.Have + ")";
            return pv;
        }

        /// <summary>Spare standard parts the Feed button turns into Metal (not equipped, locked here or in the rules, forged or Mythic).</summary>
        public static List<PartItem> Feedable(GameState s, ContentDb c, ISet<string> locks)
        {
            var list = new List<PartItem>();
            foreach (var uid in PartsSystem.SortedPartUids(s))
            {
                var p = s.Parts.Items[uid];
                if (PartsSystem.IsFeedable(s, c, p) && (locks == null || !locks.Contains(uid))) list.Add(p);
            }
            return list;
        }
    }
}
