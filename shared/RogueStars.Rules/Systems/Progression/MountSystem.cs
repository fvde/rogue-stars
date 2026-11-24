using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Mounts (spaceships): one active, stars 1–5 then awakening 1–5, level by Fuel. Hero &amp; mount kits: a vehicle on a reload with
    /// no stats, one passive + one active that change at 1★ / 3★ / 5★ / A3 / A5 (mount damage +5% at the steps between, +1% per level). Granter for RewardKinds.Mount (first copy unlocks at 1★, duplicates
    /// become mount shards).
    /// </summary>
    public static class MountSystem
    {
        public static void Register(GameEngine e)
        {
            e.Register<MountStarUp>(HandleStarUp);
            e.Register<MountAwaken>(HandleAwaken);
            e.Register<MountLevelUp>(HandleLevelUp);
            e.Register<MountEquip>(HandleEquip);
            e.Rewards.Register(RewardKinds.Mount, GrantMount);
            e.RegisterLoadoutContributor(Contribute);
        }

        // ---- queries -------------------------------------------------------------------------------

        public static MountEntry Get(GameState s, string id) => id != null && s.Mounts.Owned.TryGetValue(id, out var m) ? m : null;

        /// <summary>Weapon / support skill ids active at the given star and awakening level.</summary>
        public static List<string> WeaponSkills(ContentDb c, string mountId, int stars, int awaken)
        {
            var def = c.MountConfig.Defs[mountId];
            return ProgressionUtil.ResolveSkillSteps(def.Weapons, st => st.Star <= stars && st.Awaken <= awaken);
        }

        /// <summary>
        /// Hero &amp; mount kits: a mount has no stats. Its own bonus is mount damage (StatId.MountPower, % of the worm's ATK it hits
        /// with): +DamagePctPerStep at each in-between step (2★, 4★, A1, A2, A4) and +DamagePctPerLevel per level above 1 (Fuel).
        /// </summary>
        public static StatBlock MountStats(ContentDb c, string mountId, MountEntry m)
        {
            var cfg = c.MountConfig;
            var b = new StatBlock();
            double pct = cfg.BetweenSteps(m.Stars, m.Awaken) * cfg.DamagePctPerStep + Math.Max(0, m.Level - 1) * cfg.DamagePctPerLevel;
            if (pct > 0) b.Add(new StatMod(StatId.MountPower, pct));
            return b;
        }

        /// <summary>The mount's kit lines at its step: the passive and the active it has now.</summary>
        public static List<KitText> KitNow(ContentDb c, string mountId, int stars, int awaken)
        {
            var def = c.MountConfig.Defs[mountId];
            int k = c.MountConfig.KitStep(stars, awaken);
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
            foreach (var kv in s.Mounts.Owned) steps += kv.Value.Stars + kv.Value.Awaken;
            return ProgressionUtil.Scaled(c.MountConfig.CollectionPerStep, steps);
        }

        private static void Contribute(GameState s, ContentDb c, long now, CombatLoadout l)
        {
            l.WormStats.AddBlock(CollectionStats(s, c));
            var m = Get(s, s.Mounts.Active);
            if (m == null) return;
            l.MountId = s.Mounts.Active;
            l.MountStats.AddBlock(MountStats(c, s.Mounts.Active, m));
            foreach (var sk in WeaponSkills(c, s.Mounts.Active, m.Stars, m.Awaken)) if (!l.MetaSkillIds.Contains(sk)) l.MetaSkillIds.Add(sk);
        }

        // ---- granter / handlers -------------------------------------------------------------------

        private static void GrantMount(CommandContext ctx, Reward r)
        {
            var s = ctx.State; var cfg = ctx.Content.MountConfig;
            GameEngine.Require(cfg.Defs.ContainsKey(r.Id), "unknown mount " + r.Id);
            long copies = r.Amount;
            if (Get(s, r.Id) == null)
            {
                s.Mounts.Owned[r.Id] = new MountEntry();
                if (string.IsNullOrEmpty(s.Mounts.Active)) s.Mounts.Active = r.Id;
                s.Count("mounts_unlocked");
                ctx.Emit("mount_unlocked", r.Id);
                copies--;
            }
            if (copies > 0) s.Wallet.Add(Currencies.MountShards(r.Id), copies * cfg.DupeShards);
        }

        private static MountEntry RequireOwned(CommandContext ctx, string id)
        {
            var m = Get(ctx.State, id);
            GameEngine.Require(m != null, "mount not owned");
            return m;
        }

        private static CommandResult HandleStarUp(MountStarUp cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Mounts);
            var s = ctx.State; var cfg = ctx.Content.MountConfig; var m = RequireOwned(ctx, cmd.MountId);
            GameEngine.Require(m.Stars < cfg.MaxStars, "mount is at max stars (awaken next)");
            ProgressionUtil.Spend(s, Currencies.MountShards(cmd.MountId), cfg.StarShardCosts[m.Stars + 1]);
            m.Stars++;
            s.Count("mount_stars");
            ctx.Emit("mount_star", cmd.MountId, m.Stars.ToString());
            return CommandResult.Success(m.Stars);
        }

        private static CommandResult HandleAwaken(MountAwaken cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Mounts);
            var s = ctx.State; var cfg = ctx.Content.MountConfig; var m = RequireOwned(ctx, cmd.MountId);
            GameEngine.Require(m.Stars >= cfg.MaxStars, "mount needs max stars before awakening");
            GameEngine.Require(m.Awaken < cfg.MaxAwaken, "mount is fully awakened");
            int next = m.Awaken + 1;
            ProgressionUtil.Spend(s, new[] { new Cost(Currencies.MountShards(cmd.MountId), cfg.AwakenShardCosts[next]), new Cost(Currencies.MountCores, cfg.AwakenCoreCosts[next]) });
            m.Awaken = next;
            s.Count("mount_awakens");
            ctx.Emit("mount_awaken", cmd.MountId, next.ToString());
            return CommandResult.Success(next);
        }

        private static CommandResult HandleLevelUp(MountLevelUp cmd, CommandContext ctx)
        {
            Features.Require(ctx.State, ctx.Content, Features.Mounts);
            var s = ctx.State; var cfg = ctx.Content.MountConfig; var m = RequireOwned(ctx, cmd.MountId);
            GameEngine.Require(cmd.Levels >= 1, "levels must be >= 1");
            GameEngine.Require(m.Level + cmd.Levels <= cfg.MaxLevel(m.Stars, m.Awaken), "mount level cap reached");
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
