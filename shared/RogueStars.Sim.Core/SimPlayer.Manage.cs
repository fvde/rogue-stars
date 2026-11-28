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

        // ---- city (City redesign: land, streets, copies, aliens as the only workers) -------------------------------------

        /// <summary>
        /// Build order of blueprints (lower first): the squad and research buildings, then Biomass / Ore producers and the pull
        /// buildings, then the rest. Copies of one blueprint follow each other.
        /// </summary>
        private static readonly string[] BuildOrder =
        {
            ContentDb.LaunchPodId, "bp_biomass_farm", ContentDb.LaboratoryId, "bp_ore_mine", "bp_gear_works", "bp_token_relay",
            "bp_signal_beacon", "bp_part_fabricator", "bp_diamond_mine", "bp_stasis_beacon", "bp_command_spire", "bp_fuel_refinery",
            "bp_stamina_well", "bp_supply_depot", "bp_war_academy", "bp_training_grounds", "bp_scout_post", "bp_grain_farm", "bp_breeding_pen",
            ContentDb.ShardReactorId, "bp_regen_spire", "bp_observatory", "bp_barracks_dome", "bp_gene_lab",
        };

        private int Rank(string bp) { int i = Array.IndexOf(BuildOrder, bp); return i < 0 ? BuildOrder.Length : i; }

        /// <summary>
        /// Share of income the bot puts aside for digging (City design §4: "a dig eats about 30% of all Biomass earned since the last
        /// one, the last five 40–45%"): 35%, 45% for digs 16–20, 75% of Gear Cores. The rest goes to the worm, parts and buildings.
        /// </summary>
        private const double DigSavingShare = 0.35, LateDigSavingShare = 0.45, GearCoreSavingShare = 0.75;

        /// <summary>Earned totals (SimPlayer.Earned) when the last dig started: the dig pot fills from what was earned since.</summary>
        private readonly Dictionary<string, double> _earnedAtDig = new Dictionary<string, double>();

        private double EarnedTotal(string currency) { int i = Array.IndexOf(Tracked, currency); return i < 0 ? 0 : Earned[i]; }

        /// <summary>
        /// Held back for the next dig: the dig pot, DigSavingShare of what was earned since the last dig started, up to the dig's cost
        /// (worm and parts spend above it).
        /// </summary>
        private long DigReserve(string currency)
        {
            if (S.City.DigPatchId != null || NextDigPatch() == null) return 0;
            foreach (var c in CitySystem.DigCost(C, CitySystem.NextDigNumber(S)))
                if (c.Currency == currency)
                {
                    double since = EarnedTotal(currency) - (_earnedAtDig.TryGetValue(currency, out var e) ? e : 0);
                    // Gear Cores have one other sink (part levels) and digs from #10 on need a lot of them: a bigger share.
                    double share = currency == Currencies.GearCores ? GearCoreSavingShare : CitySystem.NextDigNumber(S) > 15 ? LateDigSavingShare : DigSavingShare;
                    return Math.Min(c.Amount, (long)(since * share));
                }
            return 0;
        }

        /// <summary>The patch the bot digs next: the diggable patch touching the most of its land (compact growth), content order on ties.</summary>
        private CityPatchDef NextDigPatch()
        {
            if (!Unlocked(Features.BiomassFarm)) return null;
            CityPatchDef best = null; int bestTouch = -1;
            foreach (var p in C.CityPatches)
            {
                if (CitySystem.DigBlockReason(S, C, p.Id) != null) continue;
                int touch = p.Cells.Count(cell => Neighbours(cell).Any(n => CityLayout.IsLand(S, C, n)));
                if (touch > bestTouch) { best = p; bestTouch = touch; }
            }
            return best;
        }

        private IEnumerable<int> Neighbours(int cell)
        {
            int w = C.CityTuning.GridWidth, x = cell % w, y = cell / w;
            if (x > 0) yield return cell - 1;
            if (x < w - 1) yield return cell + 1;
            if (y > 0) yield return cell - w;
            if (y < C.CityTuning.GridHeight - 1) yield return cell + w;
        }

        /// <summary>Layout revision and blueprints the planner found no spot for (retried only after the layout changes).</summary>
        private int _noSpotRev = -1;
        private readonly HashSet<string> _noSpot = new HashSet<string>();

        private void City()
        {
            UseTime(Timing.City);
            Exec(new CityCollect());
            if (S.City.PriorityCurrency != Currencies.Biomass) Exec(new CitySetPriority { Currency = Currencies.Biomass });
            if (S.City.Buildings.Any(b => CitySystem.ChancesReady(b) >= 1)) Exec(new CityPop());
            SpeedUpTimers();

            // Dig as soon as the next dig is affordable (saved for: DigReserve).
            var patch = S.City.DigPatchId == null ? NextDigPatch() : null;
            if (patch != null && CitySystem.DigCost(C, CitySystem.NextDigNumber(S)).All(c => c.Amount <= Bal(c.Currency) - BuildReserve(c.Currency))
                && Ok(new CityDig { PatchId = patch.Id }))
                foreach (var cur in new[] { Currencies.Biomass, Currencies.Ore, Currencies.GearCores }) _earnedAtDig[cur] = EarnedTotal(cur);

            PlaceBuildings();

            // Upgrades, cheapest first, each within a share of the balance above the dig reserve.
            var cands = S.City.Buildings
                .Where(b => Unlocked(Features.BiomassFarm) && CitySystem.IsBuilt(b) && b.TimerId == null && b.Level < CitySystem.MaxLevel(C))
                .Select(b => (b, cost: CitySystem.UpgradeCost(C, C.Blueprint(b.BlueprintId), b.Level)))
                .OrderBy(x => x.cost.Sum(c => c.Amount)).ThenBy(x => x.b.Uid, StringComparer.Ordinal).ToList();
            for (int i = 0; i < cands.Count; i++)
            {
                var (b, cost) = cands[i];
                if (!cost.All(c => c.Amount <= (Bal(c.Currency) - DigReserve(c.Currency) - BuildReserve(c.Currency) - (c.Currency == Currencies.Ore ? _oreReserve : 0)) * (i == 0 ? 1.0 : CityShare))) continue;
                Exec(new CityUpgrade { Uid = b.Uid });
            }
            if (Unlocked(Features.BiomassFarm)) // camp mode (levels 6-14): no star-ups
            foreach (var b in CitySystem.SortedBlueprints(S))
                while (CitySystem.StarCost(C, b) > 0 && Bal(Currencies.BlueprintShards(b.Id)) >= CitySystem.StarCost(C, b) && Ok(new BlueprintStarUp { BlueprintId = b.Id })) { }
            SpeedUpTimers();

            Breeding();
            Aliens();
            Reactor();
        }

        /// <summary>
        /// Places stored copies, then builds new copies (up to the rarity cap) in BuildOrder, at the planner's best spot (street spine
        /// grown as needed, best placement bonus). A copy is only built when an alien could run it (built copies ≤ aliens + 2).
        /// </summary>
        private HashSet<int> _spine;

        private void PlaceBuildings()
        {
            if (!Unlocked(Features.BiomassFarm)) return;
            // Street spine: pave every spine tile the network reaches (free), keep the rest of the spine clear of buildings.
            _spine ??= CityPlanner.SpineCells(C);
            var pave = CityPlanner.PaveableSpine(S, C, _spine);
            if (pave.Count > 0) Exec(new CityStreets { Adds = pave });
            if (_noSpotRev != S.City.LayoutRev) { _noSpot.Clear(); _noSpotRev = S.City.LayoutRev; }
            _buildReserve = new List<Cost>();
            // A building only runs with an alien: build while there are aliens to run it (squad aliens excluded).
            int workers = S.Aliens.Owned.Count - AlienSystem.ActiveSquad(S, C).Count;
            int Running() => S.City.Buildings.Count(b => b.Placed && b.BlueprintId != ContentDb.LaunchPodId);
            foreach (var owned in CitySystem.SortedBlueprints(S).OrderBy(o => Rank(o.Id)).ThenBy(o => o.Id, StringComparer.Ordinal).ToList())
            {
                var def = C.Blueprint(owned.Id);
                if (def == null || !Useful(def)) continue;
                bool key = def.Id == ContentDb.LaunchPodId || def.Id == ContentDb.LaboratoryId || def.Pull != null || def.Id == "bp_command_spire";
                // Stored copies first (free), then new ones.
                foreach (var stored in S.City.Buildings.Where(b => !b.Placed && b.BlueprintId == def.Id).OrderBy(b => b.Uid, StringComparer.Ordinal).ToList())
                {
                    if (def.Id != ContentDb.LaunchPodId && Running() >= workers && !StoreWorst(Rank(def.Id), new List<Cost>())) return;
                    if (!TryPlace(def.Id, stored.Uid) && !(key && MakeRoom(def.Id, stored.Uid))) break;
                }
                while (CitySystem.CopiesBuilt(S, def.Id) < def.Copies)
                {
                    var cost = CitySystem.BuildCost(C, def);
                    // Save up for the most important unaffordable building (worm, parts and lesser buildings spend above it);
                    // new buildings come before the dig savings: land without buildings is worth nothing.
                    if (!cost.All(c => c.Amount <= Bal(c.Currency) - BuildReserve(c.Currency)))
                    {
                        if (_buildReserve.Count == 0 && (key || (!_noSpot.Contains(def.Id) && CityPlanner.Find(S, C, def.Id, null, 2, _spine) != null))) _buildReserve = cost;
                        break;
                    }
                    // Out of aliens: swap the least important placed building (stored, level kept) for this better one.
                    if (def.Id != ContentDb.LaunchPodId && Running() >= workers && !StoreWorst(Rank(def.Id), cost)) return;
                    // No spot: a key building makes room (stores lesser ones), others wait for land; try the next blueprint.
                    if (!TryPlace(def.Id, null) && !(key && MakeRoom(def.Id, null))) break;
                }
            }
        }

        /// <summary>
        /// Stores the placed building ranked lowest in BuildOrder (if it ranks below <paramref name="rank"/>) so a better one can
        /// take its alien and land; only when the better one is affordable. True if one was stored.
        /// </summary>
        private bool StoreWorst(int rank, List<Cost> cost)
        {
            if (!S.Wallet.CanAfford(cost)) return false;
            var worst = S.City.Buildings.Where(b => b.Placed && CitySystem.IsBuilt(b) && b.TimerId == null && Rank(b.BlueprintId) > rank)
                .OrderByDescending(b => Rank(b.BlueprintId)).ThenBy(b => b.Level).ThenBy(b => b.Uid, StringComparer.Ordinal).FirstOrDefault();
            return worst != null && Ok(new CityStore { Uid = worst.Uid });
        }

        /// <summary>
        /// Makes room for a key building: finds the footprint next to the street network that only covers free land or lesser
        /// (lower-ranked, idle) buildings, the cheapest to clear (fewest and least important), stores those and places it there.
        /// </summary>
        private bool MakeRoom(string blueprintId, string storedUid)
        {
            var def = C.Blueprint(blueprintId);
            int rank = Rank(blueprintId);
            var g = CityLayout.Grid(S, C);
            var linked = CityLayout.LinkedStreets(g);
            List<CityGridItem> best = null; int bx = 0, by = 0; bool brot = false; double bestCost = double.MaxValue;
            for (int rot = 0; rot < (def.Width == def.Height ? 1 : 2); rot++)
            {
                CityLayout.Footprint(def, rot == 1, out int w, out int h);
                for (int y = 0; y + h <= g.H; y++)
                    for (int x = 0; x + w <= g.W; x++)
                    {
                        var victims = new List<CityGridItem>();
                        bool ok = true, touches = false;
                        for (int yy = y; yy < y + h && ok; yy++)
                            for (int xx = x; xx < x + w && ok; xx++)
                            {
                                int cell = g.Cell(xx, yy);
                                if (!g.Land[cell] || g.Plaza[cell] || g.Feature[cell] || g.Street[cell] || _spine.Contains(cell)) { ok = false; break; }
                                if (g.Owner[cell] >= 0)
                                {
                                    var it = g.Items[g.Owner[cell]];
                                    if (Rank(it.Def.Id) <= rank || it.Building == null || !CitySystem.IsBuilt(it.Building) || it.Building.TimerId != null) { ok = false; break; }
                                    if (!victims.Contains(it)) victims.Add(it);
                                }
                                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                                {
                                    int nx = xx + dx, ny = yy + dy;
                                    if (nx >= x && nx < x + w && ny >= y && ny < y + h) continue;
                                    if (g.In(nx, ny) && linked[g.Cell(nx, ny)]) touches = true;
                                }
                            }
                        if (!ok || !touches || victims.Count == 0) continue;
                        double cost = victims.Sum(v => 100 - Rank(v.Def.Id) + v.Building.Level);
                        if (cost < bestCost) { bestCost = cost; best = victims; bx = x; by = y; brot = rot == 1; }
                    }
            }
            if (best == null) return false;
            var undo = new List<CityGridItem>();
            foreach (var v in best) { if (!Ok(new CityStore { Uid = v.Uid })) break; undo.Add(v); }
            if (undo.Count == best.Count && Ok(new CityPlace { BlueprintId = blueprintId, Uid = storedUid, X = bx, Y = by, Rotated = brot })) return true;
            foreach (var v in undo) Exec(new CityPlace { BlueprintId = v.Def.Id, Uid = v.Uid, X = v.X, Y = v.Y, Rotated = v.Building.Rotated });
            return false;
        }

        /// <summary>Buildings whose output has a use now (Grain and the Breeding Pen wait for Breeding).</summary>
        private bool Useful(BlueprintDef def)
        {
            if (def.Id == "bp_breeding_pen" || def.Id == "bp_gene_lab") return Unlocked(Features.Breeding);
            if (def.ProduceCurrency != null) return RewardGate.CurrencyHasUse(S, C, def.ProduceCurrency, Now) && (def.ProduceCurrency != Currencies.Grain || Unlocked(Features.Breeding));
            return true;
        }

        /// <summary>Cost of the next important building the bot saves up for (set by PlaceBuildings).</summary>
        private List<Cost> _buildReserve = new List<Cost>();

        private long BuildReserve(string currency) => _buildReserve.Where(c => c.Currency == currency).Sum(c => c.Amount);

        private bool TryPlace(string blueprintId, string storedUid)
        {
            if (_noSpot.Contains(blueprintId)) return false;
            var plan = CityPlanner.Find(S, C, blueprintId, storedUid, 2, _spine);
            if (plan == null) { _noSpot.Add(blueprintId); return false; }
            if (plan.Streets.Count > 0 && !Ok(new CityStreets { Adds = new List<int>(plan.Streets) })) { _noSpot.Add(blueprintId); return false; }
            return Ok(new CityPlace { BlueprintId = blueprintId, Uid = storedUid, X = plan.X, Y = plan.Y, Rotated = plan.Rotated });
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

        /// <summary>Squad pick (aliens pass: no stats): rarity first, then stars, then how often it acts.</summary>
        private double AlienPower(OwnedAlien a) =>
            (int)C.AlienType(a.TypeId).Rarity * 100 + a.Stars * 10 - AlienSystem.ActsEvery(C, a);

        /// <summary>
        /// From the Aliens unlock (level 6) the rules build the Launch Pod on the crash site (CitySystem.EnsureCrashSitePod): the squad
        /// below is set from then on, before the City; the pod is neither upgraded nor starred before the City (camp mode).
        /// </summary>
        private void Aliens()
        {
            if (!Unlocked(Features.Aliens) || S.Aliens.Owned.Count == 0) return;
            var owned = S.Aliens.Owned.Values.OrderByDescending(AlienPower).ThenBy(a => a.TypeId, StringComparer.Ordinal).ToList();
            if (Unlocked(Features.AlienSquad) && CitySystem.SquadSlots(S, C) > 0)
            {
                int slots = CitySystem.SquadSlots(S, C);
                var want = owned.Take(slots).Select(a => a.TypeId).ToList();
                if (want.Count > 0 && !want.SequenceEqual(AlienSystem.ActiveSquad(S, C))) Exec(new AlienSetSquad { AlienTypeIds = want });
            }
            StaffBuildings();
            foreach (var a in owned)
                while (AlienSystem.StarCost(C, a) > 0 && Bal(Currencies.AlienShards(a.TypeId)) >= AlienSystem.StarCost(C, a) && Ok(new AlienStarUp { AlienTypeId = a.TypeId })) { }
            // (aliens pass) no alien levels: Grain goes to breeding only
        }

        /// <summary>
        /// Aliens are the only workers: every built building gets one alien (BuildOrder first; the Reactor up to its slots), then
        /// spare aliens add their work bonus to Biomass, then other producers. Squad aliens (Launch Pod) stay in the squad.
        /// </summary>
        private void StaffBuildings()
        {
            var pod = CitySystem.LaunchPod(S);
            var buildings = S.City.Buildings.Where(b => CitySystem.IsBuilt(b) && b.BlueprintId != ContentDb.LaunchPodId)
                .OrderBy(b => Rank(b.BlueprintId)).ThenBy(b => b.Uid, StringComparer.Ordinal).ToList();
            List<OwnedAlien> Idle() => S.Aliens.Owned.Values.Where(a => string.IsNullOrEmpty(a.WorkBuildingId))
                .OrderBy(a => AlienSystem.WorkPower(C, a)).ThenBy(a => a.TypeId, StringComparer.Ordinal).ToList();
            // 1) one alien per building; take it from a building with two or more if nobody is idle
            foreach (var b in buildings)
            {
                if (CitySystem.Workers(S, b.Uid).Count > 0) continue;
                var a = Idle().FirstOrDefault()
                        ?? buildings.Where(o => CitySystem.Workers(S, o.Uid).Count > 1).SelectMany(o => CitySystem.Workers(S, o.Uid)).FirstOrDefault();
                if (a == null) break;
                Exec(new AlienAssign { AlienTypeId = a.TypeId, BuildingUid = b.Uid });
            }
            // 2) the Reactor wants every slot (one job per alien)
            var reactor = ShardReactorSystem.Building(S, C);
            if (CitySystem.IsBuilt(reactor))
                foreach (var a in Idle())
                {
                    if (CitySystem.Workers(S, reactor.Uid).Count >= ShardReactorSystem.WorkerSlots(S, C)) break;
                    Exec(new AlienAssign { AlienTypeId = a.TypeId, BuildingUid = reactor.Uid });
                }
            // 3) spare aliens: best workers into Biomass, then Ore, then other producers
            foreach (var a in Idle().OrderByDescending(x => AlienSystem.WorkPower(C, x)).ThenBy(x => x.TypeId, StringComparer.Ordinal))
            {
                var target = buildings.Where(b => C.Blueprint(b.BlueprintId).ProduceCurrency != null && CitySystem.Workers(S, b.Uid).Count < CitySystem.WorkerSlots(S, C, b))
                    .OrderBy(b => C.Blueprint(b.BlueprintId).ProduceCurrency == Currencies.Biomass ? 0 : C.Blueprint(b.BlueprintId).ProduceCurrency == Currencies.Ore ? 1 : 2)
                    .ThenBy(b => b.Uid, StringComparer.Ordinal).FirstOrDefault();
                if (target == null) break;
                Exec(new AlienAssign { AlienTypeId = a.TypeId, BuildingUid = target.Uid });
            }
        }

        private long _breedWeek = -1, _breedWeekBase;
        private long _breedsThisWeek(long weekStart)
        {
            if (_breedWeek != weekStart) { _breedWeek = weekStart; _breedWeekBase = S.Counter("breeds_started"); }
            return S.Counter("breeds_started") - _breedWeekBase;
        }

        /// <summary>Breeding value of traits (aliens pass: genes are inactive, only the species' combat traits matter).</summary>
        private int TraitScore(List<string> traits) => traits.Count(t => C.AlienTrait(t)?.Kind == AlienTraitKind.Effect || C.AlienTrait(t)?.Kind == AlienTraitKind.Tempo);

        private void Breeding()
        {
            if (!Unlocked(Features.Breeding)) return;
            foreach (var job in S.Breeding.Jobs.ToList())
                if (TimerSystem.Get(S, job.TimerId) == null || TimerSystem.IsDone(S, job.TimerId, Now)) Exec(new BreedCollect { JobId = job.Id });
            foreach (var child in S.Breeding.Pending.ToList())
            {
                var cur = AlienSystem.Owned(S, child.TypeId);
                // aliens pass: genes do nothing yet, so a child is kept only for better traits; otherwise it becomes shards of its species
                bool better = cur != null && TraitScore(child.Traits) > TraitScore(cur.Traits);
                Exec(new BreedResolve { ChildId = child.Id, Keep = better ? BreedKeep.Replace : BreedKeep.Discard });
            }
            var ranked = S.Aliens.Owned.Values.OrderByDescending(a => (int)C.AlienType(a.TypeId).Rarity).ThenBy(a => a.Stars)
                .ThenBy(a => a.TypeId, StringComparer.Ordinal).ToList();
            if (ranked.Count < 2) return;
            int slots = BreedingSystem.Slots(S, C);
            // Aliens pass: breeding is the targeted shard source and Grain's only sink: every free slot pairs the best squad alien
            // that is not 10★ yet with the cheapest other alien (half the children are its species → shards).
            var pairs = new List<(OwnedAlien a, OwnedAlien b)>();
            var squadIds = AlienSystem.ActiveSquad(S, C);
            var target = ranked.Where(a => squadIds.Contains(a.TypeId)).FirstOrDefault(a => a.Stars < AlienSystem.MaxStars);
            var cheap = ranked.OrderBy(a => (int)C.AlienType(a.TypeId).Rarity).ThenBy(a => a.TypeId, StringComparer.Ordinal).Take(2).ToList();
            if (target != null)
            {
                var partner = cheap.FirstOrDefault(o => o != target) ?? ranked.First(o => o != target);
                for (int n = 0; n < slots; n++) pairs.Add((target, partner));
            }
            // Cheap pairs for the weekly "breed 2" quest.
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

        /// <summary>
        /// Held back from worm / part spending: the next dig's cost and the next key building's (research costs Data only). The worm is
        /// never starved for long: after <see cref="WormPatienceDays"/> without a worm upgrade it may take one upgrade out of the savings.
        /// </summary>
        private long LabReserve(string currency) => DigReserve(currency) + BuildReserve(currency);

        private const int WormPatienceDays = 5;
        private long _lastWormUpgradeMs;

        /// <summary>Next node: lowest level first (spread), stat nodes before skill nodes (power), shallow rows first.</summary>
        private LabResearchDef NextResearch() => C.LabResearch.Values
            .Where(d => LaboratorySystem.BlockReason(S, C, d) == null)
            .OrderBy(d => LaboratorySystem.Entry(S, d.Id).Level).ThenBy(d => d.Kind == LabNodeKind.Stat ? 0 : 1).ThenBy(d => d.Tier)
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
                while (S.Gacha.FreePulls(t) > 0 && Ok(new GachaPull { PullType = t, Count = 10, Payment = GachaPayment.Free, BannerId = banner }))
                    UseTime(Timing.PullCommand); // the unlock's free 10×
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

        /// <summary>The least-pulled unlocked type (heroes and mounts come from summon events: SimPlayer.Summons).</summary>
        private GachaType PickDiamondPullType()
        {
            return PullTypes.Where(t => Unlocked(GachaSystem.FeatureFor(t)))
                .OrderBy(t => S.Counter("pulls_" + t.ToString().ToLowerInvariant())).FirstOrDefault();
        }
    }
}
