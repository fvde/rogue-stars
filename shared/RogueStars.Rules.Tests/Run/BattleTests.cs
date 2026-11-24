using System.Text.Json;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

public class CombatMathTests
{
    private static readonly BattleTuning T = ContentDb.Default.Combat;

    [Fact]
    public void RawDamage_SubtractsDef_WithTenPercentFloor()
    {
        Assert.Equal(70, CombatMath.RawDamage(100, 0, 30, 10), 6);
        Assert.Equal(120, CombatMath.RawDamage(100, 50, 30, 10), 6);   // ATK·(1+bonus) − DEF
        Assert.Equal(10, CombatMath.RawDamage(100, 0, 500, 10), 6);    // floor 10% of ATK
        Assert.Equal(15, CombatMath.RawDamage(100, 50, 500, 10), 6);   // floor uses bonus-adjusted ATK
    }

    [Fact]
    public void SoftCap_MatchesFormula()
    {
        Assert.Equal(20, CombatMath.SoftCap(20, 10), 6);                         // below the knee: unchanged
        Assert.Equal(60, CombatMath.SoftCap(100, 10), 6);                        // 100·100/200 + 10
        Assert.Equal(65, CombatMath.SoftCap(100, 15), 6);                        // crit knee +15
        Assert.Equal(100.0 * 300 / 400 + 10, CombatMath.SoftCap(300, 10), 6);
        Assert.True(CombatMath.SoftCap(10_000, 10) < 110);
        Assert.Equal(0.1, CombatMath.DamageReductionFactor(100_000, T), 6);      // hard cap 90% DR
        Assert.Equal(0.4, CombatMath.DamageReductionFactor(100, T), 6);
        Assert.Equal(0.65, CombatMath.CritProbability(100, T), 6);
    }

    [Fact]
    public void CritMultiplier_And_DodgeDecay()
    {
        Assert.Equal(1.5, CombatMath.CritMultiplier(50, 0), 6);
        Assert.Equal(1.5, CombatMath.CritMultiplier(100, 50), 6);
        Assert.Equal(1.0, CombatMath.CritMultiplier(100, 150), 6);
        Assert.Equal(0.30, CombatMath.DodgeProbability(30, 0, 0, T), 6);
        Assert.Equal(0.30 * 0.8 * 0.8, CombatMath.DodgeProbability(30, 0, 2, T), 6);
        Assert.Equal(0.20, CombatMath.DodgeProbability(30, 10, 0, T), 6);        // dodge ignore
    }

    [Fact]
    public void TrueDamage_IgnoresDefAndDr()
    {
        double normal = CombatMath.HitDamage(100, 100, 0, 50, 50, false, false, 1.5, 1, 1, T);
        double tru = CombatMath.HitDamage(100, 100, 0, 50, 50, true, false, 1.5, 1, 1, T);
        Assert.Equal(50 * CombatMath.DamageReductionFactor(50, T), normal, 6);
        Assert.Equal(100, tru, 6);
    }
}

public class BattleSimulatorTests
{
    private static CombatantSpec Unit(string id, CombatRole role, double hp, double atk, double def = 0, double speed = 100, bool targetable = true)
    {
        var s = new CombatantSpec { Id = id, Name = id, Role = role, Targetable = targetable };
        s.Stats.Add(new StatMod(StatId.Hp, hp));
        s.Stats.Add(new StatMod(StatId.Atk, atk));
        s.Stats.Add(new StatMod(StatId.Def, def));
        s.Stats.Add(new StatMod(StatId.Speed, speed));
        return s;
    }

    private static BattleSetup Duel(CombatantSpec p, CombatantSpec e, int cap = 0)
    {
        var setup = new BattleSetup { RoundCap = cap };
        setup.Player.Add(p);
        setup.Enemy.Add(e);
        return setup;
    }

    [Fact]
    public void BasicHit_UsesFormula_NoVariance()
    {
        var c = ContentDb.Default;
        var worm = Unit("worm", CombatRole.Worm, 1000, 100, 0, 200);
        var foe = Unit("foe", CombatRole.Enemy, 10_000, 0, 30, 1);
        var res = BattleSimulator.Simulate(Duel(worm, foe, 1), c, new Rng(1));
        var hit = res.Log.First(e => e.Action == BattleAction.Basic);
        double slam = c.Combat.Styles[AttackStyle.Slam].Basic;
        Assert.Equal((long)Math.Round(100 * slam / 100 - 30), hit.Amount);
        Assert.False(hit.Crit);
    }

