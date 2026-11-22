using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Meta.MetaTestKit;

namespace RogueStars.Rules.Tests.Meta;

public class LiveOpsTests
{
    private static long Epoch => ContentDb.Default.LiveOps.EpochMs;
    // diamond_rain occurrence (Tuesday, 4 days) and grain_blight one week later.
    private static long DiamondRainStart => Epoch + 7 * 56 * D + 1 * D;
    private static long GloopStart => Epoch + 14 * 28 * D + 5 * D;

    [Fact]
    public void Content_HasFourInvasionsEightEvents_AndOutcomeBoundsHold()
    {
        var c = ContentDb.Default;
        Assert.Equal(4, c.Invasions.Count);
        Assert.Equal(8, c.PlanetaryEvents.Count);
        Assert.Empty(LiveOpsSystem.Validate(c));
        foreach (var inv in c.Invasions.Values)
        {
            Assert.All(inv.WinModifiers, m => Assert.InRange(m.Multiplier, 1.5, 3.0));
            Assert.All(inv.LoseModifiers, m => Assert.InRange(m.Multiplier, 0.25 - 1e-9, 1 / 1.5 + 1e-9));
            Assert.All(inv.WinModifiers.Concat(inv.LoseModifiers), m => Assert.InRange(m.DurationMs, 2 * D, 7 * D));
        }
        Assert.Contains(c.PlanetaryEvents.Values, pe => pe.SideEffect != null && pe.Modifiers.Any(m => m.Multiplier < 1) && pe.Modifiers.Any(m => m.Multiplier > 1));
    }

    [Fact]
    public void Validate_RejectsOutOfBoundsOutcome()
    {
        var c = new ContentDb();
        c.Invasions["gloop_tide"].WinModifiers[0].Multiplier = 5;
        c.Invasions["rust_swarm"].LoseModifiers[0].DurationMs = 10 * D;
        Assert.Equal(2, LiveOpsSystem.Validate(c).Count);
    }

    [Fact]
    public void InvasionOutcome_Repelled_AppliesWinModifiersForDuration_AndRankRewards()
    {
        var e = Engine(); var s = Player(e);
        long t = TestHelpers.T0;
        long dia = Bal(s, Currencies.Diamonds);
        Ok(e, ref s, new LiveOpsApplyOutcome { InvasionId = "void_choir", OccurrenceId = "occ1", Repelled = true, ContributionRank = 1 }, t);
        var mod = s.Modifiers.Single(m => m.Source == "invasion:void_choir");
        Assert.Equal("production:diamonds", mod.Target);
        Assert.Equal(2.0, mod.Multiplier);
        Assert.Equal(t, mod.StartMs);
        Assert.Equal(t + 7 * D, mod.EndMs);
        Assert.Equal(2.0, ModifierMath.Multiplier(s.Modifiers, "production:diamonds", t + 6 * D), 6);
        // Champion tier (100) + repelled bonus (30)
        Assert.Equal(dia + 130, Bal(s, Currencies.Diamonds));
        // idempotent per occurrence
        Fail(e, ref s, new LiveOpsApplyOutcome { InvasionId = "void_choir", OccurrenceId = "occ1", Repelled = true, ContributionRank = 1 }, t);
        // expires after 7 days
        e.Settle(s, t + 7 * D);
        Assert.DoesNotContain(s.Modifiers, m => m.Source == "invasion:void_choir");
    }

