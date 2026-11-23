using RogueStars.Rules.Content;

namespace RogueStars.Server.Infrastructure;

/// <summary>The rules content the server runs with, including host-level switches from the configuration.</summary>
public static class ServerContent
{
    /// <summary>
    /// <c>Ads:Mode=disabled</c> → a private <see cref="ContentDb"/> with <c>Shop.AdsDisabled</c> (every player is treated as a
    /// No Ads owner by the rules; the shared <see cref="ContentDb.Default"/> is never mutated). Otherwise the default content.
    /// </summary>
    public static ContentDb Create(ServerOptions options)
    {
        if (!options.Ads.IsDisabled) return ContentDb.Default;
        var content = new ContentDb();
        content.Shop.AdsDisabled = true;
        return content;
    }
}
