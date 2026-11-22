using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.City.Kit;

namespace RogueStars.Rules.Tests.City;

public class AlienTests
{
    private const long T0 = TestHelpers.T0;

    [Fact]
    public void Content_SixteenSpecies_WithArchetypeSkillChains()
    {
        var c = TestHelpers.Engine().Content;
        Assert.Equal(16, c.AlienTypes.Count);
        foreach (var a in c.AlienTypes.Values)
        {
            Assert.Equal(new[] { 1, 3, 5, 7, 10 }, a.ArchetypeSkills.Select(x => x.Stars));
            foreach (var step in a.ArchetypeSkills) Assert.Equal(SkillSource.Alien, c.Skill(step.SkillId).Source);
            foreach (var t in a.PossibleTraits) Assert.NotNull(c.AlienTrait(t));
        }
    }

    [Fact]
    public void Granter_NewTypeRollsAlien_DupesBecomeShards_OnePerType()
    {
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        GrantAlien(e, s, "alien_emberkit");
        var a = AlienSystem.Owned(s, "alien_emberkit");
        Assert.Equal(1, a.Level);
        Assert.Equal(1, a.Stars);
        Assert.InRange(a.Traits.Count, 1, 3);
        foreach (var t in a.Traits) Assert.Contains(t, e.Content.AlienType("alien_emberkit").PossibleTraits);
        foreach (var g in new[] { a.Genes.Hp, a.Genes.Atk, a.Genes.Def, a.Genes.Speed, a.Genes.Work }) Assert.InRange(g, -15, 15);
        var genes = TestHelpers.Clone(a.Genes);

        GrantAlien(e, s, "alien_emberkit", 2);
        GrantAlien(e, s, "alien_emberkit");
        Assert.Single(s.Aliens.Owned);
        Assert.Equal(genes.Atk, AlienSystem.Owned(s, "alien_emberkit").Genes.Atk); // existing alien untouched
        Assert.Equal(3, s.Wallet.Get(Currencies.AlienShards("alien_emberkit")));
        Assert.Equal(1, s.Counter("aliens_obtained"));
        Assert.Equal(3, s.Counter("alien_duplicates"));
        Assert.Throws<RuleException>(() => GrantAlien(e, s, "alien_nope"));
    }

