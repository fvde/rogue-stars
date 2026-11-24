using System.Text.Json;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Dead-effects pass (owner: "things that don't work or do nothing must be fixed or removed; the drone stuff must go"):
/// every skill of every source changes a seeded battle or run, Loot Luck does something, no drones are left.
/// </summary>
public class DeadEffectsTests
{
    private static readonly ContentDb C = ContentDb.Default;

    // ---- every skill of every source changes an outcome ------------------------------------------------------------

    private static readonly HashSet<TriggerKind> BoardTriggers = new()
    {
        TriggerKind.OnRunStart, TriggerKind.OnRoll, TriggerKind.OnTileLand, TriggerKind.OnTilePass, TriggerKind.OnLevelUp, TriggerKind.OnBossAwaken,
    };

    private static readonly HashSet<EffectKind> BattleEffects = new()
    {
        EffectKind.Damage, EffectKind.TrueDamage, EffectKind.ApplyStatus, EffectKind.Heal, EffectKind.Shield, EffectKind.GainRage,
        EffectKind.BuffStat, EffectKind.Summon, EffectKind.RemoveStatus, EffectKind.ConsumeStatus, EffectKind.Strike, EffectKind.AllyAction,
        EffectKind.Block, EffectKind.BlockStatus, EffectKind.Rule, EffectKind.ForceSupernova, EffectKind.MultiplyStatus, EffectKind.TransferStatus,
        EffectKind.TickStatus, EffectKind.Execute, EffectKind.Reflect, EffectKind.CheatDeath, EffectKind.FreeRageAttack, EffectKind.Charge,
        EffectKind.LoseHp, EffectKind.StackAll, EffectKind.Sacrifice, EffectKind.Echo, EffectKind.Rewind, EffectKind.Loop,
    };

    /// <summary>Run-only stats: read by the board (run XP, loot luck), never by a battle.</summary>
    private static readonly HashSet<StatId> BoardStats = new() { StatId.XpGain, StatId.LootLuck };

    private static bool ActsInBattle(SkillDef s) =>
        s.Stats.Any(m => !BoardStats.Contains(m.Stat))
        || s.Triggers.Any(t => !BoardTriggers.Contains(t.When) && BattleEffects.Contains(t.Effect) && !(t.When == TriggerKind.OnBattleWon && t.Effect == EffectKind.Heal));

    private static bool IsEnemySkill(ContentDb c, string id) => c.Enemies.Values.Any(e => e.SkillIds.Contains(id));

    /// <summary>
    /// SkillTuningTests' battle signature, but the HP left of untargetable fighters (aliens, mount) does not count: they are never hit,
    /// so a bigger HP pool on them changes nothing but a number nobody reads.
    /// </summary>
    private static string Sig(BattleResult r) =>
        r.Winner + "|" + r.Rounds + "|" + r.PlayerDamageDealt + "|" + r.PlayerDamageTaken + "|" + Math.Round(r.WormHpLeft)
        + "|" + string.Join(",", r.Fighters.Select(f => f.DamageDealt + ":" + (f.Targetable ? f.EndHp + "+" + f.EndShield : "0") + ":" + f.EndRage));

