using System.Collections.Concurrent;
using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;

namespace RogueStars.Sim;

/// <summary>
/// Run Quests (docs/design/run-quests/recipe.md): how long a quest chain takes per profile. Bots play from a new account (Run
/// Quests have no level gate); the report counts runs (not the Burrow), encounters, battles (a lost one ends the run) and
/// prices until the first chain is complete (median / min / max of seeds; "–" = not finished within the simulated days).
/// </summary>
public static class RunQuestReport
{
    public sealed class Row
    {
        public string Profile = "", Policy = "";
        public long Seed;
        public int FirstMeetDay = -1, CompleteDay = -1;
        public long RunsAtComplete = -1;
        public int Encounters, Battles, BattlesWon, BattlesLost, Paid, Unaffordable, Chapter;
        public int[] Waits = new int[4];
        public int[] UnaffordableBy = new int[6];
        public double RunsPerDay;
    }

    public static Row Play(TimeProfile time, SpendProfile spend, long seed, int days, QuestPolicy policy)
    {
        var p = new SimPlayer(time, spend, seed) { QuestPolicy = policy };
        var row = new Row { Profile = time.Name + " " + spend.Name, Policy = policy.ToString(), Seed = seed };
        var q = p.C.RunQuestOrder[0];
        long Eligible() => p.S.Counter("runs_started") - p.S.Counter("runs_started_burrow");
        for (int d = 0; d < days; d++)
        {
            p.PlayDay(d);
            var prog = RunSystem.QuestProgressOf(p.S, q.Id);
            if (row.FirstMeetDay < 0 && prog != null) row.FirstMeetDay = d + 1;
            if (row.CompleteDay < 0 && prog != null && prog.Completed) { row.CompleteDay = d + 1; row.RunsAtComplete = Eligible(); break; }
        }
        var pr = RunSystem.QuestProgressOf(p.S, q.Id);
        row.Encounters = p.QuestEncounters; row.Battles = p.QuestBattles; row.BattlesWon = p.QuestBattlesWon; row.BattlesLost = p.QuestBattlesLost; row.Paid = p.QuestPaid;
        row.Unaffordable = p.QuestUnaffordable; row.UnaffordableBy = (int[])p.QuestUnaffordableBy.Clone(); row.Waits = (int[])p.QuestWaits.Clone();
        row.Chapter = RunSystem.QuestChapter(p.C, p.S, q);
        int span = row.CompleteDay > 0 ? row.CompleteDay : days;
        long runs = row.RunsAtComplete >= 0 ? row.RunsAtComplete : Eligible();
        row.RunsPerDay = span > 0 ? runs / (double)span : 0;
        return row;
    }

    public static string Markdown(int days, int seeds, string profiles)
    {
        var prof = profiles.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split(':'))
            .Select(x => (TimeProfile.ByName(x[0]), SpendProfile.ByName(x[1]))).ToList();
        var jobs = new List<(TimeProfile t, SpendProfile s, long seed, QuestPolicy pol)>();
        foreach (var (t, s) in prof)
            foreach (var pol in new[] { QuestPolicy.Typical, QuestPolicy.Eager })
                for (int i = 1; i <= seeds; i++) jobs.Add((t, s, i, pol));
        var rows = new ConcurrentBag<Row>();
        Parallel.ForEach(jobs, j => rows.Add(Play(j.t, j.s, j.seed, days, j.pol)));
        var sb = new StringBuilder();
        var c = ContentDb.Default;
        var q = c.RunQuestOrder[0];
        sb.AppendLine($"## Run Quests: {q.Character} ({q.Chapters} chapters), {days} days max, {seeds} seeds per profile and policy");
        sb.AppendLine();
        sb.AppendLine($"No level gate: runs = every run but the Burrow from the first day (the tutorial run included). Chance {c.QuestTuning.ChancePct:0}% per run, no pity. Median [min–max] of seeds.");
        sb.AppendLine();
        sb.AppendLine("| Profile | Policy | First meeting (day) | Runs to finish | Days to finish | Runs/day | Encounters | Battles won/fought (lost = run over) | Waits story/battle/price | Unaffordable | Done |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var g in rows.GroupBy(r => (r.Profile, r.Policy)).OrderBy(g => g.Key.Profile).ThenBy(g => g.Key.Policy))
        {
            var l = g.ToList();
            var done = l.Where(r => r.RunsAtComplete >= 0).ToList();
            string Med(IEnumerable<double> xs)
            {
                var a = xs.ToList();
                return a.Count == 0 ? "–" : $"{Output.Median(a):0} [{a.Min():0}–{a.Max():0}]";
            }
            sb.Append($"| {g.Key.Profile} | {g.Key.Policy} | {Med(l.Where(r => r.FirstMeetDay > 0).Select(r => (double)r.FirstMeetDay))} | ");
            sb.Append(Med(done.Select(r => (double)r.RunsAtComplete))).Append(" | ");
            sb.Append(Med(done.Select(r => (double)r.CompleteDay))).Append(" | ");
            sb.Append($"{Output.Median(l.Select(r => r.RunsPerDay)):0.0} | ");
            sb.Append(Med(l.Select(r => (double)r.Encounters))).Append(" | ");
            sb.Append($"{l.Sum(r => r.BattlesWon)}/{l.Sum(r => r.Battles)} ({l.Sum(r => r.BattlesLost)} lost) | ");
            sb.Append($"{l.Sum(r => r.Waits[0])}/{l.Sum(r => r.Waits[1])}/{l.Sum(r => r.Waits[2])} | {l.Sum(r => r.Unaffordable)} ({string.Join(", ", Enumerable.Range(1, 5).Where(k => l.Sum(r => r.UnaffordableBy[k]) > 0).Select(k => (QuestCostKind)k + " " + l.Sum(r => r.UnaffordableBy[k])))}) | ");
            sb.AppendLine($"{done.Count}/{l.Count} (unfinished at chapter {string.Join(", ", l.Where(r => r.RunsAtComplete < 0).Select(r => r.Chapter))}) |");
        }
        return sb.ToString();
    }
}
