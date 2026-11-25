using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.City.Kit;

namespace RogueStars.Rules.Tests.City;

/// <summary>City buildings: content, growth ladders, costs, production, workers, store / move, boons, squad, the redesign reset.</summary>
public class CityTests
{
    private const long T0 = TestHelpers.T0;
    private const string Farm = "bp_biomass_farm";
    private const string Worker = "alien_mossling";

    /// <summary>
    /// Production-math fixture: the Biomass Farm pinned to 60/h and the Mossling's work power to 0 (a neutral worker) so these tests
    /// check the mechanics, not the current tuning.
    /// </summary>
    private static readonly ContentDb Fixture = MakeFixture();

    private static ContentDb MakeFixture()
    {
        var c = new ContentDb();
        c.Onboarding.StarterKit.Clear();
        c.Blueprints[Farm].BaseRatePerHour = 60;
        c.AlienType(Worker).BaseWork = 0;
        return c;
    }

    private static GameEngine Engine() => new GameEngine(Fixture) { Cloner = TestHelpers.Clone };

    /// <summary>A farm next to the plaza (11,9), staffed by a neutral worker.</summary>
    private static (GameEngine e, GameState s, long t, string uid) FarmReady()
    {
        var e = Engine();
        var s = Rich(e);
        long t = BuildAt(e, ref s, Farm, 11, 9, T0, out var uid);
        GrantAlien(e, s, Worker);
        Neutral(s, Worker);
        Ok(e, ref s, new AlienAssign { AlienTypeId = Worker, BuildingUid = uid }, t);
        return (e, s, t, uid);
    }

    private static double Stored(GameState s, string uid) => CitySystem.Building(s, uid).Stored;

    // ---- content ----

    [Fact]
    public void Content_TwentyFiveBuildings_FootprintAndCopiesByRarity()
    {
        var c = new ContentDb();
        var ids = new[]
        {
            "bp_biomass_farm", "bp_grain_farm", "bp_ore_mine", "bp_fuel_refinery", "bp_gear_works", "bp_training_grounds", "bp_plate_foundry",
            "bp_diamond_mine", "bp_token_relay", "bp_signal_beacon", "bp_part_fabricator", "bp_launch_pod", "bp_barracks_dome", "bp_command_spire",
            "bp_breeding_pen", "bp_gene_lab", "bp_laboratory", "bp_observatory", "bp_shard_reactor", "bp_stamina_well", "bp_regen_spire",
            "bp_supply_depot", "bp_scout_post", "bp_war_academy", "bp_stasis_beacon",
        };
        Assert.Equal(ids.OrderBy(x => x, StringComparer.Ordinal), c.Blueprints.Keys.OrderBy(x => x, StringComparer.Ordinal));
        int[] copies = { 3, 3, 2, 2, 1 };
        (int w, int h)[] foot = { (1, 1), (2, 1), (2, 2), (3, 2), (3, 3) };
        foreach (var d in c.Blueprints.Values)
        {
            Assert.Equal(d.Id == ContentDb.LaunchPodId ? 1 : copies[(int)d.Rarity], d.Copies);
            Assert.Equal(foot[(int)d.Rarity], (d.Width, d.Height));
            bool noSkill = d.Id == ContentDb.LaunchPodId || d.Id == ContentDb.LaboratoryId || d.Id == ContentDb.ShardReactorId;
            Assert.Equal(noSkill, d.Placement == null);
        }
        Assert.Equal(8, c.Blueprints.Values.Count(d => d.CostType == BuildingCostType.Biomass));
        Assert.Equal(7, c.Blueprints.Values.Count(d => d.CostType == BuildingCostType.Ore));
        Assert.Equal(10, c.Blueprints.Values.Count(d => d.CostType == BuildingCostType.Both));
        Assert.Equal(Rarity.Mythic, c.Blueprint(ContentDb.LaboratoryId).Rarity);
        Assert.Equal(Rarity.Legendary, c.Blueprint("bp_scout_post").Rarity);
        Assert.Equal(Rarity.Legendary, c.Blueprint("bp_war_academy").Rarity);
        Assert.Equal(3, c.Blueprints.Values.Count(d => d.Pull != null));
        // Squad slots come from the Launch Pod only.
        foreach (var d in c.Blueprints.Values)
            if (d.Id != ContentDb.LaunchPodId) Assert.DoesNotContain(d.Milestones, m => m.Kind == BoonKind.SquadSlots);
    }

