using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Generic timers (construction, upgrades, breeding, research). Owners start timers with <see cref="Start"/>,
    /// and either register an auto-completion handler (<see cref="RegisterCompletion"/>; runs when the timer is
    /// due during settle or right after a speed-up) or poll <see cref="IsDone"/> and <see cref="Remove"/> the timer
    /// themselves (e.g. breeding requires an explicit collect).
    ///
    /// Speed-ups (<see cref="TimerSpeedUp"/>):
    ///  - Diamonds: cost = max(1, ceil(remaining minutes × TimerTuning.DiamondsPerMinute)); completes the timer.
    ///  - Ad: removes AdReductionPct (15%) of the remaining time, at most MaxAdsPerTimer (3) per timer, and consumes
    ///    one view of Meta's ad placement TimerTuning.AdPlacementId (daily cap, ad token via ShopSystem.ConsumeAdView).
    ///    With No Ads it completes instantly without an ad (<see cref="AdRequired"/>); the rule effect is identical.
    ///  - Free: FreeSpeedUpsPerDay per reset-day (GameClock.DayIndex), removes FreeSpeedUpMs (0 = completes).
    /// </summary>
    public static class TimerSystem
    {
        public const long DayMs = GameClock.Day;

        private static readonly ConditionalWeakTable<GameEngine, Dictionary<string, Action<CommandContext, GameTimer>>> Completions =
            new ConditionalWeakTable<GameEngine, Dictionary<string, Action<CommandContext, GameTimer>>>();

        public static void Register(GameEngine e)
        {
            e.Register<TimerSpeedUp>(HandleSpeedUp);
            e.RegisterSettler(CompleteDue);
            // Auto-completing timers change the state at their end (a building starts producing, a boon applies).
            e.RegisterBreakpoints((s, c, from, to) =>
            {
                long next = long.MaxValue;
                if (!Completions.TryGetValue(e, out var map)) return next;
                foreach (var t in s.Timers.Active)
                {
                    if (t.EndsMs <= from || t.EndsMs >= next) continue;
                    lock (map) { if (map.ContainsKey(t.Kind)) next = t.EndsMs; }
                }
                return next;
            });
        }

        /// <summary>
        /// Register a handler that runs when a timer of <paramref name="kind"/> is due. The timer is removed
        /// after the handler returns. Handlers run in EndsMs order; they may read timer.EndsMs as the completion time.
        /// </summary>
        public static void RegisterCompletion(GameEngine e, string kind, Action<CommandContext, GameTimer> handler)
        {
            var map = Completions.GetValue(e, _ => new Dictionary<string, Action<CommandContext, GameTimer>>());
            lock (map) map[kind] = handler;
        }

        // ---- API for owners ---------------------------------------------------------------------

        /// <summary>Start a timer. Duration is used as given (owners apply their own speed bonuses).</summary>
        public static GameTimer Start(CommandContext ctx, string kind, string refId, long durationMs)
        {
            var ts = ctx.State.Timers;
            ts.NextId++;
            var t = new GameTimer
            {
                Id = "t" + ts.NextId, Kind = kind, RefId = refId, StartMs = ctx.NowMs,
                DurationMs = Math.Max(0, durationMs), EndsMs = ctx.NowMs + Math.Max(0, durationMs),
            };
            ts.Active.Add(t);
            ctx.Emit("timer_started", t.Id, kind, refId ?? "", t.EndsMs.ToString());
            return t;
        }

        public static GameTimer Get(GameState state, string timerId)
        {
            if (timerId == null) return null;
            foreach (var t in state.Timers.Active) if (t.Id == timerId) return t;
            return null;
        }

        /// <summary>First active timer of a kind for a reference, or null.</summary>
        public static GameTimer Find(GameState state, string kind, string refId)
        {
            foreach (var t in state.Timers.Active) if (t.Kind == kind && t.RefId == refId) return t;
            return null;
        }

        public static bool IsDone(GameState state, string timerId, long nowMs)
        {
            var t = Get(state, timerId);
            return t != null && t.EndsMs <= nowMs;
        }

        public static long Remaining(GameTimer t, long nowMs) => t == null ? 0 : Math.Max(0, t.EndsMs - nowMs);

        /// <summary>Remove (consume/cancel) a timer.</summary>
        public static bool Remove(GameState state, string timerId) => state.Timers.Active.RemoveAll(t => t.Id == timerId) > 0;

        /// <summary>Published Diamond price to finish a timer with this much time left.</summary>
        public static long DiamondCost(ContentDb content, long remainingMs)
        {
            if (remainingMs <= 0) return 0;
            double minutes = remainingMs / 60_000.0;
            return Math.Max(1, (long)Math.Ceiling(minutes * content.TimerTuning.DiamondsPerMinute - 1e-9));
        }

        /// <summary>False when the player owns the no-ads entitlement: ad speed-ups apply instantly.</summary>
        public static bool AdRequired(GameState state, ContentDb content) => !ShopSystem.HasNoAds(state, content);

        public static int AdsLeft(GameTimer t, ContentDb content) => t == null ? 0 : Math.Max(0, content.TimerTuning.MaxAdsPerTimer - t.AdsUsed);

        public static int FreeSpeedUpsLeft(GameState state, ContentDb content, long nowMs)
        {
            int perDay = content.TimerTuning.FreeSpeedUpsPerDay;
            if (perDay <= 0) return 0;
            return state.Timers.FreeDay == GameClock.DayIndex(content, nowMs) ? Math.Max(0, perDay - state.Timers.FreeUsed) : perDay;
        }

        /// <summary>Run completion handlers of all due timers (EndsMs order, then id). Safe to call repeatedly.</summary>
        public static void CompleteDue(CommandContext ctx)
        {
            if (!Completions.TryGetValue(ctx.Engine, out var map)) return;
            while (true)
            {
                GameTimer next = null;
                Action<CommandContext, GameTimer> handler = null;
                foreach (var t in ctx.State.Timers.Active)
                {
                    if (t.EndsMs > ctx.NowMs) continue;
                    Action<CommandContext, GameTimer> h;
                    lock (map) { if (!map.TryGetValue(t.Kind, out h)) continue; }
                    if (next == null || t.EndsMs < next.EndsMs || (t.EndsMs == next.EndsMs && string.CompareOrdinal(t.Id, next.Id) < 0))
                    {
                        next = t; handler = h;
                    }
                }
                if (next == null) return;
                Remove(ctx.State, next.Id);
                handler(ctx, next);
                ctx.Emit("timer_completed", next.Id, next.Kind, next.RefId ?? "");
            }
        }

        // ---- command ------------------------------------------------------------------------

        private static CommandResult HandleSpeedUp(TimerSpeedUp cmd, CommandContext ctx)
        {
            var content = ctx.Content;
            var tuning = content.TimerTuning;
            var t = Get(ctx.State, cmd.TimerId);
            GameEngine.Require(t != null, "unknown timer");
            long remaining = Remaining(t, ctx.NowMs);
            GameEngine.Require(remaining > 0, "timer already done");

            switch (cmd.Method)
            {
                case SpeedUpMethod.Diamonds:
                {
                    long cost = DiamondCost(content, remaining);
                    GameEngine.Require(ctx.State.Wallet.TrySpend(Currencies.Diamonds, cost), "not enough diamonds");
                    t.EndsMs = ctx.NowMs;
                    ctx.State.Count("timer_speedups_diamond");
                    ctx.State.Count("diamonds_spent_timers", cost);
                    break;
                }
                case SpeedUpMethod.Ad:
                {
                    GameEngine.Require(t.AdsUsed < tuning.MaxAdsPerTimer, "no ad speed-ups left for this timer");
                    ShopSystem.ConsumeAdView(ctx, tuning.AdPlacementId, cmd.AdToken);
                    long cut = (long)Math.Ceiling(remaining * tuning.AdReductionPct / 100.0);
                    t.EndsMs -= cut;
                    t.AdsUsed++;
                    ctx.State.Count("timer_speedups_ad");
                    break;
                }
                case SpeedUpMethod.Free:
                {
                    GameEngine.Require(FreeSpeedUpsLeft(ctx.State, content, ctx.NowMs) > 0, "no free speed-up left today");
                    long day = GameClock.DayIndex(content, ctx.NowMs);
                    if (ctx.State.Timers.FreeDay != day) { ctx.State.Timers.FreeDay = day; ctx.State.Timers.FreeUsed = 0; }
                    ctx.State.Timers.FreeUsed++;
                    t.EndsMs = tuning.FreeSpeedUpMs <= 0 ? ctx.NowMs : Math.Max(ctx.NowMs, t.EndsMs - tuning.FreeSpeedUpMs);
                    ctx.State.Count("timer_speedups_free");
                    break;
                }
                default:
                    throw new RuleException("unknown speed-up method");
            }
            t.SpeedUps++;
            ctx.Emit("timer_sped_up", t.Id, cmd.Method.ToString(), t.EndsMs.ToString());
            CompleteDue(ctx);
            return CommandResult.Success();
        }
    }
}
