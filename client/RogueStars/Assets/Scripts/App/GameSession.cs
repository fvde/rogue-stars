using System;
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

        public IStoreGateway Store = new FakeStoreGateway();
        public IAdGateway Ads = new FakeAdGateway();

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

        /// <summary>IAP: PurchaseStart → store gateway → verification (dev receipt offline, /v1/iap/verify online) → finish.</summary>
        public void Purchase(string productId, Action<CommandResult> then = null)
        {
            var def = Content?.Product(productId);
            if (def == null) { Error("Unknown product"); return; }
            Run(new PurchaseStart { ProductId = productId }, start =>
            {
                if (!start.Ok) { then?.Invoke(start); return; }
                Store.Purchase(productId, def.StoreProductId, sp =>
                {
                    if (!sp.Ok) { Error(sp.Error); then?.Invoke(CommandResult.Fail(sp.Error)); return; }
                    InFlight++;
                    Service.VerifyPurchase(productId, sp.Value.PurchaseToken, r =>
                    {
                        InFlight--;
                        LastResult = r;
                        if (r.Ok)
                        {
                            Store.Finish(sp.Value);
                            foreach (var e in r.Events) EventRaised?.Invoke(e);
                            Info("Purchased " + def.Name);
                        }
                        else Error(Fmt.Humanize(r.Error));
                        then?.Invoke(r);
                    });
                });
            });
        }

        /// <summary>Rewarded ad for a placement with direct rewards (offline: AdWatched locally; online: server SSV callback).</summary>
        public void WatchAd(string placementId, Action<CommandResult> then = null)
        {
            Ads.Show(placementId, ad =>
            {
                if (!ad.Ok) { Error(ad.Error); return; }
                if (IsOnline)
                {
                    // AdWatched is a server hook (issued by /v1/ads/ssv); the reward appears after the callback.
                    Info("Ad reward will arrive after server verification");
                    Service.Refresh(_ => { });
                    return;
                }
                Run(new AdWatched { PlacementId = placementId, AdToken = ad.Value }, then);
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

        /// <summary>Feature gate helper (Meta's Features).</summary>
        public bool IsUnlocked(string featureId) => State != null && Features.IsUnlocked(State, Content, featureId);
    }
}
