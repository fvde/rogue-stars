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
    public void PartPulls_GrantPartsAndRunes_AlienPulls_GrantAliensOrHeroSupplies()
    {
        var e = Engine(); var s = Rich(e);
        s.Flags.Add(Features.DevUnlockAllFlag);
        var res = Pull(e, ref s, GachaType.Part, 10);
        Assert.Equal(10, s.Parts.Items.Count + s.Parts.Runes.Count);
        Assert.Equal(10, res.Items.Count);
        for (int i = 0; i < 5; i++) Pull(e, ref s, GachaType.Alien, 10);
        // Heroes come from summon events: the Alien Signal's old hero-shard share is Grain and hero summon tickets now.
        Assert.DoesNotContain(s.Wallet.Balances.Keys, k => k.StartsWith("shards_hero_"));
        Assert.True(s.Wallet.Get(Currencies.Grain) + s.Wallet.Get(Currencies.HeroTickets) > 0);
        Assert.NotEmpty(s.Aliens.Owned);
    }

    [Fact]
    public void Pools_ContainEveryBlueprintAndAlien_InTheTierOfTheirRarity()
    {
        var bp = C.GachaConfig.Types[GachaType.Blueprint];
        var al = C.GachaConfig.Types[GachaType.Alien];
        foreach (var d in C.Blueprints.Values)
            Assert.Contains(bp.Tiers[(int)d.Rarity].Pool, en => en.Kind == RewardKinds.Blueprint && en.Id == d.Id);
        foreach (var d in C.AlienTypes.Values)
            Assert.Contains(al.Tiers[(int)d.Rarity].Pool, en => en.Kind == RewardKinds.Alien && en.Id == d.Id);
        // Summon events are the way to get heroes and mounts: no mount copies or hero shards in the Signals. Their old share
        // (~25% of Blueprint, ~40% of Alien outcomes) is supplies: Ore / Grain, summon tickets in Epic and Legendary. The Mythic
        // tier holds Mythic items only (pull system).
        void Supplies(GachaTypeDef def, string low, string ticket, double min, double max)
        {
            Assert.All(def.Tiers.Single(x => x.Tier == Rarity.Mythic).Pool, en => Assert.NotEqual(RewardKinds.Currency, en.Kind));
            foreach (var t in def.Tiers.Where(x => x.Tier < Rarity.Mythic))
            {
                var sup = t.Pool.Where(en => en.Kind == RewardKinds.Currency).ToList();
                Assert.Single(sup);
                Assert.Equal(t.Tier < Rarity.Epic ? low : ticket, sup[0].Id);
                Assert.InRange(sup[0].Weight / t.Pool.Sum(en => en.Weight), min, max);
            }
        }
        Supplies(bp, Currencies.Ore, Currencies.MountTickets, 0.2, 0.3);
        Supplies(al, Currencies.Grain, Currencies.HeroTickets, 0.35, 0.45);
        foreach (var t in bp.Tiers) foreach (var en in t.Pool)
            if (en.Kind != RewardKinds.Currency) { Assert.Equal(RewardKinds.Blueprint, en.Kind); Assert.NotNull(C.Blueprint(en.Id)); }
        foreach (var t in al.Tiers) foreach (var en in t.Pool)
            if (en.Kind != RewardKinds.Currency) { Assert.Equal(RewardKinds.Alien, en.Kind); Assert.NotNull(C.AlienType(en.Id)); }
        foreach (var r in C.GachaConfig.Rotations) foreach (var th in r.Themes)
        {
            Assert.NotEqual(RewardKinds.Mount, th.FeaturedKind);
            Assert.NotEqual(RewardKinds.HeroShards, th.FeaturedKind);
            if (th.FeaturedKind == RewardKinds.Blueprint) Assert.Equal(Rarity.Mythic, C.Blueprint(th.FeaturedId)!.Rarity);
            if (th.FeaturedKind == RewardKinds.Alien) Assert.Equal(Rarity.Mythic, C.AlienType(th.FeaturedId)!.Rarity);
        }
    }

    [Fact]
    public void EachPullType_GrantsRealItems_EndToEnd()
    {
        var e = Engine(); var s = Rich(e);
        s.Flags.Add(Features.DevUnlockAllFlag);

        // Blueprint Signal → City's blueprint granter: owned blueprints (1★) or shards for duplicates.
        long copiesBefore = s.Counter("blueprints_obtained") + s.Counter("blueprint_duplicates");
        var bpRes = Pull(e, ref s, GachaType.Blueprint, 10);
        int bpItems = 0;
        foreach (var it in bpRes.Items)
            if (it.Kind == RewardKinds.Blueprint) { Assert.NotNull(CitySystem.Owned(s, it.Id)); bpItems++; }
            else Assert.Equal(RewardKinds.Currency, it.Kind); // Ore / mount summon tickets
        Assert.Equal(s.City.Blueprints.Count, s.Counter("blueprints_obtained"));
        Assert.Equal(copiesBefore + bpItems, s.Counter("blueprints_obtained") + s.Counter("blueprint_duplicates"));

        // Alien Signal → City's alien granter (rolled genes/traits) or supplies (Grain / hero summon tickets).
        var alRes = Pull(e, ref s, GachaType.Alien, 10);
        foreach (var it in alRes.Items)
        {
            if (it.Kind == RewardKinds.Alien) Assert.NotNull(AlienSystem.Owned(s, it.Id));
            else Assert.True(s.Wallet.Get(it.Id) > 0);
        }

        // Part Signal → Progression's part/rune granters.
        int before = s.Parts.Items.Count + s.Parts.Runes.Count;
        Pull(e, ref s, GachaType.Part, 10);
        Assert.Equal(before + 10, s.Parts.Items.Count + s.Parts.Runes.Count);

        // Featured mythic of the active blueprint banner is a real City blueprint.
        var banner = GachaSystem.ActiveBanner(C, GachaType.Blueprint, TestHelpers.T0)!;
        Assert.True(C.Blueprint(banner.Theme.FeaturedId) != null || C.MountConfig.Defs.ContainsKey(banner.Theme.FeaturedId));
        for (int i = 0; i < 6; i++) Pull(e, ref s, GachaType.Blueprint, 10, banner.Id);
        Assert.True(s.Counter("pulls_mythic") >= 1);
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
        Assert.Equal(def.MythicPity, maxGap); // hit exactly at the threshold at least once
        Assert.True(s.Gacha.Pity[GachaType.Part].MythicPityTriggers > 0);

        // Deterministic check: one pull before the threshold is guaranteed Mythic.
        var s2 = Rich(e, 99);
        s2.Gacha.For(GachaType.Alien).SinceMythic = def.MythicPity - 1;
        Assert.Equal(Rarity.Mythic, Pull(e, ref s2, GachaType.Alien).Items[0].Tier);
        Assert.Equal(0, s2.Gacha.Pity[GachaType.Alien].SinceMythic);
    }

    [Fact]
    public void LegendaryPity_FixedAt40_NoSoftPity()
    {
        var e = Engine(cloner: false);
        var def = C.GachaConfig.Types[GachaType.Blueprint];
        Assert.Equal(40, def.LegendaryPity);
        int maxGap = 0;
        var s = Rich(e);
        int gap = 0;
        for (int i = 0; i < 3000; i++)
        {
            var it = Pull(e, ref s, GachaType.Blueprint).Items[0];
            gap++;
            if (it.Tier >= Rarity.Legendary) { maxGap = Math.Max(maxGap, gap); gap = 0; }
        }
        Assert.True(maxGap <= def.LegendaryPity);
        // Deterministic: the 39th pull without Legendary+ is never forced (no soft pity), the 40th always is.
        var s2 = Rich(e, 5);
        s2.Gacha.For(GachaType.Blueprint).SinceLegendary = def.LegendaryPity - 1;
        Assert.True(Pull(e, ref s2, GachaType.Blueprint).Items[0].Tier >= Rarity.Legendary);
        Assert.Equal(1, s2.Gacha.Pity[GachaType.Blueprint].LegendaryPityTriggers + s2.Gacha.Pity[GachaType.Blueprint].Mythics);
    }

    [Fact]
    public void Pity_IsSharedAcrossBanners_AndCarriesOver()
    {
        var e = Engine(); var s = Rich(e);
        var def = C.GachaConfig.Types[GachaType.Part];
        var b1 = GachaSystem.ActiveBanner(C, GachaType.Part, TestHelpers.T0)!;
        var b2 = GachaSystem.ActiveBanner(C, GachaType.Part, TestHelpers.T0 + Week)!;
        Assert.NotEqual(b1.Id, b2.Id);
        // 10 pulls on banner 1 (the first seed where none is a natural Mythic), then banner 2 next week.
        for (long seed = 1; ; seed++)
        {
            s = Rich(e, seed);
            s.Gacha.For(GachaType.Part).SinceMythic = def.MythicPity - 11;
            var r1 = Pull(e, ref s, GachaType.Part, 10, b1.Id);
            if (r1.Items.All(i => i.Tier != Rarity.Mythic)) break;
        }
        Assert.Equal(def.MythicPity - 1, s.Gacha.Pity[GachaType.Part].SinceMythic);
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
            Assert.Equal(rot.Themes.Select(x => x.Id).Distinct().Count(), ids.Count); // every theme appears in a cycle
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
    public void FeaturedItem_Gets80PercentOfMythics()
    {
        var e = Engine(cloner: false); var s = Rich(e);
        var b = GachaSystem.ActiveBanner(C, GachaType.Part, TestHelpers.T0)!;
        var def = C.GachaConfig.Types[GachaType.Part];
        int featured = 0, n = 400;
        for (int i = 0; i < n; i++)
        {
            s.Gacha.For(GachaType.Part).SinceMythic = def.MythicPity - 1;
            var it = Pull(e, ref s, GachaType.Part, 1, b.Id).Items[0];
            Assert.Equal(Rarity.Mythic, it.Tier);
            if (it.Featured) { featured++; Assert.Equal(b.Theme.FeaturedId, it.Id); }
        }
        Assert.InRange(featured / (double)n, 0.72, 0.88); // 80% on banner, 20% another Mythic (owner, 2026-11-25)
    }

    [Fact]
    public void PublishedOdds_SumTo100_AndPublishTheFixedPity()
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
                Assert.True(odds.TenPullGuarantee);
                Assert.Equal((100, 40), (odds.MythicPity, odds.LegendaryPity));
                Assert.Equal(1.2, odds.Tiers.Single(x => x.Tier == Rarity.Mythic).Percent, 6);
                Assert.Equal(3.0, odds.Tiers.Single(x => x.Tier == Rarity.Legendary).Percent, 6);
            }
    }
}
