using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Progression;

/// <summary>Helpers for Progression tests: engines with stub City granters, direct grants, command shortcuts.</summary>
public static class ProgressionKit
{
    public const long Day = 86_400_000;
    public static ContentDb C => ContentDb.Default;

    /// <summary>Engine with stub granters for City-owned kinds (Blueprint/Alien) until City's real ones exist.</summary>
    public static GameEngine Engine(bool cloner = true, bool stubCity = true)
    {
        var e = cloner ? TestHelpers.Engine() : new GameEngine(ContentDb.Default);
        if (stubCity)
        {
            e.Rewards.Register(RewardKinds.Blueprint, (ctx, r) => ctx.State.Wallet.Add("stub_bp_" + r.Id, r.Amount));
            e.Rewards.Register(RewardKinds.Alien, (ctx, r) => ctx.State.Wallet.Add("stub_alien_" + r.Id, r.Amount));
        }
        return e;
    }

    public static GameState Player(GameEngine e, long seed = 42) => e.NewPlayer("p1", TestHelpers.T0, seed);

    public static void Grant(GameEngine e, GameState s, Reward r)
    {
        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = TestHelpers.T0 };
        e.Rewards.Grant(ctx, r);
    }

    /// <summary>Execute and assert success; returns the result.</summary>
    public static CommandResult Ok(GameEngine e, ref GameState s, GameCommand cmd, long now = TestHelpers.T0)
    {
        var r = e.Execute(ref s, cmd, now);
        Assert.True(r.Ok, cmd.Type + " failed: " + r.Error);
        return r;
    }

    /// <summary>Execute and assert failure; returns the error.</summary>
    public static string Fail(GameEngine e, ref GameState s, GameCommand cmd, long now = TestHelpers.T0)
    {
        var r = e.Execute(ref s, cmd, now);
        Assert.False(r.Ok, cmd.Type + " unexpectedly succeeded");
        return r.Error;
    }

    public static void SetBalance(GameState s, string currency, long amount) => s.Wallet.Balances[currency] = amount;
}
