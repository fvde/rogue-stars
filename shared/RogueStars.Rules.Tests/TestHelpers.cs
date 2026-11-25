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
        return Run.RunTestKit.ClassicBoards(c); // shared by parallel tests: the run-test boards are set up once, here
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
        engine.Settle(s, T0); // releases the reward stash (arena tickets, burrow keys...) now that everything has a use
        return s;
    }

    public static GameState NewLockedPlayer(GameEngine engine, long seed = 42) => engine.NewPlayer("p1", T0, seed);

    /// <summary>
    /// Research needs a running Laboratory building (City design): puts a built Laboratory straight into the state (no layout
    /// rules) and staffs it with a Zib. Returns its uid.
    /// </summary>
    public static string WithLaboratory(GameEngine engine, GameState s, int stars = 1, int level = 1)
    {
        var ctx = new CommandContext { State = s, Content = engine.Content, Engine = engine, NowMs = T0 };
        if (RogueStars.Rules.Systems.CitySystem.Owned(s, ContentDb.LaboratoryId) == null) ctx.Grant(new Reward(RewardKinds.Blueprint, ContentDb.LaboratoryId, 1));
        RogueStars.Rules.Systems.CitySystem.Owned(s, ContentDb.LaboratoryId).Stars = stars;
        string uid = ContentDb.LaboratoryId + "#1";
        s.City.Buildings.Add(new RogueStars.Rules.Systems.CityBuilding { Uid = uid, BlueprintId = ContentDb.LaboratoryId, X = 11, Y = 8, Placed = true, Level = level, LastSettleMs = T0 });
        if (RogueStars.Rules.Systems.AlienSystem.Owned(s, "alien_zib") == null) ctx.Grant(new Reward(RewardKinds.Alien, "alien_zib", 1));
        RogueStars.Rules.Systems.AlienSystem.Owned(s, "alien_zib").WorkBuildingId = uid;
        RogueStars.Rules.Systems.CityLayout.Invalidate(s);
        return uid;
    }
}
