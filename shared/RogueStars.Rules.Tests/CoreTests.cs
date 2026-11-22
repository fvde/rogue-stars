using RogueStars.Rules.Core;
using Xunit;

namespace RogueStars.Rules.Tests;

public class CoreTests
{
    [Fact]
    public void Rng_IsDeterministic_AndSurvivesSerialization()
    {
        var a = new Rng(123); var b = new Rng(123);
        for (int i = 0; i < 100; i++) Assert.Equal(a.NextULong(), b.NextULong());
        var c = TestHelpers.Clone(a);
        Assert.Equal(a.NextULong(), c.NextULong());
    }

    [Fact]
    public void Rng_RangeAndWeightsStayInBounds()
    {
        var r = new Rng(7);
        for (int i = 0; i < 1000; i++) { int v = r.Range(1, 7); Assert.InRange(v, 1, 6); }
        Assert.Equal(-1, r.PickWeighted(new double[] { 0, 0 }));
        Assert.Equal(1, r.PickWeighted(new double[] { 0, 5 }));
    }

    [Fact]
    public void Wallet_RegenSettlesWholeTicks_AndStopsAtCap()
    {
        var rule = new RegenRule { Currency = "stamina", BaseCap = 10, AmountPerTick = 1, TickMs = 60_000 };
        var w = new Wallet();
        w.SettleRegen(rule, 0);
        w.SettleRegen(rule, 150_000);               // 2.5 ticks -> 2
        Assert.Equal(2, w.Get("stamina"));
        w.SettleRegen(rule, 180_000);               // remainder carried -> 3
        Assert.Equal(3, w.Get("stamina"));
        w.SettleRegen(rule, 10_000_000);
        Assert.Equal(10, w.Get("stamina"));         // capped
        w.Add("stamina", 5);                        // rewards may exceed cap
        w.SettleRegen(rule, 20_000_000);
        Assert.Equal(15, w.Get("stamina"));
    }

    [Fact]
    public void Wallet_TrySpendMany_IsAllOrNothing()
    {
        var w = new Wallet(); w.Add("a", 5); w.Add("b", 1);
        Assert.False(w.TrySpend(new[] { new Cost("a", 3), new Cost("b", 2) }));
        Assert.Equal(5, w.Get("a"));
        Assert.True(w.TrySpend(new[] { new Cost("a", 3), new Cost("b", 1) }));
        Assert.Equal(2, w.Get("a"));
    }

    [Fact]
    public void Modifiers_MultiplyAndRespectTimeAndWildcard()
    {
        var mods = new List<Modifier>
        {
            new() { Target = "production:diamonds", Multiplier = 2, StartMs = 0, EndMs = 100 },
            new() { Target = "production:*", Multiplier = 1.5, StartMs = 0, EndMs = 0 },
        };
        Assert.Equal(3, ModifierMath.Multiplier(mods, "production:diamonds", 50), 6);
        Assert.Equal(1.5, ModifierMath.Multiplier(mods, "production:diamonds", 150), 6);
        Assert.Equal(1.5, ModifierMath.Multiplier(mods, "production:grain", 50), 6);
    }

    private sealed class Boom : GameCommand { }

    [Fact]
    public void Engine_FailedCommandLeavesStateUntouched()
    {
        var engine = TestHelpers.Engine();
        engine.Register<Boom>((cmd, ctx) => { ctx.State.Wallet.Add("diamonds", 99); throw new RuleException("nope"); });
        var state = TestHelpers.NewPlayer(engine);
        long before = state.Wallet.Get("diamonds"); long version = state.Version;
        var result = engine.Execute(ref state, new Boom(), TestHelpers.T0);
        Assert.False(result.Ok);
        Assert.Equal("nope", result.Error);
        Assert.Equal(before, state.Wallet.Get("diamonds"));
        Assert.Equal(version, state.Version);
    }
}
