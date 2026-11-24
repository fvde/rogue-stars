using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Mounts (spaceships, all Mythic): one active, stars 1–10 on the shared shard table (ContentDb.ShardStars; stars 6–10 also cost
    /// Mount Cores, the former awakening), level by Fuel. Hero &amp; mount kits: a vehicle on a reload with no stats, one passive + one
    /// active that change at 1★ / 3★ / 5★ / 8★ / 10★ (mount damage +5% at the steps between, +1% per level).
    /// Granter for RewardKinds.Mount (a batch: 50 shards; the batch of a mount the player does not own unlocks it at 1★; a batch for a
    /// 10★ mount becomes MountConfig.CopyOverflow). Pull system: docs/design/pull-system.md.
    /// </summary>
    public static class MountSystem
    {
        public static void Register(GameEngine e)
        {
            e.Register<MountUnlock>(HandleUnlock);
            e.Register<MountStarUp>(HandleStarUp);
            e.Register<MountLevelUp>(HandleLevelUp);
            e.Register<MountEquip>(HandleEquip);
            e.Rewards.Register(RewardKinds.Mount, GrantMount);
            e.RegisterLoadoutContributor(Contribute);
        }

        // ---- queries -------------------------------------------------------------------------------

        public static MountEntry Get(GameState s, string id) => id != null && s.Mounts.Owned.TryGetValue(id, out var m) ? m : null;

        /// <summary>Shards needed for the next star (or to unlock when not owned); -1 at max stars.</summary>
        public static long NextStarCost(GameState s, ContentDb c, string mountId)
        {
            var m = Get(s, mountId);
            return m == null ? c.ShardStars.UnlockShards : m.Stars >= c.MountConfig.MaxStars ? -1 : c.ShardStars.NextCost(m.Stars);
        }

        /// <summary>Mount Cores needed for the next star besides the shards (0 below 6★ and at max).</summary>
        public static long NextStarCores(GameState s, ContentDb c, string mountId)
        {
            var m = Get(s, mountId); var cfg = c.MountConfig;
            int next = m == null ? 1 : m.Stars + 1;
            return next <= cfg.MaxStars && next < cfg.StarCoreCosts.Count ? cfg.StarCoreCosts[next] : 0;
        }

        /// <summary>Weapon / support skill ids active at the given stars.</summary>
        public static List<string> WeaponSkills(ContentDb c, string mountId, int stars)
        {
            var def = c.MountConfig.Defs[mountId];
            return ProgressionUtil.ResolveSkillSteps(def.Weapons, st => st.Star <= stars);
        }

        /// <summary>
        /// Hero &amp; mount kits: a mount has no stats. Its own bonus is mount damage (StatId.MountPower, % of the worm's ATK it hits
        /// with): +DamagePctPerStep at each in-between step (2★, 4★, 6★, 7★, 9★) and +DamagePctPerLevel per level above 1 (Fuel).
        /// </summary>
        public static StatBlock MountStats(ContentDb c, string mountId, MountEntry m)
        {
            var cfg = c.MountConfig;
            var b = new StatBlock();
            double pct = cfg.BetweenSteps(m.Stars) * cfg.DamagePctPerStep + Math.Max(0, m.Level - 1) * cfg.DamagePctPerLevel;
            if (pct > 0) b.Add(new StatMod(StatId.MountPower, pct));
            return b;
        }

        /// <summary>The mount's kit lines at its step: the passive and the active it has now.</summary>
        public static List<KitText> KitNow(ContentDb c, string mountId, int stars)
        {
            var def = c.MountConfig.Defs[mountId];
            int k = c.MountConfig.KitStep(stars);
            var list = new List<KitText>();
            foreach (var slot in new[] { "Passive", "Active" })
            {
                KitText best = null;
                int i = 0;
                foreach (var line in def.Kit) if (line.Slot == slot) { if (i <= k) best = line; i++; }
                if (best != null) list.Add(best);
            }
            return list;
        }

        public static StatBlock CollectionStats(GameState s, ContentDb c)
        {
            int steps = 0;
            foreach (var kv in s.Mounts.Owned) steps += kv.Value.Stars;
            return ProgressionUtil.Scaled(c.MountConfig.CollectionPerStep, steps);
        }

        private static void Contribute(GameState s, ContentDb c, long now, CombatLoadout l)
        {
            l.WormStats.AddBlock(CollectionStats(s, c));
            var m = Get(s, s.Mounts.Active);
            if (m == null) return;
            l.MountId = s.Mounts.Active;
            l.MountStats.AddBlock(MountStats(c, s.Mounts.Active, m));
            foreach (var sk in WeaponSkills(c, s.Mounts.Active, m.Stars)) if (!l.MetaSkillIds.Contains(sk)) l.MetaSkillIds.Add(sk);
        }

        // ---- granter / handlers -------------------------------------------------------------------

        /// <summary>What the next batch of a mount does: "new" (unlocks it), "shards" (+50 shards) or "overflow" (10★).</summary>
        public static string CopyEffect(GameState s, ContentDb c, string mountId)
        {
            var m = Get(s, mountId);
            return m == null ? "new" : m.Stars < c.MountConfig.MaxStars ? "shards" : "overflow";
        }

        private static void GrantMount(CommandContext ctx, Reward r)
        {
            var s = ctx.State; var c = ctx.Content; var cfg = c.MountConfig;
            GameEngine.Require(cfg.Defs.ContainsKey(r.Id), "unknown mount " + r.Id);
            for (long i = 0; i < r.Amount; i++)
            {
                s.Count("mount_copies");
                var m = Get(s, r.Id);
                if (m != null && m.Stars >= cfg.MaxStars)
                {
                    if (cfg.CopyOverflow != null) ctx.Grant(cfg.CopyOverflow);
                    continue;
                }
                s.Wallet.Add(Currencies.MountShards(r.Id), c.ShardStars.BatchShards);
                if (m == null) Unlock(ctx, r.Id);
            }
        }

        /// <summary>Unlock a mount at 1★ with UnlockShards of its shards (the caller checked it is not owned).</summary>
        private static void Unlock(CommandContext ctx, string mountId)
        {
            var s = ctx.State;
            ProgressionUtil.Spend(s, Currencies.MountShards(mountId), ctx.Content.ShardStars.UnlockShards);
            s.Mounts.Owned[mountId] = new MountEntry();
            if (string.IsNullOrEmpty(s.Mounts.Active)) s.Mounts.Active = mountId;
            s.Count("mounts_unlocked");
            ctx.Emit("mount_unlocked", mountId);
        }

        private static MountEntry RequireOwned(CommandContext ctx, string id)
        {
            var m = Get(ctx.State, id);
            GameEngine.Require(m != null, "mount not owned");
            return m;
        }

        private static CommandResult HandleUnlock(MountUnlock cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Mounts);
            var s = ctx.State; var cfg = ctx.Content.MountConfig;
            GameEngine.Require(cmd.MountId != null && cfg.Defs.ContainsKey(cmd.MountId), "unknown mount");
            GameEngine.Require(Get(s, cmd.MountId) == null, "mount already owned");
            Unlock(ctx, cmd.MountId);
            return CommandResult.Success();
        }

        private static CommandResult HandleStarUp(MountStarUp cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Mounts);
            var s = ctx.State; var c = ctx.Content; var cfg = c.MountConfig; var m = RequireOwned(ctx, cmd.MountId);
            GameEngine.Require(m.Stars < cfg.MaxStars, "mount is at max stars");
            var costs = new List<Cost> { new Cost(Currencies.MountShards(cmd.MountId), c.ShardStars.NextCost(m.Stars)) };
            long cores = NextStarCores(s, c, cmd.MountId);
            if (cores > 0) costs.Add(new Cost(Currencies.MountCores, cores));
            ProgressionUtil.Spend(s, costs);
            m.Stars++;
            s.Count("mount_stars");
            ctx.Emit("mount_star", cmd.MountId, m.Stars.ToString());
            return CommandResult.Success(m.Stars);
        }

        private static CommandResult HandleLevelUp(MountLevelUp cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Mounts);
            var s = ctx.State; var cfg = ctx.Content.MountConfig; var m = RequireOwned(ctx, cmd.MountId);
            GameEngine.Require(cmd.Levels >= 1, "levels must be >= 1");
            GameEngine.Require(m.Level + cmd.Levels <= cfg.MaxLevel(m.Stars), "mount level cap reached");
            long fuel = 0;
            for (int l = m.Level; l < m.Level + cmd.Levels; l++) fuel += cfg.LevelFuelCosts[l];
            ProgressionUtil.Spend(s, Currencies.Fuel, fuel);
            m.Level += cmd.Levels;
            s.Count("mount_levels", cmd.Levels);
            return CommandResult.Success(m.Level);
        }

        private static CommandResult HandleEquip(MountEquip cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Mounts);
            if (string.IsNullOrEmpty(cmd.MountId)) { ctx.State.Mounts.Active = ""; return CommandResult.Success(); }
            RequireOwned(ctx, cmd.MountId);
            ctx.State.Mounts.Active = cmd.MountId;
            return CommandResult.Success();
        }
    }
}
