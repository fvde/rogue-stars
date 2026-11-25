using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Regression (sim crash "Collection was modified" in BattleSim.DealDamage, Hardcore Whale seed 2): a trigger fired while the
/// battle walks its fighter list (OnEnemyDeath, OnSkipTurn, OnSidekickAct) may summon a sidekick, which adds a fighter to that
/// list. The walk must cover the fighters present when the event happened and must not throw.
/// </summary>
public class SummonDuringTriggerTests
{
    [Fact]
    public void AnOnEnemyDeathSummon_DoesNotBreakTheDeathWalk()
    {
        var c = new ContentDb();
        Assert.NotNull(c.Sidekick("sk_drone"));
        c.AddSkill(new SkillDef { Id = "p_death_drone", Name = "Death Drone", Source = SkillSource.Building, Draftable = false,
            Triggers = { new SkillTrigger { When = TriggerKind.OnEnemyDeath, Effect = EffectKind.Summon, Param = "sk_drone", Stacks = 1 } } });
        var lo = new CombatLoadout { DisplayName = "Worm" };
        lo.WormStats.Add(new StatMod(StatId.Hp, 50_000)).Add(new StatMod(StatId.Atk, 400)).Add(new StatMod(StatId.Speed, 100));
        var setup = new BattleSetup();
        setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, new[] { "p_death_drone" }));
        for (int i = 0; i < 3; i++) setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "magma_slug", 1));

        var r = BattleSimulator.Simulate(setup, c, new Rng(2UL));

        Assert.Equal(BattleSide.Player, r.Winner);
        Assert.Contains(r.Log, x => x.Action == BattleAction.Summon); // the trigger did summon during the walk
        Assert.True(r.Fighters.Count(f => f.Role == CombatRole.Summon) >= 2);
    }
}
