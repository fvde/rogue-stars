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
}
