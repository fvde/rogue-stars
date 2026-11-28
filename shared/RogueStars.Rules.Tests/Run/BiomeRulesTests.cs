using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Run.BattleLab;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Biome identity rules (docs/design/biomes, owner-approved 2026-11-28): synergy weather (a separate final damage multiplier of one
/// synergy: +25% for both sides' users, −25% / −50% for the player only; main campaign levels only) and the biome reward (×2 of its
/// resource, a guaranteed roll for resources without a completion line, the Biomass surge ×2 on top).
/// </summary>
public class BiomeRulesTests
{
    private static BattleResult Weathered(ContentDb c, BattleWeather? w, CombatantSpec worm, params CombatantSpec[] enemies)
    {
        var setup = new BattleSetup { RoundCap = 3, Weather = w };
        setup.Player.Add(worm);
        setup.Enemy.AddRange(enemies.Length > 0 ? enemies : new[] { Bag() });
        return BattleSimulator.Simulate(setup, c, new Rng(7UL));
    }

    [Fact]
    public void Weather_ScalesOnlyItsSynergysDamage_AsAFinalMultiplier()
    {
        var c = new ContentDb();
        string fire = Skill(c, "t_fire", On(TriggerKind.OnAttack, EffectKind.Damage, 50));
        c.Skill(fire)!.Family = SkillFamily.Fire;
        string plain = Skill(c, "t_plain", On(TriggerKind.OnAttack, EffectKind.Damage, 50)); // untagged: General
        long Hit(BattleWeather? w, string skill) => Amounts(Weathered(c, w, Worm(skill)), BattleAction.Skill, skill)[0];
        long basic = Amounts(Weathered(c, null, Worm(fire)), BattleAction.Basic)[0];
        var lava = new BattleWeather { Boost = SkillFamily.Fire, BoostPct = 25, Weak = SkillFamily.Ice, WeakPct = 50 };
        Assert.InRange(Hit(lava, fire) - Hit(null, fire) * 1.25, -1, 1); // Heat +25% (log amounts are rounded)
        Assert.Equal(Hit(null, plain), Hit(lava, plain)); // other synergies untouched
        Assert.Equal(basic, Amounts(Weathered(c, lava, Worm(fire)), BattleAction.Basic)[0]); // plain hits: no synergy
        var seabed = new BattleWeather { Boost = SkillFamily.Counter, BoostPct = 25, Weak = SkillFamily.Fire, WeakPct = 50 };
        Assert.InRange(Hit(seabed, fire) - Hit(null, fire) * 0.5, -1, 1); // Heat −50%
        var desert = new BattleWeather { Boost = SkillFamily.None, BoostPct = 25, Weak = SkillFamily.Ice, WeakPct = 50 };
        Assert.InRange(Hit(desert, plain) - Hit(null, plain) * 1.25, -1, 1); // General: untagged skills
    }

    [Fact]
    public void Weather_TheBoostHelpsEnemiesOfThatSynergy_TheWeakeningOnlyThePlayer()
    {
        var c = new ContentDb();
        string fire = Skill(c, "t_enemy_fire", On(TriggerKind.OnAttack, EffectKind.Damage, 50));
        c.Skill(fire)!.Family = SkillFamily.Fire;
        var foe = Unit("foe", CombatRole.Enemy, 1_000_000, 50, 300);
        foe.SkillIds.Add(fire);
        long Taken(BattleWeather? w)
        {
            var r = Weathered(c, w, Worm(), foe);
            return r.Log.Where(e => e.Action == BattleAction.Skill && e.SkillId == fire).Select(e => e.Amount).First();
        }
        long none = Taken(null);
        Assert.InRange(none * 1.25 - Taken(new BattleWeather { Boost = SkillFamily.Fire, BoostPct = 25, Weak = SkillFamily.Ice, WeakPct = 50 }), -1, 1);
        Assert.Equal(none, Taken(new BattleWeather { Boost = SkillFamily.Counter, BoostPct = 25, Weak = SkillFamily.Fire, WeakPct = 50 }));
    }

    [Fact]
    public void Weather_IsOnMainLevelsFromTheSignaturePick_NotOnDarkSideOrBurrow()
    {
        var c = ContentDb.Default;
        Assert.Null(c.Weather(c.MainLevel(1)));
        Assert.Null(c.Weather(c.MainLevel(2)));
        var w = c.Weather(c.MainLevel(3))!;
        var biome = c.CampaignBiome(3);
        Assert.Equal((biome.Boost, 25.0, biome.Weak, biome.WeakPct), (w.Boost, w.BoostPct, w.Weak, w.WeakPct));
        Assert.Null(c.Weather(c.DarkSideLevel(1)));
        Assert.Null(c.Weather(c.RunLevel("the_burrow_1")));
        // the doc's weather table: every synergy boosted somewhere; no biome boosts what it weakens
        var lava = c.CampaignBiomeById("lava")!;
        Assert.Equal((SkillFamily.Fire, SkillFamily.Ice, 50.0), (lava.Boost, lava.Weak, lava.WeakPct));
        Assert.All(c.Campaign.Biomes, b => Assert.NotEqual(b.Boost, b.Weak));
        Assert.All(c.Campaign.Biomes, b => Assert.Contains(b.WeakPct, new[] { 25.0, 50.0 }));
    }

    [Fact]
    public void BiomeReward_DoublesItsResource_AndAddsAGuaranteedRollWhereThereIsNoCompletionLine()
    {
        var c = ContentDb.Default;
        for (int n = 3; n <= 120; n++)
        {
            var l = c.MainLevel(n)!;
            var b = c.CampaignBiome(n);
            Assert.Equal(b.Resource, l.BiomeResource);
            bool line = l.CompletionRewards.Any(r => r.Id == b.Resource);
            Assert.Equal(!line, l.BiomeRollMax > 0);
            if (b.Resource == Currencies.Data) Assert.Equal(c.Campaign.BiomeDataRollMax * (long)Math.Round(c.MainRewardScale(c.Campaign.ChapterOf(n))), l.BiomeRollMax);
        }
        Assert.Equal(0, c.DarkSideLevel(1).BiomeRollMax);
        Assert.Null(c.DarkSideLevel(1).BiomeResource);
    }

    [Theory]
    [InlineData("lava", "ore")]
    [InlineData("shipwreck", "gear_cores")]
    [InlineData("space_station", "data")]
    public void AWonRun_PaysTheBiomeResourceTwice_AndMarksTheLine(string biomeId, string currency)
    {
        var c = RunTestKit.Engine(RunTestKit.Strong()).Content;
        int n = Enumerable.Range(3, 200).First(i => c.CampaignBiome(i).Id == biomeId);
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = TestHelpers.NewPlayer(e, 5);
        s.Run.Levels[c.MainLevelId(n - 1)!] = new LevelProgress { Cleared = true };
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = c.MainLevelId(n) }).Ok);
        RunTestKit.AutoToEnd(e, ref s);
        var sum = s.Run.LastSummary!;
        Assert.True(sum.Won);
        var lines = sum.Rewards.Where(r => r.Reward.Id == currency).ToList();
        Assert.NotEmpty(lines);
        Assert.All(lines, r => Assert.True(r.Biome, r.Source));
        Assert.All(sum.Rewards.Where(r => r.Reward.Id != currency), r => Assert.False(r.Biome, r.Reward.Id));
        var level = c.MainLevel(n)!;
        // the mode / reward modifiers of this run, read off a line the biome leaves alone (the Ore line: Ore is no biome resource here … or Biomass)
        var plainDef = level.CompletionRewards.First(r => r.Id == (currency == Currencies.Ore ? Currencies.Biomass : Currencies.Ore));
        var plainLine = sum.Rewards.First(r => r.Source == RunRewardLine.Completion && r.Reward.Id == plainDef.Id);
        double mods = plainLine.Reward.Amount / (double)plainDef.Amount / (plainDef.Id == Currencies.Biomass && sum.BiomassBonus ? c.RunTuning.DailyBiomassBonusMult : 1);
        var completion = level.CompletionRewards.FirstOrDefault(r => r.Id == currency);
        if (completion != null)
        {
            var line = lines.Single(r => r.Source == RunRewardLine.Completion);
            Assert.Equal(Math.Round(completion.Amount * 2 * mods), line.Reward.Amount);
        }
        else
        {
            // the guaranteed roll (its currency's own reward modifiers apply on top, so only the doubled floor is pinned)
            Assert.Contains(lines, r => r.Source == RunRewardLine.CompletionRoll && r.Reward.Amount >= level.BiomeRollMin * 2);
        }
    }

    [Fact]
    public void TheBiomassSurge_IsTwo()
    {
        Assert.Equal(2, ContentDb.Default.RunTuning.DailyBiomassBonusMult);
        Assert.Equal(2, ContentDb.Default.Campaign.BiomeRewardMult);
    }
}
