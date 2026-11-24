using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.City.Kit;

namespace RogueStars.Rules.Tests.City;

public class AlienTests
{
    private const long T0 = TestHelpers.T0;

    /// <summary>Aliens pass: 20 species (7 C · 5 R · 4 E · 2 L · 2 M), one ability step per star, species-fitted trait pools.</summary>
    [Fact]
    public void Content_TwentySpecies_OneAbilityStepPerStar_AndFittedTraitPools()
    {
        var c = TestHelpers.Engine().Content;
        Assert.Equal(20, c.AlienTypes.Count);
        Assert.Equal(new[] { 7, 5, 4, 2, 2 }, Enumerable.Range(0, 5).Select(r => c.AlienTypes.Values.Count(a => (int)a.Rarity == r)));
        Assert.Equal(Rarity.Common, c.AlienType("alien_emberkit")!.Rarity);
        foreach (var id in new[] { "alien_shiverpup", "alien_arcwhisk", "alien_chronoslug", "alien_swarmheart" }) Assert.NotNull(c.AlienType(id));
        foreach (var a in c.AlienTypes.Values)
        {
            Assert.Equal(Enumerable.Range(1, 10), a.ArchetypeSkills.Select(x => x.Stars));
            foreach (var step in a.ArchetypeSkills)
            {
                var s = c.Skill(step.SkillId)!;
                Assert.Equal(SkillSource.Alien, s.Source);
                Assert.Equal(a.AbilityName, s.Name); // no trailing tier number in the name
            }
            Assert.Equal(a.Family, a.Synergies[0]);
            // pool: Swift and Eager (one tempo trait at a time), effect traits that fit the ability, exactly one work trait
            Assert.Contains(ContentDb.TraitSwift, a.PossibleTraits);
            Assert.Contains(ContentDb.TraitEager, a.PossibleTraits);
            Assert.Single(a.PossibleTraits, t => c.AlienTrait(t)!.Kind == AlienTraitKind.Work);
            Assert.DoesNotContain(a.PossibleTraits, t => c.AlienTrait(t)!.Kind == AlienTraitKind.Legacy);
        }
    }

