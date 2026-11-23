using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

namespace RogueStars.Client.Services
{
    /// <summary>
    /// Google Play Billing through Unity IAP 5 (<c>com.unity.purchasing</c>, StoreController API).
    /// <list type="bullet">
    /// <item>Catalogue: rules products with a store SKU (<see cref="StoreCatalog"/>): definition id = rules product id,
    /// store-specific id = SKU, Consumable / NonConsumable (no subscriptions: the monthly pass is a 30-day consumable).</item>
    /// <item><c>SetObfuscatedAccountId</c> = hash of the player id; the server rejects receipts of other accounts.</item>
    /// <item>A purchase becomes a <see cref="PendingOrder"/> (<c>OnPurchasePending</c>); it is confirmed with
    /// <c>ConfirmPurchase</c> only after /v1/iap/verify succeeded (<see cref="Finish"/>). The server already consumed /
    /// acknowledged it at Google, so the client confirmation only clears Unity IAP's queue (a failure there is harmless).</item>
    /// <item>Deferred payments (<c>OnPurchaseDeferred</c>) report <see cref="StoreErrors.Deferred"/>; when Google completes them
    /// they arrive as pending orders and are raised through <see cref="PurchaseDelivered"/>, as are unconfirmed orders
    /// after an app restart (<c>ProcessPendingOrdersOnPurchasesFetched</c>) and owned non-consumables (restore).</item>
    /// </list>
    /// </summary>
    public sealed class UnityIapStoreGateway : IStoreGateway
    {
        private StoreController _c;
        private readonly Dictionary<string, Action<ServiceResult<StorePurchase>>> _waiting = new Dictionary<string, Action<ServiceResult<StorePurchase>>>();
        private readonly Dictionary<string, bool> _consumable = new Dictionary<string, bool>();
        private Action<ServiceResult> _initDone;

        public string Name => "unity_iap";
        public bool IsReady { get; private set; }
        public event Action<StorePurchase> PurchaseDelivered;

        public async void Initialize(IList<StoreProductInfo> products, string obfuscatedAccountId, Action<ServiceResult> done)
        {
            if (_c == null)
            {
                _c = UnityIAPServices.StoreController();
                _c.OnStoreConnected += () => Debug.Log("[IAP] store connected");
                _c.OnStoreDisconnected += d => { IsReady = false; Debug.LogWarning("[IAP] store disconnected: " + d.message); };
                _c.OnProductsFetched += OnProductsFetched;
                _c.OnProductsFetchFailed += OnProductsFetchFailed;
                _c.OnPurchasePending += OnPurchasePending;
                _c.OnPurchaseDeferred += OnPurchaseDeferred;
                _c.OnPurchaseFailed += OnPurchaseFailed;
                _c.OnPurchaseConfirmed += o => Debug.Log("[IAP] confirmed " + FirstProductId(o) + " (" + o.GetType().Name + ")");
                _c.OnPurchasesFetched += OnPurchasesFetched;
                _c.OnPurchasesFetchFailed += f => Debug.LogWarning("[IAP] fetch purchases failed: " + f.message);
                _c.ProcessPendingOrdersOnPurchasesFetched(true);
            }
            _initDone = done;
            try
            {
                await _c.Connect();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[IAP] connect failed: " + ex.Message);
                Complete(ServiceResult.Fail(StoreErrors.NotReady));
                return;
            }
            if (!string.IsNullOrEmpty(obfuscatedAccountId)) _c.GooglePlayStoreExtendedService?.SetObfuscatedAccountId(obfuscatedAccountId);
            var defs = new List<ProductDefinition>();
            foreach (var p in products)
            {
                _consumable[p.ProductId] = p.Consumable;
                defs.Add(new ProductDefinition(p.ProductId, p.StoreProductId, p.Consumable ? ProductType.Consumable : ProductType.NonConsumable));
            }
            _c.FetchProducts(defs);
        }

