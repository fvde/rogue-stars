using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Run.BattleLab;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// The owner's four Mythics (2026-11-26): Plague and Shatter (Laboratory research), Horrifying Presence and Beast (generic pool).
/// Battle numbers use BattleLab (worm ATK 100, Slam basic 100%, punching bags with DEF 0).
/// </summary>
public class MythicFourTests
{
    private static readonly ContentDb C = ContentDb.Default;

    /// <summary>The real content plus test kits; Freeze lasts the whole fight here so every hit lands on a frozen enemy.</summary>
    private static ContentDb Kit(bool longFreeze = false)
    {
        var c = new ContentDb();
        Skill(c, "k_poison5", Apply(TriggerKind.OnHit, StatusId.Poison, 5));
        Skill(c, "k_freeze_start", Apply(TriggerKind.OnBattleStart, StatusId.Freeze, 1, TargetKind.AllEnemies));
        Skill(c, "k_alien_hit", On(TriggerKind.OnAct, EffectKind.Damage, 50));
        if (longFreeze) c.Combat.Statuses[StatusId.Freeze].DefaultDuration = 100;
        return c;
    }

    private static CombatantSpec Alien(string id = "a0")
    {
        var a = Unit(id, CombatRole.Alien, 100, 20, 1000); // acts every round
        a.SkillIds.Add("k_alien_hit");
        return a;
    }

    private static int ShatterStacks(BattleResult r) => r.Log.Count(e => e.Action == BattleAction.Buff && e.SkillId == "lab_shatter");

    // ---- Shatter --------------------------------------------------------------------------------------------------

    [Fact]
    public void Shatter_EachWormHitOnAFrozenEnemy_AddsTenPercentAtk_UpToFifteenStacks()
    {
        var c = Kit(longFreeze: true);
        var r = Fight(c, Worm("k_freeze_start", "lab_shatter"), 25);
        Assert.Equal(15, ShatterStacks(r)); // 25 hits, capped at 15
        var hits = Amounts(r, BattleAction.Basic, actor: 0);
        Assert.Equal(120, hits[0]);  // 100 ATK, frozen +20% taken; the stack lands after the hit
        Assert.Equal(132, hits[1]);  // 110 ATK
        Assert.Equal(300, hits[20]); // 250 ATK (+150%) holds: no decay
        Assert.Equal(300, hits[24]);
    }

    [Fact]
    public void Shatter_NeedsAFrozenTarget()
    {
        var r = Fight(Kit(), Worm("lab_shatter"), 5);
        Assert.Equal(0, ShatterStacks(r));
        Assert.All(Amounts(r, BattleAction.Basic, actor: 0), a => Assert.Equal(100, a));
    }

    [Fact]
    public void Shatter_AlienHitsOnAFrozenEnemy_AddStacksToo()
    {
        var c = Kit(longFreeze: true);
        var solo = Fight(c, Worm("k_freeze_start", "lab_shatter"), 3);
        var squad = Fight(c, new[] { Alien(), Worm("k_freeze_start", "lab_shatter") }, 3);
        Assert.Equal(3, ShatterStacks(solo));
        Assert.Equal(6, ShatterStacks(squad)); // the alien's hit each round adds one
        // the stacks are the worm's buffs: one per hit, shared cap
        var many = Fight(c, new[] { Alien("a0"), Alien("a1"), Worm("k_freeze_start", "lab_shatter") }, 10);
        Assert.Equal(15, ShatterStacks(many));
    }

    [Fact]
    public void Shatter_StacksLastOneBattle()
    {
        var c = Kit(longFreeze: true);
        // a fresh battle starts at 0 stacks: the first hit of a second fight is the unbuffed one again (battle-scoped buffs)
        var first = Fight(c, Worm("k_freeze_start", "lab_shatter"), 20);
        var second = Fight(c, Worm("k_freeze_start", "lab_shatter"), 20);
        Assert.Equal(120, Amounts(second, BattleAction.Basic, actor: 0)[0]);
        Assert.Equal(15, ShatterStacks(first));
        Assert.All(first.Log.Where(e => e.Action == BattleAction.Buff && e.SkillId == "lab_shatter"), e => Assert.Equal(10, e.Amount));
    }

