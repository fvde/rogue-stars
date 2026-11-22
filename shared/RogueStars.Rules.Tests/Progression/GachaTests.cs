using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Progression.ProgressionKit;

namespace RogueStars.Rules.Tests.Progression;

public class GachaTests
{
    private const long Week = 7 * Day;

    private static GachaPullResult Pull(GameEngine e, ref GameState s, GachaType t, int count = 1, string? banner = null, long now = TestHelpers.T0, GachaPayment pay = GachaPayment.Diamonds)
    {
        var r = Ok(e, ref s, new GachaPull { PullType = t, Count = count, BannerId = banner, Payment = pay }, now);
        return (GachaPullResult)r.Payload;
    }

    private static GameState Rich(GameEngine e, long seed = 42)
    {
        var s = Player(e, seed);
        SetBalance(s, Currencies.Diamonds, 1_000_000_000);
        return s;
    }

    [Fact]
    public void Pulls_AreDeterministicForTheSameSeed()
    {
        string Run(long seed)
        {
            var e = Engine(); var s = Rich(e, seed);
            var all = new List<string>();
            foreach (var t in new[] { GachaType.Part, GachaType.Alien, GachaType.Blueprint })
                all.AddRange(Pull(e, ref s, t, 10).Items.Select(i => i.Kind + ":" + i.Id + ":" + i.Rarity));
            return string.Join("|", all);
        }
        Assert.Equal(Run(7), Run(7));
        Assert.NotEqual(Run(7), Run(8));
    }

    [Fact]
    public void Payment_TokensOrDiamonds()
    {
        var e = Engine(); var s = Player(e);
        var def = C.GachaConfig.Types[GachaType.Part];
        SetBalance(s, Currencies.PartTokens, 10); SetBalance(s, Currencies.Diamonds, def.DiamondCost1);
        Pull(e, ref s, GachaType.Part, 10, pay: GachaPayment.Tokens);
        Assert.Equal(0, s.Wallet.Get(Currencies.PartTokens));
        Fail(e, ref s, new GachaPull { PullType = GachaType.Part, Payment = GachaPayment.Tokens });
        Pull(e, ref s, GachaType.Part, 1, pay: GachaPayment.Diamonds);
        Assert.Equal(0, s.Wallet.Get(Currencies.Diamonds));
        Fail(e, ref s, new GachaPull { PullType = GachaType.Part, Count = 5, Payment = GachaPayment.Diamonds });
        Assert.Equal(11, s.Counter("pulls_part"));
        // Each type uses its own token.
        SetBalance(s, Currencies.AlienTokens, 1); SetBalance(s, Currencies.BlueprintTokens, 1);
        Pull(e, ref s, GachaType.Alien, pay: GachaPayment.Tokens);
        Pull(e, ref s, GachaType.Blueprint, pay: GachaPayment.Tokens);
    }

    [Fact]
    public void PartPulls_GrantPartsAndRunes_AlienPulls_GrantAliensOrHeroShards()
    {
        var e = Engine(); var s = Rich(e);
        var res = Pull(e, ref s, GachaType.Part, 10);
        Assert.Equal(10, s.Parts.Items.Count + s.Parts.Runes.Count);
        Assert.Equal(10, res.Items.Count);
        for (int i = 0; i < 5; i++) Pull(e, ref s, GachaType.Alien, 10);
        Assert.Contains(s.Wallet.Balances.Keys, k => k.StartsWith("shards_hero_"));
        Assert.Contains(s.Wallet.Balances.Keys, k => k.StartsWith("stub_alien_"));
    }

    [Fact]
    public void TenPull_AlwaysContainsEpicOrBetter()
    {
        var e = Engine(cloner: false); var s = Rich(e);
        for (int i = 0; i < 300; i++)
        {
            var res = Pull(e, ref s, (GachaType)(i % 3), 10);
            Assert.Contains(res.Items, it => it.Tier >= Rarity.Epic);
        }
    }

