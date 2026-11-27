using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Run.BattleLab;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Skill redesign, step 2: status rules owned by the applier (R6), new effects and triggers (R7 / R8), turn-scoped buffs (R9),
/// combo loop settings (R11) and sidekick hooks (R12), each in a seeded BattleLab fight with exact numbers.
/// </summary>
public class EngineRuleTests
{
    private const TriggerKind Hit = TriggerKind.OnHit;

    private static int Bag0(BattleResult r) => Index(r, "bag");

    // ---- R6: status rules owned by the applier -----------------------------------------------------------------------

    [Fact]
    public void SupernovaThreshold_And_NoRecoil_AreTheAppliersRules()
    {
        var c = new ContentDb();
        var plain = Skill(c, "plain", Apply(Hit, StatusId.Heat, 4));
        var burn = Skill(c, "burn", Apply(Hit, StatusId.Heat, 4), Rule("burst_at:12", StatusId.Heat), Rule("no_recoil", StatusId.Heat));
        var a = Fight(c, Worm(plain), 3);
        var b = Fight(c, Worm(burn), 3);
        // Heat 4, 8, 12: the default blows at 10 (third hit) and singes the worm (12% of 10 000 = 1 200)
        Assert.Equal(3, a.Log.First(e => e.Action == BattleAction.Supernova && e.Amount == 0).Round);
        Assert.Contains(a.Log, e => e.Action == BattleAction.Supernova && e.Target == 0 && e.Amount == 1200);
        // with the rule it needs 12 (also the third hit here: 12 ≥ 12), deals 250% ATK = 250 and never hurts the worm
        var mark = b.Log.First(e => e.Action == BattleAction.Supernova && e.Amount == 0);
        Assert.Equal(12, mark.Stacks);
        Assert.Contains(b.Log, e => e.Action == BattleAction.Supernova && e.Target == Bag0(b) && e.Amount == 250);
        Assert.DoesNotContain(b.Log, e => e.Action == BattleAction.Supernova && e.Target == 0);
        // Heat ×4 per hit with burst at 12: 4, 8 — not yet at 10
        var c2 = Fight(c, Worm(burn), 2);
        Assert.Equal(8, StacksAfter(c2, Bag0(c2), StatusId.Heat, 2));
        Assert.DoesNotContain(c2.Log, e => e.Action == BattleAction.Supernova);
    }

    [Fact]
    public void RaisedCap_And_KeptShields_ForBleed()
    {
        var c = new ContentDb();
        var hemo = Skill(c, "hemo", Apply(Hit, StatusId.Bleed), Rule("max", StatusId.Bleed, 5), Rule("keeps_shields", StatusId.Bleed));
        var r = Fight(c, Worm(hemo), 17);
        Assert.Equal(15, StacksAfter(r, Bag0(r), StatusId.Bleed, 17));
        // a shielded bag: Bleed normally ignores shields; with keeps_shields the tick is absorbed
        var plain = Skill(c, "plainb", Apply(Hit, StatusId.Bleed));
        CombatantSpec Shielded() { var b = Bag(); b.StartShield = 1_000_000; return b; }
        var p = Fight(c, Worm(plain), 3, Shielded());
        var k = Fight(c, Worm(hemo), 3, Shielded());
        Assert.True(p.Log.First(e => e.Action == BattleAction.StatusTick).TargetHp < 1_000_000);
        Assert.Equal(1_000_000, k.Log.First(e => e.Action == BattleAction.StatusTick).TargetHp);
    }

