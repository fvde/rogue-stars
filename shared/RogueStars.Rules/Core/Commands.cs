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
        public List<GameEvent> Events = new List<GameEvent>();

        public Rng Rng => State.Rng;

        public void Emit(string type, params string[] args)
        {
            var e = new GameEvent { Type = type };
            if (args != null) e.Args.AddRange(args);
            Events.Add(e);
        }

        public void Grant(Reward reward) => Engine.Rewards.Grant(this, reward);
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

        public static CommandResult Success(object payload = null) => new CommandResult { Ok = true, Payload = payload };
        public static CommandResult Fail(string error) => new CommandResult { Ok = false, Error = error };
    }

    /// <summary>Thrown by handlers for expected rule violations; turned into a failed result.</summary>
    public sealed class RuleException : Exception
    {
        public RuleException(string message) : base(message) { }
    }
}
