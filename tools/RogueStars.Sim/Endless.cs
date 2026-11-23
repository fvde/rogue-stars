using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim;

/// <summary>
/// Endless campaign checks (docs/balance/progression-report.md, "Endless campaign"): runs needed for the next upgrade of
/// each currency sink, by frontier main level, plus campaign pace (days per 50 levels) and milestone/state-size numbers.
/// "Runs per upgrade" = cost of the cheapest next upgrade of a sink (the real content cost curve at the player's current
/// state) ÷ that currency's reward of one won run at the frontier (completion + the 2 completion loot rolls, expected
/// value, no daily surge, no ad doubling; Data: the Dark Side frontier level).
/// </summary>
public static class Endless
{
    public static readonly string[] Sinks =
    {
        "Worm level (Biomass)", "Building (Ore)", "Part level (Ore)", "Part level (Gear Cores)", "Mount level (Fuel)", "Laboratory (Data)",
    };

    /// <summary>Expected amount of <paramref name="currency"/> from one won run of <paramref name="l"/> (completion + loot rolls).</summary>
    public static double RewardPerRun(ContentDb c, LevelDef l, string currency)
    {
        double v = 0;
        foreach (var r in l.CompletionRewards) if (r.Kind == RewardKinds.Currency && r.Id == currency) v += r.Amount;
        var t = c.RunLootTable(l.LootTableId);
        if (t != null && t.Entries.Count > 0)
        {
            double total = t.Entries.Sum(e => e.Weight), per = 0;
            foreach (var e in t.Entries) if (e.Kind == RewardKinds.Currency && e.Id == currency) per += e.Weight / total * (e.Min + e.Max) / 2.0;
            v += per * l.CompletionLootRolls;
        }
        return v * l.RewardMultiplier;
    }

    /// <summary>Runs per next upgrade for each <see cref="Sinks"/> entry (NaN when the sink is maxed / locked).</summary>
    public static double[] RunsPerUpgrade(SimPlayer p)
    {
        var s = p.S; var c = p.C;
        var r = Enumerable.Repeat(double.NaN, Sinks.Length).ToArray();
        int main = (int)s.Counter(Requirements.MainLevelCounter);
        if (main < 1) return r;
        var frontier = c.MainLevel(main);
        double Per(long cost, string cur, LevelDef l)
        {
            double rew = RewardPerRun(c, l, cur);
            return cost > 0 && rew > 0 ? cost / rew : double.NaN;
        }

        r[0] = Per(WormSystem.NextLevelCost(s, c), Currencies.Biomass, frontier);

        long building = long.MaxValue;
        foreach (var b in s.City.Blueprints.Values)
        {
            if (!CitySystem.IsActive(b) || b.Level >= CitySystem.MaxLevel(c, b)) continue;
            long ore = CitySystem.UpgradeCost(c, c.Blueprint(b.Id), b.Level).Where(x => x.Currency == Currencies.Ore).Sum(x => x.Amount);
            if (ore > 0) building = Math.Min(building, ore);
        }
        if (building != long.MaxValue) r[1] = Per(building, Currencies.Ore, frontier);

        var cfg = c.PartsConfig;
        PartItem cheapest = null;
        foreach (var uid in s.Parts.Equipped.Values)
        {
            var part = s.Parts.Items[uid];
            if (part.Level >= PartsSystem.LevelCap(c, part) || part.Level >= cfg.LevelOreCosts.Count) continue;
            if (cheapest == null || cfg.LevelOreCosts[part.Level] < cfg.LevelOreCosts[cheapest.Level]) cheapest = part;
        }
        if (cheapest != null)
        {
            r[2] = Per(cfg.LevelOreCosts[cheapest.Level], Currencies.Ore, frontier);
            r[3] = Per(cfg.LevelGearCoreCosts[cheapest.Level], Currencies.GearCores, frontier);
        }

        var mount = string.IsNullOrEmpty(s.Mounts.Active) ? null : MountSystem.Get(s, s.Mounts.Active);
        var mc = c.MountConfig;
        if (mount != null && mount.Level < mc.MaxLevel(mount.Stars, mount.Awaken)) r[4] = Per(mc.LevelFuelCosts[mount.Level], Currencies.Fuel, frontier);

        int darkMax = (int)s.Counter(RunSystem.DarkSideMaxCounter);
        if (darkMax >= 1 && Metrics.LabDone(s) < Metrics.LabTotal(c)) r[5] = Per(LaboratorySystem.NextDataCost(s, c), Currencies.Data, c.DarkSideLevel(darkMax));
        return r;
    }

