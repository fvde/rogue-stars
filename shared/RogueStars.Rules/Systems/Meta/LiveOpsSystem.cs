using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Live ops: global calendar of Invasions (weekly + surprise) and Planetary events (GDD §9).
    /// - Planetary event modifiers are written into state.Modifiers by a settler for the event window.
    /// - Invasions: daily attempts (currency invasion_attempts), server-reported damage → contribution score,
    ///   server-applied group outcome → lasting modifiers + contribution-rank rewards.
    /// Battles are never run here: the server runs BattleSimulator and reports damage (InvasionRecordDamage).
    /// Events: "planetary_event"(eventId, occurrenceId), "invasion_outcome"(invasionId, occurrenceId, repelled|overrun).
    /// </summary>
    public static class LiveOpsSystem
    {
        public static void Register(GameEngine e)
        {
            e.RegisterSettler(Settle);
            e.RegisterNewPlayer(ctx => Settle(ctx));
            e.Register<InvasionStartAttempt>(HandleStartAttempt);
            e.Register<InvasionRecordDamage>(HandleRecordDamage);
            e.Register<LiveOpsApplyOutcome>(HandleApplyOutcome);
            e.Register<LiveOpsStartSurpriseInvasion>(HandleStartSurprise);
        }

        // ---- queries ---------------------------------------------------------------------------

        public static string OccurrenceId(string eventId, long startMs) => eventId + "@" + startMs;

        /// <summary>All live events active at <paramref name="nowMs"/> (calendar + server-pushed surprises), by start time.</summary>
        public static List<ActiveLiveEvent> ActiveEvents(GameState state, ContentDb content, long nowMs)
        {
            var list = new List<ActiveLiveEvent>();
            foreach (var ev in content.LiveOpsSchedule)
                if (ev.Schedule.TryActive(nowMs, out long s, out long e) && OccurrenceRuns(content, ev, s)) list.Add(Make(content, ev, s, e));
            if (state != null)
                foreach (var si in state.LiveOps.ServerInvasions)
                    if (nowMs >= si.StartMs && nowMs < si.EndMs)
                        list.Add(new ActiveLiveEvent
                        {
                            OccurrenceId = si.OccurrenceId, EventId = si.OccurrenceId, Kind = LiveOpsKind.Invasion, RefId = si.InvasionId,
                            Name = content.Invasion(si.InvasionId)?.Name, StartMs = si.StartMs, EndMs = si.EndMs, Surprise = true,
                        });
            list.Sort((a, b) => a.StartMs != b.StartMs ? a.StartMs.CompareTo(b.StartMs) : string.CompareOrdinal(a.OccurrenceId, b.OccurrenceId));
            return list;
        }

        /// <summary>Announced events starting within the horizon (surprise invasions stay hidden).</summary>
        public static List<ActiveLiveEvent> UpcomingEvents(ContentDb content, long nowMs, long horizonMs)
        {
            var list = new List<ActiveLiveEvent>();
            foreach (var ev in content.LiveOpsSchedule)
            {
                if (!ev.Announced) continue;
                long from = nowMs;
                for (int guard = 0; guard < 16 && ev.Schedule.TryNext(from, out long s, out long e) && s <= nowMs + horizonMs; guard++)
                {
                    if (OccurrenceRuns(content, ev, s)) { list.Add(Make(content, ev, s, e)); break; }
                    from = s;
                }
            }
            list.Sort((a, b) => a.StartMs != b.StartMs ? a.StartMs.CompareTo(b.StartMs) : string.CompareOrdinal(a.OccurrenceId, b.OccurrenceId));
            return list;
        }

        /// <summary>
        /// Whether the calendar occurrence of <paramref name="ev"/> starting at <paramref name="startMs"/> really runs.
        /// Planetary events always do. An invasion occurrence is skipped when an overlapping occurrence that takes
        /// precedence (announced before surprise, then earlier start, then calendar order) and itself runs is of the
        /// same invasion type, or when <see cref="LiveOpsConfig.MaxConcurrentInvasions"/> such occurrences overlap it.
        /// Pure and deterministic (content only), so client, server and simulator agree on the calendar.
        /// </summary>
        public static bool OccurrenceRuns(ContentDb content, LiveOpsEventDef ev, long startMs) => OccurrenceRuns(content, ev, startMs, 0);

        private const int PrecedenceDepth = 6;

        private static bool OccurrenceRuns(ContentDb content, LiveOpsEventDef ev, long startMs, int depth)
        {
            if (ev.Kind != LiveOpsKind.Invasion) return true;
            long endMs = startMs + ev.Schedule.DurationMs;
            var schedule = content.LiveOpsSchedule;
            int index = schedule.IndexOf(ev);
            int running = 0;
            for (int i = 0; i < schedule.Count; i++)
            {
                var other = schedule[i];
                if (other.Kind != LiveOpsKind.Invasion) continue;
                foreach (long os in other.Schedule.Overlapping(startMs, endMs))
                {
                    if (i == index && os == startMs) continue;
                    if (!Precedes(other, os, i, ev, startMs, index)) continue;
                    // Beyond the depth guard a preceding occurrence counts as running (skips more, never doubles up).
                    if (depth < PrecedenceDepth && !OccurrenceRuns(content, other, os, depth + 1)) continue;
                    if (other.RefId == ev.RefId) return false;
                    running++;
                }
            }
            return running < Math.Max(1, content.LiveOps.MaxConcurrentInvasions);
        }

        private static bool Precedes(LiveOpsEventDef a, long aStart, int aIndex, LiveOpsEventDef b, long bStart, int bIndex)
        {
            if (a.Announced != b.Announced) return a.Announced;
            if (aStart != bStart) return aStart < bStart;
            return aIndex < bIndex;
        }

        /// <summary>
        /// Why an extra (server-pushed) invasion of <paramref name="typeId"/> over [startMs, endMs) may not run next to
        /// the calendar and the player's server-pushed invasions: same type already running, or the concurrency cap
        /// reached. Null when it may run.
        /// </summary>
        public static string InvasionConflict(GameState state, ContentDb content, string typeId, long startMs, long endMs)
        {
            int overlapping = 0;
            foreach (var ev in content.LiveOpsSchedule)
            {
                if (ev.Kind != LiveOpsKind.Invasion) continue;
                foreach (long os in ev.Schedule.Overlapping(startMs, endMs))
                {
                    if (!OccurrenceRuns(content, ev, os)) continue;
                    if (ev.RefId == typeId) return "invasion type already running";
                    overlapping++;
                }
            }
            if (state != null)
                foreach (var si in state.LiveOps.ServerInvasions)
                    if (si.StartMs < endMs && startMs < si.EndMs)
                    {
                        if (si.InvasionId == typeId) return "invasion type already running";
                        overlapping++;
                    }
            return overlapping >= Math.Max(1, content.LiveOps.MaxConcurrentInvasions) ? "too many invasions" : null;
        }

        private static ActiveLiveEvent Make(ContentDb content, LiveOpsEventDef ev, long s, long e) => new ActiveLiveEvent
        {
            OccurrenceId = OccurrenceId(ev.Id, s), EventId = ev.Id, Kind = ev.Kind, RefId = ev.RefId, StartMs = s, EndMs = e, Surprise = !ev.Announced,
            Name = ev.Kind == LiveOpsKind.Invasion ? content.Invasion(ev.RefId)?.Name : content.PlanetaryEvent(ev.RefId)?.Name,
        };

        /// <summary>Contribution score of a battle's damage (content: damage per point). Pure.</summary>
        public static long ScoreFromDamage(long damage) => ScoreFromDamage(ContentDb.Default, damage);

        public static long ScoreFromDamage(ContentDb content, long damage)
        {
            if (damage <= 0) return 0;
            return damage / Math.Max(1, content.LiveOps.DamagePerScorePoint);
        }

        /// <summary>Contribution tier for a 1-based rank (null for rank 0 = no participation).</summary>
        public static ContributionTierDef TierForRank(InvasionDef def, int rank)
        {
            if (def == null || rank <= 0) return null;
            foreach (var t in def.Tiers) if (rank <= t.MaxRank) return t;
            return null;
        }

        /// <summary>Content validation: invasion outcome bounds (×1.5..×3 / ÷1.5..÷4, 2..7 days), schedule references.</summary>
        public static List<string> Validate(ContentDb content)
        {
            var errors = new List<string>();
            var cfg = content.LiveOps;
            const double eps = 1e-9;
            foreach (var inv in content.Invasions.Values)
            {
                if (inv.WinModifiers.Count == 0 || inv.LoseModifiers.Count == 0) errors.Add(inv.Id + ": needs win and lose modifiers");
                foreach (var m in inv.WinModifiers)
                    if (m.Multiplier < cfg.BoostMin - eps || m.Multiplier > cfg.BoostMax + eps) errors.Add(inv.Id + ": win multiplier out of bounds " + m.Multiplier);
                foreach (var m in inv.LoseModifiers)
                    if (m.Multiplier < 1.0 / cfg.CutMaxDivisor - eps || m.Multiplier > 1.0 / cfg.CutMinDivisor + eps) errors.Add(inv.Id + ": lose multiplier out of bounds " + m.Multiplier);
                foreach (var m in Concat(inv.WinModifiers, inv.LoseModifiers))
                    if (m.DurationMs < cfg.OutcomeMinDurationMs || m.DurationMs > cfg.OutcomeMaxDurationMs) errors.Add(inv.Id + ": duration out of bounds " + m.DurationMs);
                if (inv.Tiers.Count == 0) errors.Add(inv.Id + ": no contribution tiers");
            }
            foreach (var pe in content.PlanetaryEvents.Values)
                foreach (var m in pe.Modifiers)
                    if (m.Multiplier <= 0 || Math.Abs(m.Multiplier - 1) < eps) errors.Add(pe.Id + ": modifier must change something");
            foreach (var ev in content.LiveOpsSchedule)
            {
                bool ok = ev.Kind == LiveOpsKind.Invasion ? content.Invasion(ev.RefId) != null : content.PlanetaryEvent(ev.RefId) != null;
                if (!ok) errors.Add(ev.Id + ": unknown ref " + ev.RefId);
                if (ev.Schedule.DurationMs <= 0) errors.Add(ev.Id + ": empty schedule");
            }
            return errors;
        }

        private static IEnumerable<ModifierDef> Concat(List<ModifierDef> a, List<ModifierDef> b)
        {
            foreach (var x in a) yield return x;
            foreach (var x in b) yield return x;
        }

        // ---- settler ---------------------------------------------------------------------------

        /// <summary>Daily attempt top-up, planetary event modifiers for active windows, cleanup of expired data.</summary>
        public static void Settle(CommandContext ctx)
        {
            var s = ctx.State;
            var lo = s.LiveOps;
            long now = ctx.NowMs;

            long day = MetaTime.DayIndex(ctx.Content, now);
            if (lo.AttemptDay != day)
            {
                lo.AttemptDay = day;
                long have = s.Wallet.Get(Currencies.InvasionAttempts);
                long want = ctx.Content.LiveOps.InvasionAttemptsPerDay;
                if (have < want) s.Wallet.Add(Currencies.InvasionAttempts, want - have);
            }

            ModifierMath.RemoveExpired(s.Modifiers, now);
            MaterialiseScheduledModifiers(ctx); // no-op when the early settler already ran for this command

            // Forget finished contributions/surprises after two weeks.
            long horizon = now - 14 * MetaTime.Day;
            var stale = new List<string>();
            foreach (var kv in lo.Contributions) if (kv.Value.EndMs < horizon) stale.Add(kv.Key);
            foreach (var k in stale) lo.Contributions.Remove(k);
            lo.ServerInvasions.RemoveAll(x => x.EndMs < horizon);
        }

        /// <summary>How far back planetary event occurrences are materialised for a player who was away.</summary>
        public const long ScheduleLookbackMs = 60 * MetaTime.Day;

        /// <summary>
        /// Writes the modifiers of every planetary event occurrence that overlaps the time since the last settle
        /// (including ones that started and ended between two commands) with their real start/end times.
        /// Registered as the FIRST settler in SystemsRegistry, so production settlement (City) and every other
        /// settler integrate modifier windows correctly regardless of settler order; expired modifiers are only
        /// removed afterwards by <see cref="Settle"/> (registered with the Meta systems, after City).
        /// </summary>
        public static void MaterialiseScheduledModifiers(CommandContext ctx)
        {
            var s = ctx.State;
            var lo = s.LiveOps;
            long now = ctx.NowMs;
            if (lo.ScheduleSettledMs >= now && lo.ScheduleSettledMs != 0) return;
            long since = lo.ScheduleSettledMs > 0 ? Math.Max(lo.ScheduleSettledMs, now - ScheduleLookbackMs) : now;
            lo.ScheduleSettledMs = now;
            foreach (var ev in ctx.Content.LiveOpsSchedule)
            {
                if (ev.Kind != LiveOpsKind.PlanetaryEvent) continue;
                var def = ctx.Content.PlanetaryEvent(ev.RefId);
                if (def == null) continue;
                // Walk occurrences backwards from the latest one that started at or before now.
                if (!ev.Schedule.TryLatest(now, out long start, out long end)) continue;
                for (int guard = 0; guard < 400; guard++)
                {
                    if (end > since || (end > now && start <= now)) AddEventModifiers(ctx, ev, def, start, end);
                    if (ev.Schedule.RecurrenceMs <= 0 || start - ev.Schedule.RecurrenceMs < ev.Schedule.StartMs) break;
                    start -= ev.Schedule.RecurrenceMs;
                    end = start + ev.Schedule.DurationMs;
                    if (end <= since) break;
                }
            }
        }

        private static void AddEventModifiers(CommandContext ctx, LiveOpsEventDef ev, PlanetaryEventDef def, long start, long end)
        {
            var s = ctx.State;
            string occ = OccurrenceId(ev.Id, start);
            bool added = false;
            for (int i = 0; i < def.Modifiers.Count; i++)
            {
                string id = "evt:" + occ + ":" + i;
                if (HasModifier(s, id)) continue;
                var m = def.Modifiers[i];
                s.Modifiers.Add(new Modifier
                {
                    Id = id, Source = "event:" + def.Id, Target = m.Target, Multiplier = m.Multiplier,
                    StartMs = start, EndMs = end, Description = m.Description,
                });
                added = true;
            }
            if (added && end > ctx.NowMs) ctx.Emit("planetary_event", def.Id, occ);
        }

        private static bool HasModifier(GameState s, string id)
        {
            foreach (var m in s.Modifiers) if (m.Id == id) return true;
            return false;
        }

        // ---- invasion outcome (server-driven) ---------------------------------------------------

        /// <summary>
        /// Apply an invasion's group outcome to this player: lasting win/lose modifiers (from now for the
        /// content duration) and contribution-rank rewards (+ repelled bonus for participants). Idempotent per
        /// occurrence; when <paramref name="occurrenceId"/> is null the latest calendar occurrence of the invasion is used.
        /// </summary>
        public static void ApplyInvasionOutcome(CommandContext ctx, string invasionId, bool repelled, int contributionRank, string occurrenceId = null)
        {
            var def = ctx.Content.Invasion(invasionId);
            GameEngine.Require(def != null, "unknown invasion");
            GameEngine.Require(contributionRank >= 0, "invalid rank");
            var lo = ctx.State.LiveOps;
            string occ = occurrenceId ?? LatestOccurrence(ctx.State, ctx.Content, invasionId, ctx.NowMs);
            GameEngine.Require(!lo.AppliedOutcomes.Contains(occ), "outcome already applied");

            lo.AppliedOutcomes.Add(occ);
            if (lo.AppliedOutcomes.Count > 50) lo.AppliedOutcomes.RemoveAt(0);

            var mods = repelled ? def.WinModifiers : def.LoseModifiers;
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                ctx.State.Modifiers.Add(new Modifier
                {
                    Id = "inv:" + occ + ":" + i, Source = "invasion:" + def.Id, Target = m.Target, Multiplier = m.Multiplier,
                    StartMs = ctx.NowMs, EndMs = ctx.NowMs + m.DurationMs, Description = m.Description,
                });
            }

            var tier = TierForRank(def, contributionRank);
            if (tier != null)
            {
                ctx.GrantAll(tier.Rewards);
                if (repelled) ctx.GrantAll(def.RepelledRewards);
                if (repelled)
                    foreach (var rr in def.RepelledRareRewards)
                    {
                        if (contributionRank > rr.MaxRank) continue;
                        if (ctx.Rng.NextDouble() * 100 < rr.ChancePct)
                        {
                            ctx.Grant(rr.Reward);
                            ctx.State.Count("invasion_rare_rewards");
                        }
                        break;
                    }
            }
            if (repelled) ctx.State.Count("invasions_repelled");
            if (contributionRank == 1) ctx.State.Count("invasion_top_contributor");
            ctx.Emit("invasion_outcome", def.Id, occ, repelled ? "repelled" : "overrun");
        }

        private static string LatestOccurrence(GameState state, ContentDb content, string invasionId, long now)
        {
            string best = null; long bestStart = long.MinValue;
            foreach (var ev in content.LiveOpsSchedule)
            {
                if (ev.Kind != LiveOpsKind.Invasion || ev.RefId != invasionId || !ev.Schedule.TryLatest(now, out long s, out _)) continue;
                for (int guard = 0; guard < 8 && !OccurrenceRuns(content, ev, s); guard++)
                {
                    if (ev.Schedule.RecurrenceMs <= 0 || s - ev.Schedule.RecurrenceMs < ev.Schedule.StartMs) { s = long.MinValue; break; }
                    s -= ev.Schedule.RecurrenceMs;
                }
                if (s != long.MinValue && s > bestStart) { bestStart = s; best = OccurrenceId(ev.Id, s); }
            }
            foreach (var si in state.LiveOps.ServerInvasions)
                if (si.InvasionId == invasionId && si.StartMs <= now && si.StartMs > bestStart) { bestStart = si.StartMs; best = si.OccurrenceId; }
            return best ?? OccurrenceId(invasionId, now);
        }

        // ---- handlers --------------------------------------------------------------------------

        private static ActiveLiveEvent FindActiveInvasion(CommandContext ctx, string occurrenceId)
        {
            foreach (var ev in ActiveEvents(ctx.State, ctx.Content, ctx.NowMs))
                if (ev.Kind == LiveOpsKind.Invasion && ev.OccurrenceId == occurrenceId) return ev;
            return null;
        }

        private static CommandResult HandleStartAttempt(InvasionStartAttempt cmd, CommandContext ctx)
        {
            var s = ctx.State;
            GameEngine.Require(Features.IsUnlocked(s, ctx.Content, Features.Invasions), "feature locked");
            var ev = FindActiveInvasion(ctx, cmd.OccurrenceId);
            GameEngine.Require(ev != null, "invasion not active");
            GameEngine.Require(s.Wallet.CanAfford(Currencies.InvasionAttempts, 1), "no invasion attempts");

            s.Wallet.TrySpend(Currencies.InvasionAttempts, 1);
            if (!s.LiveOps.Contributions.TryGetValue(ev.OccurrenceId, out var c))
            {
                c = new InvasionContribution { InvasionId = ev.RefId, StartMs = ev.StartMs, EndMs = ev.EndMs };
                s.LiveOps.Contributions[ev.OccurrenceId] = c;
            }
            c.Attempts++;
            c.PendingAttempts++;
            s.Count("invasion_attempts_used");
            return CommandResult.Success(ev);
        }

        private static CommandResult HandleRecordDamage(InvasionRecordDamage cmd, CommandContext ctx)
        {
            var s = ctx.State;
            GameEngine.Require(!string.IsNullOrEmpty(cmd.OccurrenceId), "no attempt for occurrence");
            GameEngine.Require(cmd.Damage >= 0, "invalid damage");
            s.LiveOps.Contributions.TryGetValue(cmd.OccurrenceId, out var c);
            if ((c == null || c.PendingAttempts <= 0) && !string.IsNullOrEmpty(cmd.InvasionId))
            {
                // Server-run attack: start the attempt in the same atomic command.
                GameEngine.Require(Features.IsUnlocked(s, ctx.Content, Features.Invasions), "feature locked");
                GameEngine.Require(ctx.Content.Invasion(cmd.InvasionId) != null, "unknown invasion");
                GameEngine.Require(cmd.EndMs <= 0 || ctx.NowMs < cmd.EndMs, "invasion not active");
                GameEngine.Require(s.Wallet.CanAfford(Currencies.InvasionAttempts, 1), "no invasion attempts");
                s.Wallet.TrySpend(Currencies.InvasionAttempts, 1);
                if (c == null)
                {
                    c = new InvasionContribution { InvasionId = cmd.InvasionId, StartMs = cmd.StartMs, EndMs = cmd.EndMs > 0 ? cmd.EndMs : ctx.NowMs };
                    s.LiveOps.Contributions[cmd.OccurrenceId] = c;
                }
                c.Attempts++;
                c.PendingAttempts++;
                s.Count("invasion_attempts_used");
            }
            GameEngine.Require(c != null, "no attempt for occurrence");
            GameEngine.Require(c.PendingAttempts > 0, "no pending attempt");

            long score = ScoreFromDamage(ctx.Content, cmd.Damage);
            c.PendingAttempts--;
            c.Damage += cmd.Damage;
            c.Score += score;
            s.Count("invasion_battles");
            s.Count("invasion_damage", cmd.Damage);
            s.Count("invasion_score", score);
            GuildSystem.AddInvasionContribution(ctx, score);
            return CommandResult.Success(score);
        }

        private static CommandResult HandleApplyOutcome(LiveOpsApplyOutcome cmd, CommandContext ctx)
        {
            ApplyInvasionOutcome(ctx, cmd.InvasionId, cmd.Repelled, cmd.ContributionRank, cmd.OccurrenceId);
            return CommandResult.Success();
        }

        private static CommandResult HandleStartSurprise(LiveOpsStartSurpriseInvasion cmd, CommandContext ctx)
        {
            GameEngine.Require(ctx.Content.Invasion(cmd.InvasionId) != null, "unknown invasion");
            GameEngine.Require(cmd.DurationMs > 0, "invalid duration");
            string occ = OccurrenceId("server_" + cmd.InvasionId, cmd.StartMs);
            foreach (var si in ctx.State.LiveOps.ServerInvasions) GameEngine.Require(si.OccurrenceId != occ, "already started");
            string conflict = InvasionConflict(ctx.State, ctx.Content, cmd.InvasionId, cmd.StartMs, cmd.StartMs + cmd.DurationMs);
            GameEngine.Require(conflict == null, conflict);
            ctx.State.LiveOps.ServerInvasions.Add(new ServerInvasion { OccurrenceId = occ, InvasionId = cmd.InvasionId, StartMs = cmd.StartMs, EndMs = cmd.StartMs + cmd.DurationMs });
            return CommandResult.Success(occ);
        }
    }
}
