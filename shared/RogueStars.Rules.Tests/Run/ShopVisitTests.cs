using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>Owner playtest: the shop's reroll price is per visit, and stat perks apply the moment they are bought.</summary>
public class ShopVisitTests
{
    [Fact]
    public void RerollCost_ResetsToTheBasePrice_OnEveryNewShopVisit()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, new[] { "shop", "shop" }.Concat(Enumerable.Repeat("empty", 20)).ToArray());
        run.Coins = 5000;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.Equal(RunPhase.Shop, s.Run.Active!.Phase);
        long basePrice = RunSystem.ShopRerollCost(e.Content, s.Run.Active);
        Assert.True(basePrice > 0);
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopReroll()).Ok);
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopReroll()).Ok);
        long raised = RunSystem.ShopRerollCost(e.Content, s.Run.Active!);
        Assert.True(raised > basePrice, "the price rises within one visit");
        long coins = s.Run.Active!.Coins;
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopReroll()).Ok);
        Assert.Equal(coins - raised, s.Run.Active!.Coins); // the handler charges what ShopRerollCost shows
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopLeave()).Ok);

        // the next shop tile: a new visit starts at the base price again
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.Equal(RunPhase.Shop, s.Run.Active!.Phase);
        Assert.Equal(2, s.Run.Active.ShopsVisited);
        Assert.Equal(0, s.Run.Active.ShopRerolls);
        Assert.Equal(basePrice, RunSystem.ShopRerollCost(e.Content, s.Run.Active));
        coins = s.Run.Active.Coins;
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopReroll()).Ok);
        Assert.Equal(coins - basePrice, s.Run.Active!.Coins);
    }

    [Fact]
    public void StatPerk_RaisesTheWormsStats_WhileTheShopIsStillOpen()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, new[] { "shop" }.Concat(Enumerable.Repeat("empty", 20)).ToArray());
        run.Coins = 5000;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        run = s.Run.Active!;
        Assert.Equal(RunPhase.Shop, run.Phase);
        run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Stat, Stat = new StatMod(StatId.Atk, 0, 8), Value = 8, Price = 10 });
        double atk = RunSystem.WormStats(e.Content, run).Atk;
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopBuy { Index = run.Shop.Count - 1 }).Ok);
        Assert.Equal(RunPhase.Shop, s.Run.Active!.Phase);
        Assert.True(RunSystem.WormStats(e.Content, s.Run.Active).Atk > atk);
    }
}
