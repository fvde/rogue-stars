using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Meta;

/// <summary>Helpers for Meta tests. Each engine gets its own ContentDb so tests can tweak config safely.</summary>
public static class MetaTestKit
{
    public const long D = MetaTime.Day;
    public const long H = MetaTime.Hour;
    /// <summary>Start of the reset-day containing TestHelpers.T0 (Friday 2027-01-15 00:00 UTC).</summary>
    public const long Day0 = 1_799_971_200_000;
    /// <summary>Monday 2027-01-18 00:00 UTC.</summary>
    public const long Monday = Day0 + 3 * D;

    public static GameEngine Engine(bool devReceipts = false)
    {
        var content = new ContentDb();
        content.Shop.AllowDevReceipts = devReceipts;
        return new GameEngine(content) { Cloner = TestHelpers.Clone };
    }

    public static GameState Player(GameEngine e, long now = TestHelpers.T0) => e.NewPlayer("p1", now, 42);

    public static CommandResult Ok(GameEngine e, ref GameState s, GameCommand cmd, long now)
    {
        var r = e.Execute(ref s, cmd, now);
        Assert.True(r.Ok, cmd.Type + " failed: " + r.Error);
        return r;
    }

    public static string Fail(GameEngine e, ref GameState s, GameCommand cmd, long now)
    {
        var r = e.Execute(ref s, cmd, now);
        Assert.False(r.Ok, cmd.Type + " should have failed");
        return r.Error;
    }

    public static void UnlockAll(GameState s) => s.Flags.Add(Features.DevUnlockAllFlag);

    /// <summary>Record feature unlocks directly (as the onboarding settler would), bypassing day gates.</summary>
    public static void Unlock(GameState s, params string[] featureIds)
    {
        foreach (var id in featureIds) s.Onboarding.Unlocked[id] = s.CreatedMs;
    }

    public static long Bal(GameState s, string currency) => s.Wallet.Get(currency);
}
