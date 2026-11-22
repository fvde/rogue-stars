using RogueStars.Rules.Content;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Server-time calendar helpers shared by Meta systems. All times are UTC epoch milliseconds
    /// (ctx.NowMs). Daily reset and weekly reset anchors are content-defined (<see cref="ResetConfig"/>).
    /// </summary>
    public static class MetaTime
    {
        public const long Hour = 3_600_000L;
        public const long Day = 86_400_000L;
        public const long Week = 7 * Day;

        /// <summary>Index of the reset-day containing <paramref name="nowMs"/> (day boundary = daily reset).</summary>
        public static long DayIndex(ContentDb content, long nowMs) => FloorDiv(nowMs - content.Resets.DailyResetOffsetMs, Day);

        /// <summary>Index of the reset-week containing <paramref name="nowMs"/> (boundary = weekly reset, Monday 00:00 UTC by default).</summary>
        public static long WeekIndex(ContentDb content, long nowMs) => FloorDiv(nowMs - content.Resets.WeeklyResetAnchorMs, Week);

        /// <summary>Start (ms) of a reset-day index.</summary>
        public static long DayStartMs(ContentDb content, long dayIndex) => dayIndex * Day + content.Resets.DailyResetOffsetMs;

        /// <summary>Start (ms) of a reset-week index.</summary>
        public static long WeekStartMs(ContentDb content, long weekIndex) => weekIndex * Week + content.Resets.WeeklyResetAnchorMs;

        public static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }
    }
}
