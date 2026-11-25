using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Progression.ProgressionKit;

namespace RogueStars.Rules.Tests.Progression;

/// <summary>
/// Owner decisions 2026-11-25: featured banners pay 80% featured / 20% off banner on Legendary and Mythic hits (no carry-over
/// guarantee); the recurring core blueprint banner features only the Laboratory and the three pull buildings; each Signal's unlock
/// gives one free 10× pull, the Blueprint one always holding the Launch Pod.
/// </summary>
public class BannerAndFreePullTests
{
    private const long Week = 7 * Day;
    private static readonly string[] PullBuildings = { "bp_token_relay", "bp_signal_beacon", "bp_part_fabricator" };

    private static GachaPullResult Pull(GameEngine e, ref GameState s, GachaType t, int count, string? banner, long now, GachaPayment pay = GachaPayment.Diamonds) =>
        (GachaPullResult)Ok(e, ref s, new GachaPull { PullType = t, Count = count, BannerId = banner, Payment = pay }, now).Payload!;

    private static GameState Rich(GameEngine e, long seed = 42)
    {
        var s = Player(e, seed);
        SetBalance(s, Currencies.Diamonds, 1_000_000_000);
        return s;
    }

    private static ActiveBannerInfo CoreBanner(ContentDb c, out long at)
    {
        for (int w = 0; w < 12; w++)
        {
            at = TestHelpers.T0 + w * Week;
            var b = GachaSystem.ActiveBanner(c, GachaType.Blueprint, at)!;
            if (b.Theme.Id == ContentDb.CoreBlueprintBannerId) return b;
        }
        throw new Xunit.Sdk.XunitException("no core banner in 12 weeks");
    }

    [Fact]
    public void CoreBlueprintBanner_FeaturesOnlyTheLaboratory_AndThePullBuildings_EveryOtherWeek()
    {
        var c = ContentDb.Default;
        var rot = c.GachaConfig.Rotations.Single(r => r.Type == GachaType.Blueprint);
        Assert.Equal(7 * Day, rot.DurationMs);
        Assert.Equal(6, rot.Themes.Count);
        for (int i = 0; i < rot.Themes.Count; i++)
            Assert.Equal(i % 2 == 0, rot.Themes[i].Id == ContentDb.CoreBlueprintBannerId); // every other week
        var core = rot.Themes[0];
        Assert.Equal(new[] { ContentDb.LaboratoryId }, core.FeaturedIn(Rarity.Mythic).Select(f => f.Id));
        Assert.Equal(PullBuildings.OrderBy(x => x), core.FeaturedIn(Rarity.Legendary).Select(f => f.Id).OrderBy(x => x));
        foreach (var r in new[] { Rarity.Common, Rarity.Rare, Rarity.Epic }) Assert.Empty(core.FeaturedIn(r));
        foreach (var f in core.FeaturedIn(Rarity.Legendary).Concat(core.FeaturedIn(Rarity.Mythic)))
            Assert.Equal(f.Tier, c.Blueprint(f.Id)!.Rarity); // featured in the tier of its own rarity
        // every banner of every type features Legendary / Mythic items only, at 80%
        foreach (var t in c.GachaConfig.Rotations.SelectMany(r => r.Themes))
        {
            Assert.Equal(0.8, t.FeaturedShare, 6);
            Assert.True(t.FeaturedTier >= Rarity.Legendary && t.MoreFeatured.All(f => f.Tier >= Rarity.Legendary), t.Id);
        }
    }