    [Fact]
    public void DotRules_CritAndDouble_AndConsumeHealBoost()
    {
        var c = new ContentDb();
        var crit = Skill(c, "crit", Apply(Hit, StatusId.Poison, 10), Rule("dot_crit", StatusId.Poison));
        var w = Worm(crit); w.Stats.Add(new StatMod(StatId.CritChance, 100_000));
        var r = Fight(c, w, 2);
        // 10 Radiation × 4% × 100 ATK = 40, crit ×1.5 = 60
        var tick = r.Log.First(e => e.Action == BattleAction.StatusTick);
        Assert.True(tick.Crit);
        Assert.Equal(60, tick.Amount);

        var dbl = Skill(c, "dbl", Apply(Hit, StatusId.Bleed, 5), Rule("dot", StatusId.Bleed, 100));
        // 5 Bleed × 6% × 100 = 30, double = 60
        Assert.Equal(60, Fight(c, Worm(dbl), 2).Log.First(e => e.Action == BattleAction.StatusTick).Amount);

        var heal = Skill(c, "heal", Apply(Hit, StatusId.Bleed, 2), On(Hit, EffectKind.ConsumeStatus, 25, status: StatusId.Bleed, param: "heal:50"),
            Rule("consume_heal", StatusId.Bleed, 100));
        var hw = Worm(heal); hw.StartHp = 5000;
        // 2 × 25 = 50 damage, heal 50% = 25, doubled = 50
        Assert.Equal(new long[] { 50 }, Amounts(Fight(c, hw, 1), BattleAction.Heal, heal));
    }

    [Fact]
    public void Chill_NoExpiry_NoCooling_Shutdown()
    {
        var c = new ContentDb();
        var keep = Skill(c, "keep", Apply(TriggerKind.OnBattleStart, StatusId.Chill, 5), Rule("no_expire:5", StatusId.Chill));
        var fade = Skill(c, "fade", Apply(TriggerKind.OnBattleStart, StatusId.Chill, 5));
        Assert.Contains(Fight(c, Worm(fade), 6).Log, e => e.Action == BattleAction.StatusExpired && e.Status == StatusId.Chill);
        Assert.DoesNotContain(Fight(c, Worm(keep), 6).Log, e => e.Action == BattleAction.StatusExpired && e.Status == StatusId.Chill);

        // Cold Fusion's rule: Chill stops cooling Heat
        var cool = Skill(c, "cool", Apply(Hit, StatusId.Heat, 3), Apply(Hit, StatusId.Chill, 1));
        var warm = Skill(c, "warm", Apply(Hit, StatusId.Heat, 3), Apply(Hit, StatusId.Chill, 1), Rule("no_cool", StatusId.Chill));
        var rc = Fight(c, Worm(cool), 2); var rw = Fight(c, Worm(warm), 2);
        Assert.Equal(4, StacksAfter(rc, Bag0(rc), StatusId.Heat, 2));
        Assert.Equal(6, StacksAfter(rw, Bag0(rw), StatusId.Heat, 2));

        // Winter's Grip: a chilled enemy with 100% crit / 95% combo / counter does none of them
        var grip = Skill(c, "grip", Apply(TriggerKind.OnBattleStart, StatusId.Chill, 5), Rule("no_expire:1", StatusId.Chill), Rule("shutdown", StatusId.Chill));
        CombatantSpec Brute() { var u = Unit("brute", CombatRole.Enemy, 1_000_000, 50); u.Stats.Add(new StatMod(StatId.CritChance, 100_000)).Add(new StatMod(StatId.ComboChance, 95)).Add(new StatMod(StatId.CounterChance, 95)); return u; }
        var free = Fight(c, Worm(fade), 4, Brute());
        var held = Fight(c, Worm(grip), 4, Brute());
        Assert.Contains(free.Log, e => e.Actor == 1 && e.Crit);
        Assert.DoesNotContain(held.Log, e => e.Actor == 1 && (e.Crit || e.Action is BattleAction.Combo or BattleAction.Counter));
    }