    [Fact]
    public void Content_LandPatchesFeaturesAndDigs()
    {
        var c = new ContentDb();
        var t = c.CityTuning;
        Assert.Equal((20, 20), (t.GridWidth, t.GridHeight));
        Assert.Equal(30, t.StartCells.Count);
        for (int y = t.PlazaY; y < t.PlazaY + 3; y++)
            for (int x = t.PlazaX; x < t.PlazaX + 3; x++) Assert.Contains(y * 20 + x, t.StartCells);
        Assert.Equal(20, c.CityPatches.Count);
        Assert.Equal(210, c.CityPatches.Sum(p => p.Cells.Count));
        Assert.Equal(new[] { 7, 7, 8, 8, 8, 9, 9, 9, 10, 10, 10, 11, 11, 11, 12, 12, 13, 14, 15, 16 }, c.CityPatches.Select(p => p.Cells.Count));
        var features = c.CityPatches.SelectMany(p => p.Features.Select(f => (p, f))).ToList();
        Assert.Equal(10, features.Count);
        Assert.Equal(4, features.Count(x => x.f.Kind == "crystal"));
        Assert.Equal(4, features.Count(x => x.f.Kind == "spring"));
        Assert.Equal(2, features.Count(x => x.f.Kind == "ruin"));
        foreach (var (p, f) in features) Assert.Contains(f.Y * 20 + f.X, p.Cells);
        Assert.Equal(20, t.Digs.Count);
        Assert.Equal((2500, 750, 0L, 60_000L), (t.Digs[0].Biomass, t.Digs[0].Ore, t.Digs[0].GearCores, t.Digs[0].Ms));
        Assert.Equal((600000, 150000, 12000L, 24 * Hour), (t.Digs[19].Biomass, t.Digs[19].Ore, t.Digs[19].GearCores, t.Digs[19].Ms));
        Assert.Equal(3_002_500, t.Digs.Sum(d => d.Biomass)); // ~3.0M (City design §4)
        Assert.All(t.Digs.Take(9), d => Assert.Equal(0, d.GearCores));
    }

    // ---- growth and costs ----

    [Fact]
    public void Ladders_TenPercentPerStep_FifteenAtFiveAndTen_ThreeTimesAtMax()
    {
        var c = new ContentDb();
        double[] want = { 0, 0, 0.1, 0.2, 0.3, 0.45, 0.55, 0.65, 0.75, 0.85, 1.0 };
        for (int n = 1; n <= 10; n++) Assert.Equal(want[n], CitySystem.Ladder(c, n), 9);
        Assert.Equal(3, CitySystem.Growth(c, null, 10, 10), 9);
        Assert.Equal(2, CitySystem.Growth(c, new BuildingBoon { GrowsWithStars = false }, 10, 10), 9);
    }

