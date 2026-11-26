using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using Xunit.Abstractions;

namespace RogueStars.Rules.Tests.Integration;

/// <summary>
/// The first levels are a gentle ramp (CampaignDef.EarlyLevelEnemyHpPct / AtkPct): a new player walking the real path
/// (level 1 without parts, then the starter parts, part levels between runs, no worm levels before they unlock) almost
/// never dies in levels 1–3, and seldom in levels 4–6 (later day-1 levels may wall until worm levels unlock on day 3).
/// </summary>
public class EarlyLevelBalanceTests(ITestOutputHelper output)
{
    private const long T0 = TestHelpers.T0, Min = 60_000;
    private const int Seeds = 60, Levels = 10;

    [Fact]
    public void NewPlayerPath_AlmostNeverDiesInTheFirstLevels()
    {
        var c = new ContentDb();
        var losses = new int[Levels + 1];
        var wins = new int[Levels + 1];
        for (int seed = 1; seed <= Seeds; seed++)
        {
            var e = new GameEngine(c) { Cloner = TestHelpers.Clone };
            var s = e.NewPlayer("p", T0, seed);
            long now = T0;
            for (int n = 1; n <= Levels; n++)
            {
                if (!RunSystem.IsLevelUnlocked(s, c, c.MainLevel(n).Id, RunMode.Normal)) { losses[n] += 100; break; }
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    if (s.Wallet.Get(Currencies.Stamina) < 10) s.Wallet.Add(Currencies.Stamina, 50); // stamina is not under test
                    var sum = RunIntegrationTests.AutoRun(e, ref s, c.MainLevel(n).Id, now);
                    now += Min;
                    if (n == 1 && s.Parts.Equipped.Count == 0) RunIntegrationTests.EquipStarterParts(e, ref s, now);
                    RunIntegrationTests.SpendOnUpgrades(e, ref s, now);
                    if (sum.Won) { wins[n]++; break; }
                    losses[n]++;
                }
            }
        }
        for (int n = 1; n <= Levels; n++)
            output.WriteLine($"level {n,2}: losses {losses[n],3} / {Seeds} players");
        // ≤ 4 of 180 runs (was 3; 2 before playtest 0.1.308): playtest 0.1.325's shops (3 skills + support) from level 3 moved
        // one loss of the fixed seeds from level 4 to level 3. Over 300 seeds levels 1–4 lose 20 runs before and after (level 3:
        // 2 → 8, level 4: 8 → 2): the same weak players, one level earlier.
        Assert.True(losses[1] + losses[2] + losses[3] <= 4, $"levels 1-3 lost {losses[1] + losses[2] + losses[3]} times");
        Assert.True(losses[4] + losses[5] + losses[6] <= 30, $"levels 4-6 lost {losses[4] + losses[5] + losses[6]} times");
    }
}
