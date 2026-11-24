using System.Text.Json;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Skill redesign, step 5: the 80-skill content (per synergy 3 Common, 2 Legendary, 3 Mythic; Alien 4 + 2 + 4 by owner decision),
/// its card texts, offer gates, the board → battle carry (Ambush Instinct) and old saves with removed ids.
/// </summary>
public class SkillRedesignContentTests
{
    private static readonly ContentDb C = ContentDb.Default;

    private static IEnumerable<SkillDef> RunPool() =>
        C.Skills.Values.Where(s => s.Source == SkillSource.Run && s.Draftable).OrderBy(s => s.Id, StringComparer.Ordinal);

    /// <summary>Owner: "card texts short (about 20 words)", every level reads differently (SkillTuningTests checks the levels).</summary>
    [Fact]
    public void CardTexts_AreShort_AtEveryLevel()
    {
        var bad = new List<string>();
        foreach (var s in RunPool())
            for (int lv = 1; lv <= C.RunTuning.MaxSkillLevelOf(s.Rarity); lv++)
            {
                string t = SkillText.Full(C, s, lv);
                if (SkillText.WordCount(t) > 22) bad.Add(s.Id + " Lv" + lv + " (" + SkillText.WordCount(t) + "): " + t);
            }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    /// <summary>Owner: Legendaries and Mythics are never a plain "+X% stat": each carries a trigger, rule or conditional bonus.</summary>
    [Fact]
    public void NoLegendaryOrMythic_IsAPlainStatStick()
    {
        foreach (var s in RunPool().Where(s => s.Rarity is Rarity.Legendary or Rarity.Mythic))
            Assert.True(s.Triggers.Count > 0 || s.Stats.Any(SkillLeveling.IsCost), s.Id + " is a plain stat package"); // a trade-off (Overclocked Core) counts
    }

    [Fact]
    public void TradeOffCosts_ReadAsCosts()
    {
        foreach (var id in new[] { "gen_overclocked", "bleed_hemorrhage", "mythic_crimson_pact", "mythic_chain_fission", "counter_mirror", "mythic_bastion", "mythic_symbiote", "mythic_endless_chain", "mythic_thermal_shock" })
            Assert.Contains(SkillText.RichLines(C, C.Skill(id)!, 1), l => l.Cost);
        Assert.DoesNotContain(SkillText.RichLines(C, C.Skill("fire_ember")!, 1), l => l.Cost);
    }

    // ---- offer gates ----------------------------------------------------------------------------------------------

    /// <summary>Owner: skills that need a source the run might not have are only offered once it owns one (SkillDef.Needs).</summary>
    [Fact]
    public void SkillsNeedingASource_AreOfferedOnlyWithIt()
    {
        var run = new ActiveRun();
        run.Skills.Add("sig_fire");
        Assert.False(RunDraft.IsEligible(C, run, C.Skill("light_conductor")!));   // no Arc yet
        Assert.False(RunDraft.IsMythicEligible(C, run, C.Skill("mythic_superconductor")!));
        Assert.False(RunDraft.IsMythicEligible(C, run, C.Skill("mythic_swarm_link")!)); // no aliens
        Assert.False(RunDraft.IsMythicEligible(C, run, C.Skill("mythic_shatter")!));   // no Freeze
        run.Skills.Add("sig_lightning");                                                   // a second signature counts
        Assert.True(RunDraft.IsEligible(C, run, C.Skill("light_conductor")!));
        Assert.True(RunDraft.IsMythicEligible(C, run, C.Skill("mythic_superconductor")!));
        Assert.False(RunDraft.IsMythicEligible(C, run, C.Skill("mythic_capacitor")!));    // Arc is no Shock source …
        run.Skills.Add("light_spark");
        Assert.True(RunDraft.IsMythicEligible(C, run, C.Skill("mythic_capacitor")!));     // … Spark is
        run.Loadout.Aliens.Add(new CombatAlien { AlienTypeId = "a" });
        Assert.True(RunDraft.IsMythicEligible(C, run, C.Skill("mythic_swarm_link")!));
        Assert.False(RunDraft.IsEligible(C, run, C.Skill("alien_decoy")!) && RunDraft.HasSource(C, run, "blocks"));
        run.Skills.Add("alien_bodyguard");
        Assert.True(RunDraft.HasSource(C, run, "blocks"));
        run.Loadout.MetaSkillIds.Add("lab_frost_nova");                                    // a lab Freeze source counts too
        Assert.True(RunDraft.IsMythicEligible(C, run, C.Skill("mythic_shatter")!));
        // a drone launcher is an alien source; a mount is a shield source (Spiked Hide)
        var drones = new ActiveRun(); drones.Skills.Add("alien_rally");
        Assert.True(RunDraft.HasSource(C, drones, "aliens"));
        var mount = new ActiveRun(); mount.Loadout.MountId = "m";
        Assert.True(RunDraft.HasSource(C, mount, "shield"));
        Assert.False(RunDraft.HasSource(C, new ActiveRun(), "shield"));
    }

    // ---- old saves --------------------------------------------------------------------------------------------------

    /// <summary>Ids the redesign replaced (new ids) or removed: a saved run that holds them still loads and plays (they are skipped).</summary>
    public static readonly string[] RemovedByRedesign =
    {
        "fire_flare", "fire_inferno", "mythic_sunheart", "mythic_cinder_storm", "fire_phoenix", "poison_plague", "poison_miasma",
        "mythic_blightbloom", "mythic_toxic_aura", "poison_venom_heart", "bleed_razor", "mythic_bloodlust", "bleed_crimson_tide",
        "mythic_thousand_cuts", "ice_frostbite", "ice_glacier", "ice_absolute_zero", "mythic_winter_crown", "mythic_shatterpoint",
        "light_chain", "light_storm", "light_thunder_god", "mythic_overcharge", "mythic_ball_lightning", "mythic_blade_storm",
        "mythic_cascade", "combo_endless", "counter_vengeance", "counter_parry", "mythic_thorn_king", "alien_charmer",
        "alien_overmind", "mythic_ark_command", "crit_executioner", "gen_boss_hunter", "mythic_reaper", "mythic_deadeye",
        "mythic_titan_blood", "mythic_warlord", "crit_assassin",
    };

    [Fact]
    public void RemovedIds_AreGone_AndASavedRunHoldingThem_StillLoadsAndPlays()
    {
        foreach (var id in RemovedByRedesign) Assert.Null(C.Skill(id));
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.SignatureIds.Clear(); // saved before several signatures: only SignatureId
        foreach (var id in RemovedByRedesign) { run.Skills.Add(id); run.SkillLevels[id] = 2; run.Gains.Add(new RunGain("draft", id, 1)); }
        run.LastBattle = new BattleResult();
        run.LastBattle.Log.Add(new BattleLogEntry { Action = BattleAction.Basic, Amount = 10, Parry = true }); // an old parried hit
        string json = JsonSerializer.Serialize(s, TestHelpers.Json);
        var loaded = JsonSerializer.Deserialize<GameState>(json, TestHelpers.Json)!;
        Assert.Equal(new[] { run.SignatureId }, RunSystem.Signatures(loaded.Run.Active!));
        Assert.NotNull(RunSystem.WormStats(e.Content, loaded.Run.Active!));
        Assert.Equal(0, RunDraft.FamilyCount(e.Content, loaded.Run.Active!, SkillFamily.Fire) - RunDraft.FamilyCount(e.Content, run, SkillFamily.Fire));
        RunTestKit.AutoToEnd(e, ref loaded);
        Assert.True(loaded.Run.LastSummary!.Won);
    }
}