    [Fact]
    public void Costs_ByCostType_AndLevelCurve()
    {
        var c = new ContentDb();
        long[] Up(string id, int level) => CitySystem.UpgradeCost(c, c.Blueprint(id), level).Select(x => x.Amount).ToArray();
        // First level-up by rarity (Biomass only / Ore only / both): City design §6.
        Assert.Equal(new long[] { 150 }, Up("bp_biomass_farm", 1));
        Assert.Equal(new long[] { 45 }, Up("bp_ore_mine", 1));
        Assert.Equal(new long[] { 50, 30 }, Up("bp_supply_depot", 1));
        Assert.Equal(new long[] { 600 }, Up("bp_stamina_well", 1));
        Assert.Equal(new long[] { 180 }, Up("bp_gear_works", 1));
        Assert.Equal(new long[] { 200, 120 }, Up("bp_observatory", 1));
        Assert.Equal(new long[] { 2400 }, Up("bp_regen_spire", 1));
        Assert.Equal(new long[] { 750 }, Up("bp_diamond_mine", 1));
        Assert.Equal(new long[] { 2800, 1800 }, Up("bp_scout_post", 1));
        Assert.Equal(new long[] { 2700 }, Up("bp_war_academy", 1));
        Assert.Equal(new long[] { 27000 }, CitySystem.UpgradeCost(c, new BlueprintDef { Rarity = Rarity.Mythic, CostType = BuildingCostType.Biomass }, 1).Select(x => x.Amount));
        Assert.Equal(new long[] { 9000 }, CitySystem.UpgradeCost(c, new BlueprintDef { Rarity = Rarity.Mythic, CostType = BuildingCostType.Ore }, 1).Select(x => x.Amount));
        Assert.Equal(new long[] { 9000, 6000 }, Up("bp_laboratory", 1));
        // Curve: ×1, 2, 3, 5, 9, 15, 25, 45, 80 of the first cost (9 level-ups, 185× in all), clean numbers.
        Assert.Equal(new long[] { 150, 300, 450, 750, 1400, 2300, 3800, 6800, 12000 }, Enumerable.Range(1, 9).Select(l => Up("bp_biomass_farm", l)[0]));
        Assert.Equal(new long[] { 220000, 140000 }, Up("bp_scout_post", 9));
        // Build: Common both 60 + 40; Biomass only 3×; Ore only 1.5×.
        Assert.Equal(new long[] { 180 }, CitySystem.BuildCost(c, c.Blueprint(Farm)).Select(x => x.Amount));
        Assert.Equal(new long[] { 60 }, CitySystem.BuildCost(c, c.Blueprint("bp_ore_mine")).Select(x => x.Amount));
        Assert.Equal(new long[] { 15000, 10000 }, CitySystem.BuildCost(c, c.Blueprint(ContentDb.LaboratoryId)).Select(x => x.Amount));
        Assert.Equal(new long[] { 70, 65, 70, 680, 1200 }, new[] { 67.5, 65, 68, 683, 1234 }.Select(CitySystem.NiceRound));
    }

    // ---- build, production ----

    [Fact]
    public void Place_NewCopy_SpendsCost_RunsTimer_RunsOnlyWhenStaffed()
    {
        var e = Engine();
        var s = Broke(e);
        s.Wallet.Add(Currencies.Biomass, 180);
        GrantBlueprint(e, s, Farm);
        string uid = (string)Ok(e, ref s, new CityPlace { BlueprintId = Farm, X = 11, Y = 9 }, T0).Payload;
        Assert.Equal("bp_biomass_farm#1", uid);
        Assert.Equal(0, s.Wallet.Get(Currencies.Biomass));
        var b = CitySystem.Building(s, uid);
        Assert.True(b.Placed);
        Assert.Equal(0, b.Level);
        e.Settle(s, T0 + Min);
        Assert.Equal(1, CitySystem.Building(s, uid).Level);
        Assert.Equal(1, s.Counter("city_buildings_built"));
        Assert.False(CitySystem.IsActive(s, e.Content, CitySystem.Building(s, uid)));
        e.Settle(s, T0 + Hour);
        Assert.Equal(0, Stored(s, uid)); // no alien, no production
        GrantAlien(e, s, Worker);
        Ok(e, ref s, new AlienAssign { AlienTypeId = Worker, BuildingUid = uid }, T0 + Hour);
        Assert.True(CitySystem.IsActive(s, e.Content, CitySystem.Building(s, uid)));
    }

    [Fact]
    public void Production_SettlesFromTimestamps_AndCollectKeepsRemainder()
    {
        var (e, s, t, uid) = FarmReady();
        e.Settle(s, t + 90_000); // 1.5 minutes at 60/h
        Assert.Equal(1.5, Stored(s, uid), 6);
        var r = Ok(e, ref s, new CityCollect { Uid = uid }, t + 90_000);
        Assert.Equal(1, ((Dictionary<string, long>)r.Payload)[Currencies.Biomass]);
        Assert.Equal(0.5, Stored(s, uid), 6);
        e.Settle(s, t + 2 * Hour);
        Assert.Equal(0.5 + 118.5, Stored(s, uid), 6);
        long before = s.Wallet.Get(Currencies.Biomass);
        Ok(e, ref s, new CityCollect(), t + 2 * Hour);
        Assert.Equal(before + 119, s.Wallet.Get(Currencies.Biomass));
    }

