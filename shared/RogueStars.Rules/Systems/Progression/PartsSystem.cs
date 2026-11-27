using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Parts v2 (docs/design/parts.md): six slots (arms interchangeable), four standard item sets (Common … Legendary, no merging)
    /// and 30 Mythics that climb their own ladder (Mythic → +1 → +2 → S → S+1 → S+2 → SS) with copies and other Mythics of the same
    /// slot, Metal (from feeding spare standard parts) and Ore; the Replicator; levels and caps; runes; Forge; one-time awakening.
    /// Granters for RewardKinds.Part (Rarity = PartRarity index; Id "*" = a random def of that rarity) and RewardKinds.Rune.
    /// </summary>
    public static class PartsSystem
    {
        /// <summary>The parts model of this build (PartsState.Model): older saves are reset to the starter set.</summary>
        public const int ModelVersion = 2;

        public static readonly PartSlot[] AllSlots = { PartSlot.Head, PartSlot.Body, PartSlot.Tail, PartSlot.Legs, PartSlot.LeftArm, PartSlot.RightArm };

        public static void Register(GameEngine e)
        {
            e.Register<PartEquip>(HandleEquip);
            e.Register<PartUnequip>(HandleUnequip);
            e.Register<PartLevelUp>(HandleLevelUp);
            e.Register<PartLevelDown>(HandleLevelDown);
            e.Register<PartLock>(HandleLock);
            e.Register<PartFeed>(HandleFeed);
            e.Register<PartClimb>(HandleClimb);
            e.Register<PartReplicate>(HandleReplicate);
            e.Register<RuneSocket>(HandleRuneSocket);
            e.Register<RuneUnsocket>(HandleRuneUnsocket);
            e.Register<RuneMerge>(HandleRuneMerge);
            e.Register<RuneLock>(HandleRuneLock);
            e.Register<PartForgeUnlock>(HandleForge);
            e.Register<PartAwaken>(HandleAwaken);
            e.Rewards.Register(RewardKinds.Part, GrantPart);
            e.Rewards.Register(RewardKinds.Rune, GrantRune);
            e.RegisterLoadoutContributor(Contribute);
            e.RegisterNewPlayer(ctx => ctx.State.Parts.Model = ModelVersion); // a new save starts on v2 (the starter kit grants its parts)
            e.RegisterSettler(ResetOldModel);
        }

        /// <summary>
        /// Pre-release, no migration (owner): a save from before parts v2 drops its old parts and gets the starter set (one Common per
        /// slot, equipped). Runes stay in the inventory (unsocketed).
        /// </summary>
        private static void ResetOldModel(CommandContext ctx)
        {
            var s = ctx.State;
            if (s.Parts.Model >= ModelVersion) return;
            s.Parts.Model = ModelVersion;
            s.Parts.Items.Clear();
            s.Parts.Equipped.Clear();
            foreach (var r in s.Parts.Runes.Values) r.SocketedIn = "";
            var slots = new List<PartSlot>(AllSlots);
            foreach (var id in ctx.Content.PartsConfig.StarterParts)
            {
                if (!ctx.Content.PartsConfig.Defs.TryGetValue(id, out var def)) continue;
                var item = AddPart(s, ctx.Content, id, (int)def.Rarity);
                foreach (var slot in slots)
                    if (Fits(def, slot) && !s.Parts.Equipped.ContainsKey(slot)) { s.Parts.Equipped[slot] = item.Uid; break; }
            }
        }

        // ---- inventory helpers (public: used by tests, debug tools and other grant paths) ---------

        /// <summary>
        /// Add a part instance directly (granters use this). A standard def is always its own rarity; a Mythic may be added at a ladder
        /// step (tests, tools). Returns the new item.
        /// </summary>
        public static PartItem AddPart(GameState s, ContentDb c, string defId, int rarity)
        {
            if (!c.PartsConfig.Defs.TryGetValue(defId, out var def)) throw new InvalidOperationException("unknown part " + defId);
            int r = def.IsMythic ? Math.Max((int)PartRarity.Mythic, Math.Min((int)c.PartsConfig.TopRarity, rarity)) : (int)def.Rarity;
            var item = new PartItem { Uid = "p" + (s.Parts.NextUid++).ToString("D8"), DefId = defId, Rarity = r };
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

        /// <summary>True if a def fits a slot: an arm in either arm, every other kind in its own slot.</summary>
        public static bool Fits(PartDef def, PartSlot slot)
        {
            bool armSlot = slot == PartSlot.LeftArm || slot == PartSlot.RightArm;
            return def.Kind == PartKind.Arm ? armSlot : !armSlot && (int)def.Kind == (int)slot;
        }

        /// <summary>Level cap: the rarity's (Common 10, Rare 20, Epic 30, Legendary 50; Mythic 70 … SS 100).</summary>
        public static int LevelCap(ContentDb c, PartItem p) => Math.Min(c.PartsConfig.MaxLevel, c.PartsConfig.RarityDef(p.Rarity).LevelCap);

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
        /// Stats of a part: level-1 stats × (1 + forge%) + per-level stats × (level − 1), plus socketed runes. Awakened parts use their
        /// def's awakened stats (×1.4 level-1 values).
        /// </summary>
        public static StatBlock PartStats(GameState s, ContentDb c, PartItem p)
        {
            var b = DefStats(c, Def(c, p), p.Level, p.Awaken > 0, ForgeBonusPercent(c, p));
            foreach (var ru in p.Runes)
            {
                if (string.IsNullOrEmpty(ru) || !s.Parts.Runes.TryGetValue(ru, out var rune)) continue;
                b.Add(RuneStat(c, rune));
            }
            return b;
        }

        /// <summary>A def's stats at a level (no runes): level-1 × (1 + forge%) + per level × (level − 1).</summary>
        public static StatBlock DefStats(ContentDb c, PartDef def, int level, bool awakened = false, double forgePct = 0)
        {
            var b = ProgressionUtil.Scaled(awakened ? def.AwakenedBaseStats : def.BaseStats, 1 + forgePct / 100.0);
            b.AddBlock(ProgressionUtil.Scaled(awakened ? def.AwakenedStatsPerLevel : def.StatsPerLevel, Math.Max(0, level - 1)));
            return b;
        }

        public static StatMod RuneStat(ContentDb c, RuneItem r)
        {
            var d = c.PartsConfig.Runes[r.DefId];
            double v = d.BaseValue * c.PartsConfig.RuneRarityMultipliers[Math.Min(r.Rarity, c.PartsConfig.RuneRarityMultipliers.Count - 1)];
            return d.IsPercent ? new StatMod(d.Stat, 0, v) : new StatMod(d.Stat, v);
        }

        /// <summary>Display name: the awakened name once awakened.</summary>
        public static string DisplayName(ContentDb c, PartItem p)
        {
            var d = Def(c, p);
            return p.Awaken > 0 && !string.IsNullOrEmpty(d.AwakenedName) ? d.AwakenedName : d.Name;
        }

        /// <summary>Number of a part's skills active at a rarity (0..5): Epic 1, Legendary 2, Mythic 3, Mythic S 4, Mythic SS 5.</summary>
        public static int SkillCount(ContentDb c, int rarity) => c.PartsConfig.RarityDef(rarity).SkillCount;

        /// <summary>Skills a part grants while equipped (the first SkillCount of its list; the awakened versions once awakened).</summary>
        public static List<string> PartSkills(ContentDb c, PartItem p)
        {
            var def = Def(c, p);
            var ids = p.Awaken > 0 && def.AwakenedSkillIds.Count > 0 ? def.AwakenedSkillIds : def.SkillIds;
            var list = new List<string>();
            int n = SkillCount(c, p.Rarity);
            for (int i = 0; i < ids.Count && i < n; i++) list.Add(ids[i]);
            return list;
        }

        /// <summary>Metal a part is worth when fed (0 for Mythics: they are ladder material, never Metal).</summary>
        public static long MetalValue(ContentDb c, PartItem p) => Def(c, p).IsMythic ? 0 : c.PartsConfig.RarityDef(p.Rarity).Metal;

        /// <summary>True if a spare standard part may be fed for Metal: not equipped, locked or forged, never a Mythic.</summary>
        public static bool IsFeedable(GameState s, ContentDb c, PartItem p) =>
            !Def(c, p).IsMythic && !p.Locked && p.ForgedSlots == 0 && !IsEquipped(s, p.Uid);

        /// <summary>
        /// True if a Mythic may be ladder or Replicator material: base step (not climbed), not awakened, not equipped, locked or forged.
        /// </summary>
        public static bool IsMythicMaterial(GameState s, ContentDb c, PartItem p) =>
            Def(c, p).IsMythic && p.Rarity == (int)PartRarity.Mythic && p.Awaken == 0 && !p.Locked && p.ForgedSlots == 0 && !IsEquipped(s, p.Uid);

        /// <summary>The next ladder step of a Mythic (null at SS or for a standard part).</summary>
        public static PartClimbStep NextClimb(ContentDb c, PartItem p)
        {
            if (!Def(c, p).IsMythic || p.Rarity >= (int)c.PartsConfig.TopRarity) return null;
            return c.PartsConfig.Rarities[p.Rarity + 1].Climb;
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
                if (p == null || !c.PartsConfig.Defs.ContainsKey(p.DefId)) continue;
                l.WormStats.AddBlock(PartStats(s, c, p));
                // the same skill twice (two copies of an arm) counts once
                foreach (var sk in PartSkills(c, p)) if (!l.MetaSkillIds.Contains(sk)) l.MetaSkillIds.Add(sk);
            }
        }

        // ---- granters ------------------------------------------------------------------------------

        private static void GrantPart(CommandContext ctx, Reward r)
        {
            if (RewardWildcard.IsWildcard(r))
            {
                int rarity = RewardWildcard.PartRarityForTier(r.Rarity);
                var pool = new Dictionary<string, PartDef>();
                foreach (var kv in ctx.Content.PartsConfig.Defs) if ((int)kv.Value.Rarity == Math.Min(rarity, (int)PartRarity.Mythic)) pool[kv.Key] = kv.Value;
                for (long i = 0; i < r.Amount; i++) AddPart(ctx.State, ctx.Content, RewardWildcard.PickAny(ctx.Rng, pool), rarity);
                return;
            }
            for (long i = 0; i < r.Amount; i++) AddPart(ctx.State, ctx.Content, r.Id, r.Rarity);
        }

        private static void GrantRune(CommandContext ctx, Reward r)
        {
            for (long i = 0; i < r.Amount; i++)
                AddRune(ctx.State, ctx.Content, RewardWildcard.IsWildcard(r) ? RewardWildcard.PickAny(ctx.Rng, ctx.Content.PartsConfig.Runes) : r.Id, r.Rarity);
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

        /// <summary>Refund 100% of level materials and return socketed runes to the inventory, then remove the part.</summary>
        private static void Consume(GameState s, PartItem p)
        {
            s.Wallet.Add(Currencies.Ore, p.SpentOre);
            s.Wallet.Add(Currencies.GearCores, p.SpentGearCores);
            p.SpentOre = p.SpentGearCores = 0;
            for (int i = 0; i < p.Runes.Count; i++)
            {
                if (!string.IsNullOrEmpty(p.Runes[i]) && s.Parts.Runes.TryGetValue(p.Runes[i], out var r)) r.SocketedIn = "";
                p.Runes[i] = "";
            }
            s.Parts.Items.Remove(p.Uid);
        }

        // ---- handlers ------------------------------------------------------------------------------

        private static CommandResult HandleEquip(PartEquip cmd, CommandContext ctx)
        {
            var s = ctx.State; var p = RequirePart(s, cmd.Uid); var def = Def(ctx.Content, p);
            if (def.Kind == PartKind.Arm) GameEngine.Require(cmd.Slot == PartSlot.LeftArm || cmd.Slot == PartSlot.RightArm, "arm parts go into an arm slot");
            else GameEngine.Require(Fits(def, cmd.Slot), "part does not fit this slot");
            // An arm part moved from the other arm leaves that arm empty.
            foreach (var slot in AllSlots)
                if (slot != cmd.Slot && s.Parts.Equipped.TryGetValue(slot, out var u) && u == p.Uid) s.Parts.Equipped.Remove(slot);
            s.Parts.Equipped[cmd.Slot] = p.Uid;
            s.Count("part_equips");
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

        private static CommandResult HandleLock(PartLock cmd, CommandContext ctx)
        {
            RequirePart(ctx.State, cmd.Uid).Locked = cmd.Locked;
            return CommandResult.Success();
        }

        /// <summary>Metal from feeding these parts; throws RuleException on any part that may not be fed (UI preview).</summary>
        public static long ValidateFeed(GameState s, ContentDb c, IList<string> uids)
        {
            GameEngine.Require(uids != null && uids.Count > 0, "nothing to feed");
            var seen = new HashSet<string>();
            long metal = 0;
            foreach (var uid in uids)
            {
                var p = RequirePart(s, uid);
                GameEngine.Require(seen.Add(uid), "duplicate part");
                GameEngine.Require(!Def(c, p).IsMythic, "Mythics are never fed: they are ladder material");
                GameEngine.Require(!IsEquipped(s, uid), "unequip the part first");
                GameEngine.Require(!p.Locked, "the part is locked");
                GameEngine.Require(p.ForgedSlots == 0, "forged parts are never fed");
                metal += MetalValue(c, p);
            }
            return metal;
        }

        private static CommandResult HandleFeed(PartFeed cmd, CommandContext ctx)
        {
            var s = ctx.State;
            long metal = ValidateFeed(s, ctx.Content, cmd.Uids);
            foreach (var uid in cmd.Uids) Consume(s, s.Parts.Items[uid]);
            s.Wallet.Add(Currencies.Metal, metal);
            s.Count("parts_fed", cmd.Uids.Count);
            ctx.Emit("parts_fed", cmd.Uids.Count.ToString(), metal.ToString());
            return CommandResult.Success(metal);
        }

        /// <summary>
        /// Validate a ladder step (UI preview); throws RuleException. Materials are exactly the step's copies (same def) and other Mythics
        /// (a different def of the same slot; every arm Mythic is an arm), each at the base step, not awakened, equipped, locked or
        /// forged. A copy of the climbed item always counts as a copy, never as an other Mythic.
        /// </summary>
        public static void ValidateClimb(GameState s, ContentDb c, string targetUid, IList<string> materialUids)
        {
            var target = RequirePart(s, targetUid);
            var def = Def(c, target);
            GameEngine.Require(def.IsMythic, "only Mythics climb");
            var step = NextClimb(c, target);
            GameEngine.Require(step != null, "the part is at Mythic SS");
            var mats = materialUids ?? new List<string>();
            var seen = new HashSet<string>();
            int copies = 0, others = 0;
            foreach (var uid in mats)
            {
                GameEngine.Require(uid != targetUid && seen.Add(uid), "duplicate material");
                var m = RequirePart(s, uid);
                GameEngine.Require(IsMythicMaterial(s, c, m), "materials are base-step Mythics that are not awakened, equipped, locked or forged");
                var md = Def(c, m);
                if (m.DefId == target.DefId) copies++;
                else if (md.Kind == def.Kind) others++;
                else throw new RuleException("other Mythics must be of the same slot");
            }
            GameEngine.Require(copies == step.Copies, "needs " + step.Copies + " copies of this Mythic");
            GameEngine.Require(others == step.Others, "needs " + step.Others + " other Mythics of this slot");
            GameEngine.Require(s.Wallet.CanAfford(Currencies.Metal, step.Metal), "not enough metal");
            GameEngine.Require(s.Wallet.CanAfford(Currencies.Ore, step.Ore), "not enough ore");
        }

        private static CommandResult HandleClimb(PartClimb cmd, CommandContext ctx)
        {
            var s = ctx.State; var c = ctx.Content;
            ValidateClimb(s, c, cmd.TargetUid, cmd.MaterialUids);
            var target = s.Parts.Items[cmd.TargetUid];
            var step = NextClimb(c, target);
            ProgressionUtil.Spend(s, new[] { new Cost(Currencies.Metal, step.Metal), new Cost(Currencies.Ore, step.Ore) });
            foreach (var uid in cmd.MaterialUids) Consume(s, s.Parts.Items[uid]);
            target.Rarity++;
            EnsureRuneSlots(c, target);
            s.Count("part_climbs");
            ProgressionUtil.CountMax(s, "part_rarity_max", target.Rarity);
            ctx.Emit("part_climbed", target.Uid, target.Rarity.ToString());
            return CommandResult.Success(target.Rarity);
        }

        /// <summary>Validate a replication (UI preview); throws RuleException.</summary>
        public static void ValidateReplicate(GameState s, ContentDb c, string targetUid, IList<string> materialUids)
        {
            var cfg = c.PartsConfig; var target = RequirePart(s, targetUid);
            GameEngine.Require(Def(c, target).IsMythic, "the Replicator copies Mythics only");
            GameEngine.Require(materialUids != null && materialUids.Count == cfg.ReplicateMythics, "needs " + cfg.ReplicateMythics + " spare Mythics");
            var seen = new HashSet<string>();
            foreach (var uid in materialUids)
            {
                GameEngine.Require(uid != targetUid && seen.Add(uid), "duplicate material");
                GameEngine.Require(IsMythicMaterial(s, c, RequirePart(s, uid)), "materials are base-step Mythics that are not awakened, equipped, locked or forged");
            }
            GameEngine.Require(s.Wallet.CanAfford(Currencies.Ore, cfg.ReplicateOre), "not enough ore");
        }

        private static CommandResult HandleReplicate(PartReplicate cmd, CommandContext ctx)
        {
            var s = ctx.State; var cfg = ctx.Content.PartsConfig;
            ValidateReplicate(s, ctx.Content, cmd.TargetUid, cmd.MaterialUids);
            ProgressionUtil.Spend(s, Currencies.Ore, cfg.ReplicateOre);
            foreach (var uid in cmd.MaterialUids) Consume(s, s.Parts.Items[uid]);
            var copy = AddPart(s, ctx.Content, s.Parts.Items[cmd.TargetUid].DefId, (int)PartRarity.Mythic);
            s.Count("part_replications");
            ctx.Emit("part_replicated", copy.Uid, copy.DefId);
            return CommandResult.Success(copy.Uid);
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
            GameEngine.Require(p.Awaken == 0, "part is already awakened");
            GameEngine.Require(Def(ctx.Content, p).IsMythic && p.Rarity >= (int)cfg.AwakenMinRarity, "part rarity too low to awaken");
            ProgressionUtil.Spend(s, ProgressionCurrencies.PartAwakenCores, cfg.AwakenCost);
            p.Awaken = 1;
            s.Count("part_awakens");
            ctx.Emit("part_awakened", p.Uid, DisplayName(ctx.Content, p));
            return CommandResult.Success(p.Awaken);
        }
    }
}
