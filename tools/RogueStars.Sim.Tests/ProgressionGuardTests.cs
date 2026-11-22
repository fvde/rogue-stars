using RogueStars.Sim;

namespace RogueStars.Sim.Tests;

/// <summary>
/// Guard rails for content changes: a 60-day Active F2P and Active Whale account (seed 1) played by the simulator bot
/// through the real rules must stay inside the bands agreed in docs/balance/progression-report.md. If a content change
/// moves a number out of its band, re-run the simulator, update the report, and only then move the band.
/// </summary>
public class ProgressionGuardTests
{
    private const int Days = 60;
    private static readonly Lazy<SimResult> F2p = new(() => Simulation.Run(TimeProfile.Active, SpendProfile.F2P, 1, Days));
    private static readonly Lazy<SimResult> Whale = new(() => Simulation.Run(TimeProfile.Active, SpendProfile.Whale, 1, Days));
    private static readonly Lazy<Analysis.Result> Both = new(() => Analysis.Run(new List<SimResult> { F2p.Value, Whale.Value }, Days));

    private static double At(SimResult r, int day, string metric)
    {
        double v = r.At(day, metric);
        Assert.False(double.IsNaN(v), metric + " missing at day " + day);
        return v;
    }

    [Theory]
    [InlineData(7, 18, 32)]
    [InlineData(30, 32, 55)]
    [InlineData(60, 40, 75)]
    public void ActiveF2p_MainLevel_WithinBand(int day, int min, int max) =>
        Assert.InRange(At(F2p.Value, day, "main_level"), min, max);

    [Theory]
    [InlineData(7, 20, 40)]
    [InlineData(30, 40, 75)]
    [InlineData(60, 50, 100)]
    public void ActiveWhale_MainLevel_WithinBand(int day, int min, int max) =>
        Assert.InRange(At(Whale.Value, day, "main_level"), min, max);

    [Theory]
    [InlineData("Campaign", 10)]
    [InlineData("Worm", 10)]
    [InlineData("Parts", 7)]
    [InlineData("PartLevels", 9)]
    [InlineData("Heroes", 25)]
    [InlineData("Mounts", 21)]
    [InlineData("Buildings", 7)]
    [InlineData("Aliens", 10)]
    [InlineData("Laboratory", 28)]
    public void NoWall_InTheFirst60Days(string system, int maxGapDays)
    {
        foreach (var p in Both.Value.Profiles)
        {
            var st = p.Systems.Single(s => s.System == system);
            Assert.False(double.IsNaN(st.FirstEventDay), $"{p.Profile}: {system} never progressed");
            Assert.True(st.MaxGap[0] <= maxGapDays, $"{p.Profile}: {system} went {st.MaxGap[0]} days without an upgrade (limit {maxGapDays})");
        }
    }

    [Theory]
    [InlineData(30, 1.15, 2.6)]
    [InlineData(60, 1.15, 2.6)]
    public void WhaleToF2p_PowerRatio_WithinBand(int day, double min, double max) =>
        Assert.InRange(At(Whale.Value, day, "power") / At(F2p.Value, day, "power"), min, max);

    [Fact]
    public void F2p_LongHaulSystems_AreNotMaxedEarly()
    {
        // Built for years: two months in, a free player is far from 10★ heroes, maxed mounts or Mythic+3 parts.
        Assert.InRange(At(F2p.Value, 60, "hero_stars_best"), 2, 6);
        Assert.InRange(At(F2p.Value, 60, "mount_stars_best"), 1, 4);
        Assert.True(At(F2p.Value, 60, "part_rarity_avg") < 12.5, "parts near Mythic+2 after two months");
        Assert.True(At(F2p.Value, 60, "lab_research") < At(F2p.Value, 60, "lab_research_max"), "laboratory maxed in two months");
        // Mythic pity about weekly per pull type for an active free player (3 pull types).
        Assert.InRange(At(F2p.Value, 60, "gacha_mythics") / 3.0, 5, 16);
    }

    [Fact]
    public void Simulation_IsDeterministicBySeed()
    {
        var a = Simulation.Run(TimeProfile.Casual, SpendProfile.Dolphin, 7, 5);
        var b = Simulation.Run(TimeProfile.Casual, SpendProfile.Dolphin, 7, 5);
        Assert.Equal(a.Checkpoints[5].Select(x => x.value), b.Checkpoints[5].Select(x => x.value));
        Assert.Equal(a.Commands, b.Commands);
    }
}
