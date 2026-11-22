using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.City.Kit;

namespace RogueStars.Rules.Tests.City;

public class CityTests
{
    private const long T0 = TestHelpers.T0;
    private const string Farm = "bp_biomass_farm";

    private static (GameEngine e, GameState s, long t) FarmReady()
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        long t = BuildActive(e, ref s, Farm, 4, 4, T0);
        return (e, s, t);
    }

    private static double Stored(GameState s, string id) => CitySystem.Owned(s, id).Stored;

    // ---- content sanity ----

    [Fact]
    public void Content_HasBlueprintsAreasAndValidSkills()
    {
        var c = TestHelpers.Engine().Content;
        Assert.True(c.Blueprints.Count >= 24);
        Assert.Equal(9, c.CityAreas.Count);
        foreach (var bp in c.Blueprints.Values)
        {
            foreach (var boon in bp.Boons) foreach (var tier in boon.SkillTiers)
                Assert.Equal(SkillSource.Building, c.Skill(tier.SkillId).Source);
            foreach (var m in bp.Milestones) if (m.SkillId != null) Assert.NotNull(c.Skill(m.SkillId));
        }
        foreach (Rarity r in Enum.GetValues(typeof(Rarity)))
            Assert.Contains(c.Blueprints.Values, b => b.Rarity == r);
    }

    // ---- areas & placement ----

    [Fact]
    public void Areas_UnlockOneByOne_WithRequirementsAndCosts()
    {
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        Assert.Equal(new[] { "area_core" }, s.City.UnlockedAreas);
        Assert.Equal("areas unlock in order", Fail(e, ref s, new CityUnlockArea { AreaId = "area_east" }, T0));
        Assert.Equal("not enough resources", Fail(e, ref s, new CityUnlockArea { AreaId = "area_north" }, T0));
        s.Wallet.Add(Currencies.Biomass, 300); s.Wallet.Add(Currencies.Ore, 150);
        Ok(e, ref s, new CityUnlockArea { AreaId = "area_north" }, T0);
        Assert.Equal(0, s.Wallet.Get(Currencies.Biomass));
        Assert.Equal(1, s.Counter("city_areas_unlocked"));
        s.Wallet.Add(Currencies.Biomass, 100_000); s.Wallet.Add(Currencies.Ore, 100_000);
        Assert.Equal("area requirement not met", Fail(e, ref s, new CityUnlockArea { AreaId = "area_east" }, T0));
        s.Count("city_buildings_built", 3);
        Ok(e, ref s, new CityUnlockArea { AreaId = "area_east" }, T0);
        Assert.Equal("area already unlocked", Fail(e, ref s, new CityUnlockArea { AreaId = "area_east" }, T0));
    }

    [Fact]
    public void Placement_RequiresUnlockedFreeCellsInsideGrid()
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        GrantBlueprint(e, s, Farm);
        GrantBlueprint(e, s, "bp_ore_mine");
        GrantBlueprint(e, s, "bp_diamond_mine");
        Assert.Equal("area locked", Fail(e, ref s, new CityBuild { BlueprintId = Farm, X = 4, Y = 0 }, T0));
        Assert.Equal("outside the city grid", Fail(e, ref s, new CityBuild { BlueprintId = Farm, X = 12, Y = 4 }, T0));
        Assert.Equal("area locked", Fail(e, ref s, new CityBuild { BlueprintId = "bp_diamond_mine", X = 7, Y = 4 }, T0)); // 2×2 spills into east
        Ok(e, ref s, new CityBuild { BlueprintId = Farm, X = 4, Y = 4 }, T0);
        Assert.Equal("cells occupied", Fail(e, ref s, new CityBuild { BlueprintId = "bp_diamond_mine", X = 4, Y = 4 }, T0));
        Assert.Equal("already placed", Fail(e, ref s, new CityBuild { BlueprintId = Farm, X = 5, Y = 5 }, T0));
        Assert.Equal("blueprint not owned", Fail(e, ref s, new CityBuild { BlueprintId = "bp_gene_lab", X = 6, Y = 6 }, T0));
        UnlockAll(e, s);
        Ok(e, ref s, new CityBuild { BlueprintId = "bp_ore_mine", X = 4, Y = 0 }, T0);
    }

    [Fact]
    public void Build_SpendsCost_RunsTimer_ThenLevelOne()
    {
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        s.Wallet.Add(Currencies.Ore, 40); s.Wallet.Add(Currencies.Biomass, 60);
        GrantBlueprint(e, s, Farm);
        Ok(e, ref s, new CityBuild { BlueprintId = Farm, X = 4, Y = 4 }, T0);
        Assert.Equal(0, s.Wallet.Get(Currencies.Ore));
        var b = CitySystem.Owned(s, Farm);
        Assert.True(b.Placed);
        Assert.Equal(0, b.Level);
        Assert.False(CitySystem.IsActive(b));
        e.Settle(s, T0 + 59_999);
        Assert.Equal(0, CitySystem.Owned(s, Farm).Level);
        e.Settle(s, T0 + Min);
        Assert.Equal(1, CitySystem.Owned(s, Farm).Level);
        Assert.Equal(1, s.Counter("city_buildings_built"));
        Assert.Equal(0, Stored(s, Farm));
    }

    // ---- production ----

    [Fact]
    public void Production_SettlesFromTimestamps_AndCollectKeepsRemainder()
    {
        var (e, s, t) = FarmReady();
        e.Settle(s, t + 90_000); // 1.5 minutes at 60/h
        Assert.Equal(1.5, Stored(s, Farm), 6);
        var r = Ok(e, ref s, new CityCollect { BlueprintId = Farm }, t + 90_000);
        Assert.Equal(1, ((Dictionary<string, long>)r.Payload)[Currencies.Biomass]);
        Assert.Equal(0.5, Stored(s, Farm), 6);
        e.Settle(s, t + 2 * Hour);
        Assert.Equal(0.5 + 118.5, Stored(s, Farm), 6);
        long before = s.Wallet.Get(Currencies.Biomass);
        Ok(e, ref s, new CityCollect(), t + 2 * Hour);
        Assert.Equal(before + 119, s.Wallet.Get(Currencies.Biomass));
        Assert.Equal(120, s.Counter("city_resources_collected"));
    }

    [Fact]
    public void Production_IsCappedByStorage()
    {
        var (e, s, t) = FarmReady();
        Assert.Equal(480, CitySystem.StorageCap(e.Content, CitySystem.Owned(s, Farm)), 6);
        e.Settle(s, t + 20 * Hour);
        Assert.Equal(480, Stored(s, Farm), 6);
        CitySystem.Owned(s, Farm).Level = 3; // +12% storage per level
        Assert.Equal(480 * 1.24, CitySystem.StorageCap(e.Content, CitySystem.Owned(s, Farm)), 6);
    }

    [Fact]
    public void Production_WorkersBoost_SquadAliensWorkAtHalf()
    {
        var (e, s, t) = FarmReady();
        GrantAlien(e, s, "alien_mossling");
        Neutral(s, "alien_mossling");
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_mossling", BlueprintId = Farm }, t);
        Assert.Equal(14, AlienSystem.WorkBonusPct(s, e.Content, AlienSystem.Owned(s, "alien_mossling"), Currencies.Biomass), 6);
        e.Settle(s, t + 2 * Hour);
        Assert.Equal(120 * 1.14, Stored(s, Farm), 6);

        // Squad alien: needs a housing slot; then works at 50%.
        long t2 = BuildActive(e, ref s, "bp_housing_pod", 7, 7, t + 2 * Hour);
        Ok(e, ref s, new CityCollect(), t2);
        double rest = Stored(s, Farm);
        Ok(e, ref s, new AlienSetSquad { AlienTypeIds = { "alien_mossling" } }, t2);
        e.Settle(s, t2 + Hour);
        Assert.Equal(rest + 60 * 1.07, Stored(s, Farm), 6);
    }

    [Fact]
    public void Production_WorkTraitsAddWhenCurrencyMatches()
    {
        var (e, s, t) = FarmReady();
        GrantAlien(e, s, "alien_mossling");
        var a = Neutral(s, "alien_mossling");
        a.Traits.Add("trait_green_thumb"); // +20% biomass/grain
        a.Traits.Add("trait_deep_digger"); // ore only: no effect here
        Assert.Equal(34, AlienSystem.WorkBonusPct(s, e.Content, a, Currencies.Biomass), 6);
        Assert.Equal(34, AlienSystem.WorkBonusPct(s, e.Content, a, Currencies.Ore), 6);
        Assert.Equal(14, AlienSystem.WorkBonusPct(s, e.Content, a, Currencies.Fuel), 6);
    }

    [Fact]
    public void Production_PriorityBoostsOneCurrency_PenalizesOthers()
    {
        var (e, s, t) = FarmReady();
        t = BuildActive(e, ref s, "bp_grain_farm", 5, 4, t);
        Ok(e, ref s, new CityCollect(), t);
        CitySystem.Owned(s, Farm).Stored = 0;
        Ok(e, ref s, new CitySetPriority { Currency = Currencies.Biomass }, t);
        e.Settle(s, t + 2 * Hour);
        Assert.Equal(120 * 1.3, Stored(s, Farm), 6);
        Assert.Equal(80 * 0.9, Stored(s, "bp_grain_farm"), 6);
        Assert.Equal("currency is not produced in the city", Fail(e, ref s, new CitySetPriority { Currency = Currencies.Stamina }, t + 2 * Hour));
        Ok(e, ref s, new CitySetPriority { Currency = null }, t + 2 * Hour);
        Assert.Null(s.City.PriorityCurrency);
    }

    [Fact]
    public void Production_ModifiersIntegratePiecewiseOverTheirWindow()
    {
        var (e, s, t) = FarmReady();
        s.Modifiers.Add(new Modifier { Target = "production:biomass", Multiplier = 2, StartMs = t + Hour, EndMs = t + 2 * Hour });
        s.Modifiers.Add(new Modifier { Target = "production:*", Multiplier = 0.5, StartMs = t + 2 * Hour + 30 * Min, EndMs = 0 });
        s.Modifiers.Add(new Modifier { Target = "production:ore", Multiplier = 10, StartMs = 0, EndMs = 0 }); // other currency
        e.Settle(s, t + 3 * Hour);
        // 1h×60 + 1h×120 + 0.5h×60 + 0.5h×30
        Assert.Equal(60 + 120 + 30 + 15, Stored(s, Farm), 6);
        Assert.Equal(30, CitySystem.ProductionPerHour(s, e.Content, CitySystem.Owned(s, Farm), t + 3 * Hour), 6);
    }

    [Fact]
    public void Upgrade_SpendsCost_CompletesAtExactTime_AndRaisesRate()
    {
        var (e, s, t) = FarmReady();
        var def = e.Content.Blueprint(Farm);
        Assert.Equal(new long[] { 30, 50 }, CitySystem.UpgradeCost(e.Content, def, 1).Select(c => c.Amount));
        Assert.Equal(new long[] { 55, 91 }, CitySystem.UpgradeCost(e.Content, def, 3).Select(c => c.Amount));
        long ore = s.Wallet.Get(Currencies.Ore);
        Ok(e, ref s, new CityUpgrade { BlueprintId = Farm }, t);
        Assert.Equal(ore - 30, s.Wallet.Get(Currencies.Ore));
        Assert.Equal("upgrade already running", Fail(e, ref s, new CityUpgrade { BlueprintId = Farm }, t));
        Assert.Equal("cannot demolish while constructing or upgrading", Fail(e, ref s, new CityDemolish { BlueprintId = Farm }, t));
        e.Settle(s, t + 2 * Min + Hour); // 2 min at 60/h, then 1h at 69/h
        Assert.Equal(2, CitySystem.Owned(s, Farm).Level);
        Assert.Equal(2 + 69, Stored(s, Farm), 6);
        Assert.Equal(1, s.Counter("city_building_upgrades"));
    }

    [Fact]
    public void Upgrade_MaxLevelGrowsWithStars()
    {
        var (e, s, t) = FarmReady();
        var b = CitySystem.Owned(s, Farm);
        Assert.Equal(10, CitySystem.MaxLevel(e.Content, b));
        b.Level = 10;
        Assert.Equal("max level for current stars", Fail(e, ref s, new CityUpgrade { BlueprintId = Farm }, t));
        CitySystem.Owned(s, Farm).Stars = 2;
        Ok(e, ref s, new CityUpgrade { BlueprintId = Farm }, t);
    }

    [Fact]
    public void BuildSpeedBoonsShortenTimers()
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        long t = BuildActive(e, ref s, "bp_construction_yard", 6, 6, T0); // +5% build speed
        var def = e.Content.Blueprint("bp_diamond_mine");
        Assert.Equal((long)Math.Ceiling(Hour / 1.05), CitySystem.BuildDurationMs(s, e.Content, def, t));
        s.Modifiers.Add(new Modifier { Target = "build_speed", Multiplier = 2 });
        Assert.Equal((long)Math.Ceiling(Hour / 2.1), CitySystem.BuildDurationMs(s, e.Content, def, t));
    }

    [Fact]
    public void Demolish_StoresBuilding_CollectsAndUnassigns_ReplaceIsInstant()
    {
        var (e, s, t) = FarmReady();
        GrantAlien(e, s, "alien_blorp");
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_blorp", BlueprintId = Farm }, t);
        CitySystem.Owned(s, Farm).Level = 4;
        long bio = s.Wallet.Get(Currencies.Biomass);
        e.Settle(s, t + Hour);
        double stored = Stored(s, Farm);
        Ok(e, ref s, new CityDemolish { BlueprintId = Farm }, t + Hour);
        Assert.Equal(bio + (long)Math.Floor(stored), s.Wallet.Get(Currencies.Biomass));
        Assert.False(CitySystem.Owned(s, Farm).Placed);
        Assert.Null(AlienSystem.Owned(s, "alien_blorp").WorkBuildingId);
        e.Settle(s, t + 5 * Hour);
        Assert.Equal(0, Stored(s, Farm));
        Ok(e, ref s, new CityBuild { BlueprintId = Farm, X = 7, Y = 7 }, t + 5 * Hour);
        var b = CitySystem.Owned(s, Farm);
        Assert.Equal(4, b.Level);
        Assert.Null(b.TimerId);
        Assert.True(CitySystem.IsActive(b));
        e.Settle(s, t + 6 * Hour);
        Assert.Equal(60 * 1.45, Stored(s, Farm), 6);
    }

    [Fact]
    public void Move_ChecksPlacementIgnoringItself()
    {
        var (e, s, t) = FarmReady();
        t = BuildActive(e, ref s, "bp_diamond_mine", 6, 6, t);
        Ok(e, ref s, new CityMove { BlueprintId = "bp_diamond_mine", X = 5, Y = 6 }, t); // overlaps its old cells
        Assert.Equal("cells occupied", Fail(e, ref s, new CityMove { BlueprintId = Farm, X = 6, Y = 7 }, t));
        Assert.Equal("area locked", Fail(e, ref s, new CityMove { BlueprintId = Farm, X = 0, Y = 0 }, t));
        Ok(e, ref s, new CityMove { BlueprintId = Farm, X = 7, Y = 4 }, t);
        Assert.Equal(7, CitySystem.Owned(s, Farm).X);
    }

    // ---- blueprints: dupes, stars, milestones, collection ----

    [Fact]
    public void BlueprintGranter_FirstCopyOwned_DupesBecomeShards()
    {
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        GrantBlueprint(e, s, Farm);
        Assert.Equal(1, CitySystem.Owned(s, Farm).Stars);
        Assert.Equal(0, s.Wallet.Get(Currencies.BlueprintShards(Farm)));
        GrantBlueprint(e, s, Farm, 3);
        Assert.Single(s.City.Blueprints);
        Assert.Equal(3, s.Wallet.Get(Currencies.BlueprintShards(Farm)));
        Assert.Equal(1, s.Counter("blueprints_obtained"));
        Assert.Equal(3, s.Counter("blueprint_duplicates"));
        Assert.Throws<RuleException>(() => GrantBlueprint(e, s, "bp_nope"));
    }

    [Fact]
    public void BlueprintStars_OneToTen_FromShards()
    {
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        long total = e.Content.CityTuning.BlueprintStarCosts.Sum();
        Assert.Equal(111, total);
        GrantBlueprint(e, s, Farm, 1 + total);
        GrantBlueprint(e, s, "bp_ore_mine");
        Assert.Equal("not enough shards", Fail(e, ref s, new BlueprintStarUp { BlueprintId = "bp_ore_mine" }, T0));
        for (int star = 2; star <= 10; star++)
        {
            Ok(e, ref s, new BlueprintStarUp { BlueprintId = Farm }, T0);
            Assert.Equal(star, CitySystem.Owned(s, Farm).Stars);
        }
        Assert.Equal(0, s.Wallet.Get(Currencies.BlueprintShards(Farm)));
        Assert.Equal("max stars", Fail(e, ref s, new BlueprintStarUp { BlueprintId = Farm }, T0));
        Assert.Equal(9, s.Counter("blueprint_star_ups"));
    }

    [Fact]
    public void BlueprintStarMilestones_ProductionWorkersStorage()
    {
        var (e, s, t) = FarmReady();
        var b = CitySystem.Owned(s, Farm);
        var def = e.Content.Blueprint(Farm);
        b.Stars = 2;
        Assert.Equal(60 * 1.05, CitySystem.BaseRatePerHour(s, e.Content, b), 6);
        Assert.Equal(1, CitySystem.WorkerSlots(e.Content, def, b));
        b.Stars = 3; // +10% milestone
        Assert.Equal(60 * 1.10 * 1.10, CitySystem.BaseRatePerHour(s, e.Content, b), 6);
        b.Stars = 5; // +1 worker slot
        Assert.Equal(2, CitySystem.WorkerSlots(e.Content, def, b));
        b.Stars = 7; // +50% storage
        Assert.Equal(480 * 1.30 * 1.5, CitySystem.StorageCap(e.Content, b), 6);
        b.Stars = 10; // +30% more production
        Assert.Equal(60 * 1.45 * 1.40, CitySystem.BaseRatePerHour(s, e.Content, b), 6);
    }

    [Fact]
    public void CollectionBonus_OwnedBlueprintsGiveStatsByStars()
    {
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        GrantBlueprint(e, s, Farm);               // Common Hp 0.3%/star
        GrantBlueprint(e, s, "bp_diamond_mine");  // Epic CritChance 0.8%/star
        CitySystem.Owned(s, Farm).Stars = 10;
        var l = e.BuildLoadout(s, T0);            // not even placed: collection counts ownership
        Assert.Equal(3.0, l.WormStats.Percent[StatId.Hp], 6);
        Assert.Equal(0.8, l.WormStats.Percent[StatId.CritChance], 6);
    }

    // ---- boons ----

    [Fact]
    public void BoonBuildings_ReachLoadout_SkillsAndRerolls()
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        long t = BuildActive(e, ref s, "bp_star_forge", 4, 4, T0);
        t = BuildActive(e, ref s, "bp_ancient_reliquary", 6, 4, t);
        t = BuildActive(e, ref s, "bp_war_academy", 4, 6, t);
        t = BuildActive(e, ref s, "bp_scout_post", 6, 6, t);
        var l = e.BuildLoadout(s, t);
        Assert.Contains("bsk_aegis_core", l.MetaSkillIds);
        Assert.Contains("bsk_ember_crown", l.MetaSkillIds);
        Assert.Contains("bp_scout_post_t1", l.MetaSkillIds);
        Assert.DoesNotContain("bsk_storm_mantle", l.MetaSkillIds);
        Assert.Equal(1, l.DraftRerolls);
        Assert.Equal(EffectKind.SkipNegativeEvent, e.Content.Skill("bp_scout_post_t1").Triggers[0].Effect);

        CitySystem.Owned(s, "bp_scout_post").Level = 5;
        CitySystem.Owned(s, "bp_war_academy").Level = 10;
        CitySystem.Owned(s, "bp_war_academy").Stars = 7;
        CitySystem.Owned(s, "bp_star_forge").Stars = 7;
        CitySystem.Owned(s, "bp_ancient_reliquary").Stars = 10;
        l = e.BuildLoadout(s, t);
        Assert.Contains("bp_scout_post_t2", l.MetaSkillIds);
        Assert.DoesNotContain("bp_scout_post_t1", l.MetaSkillIds);
        Assert.Contains("bsk_aegis_core_plus", l.MetaSkillIds);
        Assert.DoesNotContain("bsk_aegis_core", l.MetaSkillIds);
        Assert.Contains("bsk_storm_mantle", l.MetaSkillIds);
        Assert.Contains("bsk_frost_sigil", l.MetaSkillIds);
        Assert.Equal(3, l.DraftRerolls);
        foreach (var id in l.MetaSkillIds) Assert.Equal(SkillSource.Building, e.Content.Skill(id).Source);
    }

    [Fact]
    public void BoonBuildings_InactiveWhileUnderConstructionOrStored()
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        GrantBlueprint(e, s, "bp_war_academy");
        Ok(e, ref s, new CityBuild { BlueprintId = "bp_war_academy", X = 4, Y = 4 }, T0);
        Assert.Equal(0, e.BuildLoadout(s, T0).DraftRerolls);
        e.Settle(s, T0 + Hour);
        Assert.Equal(1, e.BuildLoadout(s, T0 + Hour).DraftRerolls);
        Ok(e, ref s, new CityDemolish { BlueprintId = "bp_war_academy" }, T0 + Hour);
        Assert.Equal(0, e.BuildLoadout(s, T0 + Hour).DraftRerolls);
    }

    [Fact]
    public void SquadSlots_ZeroUntilHousing_ThenUpgradesAndRareBlueprints_CappedAtThree()
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        Assert.Equal(0, CitySystem.SquadSlots(s, e.Content));
        long t = BuildActive(e, ref s, "bp_housing_pod", 4, 4, T0);
        Assert.Equal(1, CitySystem.SquadSlots(s, e.Content));
        CitySystem.Owned(s, "bp_housing_pod").Level = 12;
        Assert.Equal(2, CitySystem.SquadSlots(s, e.Content));
        t = BuildActive(e, ref s, "bp_barracks_dome", 6, 4, t);
        Assert.Equal(3, CitySystem.SquadSlots(s, e.Content));
        t = BuildActive(e, ref s, "bp_command_spire", 4, 6, t);
        Assert.Equal(3, CitySystem.SquadSlots(s, e.Content));
    }

    [Fact]
    public void StaminaAndOtherBonusProviders()
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        long t = BuildActive(e, ref s, "bp_stamina_well", 4, 4, T0);
        t = BuildActive(e, ref s, "bp_regen_spire", 5, 4, t);
        t = BuildActive(e, ref s, "bp_laboratory", 6, 4, t);
        var bonus = CitySystem.StaminaBonus(s, e.Content, t);
        Assert.Equal(5, bonus.CapBonus);
        Assert.Equal(5, bonus.TickSpeedPercent, 6);
        CitySystem.Owned(s, "bp_stamina_well").Level = 3;
        CitySystem.Owned(s, "bp_stamina_well").Stars = 5;
        CitySystem.Owned(s, "bp_regen_spire").Stars = 2;
        bonus = CitySystem.StaminaBonus(s, e.Content, t);
        Assert.Equal(7 + 5, bonus.CapBonus);
        Assert.Equal(5 * 1.06, bonus.TickSpeedPercent, 6);
        Assert.Equal(5, CitySystem.ResearchSpeedPct(s, e.Content), 6);
    }

    [Fact]
    public void GlobalProductionBoon_AppliesToEveryProducer()
    {
        var (e, s, t) = FarmReady();
        t = BuildActive(e, ref s, "bp_observatory", 7, 7, t); // +2% production everywhere
        Assert.Equal(60 * 1.02, CitySystem.BaseRatePerHour(s, e.Content, CitySystem.Owned(s, Farm)), 6);
        Assert.Contains("bp_observatory_t1", e.BuildLoadout(s, t).MetaSkillIds);
    }

    [Fact]
    public void State_SurvivesJsonRoundTrip()
    {
        var (e, s, t) = FarmReady();
        GrantAlien(e, s, "alien_zib");
        var c = TestHelpers.Clone(s);
        Assert.Equal(1, CitySystem.Owned(c, Farm).Level);
        Assert.NotNull(AlienSystem.Owned(c, "alien_zib"));
        Assert.Equal(s.Aliens.Owned["alien_zib"].Genes.Atk, c.Aliens.Owned["alien_zib"].Genes.Atk);
    }
}