    [Fact]
    public void Production_StorageIsEightHours_GrowsWithLadders()
    {
        var (e, s, t, uid) = FarmReady();
        var b = CitySystem.Building(s, uid);
        Assert.Equal(480, CitySystem.StorageCap(s, e.Content, b), 6);
        e.Settle(s, t + 20 * Hour);
        Assert.Equal(480, Stored(s, uid), 6);
        b.Level = 5;
        CitySystem.Owned(s, Farm).Stars = 10;
        Assert.Equal(480 * (1 + 0.45 + 1.0), CitySystem.StorageCap(s, e.Content, b), 6);
        Assert.Equal(60 * 2.45, CitySystem.BaseRatePerHour(s, e.Content, b), 6);
    }

    [Fact]
    public void Production_WorkersAddTheirBonus_TraitsByCurrency()
    {
        var (e, s, t, uid) = FarmReady();
        Ok(e, ref s, new AlienAssign { AlienTypeId = Worker }, t);
        GrantAlien(e, s, "alien_zib");
        var zib = Neutral(s, "alien_zib");
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_zib", BuildingUid = uid }, t);
        double work = AlienSystem.WorkPower(e.Content, zib);
        Assert.True(work > 0);
        Assert.Equal(60 * (1 + work / 100), CitySystem.BaseRatePerHour(s, e.Content, CitySystem.Building(s, uid)), 6);
        zib.Traits.Add("trait_green_thumb"); // +20% biomass/grain
        zib.Traits.Add("trait_deep_digger"); // ore only: no effect here
        Assert.Equal(work + 20, AlienSystem.WorkBonusPct(s, e.Content, zib, Currencies.Biomass), 6);
        Assert.Equal(work, AlienSystem.WorkBonusPct(s, e.Content, zib, Currencies.Fuel), 6);
    }

    [Fact]
    public void Production_PriorityBoostsOneCurrency_PenalizesOthers()
    {
        var (e, s, t, uid) = FarmReady();
        t = BuildAt(e, ref s, "bp_grain_farm", 7, 9, t, out var grain); // 3 open tiles: Open Ground +20%
        GrantAlien(e, s, "alien_pebblehorn");
        Neutral(s, "alien_pebblehorn");
        double work = AlienSystem.WorkBonusPct(s, e.Content, AlienSystem.Owned(s, "alien_pebblehorn"), Currencies.Grain);
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_pebblehorn", BuildingUid = grain }, t);
        Ok(e, ref s, new CityCollect(), t);
        CitySystem.Building(s, uid).Stored = 0;
        CitySystem.Building(s, grain).Stored = 0;
        Ok(e, ref s, new CitySetPriority { Currency = Currencies.Biomass }, t);
        e.Settle(s, t + 2 * Hour);
        Assert.Equal(120 * 1.3, Stored(s, uid), 6);
        Assert.Equal(80 * 1.2 * (1 + work / 100) * 0.9, Stored(s, grain), 6);
        Assert.Equal("currency is not produced in the city", Fail(e, ref s, new CitySetPriority { Currency = Currencies.Stamina }, t + 2 * Hour));
        Ok(e, ref s, new CitySetPriority { Currency = null }, t + 2 * Hour);
        Assert.Null(s.City.PriorityCurrency);
    }

    [Fact]
    public void Production_ModifiersIntegratePiecewiseOverTheirWindow()
    {
        var (e, s, t, uid) = FarmReady();
        s.Modifiers.Add(new Modifier { Target = "production:biomass", Multiplier = 2, StartMs = t + Hour, EndMs = t + 2 * Hour });
        s.Modifiers.Add(new Modifier { Target = "production:*", Multiplier = 0.5, StartMs = t + 2 * Hour + 30 * Min, EndMs = 0 });
        s.Modifiers.Add(new Modifier { Target = "production:ore", Multiplier = 10, StartMs = 0, EndMs = 0 }); // other currency
        e.Settle(s, t + 3 * Hour);
        Assert.Equal(60 + 120 + 30 + 15, Stored(s, uid), 6);
        Assert.Equal(30, CitySystem.ProductionPerHour(s, e.Content, CitySystem.Building(s, uid), t + 3 * Hour), 6);
    }

