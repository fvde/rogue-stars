using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Catalogue, offers, passes, funds, permanent purchases, Diamond items and rewarded ads. Real-money products go
    /// through GameSession.Purchase (IStoreGateway → dev receipt offline / /v1/iap/verify online).
    /// </summary>
    public sealed class ShopScreen : UiScreen
    {
        private static readonly string[] Tabs = { "Offers", "Diamonds", "Passes", "Forever", "Items", "Ads" };
        private int _tab;

        public override string Title => "Shop";

        protected override void Build(RectTransform body)
        {
            Ui.TabBar(body, Tabs, _tab, i => { _tab = i; Rebuild(); });
            if (!Session.IsOnline) Note(body, "Offline mode: purchases complete instantly with dev receipts.");
            else Note(body, "Store: " + Session.Store.Name + " (TODO: Google Play Billing gateway).");
            switch (_tab)
            {
                case 0:
                    Products(body, ProductKind.StarterPack);
                    Rolling(body);
                    Products(body, ProductKind.FlashOffer);
                    Products(body, ProductKind.GiftPack);
                    break;
                case 1: Products(body, ProductKind.DiamondPack); break;
                case 2: Products(body, ProductKind.MonthlyPass); SeasonPass(body); FundsSection(body); break;
                case 3: Products(body, ProductKind.Permanent); break;
                case 4: Items(body); break;
                case 5: Ads(body); break;
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
            Section(body, Fmt.Humanize(kind.ToString()));
            foreach (var p in list) ProductCard(body, p);
        }

        private void Rolling(RectTransform body)
        {
            foreach (var chain in Content.RollingChains.Values)
            {
                string stepId = ShopSystem.CurrentRollingStep(State, Content, chain.Id);
                var p = Content.Product(stepId);
                if (p == null) continue;
                Section(body, "Rolling offer");
                ProductCard(body, p);
            }
        }

        private void ProductCard(RectTransform body, ProductDef p)
        {
            string block = ShopSystem.PurchaseBlockReason(State, Content, p, Now);
            var card = Card(body, p.PriceUsdCents == 0 ? Theme.Header : Theme.PanelAlt);
            var head = Ui.Row(card, 60);
            Ui.Label(head, p.Name, Theme.FontNormal);
            Ui.Label(head, Fmt.Price(p.PriceUsdCents), Theme.FontNormal, Theme.Premium, TextAnchor.MiddleRight);
            if (p.Contents.Count > 0) Note(card, Fmt.Rewards(p.Contents, Content));
            if (p.FirstPurchaseBonus.Count > 0 && ShopSystem.PurchaseCount(State, p.Id) == 0) Ui.Label(card, "First purchase bonus: " + Fmt.Rewards(p.FirstPurchaseBonus, Content), Theme.FontSmall, Theme.Good);
            if (p.DailyRewards.Count > 0) Note(card, "Daily: " + Fmt.Rewards(p.DailyRewards, Content) + (p.DailyDays > 0 ? " for " + p.DailyDays + " days" : " forever"));
            if (p.Schedule != null && p.Schedule.TryActive(Now, out _, out long end)) Live(Note(card, ""), () => "Ends in " + Fmt.Duration(end - Now));
            var row = Ui.Row(card, 84);
            if (block != null) Ui.Label(row, Fmt.Humanize(block), Theme.FontSmall, Theme.TextDim);
            else if (p.PriceUsdCents == 0) Ui.Button(row, "Claim free", () => Run(new ShopClaimFree { ProductId = p.Id }), Theme.Good, -1, 84);
            else if (!Session.IsOffered(p.Id)) Ui.Button(row, "Coming soon", null, Theme.Button, -1, 84, false).name = "ComingSoon:" + p.Id;
            else Ui.Button(row, "Buy " + Fmt.Price(p.PriceUsdCents), () => Session.Purchase(p.Id), Theme.Accent, -1, 84);
            if (p.DailyRewards.Count > 0 && ShopSystem.DailyGrantActive(State, Content, p.Id, Now))
                Ui.Button(row, "Claim daily", () => Run(new ShopClaimDaily { ProductId = p.Id }), Theme.Good, -1, 84, ShopSystem.CanClaimDaily(State, Content, p.Id, Now));
        }

        private void SeasonPass(RectTransform body)
        {
            Section(body, "Season pass");
            if (!Session.IsUnlocked(Features.SeasonPass)) { Note(body, "Locked: " + Requirements.Describe(Content, Content.Feature(Features.SeasonPass)?.Requires ?? new RequirementSet())); return; }
            var sp = Content.SeasonPass;
            int level = ShopSystem.SeasonLevel(State, Content);
            bool premium = ShopSystem.HasPremiumSeason(State, Content, Now);
            var card = Card(body, Theme.Header);
            long xp = ShopSystem.SeasonXp(State, Content);
            Ui.Bar(card, sp.XpPerLevel > 0 ? (xp % sp.XpPerLevel) / (float)sp.XpPerLevel : 0, Theme.Premium, "Level " + level + " — " + xp + " XP");
            Live(Note(card, ""), () => "Season ends in " + Fmt.Duration(ShopSystem.SeasonEndMs(Content, Now) - Now));
            var row = Ui.Row(card, 84);
            if (!premium) { var pp = Content.Product(sp.PremiumProductId); if (pp != null) Ui.Button(row, Session.IsOffered(pp.Id) ? "Premium " + Fmt.Price(pp.PriceUsdCents) : "Premium: coming soon", () => Session.Purchase(pp.Id), Theme.Accent, -1, 84, Session.IsOffered(pp.Id)); }
            else Ui.Label(row, "Premium owned", Theme.FontSmall, Theme.Good);
            Ui.Button(row, "Claim all", () => Run(new SeasonPassClaimAll()), Theme.Good, -1, 84);
            foreach (var l in sp.Levels)
            {
                var lv = l;
                if (lv.Level > level + 3) break;
                var r = Ui.Row(body, 80, 8, Theme.Panel, 10);
                var lab = Ui.Label(r, "Lv " + lv.Level, Theme.FontSmall, lv.Level <= level ? Theme.Text : Theme.TextDim);
                Ui.Size(lab, 110, -1, 0);
                LevelButton(r, lv.Level, false, Fmt.Rewards(lv.Free, Content), level);
                LevelButton(r, lv.Level, true, Fmt.Rewards(lv.Premium, Content), level, premium);
            }
        }

        private void LevelButton(Transform row, int level, bool premiumTrack, string rewards, int reached, bool premiumOwned = true)
        {
            bool claimed = State.Shop.SeasonClaims.Contains((premiumTrack ? "p:" : "f:") + level);
            bool can = !claimed && level <= reached && premiumOwned;
            Ui.Button(row, (claimed ? "done: " : "") + rewards, () => Run(new SeasonPassClaim { Level = level, Premium = premiumTrack }),
                premiumTrack ? Theme.Premium : Theme.Button, -1, 72, can, Theme.FontSmall);
        }

        private void FundsSection(RectTransform body)
        {
            Section(body, "Funds");
            foreach (var f in Content.Funds.Values)
            {
                var fund = f;
                var card = Card(body, Theme.PanelAlt);
                bool owned = State.Shop.FundsOwned.Contains(fund.Id);
                long progress = ShopSystem.FundProgress(State, Content, fund);
                var p = Content.Product(fund.ProductId);
                var head = Ui.Row(card, 60);
                Ui.Label(head, fund.Name + " — progress " + progress, Theme.FontNormal);
                if (!owned && p != null)
                {
                    string block = ShopSystem.PurchaseBlockReason(State, Content, p, Now);
                    if (block == null) Ui.Button(head, Session.IsOffered(p.Id) ? "Buy " + Fmt.Price(p.PriceUsdCents) : "Coming soon", () => Session.Purchase(p.Id), Theme.Accent, 240, 60, Session.IsOffered(p.Id));
                    else Ui.Label(head, Fmt.Humanize(block), Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight);
                }
                var steps = Ui.Grid(card, new Vector2(190, 90), 5, 8);
                for (int i = 0; i < fund.Steps.Count; i++)
                {
                    int step = i;
                    var s = fund.Steps[i];
                    bool claimed = State.Shop.FundClaims.Contains(fund.Id + ":" + step);
                    bool can = owned && !claimed && progress >= s.Target;
                    Ui.Button(steps, s.Target + "\n" + (claimed ? "done" : Fmt.Rewards(s.Rewards, Content)), () => Run(new FundClaim { FundId = fund.Id, Step = step }),
                        can ? Theme.Good : Theme.Button, -1, 90, can, 22);
                }
            }
        }

        private void Items(RectTransform body)
        {
            var bySection = new SortedDictionary<string, List<ShopItemDef>>();
            foreach (var it in Content.ShopItems.Values)
            {
                if (it.Section == GuildSystem.ShopSection || it.Section == ContentDb.ArenaShopSection) continue; // own shops (Guild, Arena)
                if (!bySection.TryGetValue(it.Section, out var l)) bySection[it.Section] = l = new List<ShopItemDef>();
                l.Add(it);
            }
            foreach (var kv in bySection)
            {
                Section(body, Fmt.Humanize(kv.Key));
                foreach (var item in kv.Value)
                {
                    var it = item;
                    var row = Ui.Row(body, 90, 10, Theme.Panel, 12);
                    Ui.Label(row, it.Name + "\n<size=22>" + Fmt.Rewards(it.Contents, Content) + (it.Limit > 0 ? "  (limit " + it.Limit + " " + it.Period.ToString().ToLowerInvariant() + ")" : "") + "</size>", Theme.FontSmall);
                    Ui.Button(row, it.Price.Count == 0 ? "Free" : Fmt.Costs(it.Price, Content), () => Run(new ShopBuyItem { ItemId = it.Id }),
                        it.Price.Count == 0 ? Theme.Good : Theme.Premium, 300, 76, State.Wallet.CanAfford(it.Price), Theme.FontSmall);
                }
            }
        }

        private void Ads(RectTransform body)
        {
            bool noAds = ShopSystem.HasNoAds(State, Content);
            Note(body, noAds ? "No Ads owned: placements complete instantly." : "Rewarded ads (TODO: AppLovin MAX; fake ads for now).");
            foreach (var a in Content.AdPlacements.Values)
            {
                if (a.Rewards.Count == 0) continue;
                var ad = a;
                int left = ShopSystem.AdViewsLeft(State, Content, ad.Id, Now);
                var row = Ui.Row(body, 90, 10, Theme.Panel, 12);
                Ui.Label(row, ad.Name + "\n<size=22>" + Fmt.Rewards(ad.Rewards, Content) + " — " + left + "/" + ad.DailyCap + " left today</size>", Theme.FontSmall);
                Ui.Button(row, noAds ? "Claim" : "Watch", () => Session.WatchAd(ad.Id), Theme.Good, 220, 76, left > 0);
            }
        }
    }
}
