using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// The Shop (owner, 2026-11-27): the one screen for pulls and purchases, the hub's "Shop" tab. Two rows of section chips in
    /// the footer: the pull sections Aliens / Blueprints / Parts first (ShopScreen.Pulls.cs, each once its pull feature is
    /// unlocked), then the offer sections, there from the start (no unlock, owner 2026-11-27): Offers, Diamonds, Passes, Items, Ads (catalogue,
    /// offers, Diamond items and rewarded ads). Real-money products go through GameSession.Purchase (IStoreGateway → dev receipt
    /// offline / /v1/iap/verify online). The "Passes" chip opens the Passes page (<see cref="PassesScreen"/>: daily gift,
    /// Monthly Pass, the Forever products, season pass, funds) instead of switching the section.
    /// </summary>
    public sealed partial class ShopScreen : UiScreen
    {
        private static readonly string[] Tabs = { "Offers", "Diamonds", "Passes", "Items", "Ads" };
        /// <summary>Index of the "Offers" chip (the hub's Offer icon opens it).</summary>
        public const int OffersTab = 0;
        /// <summary>Index of the "Passes" chip: opens the Passes page.</summary>
        public const int PassesTab = 2;
        /// <summary>The open section: a pull type, or (null) the offer chip <see cref="_tab"/>.</summary>
        private GachaType? _pull;
        private int _tab;
        /// <summary>False until a section was chosen (the first unlocked section then opens).</summary>
        private bool _chosen;

        public ShopScreen() { }

        /// <summary>Opens on the pull section of <paramref name="type"/> (the Aliens screen's Shop chip).</summary>
        public ShopScreen(GachaType type) { _pull = type; _chosen = true; }

        /// <summary>Opens on an offer section (<see cref="OffersTab"/>: the hub's Offer icon).</summary>
        public ShopScreen(int offerTab) { _tab = offerTab; _chosen = true; }

        public override string Title => "Shop";
        // the pulls come with Parts / Aliens / the City; the offers need no unlock (no intro)
        public override string[] IntroFeatures => new[] { Features.Parts, Features.Aliens, Features.BiomassFarm };

        /// <summary>The section chips sit at the bottom (kit chip tabs in the footer, pulls above offers), the back button in the header.</summary>
        public override float FooterHeight => HasPullRow ? 124 + 84 + FooterSpacing : 124;

        private GameState Loaded => App?.Session?.State;
        private bool HasPullRow => Loaded != null && PullSections(Loaded, Content).Count > 0;

        /// <summary>The open section's name ("Aliens", "Offers"): tests and screenshots.</summary>
        public string Section => _pull.HasValue ? PullSectionName(_pull.Value) : Tabs[_tab];

        protected override void Build(RectTransform body)
        {
            var pulls = PullSections(State, Content);
            // an unavailable or not yet chosen section falls back to the first one there is (the first pull section, else Offers)
            if (_pull.HasValue && !pulls.Contains(_pull.Value)) { _pull = null; _chosen = false; }
            if (!_chosen) { _pull = pulls.Count > 0 ? (GachaType?)pulls[0] : null; _tab = OffersTab; _chosen = true; }

            if (Footer != null)
            {
                if (pulls.Count > 0)
                {
                    var names = new List<string>();
                    var free = new List<bool>();
                    foreach (var t in pulls) { names.Add(PullSectionName(t)); free.Add(State.Gacha.FreePulls(t) > 0); } // a free 10× waits there
                    Kit.TabChips(Footer, names, _pull.HasValue ? pulls.IndexOf(_pull.Value) : -1, i => { _pull = pulls[i]; Rebuild(); }, 84, free)
                        .Buttons[0].transform.parent.name = "PullSections";
                }
                // the offer sections: always there (no unlock)
                var dots = new bool[Tabs.Length];
                dots[PassesTab] = ShopSystem.PassClaimables(State, Content, Now).Count > 0; // something to claim on the Passes page
                Kit.TabChips(Footer, Tabs, _pull.HasValue ? -1 : _tab, i =>
                {
                    if (i == PassesTab) { App.Router.Push(new PassesScreen()); return; }
                    _pull = null; _tab = i; Rebuild();
                }, 84, dots).Buttons[0].transform.parent.name = "OfferSections";
            }
            if (_pull.HasValue) { BuildPulls(body, _pull.Value); return; }
            // G7: store / offline hints are for developers only
            if (Debug.isDebugBuild) Note(body, !Session.IsOnline ? "Offline mode: purchases complete instantly with dev receipts." : "Store: " + Session.Store.Name + ".").name = "DevHint";
            switch (_tab)
            {
                case 0:
                    _featuredId = null;
                    Featured(body);
                    Products(body, ProductKind.StarterPack);
                    Rolling(body);
                    Products(body, ProductKind.FlashOffer);
                    Products(body, ProductKind.GiftPack);
                    break;
                case 1: Products(body, ProductKind.DiamondPack); break;
                case 3: Items(body); break;
                case 4: Ads(body); break;
            }
        }

        private List<ProductDef> OfKind(ProductKind kind)
        {
            var list = new List<ProductDef>();
            foreach (var p in Content.Products.Values) if (p.Kind == kind) list.Add(p);
            list.Sort((a, b) => a.PriceUsdCents != b.PriceUsdCents ? a.PriceUsdCents.CompareTo(b.PriceUsdCents) : string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        private void Products(RectTransform body, ProductKind kind)
        {
            var list = OfKind(kind);
            if (list.Count == 0) return;
            list.RemoveAll(p => !Usable(p) || !Active(p) || (_tab == OffersTab && p.Id == _featuredId));
            if (list.Count == 0) return;
            Kit.Ribbon(body, SectionTitle(kind)).name = "Section:" + kind;
            ProductGrid(body, list);
        }

        /// <summary>Offers two to a row (equal cards); a lone last offer takes the full width.</summary>
        private void ProductGrid(RectTransform body, List<ProductDef> list)
        {
            for (int i = 0; i < list.Count; i += 2)
            {
                if (i + 1 >= list.Count && list.Count % 2 == 1 && list.Count == 1) { ProductCard(body, list[i], true); continue; }
                var row = Ui.Row(body, 0, 14, null, 0, "OfferRow");
                ProductCard(row, list[i], false);
                if (i + 1 < list.Count) ProductCard(row, list[i + 1], false);
                else Ui.Size(Ui.Spacer(row), 450, -1, 1);
            }
        }

        /// <summary>Card colour of an offer by its price tier (the rarity colours: free grey, up to $5 green, $20 blue, $50 purple, above red).</summary>
        public static Rarity TierOf(ProductDef p) =>
            p.PriceUsdCents <= 0 ? Rarity.Common : p.PriceUsdCents < 500 ? Rarity.Rare : p.PriceUsdCents < 2000 ? Rarity.Epic : p.PriceUsdCents < 5000 ? Rarity.Legendary : Rarity.Mythic;

        /// <summary>
        /// Rewards follow use: an offer is only shown when its headline (first) reward has a use now; offers for locked
        /// features stay hidden (bought anyway, their contents would wait in the reward stash, kept in full).
        /// </summary>
        private bool Usable(ProductDef p) => Usable(State, Content, p, Now);

        public static bool Usable(GameState state, ContentDb content, ProductDef p, long now)
        {
            var list = p.Contents.Count > 0 ? p.Contents : p.DailyRewards.Count > 0 ? p.DailyRewards : p.FirstPurchaseBonus;
            return list.Count == 0 || RewardGate.HasUse(state, content, list[0], now);
        }

        /// <summary>MS1: offers outside their schedule are hidden instead of listed as "offer not active".</summary>
        private bool Active(ProductDef p) => p.Schedule == null || p.Schedule.TryActive(Now, out _, out _);

        /// <summary>Title of a shop item section: the "tokens" section holds the pull currencies (Crates, Signals, Schematics), never "Tokens".</summary>
        public static string ItemSectionTitle(string section) => section == "tokens" ? "Pull Currencies" : Fmt.Humanize(section);

        /// <summary>Player-facing section titles ("Starter Packs", "Flash Offers"), never the code ids.</summary>
        public static string SectionTitle(ProductKind kind)
        {
            switch (kind)
            {
                case ProductKind.StarterPack: return "Starter Packs";
                case ProductKind.FlashOffer: return "Flash Offers";
                case ProductKind.GiftPack: return "Gift Packs";
                case ProductKind.DiamondPack: return "Diamond Packs";
                case ProductKind.MonthlyPass: return "Monthly Pass";
                case ProductKind.Permanent: return "Forever";
                case ProductKind.RollingOffer: return "Special Offer";
                default: return Fmt.Words(kind.ToString());
            }
        }

        /// <summary>The pack art of an offer (placeholder icons by kind until dedicated pack art exists).</summary>
        private static string PackIcon(ProductDef p)
        {
            switch (p.Kind)
            {
                case ProductKind.DiamondPack: return "diamond";
                case ProductKind.MonthlyPass: case ProductKind.SeasonPass: return "pass";
                case ProductKind.Permanent: return "star";
                case ProductKind.FlashOffer: return "luck_gem";
                default: return "packs";
            }
        }

        /// <summary>Usable rewards of an offer, and a hint that the rest arrives with later features (never named).</summary>
        private string OfferRewards(List<RogueStars.Rules.Core.Reward> rewards)
        {
            string text = Fmt.Rewards(rewards, Content, State, Now);
            return RewardGate.Usable(State, Content, rewards, Now).Count < rewards.Count ? text + " + more for later features" : text;
        }

        private void Rolling(RectTransform body)
        {
            foreach (var chain in Content.RollingChains.Values)
            {
                string stepId = ShopSystem.CurrentRollingStep(State, Content, chain.Id);
                var p = Content.Product(stepId);
                if (p == null || !Usable(p)) continue;
                Kit.Ribbon(body, SectionTitle(ProductKind.RollingOffer));
                ProductCard(body, p, true);
            }
        }

        /// <summary>
        /// One offer as a rarity-coloured card (colour by price tier): name, pack art, contents, bonus, countdown and the buy
        /// button. <paramref name="wide"/> = the featured banner layout (art beside the text).
        /// </summary>
        private void ProductCard(Transform parent, ProductDef p, bool wide)
        {
            string block = ShopSystem.PurchaseBlockReason(State, Content, p, Now);
            var tier = Theme.RarityColor(TierOf(p));
            var card = Ui.Card(parent, Color.Lerp(Theme.Panel, tier, wide ? 0.5f : 0.42f), Color.Lerp(tier, Color.white, 0.25f), Theme.RadiusCard, 20, 8, 5, "Offer:" + p.Id);
            if (!wide) Ui.Size(card, 450, -1, 1);
            var top = wide ? Ui.Row(card, 190, 18, null, 0, "Head") : (Transform)card;
            var art = SummonKit.Img(top, World.SummonArt.Icon(PackIcon(p)), "PackArt");
            if (wide) Ui.Size(art, 190, 190, 0, 0); else Ui.Size(art, -1, 130, 1);
            var text = wide ? (Transform)Ui.Column(top, 4, 0, null, "Text") : card;
            if (wide) Ui.Size((Component)text, -1, -1, 1);
            var name = Ui.Display(text, OfferName(p), wide ? Theme.FontLarge : 30, Theme.Text, wide ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, "OfferName");
            name.supportRichText = true;
            var price = Ui.Chip(text, Fmt.Price(p.PriceUsdCents), new Color(0.04f, 0.02f, 0.05f, 0.7f), Theme.Premium, 28, 46, null, "Price");
            if (!wide) price.GetComponent<LayoutElement>().flexibleWidth = 1;
            var anchor = wide ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            if (p.Contents.Count > 0) Ui.Label(text, OfferRewards(p.Contents), Theme.FontSmall, Theme.Cream, anchor, "Note");
            if (p.FirstPurchaseBonus.Count > 0 && ShopSystem.PurchaseCount(State, p.Id) == 0) Ui.Label(text, "First purchase bonus: " + OfferRewards(p.FirstPurchaseBonus), Theme.FontSmall, Theme.Good, anchor);
            if (p.DailyRewards.Count > 0) Ui.Label(text, "Daily: " + OfferRewards(p.DailyRewards) + (p.DailyDays > 0 ? " for " + p.DailyDays + " days" : " forever"), Theme.FontSmall, Theme.Cream, anchor, "Note");
            if (p.Schedule != null && p.Schedule.TryActive(Now, out _, out long end)) Live(Ui.Label(text, "", Theme.FontSmall, Theme.Warn, anchor, "Note"), () => "Ends in " + Fmt.Duration(end - Now));
            var row = wide ? Ui.Row(card, 84) : Ui.Column(card, 8, 0, null, "Buy");
            if (block != null) Ui.Label(row, Fmt.Humanize(block), Theme.FontSmall, Theme.TextDim, anchor);
            else if (p.PriceUsdCents == 0) Kit.Button(row, "Claim free", () => Run(new ShopClaimFree { ProductId = p.Id }), KitButtonColor.Positive, -1, 80);
            else PurchaseButton(row, Session, p, "Buy ", -1, 80);
            if (p.DailyRewards.Count > 0 && ShopSystem.DailyGrantActive(State, Content, p.Id, Now))
                Kit.Button(row, "Claim daily", () => Run(new ShopClaimDaily { ProductId = p.Id }), KitButtonColor.Positive, -1, 80, ShopSystem.CanClaimDaily(State, Content, p.Id, Now));
        }

        /// <summary>
        /// The buy button of a real-money product: "Soon" while the store does not offer it yet (T53), "PENDING" while a
        /// payment waits for approval (T52), else <paramref name="verb"/> + price.
        /// </summary>
        public static Button PurchaseButton(Transform row, GameSession session, ProductDef p, string verb, float width, float height)
        {
            if (!session.IsOffered(p.Id))
            {
                var soon = Kit.Button(row, "Soon", null, KitButtonColor.Secondary, width, height, false);
                soon.name = "ComingSoon:" + p.Id;
                return soon;
            }
            if (session.PendingPayments.Contains(p.Id))
            {
                var pending = Ui.ButtonWithReason(row, "PENDING", null, Theme.Button, width, height, false, "Arrives once the payment is approved");
                pending.name = "Pending:" + p.Id;
                return pending;
            }
            return Kit.Button(row, verb + Fmt.Price(p.PriceUsdCents), () => session.Purchase(p.Id), KitButtonColor.Primary, width, height);
        }

        /// <summary>The Offers tab's banner: the best usable offer on sale (flash, rolling, starter, then gift), wide.</summary>
        private void Featured(RectTransform body)
        {
            ProductDef best = null;
            var order = new[] { ProductKind.FlashOffer, ProductKind.StarterPack, ProductKind.GiftPack };
            foreach (var kind in order)
            {
                foreach (var p in OfKind(kind)) if (Usable(p) && Active(p)) { best = p; break; }
                if (best != null) break;
            }
            if (best == null) return;
            Kit.Ribbon(body, "Featured", new Color32(0xB0, 0x6A, 0x1E, 255));
            ProductCard(body, best, true);
            _featuredId = best.Id;
        }

        private string _featuredId;

        /// <summary>"Rolling Offer 1" → "Special Offer · Step 1" (the chain step reads as progress, not a code name).</summary>
        private static string OfferName(ProductDef p) =>
            p.Kind == ProductKind.RollingOffer && p.Name != null && p.Name.StartsWith("Rolling Offer ") ? "Special Offer · Step " + p.Name.Substring(14) : p.Name;

        private void Items(RectTransform body)
        {
            var bySection = new SortedDictionary<string, List<ShopItemDef>>();
            foreach (var it in Content.ShopItems.Values)
            {
                if (it.Section == GuildSystem.ShopSection || it.Section == ContentDb.ArenaShopSection) continue; // own shops (Guild, Arena)
                if (it.Id == Content.Shop.DailyGiftItemId) continue; // the free daily gift lives on the Passes page
                if (it.Contents.Count > 0 && !RewardGate.AnyUsable(State, Content, it.Contents, Now)) continue; // for a locked feature
                if (!bySection.TryGetValue(it.Section, out var l)) bySection[it.Section] = l = new List<ShopItemDef>();
                l.Add(it);
            }
            foreach (var kv in bySection)
            {
                Kit.Ribbon(body, ItemSectionTitle(kv.Key));
                foreach (var item in kv.Value)
                {
                    var it = item;
                    var row = Ui.Row(body, 90, 10, Theme.Panel, 12);
                    Ui.Label(row, it.Name + "\n<size=22>" + Fmt.Rewards(it.Contents, Content, State, Now) + (it.Limit > 0 ? "  (limit " + it.Limit + " " + it.Period.ToString().ToLowerInvariant() + ")" : "") + "</size>", Theme.FontSmall);
                    Kit.Button(row, it.Price.Count == 0 ? "Free" : Fmt.Costs(it.Price, Content), () => Run(new ShopBuyItem { ItemId = it.Id }),
                        it.Price.Count == 0 ? KitButtonColor.Positive : KitButtonColor.Premium, 300, 76, State.Wallet.CanAfford(it.Price), Theme.FontSmall);
                }
            }
        }

        private void Ads(RectTransform body)
        {
            bool noAds = ShopSystem.HasNoAds(State, Content);
            if (noAds) Note(body, "No Ads owned: rewards are instant.");
            foreach (var a in Content.AdPlacements.Values)
            {
                if (a.Rewards.Count == 0 || !RewardGate.AnyUsable(State, Content, a.Rewards, Now)) continue;
                var ad = a;
                int left = ShopSystem.AdViewsLeft(State, Content, ad.Id, Now);
                var row = Ui.Row(body, 90, 10, Theme.Panel, 12);
                Ui.Label(row, ad.Name + "\n<size=22>" + Fmt.Rewards(ad.Rewards, Content, State, Now) + " — " + left + "/" + ad.DailyCap + " left today</size>", Theme.FontSmall);
                if (Session.IsAdPending(ad.Id)) AdPending(row); // T51: a spinner until the server grants the reward
                else Kit.Button(row, noAds ? "Claim" : "Watch", () => Session.WatchAd(ad.Id), KitButtonColor.Positive, 220, 76, left > 0);
            }
        }

        /// <summary>The ad button while its reward is on the way (online, G1 T51): a spinning ring and "Pending".</summary>
        public static RectTransform AdPending(Transform row)
        {
            var box = Ui.Row(row, 76, 8, null, 0, "AdPending");
            Ui.Size(box, 220, 76, 0);
            var ring = Ui.Panel(box, Theme.Good, "Spinner");
            ring.sprite = UiShapes.CircleRing; ring.type = UnityEngine.UI.Image.Type.Filled;
            ring.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360; ring.fillAmount = 0.75f;
            Ui.Size(ring, 44, 44, 0);
            if (Application.isPlaying) ring.gameObject.AddComponent<UiSpin>();
            Ui.Label(box, "Pending", Theme.FontSmall, Theme.TextDim);
            return box;
        }
    }
}
