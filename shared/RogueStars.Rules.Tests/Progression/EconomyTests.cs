using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Progression.ProgressionKit;

namespace RogueStars.Rules.Tests.Progression;

public class EconomyTests
{
    private const long Tick = 6 * 60_000;

    [Fact]
    public void NewPlayer_GetsContentDefinedStartingWallet()
    {
        var e = Engine(); var s = Player(e);
        foreach (var kv in C.EconomyConfig.StartingWallet) Assert.Equal(kv.Value, s.Wallet.Get(kv.Key));
    }

    [Fact]
    public void Settler_RegeneratesStaminaFromTimestamps_OnEveryCommand()
    {
        var e = Engine(); var s = Player(e);
        SetBalance(s, Currencies.Stamina, 0);
        e.Settle(s, TestHelpers.T0); // anchor
        e.Settle(s, TestHelpers.T0 + 5 * Tick + 1000);
        Assert.Equal(5, s.Wallet.Get(Currencies.Stamina));
        // Any command also settles first.
        Ok(e, ref s, new HeroEquip { HeroId = "" }, TestHelpers.T0 + 10 * Tick + 1000);
        Assert.Equal(10, s.Wallet.Get(Currencies.Stamina));
    }

    [Fact]
    public void RegenBonusProviders_AddCapAndTickSpeed()
    {
        var e = Engine();
        EconomySystem.RegisterRegenBonusProvider(e, (st, c, now, cur) => cur == Currencies.Stamina ? (20L, 50.0) : (0L, 0.0));
        var s = Player(e);
        Assert.Equal(80, EconomySystem.RegenCap(s, C, Currencies.Stamina));
        SetBalance(s, Currencies.Stamina, 0);
        e.Settle(s, TestHelpers.T0);
        e.Settle(s, TestHelpers.T0 + 4 * Tick); // ticks are half as long -> 8
        Assert.Equal(8, s.Wallet.Get(Currencies.Stamina));
        e.Settle(s, TestHelpers.T0 + 1000 * Tick);
        Assert.Equal(80, s.Wallet.Get(Currencies.Stamina));
    }

    [Fact]
    public void RegenModifier_SpeedsRegen_AndExpiredModifiersAreRemoved()
    {
        var e = Engine(); var s = Player(e);
        s.Modifiers.Add(new Modifier { Id = "m1", Target = "stamina_regen", Multiplier = 2, StartMs = 0, EndMs = TestHelpers.T0 + 10 * Tick });
        SetBalance(s, Currencies.Stamina, 0);
        e.Settle(s, TestHelpers.T0);
        e.Settle(s, TestHelpers.T0 + 3 * Tick);
        Assert.Equal(6, s.Wallet.Get(Currencies.Stamina));
        e.Settle(s, TestHelpers.T0 + 11 * Tick);
        Assert.DoesNotContain(s.Modifiers, m => m.Id == "m1"); // expired modifiers are removed (LiveOps settler)
    }

    [Fact]
    public void DiamondRefills_EscalateAndHitDailyLimit_ThenReset()
    {
        var e = Engine(); var s = Player(e);
        SetBalance(s, Currencies.Diamonds, 100_000);
        var prices = C.EconomyConfig.StaminaRefill.DiamondPrices;
        long before = s.Wallet.Get(Currencies.Diamonds);
        for (int i = 0; i < prices.Count; i++)
        {
            var info = EconomySystem.GetStaminaRefillInfo(s, C, TestHelpers.T0);
            Assert.Equal(prices[i], info.NextDiamondPrice);
            var r = Ok(e, ref s, new BuyStamina { Method = StaminaRefillMethod.Diamonds });
            Assert.Equal(prices[i], ((StaminaRefillResult)r.Payload).DiamondsSpent);
            before -= prices[i];
            Assert.Equal(before, s.Wallet.Get(Currencies.Diamonds));
        }
        Assert.Contains("limit", Fail(e, ref s, new BuyStamina { Method = StaminaRefillMethod.Diamonds }));
        Assert.Equal(-1, EconomySystem.GetStaminaRefillInfo(s, C, TestHelpers.T0).NextDiamondPrice);
        // Next day: first price again.
        var next = Ok(e, ref s, new BuyStamina { Method = StaminaRefillMethod.Diamonds }, TestHelpers.T0 + Day);
        Assert.Equal(prices[0], ((StaminaRefillResult)next.Payload).DiamondsSpent);
    }

