using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Meta.MetaTestKit;

namespace RogueStars.Rules.Tests.City;

/// <summary>
/// "The crash site comes first" (owner, 2026-11-25): the Aliens unlock (level 6) brings the Launch Pod, built at once on the crash site
/// (touching the plaza, inside the start land), so the squad flies from level 6; the City (level 15) opens around it.
/// </summary>
public class CrashSiteTests
{
    private static GameState AtLevel(GameEngine e, ref GameState s, int level, long now)
    {
        s.Counters[Requirements.MainLevelCounter] = level;
        Ok(e, ref s, new LobbyPopupShown { PopupId = "tick" + now }, now);
        return s;
    }

    [Fact]
    public void AliensUnlock_GrantsThePod_BuiltOnTheCrashSite_InAValidLayout()
    {
        var e = Engine(); var s = Player(e);
        AtLevel(e, ref s, 5, TestHelpers.T0);
        Assert.Null(CitySystem.LaunchPod(s));
        Assert.Null(CitySystem.Owned(s, ContentDb.LaunchPodId));

        var r = Ok(e, ref s, new LobbyPopupShown { PopupId = "x" }, TestHelpers.T0 + 1000); // still level 5: nothing
        Assert.DoesNotContain(r.Events, ev => ev.Type == "crash_site_pod");
        s.Counters[Requirements.MainLevelCounter] = 6;
        r = Ok(e, ref s, new LobbyPopupShown { PopupId = "y" }, TestHelpers.T0 + 2000);
        Assert.Contains(r.Events, ev => ev.Type == "feature_unlocked" && ev.Args[0] == Features.Aliens);
        Assert.Contains(r.Events, ev => ev.Type == "feature_unlocked" && ev.Args[0] == Features.AlienSquad);
        Assert.Contains(r.Events, ev => ev.Type == "crash_site_pod");

        var bp = CitySystem.Owned(s, ContentDb.LaunchPodId)!;
        Assert.Equal(1, bp.Stars);
        var pod = CitySystem.LaunchPod(s)!;
        Assert.True(CitySystem.IsBuilt(pod));
        Assert.Equal(1, pod.Level);
        Assert.Null(pod.TimerId); // instant: no build timer for this first copy
        Assert.DoesNotContain(s.Timers.Active, t => t.Kind == CitySystem.TimerBuild);

        // crash site: inside the 30 start tiles, touching the plaza with the fireplace, the layout valid
        var c = e.Content;
        Assert.Contains(CityLayout.Cell(c, pod.X, pod.Y), c.CityTuning.StartCells);
        var g = CityLayout.Grid(s, c);
        var it = g.Find(pod.Uid)!;
        Assert.Contains(CityLayout.SideCells(g, it), cell => cell >= 0 && g.Plaza[cell]);
        Assert.True(CityLayout.Check(g).Ok);
        Assert.Empty(s.City.Streets);

        // the squad works right away: the first alien sits in the seat and joins runs
        Assert.Equal(1, CitySystem.SquadSlots(s, c));
        Assert.Equal(new[] { "alien_blorp" }, AlienSystem.ActiveSquad(s, c));
        var lo = e.BuildLoadout(s, TestHelpers.T0 + 2000);
        Assert.Contains(lo.Aliens, a => a.AlienTypeId == "alien_blorp");
        Assert.False(Features.IsUnlocked(s, c, Features.BiomassFarm)); // the City stays locked
    }

    [Fact]
    public void ThePodSpot_IsDeterministic()
    {
        var e = Engine();
        var a = Player(e); var b = e.NewPlayer("p2", TestHelpers.T0, 7);
        AtLevel(e, ref a, 6, TestHelpers.T0);
        AtLevel(e, ref b, 6, TestHelpers.T0 + 5000);
        var pa = CitySystem.LaunchPod(a)!; var pb = CitySystem.LaunchPod(b)!;
        Assert.Equal((pa.X, pa.Y, pa.Rotated), (pb.X, pb.Y, pb.Rotated));
        // off the fireplace's street axes, next to the plaza (the north side, west of the axis)
        Assert.DoesNotContain(CityLayout.Cell(e.Content, pa.X, pa.Y), CityPlanner.SpineCells(e.Content));
        Assert.Equal((8, 7), (pa.X, pa.Y));
    }

    [Fact]
    public void BeforeTheCity_TheSquadSeatIsUsable_LevelUpWorks_ButNoLayoutEdits()
    {
        var e = Engine(); var s = Player(e);
        long t = TestHelpers.T0;
        AtLevel(e, ref s, 6, t);
        var pod = CitySystem.LaunchPod(s)!;
        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = t };
        ctx.Grant(new Reward(RewardKinds.Alien, "alien_zib", 1)); // a second alien (as from the free Alien pull)
        Assert.NotNull(AlienSystem.Owned(s, "alien_zib"));

        // remove / assign (the pod card) and the Aliens screen's squad toggle
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_blorp", BuildingUid = null }, t + 1);
        Assert.Empty(AlienSystem.ActiveSquad(s, e.Content));
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_zib", BuildingUid = pod.Uid }, t + 2);
        Assert.Equal(new[] { "alien_zib" }, AlienSystem.ActiveSquad(s, e.Content));
        Assert.Equal("no free worker slot", Fail(e, ref s, new AlienAssign { AlienTypeId = "alien_blorp", BuildingUid = pod.Uid }, t + 3));
        Ok(e, ref s, new AlienSetSquad { AlienTypeIds = { "alien_blorp" } }, t + 4);
        Assert.Equal(new[] { "alien_blorp" }, AlienSystem.ActiveSquad(s, e.Content));

