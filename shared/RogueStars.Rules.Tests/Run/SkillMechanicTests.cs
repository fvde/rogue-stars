using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Run.BattleLab;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Skill redesign: every Legendary and Mythic proves its own mechanic with the real content skill in a seeded BattleLab fight
/// (worm ATK 100, Slam basic 100%, punching bags with DEF 0, no rage unless the test gives it). Test kits apply statuses on hit.
/// </summary>
public class SkillMechanicTests
{
    private const TriggerKind Hit = TriggerKind.OnHit;

    private static ContentDb Kit()
    {
        var c = new ContentDb();
        foreach (var st in new[] { StatusId.Heat, StatusId.Poison, StatusId.Bleed, StatusId.Chill, StatusId.Shock })
            foreach (var n in new[] { 1, 2, 3, 4, 5, 10 })
                Skill(c, "k_" + st + n, Apply(Hit, st, n));
        Skill(c, "k_freeze_start", Apply(TriggerKind.OnBattleStart, StatusId.Freeze, 1, TargetKind.AllEnemies));
        Skill(c, "k_freeze", Apply(Hit, StatusId.Freeze));
        Skill(c, "k_heal", On(TriggerKind.OnTurnStart, EffectKind.Heal, 10, TargetKind.Self));
        Skill(c, "k_consume", On(Hit, EffectKind.ConsumeStatus, 10, status: StatusId.Bleed));
        Skill(c, "k_no_decay", Rule("combo_no_decay"));
        return c;
    }

    private static CombatantSpec Raging(params string[] skills) { var w = Worm(skills); w.StartRage = 100; return w; }
    private static CombatantSpec With(CombatantSpec w, StatId stat, double flat) { w.Stats.Add(new StatMod(stat, flat)); return w; }
    private static CombatantSpec Leveled(CombatantSpec w, string id, int lv) { w.SkillLevels[id] = lv; return w; }
    private static CombatantSpec Hitter(string id = "x", double atk = 40) => Unit(id, CombatRole.Enemy, 1_000_000, atk);

    // ---- Fire --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Flashpoint_RageAttackOn6Heat_GoesSupernovaNow_WithBonusDamage()
    {
        var c = Kit();
        var r = Fight(c, Raging("k_Heat3", "fire_flashpoint"), 1);
        var mark = r.Log.Single(e => e.Action == BattleAction.Supernova && e.Amount == 0);
        Assert.Equal(("fire_flashpoint", 6), (mark.SkillId, mark.Stacks)); // basic 3 + rage 3 = 6 Heat, blown at once
        Assert.Contains(r.Log, e => e.Action == BattleAction.Supernova && e.Target == 1 && e.Amount == 313); // 250 × 1.25
        var r2 = Fight(c, Leveled(Raging("k_Heat3", "fire_flashpoint"), "fire_flashpoint", 2), 1);
        Assert.Contains(r2.Log, e => e.Action == BattleAction.Supernova && e.Target == 1 && e.Amount == 350); // 250 × 1.4
        Assert.DoesNotContain(Fight(c, Raging("k_Heat2", "fire_flashpoint"), 1).Log, e => e.Action == BattleAction.Supernova); // 4 Heat: no
    }