    [Fact]
    public void MythicPity_TriggersAtThreshold_AndIsNeverExceeded()
    {
        var e = Engine(cloner: false); var s = Rich(e);
        var def = C.GachaConfig.Types[GachaType.Part];
        int gap = 0, maxGap = 0;
        for (int i = 0; i < 3000; i++)
        {
            var it = Pull(e, ref s, GachaType.Part).Items[0];
            gap++;
            if (it.Tier == Rarity.Mythic) { maxGap = Math.Max(maxGap, gap); gap = 0; }
        }
        Assert.Equal(def.MythicHardPity, maxGap); // hit exactly at the threshold at least once
        Assert.True(s.Gacha.Pity[GachaType.Part].MythicPityTriggers > 0);

        // Deterministic check: one pull before the threshold is guaranteed Mythic.
        var s2 = Rich(e, 99);
        s2.Gacha.For(GachaType.Alien).SinceMythic = def.MythicHardPity - 1;
        Assert.Equal(Rarity.Mythic, Pull(e, ref s2, GachaType.Alien).Items[0].Tier);
        Assert.Equal(0, s2.Gacha.Pity[GachaType.Alien].SinceMythic);
    }

    [Fact]
    public void LegendaryPity_SoftAndHard()
    {
        var e = Engine(cloner: false);
        var def = C.GachaConfig.Types[GachaType.Blueprint];
        int maxGap = 0;
        var s = Rich(e);
        int gap = 0;
        for (int i = 0; i < 3000; i++)
        {
            var it = Pull(e, ref s, GachaType.Blueprint).Items[0];
            gap++;
            if (it.Tier >= Rarity.Legendary) { maxGap = Math.Max(maxGap, gap); gap = 0; }
        }
        Assert.True(maxGap <= def.LegendaryHardPity);
        // Effective Legendary+ rate is clearly above the published 2.5% because of the soft pity.
        long leg = s.Counter("pulls_legendary") + s.Counter("pulls_mythic");
        Assert.True(leg / 3000.0 > 0.03);
    }

    [Fact]
    public void Pity_IsSharedAcrossBanners_AndCarriesOver()
    {
        var e = Engine(); var s = Rich(e);
        var def = C.GachaConfig.Types[GachaType.Part];
        var b1 = GachaSystem.ActiveBanner(C, GachaType.Part, TestHelpers.T0)!;
        var b2 = GachaSystem.ActiveBanner(C, GachaType.Part, TestHelpers.T0 + Week)!;
        Assert.NotEqual(b1.Id, b2.Id);
        s.Gacha.For(GachaType.Part).SinceMythic = def.MythicHardPity - 11;
        // 10 pulls on banner 1 (seed chosen so none is a natural Mythic), then banner 2 next week.
        var r1 = Pull(e, ref s, GachaType.Part, 10, b1.Id);
        Assert.All(r1.Items, i => Assert.NotEqual(Rarity.Mythic, i.Tier));
        Assert.Equal(def.MythicHardPity - 1, s.Gacha.Pity[GachaType.Part].SinceMythic);
        var next = Pull(e, ref s, GachaType.Part, 1, b2.Id, TestHelpers.T0 + Week).Items[0];
        Assert.Equal(Rarity.Mythic, next.Tier);
        Assert.Equal(1, s.Gacha.Pity[GachaType.Part].MythicPityTriggers);
        // Other pull types keep their own counters.
        Assert.False(s.Gacha.Pity.ContainsKey(GachaType.Alien));
    }

