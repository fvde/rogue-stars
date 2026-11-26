using System.Collections.Concurrent;
using System.Text;

namespace RogueStars.Sim;

/// <summary>
/// Quest reachability check (docs/balance/daily-quests.md): per profile, the median state at the end of each of the first days
/// (main level, worm level, cumulative counters the new-player and weekly quests read) and the daily / weekly points the bot
/// actually claimed that day. Bots play the real rules, claim every quest and chest.
/// </summary>
public static class QuestReport
{
    public static readonly string[] Keys =
    {
        "runs_completed", "run_bosses_killed", "run_battles_won", "run_skills_drafted", "stamina_spent", "pulls_part", "daily_quests_claimed",
    };

    public static string Markdown(int days, int seeds, string profiles)
    {
        var prof = profiles.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(':'))
            .Select(p => (t: TimeProfile.ByName(p[0]), s: SpendProfile.ByName(p[1]))).ToList();
        // [day, column]: main level, worm level, daily points, weekly points, then Keys
        int cols = 4 + Keys.Length;
        var res = new ConcurrentDictionary<(string name, long seed), long[,]>();
        var jobs = prof.SelectMany(p => Enumerable.Range(1, seeds).Select(s => (p.t, p.s, seed: (long)s))).ToList();
        Parallel.ForEach(jobs, j =>
        {
            var pl = new SimPlayer(j.t, j.s, j.seed);
            var t = new long[days + 1, cols];
            for (int d = 0; d < days; d++)
            {
                pl.PlayDay(d);
                t[d + 1, 0] = pl.S.Counter("main_level_max");
                t[d + 1, 1] = pl.S.Counter("worm_level_max");
                t[d + 1, 2] = pl.S.Quests.DailyPoints;
                t[d + 1, 3] = pl.S.Quests.WeeklyPoints;
                for (int k = 0; k < Keys.Length; k++) t[d + 1, 4 + k] = pl.S.Counter(Keys[k]);
            }
            res[(j.t.Name + ":" + j.s.Name, j.seed)] = t;
        });
        var names = new[] { "main level", "worm level", "daily points claimed", "weekly points claimed" }.Concat(Keys).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine("| Profile | End of day (median) | " + string.Join(" | ", Enumerable.Range(1, days).Select(d => "day " + d)) + " |");
        sb.AppendLine("|---|---|" + string.Concat(Enumerable.Range(1, days).Select(_ => "---|")));
        foreach (var name in res.Keys.Select(k => k.name).Distinct().OrderBy(n => n))
            for (int c = 0; c < cols; c++)
            {
                var cells = new List<string>();
                for (int d = 1; d <= days; d++)
                {
                    var v = res.Where(r => r.Key.name == name).Select(r => (double)r.Value[d, c]).OrderBy(x => x).ToList();
                    cells.Add(v[v.Count / 2].ToString("0"));
                }
                sb.AppendLine("| " + name + " | " + names[c] + " | " + string.Join(" | ", cells) + " |");
            }
        return sb.ToString();
    }
}
