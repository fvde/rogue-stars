using System.Text.Json;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Signatures pass (owner design): one simple signature per synergy picked at run start, awakening at 3 / 6 synergy skills,
/// Laboratory packages for the non-starter synergies, and the key mechanics Heat / Supernova (Fire), Radiation (unlimited),
/// Bleed (capped, consumed) and Chill (Ice, cools Heat).
/// </summary>
public class SignatureTests
{
    private static readonly ContentDb C = ContentDb.Default;
    private static readonly string[] Starters = { "sig_bleed", "sig_fire", "sig_radiation" };

    // ---- run start: the pick ------------------------------------------------------------------------------------------

    [Fact]
    public void NewPlayer_IsOfferedTheThreeStarterSignatures_StableUntilTheRunStarts()
    {
        var e = RunTestKit.Engine();
        var s = TestHelpers.NewPlayer(e, 3);
        var offer = RunSystem.SignatureOffer(e, s, TestHelpers.T0);
        Assert.Equal(Starters, offer.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(offer, RunSystem.SignatureOffer(e, s, TestHelpers.T0 + 3_600_000)); // the same offer until a run starts
        string pick = offer[2];
        var r = RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1", SignatureId = pick });
        Assert.True(r.Ok, r.Error);
        var run = s.Run.Active!;
        Assert.Equal(pick, run.SignatureId);
        Assert.Contains(pick, run.Skills);
        Assert.Contains(run.Gains, g => g.Source == "signature" && g.SkillId == pick);
        Assert.Equal(1, s.Counter("run_signature_" + pick));
    }

    [Fact]
    public void AutoStart_TakesTheFirstOffered_AndAnUnofferedSignatureIsRefused()
    {
        var e = RunTestKit.Engine();
        var s = TestHelpers.NewPlayer(e, 5);
        var offer = RunSystem.SignatureOffer(e, s, TestHelpers.T0);
        var bad = TestHelpers.Clone(s);
        Assert.False(RunTestKit.Exec(e, ref bad, new RunStart { LevelId = "ember_wastes_1", SignatureId = "sig_ice" }).Ok); // Ice is a package
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1" }).Ok);
        Assert.Equal(offer[0], s.Run.Active!.SignatureId);
    }

    [Fact]
    public void TheOffer_IsShuffledPerRun_AndCoversEveryStarterOverRuns()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var firsts = new HashSet<string>();
        var s = TestHelpers.NewPlayer(e, 9);
        for (int i = 0; i < 12; i++)
        {
            Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1" }).Ok);
            firsts.Add(s.Run.Active!.SignatureId!);
            Assert.True(RunTestKit.Exec(e, ref s, new RunAbandon()).Ok);
            s.Wallet.Add(Currencies.Stamina, 100);
        }
        Assert.Equal(Starters, firsts.OrderBy(x => x, StringComparer.Ordinal));
    }

    // ---- packages ---------------------------------------------------------------------------------------------------

    [Fact]
    public void APackage_UnlocksItsSynergy_ForTheOfferAndThePool()
    {
        var e = RunTestKit.Engine();
        var s = TestHelpers.NewPlayer(e, 3);
        Assert.Equal(C.RunTuning.StarterSynergies, e.BuildLoadout(s, TestHelpers.T0).UnlockedFamilies);
        s.Lab.Entries["pkg_combo"] = new LabEntry { Level = 1 };
        var lo = e.BuildLoadout(s, TestHelpers.T0);
        Assert.Contains(SkillFamily.Combo, lo.UnlockedFamilies);
        Assert.DoesNotContain("sig_combo", lo.DraftPoolSkillIds); // a package is no draft skill
        // 4 synergies: 3 of them are offered, over a few runs every one of them
        var seen = new HashSet<string>();
        for (int i = 0; i < 10; i++)
        {
            s.Run.RunsStarted = i;
            var offer = RunSystem.SignatureOffer(e, s, TestHelpers.T0);
            Assert.Equal(3, offer.Count);
            seen.UnionWith(offer);
        }
        Assert.Contains("sig_combo", seen);
        Assert.Equal(4, seen.Count);
        Assert.Equal("Combo package: Flurry + 8 skills.", C.LabNode("pkg_combo")!.Description);
    }