    [Fact]
    public void HitOrder_CritThenDodge_ThenDamage_AndParryIsGone()
    {
        var c = new ContentDb();
        c.Combat.MaxDodge = 100;
        // Dodger: every first attack in a streak is dodged (crit was rolled but negated).
        var att = Unit("worm", CombatRole.Worm, 1000, 100, 0, 200);
        att.Stats.Add(new StatMod(StatId.CritChance, 10_000));
        var dodger = Unit("foe", CombatRole.Enemy, 100_000, 0, 0, 1);
        dodger.Stats.Add(new StatMod(StatId.Dodge, 10_000));
        var res = BattleSimulator.Simulate(Duel(att, dodger, 10), c, new Rng(3));
        var first = res.Log.First(e => e.Action == BattleAction.Basic);
        Assert.True(first.Dodge);
        Assert.False(first.Crit);
        Assert.Equal(0, first.Amount);
        // consecutive dodges get ×0.8 odds, so some later attacks land
        Assert.Contains(res.Log, e => e.Action == BattleAction.Basic && !e.Dodge && e.Crit);

        // Skill redesign: parry was removed. An old save's parry chance no longer cuts damage; it counts as counter chance.
        var parrier = Unit("foe", CombatRole.Enemy, 100_000, 0, 0, 1);
        parrier.Stats.Add(new StatMod(StatId.ParryChance, 10_000));
        var res2 = BattleSimulator.Simulate(Duel(att, parrier, 1), c, new Rng(3));
        var hit = res2.Log.First(e => e.Action == BattleAction.Basic);
        Assert.True(hit.Crit && !hit.Parry);
        Assert.Equal((long)Math.Round(100 * CombatMath.CritMultiplier(c.Combat.BaseCritDamage, 0)), hit.Amount);
        Assert.Contains(res2.Log, e => e.Action == BattleAction.Counter && e.Actor == 1);
        Assert.Equal(10_000, BattleSimulator.FinalStats(parrier, c).Value(StatId.CounterChance));
        Assert.Equal(0, BattleSimulator.FinalStats(parrier, c).Value(StatId.ParryChance));
    }

    [Fact]
    public void RoundCap_WithEnemiesAlive_IsALoss()
    {
        var res = BattleSimulator.Simulate(Duel(Unit("worm", CombatRole.Worm, 1000, 1), Unit("wall", CombatRole.Enemy, 1e9, 0, 0, 1), 7), ContentDb.Default, new Rng(5));
        Assert.Equal(BattleSide.Enemy, res.Winner);
        Assert.Equal(BattleEndReason.RoundCap, res.EndReason);
        Assert.Equal(7, res.Rounds);
        Assert.False(res.PlayerWon());
    }

