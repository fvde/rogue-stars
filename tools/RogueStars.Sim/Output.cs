using System.Globalization;
using System.Text;

namespace RogueStars.Sim;

/// <summary>Medians over seeds per profile, CSVs (checkpoints only) and the markdown report.</summary>
public static class Output
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static double Median(IEnumerable<double> xs)
    {
        var l = xs.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
        if (l.Count == 0) return double.NaN;
        return l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
    }

    /// <summary>Profile → checkpoint → metric → median.</summary>
    public static Dictionary<string, SortedDictionary<int, Dictionary<string, double>>> Medians(List<SimResult> results)
    {
        var d = new Dictionary<string, SortedDictionary<int, Dictionary<string, double>>>();
        foreach (var g in results.GroupBy(r => r.Profile))
        {
            var byDay = new SortedDictionary<int, Dictionary<string, double>>();
            foreach (int day in g.First().Checkpoints.Keys)
            {
                var names = g.First().Checkpoints[day].Select(x => x.name).ToList();
                byDay[day] = names.ToDictionary(n => n, n => Median(g.Select(r => r.At(day, n))));
            }
            d[g.Key] = byDay;
        }
        return d;
    }

    public static string F(double v)
    {
        if (double.IsNaN(v)) return "–";
        double a = Math.Abs(v);
        if (a >= 1e9) return (v / 1e9).ToString("0.##", Inv) + "B";
        if (a >= 1e6) return (v / 1e6).ToString("0.##", Inv) + "M";
        if (a >= 1e4) return (v / 1e3).ToString("0.#", Inv) + "k";
        if (a >= 100 || v == Math.Floor(v)) return v.ToString("0", Inv);
        return v.ToString("0.##", Inv);
    }

    public static void Write(List<SimResult> results, string outDir, string label, int days)
    {
        string dataDir = Path.Combine(outDir, "data");
        Directory.CreateDirectory(dataDir);
        string suffix = string.IsNullOrEmpty(label) ? "" : "_" + label;
        var med = Medians(results);
        foreach (var (profile, byDay) in med)
        {
            var sb = new StringBuilder();
            sb.Append("metric");
            foreach (var day in byDay.Keys) sb.Append(",day").Append(day);
            sb.AppendLine();
            foreach (var name in byDay.First().Value.Keys)
            {
                sb.Append(name);
                foreach (var day in byDay.Keys) sb.Append(',').Append(byDay[day][name].ToString("0.###", Inv));
                sb.AppendLine();
            }
            File.WriteAllText(Path.Combine(dataDir, profile.Replace(' ', '_').ToLowerInvariant() + suffix + ".csv"), sb.ToString());
        }

        // Weekly series for charts: median power / main level / hero stars per profile.
        var ws = new StringBuilder("profile,day,power,main_level,spent_usd,minutes_per_day\n");
        foreach (var g in results.GroupBy(r => r.Profile))
            for (int day = 7; day <= days; day += 7)
            {
                var recs = g.Select(r => r.Daily[day - 1]).ToList();
                ws.Append(g.Key).Append(',').Append(day).Append(',')
                    .Append(Median(recs.Select(x => (double)x.Power)).ToString("0", Inv)).Append(',')
                    .Append(Median(recs.Select(x => (double)x.MainLevel)).ToString("0.#", Inv)).Append(',')
                    .Append(Median(recs.Select(x => x.SpentCents / 100.0)).ToString("0.##", Inv)).Append(',')
                    .Append(Median(recs.Select(x => x.Minutes / day)).ToString("0.#", Inv)).AppendLine();
            }
        File.WriteAllText(Path.Combine(dataDir, "weekly" + suffix + ".csv"), ws.ToString());

        var analysis = Analysis.Run(results, days);
        File.WriteAllText(Path.Combine(dataDir, "analysis" + suffix + ".md"), Report.AnalysisMarkdown(analysis));
        Console.WriteLine(Report.Summary(med, analysis));
    }
}
