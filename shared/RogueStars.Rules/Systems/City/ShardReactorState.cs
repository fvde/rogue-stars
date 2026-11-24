using System;
using System.Collections.Generic;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        /// <summary>Shard Reactor jobs (City workstream, ShardReactorSystem).</summary>
        public ShardReactorState Reactor = new ShardReactorState();
    }
}

namespace RogueStars.Rules.Systems
{
    /// <summary>What a Shard Reactor job converts: alien shards or blueprint shards (always into the same kind).</summary>
    public enum ReactorKind { Alien = 0, Blueprint = 1 }

    [Serializable]
    public sealed class ReactorJob
    {
        public string Id;
        public ReactorKind Kind;
        public string SourceId;
        public string TargetId;
        public int Units;
        /// <summary>Source shards and fuel paid at the start, target shards paid at the collect.</summary>
        public long SourceShards;
        public string FuelCurrency;
        public long Fuel;
        public long OutputShards;
        public string TimerId;
        public long StartMs;
    }

    [Serializable]
    public sealed class ShardReactorState
    {
        public List<ReactorJob> Jobs = new List<ReactorJob>();
        public int NextId;
    }

    /// <summary>A job's price before it starts (screen preview and the start handler).</summary>
    [Serializable]
    public sealed class ReactorQuote
    {
        /// <summary>Null when the job is possible in principle; otherwise why not (wrong kind, not a higher rarity, …).</summary>
        public string Error;
        public long SourceShards;
        public string FuelCurrency;
        public long Fuel;
        public long OutputShards;
        public long DurationMs;
    }
}
