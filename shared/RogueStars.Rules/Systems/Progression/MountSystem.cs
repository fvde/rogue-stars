using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Mounts (spaceships): one active, stars 1–5 then awakening 1–5, level by Fuel, weapon skills upgraded
    /// at 2/4/5★ and awakening 3/5. Granter for RewardKinds.Mount (a copy: the first unlocks at 1★, every further copy
    /// raises a star up to 5★, then pays the mount shards of the next awakening level; fully awakened → MountConfig.CopyOverflow).
    /// </summary>
    public static class MountSystem
    {
        public static void Register(GameEngine e)
        {
            e.Register<MountUnlock>(HandleUnlock);
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

        /// <summary>What the next copy of a mount does: "new", "star" (+1★), "awaken" (shards for the next awakening) or "overflow".</summary>
        public static string CopyEffect(GameState s, ContentDb c, string mountId)
        {
            var m = Get(s, mountId); var cfg = c.MountConfig;
            if (m == null) return "new";
            if (m.Stars < cfg.MaxStars) return "star";
            return m.Awaken < cfg.MaxAwaken ? "awaken" : "overflow";
        }

        private static void GrantMount(CommandContext ctx, Reward r)
        {
            var s = ctx.State; var cfg = ctx.Content.MountConfig;
            GameEngine.Require(cfg.Defs.ContainsKey(r.Id), "unknown mount " + r.Id);
            for (long i = 0; i < r.Amount; i++)
            {
                s.Count("mount_copies");
                var m = Get(s, r.Id);
                if (m == null)
                {
                    s.Mounts.Owned[r.Id] = new MountEntry();
                    if (string.IsNullOrEmpty(s.Mounts.Active)) s.Mounts.Active = r.Id;
                    s.Count("mounts_unlocked");
                    ctx.Emit("mount_unlocked", r.Id);
                }
                else if (m.Stars < cfg.MaxStars)
                {
                    m.Stars++;
                    s.Count("mount_stars");
                    ctx.Emit("mount_star", r.Id, m.Stars.ToString());
                }
                else if (m.Awaken < cfg.MaxAwaken) s.Wallet.Add(Currencies.MountShards(r.Id), cfg.AwakenShardCosts[m.Awaken + 1]);
                else if (cfg.CopyOverflow != null) ctx.Grant(cfg.CopyOverflow);
            }
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
            ProgressionUtil.Spend(s, Currencies.MountShards(cmd.MountId), cfg.UnlockShards);
            s.Mounts.Owned[cmd.MountId] = new MountEntry();
            if (string.IsNullOrEmpty(s.Mounts.Active)) s.Mounts.Active = cmd.MountId;
            s.Count("mounts_unlocked");
            ctx.Emit("mount_unlocked", cmd.MountId);
            return CommandResult.Success();
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
