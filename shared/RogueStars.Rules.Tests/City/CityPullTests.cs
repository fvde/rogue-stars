using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.City.Kit;

namespace RogueStars.Rules.Tests.City;

/// <summary>Pull buildings (City design §6 "Pull buildings: chances you pop"): chances pile up, pop for 1..X pulls.</summary>
public class CityPullTests
{
    private const long T0 = TestHelpers.T0;
    private const long Day = 24 * Hour;
    private const string Relay = "bp_token_relay";

    private static (GameEngine e, GameState s, string uid, long t) RelayReady()
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        UnlockAll(e, s);
        long t = BuildActive(e, ref s, Relay, T0, out var uid);
        // No Signal Beacon across a street: no placement bonus.
        Assert.Equal(0, CityLayout.PlacementPct(s, e.Content, uid));
        Staff(e, ref s, uid, "alien_zib", t);
        return (e, s, uid, t);
    }

    [Fact]
    public void Chances_OneEveryTwoDays_StorageThree_OnlyWhileStaffed()
    {
        var (e, s, uid, t) = RelayReady();
        var b = CitySystem.Building(s, uid);
        Assert.Equal(0.5, CitySystem.ChancesPerDay(s, e.Content, b), 9);
        Assert.Equal(3, CitySystem.ChanceStorage(e.Content, e.Content.Blueprint(Relay), 1));
        e.Settle(s, t + 2 * Day);
        Assert.Equal(1, CitySystem.ChancesReady(CitySystem.Building(s, uid)));
        e.Settle(s, t + 20 * Day);
        Assert.Equal(3, CitySystem.Building(s, uid).Chances, 9); // full: stops
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_zib" }, t + 20 * Day);
        Ok(e, ref s, new CityPop { Uid = uid }, t + 20 * Day);
        e.Settle(s, t + 30 * Day);
        Assert.Equal(2, CitySystem.Building(s, uid).Chances, 9); // unstaffed: nothing made
    }

    [Fact]
    public void Chances_LevelsAndStarsTwoToFour_WorkBonusDoesNotApply()
    {
        var c = new ContentDb();
        var def = c.Blueprint(Relay);
        Assert.Equal(0.5, CitySystem.NominalChancesPerDay(c, def, 1, 1), 9);
        Assert.Equal(1.0, CitySystem.NominalChancesPerDay(c, def, 10, 1), 9);
        Assert.Equal(0.5 * 1.3, CitySystem.NominalChancesPerDay(c, def, 1, 4), 9);
        Assert.Equal(0.5 * 1.3, CitySystem.NominalChancesPerDay(c, def, 1, 10), 9);
        Assert.Equal(1.15, CitySystem.NominalChancesPerDay(c, def, 10, 10), 9);
        Assert.Equal(6, CitySystem.ChanceStorage(c, def, 10));
        Assert.Equal(4, CitySystem.ChanceStorage(c, def, 5));
        Assert.Equal(new[] { 1, 1, 1, 1, 2, 2, 2, 2, 2, 3 }, Enumerable.Range(1, 10).Select(st => CitySystem.MaxPullsPerPop(c, st)));

        var (e, s, uid, t) = RelayReady();
        GrantAlien(e, s, "alien_blorp");
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_blorp", BuildingUid = uid }, t);
        Assert.Equal(0.5, CitySystem.ChancesPerDay(s, e.Content, CitySystem.Building(s, uid)), 9);
    }

    [Fact]
    public void Pop_GrantsOneToXPulls_ByStarOdds_Deterministic()
    {
        var (e, s, uid, t) = RelayReady();
        CitySystem.Building(s, uid).Level = 10;
        CitySystem.Owned(s, Relay).Stars = 10;
        e.Settle(s, t + 30 * Day);
        Assert.Equal(6, CitySystem.ChancesReady(CitySystem.Building(s, uid)));
        long before = s.Wallet.Get(Currencies.BlueprintTokens);
        var r = Ok(e, ref s, new CityPop { Uid = uid }, t + 30 * Day);
        var one = Assert.Single((List<CityPopResult>)r.Payload);
        Assert.InRange(one.Pulls, 1, 3);
        Assert.Equal(Currencies.BlueprintTokens, one.Currency);
        Assert.Equal(before + one.Pulls, s.Wallet.Get(Currencies.BlueprintTokens));
        Assert.Equal(5, CitySystem.ChancesReady(CitySystem.Building(s, uid)));
        var all = (List<CityPopResult>)Ok(e, ref s, new CityPop(), t + 30 * Day).Payload; // pop all
        Assert.Equal(5, all.Count);
        Assert.Equal(0, CitySystem.ChancesReady(CitySystem.Building(s, uid)));
        Assert.Equal("nothing to pop", Fail(e, ref s, new CityPop(), t + 30 * Day));
        Assert.Equal(6, s.Counter("city_chances_popped"));

        // Many pops at 10★ average ~1.4 pulls (70 / 20 / 10).
        var c = e.Content;
        var rng = new Rng(7);
        double sum = 0;
        for (int i = 0; i < 20000; i++) sum += 1 + rng.PickWeighted(new List<double>(CitySystem.PopOdds(c, 10)));
        Assert.InRange(sum / 20000, 1.37, 1.43);
    }

    [Fact]
    public void Placement_SpeedsChancesUp()
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        // The Beacon's Cliffside: +30% on the land edge: (6..7, 8..10) rotated, patch A (undug) to the west.
        long t = BuildAt(e, ref s, "bp_signal_beacon", 6, 8, T0, out var beacon, rotated: true);
        Assert.Equal(30, CityLayout.PlacementPct(s, e.Content, beacon));
        Staff(e, ref s, beacon, "alien_zib", t);
        Assert.Equal(0.65, CitySystem.ChancesPerDay(s, e.Content, CitySystem.Building(s, beacon)), 9);
    }
}
