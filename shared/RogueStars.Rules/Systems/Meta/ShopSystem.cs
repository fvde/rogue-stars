using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Shop & monetization (GDD §14): one fixed catalogue for everyone (no A/B, no spend-based offers).
    /// IAP flow: PurchaseStart (client, pending record) → PurchaseComplete (server-only, after receipt validation).
    /// Also: daily entitlements (gift packs, monthly pass, Daily Diamonds), rolling/flash offers, permanent
    /// entitlements (No Ads), season pass (free + retroactive premium), Funds, Diamond shop items, rewarded ads.
    /// Events: "purchase"(productId), "ad_watched"(placementId), "season_level"(level).
    /// </summary>
    public static class ShopSystem
    {
        public static void Register(GameEngine e)
        {
            e.RegisterSettler(Settle);
            e.RegisterNewPlayer(Settle);
            // Monthly pass stamina cap bonus feeds Progression's regen engine (EconomySystem is registered before Meta).
            EconomySystem.RegisterRegenBonusProvider(e, (s, c, now, currency) =>
                currency == Currencies.Stamina ? (StaminaBonus(s, c, now), 0.0) : (0L, 0.0));
            e.Register<PurchaseStart>(HandlePurchaseStart);
            e.Register<PurchaseComplete>(HandlePurchaseComplete);
            e.Register<ShopClaimFree>(HandleClaimFree);
            e.Register<ShopClaimDaily>(HandleClaimDaily);
            e.Register<ShopBuyItem>(HandleBuyItem);
            e.Register<AdWatched>(HandleAdWatched);
            e.Register<SeasonPassClaim>(HandleSeasonClaim);
            e.Register<SeasonPassClaimAll>(HandleSeasonClaimAll);
            e.Register<FundClaim>(HandleFundClaim);
        }

        public const string MonthlyPassModifierId = "pass:monthly";

        // ---- queries ---------------------------------------------------------------------------

        public static int PurchaseCount(GameState s, string productId) => s.Shop.PurchaseCounts.TryGetValue(productId, out var n) ? n : 0;

        /// <summary>Why a product cannot be bought right now (null = purchasable). Same rules for every player.</summary>
        public static string PurchaseBlockReason(GameState s, ContentDb content, ProductDef p, long nowMs)
        {
            if (p == null) return "unknown product";
            if (!Requirements.IsMet(s, content, p.Requires, nowMs)) return "product locked";
            if (p.Limit > 0 && PurchaseCount(s, p.Id) >= p.Limit) return "purchase limit reached";
            if (p.Kind == ProductKind.RollingOffer && CurrentRollingStep(s, content, p.ChainId) != p.Id) return "not the current rolling offer";
            if (p.Schedule != null)
            {
                if (!p.Schedule.TryActive(nowMs, out long start, out _)) return "offer not active";
                if (p.LimitPerOccurrence > 0 && OccurrenceCount(s, p.Id, start) >= p.LimitPerOccurrence) return "offer limit reached";
            }
            if (p.Kind == ProductKind.SeasonPass && s.Shop.PremiumSeason == SeasonIndex(content, nowMs)) return "already owned this season";
            if (p.Kind == ProductKind.GiftPack && DailyGrantActive(s, content, p.Id, nowMs)) return "gift pack still active";
            if (p.EntitlementFlag != null && p.Limit > 0 && s.Flags.Contains(p.EntitlementFlag)) return "already owned";
            if (p.DailyLimit > 0 && DailyPurchaseCount(s, content, p.Id, nowMs) >= p.DailyLimit) return "daily limit reached";
            if (p.SummonEvent != null) return SummonEventSystem.ProductBlockReason(s, content, p, nowMs);
            return null;
        }

        /// <summary>Catalogue entries purchasable now (visible store page). Same for every player at the same progress.</summary>
        public static List<ProductDef> AvailableProducts(GameState s, ContentDb content, long nowMs)
        {
            var list = new List<ProductDef>();
            foreach (var p in content.Products.Values) if (PurchaseBlockReason(s, content, p, nowMs) == null) list.Add(p);
            list.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : a.PriceUsdCents != b.PriceUsdCents ? a.PriceUsdCents.CompareTo(b.PriceUsdCents) : string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        /// <summary>Product id of the current step of a rolling chain.</summary>
        public static string CurrentRollingStep(GameState s, ContentDb content, string chainId)
        {
            if (chainId == null || !content.RollingChains.TryGetValue(chainId, out var chain) || chain.Steps.Count == 0) return null;
            int taken = s.Shop.RollingTaken.TryGetValue(chainId, out var t) ? t : 0;
            if (!chain.Loop && taken >= chain.Steps.Count) return null;
            return chain.Steps[taken % chain.Steps.Count];
        }

        /// <summary>Purchases of a daily-limited product on the current reset day.</summary>
        public static int DailyPurchaseCount(GameState s, ContentDb content, string productId, long nowMs) =>
            s.Shop.ProductDailyBuys.TryGetValue(productId, out var pc) && pc.Period == MetaTime.DayIndex(content, nowMs) ? pc.Count : 0;

        private static int OccurrenceCount(GameState s, string productId, long start) =>
            s.Shop.OccurrencePurchases.TryGetValue(productId + "@" + start, out var n) ? n : 0;

        public static bool DailyGrantActive(GameState s, ContentDb content, string productId, long nowMs) =>
            s.Shop.DailyGrants.TryGetValue(productId, out var g) && MetaTime.DayIndex(content, nowMs) < g.EndDay;

        public static bool CanClaimDaily(GameState s, ContentDb content, string productId, long nowMs) =>
            DailyGrantActive(s, content, productId, nowMs) && s.Shop.DailyGrants[productId].LastClaimDay < MetaTime.DayIndex(content, nowMs);

        public static bool HasMonthlyPass(GameState s, ContentDb content, long nowMs) => DailyGrantActive(s, content, content.Shop.MonthlyPassProductId, nowMs);

        /// <summary>No Ads entitlement (or the global ads-disabled mode): every ad placement becomes instant (caps still apply).</summary>
        public static bool HasNoAds(GameState s, ContentDb content) => content.Shop.AdsDisabled || s.Flags.Contains(content.Shop.NoAdsFlag);

        /// <summary>
        /// Stamina cap bonus from Meta sources (monthly pass); registered as an EconomySystem regen bonus provider.
        /// </summary>
        public static long StaminaBonus(GameState s, ContentDb content, long nowMs) =>
            HasMonthlyPass(s, content, nowMs) ? content.Shop.MonthlyPassStaminaCapBonus : 0;

        /// <summary>True when battles may run at ×3 speed (monthly pass).</summary>
        public static bool HasBattleSpeed3(GameState s, ContentDb content, long nowMs) => HasMonthlyPass(s, content, nowMs);

        public static int AdViewsLeft(GameState s, ContentDb content, string placementId, long nowMs)
        {
            var def = content.AdPlacement(placementId);
            if (def == null) return 0;
            int used = s.Shop.AdDay == MetaTime.DayIndex(content, nowMs) && s.Shop.AdViews.TryGetValue(placementId, out var n) ? n : 0;
            return Math.Max(0, def.DailyCap - used);
        }

        // Season pass ------------------------------------------------------------------------------

        public static long SeasonIndex(ContentDb content, long nowMs) => MetaTime.FloorDiv(nowMs - content.SeasonPass.EpochMs, content.SeasonPass.LengthMs);

        public static long SeasonEndMs(ContentDb content, long nowMs) => content.SeasonPass.EpochMs + (SeasonIndex(content, nowMs) + 1) * content.SeasonPass.LengthMs;

        /// <summary>Season XP: quest XP + counter progress since season start × content rates.</summary>
        public static long SeasonXp(GameState s, ContentDb content)
        {
            long xp = s.Shop.SeasonQuestXp;
            foreach (var kv in content.SeasonPass.XpPerCounter)
            {
                long baseline = s.Shop.SeasonBaseline.TryGetValue(kv.Key, out var b) ? b : 0;
                xp += Math.Max(0, s.Counter(kv.Key) - baseline) * kv.Value;
            }
            return xp;
        }

        public static int SeasonLevel(GameState s, ContentDb content) =>
            (int)Math.Min(content.SeasonPass.Levels.Count, SeasonXp(s, content) / Math.Max(1, content.SeasonPass.XpPerLevel));

        public static bool HasPremiumSeason(GameState s, ContentDb content, long nowMs) => s.Shop.PremiumSeason == SeasonIndex(content, nowMs);

        /// <summary>Add season XP from quests/other Meta sources.</summary>
        public static void AddSeasonXp(GameState s, long xp) { if (xp > 0) s.Shop.SeasonQuestXp += xp; }

        // Funds ------------------------------------------------------------------------------------

        public static long FundProgress(GameState s, ContentDb content, FundDef fund) => Requirements.Current(s, content, fund.Measure);

        // ---- shared helpers for other systems ------------------------------------------------------

        /// <summary>
        /// Consume one rewarded-ad view of a placement (daily cap) and grant its direct rewards. Call from the
        /// owning system's command (stamina refill, revive, timer speed-up, extra free pull...). With No Ads no
        /// token is required. Throws RuleException when capped or when the token is missing.
        /// </summary>
        public static void ConsumeAdView(CommandContext ctx, string placementId, string adToken)
        {
            var s = ctx.State;
            var def = ctx.Content.AdPlacement(placementId);
            GameEngine.Require(def != null, "unknown ad placement");
            GameEngine.Require(AdViewsLeft(s, ctx.Content, placementId, ctx.NowMs) > 0, "ad daily cap reached");
            bool noAds = HasNoAds(s, ctx.Content);
            GameEngine.Require(noAds || !string.IsNullOrEmpty(adToken), "ad token required");
            long day = MetaTime.DayIndex(ctx.Content, ctx.NowMs);
            if (s.Shop.AdDay != day) { s.Shop.AdDay = day; s.Shop.AdViews.Clear(); }
            s.Shop.AdViews[placementId] = (s.Shop.AdViews.TryGetValue(placementId, out var n) ? n : 0) + 1;
            s.Count("ads_watched");
            ctx.GrantAll(def.Rewards);
            ctx.Emit("ad_watched", placementId, noAds ? "instant" : "ad");
        }

        /// <summary>Buy a shop item with per-period limits (used by the Diamond shop and the guild shop).</summary>
        public static void BuyItem(CommandContext ctx, ShopItemDef item, Dictionary<string, PeriodCount> counts)
        {
            var s = ctx.State;
            GameEngine.Require(item != null, "unknown item");
            GameEngine.Require(Requirements.IsMet(s, ctx.Content, item.Requires, ctx.NowMs), "item locked");
            long period = item.Period == LimitPeriod.Daily ? MetaTime.DayIndex(ctx.Content, ctx.NowMs)
                : item.Period == LimitPeriod.Weekly ? MetaTime.WeekIndex(ctx.Content, ctx.NowMs) : 0;
            counts.TryGetValue(item.Id, out var pc);
            int bought = pc != null && pc.Period == period ? pc.Count : 0;
            GameEngine.Require(item.Limit <= 0 || bought < item.Limit, "item limit reached");
            GameEngine.Require(s.Wallet.CanAfford(item.Price), "not enough currency");
            s.Wallet.TrySpend(item.Price);
            counts[item.Id] = new PeriodCount { Period = period, Count = bought + 1 };
            foreach (var c in item.Price) if (c.Currency == Currencies.Diamonds) s.Count("diamonds_spent", c.Amount);
            s.Count("shop_items_bought");
            ctx.GrantAll(item.Contents);
        }

        // ---- settler -----------------------------------------------------------------------------

        /// <summary>Season rollover, monthly pass effects (flag + production modifier), pending purchase expiry.</summary>
        public static void Settle(CommandContext ctx)
        {
            var s = ctx.State;
            var content = ctx.Content;
            long season = SeasonIndex(content, ctx.NowMs);
            if (s.Shop.SeasonIndex != season)
            {
                s.Shop.SeasonIndex = season;
                s.Shop.SeasonQuestXp = 0;
                s.Shop.SeasonClaims.Clear();
                s.Shop.SeasonBaseline.Clear();
                foreach (var key in content.SeasonPass.XpPerCounter.Keys) s.Shop.SeasonBaseline[key] = s.Counter(key);
            }
            ApplyPassEffects(s, content, ctx.NowMs);
            long ttl = content.Shop.PendingPurchaseTtlMs;
            s.Shop.Pending.RemoveAll(p => ctx.NowMs - p.StartedMs > ttl);
        }

        private static void ApplyPassEffects(GameState s, ContentDb content, long nowMs)
        {
            string flag = content.Shop.BattleSpeedFlag;
            if (!HasMonthlyPass(s, content, nowMs)) { s.Flags.Remove(flag); return; }
            s.Flags.Add(flag);
            long endMs = MetaTime.DayStartMs(content, s.Shop.DailyGrants[content.Shop.MonthlyPassProductId].EndDay);
            Modifier mod = null;
            foreach (var m in s.Modifiers) if (m.Id == MonthlyPassModifierId) { mod = m; break; }
            if (mod == null)
            {
                mod = new Modifier { Id = MonthlyPassModifierId, Source = "pass:monthly", Target = "production:*", StartMs = nowMs, Description = "Monthly Pass production bonus" };
                s.Modifiers.Add(mod);
            }
            mod.Multiplier = content.Shop.MonthlyPassProductionMultiplier;
            mod.EndMs = endMs;
        }

        // ---- granting ----------------------------------------------------------------------------

        private static void GrantProduct(CommandContext ctx, ProductDef p)
        {
            var s = ctx.State;
            var content = ctx.Content;
            long today = MetaTime.DayIndex(content, ctx.NowMs);
            if (PurchaseCount(s, p.Id) == 0) ctx.GrantAll(p.FirstPurchaseBonus);
            ctx.GrantAll(p.Contents);
            s.Shop.PurchaseCounts[p.Id] = PurchaseCount(s, p.Id) + 1;
            if (p.EntitlementFlag != null) s.Flags.Add(p.EntitlementFlag);
            if (p.DailyRewards.Count > 0)
            {
                if (!s.Shop.DailyGrants.TryGetValue(p.Id, out var g)) { g = new DailyGrant { ProductId = p.Id, EndDay = today }; s.Shop.DailyGrants[p.Id] = g; }
                if (p.DailyDays <= 0) g.EndDay = long.MaxValue;
                else if (g.EndDay != long.MaxValue) g.EndDay = Math.Max(g.EndDay, today) + p.DailyDays;
            }
            if (p.Kind == ProductKind.RollingOffer)
                s.Shop.RollingTaken[p.ChainId] = (s.Shop.RollingTaken.TryGetValue(p.ChainId, out var t) ? t : 0) + 1;
            if (p.Schedule != null && p.Schedule.TryActive(ctx.NowMs, out long start, out _))
                s.Shop.OccurrencePurchases[p.Id + "@" + start] = OccurrenceCount(s, p.Id, start) + 1;
            if (p.Kind == ProductKind.SeasonPass) s.Shop.PremiumSeason = SeasonIndex(content, ctx.NowMs);
            if (p.FundId != null) s.Shop.FundsOwned.Add(p.FundId);
            if (p.DailyLimit > 0)
                s.Shop.ProductDailyBuys[p.Id] = new PeriodCount { Period = today, Count = DailyPurchaseCount(s, content, p.Id, ctx.NowMs) + 1 };
            if (p.Kind == ProductKind.SummonPass) SummonEventSystem.OnPassPurchased(ctx, p);
            ApplyPassEffects(s, content, ctx.NowMs);
            ctx.Emit("purchase", p.Id);
        }

        // ---- handlers ----------------------------------------------------------------------------

        private static CommandResult HandlePurchaseStart(PurchaseStart cmd, CommandContext ctx)
        {
            var p = ctx.Content.Product(cmd.ProductId);
            GameEngine.Require(p != null, "unknown product");
            GameEngine.Require(p.PriceUsdCents > 0, "free product: use ShopClaimFree");
            string block = PurchaseBlockReason(ctx.State, ctx.Content, p, ctx.NowMs);
            GameEngine.Require(block == null, block);
            ctx.State.Shop.Pending.RemoveAll(x => x.ProductId == p.Id);
            ctx.State.Shop.Pending.Add(new PendingPurchase { ProductId = p.Id, StartedMs = ctx.NowMs });
            return CommandResult.Success(p.StoreProductId);
        }

        private static CommandResult HandlePurchaseComplete(PurchaseComplete cmd, CommandContext ctx)
        {
            var s = ctx.State;
            var cfg = ctx.Content.Shop;
            var p = ctx.Content.Product(cmd.ProductId);
            GameEngine.Require(p != null, "unknown product");
            GameEngine.Require(s.Shop.Pending.Exists(x => x.ProductId == p.Id), "no pending purchase");
            GameEngine.Require(!string.IsNullOrEmpty(cmd.ReceiptId), "receipt required");
            GameEngine.Require(!s.Shop.Receipts.Contains(cmd.ReceiptId), "receipt already used");
            GameEngine.Require(!cmd.ReceiptId.StartsWith(cfg.DevReceiptPrefix, StringComparison.Ordinal) || cfg.AllowDevReceipts, "dev receipts not allowed");
            string block = PurchaseBlockReason(s, ctx.Content, p, ctx.NowMs);
            GameEngine.Require(block == null, block);

            s.Shop.Pending.RemoveAll(x => x.ProductId == p.Id);
            s.Shop.Receipts.Add(cmd.ReceiptId);
            if (s.Shop.Receipts.Count > 200) s.Shop.Receipts.RemoveAt(0);
            s.Count("iap_purchases");
            GrantProduct(ctx, p);
            return CommandResult.Success();
        }

        private static CommandResult HandleClaimFree(ShopClaimFree cmd, CommandContext ctx)
        {
            var p = ctx.Content.Product(cmd.ProductId);
            GameEngine.Require(p != null, "unknown product");
            GameEngine.Require(p.PriceUsdCents == 0, "not free");
            string block = PurchaseBlockReason(ctx.State, ctx.Content, p, ctx.NowMs);
            GameEngine.Require(block == null, block);
            GrantProduct(ctx, p);
            return CommandResult.Success();
        }

        private static CommandResult HandleClaimDaily(ShopClaimDaily cmd, CommandContext ctx)
        {
            var p = ctx.Content.Product(cmd.ProductId);
            GameEngine.Require(p != null && p.DailyRewards.Count > 0, "no daily rewards");
            GameEngine.Require(DailyGrantActive(ctx.State, ctx.Content, p.Id, ctx.NowMs), "not active");
            GameEngine.Require(CanClaimDaily(ctx.State, ctx.Content, p.Id, ctx.NowMs), "already claimed today");
            ctx.State.Shop.DailyGrants[p.Id].LastClaimDay = MetaTime.DayIndex(ctx.Content, ctx.NowMs);
            ctx.GrantAll(p.DailyRewards);
            return CommandResult.Success();
        }

        private static CommandResult HandleBuyItem(ShopBuyItem cmd, CommandContext ctx)
        {
            var item = ctx.Content.ShopItem(cmd.ItemId);
            GameEngine.Require(item != null, "unknown item");
            GameEngine.Require(item.Section != GuildSystem.ShopSection, "guild item: use GuildShopBuy");
            BuyItem(ctx, item, ctx.State.Shop.ItemBuys);
            return CommandResult.Success();
        }

        private static CommandResult HandleAdWatched(AdWatched cmd, CommandContext ctx)
        {
            var def = ctx.Content.AdPlacement(cmd.PlacementId);
            GameEngine.Require(def != null, "unknown ad placement");
            GameEngine.Require(def.Rewards.Count > 0, "placement is consumed by its owning system");
            ConsumeAdView(ctx, cmd.PlacementId, cmd.AdToken);
            return CommandResult.Success();
        }

        private static void ClaimSeasonLevel(CommandContext ctx, int level, bool premium)
        {
            var s = ctx.State;
            var content = ctx.Content;
            GameEngine.Require(Features.IsUnlocked(s, content, Features.SeasonPass), "feature locked");
            GameEngine.Require(level >= 1 && level <= content.SeasonPass.Levels.Count, "invalid level");
            GameEngine.Require(SeasonLevel(s, content) >= level, "level not reached");
            GameEngine.Require(!premium || HasPremiumSeason(s, content, ctx.NowMs), "premium not owned");
            string key = (premium ? "p:" : "f:") + level;
            GameEngine.Require(!s.Shop.SeasonClaims.Contains(key), "already claimed");
            s.Shop.SeasonClaims.Add(key);
            var def = content.SeasonPass.Levels[level - 1];
            ctx.GrantAll(premium ? def.Premium : def.Free);
        }

        private static CommandResult HandleSeasonClaim(SeasonPassClaim cmd, CommandContext ctx)
        {
            ClaimSeasonLevel(ctx, cmd.Level, cmd.Premium);
            return CommandResult.Success();
        }

        private static CommandResult HandleSeasonClaimAll(SeasonPassClaimAll cmd, CommandContext ctx)
        {
            var s = ctx.State;
            int reached = SeasonLevel(s, ctx.Content);
            bool premium = HasPremiumSeason(s, ctx.Content, ctx.NowMs);
            int claimed = 0;
            for (int lvl = 1; lvl <= reached; lvl++)
            {
                if (!s.Shop.SeasonClaims.Contains("f:" + lvl)) { ClaimSeasonLevel(ctx, lvl, false); claimed++; }
                if (premium && !s.Shop.SeasonClaims.Contains("p:" + lvl)) { ClaimSeasonLevel(ctx, lvl, true); claimed++; }
            }
            GameEngine.Require(claimed > 0, "nothing to claim");
            return CommandResult.Success(claimed);
        }

        private static CommandResult HandleFundClaim(FundClaim cmd, CommandContext ctx)
        {
            var s = ctx.State;
            var fund = ctx.Content.Fund(cmd.FundId);
            GameEngine.Require(fund != null, "unknown fund");
            GameEngine.Require(s.Shop.FundsOwned.Contains(fund.Id), "fund not owned");
            GameEngine.Require(cmd.Step >= 0 && cmd.Step < fund.Steps.Count, "invalid step");
            var step = fund.Steps[cmd.Step];
            GameEngine.Require(FundProgress(s, ctx.Content, fund) >= step.Target, "step not reached");
            string key = fund.Id + ":" + cmd.Step;
            GameEngine.Require(!s.Shop.FundClaims.Contains(key), "already claimed");
            s.Shop.FundClaims.Add(key);
            ctx.GrantAll(step.Rewards);
            return CommandResult.Success();
        }
    }
}
