using System;
using System.Collections.Generic;

namespace RogueStars.Rules.Core
{
    /// <summary>Well-known currency ids. Per-entity shard currencies use the helpers below.</summary>
    public static class Currencies
    {
        public const string Biomass = "biomass";
        public const string Grain = "grain";
        public const string Ore = "ore";
        public const string GearCores = "gear_cores";
        public const string AdvancementPlates = "advancement_plates";
        public const string Fuel = "fuel";
        public const string HeroXp = "hero_xp";
        public const string Diamonds = "diamonds";
        public const string BlueprintTokens = "blueprint_tokens";
        public const string PartTokens = "part_tokens";
        public const string AlienTokens = "alien_tokens";
        public const string Stamina = "stamina";
        public const string ArenaTickets = "arena_tickets";
        public const string BurrowKeys = "burrow_keys";
        public const string InvasionAttempts = "invasion_attempts";
        public const string GuildMarks = "guild_marks";
        public const string MountCores = "mount_cores";

        public static string HeroShards(string heroId) => "shards_hero_" + heroId;
        public static string AlienShards(string alienTypeId) => "shards_alien_" + alienTypeId;
        public static string BlueprintShards(string blueprintId) => "shards_bp_" + blueprintId;
        public static string MountShards(string mountId) => "shards_mount_" + mountId;
    }

    /// <summary>Regeneration rule for a currency (stamina, tickets...). Rewards may push a balance above the cap.</summary>
    [Serializable]
    public sealed class RegenRule
    {
        public string Currency;
        public long BaseCap;
        public long AmountPerTick = 1;
        public long TickMs;
    }

    [Serializable]
    public sealed class RegenState
    {
        public long LastTickMs;
        /// <summary>Extra cap from buildings, talents, passes (recomputed by systems).</summary>
        public long CapBonus;
        /// <summary>Tick length reduction in percent from buildings (recomputed by systems).</summary>
        public double TickSpeedPercent;
    }

    /// <summary>One string-keyed wallet for every resource.</summary>
    [Serializable]
    public sealed class Wallet
    {
        public Dictionary<string, long> Balances = new Dictionary<string, long>();
        public Dictionary<string, RegenState> Regen = new Dictionary<string, RegenState>();

        public long Get(string currency) => Balances.TryGetValue(currency, out var v) ? v : 0;

        public void Add(string currency, long amount)
        {
            if (amount == 0) return;
            long next = Get(currency) + amount;
            if (next < 0) throw new InvalidOperationException("Negative balance for " + currency);
            Balances[currency] = next;
        }

        public bool CanAfford(string currency, long amount) => amount <= 0 || Get(currency) >= amount;

        public bool TrySpend(string currency, long amount)
        {
            if (!CanAfford(currency, amount)) return false;
            if (amount > 0) Balances[currency] = Get(currency) - amount;
            return true;
        }

        public bool CanAfford(IList<Cost> costs)
        {
            if (costs == null) return true;
            var totals = new Dictionary<string, long>();
            foreach (var c in costs) totals[c.Currency] = (totals.TryGetValue(c.Currency, out var t) ? t : 0) + c.Amount;
            foreach (var kv in totals) if (!CanAfford(kv.Key, kv.Value)) return false;
            return true;
        }

        public bool TrySpend(IList<Cost> costs)
        {
            if (!CanAfford(costs)) return false;
            if (costs != null) foreach (var c in costs) TrySpend(c.Currency, c.Amount);
            return true;
        }

        public long Cap(RegenRule rule) => rule.BaseCap + (Regen.TryGetValue(rule.Currency, out var r) ? r.CapBonus : 0);

        /// <summary>
        /// Settle regeneration lazily from timestamps: whole ticks only, remainder carried,
        /// no regen while at or above the cap (timer restarts when the cap is reached).
        /// </summary>
        public void SettleRegen(RegenRule rule, long nowMs)
        {
            if (!Regen.TryGetValue(rule.Currency, out var rs)) { rs = new RegenState { LastTickMs = nowMs }; Regen[rule.Currency] = rs; }
            long cap = Cap(rule);
            long tick = Math.Max(1000, (long)(rule.TickMs * (1 - Math.Min(90, rs.TickSpeedPercent) / 100.0)));
            long bal = Get(rule.Currency);
            if (bal >= cap) { rs.LastTickMs = nowMs; return; }
            long elapsed = nowMs - rs.LastTickMs;
            if (elapsed < tick) return;
            long ticks = elapsed / tick;
            long gained = Math.Min(ticks * rule.AmountPerTick, cap - bal);
            Balances[rule.Currency] = bal + gained;
            rs.LastTickMs = (bal + gained >= cap) ? nowMs : rs.LastTickMs + ticks * tick;
        }
    }

    [Serializable]
    public sealed class Cost
    {
        public string Currency;
        public long Amount;
        public Cost() { }
        public Cost(string currency, long amount) { Currency = currency; Amount = amount; }
    }
}
