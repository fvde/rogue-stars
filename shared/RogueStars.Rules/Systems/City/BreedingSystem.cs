using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Breeding Pen: two owned aliens (parents are kept) breed for Grain + time (by the higher parent rarity).
    /// Building boons: BreedingSlots (parallel jobs), BreedingSpeedPct (shorter timers; also the "breeding_speed"
    /// modifier), BreedingQualityPct (gene points added to the child + higher inherit/mutation chances).
    /// Flow: BreedStart → timer (kind "breeding", speed-ups via TimerSpeedUp) → BreedCollect (child rolled with
    /// ctx.Rng, becomes a pending choice) → BreedResolve Replace (child's genes/traits replace the current alien of
    /// its type; level, stars, squad and work assignment are kept) or Discard (shards + Grain refund).
    /// Child type: one of the parents' types (50/50).
    /// </summary>
    public static class BreedingSystem
    {
        public const string TimerKind = "breeding";

        public static void Register(GameEngine e)
        {
            e.Register<BreedStart>(HandleStart);
            e.Register<BreedCollect>(HandleCollect);
            e.Register<BreedResolve>(HandleResolve);
        }

        // ---- queries ----------------------------------------------------------------------------

        public static int Slots(GameState state, ContentDb content) =>
            (int)Math.Floor(CitySystem.BoonTotal(state, content, BoonKind.BreedingSlots) + 1e-9);

        public static double QualityBonus(GameState state, ContentDb content) => CitySystem.BoonTotal(state, content, BoonKind.BreedingQualityPct);

        private static Rarity PairRarity(ContentDb content, string a, string b) =>
            (Rarity)Math.Max((int)content.AlienType(a).Rarity, (int)content.AlienType(b).Rarity);

        public static long GrainCost(ContentDb content, string parentA, string parentB) =>
            content.BreedingTuning.GrainCost[(int)PairRarity(content, parentA, parentB)];

        public static long DurationMs(GameState state, ContentDb content, string parentA, string parentB, long nowMs)
        {
            double hours = content.BreedingTuning.DurationHours[(int)PairRarity(content, parentA, parentB)];
            double speed = (1 + CitySystem.BoonTotal(state, content, BoonKind.BreedingSpeedPct) / 100.0)
                * ModifierMath.Multiplier(state.Modifiers, "breeding_speed", nowMs);
            return (long)Math.Ceiling(hours * 3_600_000.0 / speed);
        }

        public static BreedingJob Job(GameState state, string id)
        {
            foreach (var j in state.Breeding.Jobs) if (j.Id == id) return j;
            return null;
        }

        public static PendingChild Pending(GameState state, string id)
        {
            foreach (var c in state.Breeding.Pending) if (c.Id == id) return c;
            return null;
        }

        /// <summary>
        /// Roll a child (deterministic for a given rng state). Genes: lerp(parentA, parentB, u) + uniform(±GeneVariance)
        /// + quality, clamped to [GeneMin, GeneMax]. Traits: each parent trait inherited with chance; then a
        /// mutation may add a new random trait (replacing a random one when full).
        /// </summary>
        public static PendingChild RollChild(Rng rng, ContentDb content, OwnedAlien a, OwnedAlien b, double quality)
        {
            var bt = content.BreedingTuning;
            var at = content.AlienTuning;
            var child = new PendingChild { ParentA = a.TypeId, ParentB = b.TypeId, TypeId = rng.Chance(0.5) ? a.TypeId : b.TypeId };
            child.Genes.Hp = Gene(rng, a.Genes.Hp, b.Genes.Hp, bt.GeneVariance, quality, at);
            child.Genes.Atk = Gene(rng, a.Genes.Atk, b.Genes.Atk, bt.GeneVariance, quality, at);
            child.Genes.Def = Gene(rng, a.Genes.Def, b.Genes.Def, bt.GeneVariance, quality, at);
            child.Genes.Speed = Gene(rng, a.Genes.Speed, b.Genes.Speed, bt.GeneVariance, quality, at);
            child.Genes.Work = Gene(rng, a.Genes.Work, b.Genes.Work, bt.GeneVariance, quality, at);

            var candidates = new List<string>();
            foreach (var t in a.Traits) if (!candidates.Contains(t)) candidates.Add(t);
            foreach (var t in b.Traits) if (!candidates.Contains(t)) candidates.Add(t);
            double inherit = Math.Min(1, bt.TraitInheritChance + quality * bt.InheritPerQuality);
            foreach (var t in candidates)
                if (rng.Chance(inherit) && child.Traits.Count < at.MaxTraits) child.Traits.Add(t);

            double mutation = Math.Min(1, bt.MutationChance + quality * bt.MutationPerQuality);
            if (rng.Chance(mutation))
            {
                string trait = AlienSystem.PickTrait(rng, content, content.AlienTraitOrder, child.Traits);
                if (trait != null)
                {
                    if (child.Traits.Count < at.MaxTraits) child.Traits.Add(trait);
                    else child.Traits[rng.Range(0, child.Traits.Count)] = trait;
                    child.Mutated = true;
                }
            }
            return child;
        }

        private static double Gene(Rng rng, double a, double b, double variance, double quality, AlienTuningDef t)
        {
            double mid = a + (b - a) * rng.NextDouble();
            double v = mid + (rng.NextDouble() * 2 - 1) * variance + quality;
            return Math.Round(Math.Max(t.GeneMin, Math.Min(t.GeneMax, v)), 2);
        }

        // ---- handlers ---------------------------------------------------------------------------

        private static CommandResult HandleStart(BreedStart cmd, CommandContext ctx)
        {
            var a = AlienSystem.Owned(ctx.State, cmd.ParentA);
            var b = AlienSystem.Owned(ctx.State, cmd.ParentB);
            GameEngine.Require(a != null && b != null, "parent not owned");
            GameEngine.Require(a.TypeId != b.TypeId, "parents must be two different aliens");
            int slots = Slots(ctx.State, ctx.Content);
            GameEngine.Require(slots > 0, "no Breeding Pen");
            GameEngine.Require(ctx.State.Breeding.Jobs.Count < slots, "all breeding slots busy");
            GameEngine.Require(ctx.State.Wallet.TrySpend(Currencies.Grain, GrainCost(ctx.Content, a.TypeId, b.TypeId)), "not enough grain");

            var bs = ctx.State.Breeding;
            bs.NextId++;
            var job = new BreedingJob
            {
                Id = "breed" + bs.NextId, ParentA = a.TypeId, ParentB = b.TypeId, StartMs = ctx.NowMs,
                QualityBonus = QualityBonus(ctx.State, ctx.Content),
            };
            job.TimerId = TimerSystem.Start(ctx, TimerKind, job.Id, DurationMs(ctx.State, ctx.Content, a.TypeId, b.TypeId, ctx.NowMs)).Id;
            bs.Jobs.Add(job);
            ctx.State.Count("breeds_started");
            ctx.Emit("breed_started", job.Id, a.TypeId, b.TypeId);
            return CommandResult.Success(job.Id);
        }

        private static CommandResult HandleCollect(BreedCollect cmd, CommandContext ctx)
        {
            var job = Job(ctx.State, cmd.JobId);
            GameEngine.Require(job != null, "unknown breeding job");
            GameEngine.Require(TimerSystem.Get(ctx.State, job.TimerId) == null || TimerSystem.IsDone(ctx.State, job.TimerId, ctx.NowMs), "breeding not finished");
            GameEngine.Require(ctx.State.Breeding.Pending.Count < ctx.Content.BreedingTuning.MaxPending, "too many children waiting for a decision");
            var a = AlienSystem.Owned(ctx.State, job.ParentA);
            var b = AlienSystem.Owned(ctx.State, job.ParentB);
            GameEngine.Require(a != null && b != null, "parent missing");

            var child = RollChild(ctx.Rng, ctx.Content, a, b, job.QualityBonus);
            child.Id = job.Id;
            TimerSystem.Remove(ctx.State, job.TimerId);
            ctx.State.Breeding.Jobs.Remove(job);
            ctx.State.Breeding.Pending.Add(child);
            ctx.State.Count("breeds_collected");
            if (child.Mutated) ctx.State.Count("breeding_mutations");
            ctx.Emit("breed_born", child.Id, child.TypeId);
            return CommandResult.Success(child.Id);
        }

        private static CommandResult HandleResolve(BreedResolve cmd, CommandContext ctx)
        {
            var child = Pending(ctx.State, cmd.ChildId);
            GameEngine.Require(child != null, "unknown child");
            var current = AlienSystem.Owned(ctx.State, child.TypeId);
            switch (cmd.Keep)
            {
                case BreedKeep.Replace:
                    GameEngine.Require(current != null, "alien type not owned");
                    current.Genes = child.Genes.Copy();
                    current.Traits = new List<string>(child.Traits);
                    current.Generation++;
                    ctx.State.Count("breeds_kept");
                    ctx.Emit("breed_replaced", child.TypeId);
                    break;
                case BreedKeep.Discard:
                {
                    var bt = ctx.Content.BreedingTuning;
                    var def = ctx.Content.AlienType(child.TypeId);
                    long shards = bt.DiscardShards[(int)def.Rarity];
                    long grain = (long)Math.Floor(GrainCost(ctx.Content, child.ParentA, child.ParentB) * bt.DiscardGrainRefundPct / 100.0);
                    ctx.Grant(Reward.Cur(Currencies.AlienShards(child.TypeId), shards));
                    ctx.Grant(Reward.Cur(Currencies.Grain, grain));
                    ctx.State.Count("breeds_discarded");
                    ctx.Emit("breed_discarded", child.TypeId);
                    break;
                }
                default:
                    throw new RuleException("unknown choice");
            }
            ctx.State.Breeding.Pending.Remove(child);
            return CommandResult.Success();
        }
    }
}