    [Fact]
    public void Superconductor_ShockCountsDouble_OnChilled_AndArcsSeekThem()
    {
        var c = new ContentDb();
        var plain = Skill(c, "sc0", Apply(TriggerKind.OnBattleStart, StatusId.Chill, 1), Apply(TriggerKind.OnBattleStart, StatusId.Shock, 5));
        var dbl = Skill(c, "sc1", Apply(TriggerKind.OnBattleStart, StatusId.Chill, 1), Apply(TriggerKind.OnBattleStart, StatusId.Shock, 5), Rule("double_vs_chill", StatusId.Shock));
        // Shock 5 × 6% = +30% → 130; doubled on a chilled bag → 160
        Assert.Equal(130, Amounts(Fight(c, Worm(plain), 1), BattleAction.Basic, actor: 0)[0]);
        Assert.Equal(160, Amounts(Fight(c, Worm(dbl), 1), BattleAction.Basic, actor: 0)[0]);

        // Arc seeking: front bag hit, two others, only "cold" is chilled → every jump lands on it
        var seek = Skill(c, "seek", On(Hit, EffectKind.Damage, 50, TargetKind.OtherEnemies), Rule("arc_seek_chill"),
            Apply(TriggerKind.OnBattleStart, StatusId.Chill, 1, TargetKind.RandomEnemy));
        var r = Fight(c, Worm(seek), 3, Bag("front"), Bag("a"), Bag("b"));
        int chilled = r.Log.First(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Chill).Target;
        var jumps = r.Log.Where(e => e.Action == BattleAction.Skill && e.SkillId == seek).Select(e => e.Target).ToList();
        Assert.Equal(3, jumps.Count);
        if (chilled != Index(r, "front")) Assert.All(jumps, t => Assert.Equal(chilled, t));
    }

    // ---- owner rules (Arc, basic attack, overkill, Last Light) ------------------------------------------------------

    [Fact]
    public void GroundLoop_ArcStrikesTheLastEnemy_ThunderheadHitsAll_OverkillCarries()
    {
        var c = new ContentDb();
        var arc = Skill(c, "arc", On(Hit, EffectKind.Damage, 50, TargetKind.OtherEnemies));
        var loop = Skill(c, "loop", On(Hit, EffectKind.Damage, 50, TargetKind.OtherEnemies), Rule("arc_loop", value: 25));
        Assert.Empty(Amounts(Fight(c, Worm(arc), 2), BattleAction.Skill, arc));
        Assert.Equal(new long[] { 63, 63 }, Amounts(Fight(c, Worm(loop), 2), BattleAction.Skill, loop)); // 50 × 1.25 = 62.5
        // Conductor's Arc damage stat: +40% → 70
        var coil = SkillWithStats(c, "coil", new[] { new StatMod(StatId.ArcDamage, 40) }, On(Hit, EffectKind.Damage, 50, TargetKind.OtherEnemies));
        Assert.Equal(70, Amounts(Fight(c, Worm(coil), 1, Bag("a"), Bag("b")), BattleAction.Skill, coil)[0]);

        var head = Skill(c, "head", Rule("basic_aoe", value: 60));
        var r = Fight(c, Worm(head), 1, Bag("a"), Bag("b"), Bag("c"));
        Assert.Equal(new long[] { 60, 60, 60 }, Amounts(r, BattleAction.Basic, actor: 0));

        var over = Skill(c, "over", Rule("overkill"));
        var o = Fight(c, Worm(over), 1, Bag("a", 30), Bag("b"));
        Assert.Equal(new long[] { 70 }, Amounts(o, BattleAction.Skill, over)); // 100 − 30 carries to the next bag
    }

    [Fact]
    public void LastLight_BelowItsThreshold_EveryHitCrits_AndNoHealing()
    {
        var c = new ContentDb();
        var ll = Skill(c, "ll", Rule("last_light:30"), On(Hit, EffectKind.Heal, 10, TargetKind.Self));
        var w = Worm(ll); w.StartHp = 2000;
        var r = Fight(c, w, 2);
        Assert.All(r.Log.Where(e => e.Action == BattleAction.Basic && e.Actor == 0), e => { Assert.True(e.Crit); Assert.Equal(150, e.Amount); });
        Assert.Empty(Amounts(r, BattleAction.Heal, ll));
        Assert.Contains(Fight(c, Worm(ll), 1).Log, e => e.Action == BattleAction.Basic && !e.Crit); // above 30%: as usual
    }

