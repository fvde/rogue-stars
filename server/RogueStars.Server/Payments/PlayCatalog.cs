using System.Globalization;
using System.Text;
using RogueStars.Rules.Content;

namespace RogueStars.Server.Payments;

/// <summary>Google Play product type of a rules product.</summary>
public enum StoreProductType { Consumable, NonConsumable }

/// <summary>
/// Maps the rules shop catalogue (<see cref="ContentDb.Products"/>) to Google Play one-time products.
/// <list type="bullet">
/// <item>Lifetime limit 1 (starter packs, No Ads, Daily Diamonds Forever, funds) → <b>non-consumable</b> (acknowledged,
/// restorable, owned forever).</item>
/// <item>Everything else → <b>consumable</b> (consumed after the grant so it can be bought again): Diamond packs, rolling and
/// flash offers, gift packs, the season pass premium (once per season) and the <b>monthly pass</b>. The monthly pass is
/// deliberately a consumable 30-day block, not a Play subscription: the rules grant 30 daily claims per purchase and
/// stack repeat purchases (<c>ShopSystem.GrantProduct</c>), there is no auto-renewal state in the rules, and a
/// subscription would need RTDN/renewal handling the rules do not model.</item>
/// </list>
/// Free rolling steps (price 0) have no store product.
/// </summary>
public static class PlayCatalog
{
    public const string SetInitial = "initial", SetAll = "all";

    /// <summary>
    /// The first products created in Play Console for verification (docs/play-console-products-initial.csv): one consumable
    /// Diamond pack, the Daily Diamonds non-consumable, the monthly pass (30-day consumable) and a starter pack. Must equal
    /// the client's <c>StoreCatalog.InitialProductIds</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> InitialProductIds = new[] { "diamonds_60", "starter_worm", "monthly_pass", "perm_daily_diamonds" };

    public static StoreProductType TypeOf(ProductDef p) => p.Limit == 1 ? StoreProductType.NonConsumable : StoreProductType.Consumable;

    public static IEnumerable<ProductDef> StoreProducts(ContentDb content) =>
        content.Products.Values.Where(p => p.PriceUsdCents > 0 && !string.IsNullOrEmpty(p.StoreProductId))
            .OrderBy(p => p.Kind).ThenBy(p => p.PriceUsdCents).ThenBy(p => p.Id, StringComparer.Ordinal);

    /// <summary>Store products sold under <c>Iap:ProductSet</c> ("all", else the initial set).</summary>
    public static IEnumerable<ProductDef> Offered(ContentDb content, string? productSet) =>
        StoreProducts(content).Where(p => IsOffered(p.Id, productSet));

    public static bool IsOffered(string productId, string? productSet) =>
        string.Equals(productSet, SetAll, StringComparison.OrdinalIgnoreCase) || InitialProductIds.Contains(productId);

    /// <summary>
    /// CSV for docs/play-console-products.csv (all) and docs/play-console-products-initial.csv (initial set); regenerate with
    /// <c>UPDATE_PLAY_CATALOG=1 dotnet test</c>.
    /// </summary>
    public static string Csv(ContentDb content, string productSet = SetAll)
    {
        var sb = new StringBuilder();
        sb.Append("product_id,type,price_usd,title,description,rules_product_id,rules_kind\n");
        foreach (var p in Offered(content, productSet))
        {
            string type = TypeOf(p) == StoreProductType.Consumable ? "consumable" : "non_consumable";
            string price = (p.PriceUsdCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            sb.Append(string.Join(",", p.StoreProductId, type, price, Esc(p.Name), Esc(Describe(p)), p.Id, p.Kind)).Append('\n');
        }
        return sb.ToString();
    }

    private static string Describe(ProductDef p) => p.Kind switch
    {
        ProductKind.DiamondPack => p.Name + " (double on first purchase)",
        ProductKind.MonthlyPass => "30 days of daily Diamonds, battle speed x3 and bonuses",
        ProductKind.SeasonPass => "Premium rewards of the current season",
        ProductKind.Fund => "Diamond rewards for reaching milestones",
        ProductKind.GiftPack => "Instant rewards plus daily gifts for " + p.DailyDays + " days",
        _ => p.Name,
    };

    private static string Esc(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
