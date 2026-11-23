using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim;

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
            long bio = Bal(Currencies.Biomass) - LabReserve(Currencies.Biomass);
            bool canLevel = levelCost > 0 && levelCost <= bio;
            bool canTalent = talentCost > 0 && talentCost <= bio;
            if (canTalent && (!canLevel || talentCost <= levelCost))
            {
                if (!(tierUp ? Ok(new WormTalentTierUp()) : Ok(new WormTalentUpgrade { Button = talentBtn, Ranks = 1 }))) break;
            }
            else if (canLevel) { if (!Ok(new WormLevelUp { Levels = 1 })) break; }
            else break;
        }
    }

    // ---- parts --------------------------------------------------------------------------------------------------

    private PartDef PDef(PartItem p) => C.PartsConfig.Defs[p.DefId];

    private static int PartScore(PartItem p) => p.Rarity * 1000 + p.Awaken * 600 + p.ForgedSlots * 100 + p.Level;

    private static readonly PartSlot[] Slots = { PartSlot.Head, PartSlot.Body, PartSlot.Tail, PartSlot.Legs, PartSlot.LeftArm, PartSlot.RightArm };

    private void Parts()
    {
        if (!Unlocked(Features.Parts)) return;
        Exec(new PartQuickMerge());
        HighMerges();
        EquipBest();
        // Level equipped parts, cheapest first (Ore + Gear Cores), raising caps with plates when capped.
        var cfg = C.PartsConfig;
        for (int guard = 0; guard < 3000; guard++)
        {
            PartItem pick = null; long pickCost = long.MaxValue;
            foreach (var uid in S.Parts.Equipped.Values)
            {
                var p = S.Parts.Items[uid];
                if (p.Level >= PartsSystem.LevelCap(C, p))
                {
                    var rd = cfg.RarityDef(p.Rarity);
                    if (p.CapRaises < rd.MaxCapRaises && p.CapRaises < cfg.CapRaisePlates.Count && Bal(Currencies.AdvancementPlates) >= cfg.CapRaisePlates[p.CapRaises])
                        Exec(new PartRaiseCap { Uid = uid });
                    if (p.Level >= PartsSystem.LevelCap(C, p)) continue;
                }
                long ore = cfg.LevelOreCosts[p.Level];
                if (ore < pickCost && cfg.LevelGearCoreCosts[p.Level] <= Bal(Currencies.GearCores)) { pick = p; pickCost = ore; }
            }
            if (pick == null || pickCost > Bal(Currencies.Ore) - LabReserve(Currencies.Ore) - _oreReserve) break;
            if (!Ok(new PartLevelUp { Uid = pick.Uid, Levels = 1 })) break;
        }
        Runes();
        ForgeAndAwaken();
        DismantleSurplus();
    }

    private void EquipBest()
    {
        var byKind = S.Parts.Items.Values.Where(p => p.DefId != RewardWildcard.Any).GroupBy(p => PDef(p).Kind);
        foreach (var g in byKind)
        {
            var best = g.OrderByDescending(PartScore).ThenBy(p => p.Uid, StringComparer.Ordinal).ToList();
            if (g.Key == PartKind.Arm)
            {
                for (int i = 0; i < 2 && i < best.Count; i++)
                {
                    var slot = i == 0 ? PartSlot.RightArm : PartSlot.LeftArm;
                    var other = i == 0 ? PartSlot.LeftArm : PartSlot.RightArm;
                    if (S.Parts.Equipped.TryGetValue(slot, out var cur) && cur == best[i].Uid) continue;
                    if (S.Parts.Equipped.TryGetValue(other, out var o) && o == best[i].Uid) continue;
                    Exec(new PartEquip { Uid = best[i].Uid, Slot = slot });
                }
            }
            else
            {
                var slot = (PartSlot)(int)g.Key;
                if (!S.Parts.Equipped.TryGetValue(slot, out var cur) || cur != best[0].Uid) Exec(new PartEquip { Uid = best[0].Uid, Slot = slot });
            }
        }
    }

    /// <summary>
    /// Merges above the quick-merge ceiling (Mythic … Mythic SS+2) for equipped parts, best part first (the bot pushes one
    /// item up the exponential road): same-def copies (lowest rarity first), then Legendary+ fodder of any def, cheapest
    /// first. Copies of equipped defs are kept as copies, never burnt as fodder.
    /// </summary>
    /// <summary>Ore kept back for a high merge whose materials are ready (part levels wait for it).</summary>
    private long _oreReserve;

    private void HighMerges()
    {
        var cfg = C.PartsConfig;
        _oreReserve = 0;
        var order = S.Parts.Equipped.Values.Select(u => S.Parts.Items[u]).OrderByDescending(p => p.Rarity).ThenBy(p => p.Uid, StringComparer.Ordinal).Select(p => p.Uid).ToList();
        // Copies still needed by the equipped parts (sum of own-def copies on their remaining steps) stay reserved; extra
        // copies of an equipped def are ordinary fodder.
        var copiesNeeded = new Dictionary<string, int>();
        foreach (var u in S.Parts.Equipped.Values)
        {
            var e = S.Parts.Items[u]; int n = 0;
            for (int r = e.Rarity + 1; r < cfg.Rarities.Count; r++) n += cfg.Rarities[r].Merge?.SameDefCount ?? 0;
            copiesNeeded[e.DefId] = copiesNeeded.GetValueOrDefault(e.DefId) + n;
        }
        HashSet<string> Reserved() => new(copiesNeeded.SelectMany(kv => S.Parts.Items.Values
            .Where(p => p.DefId == kv.Key && p.Rarity >= (int)PartRarity.Legendary && PartsSystem.IsMergeMaterial(S, p))
            .OrderBy(p => p.Rarity).ThenBy(p => p.Uid, StringComparer.Ordinal).Take(kv.Value).Select(p => p.Uid)));
        foreach (var uid in order)
        {
            for (int guard = 0; guard < 24; guard++)
            {
                if (!S.Parts.Items.TryGetValue(uid, out var target) || target.Rarity + 1 >= cfg.Rarities.Count) break;
                if (target.Rarity + 1 <= (int)cfg.QuickMergeMaxTarget) break;
                var recipe = cfg.Rarities[target.Rarity + 1].Merge;
                if (recipe == null) break;
                var pool = S.Parts.Items.Values.Where(p => p.Uid != uid && PartsSystem.IsMergeMaterial(S, p)).ToList();
                var mats = pool.Where(p => p.DefId == target.DefId && p.Rarity >= (int)recipe.SameDefRarity)
                    .OrderBy(p => p.Rarity).ThenBy(p => p.Level).ThenBy(p => p.Uid, StringComparer.Ordinal).Take(recipe.SameDefCount).Select(p => p.Uid).ToList();
                if (mats.Count < recipe.SameDefCount)
                {
                    // Copies short: replicate one from Legendary+ surplus when there is fodder beyond this merge's own need.
                    if (target.Rarity < (int)cfg.ReplicateMinTargetRarity) break;
                    var reservedNow = Reserved();
                    var spare = pool.Where(p => p.Rarity >= (int)PartRarity.Legendary && !reservedNow.Contains(p.Uid))
                        .OrderBy(p => PartsSystem.FodderValue(C, p)).ThenBy(p => p.Uid, StringComparer.Ordinal).ToList();
                    if (spare.Sum(p => PartsSystem.FodderValue(C, p)) < cfg.ReplicateFodderPoints + recipe.FodderPoints) break;
                    if (Bal(Currencies.Ore) < cfg.ReplicateOre) { _oreReserve = Math.Max(_oreReserve, cfg.ReplicateOre); break; }
                    var pick = new List<PartItem>(); long pp = 0;
                    foreach (var p in spare) { if (pp >= cfg.ReplicateFodderPoints) break; pick.Add(p); pp += PartsSystem.FodderValue(C, p); }
                    for (int i = 0; i < pick.Count;)
                        if (pp - PartsSystem.FodderValue(C, pick[i]) >= cfg.ReplicateFodderPoints) { pp -= PartsSystem.FodderValue(C, pick[i]); pick.RemoveAt(i); }
                        else i++;
                    if (!Ok(new PartReplicate { TargetUid = uid, MaterialUids = pick.Select(p => p.Uid).ToList() })) break;
                    continue;
                }
                var reserved = Reserved();
                var fodder = pool.Where(p => !mats.Contains(p.Uid) && p.Rarity >= (int)recipe.FodderMinRarity && !reserved.Contains(p.Uid))
                    .OrderBy(p => PartsSystem.FodderValue(C, p)).ThenBy(p => p.Level).ThenBy(p => p.Uid, StringComparer.Ordinal).ToList();
                var chosen = new List<PartItem>(); long pts = 0;
                foreach (var p in fodder) { if (pts >= recipe.FodderPoints) break; chosen.Add(p); pts += PartsSystem.FodderValue(C, p); }
                if (pts < recipe.FodderPoints) break;
                // Drop the cheapest materials that are not needed (the rules reject superfluous fodder).
                for (int i = 0; i < chosen.Count;)
                    if (pts - PartsSystem.FodderValue(C, chosen[i]) >= recipe.FodderPoints) { pts -= PartsSystem.FodderValue(C, chosen[i]); chosen.RemoveAt(i); }
                    else i++;
                mats.AddRange(chosen.Select(p => p.Uid));
                if (Bal(Currencies.Ore) < recipe.Ore) { _oreReserve = Math.Max(_oreReserve, recipe.Ore); break; }
                if (!Ok(new PartMerge { TargetUid = uid, MaterialUids = mats })) break;
            }
        }
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
                if (Spendable(step.Diamonds) && Bal(Currencies.GearCores) >= step.GearCores * 2) Exec(new PartForgeUnlock { Uid = uid });
            }
        }
        // Awakening: once per part, the best equipped Mythic+ part first.
        if (Bal(ProgressionCurrencies.PartAwakenCores) >= cfg.AwakenCost)
        {
            var best = S.Parts.Equipped.Values.Select(u => S.Parts.Items[u]).Where(p => p.Awaken == 0 && p.Rarity >= (int)cfg.AwakenMinRarity)
                .OrderByDescending(p => p.Rarity).ThenBy(p => p.Uid, StringComparer.Ordinal).FirstOrDefault();
            if (best != null) Exec(new PartAwaken { Uid = best.Uid });
        }
    }

    /// <summary>Inventory hygiene: everything below Legendary merges up (quick merge); only stragglers of a huge inventory go.</summary>
    private void DismantleSurplus()
    {
        if (S.Parts.Items.Count <= 400) return;
        var junk = S.Parts.Items.Values.Where(p => !PartsSystem.IsEquipped(S, p.Uid) && p.Awaken == 0 && p.Rarity < (int)PartRarity.Epic)
            .OrderBy(p => p.Rarity).ThenBy(p => p.Uid, StringComparer.Ordinal).Take(S.Parts.Items.Count - 380).Select(p => p.Uid).ToList();
        if (junk.Count > 0) Exec(new PartDismantle { Uids = junk });
    }

    // ---- heroes & mounts --------------------------------------------------------------------------------------

    private void Heroes()
    {
        if (!Unlocked(Features.Heroes)) return;
        var cfg = C.HeroConfig;
        foreach (var id in cfg.Defs.Keys.OrderBy(x => x, StringComparer.Ordinal))
            if (HeroSystem.Get(S, id) == null && Bal(Currencies.HeroShards(id)) >= cfg.StarShardCosts[1]) Exec(new HeroUnlock { HeroId = id });
        foreach (var (id, h) in S.Heroes.Owned.OrderBy(k => k.Key, StringComparer.Ordinal).ToList())
            while (h.Stars < cfg.MaxStars && Bal(Currencies.HeroShards(id)) >= cfg.StarShardCosts[h.Stars + 1] && Ok(new HeroStarUp { HeroId = id })) { }
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
        if (!Unlocked(Features.Mounts) || S.Mounts.Owned.Count == 0) return;
        var cfg = C.MountConfig;
        foreach (var (id, m) in S.Mounts.Owned.OrderBy(k => k.Key, StringComparer.Ordinal).ToList())
        {
            while (m.Stars < cfg.MaxStars && Bal(Currencies.MountShards(id)) >= cfg.StarShardCosts[m.Stars + 1] && Ok(new MountStarUp { MountId = id })) { }
            while (m.Stars >= cfg.MaxStars && m.Awaken < cfg.MaxAwaken && Bal(Currencies.MountShards(id)) >= cfg.AwakenShardCosts[m.Awaken + 1]
                   && Bal(Currencies.MountCores) >= cfg.AwakenCoreCosts[m.Awaken + 1] && Ok(new MountAwaken { MountId = id })) { }
        }
        var best = S.Mounts.Owned.OrderByDescending(k => k.Value.Stars + k.Value.Awaken).ThenByDescending(k => k.Value.Level)
            .ThenBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key).First();
        if (S.Mounts.Active != best) Exec(new MountEquip { MountId = best });
        var a = S.Mounts.Owned[best];
        while (a.Level < cfg.MaxLevel(a.Stars, a.Awaken) && Bal(Currencies.Fuel) >= cfg.LevelFuelCosts[a.Level] && Ok(new MountLevelUp { MountId = best, Levels = 1 })) { }
    }
}