    [Fact]
    public void ControlledBurn_NeedsTwelveHeat_NeverHurtsYou_HitsHarder()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_Heat4", "fire_controlled"), 3);
        var mark = r.Log.Single(e => e.Action == BattleAction.Supernova && e.Amount == 0);
        Assert.Equal((3, 12), (mark.Round, mark.Stacks));
        Assert.Contains(r.Log, e => e.Action == BattleAction.Supernova && e.Target == 1 && e.Amount == 313); // +25% Supernova damage
        Assert.DoesNotContain(r.Log, e => e.Action == BattleAction.Supernova && e.Target == 0);
    }

    /// <summary>The owner's example: hitting a heated enemy stuns it, and Heat can't be applied to it for 3 turns afterwards.</summary>
    [Fact]
    public void ThermalShock_StunsAHeatedEnemy_ThenLocksItsHeatForThreeTurns()
    {
        var c = Kit();
        var r = Fight(c, Worm("mythic_thermal_shock", "k_Heat1"), 7, Hitter());
        // round 1: Heat 1 lands (no Heat yet when the hit lands); round 2: stun + Heat lock until round 5; round 5: again
        Assert.Equal(new[] { 2, 5 }, r.Log.Where(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Stun).Select(e => e.Round));
        Assert.Equal(new[] { 2, 5 }, Rounds(r, BattleAction.StatusBlocked, "mythic_thermal_shock"));
        Assert.Equal(new[] { 1 }, r.Log.Where(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Heat).Select(e => e.Round).Distinct());
        Assert.Equal(new[] { 2, 5 }, r.Log.Where(e => e.Action == BattleAction.SkipTurn && e.Actor == 1).Select(e => e.Round));
        // a boss is staggered instead: it loses its next rage attack, never a turn
        var b = Unit("boss", CombatRole.Boss, 1_000_000, 40); b.StartRage = 50; // 50 + hit 10 + act 25 = 85 after round 1: the rage attack would come in round 2
        var rb = Fight(c, Worm("mythic_thermal_shock", "k_Heat1"), 3, b);
        Assert.DoesNotContain(rb.Log, e => e.Status == StatusId.Stun && e.Action == BattleAction.StatusApplied);
        Assert.Contains(rb.Log, e => e.Action == BattleAction.Staggered && e.Actor == 1);
        Assert.DoesNotContain(rb.Log, e => e.Action == BattleAction.Rage && e.Actor == 1 && e.Round == 2);
    }

    [Fact]
    public void ChainReaction_SupernovaLeavesFiveHeatOnEveryEnemy_AndStillSinges()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_Heat5", "mythic_chain_reaction"), 2, Bag("a"), Bag("b"));
        int blast = r.Log.FindIndex(e => e.Action == BattleAction.Supernova && e.Amount == 0);
        Assert.Equal(2, r.Log[blast].Round);
        var after = r.Log.Skip(blast).Where(e => e.Action == BattleAction.StatusApplied && e.SkillId == "mythic_chain_reaction").ToList();
        Assert.Equal(new[] { 1, 2 }, after.Select(e => e.Target).OrderBy(x => x));
        Assert.All(after, e => Assert.Equal(5, e.Stacks));
        Assert.Contains(r.Log, e => e.Action == BattleAction.Supernova && e.Target == 0 && e.Amount == 1200);
    }

    [Fact]
    public void HeatSiphon_RageAttackDrainsHeat_IntoABattleLongAtkRamp()
    {
        var c = Kit();
        var r = Fight(c, Raging("k_Heat3", "fire_heat_siphon"), 2);
        Assert.Equal(new long[] { 24 }, Amounts(r, BattleAction.Buff, "fire_heat_siphon")); // 6 Heat × 4%
        Assert.Equal(new long[] { 100, 124 }, Amounts(r, BattleAction.Basic, actor: 0));
        Assert.Equal(0, StacksAfter(r, 1, StatusId.Heat, 1));
    }

    // ---- Radiation ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Fission_SpreadsADeadEnemysRadiation_ToEveryOtherEnemy()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_Poison10", "poison_fission"), 2, Bag("a", 150), Bag("b"), Bag("c"));
        Assert.Equal(5, StacksAfter(r, Index(r, "c"), StatusId.Poison, 2));
        var r2 = Fight(c, Leveled(Worm("k_Poison10", "poison_fission"), "poison_fission", 2), 2, Bag("a", 150), Bag("b"), Bag("c"));
        Assert.Equal(8, StacksAfter(r2, Index(r2, "c"), StatusId.Poison, 2));
    }

    [Fact]
    public void CriticalMass_TwentyRadiation_ExposesTheEnemy()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_Poison5", "poison_critical"), 5);
        Assert.Equal(new long[] { 100, 100, 100, 100, 125 }, Amounts(r, BattleAction.Basic, actor: 0)); // 20 at the 4th hit
        var r2 = Fight(c, Leveled(Worm("k_Poison5", "poison_critical"), "poison_critical", 2), 5);
        Assert.Equal(140, Amounts(r2, BattleAction.Basic, actor: 0)[4]);
    }

    [Fact]
    public void ChainFission_RageDoublesRadiation_CappedAt50_AndHalvesYourHealing()
    {
        var c = Kit();
        var w = Raging("k_Poison10", "mythic_chain_fission", "k_heal"); w.StartHp = 5000;
        var r = Fight(c, w, 1);
        Assert.Equal(40, StacksAfter(r, 1, StatusId.Poison, 1)); // 20 doubled
        Assert.Equal(new long[] { 500 }, Amounts(r, BattleAction.Heal, "k_heal")); // 10% of 10 000, halved
        var w2 = Raging("k_Poison10", "k_Poison10", "k_Poison10", "mythic_chain_fission"); // 60 Radiation: +50, not +60
        Assert.Equal(110, StacksAfter(Fight(c, w2, 1), 1, StatusId.Poison, 1));
    }

    [Fact]
    public void GammaBurst_CritsTickRadiationNow_AtDoubleDamage_OncePerRound()
    {
        var c = Kit();
        var w = With(Worm("k_Poison10", "mythic_gamma_burst"), StatId.CritChance, 100_000);
        w.Stats.Add(new StatMod(StatId.ComboChance, 95));
        var r = Fight(c, w, 1);
        Assert.Equal(new long[] { 80 }, Amounts(r, BattleAction.StatusTick, "mythic_gamma_burst")); // 10 × 4% × 100 × 2, once
        Assert.True(r.Log.Count(e => e.Action is BattleAction.Basic or BattleAction.Combo && e.Actor == 0 && e.Crit) > 1);
    }

    [Fact]
    public void FalloutAura_EachRound_TwoRadiationPerDifferentStatus()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_Bleed1", "mythic_fallout_aura"), 3);
        Assert.Equal(0, StacksAfter(r, 1, StatusId.Poison, 1)); // nothing on it at round 1's start
        Assert.Equal(2, StacksAfter(r, 1, StatusId.Poison, 2)); // Bleed: 1 status
        Assert.Equal(6, StacksAfter(r, 1, StatusId.Poison, 3)); // Bleed + Radiation: 2 statuses → +4
    }

    // ---- Bleed -------------------------------------------------------------------------------------------------------

    [Fact]
    public void BloodFrenzy_ConsumesBleed_HealsFromIt_AndRefundsRageAtMax()
    {
        var c = Kit();
        var w = Raging("k_Bleed10", "bleed_frenzy"); w.StartHp = 5000; w.Stats.Add(new StatMod(StatId.RageGain, 100));
        var r = Fight(c, w, 1);
        Assert.Equal(new long[] { 250 }, Amounts(r, BattleAction.Skill, "bleed_frenzy")); // 10 × 25%
        Assert.Equal(new long[] { 125 }, Amounts(r, BattleAction.Heal, "bleed_frenzy"));
        Assert.Equal(new long[] { 50 }, Amounts(r, BattleAction.GainRage, "bleed_frenzy")); // it was at max stacks
        var below = Raging("k_Bleed4", "bleed_frenzy");
        Assert.Empty(Amounts(Fight(c, below, 1), BattleAction.GainRage, "bleed_frenzy"));
    }

    [Fact]
    public void Hemorrhage_RaisesTheCap_ButBleedNoLongerIgnoresShields()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_Bleed3", "bleed_hemorrhage"), 6);
        Assert.Equal(15, StacksAfter(r, 1, StatusId.Bleed, 6));
        var b = Bag(); b.StartShield = 1_000_000;
        Assert.Equal(1_000_000, Fight(c, Worm("k_Bleed3", "bleed_hemorrhage"), 2, b).Log.First(e => e.Action == BattleAction.StatusTick).TargetHp);
    }

    [Fact]
    public void Exsanguinate_ExecutesAtMaxBleedBelow25Pct_BossesTakeDoubleInstead()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_Bleed10", "mythic_exsanguinate"), 8, Bag("a", 1000));
        var exec = r.Log.Single(e => e.Action == BattleAction.Execute);
        var before = r.Log.Take(r.Log.IndexOf(exec)).Last(e => e.Target == 1);
        Assert.True(before.TargetHp < 250, before.ToString());
        var boss = Fight(c, Worm("k_Bleed10", "mythic_exsanguinate"), 8, Bag("boss", 1000, CombatRole.Boss));
        Assert.DoesNotContain(boss.Log, e => e.Action == BattleAction.Execute);
        Assert.Contains(boss.Log, e => e.Action == BattleAction.Basic && e.Actor == 0 && e.Amount == 200);
    }

    [Fact]
    public void CrimsonPact_LoseHpEachTurn_BleedTicksDouble()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_Bleed5", "mythic_crimson_pact"), 2);
        Assert.Equal(new long[] { 300, 300 }, Amounts(r, BattleAction.Skill, "mythic_crimson_pact"));
        Assert.Equal(60, r.Log.First(e => e.Action == BattleAction.StatusTick).Amount); // 5 × 6% × 100 = 30, doubled
    }

    [Fact]
    public void FalloutWounds_ConsumedBleed_BecomesRadiation()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_Bleed2", "k_consume", "mythic_fallout_wounds"), 1);
        Assert.Equal(6, StacksAfter(r, 1, StatusId.Poison, 1));
        Assert.False(RunDraft.HasSource(c, new ActiveRun(), "consume:Bleed"));
    }

    // ---- Ice ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Permafrost_ChillAtFivePlusNeverWearsOff_AndIceDamageGrows()
    {
        var c = Kit();
        Skill(c, "k_chill5_start", Apply(TriggerKind.OnBattleStart, StatusId.Chill, 5));
        Assert.DoesNotContain(Fight(c, Worm("k_chill5_start", "ice_permafrost"), 8).Log, e => e.Action == BattleAction.StatusExpired);
        Assert.Equal(25, BattleSimulator.FinalStats(Worm("ice_permafrost"), c).Value(StatId.IceDamage));
    }

    [Fact]
    public void ColdSnap_AFreezeChillsEveryOtherEnemy()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_freeze", "ice_cold_snap"), 1, Bag("a"), Bag("b"), Bag("c"));
        Assert.Equal(3, StacksAfter(r, Index(r, "b"), StatusId.Chill, 1));
        Assert.Equal(3, StacksAfter(r, Index(r, "c"), StatusId.Chill, 1));
        Assert.Equal(0, StacksAfter(r, Index(r, "a"), StatusId.Chill, 1));
    }

    /// <summary>CC pass (owner rule): any Freeze, whatever its source, sets the target's Chill to 0, on enemies and on the worm.</summary>
    [Fact]
    public void AnyFreeze_ResetsTheTargetsChillToZero()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_Chill3", "k_freeze"), 2);
        // round 1: Chill 3 lands, then the freeze wipes it; round 2: the freeze is on its re-apply cooldown, the Chill stays
        Assert.Contains(r.Log, e => e.Action == BattleAction.StatusRemoved && e.Status == StatusId.Chill && e.Target == 1 && e.Amount == 3 && e.Round == 1);
        Assert.Equal(0, StacksAfter(r, 1, StatusId.Chill, 1));
        Assert.Equal(3, StacksAfter(r, 1, StatusId.Chill, 2));
        // an enemy freezing the worm resets the worm's Chill too
        var foe = Hitter("foe"); foe.SkillIds.AddRange(new[] { "k_Chill3", "k_freeze" });
        var w = Fight(c, Worm(), 1, foe);
        Assert.Contains(w.Log, e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Freeze && e.Target == 0);
        Assert.Contains(w.Log, e => e.Action == BattleAction.StatusRemoved && e.Status == StatusId.Chill && e.Target == 0 && e.Amount == 3);
        Assert.Equal(0, StacksAfter(w, 0, StatusId.Chill, 1));
    }

    /// <summary>CC pass: Deep Freeze freezes an enemy at 5 Chill (the freeze resets its Chill, so it has to be chilled up again);
    /// its levels raise the damage against frozen enemies (+25% → +40%), never the freeze.</summary>
    [Fact]
    public void DeepFreeze_FreezesAtFiveChill_ThenTheChillStartsOver_AndFrozenEnemiesTakeMore()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_Chill2", "ice_deep_freeze"), 7);
        // Chill 2 → 4 → 6: frozen in round 3 (Chill back to 0); 2 → 4 → 6 again: frozen in round 6 (the re-apply cooldown is over)
        Assert.Equal(new[] { 3, 6 }, r.Log.Where(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Freeze && e.SkillId == "ice_deep_freeze").Select(e => e.Round));
        Assert.Equal(0, StacksAfter(r, 1, StatusId.Chill, 3));
        Assert.Equal(4, StacksAfter(r, 1, StatusId.Chill, 5));
        Assert.DoesNotContain(Fight(c, Worm("k_Chill1", "ice_deep_freeze"), 4).Log, e => e.Status == StatusId.Freeze); // 4 Chill: no
        // the damage bonus vs frozen grows with the level
        long Hit(CombatantSpec w) => Amounts(Fight(c, w, 1), BattleAction.Basic)[0];
        long plain = Hit(Worm("k_freeze_start"));
        Assert.Equal(plain * 1.25, Hit(Worm("k_freeze_start", "ice_deep_freeze")), 0);
        Assert.Equal(plain * 1.40, Hit(Leveled(Worm("k_freeze_start", "ice_deep_freeze"), "ice_deep_freeze", 2)), 0);
        Assert.Equal(0, SkillLeveling.At(c, c.Skill("ice_deep_freeze")!, 2).Triggers[0].Duration); // the freeze stays 1 round
    }

    /// <summary>CC pass: lab Frost Nova casts only when the rage attack crits: Freeze and damage on every enemy.</summary>
    [Fact]
    public void FrostNova_ACriticalRageAttack_FreezesAndDamagesAllEnemies()
    {
        var c = Kit();
        var r = Fight(c, With(Raging("lab_frost_nova"), StatId.CritChance, 100_000), 1, Bag("a"), Bag("b"));
        Assert.Equal(2, r.Log.Count(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Freeze && e.SkillId == "lab_frost_nova"));
        Assert.Equal(2, Amounts(r, BattleAction.Skill, "lab_frost_nova").Count(a => a > 0));
        var plain = Fight(c, Raging("lab_frost_nova"), 1, Bag("a"), Bag("b")); // no crit: no Frost Nova
        Assert.Contains(plain.Log, e => e.Action == BattleAction.Rage);
        Assert.DoesNotContain(plain.Log, e => e.SkillId == "lab_frost_nova");
    }

    [Fact]
    public void Shatter_HittingAFrozenEnemy_ThawsIt_AndHitsAllOthersFor150()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_freeze_start", "mythic_shatter"), 1, Unit("a", CombatRole.Enemy, 1_000_000, 10), Bag("b"), Bag("c"));
        Assert.Equal(new long[] { 180, 180 }, Amounts(r, BattleAction.Skill, "mythic_shatter")); // 150% ATK, the others frozen too (+20% taken)
        Assert.Contains(r.Log, e => e.Action == BattleAction.StatusRemoved && e.Status == StatusId.Freeze && e.Target == 1);
        Assert.Contains(r.Log, e => e.Action == BattleAction.Basic && e.Actor == 1); // thawed: it acts this round
    }

    [Fact]
    public void WintersGrip_ChilledEnemies_CantCritComboOrCounter()
    {
        var c = Kit();
        CombatantSpec Brute() { var u = Hitter("brute", 50); u.Stats.Add(new StatMod(StatId.CritChance, 100_000)).Add(new StatMod(StatId.ComboChance, 95)).Add(new StatMod(StatId.CounterChance, 95)); return u; }
        var held = Fight(c, Worm("k_Chill1", "mythic_winters_grip"), 4, Brute());
        Assert.DoesNotContain(held.Log.Where(e => e.Round > 1), e => e.Actor == 1 && (e.Crit || e.Action is BattleAction.Combo or BattleAction.Counter));
        Assert.Contains(Fight(c, Worm("k_Chill1"), 4, Brute()).Log, e => e.Actor == 1 && e.Crit);
    }

    [Fact]
    public void ColdFusion_ChillStopsCooling_AndHeatPlusChillTake40More()
    {
        var c = Kit();
        Skill(c, "k_both_start", Apply(TriggerKind.OnBattleStart, StatusId.Heat, 3), Apply(TriggerKind.OnBattleStart, StatusId.Chill, 3));
        var r = Fight(c, Worm("k_both_start", "mythic_cold_fusion"), 1);
        Assert.Equal(3, StacksAfter(r, 1, StatusId.Heat, 1));
        Assert.Equal(140, Amounts(r, BattleAction.Basic, actor: 0)[0]);
        Assert.Equal(0, StacksAfter(Fight(c, Worm("k_both_start"), 1), 1, StatusId.Heat, 1)); // without: the Chill cooled it
    }

    // ---- Lightning ---------------------------------------------------------------------------------------------------

    [Fact]
    public void GroundLoop_ArcStrikesTheLastEnemy_WithBonus()
    {
        var c = Kit();
        Assert.Empty(Amounts(Fight(c, Worm("sig_lightning"), 2), BattleAction.Skill, "sig_lightning"));
        Assert.Equal(new long[] { 75, 75 }, Amounts(Fight(c, Worm("sig_lightning", "light_ground_loop"), 2), BattleAction.Skill, "sig_lightning")); // 50 × 1.5 (tuned from +25%)
        Assert.Equal(90, Amounts(Fight(c, Leveled(Worm("sig_lightning", "light_ground_loop"), "light_ground_loop", 2), 1), BattleAction.Skill, "sig_lightning")[0]); // × 1.8
    }

    [Fact]
    public void Thunderclap_Every8thArcShocksAll_AndArcsHitMaxShockHarder()
    {
        var c = Kit();
        var r = Fight(c, Worm("sig_lightning", "light_thunderclap"), 8, Bag("a"), Bag("b"));
        Assert.Equal(new[] { 8, 8 }, r.Log.Where(e => e.Action == BattleAction.StatusApplied && e.SkillId == "light_thunderclap").Select(e => e.Round));
        // Arcs at a max-Shock enemy: +25% (Shock 5 on everyone; the Arc's own damage also gets Shock's +30% taken)
        Skill(c, "k_shock_all", Apply(TriggerKind.OnBattleStart, StatusId.Shock, 5, TargetKind.AllEnemies));
        long plain = Amounts(Fight(c, Worm("k_shock_all", "sig_lightning"), 1, Bag("a"), Bag("b")), BattleAction.Skill, "sig_lightning")[0];
        long clap = Amounts(Fight(c, Worm("k_shock_all", "sig_lightning", "light_thunderclap"), 1, Bag("a"), Bag("b")), BattleAction.Skill, "sig_lightning")[0];
        Assert.Equal(65, plain);                 // 50 × 1.3
        Assert.Equal(81, clap);                  // 50 × 1.3 × 1.25 = 81.25
    }

    [Fact]
    public void Thunderhead_BasicHitsEveryEnemy_At60Pct()
    {
        var r = Fight(Kit(), Worm("mythic_thunderhead"), 1, Bag("a"), Bag("b"), Bag("c"));
        Assert.Equal(new long[] { 60, 60, 60 }, Amounts(r, BattleAction.Basic, actor: 0));
    }

    [Fact]
    public void Superconductor_ArcsSeekChilled_AndShockCountsDoubleOnThem()
    {
        var c = Kit();
        Skill(c, "k_chill_b", new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.ApplyStatus, Status = StatusId.Chill, Target = TargetKind.AllOtherEnemies });
        var r = Fight(c, Worm("k_chill_b", "sig_lightning", "mythic_superconductor"), 4, Bag("a"), Bag("b"), Bag("c"));
        var jumps = r.Log.Where(e => e.Action == BattleAction.Skill && e.SkillId == "sig_lightning").Select(e => e.Target).ToList();
        Assert.All(jumps, t => Assert.NotEqual(1, t)); // b and c are chilled, a (the front) is not: arcs never pick … the front anyway
        Skill(c, "k_cs_start", Apply(TriggerKind.OnBattleStart, StatusId.Chill), Apply(TriggerKind.OnBattleStart, StatusId.Shock, 5));
        Assert.Equal(160, Amounts(Fight(c, Worm("k_cs_start", "mythic_superconductor"), 1), BattleAction.Basic, actor: 0)[0]);
    }

    [Fact]
    public void Capacitor_ShockNeverExpires_AndDischargesAtFive()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_Shock2", "mythic_capacitor"), 3);
        Assert.Equal(new long[] { 150 }, Amounts(r, BattleAction.Skill, "mythic_capacitor")); // 2, 4, 5 → 5 × 30%
        Assert.Equal(new[] { 3 }, Rounds(r, BattleAction.Skill, "mythic_capacitor"));
        Skill(c, "k_shock_start", Apply(TriggerKind.OnBattleStart, StatusId.Shock));
        Assert.DoesNotContain(Fight(c, Worm("k_shock_start", "mythic_capacitor"), 6).Log, e => e.Action == BattleAction.StatusExpired);
        Assert.Contains(Fight(c, Worm("k_shock_start"), 6).Log, e => e.Action == BattleAction.StatusExpired);
    }

    // ---- Combo -------------------------------------------------------------------------------------------------------

    [Fact]
    public void BattleRhythm_EachComboRampsAtk()
    {
        var w = With(Worm("combo_rhythm", "k_no_decay"), StatId.ComboChance, 95);
        var r = Fight(Kit(), w, 1);
        var combos = Amounts(r, BattleAction.Combo, actor: 0);
        Assert.True(combos.Count >= 2);
        Assert.Equal(60, combos[0]);
        Assert.Equal((long)Math.Round(60 * 1.025, MidpointRounding.AwayFromZero), combos[1]); // +2.5% after the first
    }

    [Fact]
    public void Whirlwind_Every4thComboStrike_SweepsAll()
    {
        var w = With(Worm("combo_whirlwind", "k_no_decay"), StatId.ComboChance, 95);
        var r = Fight(Kit(), w, 3, Bag("a"), Bag("b"));
        int combos = r.Log.Count(e => e.Action == BattleAction.Combo && e.Actor == 0);
        Assert.Equal(combos / 4 * 2, Amounts(r, BattleAction.Skill, "combo_whirlwind").Count);
        Assert.All(Amounts(r, BattleAction.Skill, "combo_whirlwind"), a => Assert.Equal(100, a));
    }

    [Fact]
    public void EndlessChain_NoDecay_Falloff_CappedAtSixStrikes()
    {
        var w = With(Worm("mythic_endless_chain"), StatId.ComboChance, 300);
        var r = Fight(Kit(), w, 20);
        var turns = r.Log.Where(e => e.Actor == 0 && e.Action == BattleAction.Combo).GroupBy(e => e.Round).ToList();
        Assert.All(turns, g => Assert.True(g.Count() <= 6));
        Assert.Contains(turns, g => g.Count() >= 4);
        var t = turns.First(g => g.Count() >= 2).ToList();
        Assert.Equal(51, t[0].Amount); // 60 × 0.85
        Assert.Equal(43, t[1].Amount); // 60 × 0.85²
    }

    [Fact]
    public void FuryChain_ThreeComboStrikes_UnleashAFreeRageAttack()
    {
        var w = With(Worm("mythic_fury_chain", "k_no_decay"), StatId.ComboChance, 95);
        var r = Fight(Kit(), w, 2);
        Assert.Contains(r.Log, e => e.Action == BattleAction.Rage && e.Actor == 0);
        Assert.Equal(0, r.Fighters[0].EndRage); // no rage was spent: it was free
        foreach (var round in r.Log.Where(e => e.Action == BattleAction.Rage && e.Actor == 0).Select(e => e.Round))
            Assert.True(r.Log.Count(e => e.Round == round && e.Action == BattleAction.Combo && e.Actor == 0) >= 3);
    }

    [Fact]
    public void PerfectForm_ComboChanceCounts_AsCounterChance_AndCountersCombo()
    {
        var w = With(Worm("mythic_perfect_form"), StatId.ComboChance, 95);
        var r = Fight(Kit(), w, 4, Hitter());
        int i = r.Log.FindIndex(e => e.Action == BattleAction.Counter && e.Actor == 0);
        Assert.True(i >= 0);
        Assert.Contains(r.Log.Skip(i + 1).TakeWhile(e => e.Actor == 0), e => e.Action == BattleAction.Combo);
    }

    // ---- Counter -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Grudge_EachHitTaken_ChargesTheNextCounter()
    {
        var w = With(Worm("counter_grudge"), StatId.CounterChance, 95);
        var r = Fight(Kit(), w, 1, Hitter());
        Assert.Equal(98, r.Log.First(e => e.Action == BattleAction.Counter && e.Actor == 0).Amount); // 70 × 1.4 (tuned from +25%)
    }

    [Fact]
    public void ReboundPlating_CountersGrantAShield_OfTheirDamage()
    {
        var w = With(Worm("counter_rebound"), StatId.CounterChance, 95);
        var r = Fight(Kit(), w, 1, Hitter());
        Assert.Equal(new long[] { 28 }, Amounts(r, BattleAction.Shield, "counter_rebound")); // 40% of a 70 counter (tuned from 25%)
    }

    [Fact]
    public void MirrorCarapace_FirstHitEachRound_ReflectsToAll_ButYouTakeMore()
    {
        var r = Fight(Kit(), Worm("counter_mirror"), 2, Hitter("x", 40), Hitter("y", 40));
        Assert.All(r.Log.Where(e => e.Action == BattleAction.Basic && e.Target == 0), e => Assert.Equal(48, e.Amount)); // +20% taken
        Assert.Equal(new long[] { 48, 48, 48, 48 }, Amounts(r, BattleAction.Skill, "counter_mirror")); // first hit each round, to both
    }

    [Fact]
    public void Bastion_NoTurnAttacks_CountersEveryHit_AtPlus50()
    {
        var r = Fight(Kit(), Worm("mythic_bastion"), 3, Hitter());
        Assert.DoesNotContain(r.Log, e => e.Actor == 0 && e.Action is BattleAction.Basic or BattleAction.Combo);
        Assert.All(Amounts(r, BattleAction.Counter, actor: 0), a => Assert.Equal(105, a));
        Assert.Equal(r.Log.Count(e => e.Actor == 1 && e.Action is BattleAction.Basic or BattleAction.Rage), Amounts(r, BattleAction.Counter, actor: 0).Count);
    }

    [Fact]
    public void LastWord_OnceAFatalHitLeavesOneHp_AndYouCounterEveryHit()
    {
        var w = Worm("mythic_last_word"); w.StartHp = 100;
        var r = Fight(Kit(), w, 3, Hitter("x", 300), Hitter("y", 10));
        var save = r.Log.Single(e => e.Action == BattleAction.CheatDeath);
        Assert.Equal(1, save.TargetHp);
        int i = r.Log.IndexOf(save);
        Assert.Contains(r.Log.Skip(i), e => e.Action == BattleAction.Counter && e.Actor == 0 && e.Round == save.Round);
        Assert.Equal(BattleSide.Enemy, r.Winner); // once per battle
    }

    // ---- Alien -------------------------------------------------------------------------------------------------------

    private static CombatantSpec Alien(string id, double speed = 100) => Unit(id, CombatRole.Alien, 100, 20, speed);

    [Fact]
    public void Hivemind_EachAlienTriggersItsTunedSignature()
    {
        var r = Fight(Kit(), new[] { Alien("a0"), Alien("a1"), Worm("alien_hivemind", "sig_bleed", "sig_radiation") }, 1);
        Assert.Contains(r.Log, e => e.Actor == 0 && e.SkillId == "sig_bleed" && e.Status == StatusId.Bleed);
        Assert.Contains(r.Log, e => e.Actor == 1 && e.SkillId == "sig_radiation" && e.Status == StatusId.Poison);
    }

    [Fact]
    public void Decoy_BlockedAttackers_MayBeCharmed()
    {
        int charms = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var setup = new BattleSetup { RoundCap = 2 };
            var w = Leveled(Leveled(Worm("alien_bodyguard", "alien_decoy"), "alien_bodyguard", 3), "alien_decoy", 2);
            setup.Player.AddRange(new[] { Alien("a0", 1), w });
            setup.Enemy.Add(Hitter());
            var r = BattleSimulator.Simulate(setup, Kit(), new Rng(seed));
            charms += r.Log.Count(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Charm && e.SkillId == "alien_decoy");
            Assert.Contains(r.Log, e => e.Action == BattleAction.Block);
        }
        Assert.InRange(charms, 2, 12); // 30% of the blocked hits (CC pass: 20% → 30%)
    }

    [Fact]
    public void SwarmLink_SymbioteAndSacrifice()
    {
        var c = Kit();
        var link = Fight(c, new[] { Alien("a0", 1), Alien("a1", 1), Worm("mythic_swarm_link") }, 1, Bag("a", 50), Bag("b"));
        Assert.Equal(2, link.Log.Count(e => e.Action == BattleAction.SidekickAct));

        var sym = Fight(c, new[] { Alien("a0"), Alien("a1"), Raging("mythic_symbiote") }, 2);
        Assert.Equal(new[] { 1, 1 }, sym.Log.Where(e => e.Action == BattleAction.SidekickAct).Select(e => e.Round)); // only with the rage attack

        var w = Worm("mythic_sacrifice"); w.StartHp = 100;
        var sac = Fight(c, new[] { Alien("a0", 1), w }, 1, Hitter("x", 500));
        Assert.Single(sac.Log, e => e.Action == BattleAction.Sacrifice);
        Assert.Equal(3001, sac.Fighters[1].EndHp);
        Assert.False(sac.Fighters[0].Alive);
    }

    // ---- General -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Cull_KillsNonBossesBelowItsThreshold()
    {
        var c = Kit();
        Assert.Equal(9, Fight(c, Worm("gen_cull"), 10, Bag("a", 1000)).Log.Single(e => e.Action == BattleAction.Execute).Round);
        Assert.Equal(8, Fight(c, Leveled(Worm("gen_cull"), "gen_cull", 2), 10, Bag("a", 1000)).Log.Single(e => e.Action == BattleAction.Execute).Round); // 24%: 200 HP left
        Assert.DoesNotContain(Fight(c, Worm("gen_cull"), 10, Bag("b", 1000, CombatRole.Boss)).Log, e => e.Action == BattleAction.Execute);
    }

    [Fact]
    public void OverclockedCore_MoreAtk_LessMaxHp()
    {
        var r = Fight(Kit(), Worm("gen_overclocked"), 1);
        Assert.Equal(125, Amounts(r, BattleAction.Basic, actor: 0)[0]);
        Assert.Equal(8500, r.Fighters[0].MaxHp);
    }

    [Fact]
    public void Overkill_Catalyst_LastLight()
    {
        var c = Kit();
        Assert.Equal(new long[] { 70 }, Amounts(Fight(c, Worm("mythic_overkill"), 1, Bag("a", 30), Bag("b")), BattleAction.Skill, "mythic_overkill"));

        var cat = Fight(c, Worm("k_Bleed1", "k_Poison1", "k_Chill1", "mythic_catalyst"), 2);
        Assert.Equal(2, StacksAfter(cat, 1, StatusId.Bleed, 1)); // 3 statuses after the kits: +1 each
        Assert.Equal(4, StacksAfter(cat, 1, StatusId.Bleed, 2)); // again in round 2 (once per round)

        var ll = Worm("mythic_last_light", "k_heal"); ll.StartHp = 2000;
        var lr = Fight(c, ll, 2);
        Assert.All(lr.Log.Where(e => e.Action == BattleAction.Basic && e.Actor == 0), e => Assert.True(e.Crit));
        Assert.Empty(Amounts(lr, BattleAction.Heal, "k_heal"));
    }
}
