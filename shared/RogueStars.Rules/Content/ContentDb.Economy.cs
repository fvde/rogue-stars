using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    [Serializable]
    public sealed class StaminaRefillConfig
    {
        /// <summary>Stamina granted per refill (any method).</summary>
        public long Amount = 30;
        /// <summary>Escalating Diamond price per refill of the day; the list length is the daily Diamond limit.</summary>
        public List<long> DiamondPrices = new List<long>();
        /// <summary>Meta ad placement consumed by ad refills (its DailyCap is the daily ad-refill limit).</summary>
        public string AdPlacementId = "stamina_refill";
        public int FreeRefillsPerDay = 1;
    }

    /// <summary>
    /// "Typical active F2P" income assumptions. Not read by systems; used by the long-haul economy test
    /// and documented in docs/design/economy-model.md. Keep both in sync.
    /// </summary>
    [Serializable]
    public sealed class F2pIncomeModel
    {
        /// <summary>Diamonds per day from quests, login, Diamond mine, arena and events.</summary>
        public long DiamondsPerDay = 300;
        /// <summary>Share of daily Diamonds an active F2P spends on pulls (rest: refills, timers, shop).</summary>
        public double DiamondShareForPulls = 0.6;
        /// <summary>Free pull tokens per pull type per day (daily/weekly quests, login, season pass free track, runs).</summary>
        public long TokensPerTypePerDay = 6;
        /// <summary>
        /// Hero summon event pulls per 6-day event (tasks ~24, free pass 10, ad ticket 6; simulator --summon measures it).
        /// Docs: docs/design/summon-events.md.
        /// </summary>
        public long HeroEventPulls = 40;
    }

    [Serializable]
    public sealed class EconomyConfig
    {
        /// <summary>Balances every new player starts with.</summary>
        public Dictionary<string, long> StartingWallet = new Dictionary<string, long>();
        public StaminaRefillConfig StaminaRefill = new StaminaRefillConfig();
        public F2pIncomeModel F2p = new F2pIncomeModel();
    }

    public sealed partial class ContentDb
    {
        /// <summary>Economy tuning: starting wallet, stamina refills, F2P model (Progression).</summary>
        public readonly EconomyConfig EconomyConfig = new EconomyConfig();

        partial void BuildEconomy()
        {
            RegenRules.Add(new RegenRule { Currency = Currencies.Stamina, BaseCap = 60, AmountPerTick = 1, TickMs = 6 * 60_000 });

            // Rewards follow use (GDD §13): only what day-1 features spend. The rest of the old starting wallet arrives
            // with the unlock that gives it a use (ContentDb.Onboarding: Ore with Parts, Biomass with the City, tokens
            // with their Signals, Grain with the Aliens).
            var w = EconomyConfig.StartingWallet;
            w[Currencies.Stamina] = 60;
            w[Currencies.Diamonds] = 300;

            var s = EconomyConfig.StaminaRefill;
            s.Amount = 30;
            s.DiamondPrices.AddRange(new long[] { 50, 100, 150, 250, 400 });
            s.FreeRefillsPerDay = 1;
        }
    }
}