    [Fact]
    public void Banners_RotateByTime_ForAllThreeTypes()
    {
        foreach (var t in new[] { GachaType.Blueprint, GachaType.Part, GachaType.Alien })
        {
            var rot = C.GachaConfig.Rotations.Single(r => r.Type == t);
            var b0 = GachaSystem.ActiveBanner(C, t, TestHelpers.T0)!;
            Assert.True(b0.StartMs <= TestHelpers.T0 && TestHelpers.T0 < b0.EndMs);
            Assert.Equal(Rarity.Mythic, (Rarity)(t == GachaType.Part ? 4 : b0.Theme.FeaturedRarity));
            var ids = new HashSet<string>();
            for (int w = 0; w < rot.Themes.Count; w++) ids.Add(GachaSystem.ActiveBanner(C, t, TestHelpers.T0 + w * Week)!.Theme.Id);
            Assert.Equal(rot.Themes.Count, ids.Count); // every theme appears once per cycle
            var later = GachaSystem.ActiveBanner(C, t, TestHelpers.T0 + rot.Themes.Count * Week)!;
            Assert.Equal(b0.Theme.Id, later.Theme.Id);           // cycle repeats
            Assert.NotEqual(b0.Id, later.Id);                     // but is a new occurrence
        }
        Assert.Equal(3, GachaSystem.ActiveBanners(C, TestHelpers.T0).Count);
    }

    [Fact]
    public void ExpiredBanner_CannotBePulled_ScheduledBannerOverridesRotation()
    {
        var e = Engine(); var s = Rich(e);
        var b = GachaSystem.ActiveBanner(C, GachaType.Part, TestHelpers.T0)!;
        Assert.Contains("banner", Fail(e, ref s, new GachaPull { PullType = GachaType.Part, BannerId = b.Id }, b.EndMs));
        Assert.Contains("banner", Fail(e, ref s, new GachaPull { PullType = GachaType.Alien, BannerId = b.Id }));

        var content = new ContentDb();
        content.GachaConfig.ScheduledBanners.Add(new BannerDef
        {
            Id = "live_special", Type = GachaType.Part, StartMs = TestHelpers.T0, EndMs = TestHelpers.T0 + Day,
            Theme = new BannerTheme { Id = "special", FeaturedKind = RewardKinds.Part, FeaturedId = "arm_hive_whip", FeaturedRarity = (int)PartRarity.Mythic, FeaturedShare = 1 },
        });
        Assert.Equal("live_special", GachaSystem.ActiveBanner(content, GachaType.Part, TestHelpers.T0 + 1)!.Id);
        Assert.NotEqual("live_special", GachaSystem.ActiveBanner(content, GachaType.Part, TestHelpers.T0 + Day)!.Id);
    }

    [Fact]
    public void FeaturedItem_GetsRateUpShareOfMythics()
    {
        var e = Engine(cloner: false); var s = Rich(e);
        var b = GachaSystem.ActiveBanner(C, GachaType.Part, TestHelpers.T0)!;
        var def = C.GachaConfig.Types[GachaType.Part];
        int featured = 0, n = 400;
        for (int i = 0; i < n; i++)
        {
            s.Gacha.For(GachaType.Part).SinceMythic = def.MythicHardPity - 1;
            var it = Pull(e, ref s, GachaType.Part, 1, b.Id).Items[0];
            Assert.Equal(Rarity.Mythic, it.Tier);
            if (it.Featured) { featured++; Assert.Equal(b.Theme.FeaturedId, it.Id); }
        }
        Assert.InRange(featured / (double)n, 0.42, 0.58);
    }

    [Fact]
    public void PublishedOdds_SumTo100_AndHidePity()
    {
        foreach (var t in new[] { GachaType.Blueprint, GachaType.Part, GachaType.Alien })
            foreach (var withBanner in new[] { false, true })
            {
                var banner = withBanner ? GachaSystem.ActiveBanner(C, t, TestHelpers.T0)!.Id : null;
                var odds = GachaSystem.GetOdds(C, t, banner, TestHelpers.T0);
                Assert.Equal(100, odds.Tiers.Sum(x => x.Percent), 6);
                Assert.Equal(100, odds.Tiers.Sum(x => x.Items.Sum(i => i.Percent)), 6);
                foreach (var tier in odds.Tiers) Assert.Equal(tier.Percent, tier.Items.Sum(i => i.Percent), 6);
                Assert.Equal(withBanner, odds.Tiers.Single(x => x.Tier == Rarity.Mythic).Items.Any(i => i.Featured));
                Assert.Equal(Rarity.Epic, odds.TenPullMinTier);
            }
    }
}
