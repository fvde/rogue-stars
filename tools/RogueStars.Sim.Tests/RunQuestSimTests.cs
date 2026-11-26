using RogueStars.Sim;

namespace RogueStars.Sim.Tests;

/// <summary>
/// Run Quests in the simulator (docs/design/run-quests/recipe.md): an active free player meets the character about once in
/// five runs after the unlock and finishes the first chain as a long-term goal (weeks, ~150–250 runs), never in a few days.
/// </summary>
public class RunQuestSimTests
{
    [Fact]
    public void ActivePlayer_FinishesTheFirstChain_InHundredsOfRuns()
    {
        var row = RunQuestReport.Play(TimeProfile.Active, SpendProfile.F2P, 1, 60, QuestPolicy.Typical);
        Assert.True(row.UnlockDay > 0, "Run Quests not unlocked");
        Assert.True(row.RunsAtComplete >= 0, "chain not finished in 60 days (chapter " + row.Chapter + ")");
        long runs = row.RunsAtComplete - row.RunsAtUnlock;
        Console.WriteLine($"unlock day {row.UnlockDay}, {runs} runs, {row.CompleteDay - row.UnlockDay + 1} days, {row.Encounters} encounters, battles {row.BattlesWon}/{row.Battles}");
        Assert.InRange(runs, 150, 450);
        Assert.InRange(row.Encounters, 30, 90); // 30 chapters + the retries; ~1 in 5 runs
        Assert.True(row.CompleteDay - row.UnlockDay >= 14, "a long-term goal: weeks, not days");
        Assert.True(row.BattlesWon >= 6);
    }
}