    [Fact]
    public void InvasionOutcome_Overrun_AppliesCut_ParticipantRewardsOnly()
    {
        var e = Engine(); var s = Player(e);
        long t = TestHelpers.T0;
        long dia = Bal(s, Currencies.Diamonds);
        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = t };
        LiveOpsSystem.ApplyInvasionOutcome(ctx, "prism_horde", false, 50);
        var mod = s.Modifiers.Single(m => m.Source == "invasion:prism_horde");
        Assert.Equal(0.25, mod.Multiplier);
        Assert.Equal(t + 2 * D, mod.EndMs);
        Assert.Equal(dia + 10, Bal(s, Currencies.Diamonds)); // participant tier, no repelled bonus
        // rank 0 = no participation → modifiers but no rewards
        var s2 = Player(e);
        long dia2 = Bal(s2, Currencies.Diamonds);
        var ctx2 = new CommandContext { State = s2, Content = e.Content, Engine = e, NowMs = t };
        LiveOpsSystem.ApplyInvasionOutcome(ctx2, "gloop_tide", true, 0);
        Assert.Equal(dia2, Bal(s2, Currencies.Diamonds));
        Assert.Single(s2.Modifiers, m => m.Source == "invasion:gloop_tide");
    }

    [Fact]
    public void PlanetaryEvent_SettlerAppliesForWindow_AndExpires()
    {
        var e = Engine(); var s = Player(e);
        e.Settle(s, DiamondRainStart - 1);
        Assert.DoesNotContain(s.Modifiers, m => m.Source == "event:diamond_rain");
        var r = Ok(e, ref s, new LobbyPopupShown { PopupId = "x" }, DiamondRainStart + H);
        Assert.Contains(r.Events, ev => ev.Type == "planetary_event" && ev.Args[0] == "diamond_rain");
        var mod = s.Modifiers.Single(m => m.Source == "event:diamond_rain");
        Assert.Equal(DiamondRainStart, mod.StartMs);
        Assert.Equal(DiamondRainStart + 4 * D, mod.EndMs);
        Assert.Equal(2.0, ModifierMath.Multiplier(s.Modifiers, "production:diamonds", DiamondRainStart + H), 6);
        e.Settle(s, DiamondRainStart + 2 * D); // no duplicates
        Assert.Single(s.Modifiers, m => m.Source == "event:diamond_rain");
        e.Settle(s, DiamondRainStart + 4 * D);
        Assert.DoesNotContain(s.Modifiers, m => m.Source == "event:diamond_rain");

        // Grain blight next week: surprising side effect (grain ÷4, breeding ×2).
        long blight = DiamondRainStart + 7 * D + H;
        e.Settle(s, blight);
        Assert.Equal(0.25, ModifierMath.Multiplier(s.Modifiers, "production:grain", blight), 6);
        Assert.Equal(2.0, ModifierMath.Multiplier(s.Modifiers, "breeding_speed", blight), 6);
        var active = LiveOpsSystem.ActiveEvents(s, e.Content, blight);
        Assert.Contains(active, a => a.Kind == LiveOpsKind.PlanetaryEvent && a.RefId == "grain_blight");
    }

    /// <summary>Engine whose Diamond Mine never fills its storage (production integrals are compared exactly).</summary>
    private static (GameEngine, GameState, OwnedBlueprint) DiamondMineAt(long buildAt)
    {
        var c = new ContentDb();
        c.Blueprints["bp_diamond_mine"].BaseStorage = 1e12;
        var e = new GameEngine(c) { Cloner = TestHelpers.Clone };
        var s = e.NewPlayer("p1", buildAt, 42);
        s.Flags.Add(Features.DevUnlockAllFlag);
        RogueStars.Rules.Tests.City.Kit.UnlockAll(e, s);
        s.Wallet.Add(Currencies.Ore, 1_000_000); s.Wallet.Add(Currencies.Biomass, 1_000_000);
        long done = RogueStars.Rules.Tests.City.Kit.BuildActive(e, ref s, "bp_diamond_mine", 0, 0, buildAt);
        var mine = CitySystem.Owned(s, "bp_diamond_mine")!;
        mine.Stored = 0;
        mine.LastSettleMs = done;
        return (e, s, mine);
    }

    [Fact]
    public void PlanetaryEvent_StartingBetweenCommands_BoostsProductionFromItsRealStart()
    {
        // Last command 1 h before Diamond Rain starts, next one 2 h after: 1 h at ×1 + 2 h at ×2.
        var (e, s, mine) = DiamondMineAt(DiamondRainStart - 3 * H);
        long last = DiamondRainStart - H;
        e.Settle(s, last);
        double rate = CitySystem.BaseRatePerHour(s, e.Content, mine);
        double before = mine.Stored;
        Assert.DoesNotContain(s.Modifiers, m => m.Source == "event:diamond_rain");
        Ok(e, ref s, new LobbyPopupShown { PopupId = "x" }, DiamondRainStart + 2 * H);
        mine = CitySystem.Owned(s, "bp_diamond_mine")!;
        Assert.Equal(before + rate * (1 + 2 * 2), mine.Stored, 6);
    }

    [Fact]
    public void PlanetaryEvent_StartedAndEndedBetweenCommands_StillCountsForProduction()
    {
        // Away for the whole 4-day Diamond Rain window: the window is integrated, then the expired modifier is gone.
        var (e, s, mine) = DiamondMineAt(DiamondRainStart - 3 * H);
        long last = DiamondRainStart - H, next = DiamondRainStart + 4 * D + H;
        e.Settle(s, last);
        double rate = CitySystem.BaseRatePerHour(s, e.Content, mine);
        double before = mine.Stored;
        Ok(e, ref s, new LobbyPopupShown { PopupId = "x" }, next);
        mine = CitySystem.Owned(s, "bp_diamond_mine")!;
        Assert.Equal(before + rate * (1 + 4 * 24 * 2 + 1), mine.Stored, 6);
        Assert.DoesNotContain(s.Modifiers, m => m.Source == "event:diamond_rain");
        // A later settle does not resurrect the finished occurrence.
        e.Settle(s, next + H);
        Assert.DoesNotContain(s.Modifiers, m => m.Source == "event:diamond_rain");
    }

    [Fact]
    public void Schedule_OccurrenceMath()
    {
        var sch = new ScheduleDef { StartMs = 1000, DurationMs = 100, RecurrenceMs = 1000, EndMs = 3500 };
        Assert.False(sch.TryActive(999, out _, out _));
        Assert.True(sch.TryActive(2050, out long s1, out long e1)); Assert.Equal(2000, s1); Assert.Equal(2100, e1);
        Assert.False(sch.TryActive(2100, out _, out _));
        Assert.True(sch.TryNext(2100, out long n, out _)); Assert.Equal(3000, n);
        Assert.False(sch.TryNext(3000, out _, out _)); // 4000 is past EndMs
        Assert.False(sch.TryActive(4050, out _, out _));
    }

    [Fact]
    public void InvasionAttempts_DailyTopUp_KeepsExtras()
    {
        var e = Engine(); var s = Player(e);
        Assert.Equal(3, Bal(s, Currencies.InvasionAttempts));
        s.Wallet.TrySpend(Currencies.InvasionAttempts, 2);
        e.Settle(s, TestHelpers.T0 + H);
        Assert.Equal(1, Bal(s, Currencies.InvasionAttempts)); // same day: no refill
        e.Settle(s, Day0 + D);
        Assert.Equal(3, Bal(s, Currencies.InvasionAttempts));
        s.Wallet.Add(Currencies.InvasionAttempts, 5);
        e.Settle(s, Day0 + 2 * D);
        Assert.Equal(8, Bal(s, Currencies.InvasionAttempts));
    }

    [Fact]
    public void InvasionFlow_AttemptDamageScore_ServerOnlyCommands()
    {
        var e = Engine(); var s = Player(e);
        long t = GloopStart + H;
        var inv = LiveOpsSystem.ActiveEvents(s, e.Content, t).Single(a => a.Kind == LiveOpsKind.Invasion);
        Assert.Equal("gloop_tide", inv.RefId);
        Assert.Equal("feature locked", Fail(e, ref s, new InvasionStartAttempt { OccurrenceId = inv.OccurrenceId }, t));
        s.Counters[Requirements.MainLevelCounter] = 20;
        Fail(e, ref s, new InvasionRecordDamage { OccurrenceId = inv.OccurrenceId, Damage = 100 }, t);
        Ok(e, ref s, new InvasionStartAttempt { OccurrenceId = inv.OccurrenceId }, t);
        Assert.Equal(2, Bal(s, Currencies.InvasionAttempts));
        Ok(e, ref s, new InvasionRecordDamage { OccurrenceId = inv.OccurrenceId, Damage = 12_345 }, t);
        Assert.Equal(123, s.LiveOps.Contributions[inv.OccurrenceId].Score);
        Assert.Equal(123, LiveOpsSystem.ScoreFromDamage(12_345));
        Assert.Equal(0, LiveOpsSystem.ScoreFromDamage(-5));
        Assert.Equal(1, s.Counter("invasion_battles"));
        Fail(e, ref s, new InvasionRecordDamage { OccurrenceId = inv.OccurrenceId, Damage = 1 }, t); // no pending attempt
        Fail(e, ref s, new InvasionStartAttempt { OccurrenceId = "weekly_gloop_tide@0" }, t);
        Assert.True(ServerOnly.Is(typeof(InvasionRecordDamage)));
        Assert.True(ServerOnly.Is(typeof(LiveOpsApplyOutcome)));
        Assert.False(ServerOnly.Is(typeof(InvasionStartAttempt)));
        // Outcome without explicit occurrence uses the latest calendar occurrence.
        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = t };
        LiveOpsSystem.ApplyInvasionOutcome(ctx, "gloop_tide", true, 1);
        Assert.Contains(inv.OccurrenceId, s.LiveOps.AppliedOutcomes);
    }

    [Fact]
    public void SurpriseInvasions_AreHiddenFromUpcoming_ButActiveWhenRunning()
    {
        var e = Engine(); var s = Player(e);
        var upcoming = LiveOpsSystem.UpcomingEvents(e.Content, TestHelpers.T0, 60 * D);
        Assert.DoesNotContain(upcoming, u => u.Surprise);
        Assert.Contains(upcoming, u => u.Kind == LiveOpsKind.Invasion);
        var surprise = e.Content.LiveOpsSchedule.First(x => !x.Announced);
        Assert.True(surprise.Schedule.TryNext(TestHelpers.T0, out long st, out _));
        Assert.Contains(LiveOpsSystem.ActiveEvents(s, e.Content, st + H), a => a.Surprise && a.EventId == surprise.Id);

        // Server-pushed surprise.
        var r = Ok(e, ref s, new LiveOpsStartSurpriseInvasion { InvasionId = "void_choir", StartMs = TestHelpers.T0, DurationMs = 6 * H }, TestHelpers.T0);
        Assert.Contains(LiveOpsSystem.ActiveEvents(s, e.Content, TestHelpers.T0 + H), a => a.OccurrenceId == (string)r.Payload!);
        Assert.DoesNotContain(LiveOpsSystem.ActiveEvents(s, e.Content, TestHelpers.T0 + 7 * H), a => a.OccurrenceId == (string)r.Payload!);
    }
}
