using UnityEngine;

namespace RogueStars.Client.Services
{
    /// <summary>
    /// Store / ads selection and ad unit ids (Assets/Resources/PlatformConfig.asset). The committed asset holds
    /// placeholders; BuildAndroid writes the real values from environment variables for the build only and restores the
    /// placeholders afterwards (ROGUESTARS_STORE, ROGUESTARS_ADS, ROGUESTARS_MAX_REWARDED_AD_UNIT; the MAX SDK key goes into
    /// AppLovinSettings from ROGUESTARS_MAX_SDK_KEY). The editor, tests and offline play always use the fake gateways.
    /// </summary>
    [CreateAssetMenu(menuName = "Rogue Stars/Platform Config", fileName = "PlatformConfig")]
    public sealed class PlatformConfig : ScriptableObject
    {
        public const string Placeholder = "YOUR_";
        public const string StoreDisabled = "disabled", StoreFake = "fake", StoreUnityIap = "unity_iap";
        public const string AdsDisabled = "disabled", AdsFake = "fake", AdsMax = "max";

        /// <summary>
        /// "disabled" (default for now: Google Play payments on hold — Unity IAP is never initialized, online the shop shows
        /// real-money products as "coming soon"; offline keeps dev receipts), "fake" or "unity_iap" (Google Play Billing).
        /// </summary>
        public string StoreMode = StoreDisabled;
        /// <summary>
        /// "disabled" (default for now: no ad SDK is initialized, every placement completes instantly as with No Ads — the
        /// rules flag ShopConfig.AdsDisabled; online the server's session flag decides), "fake" or "max" (AppLovin MAX).
        /// </summary>
        public string AdsMode = AdsDisabled;
        /// <summary>MAX rewarded ad unit id for Android (dashboard: MAX → Ad Units).</summary>
        public string MaxRewardedAdUnitId = Placeholder + "MAX_REWARDED_AD_UNIT_ID";
        public bool MaxVerboseLogging;
        /// <summary>
        /// Products sold in this build: "initial" (only StoreCatalog.InitialProductIds, the first set created in Play Console)
        /// or "all". The server has the same switch (Iap:ProductSet). Build override: ROGUESTARS_PRODUCT_SET.
        /// </summary>
        public string ProductSet = StoreCatalog.SetInitial;

        /// <summary>Global ads-disabled mode for offline play (online, the server's <c>adsDisabled</c> session flag applies).</summary>
        public bool IsAdsDisabled => AdsMode == AdsDisabled;

        public bool HasMaxAdUnit => !string.IsNullOrEmpty(MaxRewardedAdUnitId) && !MaxRewardedAdUnitId.StartsWith(Placeholder);

        public static PlatformConfig LoadDefault()
        {
            var cfg = Resources.Load<PlatformConfig>("PlatformConfig");
            return cfg != null ? cfg : CreateInstance<PlatformConfig>();
        }
    }

    /// <summary>Creates the store and ad gateways for the running platform.</summary>
    public static class PlatformServices
    {
        public static IStoreGateway CreateStore(PlatformConfig cfg)
        {
            if (Application.isEditor || cfg == null) return new FakeStoreGateway();
            if (cfg.StoreMode == PlatformConfig.StoreDisabled) return new DisabledStoreGateway();
            if (cfg.StoreMode != PlatformConfig.StoreUnityIap) return new FakeStoreGateway();
            return new UnityIapStoreGateway();
        }

        /// <summary>
        /// Editor/tests: always the fake. Device: "disabled" → <see cref="NoAdGateway"/> (the AppLovin SDK is never initialized
        /// and needs no key), "max" with an ad unit id → MAX, otherwise the fake.
        /// </summary>
        public static IAdGateway CreateAds(PlatformConfig cfg)
        {
            if (Application.isEditor || cfg == null) return new FakeAdGateway();
            if (cfg.IsAdsDisabled) return new NoAdGateway();
            if (cfg.AdsMode != PlatformConfig.AdsMax || !cfg.HasMaxAdUnit) return new FakeAdGateway();
            return new MaxAdGateway(cfg.MaxRewardedAdUnitId, cfg.MaxVerboseLogging);
        }
    }
}
