namespace RogueStars.Sim;

/// <summary>
/// Smoothness analysis per profile (medians over seeds): walls (long gaps between meaningful upgrades per system),
/// early caps (system stalls before day 365), hoarding/starvation per currency, power growth rate, F2P/whale ratios.
/// </summary>
public static class Analysis
{
    public static readonly (int from, int to)[] Windows = { (1, 60), (61, 180), (181, 365), (366, 730) };
    /// <summary>
    /// A gap without any meaningful upgrade longer than this (per window) is a wall: a week early on, then longer as
    /// long-haul systems slow down by design (one meaningful step a month is fine in year two).
    /// </summary>
    public static readonly int[] WallDaysPerWindow = { 7, 14, 21, 35 };
    public const int WallDays = 7;

    public sealed class SystemStats
    {
        public string System = "";
        public double[] MaxGap = new double[Windows.Length];      // median over seeds, per window
        public double[] EventsPer30 = new double[Windows.Length];
        public double LastEventDay;
        public double FirstEventDay;
    }

    public sealed class CurrencyStats
    {
        public string Currency = "";
        public double DaysBanked365, DaysBanked730;    // balance / avg daily income (last 30 days)
        public double SpendShare365, SpendShare730;    // spent / earned over the previous 90 days
        public double Balance365, Balance730;
    }

    public sealed class ProfileAnalysis
    {
        public string Profile = "", Time = "", Spend = "";
        public List<SystemStats> Systems = new();
        public List<CurrencyStats> Currencies = new();
        /// <summary>Median weekly power growth (%) per window and the share of flat 4-week windows (growth &lt; 1%).</summary>
        public double[] WeeklyGrowthPct = new double[Windows.Length];
        public double[] FlatShare = new double[Windows.Length];
        public List<string> Flags = new();
    }

    public sealed class Result
    {
        public List<ProfileAnalysis> Profiles = new();
        public Dictionary<string, SortedDictionary<int, Dictionary<string, double>>> Medians = new();
        public int Days;
    }

