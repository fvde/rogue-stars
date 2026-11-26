using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;
using RogueStars.Sim;

namespace RogueStars.Sim.Tests;

/// <summary>
/// GDD §16 level unlocks played by the simulator bot: every feature unlocks the moment its level is cleared (never later
/// because of the calendar), and the first hours bring a steady stream of new things (the novelty gaps stay short).
/// </summary>
public class UnlockSimTests
{
    [Theory]
    [InlineData("Casual", 7)]
    [InlineData("Active", 3)]
    [InlineData("Hardcore", 2)]
    public void Features_UnlockAtTheirLevel_NeverWaitForADay(string profile, int days)
    {
        var r = Simulation.Run(TimeProfile.ByName(profile), SpendProfile.F2P, 2, days);
        var c = SimContent.Create();
        Assert.NotEmpty(r.Unlocks);
        foreach (var f in c.FeatureOrder)
        {
            long gate = Features.UnlockLevel(f);
            if (gate == 0) continue; // a building feature: unlocks with its blueprint (pulled), not at a level
            if (!r.Unlocks.TryGetValue(f.Id, out var u))
            {
                Assert.True(r.Daily[^1].MainLevel <= gate, $"{profile}: {f.Id} (L{gate}) not unlocked at main level {r.Daily[^1].MainLevel}");
                continue; // (a clear on the last command of the run is recorded by the next command)
            }
            Assert.True(u.level >= gate, $"{profile}: {f.Id} unlocked at L{u.level} before its gate L{gate}");
            Assert.True(u.level <= gate + 1, $"{profile}: {f.Id} unlocked at L{u.level}, its gate is L{gate}"); // recorded with the clear (or the next command)
        }
    }

    [Theory]
    [InlineData("Casual")]
    [InlineData("Active")]
    [InlineData("Hardcore")]
    public void FirstHours_BringANewThingEveryFewRuns(string profile)
    {
        var r = Simulation.Run(TimeProfile.ByName(profile), SpendProfile.F2P, 1, profile == "Casual" ? 12 : 3);
        var minutes = Unlocks.UnlockMinutes(r, SimContent.Create());
        double played = r.Daily[^1].Minutes;
        Assert.True(minutes.Count(m => m <= 60) >= 5, $"{profile}: {minutes.Count(m => m <= 60)} new things in the first hour");
        // 9 (was 10): the Part Signal now comes with Parts instead of its own unlock at level 10 (owner, 2026-11-25)
        // 8 (was 9): Daily Quests are no feature any more, no unlock moment (owner, 2026-11-26); Casual F2P gets 8
        Assert.True(minutes.Count(m => m <= 120) >= 8, $"{profile}: {minutes.Count(m => m <= 120)} new things in the first two hours");
        // the longest gaps: the median of seeds 1–3 (playtest 0.1.325: one seed's level-12/13 wall alone moved the gap by
        // ±20 min — over seeds 1–10 the gap is 19–46 min before and 21–44 after, two seeds above 40 either way)
        var gaps = new[] { r }.Concat(new long[] { 2, 3 }.AsParallel().Select(seed => Simulation.Run(TimeProfile.ByName(profile), SpendProfile.F2P, seed, profile == "Casual" ? 12 : 3)))
            .Select(x => { var m = Unlocks.UnlockMinutes(x, SimContent.Create()); double p = x.Daily[^1].Minutes; return (g60: Unlocks.LongestGap(m, 60, p), g120: Unlocks.LongestGap(m, 120, p)); })
            .ToList();
        double gap60 = gaps.Select(g => g.g60).OrderBy(g => g).ElementAt(1), gap120 = gaps.Select(g => g.g120).OrderBy(g => g).ElementAt(1);
        Assert.True(gap60 <= 25, $"{profile}: {gap60:0} min without anything new in the first hour (median of 3 seeds)");
        Assert.True(gap120 <= 40, $"{profile}: {gap120:0} min without anything new in the first two hours (median of 3 seeds)");
    }
}
