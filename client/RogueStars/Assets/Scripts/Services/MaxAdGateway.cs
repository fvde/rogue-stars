using System;
using UnityEngine;

namespace RogueStars.Client.Services
{
    /// <summary>
    /// AppLovin MAX rewarded ads (plugin in Assets/MaxSdk, SDK key from AppLovinSettings written at build time).
    /// Before every show: <c>MaxSdk.SetUserId(playerId)</c> (→ S2S <c>{USER_ID}</c>) and
    /// <c>ShowRewardedAd(adUnit, placement, customData: placement)</c> (→ <c>{PLACEMENT}</c>, <c>{CUSTOM_DATA}</c>), so the
    /// server's /v1/ads/ssv callback knows the player and the rules placement. The client never grants rewards itself
    /// online; it returns a local token and the server-verified flow takes over (ServerVerifiedAdGateway).
    /// </summary>
    public sealed class MaxAdGateway : IAdGateway
    {
        private readonly string _adUnitId;
        private bool _initialized;
        private int _retry;
        private Action<ServiceResult<string>> _pending;
        private bool _rewarded;

        /// <summary>Player id for {USER_ID}; set by the composition root.</summary>
        public Func<string> UserId;

        public string Name => "max";
        public bool IsReady => _initialized && MaxSdk.IsRewardedAdReady(_adUnitId);

        public MaxAdGateway(string adUnitId, bool verbose)
        {
            _adUnitId = adUnitId;
            MaxSdkCallbacks.OnSdkInitializedEvent += _ => { _initialized = true; Load(); };
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += (_, __) => _retry = 0;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += (_, err) =>
            {
                // Exponential retry up to 64 s, as AppLovin recommends.
                _retry = Math.Min(_retry + 1, 6);
                Debug.LogWarning("[MAX] rewarded load failed: " + err?.Message);
                RetryLater(Math.Pow(2, _retry));
            };
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += (_, __, ___) => _rewarded = true;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += (_, err, __) => { Complete(ServiceResult<string>.Fail("ad_failed")); Load(); };
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += (_, __) =>
            {
                Complete(_rewarded ? ServiceResult<string>.Success("max-" + Guid.NewGuid().ToString("N")) : ServiceResult<string>.Fail("ad_cancelled"));
                Load();
            };
            MaxSdk.SetVerboseLogging(verbose);
            MaxSdk.InitializeSdk();
        }

        public void Show(string placementId, Action<ServiceResult<string>> done)
        {
            if (_pending != null) { done(ServiceResult<string>.Fail("ad_in_progress")); return; }
            if (!IsReady)
            {
                if (_initialized) Load();
                done(ServiceResult<string>.Fail("ad_not_ready"));
                return;
            }
            string user = UserId?.Invoke();
            if (!string.IsNullOrEmpty(user)) MaxSdk.SetUserId(user);
            _pending = done;
            _rewarded = false;
            MaxSdk.ShowRewardedAd(_adUnitId, placementId, placementId);
        }

        private void Complete(ServiceResult<string> r)
        {
            var cb = _pending;
            _pending = null;
            cb?.Invoke(r);
        }

        private void Load() => MaxSdk.LoadRewardedAd(_adUnitId);

        private async void RetryLater(double seconds)
        {
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(seconds));
            Load();
        }
    }
}
