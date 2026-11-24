using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// A tiny deterministic arena for the skill-redesign engine tests: a worm (ATK 100, no crit / dodge / combo / counter, no rage gain,
/// Slam = 100% basic) that always acts first, against punching bags (ATK 0, DEF 0) so every number is exact.
/// </summary>
internal static class BattleLab
{
    public static SkillTrigger On(TriggerKind when, EffectKind effect, double value = 0, TargetKind target = TargetKind.Enemy, string? @if = null,
        StatusId status = StatusId.None, int stacks = 1, int duration = 0, string? param = null, double chance = 100, int everyN = 0, int maxPerBattle = 0) =>
        new() { When = when, Effect = effect, Value = value, Target = target, If = @if, Status = status, Stacks = stacks, Duration = duration, Param = param, Chance = chance, EveryN = everyN, MaxPerBattle = maxPerBattle };

    public static SkillTrigger Apply(TriggerKind when, StatusId status, int stacks = 1, TargetKind target = TargetKind.Enemy, string? @if = null, string? param = null) =>
        On(when, EffectKind.ApplyStatus, 0, target, @if, status, stacks, 0, param);

    public static SkillTrigger Rule(string rule, StatusId status = StatusId.None, double value = 0) =>
        new() { When = TriggerKind.Passive, Effect = EffectKind.Rule, Param = rule, Status = status, Value = value };

    public static SkillTrigger Cond(string cond, double value, string? @if = null) =>
        new() { When = TriggerKind.Passive, Effect = EffectKind.BuffStat, Param = "cond:" + cond, Value = value, If = @if };

    /// <summary>Adds a test skill (building source, not draftable) to the content and returns its id.</summary>
    public static string Skill(ContentDb c, string id, params SkillTrigger[] triggers)
    {
        var s = new SkillDef { Id = id, Name = id, Source = SkillSource.Building, Draftable = false };
        s.Triggers.AddRange(triggers);
        c.AddSkill(s);
        return id;
    }

    public static string SkillWithStats(ContentDb c, string id, StatMod[] stats, params SkillTrigger[] triggers)
    {
        Skill(c, id, triggers);
        c.Skill(id)!.Stats.AddRange(stats);
        return id;
    }

    public static CombatantSpec Unit(string id, CombatRole role, double hp, double atk, double speed = 1, double def = 0)
    {
        var s = new CombatantSpec { Id = id, Name = id, Role = role, Targetable = role != CombatRole.Alien && role != CombatRole.Mount && role != CombatRole.Summon, Style = AttackStyle.Slam };
        s.Stats.Add(new StatMod(StatId.Hp, hp)).Add(new StatMod(StatId.Atk, atk)).Add(new StatMod(StatId.Def, def)).Add(new StatMod(StatId.Speed, speed));
        return s;
    }

    public static CombatantSpec Worm(params string[] skills)
    {
        var w = Unit("worm", CombatRole.Worm, 10_000, 100, 200);
        w.Stats.Add(new StatMod(StatId.RageGain, -100)); // no rage attacks unless a test wants them
        w.SkillIds.AddRange(skills);
        return w;
    }

    public static CombatantSpec Bag(string id = "bag", double hp = 1_000_000, CombatRole role = CombatRole.Enemy) => Unit(id, role, hp, 0, 1);

    public static BattleResult Fight(ContentDb c, CombatantSpec worm, int rounds, params CombatantSpec[] enemies) =>
        Fight(c, new[] { worm }, rounds, enemies);

    public static BattleResult Fight(ContentDb c, CombatantSpec[] player, int rounds, params CombatantSpec[] enemies)
    {
        var setup = new BattleSetup { RoundCap = rounds };
        setup.Player.AddRange(player);
        setup.Enemy.AddRange(enemies.Length > 0 ? enemies : new[] { Bag() });
        return BattleSimulator.Simulate(setup, c, new Rng(7UL));
    }

    /// <summary>Amounts of the worm's (fighter index of the worm) entries of an action, in order.</summary>
    public static List<long> Amounts(BattleResult r, BattleAction action, string? skillId = null, int actor = -2) =>
        r.Log.Where(e => e.Action == action && (skillId == null || e.SkillId == skillId) && (actor == -2 || e.Actor == actor)).Select(e => e.Amount).ToList();

    public static List<int> Rounds(BattleResult r, BattleAction action, string? skillId = null) =>
        r.Log.Where(e => e.Action == action && (skillId == null || e.SkillId == skillId)).Select(e => e.Round).ToList();

    /// <summary>The stacks of a status on a fighter after its last change (applied / removed), 0 if never.</summary>
    public static int StacksAfter(BattleResult r, int fighter, StatusId status, int round)
    {
        int n = 0;
        foreach (var e in r.Log)
        {
            if (e.Round > round) break;
            if (e.Target != fighter || e.Status != status) continue;
            if (e.Action == BattleAction.StatusApplied || e.Action == BattleAction.StatusRemoved) n = e.Stacks;
            if (e.Action == BattleAction.Supernova && e.Amount == 0) n = 0;
            if (e.Action == BattleAction.StatusExpired) n = 0;
        }
        return n;
    }

    public static int Index(BattleResult r, string id) => r.Fighters.First(f => f.Id == id).Index;
}
