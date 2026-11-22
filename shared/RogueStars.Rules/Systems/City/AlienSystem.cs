using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Aliens: one alien per alien type (species). Each has genes (stat quality), up to 3 traits, level (Grain)
    /// and stars 1–10 (shards from duplicates; archetype skill upgrades at stars 3/5/7/10).
    ///
    /// Work vs squad (design decision): an alien can work in one building AND be in the squad at the same time;
    /// aliens in the active squad work at AlienTuning.SquadWorkFactor (50%). An alien works in at most one building.
    /// Squad size is limited by CitySystem.SquadSlots (0 until the first housing building is built).
    /// </summary>
    public static class AlienSystem
    {
        public const int MaxStars = 10;

        public static void Register(GameEngine e)
        {
            e.Register<AlienLevelUp>(HandleLevelUp);
            e.Register<AlienStarUp>(HandleStarUp);
            e.Register<AlienAssign>(HandleAssign);
            e.Register<AlienSetSquad>(HandleSetSquad);
            e.RegisterLoadoutContributor(ContributeLoadout);
            e.Rewards.Register(RewardKinds.Alien, GrantAlien);
        }

        // ---- queries ----------------------------------------------------------------------------

        public static OwnedAlien Owned(GameState state, string typeId) =>
            typeId != null && state.Aliens.Owned.TryGetValue(typeId, out var a) ? a : null;

        public static int MaxLevel(ContentDb content, OwnedAlien a) => content.AlienTuning.MaxLevelBase + content.AlienTuning.MaxLevelPerStar * a.Stars;

        /// <summary>Grain to go from the alien's current level to the next.</summary>
        public static long LevelCost(ContentDb content, OwnedAlien a)
        {
            var def = content.AlienType(a.TypeId);
            var t = content.AlienTuning;
            return (long)Math.Round(t.LevelGrainBase[(int)def.Rarity] * Math.Pow(t.LevelGrainGrowth, a.Level - 1));
        }

        /// <summary>Shards needed for the next star, or 0 at max.</summary>
        public static long StarCost(ContentDb content, OwnedAlien a)
        {
            var c = content.AlienTuning.StarCosts;
            return a.Stars >= MaxStars || a.Stars - 1 >= c.Length ? 0 : c[a.Stars - 1];
        }

        /// <summary>Active squad: the first SquadSlots owned entries of the squad list.</summary>
        public static List<string> ActiveSquad(GameState state, ContentDb content)
        {
            int slots = CitySystem.SquadSlots(state, content);
            var list = new List<string>();
            foreach (var id in state.Aliens.Squad)
            {
                if (list.Count >= slots) break;
                if (Owned(state, id) != null && !list.Contains(id)) list.Add(id);
            }
            return list;
        }

        /// <summary>Combat stats: base × (1+gene%) × level growth × star growth (speed: gene only) + combat traits.</summary>
        public static StatBlock CombatStats(ContentDb content, OwnedAlien a)
        {
            var def = content.AlienType(a.TypeId);
            var t = content.AlienTuning;
            double grow = (1 + t.LevelCombatPct * (a.Level - 1) / 100.0) * (1 + t.StarCombatPct * (a.Stars - 1) / 100.0);
            var s = new StatBlock();
            s.Add(new StatMod(StatId.Hp, def.BaseHp * (1 + a.Genes.Hp / 100.0) * grow));
            s.Add(new StatMod(StatId.Atk, def.BaseAtk * (1 + a.Genes.Atk / 100.0) * grow));
            s.Add(new StatMod(StatId.Def, def.BaseDef * (1 + a.Genes.Def / 100.0) * grow));
            s.Add(new StatMod(StatId.Speed, def.BaseSpeed * (1 + a.Genes.Speed / 100.0)));
            foreach (var tid in a.Traits)
            {
                var tr = content.AlienTrait(tid);
                if (tr != null) s.AddAll(tr.CombatStats);
            }
            return s;
        }

        /// <summary>Active archetype skill (highest step reached by stars). Earlier steps are replaced.</summary>
        public static string ArchetypeSkillId(ContentDb content, OwnedAlien a)
        {
            var def = content.AlienType(a.TypeId);
            string id = null;
            foreach (var step in def.ArchetypeSkills) if (a.Stars >= step.Stars) id = step.SkillId;
            return id;
        }

        /// <summary>Skill ids the alien uses in combat.</summary>
        public static List<string> SkillIds(ContentDb content, OwnedAlien a)
        {
            var list = new List<string>();
            var arch = ArchetypeSkillId(content, a);
            if (arch != null) list.Add(arch);
            return list;
        }

        /// <summary>Work power in percent before trait bonuses and squad factor.</summary>
        public static double WorkPower(ContentDb content, OwnedAlien a)
        {
            var def = content.AlienType(a.TypeId);
            var t = content.AlienTuning;
            return def.BaseWork * (1 + a.Genes.Work / 100.0)
                * (1 + t.LevelWorkPct * (a.Level - 1) / 100.0)
                * (1 + t.StarWorkPct * (a.Stars - 1) / 100.0);
        }

        /// <summary>Production bonus (percent) of this alien when working on <paramref name="currency"/>.</summary>
        public static double WorkBonusPct(GameState state, ContentDb content, OwnedAlien a, string currency)
        {
            double v = WorkPower(content, a);
            foreach (var tid in a.Traits)
            {
                var tr = content.AlienTrait(tid);
                if (tr == null || tr.WorkPct == 0) continue;
                if (tr.WorkCurrencies.Count == 0 || tr.WorkCurrencies.Contains(currency)) v += tr.WorkPct;
            }
            if (ActiveSquad(state, content).Contains(a.TypeId)) v *= content.AlienTuning.SquadWorkFactor;
            return v;
        }

        /// <summary>Roll a brand-new alien of a type (genes uniform ±InitialGeneSpread, traits by TraitChances).</summary>
        public static OwnedAlien RollNew(Rng rng, ContentDb content, AlienTypeDef def)
        {
            var t = content.AlienTuning;
            var a = new OwnedAlien { TypeId = def.Id };
            a.Genes.Hp = RollGene(rng, t.InitialGeneSpread);
            a.Genes.Atk = RollGene(rng, t.InitialGeneSpread);
            a.Genes.Def = RollGene(rng, t.InitialGeneSpread);
            a.Genes.Speed = RollGene(rng, t.InitialGeneSpread);
            a.Genes.Work = RollGene(rng, t.InitialGeneSpread);
            for (int i = 0; i < t.TraitChances.Length && i < t.MaxTraits; i++)
            {
                if (!rng.Chance(t.TraitChances[i])) break;
                string trait = PickTrait(rng, content, def.PossibleTraits, a.Traits);
                if (trait == null) break;
                a.Traits.Add(trait);
            }
            return a;
        }

        private static double RollGene(Rng rng, double spread) => Math.Round((rng.NextDouble() * 2 - 1) * spread, 2);

        /// <summary>Weighted pick from a pool (content order), excluding traits already present; null if none.</summary>
        public static string PickTrait(Rng rng, ContentDb content, IList<string> pool, ICollection<string> exclude)
        {
            var ids = new List<string>();
            var weights = new List<double>();
            foreach (var id in pool)
            {
                var tr = content.AlienTrait(id);
                if (tr == null || exclude.Contains(id) || ids.Contains(id)) continue;
                ids.Add(id); weights.Add(tr.Weight);
            }
            int i = rng.PickWeighted(weights);
            return i < 0 ? null : ids[i];
        }

        // ---- engine hooks -----------------------------------------------------------------------

        /// <summary>Alien granter: new type = new alien with rolled genes/traits; existing type → shards.</summary>
        private static void GrantAlien(CommandContext ctx, Reward r)
        {
            var def = ctx.Content.AlienType(r.Id);
            if (def == null) throw new RuleException("unknown alien type " + r.Id);
            long copies = r.Amount;
            if (!ctx.State.Aliens.Owned.ContainsKey(def.Id))
            {
                ctx.State.Aliens.Owned[def.Id] = RollNew(ctx.Rng, ctx.Content, def);
                ctx.State.Count("aliens_obtained");
                ctx.Emit("alien_new", def.Id);
                copies--;
            }
            if (copies > 0)
            {
                long shards = copies * ctx.Content.AlienTuning.ShardsPerDuplicate[(int)def.Rarity];
                ctx.State.Wallet.Add(Currencies.AlienShards(def.Id), shards);
                ctx.State.Count("alien_duplicates", copies);
                ctx.Emit("alien_shards", def.Id, shards.ToString());
            }
        }

        private static void ContributeLoadout(GameState state, ContentDb content, long nowMs, CombatLoadout loadout)
        {
            foreach (var id in ActiveSquad(state, content))
            {
                var a = Owned(state, id);
                var def = content.AlienType(id);
                if (a == null || def == null) continue;
                var ca = new CombatAlien { AlienTypeId = id, Name = def.Name, Stats = CombatStats(content, a) };
                ca.SkillIds.AddRange(SkillIds(content, a));
                loadout.Aliens.Add(ca);
            }
        }

        // ---- handlers ---------------------------------------------------------------------------

        private static CommandResult HandleLevelUp(AlienLevelUp cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Aliens);
            var a = Owned(ctx.State, cmd.AlienTypeId);
            GameEngine.Require(a != null, "alien not owned");
            GameEngine.Require(a.Level < MaxLevel(ctx.Content, a), "max level for current stars");
            GameEngine.Require(ctx.State.Wallet.TrySpend(Currencies.Grain, LevelCost(ctx.Content, a)), "not enough grain");
            a.Level++;
            ctx.State.Count("alien_level_ups");
            ctx.Emit("alien_level_up", a.TypeId, a.Level.ToString());
            return CommandResult.Success();
        }

        private static CommandResult HandleStarUp(AlienStarUp cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Aliens);
            var a = Owned(ctx.State, cmd.AlienTypeId);
            GameEngine.Require(a != null, "alien not owned");
            GameEngine.Require(a.Stars < MaxStars, "max stars");
            GameEngine.Require(ctx.State.Wallet.TrySpend(Currencies.AlienShards(a.TypeId), StarCost(ctx.Content, a)), "not enough shards");
            a.Stars++;
            ctx.State.Count("alien_star_ups");
            ctx.Emit("alien_star_up", a.TypeId, a.Stars.ToString());
            return CommandResult.Success();
        }

        private static CommandResult HandleAssign(AlienAssign cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Aliens);
            var a = Owned(ctx.State, cmd.AlienTypeId);
            GameEngine.Require(a != null, "alien not owned");
            if (string.IsNullOrEmpty(cmd.BlueprintId))
            {
                a.WorkBuildingId = null;
                ctx.Emit("alien_unassigned", a.TypeId);
                return CommandResult.Success();
            }
            var b = CitySystem.Owned(ctx.State, cmd.BlueprintId);
            GameEngine.Require(CitySystem.IsActive(b), "building not active");
            var def = ctx.Content.Blueprint(b.Id);
            int slots = CitySystem.WorkerSlots(ctx.Content, def, b);
            GameEngine.Require(slots > 0, "building has no worker slots");
            GameEngine.Require(a.WorkBuildingId != b.Id, "already working there");
            GameEngine.Require(CitySystem.Workers(ctx.State, b.Id).Count < slots, "no free worker slot");
            a.WorkBuildingId = b.Id;
            ctx.State.Count("alien_assignments");
            ctx.Emit("alien_assigned", a.TypeId, b.Id);
            return CommandResult.Success();
        }

        private static CommandResult HandleSetSquad(AlienSetSquad cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.AlienSquad);
            var ids = cmd.AlienTypeIds ?? new List<string>();
            GameEngine.Require(ids.Count <= CitySystem.SquadSlots(ctx.State, ctx.Content), "not enough squad slots");
            var seen = new HashSet<string>();
            foreach (var id in ids)
            {
                GameEngine.Require(Owned(ctx.State, id) != null, "alien not owned");
                GameEngine.Require(seen.Add(id), "duplicate alien in squad");
            }
            ctx.State.Aliens.Squad = new List<string>(ids);
            ctx.Emit("squad_set", string.Join(",", ids));
            return CommandResult.Success();
        }
    }
}
