using System;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Client starts an IAP: validates availability and records a pending purchase. Payload: store product id.</summary>
    [Serializable]
    public sealed class PurchaseStart : GameCommand { public string ProductId; }

    /// <summary>
    /// Server-only: the server validated the store receipt and grants the product. Offline/dev clients may
    /// execute it locally only with a "dev:" receipt when content Shop.AllowDevReceipts is true.
    /// </summary>
    [Serializable, ServerOnly]
    public sealed class PurchaseComplete : GameCommand { public string ProductId; public string ReceiptId; }

    /// <summary>Claim a free (price 0) step of a rolling offer chain.</summary>
    [Serializable]
    public sealed class ShopClaimFree : GameCommand { public string ProductId; }

    /// <summary>Claim today's reward of a daily entitlement (gift pack, monthly pass, Daily Diamonds).</summary>
    [Serializable]
    public sealed class ShopClaimDaily : GameCommand { public string ProductId; }

    /// <summary>Buy a soft-currency shop item (Diamond shop; guild items use GuildShopBuy).</summary>
    [Serializable]
    public sealed class ShopBuyItem : GameCommand { public string ItemId; }

    /// <summary>A rewarded ad finished for a placement with direct rewards. AdToken is server-verified (later); not needed with No Ads.</summary>
    [Serializable]
    public sealed class AdWatched : GameCommand { public string PlacementId; public string AdToken; }

    /// <summary>Claim a season pass level reward (free or premium track).</summary>
    [Serializable]
    public sealed class SeasonPassClaim : GameCommand { public int Level; public bool Premium; }

    /// <summary>Claim every reached, unclaimed season pass level (premium too when owned).</summary>
    [Serializable]
    public sealed class SeasonPassClaimAll : GameCommand { }

    /// <summary>Claim a reached step of an owned fund.</summary>
    [Serializable]
    public sealed class FundClaim : GameCommand { public string FundId; public int Step; }
}
