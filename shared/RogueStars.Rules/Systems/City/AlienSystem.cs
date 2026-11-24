using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Aliens: one alien per alien type (species). Aliens pass (owner-approved review rev 4.3): an alien is a sidekick with NO stats —
    /// its one ability works in % of the worm's ATK / max HP; stars 1–10 (shards from duplicates and breeding) are its only
    /// progression (every star is its own ability step; tier stars 3/5/7/10 change the effect); traits shape its tempo or effect;
    /// genes are stored and bred but inactive ("coming soon"). Alien levels were removed: an old save's levels are refunded in Grain
    /// (<see cref="Migrate"/>).
    ///
    /// Work vs squad (design decision): an alien can work in one building AND be in the squad at the same time;
    /// aliens in the active squad work at AlienTuning.SquadWorkFactor (50%). An alien works in at most one building.
    /// Squad size is limited by CitySystem.SquadSlots (0 until the first housing building is built).
    /// </summary>
    public static class AlienSystem
    {
        public const int MaxStars = 10;
        /// <summary>The save migration version this build applies (AlienState.Migrated).</summary>
        public const int MigrationVersion = 1;

        /// <summary>Species whose rarity changed in the aliens pass: the rarity their removed levels were paid at (Grain refund).</summary>
        public static readonly Dictionary<string, Rarity> LegacyLevelRarity = new Dictionary<string, Rarity> { ["alien_emberkit"] = Rarity.Rare };

        public static void Register(GameEngine e)
        {
            e.Register<AlienStarUp>(HandleStarUp);
            e.Register<AlienAssign>(HandleAssign);
            e.Register<AlienSetSquad>(HandleSetSquad);
            e.RegisterSettler(Migrate);
            e.RegisterNewPlayer(ctx => ctx.State.Aliens.Migrated = MigrationVersion); // a new save has nothing to migrate
            e.RegisterLoadoutContributor(ContributeLoadout);
            e.Rewards.Register(RewardKinds.Alien, GrantAlien);
        }

        // ---- queries ----------------------------------------------------------------------------

        public static OwnedAlien Owned(GameState state, string typeId) =>
            typeId != null && state.Aliens.Owned.TryGetValue(typeId, out var a) ? a : null;

        /// <summary>Shards needed for the next star (the shared table, ContentDb.ShardStars), or 0 at max.</summary>
        public static long StarCost(ContentDb content, OwnedAlien a) => a.Stars >= MaxStars ? 0 : Math.Max(0, content.ShardStars.NextCost(a.Stars));

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

        /// <summary>
        /// Combat stats (aliens pass): only Speed (species Speed, + Swift) and a Vampiric lifesteal — no HP / ATK / DEF, genes inactive.
        /// </summary>
        public static StatBlock CombatStats(ContentDb content, OwnedAlien a)
        {
            var def = content.AlienType(a.TypeId);
            var s = new StatBlock();
            s.Add(new StatMod(StatId.Speed, def != null ? def.BaseSpeed : 0));
            foreach (var tid in a.Traits)
            {
                var tr = content.AlienTrait(tid);
                if (tr != null && tr.Kind != AlienTraitKind.Legacy && tr.Kind != AlienTraitKind.Work) s.AddAll(tr.CombatStats);
            }
            return s;
        }

        /// <summary>Average rounds between the alien's own actions (Speed: species × Swift): "acts about every 3 turns".</summary>
        public static double ActsEvery(ContentDb content, OwnedAlien a) => BattleSimulator.SidekickActsEvery(CombatStats(content, a).Value(StatId.Speed), content);

        /// <summary>The ability step of a star (1..10) of a species.</summary>
        public static string AbilityAt(ContentDb content, string typeId, int stars)
        {
            var def = content.AlienType(typeId);
            if (def == null) return null;
            string id = null;
            foreach (var step in def.ArchetypeSkills) if (stars >= step.Stars) id = step.SkillId;
            return id;
        }

        /// <summary>Active ability (the step of the alien's stars). Earlier steps are replaced.</summary>
        public static string ArchetypeSkillId(ContentDb content, OwnedAlien a) => AbilityAt(content, a.TypeId, a.Stars);

        /// <summary>True if the star is a tier star (1/3/5/7/10): the ability's effect may change there, not only its main %.</summary>
        public static bool IsTierStar(ContentDb content, int star) => Array.IndexOf(content.AlienTuning.SkillStars, star) >= 0;

        /// <summary>Skill ids the alien uses in combat.</summary>
        public static List<string> SkillIds(ContentDb content, OwnedAlien a)
        {
            var list = new List<string>();
            var arch = ArchetypeSkillId(content, a);
            if (arch != null) list.Add(arch);
            return list;
        }

        /// <summary>Work power in percent before trait bonuses and squad factor: base × star growth (genes inactive).</summary>
        public static double WorkPower(ContentDb content, OwnedAlien a)
        {
            var def = content.AlienType(a.TypeId);
            var t = content.AlienTuning;
            return def.BaseWork * (1 + t.StarWorkPct * (a.Stars - 1) / 100.0);
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

        /// <summary>Roll a brand-new alien of a type (genes uniform ±InitialGeneSpread, stored but inactive; traits from its pool).</summary>
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

        /// <summary>
        /// Weighted pick from a pool (content order), excluding traits already present, legacy traits and a second tempo trait (Swift
        /// or Eager); null if none.
        /// </summary>
        public static string PickTrait(Rng rng, ContentDb content, IList<string> pool, ICollection<string> exclude)
        {
            bool tempo = false;
            foreach (var id in exclude) if (content.AlienTrait(id)?.Kind == AlienTraitKind.Tempo) tempo = true;
            var ids = new List<string>();
            var weights = new List<double>();
            foreach (var id in pool)
            {
                var tr = content.AlienTrait(id);
                if (tr == null || tr.Weight <= 0 || tr.Kind == AlienTraitKind.Legacy || exclude.Contains(id) || ids.Contains(id)) continue;
                if (tempo && tr.Kind == AlienTraitKind.Tempo) continue;
                ids.Add(id); weights.Add(tr.Weight);
            }
            int i = rng.PickWeighted(weights);
            return i < 0 ? null : ids[i];
        }

        /// <summary>
        /// A trait as the species has it (aliens pass): work traits and traits of the species' pool stay; a legacy trait becomes the
        /// first of its remap targets in the pool that is still free (Tough → Bulwark or Lingering …); any other combat trait (from another
        /// species, a mutation or a legacy remap outside the pool) becomes the pool's first free trait of the same kind (effect / tempo).
        /// Null if the pool has none of that kind.
        /// </summary>
        public static string ConvertTrait(ContentDb content, AlienTypeDef species, string traitId, ICollection<string> taken = null)
        {
            var tr = content.AlienTrait(traitId);
            if (tr == null || species == null) return null;
            if (tr.Kind == AlienTraitKind.Work || species.PossibleTraits.Contains(traitId)) return traitId;
            bool Free(string id) => taken == null || !taken.Contains(id);
            if (tr.Kind == AlienTraitKind.Legacy)
            {
                foreach (var to in tr.RemapTo) if (species.PossibleTraits.Contains(to) && Free(to)) return to;
                foreach (var to in tr.RemapTo) if (species.PossibleTraits.Contains(to)) return to;
            }
            bool tempo = tr.Kind == AlienTraitKind.Tempo || (tr.Kind == AlienTraitKind.Legacy && tr.RemapTo.Contains(ContentDb.TraitSwift));
            var kind = tempo ? AlienTraitKind.Tempo : AlienTraitKind.Effect;
            foreach (bool free in new[] { true, false })
                foreach (var id in species.PossibleTraits)
                    if (content.AlienTrait(id)?.Kind == kind && (!free || Free(id))) return id;
            return null;
        }

        /// <summary>
        /// A trait list as the species has it (<see cref="ConvertTrait"/>, in order): traits that already fit keep their place, the others
        /// convert into what is still free; duplicates dropped, at most one tempo trait.
        /// </summary>
        public static List<string> ConvertTraits(ContentDb content, AlienTypeDef species, IEnumerable<string> traits)
        {
            var src = new List<string>(traits);
            var slot = new string[src.Count];
            var taken = new List<string>();
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < src.Count; i++)
                {
                    var tr = content.AlienTrait(src[i]);
                    bool fits = tr != null && (tr.Kind == AlienTraitKind.Work || species.PossibleTraits.Contains(src[i]));
                    if (fits != (pass == 0)) continue;
                    var to = ConvertTrait(content, species, src[i], taken);
                    if (to == null || taken.Contains(to)) continue;
                    bool isTempo = content.AlienTrait(to)?.Kind == AlienTraitKind.Tempo;
                    bool hasTempo = false;
                    foreach (var t in taken) if (content.AlienTrait(t)?.Kind == AlienTraitKind.Tempo) hasTempo = true;
                    if (isTempo && hasTempo) continue;
                    slot[i] = to;
                    taken.Add(to);
                }
            var list = new List<string>();
            foreach (var t in slot) if (t != null) list.Add(t);
            return list;
        }

        /// <summary>Grain an old save paid for an alien's removed levels (level 1 → its level, at the rarity it had then).</summary>
        public static long LegacyLevelGrain(ContentDb content, string typeId, int level)
        {
            var def = content.AlienType(typeId);
            if (def == null || level <= 1) return 0;
            var t = content.AlienTuning;
            var rarity = LegacyLevelRarity.TryGetValue(typeId, out var r) ? r : def.Rarity;
            long sum = 0;
            for (int l = 1; l < level; l++) sum += (long)Math.Round(t.LegacyLevelGrainBase[(int)rarity] * Math.Pow(t.LegacyLevelGrainGrowth, l - 1));
            return sum;
        }

        // ---- save migration -------------------------------------------------------------------------

        /// <summary>
        /// Aliens pass migration (settler, once per save): alien levels are removed — each alien's level goes back to 1 and 100% of the
        /// Grain spent on it comes back (straight to the wallet: it was the player's); old combat traits are remapped by species on
        /// owned aliens and waiting newborns. Stars, shards, genes (inactive), squad and work are kept.
        /// </summary>
        public static void Migrate(CommandContext ctx)
        {
            var st = ctx.State.Aliens;
            if (st.Migrated >= MigrationVersion) return;
            var c = ctx.Content;
            long refund = 0;
            var ids = new List<string>(st.Owned.Keys);
            ids.Sort(string.CompareOrdinal);
            foreach (var id in ids)
            {
                var a = st.Owned[id];
                if (a.Level > 1) { refund += LegacyLevelGrain(c, a.TypeId, a.Level); a.Level = 1; }
                var def = c.AlienType(a.TypeId);
                if (def != null) a.Traits = ConvertTraits(c, def, a.Traits);
            }
            foreach (var child in ctx.State.Breeding.Pending)
            {
                var def = c.AlienType(child.TypeId);
                if (def != null) child.Traits = ConvertTraits(c, def, child.Traits);
            }
            st.Migrated = MigrationVersion;
            if (refund <= 0) return;
            ctx.State.Wallet.Add(Currencies.Grain, refund);
            st.LevelGrainRefunded += refund;
            ctx.Emit("alien_levels_refunded", refund.ToString());
        }

        // ---- engine hooks -----------------------------------------------------------------------

        /// <summary>
        /// Alien granter (pull system, docs/design/pull-system.md): every copy is one batch of ShardStars.BatchShards shards; the
        /// batch of a type the player does not own unlocks it (a new alien with rolled genes / traits, the batch's shards spent).
        /// </summary>
        private static void GrantAlien(CommandContext ctx, Reward r)
        {
            if (RewardWildcard.IsWildcard(r))
            {
                for (long i = 0; i < r.Amount; i++)
                    GrantAlien(ctx, new Reward(r.Kind, RewardWildcard.Pick(ctx.Rng, ctx.Content.AlienTypes, d => (int)d.Rarity, r.Rarity), 1, r.Rarity));
                return;
            }
            var def = ctx.Content.AlienType(r.Id);
            if (def == null) throw new RuleException("unknown alien type " + r.Id);
            long copies = r.Amount;
            if (!ctx.State.Aliens.Owned.ContainsKey(def.Id))
            {
                Unlock(ctx, def);
                copies--;
            }
            if (copies > 0)
            {
                long shards = copies * ctx.Content.ShardStars.BatchShards;
                ctx.State.Wallet.Add(Currencies.AlienShards(def.Id), shards);
                ctx.State.Count("alien_duplicates", copies);
                ctx.Emit("alien_shards", def.Id, shards.ToString());
            }
        }

        /// <summary>A new alien of a type the player does not own (its unlock batch: the shards are paid and spent at once).</summary>
        private static void Unlock(CommandContext ctx, AlienTypeDef def)
        {
            ctx.State.Aliens.Owned[def.Id] = RollNew(ctx.Rng, ctx.Content, def);
            ctx.State.Count("aliens_obtained");
            ctx.Emit("alien_new", def.Id);
        }

        /// <summary>
        /// Unlock an alien from loose shards (Shard Reactor output): when the type is not owned and its shards reach UnlockShards, they
        /// are spent and the alien is new. True if it unlocked.
        /// </summary>
        public static bool TryUnlockFromShards(CommandContext ctx, string typeId)
        {
            var def = ctx.Content.AlienType(typeId);
            if (def == null || Owned(ctx.State, typeId) != null) return false;
            if (!ctx.State.Wallet.TrySpend(Currencies.AlienShards(typeId), ctx.Content.ShardStars.UnlockShards)) return false;
            Unlock(ctx, def);
            return true;
        }

        private static void ContributeLoadout(GameState state, ContentDb content, long nowMs, CombatLoadout loadout)
        {
            foreach (var id in ActiveSquad(state, content))
            {
                var a = Owned(state, id);
                var def = content.AlienType(id);
                if (a == null || def == null) continue;
                var ca = new CombatAlien { AlienTypeId = id, Name = def.Name, Stats = CombatStats(content, a), Stars = a.Stars };
                ca.SkillIds.AddRange(SkillIds(content, a));
                foreach (var t in ConvertTraits(content, def, a.Traits))
                    if (content.AlienTrait(t)?.Kind != AlienTraitKind.Work) ca.Traits.Add(t);
                loadout.Aliens.Add(ca);
            }
        }

        // ---- handlers ---------------------------------------------------------------------------

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
