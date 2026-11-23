using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim;

/// <summary>
/// Run-length report (--run-length): every run the bots played, grouped by level band and dice count: rolls per run
/// (all runs and runs that reached the boss), drafts, battles, boss reached, win rate, loot items from tiles, peak run
/// coins, minutes and the meta rewards a won run paid (Biomass, Ore). Used to size the awakening clock (GDD §3.3).
/// </summary>
public static class RunLength
{
    public static readonly (string name, int min, int max)[] Bands =
    {
        ("main 1-3", 1, 3), ("main 4-10", 4, 10), ("main 11-30", 11, 30), ("main 31-60", 31, 60), ("main 61-150", 61, 150),
        ("main 151-400", 151, 400), ("main 401+", 401, int.MaxValue),
    };

    private static string BandOf(RunRecord r)
    {
        if (r.Mode == RunMode.DarkSide) return "dark side";
        if (r.Mode == RunMode.Burrow) return "burrow (per run)";
        if (r.Mode == RunMode.Overcharged) return "overcharged";
        foreach (var b in Bands) if (r.Level >= b.min && r.Level <= b.max) return b.name;
        return "other";
    }

    public static string Markdown(IEnumerable<SimResult> results)
    {
        var runs = results.SelectMany(r => r.RunLog).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("| Band | dice | runs | rolls (all) | rolls (boss reached) | p10-p90 (boss) | boss reached | won | drafts | battles | loot offers | loot found | peak coins | min | Biomass / won | Ore / won |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var order = Bands.Select(b => b.name).Concat(new[] { "overcharged", "dark side", "burrow (per run)" }).ToList();
        foreach (var g in runs.GroupBy(r => (band: BandOf(r), dice: r.Dice)).OrderBy(g => order.IndexOf(g.Key.band)).ThenBy(g => g.Key.dice))
        {
            var l = g.ToList();
            var boss = l.Where(r => r.BossReached).Select(r => (double)r.Rolls).OrderBy(x => x).ToList();
            var won = l.Where(r => r.Won).ToList();
            double P(List<double> xs, double q) => xs.Count == 0 ? double.NaN : xs[Math.Min(xs.Count - 1, (int)(q * xs.Count))];
            double Cur(string id) => won.Count == 0 ? 0 : won.Average(r => r.Granted.Where(x => x.Kind == RewardKinds.Currency && x.Id == id).Sum(x => (double)x.Amount));
            sb.AppendLine($"| {g.Key.band} | {g.Key.dice} | {l.Count} | {l.Average(r => r.Rolls):0.0} | {(boss.Count > 0 ? boss.Average() : double.NaN):0.0} | {P(boss, 0.1):0}-{P(boss, 0.9):0} | "
                + $"{100.0 * boss.Count / l.Count:0}% | {100.0 * won.Count / l.Count:0}% | {l.Average(r => r.Drafts):0.0} | {l.Average(r => r.Battles):0.0} | "
                + $"{l.Average(r => r.LootOffers):0.0} | {l.Average(r => r.TileLoot):0.0} | {l.Average(r => r.PeakCoins):0} | {l.Average(r => r.Minutes):0.0} | {Cur(Currencies.Biomass):0} | {Cur(Currencies.Ore):0} |");
        }
        return sb.ToString();
    }
}
