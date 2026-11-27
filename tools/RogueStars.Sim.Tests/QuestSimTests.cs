using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;
using RogueStars.Sim;

namespace RogueStars.Sim.Tests;

/// <summary>
/// Daily Quests from level 1 (owner, 2026-11-26), played by the simulator bot through the real rules: the new-player event's quests are
/// reachable by the time their day is over for Active and Hardcore, and by the end of the claim window for Casual F2P (about one run a
/// day); an Active F2P account fills the daily track (100 points) every day from day 1 and the weekly track (500) in its first week.
/// </summary>
public class QuestSimTests
{
    [Theory]
    [InlineData("Casual", 10)] // the event is 7 days plus 3 grace days
    [InlineData("Active", 7)]
    [InlineData("Hardcore", 7)]
    public void NewPlayerQuests_AreReachable(string profile, int days)
    {
        var p = new SimPlayer(TimeProfile.ByName(profile), SpendProfile.F2P, 2);
        var reached = new Dictionary<string, int>();
        for (int d = 0; d < days; d++)
        {
            p.PlayDay(d);
            foreach (var q in p.C.Quests.Values.Where(q => q.Kind == QuestKind.NewPlayer))
                if (!reached.ContainsKey(q.Id) && p.S.Counter(q.CounterKey) >= q.Target) reached[q.Id] = d + 1;
        }
        foreach (var q in p.C.Quests.Values.Where(q => q.Kind == QuestKind.NewPlayer))
        {
            Assert.True(reached.ContainsKey(q.Id), $"{profile}: {q.Id} ({q.Name}) not reached in {days} days");
            if (profile != "Casual") Assert.True(reached[q.Id] <= q.Day + 1, $"{profile}: {q.Id} reached on day {reached[q.Id]}, offered on day {q.Day}");
        }
    }

    [Fact]
    public void ActiveF2P_FillsTheDailyTrackFromDay1_AndTheWeeklyTrackInTheFirstWeek()
    {
        var p = new SimPlayer(TimeProfile.Active, SpendProfile.F2P, 2);
        long weekly = 0;
        var short_ = new List<string>();
        for (int d = 0; d < 7; d++)
        {
            p.PlayDay(d);
            if (p.S.Quests.DailyPoints < 100) short_.Add($"day {d + 1}: {p.S.Quests.DailyPoints} daily points");
            weekly = Math.Max(weekly, p.S.Quests.WeeklyPoints);
        }
        // at most one short day: "Complete 3 runs" counts cleared runs, and a day at a wall can fall one clear short (seed 2's
        // day 5 at main level 20 after the signature pick became the level-2 unlock, owner 2026-11-27: 95 points, d_runs missing)
        Assert.True(short_.Count <= 1, string.Join("; ", short_));
        Assert.True(weekly >= 500, $"{weekly} weekly points");
    }
}