    [Fact]
    public void Upgrade_SpendsCost_CompletesAtExactTime_TenLevels()
    {
        var (e, s, t, uid) = FarmReady();
        long bio = s.Wallet.Get(Currencies.Biomass);
        Ok(e, ref s, new CityUpgrade { Uid = uid }, t);
        Assert.Equal(bio - 150, s.Wallet.Get(Currencies.Biomass));
        Assert.Equal("upgrade already running", Fail(e, ref s, new CityUpgrade { Uid = uid }, t));
        Assert.Equal("cannot store while upgrading", Fail(e, ref s, new CityStore { Uid = uid }, t));
        e.Settle(s, t + 2 * Min + Hour); // 2 min at 60/h, then 1h at 66/h (+10%)
        Assert.Equal(2, CitySystem.Building(s, uid).Level);
        Assert.Equal(2 + 66, Stored(s, uid), 6);
        Assert.Equal(1, s.Counter("city_building_upgrades"));
        CitySystem.Building(s, uid).Level = 10;
        Assert.Equal("max level", Fail(e, ref s, new CityUpgrade { Uid = uid }, t + Hour * 2));
    }

    // ---- store, move, copies ----

    [Fact]
    public void Store_KeepsLevel_CollectsAndUnassigns_ReplaceIsFreeAndInstant()
    {
        var (e, s, t, uid) = FarmReady();
        CitySystem.Building(s, uid).Level = 4;
        e.Settle(s, t + Hour);
        long bio = s.Wallet.Get(Currencies.Biomass);
        Ok(e, ref s, new CityStore { Uid = uid }, t + Hour);
        Assert.Equal(bio + 78, s.Wallet.Get(Currencies.Biomass)); // collected (rate at level 4: 60 × 1.3)
        var b = CitySystem.Building(s, uid);
        Assert.False(b.Placed);
        Assert.Equal(4, b.Level);
        Assert.Null(AlienSystem.Owned(s, Worker).WorkBuildingId);
        Assert.Equal("building not placed", Fail(e, ref s, new CityStore { Uid = uid }, t + Hour));
        bio = s.Wallet.Get(Currencies.Biomass);
        Ok(e, ref s, new CityPlace { BlueprintId = Farm, Uid = uid, X = 7, Y = 9 }, t + Hour);
        Assert.Equal(bio, s.Wallet.Get(Currencies.Biomass));
        Assert.True(CitySystem.IsBuilt(CitySystem.Building(s, uid)));
        Assert.Equal("already placed", Fail(e, ref s, new CityPlace { BlueprintId = Farm, Uid = uid, X = 9, Y = 7 }, t + Hour));
    }

    [Fact]
    public void Store_OfANewBuildUnderConstruction_CancelsAndRefunds()
    {
        var e = Engine();
        var s = Broke(e);
        s.Wallet.Add(Currencies.Biomass, 180);
        GrantBlueprint(e, s, Farm);
        string uid = (string)Ok(e, ref s, new CityPlace { BlueprintId = Farm, X = 11, Y = 9 }, T0).Payload;
        Ok(e, ref s, new CityStore { Uid = uid }, T0 + 1000);
        Assert.Equal(180, s.Wallet.Get(Currencies.Biomass));
        Assert.Empty(s.City.Buildings);
        Assert.DoesNotContain(s.Timers.Active, x => x.Kind == CitySystem.TimerBuild);
    }

