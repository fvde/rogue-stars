using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Laboratory (GDD §5): research lines unlock skills into the run draft pool and upgrade them
    /// (Biomass/Ore + timer). Researched skills can be toggled on/off; toggled-on skills are contributed to
    /// CombatLoadout.DraftPoolSkillIds. Research runs on a City <see cref="TimerSystem"/> timer (kind "lab_research",
    /// duration shortened by CitySystem.ResearchSpeedPct), so TimerSpeedUp (Diamonds / ad / daily free) works on it;
    /// the player collects with LabCollect once the timer is due.
    /// </summary>
    public static class LaboratorySystem
    {
        public const string TimerKind = "lab_research";

        public static void Register(GameEngine e)
        {
            e.Register<LabResearch>(HandleResearch);
            e.Register<LabCollect>(HandleCollect);
            e.Register<LabToggle>(HandleToggle);
            e.RegisterNewPlayer(ctx =>
            {
                foreach (var id in SortedIds(ctx.Content))
                    if (ctx.Content.LabResearch[id].DefaultResearched) ctx.State.Lab.Entries[id] = new LabEntry { Level = 1, Enabled = true };
            });
            e.RegisterLoadoutContributor((state, content, now, loadout) =>
            {
                foreach (var sid in ActiveSkillIds(state, content))
                    if (!loadout.DraftPoolSkillIds.Contains(sid)) loadout.DraftPoolSkillIds.Add(sid);
            });
        }

        /// <summary>Skill ids currently toggled on for the draft pool (current level's skill of each enabled line).</summary>
        public static List<string> ActiveSkillIds(GameState state, ContentDb content)
        {
            var list = new List<string>();
            foreach (var id in SortedIds(content))
            {
                if (!state.Lab.Entries.TryGetValue(id, out var e) || e.Level <= 0 || !e.Enabled) continue;
                var def = content.LabResearch[id];
                list.Add(def.Levels[Math.Min(e.Level, def.Levels.Count) - 1].SkillId);
            }
            return list;
        }

        public static LabEntry Entry(GameState state, string researchId) =>
            state.Lab.Entries.TryGetValue(researchId, out var e) ? e : new LabEntry();

        /// <summary>Time the running research completes (the timer's end; speed-ups move it), or 0 when idle.</summary>
        public static long ReadyAtMs(GameState state, string researchId)
        {
            if (!state.Lab.Entries.TryGetValue(researchId, out var e) || e.ResearchingLevel == 0) return 0;
            var t = TimerSystem.Get(state, e.TimerId);
            return t != null ? t.EndsMs : e.ReadyAtMs;
        }

        /// <summary>Research duration after building boons (CitySystem.ResearchSpeedPct).</summary>
        public static long DurationMs(GameState state, ContentDb content, long baseMs) =>
            (long)Math.Ceiling(baseMs / (1 + Math.Max(0, CitySystem.ResearchSpeedPct(state, content)) / 100.0));

        /// <summary>Completes the running research timer now (still needs LabCollect). Same effect as a full speed-up.</summary>
        public static bool FinishNow(GameState state, string researchId, long nowMs)
        {
            if (!state.Lab.Entries.TryGetValue(researchId, out var e) || e.ResearchingLevel == 0) return false;
            var t = TimerSystem.Get(state, e.TimerId);
            if (t != null) t.EndsMs = Math.Min(t.EndsMs, nowMs);
            e.ReadyAtMs = Math.Min(e.ReadyAtMs, nowMs);
            return true;
        }

        private static List<string> SortedIds(ContentDb content)
        {
            var ids = new List<string>(content.LabResearch.Keys);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        private static CommandResult HandleResearch(LabResearch cmd, CommandContext ctx)
        {
            var c = ctx.Content; var s = ctx.State;
            GameEngine.Require(Features.IsUnlocked(s, c, Features.Laboratory), "laboratory locked");
            LabResearchDef def = null;
            GameEngine.Require(cmd.ResearchId != null && c.LabResearch.TryGetValue(cmd.ResearchId, out def), "unknown research");
            var entry = Entry(s, cmd.ResearchId);
            GameEngine.Require(entry.ResearchingLevel == 0, "already researching");
            GameEngine.Require(entry.Level < def.Levels.Count, "research maxed");
            int running = 0;
            foreach (var e in s.Lab.Entries.Values) if (e.ResearchingLevel > 0) running++;
            GameEngine.Require(running < Math.Max(1, c.RunTuning.LabSlots), "laboratory busy");
            var level = def.Levels[entry.Level];
            GameEngine.Require(s.Wallet.CanAfford(level.Costs), "cannot afford research");
            s.Wallet.TrySpend(level.Costs);
            entry.ResearchingLevel = entry.Level + 1;
            var timer = TimerSystem.Start(ctx, TimerKind, cmd.ResearchId, DurationMs(s, c, level.DurationMs));
            entry.TimerId = timer.Id;
            entry.ReadyAtMs = timer.EndsMs;
            s.Lab.Entries[cmd.ResearchId] = entry;
            s.Count("lab_research_started");
            ctx.Emit("lab_research", cmd.ResearchId, entry.ResearchingLevel.ToString(), entry.ReadyAtMs.ToString());
            return CommandResult.Success();
        }

        private static CommandResult HandleCollect(LabCollect cmd, CommandContext ctx)
        {
            var s = ctx.State;
            LabEntry entry = null;
            GameEngine.Require(cmd.ResearchId != null && s.Lab.Entries.TryGetValue(cmd.ResearchId, out entry) && entry.ResearchingLevel > 0, "nothing to collect");
            GameEngine.Require(ctx.NowMs >= ReadyAtMs(s, cmd.ResearchId), "research not finished");
            TimerSystem.Remove(s, entry.TimerId);
            entry.TimerId = null;
            bool first = entry.Level == 0;
            entry.Level = entry.ResearchingLevel;
            entry.ResearchingLevel = 0;
            entry.ReadyAtMs = 0;
            if (first) entry.Enabled = true;
            s.Count("lab_research_completed");
            ctx.Emit("lab_collected", cmd.ResearchId, entry.Level.ToString());
            return CommandResult.Success();
        }

        private static CommandResult HandleToggle(LabToggle cmd, CommandContext ctx)
        {
            var s = ctx.State;
            LabEntry entry = null;
            GameEngine.Require(cmd.ResearchId != null && s.Lab.Entries.TryGetValue(cmd.ResearchId, out entry) && entry.Level > 0, "not researched");
            entry.Enabled = cmd.Enabled;
            return CommandResult.Success();
        }
    }
}
