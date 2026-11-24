using System.Collections.Concurrent;
using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim;

/// <summary>
/// Summon events report (docs/design/summon-events.md): pulls per hero / mount event, featured copies, Luck guarantees,
/// heroes / mounts collected and their stars at given days, per profile (median over seeds).
/// </summary>
public static class SummonReport
{
    private sealed class Run
    {
        public string Profile = "";
        /// <summary>Event id → pulls made in it (complete events only).</summary>
        public readonly Dictionary<SummonEventType, Dictionary<string, int>> Pulls = new();
        public readonly Dictionary<int, Snap> At = new();
        public long SpentCents;
    }

    private sealed class Snap
    {
        public int Heroes, HeroStars, HeroBest, Mounts, MountStars, MountAwaken, MountBest;
        public long HeroPulls, MountPulls, HeroCopies, MountCopies;
    }

    public static string Markdown(int days, int seeds, string profiles, int[] checkpoints)
    {
        var prof = profiles.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(':'))
            .Select(p => (t: TimeProfile.ByName(p[0]), s: SpendProfile.ByName(p[1]))).ToList();
        var jobs = prof.SelectMany(p => Enumerable.Range(1, seeds).Select(s => (p.t, p.s, seed: (long)s))).ToList();
        var runs = new ConcurrentBag<Run>();
        Parallel.ForEach(jobs, j =>
        {
            var pl = new SimPlayer(j.t, j.s, j.seed);
            var r = new Run { Profile = j.t.Name + ":" + j.s.Name };
            foreach (var t in SummonEventSystem.Types) r.Pulls[t] = new Dictionary<string, int>();
            var last = new Dictionary<SummonEventType, SummonEventProgress>();
            for (int d = 0; d < days; d++)
            {
                pl.PlayDay(d);
                foreach (var t in SummonEventSystem.Types)
                    if (pl.S.Summons.Events.TryGetValue(t, out var p))
                    {
                        if (last.TryGetValue(t, out var prev) && prev.EventId != p.EventId) r.Pulls[t][prev.EventId] = prev.Pulls;
                        last[t] = TestClone(p);
                    }
                if (checkpoints.Contains(d + 1)) r.At[d + 1] = SnapOf(pl);
            }
            r.SpentCents = pl.SpentCents;
            runs.Add(r);
        });

        var sb = new StringBuilder();
        sb.AppendLine($"Summon events: {days} days, {seeds} seeds per profile (median); pulls per complete event (welcome events excluded).");
        sb.AppendLine();
        sb.AppendLine("| Profile | Pulls / hero event | Pulls / mount event | Spent $/30 days |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var g in runs.GroupBy(r => r.Profile).OrderBy(g => g.Key))
        {
            double Med(IEnumerable<double> xs) { var l = xs.OrderBy(x => x).ToList(); return l.Count == 0 ? double.NaN : l[l.Count / 2]; }
            double PerEvent(SummonEventType t) => Med(g.Select(r => r.Pulls[t].Where(kv => !kv.Key.EndsWith("@welcome")).Select(kv => (double)kv.Value).DefaultIfEmpty(0).Average()));
            sb.AppendLine($"| {g.Key} | {PerEvent(SummonEventType.Hero):0} | {PerEvent(SummonEventType.Mount):0} | {Med(g.Select(r => r.SpentCents / 100.0 / days * 30)):0} |");
        }
        sb.AppendLine();
        sb.AppendLine("| Profile | Day | Heroes owned | Hero stars (sum / best) | Hero pulls | Hero copies | Mounts owned | Mount stars (sum / best) | Mount awakening (sum) | Mount pulls | Mount copies |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var g in runs.GroupBy(r => r.Profile).OrderBy(g => g.Key))
            foreach (var day in checkpoints.Where(c => c <= days))
            {
                int Med(Func<Snap, double> f) { var l = g.Select(r => f(r.At[day])).OrderBy(x => x).ToList(); return (int)Math.Round(l[l.Count / 2]); }
                sb.AppendLine($"| {g.Key} | {day} | {Med(s => s.Heroes)} | {Med(s => s.HeroStars)} / {Med(s => s.HeroBest)} | {Med(s => s.HeroPulls)} | {Med(s => s.HeroCopies)} | "
                    + $"{Med(s => s.Mounts)} | {Med(s => s.MountStars)} / {Med(s => s.MountBest)} | {Med(s => s.MountAwaken)} | {Med(s => s.MountPulls)} | {Med(s => s.MountCopies)} |");
            }
        return sb.ToString();
    }

    private static SummonEventProgress TestClone(SummonEventProgress p) => new() { EventId = p.EventId, Pulls = p.Pulls };

    private static Snap SnapOf(SimPlayer pl)
    {
        var s = pl.S;
        return new Snap
        {
            Heroes = s.Heroes.Owned.Count,
            HeroStars = s.Heroes.Owned.Values.Sum(h => h.Stars),
            HeroBest = s.Heroes.Owned.Values.Select(h => h.Stars).DefaultIfEmpty(0).Max(),
            Mounts = s.Mounts.Owned.Count,
            MountStars = s.Mounts.Owned.Values.Sum(m => m.Stars),
            MountBest = s.Mounts.Owned.Values.Select(m => m.Stars).DefaultIfEmpty(0).Max(),
            MountAwaken = s.Mounts.Owned.Values.Sum(m => m.Awaken),
            HeroPulls = s.Counter("summon_pulls_hero"), MountPulls = s.Counter("summon_pulls_mount"),
            HeroCopies = s.Counter("hero_copies"), MountCopies = s.Counter("mount_copies"),
        };
    }
}