    [Fact]
    public void Move_KeepsEverything_ValidatesIgnoringItself()
    {
        var (e, s, t, uid) = FarmReady();
        Ok(e, ref s, new CityMove { Uid = uid, X = 11, Y = 10 }, t);
        var b = CitySystem.Building(s, uid);
        Assert.Equal((11, 10), (b.X, b.Y));
        Assert.Equal(b.Uid, AlienSystem.Owned(s, Worker).WorkBuildingId);
        Assert.Equal("not dug land", Fail(e, ref s, new CityMove { Uid = uid, X = 12, Y = 10 }, t));
    }

    [Fact]
    public void Copies_CappedByRarity_EachCopyAddsUp()
    {
        var e = Engine();
        var s = Rich(e);
        UnlockAll(e, s);
        long t = T0;
        var uids = new List<string>();
        for (int i = 0; i < 3; i++) { t = BuildActive(e, ref s, "bp_stamina_well", t, out var u); uids.Add(u); }
        Assert.Equal(new[] { "bp_stamina_well#1", "bp_stamina_well#2", "bp_stamina_well#3" }, uids);
        Assert.Equal("3/3 built", Fail(e, ref s, new CityPlace { BlueprintId = "bp_stamina_well", X = 1, Y = 13 }, t));
        Assert.Equal(0, CitySystem.StaminaBonus(s, e.Content, t).CapBonus); // unstaffed: no boon
        var aliens = new[] { "alien_zib", "alien_blorp", Worker };
        double sum = 0;
        for (int i = 0; i < 3; i++)
        {
            Staff(e, ref s, uids[i], aliens[i], t);
            sum += Math.Floor(5 * (1 + CityLayout.PlacementPct(s, e.Content, uids[i]) / 100.0) + 1e-9);
        }
        Assert.Equal((long)sum, CitySystem.StaminaBonus(s, e.Content, t).CapBonus);
        Assert.True(sum >= 15);
    }

    // ---- blueprints ----

    [Fact]
    public void BlueprintGranter_FirstCopyOwned_DupesBecomeShards()
    {
        var e = Engine();
        var s = TestHelpers.NewPlayer(e);
        GrantBlueprint(e, s, Farm);
        Assert.Equal(1, CitySystem.Owned(s, Farm).Stars);
        GrantBlueprint(e, s, Farm, 3);
        Assert.Single(s.City.Blueprints);
        Assert.Equal(150, s.Wallet.Get(Currencies.BlueprintShards(Farm)));
        Assert.Throws<RuleException>(() => GrantBlueprint(e, s, "bp_nope"));
    }

    [Fact]
    public void BlueprintStars_OneToTen_SharedByCopies_WorkerSlotAtFive()
    {
        var e = Engine();
        var s = TestHelpers.NewPlayer(e);
        long total = (e.Content.ShardStars.TotalTo(10) - e.Content.ShardStars.UnlockShards) / e.Content.ShardStars.BatchShards;
        GrantBlueprint(e, s, Farm, 1 + total);
        for (int star = 2; star <= 10; star++) Ok(e, ref s, new BlueprintStarUp { BlueprintId = Farm }, T0);
        Assert.Equal(10, CitySystem.Owned(s, Farm).Stars);
        Assert.Equal("max stars", Fail(e, ref s, new BlueprintStarUp { BlueprintId = Farm }, T0));
        var def = e.Content.Blueprint(Farm);
        Assert.Equal(1, CitySystem.WorkerSlots(def, 4));
        Assert.Equal(2, CitySystem.WorkerSlots(def, 5));
        Assert.Equal(3, CitySystem.WorkerSlots(e.Content.Blueprint("bp_fuel_refinery"), 5));
        Assert.Equal(1, CitySystem.WorkerSlots(e.Content.Blueprint(ContentDb.ShardReactorId), 4));
        Assert.Equal(2, CitySystem.WorkerSlots(e.Content.Blueprint(ContentDb.ShardReactorId), 5));
        Assert.Equal(3, CitySystem.WorkerSlots(e.Content.Blueprint(ContentDb.ShardReactorId), 10));
    }

    // ---- boons ----

