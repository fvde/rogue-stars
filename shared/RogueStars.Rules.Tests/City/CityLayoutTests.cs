using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.City.Kit;

namespace RogueStars.Rules.Tests.City;

/// <summary>
/// City layout (City design §3, §5): the shared validator (land, overlap, features, street links, building touch, copies), the
/// placement skills per kind, the cap, the cache, and digging. Map reference: plaza (8..10, 8..10), fireplace (9,9); start land
/// around it includes (11, 8..10), (7..11, 7), (6..7, 8..10), (7..10, 11); (12, 9) is undug patch B.
/// </summary>
public class CityLayoutTests
{
    private const long T0 = TestHelpers.T0;
    private const string Farm = "bp_biomass_farm";

    private static (GameEngine e, GameState s) Fresh()
    {
        var e = TestHelpers.Engine();
        return (e, Rich(e));
    }

    /// <summary>Puts a built building straight into the state (skill math without the placement rules).</summary>
    private static string Put(GameEngine e, GameState s, string bp, int x, int y, bool rotated = false)
    {
        if (CitySystem.Owned(s, bp) == null) GrantBlueprint(e, s, bp);
        var def = e.Content.Blueprint(bp);
        string uid = CitySystem.NextUid(s, def);
        s.City.Buildings.Add(new CityBuilding { Uid = uid, BlueprintId = bp, X = x, Y = y, Rotated = rotated, Placed = true, Level = 1, LastSettleMs = T0 });
        CityLayout.Invalidate(s);
        return uid;
    }

    private static void PutStreet(GameEngine e, GameState s, int x, int y)
    {
        s.City.Streets.Add(Cell(e, x, y));
        CityLayout.Invalidate(s);
    }

    private static double Pct(GameEngine e, GameState s, string uid) => CityLayout.PlacementPct(s, e.Content, uid);

    // ---- validator ----

    [Fact]
    public void Place_RejectsOffLand_Plaza_CornerOnly_Overlap()
    {
        var (e, s) = Fresh();
        GrantBlueprint(e, s, Farm);
        Assert.Equal("not dug land", Fail(e, ref s, new CityPlace { BlueprintId = Farm, X = 12, Y = 9 }, T0));
        Assert.Equal("not dug land", Fail(e, ref s, new CityPlace { BlueprintId = Farm, X = -1, Y = 9 }, T0));
        Assert.Equal("on the plaza", Fail(e, ref s, new CityPlace { BlueprintId = Farm, X = 9, Y = 9 }, T0));
        // (11,7) only touches the plaza's corner (10,8): corners don't count.
        Assert.Equal("must touch a street", Fail(e, ref s, new CityPlace { BlueprintId = Farm, X = 11, Y = 7 }, T0));
        Streets(e, ref s, T0, (11, 8));
        Ok(e, ref s, new CityPlace { BlueprintId = Farm, X = 11, Y = 7 }, T0);
        Assert.Equal("space taken", Fail(e, ref s, new CityPlace { BlueprintId = Farm, X = 11, Y = 7 }, T0));
        Assert.Equal("space taken", Fail(e, ref s, new CityPlace { BlueprintId = Farm, X = 11, Y = 8 }, T0)); // on the street
        Assert.Equal("blueprint not owned", Fail(e, ref s, new CityPlace { BlueprintId = "bp_gene_lab", X = 7, Y = 9 }, T0));
        // Rotation swaps the footprint: a 2×1 Breeding Pen fits (7,8)-(7,9) only rotated... and (6,9)-(7,9) unrotated.
        GrantBlueprint(e, s, "bp_breeding_pen");
        Assert.True(CityLayout.Validate(s, e.Content, CityEdit.Place("bp_breeding_pen", 6, 9, false)).Ok);
        Assert.True(CityLayout.Validate(s, e.Content, CityEdit.Place("bp_breeding_pen", 7, 9, true)).Ok);
        Assert.Equal("not dug land", CityLayout.Validate(s, e.Content, CityEdit.Place("bp_breeding_pen", 11, 9, false)).Error);
    }

    [Fact]
    public void Features_AreObstacles()
    {
        var (e, s) = Fresh();
        UnlockAll(e, s);
        GrantBlueprint(e, s, Farm);
        var check = CityLayout.Validate(s, e.Content, CityEdit.Place(Farm, 4, 5, false)); // spring in patch I
        Assert.Equal("blocked by terrain", check.Error);
        Assert.Contains(Cell(e, 4, 5), check.BadCells);
        Assert.Equal("not on free land", CityLayout.Validate(s, e.Content, CityEdit.Streets(new[] { Cell(e, 4, 5) }, null)).Error);
    }

