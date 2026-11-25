using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    public enum ProductKind { DiamondPack, StarterPack, RollingOffer, FlashOffer, GiftPack, Permanent, MonthlyPass, SeasonPass, Fund, SummonPack, SummonPass }

    /// <summary>
    /// A real-money product (or a free step of a rolling chain when <see cref="PriceUsdCents"/> is 0).
    /// Fixed catalogue: every player sees the same products and prices (no A/B, no spend-based offers).
    /// </summary>
    [Serializable]
    public sealed class ProductDef
    {
        public string Id;
        public string Name;
        public ProductKind Kind;
        /// <summary>Store SKU (Google Play / App Store).</summary>
        public string StoreProductId;
        public int PriceUsdCents;
        /// <summary>Granted once per purchase.</summary>
        public List<Reward> Contents = new List<Reward>();
        /// <summary>Extra on the very first purchase of this product (Diamond packs: "double first purchase").</summary>
        public List<Reward> FirstPurchaseBonus = new List<Reward>();
        /// <summary>Lifetime purchase limit (0 = unlimited).</summary>
        public int Limit;
        /// <summary>State flag set permanently on purchase (e.g. ent_no_ads).</summary>
        public string EntitlementFlag;
        /// <summary>Claimable once per day after purchase (gift packs, monthly pass, Daily Diamonds).</summary>
        public List<Reward> DailyRewards = new List<Reward>();
        /// <summary>Number of daily claims (0 with DailyRewards = forever).</summary>
        public int DailyDays;
        /// <summary>Rolling offers: chain id and step index.</summary>
        public string ChainId;
        public int ChainStep;
        /// <summary>Flash offers: purchasable only while the schedule is active.</summary>
        public ScheduleDef Schedule;
        /// <summary>Flash offers: purchases per occurrence (0 = unlimited).</summary>
        public int LimitPerOccurrence;
        /// <summary>Visibility gate (same for everyone; progress-based only).</summary>
        public RequirementSet Requires = new RequirementSet();
        /// <summary>Funds: fund track unlocked by this product.</summary>
        public string FundId;
        /// <summary>
        /// Summon event packs and pass (ProductKind.SummonPack / SummonPass): sold only while an event of this type runs for
        /// the player (SummonEventSystem.ProductBlockReason); the pass unlocks that event's premium track.
        /// </summary>
        public SummonEventType? SummonEvent;
        /// <summary>Purchases per reset day (0 = no daily limit).</summary>
        public int DailyLimit;
    }

    [Serializable]
    public sealed class RollingChainDef
    {
        public string Id;
        /// <summary>Product ids in order; buy (or claim, if free) step i to reveal step i+1.</summary>
        public List<string> Steps = new List<string>();
        public bool Loop = true;
    }

    public enum LimitPeriod { None, Daily, Weekly }

    /// <summary>Soft-currency shop item (Diamond shop, guild shop...). Limits reset at the daily/weekly reset.</summary>
    [Serializable]
    public sealed class ShopItemDef
    {
        public string Id;
        public string Name;
        /// <summary>"daily", "tokens", "guild".</summary>
        public string Section;
        public List<Cost> Price = new List<Cost>();
        public List<Reward> Contents = new List<Reward>();
        public int Limit;
        public LimitPeriod Period;
        public RequirementSet Requires = new RequirementSet();
    }

    /// <summary>Rewarded ad placement with a daily cap. With No Ads the view is instant (no token) but still capped.</summary>
    [Serializable]
    public sealed class AdPlacementDef
    {
        public string Id;
        public string Name;
        public int DailyCap;
        /// <summary>Direct rewards (empty when the effect is applied by the owning system, e.g. stamina refill).</summary>
        public List<Reward> Rewards = new List<Reward>();
    }

    [Serializable]
    public sealed class SeasonLevelDef
    {
        public int Level;
        public List<Reward> Free = new List<Reward>();
        public List<Reward> Premium = new List<Reward>();
    }

    /// <summary>4-week season pass: free + premium track; premium is retroactive.</summary>
    [Serializable]
    public sealed class SeasonPassDef
    {
        public long EpochMs;
        public long LengthMs = 28 * MetaTime.Day;
        public long XpPerLevel = 100;
        public List<SeasonLevelDef> Levels = new List<SeasonLevelDef>();
        /// <summary>Season XP per +1 of a counter during the season.</summary>
        public Dictionary<string, long> XpPerCounter = new Dictionary<string, long>();
        public string PremiumProductId;
    }

    [Serializable]
    public sealed class FundStepDef
    {
        public long Target;
        public List<Reward> Rewards = new List<Reward>();
    }

    /// <summary>One-time paid reward track per system; steps unlock by progress measured by <see cref="Measure"/>.</summary>
    [Serializable]
    public sealed class FundDef
    {
        public string Id;
        public string Name;
        public string ProductId;
        /// <summary>What is measured (Kind + Key; Target is ignored, steps define targets).</summary>
        public RequirementDef Measure = new RequirementDef();
        public List<FundStepDef> Steps = new List<FundStepDef>();
    }

    [Serializable]
    public sealed class ShopConfig
    {
        /// <summary>Allow PurchaseComplete with a fake receipt (offline/dev client). Must be false on the server.</summary>
        public bool AllowDevReceipts;
        public string DevReceiptPrefix = "dev:";
        public string NoAdsFlag = "ent_no_ads";
        /// <summary>
        /// Global "ads disabled" mode: every player is treated as owning the No Ads entitlement (every placement completes
        /// instantly, daily caps still apply). Set per deployment by the host (server <c>Ads:Mode=disabled</c>, client
        /// PlatformConfig / the server's session flag), never by content; default off.
        /// </summary>
        public bool AdsDisabled;
        public string BattleSpeedFlag = "battle_speed_3";
        public string MonthlyPassProductId = "monthly_pass";
        public long MonthlyPassStaminaCapBonus = 30;
        /// <summary>Production multiplier (all production) while the monthly pass is active.</summary>
        public double MonthlyPassProductionMultiplier = 1.2;
        /// <summary>Pending purchases older than this are dropped.</summary>
        public long PendingPurchaseTtlMs = 2 * MetaTime.Day;
    }

    public sealed partial class ContentDb
    {
        public readonly Dictionary<string, ProductDef> Products = new Dictionary<string, ProductDef>();
        public readonly Dictionary<string, RollingChainDef> RollingChains = new Dictionary<string, RollingChainDef>();
        public readonly Dictionary<string, ShopItemDef> ShopItems = new Dictionary<string, ShopItemDef>();
        public readonly Dictionary<string, AdPlacementDef> AdPlacements = new Dictionary<string, AdPlacementDef>();
        public readonly Dictionary<string, FundDef> Funds = new Dictionary<string, FundDef>();
        public readonly SeasonPassDef SeasonPass = new SeasonPassDef();
        public readonly ShopConfig Shop = new ShopConfig();

        public ProductDef Product(string id) => id != null && Products.TryGetValue(id, out var p) ? p : null;
        /// <summary>Section of the PvP (Arena Medal) shop items.</summary>
        public const string ArenaShopSection = "arena";

        public ShopItemDef ShopItem(string id) => id != null && ShopItems.TryGetValue(id, out var p) ? p : null;
        public AdPlacementDef AdPlacement(string id) => id != null && AdPlacements.TryGetValue(id, out var p) ? p : null;
        public FundDef Fund(string id) => id != null && Funds.TryGetValue(id, out var p) ? p : null;

        partial void BuildShop()
        {
            const long D = MetaTime.Day;
            Reward Dia(long n) => Reward.Cur(Currencies.Diamonds, n);
            ProductDef P(string id, string name, ProductKind kind, int cents, params Reward[] contents)
            {
                var p = new ProductDef { Id = id, Name = name, Kind = kind, PriceUsdCents = cents, StoreProductId = cents > 0 ? "rs." + id : null };
                p.Contents.AddRange(contents);
                Products[id] = p;
                return p;
            }

            // Diamond packs: fixed ladder, first purchase of each pack doubles the Diamonds.
            void Pack(string id, int cents, long dia) { var p = P(id, dia + " Diamonds", ProductKind.DiamondPack, cents, Dia(dia)); p.FirstPurchaseBonus.Add(Dia(dia)); }
            Pack("diamonds_60", 99, 60);
            Pack("diamonds_330", 499, 330);
            Pack("diamonds_700", 999, 700);
            Pack("diamonds_1450", 1999, 1450);
            Pack("diamonds_3800", 4999, 3800);
            Pack("diamonds_8000", 9999, 8000);

            // Starter packs (one-time).
            P("starter_worm", "Wriggle Starter", ProductKind.StarterPack, 99, Dia(300), Reward.Cur(Currencies.PartTokens, 10), Reward.Cur(Currencies.Stamina, 60)).Limit = 1;
            var st2 = P("starter_explorer", "Explorer Starter", ProductKind.StarterPack, 499, Dia(900), Reward.Cur(Currencies.BlueprintTokens, 10), Reward.Cur(Currencies.AlienTokens, 10), Reward.Cur(Currencies.Ore, 5000));
            st2.Limit = 1; st2.Requires = RequirementSet.All(ReqFeature(Features.Blueprints));

            // Rolling offer chain: free and paid steps alternate; looping.
            var chain = new RollingChainDef { Id = "rolling_main" };
            void Roll(int step, int cents, params Reward[] contents)
            {
                var p = P("rolling_main_" + step, "Rolling Offer " + (step + 1), ProductKind.RollingOffer, cents, contents);
                p.ChainId = chain.Id; p.ChainStep = step; chain.Steps.Add(p.Id);
            }
            Roll(0, 0, Dia(20));
            Roll(1, 199, Dia(250), Reward.Cur(Currencies.PartTokens, 5));
            Roll(2, 0, Reward.Cur(Currencies.Stamina, 20));
            Roll(3, 499, Dia(650), Reward.Cur(Currencies.BlueprintTokens, 8));
            Roll(4, 0, Reward.Cur(Currencies.GearCores, 50));
            Roll(5, 999, Dia(1400), Reward.Cur(Currencies.AlienTokens, 15));
            RollingChains[chain.Id] = chain;

            // Flash offers by schedule (same for everyone).
            var fl1 = P("flash_weekend_fuel", "Weekend Fuel Flash", ProductKind.FlashOffer, 299, Reward.Cur(Currencies.Fuel, 3000), Dia(150));
            fl1.Schedule = new ScheduleDef { StartMs = LiveOps.EpochMs + 5 * D, DurationMs = 2 * D, RecurrenceMs = 7 * D }; fl1.LimitPerOccurrence = 1;
            var fl2 = P("flash_midweek_ore", "Midweek Ore Flash", ProductKind.FlashOffer, 199, Reward.Cur(Currencies.Ore, 8000), Reward.Cur(Currencies.GearCores, 80));
            fl2.Schedule = new ScheduleDef { StartMs = LiveOps.EpochMs + 2 * D, DurationMs = 1 * D, RecurrenceMs = 7 * D }; fl2.LimitPerOccurrence = 1;

            // Periodic gift packs: instant part + daily part for N days.
            var g1 = P("gift_diamond_week", "Diamond Gift Week", ProductKind.GiftPack, 499, Dia(100));
            g1.DailyRewards.Add(Dia(120)); g1.DailyDays = 7;
            var g2 = P("gift_token_week", "Token Gift Week", ProductKind.GiftPack, 999, Reward.Cur(Currencies.PartTokens, 5));
            g2.DailyRewards.Add(Reward.Cur(Currencies.PartTokens, 3)); g2.DailyRewards.Add(Reward.Cur(Currencies.BlueprintTokens, 3)); g2.DailyDays = 7;

            // Permanent one-time purchases.
            var noAds = P("perm_no_ads", "No Ads", ProductKind.Permanent, 499);
            noAds.Limit = 1; noAds.EntitlementFlag = Shop.NoAdsFlag;
            var dd = P("perm_daily_diamonds", "Daily Diamonds Forever", ProductKind.Permanent, 999, Dia(300));
            dd.Limit = 1; dd.EntitlementFlag = "ent_daily_diamonds"; dd.DailyRewards.Add(Dia(50)); dd.DailyDays = 0;

            // Monthly pass: 30 days of daily Diamonds, battle speed ×3, stamina cap +, production +.
            var mp = P(Shop.MonthlyPassProductId, "Monthly Pass", ProductKind.MonthlyPass, 499, Dia(300));
            mp.DailyRewards.Add(Dia(100)); mp.DailyDays = 30;

            // Season pass premium (retroactive).
            SeasonPass.EpochMs = LiveOps.EpochMs;
            SeasonPass.PremiumProductId = "season_pass_premium";
            P(SeasonPass.PremiumProductId, "Season Pass Premium", ProductKind.SeasonPass, 999);
            SeasonPass.XpPerCounter["runs_completed"] = 20;
            SeasonPass.XpPerCounter["run_bosses_killed"] = 10;
            SeasonPass.XpPerCounter["arena_fights"] = 5;
            SeasonPass.XpPerCounter["invasion_battles"] = 10;
            for (int lvl = 1; lvl <= 30; lvl++)
            {
                var l = new SeasonLevelDef { Level = lvl };
                if (lvl % 5 == 0) { l.Free.Add(Reward.Cur(Currencies.PartTokens, 2)); l.Premium.Add(Dia(100)); l.Premium.Add(Reward.Cur(Currencies.PartTokens, 5)); }
                else { l.Free.Add(Reward.Cur(Currencies.Biomass, 500 * lvl)); l.Premium.Add(Dia(30)); l.Premium.Add(Reward.Cur(Currencies.Ore, 400 * lvl)); }
                SeasonPass.Levels.Add(l);
            }

            // Funds: one-time paid tracks per system.
            void Fund(string id, string name, int cents, RequirementKind kind, string key, long[] targets, long diamondsPerStep)
            {
                var p = P("fund_" + id, name, ProductKind.Fund, cents);
                p.Limit = 1; p.FundId = id; p.Requires = RequirementSet.All(ReqFeature(Features.Funds));
                var f = new FundDef { Id = id, Name = name, ProductId = p.Id, Measure = new RequirementDef(kind, key, 0) };
                foreach (var t in targets) f.Steps.Add(new FundStepDef { Target = t, Rewards = { Dia(diamondsPerStep) } });
                Funds[id] = f;
            }
            Fund("worm", "Worm Fund", 999, RequirementKind.WormLevel, null, new long[] { 10, 15, 20, 25, 30, 40, 50 }, 300);
            Fund("hero", "Hero Fund", 999, RequirementKind.Counter, "hero_stars", new long[] { 3, 6, 10, 15, 20, 30 }, 300);
            Fund("mount", "Mount Fund", 999, RequirementKind.Counter, "mount_levels", new long[] { 5, 10, 20, 30, 40 }, 300);
            Fund("city", "City Fund", 1999, RequirementKind.Counter, "city_building_upgrades", new long[] { 10, 25, 50, 75, 100, 150 }, 500);

            // Diamond shop items.
            void Item(string id, string name, string section, Cost price, Reward contents, int limit, LimitPeriod period)
            {
                var it = new ShopItemDef { Id = id, Name = name, Section = section, Limit = limit, Period = period };
                if (price != null) it.Price.Add(price);
                it.Contents.Add(contents);
                ShopItems[id] = it;
            }
            Item("daily_free_gift", "Daily Free Gift", "daily", null, Dia(10), 1, LimitPeriod.Daily);
            Item("daily_part_tokens", "Part Tokens ×3", "daily", new Cost(Currencies.Diamonds, 120), Reward.Cur(Currencies.PartTokens, 3), 3, LimitPeriod.Daily);
            Item("daily_blueprint_tokens", "Blueprint Tokens ×3", "daily", new Cost(Currencies.Diamonds, 150), Reward.Cur(Currencies.BlueprintTokens, 3), 3, LimitPeriod.Daily);
            Item("daily_alien_tokens", "Alien Tokens ×3", "daily", new Cost(Currencies.Diamonds, 150), Reward.Cur(Currencies.AlienTokens, 3), 3, LimitPeriod.Daily);
            Item("daily_fuel", "Fuel ×500", "daily", new Cost(Currencies.Diamonds, 50), Reward.Cur(Currencies.Fuel, 500), 5, LimitPeriod.Daily);
            Item("tokens_part_10", "Part Tokens ×10", "tokens", new Cost(Currencies.Diamonds, 1350), Reward.Cur(Currencies.PartTokens, 10), 5, LimitPeriod.Weekly);
            Item("tokens_blueprint_10", "Blueprint Tokens ×10", "tokens", new Cost(Currencies.Diamonds, 1500), Reward.Cur(Currencies.BlueprintTokens, 10), 5, LimitPeriod.Weekly);
            Item("tokens_alien_10", "Alien Tokens ×10", "tokens", new Cost(Currencies.Diamonds, 1500), Reward.Cur(Currencies.AlienTokens, 10), 5, LimitPeriod.Weekly);
            // Arena shop (Arena Medals, ShopBuyItem; needs the Arena). Awaken Cores are the endgame purchase (weekly limit);
            // the rest keeps medals useful once the limit is reached.
            void ArenaItem(string id, string name, long medals, Reward contents, int limit)
            {
                Item(id, name, ArenaShopSection, new Cost(Currencies.ArenaMedals, medals), contents, limit, LimitPeriod.Weekly);
                ShopItems[id].Requires = RequirementSet.All(ReqFeature(Features.Arena));
            }
            ArenaItem("arena_awaken_core", "Awaken Core", 200, Reward.Cur(ProgressionCurrencies.PartAwakenCores, 1), 2);
            ArenaItem("arena_part_tokens", "Part Tokens ×2", 60, Reward.Cur(Currencies.PartTokens, 2), 3);
            ArenaItem("arena_data", "Data ×300", 50, Reward.Cur(Currencies.Data, 300), 3);
            ArenaItem("arena_gear_cores", "Gear Cores ×150", 40, Reward.Cur(Currencies.GearCores, 150), 5);
            // Guild shop (guild_marks; bought through GuildShopBuy).
            Item("guild_ore", "Ore ×5000", "guild", new Cost(Currencies.GuildMarks, 100), Reward.Cur(Currencies.Ore, 5000), 5, LimitPeriod.Weekly);
            Item("guild_gear_cores", "Gear Cores ×100", "guild", new Cost(Currencies.GuildMarks, 150), Reward.Cur(Currencies.GearCores, 100), 5, LimitPeriod.Weekly);
            Item("guild_part_tokens", "Part Tokens ×2", "guild", new Cost(Currencies.GuildMarks, 300), Reward.Cur(Currencies.PartTokens, 2), 3, LimitPeriod.Weekly);
            Item("guild_fuel", "Fuel ×1500", "guild", new Cost(Currencies.GuildMarks, 400), Reward.Cur(Currencies.Fuel, 1500), 2, LimitPeriod.Weekly); // was Mount Cores ×5

            // Rewarded ad placements (daily caps). Effects without direct rewards are applied by the owning
            // system via ShopSystem.ConsumeAdView (stamina refill: Progression, revive/double: Run, timers: City).
            void Ad(string id, string name, int cap, params Reward[] rewards)
            {
                var a = new AdPlacementDef { Id = id, Name = name, DailyCap = cap };
                a.Rewards.AddRange(rewards);
                AdPlacements[id] = a;
            }
            Ad("stamina_refill", "Stamina refill", 3);
            Ad("revive", "Revive", 5);
            Ad("timer_speedup", "Timer −15%", 10);
            Ad("free_pull_part", "Extra free Part pull", 1);
            Ad("free_pull_blueprint", "Extra free Blueprint pull", 1);
            Ad("free_pull_alien", "Extra free Alien pull", 1);
            Ad("double_run_rewards", "Double run rewards", 3);
            Ad("daily_diamonds_ad", "Diamond snack", 5, Dia(10));
            Ad("shop_tokens_ad", "Token snack", 2, Reward.Cur(Currencies.PartTokens, 1));
        }
    }
}