    [Fact]
    public void BoonBuildings_ReachLoadout_ScaledAndWhole()
    {
        var e = Engine();
        var s = Rich(e);
        UnlockAll(e, s);
        long t = BuildActive(e, ref s, "bp_supply_depot", T0, out var depot);
        t = BuildActive(e, ref s, "bp_war_academy", t, out var academy);
        t = BuildActive(e, ref s, "bp_stasis_beacon", t, out var stasis);
        t = BuildActive(e, ref s, "bp_observatory", t, out var obs);
        var l0 = e.BuildLoadout(s, t);
        Assert.Equal(0, l0.RunStartCoins); // unstaffed
        Staff(e, ref s, depot, "alien_zib", t);
        Staff(e, ref s, academy, "alien_blorp", t);
        Staff(e, ref s, stasis, Worker, t);
        Staff(e, ref s, obs, "alien_pebblehorn", t);
        CitySystem.Building(s, depot).Level = 5; // +45%
        CitySystem.Owned(s, "bp_war_academy").Stars = 5; // 1 × 1.45 → 1
        var l = e.BuildLoadout(s, t);
        double depotPct = CityLayout.PlacementPct(s, e.Content, depot);
        Assert.Equal((int)Math.Floor(20 * 1.45 * (1 + depotPct / 100) + 1e-9), l.RunStartCoins);
        Assert.Equal(1 + (int)CityLayout.PlacementFlat(s, e.Content, academy), CitySystem.DraftRerolls(s, e.Content));
        Assert.Equal(10 * (1 + CityLayout.PlacementPct(s, e.Content, stasis) / 100), l.AwakeningSlowPct, 6);
        CitySystem.Owned(s, "bp_war_academy").Stars = 10; // 1 × 2 → 2
        Assert.Equal(2 + (int)CityLayout.PlacementFlat(s, e.Content, academy), CitySystem.DraftRerolls(s, e.Content));
    }

    [Fact]
    public void CommandSpire_RaisesEveryProducer_PlazaBonus()
    {
        var (e, s, t, uid) = FarmReady();
        UnlockAll(e, s);
        Streets(e, ref s, t, (11, 8));
        t = BuildAt(e, ref s, "bp_command_spire", 12, 6, t, out var spire);     // (12..14, 6..8): on the street, not at the plaza
        Staff(e, ref s, spire, "alien_zib", t);
        Assert.Equal(0, CityLayout.PlacementPct(s, e.Content, spire));
        Assert.Equal(60 * 1.05, CitySystem.BaseRatePerHour(s, e.Content, CitySystem.Building(s, uid)), 6);
        Ok(e, ref s, new CityMove { Uid = uid, X = 7, Y = 9 }, t);
        Ok(e, ref s, new CityMove { Uid = spire, X = 11, Y = 9 }, t);           // (11..13, 9..11): touches the plaza
        Assert.Equal(30, CityLayout.PlacementPct(s, e.Content, spire));
        Assert.Equal(60 * 1.065, CitySystem.BaseRatePerHour(s, e.Content, CitySystem.Building(s, uid)), 6);
    }

    // ---- squad ----

