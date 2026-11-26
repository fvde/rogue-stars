using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using Xunit.Abstractions;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Playtest 0.1.325: an event paid 60 Biomass while the run's completion paid ~2 000, so events felt meaningless. A reward
/// chest (events, Treasure Pods) now pays RunTuning.RewardChestCompletionPct of the level's completion rewards in bulk
/// resources on top of its loot roll: a 1-chest event ≈ 15% and a 2-chest event (Gravity Well, Treasure Pod) ≈ 30% of the
/// run's completion payout, at every level.
/// </summary>
public class RewardChestTests(ITestOutputHelper output)
{
    private static readonly ContentDb C = ContentDb.Default;

    /// <summary>Expected amount of <paramref name="currency"/> of one loot roll of the level's table (midpoint of the range).</summary>
    private static double RollValue(LevelDef l, string currency)
    {
        var t = C.RunLootTable(l.LootTableId);
        double w = t.Entries.Sum(e => e.Weight);
        return t.Entries.Where(e => e.Kind == RewardKinds.Currency && e.Id == currency).Sum(e => e.Weight / w * (e.Min + e.Max) / 2.0);
    }

    private static double Completion(LevelDef l, string currency) =>
        l.CompletionRewards.Where(r => r.Id == currency).Sum(r => r.Amount) + l.CompletionLootRolls * RollValue(l, currency);

    private static double Chest(LevelDef l, string currency, bool withBundle = true) =>
        (withBundle ? RunSystem.RewardChestBundle(C, l).Where(r => r.Id == currency).Sum(r => r.Amount) : 0) + RollValue(l, currency);

    [Fact]
    public void RewardChests_PayANotableShareOfTheRun_AtEveryLevel()
    {
        output.WriteLine("level | completion Biomass | 1 chest before → after | share before → after | 2 chests share after");
        foreach (int n in new[] { 1, 3, 5, 8, 10, 15, 20, 30, 45, 60, 100, 150, 300 })
        {
            var l = C.MainLevel(n)!;
            double comp = Completion(l, Currencies.Biomass), before = Chest(l, Currencies.Biomass, false), after = Chest(l, Currencies.Biomass);
            output.WriteLine($"{n} | {comp:0} | {before:0} → {after:0} | {before / comp:P0} → {after / comp:P0} | {2 * after / comp:P0}");
            Assert.InRange(after / comp, 0.12, 0.20);      // a 1-chest event (Abandoned Cache, Wild Beast)
            Assert.InRange(2 * after / comp, 0.24, 0.36);  // a 2-chest event (Gravity Well, Treasure Pod)
            double ore = Chest(l, Currencies.Ore) / Completion(l, Currencies.Ore);
            Assert.InRange(ore, 0.10, 0.25);
        }
        foreach (int n in new[] { 1, 10, 50, 150 })
        {
            var d = C.DarkSideLevel(n);
            double share = Chest(d, Currencies.Data) / Completion(d, Currencies.Data);
            output.WriteLine($"dark {n} | Data {Completion(d, Currencies.Data):0} | chest {Chest(d, Currencies.Data):0} ({share:P0})");
            Assert.InRange(share, 0.08, 0.20);
        }
    }

    [Fact]
    public void ChestBundle_IsRoundNumbers_AndNeverPremium()
    {
        for (int n = 1; n <= 300; n++)
            foreach (var r in RunSystem.RewardChestBundle(C, C.MainLevel(n)!))
            {
                Assert.Contains(r.Id, C.RunTuning.RewardChestCurrencies);
                Assert.True(r.Amount < 10 || r.Amount % 5 == 0, r.Id + " " + r.Amount);
                Assert.True(r.Amount < 100 || r.Amount % 10 == 0, r.Id + " " + r.Amount);
            }
        Assert.Equal(0, RunSystem.NiceAmount(0.3));
        Assert.Equal(7, RunSystem.NiceAmount(7.2));
        Assert.Equal(45, RunSystem.NiceAmount(46));
        Assert.Equal(270, RunSystem.NiceAmount(266));
        Assert.Equal(1250, RunSystem.NiceAmount(1262));
    }

    [Fact]
    public void AnEventChest_PaysItsBundle_IntoThePendingLoot()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        var level = C.RunLevel(run.LevelId)!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("treasure", 12).Concat(Enumerable.Repeat("empty", 30)).ToArray()); // any roll lands on a pod
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.Equal(RunPhase.Event, s.Run.Active!.Phase);
        Assert.True(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = 0 }).Ok);
        var bundle = RunSystem.RewardChestBundle(C, level);
        Assert.NotEmpty(bundle);
        foreach (var b in bundle) // two chests: twice the bundle (plus whatever the rolls drew)
            Assert.True(s.Run.Active!.PendingLoot.Where(r => r.Id == b.Id).Sum(r => r.Amount) >= 2 * b.Amount, b.Id);
    }
}
