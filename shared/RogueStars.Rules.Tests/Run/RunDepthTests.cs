using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using Xunit.Abstractions;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Build depth (GDD §3.3 "Build depth"): 20–40 skills and perks per run from level-up drafts, a MYTHIC choice per beaten
/// miniboss, shop purchases with in-run coins and skill levels for duplicates; the Biomass surge only once Biomass has a use.
/// </summary>
public class RunDepthTests(ITestOutputHelper output)
{
    private static RunSummary PlayFarming(GameEngine e, string levelId, int seed)
    {
        var s = TestHelpers.NewPlayer(e, seed);
        s.Flags.Add(Features.DevUnlockAllFlag);
        var c = e.Content;
        var level = c.RunLevel(levelId)!;
        if (level.Number > 1) { s.Run.Levels[c.MainLevelId(level.Number - 1)] = new LevelProgress { Cleared = true }; s.Counters[Requirements.MainLevelCounter] = level.Number - 1; }
        s.Wallet.Add(Currencies.Stamina, 100);
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = levelId }).Ok);
        RunTestKit.AutoToEnd(e, ref s, 3000);
        return s.Run.LastSummary;
    }

    [Theory]
    [InlineData("ember_wastes_1")]
    [InlineData("ember_wastes_8")]
    [InlineData("glacier_rift_5")]
    public void SkillsPerRun_20To40_FromDraftsMythicsAndShops(string levelId)
    {
        // a worm that survives everything: pure build pacing (the simulator's guard covers real worms)
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var runs = Enumerable.Range(1, 25).Select(seed => PlayFarming(e, levelId, seed)).ToList();
        double gains = runs.Average(r => r.Gains.Count);
        double drafts = runs.Average(r => r.Gains.Count(g => g.Source == "draft"));
        double mythic = runs.Average(r => r.Gains.Count(g => g.Source == "mythic"));
        double shop = runs.Average(r => r.ShopBuys);
        output.WriteLine($"{levelId}: gains {gains:0.0} (min {runs.Min(r => r.Gains.Count)}, max {runs.Max(r => r.Gains.Count)}), drafts {drafts:0.0}, mythic {mythic:0.0}, minibosses {runs.Average(r => r.Minibosses):0.0}, shop buys {shop:0.0}");
        Assert.InRange(gains, 20, 40);
        Assert.InRange(drafts, 7, 11);
        // 9 (was 10): playtest 0.1.325's 3 skill offers per shop let the bot buy the pricier Legendary more often with the same
        // coins (ember_wastes_1: 10.3 → 9.9 buys); coin income is unchanged
        Assert.InRange(shop, 9, 22);
        Assert.All(runs, r => Assert.Equal(r.Minibosses, r.Gains.Count(g => g.Source == "mythic")));  // one mythic choice per miniboss
        Assert.All(runs, r => Assert.True(r.Minibosses <= 2, "two minibosses per run (playtest 0.1.308)"));
        Assert.True(runs.Average(r => r.Minibosses) >= 1.8, "both lairs stand on the path and stop the worm");
    }

    [Fact]
    public void BeatenMiniboss_OpensAMythicChoice_OfThreeMythicSkills()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        // playtest 0.1.308: the miniboss stands on the path (its lair stops the worm), no clock ambush
        RunTestKit.LineBoard(run, new[] { "miniboss" }.Concat(Enumerable.Repeat("empty", 30)).ToArray());
        run.Awakening = 24.9;
        var r = RunTestKit.Exec(e, ref s, new RunRoll());
        Assert.True(r.Ok);
        Assert.Equal(1, s.Run.Active!.Position);                                      // the lair stopped the worm
        Assert.Contains(r.Events, ev => ev.Type == "run_battle" && ev.Args[0] == "miniboss");
        Assert.DoesNotContain(r.Events, ev => ev.Type == "run_encounter");
        run = s.Run.Active!;
        Assert.Equal(RunPhase.Draft, run.Phase);
        Assert.True(run.MythicDraft);
        Assert.Equal(e.Content.RunTuning.MythicChoices, run.DraftOffer.Count);
        Assert.All(run.DraftOffer, id => Assert.Equal(Rarity.Mythic, e.Content.Skill(id).Rarity));
        Assert.True(RunSystem.RequiresPlayer(e.Content, run));                       // a player decision, shown as MYTHIC
        Assert.True(RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = run.DraftOffer[1] }).Ok);
        Assert.Equal("mythic", s.Run.Active!.Gains.Last().Source);
        Assert.False(s.Run.Active.MythicDraft);
        Assert.Equal(1, s.Run.Active.MinibossesDefeated);
    }

    [Fact]
    public void MythicPool_IsDeep_EveryFamilyHasMythics()
    {
        var c = ContentDb.Default;
        var mythics = c.Skills.Values.Where(x => x.Rarity == Rarity.Mythic && x.Draftable && x.Source == SkillSource.Run).ToList();
        Assert.True(mythics.Count >= 29, "mythics: " + mythics.Count);
        foreach (var f in Enum.GetValues<SkillFamily>().Where(f => f != SkillFamily.Crit)) // Crit is no synergy: its Mythics are generic
            Assert.True(mythics.Count(m => m.Family == f) >= 2, f + " has at least two mythics");
        Assert.All(mythics, m => Assert.False(string.IsNullOrWhiteSpace(SkillText.Full(m)), m.Id));
        // ungated mythics: a run can take one from its first miniboss
        var run = new ActiveRun();
        // (skill redesign) Mythics that need a source (aliens, Arc, Freeze, Shock, consumed Bleed) wait until the run owns it
        Assert.True(mythics.Count(m => RunDraft.IsMythicEligible(c, run, m)) >= 20);
    }

    [Fact]
    public void Duplicates_LevelUp_StatsGrow_UpToTheMaxLevel()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        var c = e.Content;
        double atk0 = BattleSimulator.FinalStats(RunSystem.WormSpec(c, run), c).Value(StatId.Atk);
        double hp0 = BattleSimulator.FinalStats(RunSystem.WormSpec(c, run), c).Value(StatId.Hp);
        double atk1 = 0;
        var power = c.Skill("gen_power")!;
        Assert.Equal(Rarity.Common, power.Rarity);
        Assert.Equal(3, c.RunTuning.MaxSkillLevelOf(power.Rarity));                       // Common: two upgrades
        for (int lv = 1; lv <= 3; lv++)
        {
            run = s.Run.Active!;
            run.Phase = RunPhase.Draft; run.MythicDraft = false;
            run.DraftOffer.Clear(); run.DraftOffer.Add("gen_power"); run.PendingDrafts = 1;
            Assert.True(RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = "gen_power" }).Ok);
            Assert.Equal(lv, RunDraft.Level(s.Run.Active!, "gen_power"));
            if (lv == 1) atk1 = BattleSimulator.FinalStats(RunSystem.WormSpec(c, s.Run.Active!), c).Value(StatId.Atk);
        }
        run = s.Run.Active!;
        var block = BattleSimulator.FinalStats(RunSystem.WormSpec(c, run), c);
        // the level ladder: +15% → +25% → +40% ATK, and nothing else grows
        Assert.Equal(15, power.Stats[0].Percent, 6);
        Assert.Equal(25, SkillLeveling.At(c, power, 2).Stats[0].Percent, 6);
        Assert.Equal(40, SkillLeveling.At(c, power, 3).Stats[0].Percent, 6);
        Assert.True(block.Value(StatId.Atk) > atk1 && atk1 > atk0, $"{atk0} → {atk1} → {block.Value(StatId.Atk)}");
        Assert.Equal(hp0, block.Value(StatId.Hp), 6); // no generic +HP per level any more
        Assert.False(RunDraft.IsEligible(c, run, power));                                 // max level: no longer offered
        Assert.Equal(2, run.Gains.Count(g => g.SkillId == "gen_power" && g.Level > 1));
    }

    [Fact]
    public void DraftPool_NeverRunsDry_In60Picks()
    {
        var e = RunTestKit.Engine();
        var s = RunTestKit.StartRun(e);
        var c = e.Content;
        for (int i = 0; i < 60; i++)
        {
            var run = s.Run.Active!;
            var offer = RunDraft.Generate(c, run, run.Rng);
            Assert.Equal(c.RunTuning.DraftChoices, offer.Count);
            run.Phase = RunPhase.Draft; run.DraftOffer.Clear(); run.DraftOffer.AddRange(offer); run.PendingDrafts = 1;
            Assert.True(RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = RunDraft.BestPick(c, run) }).Ok);
        }
        Assert.Equal(61, s.Run.Active!.Gains.Count); // the signature + 60 picks (Fire, Radiation, Bleed and generic skills only)
        Assert.True(s.Run.Active.SkillLevels.Count > 0, "duplicates levelled up");
    }

    [Fact]
    public void CoinIncome_BuysTenToTwentyPerks_AtShopPrices()
    {
        var c = ContentDb.Default;
        var table = c.RunShopTables["shop_standard"];
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var runs = Enumerable.Range(1, 20).Select(seed => PlayFarming(e, "ember_wastes_3", seed)).ToList();
        double buys = runs.Average(r => r.ShopBuys);
        // the cheapest and the typical offer: a run's coins pay for 10–20 of them
        double typical = (2 * table.SkillPrices[Rarity.Common] + table.SkillPrices[Rarity.Legendary] + table.StatPrice) / 4.0;
        output.WriteLine($"shop buys {buys:0.0}; typical offer {typical:0} coins");
        Assert.InRange(buys, 9, 22); // 9 (was 10): 3 skill offers per shop (playtest 0.1.325) buy pricier skills with the same coins (10.2 → 9.9)
        Assert.Equal(1, table.StatPerkOffers);
        Assert.Equal(3, table.SkillOffers); // 3 skills + the support offers (playtest 0.1.325)
    }

    [Fact]
    public void BiomassSurge_OnlyOnceBiomassHasAUse()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = TestHelpers.NewLockedPlayer(e, 5);
        var c = e.Content;
        Assert.False(RewardGate.CurrencyHasUse(s, c, Currencies.Biomass, TestHelpers.T0));
        Assert.False(RunSystem.BiomassSurgeActive(s, c, TestHelpers.T0));
        Assert.Equal(0, RunSystem.BiomassBonusRunsLeft(s, c, TestHelpers.T0));      // no counter shown
        s.Wallet.Add(Currencies.Stamina, 100);
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1" }).Ok);
        RunTestKit.AutoToEnd(e, ref s, 3000);
        Assert.True(s.Run.LastSummary.Won);
        Assert.False(s.Run.LastSummary.BiomassBonus);                                    // no surge consumed
        Assert.Equal(0, s.Run.BiomassBonusRuns);
        // once Biomass has a use the surge is there with all its runs
        s.Flags.Add(Features.DevUnlockAllFlag);
        Assert.True(RunSystem.BiomassSurgeActive(s, c, TestHelpers.T0));
        Assert.Equal(c.RunTuning.DailyBiomassBonusRuns, RunSystem.BiomassBonusRunsLeft(s, c, TestHelpers.T0));
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1" }).Ok);
        RunTestKit.AutoToEnd(e, ref s, 3000);
        Assert.True(s.Run.LastSummary.BiomassBonus);
    }
}
