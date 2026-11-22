using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using Xunit.Abstractions;
using static RogueStars.Rules.Tests.Progression.ProgressionKit;

namespace RogueStars.Rules.Tests.Progression;

/// <summary>
/// Long-haul sanity check of the pull economy using the content F2P income model
/// (ContentDb.EconomyConfig.F2p, documented in docs/design/economy-model.md).
/// </summary>
public class EconomyLongHaulTests
{
    private readonly ITestOutputHelper _out;
    public EconomyLongHaulTests(ITestOutputHelper output) { _out = output; }

    private const string FocusHero = "hero_star_knight";

    private sealed class SimResult
    {
        public double[] MythicsPerWeek = new double[3];
        public double[] PullsPerWeek = new double[3];
        public long PityTriggers;
        public double FocusShardsPerWeek;
    }

    private static SimResult Simulate(int weeks, double diamondMultiplier, long seed, bool focusSpender = false)
    {
        var m = C.EconomyConfig.F2p;
        var e = Engine(cloner: false);
        var s = Player(e, seed);
        var types = new[] { GachaType.Blueprint, GachaType.Part, GachaType.Alien };
        int nextDiamondType = 0;
        long diamondBudget = 0;
        for (int day = 0; day < weeks * 7; day++)
        {
            long now = TestHelpers.T0 + day * Day;
            foreach (var t in types) s.Wallet.Add(C.GachaConfig.Types[t].TokenCurrency, m.TokensPerTypePerDay);
            diamondBudget += (long)(m.DiamondsPerDay * m.DiamondShareForPulls * diamondMultiplier);
            s.Wallet.Add(Currencies.HeroShards(FocusHero), m.FocusHeroShardsPerDay);

            foreach (var t in types)
            {
                var def = C.GachaConfig.Types[t];
                string banner = GachaSystem.ActiveBanner(C, t, now)!.Id;
                while (s.Wallet.Get(def.TokenCurrency) >= def.TokenCost10)
                    Assert.True(e.Execute(ref s, new GachaPull { PullType = t, Count = 10, Payment = GachaPayment.Tokens, BannerId = banner }, now).Ok);
            }
            // A focused spender saves Diamonds for the Alien banner that features their hero.
            bool spendNow = !focusSpender || GachaSystem.ActiveBanner(C, GachaType.Alien, now)!.Theme.FeaturedId == FocusHero;
            while (spendNow && diamondBudget >= C.GachaConfig.Types[types[nextDiamondType]].DiamondCost10)
            {
                if (focusSpender) nextDiamondType = 2; // Alien
                var t = types[nextDiamondType];
                diamondBudget -= C.GachaConfig.Types[t].DiamondCost10;
                s.Wallet.Add(Currencies.Diamonds, C.GachaConfig.Types[t].DiamondCost10);
                string banner = GachaSystem.ActiveBanner(C, t, now)!.Id;
                Assert.True(e.Execute(ref s, new GachaPull { PullType = t, Count = 10, Payment = GachaPayment.Diamonds, BannerId = banner }, now).Ok);
                nextDiamondType = (nextDiamondType + 1) % types.Length;
            }
        }
        var r = new SimResult();
        for (int i = 0; i < 3; i++)
        {
            var p = s.Gacha.For(types[i]);
            r.MythicsPerWeek[i] = p.Mythics / (double)weeks;
            r.PullsPerWeek[i] = p.TotalPulls / (double)weeks;
            r.PityTriggers += p.MythicPityTriggers;
        }
        r.FocusShardsPerWeek = s.Wallet.Get(Currencies.HeroShards(FocusHero)) / (double)weeks;
        return r;
    }

    [Fact]
    public void ActiveF2P_HitsMythicAboutWeekly_AndHero10StarTakesMonths()
    {
        const int weeks = 52;
        var f2p = Simulate(weeks, 1, 2027);
        for (int i = 0; i < 3; i++)
            _out.WriteLine($"type {(GachaType)i}: {f2p.PullsPerWeek[i]:F1} pulls/week, {f2p.MythicsPerWeek[i]:F2} mythics/week");
        long needed = HeroSystem.TotalShardsToStar(C, 10);
        double weeksTo10 = needed / f2p.FocusShardsPerWeek;
        _out.WriteLine($"focus hero: {f2p.FocusShardsPerWeek:F1} shards/week, {needed} shards to 10*, ~{weeksTo10:F0} weeks");

        for (int i = 0; i < 3; i++) Assert.InRange(f2p.MythicsPerWeek[i], 0.75, 2.0);
        Assert.True(f2p.PityTriggers > weeks / 2, "hidden mythic pity should be a regular F2P experience");
        Assert.InRange(weeksTo10, 39, 156); // 9 months .. 3 years for F2P

        // Paying speeds you up: a spender with 30x the Diamond income, focused on their hero banner, gets there several times faster.
        var whale = Simulate(26, 30, 2027, focusSpender: true);
        double whaleWeeks = needed / whale.FocusShardsPerWeek;
        _out.WriteLine($"whale: ~{whaleWeeks:F0} weeks to 10*");
        Assert.True(whaleWeeks < weeksTo10 / 2);
    }
}