    // ---- Horrifying Presence --------------------------------------------------------------------------------------

    [Fact]
    public void HorrifyingPresence_StunsEveryEnemyInRoundOne()
    {
        var c = Kit();
        var r = Fight(c, Worm("mythic_horrifying_presence"), 3, Unit("x", CombatRole.Enemy, 1_000_000, 10), Unit("y", CombatRole.Enemy, 1_000_000, 10));
        int x = Index(r, "x"), y = Index(r, "y");
        var stuns = r.Log.Where(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Stun).ToList();
        Assert.Equal(new[] { x, y }, stuns.Select(e => e.Target).OrderBy(i => i));
        Assert.All(stuns, e => Assert.Equal(0, e.Round)); // at battle start
        foreach (int i in new[] { x, y })
        {
            Assert.Contains(r.Log, e => e.Action == BattleAction.SkipTurn && e.Actor == i && e.Round == 1);
            Assert.DoesNotContain(r.Log, e => e.Action == BattleAction.Basic && e.Actor == i && e.Round == 1);
            Assert.Contains(r.Log, e => e.Action == BattleAction.Basic && e.Actor == i && e.Round == 2); // acts again from round 2
        }
        var plain = Fight(c, Worm(), 1, Unit("x", CombatRole.Enemy, 1_000_000, 10));
        Assert.Contains(plain.Log, e => e.Action == BattleAction.Basic && e.Actor == 1 && e.Round == 1);
    }

    // ---- Beast ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Beast_AddsThirtyCounterComboAndCritChance()
    {
        var w = Worm("mythic_beast");
        var stats = BattleSimulator.FinalStats(w, C);
        var bare = BattleSimulator.FinalStats(Worm(), C);
        Assert.Equal(30, stats.Value(StatId.CounterChance) - bare.Value(StatId.CounterChance));
        Assert.Equal(30, stats.Value(StatId.ComboChance) - bare.Value(StatId.ComboChance));
        Assert.Equal(30, stats.Value(StatId.CritChance) - bare.Value(StatId.CritChance));
        var r = Fight(C, Worm("mythic_beast"), 10, Unit("x", CombatRole.Enemy, 1_000_000, 10));
        Assert.Contains(r.Log, e => e.Action == BattleAction.Combo);
        Assert.Contains(r.Log, e => e.Action == BattleAction.Counter);
        Assert.Contains(r.Log, e => e.Crit);
    }

    // ---- Plague ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Plague_CountsTheStacksTheWormInflicts_PerStatus()
    {
        var c = Kit();
        var r = Fight(c, Worm("k_poison5", "lab_plague"), 4);
        Assert.Equal(20, r.StacksInflicted!["Poison"]); // 4 hits × 5
        Assert.Null(Fight(c, Worm("k_poison5"), 4).StacksInflicted); // without Plague nothing is counted
    }

    [Fact]
    public void Plague_OpeningStacks_HitEveryEnemyAtBattleStart_AndDontCountThemselves()
    {
        var c = Kit();
        var w = Worm("lab_plague");
        w.OpeningStatuses = new Dictionary<string, int> { ["Poison"] = 1, ["Chill"] = 2 };
        var r = Fight(c, w, 1, Bag("a"), Bag("b"));
        var opening = r.Log.Where(e => e.Action == BattleAction.StatusApplied && e.SkillId == "lab_plague").ToList();
        Assert.All(opening, e => Assert.Equal(0, e.Round));
        Assert.Equal(4, opening.Count); // 2 statuses × 2 enemies
        Assert.Contains(opening, e => e.Target == Index(r, "a") && e.Status == StatusId.Poison && e.Stacks == 1);
        Assert.Contains(opening, e => e.Target == Index(r, "b") && e.Status == StatusId.Chill && e.Stacks == 2);
        Assert.Null(r.StacksInflicted);
    }

