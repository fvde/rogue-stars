using System.Collections.Concurrent;
using RogueStars.Sim;

// Usage: dotnet run --project tools/RogueStars.Sim -c Release -- [--days 730] [--seeds 3] [--profiles Active:F2P,Active:Whale]
//        [--out docs/balance] [--label after] [--quiet]
var opts = Args.Parse(args);
int days = opts.Int("days", 730);
int seeds = opts.Int("seeds", 3);
string outDir = opts.Str("out", "docs/balance");
string label = opts.Str("label", "");
bool quiet = opts.Has("quiet");

var profiles = new List<(TimeProfile t, SpendProfile s)>();
if (opts.Has("profiles"))
    foreach (var p in opts.Str("profiles", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
        var parts = p.Split(':');
        profiles.Add((TimeProfile.ByName(parts[0]), SpendProfile.ByName(parts[1])));
    }
else
    foreach (var t in TimeProfile.All) foreach (var s in SpendProfile.All) profiles.Add((t, s));

var jobs = profiles.SelectMany(p => Enumerable.Range(1, seeds).Select(seed => (p.t, p.s, seed: (long)seed))).ToList();
var results = new ConcurrentBag<SimResult>();
var sw = System.Diagnostics.Stopwatch.StartNew();
Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, j =>
{
    var r = Simulation.Run(j.t, j.s, j.seed, days, null, quiet ? null : Console.WriteLine);
    results.Add(r);
    Console.WriteLine($"done {r.Profile} seed {r.Seed}: {r.WallSeconds:0}s, {r.Commands} commands ({r.FailedCommands} rejected)");
});
Console.WriteLine($"all {jobs.Count} simulations in {sw.Elapsed.TotalSeconds:0}s");

var ordered = results.OrderBy(r => Array.FindIndex(TimeProfile.All, t => t.Name == r.Time))
    .ThenBy(r => Array.FindIndex(SpendProfile.All, s => s.Name == r.Spend)).ThenBy(r => r.Seed).ToList();
Output.Write(ordered, outDir, label, days);

internal sealed class Args
{
    private readonly Dictionary<string, string> _v = new();
    public static Args Parse(string[] a)
    {
        var r = new Args();
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].StartsWith("--")) continue;
            string k = a[i][2..];
            r._v[k] = i + 1 < a.Length && !a[i + 1].StartsWith("--") ? a[++i] : "true";
        }
        return r;
    }
    public bool Has(string k) => _v.ContainsKey(k);
    public string Str(string k, string d) => _v.TryGetValue(k, out var v) ? v : d;
    public int Int(string k, int d) => _v.TryGetValue(k, out var v) ? int.Parse(v) : d;
}