    [Fact]
    public void Streets_MustLinkToThePlaza_ErasingCannotCutOff()
    {
        var (e, s) = Fresh();
        UnlockAll(e, s);
        Assert.Equal("streets must link to the plaza", Fail(e, ref s, new CityStreets { Adds = { Cell(e, 7, 6) } }, T0));
        Assert.Equal("not on free land", Fail(e, ref s, new CityStreets { Adds = { Cell(e, 9, 9) } }, T0)); // plaza
        Streets(e, ref s, T0, (11, 8), (12, 8));
        GrantBlueprint(e, s, Farm);
        string farm = (string)Ok(e, ref s, new CityPlace { BlueprintId = Farm, X = 13, Y = 8 }, T0).Payload;
        // Erasing (11,8) would cut off (12,8) and the farm.
        var check = CityLayout.Validate(s, e.Content, CityEdit.Streets(null, new[] { Cell(e, 11, 8) }));
        Assert.Equal("streets must link to the plaza", check.Error);
        Assert.Contains(Cell(e, 12, 8), check.BadCells);
        Assert.Contains(farm, check.BadUids);
        // Erasing (12,8) cuts only the farm off.
        Assert.Equal("a building would lose its street", Fail(e, ref s, new CityStreets { Removes = { Cell(e, 12, 8) } }, T0));
        Assert.Equal("not a street", Fail(e, ref s, new CityStreets { Removes = { Cell(e, 7, 7) } }, T0));
        Assert.Equal("already a street", Fail(e, ref s, new CityStreets { Adds = { Cell(e, 11, 8) } }, T0));
        // A stroke may move the link: add (13,7)… no, remove the farm first, then erase both in one stroke.
        Ok(e, ref s, new CityStore { Uid = farm }, T0);
        Ok(e, ref s, new CityStreets { Removes = { Cell(e, 11, 8), Cell(e, 12, 8) } }, T0);
        Assert.Empty(s.City.Streets);
    }

    [Fact]
    public void Validate_IsSharedByTheEditorPreview()
    {
        var (e, s) = Fresh();
        GrantBlueprint(e, s, "bp_stamina_well");
        var check = CityLayout.Preview(s, e.Content, CityEdit.Place("bp_stamina_well", 11, 9, false), out double pct, out double flat);
        Assert.Equal("not dug land", check.Error); // unrotated (11,9)-(12,9): (12,9) is undug; rotated (11,9)-(11,10) fits
        Assert.True(CityLayout.Validate(s, e.Content, CityEdit.Place("bp_stamina_well", 11, 9, true)).Ok);
        check = CityLayout.Preview(s, e.Content, CityEdit.Place("bp_stamina_well", 6, 9, false), out pct, out flat);
        Assert.True(check.Ok);
        Assert.Equal(20, pct); // Hearth: (7,9) is 2 tiles from the fire
        Assert.Equal(0, flat);
    }

    // ---- placement skills ----

    [Fact]
    public void Skill_Neighbours_PerMatch_UpToMax()
    {
        var (e, s) = Fresh();
        UnlockAll(e, s);
        string a = Put(e, s, Farm, 11, 9);
        Assert.Equal(0, Pct(e, s, a));
        Put(e, s, Farm, 12, 9);
        Assert.Equal(10, Pct(e, s, a));
        Put(e, s, "bp_grain_farm", 11, 10); // a farm too (tag "farm")
        Assert.Equal(20, Pct(e, s, a));
        Put(e, s, Farm, 11, 8);
        Assert.Equal(20, Pct(e, s, a)); // max +20%
        // Gear Works: +10% per touching Ore Mine, max +30%; corner contact does not count.
        string gw = Put(e, s, "bp_gear_works", 13, 12); // (13..14, 12)
        string m1 = Put(e, s, "bp_ore_mine", 15, 13); // corner only
        Assert.Equal(0, Pct(e, s, gw));
        var mine = CitySystem.Building(s, m1);
        mine.X = 12; mine.Y = 12;
        CityLayout.Invalidate(s);
        Assert.Equal(10, Pct(e, s, gw));
        Put(e, s, "bp_ore_mine", 13, 13);
        Put(e, s, "bp_ore_mine", 14, 13);
        Assert.Equal(30, Pct(e, s, gw));
    }

