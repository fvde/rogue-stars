using System.Text;

namespace RogueStars.Sim;

/// <summary>
/// Parts / laboratory / Awaken Core tables (docs/balance/progression-report.md, "Parts/lab/cores redesign"): first day the
/// best owned part reaches each high rarity, first awakening, Awaken Core income per week by source, laboratory completion,
/// Data/Biomass income by source and the part surplus (spare parts, dismantled vs merged).
/// </summary>
public static class Redesign
{
    private static double MedianDay(IEnumerable<double> days)
    {
        var l = days.OrderBy(x => x).ToList();
        if (l.Count == 0) return double.NaN;
        return l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
    }

    private static string Day(double d) => double.IsPositiveInfinity(d) || double.IsNaN(d) ? "–" : d.ToString("0");

    public static string Markdown(List<SimResult> results, RogueStars.Rules.Content.ContentDb content, int days)
    {
        var sb = new StringBuilder();
        var med = Output.Medians(results);
        var groups = results.GroupBy(r => r.Profile).ToList();
        var names = content.PartsConfig.Rarities.Select(r => r.Name).ToList();
        int legendary = names.IndexOf("Legendary");

        sb.AppendLine("## First day the best owned part reaches a rarity (median of seeds; – = not within " + days + " days)");
        sb.AppendLine();
        var rungs = Enumerable.Range(legendary, names.Count - legendary).ToList();
        sb.AppendLine("| Profile | " + string.Join(" | ", rungs.Select(i => names[i])) + " | first awakening |");
        sb.AppendLine("|---|" + string.Concat(rungs.Select(_ => "---|")) + "---|");
        foreach (var g in groups)
        {
            sb.Append("| ").Append(g.Key).Append(" |");
            foreach (int rung in rungs)
                sb.Append(' ').Append(Day(MedianDay(g.Select(r => (double?)r.Daily.FirstOrDefault(d => d.MaxPartRarity >= rung)?.Day ?? double.PositiveInfinity)))).Append(" |");
            sb.Append(' ').Append(Day(MedianDay(g.Select(r => (double?)r.Daily.FirstOrDefault(d => d.AwakenedParts > 0)?.Day ?? double.PositiveInfinity)))).Append(" |");
            sb.AppendLine();
        }
        sb.AppendLine();

        double Cum(string profile, int day, string metric) =>
            day <= 0 ? 0 : med[profile].TryGetValue(day, out var m) ? m.GetValueOrDefault(metric, 0) : double.NaN;
        string Rate(string profile, string cur, string[] sources, int from, int to, double per)
        {
            if (!med[profile].ContainsKey(to)) return "–";
            double v = sources.Sum(src => Cum(profile, to, "src_" + cur + "_" + src) - Cum(profile, from, "src_" + cur + "_" + src));
            return Output.F(Math.Round(v / (to - from) * per, 2));
        }
        var windows = new[] { (0, 90), (90, 365), (365, 730) }.Where(w => w.Item2 <= days).ToList();

        sb.AppendLine("## Awaken Cores per week by source (median of seeds)");
        sb.AppendLine();
        sb.AppendLine("| Profile | " + string.Join(" | ", windows.Select(w => "d" + w.Item1 + "–" + w.Item2 + " total")) + " | arena (PvP shop) d365–730 | invasions d365–730 | Burrow d365–730 | other d365–730 |");
        sb.AppendLine("|---|" + string.Concat(windows.Select(_ => "---|")) + "---|---|---|---|");
        string core = RogueStars.Rules.Systems.ProgressionCurrencies.PartAwakenCores;
        foreach (var g in groups)
        {
            sb.Append("| ").Append(g.Key).Append(" |");
            foreach (var (a, b) in windows) sb.Append(' ').Append(Rate(g.Key, core, SimPlayer.Sources, a, b, 7)).Append(" |");
            int f = days >= 730 ? 365 : 0, t = days >= 730 ? 730 : days;
            sb.Append(' ').Append(Rate(g.Key, core, new[] { "shop", "arena" }, f, t, 7)).Append(" |");
            sb.Append(' ').Append(Rate(g.Key, core, new[] { "invasion" }, f, t, 7)).Append(" |");
            sb.Append(' ').Append(Rate(g.Key, core, new[] { "run_burrow" }, f, t, 7)).Append(" |");
            sb.Append(' ').Append(Rate(g.Key, core, new[] { "run_main", "run_dark", "city", "quests", "other" }, f, t, 7)).Append(" |");
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("## Laboratory completion (% of all node levels, median)");
        sb.AppendLine();
        var labDays = new[] { 30, 90, 180, 365, 730 }.Where(d => d <= days).ToList();
        sb.AppendLine("| Profile | " + string.Join(" | ", labDays.Select(d => "d" + d)) + " |");
        sb.AppendLine("|---|" + string.Concat(labDays.Select(_ => "---|")));
        foreach (var g in groups)
            sb.AppendLine("| " + g.Key + " | " + string.Join(" | ", labDays.Select(d => Output.F(Cum(g.Key, d, "lab_pct")))) + " |");
        sb.AppendLine();

        foreach (var cur in new[] { "data", RogueStars.Rules.Core.Currencies.Biomass })
        {
            sb.AppendLine("## " + cur + " income per day by source (median of seeds)");
            sb.AppendLine();
            var srcs = SimPlayer.Sources;
            sb.AppendLine("| Profile | window | " + string.Join(" | ", srcs) + " | run share |");
            sb.AppendLine("|---|---|" + string.Concat(srcs.Select(_ => "---|")) + "---|");
            foreach (var g in groups)
                foreach (var (a, b) in windows)
                {
                    var vals = srcs.Select(src => Cum(g.Key, b, "src_" + cur + "_" + src) - Cum(g.Key, a, "src_" + cur + "_" + src)).ToList();
                    double total = vals.Sum(), runs = vals[0] + vals[1] + vals[2];
                    sb.AppendLine("| " + g.Key + " | d" + a + "–" + b + " | " + string.Join(" | ", vals.Select(v => Output.F(Math.Round(v / (b - a))))) + " | "
                                  + (total > 0 ? (runs / total * 100).ToString("0") + "%" : "–") + " |");
                }
            sb.AppendLine();
        }

        sb.AppendLine("## Part surplus (median): spare = owned and not equipped");
        sb.AppendLine();
        var sd = new[] { 90, 365, 730 }.Where(d => d <= days).ToList();
        sb.AppendLine("| Profile | " + string.Join(" | ", sd.Select(d => "spare Legendary+ d" + d)) + " | spare below Legendary d" + sd.Last() + " | obtained / merged / dismantled d" + sd.Last() + " |");
        sb.AppendLine("|---|" + string.Concat(sd.Select(_ => "---|")) + "---|---|");
        foreach (var g in groups)
        {
            int last = sd.Last();
            sb.AppendLine("| " + g.Key + " | " + string.Join(" | ", sd.Select(d => Output.F(Cum(g.Key, d, "parts_spare_legendary_plus")))) + " | "
                          + Output.F(Cum(g.Key, last, "parts_spare_below_legendary")) + " | "
                          + Output.F(Cum(g.Key, last, "parts_obtained")) + " / " + Output.F(Cum(g.Key, last, "part_merges")) + " / " + Output.F(Cum(g.Key, last, "parts_dismantled")) + " |");
        }
        return sb.ToString();
    }
}
