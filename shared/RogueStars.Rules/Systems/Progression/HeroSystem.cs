using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Heroes (worm personas): unlock by shards, stars 1–10 by shards, level by Hero XP, trait skills that
    /// upgrade at 3/5/7/10★, one equipped hero, collection bonus from every unlocked hero.
    /// Granter for RewardKinds.HeroShards (duplicates are always shards).
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
            e.RegisterLoadoutContributor(Contribute);
        }

        // ---- queries -------------------------------------------------------------------------------

        public static HeroEntry Get(GameState s, string heroId) => heroId != null && s.Heroes.Owned.TryGetValue(heroId, out var h) ? h : null;

        /// <summary>Shards needed for the next star (or to unlock when not owned); -1 at max stars.</summary>
        public static long NextStarCost(GameState s, ContentDb c, string heroId)
        {
            var h = Get(s, heroId); var cfg = c.HeroConfig;
            int next = h == null ? 1 : h.Stars + 1;
            return next <= cfg.MaxStars ? cfg.StarShardCosts[next] : -1;
        }

        /// <summary>Total shards from nothing to the given star.</summary>
        public static long TotalShardsToStar(ContentDb c, int stars)
        {
            long t = 0;
            for (int i = 1; i <= stars && i < c.HeroConfig.StarShardCosts.Count; i++) t += c.HeroConfig.StarShardCosts[i];
            return t;
        }

        /// <summary>Trait skill ids active at the given star count.</summary>
        public static List<string> TraitSkills(ContentDb c, string heroId, int stars)
        {
            var def = c.HeroConfig.Defs[heroId];
            return ProgressionUtil.ResolveSkillSteps(def.Traits, st => st.Star <= stars);
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
            ProgressionUtil.Spend(s, Currencies.HeroShards(cmd.HeroId), ctx.Content.HeroConfig.StarShardCosts[1]);
            s.Heroes.Owned[cmd.HeroId] = new HeroEntry();
            if (string.IsNullOrEmpty(s.Heroes.Equipped)) s.Heroes.Equipped = cmd.HeroId;
            s.Count("heroes_unlocked");
            ctx.Emit("hero_unlocked", cmd.HeroId);
            return CommandResult.Success();
        }

        private static CommandResult HandleStarUp(HeroStarUp cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Heroes);
            var s = ctx.State; var cfg = ctx.Content.HeroConfig; RequireDef(ctx.Content, cmd.HeroId);
            var h = Get(s, cmd.HeroId);
            GameEngine.Require(h != null, "hero not unlocked");
            GameEngine.Require(h.Stars < cfg.MaxStars, "hero is at max stars");
            ProgressionUtil.Spend(s, Currencies.HeroShards(cmd.HeroId), cfg.StarShardCosts[h.Stars + 1]);
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
