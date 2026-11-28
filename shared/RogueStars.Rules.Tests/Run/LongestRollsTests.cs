using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>"Longest survived": the most rolls of a finished run per level, kept for the recently played levels.</summary>
public class LongestRollsTests
{
    [Fact]
    public void FinishedRuns_RecordTheirLongestRolls_PerLevel()
    {
        var e = RunTestKit.Engine(RunTestKit.Weak());
        var s = RunTestKit.StartRun(e);
        s.Run.Active!.Rolls = 7;
        Assert.True(RunTestKit.Exec(e, ref s, new RunAbandon()).Ok);
        Assert.Equal(7, RunSystem.LongestRolls(s, "main_1"));
        Assert.Equal(7, s.Run.LastSummary.LongestRolls);
        var r = RunTestKit.Exec(e, ref s, new RunStart { LevelId = "main_1" });
        Assert.True(r.Ok, r.Error);
        s.Run.Active!.Rolls = 3;
        Assert.True(RunTestKit.Exec(e, ref s, new RunAbandon()).Ok);
        Assert.Equal(7, RunSystem.LongestRolls(s, "main_1")); // a shorter run keeps the record
        Assert.Equal(7, s.Run.LastSummary.LongestRolls);
        Assert.Equal(0, RunSystem.LongestRolls(s, "nowhere"));
    }

    [Fact]
    public void OnlyTheRecentLevels_AreKept()
    {
        var s = new GameState();
        var m = typeof(RunSystem).GetMethod("RecordRolls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        for (int i = 0; i < RunSystem.LongestRollsKept + 5; i++) m.Invoke(null, new object[] { s, "l" + i, i });
        Assert.Equal(RunSystem.LongestRollsKept, s.Run.LongestRolls.Count);
        Assert.Equal(0, RunSystem.LongestRolls(s, "l0"));
        Assert.Equal(24, RunSystem.LongestRolls(s, "l24"));
    }
}
