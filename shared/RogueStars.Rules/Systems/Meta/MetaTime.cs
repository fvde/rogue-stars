using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Meta's calendar helpers: thin aliases of <see cref="GameClock"/> (the one reset boundary for all systems).
    /// All times are UTC epoch milliseconds (ctx.NowMs).
    /// </summary>
    public static class MetaTime
    {
        public const long Hour = GameClock.Hour;
        public const long Day = GameClock.Day;
        public const long Week = GameClock.Week;

        public static long DayIndex(ContentDb content, long nowMs) => GameClock.DayIndex(content, nowMs);
        public static long WeekIndex(ContentDb content, long nowMs) => GameClock.WeekIndex(content, nowMs);
        public static long DayStartMs(ContentDb content, long dayIndex) => GameClock.DayStartMs(content, dayIndex);
        public static long WeekStartMs(ContentDb content, long weekIndex) => GameClock.WeekStartMs(content, weekIndex);
        public static long FloorDiv(long a, long b) => GameClock.FloorDiv(a, b);
    }
}
