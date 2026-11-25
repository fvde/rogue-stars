using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    /// <summary>
    /// What gives a currency or an item kind a use (GDD §13 "Rewards follow use"): a reward is only granted while the
    /// player can spend or use it; before that it waits in the reward stash (<see cref="RewardGate"/>).
    /// </summary>
    [Serializable]
    public sealed class RewardUseDef
    {
        /// <summary>Currency id, a currency id prefix ending in '*' (per-entity shards, "shards_hero_*"), or null for an item kind.</summary>
        public string Currency;
        /// <summary>Item reward kind (<see cref="RewardKinds"/>, not Currency) when <see cref="Currency"/> is null.</summary>
        public string Kind;
        /// <summary>
        /// When the player has a use for it: any of these (features via <see cref="RequirementKind.Feature"/>, part rarity via
        /// the <c>part_rarity_max</c> counter...). Empty = always (stamina, Diamonds).
        /// </summary>
        public RequirementSet UsedWhen = new RequirementSet { Any = true };
        /// <summary>What it is spent on (docs, currency details).</summary>
        public string Use;
        /// <summary>
        /// Most the stash keeps from free sources (runs, quests, chests, login, events) before the use unlocks; the rest is
        /// dropped. Item kinds count rewards (the best rarities are kept). -1 = no limit. Paid and explicit sources
        /// (<see cref="RewardGateConfig.KeepAllCommands"/>) are always kept in full.
        /// </summary>
        public long StashCap = -1;

        public bool Matches(string currency) =>
            Currency != null && currency != null && (Currency.EndsWith("*", StringComparison.Ordinal)
                ? currency.StartsWith(Currency.Substring(0, Currency.Length - 1), StringComparison.Ordinal)
                : Currency == currency);
    }

    [Serializable]
    public sealed class RewardGateConfig
    {
        /// <summary>
        /// Commands whose rewards are kept in full while their use is locked (paid purchases, pass/fund claims, items
        /// bought with a currency, mail, pulls): what the player paid for or was explicitly sent is never capped.
        /// </summary>
        public List<string> KeepAllCommands = new List<string>();
    }

    public sealed partial class ContentDb
    {
        /// <summary>Every wallet currency (exact or shard prefix) and every item reward kind with what gives it a use.</summary>
        public readonly List<RewardUseDef> RewardUses = new List<RewardUseDef>();
        public readonly RewardGateConfig GateConfig = new RewardGateConfig();

        /// <summary>The use entry of a wallet currency (exact id first, then shard prefixes); null if unmapped.</summary>
        public RewardUseDef CurrencyUse(string currency)
        {
            RewardUseDef prefix = null;
            foreach (var u in RewardUses)
            {
                if (u.Currency == null) continue;
                if (u.Currency == currency) return u;
                if (prefix == null && u.Matches(currency)) prefix = u;
            }
            return prefix;
        }

        /// <summary>The use entry of an item reward kind; null if unmapped.</summary>
        public RewardUseDef ItemUse(string kind)
        {
            foreach (var u in RewardUses) if (u.Currency == null && u.Kind == kind) return u;
            return null;
        }

        /// <summary>The use entry of a reward (currency or item kind).</summary>
        public RewardUseDef RewardUse(Reward r) =>
            r == null ? null : r.Kind == RewardKinds.Currency ? CurrencyUse(r.Id) : ItemUse(r.Kind);

        partial void BuildRewardGate()
        {
            RequirementDef F(string feature) => new RequirementDef(RequirementKind.Feature, feature, 1);
            RequirementDef PartMin(PartRarity r) => new RequirementDef(RequirementKind.Counter, "part_rarity_max", (int)r, "own a " + r + " part");
            void Cur(string currency, string use, long cap, params RequirementDef[] when) =>
                RewardUses.Add(new RewardUseDef { Currency = currency, Use = use, StashCap = cap, UsedWhen = RequirementSet.AnyOf(when) });
            void Item(string kind, string use, long cap, params RequirementDef[] when) =>
                RewardUses.Add(new RewardUseDef { Kind = kind, Use = use, StashCap = cap, UsedWhen = RequirementSet.AnyOf(when) });
            long RegenCap(string currency, long fallback) => Regen(currency)?.BaseCap ?? fallback;

            // Sinks verified against the rules (the handler that spends it and its feature gate). Caps: roughly what the
            // first uses after the unlock need, so the unlock releases a small stash, not a backlog.
            Cur(Currencies.Stamina, "Runs", -1);
            Cur(Currencies.Diamonds, "Stamina refills, revives, timers, pulls, shop", -1);
            Cur(Currencies.Biomass, "Worm levels and talents, city buildings and areas", 5000, F(Features.BiomassFarm), F(Features.WormLevel));
            Cur(Currencies.Ore, "Part levels and merges, the Replicator, city buildings", 1000, F(Features.Parts), F(Features.BiomassFarm), F(Features.OreMine));
            // Part levels above 10 cost Gear Cores: a part past level 10 needs Uncommon+ (cap 15); the Forge spends them too.
            Cur(Currencies.GearCores, "Part levels above 10, the Forge", 200, PartMin(PartRarity.Uncommon), F(Features.Forge));
            // Cap raises: Rare+ parts (MaxCapRaises > 0).
            Cur(Currencies.AdvancementPlates, "Part level cap raises (Rare+)", 20, PartMin(PartRarity.Rare));
            Cur(ProgressionCurrencies.PartAwakenCores, "Part awakening (Mythic+)", -1, PartMin(PartRarity.Mythic));
            Cur(Currencies.Grain, "Breeding", 2000, F(Features.Aliens), F(Features.Breeding)); // aliens pass: breeding is Grain's only sink
            Cur(Currencies.Fuel, "Mount levels", 4000, F(Features.Mounts));
            Cur(Currencies.HeroXp, "Hero levels", 3000, F(Features.Heroes));
            Cur(Currencies.Data, "Laboratory research", 100, F(Features.Laboratory));
            Cur(Currencies.PartTokens, "Part Signal pulls", 30, F(Features.PartPulls));
            Cur(Currencies.AlienTokens, "Alien Signal pulls", 30, F(Features.AlienPulls));
            Cur(Currencies.BlueprintTokens, "Blueprint Signal pulls", 30, F(Features.Blueprints));
            Cur(Currencies.HeroTickets, "Hero summon event pulls", 30, F(Features.Heroes));
            Cur(Currencies.MountTickets, "Mount summon event pulls", 30, F(Features.Mounts));
            Cur(Currencies.HeroSigils, "Hero summon exchange shop", -1, F(Features.Heroes));
            Cur(Currencies.MountBolts, "Mount summon exchange shop", -1, F(Features.Mounts));
            Cur(Currencies.ArenaTickets, "Arena fights", RegenCap(Currencies.ArenaTickets, Arena.StartTickets), F(Features.Arena));
            Cur(Currencies.ArenaMedals, "Arena shop", -1, F(Features.Arena));
            Cur(Currencies.BurrowKeys, "Burrow runs", RegenCap(Currencies.BurrowKeys, 2), F(Features.Burrow));
            Cur(Currencies.InvasionAttempts, "Invasion battles", LiveOps.InvasionAttemptsPerDay, F(Features.Invasions));
            Cur(Currencies.GuildMarks, "Guild shop", -1, F(Features.Guilds));
            Cur(Currencies.HeroShards("*"), "Hero unlock and stars", -1, F(Features.Heroes));
            Cur(Currencies.AlienShards("*"), "Alien stars", -1, F(Features.Aliens));
            Cur(Currencies.BlueprintShards("*"), "Building stars", -1, F(Features.BiomassFarm));
            Cur(Currencies.MountShards("*"), "Mount unlock and stars", -1, F(Features.Mounts));

            Item(RewardKinds.Part, "Worm parts", -1, F(Features.Parts));
            Item(RewardKinds.Rune, "Part sockets", 5, F(Features.Runes));
            Item(RewardKinds.Blueprint, "City buildings", -1, F(Features.BiomassFarm));
            Item(RewardKinds.Alien, "Squad and building staff", -1, F(Features.Aliens));
            Item(RewardKinds.HeroShards, "Heroes", -1, F(Features.Heroes));
            Item(RewardKinds.Hero, "Heroes (a copy: unlock or +1 star)", -1, F(Features.Heroes));
            Item(RewardKinds.Mount, "Mounts", -1, F(Features.Mounts));
            Item(RewardKinds.Skin, "Cosmetic", -1);

            GateConfig.KeepAllCommands.AddRange(new[]
            {
                "PurchaseComplete", "ShopClaimDaily", "ShopBuyItem", "SeasonPassClaim", "SeasonPassClaimAll", "FundClaim",
                "MailClaim", "MailClaimAll", "GachaPull", "SummonPull", "SummonExchangeBuy", "SummonPassClaim", "SummonPassClaimAll",
            });
        }
    }
}