    [Fact]
    public void Packages_AreAChain_AndTheAlienPackageNeedsAliens()
    {
        var e = RunTestKit.Engine();
        var s = TestHelpers.NewPlayer(e, 3);
        Assert.Null(LaboratorySystem.BlockReason(s, C, C.LabNode("pkg_combo")!));
        Assert.Equal("prerequisites missing", LaboratorySystem.BlockReason(s, C, C.LabNode("pkg_ice")!));
        Assert.Equal("prerequisites missing", LaboratorySystem.BlockReason(s, C, C.LabNode("res_frost_nova")!)); // Ice lab skill needs the package
        foreach (var id in new[] { "pkg_combo", "pkg_ice", "pkg_lightning", "pkg_counter" }) s.Lab.Entries[id] = new LabEntry { Level = 1 };
        s.Lab.Entries["lab_alien_power_1"] = new LabEntry { Level = 2 };
        s.Flags.Remove(Features.DevUnlockAllFlag);
        Assert.Contains("locked", LaboratorySystem.BlockReason(s, C, C.LabNode("pkg_alien")!));
        s.Onboarding.Unlocked[Features.Aliens] = TestHelpers.T0;
        Assert.Null(LaboratorySystem.BlockReason(s, C, C.LabNode("pkg_alien")!));
        Assert.Equal("research maxed", LaboratorySystem.BlockReason(s, C, C.LabNode("pkg_combo")!));
    }

    // ---- awakening and pool rules -------------------------------------------------------------------------------------

    private static GameState RunWith(GameEngine e, string signature)
    {
        var s = TestHelpers.NewPlayer(e, 3);
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1", SignatureId = signature }).Ok);
        return s;
    }

