using RogueStars.Rules.Content;

namespace RogueStars.Rules.Core
{
    /// <summary>
    /// The single daily/weekly reset boundary for every system (stamina refills, timer free speed-ups, ad caps,
    /// quests, shop limits, login, invasion attempts). Anchors come from content (<see cref="ResetConfig"/>:
    /// daily reset at 00:00 UTC + offset, weekly reset on Monday 00:00 UTC by default).
    /// </summary>
    public static class GameClock
    {
        public const long Hour = 3_600_000L;
        public const long Day = 86_400_000L;
        public const long Week = 7 * Day;

        /// <summary>Index of the reset-day containing <paramref name="nowMs"/>.</summary>
        public static long DayIndex(ContentDb content, long nowMs) => FloorDiv(nowMs - content.Resets.DailyResetOffsetMs, Day);

        /// <summary>Index of the reset-week containing <paramref name="nowMs"/>.</summary>
        public static long WeekIndex(ContentDb content, long nowMs) => FloorDiv(nowMs - content.Resets.WeeklyResetAnchorMs, Week);

        public static long DayStartMs(ContentDb content, long dayIndex) => dayIndex * Day + content.Resets.DailyResetOffsetMs;

        public static long WeekStartMs(ContentDb content, long weekIndex) => weekIndex * Week + content.Resets.WeeklyResetAnchorMs;

        public static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }
    }
}
