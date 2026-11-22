using System;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Guild, rules side (GDD §11): membership on the player (guild id, role), guild shop priced in guild_marks,
    /// weekly guild-invasion contribution. Rosters, guild level, storage and chat are server-side.
    /// </summary>
    public static class GuildSystem
    {
        public const string ShopSection = "guild";

        public static void Register(GameEngine e)
        {
            e.RegisterSettler(Settle);
            e.Register<GuildSetMembership>(HandleSetMembership);
            e.Register<GuildShopBuy>(HandleShopBuy);
        }

        public static bool InGuild(GameState s) => !string.IsNullOrEmpty(s.Guild.GuildId);

        /// <summary>Weekly contribution for the current week (0 after the weekly reset).</summary>
        public static long WeeklyContribution(GameState s, ContentDb content, long nowMs) =>
            s.Guild.ContributionWeek == MetaTime.WeekIndex(content, nowMs) ? s.Guild.WeeklyInvasionContribution : 0;

        /// <summary>Guild level for an aggregated guild XP total (server passes the total).</summary>
        public static int GuildLevel(ContentDb content, long guildXp)
        {
            int lvl = 0;
            for (int i = 0; i < content.Guild.LevelXp.Count; i++) if (guildXp >= content.Guild.LevelXp[i]) lvl = i + 1;
            return Math.Max(1, lvl);
        }

        /// <summary>
        /// Adds invasion contribution score to the member's weekly guild contribution and grants guild marks.
        /// No-op outside a guild. Called by LiveOps when the server records invasion damage.
        /// </summary>
        public static void AddInvasionContribution(CommandContext ctx, long score)
        {
            var g = ctx.State.Guild;
            if (!InGuild(ctx.State) || score <= 0) return;
            RollWeek(g, ctx.Content, ctx.NowMs);
            g.WeeklyInvasionContribution += score;
            g.TotalInvasionContribution += score;
            ctx.State.Count("guild_contribution", score);
            long marks = (long)Math.Floor(score * ctx.Content.LiveOps.GuildMarksPerScorePoint);
            if (marks > 0) ctx.Grant(Reward.Cur(Currencies.GuildMarks, marks));
        }

        private static void RollWeek(GuildState g, ContentDb content, long nowMs)
        {
            long week = MetaTime.WeekIndex(content, nowMs);
            if (g.ContributionWeek != week) { g.ContributionWeek = week; g.WeeklyInvasionContribution = 0; }
        }

        private static void Settle(CommandContext ctx) => RollWeek(ctx.State.Guild, ctx.Content, ctx.NowMs);

        private static CommandResult HandleSetMembership(GuildSetMembership cmd, CommandContext ctx)
        {
            var s = ctx.State;
            var g = s.Guild;
            if (string.IsNullOrEmpty(cmd.GuildId))
            {
                GameEngine.Require(InGuild(s), "not in a guild");
                g.GuildId = null; g.Role = null; g.LeftMs = ctx.NowMs;
                g.WeeklyInvasionContribution = 0;
                ctx.Emit("guild_left");
                return CommandResult.Success();
            }
            GameEngine.Require(Features.IsUnlocked(s, ctx.Content, Features.Guilds), "feature locked");
            GameEngine.Require(GuildRoles.IsValid(cmd.Role), "invalid role");
            bool joining = g.GuildId != cmd.GuildId;
            if (joining)
            {
                GameEngine.Require(g.LeftMs == 0 || ctx.NowMs - g.LeftMs >= ctx.Content.Guild.RejoinCooldownMs, "rejoin cooldown");
                g.JoinedMs = ctx.NowMs;
                g.WeeklyInvasionContribution = 0;
                s.Count("guilds_joined");
            }
            g.GuildId = cmd.GuildId;
            g.Role = cmd.Role;
            ctx.Emit(joining ? "guild_joined" : "guild_role", cmd.GuildId, cmd.Role);
            return CommandResult.Success();
        }

        private static CommandResult HandleShopBuy(GuildShopBuy cmd, CommandContext ctx)
        {
            var item = ctx.Content.ShopItem(cmd.ItemId);
            GameEngine.Require(item != null && item.Section == ShopSection, "unknown guild item");
            GameEngine.Require(InGuild(ctx.State), "not in a guild");
            ShopSystem.BuyItem(ctx, item, ctx.State.Guild.ShopBuys);
            return CommandResult.Success();
        }
    }
}
