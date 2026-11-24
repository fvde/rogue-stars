using RogueStars.Rules.Content;

namespace RogueStars.Sim;

/// <summary>Result of one simulated account (one profile, one seed).</summary>
public sealed class SimResult
{
    public string Time = "", Spend = "";
    public long Seed;
    public int Days;
    public List<DayRecord> Daily = new();
    /// <summary>Checkpoint day → ordered metrics.</summary>
    public SortedDictionary<int, List<(string name, double value)>> Checkpoints = new();
    public long Commands, FailedCommands;
    /// <summary>Feature id → (account day, main level, minutes played, runs played) of its unlock.</summary>
    public Dictionary<string, (int day, long level, double minutes, int runs)> Unlocks = new();
    public double WallSeconds;
    /// <summary>Every played run (only with keepRunLog).</summary>
    public List<RunRecord> RunLog = new();
    public string Profile => Time + " " + Spend;

    public double At(int day, string metric)
    {
        if (!Checkpoints.TryGetValue(day, out var list)) return double.NaN;
        foreach (var (n, v) in list) if (n == metric) return v;
        return double.NaN;
    }
}

public static class Simulation
{
    public static SimResult Run(TimeProfile time, SpendProfile spend, long seed, int days, Func<ContentDb> content = null, Action<string> log = null, bool keepRunLog = false)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var p = new SimPlayer(time, spend, seed, content?.Invoke()) { KeepRunLog = keepRunLog };
        var res = new SimResult { Time = time.Name, Spend = spend.Name, Seed = seed, Days = days };
        for (int d = 0; d < days; d++)
        {
            p.PlayDay(d);
            res.Daily.Add(Metrics.Day(p, d + 1));
            if (Array.IndexOf(Metrics.Checkpoints, d + 1) >= 0 || d + 1 == days)
            {
                res.Checkpoints[d + 1] = Metrics.Snapshot(p);
                log?.Invoke($"{res.Profile} seed {seed}: day {d + 1} power {p.Power} main {res.Daily[^1].MainLevel} ({sw.Elapsed.TotalSeconds:0}s)");
            }
        }
        res.Commands = p.Commands; res.FailedCommands = p.FailedCommands;
        res.Unlocks = new Dictionary<string, (int day, long level, double minutes, int runs)>(p.Unlocks);
        res.WallSeconds = sw.Elapsed.TotalSeconds;
        res.RunLog = p.RunLog;
        return res;
    }
}
