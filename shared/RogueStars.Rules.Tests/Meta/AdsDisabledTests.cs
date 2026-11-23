using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Meta.MetaTestKit;

namespace RogueStars.Rules.Tests.Meta;

/// <summary>Global ads-disabled mode (<see cref="ShopConfig.AdsDisabled"/>): everyone behaves like a No Ads owner.</summary>
public class AdsDisabledTests
{
    [Fact]
    public void AdsDisabled_EveryPlacementIsInstant_CapsStillApply_NoEntitlementGranted()
    {
        var e = Engine();
        var s = Player(e); long t = TestHelpers.T0;
        Assert.False(ShopSystem.HasNoAds(s, e.Content));
        Assert.Equal("ad token required", Fail(e, ref s, new AdWatched { PlacementId = "daily_diamonds_ad" }, t));

        e.Content.Shop.AdsDisabled = true;
        Assert.True(ShopSystem.HasNoAds(s, e.Content));
        var r = Ok(e, ref s, new AdWatched { PlacementId = "daily_diamonds_ad" }, t);
        Assert.Contains(r.Events, ev => ev.Type == "ad_watched" && ev.Args[1] == "instant");
        // Not an entitlement: switching ads back on restores the normal rules for the same player.
        Assert.DoesNotContain(e.Content.Shop.NoAdsFlag, s.Flags);

        int left = ShopSystem.AdViewsLeft(s, e.Content, "daily_diamonds_ad", t);
        for (int i = 0; i < left; i++) Ok(e, ref s, new AdWatched { PlacementId = "daily_diamonds_ad" }, t);
        Assert.Equal("ad daily cap reached", Fail(e, ref s, new AdWatched { PlacementId = "daily_diamonds_ad" }, t));

        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = t };
        ShopSystem.ConsumeAdView(ctx, "stamina_refill", null); // owner-system placements need no token either

        e.Content.Shop.AdsDisabled = false;
        Assert.False(ShopSystem.HasNoAds(s, e.Content));
        Assert.Throws<RuleException>(() => ShopSystem.ConsumeAdView(ctx, "revive", null));
    }

    [Fact]
    public void AdsDisabled_IsOffByDefault() => Assert.False(new ContentDb().Shop.AdsDisabled);
}
