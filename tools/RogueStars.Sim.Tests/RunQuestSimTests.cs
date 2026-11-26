using RogueStars.Sim;

namespace RogueStars.Sim.Tests;

/// <summary>
/// Run Quests in the simulator (docs/design/run-quests/recipe.md): with no level gate and a plain 20% chance per run, an active
/// free player meets the character in the first days and finishes the first chain as a long-term goal (weeks, hundreds of
/// runs), never in a few days.
/// </summary>
public class RunQuestSimTests
{
    [Fact]
    public void ActivePlayer_FinishesTheFirstChain_InHundredsOfRuns()
    {
        var row = RunQuestReport.Play(TimeProfile.Active, SpendProfile.F2P, 1, 180, QuestPolicy.Typical);
        Assert.InRange(row.FirstMeetDay, 1, 3); // no level gate: the first days
        Assert.True(row.RunsAtComplete >= 0, "chain not finished in 180 days (chapter " + row.Chapter + ")");
        Console.WriteLine($"first meeting day {row.FirstMeetDay}, {row.RunsAtComplete} runs, {row.CompleteDay} days, {row.Encounters} encounters, battles {row.BattlesWon}/{row.Battles} ({row.BattlesLost} lost)");
        Assert.InRange(row.RunsAtComplete, 150, 500);
        Assert.InRange(row.Encounters, 30, 100); // 30 chapters + the retries; ~1 in 5 runs
        Assert.True(row.CompleteDay >= 14, "a long-term goal: weeks, not days");
        Assert.True(row.BattlesWon >= 6);
    }
}
