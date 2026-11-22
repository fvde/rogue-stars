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

    public static GameEngine Engine() => new GameEngine(ContentDb.Default) { Cloner = Clone };

    public static GameState NewPlayer(GameEngine engine, long seed = 42) => engine.NewPlayer("p1", T0, seed);
}