        // level-up with its cost type (Biomass only)
        var cost = CitySystem.UpgradeCost(e.Content, e.Content.Blueprint(ContentDb.LaunchPodId)!, 1);
        Assert.Equal(Currencies.Biomass, Assert.Single(cost).Currency);
        s.Wallet.Add(Currencies.Biomass, cost[0].Amount);
        Ok(e, ref s, new CityUpgrade { Uid = pod.Uid }, t + 5);
        Ok(e, ref s, new LobbyPopupShown { PopupId = "later" }, t + D);
        Assert.Equal(2, CitySystem.LaunchPod(s)!.Level);

        // no layout edits before the City: the crash site stays as it is
        Assert.Equal("feature locked", Fail(e, ref s, new CityStore { Uid = pod.Uid }, t + D));
        Assert.Equal("feature locked", Fail(e, ref s, new CityMove { Uid = pod.Uid, X = 0, Y = 0 }, t + D));
        Assert.Equal("feature locked", Fail(e, ref s, new CityStreets { Adds = { CityLayout.Cell(e.Content, 9, 6) } }, t + D));
        Assert.Equal("feature locked", Fail(e, ref s, new CityPlace { BlueprintId = "bp_biomass_farm", X = 11, Y = 11 }, t + D));
    }

    [Fact]
    public void CityUnlock_KeepsThePodWhereItStands_AndTheLandAroundBecomesBuildable()
    {
        var e = Engine(); var s = Player(e);
        AtLevel(e, ref s, 6, TestHelpers.T0);
        var before = CitySystem.LaunchPod(s)!;
        (int x, int y, string uid) spot = (before.X, before.Y, before.Uid);
        AtLevel(e, ref s, 15, TestHelpers.T0 + 1000);
        Assert.True(Features.IsUnlocked(s, e.Content, Features.BiomassFarm));
        var pod = Assert.Single(CitySystem.Copies(s, ContentDb.LaunchPodId));
        Assert.Equal(spot, (pod.X, pod.Y, pod.Uid));
        Assert.True(CitySystem.IsBuilt(pod));
        Assert.Equal(new[] { "alien_blorp" }, AlienSystem.ActiveSquad(s, e.Content));

        // the free 10× Blueprint pull is a normal 10×: a pulled Launch Pod is just shards, never a second copy
        var r = Ok(e, ref s, new GachaPull { PullType = GachaType.Blueprint, Count = 10, Payment = GachaPayment.Free }, TestHelpers.T0 + 2000);
        Assert.Equal(10, ((GachaPullResult)r.Payload!).Items.Count);
        Assert.Single(CitySystem.Copies(s, ContentDb.LaunchPodId));

        // building next to the crash site now works (the farm on the planner's spot)
        s.Wallet.Add(Currencies.Biomass, 10_000);
        var plan = CityPlanner.Find(s, e.Content, "bp_biomass_farm")!;
        foreach (var cell in plan.Streets) Ok(e, ref s, new CityStreets { Adds = { cell } }, TestHelpers.T0 + 3000);
        Ok(e, ref s, new CityPlace { BlueprintId = "bp_biomass_farm", X = plan.X, Y = plan.Y, Rotated = plan.Rotated }, TestHelpers.T0 + 3000);
        Assert.True(CityLayout.Check(CityLayout.Grid(s, e.Content)).Ok);
    }

    [Fact]
    public void OldSave_WithTheAliensUnlock_ButNoPod_GetsItOnLoad_WithTheLaunchPodMoment()
    {
        var e = Engine(); var s = Player(e);
        s.Counters[Requirements.MainLevelCounter] = 9;
        // a save from before the crash site: Aliens recorded and celebrated, Blorp owned, no Launch Pod (feature, blueprint or building)
        Unlock(s, Features.Parts, Features.Aliens);
        s.Onboarding.Celebrated.UnionWith(new[] { Features.Parts, Features.Aliens });
        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = TestHelpers.T0 };
        ctx.Grant(new Reward(RewardKinds.Alien, "alien_blorp", 1));
        Assert.Null(CitySystem.LaunchPod(s));

        var r = Ok(e, ref s, new LobbyPopupShown { PopupId = "load" }, TestHelpers.T0 + 1000);
        Assert.Contains(r.Events, ev => ev.Type == "crash_site_pod");
        Assert.True(CitySystem.IsBuilt(CitySystem.LaunchPod(s)));
        Assert.Equal(new[] { "alien_blorp" }, AlienSystem.ActiveSquad(s, e.Content));
        // the Launch Pod's own moment (its intro) is the only new one
        Assert.Contains(r.Events, ev => ev.Type == "feature_unlocked" && ev.Args[0] == Features.AlienSquad);
        Assert.Contains(Features.AlienSquad, OnboardingSystem.PendingCelebrations(s, e.Content));

        // once only
        r = Ok(e, ref s, new LobbyPopupShown { PopupId = "again" }, TestHelpers.T0 + 2000);
        Assert.DoesNotContain(r.Events, ev => ev.Type == "crash_site_pod");
        Assert.Single(s.City.Buildings);
    }

    [Fact]
    public void DevUnlockAll_WithoutARecordedUnlock_GetsNoPod()
    {
        var e = Engine(); var s = Player(e);
        UnlockAll(s);
        Ok(e, ref s, new LobbyPopupShown { PopupId = "x" }, TestHelpers.T0);
        Assert.Null(CitySystem.LaunchPod(s));
    }
}
