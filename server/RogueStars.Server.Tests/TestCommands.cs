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

/// <summary>Client command (tests): puts the active run at a campfire owning exactly SkillIds.</summary>
public sealed class TestCampfire : GameCommand
{
    public List<string> SkillIds = new();
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
        e.Register<TestCampfire>((c, ctx) =>
        {
            var run = ctx.State.Run.Active;
            GameEngine.Require(run != null, "no run");
            run.Skills.Clear();
            run.SkillLevels.Clear();
            run.Skills.AddRange(c.SkillIds);
            run.Phase = RogueStars.Rules.Systems.RunPhase.Event;
            run.EventId = "campfire";
            run.EventTile = -1;
            return CommandResult.Success();
        });
        e.Register<TestServerGrant>((c, ctx) =>
        {
            ctx.State.Count("granted", c.Amount);
            return CommandResult.Success();
        });
    }
}
