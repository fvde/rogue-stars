using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;

namespace RogueStars.Rules.Core
{
    /// <summary>
    /// Base class for every player action. Concrete commands are plain serializable classes with public
    /// fields; the wire name is the class name (see GameEngine.CommandType).
    /// </summary>
    [Serializable]
    public abstract class GameCommand
    {
        public string Type => GetType().Name;
        /// <summary>Client-generated id for idempotency on the server.</summary>
        public string RequestId;
    }

    [Serializable]
    public sealed class GameEvent
    {
        public string Type;
        public List<string> Args = new List<string>();
        public override string ToString() => Type + "(" + string.Join(",", Args) + ")";
    }

    /// <summary>Everything a handler may touch. Handlers must only mutate ctx.State.</summary>
    public sealed class CommandContext
    {
        public GameState State;
        public ContentDb Content;
        public GameEngine Engine;
        public long NowMs;
        /// <summary>The command being executed (null for settles and new-player initialisation).</summary>
        public GameCommand Command;
        /// <summary>
        /// True while settling at the command's own time and running its handler: the player acts now. False for plain
        /// settles and for the catch-up settles at earlier breakpoints (things that need the player to be present, such as
        /// the returning-player gift or taking part in an event, check this instead of the time).
        /// </summary>
        public bool Acting;
        /// <summary>Dev tools only (offline grant cheats): rewards land at once, even without a use (no reward stash).</summary>
        public bool IgnoreRewardGate;
        public List<GameEvent> Events = new List<GameEvent>();
        /// <summary>Resolves battles (null = simulate). The server sets it for local-first commands (claims + sampling).</summary>
        public Systems.BattleResolver BattleResolver;
        /// <summary>Battles fought in this command, in order (full results with logs).</summary>
        public List<Systems.FoughtBattle> Battles = new List<Systems.FoughtBattle>();

        /// <summary>
        /// Fights a battle: the resolver's result (or a fresh simulation) is recorded in <see cref="Battles"/>. Every run battle
        /// goes through here so client and server agree on which battle is which (index = order within the command).
        /// </summary>
        public Systems.BattleResult ResolveBattle(Systems.BattleSetup setup, Rng rng, string kind)
        {
            int index = Battles.Count;
            var claimed = BattleResolver != null ? BattleResolver(this, setup, rng, kind, index) : null;
            var result = claimed ?? Systems.BattleSimulator.Simulate(setup, Content, rng);
            Battles.Add(new Systems.FoughtBattle { Kind = kind, Tier = Systems.FoughtBattle.TierOf(kind), Result = result, Claimed = claimed != null && claimed.Log.Count == 0 });
            return result;
        }

        public Rng Rng => State.Rng;

        public void Emit(string type, params string[] args)
        {
            var e = new GameEvent { Type = type };
            if (args != null) e.Args.AddRange(args);
            Events.Add(e);
        }

        /// <summary>Grants a reward; false when it went to the reward stash instead (no use yet, see RewardGate).</summary>
        public bool Grant(Reward reward) => Engine.Rewards.Grant(this, reward);
        public void GrantAll(IEnumerable<Reward> rewards) => Engine.Rewards.GrantAll(this, rewards);
    }

    [Serializable]
    public sealed class CommandResult
    {
        public bool Ok;
        public string Error;
        public List<GameEvent> Events = new List<GameEvent>();
        /// <summary>Optional structured payload for the caller (e.g. pull results, battle log id).</summary>
        public object Payload;
        /// <summary>Battles fought by the command, in order, with full logs (client playback; local-first battle claims).</summary>
        [NonSerialized] public List<Systems.FoughtBattle> Battles = new List<Systems.FoughtBattle>();

        public static CommandResult Success(object payload = null) => new CommandResult { Ok = true, Payload = payload };
        public static CommandResult Fail(string error) => new CommandResult { Ok = false, Error = error };
    }

    /// <summary>Thrown by handlers for expected rule violations; turned into a failed result.</summary>
    public sealed class RuleException : Exception
    {
        public RuleException(string message) : base(message) { }
    }
}
