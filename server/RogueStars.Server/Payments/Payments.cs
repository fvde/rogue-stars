using RogueStars.Rules.Core;
using RogueStars.Server.Commands;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Payments;

public sealed record IapVerifyRequest(string? ProductId, string? PurchaseToken);

/// <summary><c>iap_receipts/{sha256(purchaseToken)}</c>: a token is redeemed exactly once, by exactly one player.</summary>
public sealed class ReceiptClaim
{
    public string PlayerId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string? OrderId { get; set; }
    public long AtMs { get; set; }
    public bool Completed { get; set; }
    /// <summary>Consumed/acknowledged at Google after the grant.</summary>
    public bool Finalized { get; set; }
    public bool Test { get; set; }
}

/// <summary><c>ad_events/{sha256(eventId)}</c>: one claim per AppLovin event id (replays and retries).</summary>
public sealed class AdEventClaim
{
    public string PlayerId { get; set; } = "";
    public string? Placement { get; set; }
    public long AtMs { get; set; }
    /// <summary>Processing finished (granted, credited or rejected by the rules); repeats are duplicates.</summary>
    public bool Done { get; set; }
    public string? Outcome { get; set; }
}

public static class PaymentEndpoints
{
    public static void Map(RouteGroupBuilder v1, WebApplication app)
    {
        v1.MapGet("/iap/config", (HttpContext http, GameEngine engine) => Results.Ok(new
        {
            ok = true,
            obfuscatedAccountId = GooglePlayReceiptValidator.ObfuscatedAccountId(http.PlayerId()),
            products = PlayCatalog.StoreProducts(engine.Content).Select(p => new
            {
                productId = p.Id, storeProductId = p.StoreProductId, type = PlayCatalog.TypeOf(p) == StoreProductType.Consumable ? "consumable" : "non_consumable",
            }),
        }));

        v1.MapPost("/iap/verify", async (HttpContext http, IapVerifyRequest req, ServerHooks hooks, IReceiptValidator validator,
            IDocStore docs, GameEngine engine, Clock clock, ILoggerFactory lf, CancellationToken ct) =>
        {
            var log = lf.CreateLogger("Iap");
            string playerId = http.PlayerId();
            if (!hooks.Has(hooks.Names.PurchaseComplete)) throw new ApiException(StatusCodes.Status501NotImplemented, "purchases_not_available");
            if (string.IsNullOrWhiteSpace(req.ProductId) || string.IsNullOrWhiteSpace(req.PurchaseToken)) throw ApiException.BadRequest("missing_fields");
            if (req.PurchaseToken.Length > 4096) throw ApiException.BadRequest("invalid_token");
            var def = engine.Content.Product(req.ProductId);
            if (def == null || def.PriceUsdCents <= 0 || string.IsNullOrEmpty(def.StoreProductId)) throw ApiException.BadRequest("unknown_product");
            var rr = new ReceiptRequest(playerId, def.Id, def.StoreProductId, PlayCatalog.TypeOf(def), req.PurchaseToken);

            string key = Ids.Hash(req.PurchaseToken);
            var existing = await docs.GetAsync<ReceiptClaim>(Collections.IapReceipts, key, ct);
            if (existing != null)
            {
                if (existing.Value.PlayerId != playerId) throw ApiException.Conflict("receipt_already_used");
                if (existing.Value.Completed) return await AlreadyCompleted(existing.Value);
            }

            var result = await validator.ValidateAsync(rr, ct);
            if (!result.Valid)
            {
                log.LogInformation("Receipt for {ProductId} by {PlayerId} rejected: {Error}", def.Id, playerId, result.Error);
                throw new ApiException(result.Error == "store_unavailable" ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status402PaymentRequired,
                    result.Error ?? "invalid_receipt");
            }
            // Consumed/acknowledged at Google but never granted by us: not redeemable here.
            if (result.AlreadyFinalized && existing == null) throw new ApiException(StatusCodes.Status402PaymentRequired, "already_consumed");

            var claim = new ReceiptClaim { PlayerId = playerId, ProductId = def.Id, OrderId = result.OrderId, AtMs = clock.NowMs, Test = result.Test };
            if (existing == null && !await docs.TryPutAsync(Collections.IapReceipts, key, claim, 0, ct))
            {
                var raced = await docs.GetAsync<ReceiptClaim>(Collections.IapReceipts, key, ct);
                if (raced?.Value.PlayerId != playerId) throw ApiException.Conflict("receipt_already_used");
                if (raced.Value.Completed) return await AlreadyCompleted(raced.Value);
            }

            // Promo purchases have no order id; the token hash is the stable receipt id then.
            string receiptId = result.OrderId ?? "gp:" + key[..40];
            var response = await hooks.TryExecuteAsync(playerId, hooks.Names.PurchaseComplete, new
            {
                ProductId = def.Id, ReceiptId = receiptId, OrderId = result.OrderId, PurchaseToken = req.PurchaseToken, Store = "google_play", Test = result.Test,
            }, "iap-" + key[..40], ct);
            if (response is { Ok: true })
            {
                bool finalized = await validator.FinalizeAsync(rr, ct);
                await docs.MutateAsync<ReceiptClaim>(Collections.IapReceipts, key, c =>
                {
                    if (c == null || (c.Completed && c.Finalized == finalized)) return null;
                    c.Completed = true;
                    c.Finalized |= finalized;
                    return c;
                }, ct);
            }
            log.LogInformation("Purchase {ProductId} order {OrderId} for {PlayerId}: {Ok} {Error} (test {Test})",
                def.Id, result.OrderId, playerId, response?.Ok, response?.Error, result.Test);
            return Results.Json(response, StateJson.Wire);

            async Task<IResult> AlreadyCompleted(ReceiptClaim c)
            {
                if (!c.Finalized && await validator.FinalizeAsync(rr, ct))
                {
                    await docs.MutateAsync<ReceiptClaim>(Collections.IapReceipts, key, x =>
                    {
                        if (x == null || x.Finalized) return null;
                        x.Finalized = true;
                        return x;
                    }, ct);
                }
                return Results.Json(new { ok = true, alreadyCompleted = true, orderId = c.OrderId }, StateJson.Wire);
            }
        });

        v1.MapGet("/ads/credits", async (HttpContext http, AdCreditService credits, CancellationToken ct) =>
        {
            var list = await credits.UnspentAsync(http.PlayerId(), ct);
            return Results.Ok(new { ok = true, credits = list.Select(c => new { eventId = c.EventId, placement = c.Placement, atMs = c.AtMs }) });
        });

        // AppLovin calls the S2S callback with GET (5 s timeout, up to two retries); POST is accepted as well.
        app.MapMethods("/v1/ads/ssv", new[] { "GET", "POST" }, async (HttpContext http, IAdVerifier verifier, ServerHooks hooks,
            AdCreditService credits, GameEngine engine, IDocStore docs, Clock clock, ILoggerFactory lf, CancellationToken ct) =>
        {
            var log = lf.CreateLogger("Ads");
            var verified = verifier.Verify(http.Request.Query);
            switch (verified.Status)
            {
                case AdVerifyStatus.Malformed: throw ApiException.BadRequest("malformed_callback");
                case AdVerifyStatus.InvalidSignature: throw new ApiException(StatusCodes.Status401Unauthorized, "invalid_signature");
                case AdVerifyStatus.AdUnitNotAllowed: throw ApiException.Forbidden("ad_unit_not_allowed");
            }
            var ev = verified.Event!;
            if (!hooks.Has(hooks.Names.AdWatched)) throw new ApiException(StatusCodes.Status501NotImplemented, "ads_not_available");

            // Claim the event id first (first writer wins); unfinished claims of the same player are re-processed,
            // which is safe because the grant and the credit are idempotent per event id.
            string key = Ids.Hash(ev.EventId);
            var claim = new AdEventClaim { PlayerId = ev.PlayerId, Placement = ev.Placement, AtMs = clock.NowMs };
            if (!await docs.TryPutAsync(Collections.AdEvents, key, claim, 0, ct))
            {
                var existing = await docs.GetAsync<AdEventClaim>(Collections.AdEvents, key, ct);
                if (existing == null || existing.Value.PlayerId != ev.PlayerId) throw ApiException.Conflict("event_already_used");
                if (existing.Value.Done) return Results.Ok(new { ok = true, duplicate = true });
            }

            var placement = engine.Content.AdPlacement(ev.Placement);
            string outcome;
            CommandResponse? response = null;
            if (placement == null) outcome = "unknown_placement";
            else if (placement.Rewards.Count > 0)
            {
                response = await hooks.TryExecuteAsync(ev.PlayerId, hooks.Names.AdWatched, new
                {
                    PlacementId = placement.Id, AdToken = ev.EventId, Placement = placement.Id, EventId = ev.EventId, AdUnitId = ev.AdUnitId,
                }, "ad-" + key[..32], ct);
                outcome = response is { Ok: true } ? "granted" : "rejected:" + response?.Error;
            }
            else
            {
                await credits.AddAsync(ev.PlayerId, ev.EventId, placement.Id, ct);
                outcome = "credited";
            }

            await docs.MutateAsync<AdEventClaim>(Collections.AdEvents, key, c =>
            {
                if (c == null || c.Done) return null;
                c.Done = true;
                c.Outcome = outcome;
                return c;
            }, ct);
            log.LogInformation("Ad event {EventId} for {PlayerId} ({Placement}): {Outcome}", ev.EventId, ev.PlayerId, ev.Placement, outcome);
            bool ok = outcome is "granted" or "credited";
            return Results.Ok(new { ok, outcome, error = ok ? null : response?.Error ?? outcome });
        });
    }
}
