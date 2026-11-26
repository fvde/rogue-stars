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

    [Fact]
    public void Shop_OffersThreeOptionsAtATime_AndEveryRerollKeepsThree()
    {
        // playtest 0.1.308: two skills and one support offer (the heal while hurt, else a stat perk)
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, new[] { "shop", "shop" }.Concat(Enumerable.Repeat("empty", 20)).ToArray());
        run.Coins = 5000;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.Equal(RunPhase.Shop, s.Run.Active!.Phase);
        Assert.Equal(3, s.Run.Active.Shop.Count);
        Assert.Equal(2, s.Run.Active.Shop.Count(o => o.Kind == ShopOfferKind.Skill));
        Assert.Equal(ShopOfferKind.Stat, s.Run.Active.Shop[2].Kind); // full HP: a stat perk, no heal
        for (int i = 0; i < 4; i++)
        {
            Assert.True(RunTestKit.Exec(e, ref s, new RunShopReroll()).Ok);
            Assert.Equal(3, s.Run.Active!.Shop.Count);
        }
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopLeave()).Ok);

        // hurt: the support slot is the heal
        s.Run.Active!.WormHp = s.Run.Active.WormMaxHp * 0.3;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.Equal(RunPhase.Shop, s.Run.Active!.Phase);
        Assert.Equal(3, s.Run.Active.Shop.Count);
        Assert.Equal(ShopOfferKind.Heal, s.Run.Active.Shop[2].Kind);
    }
}
