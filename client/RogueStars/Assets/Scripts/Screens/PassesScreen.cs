using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// The Passes page (Shop → "Passes", the hub's Pass icon): a scrolling stack of cards. First the free daily gift
    /// (<see cref="ShopConfig.DailyGiftItemId"/>, reset timer, Claim), then one colour card per pass product: the Monthly Pass,
    /// the Forever products (No Ads, Daily Diamonds Forever) and gift packs while they run. A pass card shows its title with
    /// one highlighted word, an "ACTIVE" tag when owned, the one-time bonus on purchase, the perks (real numbers from
    /// ShopConfig), the ticket illustration (sprite slot <see cref="ArtCatalog.PassTicket"/>), today's claim (Claim /
    /// Claimed / Buy) and, for timed passes, the time left. Below: the season pass and the funds. The bottom bar's
    /// "Claim all" runs <see cref="PassesClaimAll"/> (everything claimable on this page).
    /// </summary>
    public sealed class PassesScreen : UiScreen
    {
        public override string Title => "Passes";
        public override string[] IntroFeatures => new[] { Features.Funds, Features.SeasonPass };
        public override float FooterHeight => 140;

        /// <summary>Card colours: daily gift plum, Monthly Pass gold, No Ads purple, Daily Diamonds Forever pink-red, gift packs blue.</summary>
        public static readonly Color GiftColor = Theme.Hex(0x5A4C9C);
        public static readonly Color MonthlyColor = Theme.Hex(0xE8A93A);
        public static readonly Color NoAdsColor = Theme.Hex(0xA65BFF);
        public static readonly Color ForeverColor = Theme.Hex(0xE0406A);
        public static readonly Color GiftPackColor = Theme.Hex(0x3D8BFF);

        /// <summary>The pass cards of the page in order: Monthly Pass, Forever products (by price), running gift packs.</summary>
        public static List<ProductDef> PassProducts(GameState s, ContentDb c, long now)
        {
            var list = new List<ProductDef>();
            var monthly = c.Product(c.Shop.MonthlyPassProductId);
            if (monthly != null) list.Add(monthly);
            var forever = new List<ProductDef>();
            var gifts = new List<ProductDef>();
            foreach (var p in c.Products.Values)
            {
                if (p.Kind == ProductKind.Permanent) forever.Add(p);
                else if (p.Kind == ProductKind.GiftPack && ShopSystem.DailyGrantActive(s, c, p.Id, now)) gifts.Add(p);
            }
            int ByPrice(ProductDef a, ProductDef b) => a.PriceUsdCents != b.PriceUsdCents ? a.PriceUsdCents.CompareTo(b.PriceUsdCents) : string.CompareOrdinal(a.Id, b.Id);
            forever.Sort(ByPrice);
            gifts.Sort(ByPrice);
            list.AddRange(forever);
            list.AddRange(gifts);
            list.RemoveAll(p => !ShopScreen.Usable(s, c, p, now));
            return list;
        }

        /// <summary>Card colour of a pass.</summary>
        public static Color CardColor(ContentDb c, ProductDef p)
        {
            if (p.Id == c.Shop.MonthlyPassProductId) return MonthlyColor;
            if (p.EntitlementFlag == c.Shop.NoAdsFlag) return NoAdsColor;
            if (p.Kind == ProductKind.Permanent) return ForeverColor;
            return GiftPackColor;
        }

        /// <summary>The card title with its key word highlighted in gold ("<b>Monthly</b> Pass", "No <b>Ads</b>").</summary>
        public static string CardTitle(ContentDb c, ProductDef p)
        {
            string name = p.Name ?? p.Id;
            string word = p.Id == c.Shop.MonthlyPassProductId ? "Monthly" : p.EntitlementFlag == c.Shop.NoAdsFlag ? "Ads"
                : p.Kind == ProductKind.Permanent ? "Forever" : "Gift";
            int i = name.IndexOf(word, System.StringComparison.Ordinal);
            return i < 0 ? name : name.Substring(0, i) + "<color=#FFD54A>" + word + "</color>" + name.Substring(i + word.Length);
        }

        /// <summary>The perks list of a pass, from the real rules numbers (gold = the numbers).</summary>
        public static List<string> Perks(ContentDb c, ProductDef p)
        {
            var list = new List<string>();
            var cfg = c.Shop;
            string Gold(string t) => "<color=#FFD54A>" + t + "</color>";
            if (p.Id == cfg.MonthlyPassProductId)
            {
                list.Add("Board and battles at " + Gold("×" + Fmt.Whole(PlaySpeed.Fastest)) + " speed");
                list.Add(Gold("+" + cfg.MonthlyPassStaminaCapBonus) + " Stamina cap");
                list.Add(Gold("+" + Mathf.RoundToInt((float)((cfg.MonthlyPassProductionMultiplier - 1) * 100)) + "%") + " City production");
                list.Add("Daily Diamonds for " + Gold(p.DailyDays + " days"));
            }
            else if (p.EntitlementFlag == cfg.NoAdsFlag)
            {
                list.Add("Every ad reward is " + Gold("instant") + ": no videos");
                list.Add("Daily ad limits stay the same");
            }
            else if (p.DailyRewards.Count > 0)
                list.Add(Fmt.Rewards(p.DailyRewards, c) + " every day" + (p.DailyDays > 0 ? " for " + Gold(p.DailyDays + " days") : ", " + Gold("forever")));
            return list;
        }

        /// <summary>True when the player owns the pass now (running daily grant or the permanent entitlement).</summary>
        public static bool Owned(GameState s, ContentDb c, ProductDef p, long now) =>
            p.DailyRewards.Count > 0 ? ShopSystem.DailyGrantActive(s, c, p.Id, now) : p.EntitlementFlag != null && s.Flags.Contains(p.EntitlementFlag);

        protected override void Build(RectTransform body)
        {
            BuildFooter();
            DailyGiftCard(body);
            foreach (var p in PassProducts(State, Content, Now)) PassCard(body, p);
            SeasonPass(body);
            FundsSection(body);
        }

        private void BuildFooter()
        {
            if (Footer == null) return;
            int n = ShopSystem.PassClaimables(State, Content, Now).Count;
            var row = Ui.Row(Footer, 96, 0, null, 0, "ClaimAllRow");
            Ui.Spacer(row);
            var b = Kit.Button(row, n > 0 ? "Claim all (" + n + ")" : "Claim all", () => Run(new PassesClaimAll()), n > 0 ? KitButtonColor.Positive : KitButtonColor.Secondary, 560, 96, n > 0, Theme.FontLarge);
            b.name = "ClaimAll";
            Ui.Spacer(row);
        }

        // ---- cards ------------------------------------------------------------------------------------------

        private static RectTransform ColourCard(Transform parent, Color tint, bool active, string name)
        {
            var card = Ui.Card(parent, Color.Lerp(Theme.Panel, tint, 0.5f), active ? Theme.Good : Color.Lerp(tint, Color.white, 0.3f), Theme.RadiusCard, 22, 12, active ? 6 : 4, name);
            return card;
        }

        /// <summary>The "ACTIVE" tag in the card's top-right corner (outside the layout).</summary>
        private static void ActiveTag(Transform card)
        {
            var pill = Kit.Pill(card, "ACTIVE", Theme.Good, Theme.OnAccent, "ActiveTag", 46);
            pill.GetComponent<LayoutElement>().ignoreLayout = true;
            pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(1, 1);
            pill.sizeDelta = new Vector2(150, 46);
            pill.anchoredPosition = new Vector2(-18, -18);
        }

        private static Text CardTitleText(Transform row, string richTitle)
        {
            var t = Kit.Title(row, richTitle, 46, Color.white, TextAnchor.MiddleLeft, "CardTitle");
            t.supportRichText = true;
            Ui.Size(t, -1, 64, 1);
            return t;
        }

        private static RectTransform RewardSlots(Transform parent, List<Reward> rewards, float size, string state, string name)
        {
            var row = Ui.Row(parent, size, 10, null, 0, name);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            for (int i = 0; i < rewards.Count && i < 3; i++)
                Kit.RewardSlot(row, SummonArt.RewardIcon(rewards[i]), "×" + Fmt.Num(rewards[i].Amount), size, state, null, "Slot:" + rewards[i].Id);
            return row;
        }

        private void DailyGiftCard(RectTransform body)
        {
            var item = ShopSystem.DailyGift(Content);
            if (item == null) return;
            bool can = ShopSystem.CanClaimDailyGift(State, Content, Now);
            var card = ColourCard(body, GiftColor, false, "PassCard:" + item.Id);
            var head = Ui.Row(card, 64, 12, null, 0, "Head");
            CardTitleText(head, "Daily <color=#FFD54A>Gift</color>");
            var timer = Ui.Label(head, "", Theme.FontSmall, Theme.Cream, TextAnchor.MiddleRight, "ResetTimer");
            Ui.Size(timer, 300, -1, 0);
            Live(timer, () => "New in " + Fmt.Duration(ShopSystem.NextDailyResetMs(Content, Now) - Now));
            var row = Ui.Row(card, 140, 12, null, 0, "ClaimRow");
            RewardSlots(row, item.Contents, 130, can ? "ready" : "claimed", "GiftSlots");
            Ui.Spacer(row);
            var col = Ui.Column(row, 0, 0, null, "Action");
            col.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(col, 300, -1, 0);
            var b = Kit.Button(col, can ? "Claim" : "Claimed", () => Run(new ShopBuyItem { ItemId = item.Id }), can ? KitButtonColor.Positive : KitButtonColor.Secondary, 300, 96, can);
            b.name = (can ? "Claim:" : "Claimed:") + item.Id;
        }

        private void PassCard(RectTransform body, ProductDef p)
        {
            var tint = CardColor(Content, p);
            bool owned = Owned(State, Content, p, Now);
            string block = ShopSystem.PurchaseBlockReason(State, Content, p, Now);
            var card = ColourCard(body, tint, owned, "PassCard:" + p.Id);
            var head = Ui.Row(card, 64, 12, null, 0, "Head");
            CardTitleText(head, CardTitle(Content, p));
            if (owned) { Ui.Size(Ui.Rect(head, "TagSpace"), 150, -1, 0); ActiveTag(card); }

            // middle: bonus + perks on the left, the ticket on the right
            var mid = Ui.Row(card, -1, 14, null, 0, "Middle");
            mid.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            var left = Ui.Column(mid, 12, 0, null, "Left");
            Ui.Size(left, -1, -1, 1);
            if (p.Contents.Count > 0)
            {
                var bonus = Ui.Row(left, 120, 8, null, 0, "Bonus");
                bonus.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
                bonus.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                Kit.Ribbon(bonus, "On purchase", Theme.Shade(tint, 0.55f), 76);
                RewardSlots(bonus, p.Contents, 112, "open", "BonusSlots");
            }
            var perks = Kit.Card(left, KitTone.Well, 16, 6, "Perks");
            perks.GetComponent<Image>().color = new Color(0.05f, 0.02f, 0.08f, 0.45f);
            foreach (var perk in Perks(Content, p))
            {
                var pr = Ui.Row(perks, -1, 10, null, 0, "Perk");
                pr.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
                var check = Kit.Title(pr, "✓", 34, Theme.Good, TextAnchor.MiddleCenter, "Check");
                Ui.Size(check, 40, 44, 0);
                Ui.Label(pr, perk, Theme.FontSmall, Theme.Text, TextAnchor.MiddleLeft, "PerkText");
            }
            if (p.EntitlementFlag == Content.Shop.NoAdsFlag && Content.Shop.AdsDisabled)
                Ui.Label(perks, "Ads are switched off for everyone right now: ad rewards are already instant.", 22, Theme.TextDim, TextAnchor.MiddleLeft, "AdsOffNote");
            Ticket(mid, p, tint, 300);

            // bottom: today's reward on the left, time left + the action on the right
            var bottom = Ui.Row(card, -1, 12, null, 0, "ClaimRow");
            bottom.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            bottom.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            if (p.DailyRewards.Count > 0)
            {
                var daily = Ui.Column(bottom, 4, 0, null, "Daily");
                Ui.Size(daily, 300, -1, 0);
                Ui.Label(daily, "Every day", Theme.FontSmall, Color.Lerp(tint, Color.white, 0.55f), TextAnchor.MiddleLeft, "DailyLabel");
                string state = !owned ? "open" : ShopSystem.CanClaimDaily(State, Content, p.Id, Now) ? "ready" : "claimed";
                RewardSlots(daily, p.DailyRewards, 120, state, "DailySlots");
            }
            Ui.Spacer(bottom);
            var action = Ui.Column(bottom, 8, 0, null, "Action");
            action.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.LowerCenter;
            Ui.Size(action, 320, -1, 0);
            TimeLeft(action, p, owned);
            if (owned && p.DailyRewards.Count > 0)
            {
                bool can = ShopSystem.CanClaimDaily(State, Content, p.Id, Now);
                var b = Kit.Button(action, can ? "Claim" : "Claimed", () => Run(new ShopClaimDaily { ProductId = p.Id }), can ? KitButtonColor.Positive : KitButtonColor.Secondary, 320, 96, can);
                b.name = (can ? "Claim:" : "Claimed:") + p.Id;
                if (block == null) ShopScreen.PurchaseButton(action, Session, p, "+" + p.DailyDays + " days ", 320, 64).name += ":extend"; // monthly pass: buying again extends
            }
            else if (owned) Kit.Button(action, "Owned", null, KitButtonColor.Secondary, 320, 96, false).name = "Owned:" + p.Id;
            else if (block != null) Ui.Label(action, Fmt.Humanize(block), Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter, "Blocked");
            else ShopScreen.PurchaseButton(action, Session, p, "Buy ", 320, 96);
        }

        /// <summary>Time left of a timed pass ("Ends in 28d 8h"), its length before buying ("30 days"), "Forever" for permanent ones.</summary>
        private void TimeLeft(Transform action, ProductDef p, bool owned)
        {
            if (p.DailyRewards.Count == 0) return;
            var t = Ui.Label(action, "", Theme.FontSmall, Theme.Cream, TextAnchor.MiddleCenter, "TimeLeft");
            if (p.DailyDays <= 0) { t.text = "Forever"; return; }
            if (!owned) { t.text = p.DailyDays + " days"; return; }
            Live(t, () => "Ends in " + Fmt.Duration(ShopSystem.DailyGrantEndMs(State, Content, p.Id) - Now));
        }

        /// <summary>Placeholder icon of a pass ticket until its illustration exists.</summary>
        public static string TicketIcon(ContentDb c, ProductDef p) =>
            p.Id == c.Shop.MonthlyPassProductId ? "pass" : p.EntitlementFlag == c.Shop.NoAdsFlag ? "ad" : p.Kind == ProductKind.Permanent ? "diamond" : "packs";

        /// <summary>
        /// The ticket illustration (<see cref="ArtCatalog.PassTicket"/>), else a tilted kit plate in the card colour with the
        /// pass's icon; a soft shadow under it.
        /// </summary>
        private void Ticket(Transform parent, ProductDef p, Color tint, float width)
        {
            var root = Ui.Rect(parent, "Ticket");
            Ui.Size(root, width, width * 0.8f, 0, 0);
            var shadow = Ui.Dot(root, new Color(0, 0, 0, 0.3f), 10, "Shadow");
            shadow.GetComponent<LayoutElement>().ignoreLayout = true;
            var srt = shadow.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0); srt.pivot = new Vector2(0.5f, 0);
            srt.sizeDelta = new Vector2(width * 0.7f, width * 0.1f); srt.anchoredPosition = new Vector2(0, 4);
            var art = ArtCatalog.PassTicket(p.Id);
            if (art != null)
            {
                var img = Ui.Panel(root, Color.white, "Art");
                img.sprite = art; img.preserveAspect = true; img.raycastTarget = false;
                Ui.Stretch(img.rectTransform, 0, 0, 0, width * 0.08f);
                return;
            }
            var plate = Kit.Surface(root, Color.Lerp(tint, Color.white, 0.2f), 30, Color.Lerp(tint, Color.white, 0.6f), "panel", "Placeholder");
            plate.raycastTarget = false;
            var prt = plate.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.55f); prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(width * 0.9f, width * 0.56f);
            prt.anchoredPosition = Vector2.zero;
            prt.localRotation = Quaternion.Euler(0, 0, 8);
            var inner = Kit.Surface(prt, Theme.Shade(tint, 0.75f), 22, Color.Lerp(tint, Color.white, 0.45f), "panel", "Inner");
            inner.raycastTarget = false;
            Ui.Stretch(inner.rectTransform, 22, 22, 22, 22);
            var icon = Ui.Panel(prt, Color.white, "Icon");
            icon.sprite = SummonArt.Icon(TicketIcon(Content, p)); icon.preserveAspect = true; icon.raycastTarget = false;
            Ui.Stretch(icon.rectTransform, width * 0.27f, width * 0.1f, width * 0.27f, width * 0.1f);
        }

        // ---- season pass and funds (moved from the Shop's former Passes tab) -------------------------------

        private void SeasonPass(RectTransform body)
        {
            Kit.Ribbon(body, "Season pass", new Color32(0x5A, 0x4A, 0x9C, 255));
            if (!Session.IsUnlocked(Features.SeasonPass)) { Note(body, "Locked: " + Requirements.Describe(Content, Content.Feature(Features.SeasonPass)?.Requires ?? new RequirementSet())); return; }
            var sp = Content.SeasonPass;
            int level = ShopSystem.SeasonLevel(State, Content);
            bool premium = ShopSystem.HasPremiumSeason(State, Content, Now);
            var card = Card(body, Theme.Header);
            long xp = ShopSystem.SeasonXp(State, Content);
            Kit.ProgressPill(card, sp.XpPerLevel > 0 ? (xp % sp.XpPerLevel) / (float)sp.XpPerLevel : 0, Theme.Premium, "Level " + level + " — " + xp + " XP");
            Live(Note(card, ""), () => "Season ends in " + Fmt.Duration(ShopSystem.SeasonEndMs(Content, Now) - Now));
            var row = Ui.Row(card, 84);
            if (!premium) { var pp = Content.Product(sp.PremiumProductId); if (pp != null) Ui.Button(row, !Session.IsOffered(pp.Id) ? "Premium: soon" : Session.PendingPayments.Contains(pp.Id) ? "Premium: PENDING" : "Premium " + Fmt.Price(pp.PriceUsdCents), () => Session.Purchase(pp.Id), Theme.Accent, -1, 84, Session.IsOffered(pp.Id) && !Session.PendingPayments.Contains(pp.Id)); }
            else Ui.Label(row, "Premium owned", Theme.FontSmall, Theme.Good);
            Ui.Button(row, "Claim all", () => Run(new SeasonPassClaimAll()), Theme.Good, -1, 84);
            foreach (var l in sp.Levels)
            {
                var lv = l;
                if (lv.Level > level + 3) break;
                var r = Ui.Row(body, 80, 8, Theme.Panel, 10);
                var lab = Kit.LevelBadge(r, "Lv " + lv.Level, lv.Level <= level ? Theme.Gold : Theme.TextDim, 40);
                Ui.Size(lab, 120, 40, 0, 0);
                LevelButton(r, lv.Level, false, Fmt.Rewards(lv.Free, Content, State, Now), level);
                LevelButton(r, lv.Level, true, Fmt.Rewards(lv.Premium, Content, State, Now), level, premium);
            }
        }

        private void LevelButton(Transform row, int level, bool premiumTrack, string rewards, int reached, bool premiumOwned = true)
        {
            bool claimed = State.Shop.SeasonClaims.Contains((premiumTrack ? "p:" : "f:") + level);
            bool can = !claimed && level <= reached && premiumOwned;
            Kit.Button(row, (claimed ? "done: " : "") + rewards, () => Run(new SeasonPassClaim { Level = level, Premium = premiumTrack }),
                premiumTrack ? KitButtonColor.Premium : can ? KitButtonColor.Positive : KitButtonColor.Secondary, -1, 72, can, Theme.FontSmall);
        }

        private void FundsSection(RectTransform body)
        {
            Kit.Ribbon(body, "Funds", new Color32(0x2E, 0x7A, 0x5A, 255));
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
                    if (block == null) Ui.Button(head, !Session.IsOffered(p.Id) ? "Soon" : Session.PendingPayments.Contains(p.Id) ? "PENDING" : "Buy " + Fmt.Price(p.PriceUsdCents), () => Session.Purchase(p.Id), Theme.Accent, 240, 60, Session.IsOffered(p.Id) && !Session.PendingPayments.Contains(p.Id));
                    else Ui.Label(head, Fmt.Humanize(block), Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight);
                }
                var steps = Ui.Grid(card, new Vector2(204, 96), 4, 8);
                for (int i = 0; i < fund.Steps.Count; i++)
                {
                    int step = i;
                    var s = fund.Steps[i];
                    bool claimed = State.Shop.FundClaims.Contains(fund.Id + ":" + step);
                    bool can = owned && !claimed && progress >= s.Target;
                    Kit.Button(steps, s.Target + "\n" + (claimed ? "done" : Fmt.Rewards(s.Rewards, Content, State, Now)), () => Run(new FundClaim { FundId = fund.Id, Step = step }),
                        can ? KitButtonColor.Positive : KitButtonColor.Secondary, -1, 90, can, 22);
                }
            }
        }
    }
}
