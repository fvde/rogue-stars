using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Regen-bonus provider: returns extra cap and tick-speed percent for one regenerating currency.
    /// Args: state, content, nowMs, currency id. Return (0, 0) for currencies the provider does not affect.
    /// </summary>
    public delegate (long capBonus, double tickSpeedPercent) RegenBonusProvider(GameState state, ContentDb content, long nowMs, string currency);

    /// <summary>
    /// Economy: settles all regenerating currencies before every command (cap/tick bonuses recomputed from
    /// registered providers and "regen:&lt;currency&gt;" / "stamina_regen" modifiers),
    /// seeds the new-player wallet and sells stamina refills.
    /// </summary>
    public static class EconomySystem
    {
        private static readonly ConditionalWeakTable<GameEngine, List<RegenBonusProvider>> Providers =
            new ConditionalWeakTable<GameEngine, List<RegenBonusProvider>>();

        public static void Register(GameEngine e)
        {
            Providers.GetValue(e, _ => new List<RegenBonusProvider>());
            e.RegisterNewPlayer(InitNewPlayer);
            e.RegisterSettler(ctx => Settle(ctx.Engine, ctx.State, ctx.Content, ctx.NowMs));
            e.Register<BuyStamina>(HandleBuyStamina);
        }

        /// <summary>
        /// Register a regen bonus source (City buildings, Meta passes, talents...). Bonuses from all providers
        /// are summed and written into Wallet.Regen[currency] before every settle.
        /// </summary>
        public static void RegisterRegenBonusProvider(GameEngine e, RegenBonusProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            Providers.GetValue(e, _ => new List<RegenBonusProvider>()).Add(provider);
        }

        /// <summary>Day index used for daily limits: the shared reset boundary (<see cref="GameClock.DayIndex"/>).</summary>
        public static long DayIndex(ContentDb c, long nowMs) => GameClock.DayIndex(c, nowMs);

        /// <summary>Current cap of a regenerating currency including all bonuses (as of the last settle).</summary>
        public static long RegenCap(GameState s, ContentDb c, string currency)
        {
            var rule = c.Regen(currency);
            return rule == null ? 0 : s.Wallet.Cap(rule);
        }

        /// <summary>
        /// Recompute bonuses and settle every regen rule. Safe to call at any time. Expired modifiers are not
        /// removed here: City integrates production piecewise over modifier windows in a later settler, and
        /// LiveOps (registered after City) removes expired modifiers.
        /// </summary>
        public static void Settle(GameEngine e, GameState s, ContentDb c, long nowMs)
        {
            var providers = e != null && Providers.TryGetValue(e, out var list) ? list : null;
            foreach (var rule in c.RegenRules)
            {
                long cap = 0; double speed = 0;
                if (providers != null)
                    foreach (var p in providers)
                    {
                        var b = p(s, c, nowMs, rule.Currency);
                        cap += b.capBonus; speed += b.tickSpeedPercent;
                    }
                // Timed modifiers: a multiplier m on regen speed shortens the tick to 1/m.
                double mult = ModifierMath.Multiplier(s.Modifiers, "regen:" + rule.Currency, nowMs);
                if (rule.Currency == Currencies.Stamina) mult *= ModifierMath.Multiplier(s.Modifiers, "stamina_regen", nowMs);
                if (mult > 0 && mult != 1) speed += (1 - 1 / mult) * 100;

                if (!s.Wallet.Regen.TryGetValue(rule.Currency, out var rs))
                {
                    rs = new RegenState { LastTickMs = nowMs };
                    s.Wallet.Regen[rule.Currency] = rs;
                }
                rs.CapBonus = cap;
                rs.TickSpeedPercent = speed;
                // No regeneration while the currency has no use (arena tickets before the Arena...): no backlog either; the
                // unlock releases the stashed starting amount (RewardGate).
                if (!RewardGate.CurrencyHasUse(s, c, rule.Currency, nowMs)) { rs.LastTickMs = nowMs; continue; }
                s.Wallet.SettleRegen(rule, nowMs);
            }
            RollDay(s, c, nowMs);
        }

        private static void RollDay(GameState s, ContentDb c, long nowMs)
        {
            long day = DayIndex(c, nowMs);
            var es = s.Economy;
            if (es.DayIndex == day) return;
            es.DayIndex = day;
            es.StaminaDiamondRefillsToday = 0;
            es.StaminaAdRefillsToday = 0;
            es.StaminaFreeRefillsToday = 0;
        }

        private static void InitNewPlayer(CommandContext ctx)
        {
            // Sorted for determinism of any side effects (none today, but keep the rule).
            var keys = new List<string>(ctx.Content.EconomyConfig.StartingWallet.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) ctx.Grant(Reward.Cur(k, ctx.Content.EconomyConfig.StartingWallet[k])); // through the reward gate
            Settle(ctx.Engine, ctx.State, ctx.Content, ctx.NowMs);
        }

        /// <summary>Today's refill options (call after a settle, e.g. engine.Settle).</summary>
        public static StaminaRefillInfo GetStaminaRefillInfo(GameState s, ContentDb c, long nowMs)
        {
            var cfg = c.EconomyConfig.StaminaRefill;
            var es = s.Economy;
            bool today = es.DayIndex == DayIndex(c, nowMs);
            int dia = today ? es.StaminaDiamondRefillsToday : 0;
            int free = today ? es.StaminaFreeRefillsToday : 0;
            return new StaminaRefillInfo
            {
                Amount = cfg.Amount,
                NextDiamondPrice = dia < cfg.DiamondPrices.Count ? cfg.DiamondPrices[dia] : -1,
                DiamondRefillsLeft = Math.Max(0, cfg.DiamondPrices.Count - dia),
                AdRefillsLeft = ShopSystem.AdViewsLeft(s, c, cfg.AdPlacementId, nowMs),
                FreeRefillsLeft = Math.Max(0, cfg.FreeRefillsPerDay - free),
                AdsInstant = ShopSystem.HasNoAds(s, c),
            };
        }

        private static CommandResult HandleBuyStamina(BuyStamina cmd, CommandContext ctx)
        {
            var s = ctx.State; var cfg = ctx.Content.EconomyConfig.StaminaRefill; var es = s.Economy;
            long spent = 0;
            switch (cmd.Method)
            {
                case StaminaRefillMethod.Diamonds:
                    GameEngine.Require(es.StaminaDiamondRefillsToday < cfg.DiamondPrices.Count, "daily diamond refill limit reached");
                    spent = cfg.DiamondPrices[es.StaminaDiamondRefillsToday];
                    ProgressionUtil.Spend(s, Currencies.Diamonds, spent);
                    es.StaminaDiamondRefillsToday++;
                    s.Count("stamina_refills_diamonds");
                    break;
                case StaminaRefillMethod.Ad:
                    // Daily cap, ad token and No Ads (instant) are Meta's ad placement rules.
                    ShopSystem.ConsumeAdView(ctx, cfg.AdPlacementId, cmd.AdToken);
                    es.StaminaAdRefillsToday++;
                    s.Count("stamina_refills_ad");
                    break;
                case StaminaRefillMethod.DailyFree:
                    GameEngine.Require(es.StaminaFreeRefillsToday < cfg.FreeRefillsPerDay, "daily free refill already claimed");
                    es.StaminaFreeRefillsToday++;
                    break;
                default:
                    throw new RuleException("unknown refill method");
            }
            s.Wallet.Add(Currencies.Stamina, cfg.Amount);
            s.Count("stamina_refills");
            ctx.Emit("stamina_refilled", cmd.Method.ToString(), cfg.Amount.ToString());
            return CommandResult.Success(new StaminaRefillResult { Gained = cfg.Amount, Stamina = s.Wallet.Get(Currencies.Stamina), DiamondsSpent = spent });
        }
    }
}