    /// <summary>
    /// The SkillTuningTests arena (worm + mount + two aliens vs a shielded slug, a dodging wraith and a boss), with the skill where its
    /// source puts it: mount weapons on the mount, alien skills on an alien, enemy skills on the slug, everything else on the worm.
    /// </summary>
    private static BattleResult Fight(ContentDb c, string? skillId, int seed, bool brawl, IEnumerable<string>? context = null)
    {
        var lo = new CombatLoadout { DisplayName = "Worm", MountId = "test_mount" };
        lo.WormStats.AddAll(new[]
        {
            new StatMod(StatId.Hp, brawl ? 1100 : 2600), new StatMod(StatId.Atk, 100), new StatMod(StatId.Def, 20), new StatMod(StatId.Speed, 100),
            new StatMod(StatId.CritChance, 10), new StatMod(StatId.CritDamage, 50), new StatMod(StatId.ComboChance, 15),
            new StatMod(StatId.CounterChance, 15), new StatMod(StatId.ParryChance, 20), new StatMod(StatId.Dodge, 10),
        });
        lo.MountStats.Add(new StatMod(StatId.Atk, 25)).Add(new StatMod(StatId.Hp, 80));
        for (int i = 0; i < 2; i++)
        {
            var a = new CombatAlien { AlienTypeId = "alien_" + i, Name = "Alien " + i };
            a.Stats.Add(new StatMod(StatId.Hp, 300)).Add(new StatMod(StatId.Atk, 30)).Add(new StatMod(StatId.Def, 8)).Add(new StatMod(StatId.Speed, 45));
            a.SkillIds.Add(i == 0 ? "ask_voidmaw_1" : "ask_blorp_1"); // sidekicks: a striker and a bleeder (sidekick pass)
            lo.Aliens.Add(a);
        }
        var def = skillId != null ? c.Skill(skillId) : null;
        bool enemy = skillId != null && IsEnemySkill(c, skillId);
        if (def != null && !enemy)
        {
            if (def.Source == SkillSource.Alien) lo.Aliens[0].SkillIds.Add(skillId!);
            else lo.MetaSkillIds.Add(skillId!); // mount weapons land on the mount (BuildPlayerSide)
        }
        if (context != null) lo.MetaSkillIds.AddRange(context);
        var setup = new BattleSetup();
        setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, SkillTuningTests.KitIds));
        setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "magma_slug", 1.5));
        setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "ash_wraith", 1.5));
        setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "boss_frost_leviathan", brawl ? 1.2 : 1.0));
        if (enemy) setup.Enemy[0].SkillIds.Add(skillId!);
        return BattleSimulator.Simulate(setup, c, new Rng((ulong)(1000 + seed)));
    }

    /// <summary>
    /// A mount skill is judged on its mount as the player has it when the skill unlocks: with the mount's other weapons of that
    /// star / awakening step (Star Lancer's bleed support fires on the crits its lance weapons give the mount).
    /// </summary>
    private static List<string> MountContext(ContentDb c, string skillId)
    {
        foreach (var d in c.MountConfig.Defs.Values)
        {
            var step = d.Weapons.FirstOrDefault(w => w.SkillId == skillId);
            if (step == null) continue;
            var active = new List<string>();
            foreach (var w in d.Weapons)
            {
                if (w.Star > step.Star || (w.Star == step.Star && w.Awaken > step.Awaken) || w.SkillId == skillId) continue;
                if (w.Replaces != null) active.Remove(w.Replaces);
                active.Add(w.SkillId);
            }
            active.Remove(step.Replaces ?? "");
            return active;
        }
        return new List<string>();
    }

    /// <summary>A whole auto-played run (strong worm, the skill as a meta skill) as a signature without the skill's own id.</summary>
    private static string RunSignature(string? skillId, long seed, ContentDb? content = null, IEnumerable<StatMod>? extra = null)
    {
        var worm = RunTestKit.Strong();
        if (skillId != null) worm.Meta.Add(skillId);
        if (extra != null) worm.Extra.AddRange(extra);
        var e = RunTestKit.Engine(worm, content);
        var s = RunTestKit.StartRun(e, seed: seed);
        RunTestKit.AutoToEnd(e, ref s);
        string json = JsonSerializer.Serialize(new object[] { s.Run.LastSummary!, s.Wallet, s.Counters }, TestHelpers.Json);
        return skillId == null ? json : json.Replace("\"" + skillId + "\"", "\"\"");
    }

    /// <summary>
    /// Owner: "things that don't work or do nothing must be fixed or removed". Every skill in the content — run picks, laboratory,
    /// hero traits, mount weapons, alien skills, building boons, part skills and enemy skills — changes a seeded battle (battle
    /// skills) or a seeded run (board skills: coins, dice, awakening, XP, loot, skipped events, start skills).
    /// </summary>
    [Fact]
    public void EverySkill_OfEverySource_ChangesASeededBattleOrRun()
    {
        var c = SkillTuningTests.KitContent();
        var skills = C.Skills.Values.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        foreach (SkillSource src in Enum.GetValues(typeof(SkillSource)))
            Assert.True(skills.Any(s => s.Source == src), "no skill of source " + src);

        var baseline = new Dictionary<(int, bool), string>();
        string Base(int seed, bool brawl) =>
            baseline.TryGetValue((seed, brawl), out var b) ? b : baseline[(seed, brawl)] = Sig(Fight(c, null, seed, brawl));
        var runBaseline = new Dictionary<long, string>();
        string RunBase(long seed) => runBaseline.TryGetValue(seed, out var b) ? b : runBaseline[seed] = RunSignature(null, seed);

        var failures = new List<string>();
        int battle = 0, board = 0;
        foreach (var s in skills)
        {
            bool changed = false;
            if (ActsInBattle(s))
            {
                battle++;
                var ctx = s.Source == SkillSource.Mount ? MountContext(c, s.Id) : null;
                foreach (bool brawl in new[] { false, true })
                    for (int seed = 0; seed < 8 && !changed; seed++)
                        changed = Sig(Fight(c, s.Id, seed, brawl, ctx))
                            != (ctx == null || ctx.Count == 0 ? Base(seed, brawl) : Sig(Fight(c, null, seed, brawl, ctx)));
            }
            else if (SkillTuningTests.IsBoardSkill(s)) changed = true; // situational board skills: their own run tests in SkillTuningTests
            else
            {
                board++;
                for (long seed = 1; seed <= 4 && !changed; seed++) changed = RunSignature(s.Id, seed) != RunBase(seed);
            }
            if (!changed) failures.Add(s.Id + " (" + s.Source + "): " + (ActsInBattle(s) ? "no battle changed" : "no run changed"));
        }
        Assert.True(battle > 300 && board > 20, battle + " battle / " + board + " board skills");
        Assert.True(failures.Count == 0, failures.Count + " dead skills:\n" + string.Join("\n", failures));
    }

    /// <summary>The checks catch dead skills: a % of a zero-base stat changes neither a battle nor a run; the same number as points does.</summary>
    [Fact]
    public void TheAudit_CatchesDeadSkills()
    {
        var c = SkillTuningTests.KitContent();
        for (int seed = 0; seed < 8; seed++)
            Assert.Equal(Sig(Fight(c, null, seed, false)), Sig(Fight(c, "dead_probe", seed, false)));
        var rc = new ContentDb();
        rc.AddSkill(new SkillDef { Id = "dead_xp", Name = "Dead XP", Source = SkillSource.Building, Draftable = false, Stats = { new StatMod(StatId.XpGain, 0, 50) } });
        rc.AddSkill(new SkillDef { Id = "live_xp", Name = "Live XP", Source = SkillSource.Building, Draftable = false, Stats = { new StatMod(StatId.XpGain, 50) } });
        string baseline = RunSignature(null, 3, rc);
        Assert.Equal(baseline, RunSignature("dead_xp", 3, rc));
        Assert.NotEqual(baseline, RunSignature("live_xp", 3, rc));
    }

    // ---- loot luck ------------------------------------------------------------------------------------------------

    private static (int completionRolls, int items) LootRun(double luck, long seed)
    {
        var worm = RunTestKit.Strong();
        if (luck > 0) worm.Extra.Add(new StatMod(StatId.LootLuck, luck));
        var e = RunTestKit.Engine(worm);
        var s = RunTestKit.StartRun(e, seed: seed);
        RunTestKit.AutoToEnd(e, ref s);
        var sum = s.Run.LastSummary!;
        Assert.True(sum.Won);
        return (sum.Rewards.Count(r => r.Source == RunRewardLine.CompletionRoll), sum.Rewards.Count(r => r.Source is RunRewardLine.CompletionRoll or RunRewardLine.RunLoot));
    }

    /// <summary>
    /// Loot Luck (lab Salvage Algorithms, Salvage Yard, the Salvage Yard / Blueprint Relay collection) used to be read by nothing.
    /// It is the % chance of a bonus item on every run loot roll: 100 points double every roll, 10 points add items on some runs.
    /// </summary>
    [Fact]
    public void LootLuck_RollsBonusItems()
    {
        for (long seed = 1; seed <= 3; seed++)
        {
            var none = LootRun(0, seed);
            var sure = LootRun(100, seed);
            Assert.True(none.completionRolls > 0);
            Assert.Equal(2 * none.completionRolls, sure.completionRolls);
            Assert.True(sure.items > none.items);
        }
        int more = 0;
        for (long seed = 1; seed <= 12; seed++) if (LootRun(30, seed).items > LootRun(0, seed).items) more++;
        Assert.InRange(more, 1, 11); // a chance, not a sure thing
    }

    /// <summary>The lab node reaches the worm: Salvage Algorithms Lv5 = 10 loot-luck points in the loadout.</summary>
    [Fact]
    public void SalvageAlgorithms_GivesLootLuckPoints()
    {
        var node = C.LabNode("lab_loot_luck")!;
        Assert.Equal(LabNodeKind.Stat, node.Kind);
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        double before = e.BuildLoadout(s, TestHelpers.T0).WormStats.Value(StatId.LootLuck);
        s.Lab.Entries["lab_loot_luck"] = new LabEntry { Level = 5, Enabled = true };
        double after = e.BuildLoadout(s, TestHelpers.T0).WormStats.Value(StatId.LootLuck);
        Assert.Equal(10, after - before, 6);
        Assert.Contains("bonus loot chance", node.Description);
    }

    // ---- drones came back as sidekicks (sidekick pass); enemy summons are the bosses' guards ------------------------

    /// <summary>
    /// Every Summon is either an enemy skill calling an enemy (the Storm Hive's spark drones) or a player skill launching a
    /// battle-scoped sidekick (ContentDb.Sidekicks: Hive Carrier's Drone Bay, Swarm Beacon). Only those mention drones.
    /// </summary>
    [Fact]
    public void Summons_AreEnemyGuards_OrSidekickDrones()
    {
        var enemySkills = new HashSet<string>(C.Enemies.Values.SelectMany(e => e.SkillIds));
        foreach (var s in C.Skills.Values)
        {
            bool sidekickSummon = false;
            foreach (var t in s.Triggers.Where(t => t.Effect == EffectKind.Summon))
            {
                if (enemySkills.Contains(s.Id)) Assert.True(t.Param != null && C.Enemies.ContainsKey(t.Param), s.Id + " summons an unknown enemy " + t.Param);
                else
                {
                    Assert.True(C.Sidekick(t.Param) != null, s.Id + " summons an unknown sidekick " + t.Param);
                    Assert.True(t.Value > 0, s.Id + ": a sidekick summon needs power");
                    sidekickSummon = true;
                }
            }
            if (enemySkills.Contains(s.Id) || sidekickSummon || C.Sidekicks.Values.Any(k => k.AbilitySkillId == s.Id)) continue;
            foreach (var text in new[] { s.Name, s.Description, SkillText.Full(s) })
                Assert.DoesNotContain("drone", text ?? "", StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains(C.Skills.Values, s => enemySkills.Contains(s.Id) && s.Triggers.Any(t => t.Effect == EffectKind.Summon)); // the boss summons still work
        var bay = C.Skill("mount_hive_carrier_w1")!;
        Assert.Equal("Drone Bay", bay.Name);
        Assert.Contains(bay.Triggers, t => t.Effect == EffectKind.Summon && t.Param == "sk_drone");
    }

    [Fact]
    public void ASummonOnThePlayerSide_DoesNothing()
    {
        var c = new ContentDb();
        c.AddSkill(new SkillDef { Id = "p_summon", Name = "Summon", Source = SkillSource.Building, Draftable = false,
            Triggers = { new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.Summon, Param = "spark_drone" } } });
        var lo = new CombatLoadout { DisplayName = "Worm" };
        lo.WormStats.Add(new StatMod(StatId.Hp, 3000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Speed, 100));
        var setup = new BattleSetup();
        setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, new[] { "p_summon" }));
        setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "magma_slug", 1));
        var r = BattleSimulator.Simulate(setup, c, new Rng(1UL));
        Assert.DoesNotContain(r.Log, x => x.Action == BattleAction.Summon);
        // an enemy boss still calls its guard
        var b = new BattleSetup();
        b.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, null));
        var boss = BattleSimulator.BuildEnemy(c, "magma_slug", 1);
        boss.SkillIds.Add("p_summon");
        b.Enemy.Add(boss);
        Assert.Contains(BattleSimulator.Simulate(b, c, new Rng(1UL)).Log, x => x.Action == BattleAction.Summon);
    }

    // ---- zero-base percent fixes outside skills ------------------------------------------------------------------

    [Fact]
    public void AlienCombatTraits_ChangeABattle_ForASpeciesThatRollsThem()
    {
        foreach (var tr in C.AlienTraits.Values.Where(t => t.CombatStats.Count > 0))
        {
            bool changed = false;
            foreach (var sp in C.AlienTypes.Values.Where(a => a.PossibleTraits.Contains(tr.Id)))
            {
                string Battle(bool with)
                {
                    var lo = new CombatLoadout { DisplayName = "Worm" };
                    lo.WormStats.Add(new StatMod(StatId.Hp, 2600)).Add(new StatMod(StatId.Atk, 100)).Add(new StatMod(StatId.Def, 20)).Add(new StatMod(StatId.Speed, 100));
                    var a = new CombatAlien { AlienTypeId = sp.Id, Name = sp.Name };
                    a.Stats.Add(new StatMod(StatId.Hp, 300)).Add(new StatMod(StatId.Atk, 30)).Add(new StatMod(StatId.Def, 8)).Add(new StatMod(StatId.Speed, 45));
                    if (with) a.Stats.AddAll(tr.CombatStats);
                    a.SkillIds.Add(sp.ArchetypeSkills[^1].SkillId);
                    lo.Aliens.Add(a);
                    var setup = new BattleSetup();
                    setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, C, null));
                    setup.Enemy.Add(BattleSimulator.BuildEnemy(C, "magma_slug", 1.5));
                    setup.Enemy.Add(BattleSimulator.BuildEnemy(C, "ash_wraith", 1.5));
                    setup.Enemy.Add(BattleSimulator.BuildEnemy(C, "boss_frost_leviathan", 1.0));
                    return Sig(BattleSimulator.Simulate(setup, C, new Rng(7UL)));
                }
                if (Battle(true) != Battle(false)) { changed = true; break; }
            }
            Assert.True(changed, tr.Id + " changes no battle");
        }
    }

    [Fact]
    public void PointStats_AreAddedAsPoints_ByRunesCollectionsAndTiles()
    {
        foreach (var r in C.PartsConfig.Runes.Values)
            Assert.True(!r.IsPercent || !StatMod.IsPointStat(r.Stat), r.Id);
        // every blueprint's collection bonus at 10 stars
        var e = TestHelpers.Engine();
        var s = TestHelpers.NewPlayer(e);
        foreach (var id in C.Blueprints.Keys) s.City.Blueprints[id] = new OwnedBlueprint { Id = id, Stars = 10 };
        var mods = CitySystem.CollectionBonus(s, C);
        Assert.Contains(mods, m => m.Stat == StatId.LootLuck && m.Flat > 0);
        Assert.All(mods, m => Assert.True(m.Percent == 0 || !StatMod.IsPointStat(m.Stat), m.Stat.ToString()));
        // Sharp Stone: "+15% crit chance" is 15 points (it was 15% of the worm's 5)
        Assert.Equal(15, C.BoardTiles["buff_crit"].Value);
        var w = RunTestKit.Engine(new RunTestKit.WormSetup { Extra = { new StatMod(StatId.CritChance, 5) } });
        var st = RunTestKit.StartRun(w);
        var run = st.Run.Active!;
        double crit0 = BattleSimulator.FinalStats(RunSystem.WormSpec(w.Content, run), w.Content).Value(StatId.CritChance);
        RunTestKit.LineBoard(run, Enumerable.Repeat("buff_crit", 20).ToArray());
        run.Phase = RunPhase.AwaitRoll;
        Assert.True(RunTestKit.Exec(w, ref st, new RunRoll()).Ok);
        Assert.Equal(crit0 + 15, BattleSimulator.FinalStats(RunSystem.WormSpec(w.Content, st.Run.Active!), w.Content).Value(StatId.CritChance), 6);
    }

    [Fact]
    public void TheMountsAlienPower_ReachesTheSquad()
    {
        var c = new ContentDb();
        string Sig(double ap)
        {
            var lo = new CombatLoadout { DisplayName = "Worm", MountId = "m" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 3000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Speed, 100));
            lo.MountStats.Add(new StatMod(StatId.Atk, 20)).Add(new StatMod(StatId.Hp, 50));
            if (ap > 0) lo.MountStats.Add(new StatMod(StatId.AlienPower, ap));
            var a = new CombatAlien { AlienTypeId = "a", Name = "A" };
            a.Stats.Add(new StatMod(StatId.Hp, 300)).Add(new StatMod(StatId.Atk, 30)).Add(new StatMod(StatId.Speed, 90));
            a.SkillIds.Add("ask_voidmaw_1");
            lo.Aliens.Add(a);
            var setup = new BattleSetup();
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, null));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "magma_slug", 2));
            var r = BattleSimulator.Simulate(setup, c, new Rng(3UL));
            return r.Fighters.First(f => f.Role == CombatRole.Alien).DamageDealt.ToString();
        }
        Assert.True(long.Parse(Sig(20)) > long.Parse(Sig(0)));
    }

    // ---- old saves ----------------------------------------------------------------------------------------------------

    /// <summary>Removed state (the Salvage Yard's old run-loot %): a saved run that still carries it loads and finishes.</summary>
    [Fact]
    public void ASavedRun_WithTheRemovedLootPercent_StillLoads()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        string json = JsonSerializer.Serialize(s, TestHelpers.Json);
        Assert.Contains("\"SkipNegativeEvents\":", json);
        json = json.Replace("\"SkipNegativeEvents\":", "\"ExtraLootPct\":25,\"SkipNegativeEvents\":");
        var loaded = JsonSerializer.Deserialize<GameState>(json, TestHelpers.Json)!;
        RunTestKit.AutoToEnd(e, ref loaded);
        Assert.True(loaded.Run.LastSummary!.Won);
    }
}
