using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public delegate CommandResult CommandHandler<in T>(T command, CommandContext ctx) where T : GameCommand;

    /// <summary>
    /// Executes commands against a GameState. The same engine runs on the client (offline/prediction)
    /// and on the server (authoritative). Systems register their handlers in Systems.RegisterAll.
    /// </summary>
    public sealed class GameEngine
    {
        public readonly ContentDb Content;
        public readonly RewardRegistry Rewards = new RewardRegistry();

        private readonly Dictionary<string, Func<GameCommand, CommandContext, CommandResult>> _handlers =
            new Dictionary<string, Func<GameCommand, CommandContext, CommandResult>>();
        private readonly Dictionary<string, Type> _commandTypes = new Dictionary<string, Type>();
        private readonly List<Action<CommandContext>> _settlers = new List<Action<CommandContext>>();
        private readonly List<Action<CommandContext>> _newPlayer = new List<Action<CommandContext>>();
        private readonly List<Action<GameState, ContentDb, long, CombatLoadout>> _loadout =
            new List<Action<GameState, ContentDb, long, CombatLoadout>>();

        /// <summary>Optional deep copy used to make commands atomic (set by host: JSON round-trip).</summary>
        public Func<GameState, GameState> Cloner;

        public GameEngine(ContentDb content = null)
        {
            Content = content ?? ContentDb.Default;
            Register<SettleTime>((c, ctx) => CommandResult.Success()); // the settlers ran already (Execute)
            SystemsRegistry.RegisterAll(this);
        }

        // ---- registration -------------------------------------------------------------------

        public void Register<T>(CommandHandler<T> handler) where T : GameCommand
        {
            string name = typeof(T).Name;
            _commandTypes[name] = typeof(T);
            _handlers[name] = (cmd, ctx) => handler((T)cmd, ctx);
        }

        /// <summary>Runs before every command (settle regen, production, expire modifiers...).</summary>
        public void RegisterSettler(Action<CommandContext> settler) => _settlers.Add(settler);

        /// <summary>Runs once when a new player is created.</summary>
        public void RegisterNewPlayer(Action<CommandContext> init) => _newPlayer.Add(init);

        /// <summary>Contributes to the combat loadout (worm stats, hero, mount, aliens, building boons...).</summary>
        public void RegisterLoadoutContributor(Action<GameState, ContentDb, long, CombatLoadout> contributor) => _loadout.Add(contributor);

        public Type CommandType(string name) => _commandTypes.TryGetValue(name, out var t) ? t : null;
        public IEnumerable<string> CommandNames => _commandTypes.Keys;

        // ---- execution ----------------------------------------------------------------------

        public GameState NewPlayer(string playerId, long nowMs, long seed)
        {
            var state = new GameState { PlayerId = playerId, CreatedMs = nowMs, LastCommandMs = nowMs, Rng = Rng.FromSeed(seed) };
            var ctx = new CommandContext { State = state, Content = Content, Engine = this, NowMs = nowMs };
            foreach (var init in _newPlayer) init(ctx);
            return state;
        }

        /// <summary>Settle time-based state (regen, production, modifiers) without a command.</summary>
        public void Settle(GameState state, long nowMs)
        {
            var ctx = new CommandContext { State = state, Content = Content, Engine = this, NowMs = nowMs };
            foreach (var s in _settlers) s(ctx);
        }

        /// <summary>
        /// Execute a command. On failure the state is unchanged when a Cloner is set; without a Cloner,
        /// handlers must validate before mutating (convention for all handlers anyway).
        /// Returns the resulting state (a new instance when cloning).
        /// </summary>
        public CommandResult Execute(ref GameState state, GameCommand command, long nowMs) => Execute(ref state, command, nowMs, null);

        /// <summary>Execute with a battle resolver (server: local-first battle claims and sampled re-simulation).</summary>
        public CommandResult Execute(ref GameState state, GameCommand command, long nowMs, BattleResolver battles)
        {
            if (command == null) return CommandResult.Fail("null command");
            if (!_handlers.TryGetValue(command.Type, out var handler)) return CommandResult.Fail("unknown command " + command.Type);

            GameState working = Cloner != null ? Cloner(state) : state;
            if (Cloner != null) RunState.CarryBattleLogs(state, working);
            var ctx = new CommandContext { State = working, Content = Content, Engine = this, NowMs = nowMs, Command = command, BattleResolver = battles };
            CommandResult result;
            try
            {
                foreach (var s in _settlers) s(ctx);
                result = handler(command, ctx) ?? CommandResult.Success();
            }
            catch (RuleException ex)
            {
                result = CommandResult.Fail(ex.Message);
            }

            if (!result.Ok)
            {
                result.Events.Clear();
                return result;
            }
            working.Version++;
            working.LastCommandMs = nowMs;
            result.Events.AddRange(ctx.Events);
            result.Battles = ctx.Battles;
            state = working;
            return result;
        }

        /// <summary>Build the full combat loadout (used by runs, arena snapshots and invasions).</summary>
        public CombatLoadout BuildLoadout(GameState state, long nowMs)
        {
            var loadout = new CombatLoadout { PlayerId = state.PlayerId, DisplayName = state.DisplayName };
            foreach (var c in _loadout) c(state, Content, nowMs, loadout);
            return loadout;
        }

        public static void Require(bool condition, string error)
        {
            if (!condition) throw new RuleException(error);
        }
    }
}
