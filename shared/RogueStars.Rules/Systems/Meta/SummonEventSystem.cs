using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Summon events (docs/design/summon-events.md): the way to get heroes and mounts. One hero event (6 days) and one mount
    /// event (3 days) at a time, each featuring one hero / mount from a deterministic global rotation
    /// (<see cref="SummonEventTypeDef.Rotation"/>); the player's first event of a type starts when its feature unlocks.
    /// Pulls cost persistent tickets (or Diamonds) and pay exchange tokens; the visible Luck meter is the Mythic pity (the
    /// featured copy), carried to the next event of the type; copies of a hero / mount raise its stars (granters in
    /// HeroSystem / MountSystem). Per event: task lines counted from the event start, pull-count milestones, an exchange
    /// shop with event limits, a free + premium day pass, packs (products) and a daily ad ticket.
    /// When an event ends, its unclaimed task and pass rewards are mailed (on joining the next event of the type).
    /// Events: "summon_pull"(type, count, eventId), "summon_event_joined"(type, eventId), "summon_event_mailed"(type, eventId).
    /// </summary>
    public static class SummonEventSystem
    {
        /// <summary>The save migration this build applies (SummonState.Migrated).</summary>
        public const int MigrationVersion = 1;

        public static readonly SummonEventType[] Types = { SummonEventType.Hero, SummonEventType.Mount };

        public static void Register(GameEngine e)
        {
            e.RegisterNewPlayer(ctx => ctx.State.Summons.Migrated = MigrationVersion); // a new save has nothing to migrate
            e.RegisterSettler(Settle);
            e.Register<SummonPull>(HandlePull);
            e.Register<SummonTaskClaim>(HandleTaskClaim);
            e.Register<SummonTaskClaimAll>(HandleTaskClaimAll);
            e.Register<SummonMilestoneClaim>(HandleMilestone);
            e.Register<SummonExchangeBuy>(HandleExchange);
            e.Register<SummonPassClaim>(HandlePassClaim);
            e.Register<SummonPassClaimAll>(HandlePassClaimAll);
        }

        // ---- schedule ------------------------------------------------------------------------------

        /// <summary>The global occurrence of a type at <paramref name="nowMs"/> (ignores the player's welcome event).</summary>
        public static SummonEventInfo GlobalEvent(ContentDb c, SummonEventType type, long nowMs)
        {
            var def = c.SummonType(type);
            if (def == null || def.DurationMs <= 0 || def.Rotation.Count == 0) return null;
            long k = MetaTime.FloorDiv(nowMs - def.EpochMs, def.DurationMs);
            int idx = (int)(((k % def.Rotation.Count) + def.Rotation.Count) % def.Rotation.Count);
            long start = def.EpochMs + k * def.DurationMs;
            return new SummonEventInfo { Type = type, Id = Key(type) + "@" + k, FeaturedId = def.Rotation[idx], StartMs = start, EndMs = start + def.DurationMs };
        }

        /// <summary>
        /// The player's event of a type now, or null while its feature is locked. The first event starts at the feature unlock
        /// and runs to the end of the global occurrence containing unlock + FirstEventMinMs, featuring the hero / mount of the
        /// occurrence running at the unlock; after it the player is in the global rotation.
        /// </summary>
        public static SummonEventInfo CurrentEvent(GameState s, ContentDb c, SummonEventType type, long nowMs)
        {
            var def = c.SummonType(type);
            if (def == null || !Features.IsUnlocked(s, c, def.Feature)) return null;
            if (s.Onboarding.Unlocked.TryGetValue(def.Feature, out long unlockMs))
            {
                var first = GlobalEvent(c, type, unlockMs);
                var last = GlobalEvent(c, type, unlockMs + def.FirstEventMinMs);
                if (first != null && last != null && nowMs < last.EndMs && nowMs >= unlockMs)
                    return new SummonEventInfo { Type = type, Id = Key(type) + "@welcome", FeaturedId = first.FeaturedId, StartMs = unlockMs, EndMs = last.EndMs, Welcome = true };
            }
            return GlobalEvent(c, type, nowMs);
        }

        /// <summary>The event that follows the player's current one (hub preview: "next: …").</summary>
        public static SummonEventInfo NextEvent(GameState s, ContentDb c, SummonEventType type, long nowMs)
        {
            var cur = CurrentEvent(s, c, type, nowMs);
            return cur == null ? null : GlobalEvent(c, type, cur.EndMs);
        }

        public static string Key(SummonEventType type) => type == SummonEventType.Hero ? "hero" : "mount";

        /// <summary>The player's progress in the current event (null when locked or not joined yet: the settler joins).</summary>
        public static SummonEventProgress Progress(GameState s, ContentDb c, SummonEventType type, long nowMs)
        {
            var ev = CurrentEvent(s, c, type, nowMs);
            if (ev == null || !s.Summons.Events.TryGetValue(type, out var p) || p.EventId != ev.Id) return null;
            return p;
        }

        /// <summary>1-based day of the event in reset days (the pass unlocks one day per reset).</summary>
        public static int EventDay(ContentDb c, long startMs, long nowMs) =>
            (int)(MetaTime.DayIndex(c, nowMs) - MetaTime.DayIndex(c, startMs)) + 1;

        // ---- queries -------------------------------------------------------------------------------

        public static int Luck(GameState s, SummonEventType type) => s.Summons.Luck.TryGetValue(type, out var p) ? p.SinceMythic : 0;

        public static int LuckMax(ContentDb c, SummonEventType type) => c.SummonType(type)?.Gacha.MythicHardPity ?? 0;

        /// <summary>Resolve the featured placeholder in a reward id ("@featured", "shards_mount_@featured").</summary>
        public static Reward Resolve(Reward r, string featuredId) =>
            r == null || r.Id == null || r.Id.IndexOf(SummonEventsConfig.Featured, StringComparison.Ordinal) < 0 ? r
                : new Reward(r.Kind, r.Id.Replace(SummonEventsConfig.Featured, featuredId), r.Amount, r.Rarity);

        public static List<Reward> Resolve(IEnumerable<Reward> list, string featuredId)
        {
            var res = new List<Reward>();
            foreach (var r in list) res.Add(Resolve(r, featuredId));
            return res;
        }

        public static long TaskProgress(GameState s, SummonEventProgress p, SummonTaskLineDef line)
        {
            long b = p.Baseline.TryGetValue(line.CounterKey, out var v) ? v : 0;
            return Math.Max(0, s.Counter(line.CounterKey) - b);
        }

        /// <summary>
        /// One entry per task line: the first unclaimed target (claimable when reached), or the last target when the whole
        /// line is claimed. Lines with claimable targets first, then open, then done (content order inside).
        /// </summary>
        public static List<SummonTaskStatus> Tasks(GameState s, ContentDb c, SummonEventType type, long nowMs)
        {
            var list = new List<SummonTaskStatus>();
            var def = c.SummonType(type);
            var p = Progress(s, c, type, nowMs);
            if (def == null || p == null) return list;
            foreach (var line in def.Tasks)
            {
                long prog = TaskProgress(s, p, line);
                int idx = -1;
                for (int i = 0; i < line.Targets.Count; i++) if (!p.Claimed.Contains(TaskKey(line.Id, i))) { idx = i; break; }
                bool done = idx < 0;
                int shown = done ? line.Targets.Count - 1 : idx;
                list.Add(new SummonTaskStatus
                {
                    LineId = line.Id, Index = shown, Target = line.Targets[shown], Progress = prog, Text = line.Describe(line.Targets[shown]),
                    Reward = line.Reward, Done = done, Claimable = !done && prog >= line.Targets[shown],
                });
            }
            int Rank(SummonTaskStatus t) => t.Claimable ? 0 : t.Done ? 2 : 1;
            var ordered = new List<SummonTaskStatus>();
            for (int r = 0; r <= 2; r++) foreach (var t in list) if (Rank(t) == r) ordered.Add(t);
            return ordered;
        }

        /// <summary>Claimable task targets (all lines, all reached targets).</summary>
        public static int ClaimableTasks(GameState s, ContentDb c, SummonEventType type, long nowMs)
        {
            var def = c.SummonType(type);
            var p = Progress(s, c, type, nowMs);
            if (def == null || p == null) return 0;
            int n = 0;
            foreach (var line in def.Tasks)
            {
                long prog = TaskProgress(s, p, line);
                for (int i = 0; i < line.Targets.Count; i++) if (prog >= line.Targets[i] && !p.Claimed.Contains(TaskKey(line.Id, i))) n++;
            }
            return n;
        }

        public static bool MilestoneClaimable(SummonEventProgress p, SummonMilestoneDef m) => p != null && p.Pulls >= m.Pulls && !p.Claimed.Contains(MilestoneKey(m.Pulls));

        public static int ClaimableMilestones(GameState s, ContentDb c, SummonEventType type, long nowMs)
        {
            var def = c.SummonType(type);
            var p = Progress(s, c, type, nowMs);
            int n = 0;
            if (def != null) foreach (var m in def.Milestones) if (MilestoneClaimable(p, m)) n++;
            return n;
        }

        /// <summary>Pass days unlocked so far (1..Pass.Count).</summary>
        public static int PassDaysUnlocked(GameState s, ContentDb c, SummonEventType type, long nowMs)
        {
            var def = c.SummonType(type);
            var p = Progress(s, c, type, nowMs);
            if (def == null || p == null) return 0;
            return Math.Max(0, Math.Min(def.Pass.Count, EventDay(c, p.StartMs, nowMs)));
        }

        public static bool PassClaimable(GameState s, ContentDb c, SummonEventType type, int day, bool premium, long nowMs)
        {
            var p = Progress(s, c, type, nowMs);
            if (p == null || day < 1 || day > PassDaysUnlocked(s, c, type, nowMs)) return false;
            if (premium && !p.PassPremium) return false;
            return !p.Claimed.Contains(PassKey(day, premium));
        }

        public static int ClaimablePassDays(GameState s, ContentDb c, SummonEventType type, long nowMs)
        {
            int n = 0, days = PassDaysUnlocked(s, c, type, nowMs);
            for (int d = 1; d <= days; d++)
            {
                if (PassClaimable(s, c, type, d, false, nowMs)) n++;
                if (PassClaimable(s, c, type, d, true, nowMs)) n++;
            }
            return n;
        }

        public static int ExchangeBought(SummonEventProgress p, string itemId) => p != null && p.ExchangeBuys.TryGetValue(itemId, out var n) ? n : 0;

        /// <summary>Everything claimable in the event (hub badge): tasks, milestones, pass days and the free ad ticket while ads are instant.</summary>
        public static int BadgeCount(GameState s, ContentDb c, SummonEventType type, long nowMs) =>
            ClaimableTasks(s, c, type, nowMs) + ClaimableMilestones(s, c, type, nowMs) + ClaimablePassDays(s, c, type, nowMs);

        /// <summary>Product availability for summon packs / passes (called by ShopSystem.PurchaseBlockReason).</summary>
        public static string ProductBlockReason(GameState s, ContentDb c, ProductDef p, long nowMs)
        {
            var type = p.SummonEvent.Value;
            var def = c.SummonType(type);
            if (def == null || !Features.IsUnlocked(s, c, def.Feature)) return "feature locked";
            var prog = Progress(s, c, type, nowMs);
            if (CurrentEvent(s, c, type, nowMs) == null) return "no event running";
            if (p.Kind == ProductKind.SummonPass && prog != null && prog.PassPremium) return "already owned this event";
            return null;
        }

        /// <summary>The premium pass was bought: unlock the premium track of the current event (ShopSystem.GrantProduct).</summary>
        public static void OnPassPurchased(CommandContext ctx, ProductDef p)
        {
            var type = p.SummonEvent.Value;
            Join(ctx, type);
            var prog = Progress(ctx.State, ctx.Content, type, ctx.NowMs);
            GameEngine.Require(prog != null, "no event running");
            prog.PassPremium = true;
        }

        public static string TaskKey(string lineId, int index) => "task:" + lineId + ":" + index;
        public static string MilestoneKey(int pulls) => "ms:" + pulls;
        public static string PassKey(int day, bool premium) => (premium ? "pass:p:" : "pass:f:") + day;

        // ---- settler -------------------------------------------------------------------------------

        /// <summary>Save migration (once) and joining the current event of each unlocked type (task baselines).</summary>
        public static void Settle(CommandContext ctx)
        {
            Migrate(ctx);
            foreach (var t in Types) Join(ctx, t);
        }

        /// <summary>
        /// Join the current event of a type when the player is not in it yet: a fresh progress record whose task baselines are
        /// the counters now. Counters only change through commands and every command settles first, so the first settle
        /// after the event start sees the counters as they were at the start (task progress counts from the event start).
        /// </summary>
        private static void Join(CommandContext ctx, SummonEventType type)
        {
            var s = ctx.State; var c = ctx.Content;
            var ev = CurrentEvent(s, c, type, ctx.NowMs);
            if (ev == null) return;
            var def = c.SummonType(type);
            if (s.Summons.Events.TryGetValue(type, out var cur))
            {
                if (cur.EventId == ev.Id) return;
                MailUnclaimed(ctx, def, cur); // the previous event ended: its unclaimed task and pass rewards go to the mailbox
            }
            var p = new SummonEventProgress { EventId = ev.Id, FeaturedId = ev.FeaturedId, StartMs = ev.StartMs, EndMs = ev.EndMs, JoinedMs = ctx.NowMs };
            foreach (var line in def.Tasks) p.Baseline[line.CounterKey] = s.Counter(line.CounterKey);
            s.Summons.Events[type] = p;
            ctx.Emit("summon_event_joined", Key(type), ev.Id);
        }

        /// <summary>
        /// The rewards an ended event still owes: every reached, unclaimed task target and every unclaimed pass day (all its days
        /// unlocked by the end; the premium track when it was bought), merged by reward. Milestones are not included.
        /// </summary>
        public static List<Reward> Unclaimed(GameState s, ContentDb c, SummonEventTypeDef def, SummonEventProgress p)
        {
            var list = new List<Reward>();
            if (def == null || p == null) return list;
            foreach (var line in def.Tasks)
            {
                long prog = TaskProgress(s, p, line);
                for (int i = 0; i < line.Targets.Count; i++)
                    if (prog >= line.Targets[i] && !p.Claimed.Contains(TaskKey(line.Id, i))) Add(list, Resolve(line.Reward, p.FeaturedId));
            }
            int days = Math.Max(0, Math.Min(def.Pass.Count, EventDay(c, p.StartMs, p.EndMs - 1)));
            for (int d = 1; d <= days; d++)
            {
                if (!p.Claimed.Contains(PassKey(d, false))) foreach (var r in Resolve(def.Pass[d - 1].Free, p.FeaturedId)) Add(list, r);
                if (p.PassPremium && !p.Claimed.Contains(PassKey(d, true))) foreach (var r in Resolve(def.Pass[d - 1].Premium, p.FeaturedId)) Add(list, r);
            }
            return list;
        }

        private static void Add(List<Reward> list, Reward r)
        {
            if (r == null || r.Amount <= 0) return;
            foreach (var x in list)
                if (x.Kind == r.Kind && x.Id == r.Id && x.Rarity == r.Rarity) { x.Amount += r.Amount; return; }
            list.Add(new Reward(r.Kind, r.Id, r.Amount, r.Rarity));
        }

        /// <summary>An event ended (the player joins the next one): mail its unclaimed task and pass rewards instead of letting them lapse.</summary>
        private static void MailUnclaimed(CommandContext ctx, SummonEventTypeDef def, SummonEventProgress p)
        {
            var rewards = Unclaimed(ctx.State, ctx.Content, def, p);
            if (rewards.Count == 0) return;
            string id = MailSystem.Push(ctx.State, ctx.Content, ctx.NowMs, def.Name + ": unclaimed rewards",
                "The event has ended. Here are the task and pass rewards you had not claimed yet.", rewards);
            ctx.Emit("mail", id);
            ctx.Emit("summon_event_mailed", Key(def.Type), p.EventId);
        }

        /// <summary>
        /// Summon events migration (once per save): heroes, mounts and shards are kept as they are; a save that already has
        /// the Heroes / Mounts feature gets the type's one-time ticket gift (its first event pulls).
        /// </summary>
        public static void Migrate(CommandContext ctx)
        {
            var st = ctx.State.Summons;
            if (st.Migrated >= MigrationVersion) return;
            st.Migrated = MigrationVersion;
            foreach (var t in Types)
            {
                var def = ctx.Content.SummonType(t);
                if (def != null && ctx.State.Onboarding.Unlocked.ContainsKey(def.Feature)) ctx.GrantAll(def.MigrationGift);
            }
        }

        // ---- handlers ------------------------------------------------------------------------------

        private static (SummonEventTypeDef def, SummonEventProgress prog) Require(CommandContext ctx, SummonEventType type)
        {
            var def = ctx.Content.SummonType(type);
            GameEngine.Require(def != null, "unknown event type");
            Features.Require(ctx.State, ctx.Content, def.Feature);
            Join(ctx, type);
            var prog = Progress(ctx.State, ctx.Content, type, ctx.NowMs);
            GameEngine.Require(prog != null, "no event running");
            return (def, prog);
        }

        private static CommandResult HandlePull(SummonPull cmd, CommandContext ctx)
        {
            var s = ctx.State;
            var (def, prog) = Require(ctx, cmd.EventType);
            GameEngine.Require(string.IsNullOrEmpty(cmd.EventId) || cmd.EventId == prog.EventId, "event ended");
            GameEngine.Require(cmd.Count == 1 || cmd.Count == 10, "pull count must be 1 or 10");
            if (cmd.Payment == SummonPayment.Tickets) ProgressionUtil.Spend(s, def.TicketCurrency, cmd.Count == 10 ? def.Gacha.TokenCost10 : def.Gacha.TokenCost1);
            else
            {
                long dia = cmd.Count == 10 ? def.DiamondCost10 : def.DiamondCost1;
                ProgressionUtil.Spend(s, Currencies.Diamonds, dia);
                s.Count("diamonds_spent", dia);
            }

            var pity = s.Summons.LuckFor(cmd.EventType);
            long triggersBefore = pity.MythicPityTriggers;
            var result = new SummonPullResult { Type = cmd.EventType, EventId = prog.EventId };
            bool gotMin = false;
            for (int i = 0; i < cmd.Count; i++)
            {
                bool forceMin = cmd.Count == 10 && i == cmd.Count - 1 && !gotMin;
                var tier = GachaSystem.RollTier(def.Gacha, pity, ctx.Rng, forceMin);
                if (tier.Tier >= def.Gacha.TenPullMinTier) gotMin = true;
                result.Items.Add(PickItem(tier, prog.FeaturedId, ctx.Rng));
            }
            // Grant after rolling (a granter never influences later rolls); a copy's note is what it did when granted.
            foreach (var item in result.Items)
            {
                if (item.Kind == RewardKinds.Hero)
                {
                    item.Note = HeroSystem.CopyEffect(s, ctx.Content, item.Id);
                    ctx.Grant(new Reward(item.Kind, item.Id, item.Amount));
                    item.Stars = HeroSystem.Get(s, item.Id)?.Stars ?? 0;
                }
                else if (item.Kind == RewardKinds.Mount)
                {
                    item.Note = MountSystem.CopyEffect(s, ctx.Content, item.Id);
                    ctx.Grant(new Reward(item.Kind, item.Id, item.Amount));
                    item.Stars = MountSystem.Get(s, item.Id)?.Stars ?? 0;
                }
                else ctx.Grant(new Reward(item.Kind, item.Id, item.Amount));
            }
            result.Tokens = def.TokensPerPull * cmd.Count;
            ctx.Grant(Reward.Cur(def.TokenCurrency, result.Tokens));
            prog.Pulls += cmd.Count;
            result.Luck = pity.SinceMythic;
            result.LuckMax = def.Gacha.MythicHardPity;
            result.LuckTriggered = pity.MythicPityTriggers > triggersBefore;

            string key = Key(cmd.EventType);
            s.Count("summon_pulls_" + key, cmd.Count);
            s.Count("summon_pulls_total", cmd.Count);
            ctx.Emit("summon_pull", key, cmd.Count.ToString(), prog.EventId);
            return CommandResult.Success(result);
        }

        private static SummonPullItem PickItem(GachaTierDef tier, string featuredId, Rng rng)
        {
            var weights = new double[tier.Pool.Count];
            for (int i = 0; i < weights.Length; i++)
            {
                var e = tier.Pool[i];
                weights[i] = e.Id == featuredId ? 0 : e.Weight; // "other heroes": never the featured one
            }
            int idx = rng.PickWeighted(weights);
            if (idx < 0) throw new RuleException("empty summon pool");
            var en = tier.Pool[idx];
            bool featured = en.Id != null && en.Id.IndexOf(SummonEventsConfig.Featured, StringComparison.Ordinal) >= 0;
            var r = Resolve(new Reward(en.Kind, en.Id, en.Amount, en.Rarity), featuredId);
            return new SummonPullItem { Kind = r.Kind, Id = r.Id, Amount = r.Amount, Tier = tier.Tier, Featured = featured };
        }

        private static void ClaimTask(CommandContext ctx, SummonEventProgress p, SummonTaskLineDef line, int index)
        {
            p.Claimed.Add(TaskKey(line.Id, index));
            ctx.State.Count("summon_tasks_claimed");
            ctx.Grant(Resolve(line.Reward, p.FeaturedId));
        }

        private static CommandResult HandleTaskClaim(SummonTaskClaim cmd, CommandContext ctx)
        {
            var (def, p) = Require(ctx, cmd.EventType);
            var line = def.Tasks.Find(l => l.Id == cmd.LineId);
            GameEngine.Require(line != null, "unknown task");
            long prog = TaskProgress(ctx.State, p, line);
            if (cmd.Index >= 0)
            {
                GameEngine.Require(cmd.Index < line.Targets.Count, "invalid task step");
                GameEngine.Require(!p.Claimed.Contains(TaskKey(line.Id, cmd.Index)), "already claimed");
                GameEngine.Require(prog >= line.Targets[cmd.Index], "task not complete");
                ClaimTask(ctx, p, line, cmd.Index);
                return CommandResult.Success(1);
            }
            int n = 0;
            for (int i = 0; i < line.Targets.Count; i++)
                if (prog >= line.Targets[i] && !p.Claimed.Contains(TaskKey(line.Id, i))) { ClaimTask(ctx, p, line, i); n++; }
            GameEngine.Require(n > 0, "nothing to claim");
            return CommandResult.Success(n);
        }

        private static CommandResult HandleTaskClaimAll(SummonTaskClaimAll cmd, CommandContext ctx)
        {
            var (def, p) = Require(ctx, cmd.EventType);
            int n = 0;
            foreach (var line in def.Tasks)
            {
                long prog = TaskProgress(ctx.State, p, line);
                for (int i = 0; i < line.Targets.Count; i++)
                    if (prog >= line.Targets[i] && !p.Claimed.Contains(TaskKey(line.Id, i))) { ClaimTask(ctx, p, line, i); n++; }
            }
            GameEngine.Require(n > 0, "nothing to claim");
            return CommandResult.Success(n);
        }

        private static CommandResult HandleMilestone(SummonMilestoneClaim cmd, CommandContext ctx)
        {
            var (def, p) = Require(ctx, cmd.EventType);
            var m = def.Milestones.Find(x => x.Pulls == cmd.Pulls);
            GameEngine.Require(m != null, "unknown milestone");
            GameEngine.Require(!p.Claimed.Contains(MilestoneKey(m.Pulls)), "already claimed");
            GameEngine.Require(p.Pulls >= m.Pulls, "milestone not reached");
            p.Claimed.Add(MilestoneKey(m.Pulls));
            ctx.GrantAll(Resolve(m.Rewards, p.FeaturedId));
            return CommandResult.Success();
        }

        private static CommandResult HandleExchange(SummonExchangeBuy cmd, CommandContext ctx)
        {
            var s = ctx.State;
            var (def, p) = Require(ctx, cmd.EventType);
            var item = def.Exchange.Find(x => x.Id == cmd.ItemId);
            GameEngine.Require(item != null, "unknown item");
            GameEngine.Require(item.EventLimit <= 0 || ExchangeBought(p, item.Id) < item.EventLimit, "item limit reached");
            ProgressionUtil.Spend(s, def.TokenCurrency, item.Price);
            p.ExchangeBuys[item.Id] = ExchangeBought(p, item.Id) + 1;
            s.Count("summon_exchange_buys");
            ctx.Grant(Resolve(item.Reward, p.FeaturedId));
            return CommandResult.Success();
        }

        private static void ClaimPassDay(CommandContext ctx, SummonEventTypeDef def, SummonEventProgress p, int day, bool premium)
        {
            p.Claimed.Add(PassKey(day, premium));
            var d = def.Pass[day - 1];
            ctx.GrantAll(Resolve(premium ? d.Premium : d.Free, p.FeaturedId));
        }

        private static CommandResult HandlePassClaim(SummonPassClaim cmd, CommandContext ctx)
        {
            var (def, p) = Require(ctx, cmd.EventType);
            GameEngine.Require(cmd.Day >= 1 && cmd.Day <= def.Pass.Count, "invalid day");
            GameEngine.Require(cmd.Day <= PassDaysUnlocked(ctx.State, ctx.Content, cmd.EventType, ctx.NowMs), "day not reached");
            GameEngine.Require(!cmd.Premium || p.PassPremium, "premium not owned");
            GameEngine.Require(!p.Claimed.Contains(PassKey(cmd.Day, cmd.Premium)), "already claimed");
            ClaimPassDay(ctx, def, p, cmd.Day, cmd.Premium);
            return CommandResult.Success();
        }

        private static CommandResult HandlePassClaimAll(SummonPassClaimAll cmd, CommandContext ctx)
        {
            var (def, p) = Require(ctx, cmd.EventType);
            int days = PassDaysUnlocked(ctx.State, ctx.Content, cmd.EventType, ctx.NowMs), n = 0;
            for (int d = 1; d <= days; d++)
            {
                if (!p.Claimed.Contains(PassKey(d, false))) { ClaimPassDay(ctx, def, p, d, false); n++; }
                if (p.PassPremium && !p.Claimed.Contains(PassKey(d, true))) { ClaimPassDay(ctx, def, p, d, true); n++; }
            }
            GameEngine.Require(n > 0, "nothing to claim");
            return CommandResult.Success(n);
        }
    }
}
