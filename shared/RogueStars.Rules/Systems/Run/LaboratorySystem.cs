using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Laboratory (GDD §5): one tree of skill nodes (draft-pool skills, toggleable) and stat nodes (loadout bonuses).
    /// Research costs Data and time; the k-th research level started takes LabTuning.Hours(k) (exponential in k), shortened
    /// by CitySystem.ResearchSpeedPct. Research is locked until the Laboratory building (Mythic) is placed; its slots
    /// (CitySystem.ResearchSlots: 1, +1 at 5★ and 10★) need it staffed. A research started while the first slot is busy is flagged
    /// InAnnex (×AnnexDurationMult, 1 since the City redesign).
    /// Research runs on a City <see cref="TimerSystem"/> timer (kind "lab_research"), so TimerSpeedUp (Diamonds / ad /
    /// daily free) works on it; the player collects with LabCollect once the timer is due.
    /// </summary>
    public static class LaboratorySystem
    {
        public const string TimerKind = "lab_research";

        public static void Register(GameEngine e)
        {
            e.Register<LabResearch>(HandleResearch);
            e.Register<LabCollect>(HandleCollect);
            e.Register<LabToggle>(HandleToggle);
            e.RegisterSettler(StopRemovedResearch);
            e.RegisterNewPlayer(ctx =>
            {
                foreach (var id in SortedIds(ctx.Content))
                    if (ctx.Content.LabResearch[id].DefaultResearched) ctx.State.Lab.Entries[id] = new LabEntry { Level = 1, Enabled = true };
            });
            e.RegisterLoadoutContributor((state, content, now, loadout) =>
            {
                foreach (var sid in ActiveSkillIds(state, content))
                    if (!loadout.DraftPoolSkillIds.Contains(sid)) loadout.DraftPoolSkillIds.Add(sid);
                foreach (var m in StatBonuses(state, content)) loadout.WormStats.Add(m);
                loadout.UnlockedFamilies.Clear();
                loadout.UnlockedFamilies.AddRange(UnlockedFamilies(state, content));
            });
        }

        /// <summary>
        /// Saved states may hold nodes that content no longer has (the skill tuning pass removed Treasure Sense and Drone Bay):
        /// they are ignored everywhere (sorted content ids drive the tree), and a research still running on one is stopped so it
        /// never blocks a slot. Its researched levels stay counted in the research index (the Data was spent).
        /// </summary>
        public static void StopRemovedResearch(CommandContext ctx)
        {
            var lab = ctx.State.Lab;
            if (lab.Entries.Count == 0) return;
            List<string> stale = null;
            foreach (var kv in lab.Entries)
                if (kv.Value.ResearchingLevel > 0 && ctx.Content.LabNode(kv.Key) == null) (stale = stale ?? new List<string>()).Add(kv.Key);
            if (stale == null) return;
            stale.Sort(StringComparer.Ordinal);
            foreach (var id in stale)
            {
                var entry = lab.Entries[id];
                if (entry.TimerId != null) TimerSystem.Remove(ctx.State, entry.TimerId);
                entry.TimerId = null;
                entry.ResearchingLevel = 0;
                entry.ReadyAtMs = 0;
                entry.InAnnex = false;
            }
        }

        /// <summary>Skill ids currently toggled on for the draft pool (current level's skill of each enabled skill node).</summary>
        public static List<string> ActiveSkillIds(GameState state, ContentDb content)
        {
            var list = new List<string>();
            foreach (var id in SortedIds(content))
            {
                var def = content.LabResearch[id];
                if (def.Kind != LabNodeKind.Skill || !state.Lab.Entries.TryGetValue(id, out var e) || e.Level <= 0 || !e.Enabled) continue;
                list.Add(def.Levels[Math.Min(e.Level, def.Levels.Count) - 1].SkillId);
            }
            return list;
        }

        /// <summary>
        /// Synergies open for runs: RunTuning.StarterSynergies (Fire, Radiation, Bleed) plus every researched package node's family,
        /// in a stable order (starters, then packages by id).
        /// </summary>
        public static List<SkillFamily> UnlockedFamilies(GameState state, ContentDb content)
        {
            var list = new List<SkillFamily>(content.RunTuning.StarterSynergies);
            foreach (var id in SortedIds(content))
            {
                var def = content.LabResearch[id];
                if (def.Kind == LabNodeKind.Package && Entry(state, id).Level > 0 && !list.Contains(def.Family)) list.Add(def.Family);
            }
            return list;
        }

        /// <summary>Stat bonuses of all researched stat nodes (contributed to the worm's stats; AlienPower/MountPower reach the squad/mount).</summary>
        public static List<StatMod> StatBonuses(GameState state, ContentDb content)
        {
            var list = new List<StatMod>();
            foreach (var id in SortedIds(content))
            {
                var def = content.LabResearch[id];
                if (def.Kind != LabNodeKind.Stat || !state.Lab.Entries.TryGetValue(id, out var e) || e.Level <= 0) continue;
                list.AddRange(def.Levels[Math.Min(e.Level, def.Levels.Count) - 1].Stats);
            }
            return list;
        }

        public static LabEntry Entry(GameState state, string researchId) =>
            state.Lab.Entries.TryGetValue(researchId, out var e) ? e : new LabEntry();

        /// <summary>
        /// Research index k: levels researched or running beyond the default ones. The next research is the k-th and
        /// takes LabTuning.Hours(k) / costs LabTuning.DataCost(k), whatever the node.
        /// </summary>
        public static int ResearchIndex(GameState state, ContentDb content)
        {
            int k = 0;
            foreach (var kv in state.Lab.Entries)
            {
                k += kv.Value.Level + (kv.Value.ResearchingLevel > 0 ? 1 : 0);
                var def = content.LabNode(kv.Key);
                if (def != null && def.DefaultResearched) k--;
            }
            return Math.Max(0, k);
        }

        /// <summary>Data cost of the next research.</summary>
        public static long NextDataCost(GameState state, ContentDb content) => content.LabTuning.DataCost(ResearchIndex(state, content));

        /// <summary>Running research count.</summary>
        public static int Running(GameState state)
        {
            int n = 0;
            foreach (var e in state.Lab.Entries.Values) if (e.ResearchingLevel > 0) n++;
            return n;
        }

        /// <summary>
        /// Research slots: the Laboratory building's (1, +1 at 5★ and 10★) while it runs (placed, built, staffed), capped by
        /// LabTuning.MaxSlots; 0 without a running Laboratory.
        /// </summary>
        public static int Slots(GameState state, ContentDb content) =>
            Math.Min(Math.Max(1, content.LabTuning.MaxSlots), CitySystem.ResearchSlots(state, content));

        /// <summary>Skill research stays locked until the Laboratory building is placed and built in the City.</summary>
        public static bool BuildingPlaced(GameState state) => CitySystem.Laboratory(state) != null;

        /// <summary>True if the next research would run in the Annex slot (every main slot busy).</summary>
        public static bool NextInAnnex(GameState state, ContentDb content)
        {
            int main = 0;
            foreach (var e in state.Lab.Entries.Values) if (e.ResearchingLevel > 0 && !e.InAnnex) main++;
            return main >= Math.Max(1, content.RunTuning.LabSlots);
        }

        /// <summary>Nominal duration of the next research (before the building boon), Annex multiplier included.</summary>
        public static long NextNominalMs(GameState state, ContentDb content)
        {
            double h = content.LabTuning.Hours(ResearchIndex(state, content));
            if (NextInAnnex(state, content)) h *= content.LabTuning.AnnexDurationMult;
            return (long)Math.Round(h * 3_600_000);
        }

        /// <summary>Duration of the next research after building boons (what LabResearch starts).</summary>
        public static long NextDurationMs(GameState state, ContentDb content) => DurationMs(state, content, NextNominalMs(state, content));

        /// <summary>True if level 1 of the node may be started (prerequisites: any / all of def.Requires).</summary>
        public static bool PrerequisitesMet(GameState state, ContentDb content, LabResearchDef def)
        {
            if (def.Requires.Count == 0) return true;
            int met = 0;
            foreach (var r in def.Requires) if (Entry(state, r.ResearchId).Level >= r.Level) met++;
            return def.RequireAll ? met == def.Requires.Count : met > 0;
        }

        /// <summary>Why a node cannot be researched now (null = it can, Data and slots permitting).</summary>
        public static string BlockReason(GameState state, ContentDb content, LabResearchDef def)
        {
            var entry = Entry(state, def.Id);
            if (entry.ResearchingLevel > 0) return "already researching";
            if (entry.Level >= def.Levels.Count) return "research maxed";
            if (entry.Level == 0 && !PrerequisitesMet(state, content, def)) return "prerequisites missing";
            if (entry.Level == 0 && def.RequiresFeature != null && !Features.IsUnlocked(state, content, def.RequiresFeature))
                return (content.Feature(def.RequiresFeature)?.Name ?? def.RequiresFeature) + " locked";
            if (!BuildingPlaced(state)) return "build the Laboratory in the City";
            if (Slots(state, content) == 0) return "the Laboratory needs an alien";
            if (Running(state) >= Slots(state, content)) return "laboratory busy";
            return null;
        }

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
            string block = BlockReason(s, c, def);
            GameEngine.Require(block == null, block);
            long data = NextDataCost(s, c);
            GameEngine.Require(s.Wallet.CanAfford(Currencies.Data, data), "not enough data");
            bool annex = NextInAnnex(s, c);
            long duration = NextDurationMs(s, c);
            s.Wallet.TrySpend(Currencies.Data, data);
            var entry = Entry(s, cmd.ResearchId);
            entry.ResearchingLevel = entry.Level + 1;
            entry.InAnnex = annex;
            var timer = TimerSystem.Start(ctx, TimerKind, cmd.ResearchId, duration);
            entry.TimerId = timer.Id;
            entry.ReadyAtMs = timer.EndsMs;
            s.Lab.Entries[cmd.ResearchId] = entry;
            s.Count("lab_research_started");
            s.Count("data_spent", data);
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
            entry.InAnnex = false;
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
            var def = ctx.Content.LabNode(cmd.ResearchId);
            GameEngine.Require(def != null && def.Kind == LabNodeKind.Skill, "only skill research can be toggled");
            entry.Enabled = cmd.Enabled;
            return CommandResult.Success();
        }
    }
}