    // ---- R7 effects --------------------------------------------------------------------------------------------------

    [Fact]
    public void ForceSupernova_Multiply_Transfer_Execute()
    {
        var c = new ContentDb();
        var flash = Skill(c, "flash", Apply(Hit, StatusId.Heat, 3), On(Hit, EffectKind.ForceSupernova, 25, @if: "has:Heat:6"));
        var r = Fight(c, Worm(flash), 2);
        var mark = r.Log.Single(e => e.Action == BattleAction.Supernova && e.Amount == 0);
        Assert.Equal((2, 6, flash), (mark.Round, mark.Stacks, mark.SkillId));
        Assert.Contains(r.Log, e => e.Action == BattleAction.Supernova && e.Target == Bag0(r) && e.Amount == 313); // 250 × 1.25

        var mult = Skill(c, "mult", Apply(Hit, StatusId.Poison, 10), On(Hit, EffectKind.MultiplyStatus, 200, status: StatusId.Poison, param: "cap:5"));
        var m = Fight(c, Worm(mult), 2);
        Assert.Equal(15, StacksAfter(m, Bag0(m), StatusId.Poison, 1));  // 10 → doubled but at most +5
        Assert.Equal(30, StacksAfter(m, Bag0(m), StatusId.Poison, 2));  // 25 → 30

        var fis = Skill(c, "fis", Apply(Hit, StatusId.Poison, 10),
            On(TriggerKind.OnEnemyDeath, EffectKind.TransferStatus, 50, TargetKind.AllOtherEnemies, "has:Poison", StatusId.Poison));
        // "a" (150 HP) takes 100 + Radiation ×10 in round 1 (its tick: 40), dies to the second hit: half its 10 stacks spread
        var f = Fight(c, Worm(fis), 2, Bag("a", 150), Bag("b"), Bag("c"));
        Assert.Equal(5, StacksAfter(f, Index(f, "c"), StatusId.Poison, 2));
        Assert.Equal(15, StacksAfter(f, Index(f, "b"), StatusId.Poison, 2)); // + the hit's own ×10 on the new front

        var cull = Skill(c, "cull", On(Hit, EffectKind.Execute, 15));
        var x = Fight(c, Worm(cull), 10, Bag("a", 1000));
        Assert.Equal(9, x.Log.Single(e => e.Action == BattleAction.Execute).Round); // 100 HP left = 10% < 15%
        Assert.Empty(Fight(c, Worm(cull), 9, Bag("boss", 1000, CombatRole.Boss)).Log.Where(e => e.Action == BattleAction.Execute));
    }

