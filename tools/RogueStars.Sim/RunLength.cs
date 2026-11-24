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
        sb.AppendLine("| Band | dice | runs | rolls (all) | rolls (boss reached) | p10-p90 (boss) | boss reached | won | drafts | battles | games | loot offers | loot found | peak coins | min | Biomass / won | Ore / won |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var order = Bands.Select(b => b.name).Concat(new[] { "overcharged", "dark side", "burrow (per run)" }).ToList();
        foreach (var g in runs.GroupBy(r => (band: BandOf(r), dice: r.Dice)).OrderBy(g => order.IndexOf(g.Key.band)).ThenBy(g => g.Key.dice))
        {
            var l = g.ToList();
            var boss = l.Where(r => r.BossReached).Select(r => (double)r.Rolls).OrderBy(x => x).ToList();
            var won = l.Where(r => r.Won).ToList();
            double P(List<double> xs, double q) => xs.Count == 0 ? double.NaN : xs[Math.Min(xs.Count - 1, (int)(q * xs.Count))];
            double Cur(string id) => won.Count == 0 ? 0 : won.Average(r => r.Granted.Where(x => x.Kind == RewardKinds.Currency && x.Id == id).Sum(x => (double)x.Amount));
            sb.AppendLine($"| {g.Key.band} | {g.Key.dice} | {l.Count} | {l.Average(r => r.Rolls):0.0} | {(boss.Count > 0 ? boss.Average() : double.NaN):0.0} | {P(boss, 0.1):0}-{P(boss, 0.9):0} | "
                + $"{100.0 * boss.Count / l.Count:0}% | {100.0 * won.Count / l.Count:0}% | {l.Average(r => r.Drafts):0.0} | {l.Average(r => r.Battles):0.0} | {l.Average(r => r.Minigames):0.0} | "
                + $"{l.Average(r => r.LootOffers):0.0} | {l.Average(r => r.TileLoot):0.0} | {l.Average(r => r.PeakCoins):0} | {l.Average(r => r.Minutes):0.0} | {Cur(Currencies.Biomass):0} | {Cur(Currencies.Ore):0} |");
        }
        sb.AppendLine();
        sb.AppendLine("Build and time per run (all runs; skills = new skills + skill levels + stat perks bought; minutes from the RunClock model: "
            + "manual / auto mode at battle speed ×1 (default) and ×2, farming ×2):");
        sb.AppendLine();
        sb.AppendLine("| Band | dice | won | skills | skills (boss reached) | drafted | mythic | shop | other | shop buys | coins earned | coins spent | minibosses | battles | log entries | manual ×1 | auto ×1 | auto ×2 | farming ×2 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var g in runs.GroupBy(r => (band: BandOf(r), dice: r.Dice)).OrderBy(g => order.IndexOf(g.Key.band)).ThenBy(g => g.Key.dice))
        {
            var l = g.ToList();
            double A(Func<RunRecord, double> f) => l.Average(f);
            double M(RunPlayStyle st, int sp) => l.Average(r => RunClock.Minutes(r.Tally, st, sp));
            sb.AppendLine($"| {g.Key.band} | {g.Key.dice} | {100.0 * l.Count(r => r.Won) / l.Count:0}% | {A(r => r.BuildGains):0.0} | {(l.Any(r => r.BossReached) ? l.Where(r => r.BossReached).Average(r => r.BuildGains) : double.NaN):0.0} | {A(r => r.DraftPicks):0.0} | {A(r => r.MythicPicks):0.0} | "
                + $"{A(r => r.ShopSkills + r.ShopPerks):0.0} | {A(r => r.BuildGains - r.DraftPicks - r.MythicPicks - r.ShopSkills - r.ShopPerks):0.0} | {A(r => r.ShopSkills + r.ShopPerks + r.ShopHeals):0.0} | "
                + $"{A(r => r.CoinsEarned):0} | {A(r => r.CoinsSpent):0} | {A(r => r.Minibosses):0.0} | {A(r => r.Battles):0.0} | {A(r => r.Tally.BattleLogEntries / Math.Max(1.0, r.Tally.Battles)):0} | "
                + $"{M(RunPlayStyle.Manual, 1):0.0} | {M(RunPlayStyle.Auto, 1):0.0} | {M(RunPlayStyle.Auto, 2):0.0} | {M(RunPlayStyle.Farming, 2):0.0} |");
        }
        var main = runs.Where(r => r.Mode != RunMode.Burrow).ToList();
        var mainBoss = main.Where(r => r.BossReached).ToList();
        if (mainBoss.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"All non-Burrow runs ({main.Count}; {mainBoss.Count} reached the boss): skills {mainBoss.Average(r => r.BuildGains):0.0} (boss reached), "
                + $"shop buys {mainBoss.Average(r => r.ShopSkills + r.ShopPerks + r.ShopHeals):0.0}, mythic picks {mainBoss.Average(r => r.MythicPicks):0.0}, "
                + $"coins earned {mainBoss.Average(r => r.CoinsEarned):0} / spent {mainBoss.Average(r => r.CoinsSpent):0}; minutes per run: manual ×1 "
                + $"{main.Average(r => RunClock.Minutes(r.Tally, RunPlayStyle.Manual, 1)):0.0}, auto ×1 {main.Average(r => RunClock.Minutes(r.Tally, RunPlayStyle.Auto, 1)):0.0}, "
                + $"auto ×2 {main.Average(r => RunClock.Minutes(r.Tally, RunPlayStyle.Auto, 2)):0.0}, at the player's speed {main.Average(r => r.Minutes):0.0}, farming ×2 {main.Average(r => RunClock.Minutes(r.Tally, RunPlayStyle.Farming, 2)):0.0}.");
        }
        sb.AppendLine();
        sb.AppendLine(SignatureBreakdown(runs));
        sb.AppendLine();
        sb.AppendLine(SidekickBreakdown(runs));
        return sb.ToString();
    }

    /// <summary>
    /// Sidekick damage share (aliens pass): per alien species (and drones), the runs it fought in and its damage as a share of the
    /// worm's damage in those runs (damage credited to the sidekick; statuses count for their source, an alien's Echo for the alien).
    /// </summary>
    public static string SidekickBreakdown(List<RunRecord> runs)
    {
        var sb = new StringBuilder();
        var l = runs.Where(r => r.WormDamage > 0).ToList();
        sb.AppendLine($"Sidekick damage share (all runs: sidekicks {(l.Count == 0 ? 0 : 100.0 * l.Sum(r => (double)r.SidekickDamage) / l.Sum(r => (double)r.WormDamage)):0.0}% of the worm's damage):");
        sb.AppendLine();
        sb.AppendLine("| Sidekick | runs | share of worm dmg | share (main 11+, OC, dark) |");
        sb.AppendLine("|---|---|---|---|");
        bool Hard(RunRecord r) => r.Mode != RunMode.Burrow && (r.Mode != RunMode.Normal || r.Level >= 11);
        foreach (var id in l.SelectMany(r => r.AlienDamage.Keys).Distinct().OrderBy(x => x, StringComparer.Ordinal))
        {
            var with = l.Where(r => r.AlienDamage.ContainsKey(id)).ToList();
            var hard = with.Where(Hard).ToList();
            double Share(List<RunRecord> xs) => xs.Count == 0 ? double.NaN : 100.0 * xs.Sum(r => (double)r.AlienDamage[id]) / xs.Sum(r => (double)r.WormDamage);
            sb.AppendLine($"| {id} | {with.Count} | {Share(with):0.0}% | {Share(hard):0.0}% ({hard.Count}) |");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Per chosen signature (signatures pass): runs, win rate overall and in the losable bands (main 11+, overcharged, dark side),
    /// damage per battle round relative to the run's band average (1.00 = average; bands differ by orders of magnitude, so raw
    /// damage is compared within a band), damage taken likewise, Supernovas per run and minutes.
    /// </summary>
    public static string SignatureBreakdown(List<RunRecord> runs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Per signature (damage per battle round and damage taken relative to the band's average over all signatures):");
        sb.AppendLine();
        sb.AppendLine("| Signature | runs | won | won (main 11+, OC, dark) | dmg / round (rel.) | taken / round (rel.) | Supernovas / run | skills | min |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        var withRounds = runs.Where(r => r.BattleRounds > 0 && r.Mode != RunMode.Burrow).ToList();
        var bandDealt = withRounds.GroupBy(BandOf).ToDictionary(g => g.Key, g => g.Average(r => (double)r.DamageDealt / r.BattleRounds));
        var bandTaken = withRounds.GroupBy(BandOf).ToDictionary(g => g.Key, g => g.Average(r => (double)r.DamageTaken / r.BattleRounds));
        bool Losable(RunRecord r) => r.Mode != RunMode.Burrow && (r.Mode != RunMode.Normal || r.Level >= 11);
        foreach (var g in runs.Where(r => r.Mode != RunMode.Burrow).GroupBy(r => r.Signature ?? "(none)").OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var l = g.ToList();
            var lr = l.Where(r => r.BattleRounds > 0).ToList();
            var hard = l.Where(Losable).ToList();
            double Rel(Func<RunRecord, double> f, Dictionary<string, double> band) =>
                lr.Count == 0 ? double.NaN : lr.Average(r => band[BandOf(r)] > 0 ? f(r) / band[BandOf(r)] : 1);
            sb.AppendLine($"| {g.Key} | {l.Count} | {100.0 * l.Count(r => r.Won) / l.Count:0}% | {(hard.Count > 0 ? 100.0 * hard.Count(r => r.Won) / hard.Count : double.NaN):0}% ({hard.Count}) | "
                + $"{Rel(r => (double)r.DamageDealt / r.BattleRounds, bandDealt):0.00} | {Rel(r => (double)r.DamageTaken / r.BattleRounds, bandTaken):0.00} | "
                + $"{l.Average(r => r.Supernovas):0.0} | {l.Average(r => r.BuildGains):0.0} | {l.Average(r => r.Minutes):0.0} |");
        }
        return sb.ToString();
    }
}
