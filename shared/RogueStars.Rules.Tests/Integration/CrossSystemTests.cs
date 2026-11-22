using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Integration;

/// <summary>Hooks between workstreams wired at integration (regen providers, power score, gates, ads, resets).</summary>
public class CrossSystemTests
{
    private const long T0 = TestHelpers.T0;
    private static ContentDb C => ContentDb.Default;

    private static GameEngine Engine(bool devReceipts = false)
    {
        var content = new ContentDb();
        content.Shop.AllowDevReceipts = devReceipts;
        return new GameEngine(content) { Cloner = TestHelpers.Clone };
    }

    private static CommandResult Ok(GameEngine e, ref GameState s, GameCommand cmd, long now)
    {
        var r = e.Execute(ref s, cmd, now);
        Assert.True(r.Ok, cmd.Type + " failed: " + r.Error);
        return r;
    }

    private static string Fail(GameEngine e, ref GameState s, GameCommand cmd, long now)
    {
        var r = e.Execute(ref s, cmd, now);
        Assert.False(r.Ok, cmd.Type + " should have failed");
        return r.Error;
    }

    private static void Grant(GameEngine e, GameState s, Reward r, long now = T0) =>
        e.Rewards.Grant(new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = now }, r);

    private static long BuildActive(GameEngine e, ref GameState s, string bp, int x, int y, long now)
    {
        if (CitySystem.Owned(s, bp) == null) Grant(e, s, new Reward(RewardKinds.Blueprint, bp, 1));
        Ok(e, ref s, new CityBuild { BlueprintId = bp, X = x, Y = y }, now);
        var t = TimerSystem.Get(s, CitySystem.Owned(s, bp).TimerId);
        long done = t?.EndsMs ?? now;
        e.Settle(s, done);
        Assert.True(CitySystem.IsActive(CitySystem.Owned(s, bp)));
        return done;
    }

    // ---- regen providers --------------------------------------------------------------------------

    [Fact]
    public void StaminaCap_IncludesCityBoons_AndMonthlyPass()
    {
        var e = Engine(devReceipts: true);
        var s = e.NewPlayer("p1", T0, 1);
        s.Wallet.Add(Currencies.Ore, 1_000_000); s.Wallet.Add(Currencies.Biomass, 1_000_000);
        long baseCap = C.Regen(Currencies.Stamina)!.BaseCap;
        Assert.Equal(baseCap, EconomySystem.RegenCap(s, e.Content, Currencies.Stamina));

        long t = BuildActive(e, ref s, "bp_stamina_well", 4, 4, T0);
        e.Settle(s, t);
        long withWell = EconomySystem.RegenCap(s, e.Content, Currencies.Stamina);
        Assert.Equal(baseCap + (long)CitySystem.StaminaBonus(s, e.Content, t).CapBonus, withWell);
        Assert.True(withWell > baseCap);

        Ok(e, ref s, new PurchaseStart { ProductId = e.Content.Shop.MonthlyPassProductId }, t);
        Ok(e, ref s, new PurchaseComplete { ProductId = e.Content.Shop.MonthlyPassProductId, ReceiptId = "dev:mp1" }, t);
        e.Settle(s, t);
        Assert.Equal(withWell + e.Content.Shop.MonthlyPassStaminaCapBonus, EconomySystem.RegenCap(s, e.Content, Currencies.Stamina));
    }

    [Fact]
    public void StaminaRegen_TickSpeedFromRegenSpire()
    {
        var e = Engine();
        var s = e.NewPlayer("p1", T0, 1);
        s.Wallet.Add(Currencies.Ore, 1_000_000); s.Wallet.Add(Currencies.Biomass, 1_000_000);
        long t = BuildActive(e, ref s, "bp_regen_spire", 4, 4, T0);
        e.Settle(s, t);
        double pct = CitySystem.StaminaBonus(s, e.Content, t).TickSpeedPercent;
        Assert.True(pct > 0);
        Assert.Equal(pct, s.Wallet.Regen[Currencies.Stamina].TickSpeedPercent, 6);
    }

    [Fact]
    public void ArenaTickets_AreSettledOnlyByEconomy_WithRegenModifiers()
    {
        var e = Engine();
        var s = e.NewPlayer("p1", T0, 1);
        var rule = C.Regen(Currencies.ArenaTickets)!;
        s.Wallet.Balances[Currencies.ArenaTickets] = 0;
        e.Settle(s, T0);
        // A ×2 regen modifier halves the tick; a second (unmodified) settler would have settled at base speed.
        s.Modifiers.Add(new Modifier { Id = "x", Target = "regen:" + Currencies.ArenaTickets, Multiplier = 2, StartMs = 0, EndMs = 0 });
        e.Settle(s, T0 + rule.TickMs);
        Assert.Equal(2, s.Wallet.Get(Currencies.ArenaTickets));
    }

    // ---- power score ------------------------------------------------------------------------------

    [Fact]
    public void Power_IncludesSquadAliens_AndCityCollectionBonus()
    {
        var e = Engine();
        var s = e.NewPlayer("p1", T0, 1);
        s.Flags.Add(Features.DevUnlockAllFlag);
        s.Wallet.Add(Currencies.Ore, 1_000_000); s.Wallet.Add(Currencies.Biomass, 1_000_000);
        long t = BuildActive(e, ref s, "bp_housing_pod", 4, 4, T0);
        var before = e.BuildLoadout(s, t);
        Assert.Equal(WormSystem.ComputePower(before, e.Content), before.Power);

        Grant(e, s, new Reward(RewardKinds.Alien, "alien_voidmaw", 1), t);
        Ok(e, ref s, new AlienSetSquad { AlienTypeIds = { "alien_voidmaw" } }, t);
        var after = e.BuildLoadout(s, t);
        Assert.Single(after.Aliens);
        Assert.Equal(WormSystem.ComputePower(after, e.Content), after.Power);
        Assert.True(after.Power > before.Power);
        // Deterministic.
        Assert.Equal(after.Power, e.BuildLoadout(TestHelpers.Clone(s), t).Power);
    }

    // ---- feature gates ----------------------------------------------------------------------------

    [Fact]
    public void Gates_FollowGddOrder_AndUnlockFromRealProgress()
    {
        var e = Engine();
        var s = e.NewPlayer("p1", T0, 1);
        s.Wallet.Add(Currencies.Diamonds, 1_000_000);
        long t = T0;
        GachaPull P(GachaType ty) => new GachaPull { PullType = ty, Payment = GachaPayment.Diamonds };
        Assert.Equal("feature locked", Fail(e, ref s, P(GachaType.Part), t));
        Assert.Equal("feature locked", Fail(e, ref s, P(GachaType.Alien), t));
        Assert.Equal("feature locked", Fail(e, ref s, P(GachaType.Blueprint), t));
        Assert.Equal("feature locked", Fail(e, ref s, new HeroUnlock { HeroId = "hero_star_knight" }, t));
        Assert.Equal("feature locked", Fail(e, ref s, new MountEquip { MountId = "mount_scout_saucer" }, t));
        Assert.Equal("feature locked", Fail(e, ref s, new BreedStart { ParentA = "a", ParentB = "b" }, t));
        Assert.Equal("feature locked", Fail(e, ref s, new AlienSetSquad(), t));
        Assert.Equal("feature locked", Fail(e, ref s, new RuneLock { RuneUid = "r" }, t));
        Assert.Equal("feature locked", Fail(e, ref s, new PartForgeUnlock { Uid = "p" }, t));

        // Portal progress is Run's main_level_max counter (max-style).
        s.Counters[Requirements.MainLevelCounter] = 3;
        Ok(e, ref s, P(GachaType.Part), t);
        Assert.Equal("feature locked", Fail(e, ref s, P(GachaType.Alien), t));
        s.Counters[Requirements.MainLevelCounter] = 10;
        Ok(e, ref s, P(GachaType.Alien), t);
        Ok(e, ref s, P(GachaType.Blueprint), t);
        Assert.Equal("feature locked", Fail(e, ref s, new MountEquip { MountId = "mount_scout_saucer" }, t));

        // Forge: worm level 25 OR main level 28 — read from the real worm level, not a counter.
        Assert.False(Features.IsUnlocked(s, e.Content, Features.Forge));
        s.Counters[Requirements.WormLevelCounter] = 99; // stale counters do not unlock
        Assert.False(Features.IsUnlocked(s, e.Content, Features.Forge));
        s.Worm.Level = 25;
        Assert.True(Features.IsUnlocked(s, e.Content, Features.Forge));
    }

    // ---- onboarding -------------------------------------------------------------------------------

    [Fact]
    public void NewPlayer_GetsStarterKit_AndFeatureUnlockGrants()
    {
        var e = Engine();
        var s = e.NewPlayer("p1", T0, 1);
        Assert.Equal(e.Content.EconomyConfig.StartingWallet[Currencies.Diamonds], s.Wallet.Get(Currencies.Diamonds));
        Assert.Equal(3, s.Parts.Items.Count);
        Assert.NotNull(CitySystem.Owned(s, "bp_biomass_farm"));
        Assert.Empty(s.Aliens.Owned);

        // Clearing main level 7 unlocks Aliens (first alien) and Housing (squad slot building).
        s.Counters[Requirements.MainLevelCounter] = 7;
        var r = Ok(e, ref s, new CityCollect(), T0 + 1000);
        Assert.Contains(r.Events, ev => ev.Type == "feature_unlocked" && ev.Args[0] == Features.Aliens);
        Assert.NotNull(AlienSystem.Owned(s, "alien_blorp"));
        Assert.NotNull(CitySystem.Owned(s, "bp_housing_pod"));
        Assert.NotNull(CitySystem.Owned(s, "bp_laboratory"));
        // Sticky: a later command does not grant again.
        Ok(e, ref s, new CityCollect(), T0 + 2000);
        Assert.Equal(0, s.Counter("alien_duplicates"));
        Assert.Equal(0, s.Counter("blueprint_duplicates"));

        // Heroes unlock grants exactly the shards to unlock the first hero.
        s.Counters[Requirements.MainLevelCounter] = 10;
        Ok(e, ref s, new HeroUnlock { HeroId = "hero_star_knight" }, T0 + 3000);
    }

    // ---- ads --------------------------------------------------------------------------------------

    [Fact]
    public void Ads_StaminaTimerAndGacha_ConsumeMetaCaps_NoAdsIsInstant()
    {
        var e = Engine();
        var s = e.NewPlayer("p1", T0, 1);
        s.Flags.Add(Features.DevUnlockAllFlag);
        s.Wallet.Add(Currencies.Ore, 1_000_000); s.Wallet.Add(Currencies.Biomass, 1_000_000);
        long t = T0;

        // Token required without No Ads.
        Assert.Equal("ad token required", Fail(e, ref s, new BuyStamina { Method = StaminaRefillMethod.Ad }, t));
        Ok(e, ref s, new BuyStamina { Method = StaminaRefillMethod.Ad, AdToken = "a1" }, t);
        Assert.Equal(e.Content.AdPlacement("stamina_refill")!.DailyCap - 1, ShopSystem.AdViewsLeft(s, e.Content, "stamina_refill", t));

        // Gacha extra free pull: one per day per type.
        var pull = new GachaPull { PullType = GachaType.Part, Payment = GachaPayment.Ad, AdToken = "a2" };
        long diamonds = s.Wallet.Get(Currencies.Diamonds), tokens = s.Wallet.Get(Currencies.PartTokens);
        Ok(e, ref s, pull, t);
        Assert.Equal(diamonds, s.Wallet.Get(Currencies.Diamonds));
        Assert.Equal(tokens, s.Wallet.Get(Currencies.PartTokens));
        Assert.Equal("ad daily cap reached", Fail(e, ref s, pull, t));
        Assert.Equal("ad pulls are single pulls", Fail(e, ref s, new GachaPull { PullType = GachaType.Alien, Payment = GachaPayment.Ad, Count = 10, AdToken = "x" }, t));

        // Timer speed-up ad shares the "timer_speedup" placement cap.
        Grant(e, s, new Reward(RewardKinds.Blueprint, "bp_ore_mine", 1));
        Ok(e, ref s, new CityBuild { BlueprintId = "bp_ore_mine", X = 5, Y = 4 }, t);
        var timer = TimerSystem.Get(s, CitySystem.Owned(s, "bp_ore_mine").TimerId)!;
        Ok(e, ref s, new TimerSpeedUp { TimerId = timer.Id, Method = SpeedUpMethod.Ad, AdToken = "a3" }, t);
        Assert.Equal(e.Content.AdPlacement("timer_speedup")!.DailyCap - 1, ShopSystem.AdViewsLeft(s, e.Content, "timer_speedup", t));

        // No Ads: instant without a token, caps still apply; every ad counts once in ads_watched.
        s.Flags.Add(e.Content.Shop.NoAdsFlag);
        Ok(e, ref s, new TimerSpeedUp { TimerId = timer.Id, Method = SpeedUpMethod.Ad }, t);
        Ok(e, ref s, new BuyStamina { Method = StaminaRefillMethod.Ad }, t);
        Ok(e, ref s, new GachaPull { PullType = GachaType.Alien, Payment = GachaPayment.Ad }, t);
        Assert.Equal(6, s.Counter("ads_watched"));
    }

    // ---- daily resets -----------------------------------------------------------------------------

    [Fact]
    public void DailyLimits_AllResetOnTheSameBoundary()
    {
        var e = Engine();
        var s = e.NewPlayer("p1", T0, 1);
        s.Flags.Add(Features.DevUnlockAllFlag);
        s.Flags.Add(e.Content.Shop.NoAdsFlag);
        s.Wallet.Add(Currencies.Ore, 1_000_000); s.Wallet.Add(Currencies.Biomass, 1_000_000);
        long dayStart = GameClock.DayStartMs(e.Content, GameClock.DayIndex(e.Content, T0));
        long lastMs = dayStart + GameClock.Day - 1, next = dayStart + GameClock.Day;
        Assert.Equal(MetaTime.DayIndex(e.Content, T0), EconomySystem.DayIndex(e.Content, T0));

        Grant(e, s, new Reward(RewardKinds.Blueprint, "bp_spore_vats", 1));
        Ok(e, ref s, new CityBuild { BlueprintId = "bp_spore_vats", X = 4, Y = 4 }, T0);
        string timerId = CitySystem.Owned(s, "bp_spore_vats").TimerId;
        Ok(e, ref s, new BuyStamina { Method = StaminaRefillMethod.DailyFree }, T0);
        Ok(e, ref s, new GachaPull { PullType = GachaType.Part, Payment = GachaPayment.Ad }, T0);
        Ok(e, ref s, new TimerSpeedUp { TimerId = timerId, Method = SpeedUpMethod.Free }, T0);
        Ok(e, ref s, new LoginClaim(), T0);

        // Still the same reset-day one millisecond before the boundary: every daily limit holds.
        Fail(e, ref s, new BuyStamina { Method = StaminaRefillMethod.DailyFree }, lastMs);
        Fail(e, ref s, new GachaPull { PullType = GachaType.Part, Payment = GachaPayment.Ad }, lastMs);
        Assert.Equal(0, TimerSystem.FreeSpeedUpsLeft(s, e.Content, lastMs));
        Assert.False(QuestSystem.CanClaimLogin(s, e.Content, lastMs));

        // At the boundary everything resets together.
        Ok(e, ref s, new BuyStamina { Method = StaminaRefillMethod.DailyFree }, next);
        Ok(e, ref s, new GachaPull { PullType = GachaType.Part, Payment = GachaPayment.Ad }, next);
        Assert.Equal(e.Content.TimerTuning.FreeSpeedUpsPerDay, TimerSystem.FreeSpeedUpsLeft(s, e.Content, next));
        Assert.True(QuestSystem.CanClaimLogin(s, e.Content, next));
    }
}
