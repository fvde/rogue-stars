using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Parts (worm equipment): six slots, 15-step merge ladder, level/cap, runes, Forge rune slots,
    /// S/SS awakening, dismantle. Granters for RewardKinds.Part (Rarity = PartRarity index) and
    /// RewardKinds.Rune (Rarity = Core.Rarity index).
    /// </summary>
    public static class PartsSystem
    {
        private static readonly PartSlot[] AllSlots = { PartSlot.Head, PartSlot.Body, PartSlot.Tail, PartSlot.Legs, PartSlot.LeftArm, PartSlot.RightArm };

        public static void Register(GameEngine e)
        {
            e.Register<PartEquip>(HandleEquip);
            e.Register<PartUnequip>(HandleUnequip);
            e.Register<PartLevelUp>(HandleLevelUp);
            e.Register<PartLevelDown>(HandleLevelDown);
            e.Register<PartRaiseCap>(HandleRaiseCap);
            e.Register<PartMerge>(HandleMerge);
            e.Register<PartQuickMerge>(HandleQuickMerge);
            e.Register<PartDismantle>(HandleDismantle);
            e.Register<RuneSocket>(HandleRuneSocket);
            e.Register<RuneUnsocket>(HandleRuneUnsocket);
            e.Register<RuneMerge>(HandleRuneMerge);
            e.Register<RuneLock>(HandleRuneLock);
            e.Register<PartForgeUnlock>(HandleForge);
            e.Register<PartAwaken>(HandleAwaken);
            e.Rewards.Register(RewardKinds.Part, GrantPart);
            e.Rewards.Register(RewardKinds.Rune, GrantRune);
            e.RegisterLoadoutContributor(Contribute);
        }

        // ---- inventory helpers (public: used by tests, debug tools and other grant paths) ---------

        /// <summary>Add a part instance directly (granters use this). Returns the new item.</summary>
        public static PartItem AddPart(GameState s, ContentDb c, string defId, int rarity)
        {
            if (!c.PartsConfig.Defs.ContainsKey(defId)) throw new InvalidOperationException("unknown part " + defId);
            var item = new PartItem { Uid = "p" + (s.Parts.NextUid++).ToString("D8"), DefId = defId, Rarity = Math.Max(0, Math.Min(c.PartsConfig.Rarities.Count - 1, rarity)) };
            s.Parts.Items[item.Uid] = item;
            s.Count("parts_obtained");
            ProgressionUtil.CountMax(s, "part_rarity_max", item.Rarity);
            return item;
        }

        /// <summary>Add a rune instance directly (granters use this). Returns the new item.</summary>
        public static RuneItem AddRune(GameState s, ContentDb c, string defId, int rarity)
        {
            if (!c.PartsConfig.Runes.ContainsKey(defId)) throw new InvalidOperationException("unknown rune " + defId);
            var item = new RuneItem { Uid = "r" + (s.Parts.NextUid++).ToString("D8"), DefId = defId, Rarity = Math.Max(0, Math.Min((int)c.PartsConfig.RuneMaxRarity, rarity)) };
            s.Parts.Runes[item.Uid] = item;
            s.Count("runes_obtained");
            return item;
        }

        // ---- queries -------------------------------------------------------------------------------

        public static PartItem Get(GameState s, string uid) => uid != null && s.Parts.Items.TryGetValue(uid, out var p) ? p : null;

        public static PartDef Def(ContentDb c, PartItem p) => c.PartsConfig.Defs[p.DefId];

        public static int LevelCap(ContentDb c, PartItem p)
        {
            var cfg = c.PartsConfig;
            return Math.Min(cfg.MaxLevel, cfg.RarityDef(p.Rarity).BaseLevelCap + p.CapRaises * cfg.CapRaiseLevels);
        }

        public static int RuneSlotCount(ContentDb c, PartItem p) =>
            Math.Min(c.PartsConfig.MaxRuneSlots, c.PartsConfig.RarityDef(p.Rarity).RuneSlots + p.ForgedSlots);

        /// <summary>Total Forge bonus percent on base stats.</summary>
        public static double ForgeBonusPercent(ContentDb c, PartItem p)
        {
            double v = 0;
            for (int i = 0; i < p.ForgedSlots && i < c.PartsConfig.Forge.Count; i++) v += c.PartsConfig.Forge[i].BonusPercent;
            return v;
        }

        public static bool IsEquipped(GameState s, string uid)
        {
            foreach (var kv in s.Parts.Equipped) if (kv.Value == uid) return true;
            return false;
        }

        /// <summary>
        /// Stats of a part: (base × rarity mult × (1 + forge%) + per-level × (level − 1) × rarity mult) × (1 + awaken%),
        /// plus socketed runes.
        /// </summary>
        public static StatBlock PartStats(GameState s, ContentDb c, PartItem p)
        {
            var cfg = c.PartsConfig; var def = Def(c, p);
            double mult = cfg.RarityDef(p.Rarity).StatMultiplier;
            double awaken = 1 + (p.Awaken < cfg.AwakenStatPercent.Count ? cfg.AwakenStatPercent[p.Awaken] : 0) / 100.0;
            var b = ProgressionUtil.Scaled(def.BaseStats, mult * (1 + ForgeBonusPercent(c, p) / 100.0) * awaken);
            b.AddBlock(ProgressionUtil.Scaled(def.StatsPerLevel, (p.Level - 1) * mult * awaken));
            foreach (var ru in p.Runes)
            {
                if (string.IsNullOrEmpty(ru) || !s.Parts.Runes.TryGetValue(ru, out var rune)) continue;
                b.Add(RuneStat(c, rune));
            }
            return b;
        }

        public static StatMod RuneStat(ContentDb c, RuneItem r)
        {
            var d = c.PartsConfig.Runes[r.DefId];
            double v = d.BaseValue * c.PartsConfig.RuneRarityMultipliers[Math.Min(r.Rarity, c.PartsConfig.RuneRarityMultipliers.Count - 1)];
            return d.IsPercent ? new StatMod(d.Stat, 0, v) : new StatMod(d.Stat, v);
        }

        /// <summary>Skills a part grants while equipped: signature (Mythic / Mythic+3) and S/SS awakening skill.</summary>
        public static List<string> PartSkills(ContentDb c, PartItem p)
        {
            var cfg = c.PartsConfig; var def = Def(c, p); var list = new List<string>();
            if (p.Rarity >= (int)cfg.Signature2Rarity && def.Signature2SkillId != null) list.Add(def.Signature2SkillId);
            else if (p.Rarity >= (int)cfg.SignatureRarity && def.SignatureSkillId != null) list.Add(def.SignatureSkillId);
            if (p.Awaken >= 2 && def.AwakenSSSkillId != null) list.Add(def.AwakenSSSkillId);
            else if (p.Awaken == 1 && def.AwakenSSkillId != null) list.Add(def.AwakenSSkillId);
            return list;
        }

        /// <summary>Uids sorted ordinally (deterministic iteration).</summary>
        public static List<string> SortedPartUids(GameState s)
        {
            var keys = new List<string>(s.Parts.Items.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        private static void Contribute(GameState s, ContentDb c, long now, CombatLoadout l)
        {
            foreach (var slot in AllSlots)
            {
                if (!s.Parts.Equipped.TryGetValue(slot, out var uid)) continue;
                var p = Get(s, uid);
                if (p == null) continue;
                l.WormStats.AddBlock(PartStats(s, c, p));
                var def = Def(c, p);
                if (def.Kind == PartKind.Arm) l.ArmStyles.Add(def.Style);
                foreach (var sk in PartSkills(c, p)) if (!l.MetaSkillIds.Contains(sk)) l.MetaSkillIds.Add(sk);
            }
        }

        // ---- granters ------------------------------------------------------------------------------

        private static void GrantPart(CommandContext ctx, Reward r)
        {
            for (long i = 0; i < r.Amount; i++) AddPart(ctx.State, ctx.Content, r.Id, r.Rarity);
        }

        private static void GrantRune(CommandContext ctx, Reward r)
        {
            for (long i = 0; i < r.Amount; i++) AddRune(ctx.State, ctx.Content, r.Id, r.Rarity);
        }

        // ---- shared mutation helpers ---------------------------------------------------------------

        private static PartItem RequirePart(GameState s, string uid)
        {
            var p = Get(s, uid);
            GameEngine.Require(p != null, "unknown part");
            return p;
        }

        private static void EnsureRuneSlots(ContentDb c, PartItem p)
        {
            int n = RuneSlotCount(c, p);
            while (p.Runes.Count < n) p.Runes.Add("");
        }

        /// <summary>Refund 100% of level and cap materials and return socketed runes to the inventory.</summary>
        private static void RefundAndStrip(GameState s, PartItem p)
        {
            s.Wallet.Add(Currencies.Ore, p.SpentOre);
            s.Wallet.Add(Currencies.GearCores, p.SpentGearCores);
            s.Wallet.Add(Currencies.AdvancementPlates, p.SpentPlates);
            p.SpentOre = p.SpentGearCores = p.SpentPlates = 0;
            for (int i = 0; i < p.Runes.Count; i++)
            {
                if (!string.IsNullOrEmpty(p.Runes[i]) && s.Parts.Runes.TryGetValue(p.Runes[i], out var r)) r.SocketedIn = "";
                p.Runes[i] = "";
            }
        }

        // ---- handlers ------------------------------------------------------------------------------

        private static CommandResult HandleEquip(PartEquip cmd, CommandContext ctx)
        {
            var s = ctx.State; var p = RequirePart(s, cmd.Uid); var def = Def(ctx.Content, p);
            bool armSlot = cmd.Slot == PartSlot.LeftArm || cmd.Slot == PartSlot.RightArm;
            if (def.Kind == PartKind.Arm) GameEngine.Require(armSlot, "arm parts go into an arm slot");
            else GameEngine.Require(!armSlot && (int)def.Kind == (int)cmd.Slot, "part does not fit this slot");
            // An arm part moved from the other arm leaves that arm empty.
            foreach (var slot in AllSlots)
                if (slot != cmd.Slot && s.Parts.Equipped.TryGetValue(slot, out var u) && u == p.Uid) s.Parts.Equipped.Remove(slot);
            s.Parts.Equipped[cmd.Slot] = p.Uid;
            ctx.Emit("part_equipped", cmd.Slot.ToString(), p.Uid);
            return CommandResult.Success();
        }

        private static CommandResult HandleUnequip(PartUnequip cmd, CommandContext ctx)
        {
            GameEngine.Require(ctx.State.Parts.Equipped.Remove(cmd.Slot), "slot is empty");
            return CommandResult.Success();
        }

        private static CommandResult HandleLevelUp(PartLevelUp cmd, CommandContext ctx)
        {
            var s = ctx.State; var c = ctx.Content; var cfg = c.PartsConfig; var p = RequirePart(s, cmd.Uid);
            GameEngine.Require(cmd.Levels >= 1, "levels must be >= 1");
            GameEngine.Require(p.Level + cmd.Levels <= LevelCap(c, p), "level cap reached");
            long ore = 0, cores = 0;
            for (int l = p.Level; l < p.Level + cmd.Levels; l++) { ore += cfg.LevelOreCosts[l]; cores += cfg.LevelGearCoreCosts[l]; }
            ProgressionUtil.Spend(s, new[] { new Cost(Currencies.Ore, ore), new Cost(Currencies.GearCores, cores) });
            p.Level += cmd.Levels; p.SpentOre += ore; p.SpentGearCores += cores;
            s.Count("part_levels", cmd.Levels);
            return CommandResult.Success(p.Level);
        }

        private static CommandResult HandleLevelDown(PartLevelDown cmd, CommandContext ctx)
        {
            var s = ctx.State; var p = RequirePart(s, cmd.Uid);
            GameEngine.Require(p.Level > 1, "part is already level 1");
            s.Wallet.Add(Currencies.Ore, p.SpentOre);
            s.Wallet.Add(Currencies.GearCores, p.SpentGearCores);
            p.SpentOre = p.SpentGearCores = 0;
            p.Level = 1;
            return CommandResult.Success();
        }

        private static CommandResult HandleRaiseCap(PartRaiseCap cmd, CommandContext ctx)
        {
            var s = ctx.State; var cfg = ctx.Content.PartsConfig; var p = RequirePart(s, cmd.Uid);
            GameEngine.Require(p.CapRaises < cfg.RarityDef(p.Rarity).MaxCapRaises && p.CapRaises < cfg.CapRaisePlates.Count, "no more cap raises at this rarity");
            long plates = cfg.CapRaisePlates[p.CapRaises];
            ProgressionUtil.Spend(s, Currencies.AdvancementPlates, plates);
            p.CapRaises++; p.SpentPlates += plates;
            s.Count("part_cap_raises");
            return CommandResult.Success(LevelCap(ctx.Content, p));
        }

        /// <summary>Validate a merge; throws RuleException on any problem. Public for UI previews.</summary>
        public static void ValidateMerge(GameState s, ContentDb c, string targetUid, IList<string> materialUids)
        {
            var cfg = c.PartsConfig; var target = RequirePart(s, targetUid); var def = Def(c, target);
            GameEngine.Require(target.Rarity + 1 < cfg.Rarities.Count, "part is at max rarity");
            var recipe = cfg.Rarities[target.Rarity + 1].Merge;
            GameEngine.Require(recipe != null, "no merge recipe");
            GameEngine.Require(materialUids != null && materialUids.Count == recipe.SameDefCount + recipe.FodderCount, "wrong number of materials");
            var seen = new HashSet<string>();
            int onlySame = 0, onlyFodder = 0;
            foreach (var uid in materialUids)
            {
                GameEngine.Require(uid != targetUid && seen.Add(uid), "duplicate material");
                var m = RequirePart(s, uid);
                GameEngine.Require(!IsEquipped(s, uid), "equipped parts cannot be merge materials");
                GameEngine.Require(m.Awaken == 0 && m.ForgedSlots == 0, "awakened or forged parts cannot be merge materials");
                var md = Def(c, m);
                bool same = m.DefId == target.DefId && m.Rarity == (int)recipe.SameDefRarity && recipe.SameDefCount > 0;
                bool fodder = md.Kind == def.Kind && m.Rarity == (int)recipe.FodderRarity && recipe.FodderCount > 0;
                GameEngine.Require(same || fodder, "material does not match the recipe");
                if (same && !fodder) onlySame++;
                if (fodder && !same) onlyFodder++;
            }
            GameEngine.Require(onlySame <= recipe.SameDefCount && onlyFodder <= recipe.FodderCount, "material does not match the recipe");
        }

        private static void DoMerge(CommandContext ctx, PartItem target, IList<string> materialUids)
        {
            var s = ctx.State;
            foreach (var uid in materialUids)
            {
                var m = s.Parts.Items[uid];
                RefundAndStrip(s, m);
                s.Parts.Items.Remove(uid);
            }
            target.Rarity++;
            EnsureRuneSlots(ctx.Content, target);
            s.Count("part_merges");
            ProgressionUtil.CountMax(s, "part_rarity_max", target.Rarity);
            ctx.Emit("part_merged", target.Uid, target.Rarity.ToString());
        }

        private static CommandResult HandleMerge(PartMerge cmd, CommandContext ctx)
        {
            ValidateMerge(ctx.State, ctx.Content, cmd.TargetUid, cmd.MaterialUids);
            var target = ctx.State.Parts.Items[cmd.TargetUid];
            DoMerge(ctx, target, cmd.MaterialUids);
            return CommandResult.Success(target.Rarity);
        }

        private static CommandResult HandleQuickMerge(PartQuickMerge cmd, CommandContext ctx)
        {
            var s = ctx.State; var c = ctx.Content; var cfg = c.PartsConfig;
            int merges = 0; bool changed = true;
            while (changed)
            {
                changed = false;
                for (int t = 1; t <= (int)cfg.QuickMergeMaxTarget && t < cfg.Rarities.Count; t++)
                {
                    var recipe = cfg.Rarities[t].Merge;
                    if (recipe == null) continue;
                    // Targets: equipped first, then highest level, then uid.
                    var targets = new List<PartItem>();
                    foreach (var uid in SortedPartUids(s)) { var p = s.Parts.Items[uid]; if (p.Rarity == t - 1 && p.Awaken == 0) targets.Add(p); }
                    targets.Sort((a, b) =>
                    {
                        int ea = IsEquipped(s, a.Uid) ? 0 : 1, eb = IsEquipped(s, b.Uid) ? 0 : 1;
                        if (ea != eb) return ea.CompareTo(eb);
                        if (a.Level != b.Level) return b.Level.CompareTo(a.Level);
                        return string.CompareOrdinal(a.Uid, b.Uid);
                    });
                    foreach (var target in targets)
                    {
                        if (!s.Parts.Items.ContainsKey(target.Uid) || target.Rarity != t - 1) continue;
                        var mats = PickMaterials(s, c, target, recipe);
                        if (mats == null) continue;
                        DoMerge(ctx, target, mats);
                        merges++; changed = true;
                    }
                }
            }
            return CommandResult.Success(merges);
        }

        private static List<string> PickMaterials(GameState s, ContentDb c, PartItem target, PartMergeRecipe recipe)
        {
            var kind = Def(c, target).Kind;
            var pool = new List<PartItem>();
            foreach (var uid in SortedPartUids(s))
            {
                var p = s.Parts.Items[uid];
                if (p.Uid == target.Uid || p.Awaken != 0 || p.ForgedSlots != 0 || IsEquipped(s, uid)) continue;
                pool.Add(p);
            }
            pool.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : string.CompareOrdinal(a.Uid, b.Uid));
            var picked = new List<string>();
            int same = 0, fodder = 0;
            foreach (var p in pool)
                if (same < recipe.SameDefCount && p.DefId == target.DefId && p.Rarity == (int)recipe.SameDefRarity) { picked.Add(p.Uid); same++; }
            // Fodder: prefer other defs so same-def copies stay available for later steps.
            for (int pass = 0; pass < 2 && fodder < recipe.FodderCount; pass++)
                foreach (var p in pool)
                {
                    if (fodder >= recipe.FodderCount) break;
                    if (picked.Contains(p.Uid) || p.Rarity != (int)recipe.FodderRarity || Def(c, p).Kind != kind) continue;
                    if (pass == 0 && p.DefId == target.DefId) continue;
                    picked.Add(p.Uid); fodder++;
                }
            return same == recipe.SameDefCount && fodder == recipe.FodderCount ? picked : null;
        }

        private static CommandResult HandleDismantle(PartDismantle cmd, CommandContext ctx)
        {
            var s = ctx.State; var cfg = ctx.Content.PartsConfig;
            GameEngine.Require(cmd.Uids != null && cmd.Uids.Count > 0, "nothing to dismantle");
            var seen = new HashSet<string>();
            foreach (var uid in cmd.Uids)
            {
                var p = RequirePart(s, uid);
                GameEngine.Require(seen.Add(uid), "duplicate part");
                GameEngine.Require(!IsEquipped(s, uid), "unequip the part first");
                GameEngine.Require(p.Awaken == 0, "awakened parts cannot be dismantled");
            }
            long ore = 0;
            foreach (var uid in cmd.Uids)
            {
                var p = s.Parts.Items[uid];
                RefundAndStrip(s, p);
                ore += cfg.RarityDef(p.Rarity).DismantleOre;
                s.Parts.Items.Remove(uid);
            }
            s.Wallet.Add(Currencies.Ore, ore);
            s.Count("parts_dismantled", cmd.Uids.Count);
            return CommandResult.Success(ore);
        }

        private static RuneItem RequireRune(GameState s, string uid)
        {
            GameEngine.Require(uid != null && s.Parts.Runes.ContainsKey(uid), "unknown rune");
            return s.Parts.Runes[uid];
        }

        private static CommandResult HandleRuneSocket(RuneSocket cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Runes);
            var s = ctx.State; var c = ctx.Content; var rune = RequireRune(s, cmd.RuneUid); var p = RequirePart(s, cmd.PartUid);
            GameEngine.Require(string.IsNullOrEmpty(rune.SocketedIn), "rune is already socketed");
            GameEngine.Require(cmd.SlotIndex >= 0 && cmd.SlotIndex < RuneSlotCount(c, p), "rune slot locked");
            EnsureRuneSlots(c, p);
            foreach (var other in p.Runes)
                if (!string.IsNullOrEmpty(other) && s.Parts.Runes[other].DefId == rune.DefId && other != p.Runes[cmd.SlotIndex])
                    throw new RuleException("part already has a rune of this type");
            var old = p.Runes[cmd.SlotIndex];
            if (!string.IsNullOrEmpty(old)) s.Parts.Runes[old].SocketedIn = "";
            p.Runes[cmd.SlotIndex] = rune.Uid;
            rune.SocketedIn = p.Uid;
            return CommandResult.Success();
        }

        private static CommandResult HandleRuneUnsocket(RuneUnsocket cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Runes);
            var s = ctx.State; var p = RequirePart(s, cmd.PartUid);
            GameEngine.Require(cmd.SlotIndex >= 0 && cmd.SlotIndex < p.Runes.Count && !string.IsNullOrEmpty(p.Runes[cmd.SlotIndex]), "slot is empty");
            s.Parts.Runes[p.Runes[cmd.SlotIndex]].SocketedIn = "";
            p.Runes[cmd.SlotIndex] = "";
            return CommandResult.Success();
        }

        private static CommandResult HandleRuneMerge(RuneMerge cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Runes);
            var s = ctx.State; var cfg = ctx.Content.PartsConfig;
            GameEngine.Require(cmd.RuneUids != null && cmd.RuneUids.Count == cfg.RuneMergeCount, "need " + cfg.RuneMergeCount + " runes");
            var seen = new HashSet<string>();
            RuneItem first = null;
            foreach (var uid in cmd.RuneUids)
            {
                var r = RequireRune(s, uid);
                GameEngine.Require(seen.Add(uid), "duplicate rune");
                GameEngine.Require(!r.Locked, "rune is locked");
                GameEngine.Require(string.IsNullOrEmpty(r.SocketedIn), "unsocket the rune first");
                if (first == null) first = r;
                GameEngine.Require(r.DefId == first.DefId && r.Rarity == first.Rarity, "runes must share type and rarity");
            }
            GameEngine.Require(first.Rarity < (int)cfg.RuneMaxRarity, "rune is at max rarity");
            foreach (var uid in cmd.RuneUids) s.Parts.Runes.Remove(uid);
            var result = AddRune(s, ctx.Content, first.DefId, first.Rarity + 1);
            s.Count("rune_merges");
            return CommandResult.Success(result.Uid);
        }

        private static CommandResult HandleRuneLock(RuneLock cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Runes);
            RequireRune(ctx.State, cmd.RuneUid).Locked = cmd.Locked;
            return CommandResult.Success();
        }

        private static CommandResult HandleForge(PartForgeUnlock cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Forge);
            var s = ctx.State; var cfg = ctx.Content.PartsConfig; var p = RequirePart(s, cmd.Uid);
            GameEngine.Require(p.Rarity >= (int)cfg.ForgeMinRarity, "part rarity too low for the Forge");
            GameEngine.Require(p.ForgedSlots < cfg.Forge.Count, "all Forge unlocks done");
            var step = cfg.Forge[p.ForgedSlots];
            ProgressionUtil.Spend(s, new[] { new Cost(Currencies.Diamonds, step.Diamonds), new Cost(Currencies.GearCores, step.GearCores) });
            p.ForgedSlots++;
            EnsureRuneSlots(ctx.Content, p);
            s.Count("part_forges");
            return CommandResult.Success(RuneSlotCount(ctx.Content, p));
        }

        private static CommandResult HandleAwaken(PartAwaken cmd, CommandContext ctx)
        {
            var s = ctx.State; var cfg = ctx.Content.PartsConfig; var p = RequirePart(s, cmd.Uid);
            GameEngine.Require(p.Awaken < 2 && p.Awaken < cfg.AwakenCores.Count, "part is fully awakened");
            var min = p.Awaken == 0 ? cfg.AwakenMinRarity : cfg.SSMinRarity;
            GameEngine.Require(p.Rarity >= (int)min, "part rarity too low to awaken");
            ProgressionUtil.Spend(s, ProgressionCurrencies.PartAwakenCores, cfg.AwakenCores[p.Awaken]);
            p.Awaken++;
            s.Count("part_awakens");
            ctx.Emit("part_awakened", p.Uid, p.Awaken == 1 ? "S" : "SS");
            return CommandResult.Success(p.Awaken);
        }
    }
}
