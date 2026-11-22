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

// ---- fakes for the server hook commands (real ones are owned by the rules workstreams) ----

public sealed class PurchaseComplete : GameCommand
{
    public string ProductId;
    public string OrderId;
}

public sealed class AdWatched : GameCommand
{
    public string Placement;
    public string EventId;
}

public sealed class MailDeliver : GameCommand
{
    public string Title;
    public string Source;
    public List<Reward> Rewards = new();
}

public sealed class LiveOpsApplyOutcome : GameCommand
{
    public string InvasionId;
    public string Outcome;
    public int Rank;
    public long Damage;
}

public sealed class ArenaFightResolved : GameCommand
{
    public string OpponentId;
    public bool Won;
}

public static class TestCommands
{
    public static void Register(GameEngine e, bool hooks)
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
        if (!hooks) return;

        e.Register<PurchaseComplete>((c, ctx) =>
        {
            ctx.State.Count("purchase_" + c.ProductId);
            return CommandResult.Success();
        });
        e.Register<AdWatched>((c, ctx) =>
        {
            ctx.State.Count("ad_" + c.Placement);
            return CommandResult.Success();
        });
        e.Register<MailDeliver>((c, ctx) =>
        {
            ctx.State.Count("mail_" + c.Source);
            foreach (var r in c.Rewards) ctx.State.Count("mail_reward_" + r.Id, r.Amount);
            return CommandResult.Success();
        });
        e.Register<LiveOpsApplyOutcome>((c, ctx) =>
        {
            ctx.State.Flags.Add("invasion:" + c.InvasionId + ":" + c.Outcome);
            ctx.State.Count("invasion_rank", c.Rank);
            return CommandResult.Success();
        });
        e.Register<ArenaFightResolved>((c, ctx) =>
        {
            // Stand-in for ticket consumption: fights need the "arena_blocked" flag to be absent.
            GameEngine.Require(!ctx.State.Flags.Contains("arena_blocked"), "no arena tickets");
            ctx.State.Count("arena_fights");
            return CommandResult.Success();
        });
    }
}
