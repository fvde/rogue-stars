using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>Shop SALE: now and then one skill offer costs SalePct less; the list price is kept for the struck-through display.</summary>
public class ShopSaleTests
{
    private static void Fill(ContentDb c, ActiveRun run) =>
        typeof(RunSystem).GetMethod("FillShop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { c, run, false });

    [Fact]
    public void SomeFills_HaveOneSkillOnSale_AtHalfPrice()
    {
        var c = ContentDb.Default;
        var e = RunTestKit.Engine();
        int sales = 0, fills = 200;
        for (int seed = 1; seed <= fills; seed++)
        {
            var s = RunTestKit.StartRun(e, seed: seed);
            var run = s.Run.Active!;
            Fill(c, run);
            var onSale = run.Shop.FindAll(o => o.ListPrice > 0);
            Assert.True(onSale.Count <= 1);
            foreach (var o in onSale)
            {
                Assert.Equal(ShopOfferKind.Skill, o.Kind);
                Assert.Equal((long)Math.Round(o.ListPrice * 0.5), o.Price);
                sales++;
            }
        }
        Assert.InRange(sales, fills / 10, fills * 3 / 10); // ~20 %
    }

    [Fact]
    public void Sale_DoesNotTouchTheRunsRandomStream()
    {
        var c = ContentDb.Default;
        var noSale = new ContentDb();
        foreach (var t in noSale.RunShopTables.Values) t.SaleChancePct = 0;
        var e = RunTestKit.Engine();
        var a = RunTestKit.StartRun(e, seed: 7).Run.Active!;
        var b = RunTestKit.StartRun(e, seed: 7).Run.Active!;
        Fill(noSale, a);
        Fill(c, b);
        Assert.Equal(a.Rng.NextULong(), b.Rng.NextULong());
        Assert.Equal(a.Shop.ConvertAll(o => o.SkillId ?? o.Kind.ToString()), b.Shop.ConvertAll(o => o.SkillId ?? o.Kind.ToString()));
    }

    [Fact]
    public void Buying_ASaleOffer_ChargesTheSalePrice()
    {
        var c = ContentDb.Default;
        var e = RunTestKit.Engine();
        for (int seed = 1; seed <= 200; seed++)
        {
            var s = RunTestKit.StartRun(e, seed: seed);
            var run = s.Run.Active!;
            Fill(c, run);
            int i = run.Shop.FindIndex(o => o.ListPrice > 0);
            if (i < 0) continue;
            run.Phase = RunPhase.Shop;
            run.Coins = run.Shop[i].Price;
            Assert.True(RunTestKit.Exec(e, ref s, new RunShopBuy { Index = i }).Ok);
            Assert.Equal(0, s.Run.Active!.Coins);
            return;
        }
        Assert.Fail("no sale in 200 fills");
    }
}
