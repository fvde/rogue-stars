using System;
using System.Diagnostics;
using System.Linq;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim
{
    /// <summary>Result of <see cref="AccountAgeJump.Run"/>.</summary>
    public sealed class AgeJumpResult
    {
        public GameState State;
        /// <summary>Account day the player is on now (1 = created today).</summary>
        public int Day;
        public string Profile;
        public long Seed;
        /// <summary>Account creation time: UTC midnight (daily reset) <see cref="Day"/> − 1 days before now.</summary>
        public long CreatedMs;
        /// <summary>Highest main level cleared.</summary>
        public long MainLevel;
        public long WormLevel;
        public long Commands;
        public long ElapsedMs;
    }

    /// <summary>
    /// Dev tool "jump to account day N": a brand-new account played by the simulator's bot (<see cref="SimPlayer"/>, F2P)
    /// with a time profile for N days of real rules commands, so the state is what such a player really has (levels,
    /// worm, parts, city, aliens, laboratory, currencies, unlocks with their days, tutorial, quests...).
    /// The account is created at the daily reset N − 1 days before <c>now</c> and the bot plays every past day in full and
    /// today's sessions that ended before now, so all timestamps (timers, regen, daily/weekly resets, live-ops) line up
    /// with the real clock: nothing lies in the future, the hub shows "day N".
    /// Live invasions still running now are left untouched (the server's own instances take over), ended ones get their
    /// outcomes. The rules' guild membership is cleared (guilds live on the server; the server re-applies its own record).
    /// Deterministic: same content, player id, day, profile, seed and now → the same state.
    /// </summary>
    public static class AccountAgeJump
    {
        public const int MinDays = 1, MaxDays = 60;

        /// <summary>Profile names (lower case, = <see cref="TimeProfile"/> names): casual (~15 min/day), active (~1 h/day), hardcore (~3 h/day).</summary>
        public static readonly string[] ProfileNames = TimeProfile.All.Select(p => p.Name.ToLowerInvariant()).ToArray();

        /// <summary>The time profile by name (case-insensitive), null if unknown.</summary>
        public static TimeProfile Profile(string name) =>
            TimeProfile.All.FirstOrDefault(p => string.Equals(p.Name, name ?? "", StringComparison.OrdinalIgnoreCase));

        /// <summary>Account creation time for day <paramref name="day"/> at <paramref name="nowMs"/>.</summary>
        public static long CreatedMs(ContentDb content, int day, long nowMs) =>
            GameClock.DayStartMs(content, GameClock.DayIndex(content, nowMs) - (day - 1));

        public static AgeJumpResult Run(ContentDb content, string playerId, int day, TimeProfile profile, long seed, long nowMs)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (day < MinDays || day > MaxDays) throw new ArgumentOutOfRangeException(nameof(day), day, "day must be " + MinDays + ".." + MaxDays);
            var sw = Stopwatch.StartNew();
            long created = CreatedMs(content, day, nowMs);
            var bot = new SimPlayer(profile, SpendProfile.F2P, seed, content, playerId, created) { InvasionsEndingBefore = nowMs };
            for (int d = 0; d < day; d++) bot.PlayDay(d, nowMs);
            bot.Finish(nowMs);
            var state = bot.S;
            state.Guild = new GuildState();
            return new AgeJumpResult
            {
                State = state,
                Day = day,
                Profile = profile.Name.ToLowerInvariant(),
                Seed = seed,
                CreatedMs = created,
                MainLevel = state.Counter(Requirements.MainLevelCounter),
                WormLevel = state.Counter(Requirements.WormLevelCounter),
                Commands = bot.Commands,
                ElapsedMs = sw.ElapsedMilliseconds,
            };
        }
    }
}
