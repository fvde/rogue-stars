using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Parts: the same gear inventory as the Worm tab without the worm (every owned part, filters, sort, detail, merge
    /// picker, Replicator, awakening: <see cref="GearScreen"/>) plus the rune inventory tab. Opened by the Worm's Runes
    /// button, the Forge unlock and deep links.
    /// </summary>
    public sealed class PartsScreen : GearScreen
    {
        private static readonly string[] Tabs = { "Parts", "Runes" };

        /// <summary>0 = parts, 1 = runes.</summary>
        public int Tab;

        public override string Title => "Parts";

        public override string Subtitle =>
            "Ore " + Fmt.Num(State.Wallet.Get(Currencies.Ore)) + " · Gear Cores " + Fmt.Num(State.Wallet.Get(Currencies.GearCores))
            + " · Awaken Cores " + State.Wallet.Get(ProgressionCurrencies.PartAwakenCores) + "/" + Content.PartsConfig.AwakenCost;

        protected override void Build(RectTransform body)
        {
            Ui.TabBar(body, Tabs, Tab, i => { Tab = i; Rebuild(); }, 76);
            if (Tab == 1) { Runes(body); return; }
            BuildInventory(body, "All parts");
        }
    }
}
