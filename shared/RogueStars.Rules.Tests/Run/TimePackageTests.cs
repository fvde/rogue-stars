using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Run.BattleLab;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Skill redesign, step 6 (R13): the Time package. Echo signature ("Every 3rd turn: repeat your last attack"), delayed turns,
/// extra / lost turns, damage-taken history (Rewind Shell) and a full battle-state rewind (Loop); the package after Alien.
/// </summary>
public class TimePackageTests
{
    private static readonly ContentDb C = ContentDb.Default;
    private static CombatantSpec Hitter(string id = "x", double atk = 40) => Unit(id, CombatRole.Enemy, 1_000_000, atk);

    [Fact]
    public void TimeIsALabPackage_AfterAlien_WithTheEchoSignature()
    {
        var pkg = C.LabNode("pkg_time")!;
        Assert.Equal(LabNodeKind.Package, pkg.Kind);
        Assert.Equal(SkillFamily.Time, pkg.Family);
        Assert.Contains(pkg.Requires, r => r.ResearchId == "pkg_alien");
        Assert.Equal("sig_time", pkg.Levels[0].SkillId);
        Assert.Equal("Time package: Echo + 8 skills.", pkg.Description);
        var sig = C.Skill("sig_time")!;
        Assert.True(sig.Signature);
        Assert.Equal("Every 3rd turn: repeat your last attack.", SkillText.Full(sig));
        Assert.Equal(new[] { 1, 2, 3 }, Enumerable.Range(1, 3).Select(l => SkillLeveling.At(C, sig, l).Triggers.Single().Stacks)); // awakened: ×2, ×3
        Assert.Equal("Time", SkillText.FamilyName(SkillFamily.Time));
    }

    [Fact]
    public void Echo_EveryThirdTurn_RepeatsTheLastAttack()
    {
        var r = Fight(new ContentDb(), Worm("sig_time"), 6);
        Assert.Equal(new[] { 3, 6 }, Rounds(r, BattleAction.Echo, "sig_time"));
        // the repeat is a basic attack again: 100, right after the echo marker
        int i = r.Log.FindIndex(e => e.Action == BattleAction.Echo);
        Assert.Equal((BattleAction.Basic, 100L), (r.Log[i + 1].Action, r.Log[i + 1].Amount));
        Assert.Equal(8, r.Log.Count(e => e.Action == BattleAction.Basic && e.Actor == 0));
    }

    [Fact]
    public void Afterimage_Paradox_StasisField()
    {
        var c = new ContentDb();
        var a = Fight(c, Worm("sig_time", "time_afterimage"), 3);
        int i = a.Log.FindIndex(e => e.Action == BattleAction.Echo);
        Assert.Equal(115, a.Log[i + 1].Amount);
        // Paradox: the strongest attack this battle (a rage attack, 220%) at +25%
        var w = Worm("sig_time", "time_paradox"); w.StartRage = 100;
        var p = Fight(c, w, 3);
        int j = p.Log.FindIndex(e => e.Action == BattleAction.Echo);
        Assert.Equal((BattleAction.Rage, 275L), (p.Log[j + 1].Action, p.Log[j + 1].Amount));
        Assert.False(RunDraft.HasSource(c, new ActiveRun(), "echo"));
        // Stasis Field: every 4th round all enemies lose their turn
        var s = Fight(c, Worm("time_stasis"), 8, Hitter("x"), Hitter("y"));
        Assert.Equal(new[] { 4, 4, 8, 8 }, s.Log.Where(e => e.Action == BattleAction.SkipTurn).Select(e => e.Round));
        Assert.DoesNotContain(s.Log, e => e.Action == BattleAction.Basic && e.Actor > 0 && e.Round == 4);
        var se = Fight(c, Worm("sig_time", "time_stasis"), 12);
        int k = se.Log.FindIndex(e => e.Action == BattleAction.Echo && e.Round == 12);
        Assert.Equal(125, se.Log[k + 1].Amount); // round 12: a Stasis round, the Echo deals +25%
    }

