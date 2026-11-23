using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Sim;

/// <summary>
/// What a new player owns at the end of given days (median wallet balance over seeds, F2P bots per time profile) and how
/// many parts they hold. Used to check that rewards develop with unlocks (GDD §13 "Rewards follow use").
/// </summary>
public static class Inventory
{
    public static string Markdown(int[] days, int seeds, Func<ContentDb> content = null)
    {
        int maxDay = days.Max();
        var profiles = TimeProfile.All;
        // profile → day → currency → values over seeds
        var data = new Dictionary<string, Dictionary<int, Dictionary<string, List<double>>>>();
        var parts = new Dictionary<string, Dictionary<int, List<double>>>();
        var levels = new Dictionary<string, Dictionary<int, List<double>>>();
        var jobs = profiles.SelectMany(t => Enumerable.Range(1, seeds).Select(s => (t, seed: (long)s))).ToList();
        var results = new System.Collections.Concurrent.ConcurrentBag<(string profile, int day, Dictionary<string, long> wallet, int parts, long level)>();
        Parallel.ForEach(jobs, j =>
        {
            var p = new SimPlayer(j.t, SpendProfile.F2P, j.seed, content?.Invoke());
            for (int d = 0; d < maxDay; d++)
            {
                p.PlayDay(d);
                if (Array.IndexOf(days, d + 1) < 0) continue;
                results.Add((j.t.Name, d + 1, new Dictionary<string, long>(p.S.Wallet.Balances), p.S.Parts.Items.Count,
                    p.S.Counter(RogueStars.Rules.Systems.Requirements.MainLevelCounter)));
            }
        });
        foreach (var r in results)
        {
            if (!data.TryGetValue(r.profile, out var byDay)) data[r.profile] = byDay = new();
            if (!byDay.TryGetValue(r.day, out var cur)) byDay[r.day] = cur = new();
            var summed = new Dictionary<string, long>();
            foreach (var kv in r.wallet) summed[Bucket(kv.Key)] = (summed.TryGetValue(Bucket(kv.Key), out var v) ? v : 0) + kv.Value;
            foreach (var kv in summed)
            {
                if (!cur.TryGetValue(kv.Key, out var l)) cur[kv.Key] = l = new List<double>();
                l.Add(kv.Value);
            }
            if (!parts.TryGetValue(r.profile, out var pd)) parts[r.profile] = pd = new();
            if (!pd.TryGetValue(r.day, out var pl)) pd[r.day] = pl = new();
            pl.Add(r.parts);
            if (!levels.TryGetValue(r.profile, out var ld)) levels[r.profile] = ld = new();
            if (!ld.TryGetValue(r.day, out var ll)) ld[r.day] = ll = new();
            ll.Add(r.level);
        }
        var sb = new StringBuilder();
        foreach (var t in profiles)
        {
            if (!data.TryGetValue(t.Name, out var byDay)) continue;
            sb.AppendLine("### " + t.Name + " F2P (median of " + seeds + " seeds; balance at the end of the day)");
            sb.AppendLine();
            sb.AppendLine("| | " + string.Join(" | ", days.Select(d => "day " + d)) + " |");
            sb.AppendLine("|---|" + string.Concat(days.Select(_ => "---|")));
            sb.AppendLine("| main level | " + string.Join(" | ", days.Select(d => Output.F(Output.Median(levels[t.Name][d])))) + " |");
            sb.AppendLine("| parts owned | " + string.Join(" | ", days.Select(d => Output.F(Output.Median(parts[t.Name][d])))) + " |");
            var keys = byDay.Values.SelectMany(x => x.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
            foreach (var k in keys)
            {
                var cells = days.Select(d =>
                {
                    if (!byDay[d].TryGetValue(k, out var l)) return 0.0;
                    while (l.Count < seeds) l.Add(0);
                    return Output.Median(l);
                }).ToList();
                if (cells.All(v => v == 0)) continue;
                sb.AppendLine("| " + k + " | " + string.Join(" | ", cells.Select(Output.F)) + " |");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Per-entity shard currencies are summed per family (shards_hero_*, ...).</summary>
    private static string Bucket(string currency)
    {
        foreach (var prefix in new[] { "shards_hero_", "shards_alien_", "shards_bp_", "shards_mount_" })
            if (currency.StartsWith(prefix, StringComparison.Ordinal)) return prefix + "*";
        return currency;
    }
}
