using System.Text.Json;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Sidekick pass (owner design): aliens and summons are sidekicks. Only the worm is attacked; sidekicks charge by Speed (a Common
/// acts about every 3 worm turns) and act with their one ability; HP / ATK / DEF fold into ability power; a Block ability
/// intercepts hits on the worm; Command gives extra sidekick actions; drones come back as battle-scoped sidekicks.
/// </summary>
public class SidekickTests
{
    private static readonly ContentDb C = ContentDb.Default;

    private static CombatAlien Alien(string speciesId, int tier = 0, double speed = 0)
    {
        var def = C.AlienType(speciesId)!;
        var a = new CombatAlien { AlienTypeId = def.Id, Name = def.Name };
        a.Stats.Add(new StatMod(StatId.Hp, def.BaseHp)).Add(new StatMod(StatId.Atk, def.BaseAtk)).Add(new StatMod(StatId.Def, def.BaseDef))
            .Add(new StatMod(StatId.Speed, speed > 0 ? speed : def.BaseSpeed));
        a.SkillIds.Add(def.ArchetypeSkills[tier].SkillId);
        return a;
    }

    private static BattleResult Fight(IEnumerable<CombatAlien> aliens, IEnumerable<string>? wormSkills = null, int rounds = 30, ulong seed = 5,
        double enemyHp = 40, Dictionary<string, int>? levels = null, string? mountId = null, IEnumerable<string>? meta = null)
    {
        var lo = new CombatLoadout { DisplayName = "Worm", MountId = mountId };
        lo.WormStats.Add(new StatMod(StatId.Hp, 6000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Def, 10)).Add(new StatMod(StatId.Speed, 100));
        if (mountId != null) lo.MountStats.Add(new StatMod(StatId.Atk, 40)).Add(new StatMod(StatId.Hp, 10));
        lo.Aliens.AddRange(aliens);
        if (meta != null) lo.MetaSkillIds.AddRange(meta);
        var setup = new BattleSetup { RoundCap = rounds };
        setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, C, wormSkills?.ToList(), null, levels));
        setup.Enemy.Add(BattleSimulator.BuildEnemy(C, "magma_slug", 1, 0, (enemyHp - 1) * 100));
        setup.Enemy.Add(BattleSimulator.BuildEnemy(C, "ash_wraith", 1, 0, (enemyHp - 1) * 100));
        return BattleSimulator.Simulate(setup, C, new Rng(seed));
    }

    private static int Acts(BattleResult r, CombatRole role, bool extra = false) =>
        r.Log.Count(e => e.Action == BattleAction.SidekickAct && r.Fighters[e.Actor].Role == role && (e.Stacks == 1) == extra);

    [Fact]
    public void ACommonSidekick_ActsAboutEveryThirdTurn_AndRarerOnesMoreOften()
    {
        var common = Fight(new[] { Alien("alien_blorp") }, rounds: 30);
        Assert.Equal(30, common.Rounds);
        Assert.InRange(Acts(common, CombatRole.Alien), 9, 11);                       // 30 worm turns, ~1 in 3
        var mythic = Fight(new[] { Alien("alien_nebulisk") }, rounds: 30);
        Assert.InRange(Acts(mythic, CombatRole.Alien), 14, 16);                       // Speed 50: every other turn
        Assert.Equal(100 / 34.0, BattleSimulator.SidekickActsEvery(34, C), 6);
        // never more than one own action per turn, however fast
        var fast = Fight(new[] { Alien("alien_blorp", 0, 400) }, rounds: 10);
        Assert.Equal(10, Acts(fast, CombatRole.Alien));
    }

    [Fact]
    public void OnlyTheWorm_IsAttacked_SidekicksNeverAttackOrTakeDamage()
    {
        var r = Fight(new[] { Alien("alien_voidmaw"), Alien("alien_mossling") }, rounds: 12);
        int worm = r.Fighters.Single(f => f.Role == CombatRole.Worm).Index;
        foreach (var e in r.Log)
        {
            if (e.Actor >= 0 && r.Fighters[e.Actor].Side == BattleSide.Enemy && e.Target >= 0 && r.Fighters[e.Target].Side == BattleSide.Player)
                Assert.Equal(worm, e.Target);
            if (e.Actor >= 0 && r.Fighters[e.Actor].Role == CombatRole.Alien)
                Assert.DoesNotContain(e.Action, new[] { BattleAction.Basic, BattleAction.Rage, BattleAction.Combo, BattleAction.Counter });
        }
        Assert.All(r.Fighters.Where(f => f.Role == CombatRole.Alien), f => Assert.Equal(f.StartHp, f.EndHp));
        // each action: a SidekickAct marker naming the ability, then its effect with the same skill id
        var act = r.Log.First(e => e.Action == BattleAction.SidekickAct && r.Fighters[e.Actor].Id == "alien_voidmaw");
        int i = r.Log.IndexOf(act);
        Assert.Equal("ask_voidmaw_1", act.SkillId);
        Assert.Equal(BattleAction.Skill, r.Log[i + 1].Action);
        Assert.Equal("ask_voidmaw_1", r.Log[i + 1].SkillId);
        Assert.Contains(r.Log, e => e.Action == BattleAction.Heal && e.SkillId == "ask_mossling_1" && e.Target == worm);
    }

    /// <summary>Every alien species' ability, at every star tier, acts in a seeded battle and changes it (audit extension).</summary>
    [Fact]
    public void EveryAlienAbility_AtEveryTier_ChangesASeededBattle()
    {
        string Sig(BattleResult r) => SkillTuningTests.Signature(r).Replace(":", ";") + "|" + string.Join(",", r.Fighters.Select(f => f.EndShield));
        var baseline = new Dictionary<ulong, string>();
        var failures = new List<string>();
        foreach (var sp in C.AlienTypes.Values.OrderBy(a => a.Id, StringComparer.Ordinal))
            for (int tier = 0; tier < sp.ArchetypeSkills.Count; tier++)
            {
                bool changed = false, acted = false;
                for (ulong seed = 1; seed <= 4 && !changed; seed++)
                {
                    if (!baseline.TryGetValue(seed, out var b)) baseline[seed] = b = Sig(Fight(new CombatAlien[0], rounds: 12, seed: seed));
                    var r = Fight(new[] { Alien(sp.Id, tier) }, rounds: 12, seed: seed);
                    acted |= r.Log.Any(e => e.Action == BattleAction.SidekickAct && e.SkillId == sp.ArchetypeSkills[tier].SkillId);
                    changed = Sig(r) != b;
                }
                if (!acted || !changed) failures.Add(sp.ArchetypeSkills[tier].SkillId + (acted ? " changes nothing" : " never acted"));
            }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void ABlockingSidekick_InterceptsTheNextHit_OnTheWorm()
    {
        var r = Fight(new[] { Alien("alien_pebblehorn") }, rounds: 8);
        int worm = r.Fighters.Single(f => f.Role == CombatRole.Worm).Index;
        var guard = r.Log.First(e => e.Action == BattleAction.Guard);
        Assert.Equal(worm, guard.Target);
        Assert.Equal(1, guard.Stacks);
        var block = r.Log.First(e => e.Action == BattleAction.Block);
        Assert.Equal(guard.Actor, block.Actor);
        Assert.Equal(worm, block.Target);
        Assert.True(block.Amount > 0, "the stopped damage is logged");
        // the intercepted hit lands nothing: the worm's HP / shield are unchanged until the next hit
        var before = r.Log.Take(r.Log.IndexOf(block)).Last(e => e.Target == worm);
        Assert.Equal(before.TargetHp, block.TargetHp);
        // a 10-star Pebblehorn holds 3 hits per action
        Assert.Equal(3, C.Skill(C.AlienType("alien_pebblehorn")!.ArchetypeSkills[^1].SkillId)!.Triggers[0].Stacks);
        Assert.Equal("Intercept the next hit on the worm.", SkillText.Full(C.Skill("ask_pebblehorn_1")));
    }

    [Fact]
    public void Command_GivesExtraSidekickActions_PerAwakening()
    {
        var squad = new[] { Alien("alien_blorp"), Alien("alien_voidmaw") };
        var none = Fight(squad, rounds: 9);
        Assert.Equal(0, Acts(none, CombatRole.Alien, true));
        for (int lv = 1; lv <= 3; lv++)
        {
            var r = Fight(squad, new[] { "sig_alien" }, rounds: 9, levels: new Dictionary<string, int> { ["sig_alien"] = lv });
            Assert.Equal(9 * lv, Acts(r, CombatRole.Alien, true)); // each round N extra actions, round-robin
            Assert.Equal(Acts(none, CombatRole.Alien), Acts(r, CombatRole.Alien));
        }
    }

    [Fact]
    public void TheHiveCarrier_LaunchesADroneSidekick_ForTheBattle()
    {
        var r = Fight(new CombatAlien[0], rounds: 9, mountId: "mount_hive_carrier", meta: new[] { "mount_hive_carrier_w1" });
        var summon = r.Log.Single(e => e.Action == BattleAction.Summon);
        var drone = r.Fighters[summon.Target];
        Assert.Equal(CombatRole.Summon, drone.Role);
        Assert.False(drone.Targetable);
        Assert.Equal(CombatRole.Mount, r.Fighters[summon.Actor].Role);
        Assert.True(Acts(r, CombatRole.Summon) >= 3);
        Assert.Contains(r.Log, e => e.Action == BattleAction.Skill && e.Actor == drone.Index && e.SkillId == "skab_drone" && e.Amount > 0);
        // battle-scoped: nothing in the loadout; Swarm Beacon launches three
        var swarm = Fight(new CombatAlien[0], new[] { "mythic_swarm_call" }, rounds: 3);
        Assert.Equal(3, swarm.Fighters.Count(f => f.Role == CombatRole.Summon));
    }

    [Fact]
    public void AbilityPower_FoldsHpAtkDef_AndAlienPowerScalesSupport()
    {
        var t = C.Combat;
        Assert.Equal(20 + 200 * t.SidekickHpToPower + 5 * t.SidekickDefToPower, BattleSimulator.SidekickPower(20, 200, 5, t), 6);
        double Shield(double atk, string? bond = null)
        {
            var a = Alien("alien_gravitoad");
            a.Stats.Add(new StatMod(StatId.Atk, atk));
            var r = Fight(new[] { a }, bond != null ? new[] { bond } : null, rounds: 6);
            return r.Log.Where(e => e.Action == BattleAction.Shield && e.SkillId == "ask_gravitoad_1").Select(e => e.Amount).DefaultIfEmpty(0).Max();
        }
        // a shield is never capped: more power (stats or alien power) = a bigger shield on the worm
        double weak = Shield(0), strong = Shield(300), bonded = Shield(0, "alien_bond");
        Assert.True(strong > weak, strong + " vs " + weak);
        Assert.True(bonded > weak, bonded + " vs " + weak);
    }

    [Fact]
    public void ASidekicksBuff_Refreshes_InsteadOfStacking()
    {
        var r = Fight(new[] { Alien("alien_whirrbit", 0, 400) }, rounds: 6);
        int worm = r.Fighters.Single(f => f.Role == CombatRole.Worm).Index;
        Assert.Equal(6, r.Log.Count(e => e.Action == BattleAction.Buff && e.SkillId == "ask_whirrbit_1" && e.Target == worm));
        // a sidekick buffing the worm's DEF by +1000 for 3 rounds every round: refreshed, the enemies' hits stay the same size
        // from round 1 on (stacked, they would shrink further in rounds 2 and 3)
        var c = new ContentDb();
        c.AddSkill(new SkillDef { Id = "def_up", Name = "Def Up", Source = SkillSource.Alien, Draftable = false,
            Triggers = { new SkillTrigger { When = TriggerKind.OnAct, Effect = EffectKind.BuffStat, Target = TargetKind.Worm, Stat = new StatMod(StatId.Def, 1000), Duration = 3 } } });
        var lo = new CombatLoadout { DisplayName = "Worm" };
        lo.WormStats.Add(new StatMod(StatId.Hp, 90000)).Add(new StatMod(StatId.Atk, 1)).Add(new StatMod(StatId.Speed, 100));
        var a = new CombatAlien { AlienTypeId = "a", Name = "A" };
        a.Stats.Add(new StatMod(StatId.Atk, 10)).Add(new StatMod(StatId.Speed, 400));
        a.SkillIds.Add("def_up");
        lo.Aliens.Add(a);
        var setup = new BattleSetup { RoundCap = 4 };
        setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c));
        var foe = BattleSimulator.BuildEnemy(c, "magma_slug", 200);
        foe.SkillIds.Clear();
        setup.Enemy.Add(foe);
        var b = BattleSimulator.Simulate(setup, c, new Rng(9UL));
        int w = b.Fighters.Single(f => f.Role == CombatRole.Worm).Index;
        var hits = b.Log.Where(e => e.Action == BattleAction.Basic && e.Target == w && !e.Crit && !e.Dodge).GroupBy(e => e.Round).Select(g => g.First().Amount).ToList();
        Assert.True(hits.Count >= 3);
        Assert.All(hits, h => Assert.Equal(hits[0], h));
    }

    [Fact]
    public void HivemindAndDecoy_HookOntoSidekickActionsAndBlocks()
    {
        // Hivemind: every sidekick action also triggers its tuned signature once (one Heat stack from Ignite)
        var squad = new[] { Alien("alien_blorp") };
        var r = Fight(squad, new[] { "alien_hivemind", "sig_fire" }, rounds: 12);
        int kick = r.Fighters.First(f => f.Role == CombatRole.Alien).Index;
        foreach (var act in r.Log.Where(e => e.Action == BattleAction.SidekickAct && e.Actor == kick))
        {
            int i = r.Log.IndexOf(act);
            var after = r.Log.Skip(i + 1).TakeWhile(e => e.Action != BattleAction.SidekickAct && e.Round == act.Round).ToList();
            Assert.Contains(after, e => e.SkillId == "sig_fire" && e.Actor == kick && e.Action is BattleAction.StatusApplied or BattleAction.Supernova);
        }
        // Decoy: blocked attackers may be charmed (Bodyguard's guards)
        var d = Fight(squad, new[] { "alien_bodyguard", "alien_decoy" }, rounds: 12, levels: new Dictionary<string, int> { ["alien_bodyguard"] = 3, ["alien_decoy"] = 2 });
        Assert.Contains(d.Log, e => e.Action == BattleAction.Block);
    }

    /// <summary>Sidekick actions and guarded hits are counts: a levelled run skill grows them (2 → 3 → 5), never a fraction.</summary>
    [Fact]
    public void SidekickCounts_GrowWithSkillLevels()
    {
        var c = new ContentDb();
        c.AddSkill(new SkillDef { Id = "rally_probe", Name = "Rally", Family = SkillFamily.AlienSynergy, Rarity = Rarity.Common, Source = SkillSource.Run,
            Triggers = { new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.AllyAction, Stacks = 2 },
                         new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.Block, Target = TargetKind.Worm, Stacks = 1 } } });
        var s = c.Skill("rally_probe")!;
        Assert.Equal(new[] { 3, 2 }, SkillLeveling.At(c, s, 2).Triggers.Select(t => t.Stacks));
        Assert.Equal(new[] { 5, 3 }, SkillLeveling.At(c, s, 3).Triggers.Select(t => t.Stacks));
        Assert.Equal("Battle start: 2 extra sidekick actions.", SkillText.Full(s).Split(". ")[0] + ".");
    }

    /// <summary>Old saved aliens (genes HP / ATK / DEF / Speed, old trait ids) load and read as ability power and act frequency.</summary>
    [Fact]
    public void OldSavedAliens_Load_AndReadAsAbilityPowerAndFrequency()
    {
        const string json = "{\"TypeId\":\"alien_pebblehorn\",\"Level\":12,\"Stars\":3,\"Genes\":{\"Hp\":20,\"Atk\":-5,\"Def\":35,\"Speed\":10,\"Work\":0},\"Traits\":[\"trait_tough\",\"trait_armored\",\"trait_swift\"]}";
        var a = JsonSerializer.Deserialize<OwnedAlien>(json, TestHelpers.Json)!;
        Assert.True(AlienSystem.AbilityPower(C, a) > 40);
        Assert.Equal(100 / (34 * 1.1 * 1.15), AlienSystem.ActsEvery(C, a), 6);
        Assert.Equal(new[] { "Tough", "Armored", "Swift" }, a.Traits.Select(t => C.AlienTrait(t)!.Name));
        Assert.Equal("ask_pebblehorn_2", AlienSystem.ArchetypeSkillId(C, a));
    }
}
