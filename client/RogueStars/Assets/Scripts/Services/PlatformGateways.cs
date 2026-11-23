using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Client.Services
{
    /// <summary>A store transaction waiting for server verification and confirmation.</summary>
    public sealed class StorePurchase
    {
        /// <summary>Rules product id (ContentDb.Products).</summary>
        public string ProductId;
        /// <summary>Store SKU, e.g. "rs.diamonds_60".</summary>
        public string StoreProductId;
        /// <summary>Google Play purchase token (sent to /v1/iap/verify).</summary>
        public string PurchaseToken;
        public string OrderId;
        /// <summary>Delivered outside a Purchase call: app restart before verification, deferred payment approved, restore.</summary>
        public bool Delivered;
        /// <summary>Already confirmed at the store (an owned non-consumable found by a restore).</summary>
        public bool Confirmed;
        /// <summary>Store-specific handle needed to confirm (Unity IAP PendingOrder).</summary>
        public object Handle;
    }

    /// <summary>Store product as the gateway registers it with the platform store.</summary>
    public sealed class StoreProductInfo
    {
        public string ProductId;
        public string StoreProductId;
        public bool Consumable;
    }

    public static class StoreErrors
    {
        public const string Cancelled = "purchase_cancelled";
        /// <summary>Payment not completed yet (Play "pending" / deferred); the purchase is delivered later.</summary>
        public const string Deferred = "purchase_deferred";
        public const string NotReady = "store_not_ready";
    }

    /// <summary>
    /// Google Play product types of the rules catalogue — must match the server's PlayCatalog
    /// (server/RogueStars.Server/Payments/PlayCatalog.cs) and docs/play-console-products.csv:
    /// lifetime limit 1 → non-consumable, everything else (incl. the monthly pass, a 30-day block) → consumable.
    /// </summary>
    public static class StoreCatalog
    {
        public const string SetInitial = "initial", SetAll = "all";

        /// <summary>
        /// The first products created in Play Console (docs/play-console-products-initial.csv); same list as the server's
        /// PlayCatalog.InitialProductIds. With product set "initial" only these are offered.
        /// </summary>
        public static readonly string[] InitialProductIds = { "diamonds_60", "starter_worm", "monthly_pass", "perm_daily_diamonds" };

        /// <summary>Product set "all" offers everything; anything else ("initial") only <see cref="InitialProductIds"/>.</summary>
        public static bool IsOffered(string productId, string productSet) =>
            productSet == SetAll || Array.IndexOf(InitialProductIds, productId) >= 0;

        public static List<StoreProductInfo> Products(ContentDb content, string productSet = SetAll)
        {
            var list = new List<StoreProductInfo>();
            foreach (var p in content.Products.Values)
                if (p.PriceUsdCents > 0 && !string.IsNullOrEmpty(p.StoreProductId) && IsOffered(p.Id, productSet))
                    list.Add(new StoreProductInfo { ProductId = p.Id, StoreProductId = p.StoreProductId, Consumable = p.Limit != 1 });
            list.Sort((a, b) => string.CompareOrdinal(a.ProductId, b.ProductId));
            return list;
        }

        /// <summary>Play obfuscatedAccountId = hex(sha256("rs-account:" + playerId)); the server checks it on every receipt.</summary>
        public static string ObfuscatedAccountId(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return null;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes("rs-account:" + playerId));
                var sb = new StringBuilder(64);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// Platform store (Google Play through Unity IAP, or the fake store). Flow (GameSession.Purchase):
    /// PurchaseStart (rules pending record) → <see cref="Purchase"/> → POST /v1/iap/verify → <see cref="Finish"/> only after the
    /// server granted the product. Purchases that arrive later (deferred payments, restarts, restores) are raised through
    /// <see cref="PurchaseDelivered"/> and go through the same verification.
    /// </summary>
    public interface IStoreGateway
    {
        string Name { get; }
        bool IsReady { get; }
        /// <summary>Connects to the store and registers the catalogue. <paramref name="obfuscatedAccountId"/> binds purchases to the player.</summary>
        void Initialize(IList<StoreProductInfo> products, string obfuscatedAccountId, Action<ServiceResult> done);
        void Purchase(string productId, string storeProductId, Action<ServiceResult<StorePurchase>> done);
        /// <summary>Confirms (consume/acknowledge) after the server granted the product.</summary>
        void Finish(StorePurchase purchase);
        /// <summary>Asks the store for owned/unfinished purchases (non-consumable restore); results arrive via <see cref="PurchaseDelivered"/>.</summary>
        void Restore();
        event Action<StorePurchase> PurchaseDelivered;
    }

    /// <summary>Completes every purchase instantly with a "fake-" token (accepted by the server's fake IAP mode). Editor/offline/tests.</summary>
    public sealed class FakeStoreGateway : IStoreGateway
    {
        public string Name => "fake";
        public bool IsReady { get; private set; } = true;
        public bool FailNext;
        public bool DeferNext;
        public readonly List<StorePurchase> Finished = new List<StorePurchase>();
        public readonly List<StorePurchase> Unfinished = new List<StorePurchase>();
        public event Action<StorePurchase> PurchaseDelivered;

        public void Initialize(IList<StoreProductInfo> products, string obfuscatedAccountId, Action<ServiceResult> done)
        {
            IsReady = true;
            done?.Invoke(ServiceResult.Success());
        }

        public void Purchase(string productId, string storeProductId, Action<ServiceResult<StorePurchase>> done)
        {
            if (FailNext) { FailNext = false; done(ServiceResult<StorePurchase>.Fail(StoreErrors.Cancelled)); return; }
            var p = NewPurchase(productId, storeProductId);
            Unfinished.Add(p);
            if (DeferNext) { DeferNext = false; done(ServiceResult<StorePurchase>.Fail(StoreErrors.Deferred)); return; }
            done(ServiceResult<StorePurchase>.Success(p));
        }

        /// <summary>Test helper: the store delivers a purchase on its own (deferred payment approved, restart).</summary>
        public void Deliver(StorePurchase p)
        {
            p.Delivered = true;
            PurchaseDelivered?.Invoke(p);
        }

        public void Finish(StorePurchase purchase)
        {
            Unfinished.RemoveAll(x => x.PurchaseToken == purchase.PurchaseToken);
            Finished.Add(purchase);
        }

        public void Restore()
        {
            foreach (var p in Unfinished.ToArray()) Deliver(p);
        }

        public static StorePurchase NewPurchase(string productId, string storeProductId)
        {
            string id = Guid.NewGuid().ToString("N");
            return new StorePurchase { ProductId = productId, StoreProductId = storeProductId, PurchaseToken = "fake-" + id, OrderId = "fake-order-" + id };
        }
    }

    /// <summary>
    /// Rewarded ads. The returned value is the ad token the rules command carries (AdWatched/RunRevive/...). Offline any
    /// token works; online the server only accepts AppLovin S2S-verified event ids (see <c>ServerVerifiedAdGateway</c>).
    /// </summary>
    public interface IAdGateway
    {
        string Name { get; }
        void Show(string placementId, Action<ServiceResult<string>> done);
    }

    public sealed class FakeAdGateway : IAdGateway
    {
        public string Name => "fake";
        public bool FailNext;
        public readonly List<string> Shown = new List<string>();

        public void Show(string placementId, Action<ServiceResult<string>> done)
        {
            if (FailNext) { FailNext = false; done(ServiceResult<string>.Fail("ad_cancelled")); return; }
            Shown.Add(placementId);
            done(ServiceResult<string>.Success("fake-ad-" + Guid.NewGuid().ToString("N")));
        }
    }

    /// <summary>
    /// Ads-disabled builds: no ad SDK at all. Never asked in practice (the rules treat everyone as a No Ads owner, so
    /// <c>ServerVerifiedAdGateway</c> completes placements without an ad); if a server with ads switched on asks anyway, the
    /// placement fails with <c>ads_unavailable</c>.
    /// </summary>
    public sealed class NoAdGateway : IAdGateway
    {
        public string Name => "none";
        public void Show(string placementId, Action<ServiceResult<string>> done) => done(ServiceResult<string>.Fail("ads_unavailable"));
    }

    /// <summary>A verified rewarded-ad view the player can spend (GET /v1/ads/credits).</summary>
    [Serializable]
    public sealed class AdCreditInfo
    {
        public string EventId;
        public string Placement;
        public long AtMs;
    }

    /// <summary>Server endpoints for rewarded ads (online only).</summary>
    public interface IAdRewardService
    {
        string PlayerId { get; }
        void AdCredits(Action<ServiceResult<List<AdCreditInfo>>> done);
        /// <summary>
        /// POST /v1/ads/instant: grants a direct-reward placement without an ad. The server's rules accept it only for No Ads
        /// owners and in the global ads-disabled mode.
        /// </summary>
        void InstantAd(string placementId, Action<CommandResult> done);
        /// <summary>Dev only: plays AppLovin's S2S callback against a server in fake ads mode (fake ad gateway online).</summary>
        void SimulateAdCallback(string placementId, string eventId, Action<ServiceResult> done);
    }
}
