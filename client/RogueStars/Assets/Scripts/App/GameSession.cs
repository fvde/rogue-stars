using System;
using System.Collections.Generic;
using RogueStars.Client.Services;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.App
{
    /// <summary>
    /// Reactive app state: wraps the active <see cref="IGameService"/>, runs commands and fans out
    /// state changes, game events and errors to the UI (screens, toasts, resource bar).
    /// </summary>
    public sealed class GameSession
    {
        public IGameService Service { get; private set; }
        public GameState State => Service?.State;
        public ContentDb Content => Service?.Content;
        public GameEngine Engine => Service?.Engine;
        public long NowMs => Service != null ? Service.NowMs : SystemClock.Instance.NowMs;
        public bool IsOnline => Service != null && Service.IsOnline;
        public bool IsReady => Service != null && Service.IsConnected;

        public CommandResult LastResult { get; private set; }
        public int InFlight { get; private set; }

        private IStoreGateway _store;
        private IAdGateway _platformAds = new FakeAdGateway();
        private readonly List<(float At, Action Run)> _timers = new List<(float, Action)>();
        private readonly HashSet<string> _verifying = new HashSet<string>();
        private float _clock;

        /// <summary>Platform store (fake in editor/offline/tests, Unity IAP on device; see PlatformServices).</summary>
        public IStoreGateway Store
        {
            get => _store;
            set
            {
                if (_store != null) _store.PurchaseDelivered -= OnPurchaseDelivered;
                _store = value;
                if (_store != null) _store.PurchaseDelivered += OnPurchaseDelivered;
            }
        }

        /// <summary>Rewarded ads as screens use them: online, tokens are server-verified AppLovin events.</summary>
        public IAdGateway Ads { get; }

        /// <summary>The platform ad SDK (fake or AppLovin MAX).</summary>
        public IAdGateway PlatformAds { get => _platformAds; set => _platformAds = value ?? new FakeAdGateway(); }

        /// <summary>How long the client waits for the AppLovin S2S callback to reach the server (polling /v1/ads/credits).</summary>
        public float AdVerifyTimeoutSeconds = 45f;
        public float AdVerifyPollSeconds = 1.5f;

        public GameSession()
        {
            Store = new FakeStoreGateway();
            Ads = new ServerVerifiedAdGateway(this);
        }

        /// <summary>State replaced/changed (screens rebuild).</summary>
        public event Action Changed;
        /// <summary>A game event from a successful command (toasts).</summary>
        public event Action<GameEvent> EventRaised;
        public event Action<string> ErrorRaised;
        public event Action<string> InfoRaised;

        public void Use(IGameService service)
        {
            if (Service != null) Service.StateChanged -= OnServiceChanged;
            Service = service;
            if (Service != null) Service.StateChanged += OnServiceChanged;
        }

        private void OnServiceChanged() => Changed?.Invoke();

        /// <summary>Advances session timers (ad verification polling, pending rewards). Called every frame by ClientApp.</summary>
        public void Tick(float dt)
        {
            _clock += dt;
            for (int i = 0; i < _timers.Count; i++)
            {
                if (_timers[i].At > _clock) continue;
                var run = _timers[i].Run;
                _timers.RemoveAt(i--);
                run();
            }
        }

        public void After(float seconds, Action run) => _timers.Add((_clock + seconds, run));

        /// <summary>
        /// Products sold (PlatformConfig.ProductSet, set by ClientApp): "initial" = only the first Play Console products, "all"
        /// (default for sessions built without a config, e.g. tests) = every store product.
        /// </summary>
        public string ProductSet = StoreCatalog.SetAll;

        public bool IsOffered(string productId) => StoreCatalog.IsOffered(productId, ProductSet);

        /// <summary>After connecting: register the catalogue with the store (and bind purchases to the player).</summary>
        public void InitPlatform(Action<ServiceResult> done = null)
        {
            if (Store == null || Content == null) { done?.Invoke(ServiceResult.Fail(StoreErrors.NotReady)); return; }
            string account = IsOnline ? StoreCatalog.ObfuscatedAccountId(State?.PlayerId) : null;
            Store.Initialize(StoreCatalog.Products(Content, ProductSet), account, r =>
            {
                if (r.Ok) Store.Restore(); // unfinished/owned purchases come back through PurchaseDelivered
                done?.Invoke(r);
            });
        }

        /// <summary>Execute a command; failures become error toasts, events become toasts.</summary>
        public void Run(GameCommand command, Action<CommandResult> then = null)
        {
            if (!IsReady) { Error("Not connected"); return; }
            InFlight++;
            Service.Execute(command, r =>
            {
                InFlight--;
                LastResult = r;
                if (r.Ok) foreach (var e in r.Events) EventRaised?.Invoke(e);
                else Error(Fmt.Humanize(r.Error));
                then?.Invoke(r);
            });
        }

        /// <summary>
        /// IAP: PurchaseStart (pending record) → store → verification (dev receipt offline, /v1/iap/verify online) → Finish
        /// (consume/acknowledge) only after the grant. Deferred payments complete later through <see cref="IStoreGateway.PurchaseDelivered"/>.
        /// </summary>
        public void Purchase(string productId, Action<CommandResult> then = null)
        {
            var def = Content?.Product(productId);
            if (def == null) { Error("Unknown product"); return; }
            if (!IsOffered(productId)) { Error(def.Name + " is not available yet"); then?.Invoke(CommandResult.Fail("product_not_offered")); return; }
            Run(new PurchaseStart { ProductId = productId }, start =>
            {
                if (!start.Ok) { then?.Invoke(start); return; }
                Store.Purchase(productId, def.StoreProductId, sp =>
                {
                    if (!sp.Ok)
                    {
                        if (sp.Error == StoreErrors.Deferred) Info("Payment pending — " + def.Name + " arrives once it is approved");
                        else if (sp.Error != StoreErrors.Cancelled) Error(Fmt.Humanize(sp.Error));
                        then?.Invoke(CommandResult.Fail(sp.Error));
                        return;
                    }
                    Verify(def, sp.Value, then);
                });
            });
        }

        /// <summary>Sends a store purchase to the server; confirms it at the store only when the server granted it.</summary>
        private void Verify(ProductDef def, StorePurchase purchase, Action<CommandResult> then)
        {
            if (!_verifying.Add(purchase.PurchaseToken)) return;
            InFlight++;
            Service.VerifyPurchase(def.Id, purchase.PurchaseToken, r =>
            {
                InFlight--;
                _verifying.Remove(purchase.PurchaseToken);
                LastResult = r;
                if (r.Ok)
                {
                    Store.Finish(purchase);
                    foreach (var e in r.Events) EventRaised?.Invoke(e);
                    Info((purchase.Delivered ? "Delivered " : "Purchased ") + def.Name);
                }
                else if (r.Error == "purchase_pending") Info("Payment pending — " + def.Name + " arrives once it is approved");
                // Anything else stays unconfirmed: Play refunds unacknowledged purchases after 3 days.
                else Error(Fmt.Humanize(r.Error));
                then?.Invoke(r);
            });
        }

        /// <summary>A purchase the store delivered on its own (deferred approved, restart, restore): same verification path.</summary>
        private void OnPurchaseDelivered(StorePurchase purchase)
        {
            var def = Content?.Product(purchase.ProductId);
            if (def == null || !IsReady) return;
            bool consumable = def.Limit != 1;
            bool owned = ShopSystem.PurchaseCount(State, def.Id) > 0;
            // Owned non-consumable that the store already confirmed: nothing to do. Unconfirmed + owned: the server
            // answers alreadyCompleted and (re)acknowledges.
            if (!consumable && owned) { if (!purchase.Confirmed) Verify(def, purchase, null); return; }
            if (State.Shop.Pending.Exists(x => x.ProductId == def.Id)) { Verify(def, purchase, null); return; }
            Run(new PurchaseStart { ProductId = def.Id }, start =>
            {
                // A rejected PurchaseStart (e.g. limit reached because it was granted already) still goes to the server,
                // which answers alreadyCompleted for granted tokens and otherwise leaves the purchase unconfirmed.
                Verify(def, purchase, null);
            });
        }

        /// <summary>
        /// Rewarded ad for a placement with direct rewards. Offline: AdWatched locally. Online: the reward is granted by the
        /// server when AppLovin's S2S callback arrives; the client shows "pending" and refreshes until it is there.
        /// </summary>
        public void WatchAd(string placementId, Action<CommandResult> then = null)
        {
            if (NoAds)
            {
                // No Ads entitlement or the global ads-disabled mode: no ad at all. Offline the rules grant it directly;
                // online the server runs AdWatched without a token (its rules accept that only in this case).
                if (IsOnline && Service is IAdRewardService reward)
                {
                    InFlight++;
                    reward.InstantAd(placementId, r =>
                    {
                        InFlight--;
                        LastResult = r;
                        if (r.Ok) foreach (var e in r.Events) EventRaised?.Invoke(e);
                        else Error(Fmt.Humanize(r.Error));
                        then?.Invoke(r);
                    });
                }
                else Run(new AdWatched { PlacementId = placementId, AdToken = "" }, then);
                return;
            }
            Ads.Show(placementId, ad =>
            {
                if (!ad.Ok) { if (ad.Error != "ad_cancelled") Error(Fmt.Humanize(ad.Error)); return; }
                if (IsOnline)
                {
                    Info("Ad reward pending server verification…");
                    int before = AdViews(placementId);
                    PollReward(placementId, before, AdVerifyTimeoutSeconds, then);
                    return;
                }
                Run(new AdWatched { PlacementId = placementId, AdToken = ad.Value }, then);
            });
        }

        private int AdViews(string placementId)
        {
            var s = State?.Shop;
            return s != null && s.AdViews.TryGetValue(placementId, out var n) ? n : 0;
        }

        private void PollReward(string placementId, int before, float left, Action<CommandResult> then)
        {
            if (!IsReady) return;
            Service.Refresh(r =>
            {
                if (AdViews(placementId) > before) { Info("Ad reward received"); then?.Invoke(CommandResult.Success()); return; }
                if (left <= 0) { Info("Ad reward still pending — it will appear shortly"); then?.Invoke(CommandResult.Fail("ad_reward_pending")); return; }
                After(AdVerifyPollSeconds, () => PollReward(placementId, before, left - AdVerifyPollSeconds, then));
            });
        }

        public void Refresh(Action<ServiceResult> then = null)
        {
            if (!IsReady) { then?.Invoke(ServiceResult.Fail(ServiceErrors.NotConnected)); return; }
            Service.Refresh(r => { if (!r.Ok) Error(r.Error); then?.Invoke(r); });
        }

        public void Error(string message) => ErrorRaised?.Invoke(message);
        public void Info(string message) => InfoRaised?.Invoke(message);
        public void NotifyChanged() => Changed?.Invoke();

        /// <summary>No Ads entitlement or global ads-disabled mode (rules <c>ShopSystem.HasNoAds</c>): placements complete without an ad.</summary>
        public bool NoAds => State != null && Content != null && ShopSystem.HasNoAds(State, Content);

        /// <summary>Feature gate helper (Meta's Features).</summary>
        public bool IsUnlocked(string featureId) => State != null && Features.IsUnlocked(State, Content, featureId);
    }

    /// <summary>
    /// The ad gateway screens use. Offline: the platform ad's token. Online:
    /// <list type="bullet">
    /// <item>No Ads entitlement or global ads-disabled mode (offline and online) → no ad, empty token (rules: instant).</item>
    /// <item>Placements with direct rewards → the platform ad only; the server grants on the S2S callback (GameSession.WatchAd).</item>
    /// <item>Placements spent by a command (revive, stamina, timer, pulls, double rewards) → after the ad, poll
    /// /v1/ads/credits until the S2S callback's event id is there and return it as the command's AdToken.</item>
    /// <item>Fake platform ads (dev server in fake ads mode) → the client plays the callback itself.</item>
    /// </list>
    /// </summary>
    public sealed class ServerVerifiedAdGateway : IAdGateway
    {
        private readonly GameSession _s;
        private readonly HashSet<string> _spent = new HashSet<string>();

        public ServerVerifiedAdGateway(GameSession session) { _s = session; }

        public string Name => "verified:" + _s.PlatformAds.Name;

        public void Show(string placementId, Action<ServiceResult<string>> done)
        {
            // No Ads / global ads-disabled mode (offline and online): no ad, empty token (rules: instant).
            if (_s.NoAds) { done(ServiceResult<string>.Success("")); return; }
            var reward = _s.IsOnline ? _s.Service as IAdRewardService : null;
            if (reward == null) { _s.PlatformAds.Show(placementId, done); return; }

            var def = _s.Content.AdPlacement(placementId);
            bool direct = def != null && def.Rewards.Count > 0;
            _s.PlatformAds.Show(placementId, ad =>
            {
                if (!ad.Ok) { done(ad); return; }
                if (_s.PlatformAds.Name == "fake")
                {
                    // Dev server (fake ads mode): simulate AppLovin's callback; the token is not checked there.
                    reward.SimulateAdCallback(placementId, ad.Value, _ => done(ad));
                    return;
                }
                if (direct) { done(ad); return; }
                _s.Info("Verifying ad…");
                WaitForCredit(reward, placementId, _s.AdVerifyTimeoutSeconds, done);
            });
        }

        private void WaitForCredit(IAdRewardService reward, string placementId, float left, Action<ServiceResult<string>> done)
        {
            reward.AdCredits(r =>
            {
                if (r.Ok)
                {
                    foreach (var c in r.Value)
                    {
                        if (c.Placement != placementId || _spent.Contains(c.EventId)) continue;
                        _spent.Add(c.EventId);
                        done(ServiceResult<string>.Success(c.EventId));
                        return;
                    }
                }
                if (left <= 0) { done(ServiceResult<string>.Fail("ad_not_verified_yet")); return; }
                _s.After(_s.AdVerifyPollSeconds, () => WaitForCredit(reward, placementId, left - _s.AdVerifyPollSeconds, done));
            });
        }
    }
}