    [Fact]
    public void Granter_IsDeterministicForSameSeed()
    {
        var e = TestHelpers.Engine();
        var s1 = TestHelpers.NewPlayer(e, 7);
        var s2 = TestHelpers.NewPlayer(e, 7);
        foreach (var id in new[] { "alien_zib", "alien_voidmaw", "alien_nebulisk" }) { GrantAlien(e, s1, id); GrantAlien(e, s2, id); }
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(s1.Aliens, TestHelpers.Json), System.Text.Json.JsonSerializer.Serialize(s2.Aliens, TestHelpers.Json));
    }

    [Fact]
    public void LevelUp_CostsGrain_CappedByStars()
    {
        var e = TestHelpers.Engine();
        var s = Broke(e);
        GrantAlien(e, s, "alien_blorp");
        var a = AlienSystem.Owned(s, "alien_blorp");
        Assert.Equal(20, AlienSystem.LevelCost(e.Content, a));
        Assert.Equal("not enough grain", Fail(e, ref s, new AlienLevelUp { AlienTypeId = "alien_blorp" }, T0));
        s.Wallet.Add(Currencies.Grain, 42);
        Ok(e, ref s, new AlienLevelUp { AlienTypeId = "alien_blorp" }, T0);
        Ok(e, ref s, new AlienLevelUp { AlienTypeId = "alien_blorp" }, T0); // 20 + 22
        Assert.Equal(3, AlienSystem.Owned(s, "alien_blorp").Level);
        Assert.Equal(0, s.Wallet.Get(Currencies.Grain));
        Assert.Equal(28, AlienSystem.MaxLevel(e.Content, AlienSystem.Owned(s, "alien_blorp")));
        AlienSystem.Owned(s, "alien_blorp").Level = 28;
        s.Wallet.Add(Currencies.Grain, 1_000_000);
        Assert.Equal("max level for current stars", Fail(e, ref s, new AlienLevelUp { AlienTypeId = "alien_blorp" }, T0));
        Assert.Equal(2, s.Counter("alien_level_ups"));
    }

    [Fact]
    public void Stars_OneToTen_UpgradeArchetypeSkillAt3_5_7_10()
    {
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        long total = e.Content.AlienTuning.StarCosts.Sum();
        GrantAlien(e, s, "alien_frostmite", 1 + total);
        var expected = new Dictionary<int, string> { [1] = "ask_frostmite_1", [2] = "ask_frostmite_1", [3] = "ask_frostmite_2", [4] = "ask_frostmite_2",
            [5] = "ask_frostmite_3", [6] = "ask_frostmite_3", [7] = "ask_frostmite_4", [9] = "ask_frostmite_4", [10] = "ask_frostmite_5" };
        for (int star = 1; star <= 10; star++)
        {
            if (star > 1) Ok(e, ref s, new AlienStarUp { AlienTypeId = "alien_frostmite" }, T0);
            var a = AlienSystem.Owned(s, "alien_frostmite");
            Assert.Equal(star, a.Stars);
            if (expected.TryGetValue(star, out var skill)) Assert.Equal(new[] { skill }, AlienSystem.SkillIds(e.Content, a));
        }
        Assert.Equal(0, s.Wallet.Get(Currencies.AlienShards("alien_frostmite")));
        Assert.Equal("max stars", Fail(e, ref s, new AlienStarUp { AlienTypeId = "alien_frostmite" }, T0));
        Assert.Equal("ask_frostmite_4", e.Content.Skill("ask_frostmite_5").UpgradesFrom);
    }

    [Fact]
    public void CombatStats_ScaleWithGenesLevelStarsAndTraits()
    {
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        GrantAlien(e, s, "alien_blorp"); // common balanced: 200 HP / 20 ATK / 5 DEF / 10 SPD
        var a = Neutral(s, "alien_blorp");
        var st = AlienSystem.CombatStats(e.Content, a);
        Assert.Equal(200, st.Value(StatId.Hp), 6);
        a.Genes.Atk = 10; a.Level = 11; a.Stars = 3;
        a.Traits.Add("trait_fierce");
        st = AlienSystem.CombatStats(e.Content, a);
        Assert.Equal(20 * 1.1 * 1.6 * 1.24 * 1.12, st.Value(StatId.Atk), 6);
        Assert.Equal(10, st.Value(StatId.Speed), 6);
    }

    [Fact]
    public void Squad_GatedBySlots_AndFillsLoadout()
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        GrantAlien(e, s, "alien_zib");
        GrantAlien(e, s, "alien_blorp");
        Assert.Equal("not enough squad slots", Fail(e, ref s, new AlienSetSquad { AlienTypeIds = { "alien_zib" } }, T0));
        Ok(e, ref s, new AlienSetSquad(), T0); // empty squad always allowed
        long t = BuildActive(e, ref s, "bp_housing_pod", 4, 4, T0);
        Assert.Equal("alien not owned", Fail(e, ref s, new AlienSetSquad { AlienTypeIds = { "alien_solarch" } }, t));
        Assert.Equal("not enough squad slots", Fail(e, ref s, new AlienSetSquad { AlienTypeIds = { "alien_zib", "alien_blorp" } }, t));
        Ok(e, ref s, new AlienSetSquad { AlienTypeIds = { "alien_zib" } }, t);

        var l = e.BuildLoadout(s, t);
        var ca = Assert.Single(l.Aliens);
        Assert.Equal("alien_zib", ca.AlienTypeId);
        Assert.Equal(new[] { "ask_zib_1" }, ca.SkillIds);
        Assert.True(ca.Stats.Value(StatId.Hp) > 0);

        CitySystem.Owned(s, "bp_housing_pod").Level = 12; // second slot
        Assert.Equal("duplicate alien in squad", Fail(e, ref s, new AlienSetSquad { AlienTypeIds = { "alien_zib", "alien_zib" } }, t));
        Ok(e, ref s, new AlienSetSquad { AlienTypeIds = { "alien_blorp", "alien_zib" } }, t);
        Assert.Equal(new[] { "alien_blorp", "alien_zib" }, e.BuildLoadout(s, t).Aliens.Select(x => x.AlienTypeId));

        // Losing the slot (building stored) shrinks the active squad without touching the saved order.
        CitySystem.Owned(s, "bp_housing_pod").Level = 1;
        Assert.Equal(new[] { "alien_blorp" }, e.BuildLoadout(s, t).Aliens.Select(x => x.AlienTypeId));
    }

    [Fact]
    public void Assign_RulesForBuildingsAndSlots()
    {
        var e = TestHelpers.Engine();
        var s = Rich(e);
        GrantAlien(e, s, "alien_zib");
        GrantAlien(e, s, "alien_blorp");
        GrantAlien(e, s, "alien_mossling");
        Assert.Equal("building not active", Fail(e, ref s, new AlienAssign { AlienTypeId = "alien_zib", BlueprintId = "bp_ore_mine" }, T0));
        long t = BuildActive(e, ref s, "bp_ore_mine", 4, 4, T0);         // common: 1 slot
        t = BuildActive(e, ref s, "bp_fuel_refinery", 5, 4, t);          // rare: 2 slots
        t = BuildActive(e, ref s, "bp_laboratory", 7, 7, t);             // utility: no workers
        Assert.Equal("alien not owned", Fail(e, ref s, new AlienAssign { AlienTypeId = "alien_solarch", BlueprintId = "bp_ore_mine" }, t));
        Assert.Equal("building has no worker slots", Fail(e, ref s, new AlienAssign { AlienTypeId = "alien_zib", BlueprintId = "bp_laboratory" }, t));
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_zib", BlueprintId = "bp_ore_mine" }, t);
        Assert.Equal("already working there", Fail(e, ref s, new AlienAssign { AlienTypeId = "alien_zib", BlueprintId = "bp_ore_mine" }, t));
        Assert.Equal("no free worker slot", Fail(e, ref s, new AlienAssign { AlienTypeId = "alien_blorp", BlueprintId = "bp_ore_mine" }, t));
        // Moving to another building frees the old slot (an alien works in one building at a time).
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_zib", BlueprintId = "bp_fuel_refinery" }, t);
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_blorp", BlueprintId = "bp_ore_mine" }, t);
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_mossling", BlueprintId = "bp_fuel_refinery" }, t);
        Assert.Equal(new[] { "alien_mossling", "alien_zib" }, CitySystem.Workers(s, "bp_fuel_refinery").Select(a => a.TypeId));
        Ok(e, ref s, new AlienAssign { AlienTypeId = "alien_zib", BlueprintId = null }, t);
        Assert.Null(AlienSystem.Owned(s, "alien_zib").WorkBuildingId);
        Assert.Single(CitySystem.Workers(s, "bp_fuel_refinery"));
    }
}