        private void Complete(ServiceResult r)
        {
            var cb = _initDone;
            _initDone = null;
            cb?.Invoke(r);
        }

        private void OnProductsFetched(List<Product> products)
        {
            IsReady = products.Count > 0;
            Debug.Log("[IAP] products fetched: " + products.Count);
            Complete(IsReady ? ServiceResult.Success() : ServiceResult.Fail(StoreErrors.NotReady));
        }

        private void OnProductsFetchFailed(ProductFetchFailed f)
        {
            Debug.LogWarning("[IAP] " + f.FailedFetchProducts.Count + " products unavailable: " + f.FailureReason);
            IsReady = _c.GetProducts().Count > 0;
            Complete(IsReady ? ServiceResult.Success() : ServiceResult.Fail(StoreErrors.NotReady));
        }

        public void Purchase(string productId, string storeProductId, Action<ServiceResult<StorePurchase>> done)
        {
            if (!IsReady || _c == null || _c.GetProductById(productId) == null) { done(ServiceResult<StorePurchase>.Fail(StoreErrors.NotReady)); return; }
            if (_waiting.ContainsKey(productId)) { done(ServiceResult<StorePurchase>.Fail("purchase_in_progress")); return; }
            _waiting[productId] = done;
            _c.PurchaseProduct(productId);
        }

        private void OnPurchasePending(PendingOrder order)
        {
            var p = ToPurchase(order);
            p.Handle = order;
            if (_waiting.TryGetValue(p.ProductId, out var cb)) { _waiting.Remove(p.ProductId); cb(ServiceResult<StorePurchase>.Success(p)); return; }
            p.Delivered = true;
            PurchaseDelivered?.Invoke(p);
        }

        private void OnPurchaseDeferred(DeferredOrder order)
        {
            string id = FirstProductId(order);
            if (id != null && _waiting.TryGetValue(id, out var cb)) { _waiting.Remove(id); cb(ServiceResult<StorePurchase>.Fail(StoreErrors.Deferred)); }
        }

        private void OnPurchaseFailed(FailedOrder order)
        {
            string id = FirstProductId(order);
            Debug.LogWarning("[IAP] purchase failed " + id + ": " + order.FailureReason + " " + order.Details);
            if (id == null || !_waiting.TryGetValue(id, out var cb)) return;
            _waiting.Remove(id);
            cb(ServiceResult<StorePurchase>.Fail(order.FailureReason == PurchaseFailureReason.UserCancelled ? StoreErrors.Cancelled : "purchase_failed_" + order.FailureReason));
        }

        private void OnPurchasesFetched(Orders orders)
        {
            // Pending orders are re-raised through OnPurchasePending (ProcessPendingOrdersOnPurchasesFetched).
            foreach (var o in orders.ConfirmedOrders)
            {
                var p = ToPurchase(o);
                if (p.ProductId == null || !_consumable.TryGetValue(p.ProductId, out bool consumable) || consumable) continue;
                p.Delivered = true;
                p.Confirmed = true;
                PurchaseDelivered?.Invoke(p);
            }
        }

        public void Finish(StorePurchase purchase)
        {
            if (purchase.Handle is PendingOrder order) _c?.ConfirmPurchase(order);
        }

        public void Restore() => _c?.FetchPurchases();

        private static StorePurchase ToPurchase(Order order)
        {
            var product = FirstProduct(order);
            return new StorePurchase
            {
                ProductId = product?.definition?.id,
                StoreProductId = product?.definition?.storeSpecificId,
                PurchaseToken = order.Info?.TransactionID, // Google Play: the purchase token
                OrderId = order.Info?.Google?.OrderId,
            };
        }

        private static Product FirstProduct(Order order)
        {
            var items = order?.CartOrdered?.Items();
            return items != null && items.Count > 0 ? items[0].Product : null;
        }

        private static string FirstProductId(Order order) => FirstProduct(order)?.definition?.id;
    }
}
