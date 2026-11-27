using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Quests & retention (GDD §15): daily/weekly quests and achievements driven by state.Counters, daily and
    /// weekly point tracks with chests, 7-day login cycle without streak loss, new-player 7-day event,
    /// returning-player gift (mail). Resets: daily 00:00 UTC, weekly Monday 00:00 UTC (content ResetConfig).
    /// </summary>
    public static class QuestSystem
    {
        public static void Register(GameEngine e)
        {
            e.RegisterNewPlayer(Settle);
            e.RegisterSettler(Settle);
            e.Register<QuestClaim>(HandleClaim);
            e.Register<QuestClaimAll>(HandleClaimAll);
            e.Register<QuestClaimChest>(HandleChest);
            e.Register<LoginClaim>(HandleLogin);
            e.Register<NewPlayerClaim>(HandleNewPlayer);
        }

        // ---- queries ---------------------------------------------------------------------------

        /// <summary>Current progress of a quest (period-relative for daily/weekly, absolute otherwise).</summary>
        public static long Progress(GameState s, QuestDef q)
        {
            long cur = s.Counter(q.CounterKey);
            if (q.Every > 0) return Math.Max(0, cur - Claimed(s, q) * q.Every); // distance covered since the last claimed milestone
            Dictionary<string, long> baseline = q.Kind == QuestKind.Daily ? s.Quests.DailyBaseline
                : q.Kind == QuestKind.Weekly ? s.Quests.WeeklyBaseline : null;
            if (baseline != null && baseline.TryGetValue(q.CounterKey, out var b)) cur -= b;
            return Math.Max(0, cur);
        }

        /// <summary>1-based day of the new-player event for this account (day 1 = creation day).</summary>
        public static int NewPlayerDay(GameState s, ContentDb content, long nowMs) =>
            (int)(MetaTime.DayIndex(content, nowMs) - MetaTime.DayIndex(content, s.CreatedMs)) + 1;

        public static bool NewPlayerEventOpen(GameState s, ContentDb content, long nowMs) =>
            NewPlayerDay(s, content, nowMs) <= content.Retention.NewPlayerEventDays + content.Retention.NewPlayerGraceDays;

        /// <summary>Quest visible/claimable now (feature gate, new-player day window).</summary>
        public static bool IsAvailable(GameState s, ContentDb content, QuestDef q, long nowMs)
        {
            if (q.Feature != null && !Features.IsUnlocked(s, content, q.Feature)) return false;
            if (q.NeedsAds && content.Shop.AdsDisabled) return false;
            if (q.Kind == QuestKind.NewPlayer)
                return NewPlayerEventOpen(s, content, nowMs) && NewPlayerDay(s, content, nowMs) >= q.Day;
            return true;
        }

        public static bool IsComplete(GameState s, QuestDef q) => Progress(s, q) >= q.Target;

        /// <summary>Repeating achievements: milestones already claimed.</summary>
        public static long Claimed(GameState s, QuestDef q) => s.Quests.AchievementClaims.TryGetValue(q.Id, out var n) ? n : 0;

        /// <summary>Repeating achievements: milestones reached and not claimed yet.</summary>
        public static long Pending(GameState s, QuestDef q) => q.Every > 0 ? Math.Max(0, s.Counter(q.CounterKey) / q.Every - Claimed(s, q)) : 0;

        /// <summary>Repeating achievements: the counter value of the next unclaimed milestone (shown by the UI).</summary>
        public static long NextMilestone(GameState s, QuestDef q) => (Claimed(s, q) + 1) * q.Every;

        public static bool CanClaim(GameState s, ContentDb content, QuestDef q, long nowMs) =>
            IsAvailable(s, content, q, nowMs) && (q.Every > 0 || !s.Quests.Claimed.Contains(q.Id)) && IsComplete(s, q);

        /// <summary>Visible quests of a kind (content order is dictionary insertion; sorted by id for stability).</summary>
        public static List<QuestDef> Visible(GameState s, ContentDb content, QuestKind kind, long nowMs)
        {
            var list = new List<QuestDef>();
            foreach (var q in content.Quests.Values) if (q.Kind == kind && IsAvailable(s, content, q, nowMs)) list.Add(q);
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        public static bool CanClaimLogin(GameState s, ContentDb content, long nowMs) => s.Quests.LastLoginDay < MetaTime.DayIndex(content, nowMs);

        // ---- settler -----------------------------------------------------------------------------

        /// <summary>Daily/weekly resets (baseline snapshot, claims, points) and the returning-player gift.</summary>
        public static void Settle(CommandContext ctx)
        {
            var s = ctx.State;
            var q = s.Quests;
            var content = ctx.Content;
            long day = MetaTime.DayIndex(content, ctx.NowMs);
            long week = MetaTime.WeekIndex(content, ctx.NowMs);
            if (q.DailyIndex != day)
            {
                q.DailyIndex = day;
                ResetPeriod(s, content, QuestKind.Daily, q.DailyBaseline);
                q.DailyPoints = 0;
                q.ChestsClaimed.RemoveWhere(k => k.StartsWith(PointTrack.Daily + ":", StringComparison.Ordinal));
            }
            if (q.WeeklyIndex != week)
            {
                q.WeeklyIndex = week;
                ResetPeriod(s, content, QuestKind.Weekly, q.WeeklyBaseline);
                q.WeeklyPoints = 0;
                q.ChestsClaimed.RemoveWhere(k => k.StartsWith(PointTrack.Weekly + ":", StringComparison.Ordinal));
            }

            // The returning-player gift greets a player who acts again (a command) after a long break, never a mere settle.
            var ret = content.Retention;
            if (ctx.Acting && s.Version > 0 && ctx.NowMs - s.LastCommandMs >= ret.ReturningAfterMs
                && (q.LastReturningGiftMs == 0 || ctx.NowMs - q.LastReturningGiftMs >= ret.ReturningCooldownMs)
                && content.MailTemplate(ret.ReturningMailTemplate) != null)
            {
                q.LastReturningGiftMs = ctx.NowMs;
                string id = MailSystem.PushTemplate(s, content, ctx.NowMs, ret.ReturningMailTemplate);
                ctx.Emit("mail", id);
                ctx.Emit("returning_player");
            }
        }

        private static void ResetPeriod(GameState s, ContentDb content, QuestKind kind, Dictionary<string, long> baseline)
        {
            baseline.Clear();
            foreach (var def in content.Quests.Values)
            {
                if (def.Kind != kind) continue;
                baseline[def.CounterKey] = s.Counter(def.CounterKey);
                s.Quests.Claimed.Remove(def.Id);
            }
        }

        // ---- handlers ----------------------------------------------------------------------------

        private static void Claim(CommandContext ctx, QuestDef def)
        {
            var s = ctx.State;
            long times = 1;
            if (def.Every > 0) { times = Pending(s, def); s.Quests.AchievementClaims[def.Id] = Claimed(s, def) + times; }
            else s.Quests.Claimed.Add(def.Id);
            if (def.Kind == QuestKind.Daily) { s.Quests.DailyPoints += def.Points; s.Count("daily_quests_claimed"); }
            else if (def.Kind == QuestKind.Weekly) { s.Quests.WeeklyPoints += def.Points; s.Count("weekly_quests_claimed"); }
            else if (def.Kind == QuestKind.Achievement) s.Count("achievements_claimed", times);
            ShopSystem.AddSeasonXp(s, def.SeasonXp);
            foreach (var r in def.Rewards) ctx.Grant(new Reward(r.Kind, r.Id, r.Amount * times, r.Rarity));
            ctx.Emit("quest_claimed", def.Id);
        }

        private static CommandResult HandleClaim(QuestClaim cmd, CommandContext ctx)
        {
            var def = ctx.Content.Quest(cmd.QuestId);
            GameEngine.Require(def != null, "unknown quest");
            GameEngine.Require(IsAvailable(ctx.State, ctx.Content, def, ctx.NowMs), "quest not available");
            GameEngine.Require(def.Every > 0 || !ctx.State.Quests.Claimed.Contains(def.Id), "already claimed");
            GameEngine.Require(IsComplete(ctx.State, def), "quest not complete");
            Claim(ctx, def);
            return CommandResult.Success();
        }

        private static CommandResult HandleClaimAll(QuestClaimAll cmd, CommandContext ctx)
        {
            var ready = new List<QuestDef>();
            foreach (var def in ctx.Content.Quests.Values) if (CanClaim(ctx.State, ctx.Content, def, ctx.NowMs)) ready.Add(def);
            GameEngine.Require(ready.Count > 0, "nothing to claim");
            ready.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (var def in ready) Claim(ctx, def);
            return CommandResult.Success(ready.Count);
        }

        private static CommandResult HandleChest(QuestClaimChest cmd, CommandContext ctx)
        {
            var s = ctx.State;
            PointChestDef chest = null;
            foreach (var c in ctx.Content.PointChests) if (c.Track == cmd.Track && c.Points == cmd.Points) chest = c;
            GameEngine.Require(chest != null, "unknown chest");
            long points = cmd.Track == PointTrack.Daily ? s.Quests.DailyPoints : s.Quests.WeeklyPoints;
            GameEngine.Require(points >= chest.Points, "not enough points");
            string key = cmd.Track + ":" + cmd.Points;
            GameEngine.Require(!s.Quests.ChestsClaimed.Contains(key), "already claimed");
            s.Quests.ChestsClaimed.Add(key);
            ctx.GrantAll(chest.Rewards);
            GrantChestTokens(ctx, chest);
            return CommandResult.Success();
        }

        /// <summary>The pull token currencies of the Signals the player has unlocked (Part first; before any, just Part).</summary>
        public static List<string> UnlockedTokenTypes(GameState s, ContentDb c)
        {
            var list = new List<string>();
            if (Features.IsUnlocked(s, c, Features.PartPulls)) list.Add(Currencies.PartTokens);
            if (Features.IsUnlocked(s, c, Features.AlienPulls)) list.Add(Currencies.AlienTokens);
            if (Features.IsUnlocked(s, c, Features.Blueprints)) list.Add(Currencies.BlueprintTokens);
            if (list.Count == 0) list.Add(Currencies.PartTokens);
            return list;
        }

        private static void GrantChestTokens(CommandContext ctx, PointChestDef chest)
        {
            var s = ctx.State;
            s.Quests.LastChestTokens.Clear();
            var types = UnlockedTokenTypes(s, ctx.Content);
            if (chest.RandomTokens > 0)
            {
                // A fork of the player stream by chest and period: deterministic, and the stream itself is not consumed.
                var rng = ctx.Rng.Fork("quest_chest:" + chest.Track + ":" + chest.Points + ":" +
                    (chest.Track == PointTrack.Daily ? s.Quests.DailyIndex : s.Quests.WeeklyIndex));
                for (int i = 0; i < chest.RandomTokens; i++)
                {
                    string cur = rng.Pick(types);
                    s.Quests.LastChestTokens.Add(cur);
                    ctx.Grant(Reward.Cur(cur, 1));
                }
            }
            if (chest.TokensEachType > 0)
                foreach (var cur in types) ctx.Grant(Reward.Cur(cur, chest.TokensEachType));
        }

        private static CommandResult HandleLogin(LoginClaim cmd, CommandContext ctx)
        {
            var s = ctx.State;
            var cycle = ctx.Content.Retention.LoginCycle;
            GameEngine.Require(cycle.Count > 0, "no login rewards");
            GameEngine.Require(CanClaimLogin(s, ctx.Content, ctx.NowMs), "already claimed today");
            int idx = s.Quests.LoginCycleIndex % cycle.Count;
            s.Quests.LastLoginDay = MetaTime.DayIndex(ctx.Content, ctx.NowMs);
            s.Quests.LoginCycleIndex = (idx + 1) % cycle.Count;
            s.Count("login_days");
            ctx.GrantAll(cycle[idx]);
            return CommandResult.Success(idx + 1);
        }

        private static CommandResult HandleNewPlayer(NewPlayerClaim cmd, CommandContext ctx)
        {
            var s = ctx.State;
            var days = ctx.Content.Retention.NewPlayerDays;
            GameEngine.Require(cmd.Day >= 1 && cmd.Day <= days.Count, "invalid day");
            GameEngine.Require(NewPlayerEventOpen(s, ctx.Content, ctx.NowMs), "event over");
            GameEngine.Require(NewPlayerDay(s, ctx.Content, ctx.NowMs) >= cmd.Day, "day not reached");
            GameEngine.Require(!s.Quests.NewPlayerDaysClaimed.Contains(cmd.Day), "already claimed");
            s.Quests.NewPlayerDaysClaimed.Add(cmd.Day);
            ctx.GrantAll(days[cmd.Day - 1]);
            return CommandResult.Success();
        }
    }
}
