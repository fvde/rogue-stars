using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Arena, rules side (GDD §10): snapshots, weekly leagues in groups of 150 with promotion/demotion and rank
    /// rewards, regenerating arena_tickets, Elo-style points. Matchmaking, group assignment and the fight run on
    /// the server, which reports results via ArenaFightResult / ArenaWeekEnd (server-only).
    /// </summary>
    public static class ArenaSystem
    {
        public static void Register(GameEngine e)
        {
            e.RegisterNewPlayer(ctx =>
            {
                ctx.State.Arena.Points = ctx.Content.Arena.StartPoints;
                ctx.State.Arena.BestPoints = ctx.State.Arena.Points;
                ctx.State.Arena.Week = GameClock.WeekIndex(ctx.Content, ctx.NowMs);
                ctx.State.Wallet.Add(Currencies.ArenaTickets, ctx.Content.Arena.StartTickets);
            });
            // Ticket regeneration is settled by EconomySystem with every other RegenRule (single settler).
            e.Register<ArenaStartFight>(HandleStartFight);
            e.Register<ArenaFightResult>(HandleFightResult);
            e.Register<ArenaWeekEnd>(HandleWeekEnd);
        }

        /// <summary>Snapshot of the player's current loadout for other players to fight (stored by the server).</summary>
        public static ArenaSnapshot CreateSnapshot(GameEngine engine, GameState state, long nowMs)
        {
            var loadout = engine.BuildLoadout(state, nowMs);
            return new ArenaSnapshot
            {
                PlayerId = state.PlayerId, DisplayName = state.DisplayName, League = state.Arena.League, Points = state.Arena.Points,
                Power = loadout.Power, CreatedMs = nowMs, Loadout = loadout,
            };
        }

        // ---- pure math ---------------------------------------------------------------------------

        /// <summary>Elo-style points change for one fight. Wins always gain at least MinWinGain; losses never gain.</summary>
        public static long PointsDelta(ContentDb content, long myPoints, long opponentPoints, bool won)
        {
            var cfg = content.Arena;
            double expected = 1.0 / (1.0 + Math.Pow(10, (opponentPoints - myPoints) / 400.0));
            long delta = (long)Math.Round(cfg.K * ((won ? 1.0 : 0.0) - expected), MidpointRounding.AwayFromZero);
            return won ? Math.Max(cfg.MinWinGain, delta) : Math.Min(0, delta);
        }

        /// <summary>Week-end outcome for a rank in a group of <paramref name="groupSize"/> in a league.</summary>
        public static LeagueOutcome Outcome(ContentDb content, int leagueIndex, int rank, int groupSize)
        {
            var leagues = content.Arena.Leagues;
            var league = leagues[Math.Max(0, Math.Min(leagues.Count - 1, leagueIndex))];
            int promote = leagueIndex >= leagues.Count - 1 ? 0 : (int)Math.Floor(groupSize * league.PromotePercent / 100.0);
            int demote = leagueIndex <= 0 ? 0 : (int)Math.Floor(groupSize * league.DemotePercent / 100.0);
            if (rank >= 1 && rank <= promote) return LeagueOutcome.Promote;
            if (demote > 0 && rank > groupSize - demote) return LeagueOutcome.Demote;
            return LeagueOutcome.Stay;
        }

        /// <summary>Rank rewards for a league (null when none apply).</summary>
        public static List<Reward> RankRewards(ContentDb content, int leagueIndex, int rank)
        {
            var leagues = content.Arena.Leagues;
            if (leagues.Count == 0 || rank <= 0) return null;
            var league = leagues[Math.Max(0, Math.Min(leagues.Count - 1, leagueIndex))];
            foreach (var r in league.RankRewards) if (rank <= r.MaxRank) return r.Rewards;
            return null;
        }

        /// <summary>
        /// Rank a weekly group (points desc, ties by player id) and compute promotion/demotion. Server helper;
        /// groups hold at most content Arena.GroupSize (150) players.
        /// </summary>
        public static List<ArenaStanding> ResolveGroup(ContentDb content, int leagueIndex, IList<ArenaStanding> players)
        {
            if (players.Count > content.Arena.GroupSize) throw new ArgumentException("group larger than " + content.Arena.GroupSize);
            var list = new List<ArenaStanding>(players);
            list.Sort((a, b) => a.Points != b.Points ? b.Points.CompareTo(a.Points) : string.CompareOrdinal(a.PlayerId, b.PlayerId));
            for (int i = 0; i < list.Count; i++)
            {
                list[i].Rank = i + 1;
                list[i].Outcome = Outcome(content, leagueIndex, i + 1, list.Count);
            }
            return list;
        }

        /// <summary>Split a league's players (already ordered by the server, e.g. by signup) into groups of GroupSize.</summary>
        public static List<List<string>> SplitIntoGroups(ContentDb content, IList<string> playerIds)
        {
            var groups = new List<List<string>>();
            int size = content.Arena.GroupSize;
            int count = (playerIds.Count + size - 1) / size;
            for (int g = 0; g < count; g++) groups.Add(new List<string>());
            // Even distribution: round-robin so the last group is never tiny.
            for (int i = 0; i < playerIds.Count; i++) groups[i % Math.Max(1, count)].Add(playerIds[i]);
            return groups;
        }

        // ---- state changes -----------------------------------------------------------------------

        /// <summary>
        /// Weekly points belong to a reset-week: the first arena action of a new week starts again at StartPoints
        /// (the server's weekly groups start their members at the same StartPoints).
        /// </summary>
        public static void EnsureWeek(GameState s, ContentDb content, long nowMs)
        {
            var a = s.Arena;
            long week = GameClock.WeekIndex(content, nowMs);
            if (a.Week == week) return;
            if (a.Week >= 0)
            {
                a.Points = content.Arena.StartPoints;
                a.WeekWins = a.WeekLosses = 0;
            }
            a.Week = week;
        }

        /// <summary>Apply a fight result to this player's weekly points (server calls this after simulating).</summary>
        public static long ApplyFightResult(CommandContext ctx, bool won, long opponentPoints)
        {
            EnsureWeek(ctx.State, ctx.Content, ctx.NowMs);
            var a = ctx.State.Arena;
            long delta = PointsDelta(ctx.Content, a.Points, opponentPoints, won);
            a.Points = Math.Max(ctx.Content.Arena.MinPoints, a.Points + delta);
            a.BestPoints = Math.Max(a.BestPoints, a.Points);
            if (won) { a.WeekWins++; ctx.State.Count("arena_wins"); } else a.WeekLosses++;
            ctx.State.Count("arena_fights");
            long medals = won ? ctx.Content.Arena.MedalsPerWin : ctx.Content.Arena.MedalsPerLoss;
            if (medals > 0) ctx.Grant(Reward.Cur(Currencies.ArenaMedals, medals));
            ctx.Emit("arena_result", won ? "win" : "loss", delta.ToString());
            return delta;
        }

        /// <summary>Close the week: rank rewards, promotion/demotion, points reset.</summary>
        public static LeagueOutcome ApplyWeekEnd(CommandContext ctx, int rank, int groupSize)
        {
            GameEngine.Require(groupSize >= 1 && groupSize <= ctx.Content.Arena.GroupSize, "invalid group size");
            GameEngine.Require(rank >= 1 && rank <= groupSize, "invalid rank");
            var a = ctx.State.Arena;
            ctx.GrantAll(RankRewards(ctx.Content, a.League, rank));
            var outcome = Outcome(ctx.Content, a.League, rank, groupSize);
            if (outcome == LeagueOutcome.Promote) a.League++;
            else if (outcome == LeagueOutcome.Demote) a.League--;
            a.Points = ctx.Content.Arena.StartPoints;
            a.WeekWins = a.WeekLosses = 0;
            a.PendingFights = 0;
            a.WeeksPlayed++;
            a.Week = GameClock.WeekIndex(ctx.Content, ctx.NowMs);
            ctx.Emit("arena_week_end", rank.ToString(), outcome.ToString());
            return outcome;
        }

        private static CommandResult HandleStartFight(ArenaStartFight cmd, CommandContext ctx)
        {
            var s = ctx.State;
            GameEngine.Require(Features.IsUnlocked(s, ctx.Content, Features.Arena), "feature locked");
            GameEngine.Require(!string.IsNullOrEmpty(cmd.OpponentId) && cmd.OpponentId != s.PlayerId, "invalid opponent");
            long cost = ctx.Content.Arena.TicketsPerFight;
            GameEngine.Require(s.Wallet.CanAfford(Currencies.ArenaTickets, cost), "no arena tickets");
            s.Wallet.TrySpend(Currencies.ArenaTickets, cost);
            EnsureWeek(s, ctx.Content, ctx.NowMs);
            s.Arena.PendingFights++;
            s.Arena.PendingOpponentId = cmd.OpponentId;
            return CommandResult.Success();
        }

        private static CommandResult HandleFightResult(ArenaFightResult cmd, CommandContext ctx)
        {
            GameEngine.Require(ctx.State.Arena.PendingFights > 0, "no pending fight");
            ctx.State.Arena.PendingFights--;
            long delta = ApplyFightResult(ctx, cmd.Won, cmd.OpponentPoints);
            var a = ctx.State.Arena;
            return CommandResult.Success(new ArenaFightOutcome { Won = cmd.Won, Delta = delta, Points = a.Points, BestPoints = a.BestPoints });
        }

        private static CommandResult HandleWeekEnd(ArenaWeekEnd cmd, CommandContext ctx) =>
            CommandResult.Success(ApplyWeekEnd(ctx, cmd.Rank, cmd.GroupSize));
    }
}
