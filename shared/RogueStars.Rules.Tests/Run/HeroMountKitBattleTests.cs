using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Hero and mount kits in battle (engine requirements X1–X18 of the Hero and Mount Mechanics page): mount reloads and the rider
/// link, hero timers and counters, signature carries, extra hits, the rage bank, marks, coils and shields, and the shared caps
/// (one fatal-save per battle, rage gain per round, one timer advance per round, copied stacks, one extra mount shot per round).
/// </summary>
public class HeroMountKitBattleTests
{
    /// <summary>Default content plus test probes (status appliers, an on-crit marker, a sure shield, a weak hitter).</summary>
    internal static readonly ContentDb C = Probes();

    private static ContentDb Probes()
    {
        var c = new ContentDb();
        void P(string id, params SkillTrigger[] t) => c.AddSkill(new SkillDef { Id = id, Name = id, Source = SkillSource.Building, Draftable = false, Triggers = t.ToList() });
        P("probe_bleed3", new SkillTrigger { When = TriggerKind.OnHit, Effect = EffectKind.ApplyStatus, Status = StatusId.Bleed, Stacks = 3 });
        P("probe_rad5", new SkillTrigger { When = TriggerKind.OnHit, Effect = EffectKind.ApplyStatus, Status = StatusId.Poison, Stacks = 5 });
        P("probe_heat1", new SkillTrigger { When = TriggerKind.OnHit, Effect = EffectKind.ApplyStatus, Status = StatusId.Heat, Stacks = 1 });
        P("probe_oncrit", new SkillTrigger { When = TriggerKind.OnCrit, Effect = EffectKind.GainRage, Target = TargetKind.Self, Value = 7 });
        P("probe_bigshield", new SkillTrigger { When = TriggerKind.OnRoundStart, Effect = EffectKind.Shield, Target = TargetKind.Self, Value = 100 });
        c.AddSkill(new SkillDef { Id = "probe_sharp", Name = "Sharp", Source = SkillSource.Building, Draftable = false, Stats = { new StatMod(StatId.CritChance, 100) } });
        c.AddSkill(new SkillDef { Id = "probe_combo", Name = "Combo", Source = SkillSource.Building, Draftable = false, Stats = { new StatMod(StatId.ComboChance, 95) } });
        c.AddSkill(new SkillDef { Id = "probe_fury", Name = "Fury", Source = SkillSource.Building, Draftable = false, Stats = { new StatMod(StatId.RageGain, 300) } });
        return c;
    }

    internal static CombatantSpec Unit(string id, double hp, double atk, double speed = 50, CombatRole role = CombatRole.Enemy, double def = 0)
    {
        var s = new CombatantSpec { Id = id, Name = id, Role = role, Targetable = true };
        s.Stats.Add(new StatMod(StatId.Hp, hp)).Add(new StatMod(StatId.Atk, atk)).Add(new StatMod(StatId.Def, def)).Add(new StatMod(StatId.Speed, speed));
        return s;
    }

    internal static List<string> Hero(string id, int star) => HeroSystem.TraitSkills(C, id, star);
    internal static List<string> Mount(string id, int stars, int awaken = 0) => MountSystem.WeaponSkills(C, id, stars, awaken);

