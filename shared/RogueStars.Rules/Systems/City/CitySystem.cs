using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Stamina contribution of city buildings (cap bonus and tick speed-up percent).</summary>
    [Serializable]
    public sealed class CityStaminaBonus
    {
        public long CapBonus;
        public double TickSpeedPercent;
    }

    /// <summary>One popped chance: building, token currency and pulls granted.</summary>
    [Serializable]
    public sealed class CityPopResult
    {
        public string Uid;
        public string Currency;
        public int Pulls;
    }

    /// <summary>
    /// City (docs: City design, owner-approved): land you dig, streets you paint, buildings that want good neighbours.
    /// <list type="bullet">
    /// <item>Blueprints (stars 1–10, shared by every copy) and their buildings (copies by rarity: C3 R3 E2 L2 M1, Launch Pod 1). Each
    /// copy has its own level 1–10, timers, storage and workers.</item>
    /// <item>Value of a building = placed value × (1 + level ladder + star ladder); each ladder +10% per step and +15% at 5 and 10
    /// (3× at max). The placement skill (<see cref="CityLayout"/>) multiplies the building's main number (cap +30%).</item>
    /// <item>A building runs (produces, gives its boon) only while placed, built and staffed by at least one alien. The Launch Pod's
    /// aliens are the run squad (its worker slots are the squad slots).</item>
    /// <item>Layout edits are one command each (place / move / store / streets / dig), validated by <see cref="CityLayout.Validate"/>.</item>
    /// </list>
    /// Production per hour = BaseRatePerHour × growth × (1 + placement%) × (1 + (Command Spire% + worker bonus%)/100) × priority
    /// × ModifierMath.Multiplier("production:&lt;currency&gt;") (integrated piecewise over modifier windows). Storage = StorageHours ×
    /// BaseRatePerHour × growth. Pull buildings make chances instead (<see cref="ChancesPerDay"/>), popped with CityPop.
    /// </summary>
    public static class CitySystem
    {
        public const string TimerBuild = "city_build";
        public const string TimerUpgrade = "city_upgrade";
        public const string TimerDig = "city_dig";
        public const int MaxStars = 10;
        /// <summary>CityState.Version of the redesigned city.</summary>
        public const int LayoutVersion = 1;
        private const double HourMs = 3_600_000.0;
        private const double DayMs = 24 * HourMs;

        public static void Register(GameEngine e)
        {
            e.Register<CityPlace>(HandlePlace);
            e.Register<CityMove>(HandleMove);
            e.Register<CityStore>(HandleStore);
            e.Register<CityStreets>(HandleStreets);
            e.Register<CityDig>(HandleDig);
            e.Register<CityUpgrade>(HandleUpgrade);
            e.Register<CityCollect>(HandleCollect);
            e.Register<CityPop>(HandlePop);
            e.Register<CitySetPriority>(HandleSetPriority);
            e.Register<BlueprintStarUp>(HandleStarUp);
            e.RegisterNewPlayer(InitNewPlayer);
            e.RegisterSettler(Settle);
            e.RegisterLoadoutContributor(ContributeLoadout);
            e.Rewards.Register(RewardKinds.Blueprint, GrantBlueprint);
            TimerSystem.RegisterCompletion(e, TimerBuild, CompleteBuild);
            TimerSystem.RegisterCompletion(e, TimerUpgrade, CompleteUpgrade);
            TimerSystem.RegisterCompletion(e, TimerDig, CompleteDig);
            // Stamina cap/regen boons feed Progression's regen engine (EconomySystem is registered before City).
            EconomySystem.RegisterRegenBonusProvider(e, (s, c, now, currency) =>
            {
                if (currency != Currencies.Stamina) return (0L, 0.0);
                var b = StaminaBonus(s, c, now);
                return (b.CapBonus, b.TickSpeedPercent);
            });
        }

        // ---- blueprints and buildings ---------------------------------------------------------------

        /// <summary>Owned blueprints in deterministic (ordinal id) order.</summary>
        public static List<OwnedBlueprint> SortedBlueprints(GameState state)
        {
            var list = new List<OwnedBlueprint>(state.City.Blueprints.Values);
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        public static OwnedBlueprint Owned(GameState state, string blueprintId) =>
            blueprintId != null && state.City.Blueprints.TryGetValue(blueprintId, out var b) ? b : null;

        public static int Stars(GameState state, string blueprintId) => Owned(state, blueprintId)?.Stars ?? 1;

        public static CityBuilding Building(GameState state, string uid)
        {
            if (uid == null) return null;
            foreach (var b in state.City.Buildings) if (b.Uid == uid) return b;
            return null;
        }

        /// <summary>Copies of a blueprint (build order).</summary>
        public static List<CityBuilding> Copies(GameState state, string blueprintId)
        {
            var list = new List<CityBuilding>();
            foreach (var b in state.City.Buildings) if (b.BlueprintId == blueprintId) list.Add(b);
            return list;
        }

        /// <summary>Copies built (placed, stored or under construction).</summary>
        public static int CopiesBuilt(GameState state, string blueprintId)
        {
            int n = 0;
            foreach (var b in state.City.Buildings) if (b.BlueprintId == blueprintId) n++;
            return n;
        }

        /// <summary>The Launch Pod (null until built).</summary>
        public static CityBuilding LaunchPod(GameState state)
        {
            foreach (var b in state.City.Buildings) if (b.BlueprintId == ContentDb.LaunchPodId) return b;
            return null;
        }

        /// <summary>The first placed Laboratory copy (research is locked until one is placed and built), or null.</summary>
        public static CityBuilding Laboratory(GameState state)
        {
            foreach (var b in state.City.Buildings) if (b.BlueprintId == ContentDb.LaboratoryId && b.Placed && b.Level >= 1) return b;
            return null;
        }

        /// <summary>Placed and constructed (level ≥ 1).</summary>
        public static bool IsBuilt(CityBuilding b) => b != null && b.Placed && b.Level >= 1;

        /// <summary>
        /// A building runs (produces / gives its boon) when placed, constructed and staffed by at least one alien. The Launch Pod runs
        /// when built (its aliens are the squad).
        /// </summary>
        public static bool IsActive(GameState state, ContentDb content, CityBuilding b)
        {
            if (!IsBuilt(b)) return false;
            if (b.BlueprintId == ContentDb.LaunchPodId) return true;
            foreach (var a in state.Aliens.Owned.Values) if (a.WorkBuildingId == b.Uid) return true;
            return false;
        }

        /// <summary>Next free uid "&lt;blueprintId&gt;#n" (n = 1..copies), or null when every copy is built.</summary>
        public static string NextUid(GameState state, BlueprintDef def)
        {
            for (int n = 1; n <= def.Copies; n++)
            {
                string uid = def.Id + "#" + n;
                if (Building(state, uid) == null) return uid;
            }
            return null;
        }

        // ---- growth and costs -----------------------------------------------------------------------

        /// <summary>Ladder bonus (fraction) at step n (1..10): +10% per step, +15% at 5 and 10. Ladder(1) = 0, Ladder(10) = 1.</summary>
        public static double Ladder(ContentDb content, int n)
        {
            var t = content.CityTuning;
            double v = 0;
            for (int i = 2; i <= Math.Min(n, 10); i++) v += Array.IndexOf(t.LadderMilestones, i) >= 0 ? t.LadderMilestonePct : t.LadderStepPct;
            return v / 100.0;
        }

        /// <summary>Growth multiplier of a boon: 1 + level ladder + star ladder (each only if the boon grows with it).</summary>
        public static double Growth(ContentDb content, BuildingBoon boon, int level, int stars) =>
            1 + (boon == null || boon.GrowsWithLevel ? Ladder(content, level) : 0) + (boon == null || boon.GrowsWithStars ? Ladder(content, stars) : 0);

        public static int MaxLevel(ContentDb content) => content.CityTuning.MaxLevel;

        /// <summary>Rounds a cost to clean numbers: under 100 to a multiple of 5, from 100 on to two significant digits (2 700, 8 400).</summary>
        public static long NiceRound(double v)
        {
            if (v <= 0) return 0;
            if (v < 20) return (long)Math.Round(v, MidpointRounding.AwayFromZero);
            if (v < 100) return (long)(Math.Round(v / 5, MidpointRounding.AwayFromZero) * 5);
            double mag = Math.Pow(10, Math.Floor(Math.Log10(v)) - 1);
            return (long)(Math.Round(v / mag, MidpointRounding.AwayFromZero) * mag);
        }

        /// <summary>A "both" Biomass/Ore pair converted to the blueprint's cost type (Biomass only = 3× Biomass, Ore only = 1.5× Ore).</summary>
        private static List<Cost> Costs(ContentDb content, BlueprintDef def, double biomass, double ore)
        {
            var t = content.CityTuning;
            switch (def.CostType)
            {
                case BuildingCostType.Biomass: return new List<Cost> { new Cost(Currencies.Biomass, NiceRound(biomass * t.BiomassOnlyMult)) };
                case BuildingCostType.Ore: return new List<Cost> { new Cost(Currencies.Ore, NiceRound(ore * t.OreOnlyMult)) };
                default: return new List<Cost> { new Cost(Currencies.Biomass, NiceRound(biomass)), new Cost(Currencies.Ore, NiceRound(ore)) };
            }
        }

        /// <summary>Cost of a new copy (rarity build cost in the blueprint's currencies).</summary>
        public static List<Cost> BuildCost(ContentDb content, BlueprintDef def)
        {
            var r = content.CityTuning.Rarity[(int)def.Rarity];
            return Costs(content, def, r.BuildBiomass, r.BuildOre);
        }

        /// <summary>Cost of upgrading from <paramref name="level"/> to level+1: first level-up × LevelCostMult[level − 1].</summary>
        public static List<Cost> UpgradeCost(ContentDb content, BlueprintDef def, int level)
        {
            var t = content.CityTuning;
            var r = t.Rarity[(int)def.Rarity];
            double m = t.LevelCostMult[Math.Max(0, Math.Min(t.LevelCostMult.Length - 1, level - 1))];
            return Costs(content, def, r.UpgradeBiomass * m, r.UpgradeOre * m);
        }

        /// <summary>Speed divisor from the "build_speed" modifier.</summary>
        private static double BuildSpeed(GameState state, long nowMs) => ModifierMath.Multiplier(state.Modifiers, "build_speed", nowMs);

        public static long BuildDurationMs(GameState state, ContentDb content, BlueprintDef def, long nowMs) =>
            (long)Math.Ceiling(content.CityTuning.Rarity[(int)def.Rarity].BuildMs / BuildSpeed(state, nowMs));

        public static long UpgradeDurationMs(GameState state, ContentDb content, BlueprintDef def, int level, long nowMs)
        {
            var r = content.CityTuning.Rarity[(int)def.Rarity];
            double ms = r.UpgradeMs * Math.Pow(content.CityTuning.TimeGrowth, Math.Max(0, level - 1));
            return (long)Math.Ceiling(ms / BuildSpeed(state, nowMs));
        }

        /// <summary>Shards needed for the next star (the shared table, ContentDb.ShardStars), or 0 at max.</summary>
        public static long StarCost(ContentDb content, OwnedBlueprint b) => b.Stars >= MaxStars ? 0 : Math.Max(0, content.ShardStars.NextCost(b.Stars));

        // ---- boons ------------------------------------------------------------------------------------

        /// <summary>Sum of reached star-unlock values of a kind for one blueprint.</summary>
        public static double MilestoneValue(BlueprintDef def, int stars, BoonKind kind)
        {
            double v = 0;
            foreach (var m in def.Milestones) if (m.Kind == kind && stars >= m.Stars) v += m.Value;
            return v;
        }

        /// <summary>
        /// Value of a boon kind of one building as if it runs (ignores staffing): Σ Base × growth (× (1 + placement%) + flat for the
        /// placement boon), whole-number boons rounded down, plus reached star unlocks of that kind.
        /// </summary>
        public static double BoonValue(GameState state, ContentDb content, CityBuilding b, BoonKind kind)
        {
            var def = content.Blueprint(b?.BlueprintId);
            if (def == null) return 0;
            int stars = Stars(state, def.Id);
            int level = Math.Max(1, b.Level);
            double v = 0;
            foreach (var boon in def.Boons)
            {
                if (boon.Kind != kind) continue;
                double x = boon.Base * Growth(content, boon, level, stars);
                if (boon.Placement && b.Placed) x *= 1 + CityLayout.PlacementPct(state, content, b.Uid) / 100.0;
                if (boon.Whole) x = Math.Floor(x + 1e-9);
                if (boon.Placement && b.Placed) x += CityLayout.PlacementFlat(state, content, b.Uid);
                v += x;
            }
            if (kind != BoonKind.WorkerSlots) v += MilestoneValue(def, stars, kind);
            return v;
        }

        /// <summary>City-wide total of a boon kind over every running building (copies add up).</summary>
        public static double BoonTotal(GameState state, ContentDb content, BoonKind kind)
        {
            double v = 0;
            foreach (var b in state.City.Buildings)
                if (IsActive(state, content, b)) v += BoonValue(state, content, b, kind);
            return v;
        }

        /// <summary>Squad slots: the Launch Pod's seats (1, +1 at 5★ and 10★) once it is built; no other building gives any.</summary>
        public static int SquadSlots(GameState state, ContentDb content)
        {
            var pod = LaunchPod(state);
            if (!IsBuilt(pod)) return 0;
            return Math.Min(content.CityTuning.MaxSquadSlots, WorkerSlots(state, content, pod));
        }

        public static int DraftRerolls(GameState state, ContentDb content) => (int)Math.Floor(BoonTotal(state, content, BoonKind.DraftRerolls) + 1e-9);

        /// <summary>Laboratory research slots (1, +1 at 5★ and 10★) while the Laboratory runs; 0 otherwise.</summary>
        public static int ResearchSlots(GameState state, ContentDb content) => (int)Math.Floor(BoonTotal(state, content, BoonKind.ResearchSlots) + 1e-9);

        /// <summary>Laboratory research speed-up percent (Laboratory levels only).</summary>
        public static double ResearchSpeedPct(GameState state, ContentDb content) => BoonTotal(state, content, BoonKind.ResearchSpeedPct);

        /// <summary>Stamina regen bonus provider (cap bonus + tick speed-up percent).</summary>
        public static CityStaminaBonus StaminaBonus(GameState state, ContentDb content, long nowMs) => new CityStaminaBonus
        {
            CapBonus = (long)Math.Floor(BoonTotal(state, content, BoonKind.StaminaCap) + 1e-9),
            TickSpeedPercent = BoonTotal(state, content, BoonKind.StaminaRegenPct),
        };

        // ---- workers and production -------------------------------------------------------------------

        /// <summary>Worker slots of a building at its blueprint's stars (Launch Pod: squad slots).</summary>
        public static int WorkerSlots(GameState state, ContentDb content, CityBuilding b)
        {
            var def = content.Blueprint(b?.BlueprintId);
            if (def == null) return 0;
            return WorkerSlots(def, Stars(state, def.Id));
        }

        public static int WorkerSlots(BlueprintDef def, int stars) =>
            def.WorkerSlots + (int)MilestoneValue(def, stars, BoonKind.WorkerSlots) + (int)MilestoneValue(def, stars, BoonKind.SquadSlots);

        /// <summary>Aliens working in a building (deterministic order).</summary>
        public static List<OwnedAlien> Workers(GameState state, string uid)
        {
            var list = new List<OwnedAlien>();
            if (uid == null) return list;
            foreach (var a in state.Aliens.Owned.Values) if (a.WorkBuildingId == uid) list.Add(a);
            list.Sort((x, y) => string.CompareOrdinal(x.TypeId, y.TypeId));
            return list;
        }

        /// <summary>Production bonus percent of a building's workers (production buildings; not pull buildings).</summary>
        public static double WorkerBonusPct(GameState state, ContentDb content, CityBuilding b)
        {
            var def = content.Blueprint(b?.BlueprintId);
            if (def == null || def.ProduceCurrency == null) return 0;
            double v = 0;
            foreach (var a in Workers(state, b.Uid)) v += AlienSystem.WorkBonusPct(state, content, a, def.ProduceCurrency);
            return v;
        }

        public static double PriorityFactor(GameState state, ContentDb content, string currency)
        {
            string p = state.City.PriorityCurrency;
            if (string.IsNullOrEmpty(p)) return 1;
            return p == currency ? 1 + content.CityTuning.PriorityBoostPct / 100.0 : 1 - content.CityTuning.PriorityPenaltyPct / 100.0;
        }

        /// <summary>Growth multiplier of a production building (level and star ladders).</summary>
        public static double ProductionGrowth(GameState state, ContentDb content, CityBuilding b) =>
            Growth(content, null, Math.Max(1, b.Level), Stars(state, b.BlueprintId));

        /// <summary>Production per hour before time-limited modifiers (0 when not running or not a production building).</summary>
        public static double BaseRatePerHour(GameState state, ContentDb content, CityBuilding b)
        {
            var def = content.Blueprint(b?.BlueprintId);
            if (def == null || def.ProduceCurrency == null || !IsActive(state, content, b)) return 0;
            double rate = def.BaseRatePerHour * ProductionGrowth(state, content, b) * (1 + CityLayout.PlacementPct(state, content, b.Uid) / 100.0);
            double pct = BoonTotal(state, content, BoonKind.ProductionPct) + WorkerBonusPct(state, content, b);
            return rate * (1 + pct / 100.0) * PriorityFactor(state, content, def.ProduceCurrency);
        }

        /// <summary>Production per hour at a moment, including modifiers (for UI).</summary>
        public static double ProductionPerHour(GameState state, ContentDb content, CityBuilding b, long nowMs)
        {
            var def = content.Blueprint(b?.BlueprintId);
            if (def?.ProduceCurrency == null) return 0;
            return BaseRatePerHour(state, content, b) * ModifierMath.Multiplier(state.Modifiers, "production:" + def.ProduceCurrency, nowMs);
        }

        /// <summary>Storage cap: StorageHours of the rate at the building's level and stars (before workers / placement).</summary>
        public static double StorageCap(GameState state, ContentDb content, CityBuilding b)
        {
            var def = content.Blueprint(b?.BlueprintId);
            if (def == null || def.ProduceCurrency == null || b.Level < 1) return 0;
            return def.BaseRatePerHour * content.CityTuning.StorageHours * ProductionGrowth(state, content, b);
        }

        // ---- pull buildings (chances) -------------------------------------------------------------------

        /// <summary>
        /// Chances a pull building makes per day while it runs: BasePerDay × (1 + level ladder + StarSpeedPct per star 2..4)
        /// × (1 + placement%). Aliens' work bonus and city production bonuses do not apply.
        /// </summary>
        public static double ChancesPerDay(GameState state, ContentDb content, CityBuilding b)
        {
            var def = content.Blueprint(b?.BlueprintId);
            if (def?.Pull == null || !IsActive(state, content, b)) return 0;
            return NominalChancesPerDay(content, def, b.Level, Stars(state, def.Id)) * (1 + CityLayout.PlacementPct(state, content, b.Uid) / 100.0);
        }

        /// <summary>Chances per day without placement (for cards and the sim).</summary>
        public static double NominalChancesPerDay(ContentDb content, BlueprintDef def, int level, int stars)
        {
            var p = def.Pull;
            int starSteps = Math.Max(0, Math.Min(stars, p.StarSpeedTo) - p.StarSpeedFrom + 1);
            return p.BasePerDay * (1 + Ladder(content, Math.Max(1, level)) + starSteps * p.StarSpeedPct / 100.0);
        }

        /// <summary>Chances a pull building can hold: BaseStorage × (1 + level ladder), rounded down (3 → 6).</summary>
        public static int ChanceStorage(ContentDb content, BlueprintDef def, int level) =>
            def?.Pull == null ? 0 : (int)Math.Floor(def.Pull.BaseStorage * (1 + Ladder(content, Math.Max(1, level))) + 1e-9);

        /// <summary>Pop odds in percent of 1 / 2 / 3 pulls at a star level.</summary>
        public static double[] PopOdds(ContentDb content, int stars) => content.CityTuning.PopOdds[Math.Max(1, Math.Min(MaxStars, stars))];

        /// <summary>Most pulls one pop can give (X): 1 up to 4★, 2 at 5★, 3 at 10★.</summary>
        public static int MaxPullsPerPop(ContentDb content, int stars)
        {
            var odds = PopOdds(content, stars);
            int x = 1;
            for (int i = 0; i < odds.Length; i++) if (odds[i] > 0) x = i + 1;
            return x;
        }

        /// <summary>Whole chances ready to pop on a building.</summary>
        public static int ChancesReady(CityBuilding b) => b == null ? 0 : (int)Math.Floor(b.Chances + 1e-9);

        // ---- settle -------------------------------------------------------------------------------------

        /// <summary>Settle one building's production / chances up to <paramref name="toMs"/> (piecewise over modifier windows, capped).</summary>
        public static void SettleBuilding(GameState state, ContentDb content, CityBuilding b, long toMs)
        {
            if (toMs <= b.LastSettleMs) return;
            long from = b.LastSettleMs;
            b.LastSettleMs = toMs;
            var def = content.Blueprint(b.BlueprintId);
            if (def == null || !IsActive(state, content, b)) return;
            if (def.Pull != null)
            {
                int cap = ChanceStorage(content, def, b.Level);
                if (b.Chances >= cap) return;
                b.Chances = Math.Min(cap, b.Chances + ChancesPerDay(state, content, b) * ((toMs - from) / DayMs));
                return;
            }
            if (def.ProduceCurrency == null) return;
            double storage = StorageCap(state, content, b);
            if (b.Stored >= storage) return;
            double baseRate = BaseRatePerHour(state, content, b);
            if (baseRate <= 0) return;

            string target = "production:" + def.ProduceCurrency;
            string wildcard = "production:*";
            var cuts = new List<long> { from, toMs };
            foreach (var m in state.Modifiers)
            {
                if (m.Target != target && m.Target != wildcard) continue;
                if (m.StartMs > from && m.StartMs < toMs) cuts.Add(m.StartMs);
                if (m.EndMs > from && m.EndMs < toMs) cuts.Add(m.EndMs);
            }
            cuts.Sort();
            double gain = 0;
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                long a = cuts[i], z = cuts[i + 1];
                if (z <= a) continue;
                gain += baseRate * ModifierMath.Multiplier(state.Modifiers, target, a) * ((z - a) / HourMs);
            }
            b.Stored = Math.Min(storage, b.Stored + gain);
        }

        public static void SettleAll(GameState state, ContentDb content, long toMs)
        {
            foreach (var b in state.City.Buildings) SettleBuilding(state, content, b, toMs);
        }

        // ---- digging ------------------------------------------------------------------------------------

        /// <summary>The next dig's number (1-based: dug patches + the running dig + 1).</summary>
        public static int NextDigNumber(GameState state) => state.City.DugPatches.Count + (state.City.DigPatchId != null ? 1 : 0) + 1;

        /// <summary>Cost row of dig number <paramref name="n"/> (1-based), or null past the table.</summary>
        public static CityDigDef DigDef(ContentDb content, int n) => n >= 1 && n <= content.CityTuning.Digs.Count ? content.CityTuning.Digs[n - 1] : null;

        public static List<Cost> DigCost(ContentDb content, int n)
        {
            var d = DigDef(content, n);
            var list = new List<Cost>();
            if (d == null) return list;
            if (d.Biomass > 0) list.Add(new Cost(Currencies.Biomass, d.Biomass));
            if (d.Ore > 0) list.Add(new Cost(Currencies.Ore, d.Ore));
            if (d.GearCores > 0) list.Add(new Cost(Currencies.GearCores, d.GearCores));
            return list;
        }

        /// <summary>Why a patch cannot be dug now (null = it can, resources permitting).</summary>
        public static string DigBlockReason(GameState state, ContentDb content, string patchId)
        {
            var p = content.CityPatch(patchId);
            if (p == null) return "unknown patch";
            if (state.City.DugPatches.Contains(p.Id)) return "already dug";
            if (state.City.DigPatchId != null) return state.City.DigPatchId == p.Id ? "already digging" : "a dig is running";
            if (DigDef(content, NextDigNumber(state)) == null) return "no digs left";
            if (!CityLayout.PatchTouchesLand(state, content, p)) return "must touch your land";
            return null;
        }

        // ---- engine hooks -------------------------------------------------------------------------------

        private static void InitNewPlayer(CommandContext ctx) => ctx.State.City.Version = LayoutVersion;

        private static void Settle(CommandContext ctx)
        {
            if (ctx.State.City.Version < LayoutVersion) Reset(ctx);
            TimerSystem.CompleteDue(ctx); // completes due construction/upgrades/digs at their exact end time first
            SettleAll(ctx.State, ctx.Content, ctx.NowMs);
        }

        /// <summary>
        /// The City redesign reset (pre-release, no layout migration): layouts reset, every built building goes to storage with its
        /// level (capped at 10) and stars kept, land goes back to the start, plus one free dig per old area unlocked beyond the first.
        /// Removed blueprints are dropped (their shards stay in the wallet); the Housing Pod becomes the Launch Pod.
        /// </summary>
        public static void Reset(CommandContext ctx)
        {
            var s = ctx.State; var c = ctx.Content; var city = s.City;
            if (city.Blueprints.TryGetValue("bp_housing_pod", out var pod) && !city.Blueprints.ContainsKey(ContentDb.LaunchPodId))
            {
                city.Blueprints.Remove("bp_housing_pod");
                pod.Id = ContentDb.LaunchPodId;
                city.Blueprints[pod.Id] = pod;
            }
            var ids = new List<string>(city.Blueprints.Keys);
            ids.Sort(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var owned = city.Blueprints[id];
                var def = c.Blueprint(id);
                if (def == null) { city.Blueprints.Remove(id); continue; }
                bool built = owned.Level >= 1 || TimerSystem.Find(s, TimerBuild, id) != null;
                if (built && Building(s, id + "#1") == null)
                    city.Buildings.Add(new CityBuilding { Uid = id + "#1", BlueprintId = id, Level = Math.Max(1, Math.Min(c.CityTuning.MaxLevel, owned.Level)), LastSettleMs = ctx.NowMs });
                owned.Level = 0;
            }
            s.Timers.Active.RemoveAll(t => t.Kind == TimerBuild || t.Kind == TimerUpgrade || t.Kind == TimerDig);
            foreach (var a in s.Aliens.Owned.Values) a.WorkBuildingId = null;
            city.Streets.Clear();
            city.DugPatches.Clear();
            city.DigPatchId = null; city.DigTimerId = null;
            city.FreeDigs += Math.Max(0, city.UnlockedAreas.Count - 1);
            city.UnlockedAreas.Clear();
            city.Version = LayoutVersion;
            CityLayout.Invalidate(s);
        }

        /// <summary>
        /// The crash site comes first (owner, 2026-11-25): once the Aliens unlock is recorded, the player owns the Launch Pod blueprint and
        /// its first copy stands built (level 1, no build timer) on the crash site — touching the plaza with the fireplace, inside the start
        /// land, at <see cref="CityPlanner.Find"/>'s spot (no new streets, off the street axes: deterministic, the free plaza side closest to
        /// the fire). An idle
        /// alien takes the first seat so the squad flies at once. Runs after every unlock check (OnboardingSystem.CheckUnlocks), so a save
        /// that already had the Aliens unlock gets its pod on load the same way. Dev unlock-all states (no recorded unlock) are skipped.
        /// Returns the pod's uid when it was created now.
        /// </summary>
        public static string EnsureCrashSitePod(CommandContext ctx)
        {
            var s = ctx.State; var c = ctx.Content;
            if (!s.Onboarding.Unlocked.ContainsKey(Features.Aliens) || LaunchPod(s) != null) return null;
            var def = c.Blueprint(ContentDb.LaunchPodId);
            if (def == null) return null;
            if (Owned(s, def.Id) == null) Unlock(ctx, def);
            // off the fireplace's street axes (CityPlanner.SpineCells), so the City's main streets can still grow from the plaza
            var plan = CityPlanner.Find(s, c, def.Id, null, 0, CityPlanner.SpineCells(c)) ?? CityPlanner.Find(s, c, def.Id, null, 0);
            var b = new CityBuilding { Uid = NextUid(s, def), BlueprintId = def.Id, Level = 1, LastSettleMs = ctx.NowMs };
            if (plan != null) { b.Placed = true; b.X = plan.X; b.Y = plan.Y; b.Rotated = plan.Rotated; }
            s.City.Buildings.Add(b);
            CityLayout.Invalidate(s);
            if (b.Placed)
            {
                OwnedAlien first = null;
                foreach (var a in s.Aliens.Owned.Values)
                    if (string.IsNullOrEmpty(a.WorkBuildingId) && (first == null || string.CompareOrdinal(a.TypeId, first.TypeId) < 0)) first = a;
                if (first != null) first.WorkBuildingId = b.Uid;
                AlienSystem.SyncSquad(s);
            }
            s.Count("city_buildings_built");
            ctx.Emit("crash_site_pod", b.Uid);
            return b.Uid;
        }

        private static void CompleteBuild(CommandContext ctx, GameTimer t)
        {
            var b = Building(ctx.State, t.RefId);
            if (b == null) return;
            long at = Math.Min(t.EndsMs, ctx.NowMs);
            SettleAll(ctx.State, ctx.Content, at);
            b.TimerId = null;
            b.Level = Math.Max(1, b.Level);
            b.LastSettleMs = at;
            ctx.State.Count("city_buildings_built");
            ctx.Emit("building_built", b.Uid);
        }

        private static void CompleteUpgrade(CommandContext ctx, GameTimer t)
        {
            var b = Building(ctx.State, t.RefId);
            if (b == null) return;
            long at = Math.Min(t.EndsMs, ctx.NowMs);
            SettleAll(ctx.State, ctx.Content, at); // every building at the old rates (global boons may change)
            b.TimerId = null;
            b.Level = Math.Min(ctx.Content.CityTuning.MaxLevel, b.Level + 1);
            ctx.State.Count("city_building_upgrades");
            ctx.Emit("building_upgraded", b.Uid, b.Level.ToString());
        }

        private static void CompleteDig(CommandContext ctx, GameTimer t)
        {
            var city = ctx.State.City;
            if (city.DigPatchId != t.RefId) return;
            SettleAll(ctx.State, ctx.Content, Math.Min(t.EndsMs, ctx.NowMs)); // placement bonuses (cliffside, open ground) change
            FinishDig(ctx, t.RefId);
        }

        private static void FinishDig(CommandContext ctx, string patchId)
        {
            var city = ctx.State.City;
            city.DigPatchId = null;
            city.DigTimerId = null;
            if (!city.DugPatches.Contains(patchId)) city.DugPatches.Add(patchId);
            CityLayout.Invalidate(ctx.State);
            ctx.State.Count("city_digs");
            ctx.Emit("city_dug", patchId, city.DugPatches.Count.ToString());
        }

        private static void ContributeLoadout(GameState state, ContentDb content, long nowMs, CombatLoadout loadout)
        {
            loadout.DraftRerolls += DraftRerolls(state, content);
            loadout.RunStartCoins += (int)Math.Floor(BoonTotal(state, content, BoonKind.RunStartCoins) + 1e-9);
            loadout.SkipNegativeEvents += (int)Math.Floor(BoonTotal(state, content, BoonKind.SkipNegativeEvents) + 1e-9);
            loadout.AwakeningSlowPct += BoonTotal(state, content, BoonKind.AwakeningSlowPct);
            double xp = BoonTotal(state, content, BoonKind.RunXpPct);
            if (xp > 0) loadout.WormStats.Add(new StatMod(StatId.XpGain, xp));
            double alienDmg = BoonTotal(state, content, BoonKind.AlienDamagePct);
            if (alienDmg > 0) loadout.WormStats.Add(new StatMod(StatId.AlienPower, alienDmg));
        }

        /// <summary>
        /// Blueprint granter (pull system, docs/design/pull-system.md): every copy is one batch of ShardStars.BatchShards shards; the
        /// batch of a blueprint the player does not own unlocks it (1★, the batch's shards spent at once).
        /// </summary>
        private static void GrantBlueprint(CommandContext ctx, Reward r)
        {
            if (RewardWildcard.IsWildcard(r))
            {
                for (long i = 0; i < r.Amount; i++)
                    GrantBlueprint(ctx, new Reward(r.Kind, RewardWildcard.Pick(ctx.Rng, ctx.Content.Blueprints, d => (int)d.Rarity, r.Rarity), 1, r.Rarity));
                return;
            }
            var def = ctx.Content.Blueprint(r.Id);
            if (def == null) throw new RuleException("unknown blueprint " + r.Id);
            long copies = r.Amount;
            if (!ctx.State.City.Blueprints.ContainsKey(def.Id))
            {
                Unlock(ctx, def);
                copies--;
            }
            if (copies > 0)
            {
                long shards = copies * ctx.Content.ShardStars.BatchShards;
                ctx.State.Wallet.Add(Currencies.BlueprintShards(def.Id), shards);
                ctx.State.Count("blueprint_duplicates", copies);
                ctx.Emit("blueprint_shards", def.Id, shards.ToString());
            }
        }

        private static void Unlock(CommandContext ctx, BlueprintDef def)
        {
            ctx.State.City.Blueprints[def.Id] = new OwnedBlueprint { Id = def.Id, Stars = 1 };
            ctx.State.Count("blueprints_obtained");
            ctx.Emit("blueprint_new", def.Id);
        }

        /// <summary>
        /// Unlock a blueprint from loose shards (Shard Reactor output): when it is not owned and its shards reach UnlockShards, they
        /// are spent and the blueprint is owned at 1★. True if it unlocked.
        /// </summary>
        public static bool TryUnlockFromShards(CommandContext ctx, string blueprintId)
        {
            var def = ctx.Content.Blueprint(blueprintId);
            if (def == null || Owned(ctx.State, blueprintId) != null) return false;
            if (!ctx.State.Wallet.TrySpend(Currencies.BlueprintShards(blueprintId), ctx.Content.ShardStars.UnlockShards)) return false;
            Unlock(ctx, def);
            return true;
        }

        // ---- handlers -----------------------------------------------------------------------------------

        private static void RequireLayout(CommandContext ctx, CityEdit edit)
        {
            var check = CityLayout.Validate(ctx.State, ctx.Content, edit);
            GameEngine.Require(check.Ok, check.Error);
        }

        private static CommandResult HandlePlace(CityPlace cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.BiomassFarm); // before the City only the crash site stands (no layout edits)
            var def = ctx.Content.Blueprint(cmd.BlueprintId);
            GameEngine.Require(def != null, "unknown blueprint");
            string uid = string.IsNullOrEmpty(cmd.Uid) ? null : cmd.Uid;
            RequireLayout(ctx, CityEdit.Place(def.Id, cmd.X, cmd.Y, cmd.Rotated, uid));
            CityBuilding b;
            if (uid != null)
            {
                b = Building(ctx.State, uid);
                b.Placed = true; b.X = cmd.X; b.Y = cmd.Y; b.Rotated = cmd.Rotated;
                b.LastSettleMs = ctx.NowMs;
            }
            else
            {
                GameEngine.Require(ctx.State.Wallet.TrySpend(BuildCost(ctx.Content, def)), "not enough resources");
                b = new CityBuilding { Uid = NextUid(ctx.State, def), BlueprintId = def.Id, X = cmd.X, Y = cmd.Y, Rotated = cmd.Rotated, Placed = true, LastSettleMs = ctx.NowMs };
                ctx.State.City.Buildings.Add(b);
                b.TimerId = TimerSystem.Start(ctx, TimerBuild, b.Uid, BuildDurationMs(ctx.State, ctx.Content, def, ctx.NowMs)).Id;
            }
            CityLayout.Invalidate(ctx.State);
            ctx.Emit("building_placed", b.Uid, cmd.X.ToString(), cmd.Y.ToString());
            TimerSystem.CompleteDue(ctx);
            return CommandResult.Success(b.Uid);
        }

        private static CommandResult HandleMove(CityMove cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.BiomassFarm); // before the City only the crash site stands (no layout edits)
            RequireLayout(ctx, CityEdit.Move(cmd.Uid, cmd.X, cmd.Y, cmd.Rotated));
            var b = Building(ctx.State, cmd.Uid);
            b.X = cmd.X; b.Y = cmd.Y; b.Rotated = cmd.Rotated;
            CityLayout.Invalidate(ctx.State);
            ctx.Emit("building_moved", b.Uid, cmd.X.ToString(), cmd.Y.ToString());
            return CommandResult.Success();
        }

        /// <summary>
        /// Store: back to the tray (level and stars kept, storage collected, workers idle). A new copy whose first construction is still
        /// running is cancelled instead: its build cost is refunded and the copy removed (the editor's undo of a new build).
        /// </summary>
        private static CommandResult HandleStore(CityStore cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.BiomassFarm); // before the City only the crash site stands (no layout edits)
            var b = Building(ctx.State, cmd.Uid);
            GameEngine.Require(b != null && b.Placed, "building not placed");
            var def = ctx.Content.Blueprint(b.BlueprintId);
            bool cancelBuild = b.Level == 0;
            GameEngine.Require(cancelBuild || b.TimerId == null, "cannot store while upgrading");
            RequireLayout(ctx, CityEdit.Store(b.Uid));
            foreach (var a in Workers(ctx.State, b.Uid)) a.WorkBuildingId = null;
            if (cancelBuild)
            {
                if (b.TimerId != null) TimerSystem.Remove(ctx.State, b.TimerId);
                foreach (var c in BuildCost(ctx.Content, def)) ctx.State.Wallet.Add(c.Currency, c.Amount);
                ctx.State.City.Buildings.Remove(b);
                ctx.Emit("building_cancelled", b.Uid);
            }
            else
            {
                if (def?.ProduceCurrency != null) CollectInto(ctx, b, def, null);
                b.Placed = false;
                b.Stored = 0;
                ctx.Emit("building_stored", b.Uid);
            }
            CityLayout.Invalidate(ctx.State);
            AlienSystem.SyncSquad(ctx.State);
            return CommandResult.Success();
        }

        private static CommandResult HandleStreets(CityStreets cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.BiomassFarm); // before the City only the crash site stands (no layout edits)
            var edit = CityEdit.Streets(cmd.Adds, cmd.Removes);
            RequireLayout(ctx, edit);
            foreach (var r in edit.Removes) ctx.State.City.Streets.Remove(r);
            foreach (var a in edit.Adds) ctx.State.City.Streets.Add(a);
            CityLayout.Invalidate(ctx.State);
            ctx.Emit("city_streets", edit.Adds.Count.ToString(), edit.Removes.Count.ToString());
            return CommandResult.Success();
        }

        private static CommandResult HandleDig(CityDig cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.BiomassFarm);
            string block = DigBlockReason(ctx.State, ctx.Content, cmd.PatchId);
            GameEngine.Require(block == null, block);
            var city = ctx.State.City;
            if (city.FreeDigs > 0)
            {
                city.FreeDigs--;
                FinishDig(ctx, cmd.PatchId);
                return CommandResult.Success();
            }
            int n = NextDigNumber(ctx.State);
            GameEngine.Require(ctx.State.Wallet.TrySpend(DigCost(ctx.Content, n)), "not enough resources");
            city.DigPatchId = cmd.PatchId;
            city.DigTimerId = TimerSystem.Start(ctx, TimerDig, cmd.PatchId, DigDef(ctx.Content, n).Ms).Id;
            ctx.Emit("city_dig_started", cmd.PatchId, n.ToString());
            TimerSystem.CompleteDue(ctx);
            return CommandResult.Success();
        }

        private static CommandResult HandleUpgrade(CityUpgrade cmd, CommandContext ctx)
        {
            var b = Building(ctx.State, cmd.Uid);
            GameEngine.Require(IsBuilt(b), "building not built");
            GameEngine.Require(b.TimerId == null, "upgrade already running");
            GameEngine.Require(b.Level < MaxLevel(ctx.Content), "max level");
            var def = ctx.Content.Blueprint(b.BlueprintId);
            GameEngine.Require(ctx.State.Wallet.TrySpend(UpgradeCost(ctx.Content, def, b.Level)), "not enough resources");
            b.TimerId = TimerSystem.Start(ctx, TimerUpgrade, b.Uid, UpgradeDurationMs(ctx.State, ctx.Content, def, b.Level, ctx.NowMs)).Id;
            TimerSystem.CompleteDue(ctx);
            return CommandResult.Success();
        }

        private static CommandResult HandleCollect(CityCollect cmd, CommandContext ctx)
        {
            var collected = new Dictionary<string, long>();
            if (!string.IsNullOrEmpty(cmd.Uid))
            {
                var b = Building(ctx.State, cmd.Uid);
                GameEngine.Require(IsBuilt(b), "building not built");
                var def = ctx.Content.Blueprint(b.BlueprintId);
                GameEngine.Require(def?.ProduceCurrency != null, "not a production building");
                CollectInto(ctx, b, def, collected);
            }
            else
            {
                foreach (var b in ctx.State.City.Buildings)
                {
                    var def = ctx.Content.Blueprint(b.BlueprintId);
                    if (IsBuilt(b) && def?.ProduceCurrency != null) CollectInto(ctx, b, def, collected);
                }
            }
            return CommandResult.Success(collected);
        }

        private static void CollectInto(CommandContext ctx, CityBuilding b, BlueprintDef def, Dictionary<string, long> collected)
        {
            long whole = (long)Math.Floor(b.Stored + 1e-9);
            if (whole <= 0) return;
            // Rewards follow use: a currency without a use yet (Grain before Aliens) stays in the building's storage.
            if (!RewardGate.CurrencyHasUse(ctx.State, ctx.Content, def.ProduceCurrency, ctx.NowMs)) return;
            b.Stored = Math.Max(0, b.Stored - whole);
            ctx.State.Wallet.Add(def.ProduceCurrency, whole);
            ctx.State.Count("city_resources_collected", whole);
            if (collected != null) collected[def.ProduceCurrency] = (collected.TryGetValue(def.ProduceCurrency, out var c) ? c : 0) + whole;
            ctx.Emit("city_collected", b.Uid, def.ProduceCurrency, whole.ToString());
        }

        /// <summary>
        /// Pop chances: Uid set and All false = one chance of that building; Uid set and All true = all of its chances; Uid empty = every
        /// chance on every pull building ("pop all"). Each pop rolls 1..X pulls on the star odds (ctx.Rng). Payload: List&lt;CityPopResult&gt;.
        /// </summary>
        private static CommandResult HandlePop(CityPop cmd, CommandContext ctx)
        {
            var list = new List<CityBuilding>();
            if (!string.IsNullOrEmpty(cmd.Uid))
            {
                var b = Building(ctx.State, cmd.Uid);
                GameEngine.Require(b != null && ctx.Content.Blueprint(b.BlueprintId)?.Pull != null, "not a pull building");
                GameEngine.Require(ChancesReady(b) >= 1, "nothing to pop");
                list.Add(b);
            }
            else
            {
                foreach (var b in ctx.State.City.Buildings)
                    if (ctx.Content.Blueprint(b.BlueprintId)?.Pull != null && ChancesReady(b) >= 1) list.Add(b);
                GameEngine.Require(list.Count > 0, "nothing to pop");
            }
            bool all = string.IsNullOrEmpty(cmd.Uid) || cmd.All;
            var results = new List<CityPopResult>();
            foreach (var b in list)
            {
                var def = ctx.Content.Blueprint(b.BlueprintId);
                var odds = PopOdds(ctx.Content, Stars(ctx.State, def.Id));
                int n = all ? ChancesReady(b) : 1;
                for (int i = 0; i < n; i++)
                {
                    b.Chances = Math.Max(0, b.Chances - 1);
                    int pulls = 1 + ctx.Rng.PickWeighted(new List<double>(odds));
                    ctx.Grant(Reward.Cur(def.Pull.TokenCurrency, pulls));
                    ctx.State.Count("city_chances_popped");
                    ctx.State.Count("city_pulls_popped", pulls);
                    results.Add(new CityPopResult { Uid = b.Uid, Currency = def.Pull.TokenCurrency, Pulls = pulls });
                    ctx.Emit("city_popped", b.Uid, def.Pull.TokenCurrency, pulls.ToString());
                }
            }
            return CommandResult.Success(results);
        }

        private static CommandResult HandleSetPriority(CitySetPriority cmd, CommandContext ctx)
        {
            if (!string.IsNullOrEmpty(cmd.Currency))
            {
                bool produced = false;
                foreach (var d in ctx.Content.Blueprints.Values) if (d.ProduceCurrency == cmd.Currency) { produced = true; break; }
                GameEngine.Require(produced, "currency is not produced in the city");
            }
            ctx.State.City.PriorityCurrency = string.IsNullOrEmpty(cmd.Currency) ? null : cmd.Currency;
            ctx.Emit("city_priority", cmd.Currency ?? "");
            return CommandResult.Success();
        }

        private static CommandResult HandleStarUp(BlueprintStarUp cmd, CommandContext ctx)
        {
            var b = Owned(ctx.State, cmd.BlueprintId);
            GameEngine.Require(b != null, "blueprint not owned");
            GameEngine.Require(b.Stars < MaxStars, "max stars");
            long cost = StarCost(ctx.Content, b);
            GameEngine.Require(ctx.State.Wallet.TrySpend(Currencies.BlueprintShards(b.Id), cost), "not enough shards");
            b.Stars++;
            CityLayout.Invalidate(ctx.State);
            ctx.State.Count("blueprint_star_ups");
            ctx.Emit("blueprint_star_up", b.Id, b.Stars.ToString());
            return CommandResult.Success();
        }
    }
}
