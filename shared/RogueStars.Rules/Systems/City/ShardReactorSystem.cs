using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Start a Shard Reactor job: Units × OutputPerUnit shards of TargetId from shards of SourceId (same kind, the target of a higher
    /// rarity), paid with source shards + fuel at the start. Needs a free job (one per alien working in the Reactor). Payload: job id.
    /// </summary>
    [Serializable] public sealed class ReactorStart : GameCommand { public ReactorKind Kind; public string SourceId; public string TargetId; public int Units = 1; }

    /// <summary>Collect a finished job (JobId empty = every finished job): the target shards are paid; an item not owned yet unlocks at 50.</summary>
    [Serializable] public sealed class ReactorCollect : GameCommand { public string JobId; }

    /// <summary>
    /// Shard Reactor (City; docs/design/pull-system.md "Shard Reactor"): the Mythic building that turns excess shards into shards of a
    /// higher rarity of the same type, of an item the player picks. Parallel jobs = aliens working in the Reactor (its worker slots:
    /// 1, +1 at 3★, +1 at 6★). Flow: ReactorStart (source shards + fuel paid, timer kind "reactor_job", speed-ups via TimerSpeedUp)
    /// → ReactorCollect (target shards paid; a target not owned yet unlocks once its shards reach the unlock cost).
    /// Counters: reactor_jobs_started, reactor_shards_in, reactor_shards_out.
    /// </summary>
    public static class ShardReactorSystem
    {
        public const string TimerKind = "reactor_job";

        public static void Register(GameEngine e)
        {
            e.Register<ReactorStart>(HandleStart);
            e.Register<ReactorCollect>(HandleCollect);
        }

        // ---- queries ----------------------------------------------------------------------------

        public static OwnedBlueprint Building(GameState s, ContentDb c) => CitySystem.Owned(s, c.ShardReactor.BlueprintId);

        /// <summary>Worker slots of the Reactor (0 when not owned).</summary>
        public static int WorkerSlots(GameState s, ContentDb c)
        {
            var b = Building(s, c);
            return b == null ? 0 : CitySystem.WorkerSlots(c, c.Blueprint(b.Id), b);
        }

        /// <summary>Jobs that can run at once: aliens working in the active Reactor (at most its worker slots).</summary>
        public static int JobSlots(GameState s, ContentDb c)
        {
            var b = Building(s, c);
            if (!CitySystem.IsActive(b)) return 0;
            return Math.Min(WorkerSlots(s, c), CitySystem.Workers(s, b.Id).Count);
        }

        public static ReactorJob Job(GameState s, string id)
        {
            foreach (var j in s.Reactor.Jobs) if (j.Id == id) return j;
            return null;
        }

        public static bool IsDone(GameState s, ReactorJob j, long nowMs) => TimerSystem.Get(s, j.TimerId) == null || TimerSystem.IsDone(s, j.TimerId, nowMs);

        public static string ShardCurrency(ReactorKind kind, string id) => kind == ReactorKind.Alien ? Currencies.AlienShards(id) : Currencies.BlueprintShards(id);

        /// <summary>The rarity of an item of the kind, or null when unknown.</summary>
        public static Rarity? RarityOf(ContentDb c, ReactorKind kind, string id)
        {
            if (kind == ReactorKind.Alien) { var a = c.AlienType(id); return a?.Rarity; }
            var b = c.Blueprint(id);
            return b?.Rarity;
        }

        /// <summary>Stars of an owned item (0 = not owned).</summary>
        public static int StarsOf(GameState s, ReactorKind kind, string id) =>
            kind == ReactorKind.Alien ? AlienSystem.Owned(s, id)?.Stars ?? 0 : CitySystem.Owned(s, id)?.Stars ?? 0;

        /// <summary>Shards of an item beyond what it still needs to reach 10★ (what the Reactor is for).</summary>
        public static long ExcessShards(GameState s, ContentDb c, ReactorKind kind, string id)
        {
            long have = s.Wallet.Get(ShardCurrency(kind, id));
            return Math.Max(0, have - c.ShardStars.Remaining(StarsOf(s, kind, id)));
        }

        /// <summary>Item ids of a kind in content order (rarity, then id).</summary>
        public static List<string> Items(ContentDb c, ReactorKind kind)
        {
            var ids = kind == ReactorKind.Alien ? new List<string>(c.AlienTypes.Keys) : new List<string>(c.Blueprints.Keys);
            ids.Sort((x, y) =>
            {
                int r = ((int)RarityOf(c, kind, x).Value).CompareTo((int)RarityOf(c, kind, y).Value);
                return r != 0 ? r : string.CompareOrdinal(x, y);
            });
            return ids;
        }

        /// <summary>Targets the Reactor accepts for a source: same kind, a higher rarity, not at 10★.</summary>
        public static List<string> Targets(GameState s, ContentDb c, ReactorKind kind, string sourceId)
        {
            var list = new List<string>();
            var src = RarityOf(c, kind, sourceId);
            if (src == null) return list;
            foreach (var id in Items(c, kind))
                if ((int)RarityOf(c, kind, id).Value > (int)src.Value && StarsOf(s, kind, id) < c.ShardStars.MaxStars) list.Add(id);
            return list;
        }

        /// <summary>Speed-up percent from the Reactor's level boon.</summary>
        public static double SpeedPct(GameState s, ContentDb c) => CitySystem.BoonTotal(s, c, BoonKind.ReactorSpeedPct);

        /// <summary>The price of a job (source shards, fuel, output, duration); Error set when the pairing is not allowed.</summary>
        public static ReactorQuote Quote(GameState s, ContentDb c, ReactorKind kind, string sourceId, string targetId, int units)
        {
            var d = c.ShardReactor;
            var q = new ReactorQuote();
            var sr = RarityOf(c, kind, sourceId);
            var tr = RarityOf(c, kind, targetId);
            if (sr == null || tr == null) { q.Error = "unknown item"; return q; }
            int gap = (int)tr.Value - (int)sr.Value;
            if (gap <= 0) { q.Error = "the target must be of a higher rarity"; return q; }
            if (units < 1 || units > d.MaxUnits) { q.Error = "units must be 1 to " + d.MaxUnits; return q; }
            int r = (int)tr.Value;
            q.OutputShards = units * d.OutputPerUnit;
            q.SourceShards = q.OutputShards * d.RatioByGap[Math.Min(gap, d.RatioByGap.Length - 1)];
            q.FuelCurrency = kind == ReactorKind.Alien ? Currencies.Grain : Currencies.Ore;
            q.Fuel = units * (kind == ReactorKind.Alien ? d.AlienFuelPerUnit[r] : d.BlueprintFuelPerUnit[r]);
            double ms = units * d.MinutesPerUnit[r] * 60_000.0 / (1 + SpeedPct(s, c) / 100.0);
            q.DurationMs = (long)Math.Ceiling(ms);
            return q;
        }

        // ---- handlers ---------------------------------------------------------------------------

        private static CommandResult HandleStart(ReactorStart cmd, CommandContext ctx)
        {
            var s = ctx.State; var c = ctx.Content;
            GameEngine.Require(cmd.Kind == ReactorKind.Alien || cmd.Kind == ReactorKind.Blueprint, "unknown kind");
            if (cmd.Kind == ReactorKind.Alien) Features.Require(s, c, Features.Aliens);
            GameEngine.Require(CitySystem.IsActive(Building(s, c)), "no Shard Reactor");
            GameEngine.Require(cmd.SourceId != cmd.TargetId, "pick another target");
            var q = Quote(s, c, cmd.Kind, cmd.SourceId, cmd.TargetId, cmd.Units);
            GameEngine.Require(q.Error == null, q.Error);
            GameEngine.Require(StarsOf(s, cmd.Kind, cmd.TargetId) < c.ShardStars.MaxStars, "target is at max stars");
            int slots = JobSlots(s, c);
            GameEngine.Require(slots > 0, "no alien works in the Shard Reactor");
            GameEngine.Require(s.Reactor.Jobs.Count < slots, "all reactor jobs busy");
            GameEngine.Require(s.Wallet.TrySpend(new List<Cost>
            {
                new Cost(ShardCurrency(cmd.Kind, cmd.SourceId), q.SourceShards),
                new Cost(q.FuelCurrency, q.Fuel),
            }), "not enough shards or fuel");

            var st = s.Reactor;
            st.NextId++;
            var job = new ReactorJob
            {
                Id = "reactor" + st.NextId, Kind = cmd.Kind, SourceId = cmd.SourceId, TargetId = cmd.TargetId, Units = cmd.Units,
                SourceShards = q.SourceShards, FuelCurrency = q.FuelCurrency, Fuel = q.Fuel, OutputShards = q.OutputShards, StartMs = ctx.NowMs,
            };
            job.TimerId = TimerSystem.Start(ctx, TimerKind, job.Id, q.DurationMs).Id;
            st.Jobs.Add(job);
            s.Count("reactor_jobs_started");
            s.Count("reactor_shards_in", q.SourceShards);
            ctx.Emit("reactor_started", job.Id, cmd.SourceId, cmd.TargetId);
            return CommandResult.Success(job.Id);
        }

        private static CommandResult HandleCollect(ReactorCollect cmd, CommandContext ctx)
        {
            var s = ctx.State;
            var done = new List<ReactorJob>();
            if (!string.IsNullOrEmpty(cmd.JobId))
            {
                var j = Job(s, cmd.JobId);
                GameEngine.Require(j != null, "unknown reactor job");
                GameEngine.Require(IsDone(s, j, ctx.NowMs), "job not finished");
                done.Add(j);
            }
            else foreach (var j in s.Reactor.Jobs) if (IsDone(s, j, ctx.NowMs)) done.Add(j);
            GameEngine.Require(done.Count > 0, "nothing to collect");
            long total = 0;
            foreach (var j in done)
            {
                TimerSystem.Remove(s, j.TimerId);
                s.Reactor.Jobs.Remove(j);
                s.Wallet.Add(ShardCurrency(j.Kind, j.TargetId), j.OutputShards);
                total += j.OutputShards;
                s.Count("reactor_shards_out", j.OutputShards);
                bool unlocked = j.Kind == ReactorKind.Alien ? AlienSystem.TryUnlockFromShards(ctx, j.TargetId) : CitySystem.TryUnlockFromShards(ctx, j.TargetId);
                ctx.Emit("reactor_collected", j.Id, j.TargetId, j.OutputShards.ToString(), unlocked ? "unlocked" : "");
            }
            return CommandResult.Success(total);
        }
    }
}
