using System.Collections.Concurrent;
using System.Text;

namespace RogueStars.Sim;

/// <summary>Per-day counter rates (summon event task thresholds): median daily delta per profile over day windows.</summary>
public static class CounterRates
{
    public static readonly string[] Keys =
    {
        "run_enemies_defeated", "runs_completed", "run_minigames_won", "pulls_alien", "pulls_part", "pulls_blueprint", "run_battles_won",
    };

    public static string Markdown(int days, int seeds, string profiles)
    {
        var prof = profiles.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(':'))
            .Select(p => (t: TimeProfile.ByName(p[0]), s: SpendProfile.ByName(p[1]))).ToList();
        var res = new ConcurrentDictionary<(string name, long seed), long[,]>();
        var jobs = prof.SelectMany(p => Enumerable.Range(1, seeds).Select(s => (p.t, p.s, seed: (long)s))).ToList();
        Parallel.ForEach(jobs, j =>
        {
            var pl = new SimPlayer(j.t, j.s, j.seed);
            var t = new long[days + 1, Keys.Length];
            for (int d = 0; d < days; d++)
            {
                pl.PlayDay(d);
                for (int k = 0; k < Keys.Length; k++) t[d + 1, k] = pl.S.Counter(Keys[k]);
            }
            res[(j.t.Name + ":" + j.s.Name, j.seed)] = t;
        });
        var sb = new StringBuilder();
        var bands = new[] { 10, 20, 30, 45, 60, 90, 120 }.Where(b => b <= days).ToList();
        sb.AppendLine("| Profile | Counter per day | " + string.Join(" | ", bands.Select((b, i) => "days " + (i == 0 ? 1 : bands[i - 1] + 1) + "–" + b)) + " |");
        sb.AppendLine("|---|---|" + string.Concat(bands.Select(_ => "---|")));
        foreach (var name in res.Keys.Select(k => k.name).Distinct().OrderBy(n => n))
            for (int k = 0; k < Keys.Length; k++)
            {
                var cells = new List<string>();
                int prev = 0;
                foreach (var b in bands)
                {
                    var per = res.Where(r => r.Key.name == name).Select(r => (r.Value[b, k] - r.Value[prev, k]) / (double)(b - prev)).OrderBy(x => x).ToList();
                    cells.Add(per[per.Count / 2].ToString("0.#"));
                    prev = b;
                }
                sb.AppendLine("| " + name + " | " + Keys[k] + " | " + string.Join(" | ", cells) + " |");
            }
        return sb.ToString();
    }
}
