using System.Collections.Concurrent;
using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim;

/// <summary>
/// Pull system report (docs/design/pull-system.md): what each profile has collected at given days (heroes, mounts, aliens,
/// blueprints, stars, Mythics) and the excess shard flow into the Shard Reactor (median over seeds).
/// </summary>
public static class PullReport
{
    private sealed class Snap
    {
        public double Heroes, HeroStars, HeroBest, Mounts, MountStars, MountBest;
        public double Aliens, AlienStars, MythicAliens, Blueprints, BpStars, MythicBps;
        public double SignalMythics, SignalPulls, ReactorOwned, ReactorStars, ReactorJobs, ReactorIn, ReactorOut;
        public double ExcessAlien, ExcessBp, MaxedAliens, MaxedBps;
    }

    public static string Markdown(int days, int seeds, string profiles, int[] checkpoints)
    {
        var prof = profiles.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(':'))
            .Select(p => (t: TimeProfile.ByName(p[0]), s: SpendProfile.ByName(p[1]))).ToList();
        var jobs = prof.SelectMany(p => Enumerable.Range(1, seeds).Select(s => (p.t, p.s, seed: (long)s))).ToList();
        var runs = new ConcurrentBag<(string Profile, Dictionary<int, Snap> At)>();
        Parallel.ForEach(jobs, j =>
        {
            var pl = new SimPlayer(j.t, j.s, j.seed);
            var at = new Dictionary<int, Snap>();
            for (int d = 0; d < days; d++)
            {
                pl.PlayDay(d);
                if (checkpoints.Contains(d + 1)) at[d + 1] = SnapOf(pl);
            }
            runs.Add((j.t.Name + ":" + j.s.Name, at));
        });

        var sb = new StringBuilder();
        sb.AppendLine($"Pull system: {days} days, {seeds} seeds per profile (median). Heroes / mounts: owned · ★ sum / best. Aliens / blueprints: owned · ★ sum · Mythic owned. ");
        sb.AppendLine("Excess = shards beyond what the item still needs to 10★ (all aliens / all blueprints). Reactor: owned (share of seeds) · its ★ · jobs · shards in → out.");
        sb.AppendLine();
        sb.AppendLine("| Profile | Day | Heroes | Mounts | Aliens | Blueprints | Signal pulls · Mythics | Excess alien / bp shards | Reactor | Reactor shards in → out |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var g in runs.GroupBy(r => r.Profile).OrderBy(g => g.Key))
            foreach (var day in checkpoints.Where(c => c <= days))
            {
                double M(Func<Snap, double> f) { var l = g.Select(r => f(r.At[day])).OrderBy(x => x).ToList(); return Math.Round(l[l.Count / 2]); }
                double share = g.Average(r => r.At[day].ReactorOwned);
                sb.AppendLine($"| {g.Key} | {day} | {M(s => s.Heroes)} · {M(s => s.HeroStars)} / {M(s => s.HeroBest)} | {M(s => s.Mounts)} · {M(s => s.MountStars)} / {M(s => s.MountBest)} | "
                    + $"{M(s => s.Aliens)} · {M(s => s.AlienStars)}★ · {M(s => s.MythicAliens)} M | {M(s => s.Blueprints)} · {M(s => s.BpStars)}★ · {M(s => s.MythicBps)} M | "
                    + $"{M(s => s.SignalPulls):N0} · {M(s => s.SignalMythics)} | {M(s => s.ExcessAlien):N0} / {M(s => s.ExcessBp):N0} | "
                    + $"{share * 100:0}% · {M(s => s.ReactorStars)}★ · {M(s => s.ReactorJobs)} | {M(s => s.ReactorIn):N0} → {M(s => s.ReactorOut):N0} |");
            }
        return sb.ToString();
    }

    private static Snap SnapOf(SimPlayer pl)
    {
        var s = pl.S; var c = pl.C;
        var reactor = ShardReactorSystem.Building(s, c);
        return new Snap
        {
            Heroes = s.Heroes.Owned.Count, HeroStars = s.Heroes.Owned.Values.Sum(h => h.Stars), HeroBest = s.Heroes.Owned.Values.Select(h => h.Stars).DefaultIfEmpty(0).Max(),
            Mounts = s.Mounts.Owned.Count, MountStars = s.Mounts.Owned.Values.Sum(m => m.Stars), MountBest = s.Mounts.Owned.Values.Select(m => m.Stars).DefaultIfEmpty(0).Max(),
            Aliens = s.Aliens.Owned.Count, AlienStars = s.Aliens.Owned.Values.Sum(a => a.Stars),
            MythicAliens = s.Aliens.Owned.Keys.Count(id => c.AlienType(id)?.Rarity == Rarity.Mythic),
            Blueprints = s.City.Blueprints.Count, BpStars = s.City.Blueprints.Values.Sum(b => b.Stars),
            MythicBps = s.City.Blueprints.Keys.Count(id => c.Blueprint(id)?.Rarity == Rarity.Mythic),
            SignalPulls = s.Counter("pulls_total"), SignalMythics = s.Counter("pulls_mythic"),
            ReactorOwned = reactor != null ? 1 : 0, ReactorStars = reactor?.Stars ?? 0,
            ReactorJobs = s.Counter("reactor_jobs_started"), ReactorIn = s.Counter("reactor_shards_in"), ReactorOut = s.Counter("reactor_shards_out"),
            ExcessAlien = c.AlienTypes.Keys.Sum(id => ShardReactorSystem.ExcessShards(s, c, ReactorKind.Alien, id)),
            ExcessBp = c.Blueprints.Keys.Sum(id => ShardReactorSystem.ExcessShards(s, c, ReactorKind.Blueprint, id)),
        };
    }
}