    [Fact]
    public void Skill_Distance_HearthAndOutskirts()
    {
        var (e, s) = Fresh();
        UnlockAll(e, s);
        string well = Put(e, s, "bp_stamina_well", 11, 9);   // 2 tiles
        string well2 = Put(e, s, "bp_stamina_well", 13, 9);  // 4 tiles
        Assert.Equal(20, Pct(e, s, well));
        Assert.Equal(0, Pct(e, s, well2));
        string near = Put(e, s, "bp_fuel_refinery", 14, 10); // 5 + 1 = 6 tiles → Outskirts
        string close = Put(e, s, "bp_fuel_refinery", 13, 8); // 4 + 1 = 5
        Assert.Equal(20, Pct(e, s, near));
        Assert.Equal(0, Pct(e, s, close));
        string scout = Put(e, s, "bp_scout_post", 3, 12);   // flat +1 skip at 6+ tiles
        Assert.Equal(0, Pct(e, s, scout));
        Assert.Equal(1, CityLayout.PlacementFlat(s, e.Content, scout));
    }

    [Fact]
    public void Skill_Cliffside_LostWhenDiggingTheEdge()
    {
        var (e, s) = Fresh();
        string mine = Put(e, s, "bp_diamond_mine", 6, 8); // (6..7, 8..9): west (5, 8..9) is patch A, north (6,7) patch D
        Assert.Equal(30, Pct(e, s, mine));
        s.City.DugPatches.Add("patch_a");
        CityLayout.Invalidate(s);
        Assert.Equal(30, Pct(e, s, mine));
        s.City.DugPatches.Add("patch_d");
        CityLayout.Invalidate(s);
        Assert.Equal(0, Pct(e, s, mine));
    }

    [Fact]
    public void Skill_OpenGround_CountsEmptyLandOnly()
    {
        var (e, s) = Fresh();
        string grain = Put(e, s, "bp_grain_farm", 7, 9); // (6,9) (7,8) (7,10) open, (8,9) plaza
        Assert.Equal(20, Pct(e, s, grain));
        PutStreet(e, s, 7, 8);
        Put(e, s, Farm, 7, 10);
        Assert.Equal(0, Pct(e, s, grain)); // 1 open tile left
        string spire = Put(e, s, "bp_regen_spire", 6, 7);
        Assert.Equal(0, Pct(e, s, spire)); // needs 3+
    }

    [Fact]
    public void Skill_AcrossTheStreet_AndFlatReroll()
    {
        var (e, s) = Fresh();
        UnlockAll(e, s);
        string tg = Put(e, s, "bp_training_grounds", 11, 7);   // (11..12, 7)
        PutStreet(e, s, 11, 8); PutStreet(e, s, 12, 8);
        string wa = Put(e, s, "bp_war_academy", 11, 9);        // (11..13, 9..10)
        Assert.Equal(20, Pct(e, s, tg));
        Assert.Equal(1, CityLayout.PlacementFlat(s, e.Content, wa));
        Assert.Equal(0, Pct(e, s, wa));
        // Not across a street: touching directly does not count.
        var (e2, s2) = Fresh();
        UnlockAll(e2, s2);
        string tg2 = Put(e2, s2, "bp_training_grounds", 11, 8);
        Put(e2, s2, "bp_war_academy", 11, 9);
        Assert.Equal(0, Pct(e2, s2, tg2));
    }

    [Fact]
    public void Skill_Aura_OnlyWhileRunning_AndTheCap()
    {
        var (e, s) = Fresh();
        UnlockAll(e, s);
        string dome = Put(e, s, "bp_barracks_dome", 11, 6);   // (11..13, 6..7)
        string gw = Put(e, s, "bp_gear_works", 11, 8);        // (11..12, 8) touches the dome
        Assert.Equal(0, Pct(e, s, gw));
        GrantAlien(e, s, "alien_zib");
        AlienSystem.Owned(s, "alien_zib").WorkBuildingId = dome;
        Assert.Equal(10, Pct(e, s, gw));
        Put(e, s, "bp_ore_mine", 13, 8);
        Put(e, s, "bp_ore_mine", 11, 9);
        Put(e, s, "bp_ore_mine", 12, 9);
        Assert.Equal(30, Pct(e, s, gw)); // 30 own + 10 aura, capped at 30
    }

    [Fact]
    public void Skill_TouchPlaza_AndHousingNeighbours()
    {
        var (e, s) = Fresh();
        UnlockAll(e, s);
        string spire = Put(e, s, "bp_command_spire", 11, 8);
        Assert.Equal(30, Pct(e, s, spire));
        string pen = Put(e, s, "bp_breeding_pen", 6, 9, true);  // (6, 9..10)
        Assert.Equal(0, Pct(e, s, pen));
        Put(e, s, ContentDb.LaunchPodId, 5, 9);
        Assert.Equal(20, Pct(e, s, pen));
        Assert.Equal(0, Pct(e, s, CitySystem.Copies(s, ContentDb.LaunchPodId)[0].Uid)); // no placement skill
    }

