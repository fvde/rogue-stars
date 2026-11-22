#nullable disable
using RogueStars.Rules.Core;

namespace RogueStars.Server.Tests;

/// <summary>Client command: adds Amount to the "test_counter" counter.</summary>
public sealed class TestIncrement : GameCommand
{
    public long Amount;
}

[ServerOnly]
public sealed class TestServerGrant : GameCommand
{
    public long Amount;
}

public static class TestCommands
{
    public static void Register(GameEngine e)
    {
        e.Register<TestIncrement>((c, ctx) =>
        {
            GameEngine.Require(c.Amount > 0, "amount must be positive");
            ctx.State.Count("test_counter", c.Amount);
            ctx.Emit("incremented", c.Amount.ToString());
            return CommandResult.Success(new Dictionary<string, long> { ["total"] = ctx.State.Counter("test_counter") });
        });
        e.Register<TestServerGrant>((c, ctx) =>
        {
            ctx.State.Count("granted", c.Amount);
            return CommandResult.Success();
        });
    }
}