    [Fact]
    public void LegendaryAndMythicHits_On_TheBanner_Are80PercentFeatured_TheRestAnotherItemOfTheTier()
    {
        var e = Engine(cloner: false); var s = Rich(e);
        var b = CoreBanner(e.Content, out long at);
        var def = e.Content.GachaConfig.Types[GachaType.Blueprint];
        int n = 600, legFeat = 0, mythFeat = 0;
        for (int i = 0; i < n; i++)
        {
            s.Gacha.For(GachaType.Blueprint).SinceMythic = 0;
            s.Gacha.For(GachaType.Blueprint).SinceLegendary = def.LegendaryPity - 1; // the Legendary pity: Legendary (or a Mythic roll)
            var it = Pull(e, ref s, GachaType.Blueprint, 1, b.Id, at).Items[0];
            if (it.Tier != Rarity.Legendary) continue;
            Assert.Equal(it.Featured, PullBuildings.Contains(it.Id)); // off banner never returns a featured item
            if (it.Featured) legFeat++;
        }
        for (int i = 0; i < n; i++)
        {
            s.Gacha.For(GachaType.Blueprint).SinceMythic = def.MythicPity - 1;
            var it = Pull(e, ref s, GachaType.Blueprint, 1, b.Id, at).Items[0];
            Assert.Equal(Rarity.Mythic, it.Tier);
            Assert.Equal(it.Featured, it.Id == ContentDb.LaboratoryId);
            if (it.Featured) mythFeat++;
        }
        Assert.InRange(legFeat / (double)n, 0.73, 0.87);
        Assert.InRange(mythFeat / (double)n, 0.73, 0.87);
    }

    [Fact]
    public void PublishedOdds_Show80Featured_20OffBanner_TierRatesUnchanged()
    {
        var c = ContentDb.Default;
        var b = CoreBanner(c, out long at);
        var plain = GachaSystem.GetOdds(c, GachaType.Blueprint, null, at);
        var odds = GachaSystem.GetOdds(c, GachaType.Blueprint, b.Id, at);
        for (int i = 0; i < odds.Tiers.Count; i++) Assert.Equal(plain.Tiers[i].Percent, odds.Tiers[i].Percent, 9); // published tier odds
        Assert.Equal((100, 40), (odds.MythicPity, odds.LegendaryPity));
        foreach (var tier in odds.Tiers)
        {
            Assert.Equal(tier.Percent, tier.Items.Sum(x => x.Percent), 6);
            bool featuredTier = tier.Tier >= Rarity.Legendary;
            Assert.Equal(featuredTier ? 80 : 0, tier.FeaturedPercent, 6);
            Assert.Equal(featuredTier ? tier.Percent * 0.8 : 0, tier.Items.Where(x => x.Featured).Sum(x => x.Percent), 6);
            Assert.DoesNotContain(tier.Items, x => !x.Featured && featuredTier && b.Theme.IsFeatured(x.Kind, x.Id)); // listed once
        }
        var leg = odds.Tiers.Single(x => x.Tier == Rarity.Legendary);
        Assert.All(leg.Items.Where(x => x.Featured), x => Assert.Equal(3.0 * 0.8 / 3, x.Percent, 6)); // 0.8% each
        Assert.Equal(1.2 * 0.8, odds.Tiers.Single(x => x.Tier == Rarity.Mythic).Items.Single(x => x.Featured).Percent, 6);
    }

    [Fact]
    public void NoHiddenGuarantee_AfterAnOffBannerMythic_TheNextIsStill80Percent()
    {
        var e = Engine(cloner: false); var s = Rich(e, 7);
        var b = CoreBanner(e.Content, out long at);
        var def = e.Content.GachaConfig.Types[GachaType.Blueprint];
        bool prevOff = false;
        int afterOff = 0, afterOffFeatured = 0;
        for (int i = 0; i < 1500; i++)
        {
            s.Gacha.For(GachaType.Blueprint).SinceMythic = def.MythicPity - 1;
            bool featured = Pull(e, ref s, GachaType.Blueprint, 1, b.Id, at).Items[0].Featured;
            if (prevOff) { afterOff++; if (featured) afterOffFeatured++; }
            prevOff = !featured;
        }
        Assert.True(afterOff > 150);
        Assert.InRange(afterOffFeatured / (double)afterOff, 0.7, 0.9); // not 100%: no "lost 50/50 → next guaranteed" rule
        Assert.DoesNotContain(typeof(GachaPityState).GetFields(), x => x.Name.Contains("Featured") || x.Name.Contains("Guarantee"));
    }

