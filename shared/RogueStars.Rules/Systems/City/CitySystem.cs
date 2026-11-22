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

    /// <summary>
    /// City: a grid of areas unlocked one by one; blueprints (one building each) are placed, constructed and
    /// upgraded on timers; blueprint stars 1–10 from duplicate shards with star milestones; production buildings
    /// settle lazily from timestamps into capped storage; boon buildings feed runs (meta skills, draft rerolls,
    /// squad slots), stamina and other systems (breeding, laboratory).
    ///
    /// Production per hour of a building =
    ///   BaseRatePerHour × (1 + LevelProductionPct×(level−1)/100) × (1 + StarProductionPct×(stars−1)/100)
    ///   × (1 + (star-milestone production % + global production boons % + worker bonus %)/100)
    ///   × priority factor (+PriorityBoostPct for the priority currency, −PriorityPenaltyPct for others)
    ///   × ModifierMath.Multiplier(state.Modifiers, "production:&lt;currency&gt;", t)   (integrated piecewise over modifier windows)
    /// Storage cap = BaseStorage × level/star growth × (1 + storage milestone %/100). Collect pays whole units.
    /// </summary>
    public static class CitySystem
    {
        public const string TimerBuild = "city_build";
        public const string TimerUpgrade = "city_upgrade";
        public const int MaxStars = 10;
        private const double HourMs = 3_600_000.0;

        public static void Register(GameEngine e)
        {
            e.Register<CityUnlockArea>(HandleUnlockArea);
            e.Register<CityBuild>(HandleBuild);
            e.Register<CityMove>(HandleMove);
            e.Register<CityDemolish>(HandleDemolish);
            e.Register<CityUpgrade>(HandleUpgrade);
            e.Register<CityCollect>(HandleCollect);
            e.Register<CitySetPriority>(HandleSetPriority);
            e.Register<BlueprintStarUp>(HandleStarUp);
            e.RegisterNewPlayer(InitNewPlayer);
            e.RegisterSettler(Settle);
            e.RegisterLoadoutContributor(ContributeLoadout);
            e.Rewards.Register(RewardKinds.Blueprint, GrantBlueprint);
            TimerSystem.RegisterCompletion(e, TimerBuild, CompleteBuild);
            TimerSystem.RegisterCompletion(e, TimerUpgrade, CompleteUpgrade);
            // Stamina cap/regen boons feed Progression's regen engine (EconomySystem is registered before City).
            EconomySystem.RegisterRegenBonusProvider(e, (s, c, now, currency) =>
            {
                if (currency != Currencies.Stamina) return (0L, 0.0);
                var b = StaminaBonus(s, c, now);
                return (b.CapBonus, b.TickSpeedPercent);
            });
        }

        // ---- queries ----------------------------------------------------------------------------

        /// <summary>Owned blueprints in deterministic (ordinal id) order.</summary>
        public static List<OwnedBlueprint> SortedBlueprints(GameState state)
        {
            var list = new List<OwnedBlueprint>(state.City.Blueprints.Values);
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        public static OwnedBlueprint Owned(GameState state, string blueprintId) =>
            blueprintId != null && state.City.Blueprints.TryGetValue(blueprintId, out var b) ? b : null;

        /// <summary>A building is active (produces, gives boons) when placed and constructed.</summary>
        public static bool IsActive(OwnedBlueprint b) => b != null && b.Placed && b.Level >= 1;

        public static bool IsAreaUnlocked(GameState state, string areaId) => state.City.UnlockedAreas.Contains(areaId);

        /// <summary>The next area that can be unlocked (lowest Order not yet unlocked), or null.</summary>
        public static CityAreaDef NextArea(GameState state, ContentDb content)
        {
            CityAreaDef best = null;
            foreach (var a in content.CityAreas)
                if (!IsAreaUnlocked(state, a.Id) && (best == null || a.Order < best.Order)) best = a;
            return best;
        }

        public static CityAreaDef AreaAt(ContentDb content, int x, int y)
        {
            foreach (var a in content.CityAreas)
                if (x >= a.X && x < a.X + a.Width && y >= a.Y && y < a.Y + a.Height) return a;
            return null;
        }

        /// <summary>Null if the footprint fits (inside unlocked areas, no overlap), else the reason.</summary>
        public static string PlacementError(GameState state, ContentDb content, BlueprintDef def, int x, int y, string ignoreId = null)
        {
            var t = content.CityTuning;
            if (x < 0 || y < 0 || x + def.Width > t.GridWidth || y + def.Height > t.GridHeight) return "outside the city grid";
            for (int cx = x; cx < x + def.Width; cx++)
                for (int cy = y; cy < y + def.Height; cy++)
                {
                    var area = AreaAt(content, cx, cy);
                    if (area == null || !IsAreaUnlocked(state, area.Id)) return "area locked";
                }
            foreach (var other in state.City.Blueprints.Values)
            {
                if (!other.Placed || other.Id == ignoreId) continue;
                var od = content.Blueprint(other.Id);
                if (od == null) continue;
                bool overlap = x < other.X + od.Width && other.X < x + def.Width && y < other.Y + od.Height && other.Y < y + def.Height;
                if (overlap) return "cells occupied";
            }
            return null;
        }

        public static int MaxLevel(ContentDb content, OwnedBlueprint b) => content.CityTuning.MaxLevelBase + content.CityTuning.MaxLevelPerStar * b.Stars;

        public static List<Cost> BuildCost(ContentDb content, BlueprintDef def)
        {
            var r = content.CityTuning.Rarity[(int)def.Rarity];
            return new List<Cost>
            {
                new Cost(Currencies.Ore, (long)Math.Round(r.BuildOre * def.CostMult)),
                new Cost(Currencies.Biomass, (long)Math.Round(r.BuildBiomass * def.CostMult)),
            };
        }

        /// <summary>Cost of upgrading from <paramref name="level"/> to level+1.</summary>
        public static List<Cost> UpgradeCost(ContentDb content, BlueprintDef def, int level)
        {
            var r = content.CityTuning.Rarity[(int)def.Rarity];
            double g = Math.Pow(content.CityTuning.CostGrowth, Math.Max(0, level - 1)) * def.CostMult;
            return new List<Cost>
            {
                new Cost(Currencies.Ore, (long)Math.Round(r.UpgradeOre * g)),
                new Cost(Currencies.Biomass, (long)Math.Round(r.UpgradeBiomass * g)),
            };
        }

        /// <summary>Speed divisor from BuildSpeedPct boons and the "build_speed" modifier.</summary>
        private static double BuildSpeed(GameState state, ContentDb content, long nowMs) =>
            (1 + BoonTotal(state, content, BoonKind.BuildSpeedPct) / 100.0) * ModifierMath.Multiplier(state.Modifiers, "build_speed", nowMs);

        public static long BuildDurationMs(GameState state, ContentDb content, BlueprintDef def, long nowMs) =>
            (long)Math.Ceiling(content.CityTuning.Rarity[(int)def.Rarity].BuildMs / BuildSpeed(state, content, nowMs));

        public static long UpgradeDurationMs(GameState state, ContentDb content, BlueprintDef def, int level, long nowMs)
        {
            var r = content.CityTuning.Rarity[(int)def.Rarity];
            double ms = r.UpgradeMs * Math.Pow(content.CityTuning.TimeGrowth, Math.Max(0, level - 1));
            return (long)Math.Ceiling(ms / BuildSpeed(state, content, nowMs));
        }

        /// <summary>Shards needed for the next star, or 0 at max.</summary>
        public static long StarCost(ContentDb content, OwnedBlueprint b)
        {
            var costs = content.CityTuning.BlueprintStarCosts;
            return b.Stars >= MaxStars || b.Stars - 1 >= costs.Length ? 0 : costs[b.Stars - 1];
        }

        // ---- boons ------------------------------------------------------------------------------

        /// <summary>Sum of reached star-milestone values of a kind for one blueprint.</summary>
        public static double MilestoneValue(BlueprintDef def, int stars, BoonKind kind)
        {
            double v = 0;
            foreach (var m in def.Milestones) if (m.Kind == kind && stars >= m.Stars) v += m.Value;
            return v;
        }

        /// <summary>Level-scaled boon value of a kind for one building (0 when inactive).</summary>
        public static double LevelBoonValue(ContentDb content, BlueprintDef def, OwnedBlueprint b, BoonKind kind)
        {
            if (!IsActive(b)) return 0;
            double v = 0;
            foreach (var boon in def.Boons)
            {
                if (boon.Kind != kind || b.Level < boon.MinLevel) continue;
                double x = boon.Base + boon.PerLevel * (b.Level - boon.MinLevel);
                if (boon.StarScaled) x *= 1 + content.CityTuning.BoonPerStarPct * (b.Stars - 1) / 100.0;
                v += x;
            }
            return v;
        }

        /// <summary>True for kinds whose star milestones only affect the building itself.</summary>
        private static bool IsLocalMilestone(BoonKind kind) =>
            kind == BoonKind.ProductionPct || kind == BoonKind.StoragePct || kind == BoonKind.WorkerSlots;

        /// <summary>
        /// City-wide total of a boon kind over all active buildings (level boons + star milestones;
        /// production/storage/worker-slot milestones are local to their building and excluded).
        /// Used for StaminaCap, StaminaRegenPct, DraftRerolls, SquadSlots, BreedingSlots/SpeedPct/QualityPct,
        /// ResearchSpeedPct (Laboratory), BuildSpeedPct, ProductionPct (global).
        /// </summary>
        public static double BoonTotal(GameState state, ContentDb content, BoonKind kind)
        {
            double v = 0;
            foreach (var b in SortedBlueprints(state))
            {
                if (!IsActive(b)) continue;
                var def = content.Blueprint(b.Id);
                if (def == null) continue;
                v += LevelBoonValue(content, def, b, kind);
                if (!IsLocalMilestone(kind)) v += MilestoneValue(def, b.Stars, kind);
            }
            return v;
        }

        public static int SquadSlots(GameState state, ContentDb content) =>
            Math.Min(content.CityTuning.MaxSquadSlots, (int)Math.Floor(BoonTotal(state, content, BoonKind.SquadSlots) + 1e-9));

        public static int DraftRerolls(GameState state, ContentDb content) =>
            (int)Math.Floor(BoonTotal(state, content, BoonKind.DraftRerolls) + 1e-9);

        /// <summary>Laboratory research speed-up percent from buildings (for the Run workstream).</summary>
        public static double ResearchSpeedPct(GameState state, ContentDb content) => BoonTotal(state, content, BoonKind.ResearchSpeedPct);

        /// <summary>
        /// Stamina regen bonus provider (cap bonus + tick speed-up percent); registered with
        /// EconomySystem.RegisterRegenBonusProvider in <see cref="Register"/>.
        /// </summary>
        public static CityStaminaBonus StaminaBonus(GameState state, ContentDb content, long nowMs) => new CityStaminaBonus
        {
            CapBonus = (long)Math.Floor(BoonTotal(state, content, BoonKind.StaminaCap) + 1e-9),
            TickSpeedPercent = BoonTotal(state, content, BoonKind.StaminaRegenPct),
        };

        /// <summary>Building boon skills (Source=Building) for runs: highest level tier per boon + star milestone skills, minus replaced ones.</summary>
        public static List<string> RunSkillIds(GameState state, ContentDb content)
        {
            var ids = new List<string>();
            var replaced = new HashSet<string>();
            foreach (var b in SortedBlueprints(state))
            {
                if (!IsActive(b)) continue;
                var def = content.Blueprint(b.Id);
                if (def == null) continue;
                foreach (var boon in def.Boons)
                {
                    if (boon.Kind != BoonKind.RunSkill) continue;
                    string best = null;
                    foreach (var tier in boon.SkillTiers) if (b.Level >= tier.MinLevel) best = tier.SkillId;
                    if (best != null && !ids.Contains(best)) ids.Add(best);
                }
                foreach (var m in def.Milestones)
                {
                    if (m.Kind != BoonKind.RunSkill || b.Stars < m.Stars) continue;
                    if (m.SkillId != null && !ids.Contains(m.SkillId)) ids.Add(m.SkillId);
                    if (m.ReplacesSkillId != null) replaced.Add(m.ReplacesSkillId);
                }
            }
            ids.RemoveAll(replaced.Contains);
            return ids;
        }

        /// <summary>Collection bonus: every owned blueprint gives CollectionPctPerStar × stars percent of its stat.</summary>
        public static List<StatMod> CollectionBonus(GameState state, ContentDb content)
        {
            var mods = new List<StatMod>();
            foreach (var b in SortedBlueprints(state))
            {
                var def = content.Blueprint(b.Id);
                if (def == null || def.CollectionPctPerStar <= 0) continue;
                mods.Add(new StatMod(def.CollectionStat, 0, def.CollectionPctPerStar * b.Stars));
            }
            return mods;
        }

        // ---- production -------------------------------------------------------------------------

        public static int WorkerSlots(ContentDb content, BlueprintDef def, OwnedBlueprint b)
        {
            if (def == null || def.Category != BuildingCategory.Production) return 0;
            return content.CityTuning.Rarity[(int)def.Rarity].WorkerSlots + (int)MilestoneValue(def, b.Stars, BoonKind.WorkerSlots);
        }

        /// <summary>Aliens working in a building (deterministic order).</summary>
        public static List<OwnedAlien> Workers(GameState state, string blueprintId)
        {
            var list = new List<OwnedAlien>();
            foreach (var a in state.Aliens.Owned.Values) if (a.WorkBuildingId == blueprintId) list.Add(a);
            list.Sort((x, y) => string.CompareOrdinal(x.TypeId, y.TypeId));
            return list;
        }

        public static double WorkerBonusPct(GameState state, ContentDb content, OwnedBlueprint b)
        {
            var def = content.Blueprint(b.Id);
            if (def == null || def.ProduceCurrency == null) return 0;
            double v = 0;
            foreach (var a in Workers(state, b.Id)) v += AlienSystem.WorkBonusPct(state, content, a, def.ProduceCurrency);
            return v;
        }

        public static double PriorityFactor(GameState state, ContentDb content, string currency)
        {
            string p = state.City.PriorityCurrency;
            if (string.IsNullOrEmpty(p)) return 1;
            return p == currency ? 1 + content.CityTuning.PriorityBoostPct / 100.0 : 1 - content.CityTuning.PriorityPenaltyPct / 100.0;
        }

        /// <summary>Production per hour before time-limited modifiers (0 when inactive or not a production building).</summary>
        public static double BaseRatePerHour(GameState state, ContentDb content, OwnedBlueprint b)
        {
            var def = content.Blueprint(b?.Id);
            if (def == null || def.ProduceCurrency == null || !IsActive(b)) return 0;
            var t = content.CityTuning;
            double rate = def.BaseRatePerHour
                * (1 + t.LevelProductionPct * (b.Level - 1) / 100.0)
                * (1 + t.StarProductionPct * (b.Stars - 1) / 100.0);
            double pct = MilestoneValue(def, b.Stars, BoonKind.ProductionPct)
                + BoonTotal(state, content, BoonKind.ProductionPct)
                + WorkerBonusPct(state, content, b);
            return rate * (1 + pct / 100.0) * PriorityFactor(state, content, def.ProduceCurrency);
        }

        /// <summary>Production per hour at a moment, including modifiers (for UI).</summary>
        public static double ProductionPerHour(GameState state, ContentDb content, OwnedBlueprint b, long nowMs)
        {
            var def = content.Blueprint(b?.Id);
            if (def?.ProduceCurrency == null) return 0;
            return BaseRatePerHour(state, content, b) * ModifierMath.Multiplier(state.Modifiers, "production:" + def.ProduceCurrency, nowMs);
        }

        public static double StorageCap(ContentDb content, OwnedBlueprint b)
        {
            var def = content.Blueprint(b?.Id);
            if (def == null || def.ProduceCurrency == null || b.Level < 1) return 0;
            var t = content.CityTuning;
            return def.BaseStorage
                * (1 + t.LevelStoragePct * (b.Level - 1) / 100.0)
                * (1 + t.StarStoragePct * (b.Stars - 1) / 100.0)
                * (1 + MilestoneValue(def, b.Stars, BoonKind.StoragePct) / 100.0);
        }

        /// <summary>Settle one building's production up to <paramref name="toMs"/> (piecewise over modifier windows, capped).</summary>
        public static void SettleBuilding(GameState state, ContentDb content, OwnedBlueprint b, long toMs)
        {
            if (toMs <= b.LastSettleMs) return;
            long from = b.LastSettleMs;
            b.LastSettleMs = toMs;
            var def = content.Blueprint(b.Id);
            if (def?.ProduceCurrency == null || !IsActive(b)) return;
            double cap = StorageCap(content, b);
            if (b.Stored >= cap) return;
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
            b.Stored = Math.Min(cap, b.Stored + gain);
        }

        public static void SettleAll(GameState state, ContentDb content, long toMs)
        {
            foreach (var b in SortedBlueprints(state)) SettleBuilding(state, content, b, toMs);
        }

        // ---- engine hooks -------------------------------------------------------------------------

        private static void InitNewPlayer(CommandContext ctx)
        {
            foreach (var a in ctx.Content.CityAreas)
                if (a.UnlockCost.Count == 0 && a.RequiresCounter == null && !IsAreaUnlocked(ctx.State, a.Id))
                    ctx.State.City.UnlockedAreas.Add(a.Id);
        }

        private static void Settle(CommandContext ctx)
        {
            TimerSystem.CompleteDue(ctx); // completes due construction/upgrades at their exact end time first
            SettleAll(ctx.State, ctx.Content, ctx.NowMs);
        }

        private static void CompleteBuild(CommandContext ctx, GameTimer t)
        {
            var b = Owned(ctx.State, t.RefId);
            if (b == null) return;
            long at = Math.Min(t.EndsMs, ctx.NowMs);
            SettleAll(ctx.State, ctx.Content, at);
            b.TimerId = null;
            b.Level = Math.Max(1, b.Level);
            b.LastSettleMs = at;
            ctx.State.Count("city_buildings_built");
            ctx.Emit("building_built", b.Id);
        }

        private static void CompleteUpgrade(CommandContext ctx, GameTimer t)
        {
            var b = Owned(ctx.State, t.RefId);
            if (b == null) return;
            long at = Math.Min(t.EndsMs, ctx.NowMs);
            SettleAll(ctx.State, ctx.Content, at); // every building at the old rates (global boons may change)
            b.TimerId = null;
            b.Level++;
            ctx.State.Count("city_building_upgrades");
            ctx.Emit("building_upgraded", b.Id, b.Level.ToString());
        }

        private static void ContributeLoadout(GameState state, ContentDb content, long nowMs, CombatLoadout loadout)
        {
            foreach (var id in RunSkillIds(state, content))
                if (!loadout.MetaSkillIds.Contains(id)) loadout.MetaSkillIds.Add(id);
            loadout.DraftRerolls += DraftRerolls(state, content);
            loadout.WormStats.AddAll(CollectionBonus(state, content));
        }

        /// <summary>Blueprint granter: first copy = owned blueprint (1 star); further copies become shards.</summary>
        private static void GrantBlueprint(CommandContext ctx, Reward r)
        {
            var def = ctx.Content.Blueprint(r.Id);
            if (def == null) throw new RuleException("unknown blueprint " + r.Id);
            long copies = r.Amount;
            if (!ctx.State.City.Blueprints.ContainsKey(def.Id))
            {
                ctx.State.City.Blueprints[def.Id] = new OwnedBlueprint { Id = def.Id, Stars = 1, LastSettleMs = ctx.NowMs };
                ctx.State.Count("blueprints_obtained");
                ctx.Emit("blueprint_new", def.Id);
                copies--;
            }
            if (copies > 0)
            {
                long shards = copies * ctx.Content.CityTuning.BlueprintShardsPerDuplicate[(int)def.Rarity];
                ctx.State.Wallet.Add(Currencies.BlueprintShards(def.Id), shards);
                ctx.State.Count("blueprint_duplicates", copies);
                ctx.Emit("blueprint_shards", def.Id, shards.ToString());
            }
        }

        // ---- handlers -----------------------------------------------------------------------------

        private static CommandResult HandleUnlockArea(CityUnlockArea cmd, CommandContext ctx)
        {
            var area = ctx.Content.CityArea(cmd.AreaId);
            GameEngine.Require(area != null, "unknown area");
            GameEngine.Require(!IsAreaUnlocked(ctx.State, area.Id), "area already unlocked");
            var next = NextArea(ctx.State, ctx.Content);
            GameEngine.Require(next != null && next.Id == area.Id, "areas unlock in order");
            if (area.RequiresCounter != null)
                GameEngine.Require(ctx.State.Counter(area.RequiresCounter) >= area.RequiresCounterValue, "area requirement not met");
            GameEngine.Require(ctx.State.Wallet.TrySpend(area.UnlockCost), "not enough resources");
            ctx.State.City.UnlockedAreas.Add(area.Id);
            ctx.State.Count("city_areas_unlocked");
            ctx.Emit("area_unlocked", area.Id);
            return CommandResult.Success();
        }

        private static CommandResult HandleBuild(CityBuild cmd, CommandContext ctx)
        {
            var b = Owned(ctx.State, cmd.BlueprintId);
            GameEngine.Require(b != null, "blueprint not owned");
            var def = ctx.Content.Blueprint(b.Id);
            GameEngine.Require(def != null, "unknown blueprint");
            GameEngine.Require(!b.Placed, "already placed");
            string err = PlacementError(ctx.State, ctx.Content, def, cmd.X, cmd.Y);
            GameEngine.Require(err == null, err);
            if (b.Level == 0)
            {
                GameEngine.Require(b.TimerId == null, "construction already running");
                GameEngine.Require(ctx.State.Wallet.TrySpend(BuildCost(ctx.Content, def)), "not enough resources");
                b.Placed = true; b.X = cmd.X; b.Y = cmd.Y;
                var t = TimerSystem.Start(ctx, TimerBuild, b.Id, BuildDurationMs(ctx.State, ctx.Content, def, ctx.NowMs));
                b.TimerId = t.Id;
                ctx.Emit("building_placed", b.Id, cmd.X.ToString(), cmd.Y.ToString());
                TimerSystem.CompleteDue(ctx);
            }
            else
            {
                b.Placed = true; b.X = cmd.X; b.Y = cmd.Y;
                b.LastSettleMs = ctx.NowMs;
                ctx.Emit("building_placed", b.Id, cmd.X.ToString(), cmd.Y.ToString());
            }
            return CommandResult.Success();
        }

        private static CommandResult HandleMove(CityMove cmd, CommandContext ctx)
        {
            var b = Owned(ctx.State, cmd.BlueprintId);
            GameEngine.Require(b != null && b.Placed, "building not placed");
            var def = ctx.Content.Blueprint(b.Id);
            string err = PlacementError(ctx.State, ctx.Content, def, cmd.X, cmd.Y, b.Id);
            GameEngine.Require(err == null, err);
            b.X = cmd.X; b.Y = cmd.Y;
            ctx.Emit("building_moved", b.Id, cmd.X.ToString(), cmd.Y.ToString());
            return CommandResult.Success();
        }

        private static CommandResult HandleDemolish(CityDemolish cmd, CommandContext ctx)
        {
            var b = Owned(ctx.State, cmd.BlueprintId);
            GameEngine.Require(b != null && b.Placed, "building not placed");
            GameEngine.Require(b.TimerId == null, "cannot demolish while constructing or upgrading");
            var def = ctx.Content.Blueprint(b.Id);
            if (def?.ProduceCurrency != null) CollectInto(ctx, b, def, null);
            foreach (var a in Workers(ctx.State, b.Id)) a.WorkBuildingId = null;
            b.Placed = false;
            b.Stored = 0;
            ctx.Emit("building_demolished", b.Id);
            return CommandResult.Success();
        }

        private static CommandResult HandleUpgrade(CityUpgrade cmd, CommandContext ctx)
        {
            var b = Owned(ctx.State, cmd.BlueprintId);
            GameEngine.Require(IsActive(b), "building not active");
            GameEngine.Require(b.TimerId == null, "upgrade already running");
            GameEngine.Require(b.Level < MaxLevel(ctx.Content, b), "max level for current stars");
            var def = ctx.Content.Blueprint(b.Id);
            GameEngine.Require(ctx.State.Wallet.TrySpend(UpgradeCost(ctx.Content, def, b.Level)), "not enough resources");
            var t = TimerSystem.Start(ctx, TimerUpgrade, b.Id, UpgradeDurationMs(ctx.State, ctx.Content, def, b.Level, ctx.NowMs));
            b.TimerId = t.Id;
            TimerSystem.CompleteDue(ctx);
            return CommandResult.Success();
        }

        private static CommandResult HandleCollect(CityCollect cmd, CommandContext ctx)
        {
            var collected = new Dictionary<string, long>();
            if (!string.IsNullOrEmpty(cmd.BlueprintId))
            {
                var b = Owned(ctx.State, cmd.BlueprintId);
                GameEngine.Require(IsActive(b), "building not active");
                var def = ctx.Content.Blueprint(b.Id);
                GameEngine.Require(def?.ProduceCurrency != null, "not a production building");
                CollectInto(ctx, b, def, collected);
            }
            else
            {
                foreach (var b in SortedBlueprints(ctx.State))
                {
                    var def = ctx.Content.Blueprint(b.Id);
                    if (IsActive(b) && def?.ProduceCurrency != null) CollectInto(ctx, b, def, collected);
                }
            }
            return CommandResult.Success(collected);
        }

        private static void CollectInto(CommandContext ctx, OwnedBlueprint b, BlueprintDef def, Dictionary<string, long> collected)
        {
            long whole = (long)Math.Floor(b.Stored + 1e-9);
            if (whole <= 0) return;
            b.Stored = Math.Max(0, b.Stored - whole);
            ctx.State.Wallet.Add(def.ProduceCurrency, whole);
            ctx.State.Count("city_resources_collected", whole);
            if (collected != null) collected[def.ProduceCurrency] = (collected.TryGetValue(def.ProduceCurrency, out var c) ? c : 0) + whole;
            ctx.Emit("city_collected", b.Id, def.ProduceCurrency, whole.ToString());
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
            ctx.State.Count("blueprint_star_ups");
            ctx.Emit("blueprint_star_up", b.Id, b.Stars.ToString());
            return CommandResult.Success();
        }
    }
}