    [Fact]
    public void LaunchPod_IsTheSquad_SlotsAtFiveAndTenStars()
    {
        var e = Engine();
        var s = Rich(e);
        GrantAlien(e, s, "alien_zib");
        GrantAlien(e, s, "alien_blorp");
        GrantAlien(e, s, Worker);
        Assert.Equal(0, CitySystem.SquadSlots(s, e.Content));
        Assert.Equal("build the Launch Pod first", Fail(e, ref s, new AlienSetSquad { AlienTypeIds = { "alien_zib" } }, T0));
        long t = BuildAt(e, ref s, ContentDb.LaunchPodId, 11, 9, T0, out var pod);
        Assert.Equal(1, CitySystem.SquadSlots(s, e.Content));
        Ok(e, ref s, new AlienSetSquad { AlienTypeIds = { "alien_zib" } }, t);
        Assert.Equal(pod, AlienSystem.Owned(s, "alien_zib").WorkBuildingId);
        Assert.Equal(new[] { "alien_zib" }, e.BuildLoadout(s, t).Aliens.Select(a => a.AlienTypeId));
        Assert.Equal("no free worker slot", Fail(e, ref s, new AlienAssign { AlienTypeId = "alien_blorp", BuildingUid = pod }, t));
        CitySystem.Owned(s, ContentDb.LaunchPodId).Stars = 5;
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_blorp", BuildingUid = pod }, t);
        Assert.Equal(new[] { "alien_zib", "alien_blorp" }, AlienSystem.ActiveSquad(s, e.Content));
        CitySystem.Owned(s, ContentDb.LaunchPodId).Stars = 10;
        Assert.Equal(3, CitySystem.SquadSlots(s, e.Content));
        // Moving a squad alien to work elsewhere takes it out of the squad.
        t = BuildAt(e, ref s, Farm, 7, 9, t, out var farm);
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_zib", BuildingUid = farm }, t);
        Assert.Equal(new[] { "alien_blorp" }, AlienSystem.ActiveSquad(s, e.Content));
        // Launch Pod: squad alien damage +10% (levels only).
        Assert.Equal(10, e.BuildLoadout(s, t).WormStats.Value(StatId.AlienPower), 6);
        CitySystem.Building(s, pod).Level = 10;
        Assert.Equal(20, e.BuildLoadout(s, t).WormStats.Value(StatId.AlienPower), 6);
        Ok(e, ref s, new CityStore { Uid = pod }, t);
        Assert.Empty(AlienSystem.ActiveSquad(s, e.Content));
    }

    // ---- the redesign reset ----

    [Fact]
    public void Reset_OldSave_StoresBuildingsWithLevel_FreeDigPerArea()
    {
        var e = Engine();
        var s = Rich(e);
        s.City.Version = 0;
        s.City.UnlockedAreas.AddRange(new[] { "area_core", "area_north", "area_east", "area_south" });
        s.City.Blueprints[Farm] = new OwnedBlueprint { Id = Farm, Stars = 3, Level = 14 };
        s.City.Blueprints["bp_housing_pod"] = new OwnedBlueprint { Id = "bp_housing_pod", Stars = 2, Level = 3 };
        s.City.Blueprints["bp_spore_vats"] = new OwnedBlueprint { Id = "bp_spore_vats", Stars = 1, Level = 5 };
        s.City.Blueprints["bp_gene_lab"] = new OwnedBlueprint { Id = "bp_gene_lab", Stars = 1, Level = 0 }; // owned, never built
        GrantAlien(e, s, "alien_zib");
        AlienSystem.Owned(s, "alien_zib").WorkBuildingId = Farm;
        e.Settle(s, T0 + 1000);
        Assert.Equal(CitySystem.LayoutVersion, s.City.Version);
        Assert.Equal(3, s.City.FreeDigs);
        Assert.Null(CitySystem.Owned(s, "bp_spore_vats"));
        Assert.Equal(2, CitySystem.Owned(s, ContentDb.LaunchPodId).Stars);
        var farm = Assert.Single(CitySystem.Copies(s, Farm));
        Assert.Equal((false, 10), (farm.Placed, farm.Level));
        Assert.Equal(3, CitySystem.Copies(s, ContentDb.LaunchPodId)[0].Level);
        Assert.Empty(CitySystem.Copies(s, "bp_gene_lab"));
        Assert.Null(AlienSystem.Owned(s, "alien_zib").WorkBuildingId);
        // Free digs are instant and cost nothing.
        long bio = s.Wallet.Get(Currencies.Biomass);
        Ok(e, ref s, new CityDig { PatchId = "patch_b" }, T0 + 2000);
        Assert.Contains("patch_b", s.City.DugPatches);
        Assert.Equal(bio, s.Wallet.Get(Currencies.Biomass));
        Assert.Equal(2, s.City.FreeDigs);
    }

    [Fact]
    public void State_SurvivesJsonRoundTrip()
    {
        var (e, s, t, uid) = FarmReady();
        Streets(e, ref s, t, (11, 8));
        var c = TestHelpers.Clone(s);
        Assert.Equal(1, CitySystem.Building(c, uid).Level);
        Assert.Equal(s.City.Streets, c.City.Streets);
        Assert.Equal(uid, AlienSystem.Owned(c, Worker).WorkBuildingId);
    }
}