    [Fact]
    public void HeadStart_DelaysEveryEnemysFirstTurn()
    {
        var c = new ContentDb();
        var w = Worm("time_head_start"); w.SkillLevels["time_head_start"] = 3; // 50% (CC pass: 30% → 40% → 50%)
        int delayed = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var setup = new BattleSetup { RoundCap = 2 };
            setup.Player.Add(w);
            setup.Enemy.Add(Hitter("x")); setup.Enemy.Add(Hitter("y"));
            var r = BattleSimulator.Simulate(setup, c, new Rng(seed));
            var skips = r.Log.Where(e => e.Action == BattleAction.SkipTurn).ToList();
            Assert.All(skips, e => Assert.Equal(1, e.Round));
            if (skips.Count == 2) delayed++;
        }
        Assert.InRange(delayed, 4, 16);
        Assert.Equal(new double[] { 30, 40, 50 }, new[] { 1, 2, 3 }.Select(lv => SkillLeveling.At(c, c.Skill("time_head_start")!, lv).Triggers[0].Chance));
    }

    [Fact]
    public void RewindShell_BelowThirtyPct_UndoesTheLastHits_Once()
    {
        var c = new ContentDb();
        var w = Worm("time_rewind_shell"); w.StartHp = 3500;
        var r = Fight(c, w, 4, Hitter("x", 600));
        var undo = r.Log.Single(e => e.Action == BattleAction.Heal && e.SkillId == "time_rewind_shell");
        Assert.Equal(600, undo.Amount); // the hit that took it below 30% (3500 → 2900)
        var w3 = Worm("time_rewind_shell"); w3.StartHp = 3500; w3.SkillLevels["time_rewind_shell"] = 3;
        var r3 = Fight(c, w3, 4, Hitter("x", 600));
        Assert.Equal(600, r3.Log.Single(e => e.Action == BattleAction.Heal && e.SkillId == "time_rewind_shell").Amount); // one hit so far
        var w4 = Worm("time_rewind_shell"); w4.StartHp = 4500; w4.SkillLevels["time_rewind_shell"] = 3;
        var r4 = Fight(c, w4, 4, Hitter("x", 600));
        Assert.Equal(1800, r4.Log.Single(e => e.Action == BattleAction.Heal && e.SkillId == "time_rewind_shell").Amount); // its last 3 hits
    }

    [Fact]
    public void BorrowedTime_AnExtraTurnEachRound_ButEvery4thRoundNone()
    {
        var r = Fight(new ContentDb(), Worm("mythic_borrowed_time"), 4);
        Assert.Equal(new[] { 1, 1, 2, 2, 3, 3 }, r.Log.Where(e => e.Action == BattleAction.Basic && e.Actor == 0).Select(e => e.Round));
        Assert.Contains(r.Log, e => e.Action == BattleAction.SkipTurn && e.Actor == 0 && e.Round == 4);
    }

    [Fact]
    public void TimeDilation_TicksTwice_ButPermanentStacksDecay()
    {
        var c = new ContentDb();
        Skill(c, "k_bleed5", Apply(TriggerKind.OnHit, StatusId.Bleed, 5));
        var plain = Fight(c, Worm("k_bleed5"), 1);
        var dil = Fight(c, Worm("k_bleed5", "mythic_time_dilation"), 1);
        Assert.Equal(new long[] { 30 }, plain.Log.Where(e => e.Action == BattleAction.StatusTick).Select(e => e.Amount));
        Assert.Equal(new long[] { 30, 30 }, dil.Log.Where(e => e.Action == BattleAction.StatusTick).Select(e => e.Amount));
        Assert.Equal(4, StacksAfter(dil, 1, StatusId.Bleed, 1)); // then 1 stack decays
    }

    [Fact]
    public void Loop_WhenYoudDie_RewindsEveryoneTwoRounds_OncePerBattle()
    {
        var c = new ContentDb();
        var w = Worm("mythic_loop"); w.StartHp = 1000;
        var r = Fight(c, w, 6, Bag("y", 250), Unit("x", CombatRole.Enemy, 1_000_000, 300)); // y dies in round 3, the worm would in round 4
        var loop = r.Log.First(e => e.Action == BattleAction.Loop);
        Assert.Equal(loop.Actor, loop.Target);
        Assert.True(loop.TargetHp > 300, loop.ToString()); // back to its HP two rounds before
        Assert.True(loop.Amount >= loop.Round - 1);
        // the bag that died before the rewind is back
        var back = r.Log.Single(e => e.Action == BattleAction.Loop && e.Target == Index(r, "y"));
        Assert.Equal(1, back.Stacks);
        Assert.Single(r.Log, e => e.Action == BattleAction.Loop && e.Actor == e.Target); // once per battle
    }
}