    [Fact]
    public void Charge_LoseHp_StackAll_Reflect_ShieldOfAmount()
    {
        var c = new ContentDb();
        // Grudge-style: each hit taken charges the next counter by +50%; the counter (70% Slam counter) deals 70 × (1 + 0.5 × hits)
        var grudge = Skill(c, "grudge", On(TriggerKind.OnDamaged, EffectKind.Charge, 50, TargetKind.Self));
        var w = Worm(grudge); w.Stats.Add(new StatMod(StatId.CounterChance, 95));
        var r = Fight(c, w, 1, Unit("hitter", CombatRole.Enemy, 1_000_000, 10));
        var counter = r.Log.First(e => e.Action == BattleAction.Counter && e.Actor == 0);
        Assert.Equal(105, counter.Amount); // one hit taken → +50%

        var pact = Skill(c, "pact", On(TriggerKind.OnTurnStart, EffectKind.LoseHp, 3, TargetKind.Self));
        Assert.Equal(new long[] { 300, 300 }, Amounts(Fight(c, Worm(pact), 2), BattleAction.Skill, pact));

        var cat = Skill(c, "cat", Apply(TriggerKind.OnBattleStart, StatusId.Bleed), Apply(TriggerKind.OnBattleStart, StatusId.Poison),
            On(Hit, EffectKind.StackAll, 0, @if: "statuses:2"), On(Hit, EffectKind.StackAll, 0, @if: "statuses:2"));
        var s = Fight(c, Worm(cat), 2);
        Assert.Equal(3, StacksAfter(s, Bag0(s), StatusId.Bleed, 2)); // +1 per round, the second trigger in the same round adds nothing
        Assert.Equal(3, StacksAfter(s, Bag0(s), StatusId.Poison, 2));

        var mirror = Skill(c, "mirror", On(TriggerKind.OnDamaged, EffectKind.Reflect, 100, TargetKind.AllEnemies, "first_hit_taken"));
        var m = Fight(c, Worm(mirror), 1, Unit("x", CombatRole.Enemy, 1_000_000, 40), Unit("y", CombatRole.Enemy, 1_000_000, 40));
        Assert.Equal(new long[] { 40, 40 }, Amounts(m, BattleAction.Skill, mirror)); // the first 40-damage hit, to both

        var rebound = Skill(c, "rebound", On(TriggerKind.OnCounter, EffectKind.Shield, 25, TargetKind.Self, param: "of_amount"));
        var rw = Worm(rebound); rw.Stats.Add(new StatMod(StatId.CounterChance, 95));
        var rr = Fight(c, rw, 1, Unit("hitter", CombatRole.Enemy, 1_000_000, 10));
        var cnt = rr.Log.First(e => e.Action == BattleAction.Counter && e.Actor == 0);
        Assert.Equal((long)Math.Round(cnt.Amount * 0.25, MidpointRounding.AwayFromZero), Amounts(rr, BattleAction.Shield, rebound)[0]);
    }

    // ---- R8 triggers -------------------------------------------------------------------------------------------------

    [Fact]
    public void NewTriggers_ShieldHit_Fatal_Block_Consumed_TurnEnd_ArcJump()
    {
        var c = new ContentDb();
        // OnShieldHit: the amount is what the shield absorbed (40 of a 40 hit)
        var spike = Skill(c, "spike", On(TriggerKind.OnShieldHit, EffectKind.Reflect, 50));
        var w = Worm(spike); w.StartShield = 1000;
        Assert.Equal(new long[] { 20 }, Amounts(Fight(c, w, 1, Unit("x", CombatRole.Enemy, 1_000_000, 40)), BattleAction.Skill, spike));

        // OnFatal once per battle: survive at 1 HP
        var last = Skill(c, "last", new SkillTrigger { When = TriggerKind.OnFatal, Effect = EffectKind.CheatDeath, Target = TargetKind.Self, MaxPerBattle = 1 });
        var lw = Worm(last); lw.StartHp = 100;
        var lr = Fight(c, lw, 3, Unit("x", CombatRole.Enemy, 1_000_000, 500));
        var save = lr.Log.Single(e => e.Action == BattleAction.CheatDeath);
        Assert.Equal(1, save.TargetHp);
        Assert.Equal(BattleSide.Enemy, lr.Winner); // the second fatal hit kills
        Assert.Equal(2, lr.Rounds);

        // OnStatusConsumed with a per-stack conversion (Fallout Wounds): 2 Bleed consumed → 6 Radiation
        var conv = Skill(c, "conv", Apply(Hit, StatusId.Bleed, 2), On(Hit, EffectKind.ConsumeStatus, 10, status: StatusId.Bleed),
            Apply(TriggerKind.OnStatusConsumed, StatusId.Poison, 3, param: "Bleed;per_unit"));
        var cr = Fight(c, Worm(conv), 1);
        Assert.Equal(6, StacksAfter(cr, Bag0(cr), StatusId.Poison, 1));

        // OnTurnEnd every 3rd turn
        var end = Skill(c, "end", On(TriggerKind.OnTurnEnd, EffectKind.Damage, 10, everyN: 3));
        Assert.Equal(new[] { 3, 6 }, Rounds(Fight(c, Worm(end), 6), BattleAction.Skill, end));

        // OnArcJump every 2nd jump
        var jumps = Skill(c, "jumps", On(Hit, EffectKind.Damage, 10, TargetKind.OtherEnemies), On(TriggerKind.OnArcJump, EffectKind.ApplyStatus, 0, TargetKind.AllEnemies, status: StatusId.Shock, everyN: 2));
        var jr = Fight(c, Worm(jumps), 4, Bag("a"), Bag("b"));
        Assert.Equal(new[] { 2, 2, 4, 4 }, jr.Log.Where(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Shock).Select(e => e.Round));
    }