    [Fact]
    public void Plague_Every20StacksOfAStatus_GiveOneOpeningStack()
    {
        var run = new ActiveRun();
        run.PlagueStacks["Poison"] = 19;
        run.PlagueStacks["Bleed"] = 45;
        Assert.Empty(RunSystem.PlagueOpening(C, run)); // the run doesn't own Plague
        run.Skills.Add("lab_plague");
        Assert.Equal(20, RunSystem.PlagueEvery(C, run));
        Assert.Equal(new Dictionary<string, int> { ["Bleed"] = 2 }, RunSystem.PlagueOpening(C, run));
        run.PlagueStacks["Poison"] = 20;
        Assert.Equal(1, RunSystem.PlagueOpening(C, run)["Poison"]);
    }

    [Fact]
    public void Plague_InARun_OpensTheNextBattle_AndKeepsCountingForTheRun()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.Skills.Add("lab_plague");
        run.PlagueStacks["Poison"] = 40;
        RunTestKit.LineBoard(run, Enumerable.Repeat("enemy", 30).ToArray());
        for (int i = 0; i < 40 && s.Run.Active!.BattleCount == 0; i++)
            Assert.True(RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true }).Ok);
        run = s.Run.Active!;
        Assert.Equal(1, run.BattleCount);
        var b = run.LastBattle!;
        var enemies = b.Fighters.Where(f => f.Side == BattleSide.Enemy && f.SummonedBy < 0).Select(f => f.Index).ToList();
        Assert.NotEmpty(enemies);
        foreach (int i in enemies)
            Assert.Contains(b.Log, x => x.Round == 0 && x.Target == i && x.Action == BattleAction.StatusApplied && x.Status == StatusId.Poison
                && x.Stacks == 2 && x.SkillId == "lab_plague");
        Assert.True(run.PlagueStacks["Poison"] >= 40); // the run's count only grows; the opening stacks are not counted
    }

    [Fact]
    public void Plague_RunCount_AddsUpOverBattles()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.Skills.Add("lab_plague");
        run.Skills.Add("sig_radiation"); // a Radiation source: the starter Radiation signature
        RunTestKit.LineBoard(run, Enumerable.Repeat("enemy", 30).ToArray());
        for (int i = 0; i < 80 && s.Run.Active != null && s.Run.Active.BattleCount < 2; i++)
            Assert.True(RunTestKit.Exec(e, ref s, new RunAutoStep { Farming = true }).Ok);
        Assert.True(s.Run.Active!.BattleCount >= 2);
        Assert.True(s.Run.Active.PlagueStacks.TryGetValue("Poison", out var n) && n > 0, "no Radiation counted");
    }

    // ---- pools ----------------------------------------------------------------------------------------------------

    [Fact]
    public void LabMythics_AreLockedUntilResearched()
    {
        var run = new ActiveRun();
        run.Skills.Add("sig_fire");
        run.Loadout.UnlockedFamilies.AddRange(new[] { SkillFamily.Fire, SkillFamily.Poison, SkillFamily.Bleed, SkillFamily.Ice });
        run.Loadout.MetaSkillIds.Add("lab_frost_nova"); // a Freeze source for Shatter
        Assert.False(RunDraft.IsMythicEligible(C, run, C.Skill("lab_plague")!));
        Assert.False(RunDraft.IsMythicEligible(C, run, C.Skill("lab_shatter")!));
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var offer = RunDraft.GenerateMythic(C, run, new Rng(seed));
            Assert.DoesNotContain("lab_plague", offer);
            Assert.DoesNotContain("lab_shatter", offer);
        }

        // research puts them into the loadout's draft pool (LaboratorySystem's loadout contributor)
        var state = new GameState();
        Assert.DoesNotContain("lab_plague", LaboratorySystem.ActiveSkillIds(state, C));
        state.Lab.Entries["res_plague"] = new LabEntry { Level = 1, Enabled = true };
        state.Lab.Entries["res_shatter"] = new LabEntry { Level = 1, Enabled = true };
        var active = LaboratorySystem.ActiveSkillIds(state, C);
        Assert.Contains("lab_plague", active);
        Assert.Contains("lab_shatter", active);
        run.Loadout.DraftPoolSkillIds.AddRange(active);
        Assert.True(RunDraft.IsMythicEligible(C, run, C.Skill("lab_plague")!));
        Assert.True(RunDraft.IsMythicEligible(C, run, C.Skill("lab_shatter")!));
        Assert.False(RunDraft.IsEligible(C, run, C.Skill("lab_plague")!)); // Mythics never come from the normal draft or shop

        // Shatter still needs a Freeze source
        run.Loadout.MetaSkillIds.Clear();
        Assert.False(RunDraft.IsMythicEligible(C, run, C.Skill("lab_shatter")!));

        // toggled off in the Laboratory: out of the pool
        state.Lab.Entries["res_plague"].Enabled = false;
        Assert.DoesNotContain("lab_plague", LaboratorySystem.ActiveSkillIds(state, C));
    }

    [Fact]
    public void LabMythicNodes_AreOneLevelCapstones()
    {
        foreach (var (node, skill) in new[] { ("res_plague", "lab_plague"), ("res_shatter", "lab_shatter") })
        {
            var d = C.LabNode(node)!;
            Assert.Equal(LabNodeKind.Skill, d.Kind);
            Assert.Equal(5, d.Tier);
            Assert.True(d.RequireAll);
            Assert.Equal(2, d.Requires.Count);
            Assert.All(d.Requires, r => Assert.Equal(3, r.Level));
            Assert.Equal(new[] { skill }, d.Levels.Select(l => l.SkillId));
            var s = C.Skill(skill)!;
            Assert.Equal((Rarity.Mythic, SkillSource.Laboratory), (s.Rarity, s.Source));
            Assert.Equal("prerequisites missing", LaboratorySystem.BlockReason(new GameState(), C, d));
        }
        Assert.Equal("elements", C.LabNode("res_plague")!.Branch);
        Assert.Equal("offense", C.LabNode("res_shatter")!.Branch);
    }

    [Fact]
    public void GenericMythics_AreAlwaysEligible_WithoutUnlockOrSynergy()
    {
        foreach (var id in new[] { "mythic_horrifying_presence", "mythic_beast" })
        {
            var s = C.Skill(id)!;
            Assert.Equal((SkillFamily.None, SkillSource.Run, Rarity.Mythic), (s.Family, s.Source, s.Rarity));
            Assert.Null(s.Needs);
            var fresh = new ActiveRun(); // no unlocked synergy list, nothing researched
            Assert.True(RunDraft.IsMythicEligible(C, fresh, s));
            var starters = new ActiveRun();
            starters.Loadout.UnlockedFamilies.AddRange(C.RunTuning.StarterSynergies);
            starters.Skills.Add("sig_bleed");
            Assert.True(RunDraft.IsMythicEligible(C, starters, s));
            Assert.False(RunDraft.IsEligible(C, starters, s)); // never in the normal draft
            starters.Skills.Add(id);
            Assert.False(RunDraft.IsMythicEligible(C, starters, s)); // owned: never offered again
        }
    }

    [Fact]
    public void Glossary_ExplainsTheMechanics_AndWhereLabMythicsComeFrom()
    {
        Assert.Contains(SkillGlossary.SynergyLines(C, C.Skill("lab_plague")!), l => l.StartsWith("Mythic:") && l.Contains("Laboratory"));
        Assert.Contains(SkillGlossary.SynergyLines(C, C.Skill("lab_shatter")!), l => l == "Offered once your run has a skill that freezes.");
        Assert.DoesNotContain(SkillGlossary.SynergyLines(C, C.Skill("mythic_beast")!), l => l.Contains("Laboratory"));
        Assert.Contains(SkillGlossary.Stun, SkillGlossary.Keywords(C, C.Skill("mythic_horrifying_presence")));
        var beast = SkillGlossary.Keywords(C, C.Skill("mythic_beast"));
        Assert.Contains(SkillGlossary.Combo, beast); Assert.Contains(SkillGlossary.Counter, beast); Assert.Contains(SkillGlossary.Crit, beast);
        var shatter = SkillGlossary.Keywords(C, C.Skill("lab_shatter"));
        Assert.Contains(SkillGlossary.Freeze, shatter); Assert.Contains(SkillGlossary.Aliens, shatter);
        Assert.Contains(SkillGlossary.Statuses, SkillGlossary.Keywords(C, C.Skill("lab_plague")));
        Assert.Equal("Every enemy is stunned in round 1.", SkillText.Full(C, C.Skill("mythic_horrifying_presence")!, 1));
    }
}
