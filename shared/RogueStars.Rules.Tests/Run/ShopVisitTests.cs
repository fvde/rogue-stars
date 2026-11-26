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
    public void Shop_OffersThreeSkills_PlusTheSupportOffers_AndEveryRerollKeepsThem()
    {
        // playtest 0.1.325: 3 skills, a stat perk on top, and the heal as well while the worm is below 60% HP
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, new[] { "shop", "shop" }.Concat(Enumerable.Repeat("empty", 20)).ToArray());
        run.Coins = 5000;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.Equal(RunPhase.Shop, s.Run.Active!.Phase);
        void FullHpFill(ActiveRun r)
        {
            Assert.Equal(4, r.Shop.Count);
            Assert.Equal(3, r.Shop.Count(o => o.Kind == ShopOfferKind.Skill));
            Assert.Equal(3, r.Shop.Where(o => o.Kind == ShopOfferKind.Skill).Select(o => o.SkillId).Distinct().Count());
            Assert.Equal(ShopOfferKind.Stat, r.Shop[3].Kind); // full HP: a stat perk, no heal
        }
        FullHpFill(s.Run.Active);
        for (int i = 0; i < 4; i++)
        {
            var perk = s.Run.Active!.Shop[3].Stat.Stat;
            Assert.True(RunTestKit.Exec(e, ref s, new RunShopReroll()).Ok);
            FullHpFill(s.Run.Active!); // a reroll refreshes the skills and keeps the support offers
            Assert.Equal(perk, s.Run.Active!.Shop[3].Stat.Stat);
        }
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopLeave()).Ok);

        // hurt: the heal comes on top of the stat perk (and stays after a reroll)
        s.Run.Active!.WormHp = s.Run.Active.WormMaxHp * 0.3;
        Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok);
        Assert.Equal(RunPhase.Shop, s.Run.Active!.Phase);
        void HurtFill(ActiveRun r)
        {
            Assert.Equal(5, r.Shop.Count);
            Assert.Equal(3, r.Shop.Count(o => o.Kind == ShopOfferKind.Skill));
            Assert.Equal(ShopOfferKind.Heal, r.Shop[3].Kind);
            Assert.Equal(ShopOfferKind.Stat, r.Shop[4].Kind);
        }
        HurtFill(s.Run.Active);
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopBuy { Index = 3 }).Ok); // the heal
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopReroll()).Ok);
        HurtFill(s.Run.Active!);
        Assert.True(s.Run.Active!.Shop[3].Sold); // a bought heal stays bought: rerolls refresh the skills only
    }
}
