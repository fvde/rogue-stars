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
        private readonly List<Func<GameState, ContentDb, long, long, long>> _breakpoints = new List<Func<GameState, ContentDb, long, long, long>>();
        private readonly List<Action<GameState, ContentDb, long, CombatLoadout>> _loadout =
            new List<Action<GameState, ContentDb, long, CombatLoadout>>();

        /// <summary>Optional deep copy used to make commands atomic (set by host: JSON round-trip).</summary>
        public Func<GameState, GameState> Cloner;

        public GameEngine(ContentDb content = null)
        {
            Content = content ?? ContentDb.Default;
            // Modifier windows: production, regen and every other multiplier change at their start and end.
            RegisterBreakpoints((s, c, from, to) =>
            {
                long next = long.MaxValue;
                foreach (var m in s.Modifiers)
                {
                    if (m.StartMs > from && m.StartMs < next) next = m.StartMs;
                    if (m.EndMs > from && m.EndMs < next) next = m.EndMs;
                }
                return next;
            });
            // Daily resets (quests, refills, ad caps, account age): whatever changes with the day changes at the reset.
            RegisterBreakpoints((s, c, from, to) => GameClock.DayStartMs(c, GameClock.DayIndex(c, from) + 1));
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

        /// <summary>
        /// Registers a breakpoint source for settling: given (state, content, fromMs, toMs) it returns the earliest time in
        /// (fromMs, toMs) at which time-driven state changes discretely (a modifier starts or ends, a timer completes, an event
        /// rolls over), or long.MaxValue. Settling walks every breakpoint in order before the target time (see <see cref="Settle"/>).
        /// </summary>
        public void RegisterBreakpoints(Func<GameState, ContentDb, long, long, long> next) => _breakpoints.Add(next);

        /// <summary>Runs once when a new player is created.</summary>
        public void RegisterNewPlayer(Action<CommandContext> init) => _newPlayer.Add(init);

        /// <summary>Contributes to the combat loadout (worm stats, hero, mount, aliens, building boons...).</summary>
        public void RegisterLoadoutContributor(Action<GameState, ContentDb, long, CombatLoadout> contributor) => _loadout.Add(contributor);

        public Type CommandType(string name) => _commandTypes.TryGetValue(name, out var t) ? t : null;
        public IEnumerable<string> CommandNames => _commandTypes.Keys;

        // ---- execution ----------------------------------------------------------------------

        public GameState NewPlayer(string playerId, long nowMs, long seed)
        {
            var state = new GameState { PlayerId = playerId, CreatedMs = nowMs, LastCommandMs = nowMs, SettledMs = nowMs, Rng = Rng.FromSeed(seed) };
            var ctx = new CommandContext { State = state, Content = Content, Engine = this, NowMs = nowMs };
            foreach (var init in _newPlayer) init(ctx);
            RunSettlers(ctx, nowMs, false); // like after a command: the new state is settled at its creation time
            return state;
        }

        /// <summary>
        /// Settle time-based state (regen, production, timers, modifiers, events) up to <paramref name="nowMs"/> without a
        /// command. Path-independent: settling at t1 and then at t2 gives exactly the state of settling once at t2 (the
        /// local-first client and the server settle at different moments and must still agree, docs/server.md). To keep it so,
        /// the settlers run at every breakpoint between the last settle and <paramref name="nowMs"/> first (so discrete changes
        /// always happen at their own time, never at whichever moment somebody settled), accumulators are exact (integer
        /// ticks and carries), and nothing a settle does depends on when it ran except through those times. Settling at a time
        /// at or before the last settle changes nothing.
        /// </summary>
        public void Settle(GameState state, long nowMs)
        {
            var ctx = new CommandContext { State = state, Content = Content, Engine = this, NowMs = nowMs };
            SettleTo(ctx, nowMs, false);
        }

        /// <summary>Runs the settlers at every breakpoint before <paramref name="nowMs"/>, then at <paramref name="nowMs"/>.</summary>
        private void SettleTo(CommandContext ctx, long nowMs, bool acting)
        {
            var state = ctx.State;
            if (state.SettledMs == 0) state.SettledMs = state.LastCommandMs;
            if (nowMs < state.SettledMs && !acting) return;
            for (int guard = 0; guard < MaxBreakpoints; guard++)
            {
                long bp = NextBreakpoint(state, state.SettledMs, nowMs);
                if (bp >= nowMs) break;
                RunSettlers(ctx, bp, false);
            }
            RunSettlers(ctx, nowMs, acting);
        }

        /// <summary>Upper bound of breakpoint steps per settle (a player away for years; real gaps need a few dozen).</summary>
        public const int MaxBreakpoints = 20000;

        private long NextBreakpoint(GameState state, long fromMs, long toMs)
        {
            long next = long.MaxValue;
            foreach (var b in _breakpoints)
            {
                long t = b(state, Content, fromMs, toMs);
                if (t > fromMs && t < next) next = t;
            }
            return next;
        }

        private void RunSettlers(CommandContext ctx, long atMs, bool acting)
        {
            ctx.NowMs = atMs;
            ctx.Acting = acting;
            foreach (var s in _settlers) s(ctx);
            if (atMs > ctx.State.SettledMs) ctx.State.SettledMs = atMs;
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
                SettleTo(ctx, nowMs, true);
                result = handler(command, ctx) ?? CommandResult.Success();
                // Settle again at the command's time: whatever the command made due now (feature unlocks, releases, joins)
                // happens in this command, so the stored state is fully settled at its time and later settles only see time.
                RunSettlers(ctx, nowMs, false);
                ctx.NowMs = nowMs;
                ctx.Acting = true;
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
