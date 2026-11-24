using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Run.BattleLab;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Skill redesign, step 1: each engine primitive in isolation in a seeded battle with exact numbers (worm ATK 100, punching bags
/// with DEF 0: a basic hit is exactly 100). R1 trigger gates, R2 BlockStatus, R3 per-stack / per-variety bonuses, R4 every-Nth
/// counters, R5 stack thresholds (and Exposed), partial consume, instant ticks, drain-into-buff, R15 card text.
/// </summary>
public class EnginePrimitiveTests
{
    private const TriggerKind Hit = TriggerKind.OnHit;

    // ---- R1: trigger gates ----------------------------------------------------------------------------------------

    [Fact]
    public void If_HasStacks_GatesTheTrigger_UntilTheTargetHasEnough()
    {
        var c = new ContentDb();
        var id = Skill(c, "gate", Apply(Hit, StatusId.Bleed), On(Hit, EffectKind.Damage, 50, @if: "has:Bleed:3"));
        var r = Fight(c, Worm(id), 5);
        // Bleed 1, 2, 3 …: the 50% strike fires from the third hit on, exactly 50 each
        Assert.Equal(new[] { 3, 4, 5 }, Rounds(r, BattleAction.Skill, id));
        Assert.All(Amounts(r, BattleAction.Skill, id), a => Assert.Equal(50, a));
    }

    [Fact]
    public void If_Max_NotBoss_HpBelow_AndSelfConditions()
    {
        var c = new ContentDb();
        // at max Bleed (10): +1 stack per hit → from round 10
        var max = Skill(c, "max", Apply(Hit, StatusId.Bleed), On(Hit, EffectKind.Damage, 10, @if: "max:Bleed"));
        Assert.Equal(10, Rounds(Fight(c, Worm(max), 11), BattleAction.Skill, max).First());

        // not_boss / boss pick the right enemy
        var boss = Skill(c, "boss", On(Hit, EffectKind.Damage, 10, @if: "boss"));
        var r = Fight(c, Worm(boss), 1, Bag("b", 1_000_000, CombatRole.Boss));
        Assert.Equal(new long[] { 10 }, Amounts(r, BattleAction.Skill, boss));
        Assert.Empty(Amounts(Fight(c, Worm(boss), 1, Bag()), BattleAction.Skill, boss));

        // hp_below: 1000 HP bag; 100 per hit → below 50% after the 6th hit (400 left), the 7th hit fires
        var low = Skill(c, "low", On(Hit, EffectKind.Damage, 1, @if: "hp_below:50"));
        Assert.Equal(6, Rounds(Fight(c, Worm(low), 6, Bag("b", 1000)), BattleAction.Skill, low).First());

        // self_hp_below: a worm that starts at 20% HP
        var self = Skill(c, "self", On(Hit, EffectKind.Damage, 10, @if: "self_hp_below:30"));
        var w = Worm(self); w.StartHp = 2000;
        Assert.Single(Amounts(Fight(c, w, 1), BattleAction.Skill, self));
        Assert.Empty(Amounts(Fight(c, Worm(self), 1), BattleAction.Skill, self));
    }

    [Fact]
    public void If_FirstHit_FirstTurn_OneEnemy_FirstHitTaken_ComboStrikes()
    {
        var c = new ContentDb();
        var first = Skill(c, "first", On(Hit, EffectKind.Damage, 10, @if: "first_hit"));
        Assert.Equal(new[] { 1 }, Rounds(Fight(c, Worm(first), 4), BattleAction.Skill, first));

        var turn = Skill(c, "turn", On(TriggerKind.OnTurnStart, EffectKind.Damage, 10, @if: "first_turn"));
        Assert.Equal(new[] { 1 }, Rounds(Fight(c, Worm(turn), 4), BattleAction.Skill, turn));

        // one_enemy: the first bag dies to the round-1 hit (100 HP), so that hit's on-hit already sees one enemy left
        var one = Skill(c, "one", On(Hit, EffectKind.Damage, 10, @if: "one_enemy"));
        Assert.Equal(new[] { 1, 2, 3 }, Rounds(Fight(c, Worm(one), 3, Bag("a", 100), Bag("b")), BattleAction.Skill, one));

        // first_hit_taken: a hitter attacks twice a round (two bags with ATK) — only the first hit each round reflects
        var taken = Skill(c, "taken", On(TriggerKind.OnDamaged, EffectKind.Damage, 10, @if: "first_hit_taken"));
        var r = Fight(c, Worm(taken), 3, Unit("x", CombatRole.Enemy, 1_000_000, 50), Unit("y", CombatRole.Enemy, 1_000_000, 50));
        Assert.Equal(new[] { 1, 2, 3 }, Rounds(r, BattleAction.Skill, taken));

        // combo_strikes:2 fires exactly once, on the second combo strike of a turn (combo chance 95%, no decay rule for the test)
        var cs = Skill(c, "cs", On(TriggerKind.OnCombo, EffectKind.Damage, 10, @if: "combo_strikes:2"), Rule("combo_no_decay"));
        var cw = Worm(cs); cw.Stats.Add(new StatMod(StatId.ComboChance, 95));
        var rc = Fight(c, cw, 1);
        int combos = rc.Log.Count(e => e.Action == BattleAction.Combo && e.Actor == 0);
        Assert.True(combos >= 2, combos.ToString());
        Assert.Single(Amounts(rc, BattleAction.Skill, cs));
    }

    // ---- R2: BlockStatus ------------------------------------------------------------------------------------------

    [Fact]
    public void BlockStatus_LocksTheStatus_ForItsDuration_ReusingTheReapplyCooldown()
    {
        var c = new ContentDb();
        var id = Skill(c, "lock", Apply(Hit, StatusId.Bleed), On(Hit, EffectKind.BlockStatus, 0, @if: "has:Bleed & unblocked:Bleed", status: StatusId.Bleed, duration: 3));
        var r = Fight(c, Worm(id), 8);
        // applied in round 1, locked for rounds 1–3, applied again in round 4 (and locked again), then round 7
        Assert.Equal(new[] { 1, 4, 7 }, Rounds(r, BattleAction.StatusApplied));
        Assert.Equal(new[] { 1, 4, 7 }, Rounds(r, BattleAction.StatusBlocked, id));
        Assert.Equal(3, StacksAfter(r, 1, StatusId.Bleed, 8));
        Assert.All(r.Log.Where(e => e.Action == BattleAction.StatusBlocked), e => { Assert.Equal(StatusId.Bleed, e.Status); Assert.Equal(3, e.Stacks); });
    }

    // ---- R3: per-stack and per-variety bonuses ---------------------------------------------------------------------

    [Fact]
    public void PerStack_And_StatusCount_Bonuses_ScaleWithTheTarget()
    {
        var c = new ContentDb();
        // +5% per Bleed stack; Bleed lands after each hit: 100, 105, 110, 115
        var per = Skill(c, "per", Apply(Hit, StatusId.Bleed), Cond("per_stack:Bleed", 5));
        Assert.Equal(new long[] { 100, 105, 110, 115 }, Amounts(Fight(c, Worm(per), 4), BattleAction.Basic, actor: 0));

        // +10% per different status: Bleed + Radiation after the first hit → 120 from round 2
        var vari = Skill(c, "vari", Apply(Hit, StatusId.Bleed), Apply(Hit, StatusId.Poison), Cond("status_count", 10));
        Assert.Equal(new long[] { 100, 120, 120 }, Amounts(Fight(c, Worm(vari), 3), BattleAction.Basic, actor: 0));

        // a gated bonus (cond:if): +50% against targets with 2+ Bleed
        var gated = Skill(c, "gated", Apply(Hit, StatusId.Bleed), Cond("if", 50, "has:Bleed:2"));
        Assert.Equal(new long[] { 100, 100, 150 }, Amounts(Fight(c, Worm(gated), 3), BattleAction.Basic, actor: 0));
    }

    // ---- R4: every-Nth counters -----------------------------------------------------------------------------------

    [Fact]
    public void EveryN_CountsStacksApplied_AndEvents()
    {
        var c = new ContentDb();
        // Radiation ×4 per hit: stacks 4, 8, 12 (10th crossed), 16, 20 (20th crossed) → rounds 3 and 5
        var geiger = Skill(c, "geiger", Apply(Hit, StatusId.Poison, 4),
            On(TriggerKind.OnStatusApplied, EffectKind.Damage, 60, param: "Poison", everyN: 10));
        var r = Fight(c, Worm(geiger), 6);
        Assert.Equal(new[] { 3, 5 }, Rounds(r, BattleAction.Skill, geiger));
        Assert.All(Amounts(r, BattleAction.Skill, geiger), a => Assert.Equal(60, a));

        // every 3rd hit
        var third = Skill(c, "third", On(Hit, EffectKind.Damage, 10, everyN: 3));
        Assert.Equal(new[] { 3, 6 }, Rounds(Fight(c, Worm(third), 7), BattleAction.Skill, third));
        // every 2nd round
        var rounds = Skill(c, "rounds", On(TriggerKind.OnRoundStart, EffectKind.Damage, 10, everyN: 2));
        Assert.Equal(new[] { 2, 4 }, Rounds(Fight(c, Worm(rounds), 5), BattleAction.Skill, rounds));
    }

    [Fact]
    public void MaxPerBattle_LimitsTheTrigger()
    {
        var c = new ContentDb();
        var once = Skill(c, "once", On(Hit, EffectKind.Damage, 10, maxPerBattle: 2));
        Assert.Equal(new[] { 1, 2 }, Rounds(Fight(c, Worm(once), 5), BattleAction.Skill, once));
    }

    // ---- R5: stack thresholds and Exposed --------------------------------------------------------------------------

    [Fact]
    public void StackThreshold_FiresOnceWhenCrossed_AndExposedRaisesDamageTaken()
    {
        var c = new ContentDb();
        var id = Skill(c, "mass", Apply(Hit, StatusId.Poison, 2),
            Apply(TriggerKind.OnStackThreshold, StatusId.Exposed, 25, param: "Poison:5"));
        var r = Fight(c, Worm(id), 6);
        // Radiation 2, 4, 6 → crosses 5 on the third hit; Exposed +25% from the fourth hit on
        Assert.Equal(new[] { 3 }, r.Log.Where(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Exposed).Select(e => e.Round));
        Assert.Equal(new long[] { 100, 100, 100, 125, 125, 125 }, Amounts(r, BattleAction.Basic, actor: 0));
    }

    // ---- consume variants -----------------------------------------------------------------------------------------

    [Fact]
    public void PartialConsume_TakesNStacks_AndHealsFromTheDamage()
    {
        var c = new ContentDb();
        var id = Skill(c, "scent", Apply(Hit, StatusId.Bleed, 3),
            On(Hit, EffectKind.ConsumeStatus, 30, status: StatusId.Bleed, param: "take:2;dmg:20;heal:$"));
        var w = Worm(id); w.StartHp = 5000;
        var r = Fight(c, w, 3);
        // each hit: Bleed +3, then 2 consumed for 2 × 20% = 40 damage, heal 30% of it = 12; 1 stack left per round
        Assert.Equal(new long[] { 40, 40, 40 }, Amounts(r, BattleAction.Skill, id));
        Assert.Equal(new long[] { 12, 12, 12 }, Amounts(r, BattleAction.Heal, id));
        var removed = r.Log.Where(e => e.Action == BattleAction.StatusRemoved && e.SkillId == id).ToList();
        Assert.All(removed, e => Assert.Equal(2, e.Amount));
        Assert.Equal(new[] { 1, 2, 3 }, removed.Select(e => e.Stacks));
    }

    [Fact]
    public void Consume_DrainsIntoABattleLongBuff_UpToItsCap()
    {
        var c = new ContentDb();
        var id = Skill(c, "siphon", Apply(Hit, StatusId.Heat, 3),
            On(Hit, EffectKind.ConsumeStatus, 4, status: StatusId.Heat, @if: "has:Heat:6", param: "buff:Atk:$;cap:10"));
        var r = Fight(c, Worm(id), 5);
        // Heat 3, 6 → drained (6 × 4% = 24%, capped at +10% ATK) → 3, 6 → drained again (cap used up: no more ATK)
        Assert.Equal(new long[] { 100, 100, 110, 110, 110 }, Amounts(r, BattleAction.Basic, actor: 0));
        Assert.Equal(new long[] { 10 }, Amounts(r, BattleAction.Buff, id));
        Assert.Equal(2, r.Log.Count(e => e.Action == BattleAction.StatusRemoved && e.SkillId == id));
    }

    [Fact]
    public void LegacyConsume_StillReadsValueAsDamagePerStack()
    {
        var c = new ContentDb();
        var id = Skill(c, "legacy", Apply(Hit, StatusId.Bleed, 2), On(Hit, EffectKind.ConsumeStatus, 25, status: StatusId.Bleed, param: "heal:50"));
        var w = Worm(id); w.StartHp = 5000;
        var r = Fight(c, w, 1);
        Assert.Equal(new long[] { 50 }, Amounts(r, BattleAction.Skill, id));
        Assert.Equal(new long[] { 25 }, Amounts(r, BattleAction.Heal, id));
    }

    // ---- instant tick ---------------------------------------------------------------------------------------------

    [Fact]
    public void TickStatus_TicksNow_AtItsPercent_OncePerTargetPerRound()
    {
        var c = new ContentDb();
        // Radiation ×10 → an instant tick at 200%: 100 ATK × 4% × 10 × 2 = 80; the second tick trigger in the same round does nothing
        var id = Skill(c, "gamma", Apply(Hit, StatusId.Poison, 10),
            On(Hit, EffectKind.TickStatus, 200, status: StatusId.Poison), On(Hit, EffectKind.TickStatus, 200, status: StatusId.Poison));
        var r = Fight(c, Worm(id), 1);
        Assert.Equal(new long[] { 80 }, Amounts(r, BattleAction.StatusTick, id));
    }

    // ---- R15: card text -------------------------------------------------------------------------------------------

    [Fact]
    public void CardText_ReadsEveryPrimitive()
    {
        var c = new ContentDb();
        string T(params SkillTrigger[] t) => SkillText.Full(new SkillDef { Id = "x", Name = "x", Triggers = t.ToList() });
        Assert.Equal("On hit (it has 3+ Bleed): deal 50% ATK damage to the enemy.", T(On(Hit, EffectKind.Damage, 50, @if: "has:Bleed:3")));
        Assert.Equal("On hit (it has Heat, it can gain Heat): it can't gain Heat for 3 rounds.",
            T(On(Hit, EffectKind.BlockStatus, 0, @if: "has:Heat & unblocked:Heat", status: StatusId.Heat, duration: 3)));
        Assert.Equal("+1.5% damage per Heat stack on the target.", T(Cond("per_stack:Heat", 1.5)));
        Assert.Equal("+10% damage per different status on the target.", T(Cond("status_count", 10)));
        Assert.Equal("Every 10th Radiation stack you apply: deal 60% ATK damage to the enemy.",
            T(On(TriggerKind.OnStatusApplied, EffectKind.Damage, 60, param: "Poison", everyN: 10)));
        Assert.Equal("Every 4th combo strike: deal 100% ATK damage to all enemies.", T(On(TriggerKind.OnCombo, EffectKind.Damage, 100, TargetKind.AllEnemies, everyN: 4)));
        Assert.Equal("When an enemy reaches 20 Radiation: Expose the enemy (+25% damage taken).",
            T(Apply(TriggerKind.OnStackThreshold, StatusId.Exposed, 25, param: "Poison:20")));
        Assert.Equal("On crit: consume 2 of the enemy's Bleed: 20% ATK damage per stack, heal 30% of it.",
            T(On(TriggerKind.OnCrit, EffectKind.ConsumeStatus, 30, status: StatusId.Bleed, param: "take:2;dmg:20;heal:$")));
        Assert.Equal("On rage attack: consume the enemy's Heat: +4% ATK per stack for the battle (max +60%).",
            T(On(TriggerKind.OnRageAttack, EffectKind.ConsumeStatus, 4, status: StatusId.Heat, param: "buff:Atk:$;cap:60")));
        Assert.Equal("On crit (it has Radiation): its Radiation ticks now at 200%.",
            T(On(TriggerKind.OnCrit, EffectKind.TickStatus, 200, @if: "has:Poison", status: StatusId.Poison)));
        Assert.Equal("On hit: deal 10% ATK damage to the enemy (once per battle).", T(On(Hit, EffectKind.Damage, 10, maxPerBattle: 1)));
    }

    [Fact]
    public void CardTemplates_FillTheLeveledNumbers_AndMarkCosts()
    {
        var s = new SkillDef
        {
            Id = "tpl", Name = "Tpl", Rarity = Rarity.Legendary, Source = SkillSource.Run,
            Text = "+{p0}% ATK|!{p1}% max HP",
            Stats = { new StatMod(StatId.Atk, 0, 25), new StatMod(StatId.Hp, 0, -15) },
        };
        var c = ContentDb.Default;
        Assert.Equal("+25% ATK. -15% max HP.", SkillText.Full(c, s, 1));
        Assert.Equal("+40% ATK. -15% max HP.", SkillText.Full(c, s, 2)); // the cost never scales
        Assert.Equal("+25% → +40% ATK. -15% max HP.", SkillText.UpgradeFull(c, s, 1, 2));
        var lines = SkillText.RichLines(c, s, 2);
        Assert.False(lines[0].Cost);
        Assert.True(lines[1].Cost);
        Assert.True(SkillText.IsCostLine(s, "-15% max HP"));
        Assert.True(BattleConditions.IsValid("has:Heat:6 & not_boss"));
        Assert.False(BattleConditions.IsValid("has:Heat & sparkly"));
    }
}