    [Fact]
    public void Cache_RebuildsOnLayoutChange()
    {
        var (e, s) = Fresh();
        long t = BuildAt(e, ref s, Farm, 11, 9, T0, out var a);
        int rev = s.City.LayoutRev;
        Assert.Equal(0, Pct(e, s, a));
        BuildAt(e, ref s, Farm, 11, 10, t, out _);
        Assert.True(s.City.LayoutRev > rev);
        Assert.Equal(10, Pct(e, s, a));
    }

    // ---- digging ----

    [Fact]
    public void Dig_AnyOrder_MustTouchLand_CostByDigNumber()
    {
        var (e, s) = Fresh();
        long bio = s.Wallet.Get(Currencies.Biomass), ore = s.Wallet.Get(Currencies.Ore);
        Assert.Equal("must touch your land", Fail(e, ref s, new CityDig { PatchId = "patch_t" }, T0));
        Assert.Equal("unknown patch", Fail(e, ref s, new CityDig { PatchId = "patch_z" }, T0));
        Ok(e, ref s, new CityDig { PatchId = "patch_e" }, T0);
        Assert.Equal(bio - 2500, s.Wallet.Get(Currencies.Biomass));
        Assert.Equal(ore - 750, s.Wallet.Get(Currencies.Ore));
        Assert.Equal("a dig is running", Fail(e, ref s, new CityDig { PatchId = "patch_b" }, T0));
        Assert.Equal("already digging", Fail(e, ref s, new CityDig { PatchId = "patch_e" }, T0));
        Assert.False(CityLayout.IsLand(s, e.Content, e.Content.CityPatch("patch_e").Cells[0]));
        e.Settle(s, T0 + Min);
        Assert.Equal(new[] { "patch_e" }, s.City.DugPatches);
        Assert.True(CityLayout.IsLand(s, e.Content, e.Content.CityPatch("patch_e").Cells[0]));
        Assert.Equal(1, s.Counter("city_digs"));
        Assert.Equal("already dug", Fail(e, ref s, new CityDig { PatchId = "patch_e" }, T0 + Min));
        Assert.Equal(2, CitySystem.NextDigNumber(s));
        bio = s.Wallet.Get(Currencies.Biomass);
        Ok(e, ref s, new CityDig { PatchId = "patch_b" }, T0 + Min);
        Assert.Equal(bio - 4000, s.Wallet.Get(Currencies.Biomass));
        var timer = TimerSystem.Get(s, s.City.DigTimerId);
        Assert.Equal(5 * Min, timer.DurationMs);
        // Speed-ups work on the dig timer like any city timer.
        Ok(e, ref s, new TimerSpeedUp { TimerId = timer.Id, Method = SpeedUpMethod.Diamonds }, T0 + Min);
        e.Settle(s, T0 + Min + 1);
        Assert.Contains("patch_b", s.City.DugPatches);
        // Gear Cores from dig 10 on.
        Assert.Empty(CitySystem.DigCost(e.Content, 9).Where(c => c.Currency == Currencies.GearCores));
        Assert.Equal(500, CitySystem.DigCost(e.Content, 10).Single(c => c.Currency == Currencies.GearCores).Amount);
        Assert.Empty(CitySystem.DigCost(e.Content, 21));
    }

    [Fact]
    public void Dig_AllTwentyPatches_ReachableFromTheStart()
    {
        var (e, s) = Fresh();
        s.City.FreeDigs = 20;
        for (int n = 0; n < 20; n++)
        {
            var next = e.Content.CityPatches.First(p => CitySystem.DigBlockReason(s, e.Content, p.Id) == null);
            Ok(e, ref s, new CityDig { PatchId = next.Id }, T0);
        }
        Assert.Equal(20, s.City.DugPatches.Count);
        Assert.Equal(240, Enumerable.Range(0, 400).Count(c => CityLayout.IsLand(s, e.Content, c)));
        Assert.Equal("already dug", CitySystem.DigBlockReason(s, e.Content, "patch_a"));
        Assert.Null(CitySystem.DigDef(e.Content, 21));
    }

    [Fact]
    public void Planner_FindsAValidSpot_PavingStreetsWhenNeeded()
    {
        var (e, s) = Fresh();
        UnlockAll(e, s);
        long t = T0;
        foreach (var bp in new[] { "bp_command_spire", "bp_laboratory", "bp_shard_reactor", "bp_barracks_dome", "bp_scout_post", "bp_war_academy" })
        {
            t = BuildActive(e, ref s, bp, t);
            Assert.True(CityLayout.Check(CityLayout.Grid(s, e.Content)).Ok);
        }
        Assert.Equal(30, Pct(e, s, CitySystem.Copies(s, "bp_command_spire")[0].Uid));
    }
}
