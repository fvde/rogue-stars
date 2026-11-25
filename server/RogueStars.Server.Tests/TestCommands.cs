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

/// <summary>Client command (tests): opens every feature, adds Diamonds / Grain and builds a staffed Shard Reactor with spare Blorp shards.</summary>
public sealed class TestReactorSetup : GameCommand
{
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
        e.Register<TestReactorSetup>((c, ctx) =>
        {
            var s = ctx.State;
            s.Flags.Add(RogueStars.Rules.Systems.Features.DevUnlockAllFlag);
            s.Wallet.Add(Currencies.Diamonds, 100_000);
            s.Wallet.Add(Currencies.Grain, 100_000);
            s.Wallet.Add(Currencies.AlienShards("alien_blorp"), 5_000);
            ctx.Grant(new Reward(RewardKinds.Blueprint, RogueStars.Rules.Content.ContentDb.ShardReactorId, 1));
            var b = new RogueStars.Rules.Systems.CityBuilding
            {
                Uid = RogueStars.Rules.Content.ContentDb.ShardReactorId + "#1", BlueprintId = RogueStars.Rules.Content.ContentDb.ShardReactorId,
                Placed = true, Level = 1, X = 11, Y = 8, LastSettleMs = ctx.NowMs,
            };
            s.City.Buildings.Add(b);
            ctx.Grant(new Reward(RewardKinds.Alien, "alien_blorp", 1));
            RogueStars.Rules.Systems.AlienSystem.Owned(s, "alien_blorp").WorkBuildingId = b.Uid;
            return CommandResult.Success();
        });
        e.Register<TestServerGrant>((c, ctx) =>
        {
            ctx.State.Count("granted", c.Amount);
            return CommandResult.Success();
        });
    }
}