    [Fact]
    public void SameSeed_SameResultAndLog()
    {
        var lo = new CombatLoadout();
        lo.WormStats.Add(new StatMod(StatId.Hp, 2000)).Add(new StatMod(StatId.Atk, 120)).Add(new StatMod(StatId.Def, 10))
            .Add(new StatMod(StatId.CritChance, 30)).Add(new StatMod(StatId.ComboChance, 30)).Add(new StatMod(StatId.Dodge, 20));
        lo.Aliens.Add(new CombatAlien { AlienTypeId = "blorb", Stats = new StatBlock().Add(new StatMod(StatId.Atk, 40)) });
        var skills = new List<string> { "fire_ember", "poison_spit", "counter_riposte", "light_storm", "alien_charmer" };
        BattleSetup Setup()
        {
            var s = new BattleSetup();
            s.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, ContentDb.Default, skills));
            s.Enemy.Add(BattleSimulator.BuildEnemy(ContentDb.Default, "boss_storm_hive", 1.5));
            s.Enemy.Add(BattleSimulator.BuildEnemy(ContentDb.Default, "cinder_hound", 1.5));
            return s;
        }
        var a = BattleSimulator.Simulate(Setup(), ContentDb.Default, new Rng(99));
        var b = BattleSimulator.Simulate(Setup(), ContentDb.Default, new Rng(99));
        var json = new JsonSerializerOptions { IncludeFields = true };
        Assert.Equal(JsonSerializer.Serialize(a, json), JsonSerializer.Serialize(b, json));
        Assert.True(a.Log.Count > 10);
        var c = BattleSimulator.Simulate(Setup(), ContentDb.Default, new Rng(100));
        Assert.NotEqual(JsonSerializer.Serialize(a, json), JsonSerializer.Serialize(c, json));
    }

    [Fact]
    public void TurnOrder_MountAliensWorm_AndOnlyWormIsTargeted()
    {
        var lo = new CombatLoadout { MountId = "skiff" };
        lo.WormStats.Add(new StatMod(StatId.Hp, 5000)).Add(new StatMod(StatId.Atk, 50)).Add(new StatMod(StatId.Speed, 100));
        lo.MountStats.Add(new StatMod(StatId.Atk, 30)).Add(new StatMod(StatId.Hp, 200));
        var alien = new CombatAlien { AlienTypeId = "a1", Stats = new StatBlock().Add(new StatMod(StatId.Atk, 20)).Add(new StatMod(StatId.Hp, 100)).Add(new StatMod(StatId.Speed, 50)) };
        alien.SkillIds.Add("ask_voidmaw_1");
        lo.Aliens.Add(alien);
        var setup = new BattleSetup { RoundCap = 5 };
        setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, ContentDb.Default));
        setup.Enemy.Add(Unit("e1", CombatRole.Enemy, 100_000, 40, 0, 50));
        setup.Enemy.Add(Unit("e2", CombatRole.Enemy, 100_000, 40, 0, 50));
        var res = BattleSimulator.Simulate(setup, ContentDb.Default, new Rng(2));
        // round 1: the mount fires, the alien (a sidekick: start charge 50 + Speed 50) uses its ability, then the worm and the enemies attack
        var r1 = res.Log.Where(e => e.Round == 1 && (e.Action == BattleAction.Basic || e.Action == BattleAction.SidekickAct)).Select(e => res.Fighters[e.Actor].Role).ToList();
        Assert.Equal(new[] { CombatRole.Mount, CombatRole.Alien, CombatRole.Worm, CombatRole.Enemy, CombatRole.Enemy }, r1);
        // a sidekick never attacks: it acts every other round at Speed 50 (rounds 1, 3, 5)
        Assert.DoesNotContain(res.Log, e => e.Action == BattleAction.Basic && res.Fighters[e.Actor].Role == CombatRole.Alien);
        Assert.Equal(new[] { 1, 3, 5 }, res.Log.Where(e => e.Action == BattleAction.SidekickAct).Select(e => e.Round).ToArray());
        int worm = res.Fighters.First(f => f.Role == CombatRole.Worm).Index;
        Assert.All(res.Log.Where(e => e.Action == BattleAction.Basic && res.Fighters[e.Actor].Side == BattleSide.Enemy), e => Assert.Equal(worm, e.Target));
        // mount HP becomes a shield on the worm that absorbs first
        var firstEnemyHit = res.Log.First(e => e.Action == BattleAction.Basic && res.Fighters[e.Actor].Side == BattleSide.Enemy);
        Assert.Equal(5000, firstEnemyHit.TargetHp);
        Assert.Equal(200 - 40, firstEnemyHit.TargetShield);
    }

    [Fact]
    public void Log_CarriesRageAndShield_AfterEachEntry()
    {
        var c = ContentDb.Default;
        var T = c.Combat;
        var worm = Unit("worm", CombatRole.Worm, 1000, 10, 0, 200);
        var foe = Unit("foe", CombatRole.Enemy, 1_000_000, 0, 0, 1);
        var res = BattleSimulator.Simulate(Duel(worm, foe, 4), c, new Rng(1));
        int w = res.Fighters.First(f => f.Role == CombatRole.Worm).Index;
        // Worm acts first each round (+RagePerAction after its hit), then the foe hits it (+RagePerHitTaken after the hit);
        // in round 3 the foe's rage is full (3 × 35) and its extra rage attack hits the worm once more.
        var basics = res.Log.Where(e => e.Actor == w && e.Action == BattleAction.Basic).Select(e => e.ActorRage).ToList();
        long perRound = (long)(T.RagePerAction + T.RagePerHitTaken);
        Assert.Equal(new long[] { 0, perRound, 2 * perRound, 3 * perRound + (long)T.RagePerHitTaken }, basics);
        Assert.Contains(res.Log, e => e.Round == 3 && e.Actor != w && e.Action == BattleAction.Rage && e.ActorRage == (long)(3 * perRound - T.RageMax));
        var hitsTaken = res.Log.Where(e => e.Target == w && e.Action == BattleAction.Basic).Select(e => e.TargetRage).ToList();
        Assert.Equal((long)T.RagePerAction, hitsTaken[0]);
        // Round 4: 115 + 25 → capped at the overfill max, the rage attack spends RageMax.
        var rage = res.Log.Single(e => e.Actor == w && e.Action == BattleAction.Rage);
        Assert.Equal((long)(T.RageOverfillMax - T.RageMax), rage.ActorRage);
        Assert.Equal(res.Fighters[w].EndRage, res.Log.Last(e => e.Target == w).TargetRage + (long)T.RagePerHitTaken);
        Assert.All(res.Log, e => Assert.InRange(e.ActorRage, 0, (long)T.RageOverfillMax));

        // Mount HP is the worm's battle-start shield; entries carry the shield of actor and target.
        var lo = new CombatLoadout { MountId = "skiff" };
        lo.WormStats.Add(new StatMod(StatId.Hp, 5000)).Add(new StatMod(StatId.Atk, 50)).Add(new StatMod(StatId.Speed, 100));
        lo.MountStats.Add(new StatMod(StatId.Atk, 30)).Add(new StatMod(StatId.Hp, 200));
        var setup = new BattleSetup { RoundCap = 3 };
        setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c));
        setup.Enemy.Add(Unit("e1", CombatRole.Enemy, 100_000, 40, 0, 50));
        var r2 = BattleSimulator.Simulate(setup, c, new Rng(2));
        int w2 = r2.Fighters.First(f => f.Role == CombatRole.Worm).Index;
        Assert.Equal(200, r2.Fighters[w2].StartShield);
        var wormActs = r2.Log.First(e => e.Actor == w2 && e.Action == BattleAction.Basic);
        long shieldBefore = r2.Log.TakeWhile(e => e != wormActs).Where(e => e.Target == w2).Select(e => e.TargetShield).DefaultIfEmpty(200).Last();
        Assert.Equal(shieldBefore, wormActs.ActorShield);
    }

    [Fact]
    public void Speed_DecidesOpeningSide()
    {
        var res = BattleSimulator.Simulate(Duel(Unit("worm", CombatRole.Worm, 1000, 10, 0, 10), Unit("fast", CombatRole.Enemy, 1000, 10, 0, 50), 1), ContentDb.Default, new Rng(1));
        Assert.Equal(BattleSide.Enemy, res.Fighters[res.Log.First(e => e.Action == BattleAction.Basic).Actor].Side);
    }

    [Fact]
    public void Statuses_StackToCap_AndDealDamage()
    {
        var c = new ContentDb();
        c.AddSkill(RunTestKit.Skill("t_bleed", new SkillTrigger { When = TriggerKind.OnHit, Effect = EffectKind.ApplyStatus, Status = StatusId.Bleed, Stacks = 4 }));
        var worm = Unit("worm", CombatRole.Worm, 10_000, 100, 0, 200);
        worm.SkillIds.Add("t_bleed");
        var res = BattleSimulator.Simulate(Duel(worm, Unit("foe", CombatRole.Enemy, 1e7, 0, 0, 1), 8), c, new Rng(4));
        var applied = res.Log.Where(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Bleed).Select(e => e.Stacks).ToList();
        Assert.Equal(new[] { 4, 8, 10, 10 }, applied.Take(4));
        Assert.All(applied, s => Assert.True(s <= c.Combat.Statuses[StatusId.Bleed].MaxStacks));
        var ticks = res.Log.Where(e => e.Action == BattleAction.StatusTick).ToList();
        Assert.NotEmpty(ticks);
        // Bleed tick = stacks × 6% ATK, no DEF (target has 0 DR); capped at 10 stacks
        Assert.Contains(ticks, t => t.Amount == 60);
        // Bleed is permanent: it never expires
        Assert.DoesNotContain(res.Log, e => e.Action == BattleAction.StatusExpired && e.Status == StatusId.Bleed);
    }

    [Fact]
    public void Stun_HasPerTargetApplicationCooldown_AndSkipsTurns()
    {
        var c = new ContentDb();
        c.AddSkill(RunTestKit.Skill("t_stun", new SkillTrigger { When = TriggerKind.OnHit, Effect = EffectKind.ApplyStatus, Status = StatusId.Stun }));
        var worm = Unit("worm", CombatRole.Worm, 10_000, 10, 0, 200);
        worm.SkillIds.Add("t_stun");
        var res = BattleSimulator.Simulate(Duel(worm, Unit("foe", CombatRole.Enemy, 1e7, 10, 0, 1), 12), c, new Rng(4));
        var rounds = res.Log.Where(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Stun).Select(e => e.Round).ToList();
        Assert.True(rounds.Count >= 3);
        for (int i = 1; i < rounds.Count; i++) Assert.True(rounds[i] - rounds[i - 1] >= c.Combat.Statuses[StatusId.Stun].ApplyCooldownRounds);
        Assert.Contains(res.Log, e => e.Action == BattleAction.SkipTurn && res.Fighters[e.Actor].Side == BattleSide.Enemy);
    }

    [Fact]
    public void Shield_Heal_Lifesteal_And_ConditionalBonus()
    {
        var c = new ContentDb();
        c.AddSkill(RunTestKit.Skill("t_shield", new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.Shield, Target = TargetKind.Self, Value = 50 }));
        var tank = Unit("worm", CombatRole.Worm, 1000, 100, 0, 1);
        tank.SkillIds.Add("t_shield");
        tank.Stats.Add(new StatMod(StatId.LifeSteal, 50));
        tank.StartHp = 1000; // full HP: lifesteal only shows once hurt
        var res = BattleSimulator.Simulate(Duel(tank, Unit("foe", CombatRole.Enemy, 1e6, 100, 0, 200), 3), c, new Rng(1));
        var firstHit = res.Log.First(e => e.Action == BattleAction.Basic && res.Fighters[e.Actor].Side == BattleSide.Enemy);
        Assert.Equal(1000, firstHit.TargetHp);           // absorbed by the 500 shield
        Assert.Equal(500 - firstHit.Amount, firstHit.TargetShield);
        var hurt = tank; hurt.StartHp = 300;
        var resLs = BattleSimulator.Simulate(Duel(hurt, Unit("foe", CombatRole.Enemy, 1e6, 0, 0, 1), 2), c, new Rng(1));
        Assert.Contains(resLs.Log, e => e.Action == BattleAction.LifeSteal && e.Amount == 50);

        // conditional bonus: Giant Slayer +18% against enemies above 70% HP
        var hunter = Unit("worm", CombatRole.Worm, 1000, 100, 0, 200);
        hunter.SkillIds.Add("gen_giant_slayer");
        var boss = Unit("boss", CombatRole.Boss, 1e6, 0, 0, 1);
        var r2 = BattleSimulator.Simulate(Duel(hunter, boss, 1), ContentDb.Default, new Rng(1));
        Assert.Equal(118, r2.Log.First(e => e.Action == BattleAction.Basic).Amount);
    }

    [Fact]
    public void BossHpPool_ScoresDamageDealt()
    {
        var lo = new CombatLoadout();
        lo.WormStats.Add(new StatMod(StatId.Hp, 5000)).Add(new StatMod(StatId.Atk, 200));
        var boss = BattleSimulator.BuildEnemy(ContentDb.Default, "boss_void_maw", 1);
        boss.StartHp = 50_000_000;
        var res = BattleSimulator.PlayerVsBoss(ContentDb.Default, lo, boss, new Rng(8), 10);
        Assert.Equal(BattleSide.Enemy, res.Winner);
        Assert.True(res.PlayerDamageDealt > 0);
        var bossInfo = res.Fighters.First(f => f.Role == CombatRole.Boss);
        Assert.True(res.PlayerDamageDealt >= bossInfo.StartHp - bossInfo.EndHp); // the boss lifesteals back part of it
    }

    [Fact]
    public void ArenaSnapshot_Battle_Completes()
    {
        var a = new CombatLoadout { DisplayName = "A" };
        a.WormStats.Add(new StatMod(StatId.Hp, 3000)).Add(new StatMod(StatId.Atk, 150)).Add(new StatMod(StatId.Speed, 110));
        var d = new CombatLoadout { DisplayName = "D" };
        d.WormStats.Add(new StatMod(StatId.Hp, 3000)).Add(new StatMod(StatId.Atk, 100)).Add(new StatMod(StatId.Speed, 100));
        d.Aliens.Add(new CombatAlien { AlienTypeId = "x", Stats = new StatBlock().Add(new StatMod(StatId.Atk, 30)) });
        var res = BattleSimulator.PlayerVsSnapshot(ContentDb.Default, a, d, new Rng(3));
        Assert.Equal(BattleSide.Player, res.Winner);
        Assert.Contains(res.Fighters, f => f.Side == BattleSide.Enemy && f.Role == CombatRole.Alien && !f.Targetable);
    }
}
