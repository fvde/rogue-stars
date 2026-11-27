using System;
using System.Collections.Generic;
using System.Linq;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim
{
    public sealed partial class SimPlayer
    {
        private void UpgradeAll()
        {
            UseTime(Timing.Upgrades);
            Worm();
            Parts();
            Heroes();
            Mounts();
        }

        // ---- worm -------------------------------------------------------------------------------------------------

        private void Worm()
        {
            var w = C.WormConfig;
            for (int guard = 0; guard < 2000; guard++)
            {
                // Cheapest Biomass sink first: a talent rank of the current tier, the tier-up, or the next worm level.
                long levelCost = Unlocked(Features.WormLevel) ? WormSystem.NextLevelCost(S, C) : -1;
                var tier = WormSystem.CurrentTier(S, C);
                long talentCost = -1; string talentBtn = null; bool tierUp = false;
                if (tier != null && S.Worm.Level >= tier.RequiredWormLevel)
                {
                    foreach (var b in tier.Buttons)
                    {
                        int r = WormSystem.Rank(S, b.Key);
                        if (r >= b.MaxRank) continue;
                        long c = b.RankCost(r);
                        if (talentCost < 0 || c < talentCost) { talentCost = c; talentBtn = b.Key; }
                    }
                    if (talentBtn == null) { talentCost = tier.RankUpCost; tierUp = true; }
                }
                // Savings for the next dig / key building come first; a starved worm gets one upgrade out of them.
                bool starved = Now - _lastWormUpgradeMs >= WormPatienceDays * 86_400_000L;
                long bio = Bal(Currencies.Biomass) - (starved ? 0 : LabReserve(Currencies.Biomass));
                bool canLevel = levelCost > 0 && levelCost <= bio;
                bool canTalent = talentCost > 0 && talentCost <= bio;
                if (canTalent && (!canLevel || talentCost <= levelCost))
                {
                    if (!(tierUp ? Ok(new WormTalentTierUp()) : Ok(new WormTalentUpgrade { Button = talentBtn, Ranks = 1 }))) break;
                }
                else if (canLevel) { if (!Ok(new WormLevelUp { Levels = 1 })) break; }
                else break;
                _lastWormUpgradeMs = Now;
            }
        }

        // ---- parts --------------------------------------------------------------------------------------------------

        private PartDef PDef(PartItem p) => C.PartsConfig.Defs[p.DefId];

        private static int PartScore(PartItem p) => p.Rarity * 1000 + p.Awaken * 600 + p.ForgedSlots * 100 + p.Level;

        private static readonly PartSlot[] Slots = { PartSlot.Head, PartSlot.Body, PartSlot.Tail, PartSlot.Legs, PartSlot.LeftArm, PartSlot.RightArm };

        private void Parts()
        {
            if (!Unlocked(Features.Parts)) return;
            EquipBest();
            Climbs();
            FeedSpares();
            // Level equipped parts, cheapest first (Ore + Gear Cores).
            var cfg = C.PartsConfig;
            for (int guard = 0; guard < 3000; guard++)
            {
                PartItem pick = null; long pickCost = long.MaxValue;
                foreach (var uid in S.Parts.Equipped.Values)
                {
                    var p = S.Parts.Items[uid];
                    if (p.Level >= PartsSystem.LevelCap(C, p)) continue;
                    long ore = cfg.LevelOreCosts[p.Level];
                    if (ore < pickCost && cfg.LevelGearCoreCosts[p.Level] <= Bal(Currencies.GearCores) - LabReserve(Currencies.GearCores)) { pick = p; pickCost = ore; }
                }
                if (pick == null || pickCost > Bal(Currencies.Ore) - LabReserve(Currencies.Ore) - _oreReserve) break;
                if (!Ok(new PartLevelUp { Uid = pick.Uid, Levels = 1 })) break;
            }
            Runes();
            ForgeAndAwaken();
        }

        private void EquipBest()
        {
            var byKind = S.Parts.Items.Values.GroupBy(p => PDef(p).Kind);
            foreach (var g in byKind)
            {
                // a Mythic that is still ladder material for an equipped one is not worn instead of it: the best part by score
                var best = g.OrderByDescending(PartScore).ThenBy(p => p.Uid, StringComparer.Ordinal).ToList();
                if (g.Key == PartKind.Arm)
                {
                    // two different arms when possible (the same skill twice counts once)
                    var picks = new List<PartItem>();
                    foreach (var p in best) { if (picks.Count == 2) break; if (picks.Count == 0 || p.DefId != picks[0].DefId) picks.Add(p); }
                    for (int i = 0; i < picks.Count; i++)
                    {
                        var slot = i == 0 ? PartSlot.RightArm : PartSlot.LeftArm;
                        if (S.Parts.Equipped.TryGetValue(slot, out var cur) && cur == picks[i].Uid) continue;
                        Exec(new PartEquip { Uid = picks[i].Uid, Slot = slot });
                    }
                }
                else
                {
                    var slot = (PartSlot)(int)g.Key;
                    if (!S.Parts.Equipped.TryGetValue(slot, out var cur) || cur != best[0].Uid) Exec(new PartEquip { Uid = best[0].Uid, Slot = slot });
                }
            }
        }

        /// <summary>Ore kept back for a ladder step whose materials are ready (part levels wait for it).</summary>
        private long _oreReserve;

        /// <summary>
        /// The Mythic ladder for the equipped Mythics, the furthest one first: copies of the same Mythic and other Mythics of the slot
        /// (base step; spare Mythics of unequipped defs first), Metal and Ore; the Replicator (3 spare Mythics + Ore) when copies are
        /// short and there are spares beyond the next steps' other-Mythic needs.
        /// </summary>
        private void Climbs()
        {
            var cfg = C.PartsConfig;
            _oreReserve = 0;
            var order = S.Parts.Equipped.Values.Select(u => S.Parts.Items[u]).Where(p => PDef(p).IsMythic)
                .OrderByDescending(p => p.Rarity).ThenBy(p => p.Uid, StringComparer.Ordinal).Select(p => p.Uid).Distinct().ToList();
            var equippedDefs = new HashSet<string>(S.Parts.Equipped.Values.Select(u => S.Parts.Items[u].DefId));
            foreach (var uid in order)
            {
                for (int guard = 0; guard < 12; guard++)
                {
                    if (!S.Parts.Items.TryGetValue(uid, out var target)) break;
                    var step = PartsSystem.NextClimb(C, target);
                    if (step == null) break;
                    var kind = PDef(target).Kind;
                    var pool = S.Parts.Items.Values.Where(p => p.Uid != uid && PartsSystem.IsMythicMaterial(S, C, p)).OrderBy(p => p.Level).ThenBy(p => p.Uid, StringComparer.Ordinal).ToList();
                    var copies = pool.Where(p => p.DefId == target.DefId).Take(step.Copies).ToList();
                    var others = pool.Where(p => p.DefId != target.DefId && PDef(p).Kind == kind)
                        .OrderBy(p => equippedDefs.Contains(p.DefId) ? 1 : 0).ThenBy(p => p.Uid, StringComparer.Ordinal).Take(step.Others).ToList();
                    if (others.Count < step.Others) break;
                    if (copies.Count < step.Copies)
                    {
                        // Replicator: spare Mythics that are neither copies of an equipped def nor this step's other Mythics
                        var spare = pool.Where(p => !equippedDefs.Contains(p.DefId) && !others.Contains(p)).Take(cfg.ReplicateMythics).ToList();
                        if (spare.Count < cfg.ReplicateMythics) break;
                        if (Bal(Currencies.Ore) < cfg.ReplicateOre) { _oreReserve = Math.Max(_oreReserve, cfg.ReplicateOre); break; }
                        if (!Ok(new PartReplicate { TargetUid = uid, MaterialUids = spare.Select(p => p.Uid).ToList() })) break;
                        continue;
                    }
                    if (Bal(Currencies.Metal) < step.Metal) break;
                    if (Bal(Currencies.Ore) < step.Ore) { _oreReserve = Math.Max(_oreReserve, step.Ore); break; }
                    var mats = copies.Concat(others).Select(p => p.Uid).ToList();
                    if (!Ok(new PartClimb { TargetUid = uid, MaterialUids = mats })) break;
                }
            }
        }

        /// <summary>Every spare standard part (not equipped) becomes Metal (its level Ore and runes come back).</summary>
        private void FeedSpares()
        {
            var spare = S.Parts.Items.Values.Where(p => PartsSystem.IsFeedable(S, C, p)).OrderBy(p => p.Uid, StringComparer.Ordinal).Select(p => p.Uid).ToList();
            if (spare.Count > 0) Exec(new PartFeed { Uids = spare });
        }

        private void Runes()
        {
            if (!Unlocked(Features.Runes)) return;
            var cfg = C.PartsConfig;
            // Merge triples of unsocketed runes (same type and rarity), lowest rarity first.
            for (int guard = 0; guard < 200; guard++)
            {
                var trip = S.Parts.Runes.Values.Where(r => string.IsNullOrEmpty(r.SocketedIn) && !r.Locked && r.Rarity < (int)cfg.RuneMaxRarity)
                    .GroupBy(r => r.DefId + "|" + r.Rarity).Where(g => g.Count() >= cfg.RuneMergeCount)
                    .OrderBy(g => g.First().Rarity).ThenBy(g => g.Key, StringComparer.Ordinal).FirstOrDefault();
                if (trip == null) break;
                if (!Ok(new RuneMerge { RuneUids = trip.OrderBy(r => r.Uid, StringComparer.Ordinal).Take(cfg.RuneMergeCount).Select(r => r.Uid).ToList() })) break;
            }
            // Socket the best free runes into equipped parts (replacing weaker ones).
            foreach (var uid in S.Parts.Equipped.Values.OrderBy(x => x, StringComparer.Ordinal).ToList())
            {
                var p = S.Parts.Items[uid];
                int slots = PartsSystem.RuneSlotCount(C, p);
                for (int i = 0; i < slots; i++)
                {
                    var free = S.Parts.Runes.Values.Where(r => string.IsNullOrEmpty(r.SocketedIn))
                        .OrderByDescending(r => r.Rarity).ThenBy(r => r.Uid, StringComparer.Ordinal).FirstOrDefault();
                    if (free == null) return;
                    string cur = i < p.Runes.Count ? p.Runes[i] : null;
                    if (!string.IsNullOrEmpty(cur))
                    {
                        if (S.Parts.Runes[cur].Rarity >= free.Rarity) continue;
                        Exec(new RuneUnsocket { PartUid = uid, SlotIndex = i });
                    }
                    Exec(new RuneSocket { RuneUid = free.Uid, PartUid = uid, SlotIndex = i });
                }
            }
        }

        private void ForgeAndAwaken()
        {
            var cfg = C.PartsConfig;
            foreach (var uid in S.Parts.Equipped.Values.OrderBy(x => x, StringComparer.Ordinal).ToList())
            {
                var p = S.Parts.Items[uid];
                if (Unlocked(Features.Forge) && p.Rarity >= (int)cfg.ForgeMinRarity && p.ForgedSlots < cfg.Forge.Count)
                {
                    var step = cfg.Forge[p.ForgedSlots];
                    if (Spendable(step.Diamonds) && Bal(Currencies.GearCores) - LabReserve(Currencies.GearCores) >= step.GearCores * 2) Exec(new PartForgeUnlock { Uid = uid });
                }
            }
            // Awakening: once per part, the best equipped Mythic+ part first.
            if (Bal(ProgressionCurrencies.PartAwakenCores) >= cfg.AwakenCost)
            {
                var best = S.Parts.Equipped.Values.Select(u => S.Parts.Items[u]).Where(p => p.Awaken == 0 && PDef(p).IsMythic && p.Rarity >= (int)cfg.AwakenMinRarity)
                    .OrderByDescending(p => p.Rarity).ThenBy(p => p.Uid, StringComparer.Ordinal).FirstOrDefault();
                if (best != null) Exec(new PartAwaken { Uid = best.Uid });
            }
        }

        // ---- heroes & mounts --------------------------------------------------------------------------------------

        private void Heroes()
        {
            if (!Unlocked(Features.Heroes)) return;
            var cfg = C.HeroConfig;
            foreach (var id in cfg.Defs.Keys.OrderBy(x => x, StringComparer.Ordinal))
                if (HeroSystem.Get(S, id) == null && Bal(Currencies.HeroShards(id)) >= C.ShardStars.UnlockShards) Exec(new HeroUnlock { HeroId = id });
            foreach (var (id, h) in S.Heroes.Owned.OrderBy(k => k.Key, StringComparer.Ordinal).ToList())
                while (h.Stars < cfg.MaxStars && Bal(Currencies.HeroShards(id)) >= C.ShardStars.NextCost(h.Stars) && Ok(new HeroStarUp { HeroId = id })) { }
            var best = S.Heroes.Owned.OrderByDescending(k => k.Value.Stars).ThenByDescending(k => k.Key == TargetHero).ThenByDescending(k => k.Value.Level)
                .ThenBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key).FirstOrDefault();
            if (best != null && S.Heroes.Equipped != best) Exec(new HeroEquip { HeroId = best });
            if (best != null)
            {
                var h = S.Heroes.Owned[best];
                while (h.Level < cfg.MaxLevel(h.Stars) && Bal(Currencies.HeroXp) >= cfg.LevelXpCosts[h.Level] && Ok(new HeroLevelUp { HeroId = best, Levels = 1 })) { }
            }
        }

        private void Mounts()
        {
            if (!Unlocked(Features.Mounts)) return;
            var cfg = C.MountConfig;
            foreach (var id in cfg.Defs.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList())
                if (MountSystem.Get(S, id) == null && Bal(Currencies.MountShards(id)) >= C.ShardStars.UnlockShards) Exec(new MountUnlock { MountId = id });
            if (S.Mounts.Owned.Count == 0) return;
            foreach (var (id, m) in S.Mounts.Owned.OrderBy(k => k.Key, StringComparer.Ordinal).ToList())
            {
                while (m.Stars < cfg.MaxStars && Bal(Currencies.MountShards(id)) >= C.ShardStars.NextCost(m.Stars)
                       && Ok(new MountStarUp { MountId = id })) { }
            }
            var best = S.Mounts.Owned.OrderByDescending(k => k.Value.Stars).ThenByDescending(k => k.Value.Level)
                .ThenBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key).First();
            if (S.Mounts.Active != best) Exec(new MountEquip { MountId = best });
            var a = S.Mounts.Owned[best];
            while (a.Level < cfg.MaxLevel(a.Stars) && Bal(Currencies.Fuel) >= cfg.LevelFuelCosts[a.Level] && Ok(new MountLevelUp { MountId = best, Levels = 1 })) { }
        }
    }
}
