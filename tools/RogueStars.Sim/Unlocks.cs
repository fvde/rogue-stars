using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim;

/// <summary>
/// Onboarding v2 check (GDD §16): the account day and main level at which every feature unlocked, per profile (median of
/// seeds), next to the content's schedule (earliest day / level). Shows whether a profile hits the day gate (binge) or
/// the level gate (slow players).
/// </summary>
public static class Unlocks
{
    public static string Markdown(List<SimResult> results, ContentDb content)
    {
        var sb = new StringBuilder();
        var groups = results.GroupBy(r => r.Profile).ToList();
        sb.AppendLine("## Feature unlocks: account day / main level (median of seeds; – = not unlocked)");
        sb.AppendLine();
        sb.AppendLine("| Feature | Schedule | " + string.Join(" | ", groups.Select(g => g.Key)) + " |");
        sb.AppendLine("|---|---|" + string.Concat(groups.Select(_ => "---|")));
        foreach (var f in content.FeatureOrder)
        {
            sb.Append("| ").Append(f.Name).Append(" | D").Append(Features.UnlockDay(f)).Append(" L").Append(Features.UnlockLevel(f)).Append(" |");
            foreach (var g in groups)
            {
                var hits = g.Where(r => r.Unlocks.ContainsKey(f.Id)).Select(r => r.Unlocks[f.Id]).ToList();
                if (hits.Count * 2 <= g.Count()) { sb.Append(" – |"); continue; }
                double day = Output.Median(hits.Select(h => (double)h.day));
                double level = Output.Median(hits.Select(h => (double)h.level));
                sb.Append(" D").Append(Output.F(day)).Append(" L").Append(Output.F(level)).Append(" |");
            }
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
            sb.AppendLine("| " + g.Key + " | " + string.Join(" | ", shown.Select(d => Output.F(Output.Median(g.Select(r => (double)r.Daily[d - 1].MainLevel))))) + " |");
        return sb.ToString();
    }
}