    [Fact]
    public void DiamondRefill_FailsWithoutDiamonds()
    {
        var e = Engine(); var s = Player(e);
        SetBalance(s, Currencies.Diamonds, 10);
        long stamina = s.Wallet.Get(Currencies.Stamina);
        Fail(e, ref s, new BuyStamina { Method = StaminaRefillMethod.Diamonds });
        Assert.Equal(stamina, s.Wallet.Get(Currencies.Stamina));
    }

    [Fact]
    public void AdRefills_NeedAnAd_AreCappedPerDay_AndNoAdsMakesThemInstant()
    {
        var e = Engine(); var s = Player(e);
        Assert.Contains("ad", Fail(e, ref s, new BuyStamina { Method = StaminaRefillMethod.Ad }));
        long st = s.Wallet.Get(Currencies.Stamina);
        int cap = C.AdPlacement(C.EconomyConfig.StaminaRefill.AdPlacementId)!.DailyCap; // Meta's ad cap
        for (int i = 0; i < cap; i++) Ok(e, ref s, new BuyStamina { Method = StaminaRefillMethod.Ad, AdToken = "ad" + i });
        Assert.Equal(st + cap * C.EconomyConfig.StaminaRefill.Amount, s.Wallet.Get(Currencies.Stamina));
        Assert.Equal(cap, s.Counter("ads_watched"));
        Assert.Equal("ad daily cap reached", Fail(e, ref s, new BuyStamina { Method = StaminaRefillMethod.Ad, AdToken = "more" }));

        s.Flags.Add(ProgressionFlags.NoAds);
        Ok(e, ref s, new BuyStamina { Method = StaminaRefillMethod.Ad }, TestHelpers.T0 + Day); // no ad needed
        Assert.True(EconomySystem.GetStaminaRefillInfo(s, C, TestHelpers.T0 + Day).AdsInstant);
    }

    [Fact]
    public void DailyFreeRefill_OncePerDay()
    {
        var e = Engine(); var s = Player(e);
        Ok(e, ref s, new BuyStamina { Method = StaminaRefillMethod.DailyFree });
        Fail(e, ref s, new BuyStamina { Method = StaminaRefillMethod.DailyFree });
        Ok(e, ref s, new BuyStamina { Method = StaminaRefillMethod.DailyFree }, TestHelpers.T0 + Day);
        Assert.Equal(2, s.Counter("stamina_refills"));
    }
}

public class StaminaAtZeroTests
{
    private const long Tick = 6 * 60_000;

    [Fact]
    public void StaminaAtZero_RegeneratesOnePerInterval()
    {
        var e = ProgressionKit.Engine(); var s = TestHelpers.NewLockedPlayer(e);
        long t = TestHelpers.T0 + 3_600_000;
        e.Settle(s, t);
        Assert.True(s.Wallet.TrySpend(Currencies.Stamina, s.Wallet.Get(Currencies.Stamina)));
        Assert.Equal(0, s.Wallet.Get(Currencies.Stamina));
        e.Settle(s, t + Tick - 1000);
        Assert.Equal(0, s.Wallet.Get(Currencies.Stamina)); // not before the interval
        e.Settle(s, t + Tick + 1000);
        Assert.Equal(1, s.Wallet.Get(Currencies.Stamina));
        e.Settle(s, t + 3 * Tick + 1000);
        Assert.Equal(3, s.Wallet.Get(Currencies.Stamina));
    }

    [Fact]
    public void StaminaAtZero_WithALastTickAheadOfTheClock_DoesNotFreeze()
    {
        // A save or jump made on a later clock (or a clock moved back) left the last tick in the future: regen used to wait
        // for the clock to catch up (elapsed < 0), so the counter sat at 0.
        var e = ProgressionKit.Engine(); var s = TestHelpers.NewLockedPlayer(e);
        long t = TestHelpers.T0;
        e.Settle(s, t);
        s.Wallet.Balances[Currencies.Stamina] = 0;
        s.Wallet.Regen[Currencies.Stamina].LastTickMs = t + 5 * 3_600_000;
        e.Settle(s, t + 1); // a plain settle leaves it (restarting at whatever moment one settles is not path-independent)
        Assert.Equal(t + 5 * 3_600_000, s.Wallet.Regen[Currencies.Stamina].LastTickMs);
        // the clock is behind when the player acts: the tick restarts from the command's time
        Assert.True(e.Execute(ref s, new LobbyPopupShown { PopupId = "x" }, t).Ok);
        Assert.Equal(t, s.Wallet.Regen[Currencies.Stamina].LastTickMs);
        e.Settle(s, t + 2 * Tick + 1000);
        Assert.Equal(2, s.Wallet.Get(Currencies.Stamina));
    }
}
