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
        /// <summary>City upgrades may spend at most this share of the current Biomass/Ore balance per upgrade (rest: worm, parts).</summary>
        private const double CityShare = 0.5;

        // ---- city -----------------------------------------------------------------------------------------------

        private void City()
        {
            UseTime(Timing.City);
            Exec(new CityCollect());
            if (S.City.PriorityCurrency != Currencies.Biomass) Exec(new CitySetPriority { Currency = Currencies.Biomass });
            SpeedUpTimers();

            // Place every owned, unplaced building (unlocking the next area when it is needed and affordable).
            foreach (var b in CitySystem.SortedBlueprints(S).Where(b => !b.Placed))
            {
                var def = C.Blueprint(b.Id);
                if (b.Level == 0 && !S.Wallet.CanAfford(CitySystem.BuildCost(C, def))) continue;
                var spot = FindSpot(def);
                if (spot == null && UnlockNextArea()) spot = FindSpot(def);
                if (spot != null) Exec(new CityBuild { BlueprintId = b.Id, X = spot.Value.x, Y = spot.Value.y });
            }
            if (CitySystem.NextArea(S, C) is { } next && next.UnlockCost.All(c => Bal(c.Currency) >= c.Amount * 3)) UnlockNextArea();

            // Upgrades, cheapest first, each within a share of the balance.
            var cands = CitySystem.SortedBlueprints(S)
                .Where(b => CitySystem.IsActive(b) && b.TimerId == null && b.Level < CitySystem.MaxLevel(C, b))
                .Select(b => (b, cost: CitySystem.UpgradeCost(C, C.Blueprint(b.Id), b.Level)))
                .OrderBy(x => x.cost.Sum(c => c.Amount)).ToList();
            for (int i = 0; i < cands.Count; i++)
            {
                var (b, cost) = cands[i];
                // The cheapest upgrade may use the whole balance (the city is saved for); the rest only a share.
                if (!cost.All(c => c.Amount <= (Bal(c.Currency) - (c.Currency == Currencies.Ore ? _oreReserve : 0)) * (i == 0 ? 1.0 : CityShare))) continue;
                Exec(new CityUpgrade { BlueprintId = b.Id });
            }
            foreach (var b in CitySystem.SortedBlueprints(S))
                while (CitySystem.StarCost(C, b) > 0 && Bal(Currencies.BlueprintShards(b.Id)) >= CitySystem.StarCost(C, b) && Ok(new BlueprintStarUp { BlueprintId = b.Id })) { }
            SpeedUpTimers();

            Breeding();
            Aliens();
        }

        private bool UnlockNextArea()
        {
            var next = CitySystem.NextArea(S, C);
            if (next == null) return false;
            if (next.RequiresCounter != null && S.Counter(next.RequiresCounter) < next.RequiresCounterValue) return false;
            if (!S.Wallet.CanAfford(next.UnlockCost)) return false;
            return Ok(new CityUnlockArea { AreaId = next.Id });
        }

        private (int x, int y)? FindSpot(BlueprintDef def)
        {
            var t = C.CityTuning;
            for (int y = 0; y < t.GridHeight; y++)
                for (int x = 0; x < t.GridWidth; x++)
                    if (CitySystem.PlacementError(S, C, def, x, y) == null) return (x, y);
            return null;
        }

        /// <summary>Daily free speed-up on the longest timer, ad speed-ups (−15%, 3 per timer), Diamonds for rushers.</summary>
        private void SpeedUpTimers()
        {
            var timers = S.Timers.Active.Where(t => t.EndsMs > Now).OrderByDescending(t => t.EndsMs - Now).ToList();
            if (timers.Count == 0) return;
            if (TimerSystem.FreeSpeedUpsLeft(S, C, Now) > 0) Exec(new TimerSpeedUp { TimerId = timers[0].Id, Method = SpeedUpMethod.Free });
            foreach (var t in timers)
            {
                if (TimerSystem.Get(S, t.Id) == null || TimerSystem.Remaining(t, Now) <= 0) continue;
                if (Spend.RushesTimers)
                {
                    long cost = TimerSystem.DiamondCost(C, TimerSystem.Remaining(t, Now));
                    if (Spendable(cost) && Ok(new TimerSpeedUp { TimerId = t.Id, Method = SpeedUpMethod.Diamonds })) continue;
                }
                while (TimerSystem.Get(S, t.Id) != null && TimerSystem.AdsLeft(t, C) > 0 && TimerSystem.Remaining(t, Now) > 20 * Minute
                       && WillWatchAd(C.TimerTuning.AdPlacementId))
                {
                    if (!Ok(new TimerSpeedUp { TimerId = t.Id, Method = SpeedUpMethod.Ad, AdToken = AdToken })) break;
                    UseTime(AdCost);
                }
            }
        }

        private static double AlienPower(StatBlock s) => s.Value(StatId.Atk) * 2 + s.Value(StatId.Hp) * 0.2 + s.Value(StatId.Def);

        private void Aliens()
        {
            if (!Unlocked(Features.Aliens) || S.Aliens.Owned.Count == 0) return;
            var owned = S.Aliens.Owned.Values.OrderByDescending(a => AlienPower(AlienSystem.CombatStats(C, a))).ThenBy(a => a.TypeId, StringComparer.Ordinal).ToList();
            if (Unlocked(Features.AlienSquad))
            {
                int slots = CitySystem.SquadSlots(S, C);
                var want = owned.Take(slots).Select(a => a.TypeId).ToList();
                if (want.Count > 0 && !want.SequenceEqual(AlienSystem.ActiveSquad(S, C))) Exec(new AlienSetSquad { AlienTypeIds = want });
            }
            StaffAnnex();
            // Workers: best work power first into production buildings with free slots.
            foreach (var a in S.Aliens.Owned.Values.Where(a => string.IsNullOrEmpty(a.WorkBuildingId))
                         .OrderByDescending(a => AlienSystem.WorkPower(C, a)).ThenBy(a => a.TypeId, StringComparer.Ordinal).ToList())
            {
                var target = CitySystem.SortedBlueprints(S).FirstOrDefault(b =>
                {
                    var d = C.Blueprint(b.Id);
                    return CitySystem.IsActive(b) && d.Category == BuildingCategory.Production
                           && CitySystem.Workers(S, b.Id).Count < CitySystem.WorkerSlots(C, d, b);
                });
                if (target == null) break;
                Exec(new AlienAssign { AlienTypeId = a.TypeId, BlueprintId = target.Id });
            }
            foreach (var a in owned)
                while (AlienSystem.StarCost(C, a) > 0 && Bal(Currencies.AlienShards(a.TypeId)) >= AlienSystem.StarCost(C, a) && Ok(new AlienStarUp { AlienTypeId = a.TypeId })) { }
            // Levels: squad first, cheapest first, within a share of the Grain (breeding needs Grain too).
            var squad = AlienSystem.ActiveSquad(S, C);
            for (int guard = 0; guard < 400; guard++)
            {
                var pick = S.Aliens.Owned.Values.Where(a => a.Level < AlienSystem.MaxLevel(C, a))
                    .OrderBy(a => squad.Contains(a.TypeId) ? 0 : 1).ThenBy(a => AlienSystem.LevelCost(C, a)).ThenBy(a => a.TypeId, StringComparer.Ordinal).FirstOrDefault();
                if (pick == null || AlienSystem.LevelCost(C, pick) > Bal(Currencies.Grain) * 0.6) break;
                if (!Ok(new AlienLevelUp { AlienTypeId = pick.TypeId })) break;
            }
        }

        private long _breedWeek = -1, _breedWeekBase;
        private long _breedsThisWeek(long weekStart)
        {
            if (_breedWeek != weekStart) { _breedWeek = weekStart; _breedWeekBase = S.Counter("breeds_started"); }
            return S.Counter("breeds_started") - _breedWeekBase;
        }

        private static double GeneSum(AlienGenes g) => g.Hp + g.Atk + g.Def + g.Speed + g.Work;

        private void Breeding()
        {
            if (!Unlocked(Features.Breeding)) return;
            foreach (var job in S.Breeding.Jobs.ToList())
                if (TimerSystem.Get(S, job.TimerId) == null || TimerSystem.IsDone(S, job.TimerId, Now)) Exec(new BreedCollect { JobId = job.Id });
            foreach (var child in S.Breeding.Pending.ToList())
            {
                var cur = AlienSystem.Owned(S, child.TypeId);
                bool better = cur != null && GeneSum(child.Genes) + 10 * child.Traits.Count > GeneSum(cur.Genes) + 10 * cur.Traits.Count;
                Exec(new BreedResolve { ChildId = child.Id, Keep = better ? BreedKeep.Replace : BreedKeep.Discard });
            }
            var ranked = S.Aliens.Owned.Values.OrderByDescending(a => (int)C.AlienType(a.TypeId).Rarity).ThenByDescending(a => GeneSum(a.Genes))
                .ThenBy(a => a.TypeId, StringComparer.Ordinal).ToList();
            if (ranked.Count < 2) return;
            int slots = BreedingSystem.Slots(S, C);
            // Breed the best alien with the next best (better genes for the squad); if Grain is short, the cheapest pair.
            // Improve the best alien whose genes are not near the cap yet; once all are good, breed only for the quest.
            var pairs = new List<(OwnedAlien a, OwnedAlien b)>();
            var squadIds = AlienSystem.ActiveSquad(S, C);
            var target = ranked.Where(a => squadIds.Contains(a.TypeId)).FirstOrDefault(a => GeneSum(a.Genes) < 150);
            if (target != null)
                foreach (var other in ranked) if (other != target) { pairs.Add((target, other)); break; }
            var cheap = ranked.OrderBy(a => (int)C.AlienType(a.TypeId).Rarity).ThenBy(a => a.TypeId, StringComparer.Ordinal).Take(2).ToList();
            // Cheap pairs only for the weekly "breed 2" quest; Grain otherwise goes to alien levels.
            long weekStart = GameClock.WeekStartMs(C, GameClock.WeekIndex(C, Now));
            if (cheap.Count == 2 && _breedsThisWeek(weekStart) < 2) pairs.Add((cheap[0], cheap[1]));
            foreach (var (a, b) in pairs)
            {
                if (S.Breeding.Jobs.Count >= slots) break;
                long cost = BreedingSystem.GrainCost(C, a.TypeId, b.TypeId);
                bool best = a == target;
                if (cost > Bal(Currencies.Grain) * (best ? 1.0 : 0.5)) continue;
                Exec(new BreedStart { ParentA = a.TypeId, ParentB = b.TypeId });
            }
        }

        // ---- laboratory -----------------------------------------------------------------------------------------

        /// <summary>Research costs Data only (no Biomass/Ore reserve needed).</summary>
        private long LabReserve(string currency) => 0;

        /// <summary>Next node: lowest level first (spread), stat nodes before skill nodes (power), shallow rows first.</summary>
        private LabResearchDef NextResearch() => C.LabResearch.Values
            .Where(d => LaboratorySystem.BlockReason(S, C, d) == null)
            .OrderBy(d => d.Kind == LabNodeKind.Package ? 0 : 1) // a new synergy package first (signatures pass)
            .ThenBy(d => LaboratorySystem.Entry(S, d.Id).Level).ThenBy(d => d.Kind == LabNodeKind.Stat ? 0 : 1).ThenBy(d => d.Tier)
            .ThenBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault();

        private bool LabDataShort => Unlocked(Features.Laboratory) && LaboratorySystem.Running(S) < LaboratorySystem.Slots(S, C)
                                     && Bal(Currencies.Data) < LaboratorySystem.NextDataCost(S, C);

        private void Lab()
        {
            if (!Unlocked(Features.Laboratory)) return;
            foreach (var kv in S.Lab.Entries.OrderBy(k => k.Key, StringComparer.Ordinal).ToList())
                if (kv.Value.ResearchingLevel > 0 && Now >= LaboratorySystem.ReadyAtMs(S, kv.Key)) Exec(new LabCollect { ResearchId = kv.Key });
            for (int guard = 0; guard < 4 && LaboratorySystem.Running(S) < LaboratorySystem.Slots(S, C); guard++)
            {
                var next = NextResearch();
                if (next == null || Bal(Currencies.Data) < LaboratorySystem.NextDataCost(S, C)) return;
                if (!Ok(new LabResearch { ResearchId = next.Id })) return;
            }
        }

        /// <summary>Staffs a Research Annex with two qualifying aliens (non-squad first, weakest workers first).</summary>
        private void StaffAnnex()
        {
            foreach (var b in CitySystem.SortedBlueprints(S))
            {
                var def = C.Blueprint(b.Id);
                if (!CitySystem.IsActive(b) || def == null) continue;
                foreach (var boon in def.Boons.Where(x => x.RequiredWorkers > 0))
                {
                    if (CitySystem.BoonStaffed(S, def, b, boon)) continue;
                    foreach (var w in CitySystem.Workers(S, b.Id).Where(a => a.Stars < boon.RequiredWorkerStars).ToList())
                        Exec(new AlienAssign { AlienTypeId = w.TypeId, BlueprintId = null });
                    var squad = AlienSystem.ActiveSquad(S, C);
                    var picks = S.Aliens.Owned.Values.Where(a => a.Stars >= boon.RequiredWorkerStars && a.WorkBuildingId != b.Id)
                        .OrderBy(a => squad.Contains(a.TypeId) ? 1 : 0).ThenBy(a => AlienSystem.WorkPower(C, a)).ThenBy(a => a.TypeId, StringComparer.Ordinal).ToList();
                    foreach (var a in picks)
                    {
                        if (CitySystem.Workers(S, b.Id).Count >= CitySystem.WorkerSlots(C, def, b)) break;
                        Exec(new AlienAssign { AlienTypeId = a.TypeId, BlueprintId = b.Id });
                    }
                }
            }
        }

        // ---- gacha ------------------------------------------------------------------------------------------------

        private static readonly GachaType[] PullTypes = { GachaType.Alien, GachaType.Part, GachaType.Blueprint };

        private void Gacha()
        {
            // Diamond shop: daily token packs are the cheapest tokens (40–50 Diamonds per pull).
            foreach (var id in new[] { "daily_alien_tokens", "daily_part_tokens", "daily_blueprint_tokens" })
            {
                var item = C.ShopItem(id);
                if (item == null) continue;
                GachaType t = id.Contains("alien") ? GachaType.Alien : id.Contains("part") ? GachaType.Part : GachaType.Blueprint;
                if (!Unlocked(GachaSystem.FeatureFor(t))) continue;
                while (Spendable(item.Price[0].Amount + (Spend.IsSpender ? 0 : 400)) && Ok(new ShopBuyItem { ItemId = id })) { }
            }
            if (Spend.MonthlyBudgetCents > 0)
                foreach (var id in new[] { "tokens_alien_10", "tokens_part_10", "tokens_blueprint_10" })
                {
                    GachaType t = id.Contains("alien") ? GachaType.Alien : id.Contains("part") ? GachaType.Part : GachaType.Blueprint;
                    if (!Unlocked(GachaSystem.FeatureFor(t)) || C.ShopItem(id) == null) continue;
                    while (Spendable(C.ShopItem(id).Price[0].Amount + 3000) && Ok(new ShopBuyItem { ItemId = id })) { }
                }

            foreach (var t in PullTypes)
            {
                if (!Unlocked(GachaSystem.FeatureFor(t))) continue;
                var def = C.GachaConfig.Types[t];
                string banner = GachaSystem.ActiveBanner(C, t, Now)?.Id;
                if (WillWatchAd(def.AdPlacementId) && Ok(new GachaPull { PullType = t, Count = 1, Payment = GachaPayment.Ad, AdToken = AdToken, BannerId = banner }))
                    UseTime(AdCost + Timing.PullCommand);
                while (Bal(def.TokenCurrency) >= def.TokenCost10 && Ok(new GachaPull { PullType = t, Count = 10, Payment = GachaPayment.Tokens, BannerId = banner }))
                    UseTime(Timing.PullCommand);
                while (Bal(def.TokenCurrency) >= def.TokenCost1 && Ok(new GachaPull { PullType = t, Count = 1, Payment = GachaPayment.Tokens, BannerId = banner }))
                    UseTime(Timing.PullCommand);
            }

            // Diamond 10-pulls with what is left above the reserve (F2P keeps a larger cushion for refills/forge).
            long cushion = Spend.MonthlyBudgetCents > 0 ? 0 : Spend.IsSpender ? 600 : 1200;
            for (int guard = 0; guard < 50; guard++)
            {
                GachaType t = PickDiamondPullType();
                var def = C.GachaConfig.Types[t];
                if (!Unlocked(GachaSystem.FeatureFor(t)) || !Spendable(def.DiamondCost10 + cushion)) break;
                if (!Ok(new GachaPull { PullType = t, Count = 10, Payment = GachaPayment.Diamonds, BannerId = GachaSystem.ActiveBanner(C, t, Now)?.Id })) break;
                UseTime(Timing.PullCommand);
            }
        }

        /// <summary>Whales chase the target hero's banner when it is up; otherwise the least-pulled unlocked type.</summary>
        private GachaType PickDiamondPullType()
        {
            if (Spend.MonthlyBudgetCents > 0)
            {
                var ab = GachaSystem.ActiveBanner(C, GachaType.Alien, Now);
                if (ab != null && ab.Theme.FeaturedId == TargetHero && Unlocked(Features.AlienPulls)) return GachaType.Alien;
                var mb = PullTypes.Select(t => GachaSystem.ActiveBanner(C, t, Now)).FirstOrDefault(b => b != null && b.Theme.FeaturedKind == RewardKinds.Mount);
                if (mb != null && Unlocked(GachaSystem.FeatureFor(mb.Type))) return mb.Type;
            }
            return PullTypes.Where(t => Unlocked(GachaSystem.FeatureFor(t)))
                .OrderBy(t => S.Counter("pulls_" + t.ToString().ToLowerInvariant())).FirstOrDefault();
        }
    }
}
