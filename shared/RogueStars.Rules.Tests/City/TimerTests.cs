using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.City.Kit;

namespace RogueStars.Rules.Tests.City;

public class TimerTests
{
    private const long T0 = TestHelpers.T0;

    [Fact]
    public void DiamondCost_IsCeilOfLinearRate_MinOne()
    {
        var c = TestHelpers.Engine().Content;
        Assert.Equal(0.2, c.TimerTuning.DiamondsPerMinute);
        Assert.Equal(0, TimerSystem.DiamondCost(c, 0));
        Assert.Equal(1, TimerSystem.DiamondCost(c, 1_000));
        Assert.Equal(12, TimerSystem.DiamondCost(c, 60 * Min));
        Assert.Equal(13, TimerSystem.DiamondCost(c, 61 * Min));
        Assert.Equal(288, TimerSystem.DiamondCost(c, 24 * Hour));
    }

    private static (GameEngine, GameState, GameTimer) DiamondMineUnderConstruction()
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        GrantBlueprint(e, s, "bp_diamond_mine");
        Ok(e, ref s, new CityBuild { BlueprintId = "bp_diamond_mine", X = 4, Y = 4 }, T0);
        var t = TimerSystem.Get(s, CitySystem.Owned(s, "bp_diamond_mine").TimerId);
        Assert.Equal(Hour, t.DurationMs);
        return (e, s, t);
    }

    [Fact]
    public void Api_StartFindIsDoneRemove()
    {
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = T0 };
        var t = TimerSystem.Start(ctx, "lab_research", "skill_x", 10 * Min);
        Assert.Same(t, TimerSystem.Find(s, "lab_research", "skill_x"));
        Assert.False(TimerSystem.IsDone(s, t.Id, T0 + 9 * Min));
        Assert.True(TimerSystem.IsDone(s, t.Id, T0 + 10 * Min));
        e.Settle(s, T0 + 20 * Min); // no completion handler for this kind: stays until the owner consumes it
        Assert.NotNull(TimerSystem.Get(s, t.Id));
        Assert.True(TimerSystem.Remove(s, t.Id));
        Assert.Null(TimerSystem.Find(s, "lab_research", "skill_x"));
    }

    [Fact]
    public void Diamonds_CompleteTimer_AndCharge()
    {
        var (e, s, t) = DiamondMineUnderConstruction();
        long before = s.Wallet.Get(Currencies.Diamonds);
        Ok(e, ref s, new TimerSpeedUp { TimerId = t.Id, Method = SpeedUpMethod.Diamonds }, T0 + 30 * Min);
        Assert.Equal(before - 6, s.Wallet.Get(Currencies.Diamonds));
        var b = CitySystem.Owned(s, "bp_diamond_mine");
        Assert.Equal(1, b.Level);
        Assert.Null(b.TimerId);
        Assert.Empty(s.Timers.Active);
        Assert.Equal(1, s.Counter("timer_speedups_diamond"));
    }

    [Fact]
    public void Diamonds_NotEnough_LeavesStateUntouched()
    {
        var (e, s, t) = DiamondMineUnderConstruction();
        s.Wallet.TrySpend(Currencies.Diamonds, s.Wallet.Get(Currencies.Diamonds) - 3);
        Assert.Equal("not enough diamonds", Fail(e, ref s, new TimerSpeedUp { TimerId = t.Id, Method = SpeedUpMethod.Diamonds }, T0));
        Assert.Equal(3, s.Wallet.Get(Currencies.Diamonds));
        Assert.Equal(0, CitySystem.Owned(s, "bp_diamond_mine").Level);
    }

    [Fact]
    public void Ad_Removes15PercentOfRemaining_MaxThree()
    {
        var (e, s, t) = DiamondMineUnderConstruction();
        long remaining = Hour;
        for (int i = 0; i < 3; i++)
        {
            Ok(e, ref s, new TimerSpeedUp { TimerId = t.Id, Method = SpeedUpMethod.Ad, AdToken = "ad" }, T0);
            remaining -= (long)Math.Ceiling(remaining * 0.15);
            Assert.Equal(T0 + remaining, TimerSystem.Get(s, t.Id).EndsMs);
        }
        Assert.Equal(T0 + 2_210_850, TimerSystem.Get(s, t.Id).EndsMs); // 3600000 × 0.85³ (rounded per step)
        Assert.Equal(0, TimerSystem.AdsLeft(TimerSystem.Get(s, t.Id), e.Content));
        Assert.Equal("no ad speed-ups left for this timer", Fail(e, ref s, new TimerSpeedUp { TimerId = t.Id, Method = SpeedUpMethod.Ad, AdToken = "ad" }, T0));
    }

    [Fact]
    public void NoAdsFlag_MakesAdSpeedUpInstant_SameRuleEffect()
    {
        var (e, s, t) = DiamondMineUnderConstruction();
        Assert.True(TimerSystem.AdRequired(s, e.Content));
        s.Flags.Add("ent_no_ads");
        Assert.False(TimerSystem.AdRequired(s, e.Content));
        Ok(e, ref s, new TimerSpeedUp { TimerId = t.Id, Method = SpeedUpMethod.Ad, AdToken = "ad" }, T0);
        Assert.Equal(T0 + 3_060_000, TimerSystem.Get(s, t.Id).EndsMs);
    }

    [Fact]
    public void Ad_ThatFinishesTimer_CompletesConstruction()
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        GrantBlueprint(e, s, "bp_biomass_farm");
        Ok(e, ref s, new CityBuild { BlueprintId = "bp_biomass_farm", X = 4, Y = 4 }, T0);
        string id = CitySystem.Owned(s, "bp_biomass_farm").TimerId;
        // 1 minute left after 50 s: 15% of 10 s = 1.5 s -> ends at T0+58.5 s; at T0+59 s it is due.
        Ok(e, ref s, new TimerSpeedUp { TimerId = id, Method = SpeedUpMethod.Ad, AdToken = "ad" }, T0 + 50_000);
        Assert.Equal(0, CitySystem.Owned(s, "bp_biomass_farm").Level);
        e.Settle(s, T0 + 58_500);
        Assert.Equal(1, CitySystem.Owned(s, "bp_biomass_farm").Level);
        Assert.Equal("unknown timer", Fail(e, ref s, new TimerSpeedUp { TimerId = id, Method = SpeedUpMethod.Ad, AdToken = "ad" }, T0 + 59_000));
    }

    [Fact]
    public void Free_OncePerDay()
    {
        var (e, s, t) = DiamondMineUnderConstruction();
        Assert.Equal(1, TimerSystem.FreeSpeedUpsLeft(s, e.Content, T0));
        Ok(e, ref s, new TimerSpeedUp { TimerId = t.Id, Method = SpeedUpMethod.Free }, T0);
        Assert.Equal(T0 + 30 * Min, TimerSystem.Get(s, t.Id).EndsMs);
        Assert.Equal("no free speed-up left today", Fail(e, ref s, new TimerSpeedUp { TimerId = t.Id, Method = SpeedUpMethod.Free }, T0 + Min));
        // Next UTC day: the daily free speed-up is available again (on a new timer).
        GrantBlueprint(e, s, "bp_plate_foundry");
        long nextDay = (T0 / TimerSystem.DayMs + 1) * TimerSystem.DayMs;
        Ok(e, ref s, new CityBuild { BlueprintId = "bp_plate_foundry", X = 6, Y = 4 }, nextDay);
        string id2 = CitySystem.Owned(s, "bp_plate_foundry").TimerId;
        Ok(e, ref s, new TimerSpeedUp { TimerId = id2, Method = SpeedUpMethod.Free }, nextDay);
    }
}
