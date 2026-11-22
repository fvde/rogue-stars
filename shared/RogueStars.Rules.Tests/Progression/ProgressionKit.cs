using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Progression;

/// <summary>Helpers for Progression tests: engines (real City granters), direct grants, command shortcuts.</summary>
public static class ProgressionKit
{
    public const long Day = 86_400_000;
    public static ContentDb C => ContentDb.Default;

    public static GameEngine Engine(bool cloner = true) => cloner ? TestHelpers.Engine() : new GameEngine(TestHelpers.NoStarterKit);

    /// <summary>New player with all feature gates open (Progression tests are not about gating).</summary>
    public static GameState Player(GameEngine e, long seed = 42) => TestHelpers.NewPlayer(e, seed);

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
