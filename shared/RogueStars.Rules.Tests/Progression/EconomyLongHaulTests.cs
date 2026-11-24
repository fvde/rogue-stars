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
    }

    private static SimResult Simulate(int weeks, double diamondMultiplier, long seed)
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

            foreach (var t in types)
            {
                var def = C.GachaConfig.Types[t];
                string banner = GachaSystem.ActiveBanner(C, t, now)!.Id;
                while (s.Wallet.Get(def.TokenCurrency) >= def.TokenCost10)
                    Assert.True(e.Execute(ref s, new GachaPull { PullType = t, Count = 10, Payment = GachaPayment.Tokens, BannerId = banner }, now).Ok);
            }
            while (diamondBudget >= C.GachaConfig.Types[types[nextDiamondType]].DiamondCost10)
            {
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
        return r;
    }

    /// <summary>
    /// Weeks until the focus hero reaches 10★ through hero summon events: every event pays <c>HeroEventPulls</c> tickets
    /// (F2P model), all pulled; pull-count milestones claimed; exchange tokens saved for the focus hero's events (its copy,
    /// then its shards); shards spent on unlock / stars. A spender adds Diamond 10-pulls in the focus hero's events.
    /// </summary>
    private static double WeeksToFocus10(long seed, long diamondsPerDay, int maxWeeks = 260)
    {
        var m = C.EconomyConfig.F2p;
        var e = Engine(cloner: false);
        var s = Player(e, seed);
        var def = C.SummonType(SummonEventType.Hero)!;
        long budget = 0;
        for (long now = TestHelpers.T0; now < TestHelpers.T0 + maxWeeks * 7 * Day;)
        {
            e.Settle(s, now);
            var ev = SummonEventSystem.CurrentEvent(s, C, SummonEventType.Hero, now)!;
            bool focus = ev.FeaturedId == FocusHero;
            budget += diamondsPerDay * (ev.EndMs - Math.Max(now, ev.StartMs)) / Day;
            s.Wallet.Add(def.TicketCurrency, m.HeroEventPulls);
            void Exec(GameCommand c) => Assert.True(e.Execute(ref s, c, now).Ok, c.Type);
            while (s.Wallet.Get(def.TicketCurrency) >= 10) Exec(new SummonPull { EventType = SummonEventType.Hero, Count = 10 });
            while (s.Wallet.Get(def.TicketCurrency) >= 1) Exec(new SummonPull { EventType = SummonEventType.Hero, Count = 1 });
            if (focus)
                while (budget >= def.DiamondCost10)
                {
                    budget -= def.DiamondCost10;
                    s.Wallet.Add(Currencies.Diamonds, def.DiamondCost10);
                    Exec(new SummonPull { EventType = SummonEventType.Hero, Count = 10, Payment = SummonPayment.Diamonds });
                }
            foreach (var ms in def.Milestones)
                if (SummonEventSystem.MilestoneClaimable(s.Summons.Events[SummonEventType.Hero], ms)) Exec(new SummonMilestoneClaim { EventType = SummonEventType.Hero, Pulls = ms.Pulls });
            if (focus)
                foreach (var x in def.Exchange.Where(x => x.Reward.Kind == RewardKinds.Hero || x.Reward.Kind == RewardKinds.HeroShards))
                    while (e.Execute(ref s, new SummonExchangeBuy { EventType = SummonEventType.Hero, ItemId = x.Id }, now).Ok) { }
            if (HeroSystem.Get(s, FocusHero) == null && s.Wallet.Get(Currencies.HeroShards(FocusHero)) >= C.HeroConfig.StarShardCosts[1]) Exec(new HeroUnlock { HeroId = FocusHero });
            while (HeroSystem.Get(s, FocusHero) is { } h && h.Stars < 10 && s.Wallet.Get(Currencies.HeroShards(FocusHero)) >= C.HeroConfig.StarShardCosts[h.Stars + 1])
                Exec(new HeroStarUp { HeroId = FocusHero });
            if (HeroSystem.Get(s, FocusHero)?.Stars >= 10) return (ev.EndMs - TestHelpers.T0) / (7.0 * Day);
            now = ev.EndMs;
        }
        return double.PositiveInfinity;
    }

    [Fact]
    public void ActiveF2P_HitsMythicAboutWeekly_AndHero10StarTakesMonths()
    {
        const int weeks = 52;
        var f2p = Simulate(weeks, 1, 2027);
        for (int i = 0; i < 3; i++)
            _out.WriteLine($"type {(GachaType)i}: {f2p.PullsPerWeek[i]:F1} pulls/week, {f2p.MythicsPerWeek[i]:F2} mythics/week");
        for (int i = 0; i < 3; i++) Assert.InRange(f2p.MythicsPerWeek[i], 0.75, 2.0);
        Assert.True(f2p.PityTriggers > weeks / 2, "hidden mythic pity should be a regular F2P experience");

        // Heroes come from summon events (docs/design/summon-events.md): 10★ of one hero takes many months for F2P.
        var f2pWeeks = Enumerable.Range(1, 3).Select(seed => WeeksToFocus10(seed, 0)).OrderBy(w => w).ToList();
        double weeksTo10 = f2pWeeks[1];
        _out.WriteLine($"focus hero 10* via summon events, F2P: {string.Join(" / ", f2pWeeks.Select(w => w.ToString("F0")))} weeks");
        Assert.InRange(weeksTo10, 39, 156); // 9 months .. 3 years for F2P

        // Paying speeds you up: 30x the F2P pull Diamonds, spent in the focus hero's events, gets there several times faster.
        long whaleDiamonds = (long)(C.EconomyConfig.F2p.DiamondsPerDay * C.EconomyConfig.F2p.DiamondShareForPulls * 30);
        double whaleWeeks = Enumerable.Range(1, 3).Select(seed => WeeksToFocus10(seed, whaleDiamonds)).OrderBy(w => w).ElementAt(1);
        _out.WriteLine($"whale: ~{whaleWeeks:F0} weeks to 10*");
        Assert.True(whaleWeeks < weeksTo10 / 2);
    }
}