    public static Result Run(List<SimResult> results, int days)
    {
        var res = new Result { Days = days, Medians = Output.Medians(results) };
        foreach (var g in results.GroupBy(r => r.Profile))
        {
            var pa = new ProfileAnalysis { Profile = g.Key, Time = g.First().Time, Spend = g.First().Spend };
            for (int si = 0; si < SystemEvents.Systems.Length; si++)
            {
                var st = new SystemStats { System = SystemEvents.Systems[si].system };
                var perSeedGaps = new List<double[]>(); var perSeedRate = new List<double[]>();
                var last = new List<double>(); var first = new List<double>();
                foreach (var r in g)
                {
                    var eventDays = new List<int>();
                    long prev = 0;
                    foreach (var d in r.Daily)
                    {
                        if (d.SystemTotals[si] > prev) eventDays.Add(d.Day);
                        prev = d.SystemTotals[si];
                    }
                    var gaps = new double[Windows.Length]; var rate = new double[Windows.Length];
                    for (int w = 0; w < Windows.Length; w++)
                    {
                        var (a, b) = Windows[w];
                        if (a > days) { gaps[w] = double.NaN; rate[w] = double.NaN; continue; }
                        b = Math.Min(b, days);
                        // Longest stretch in [a, b] without an event (open at both ends of the window).
                        int lastEv = a - 1; int maxGap = 0;
                        foreach (var ed in eventDays.Where(x => x >= a && x <= b)) { maxGap = Math.Max(maxGap, ed - lastEv - 1); lastEv = ed; }
                        maxGap = Math.Max(maxGap, b - lastEv);
                        gaps[w] = maxGap;
                        long startTot = a >= 2 ? r.Daily[a - 2].SystemTotals[si] : 0;
                        long endTot = r.Daily[b - 1].SystemTotals[si];
                        rate[w] = (endTot - startTot) * 30.0 / (b - a + 1);
                    }
                    perSeedGaps.Add(gaps); perSeedRate.Add(rate);
                    last.Add(eventDays.Count == 0 ? 0 : eventDays[^1]);
                    first.Add(eventDays.Count == 0 ? double.NaN : eventDays[0]);
                }
                for (int w = 0; w < Windows.Length; w++)
                {
                    st.MaxGap[w] = Output.Median(perSeedGaps.Select(x => x[w]));
                    st.EventsPer30[w] = Output.Median(perSeedRate.Select(x => x[w]));
                }
                st.LastEventDay = Output.Median(last);
                st.FirstEventDay = Output.Median(first);
                pa.Systems.Add(st);

                bool unlockedLate = double.IsNaN(st.FirstEventDay);
                if (unlockedLate) pa.Flags.Add($"never: {st.System} (no upgrade in {days} days)");
                else
                {
                    for (int w = 0; w < Windows.Length; w++)
                        if (Windows[w].from <= days && st.MaxGap[w] > WallDaysPerWindow[w] && Windows[w].from >= st.FirstEventDay - 1)
                            pa.Flags.Add($"wall: {st.System} {st.MaxGap[w]:0} days without an upgrade in days {Windows[w].from}–{Math.Min(days, Windows[w].to)}");
                    if (days >= 365 && st.LastEventDay < Math.Min(365, days - WallDaysPerWindow[^1]))
                        pa.Flags.Add($"cap/stall: {st.System} last upgrade on day {st.LastEventDay:0}");
                }
            }

            for (int ci = 0; ci < SimPlayer.Tracked.Length; ci++)
            {
                var cs = new CurrencyStats { Currency = SimPlayer.Tracked[ci] };
                (double banked, double share, double bal) At(SimResult r, int day)
                {
                    if (day > days) return (double.NaN, double.NaN, double.NaN);
                    var d = r.Daily[day - 1];
                    var d30 = r.Daily[Math.Max(0, day - 31)];
                    var d90 = r.Daily[Math.Max(0, day - 91)];
                    double inc30 = (d.Earned[ci] - d30.Earned[ci]) / 30.0;
                    double earned90 = d.Earned[ci] - d90.Earned[ci], spent90 = d.Spent[ci] - d90.Spent[ci];
                    return (inc30 > 0 ? d.Balances[ci] / inc30 : (d.Balances[ci] > 0 ? 999 : 0), earned90 > 0 ? spent90 / earned90 : double.NaN, d.Balances[ci]);
                }
                cs.DaysBanked365 = Output.Median(g.Select(r => At(r, 365).banked));
                cs.SpendShare365 = Output.Median(g.Select(r => At(r, 365).share));
                cs.Balance365 = Output.Median(g.Select(r => At(r, 365).bal));
                cs.DaysBanked730 = Output.Median(g.Select(r => At(r, Math.Min(730, days)).banked));
                cs.SpendShare730 = Output.Median(g.Select(r => At(r, Math.Min(730, days)).share));
                cs.Balance730 = Output.Median(g.Select(r => At(r, Math.Min(730, days)).bal));
                pa.Currencies.Add(cs);
                bool regen = cs.Currency == RogueStars.Rules.Core.Currencies.Stamina || cs.Currency == RogueStars.Rules.Core.Currencies.ArenaTickets;
                if (!regen && cs.DaysBanked730 > 45 && cs.SpendShare730 < 0.5 && cs.Balance730 > 0)
                    pa.Flags.Add($"hoarding: {cs.Currency} {Output.F(cs.Balance730)} banked = {cs.DaysBanked730:0} days of income, {cs.SpendShare730 * 100:0}% spent (last 90 days, day {Math.Min(730, days)})");
                if (!regen && double.IsNaN(cs.SpendShare730) && cs.Balance730 <= 0 && days >= 365)
                    pa.Flags.Add($"no income: {cs.Currency}");
            }

            for (int w = 0; w < Windows.Length; w++)
            {
                var (a, b) = Windows[w];
                if (a > days) { pa.WeeklyGrowthPct[w] = double.NaN; pa.FlatShare[w] = double.NaN; continue; }
                b = Math.Min(b, days);
                var growth = new List<double>(); var flats = new List<double>();
                foreach (var r in g)
                {
                    double p0 = Math.Max(1, r.Daily[a - 1].Power), p1 = Math.Max(1, r.Daily[b - 1].Power);
                    double weeks = (b - a) / 7.0;
                    growth.Add(weeks > 0 ? (Math.Pow(p1 / p0, 1 / weeks) - 1) * 100 : 0);
                    int flat = 0, total = 0;
                    for (int d = a + 27; d <= b; d += 7)
                    {
                        total++;
                        if (r.Daily[d - 1].Power < r.Daily[d - 28].Power * 1.01) flat++;
                    }
                    flats.Add(total == 0 ? 0 : (double)flat / total);
                }
                pa.WeeklyGrowthPct[w] = Output.Median(growth);
                pa.FlatShare[w] = Output.Median(flats);
                if (pa.FlatShare[w] > 0.25) pa.Flags.Add($"flat power: {pa.FlatShare[w] * 100:0}% of 4-week windows grow < 1% in days {a}–{b}");
            }
            res.Profiles.Add(pa);
        }
        return res;
    }
}