    /// <summary>A worm (ATK 100, no crit unless a probe gives it) with the given meta / run skills, a mount, aliens, against enemies.</summary>
    internal static BattleResult Fight(IEnumerable<string> meta, string? mount = null, IEnumerable<CombatantSpec>? enemies = null, int rounds = 10,
        ulong seed = 1, double hp = 5000, double speed = 100, IEnumerable<CombatAlien>? aliens = null, double mountPower = 0, ContentDb? c = null)
    {
        c ??= C;
        var lo = new CombatLoadout { DisplayName = "Worm", MountId = mount };
        lo.WormStats.Add(new StatMod(StatId.Hp, hp)).Add(new StatMod(StatId.Atk, 100)).Add(new StatMod(StatId.Speed, speed));
        if (mountPower > 0) lo.MountStats.Add(new StatMod(StatId.MountPower, mountPower));
        lo.MetaSkillIds.AddRange(meta);
        if (aliens != null) lo.Aliens.AddRange(aliens);
        var setup = new BattleSetup { RoundCap = rounds };
        setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c));
        setup.Enemy.AddRange(enemies ?? new[] { Unit("e1", 20000, 40), Unit("e2", 20000, 40) });
        return BattleSimulator.Simulate(setup, c, new Rng(seed));
    }

    private static int Worm(BattleResult r) => r.Fighters.First(f => f.Role == CombatRole.Worm && f.Side == BattleSide.Player).Index;
    private static int MountIx(BattleResult r) => r.Fighters.First(f => f.Role == CombatRole.Mount).Index;
    private static List<int> Volleys(BattleResult r) => r.Log.Where(e => e.Action == BattleAction.Volley && e.Amount == 0).Select(e => e.Round).ToList();

    // ---- mounts: a vehicle on a reload ----------------------------------------------------------------------------------

    [Fact]
    public void AMount_FiresItsActive_EveryReloadRounds_FromRound1_AndNeverAttacks()
    {
        var r = Fight(Mount("mount_magma_barge", 1), "mount_magma_barge", rounds: 10);
        Assert.Equal(new[] { 1, 4, 7, 10 }, Volleys(r));
        var a3 = Fight(Mount("mount_magma_barge", 5, 3), "mount_magma_barge", rounds: 10);
        Assert.Equal(new[] { 1, 3, 5, 7, 9 }, Volleys(a3));
        int m = MountIx(r);
        Assert.DoesNotContain(r.Log, e => e.Actor == m && e.Action is BattleAction.Basic or BattleAction.Rage or BattleAction.Combo);
        Assert.All(r.Log.Where(e => e.Action == BattleAction.Volley), e => Assert.Equal(3, e.Stacks)); // the reload for the HUD
    }

    [Fact]
    public void MountDamage_IsAPercentOfTheWormsAtk_TimesMountDamage()
    {
        var r = Fight(Mount("mount_magma_barge", 1), "mount_magma_barge", rounds: 1, mountPower: 25);
        var bomb = r.Log.First(e => e.Actor == MountIx(r) && e.Action == BattleAction.Skill);
        Assert.Equal("mount_magma_barge_active_1", bomb.SkillId);
        Assert.Equal(150, bomb.Amount); // 120% × worm ATK 100 × (1 + 25% mount damage), enemy DEF 0
    }

    [Fact]
    public void RiderLink_MountStatusesAreTheWorms_AndMountCritsFireTheWormsOnCritSkills()
    {
        // Scan Pulse applies a random owned status (the worm applies Bleed) with the worm as its source: the ticks are the worm's
        var r = Fight(Mount("mount_scout_saucer", 1).Append("probe_bleed3"), "mount_scout_saucer", rounds: 3);
        int w = Worm(r), m = MountIx(r);
        Assert.Contains(r.Log, e => e.Actor == m && e.Action == BattleAction.StatusApplied && e.Status == StatusId.Bleed && e.SkillId == "mount_scout_saucer_active_1");
        Assert.All(r.Log.Where(e => e.Action == BattleAction.StatusTick && r.Fighters[e.Target].Side == BattleSide.Enemy), e => Assert.Equal(w, e.Actor));
        // Impale always crits; the crit fires the worm's on-crit skills (crit link)
        var l = Fight(Mount("mount_star_lancer", 1).Append("probe_oncrit"), "mount_star_lancer", rounds: 1);
        int lw = Worm(l), lm = MountIx(l);
        var impale = l.Log.First(e => e.Actor == lm && e.Action == BattleAction.Skill);
        Assert.True(impale.Crit);
        var after = l.Log.SkipWhile(e => e != impale).Skip(1).First();
        Assert.Equal((lw, BattleAction.GainRage, "probe_oncrit"), (after.Actor, after.Action, after.SkillId));
    }

    // ---- heroes: timers and counters ---------------------------------------------------------------------------------

    [Fact]
    public void Starfall_FiresOnEvery4thHitAShieldAbsorbs_AndTheHudCountShows()
    {
        var r = Fight(Hero("hero_star_knight", 1), enemies: new[] { Unit("e1", 20000, 40, 200), Unit("e2", 20000, 40, 200), Unit("e3", 20000, 40, 200) }, rounds: 3);
        int w = Worm(r);
        Assert.Contains(r.Log, e => e.Round == 0 && e.Action == BattleAction.Shield && e.Amount == 600); // Starguard: 12% of 5000
        var counts = r.Log.Where(e => e.Action == BattleAction.KitCounter && e.Actor == w).Select(e => e.Amount).ToList();
        Assert.Equal(new long[] { 1, 2, 3, 4, 0 }, counts.Take(5));                                  // "Starfall 1/4 … 4/4", then reset
        Assert.All(r.Log.Where(e => e.Action == BattleAction.KitCounter), e => Assert.Equal(4, e.Stacks));
        var ability = r.Log.First(e => e.Action == BattleAction.Ability);
        Assert.Equal("hero_star_knight_ability", ability.SkillId);
        var hits = r.Log.SkipWhile(e => e != ability).Where(e => e.Action == BattleAction.Skill && e.SkillId == "hero_star_knight_ability").Take(3).ToList();
        Assert.Equal(3, hits.Select(e => e.Target).Distinct().Count());                              // every enemy
        Assert.Contains(r.Log.SkipWhile(e => e != ability), e => e.Action == BattleAction.Shield && e.SkillId == "hero_star_knight_ability" && e.Amount == 300);
        Assert.All(r.Log.Where(e => e.Action == BattleAction.GainRage && e.SkillId == "hero_star_knight_trait"), e => Assert.Equal(5, e.Amount));
    }

    [Fact]
    public void Feast_EatsUpTo6OfTheLargestStatus_AndTasteTestHeals1PercentPerStack_Max8PerRound()
    {
        var r = Fight(Hero("hero_chef", 1).Append("probe_bleed3").Append("probe_rad5"), rounds: 4, hp: 5000,
            enemies: new[] { Unit("e1", 20000, 300), Unit("e2", 20000, 300) });
        var feast = r.Log.Single(e => e.Action == BattleAction.Ability);
        Assert.Equal(4, feast.Round);                                                                   // every 4th round
        var eaten = r.Log.SkipWhile(e => e != feast).First(e => e.Action == BattleAction.StatusRemoved);
        Assert.Equal((StatusId.Poison, 6L), (eaten.Status, eaten.Amount));                              // Radiation (largest), 6 stacks
        var bite = r.Log.SkipWhile(e => e != eaten).First(e => e.Action == BattleAction.Skill && e.SkillId == "hero_chef_ability");
        Assert.Equal(150, bite.Amount);                                                                 // 6 × 25% ATK
        foreach (var g in r.Log.Where(e => e.Action == BattleAction.Heal && e.SkillId == "hero_chef_trait").GroupBy(e => e.Round))
            Assert.InRange(g.Sum(e => e.Amount), 1, 400);                                               // max 8% of 5000 per round
        Assert.Contains(r.Log, e => e.Action == BattleAction.Heal && e.SkillId == "hero_chef_trait" && e.Round == 4);
    }

    [Fact]
    public void Broadside_AlwaysCrits_OnEveryEnemy_AndFiresOnCritSkills_VoidGalleonOpensWithIt()
    {
        var r = Fight(Hero("hero_void_pirate", 1).Append("probe_oncrit"), rounds: 4);
        var ab = r.Log.Single(e => e.Action == BattleAction.Ability);
        var hits = r.Log.SkipWhile(e => e != ab).Where(e => e.Action == BattleAction.Skill && e.SkillId == "hero_void_pirate_ability").ToList();
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.True(h.Crit));
        Assert.Equal(2, r.Log.SkipWhile(e => e != ab).TakeWhile(e => e.Round == 4).Count(e => e.SkillId == "probe_oncrit"));
        var g = Fight(Hero("hero_void_pirate", 10), rounds: 1);
        Assert.Contains(g.Log, e => e.Round == 0 && e.Action == BattleAction.Ability);
    }

    [Fact]
    public void DoubleTap_ExtraHitsApplyTheirStatusesTwice()
    {
        var r = Fight(Hero("hero_noodle_ninja", 1).Append("probe_heat1").Append("probe_combo"), rounds: 1,
            enemies: new[] { Unit("e1", 200000, 1) });
        var combo = r.Log.First(e => e.Action == BattleAction.Combo);
        var basic = r.Log.First(e => e.Action == BattleAction.Basic && e.Actor == Worm(r));
        var heatAfterBasic = r.Log.SkipWhile(e => e != basic).First(e => e.Action == BattleAction.StatusApplied);
        var heatAfterCombo = r.Log.SkipWhile(e => e != combo).First(e => e.Action == BattleAction.StatusApplied);
        Assert.Equal(1, heatAfterBasic.Stacks);
        Assert.Equal(heatAfterBasic.Stacks + 2, heatAfterCombo.Stacks);
    }

    [Fact]
    public void NoodleKnot_EveryThirdTurn_RaisesComboAndCounterUntilTheNextTurn()
    {
        var r = Fight(Hero("hero_noodle_ninja", 3), rounds: 7);
        Assert.Equal(new[] { 3, 6 }, r.Log.Where(e => e.Action == BattleAction.Ability).Select(e => e.Round));
        Assert.Equal(new long[] { 45, 45 }, r.Log.Where(e => e.Action == BattleAction.Buff && e.SkillId == "hero_noodle_ninja_ability_3").Take(2).Select(e => e.Amount));
    }

    [Fact]
    public void DeepWells_DoublesBleedsCap_ButMaxConditionsReadTheBaseCap()
    {
        int MaxBleed(int star)
        {
            var r = Fight(Hero("hero_alchemist", star).Append("probe_bleed3"), rounds: 12, enemies: new[] { Unit("e1", 900000, 1) });
            return r.Log.Where(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Bleed).Max(e => e.Stacks);
        }
        Assert.Equal(10, MaxBleed(7));
        Assert.Equal(20, MaxBleed(10));
        // a "max:Bleed" gate fires at the base cap (10) with Deep Wells too
        var c = new ContentDb();
        c.AddSkill(new SkillDef { Id = "probe_b", Name = "b", Source = SkillSource.Building, Draftable = false, Triggers = { new SkillTrigger { When = TriggerKind.OnHit, Effect = EffectKind.ApplyStatus, Status = StatusId.Bleed, Stacks = 5 } } });
        c.AddSkill(new SkillDef { Id = "probe_atmax", Name = "m", Source = SkillSource.Building, Draftable = false, Triggers = { new SkillTrigger { When = TriggerKind.OnHit, Effect = EffectKind.GainRage, Target = TargetKind.Self, Value = 1, If = "max:Bleed" } } });
        var g = Fight(HeroSystem.TraitSkills(c, "hero_alchemist", 10).Append("probe_b").Append("probe_atmax"), rounds: 3, enemies: new[] { Unit("e1", 900000, 1) }, c: c);
        Assert.Contains(g.Log, e => e.SkillId == "probe_atmax"); // stacks reach 10 in round 2 (5 + 5), the gate opens though the cap is 20
    }

    [Fact]
    public void Conduit_PoolsTheHighestElementBonus_AtHalf()
    {
        var c = new ContentDb();
        c.AddSkill(new SkillDef { Id = "probe_fire40", Name = "f", Source = SkillSource.Building, Draftable = false, Stats = { new StatMod(StatId.FireDamage, 40) } });
        c.AddSkill(new SkillDef { Id = "probe_icezap", Name = "z", Family = SkillFamily.Ice, Source = SkillSource.Building, Draftable = false,
            Triggers = { new SkillTrigger { When = TriggerKind.OnAttack, Effect = EffectKind.Damage, Value = 100 } } });
        long Zap(int star) => Fight(HeroSystem.TraitSkills(c, "hero_alchemist", star).Append("probe_fire40").Append("probe_icezap"), rounds: 1,
            enemies: new[] { Unit("e1", 900000, 1) }, c: c).Log.First(e => e.SkillId == "probe_icezap").Amount;
        Assert.Equal(100, Zap(5));
        Assert.Equal(120, Zap(7)); // ice damage +20 = half the +40 fire damage
    }

    [Fact]
    public void Overcharge_BanksRageAbove100_AndAFullBankFiresTheRageAttackTwice()
    {
        var r = Fight(Hero("hero_sparkplug", 1).Append("probe_fury"), rounds: 8, enemies: new[] { Unit("e1", 900000, 30, 200), Unit("e2", 900000, 30, 200) });
        int w = Worm(r);
        var bank = r.Log.Where(e => e.Action == BattleAction.KitCounter && e.Actor == w).ToList();
        Assert.Contains(bank, e => e.Amount == 100 && e.Stacks == 100);
        Assert.All(bank, e => Assert.InRange(e.Amount, 0, 100));
        var doubled = r.Log.Where(e => e.Action == BattleAction.Rage && e.Actor == w).GroupBy(e => e.Round).Where(g => g.Count() == 2).ToList();
        Assert.NotEmpty(doubled);
        Assert.DoesNotContain(r.Log.Where(e => e.Action == BattleAction.Rage && e.Actor == w).GroupBy(e => e.Round), g => g.Count() > 2); // once per round
    }

    [Fact]
    public void Ignition_IsCappedAt20RagePerRound()
    {
        var r = Fight(Hero("hero_sparkplug", 3), rounds: 6, enemies: Enumerable.Range(0, 5).Select(i => Unit("e" + i, 900000, 5, 200)));
        var perRound = r.Log.Where(e => e.Action == BattleAction.GainRage && e.SkillId == "hero_sparkplug_trait_3").GroupBy(e => e.Round).Select(g => g.Sum(e => e.Amount)).ToList();
        Assert.All(perRound, x => Assert.InRange(x, 1, 20));
        Assert.Contains(20L, perRound); // 5 hits × 8 = 40 wanted, capped at 20
    }

    [Fact]
    public void PressureValve_Every10StacksGivesRage_MaxPerRound()
    {
        var r = Fight(Mount("mount_magma_barge", 1).Append("probe_rad5").Append("probe_combo"), "mount_magma_barge", rounds: 6,
            enemies: new[] { Unit("e1", 900000, 1) });
        var valve = r.Log.Where(e => e.Action == BattleAction.GainRage && e.SkillId == "mount_magma_barge_passive_1").ToList();
        Assert.NotEmpty(valve);
        Assert.All(valve, e => Assert.Equal(Worm(r), e.Target));
        Assert.All(valve.GroupBy(e => e.Round), g => Assert.InRange(g.Sum(e => e.Amount), 10, 30)); // Rage cap: 30 per round
    }

    [Fact]
    public void Whistle_EverySidekickActsFastestFirst_ThenTheMountFires_PackGuardShields()
    {
        var slow = SidekickTests.Alien("alien_blorp", 1, 10);
        var fast = SidekickTests.Alien("alien_zib", 1, 20);
        var r = Fight(Hero("hero_beast_tamer", 7).Concat(Mount("mount_magma_barge", 1)), "mount_magma_barge", rounds: 3, aliens: new[] { slow, fast });
        var ab = r.Log.Single(e => e.Action == BattleAction.Ability);
        var after = r.Log.SkipWhile(e => e != ab).ToList();
        var acts = after.Where(e => e.Action == BattleAction.SidekickAct && e.Stacks == 1).Take(2).Select(e => r.Fighters[e.Actor].Id).ToList();
        Assert.Equal(new[] { "alien_zib", "alien_blorp" }, acts);                                      // fastest first
        Assert.Contains(after, e => e.Action == BattleAction.Volley && e.Amount == 1 && e.Round == ab.Round); // then the mount fires
        Assert.Contains(after, e => e.Action == BattleAction.Shield && e.SkillId == "hero_beast_tamer_ability_7" && e.Amount == 450); // 3% × 5000 × 3 allies
    }

    [Fact]
    public void MirrorPalm_AtThreeStillness_CopiesHalfTheLargestStatus_Capped()
    {
        var enemies = new[] { Unit("e1", 900000, 1), Unit("e2", 900000, 1), Unit("e3", 900000, 1) };
        var r = Fight(Hero("hero_glacier_monk", 1).Append("probe_rad5").Append("probe_bigshield").Append("probe_combo"), rounds: 3, enemies: enemies);
        int w = Worm(r);
        Assert.Equal(new long[] { 1, 2, 3 }, r.Log.Where(e => e.Action == BattleAction.KitCounter && e.Actor == w).Take(3).Select(e => e.Amount));
        var palm = r.Log.Single(e => e.Action == BattleAction.Ability);
        Assert.Equal(3, palm.Round);
        var front = r.Fighters.First(f => f.Id == "e1").Index;
        long rad = r.Log.TakeWhile(e => e != palm).Last(e => e.Action == BattleAction.StatusApplied && e.Target == front && e.Status == StatusId.Poison).Stacks;
        var copies = r.Log.SkipWhile(e => e != palm).Where(e => e.Action == BattleAction.StatusApplied && e.SkillId == "hero_glacier_monk_ability").ToList();
        Assert.Equal(2, copies.Count);
        Assert.All(copies, e => Assert.Equal(Math.Min(20, rad / 2), e.Stacks));                       // half, max 20 (copied-stack cap)
    }

    [Fact]
    public void Patience_AKillBringsTheMountShotOneRoundSooner_OncePerRound()
    {
        var weak = new[] { Unit("w1", 1, 1, 1), Unit("w2", 1, 1, 1), Unit("w3", 1, 1, 1), Unit("tank", 900000, 1, 1) };
        var without = Fight(Mount("mount_magma_barge", 1), "mount_magma_barge", weak, rounds: 7);
        var with = Fight(Hero("hero_hourglass_hermit", 1).Concat(Mount("mount_magma_barge", 1)), "mount_magma_barge", weak, rounds: 7);
        Assert.Equal(new[] { 1, 4, 7 }, Volleys(without));
        Assert.Equal(1, Volleys(with)[0]);
        Assert.True(Volleys(with)[1] < 4);
        // two kills in one round advance a timer only once (the next shot is never 2 rounds early from one round)
        Assert.True(Volleys(with)[1] >= 3);
    }

    [Fact]
    public void Rewind_UndoesTheLastHits_HourOfNeedStrikesTheAttackersBack()
    {
        var foes = new[] { Unit("e1", 900000, 200), Unit("e2", 900000, 200) };
        var r = Fight(Hero("hero_hourglass_hermit", 1), enemies: foes, rounds: 4);
        var ab = r.Log.Single(e => e.Action == BattleAction.Ability);
        int w = Worm(r);
        var hitsBefore = r.Log.TakeWhile(e => e != ab)
            .Where(e => e.Target == w && e.Action is BattleAction.Basic or BattleAction.Rage or BattleAction.Combo or BattleAction.Counter && !e.Dodge).Select(e => e.Amount).ToList();
        var heal = r.Log.SkipWhile(e => e != ab).First(e => e.Action == BattleAction.Heal && e.SkillId == "hero_hourglass_hermit_ability");
        Assert.Equal(hitsBefore.TakeLast(2).Sum(), heal.Amount);
        var h10 = Fight(Hero("hero_hourglass_hermit", 10), enemies: foes, rounds: 4);
        var ab10 = h10.Log.Single(e => e.Action == BattleAction.Ability);
        Assert.Equal(3, h10.Log.SkipWhile(e => e != ab10).Count(e => e.Action == BattleAction.Skill && e.SkillId == "hero_hourglass_hermit_ability_10"));
    }

    // ---- shared caps -----------------------------------------------------------------------------------------------------

    /// <summary>One fatal-save per battle across every source: Last Constellation, Cryo Pod, Last Word, Borrowed Sand.</summary>
    [Fact]
    public void OneFatalSave_PerBattle_AcrossAllSources()
    {
        var foes = new[] { Unit("e1", 900000, 800, 200), Unit("e2", 900000, 800, 200) };
        var meta = Hero("hero_star_knight", 10).Concat(Mount("mount_frost_frigate", 5)).Append("mythic_last_word").ToList();
        var r = Fight(meta, "mount_frost_frigate", foes, rounds: 10, hp: 2000);
        Assert.Equal(1, r.Log.Count(e => e.Action == BattleAction.CheatDeath));
        Assert.Equal(BattleSide.Enemy, r.Winner);
        var h = Fight(Hero("hero_hourglass_hermit", 7).Append("mythic_last_word"), null, foes, rounds: 10, hp: 2000);
        Assert.Equal(1, h.Log.Count(e => e.Action == BattleAction.CheatDeath));
    }

    [Fact]
    public void LastConstellation_SavesAt1Hp_AndFiresStarfallAtDoubleDamage()
    {
        var foes = new[] { Unit("e1", 900000, 900, 200) };
        var r = Fight(Hero("hero_star_knight", 10), null, foes, rounds: 6, hp: 2000);
        var save = r.Log.First(e => e.Action == BattleAction.CheatDeath);
        Assert.Equal("hero_star_knight_c10", save.SkillId);
        var star = r.Log.SkipWhile(e => e != save).First(e => e.Action == BattleAction.Skill && e.SkillId == "hero_star_knight_ability_3");
        Assert.Equal(180, star.Amount); // 90% × 2
    }

    [Fact]
    public void ShortCircuit_ARageAttackFiresTheMountNow_OncePerRound()
    {
        var r = Fight(Hero("hero_sparkplug", 7).Concat(Mount("mount_magma_barge", 1)).Append("probe_fury"), "mount_magma_barge", rounds: 8,
            enemies: new[] { Unit("e1", 900000, 30, 200) });
        var extra = r.Log.Where(e => e.Action == BattleAction.Volley && e.Amount == 1).ToList();
        Assert.NotEmpty(extra);
        Assert.All(extra.GroupBy(e => e.Round), g => Assert.Single(g));
    }

    [Fact]
    public void Plunder_CritKillsPayTwoRunCoins_Max20PerBattle()
    {
        var weak = Enumerable.Range(0, 6).Select(i => Unit("w" + i, 1, 1, 1)).ToArray();
        var r = Fight(Hero("hero_void_pirate", 7).Append("probe_sharp"), null, weak, rounds: 10);
        // a crit kill: the hit that killed the enemy was a crit
        int critKills = 0;
        for (int i = 0; i < r.Log.Count; i++)
        {
            var d = r.Log[i];
            if (d.Action != BattleAction.Death || r.Fighters[d.Target].Side != BattleSide.Enemy) continue;
            var blow = r.Log.Take(i).Last(e => e.Target == d.Target && e.Amount > 0 && e.Action != BattleAction.StatusApplied);
            if (blow.Crit) critKills++;
        }
        Assert.True(critKills > 0);
        Assert.Equal(Math.Min(20, 2 * critKills), r.RunCoins);
        Assert.Equal(0, Fight(Hero("hero_void_pirate", 5).Append("probe_sharp"), null, weak, rounds: 10).RunCoins);
    }

    // ---- mounts: passives and actives ------------------------------------------------------------------------------------

    [Fact]
    public void Spotlight_MarksTheHealthiest_CritsOnIt_AndContagionMovesItsStatusesOnDeath()
    {
        var foes = new[] { Unit("small", 300, 1), Unit("big", 900000, 1) };
        var r = Fight(Mount("mount_scout_saucer", 5, 5).Append("probe_bleed3"), "mount_scout_saucer", foes, rounds: 6);
        var marks = r.Log.Where(e => e.Action == BattleAction.Mark).ToList();
        Assert.Equal("big", r.Fighters[marks[0].Target].Id);                                      // the healthiest
        // a mark that dies hands all (A5) its statuses on to the next mark
        var foes2 = new[] { Unit("a", 900000, 1), Unit("b", 2000, 1, 50) };
        var c = Fight(Mount("mount_scout_saucer", 5, 5).Append("probe_bleed3").Append("probe_sharp"), "mount_scout_saucer",
            new[] { Unit("a", 2500, 1), Unit("b", 1500, 1) }, rounds: 10);
        var death = c.Log.FirstOrDefault(e => e.Action == BattleAction.Death && c.Fighters[e.Target].Id == "a");
        if (death != null)
        {
            var remark = c.Log.SkipWhile(e => e != death).FirstOrDefault(e => e.Action == BattleAction.Mark);
            Assert.NotNull(remark);
            Assert.Contains(c.Log.SkipWhile(e => e != remark), e => e.Action == BattleAction.StatusApplied && e.SkillId == "mount_scout_saucer_passive_5");
        }
        Assert.NotNull(death);
    }

    [Fact]
    public void SiphonLine_HealsAShareOfStatusDamage_AndOverhealBecomesACappedShield()
    {
        var foes = new[] { Unit("e1", 900000, 300) };
        var r = Fight(Mount("mount_toxic_hauler", 1).Append("probe_rad5"), "mount_toxic_hauler", foes, rounds: 6);
        var heals = r.Log.Where(e => e.Action == BattleAction.Heal && e.SkillId == "mount_toxic_hauler_passive_1").ToList();
        Assert.NotEmpty(heals);
        var full = Fight(Mount("mount_toxic_hauler", 5).Append("probe_rad5"), "mount_toxic_hauler", new[] { Unit("e1", 900000, 0) }, rounds: 12);
        Assert.InRange(full.Fighters[Worm(full)].EndShield, 1, 500); // overheal shield, max 10% of 5000
    }

    [Fact]
    public void LanceSight_WormCritsChargeTheNextImpale_Capped()
    {
        var r = Fight(Mount("mount_star_lancer", 1).Append("probe_sharp"), "mount_star_lancer", rounds: 5, enemies: new[] { Unit("e1", 900000, 1) });
        int m = MountIx(r);
        var charges = r.Log.Where(e => e.Action == BattleAction.KitCounter && e.Actor == m).Select(e => e.Amount).ToList();
        Assert.Contains(20L, charges);
        Assert.All(charges, x => Assert.InRange(x, 20, 100));
        var shots = r.Log.Where(e => e.Actor == m && e.Action == BattleAction.Skill).Select(e => e.Amount).ToList();
        Assert.True(shots[1] > shots[0]); // the round-4 Impale carries the charge of rounds 1–3
    }

    [Fact]
    public void CoolantLoop_CleansesAndShields_CryoPodSkipsATurnAndHeals()
    {
        var c = new ContentDb();
        var foes = new[] { Unit("e1", 900000, 50) };
        foes[0].SkillIds.Add("enemy_rend");
        var r = Fight(Mount("mount_frost_frigate", 1), "mount_frost_frigate", foes, rounds: 6, c: c);
        int w = Worm(r);
        Assert.All(r.Log.Where(e => e.Action == BattleAction.Shield && e.SkillId == "mount_frost_frigate_passive_1"), e => Assert.Equal(100, e.Amount));
        Assert.Contains(r.Log, e => e.Action == BattleAction.StatusRemoved && e.Target == w && e.SkillId == "mount_frost_frigate_passive_1" && e.Amount == 1);
        var pod = Fight(Mount("mount_frost_frigate", 5), "mount_frost_frigate", new[] { Unit("e1", 900000, 2500, 200) }, rounds: 4, hp: 2000);
        var save = pod.Log.First(e => e.Action == BattleAction.CheatDeath);
        Assert.Equal("mount_frost_frigate_passive_3", save.SkillId);
        Assert.Contains(pod.Log.SkipWhile(e => e != save), e => e.Action == BattleAction.Heal && e.Amount == 500); // 25% of 2000
        Assert.Contains(pod.Log.SkipWhile(e => e != save), e => e.Action == BattleAction.SkipTurn && e.Actor == Worm(pod));
    }

    [Fact]
    public void IceWall_BurstsWhenItBreaks()
    {
        var r = Fight(Mount("mount_frost_frigate", 5), "mount_frost_frigate", new[] { Unit("e1", 900000, 900, 200) }, rounds: 3);
        Assert.Contains(r.Log, e => e.Action == BattleAction.Skill && e.SkillId == "mount_frost_frigate_active_3" && e.Amount == 100); // 100% ATK burst
    }

    [Fact]
    public void Slipstream_TheWormOpensRoundOne_ItsFirstAttackHitsHarder()
    {
        var foes = new[] { Unit("e1", 900000, 10, 300) };
        int First(BattleResult r, int round) => r.Log.First(e => e.Round == round && e.Action == BattleAction.Basic).Actor;
        var none = Fight(Array.Empty<string>(), "mount_rift_racer", foes, rounds: 2, speed: 1);
        var slip = Fight(new[] { "mount_rift_racer_passive_2" }, "mount_rift_racer", foes, rounds: 2, speed: 1); // the passive alone (a dash would be the first attack)
        Assert.NotEqual(Worm(none), First(none, 1));
        Assert.Equal(Worm(slip), First(slip, 1));
        Assert.NotEqual(Worm(slip), First(slip, 2));                                                  // round 1 only
        long Hit(BattleResult r, int n) => r.Log.Where(e => e.Actor == Worm(r) && e.Action == BattleAction.Basic).ElementAt(n).Amount;
        Assert.Equal(130, Hit(slip, 0));                                                            // first attack +30%
        Assert.Equal(100, Hit(slip, 1));
    }

    [Fact]
    public void RiftDash_AnExtraWormAttack_CreditedToTheMount()
    {
        var r = Fight(Mount("mount_rift_racer", 1), "mount_rift_racer", new[] { Unit("e1", 900000, 1) }, rounds: 1);
        int m = MountIx(r), w = Worm(r);
        Assert.Contains(r.Log, e => e.Actor == w && e.Action == BattleAction.Basic && e.Amount == 60);
        Assert.Equal(60, r.Fighters[m].DamageDealt);
    }

    [Fact]
    public void PaybackCoil_StoresDamageTaken_TheCannonFiresItAndEmptiesIt()
    {
        var r = Fight(Mount("mount_aegis_walker", 1), "mount_aegis_walker", new[] { Unit("e1", 900000, 500, 200) }, rounds: 4);
        int m = MountIx(r);
        var coil = r.Log.Where(e => e.Action == BattleAction.KitCounter && e.Actor == m).ToList();
        Assert.Contains(coil, e => e.Amount == 100);           // 20% of a 500 hit
        Assert.All(coil, e => Assert.InRange(e.Amount, 0, 300)); // max 300% of the mount's ATK (100)
        var shot = r.Log.Last(e => e.Actor == m && e.Action == BattleAction.Skill);
        Assert.Equal(4, shot.Round);
        Assert.Equal(60 + 300, shot.Amount);                    // 60% ATK + the full coil
        Assert.Equal(0, coil.Last().Amount);
    }

    [Fact]
    public void Wingman_OncePerTurn_LaunchBay_MaxTwoDrones_GuardsBreak()
    {
        var r = Fight(Mount("mount_hive_carrier", 1).Append("probe_combo"), "mount_hive_carrier", new[] { Unit("e1", 900000, 1) }, rounds: 8);
        var wing = r.Log.Where(e => e.SkillId == "mount_hive_carrier_passive_1" && e.Action == BattleAction.Skill).ToList();
        Assert.NotEmpty(wing);
        Assert.All(wing.GroupBy(e => e.Round), g => Assert.Single(g));     // the worm's turn: once
        Assert.Equal(2, r.Fighters.Count(f => f.Role == CombatRole.Summon)); // max 2
        var g5 = Fight(Mount("mount_hive_carrier", 5), "mount_hive_carrier", new[] { Unit("e1", 900000, 300, 200) }, rounds: 3);
        var guard = g5.Log.First(e => e.Action == BattleAction.Guarded);
        Assert.Equal(150, guard.Amount);                                     // −50% of a 300 hit
        Assert.False(g5.Fighters[guard.Actor].Alive);                        // then it breaks
    }

    [Fact]
    public void TimeDebt_DelaysTheFastestNonBoss_BossesLoseRage_SkipsGiveRage()
    {
        var foes = new[] { Unit("slow", 900000, 10, 10), Unit("fast", 900000, 10, 90) };
        var r = Fight(Mount("mount_chrono_skiff", 5), "mount_chrono_skiff", foes, rounds: 2);
        var delay = r.Log.First(e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Delay);
        Assert.Equal("fast", r.Fighters[delay.Target].Id);
        var skip = r.Log.First(e => e.Action == BattleAction.SkipTurn);
        Assert.Contains(r.Log.SkipWhile(e => e != skip), e => e.Action == BattleAction.GainRage && e.SkillId == "mount_chrono_skiff_passive_3" && e.Amount == 15);
        var boss = Fight(Mount("mount_chrono_skiff", 1), "mount_chrono_skiff", new[] { Unit("boss", 900000, 10, 90, CombatRole.Boss) }, rounds: 1);
        Assert.DoesNotContain(boss.Log, e => e.Action == BattleAction.StatusApplied && e.Status == StatusId.Delay); // bosses are never skipped
        Assert.Contains(boss.Log, e => e.Action == BattleAction.GainRage && e.Amount == -20 && boss.Fighters[e.Target].Role == CombatRole.Boss);
    }

    [Fact]
    public void EchoShot_WaitsForTheWormsFirstAttack_ThenRepeatsIt_FiveStarIsAnEcho()
    {
        var r = Fight(Mount("mount_chrono_skiff", 1), "mount_chrono_skiff", new[] { Unit("e1", 900000, 1, 10) }, rounds: 7);
        Assert.Equal(new[] { 2, 6 }, Volleys(r));                                           // held in round 1 (nothing to repeat), then every 4
        Assert.Contains(r.Log, e => e.Round == 2 && e.Actor == Worm(r) && e.Action == BattleAction.Basic && e.Amount == 60);
        var echo = Fight(Mount("mount_chrono_skiff", 5), "mount_chrono_skiff", new[] { Unit("e1", 900000, 1, 10) }, rounds: 3);
        Assert.Contains(echo.Log, e => e.Action == BattleAction.Echo && e.SkillId == "mount_chrono_skiff_active_3");
    }
}
