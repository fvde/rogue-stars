using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim;

/// <summary>
/// Level unlocks check (GDD §16): when every feature unlocked per profile (median of seeds): minutes played, runs played,
/// main level and account day, next to the content's gate level; and the novelty gaps (played minutes between two new
/// things) of the first hours. Every feature is gated by the main level only, so the minutes depend on how fast a profile
/// clears levels, never on the calendar.
/// </summary>
public static class Unlocks
{
    /// <summary>Played-minute windows of the novelty summary.</summary>
    public static readonly int[] Windows = { 30, 60, 120, 180 };

    /// <summary>Median (minutes, runs, level, day) of a feature's unlock for one profile; null when most seeds did not get it.</summary>
    public static (double minutes, double runs, double level, double day)? Median(IEnumerable<SimResult> profile, string id)
    {
        var all = profile.ToList();
        var hits = all.Where(r => r.Unlocks.ContainsKey(id)).Select(r => r.Unlocks[id]).ToList();
        if (hits.Count * 2 <= all.Count) return null;
        return (Output.Median(hits.Select(h => h.minutes)), Output.Median(hits.Select(h => (double)h.runs)),
            Output.Median(hits.Select(h => (double)h.level)), Output.Median(hits.Select(h => (double)h.day)));
    }

    /// <summary>Played minutes of every feature unlock of one account, in order (lab research excluded).</summary>
    public static List<double> UnlockMinutes(SimResult r, ContentDb content) =>
        content.FeatureOrder.Where(f => r.Unlocks.ContainsKey(f.Id)).Select(f => r.Unlocks[f.Id].minutes).OrderBy(m => m).ToList();

    /// <summary>Longest stretch of played minutes without a new feature inside the first <paramref name="window"/> minutes.</summary>
    public static double LongestGap(List<double> minutes, double window, double played)
    {
        double last = 0, longest = 0;
        foreach (var m in minutes)
        {
            if (m > window) break;
            longest = Math.Max(longest, m - last);
            last = m;
        }
        return Math.Max(longest, Math.Min(window, played) - last);
    }

    public static string Markdown(List<SimResult> results, ContentDb content)
    {
        var sb = new StringBuilder();
        var groups = results.GroupBy(r => r.Profile).ToList();
        sb.AppendLine("## Feature unlocks: minutes played · runs · main level · account day (median of seeds; – = not unlocked)");
        sb.AppendLine();
        sb.AppendLine("| Feature | Gate | " + string.Join(" | ", groups.Select(g => g.Key)) + " |");
        sb.AppendLine("|---|---|" + string.Concat(groups.Select(_ => "---|")));
        foreach (var f in content.FeatureOrder)
        {
            sb.Append("| ").Append(f.Name).Append(f.RunContent ? " (run)" : "").Append(" | L").Append(Features.UnlockLevel(f)).Append(" |");
            foreach (var g in groups) sb.Append(' ').Append(Cell(Median(g, f.Id))).Append(" |");
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("## Novelty gaps (played minutes; median of seeds)");
        sb.AppendLine();
        sb.AppendLine("New things = feature unlocks (meta and run content). Longest gap = the longest stretch of play without one.");
        sb.AppendLine();
        sb.AppendLine("| Profile | " + string.Join(" | ", Windows.Select(w => "new in first " + w + " min")) + " | "
            + string.Join(" | ", Windows.Skip(1).Select(w => "longest gap, first " + w + " min")) + " | median gap, first 180 min |");
        sb.AppendLine("|---|" + string.Concat(Windows.Select(_ => "---|")) + string.Concat(Windows.Skip(1).Select(_ => "---|")) + "---|");
        foreach (var g in groups)
        {
            var per = g.Select(r => (mins: UnlockMinutes(r, content), played: r.Daily.Count > 0 ? r.Daily[^1].Minutes : 0)).ToList();
            sb.Append("| ").Append(g.Key).Append(" |");
            foreach (var w in Windows) sb.Append(' ').Append(W(Output.Median(per.Select(p => (double)p.mins.Count(m => m <= w))))).Append(" |");
            foreach (var w in Windows.Skip(1)) sb.Append(' ').Append(W(Output.Median(per.Select(p => LongestGap(p.mins, w, p.played))))).Append(" |");
            sb.Append(' ').Append(W(Output.Median(per.Select(p =>
            {
                var m = p.mins.Where(x => x <= 180).ToList();
                var gaps = m.Select((x, i) => x - (i == 0 ? 0 : m[i - 1])).ToList();
                return gaps.Count == 0 ? 180 : Output.Median(gaps);
            })))).Append(" |");
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("Main level at the end of each day (median):");
        sb.AppendLine();
        int days = results.Min(r => r.Daily.Count);
        var shown = new[] { 1, 2, 3, 4, 5, 7, 10, 14, 21, 28 }.Where(d => d <= days).ToList();
        sb.AppendLine("| Profile | " + string.Join(" | ", shown.Select(d => "day " + d)) + " |");
        sb.AppendLine("|---|" + string.Concat(shown.Select(_ => "---|")));
        foreach (var g in groups)
            sb.AppendLine("| " + g.Key + " | " + string.Join(" | ", shown.Select(d => W(Output.Median(g.Select(r => (double)r.Daily[d - 1].MainLevel))))) + " |");
        return sb.ToString();
    }

    /// <summary>Whole numbers in the tables.</summary>
    private static string W(double v) => double.IsNaN(v) ? "–" : Math.Round(v).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    private static string Cell((double minutes, double runs, double level, double day)? m) =>
        m == null ? "–" : W(m.Value.minutes) + " min · " + W(m.Value.runs) + " runs · L" + W(m.Value.level) + " · D" + W(m.Value.day);
}
