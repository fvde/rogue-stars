using System;
using System.Collections.Generic;

namespace RogueStars.Rules.Core
{
    /// <summary>Reward kinds. Systems register a granter for each kind they own (see RewardRegistry).</summary>
    public static class RewardKinds
    {
        public const string Currency = "currency";     // Id = currency id
        public const string Part = "part";             // Id = part def id, Amount = copies
        public const string Rune = "rune";             // Id = rune def id
        public const string Blueprint = "blueprint";   // Id = blueprint def id
        public const string Alien = "alien";           // Id = alien type id
        public const string HeroShards = "hero_shards";// Id = hero id
        public const string Mount = "mount";           // Id = mount id
        public const string Skin = "skin";             // cosmetic
    }

    [Serializable]
    public sealed class Reward
    {
        public string Kind = RewardKinds.Currency;
        public string Id;
        public long Amount = 1;
        /// <summary>Optional rarity for generated items (part/rune rarity index).</summary>
        public int Rarity;

        public Reward() { }
        public Reward(string kind, string id, long amount, int rarity = 0) { Kind = kind; Id = id; Amount = amount; Rarity = rarity; }
        public static Reward Cur(string currency, long amount) => new Reward(RewardKinds.Currency, currency, amount);
        public override string ToString() => Kind + ":" + Id + " x" + Amount;
    }

    public delegate void RewardGranter(CommandContext ctx, Reward reward);

    /// <summary>Dispatches reward grants to the owning system. Currency is built in.</summary>
    public sealed class RewardRegistry
    {
        private readonly Dictionary<string, RewardGranter> _granters = new Dictionary<string, RewardGranter>();

        public RewardRegistry()
        {
            Register(RewardKinds.Currency, (ctx, r) => ctx.State.Wallet.Add(r.Id, r.Amount));
        }

        public void Register(string kind, RewardGranter granter) => _granters[kind] = granter;

        public bool CanGrant(string kind) => _granters.ContainsKey(kind);

        public void Grant(CommandContext ctx, Reward reward)
        {
            if (reward == null || reward.Amount <= 0) return;
            if (!_granters.TryGetValue(reward.Kind, out var g)) throw new InvalidOperationException("No granter for reward kind " + reward.Kind);
            g(ctx, reward);
            ctx.Emit("reward", reward.Kind, reward.Id, reward.Amount.ToString());
        }

        public void GrantAll(CommandContext ctx, IEnumerable<Reward> rewards)
        {
            if (rewards == null) return;
            foreach (var r in rewards) Grant(ctx, r);
        }
    }
}
