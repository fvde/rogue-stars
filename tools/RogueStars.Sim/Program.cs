using System.Collections.Concurrent;
using RogueStars.Sim;

// Usage: dotnet run --project tools/RogueStars.Sim -c Release -- [--days 730] [--seeds 3] [--profiles Active:F2P,Active:Whale]
//        --inventory [--seeds 3]: median wallet of a new F2P player at the end of day 1 / 3 / 7 per time profile
//        --unlocks [--days 30] [--seeds 3]: onboarding unlock day/level per feature (Casual, Active ~1 h/day, Hardcore binge)
//        [--out docs/balance] [--label after] [--quiet] [--variant no_lab_stats,old_biomass]
var opts = Args.Parse(args);
if (opts.Has("reward-table"))
{
    // Content-only: the endless reward table (docs/design/economy-model.md); --variant alpha_X to try another α.
    var rc = opts.Has("variant") ? Variants.Create(opts.Str("variant", "")) : SimContent.Create();
    Console.WriteLine(Endless.RewardTable(rc, 100, 250, 500, 1000, 2000));
    return;
}
if (opts.Has("counter-rates"))
{
    // Median daily counter deltas per profile (summon event task thresholds, docs/design/summon-events.md).
    Console.WriteLine(CounterRates.Markdown(opts.Int("days", 60), opts.Int("seeds", 3), opts.Str("profiles", "Casual:F2P,Active:F2P,Hardcore:Whale")));
    return;
}
if (opts.Has("summon"))
{
    // Summon events: pulls per event, heroes / mounts collected and stars at day 30/60/120 (docs/design/summon-events.md).
    Console.WriteLine(SummonReport.Markdown(opts.Int("days", 120), opts.Int("seeds", 3),
        opts.Str("profiles", "Casual:F2P,Active:F2P,Hardcore:F2P,Active:Dolphin,Active:Whale"), new[] { 30, 60, 120, 365 }));
    return;
}
if (opts.Has("pulls"))
{
    // Pull system: collection per profile at day 30/60/120/365 and the excess shard flow into the Shard Reactor (docs/design/pull-system.md).
    Console.WriteLine(PullReport.Markdown(opts.Int("days", 365), opts.Int("seeds", 3),
        opts.Str("profiles", "Casual:F2P,Active:F2P,Hardcore:F2P,Active:Dolphin,Active:Whale"), new[] { 30, 60, 120, 365 }));
    return;
}
if (opts.Has("age-jump"))
{
    // Dev tools' "jump to account day N" (docs/server.md): wall time per profile on one thread (server content, ads disabled).
    int jd = opts.Int("days", AccountAgeJump.MaxDays);
    var jc = new RogueStars.Rules.Content.ContentDb();
    jc.Shop.AdsDisabled = true;
    long jnow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    foreach (var name in AccountAgeJump.ProfileNames)
    {
        var r = AccountAgeJump.Run(jc, "bench", jd, AccountAgeJump.Profile(name), opts.Int("seed", 1), jnow);
        Console.WriteLine($"day {r.Day} {r.Profile}: main level {r.MainLevel}, worm level {r.WormLevel}, {r.Commands} commands, {r.ElapsedMs} ms");
    }
    return;
}
if (opts.Has("alien-share"))
{
    // Aliens pass: each alien's damage contribution at 1★ / 10★ next to a day-N bot worm (AlienShare).
    Console.WriteLine(AlienShare.Markdown(opts.Int("days", 60), opts.Int("seeds", 5)));
    return;
}
if (opts.Has("kit-share"))
{
    // Hero and mount kits: win rate and damage per hero / mount × signature next to a day-N bot worm (KitShare).
    Console.WriteLine(KitShare.Markdown(opts.Int("days", 60), opts.Int("seeds", 8)));
    return;
}
if (opts.Has("run-length"))
{
    // Run length per level band (awakening clock sizing): bots play --days with every run recorded.
    int rd = opts.Int("days", 60), rs = opts.Int("seeds", 2);
    string rv = opts.Str("variant", "");
    string rp = opts.Str("profiles", "Active:F2P,Hardcore:Whale");
    var rprof = new List<(TimeProfile t, SpendProfile s)>();
    foreach (var p in rp.Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
        var parts = p.Split(':');
        rprof.Add((TimeProfile.ByName(parts[0]), SpendProfile.ByName(parts[1])));
    }
    var rjobs = rprof.SelectMany(p => Enumerable.Range(1, rs).Select(seed => (p.t, p.s, seed: (long)seed))).ToList();
    var rres = new ConcurrentBag<SimResult>();
    Parallel.ForEach(rjobs, j => rres.Add(Simulation.Run(j.t, j.s, j.seed, rd, string.IsNullOrEmpty(rv) ? null : () => Variants.Create(rv), null, true)));
    Console.WriteLine($"{rd} days, profiles {rp}, {rs} seeds; main level reached: "
        + string.Join(", ", rres.OrderBy(r => r.Profile).ThenBy(r => r.Seed).Select(r => r.Profile + " #" + r.Seed + " " + r.Daily[^1].MainLevel)));
    Console.WriteLine(RunLength.Markdown(rres));
    return;
}
if (opts.Has("inventory"))
{
    // What a new F2P player owns at the end of day 1 / 3 / 7 (wallet medians per time profile).
    string iv = opts.Str("variant", "");
    Console.WriteLine(Inventory.Markdown(new[] { 1, 3, 7 }, opts.Int("seeds", 3), string.IsNullOrEmpty(iv) ? null : () => Variants.Create(iv)));
    return;
}
if (opts.Has("unlocks"))
{
    // Onboarding schedule check: unlock day/level per feature for a ~1 h/day player (Active) and a binge player (Hardcore).
    int ud = opts.Int("days", 30), us = opts.Int("seeds", 3);
    var ujobs = new[] { TimeProfile.Casual, TimeProfile.Active, TimeProfile.Hardcore }
        .SelectMany(t => Enumerable.Range(1, us).Select(seed => (t, seed: (long)seed))).ToList();
    var ures = new ConcurrentBag<SimResult>();
    string uv = opts.Str("variant", "");
    Parallel.ForEach(ujobs, j => ures.Add(Simulation.Run(j.t, SpendProfile.F2P, j.seed, ud, string.IsNullOrEmpty(uv) ? null : () => Variants.Create(uv))));
    var ordered0 = ures.OrderBy(r => Array.FindIndex(TimeProfile.All, t => t.Name == r.Time)).ThenBy(r => r.Seed).ToList();
    Console.WriteLine(Unlocks.Markdown(ordered0, string.IsNullOrEmpty(uv) ? SimContent.Create() : Variants.Create(uv)));
    return;
}
int days = opts.Int("days", 730);
int seeds = opts.Int("seeds", 3);
string outDir = opts.Str("out", "docs/balance");
string label = opts.Str("label", "");
bool quiet = opts.Has("quiet");
string variant = opts.Str("variant", "");

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
    var r = Simulation.Run(j.t, j.s, j.seed, days, string.IsNullOrEmpty(variant) ? null : () => Variants.Create(variant), quiet ? null : Console.WriteLine);
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
