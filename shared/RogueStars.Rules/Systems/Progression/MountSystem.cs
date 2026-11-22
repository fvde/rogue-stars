using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Mounts (spaceships): one active, stars 1–5 then awakening 1–5, level by Fuel, weapon skills upgraded
    /// at 2/4/5★ and awakening 3/5. Granter for RewardKinds.Mount (first copy unlocks at 1★, duplicates
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

        /// <summary>Mount combat stats: (base × (1+star%+awaken%)) + level growth × (1+awaken%).</summary>
        public static StatBlock MountStats(ContentDb c, string mountId, MountEntry m)
        {
            var cfg = c.MountConfig; var def = cfg.Defs[mountId];
            double star = cfg.StarStatPercent[Math.Min(m.Stars, cfg.StarStatPercent.Count - 1)];
            double awk = cfg.AwakenStatPercent[Math.Min(m.Awaken, cfg.AwakenStatPercent.Count - 1)];
            var b = ProgressionUtil.Scaled(def.BaseStats, 1 + (star + awk) / 100.0);
            b.AddBlock(ProgressionUtil.Scaled(def.StatsPerLevel, (m.Level - 1) * (1 + awk / 100.0)));
            return b;
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
            if (string.IsNullOrEmpty(cmd.MountId)) { ctx.State.Mounts.Active = ""; return CommandResult.Success(); }
            RequireOwned(ctx, cmd.MountId);
            ctx.State.Mounts.Active = cmd.MountId;
            return CommandResult.Success();
        }
    }
}