    // ---- R9: turn-scoped and resetting buffs ----------------------------------------------------------------------------

    [Fact]
    public void UntilTurnBuffs_EndAtTheNextTurn_AndRhythmResetsWithoutACombo()
    {
        var c = new ContentDb();
        // a +20% ATK buff gained when hit lasts until the worm's next turn: the enemy hits after the worm, so every basic is 120
        var rally = Skill(c, "rally", new SkillTrigger { When = TriggerKind.OnDamaged, Effect = EffectKind.BuffStat, Target = TargetKind.Self, Stat = new StatMod(StatId.Atk, 0, 20), Param = "until_turn" });
        var r = Fight(c, Worm(rally), 3, Unit("x", CombatRole.Enemy, 1_000_000, 10));
        Assert.Equal(new long[] { 100, 100, 100 }, Amounts(r, BattleAction.Basic, actor: 0)); // removed at turn start, before the attack
        // a counter fired inside the enemy's turn still gets it (it lasts until the worm's own next turn)
        var w = Worm(rally); w.Stats.Add(new StatMod(StatId.CounterChance, 95));
        var rc = Fight(c, w, 1, Unit("x", CombatRole.Enemy, 1_000_000, 10));
        Assert.Equal(84, rc.Log.First(e => e.Action == BattleAction.Counter && e.Actor == 0).Amount); // 70 × 1.2

        // Battle Rhythm: +10% per combo, reset at the end of a turn without a combo
        var rhythm = Skill(c, "rhythm", new SkillTrigger { When = TriggerKind.OnCombo, Effect = EffectKind.BuffStat, Target = TargetKind.Self, Stat = new StatMod(StatId.Atk, 0, 10), Param = "reset_no_combo" });
        var rw = Worm(rhythm); rw.Stats.Add(new StatMod(StatId.ComboChance, 50));
        var rr = Fight(c, rw, 12);
        foreach (var round in rr.Log.Where(e => e.Action == BattleAction.Basic && e.Actor == 0))
        {
            bool comboBefore = rr.Log.Any(e => e.Round == round.Round - 1 && e.Action == BattleAction.Combo && e.Actor == 0);
            if (!comboBefore) Assert.Equal(100, round.Amount); // reset: the previous turn had no combo
        }
        Assert.Contains(rr.Log, e => e.Action == BattleAction.Basic && e.Actor == 0 && e.Amount > 100);
    }

    // ---- R11: combo loop settings ------------------------------------------------------------------------------------

    [Fact]
    public void ComboLoop_NoDecay_Falloff_Cap_AndProcCap()
    {
        var c = new ContentDb();
        var chain = Skill(c, "chain", Rule("combo_no_decay"), Rule("combo_falloff", value: 15), Rule("combo_cap:80"), Rule("proc_cap:6"),
            On(Hit, EffectKind.Damage, 1));
        var w = Worm(chain); w.Stats.Add(new StatMod(StatId.ComboChance, 500));
        var r = Fight(c, w, 30);
        var turns = r.Log.Where(e => e.Actor == 0 && e.Action == BattleAction.Combo).GroupBy(e => e.Round).ToList();
        Assert.All(turns, g => Assert.True(g.Count() <= c.Combat.MaxChain));
        var longest = turns.OrderByDescending(g => g.Count()).First().ToList();
        Assert.True(longest.Count >= 4, longest.Count.ToString()); // 80% without decay chains long
        // each strike deals 15% less than the last: 60 × 0.85^k
        for (int k = 0; k < longest.Count; k++) Assert.Equal((long)Math.Round(60 * Math.Pow(0.85, k + 1), MidpointRounding.AwayFromZero), longest[k].Amount);
        // on-hit effects stop after the 6th strike of a turn (basic + up to 6 combos = 7 strikes)
        foreach (var round in turns.Where(g => g.Count() == 6).Select(g => g.Key))
            Assert.Equal(6, r.Log.Count(e => e.Round == round && e.Action == BattleAction.Skill && e.SkillId == chain));
        // the 80% cap: over 30 turns the chain does not reach 6 every time
        Assert.Contains(turns, g => g.Count() < 6);
    }

