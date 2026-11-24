using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Heroes (worm personas, all Mythic): unlock by 50 shards, stars 1–10 on the shared shard table (ContentDb.ShardStars), level by
    /// Hero XP (worm stats only, one shared budget), a hand-written kit (1★ Trait + Ability, 3★ a number, 5★ / 7★ clauses, 10★
    /// capstone), one equipped hero, collection bonus. Granter for RewardKinds.HeroShards and RewardKinds.Hero (a hero batch from
    /// summon events: 50 shards; the batch of a hero the player does not own unlocks it at 1★; a batch for a 10★ hero becomes
    /// HeroConfig.CopyOverflow). Stars are raised with HeroStarUp.
    /// </summary>
    public static class HeroSystem
    {
        public static void Register(GameEngine e)
        {
            e.Register<HeroUnlock>(HandleUnlock);
            e.Register<HeroStarUp>(HandleStarUp);
            e.Register<HeroLevelUp>(HandleLevelUp);
            e.Register<HeroEquip>(HandleEquip);
            e.Rewards.Register(RewardKinds.HeroShards, (ctx, r) =>
            {
                GameEngine.Require(ctx.Content.HeroConfig.Defs.ContainsKey(r.Id), "unknown hero " + r.Id);
                ctx.State.Wallet.Add(Currencies.HeroShards(r.Id), r.Amount);
                ctx.State.Count("hero_shards_obtained", r.Amount);
            });
            e.Rewards.Register(RewardKinds.Hero, GrantCopies);
            e.RegisterLoadoutContributor(Contribute);
        }

        // ---- queries -------------------------------------------------------------------------------

        public static HeroEntry Get(GameState s, string heroId) => heroId != null && s.Heroes.Owned.TryGetValue(heroId, out var h) ? h : null;

        /// <summary>Shards needed for the next star (or to unlock when not owned); -1 at max stars.</summary>
        public static long NextStarCost(GameState s, ContentDb c, string heroId)
        {
            var h = Get(s, heroId);
            return h == null ? c.ShardStars.UnlockShards : h.Stars >= c.HeroConfig.MaxStars ? -1 : c.ShardStars.NextCost(h.Stars);
        }

        /// <summary>Total shards from nothing to the given star.</summary>
        public static long TotalShardsToStar(ContentDb c, int stars) => c.ShardStars.TotalTo(stars);

        /// <summary>Trait skill ids active at the given star count.</summary>
        public static List<string> TraitSkills(ContentDb c, string heroId, int stars)
        {
            var def = c.HeroConfig.Defs[heroId];
            return ProgressionUtil.ResolveSkillSteps(def.Traits, st => st.Star <= stars);
        }

        /// <summary>The hero's kit lines unlocked at the given stars (Trait, Ability, then the 3★ number and the clauses).</summary>
        public static List<KitText> KitAt(ContentDb c, string heroId, int stars)
        {
            var list = new List<KitText>();
            foreach (var line in c.HeroConfig.Defs[heroId].Kit) if (line.Star <= stars) list.Add(line);
            return list;
        }

        /// <summary>The next kit line above the given stars (null at the last one).</summary>
        public static KitText NextKit(ContentDb c, string heroId, int stars)
        {
            foreach (var line in c.HeroConfig.Defs[heroId].Kit) if (line.Star > stars) return line;
            return null;
        }

        /// <summary>Worm stats granted by the hero (base × star bonus + level growth).</summary>
        public static StatBlock HeroStats(ContentDb c, string heroId, HeroEntry h)
        {
            var cfg = c.HeroConfig; var def = cfg.Defs[heroId];
            double star = cfg.StarStatPercent[Math.Min(h.Stars, cfg.StarStatPercent.Count - 1)];
            var b = ProgressionUtil.Scaled(def.BaseStats, 1 + star / 100.0);
            b.AddBlock(ProgressionUtil.Scaled(def.StatsPerLevel, h.Level - 1));
            return b;
        }

        /// <summary>Collection bonus: CollectionPerStar × total stars of all unlocked heroes.</summary>
        public static StatBlock CollectionStats(GameState s, ContentDb c)
        {
            int stars = 0;
            foreach (var kv in s.Heroes.Owned) stars += kv.Value.Stars;
            return ProgressionUtil.Scaled(c.HeroConfig.CollectionPerStar, stars);
        }

        private static void Contribute(GameState s, ContentDb c, long now, CombatLoadout l)
        {
            l.WormStats.AddBlock(CollectionStats(s, c));
            var h = Get(s, s.Heroes.Equipped);
            if (h == null) return;
            l.HeroId = s.Heroes.Equipped;
            l.WormStats.AddBlock(HeroStats(c, s.Heroes.Equipped, h));
            foreach (var sk in TraitSkills(c, s.Heroes.Equipped, h.Stars)) if (!l.MetaSkillIds.Contains(sk)) l.MetaSkillIds.Add(sk);
        }

        // ---- batches (summon events) -------------------------------------------------------------

        /// <summary>What the next batch of a hero does: "new" (unlocks it), "shards" (+50 shards) or "overflow" (10★).</summary>
        public static string CopyEffect(GameState s, ContentDb c, string heroId)
        {
            var h = Get(s, heroId);
            return h == null ? "new" : h.Stars < c.HeroConfig.MaxStars ? "shards" : "overflow";
        }

        /// <summary>
        /// A hero batch (RewardKinds.Hero, Amount = batches): ShardStars.BatchShards shards each; a hero the player does not own is
        /// unlocked at 1★ with them; a batch for a 10★ hero becomes HeroConfig.CopyOverflow.
        /// </summary>
        private static void GrantCopies(CommandContext ctx, Reward r)
        {
            var s = ctx.State; var c = ctx.Content; var cfg = c.HeroConfig;
            GameEngine.Require(cfg.Defs.ContainsKey(r.Id), "unknown hero " + r.Id);
            for (long i = 0; i < r.Amount; i++)
            {
                s.Count("hero_copies");
                var h = Get(s, r.Id);
                if (h != null && h.Stars >= cfg.MaxStars)
                {
                    if (cfg.CopyOverflow != null) ctx.Grant(cfg.CopyOverflow);
                    continue;
                }
                s.Wallet.Add(Currencies.HeroShards(r.Id), c.ShardStars.BatchShards);
                s.Count("hero_shards_obtained", c.ShardStars.BatchShards);
                if (h == null) Unlock(ctx, r.Id);
            }
        }

        /// <summary>Unlock a hero at 1★ with UnlockShards of its shards (the caller checked it is not owned).</summary>
        private static void Unlock(CommandContext ctx, string heroId)
        {
            var s = ctx.State;
            ProgressionUtil.Spend(s, Currencies.HeroShards(heroId), ctx.Content.ShardStars.UnlockShards);
            s.Heroes.Owned[heroId] = new HeroEntry();
            if (string.IsNullOrEmpty(s.Heroes.Equipped)) s.Heroes.Equipped = heroId;
            s.Count("heroes_unlocked");
            ProgressionUtil.CountMax(s, "hero_stars_max", 1);
            ctx.Emit("hero_unlocked", heroId);
        }

        // ---- handlers ------------------------------------------------------------------------------

        private static HeroDef RequireDef(ContentDb c, string id)
        {
            GameEngine.Require(id != null && c.HeroConfig.Defs.ContainsKey(id), "unknown hero");
            return c.HeroConfig.Defs[id];
        }

        private static CommandResult HandleUnlock(HeroUnlock cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Heroes);
            var s = ctx.State; RequireDef(ctx.Content, cmd.HeroId);
            GameEngine.Require(Get(s, cmd.HeroId) == null, "hero already unlocked");
            Unlock(ctx, cmd.HeroId);
            return CommandResult.Success();
        }

        private static CommandResult HandleStarUp(HeroStarUp cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Heroes);
            var s = ctx.State; var cfg = ctx.Content.HeroConfig; RequireDef(ctx.Content, cmd.HeroId);
            var h = Get(s, cmd.HeroId);
            GameEngine.Require(h != null, "hero not unlocked");
            GameEngine.Require(h.Stars < cfg.MaxStars, "hero is at max stars");
            ProgressionUtil.Spend(s, Currencies.HeroShards(cmd.HeroId), ctx.Content.ShardStars.NextCost(h.Stars));
            h.Stars++;
            s.Count("hero_stars");
            ProgressionUtil.CountMax(s, "hero_stars_max", h.Stars);
            ctx.Emit("hero_star", cmd.HeroId, h.Stars.ToString());
            return CommandResult.Success(h.Stars);
        }

        private static CommandResult HandleLevelUp(HeroLevelUp cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Heroes);
            var s = ctx.State; var cfg = ctx.Content.HeroConfig; RequireDef(ctx.Content, cmd.HeroId);
            var h = Get(s, cmd.HeroId);
            GameEngine.Require(h != null, "hero not unlocked");
            GameEngine.Require(cmd.Levels >= 1, "levels must be >= 1");
            GameEngine.Require(h.Level + cmd.Levels <= cfg.MaxLevel(h.Stars), "hero level cap reached (raise stars)");
            long xp = 0;
            for (int l = h.Level; l < h.Level + cmd.Levels; l++) xp += cfg.LevelXpCosts[l];
            ProgressionUtil.Spend(s, Currencies.HeroXp, xp);
            h.Level += cmd.Levels;
            s.Count("hero_levels", cmd.Levels);
            return CommandResult.Success(h.Level);
        }

        private static CommandResult HandleEquip(HeroEquip cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Heroes);
            var s = ctx.State;
            if (string.IsNullOrEmpty(cmd.HeroId)) { s.Heroes.Equipped = ""; return CommandResult.Success(); }
            GameEngine.Require(Get(s, cmd.HeroId) != null, "hero not unlocked");
            s.Heroes.Equipped = cmd.HeroId;
            return CommandResult.Success();
        }
    }
}
