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
        public int AdRefillsPerDay = 3;
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
        /// <summary>Hero shards per day for the hero the player focuses, outside pulls (shard shop, events, season track).</summary>
        public long FocusHeroShardsPerDay = 2;
    }

    [Serializable]
    public sealed class EconomyConfig
    {
        /// <summary>Balances every new player starts with.</summary>
        public Dictionary<string, long> StartingWallet = new Dictionary<string, long>();
        public StaminaRefillConfig StaminaRefill = new StaminaRefillConfig();
        /// <summary>Length of an economy day; day 0 starts at Unix epoch 00:00 UTC.</summary>
        public long DayMs = 86_400_000;
        public F2pIncomeModel F2p = new F2pIncomeModel();
    }

    public sealed partial class ContentDb
    {
        /// <summary>Economy tuning: starting wallet, stamina refills, F2P model (Progression).</summary>
        public readonly EconomyConfig EconomyConfig = new EconomyConfig();

        partial void BuildEconomy()
        {
            RegenRules.Add(new RegenRule { Currency = Currencies.Stamina, BaseCap = 60, AmountPerTick = 1, TickMs = 6 * 60_000 });

            var w = EconomyConfig.StartingWallet;
            w[Currencies.Stamina] = 60;
            w[Currencies.Diamonds] = 300;
            w[Currencies.Biomass] = 500;
            w[Currencies.Ore] = 500;
            w[Currencies.Grain] = 300;
            w[Currencies.PartTokens] = 10;
            w[Currencies.BlueprintTokens] = 5;
            w[Currencies.AlienTokens] = 5;

            var s = EconomyConfig.StaminaRefill;
            s.Amount = 30;
            s.DiamondPrices.AddRange(new long[] { 50, 100, 150, 250, 400 });
            s.AdRefillsPerDay = 3;
            s.FreeRefillsPerDay = 1;
        }
    }
}