    [Fact]
    public void ComboAsCounter_AndCounterCombos()
    {
        var c = new ContentDb();
        var form = Skill(c, "form", Rule("combo_as_counter"), Rule("counter_combo"));
        var w = Worm(form); w.Stats.Add(new StatMod(StatId.ComboChance, 95));
        var r = Fight(c, w, 4, Unit("x", CombatRole.Enemy, 1_000_000, 10));
        int counter = r.Log.FindIndex(e => e.Action == BattleAction.Counter && e.Actor == 0);
        Assert.True(counter >= 0, string.Join("\n", r.Log)); // counter chance 0 + combo chance 95
        Assert.Equal(BattleAction.Combo, r.Log[counter + 1].Action); // the counter combos
        var plain = Worm(); plain.Stats.Add(new StatMod(StatId.ComboChance, 95));
        Assert.DoesNotContain(Fight(c, plain, 4, Unit("x", CombatRole.Enemy, 1_000_000, 10)).Log, e => e.Action == BattleAction.Counter);
    }

    [Fact]
    public void NoTurnAttack_CounterAll()
    {
        var c = new ContentDb();
        var bastion = Skill(c, "bastion", Rule("no_turn_attack"), Rule("counter_all"), Cond("counter", 50));
        var r = Fight(c, Worm(bastion), 3, Unit("x", CombatRole.Enemy, 1_000_000, 10));
        Assert.DoesNotContain(r.Log, e => e.Actor == 0 && e.Action == BattleAction.Basic);
        var counters = Amounts(r, BattleAction.Counter, actor: 0);
        Assert.Equal(r.Log.Count(e => e.Actor == 1 && e.Action is BattleAction.Basic or BattleAction.Rage), counters.Count); // every hit …
        Assert.All(counters, a => Assert.Equal(105, a)); // … at 70 × 1.5
    }

    // ---- R12: sidekick hooks -----------------------------------------------------------------------------------------

    private static CombatantSpec Alien(string id, double speed = 100) { var a = Unit(id, CombatRole.Alien, 100, 20, speed); return a; }

    [Fact]
    public void Hivemind_TunesEachAlienToOneSignature_RoundRobin()
    {
        var c = new ContentDb();
        var hive = Skill(c, "hive", Rule("hivemind"));
        var w = Worm(hive, "sig_fire", "sig_bleed"); w.Stats.Add(new StatMod(StatId.Atk, -100)); // the worm's own hits do nothing
        var r = Fight(c, new[] { Alien("a0"), Alien("a1"), Alien("a2"), w }, 1, Bag());
        int a0 = Index(r, "a0"), a1 = Index(r, "a1"), a2 = Index(r, "a2");
        var bySk = r.Log.Where(e => e.Action == BattleAction.StatusApplied && e.Actor != Index(r, "worm")).Select(e => (e.Actor, e.Status)).ToList();
        Assert.Contains((a0, StatusId.Heat), bySk);
        Assert.Contains((a1, StatusId.Bleed), bySk);
        Assert.Contains((a2, StatusId.Heat), bySk); // wraps around
        Assert.DoesNotContain((a0, StatusId.Bleed), bySk);
    }