    [Fact]
    public void Granter_NewTypeRollsAlien_DupesBecomeShards_OnePerType()
    {
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        GrantAlien(e, s, "alien_emberkit");
        var a = AlienSystem.Owned(s, "alien_emberkit");
        Assert.Equal(1, a.Stars);
        Assert.InRange(a.Traits.Count, 1, 3);
        foreach (var t in a.Traits) Assert.Contains(t, e.Content.AlienType("alien_emberkit").PossibleTraits);
        Assert.True(a.Traits.Count(t => e.Content.AlienTrait(t)!.Kind == AlienTraitKind.Tempo) <= 1, "Swift or Eager, not both");
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

    /// <summary>Aliens pass: alien levels are gone (no command, no cost, no cap); Grain is spent only on breeding.</summary>
    [Fact]
    public void AlienLevels_AreRemoved()
    {
        var e = TestHelpers.Engine();
        Assert.Null(e.CommandType("AlienLevelUp"));
        Assert.DoesNotContain(e.CommandNames, n => n.Contains("Level") && n.StartsWith("Alien", StringComparison.Ordinal));
        Assert.Contains("breeding", e.Content.CurrencyUse(Currencies.Grain)!.Use, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("level", e.Content.CurrencyUse(Currencies.Grain)!.Use, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stars_OneToTen_EveryStarIsItsOwnAbilityStep()
    {
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        long total = e.Content.AlienTuning.StarCosts.Sum();
        GrantAlien(e, s, "alien_frostmite", 1 + total);
        for (int star = 1; star <= 10; star++)
        {
            if (star > 1) Ok(e, ref s, new AlienStarUp { AlienTypeId = "alien_frostmite" }, T0);
            var a = AlienSystem.Owned(s, "alien_frostmite");
            Assert.Equal(star, a.Stars);
            Assert.Equal(new[] { "ask_frostmite_" + star }, AlienSystem.SkillIds(e.Content, a));
            Assert.Equal(star is 1 or 3 or 5 or 7 or 10, AlienSystem.IsTierStar(e.Content, star));
        }
        Assert.Equal(0, s.Wallet.Get(Currencies.AlienShards("alien_frostmite")));
        Assert.Equal("max stars", Fail(e, ref s, new AlienStarUp { AlienTypeId = "alien_frostmite" }, T0));
        Assert.Equal("ask_frostmite_9", e.Content.Skill("ask_frostmite_10").UpgradesFrom);
        Assert.Equal(9, s.Counter("alien_star_ups"));
    }

    /// <summary>Aliens pass: no HP / ATK / DEF; genes are inactive; Speed is the species' (Swift +15%); work grows with stars only.</summary>
    [Fact]
    public void CombatStats_OnlySpeed_GenesInactive_WorkGrowsWithStars()
    {
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        GrantAlien(e, s, "alien_blorp");
        var a = Neutral(s, "alien_blorp");
        a.Traits.Clear();
        var st = AlienSystem.CombatStats(e.Content, a);
        Assert.Equal(0, st.Value(StatId.Hp));
        Assert.Equal(0, st.Value(StatId.Atk));
        Assert.Equal(0, st.Value(StatId.Def));
        Assert.Equal(34, st.Value(StatId.Speed), 6);
        double work = AlienSystem.WorkPower(e.Content, a);
        a.Genes.Atk = 40; a.Genes.Speed = 40; a.Genes.Work = 40; a.Level = 50; // genes and old levels change nothing
        Assert.Equal(34, AlienSystem.CombatStats(e.Content, a).Value(StatId.Speed), 6);
        Assert.Equal(work, AlienSystem.WorkPower(e.Content, a), 6);
        a.Traits.Add(ContentDb.TraitSwift);
        Assert.Equal(34 * 1.15, AlienSystem.CombatStats(e.Content, a).Value(StatId.Speed), 6);
        Assert.Equal(100 / (34 * 1.15), AlienSystem.ActsEvery(e.Content, a), 6);
        a.Stars = 3;
        Assert.Equal(10 * (1 + 2 * e.Content.AlienTuning.StarWorkPct / 100.0), AlienSystem.WorkPower(e.Content, a), 6);
    }

    /// <summary>
    /// Save migration (aliens pass): an old save's alien levels go back to 1 and 100% of the Grain spent on them comes back (by the
    /// rarity they were paid at: Emberkit was Rare); old combat traits are remapped by species; stars, shards and genes stay.
    /// </summary>
    [Fact]
    public void OldSave_AlienLevelsRefundGrain_TraitsRemap_OnTheFirstCommand()
    {
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        string json = System.Text.Json.JsonSerializer.Serialize(s, TestHelpers.Json);
        var old = System.Text.Json.JsonSerializer.Deserialize<GameState>(json, TestHelpers.Json)!;
        old.Aliens.Migrated = 0; // a save from before the aliens pass (the field did not exist)
        old.Aliens.Owned["alien_blorp"] = new OwnedAlien { TypeId = "alien_blorp", Level = 3, Stars = 2, Traits = { "trait_tough", "trait_green_thumb" },
            Genes = new AlienGenes { Hp = 20, Atk = -5, Def = 35, Speed = 10, Work = 4 } };
        old.Aliens.Owned["alien_emberkit"] = new OwnedAlien { TypeId = "alien_emberkit", Level = 2, Traits = { "trait_pyromaniac", "trait_fierce" } };
        old.Aliens.Owned["alien_voidmaw"] = new OwnedAlien { TypeId = "alien_voidmaw", Level = 1, Traits = { "trait_fierce", "trait_lucky", "trait_vampiric" } };
        old.Aliens.Owned["alien_mossling"] = new OwnedAlien { TypeId = "alien_mossling", Level = 1, Traits = { "trait_tough" } };
        old.Wallet.Add(Currencies.AlienShards("alien_blorp"), 7);
        long grain = old.Wallet.Get(Currencies.Grain);
        e.Settle(old, T0);
        // Blorp (Common): levels 1→2→3 = 80 + 85; Emberkit (paid as a Rare): 1→2 = 120
        Assert.Equal(80 + 85 + 120, old.Aliens.LevelGrainRefunded);
        Assert.Equal(grain + 285, old.Wallet.Get(Currencies.Grain));
        Assert.All(old.Aliens.Owned.Values, a => Assert.Equal(1, a.Level));
        var blorp = old.Aliens.Owned["alien_blorp"];
        Assert.Equal(2, blorp.Stars);
        Assert.Equal(7, old.Wallet.Get(Currencies.AlienShards("alien_blorp")));
        Assert.Equal(35, blorp.Genes.Def); // genes are kept (inactive)
        Assert.Equal(new[] { "trait_lingering", "trait_green_thumb" }, blorp.Traits);              // Tough → Bulwark or Lingering: Blorp has Lingering
        Assert.Equal(new[] { "trait_twin", "trait_wide" }, old.Aliens.Owned["alien_emberkit"].Traits); // Pyromaniac → Twin, Fierce → Twin or Wide
        Assert.Equal(new[] { "trait_swift", "trait_vampiric" }, old.Aliens.Owned["alien_voidmaw"].Traits); // Fierce → (no Twin / Wide, Vampiric taken) dropped; Lucky → Swift
        Assert.Equal(new[] { "trait_twin" }, old.Aliens.Owned["alien_mossling"].Traits);            // Tough → (no Bulwark / Lingering) Twin
        Assert.Equal(AlienSystem.MigrationVersion, old.Aliens.Migrated);
        // once only
        old.Aliens.Owned["alien_blorp"].Level = 9;
        e.Settle(old, T0 + 1);
        Assert.Equal(grain + 285, old.Wallet.Get(Currencies.Grain));
        // a new save is born migrated
        Assert.Equal(AlienSystem.MigrationVersion, TestHelpers.NewPlayer(e).Aliens.Migrated);
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
        Assert.Equal(0, ca.Stats.Value(StatId.Hp)); // aliens pass: sidekicks have no stats but Speed
        Assert.Equal(37, ca.Stats.Value(StatId.Speed) / (ca.Traits.Contains(ContentDb.TraitSwift) ? 1.15 : 1), 6);
        Assert.Equal(1, ca.Stars);

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