    private static readonly int[] Bands = { 1, 100, 200, 300, 400, 500, 600, 700, 800, 1000, 1500, 2000 };

    private static string F(double v) => double.IsNaN(v) ? "–" : v >= 100 ? v.ToString("0", Output.Inv) : v.ToString("0.#", Output.Inv);

    public static string Markdown(List<SimResult> results, ContentDb content, int days)
    {
        var sb = new StringBuilder();
        var groups = results.GroupBy(r => r.Profile).ToList();
        int top = groups.Max(g => g.Max(r => r.Daily[^1].MainLevel));

        sb.AppendLine("## Main level (median of seeds)");
        sb.AppendLine();
        var days0 = new[] { 30, 90, 180, 365, 545, 730, 910, 1095 }.Where(d => d <= days).ToList();
        sb.AppendLine("| Profile | " + string.Join(" | ", days0.Select(d => "d" + d)) + " |");
        sb.AppendLine("|---|" + string.Concat(days0.Select(_ => "---|")));
        foreach (var g in groups)
            sb.AppendLine("| " + g.Key + " | " + string.Join(" | ", days0.Select(d => Output.F(Output.Median(g.Select(r => (double)r.Daily[d - 1].MainLevel))))) + " |");
        sb.AppendLine();

        sb.AppendLine("## Days per 50 main levels (median of seeds; – = not reached)");
        sb.AppendLine();
        var steps = Enumerable.Range(1, Math.Max(1, top / 50)).Select(k => k * 50).ToList();
        sb.AppendLine("| Profile | " + string.Join(" | ", steps.Select(x => (x - 50) + "→" + x)) + " |");
        sb.AppendLine("|---|" + string.Concat(steps.Select(_ => "---|")));
        foreach (var g in groups)
        {
            sb.Append("| ").Append(g.Key).Append(" |");
            foreach (int x in steps)
            {
                var spans = g.Select(r =>
                {
                    int a = r.Daily.FirstOrDefault(d => d.MainLevel >= x - 50)?.Day ?? -1;
                    int b = r.Daily.FirstOrDefault(d => d.MainLevel >= x)?.Day ?? -1;
                    return a >= 0 && b >= 0 ? (double)(b - a) : double.NaN;
                }).ToList();
                sb.Append(' ').Append(spans.Count(v => !double.IsNaN(v)) * 2 > spans.Count ? F(Output.Median(spans)) : "–").Append(" |");
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("## Runs per upgrade by frontier main level (median over the days spent in the band, all seeds)");
        sb.AppendLine();
        sb.AppendLine("Cost of the cheapest next upgrade of each sink ÷ that currency's reward of one won frontier run (completion + 2 loot");
        sb.AppendLine("rolls, expected value, no surge/ads; Data: Dark Side frontier). – = sink maxed/locked or band not reached.");
        sb.AppendLine();
        foreach (var g in groups)
        {
            sb.AppendLine("### " + g.Key);
            sb.AppendLine();
            sb.AppendLine("| Levels | days | " + string.Join(" | ", Sinks) + " |");
            sb.AppendLine("|---|---|" + string.Concat(Sinks.Select(_ => "---|")));
            for (int b = 0; b + 1 < Bands.Length; b++)
            {
                var recs = g.SelectMany(r => r.Daily).Where(d => d.MainLevel >= Bands[b] && d.MainLevel < Bands[b + 1]).ToList();
                if (recs.Count == 0) continue;
                sb.Append("| ").Append(Bands[b]).Append('–').Append(Bands[b + 1] - 1).Append(" | ").Append(F(recs.Count / (double)g.Count())).Append(" |");
                for (int k = 0; k < Sinks.Length; k++)
                    sb.Append(' ').Append(F(Output.Median(recs.Select(d => d.RunsPerUpgrade[k])))).Append(" |");
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Milestones and state size at the end (median of seeds)");
        sb.AppendLine();
        sb.AppendLine("| Profile | main level | milestone chests | level records | archive bytes |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var g in groups)
        {
            double M(string n) => Output.Median(g.Select(r => r.At(r.Checkpoints.Keys.Max(), n)));
            sb.AppendLine("| " + g.Key + " | " + F(M("main_level")) + " | " + F(M("milestone_chests")) + " | " + F(M("run_levels_records")) + " | " + F(M("archive_bytes")) + " |");
        }
        return sb.ToString();
    }
}