    [Fact]
    public void Hivemind_Command_ActsAgainOncePerRound_AndArcJumps()
    {
        var c = new ContentDb();
        var hive = Skill(c, "hive2", Rule("hivemind"));
        var w = Worm(hive, "sig_alien", "sig_lightning");
        var r = Fight(c, new[] { Alien("a0"), Alien("a1"), w }, 2, Bag("x"), Bag("y"));
        int a0 = Index(r, "a0"), a1 = Index(r, "a1");
        // a0 (Command): its own action + one repeat per round; a1 (Arc): a 50% jump of its power
        Assert.True(r.Log.Count(e => e.Round == 1 && e.Action == BattleAction.SidekickAct && e.Actor == a0) >= 2);
        Assert.Contains(r.Log, e => e.Action == BattleAction.Skill && e.Actor == a1 && e.SkillId == "sig_lightning");
    }

    [Fact]
    public void Symbiote_SwarmLink_Bodyguard_Decoy_Sacrifice()
    {
        var c = new ContentDb();
        // parts v2 (Symbiote rework): the old "symbiote" rule does nothing any more; the aliens keep their own turns, the rage attack
        // also fires every alien and each one that joins loses 50 charge
        var sym = Skill(c, "sym", Rule("symbiote"), new SkillTrigger { When = TriggerKind.OnRageAttack, Effect = EffectKind.AllyAction, Param = "all;charge:-50", Target = TargetKind.Self });
        var w = Worm(sym); w.Stats.Add(new StatMod(StatId.RageGain, 100)); // back to normal rage gain
        var r = Fight(c, new[] { Alien("a0"), Alien("a1"), w }, 4, Bag());
        var acts = r.Log.Where(e => e.Action == BattleAction.SidekickAct).ToList();
        Assert.Equal(2, acts.Count(e => e.Round == 4 && e.Stacks == 1)); // the rage attack (round 4: 25 rage per turn) fires both
        Assert.Contains(acts, e => e.Stacks == 0);                       // and they still act on their own

        var link = Skill(c, "link", new SkillTrigger { When = TriggerKind.OnKill, Effect = EffectKind.AllyAction, Param = "all", Target = TargetKind.Self });
        var lr = Fight(c, new[] { Alien("a0", 1), Alien("a1", 1), Worm(link) }, 1, Bag("a", 50), Bag("b"));
        Assert.Equal(2, lr.Log.Count(e => e.Action == BattleAction.SidekickAct));

        var guard = Skill(c, "guard", new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.Block, Stacks = 2, Target = TargetKind.Self },
            new SkillTrigger { When = TriggerKind.OnBlock, Effect = EffectKind.ApplyStatus, Status = StatusId.Charm, Chance = 100, Target = TargetKind.Enemy });
        var gr = Fight(c, new[] { Alien("a0", 1), Worm(guard) }, 3, Unit("x", CombatRole.Enemy, 1_000_000, 10));
        Assert.Equal(2, gr.Log.Count(e => e.Action == BattleAction.Block)); // the first two hits are blocked, the attacker charmed
        Assert.Contains(gr.Log, e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Charm);

        var sac = Skill(c, "sac", new SkillTrigger { When = TriggerKind.OnFatal, Effect = EffectKind.Sacrifice, Value = 30, Target = TargetKind.Self, If = "sidekicks", MaxPerBattle = 1 });
        var sw = Worm(sac); sw.StartHp = 100;
        var sr = Fight(c, new[] { Alien("a0", 1), sw }, 1, Unit("x", CombatRole.Enemy, 1_000_000, 500));
        var gone = sr.Log.Single(e => e.Action == BattleAction.Sacrifice);
        Assert.Equal(Index(sr, "a0"), gone.Actor);
        Assert.Equal(3001, sr.Log.First(e => e.Action == BattleAction.Heal && e.SkillId == sac).TargetHp); // 1 HP + 30% of 10 000
        Assert.False(sr.Fighters[Index(sr, "a0")].Alive);
    }
}