    [Fact]
    public void EachSignalUnlock_GivesOneFree10x_NoneHoldsAGuarantee()
    {
        var e = Engine(); var s = e.NewPlayer("p1", TestHelpers.T0, 42);
        Assert.Equal(0, s.Gacha.FreePulls(GachaType.Part));
        s.Counters[Requirements.MainLevelCounter] = 15;
        Ok(e, ref s, new CityCollect(), TestHelpers.T0);
        foreach (var t in new[] { GachaType.Part, GachaType.Alien, GachaType.Blueprint })
            Assert.Equal(1, s.Gacha.FreePulls(t));

        long dia = s.Wallet.Get(Currencies.Diamonds), bpTok = s.Wallet.Get(Currencies.BlueprintTokens);
        Assert.Contains("10×", Fail(e, ref s, new GachaPull { PullType = GachaType.Blueprint, Count = 1, Payment = GachaPayment.Free }));
        var r = Pull(e, ref s, GachaType.Blueprint, 10, null, TestHelpers.T0, GachaPayment.Free);
        Assert.Equal(10, r.Items.Count);
        // the crash site comes first (owner, 2026-11-25): the Launch Pod came with the Aliens, so the City's free 10× is a normal 10×
        Assert.DoesNotContain(typeof(GachaTypeDef).GetFields(), x => x.Name == "FreePullGuarantee");
        Assert.True(CitySystem.IsBuilt(CitySystem.LaunchPod(s)));
        Assert.Contains(r.Items, i => i.Tier >= Rarity.Epic); // the 10× rule still holds
        Assert.Equal((dia, bpTok), (s.Wallet.Get(Currencies.Diamonds), s.Wallet.Get(Currencies.BlueprintTokens))); // free
        Assert.Equal(10, s.Gacha.For(GachaType.Blueprint).TotalPulls); // counts for the pity like any 10×
        Assert.Equal("no free pull", Fail(e, ref s, new GachaPull { PullType = GachaType.Blueprint, Count = 10, Payment = GachaPayment.Free }));

        // the Part and Alien free pulls have no guarantee: ordinary 10× results
        var p = Pull(e, ref s, GachaType.Part, 10, null, TestHelpers.T0, GachaPayment.Free);
        Assert.Equal(10, p.Items.Count);
        Assert.Equal(0, s.Gacha.FreePulls(GachaType.Part));
    }

    [Fact]
    public void BuildingFeatures_UnlockWithTheirBlueprint_NotByLevel()
    {
        var e = Engine(); var s = e.NewPlayer("p1", TestHelpers.T0, 42);
        s.Counters[Requirements.MainLevelCounter] = 60;
        Ok(e, ref s, new CityCollect(), TestHelpers.T0);
        Assert.True(Features.IsUnlocked(s, e.Content, Features.AlienSquad)); // the Launch Pod came with the Aliens (crash site)
        foreach (var id in new[] { Features.Laboratory, Features.GrainFarm, Features.OreMine, Features.Breeding })
            Assert.False(Features.IsUnlocked(s, e.Content, id), id);
        Assert.Equal("laboratory locked", Fail(e, ref s, new LabResearch { ResearchId = "res_burning_soul" }));

        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = TestHelpers.T0 };
        ctx.Grant(new Reward(RewardKinds.Blueprint, ContentDb.LaboratoryId, 1));
        long data = s.Wallet.Get(Currencies.Data);
        var r = Ok(e, ref s, new CityCollect(), TestHelpers.T0 + 1000);
        Assert.Contains(r.Events, ev => ev.Type == "feature_unlocked" && ev.Args[0] == Features.Laboratory);
        Assert.Equal(data + 60, s.Wallet.Get(Currencies.Data)); // its unlock gift: the first researches
        Assert.Equal("Pull the Laboratory blueprint",
            Requirements.Describe(e.Content, e.Content.Feature(Features.Laboratory)!.Requires.Items.Single(x => x.Kind == RequirementKind.Blueprint)));
        // owning the blueprint is not enough to research: the Laboratory must be built and staffed (City design)
        Assert.NotEqual("laboratory locked", Fail(e, ref s, new LabResearch { ResearchId = "res_burning_soul" }, TestHelpers.T0 + 2000));
    }
}
