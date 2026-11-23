using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    // Endless campaign: compact level progress far behind the frontier so the state does not grow with every level.
    public static partial class RunSystem
    {
        /// <summary>Counter: highest Dark Side level cleared (drives the Dark Side progress archive).</summary>
        public const string DarkSideMaxCounter = "dark_side_level_max";

        private const string ArchiveDigits = "0123456789abcdefghijklmnopqrstuv";
        private const int FullFlags = 31;

        private static bool IsEmpty(ProgressArchive a) => a == null || (a.Full == 0 && string.IsNullOrEmpty(a.Flags));

        private static int Encode(LevelProgress p)
        {
            int f = 0;
            if (p.Cleared) f |= 1;
            if (p.OverchargedCleared) f |= 2;
            for (int i = 0; i < 3 && i < p.Feats.Count; i++) if (p.Feats[i]) f |= 4 << i;
            return f;
        }

        private static LevelProgress Decode(int f)
        {
            var p = new LevelProgress { Cleared = (f & 1) != 0, OverchargedCleared = (f & 2) != 0 };
            p.Clears = p.Cleared ? 1 : 0;
            for (int i = 0; i < 3; i++) p.Feats[i] = (f & (4 << i)) != 0;
            return p;
        }

        /// <summary>The archive a level belongs to (main campaign or Dark Side) and its number in it; null for other levels.</summary>
        private static ProgressArchive ArchiveOf(GameState s, ContentDb c, LevelDef level, out int number)
        {
            number = level != null ? level.Number : 0;
            if (number <= 0) return null;
            if (level.PortalId == ContentDb.DarkSidePortalId) return s.Run.DarkArchive ?? (s.Run.DarkArchive = new ProgressArchive());
            var portal = c.Portal(level.PortalId);
            if (portal == null || portal.Kind != PortalKind.Main) return null;
            return s.Run.MainArchive ?? (s.Run.MainArchive = new ProgressArchive());
        }

        /// <summary>Progress of a level folded into an archive (a fresh copy), or null if it is not archived.</summary>
        private static LevelProgress Archived(GameState s, ContentDb c, string levelId)
        {
            if (IsEmpty(s.Run.MainArchive) && IsEmpty(s.Run.DarkArchive)) return null;
            var a = ArchiveOf(s, c, c.RunLevel(levelId), out int n);
            if (a == null || n > a.Full + a.Flags.Length) return null;
            return Decode(n <= a.Full ? FullFlags : ArchiveDigits.IndexOf(a.Flags[n - a.Full - 1]));
        }

        /// <summary>
        /// Folds main / Dark Side level records more than <see cref="CampaignDef.ProgressWindowLevels"/> behind the best clear
        /// into the archives (amortized: only once the records exceed twice the window). Re-opened archived levels (replayed for a
        /// missed feat) are folded back with their new flags.
        /// </summary>
        public static void CompactProgress(GameState s, ContentDb c)
        {
            int window = Math.Max(1, c.Campaign.ProgressWindowLevels);
            if (s.Run.Levels.Count <= 2 * window + 20) return;
            var main = s.Run.MainArchive ?? (s.Run.MainArchive = new ProgressArchive());
            var dark = s.Run.DarkArchive ?? (s.Run.DarkArchive = new ProgressArchive());
            var mainFlags = Expand(main, (int)s.Counter(Requirements.MainLevelCounter) - window);
            var darkFlags = Expand(dark, (int)s.Counter(DarkSideMaxCounter) - window);
            var fold = new List<string>();
            foreach (var kv in s.Run.Levels)
            {
                var a = ArchiveOf(s, c, c.RunLevel(kv.Key), out int n);
                if (a == null) continue;
                var flags = a == main ? mainFlags : darkFlags;
                if (n <= a.Full)
                {
                    // Already fully done: a replay cannot add anything; just drop the record.
                    fold.Add(kv.Key);
                    continue;
                }
                if (n > a.Full + flags.Count) continue;
                flags[n - a.Full - 1] = Encode(kv.Value);
                fold.Add(kv.Key);
            }
            foreach (var id in fold) s.Run.Levels.Remove(id);
            Store(main, mainFlags);
            Store(dark, darkFlags);
        }

        /// <summary>Archive flags as a list, extended (with "never played") up to level <paramref name="through"/>.</summary>
        private static List<int> Expand(ProgressArchive a, int through)
        {
            var flags = new List<int>(a.Flags.Length);
            foreach (char ch in a.Flags) flags.Add(Math.Max(0, ArchiveDigits.IndexOf(ch)));
            while (a.Full + flags.Count < through) flags.Add(0);
            return flags;
        }

        private static void Store(ProgressArchive a, List<int> flags)
        {
            int done = 0;
            while (done < flags.Count && flags[done] == FullFlags) done++;
            a.Full += done;
            var chars = new char[flags.Count - done];
            for (int i = done; i < flags.Count; i++) chars[i - done] = ArchiveDigits[flags[i]];
            a.Flags = new string(chars);
        }
    }
}
