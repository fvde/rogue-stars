using System.Text.Json;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Tests;

/// <summary>Shared helpers: JSON round-trip cloning (same approach as the server) and a ready engine.</summary>
public static class TestHelpers
{
    public static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;

    public const long T0 = 1_800_000_000_000; // fixed epoch ms for deterministic tests

    /// <summary>
    /// Content for workstream tests: identical to ContentDb.Default except no new-player starter kit, because those
    /// tests assert exact inventories. Integration tests use the full content.
    /// </summary>
    public static readonly ContentDb NoStarterKit = MakeNoStarterKit();

    private static ContentDb MakeNoStarterKit()
    {
        var c = new ContentDb();
        c.Onboarding.StarterKit.Clear();
        return c;
    }

    public static GameEngine Engine() => new GameEngine(NoStarterKit) { Cloner = Clone };

    /// <summary>
    /// New player with every feature gate open (dev-unlock flag): for tests that are not about gating.
    /// Gating tests use <see cref="NewLockedPlayer"/> (or the Meta kit).
    /// </summary>
    public static GameState NewPlayer(GameEngine engine, long seed = 42)
    {
        var s = engine.NewPlayer("p1", T0, seed);
        s.Flags.Add(RogueStars.Rules.Systems.Features.DevUnlockAllFlag);
        return s;
    }

    public static GameState NewLockedPlayer(GameEngine engine, long seed = 42) => engine.NewPlayer("p1", T0, seed);
}
