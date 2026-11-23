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
        public const string StoreFake = "fake", StoreUnityIap = "unity_iap";
        public const string AdsFake = "fake", AdsMax = "max";

        /// <summary>"fake" or "unity_iap" (Google Play Billing through Unity IAP).</summary>
        public string StoreMode = StoreUnityIap;
        /// <summary>"fake" or "max" (AppLovin MAX rewarded ads).</summary>
        public string AdsMode = AdsMax;
        /// <summary>MAX rewarded ad unit id for Android (dashboard: MAX → Ad Units).</summary>
        public string MaxRewardedAdUnitId = Placeholder + "MAX_REWARDED_AD_UNIT_ID";
        public bool MaxVerboseLogging;

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
            if (Application.isEditor || cfg == null || cfg.StoreMode != PlatformConfig.StoreUnityIap) return new FakeStoreGateway();
            return new UnityIapStoreGateway();
        }

        public static IAdGateway CreateAds(PlatformConfig cfg)
        {
            if (Application.isEditor || cfg == null || cfg.AdsMode != PlatformConfig.AdsMax || !cfg.HasMaxAdUnit) return new FakeAdGateway();
            return new MaxAdGateway(cfg.MaxRewardedAdUnitId, cfg.MaxVerboseLogging);
        }
    }
}
