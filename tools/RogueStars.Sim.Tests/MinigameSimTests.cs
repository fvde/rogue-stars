using RogueStars.Rules.Systems;
using RogueStars.Sim;

namespace RogueStars.Sim.Tests;

/// <summary>
/// Run mini-games in the simulator (GDD §3.6): the bot plays GAME tiles with a configurable skill (median player = Silver),
/// so the number of mini-games per run and their effect on the run economy (coins, skill rolls) stay measurable.
/// </summary>
public class MinigameSimTests
{
    private static SimPlayer Play(MinigameTier skill, int days = 5)
    {
        var p = new SimPlayer(TimeProfile.Active, SpendProfile.F2P, 4) { KeepRunLog = true, MinigameSkill = skill };
        for (int d = 0; d < days; d++) p.PlayDay(d);
        return p;
    }

    [Fact]
    public void Runs_PlayOneToTwoMinigames_AtTheBotsSkill()
    {
        var p = Play(MinigameTier.Silver);
        var runs = p.RunLog.Where(r => r.Mode != RunMode.Burrow).ToList();
        Assert.True(runs.Count >= 10, "runs played: " + runs.Count);
        double perRun = runs.Average(r => r.Minigames);
        Assert.InRange(perRun, 0.8, 2.0);
        Assert.All(runs, r => Assert.InRange(r.Minigames, 0, 2));
        // the median player is Silver: most games end Silver, the rest one tier off
        int total = p.MinigameTiers.Sum();
        Assert.Equal(runs.Sum(r => r.Minigames) + p.RunLog.Where(r => r.Mode == RunMode.Burrow).Sum(r => r.Minigames), total);
        Assert.True(p.MinigameTiers[(int)MinigameTier.Silver] > total / 2, string.Join(",", p.MinigameTiers));
        Assert.True(p.MinigameCoins > 0 && p.MinigameSkillRolls > 0, $"coins {p.MinigameCoins}, skill rolls {p.MinigameSkillRolls}");
    }

    [Fact]
    public void BetterPlayers_EarnMoreRunCoins_AndMoreSkills()
    {
        var gold = Play(MinigameTier.Gold);
        var miss = Play(MinigameTier.Miss);
        double CoinsPerGame(SimPlayer p) => (double)p.MinigameCoins / Math.Max(1, p.MinigameTiers.Sum() - p.MinigameSkillRolls);
        Assert.True(CoinsPerGame(gold) > 4 * CoinsPerGame(miss), $"gold {CoinsPerGame(gold):0} vs miss {CoinsPerGame(miss):0} coins per money game");
        Assert.True(gold.MinigameSkillRolls > miss.MinigameSkillRolls, $"skill rolls gold {gold.MinigameSkillRolls} vs miss {miss.MinigameSkillRolls}"); // a Miss opens none
        // the economy impact stays bounded: a gold player's mini-game coins are a fraction of the run's peak coins
        double peak = gold.RunLog.Average(r => (double)r.PeakCoins);
        double perRun = (double)gold.MinigameCoins / gold.RunLog.Count;
        Assert.True(perRun < peak, $"mini-game coins per run {perRun:0} vs peak run coins {peak:0}");
    }
}