    private static void Draft(GameEngine e, ref GameState s, string id)
    {
        var run = s.Run.Active!;
        run.Phase = RunPhase.Draft; run.MythicDraft = false; run.DraftOffer.Clear(); run.DraftOffer.Add(id); run.PendingDrafts = 1;
        var r = RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = id });
        Assert.True(r.Ok, r.Error);
    }

    [Fact]
    public void TheSignature_AwakensAtThreeAndSixSynergySkills_AndOnlyGrowsItsNumber()
    {
        var e = RunTestKit.Engine();
        var s = RunWith(e, "sig_fire");
        int Lv() => RunDraft.Level(s.Run.Active!, "sig_fire");
        Assert.Equal(1, Lv());
        Assert.Equal(3, RunSystem.NextAwakeningAt(C, s.Run.Active!));
        Draft(e, ref s, "fire_ember"); Draft(e, ref s, "fire_kindle");
        Draft(e, ref s, "fire_ember"); // a level-up is no new skill
        Assert.Equal(1, Lv());
        Draft(e, ref s, "fire_flashpoint");
        Assert.Equal(2, Lv());
        Assert.Equal(1, s.Counter("run_signature_awakened"));
        Draft(e, ref s, "fire_controlled");
        var run = s.Run.Active!;
        run.Skills.Add("fire_heat_siphon"); // 5 Fire skills (a Mythic from a miniboss choice counts too)
        Draft(e, ref s, "gen_power"); // any gain re-reads the awakening
        Assert.Equal(2, Lv());
        s.Run.Active!.Skills.Add("mythic_thermal_shock");
        Draft(e, ref s, "gen_guard");
        Assert.Equal(3, Lv());
        Assert.Equal(0, RunSystem.NextAwakeningAt(C, s.Run.Active!));
        // Ignite: 1 → 2 → 3 Heat, nothing else
        var sig = C.Skill("sig_fire")!;
        Assert.Equal(new[] { 1, 2, 3 }, Enumerable.Range(1, 3).Select(l => SkillLeveling.At(C, sig, l).Triggers.Single().Stacks));
        Assert.Equal("Ignite (Awakened 2)", SkillText.Title(C, sig, 3));
        Assert.Equal(new[] { 30.0, 45, 60 }, Enumerable.Range(1, 3).Select(l => SkillLeveling.At(C, C.Skill("sig_combo")!, l).Triggers.Single().Chance));
        Assert.Equal(new[] { 1, 2, 3 }, Enumerable.Range(1, 3).Select(l => SkillLeveling.At(C, C.Skill("sig_lightning")!, l).Triggers.Single().Stacks));
        Assert.Equal(new[] { 1, 2, 3 }, Enumerable.Range(1, 3).Select(l => SkillLeveling.At(C, C.Skill("sig_alien")!, l).Triggers.Single().Stacks));
    }

    [Fact]
    public void SignatureTexts_AreShort_AndReadTheOwnersWay()
    {
        string T(string id) => SkillText.Full(C.Skill(id));
        Assert.Equal("On hit: apply Heat to the enemy.", T("sig_fire"));
        Assert.Equal("On hit: apply Radiation to the enemy.", T("sig_radiation"));
        Assert.Equal("On hit: apply Bleed to the enemy.", T("sig_bleed"));
        Assert.Equal("On hit: apply Chill to the enemy.", T("sig_ice"));
        Assert.Equal("On hit: Arc to 1 other enemy for 50% ATK damage.", T("sig_lightning"));
        Assert.Equal("Each round: one of your sidekicks takes an extra action.", T("sig_alien"));
        Assert.Equal("When hit: 30% chance to counterattack.", T("sig_counter"));
        Assert.Equal("On hit: 30% chance to strike again.", T("sig_combo"));
        Assert.Equal(new[] { "Awakened (3 Fire skills): On hit: apply Heat ×2 to the enemy.", "Awakened (6 Fire skills): On hit: apply Heat ×3 to the enemy." },
            SkillText.Awakenings(C, C.Skill("sig_fire")));
        foreach (var s in C.Skills.Values.Where(x => x.Signature)) Assert.True(SkillText.Full(s).Length <= 60, s.Id);
    }

    [Fact]
    public void Signatures_AreNeverDraftedBoughtLevelledOrLost_AndWeightTheirSynergy()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunWith(e, "sig_bleed");
        var run = s.Run.Active!;
        var sig = C.Skill("sig_bleed")!;
        Assert.False(RunDraft.IsEligible(C, run, sig));
        Assert.False(RunDraft.IsMythicEligible(C, run, sig));
        Assert.False(RunDraft.CanLevelUp(C, run, sig));
        Assert.DoesNotContain(RunDraft.Eligible(C, run), x => x.Signature);
        // +40% offer weight for the signature's synergy
        Assert.Equal(1.4, RunDraft.OfferWeight(C, run, C.Skill("bleed_serrated")!, 1), 6);
        Assert.Equal(1, RunDraft.OfferWeight(C, run, C.Skill("fire_ember")!, 1), 6);
        // the synergy slot offers the signature's synergy from the first draft
        int bleed = 0;
        for (ulong seed = 1; seed <= 40; seed++) if (RunDraft.Generate(C, run, new Rng(seed)).Any(id => C.Skill(id)!.Family == SkillFamily.Bleed)) bleed++;
        Assert.Equal(40, bleed);
        // a "lose a random skill" outcome never takes it
        for (int i = 0; i < 20; i++)
        {
            run.Skills.Add("gen_power");
            var ctx = RunTestKit.Ctx(e, s);
            typeof(RunSystem).GetMethod("ApplyEffects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, new object[] { ctx, run, new List<RunEffect> { new RunEffect(RunEffectKind.LoseRandomSkill, 1) }, 0, null });
            Assert.Contains("sig_bleed", run.Skills);
        }
        RunTestKit.AutoToEnd(e, ref s);
        Assert.True(s.Run.LastSummary!.Won);
    }

    [Fact]
    public void MythicsComeOnlyFromMinibossChoices_FromUnlockedSynergies()
    {
        var e = RunTestKit.Engine();
        var s = RunWith(e, "sig_fire");
        var run = s.Run.Active!;
        Assert.DoesNotContain(RunDraft.Eligible(C, run), x => x.Rarity == Rarity.Mythic);
        var pool = C.Skills.Values.Where(x => RunDraft.IsMythicEligible(C, run, x)).ToList();
        Assert.All(pool, x => Assert.True(x.Family is SkillFamily.None or SkillFamily.Fire or SkillFamily.Poison or SkillFamily.Bleed, x.Id));
        Assert.True(pool.Count >= 11, pool.Count.ToString()); // Mythics needing a source the run lacks (consumed Bleed …) wait for it
    }

    // ---- the mechanics ------------------------------------------------------------------------------------------------

    private static ContentDb Kit(params SkillDef[] skills)
    {
        var c = new ContentDb();
        foreach (var s in skills) c.AddSkill(s);
        return c;
    }

    private static SkillDef On(string id, TriggerKind when, EffectKind fx, StatusId st = StatusId.None, int stacks = 1, double value = 0,
        TargetKind target = TargetKind.Enemy, string? param = null, SkillFamily family = SkillFamily.None, params StatMod[] stats)
    {
        var s = RunTestKit.Skill(id, new SkillTrigger { When = when, Effect = fx, Status = st, Stacks = stacks, Value = value, Target = target, Param = param });
        s.Family = family;
        s.Stats.AddRange(stats);
        return s;
    }

    /// <summary>Worm (and two aliens) against three enemies.</summary>
    private static BattleResult Fight(ContentDb c, IEnumerable<string> skills, int seed = 1, int rounds = 0, double enemyHp = 1, bool aliens = false, Dictionary<string, int>? levels = null, int enemies = 3)
    {
        var lo = new CombatLoadout { DisplayName = "Worm" };
        lo.WormStats.Add(new StatMod(StatId.Hp, 4000)).Add(new StatMod(StatId.Atk, 100)).Add(new StatMod(StatId.Def, 10)).Add(new StatMod(StatId.Speed, 200));
        if (aliens)
            for (int i = 0; i < 3; i++)
            {
                var a = new CombatAlien { AlienTypeId = "a" + i, Name = "A" + i };
                a.Stats.Add(new StatMod(StatId.Hp, 300)).Add(new StatMod(StatId.Atk, 40)).Add(new StatMod(StatId.Speed, 40));
                a.SkillIds.Add("ask_voidmaw_1"); // a sidekick acts with its ability (sidekick pass)
                lo.Aliens.Add(a);
            }
        var setup = new BattleSetup { RoundCap = rounds };
        setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, skills.ToList(), null, levels));
        for (int i = 0; i < enemies; i++) setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "magma_slug", 1, 0, (enemyHp - 1) * 100)); // only HP scales
        return BattleSimulator.Simulate(setup, c, new Rng((ulong)seed));
    }

    [Fact]
    public void Heat_GoesSupernova_HittingEveryEnemy_AndRecoilingOnTheWorm()
    {
        var c = Kit(On("heat5", TriggerKind.OnHit, EffectKind.ApplyStatus, StatusId.Heat, 5));
        var r = Fight(c, new[] { "heat5" }, rounds: 3, enemyHp: 20);
        int mark = r.Log.FindIndex(x => x.Action == BattleAction.Supernova && x.Amount == 0);
        Assert.True(mark >= 0, "no Supernova");
        Assert.Equal(10, r.Log[mark].Stacks);
        int boom = r.Log[mark].Target;
        var hits = r.Log.Skip(mark + 1).TakeWhile(x => x.Action == BattleAction.Supernova).ToList();
        var worm = r.Fighters.Single(f => f.Role == CombatRole.Worm).Index;
        // every enemy (the exploding one included) and then the worm's recoil: 12% of its max HP
        Assert.Equal(new HashSet<int>(r.Fighters.Where(f => f.Side == BattleSide.Enemy).Select(f => f.Index).Append(worm)), hits.Select(h => h.Target).ToHashSet());
        Assert.Equal(480, hits.Single(h => h.Target == worm).Amount);
        Assert.True(hits.Where(h => h.Target != worm).All(h => h.Amount > 100));
        // the Heat is gone (a new stack count starts after the burst)
        var after = r.Log.Skip(mark).FirstOrDefault(x => x.Action == BattleAction.StatusApplied && x.Status == StatusId.Heat && x.Target == boom);
        Assert.True(after == null || after.Stacks <= 5);
    }

    [Fact]
    public void SupernovaStats_RaiseTheBurst_AndGuardTheWorm()
    {
        var heat = On("heat5", TriggerKind.OnHit, EffectKind.ApplyStatus, StatusId.Heat, 5);
        var big = On("big", TriggerKind.Passive, EffectKind.BuffStat, stats: new[] { new StatMod(StatId.SupernovaDamage, 50), new StatMod(StatId.SupernovaGuard, 100) });
        var c = Kit(heat, big);
        long Burst(BattleResult r) => r.Log.Where(x => x.Action == BattleAction.Supernova && r.Fighters[x.Target].Side == BattleSide.Enemy).Sum(x => x.Amount);
        long Recoil(BattleResult r) => r.Log.Where(x => x.Action == BattleAction.Supernova && r.Fighters[x.Target].Role == CombatRole.Worm).Sum(x => x.Amount);
        var plain = Fight(c, new[] { "heat5" }, rounds: 2, enemyHp: 30);
        var boosted = Fight(c, new[] { "heat5", "big" }, rounds: 2, enemyHp: 30);
        Assert.True(Burst(boosted) > Burst(plain) * 1.4, Burst(boosted) + " vs " + Burst(plain));
        Assert.True(Recoil(plain) > 0);
        Assert.Equal(0, Recoil(boosted));
    }

    [Fact]
    public void Cooling_RemovesHeat_BeforeItBlows()
    {
        var heat = On("heat5", TriggerKind.OnHit, EffectKind.ApplyStatus, StatusId.Heat, 5);
        var cool = On("cool", TriggerKind.OnHit, EffectKind.RemoveStatus, StatusId.Heat, 0); // after every heating hit: all Heat off
        var chill = On("chill", TriggerKind.OnHit, EffectKind.ApplyStatus, StatusId.Chill, 5);
        var c = Kit(heat, cool, chill);
        Assert.Contains(Fight(c, new[] { "heat5" }, rounds: 4, enemyHp: 30).Log, x => x.Action == BattleAction.Supernova);
        var cooled = Fight(c, new[] { "heat5", "cool" }, rounds: 4, enemyHp: 30);
        Assert.DoesNotContain(cooled.Log, x => x.Action == BattleAction.Supernova);
        Assert.Contains(cooled.Log, x => x.Action == BattleAction.StatusRemoved && x.Status == StatusId.Heat && x.Stacks == 0);
        // Chill cools as it lands: each Chill stack removes one Heat (5 Heat, then 5 Chill → 0)
        var chilled = Fight(c, new[] { "heat5", "chill" }, rounds: 4, enemyHp: 30);
        Assert.DoesNotContain(chilled.Log, x => x.Action == BattleAction.Supernova);
        Assert.Contains(chilled.Log, x => x.Action == BattleAction.StatusRemoved && x.Status == StatusId.Heat);
    }

    [Fact]
    public void Chill_WeakensTheEnemy_PerStack()
    {
        var c = Kit(On("chill", TriggerKind.OnHit, EffectKind.ApplyStatus, StatusId.Chill, 10));
        var without = Fight(c, new string[0], rounds: 6, enemyHp: 50);
        var with = Fight(c, new[] { "chill" }, rounds: 6, enemyHp: 50);
        Assert.True(with.PlayerDamageTaken < without.PlayerDamageTaken * 0.95, with.PlayerDamageTaken + " vs " + without.PlayerDamageTaken);
    }

    [Fact]
    public void Radiation_StacksWithoutLimit_AndNeverFades()
    {
        var c = Kit(On("rad", TriggerKind.OnHit, EffectKind.ApplyStatus, StatusId.Poison, 4));
        var r = Fight(c, new[] { "rad" }, rounds: 12, enemyHp: 400);
        Assert.True(r.Log.Where(x => x.Action == BattleAction.StatusApplied && x.Status == StatusId.Poison).Max(x => x.Stacks) > 30);
        Assert.DoesNotContain(r.Log, x => x.Action == BattleAction.StatusExpired && x.Status == StatusId.Poison);
        var ticks = r.Log.Where(x => x.Action == BattleAction.StatusTick && x.Status == StatusId.Poison).Select(x => x.Amount).ToList();
        Assert.True(ticks.Last() > ticks.First() * 5); // the damage keeps growing
    }

    [Fact]
    public void Bleed_CapsAtTen_BleedsEveryRound_AndIsConsumedForBurstAndLifesteal()
    {
        var bleed = On("bleed", TriggerKind.OnHit, EffectKind.ApplyStatus, StatusId.Bleed, 4);
        var drink = On("drink", TriggerKind.OnRoundEnd, EffectKind.ConsumeStatus, StatusId.Bleed, value: 25, param: "heal:50");
        var c = Kit(bleed, drink);
        var r = Fight(c, new[] { "bleed" }, rounds: 6, enemyHp: 400);
        Assert.Equal(10, r.Log.Where(x => x.Action == BattleAction.StatusApplied && x.Status == StatusId.Bleed).Max(x => x.Stacks));
        Assert.Contains(r.Log, x => x.Action == BattleAction.StatusTick && x.Status == StatusId.Bleed);
        Assert.DoesNotContain(r.Log, x => x.Action == BattleAction.StatusExpired && x.Status == StatusId.Bleed);
        var d = Fight(c, new[] { "bleed", "drink" }, rounds: 6, enemyHp: 400);
        var removed = d.Log.First(x => x.Action == BattleAction.StatusRemoved && x.Status == StatusId.Bleed);
        Assert.Equal(0, removed.Stacks);
        int i = d.Log.IndexOf(removed);
        Assert.Equal(BattleAction.Skill, d.Log[i + 1].Action);
        Assert.True(d.Log[i + 1].Amount > 0);
        Assert.Contains(d.Log, x => x.Action == BattleAction.Heal && x.SkillId == "drink");
    }

    [Fact]
    public void Arc_Command_Riposte_Flurry_DoTheirOneThing()
    {
        var c = C;
        BattleResult F(string id, bool aliens = false) => Fight(c, new[] { id }, rounds: 12, enemyHp: 30, aliens: aliens);
        var arc = F("sig_lightning");
        Assert.True(arc.Log.Any(x => x.SkillId == "sig_lightning" && x.Action == BattleAction.Skill && x.Amount > 0), "arc");
        Assert.All(arc.Log.Where(x => x.SkillId == "sig_lightning" && x.Action == BattleAction.Skill), x => Assert.Equal(BattleSide.Enemy, arc.Fighters[x.Target].Side));
        int alienHits(BattleResult r) => r.Log.Count(x => x.Action == BattleAction.SidekickAct && x.Actor >= 0 && r.Fighters[x.Actor].Role == CombatRole.Alien);
        Assert.True(alienHits(F("sig_alien", true)) > alienHits(Fight(c, new string[0], rounds: 12, enemyHp: 30, aliens: true)));
        Assert.True(F("sig_counter").Log.Any(x => x.Action == BattleAction.Counter), "riposte");
        Assert.True(F("sig_combo").Log.Any(x => x.Action == BattleAction.Combo), "flurry");
    }

    /// <summary>Audit extension: every signature changes a seeded battle, and so does each awakening level.</summary>
    [Fact]
    public void EverySignature_AndEveryAwakening_ChangesASeededBattle()
    {
        var c = C;
        var failures = new List<string>();
        foreach (var s in c.Skills.Values.Where(x => x.Signature).OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            for (int lv = 1; lv <= 3; lv++)
            {
                bool moved = false;
                for (int seed = 1; seed <= 6 && !moved; seed++)
                {
                    string A(int l) => SkillTuningTests.Signature(l == 0
                        ? Fight(c, new string[0], seed, 0, 6, true, null, 4)
                        : Fight(c, new[] { s.Id }, seed, 0, 6, true, new Dictionary<string, int> { [s.Id] = l }, 4)); // 4 enemies, 3 aliens: Arc's and Command's third
                    moved = A(lv) != A(lv - 1);
                }
                if (!moved) failures.Add(s.Id + " Lv" + lv);
            }
        }
        Assert.True(failures.Count == 0, string.Join(", ", failures));
    }

    // ---- old saves ----------------------------------------------------------------------------------------------------

    [Fact]
    public void ASavedRun_FromBeforeSignatures_WithBurnLogsAndRemovedIds_Loads()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        // an old run: no signature, no synergy lock, a Burn status in its last battle log, removed / renamed skills
        run.Skills.Remove(run.SignatureId!);
        run.SignatureId = null;
        run.Loadout.UnlockedFamilies.Clear();
        run.Skills.Add("crit_precision");
        run.Skills.Add("ice_shard");
        run.LastBattle = new BattleResult();
        run.LastBattle.Log.Add(new BattleLogEntry { Action = BattleAction.StatusApplied, Status = StatusId.Burn, Stacks = 3 });
        string json = JsonSerializer.Serialize(s, TestHelpers.Json);
        var loaded = JsonSerializer.Deserialize<GameState>(json, TestHelpers.Json)!;
        Assert.True(RunDraft.FamilyUnlocked(loaded.Run.Active!, SkillFamily.Ice)); // old run: every synergy, as before
        Assert.Equal("Heat", SkillText.StatusName(StatusId.Burn));
        RunTestKit.AutoToEnd(e, ref loaded);
        Assert.True(loaded.Run.LastSummary!.Won);
    }
}
