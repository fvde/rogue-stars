using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Run skill levels (GDD §3.3 "Skill levels"): a skill drafted, bought or forged again levels up (up to
    /// RunTuning.MaxSkillLevel) and its OWN effect grows — no generic stat bonus. Level L (L ≥ 2) is the skill with:
    /// <list type="bullet">
    /// <item>every passive stat (flat and %) × (1 + SkillLevelEffectPct/100 × (L − 1));</item>
    /// <item>every trigger value (damage / true damage / heal / shield / rage / coins / heal-on-tile / awakening slow / loot % /
    /// summon power / conditional damage bonus) and the stat of BuffStat / RunStatGrowth triggers × the same factor;</item>
    /// <item>trigger chances below 100% × the same factor, capped at SkillLevelChanceCap (never below the base chance);</item>
    /// <item>+1 status stack every SkillLevelStacksEvery levels, +1 round of duration (timed statuses / buffs) every
    /// SkillLevelDurationEvery levels.</item>
    /// </list>
    /// Counts stay whole: extra dice and skipped bad events do not scale, nor do skill grants (OnRunStart "skill:" params).
    /// A skill whose leveled form equals its base form cannot level (<see cref="Grows"/>; never offered as a level-up).
    /// </summary>
    public static class SkillLeveling
    {
        /// <summary>The skill at a level (the def itself for level ≤ 1; a cached scaled copy otherwise, never mutate it).</summary>
        public static SkillDef At(ContentDb content, SkillDef def, int level)
        {
            if (def == null || level <= 1) return def;
            var t = (content ?? ContentDb.Default).RunTuning;
            var key = Key(t);
            var cache = Cache.GetOrCreateValue(def);
            lock (cache)
            {
                if (cache.Key != key) { cache.Key = key; cache.Levels.Clear(); }
                if (cache.Levels.TryGetValue(level, out var hit)) return hit;
                var made = Scale(t, def, level);
                cache.Levels[level] = made;
                return made;
            }
        }

        /// <summary>The skill at a level by id (null for unknown ids).</summary>
        public static SkillDef At(ContentDb content, string skillId, int level) => At(content, content?.Skill(skillId), level);

        /// <summary>True if leveling changes the skill (it has a number that scales); false: it is never offered as a level-up.</summary>
        public static bool Grows(ContentDb content, SkillDef def)
        {
            if (def == null) return false;
            var two = At(content, def, 2);
            for (int i = 0; i < def.Stats.Count; i++)
                if (def.Stats[i].Flat != two.Stats[i].Flat || def.Stats[i].Percent != two.Stats[i].Percent) return true;
            for (int i = 0; i < def.Triggers.Count; i++)
            {
                SkillTrigger a = def.Triggers[i], b = two.Triggers[i];
                if (a.Value != b.Value || a.Chance != b.Chance || a.Stacks != b.Stacks || a.Duration != b.Duration) return true;
                if (a.Stat != null && (a.Stat.Flat != b.Stat.Flat || a.Stat.Percent != b.Stat.Percent)) return true;
            }
            // stacks / durations may only grow at level 3
            var three = At(content, def, 3);
            for (int i = 0; i < def.Triggers.Count; i++)
                if (def.Triggers[i].Stacks != three.Triggers[i].Stacks || def.Triggers[i].Duration != three.Triggers[i].Duration) return true;
            return false;
        }

        /// <summary>Factor on the skill's numbers at a level: 1 + SkillLevelEffectPct/100 × (level − 1).</summary>
        public static double Factor(ContentDb content, int level) =>
            1 + (content ?? ContentDb.Default).RunTuning.SkillLevelEffectPct / 100.0 * Math.Max(0, level - 1);

        private static SkillDef Scale(RunTuning t, SkillDef d, int level)
        {
            double f = 1 + t.SkillLevelEffectPct / 100.0 * (level - 1);
            int extraStacks = t.SkillLevelStacksEvery > 0 ? (level - 1) / t.SkillLevelStacksEvery : 0;
            int extraRounds = t.SkillLevelDurationEvery > 0 ? (level - 1) / t.SkillLevelDurationEvery : 0;
            var s = new SkillDef
            {
                Id = d.Id, Name = d.Name, Description = d.Description, Family = d.Family, Rarity = d.Rarity, Source = d.Source,
                UpgradesFrom = d.UpgradesFrom, RequiresFamilyCount = d.RequiresFamilyCount, Draftable = d.Draftable,
            };
            s.Requires.AddRange(d.Requires);
            foreach (var m in d.Stats) s.Stats.Add(new StatMod(m.Stat, Round(m.Flat * f), Round(m.Percent * f)));
            foreach (var tr in d.Triggers)
            {
                var c = new SkillTrigger
                {
                    When = tr.When, Chance = tr.Chance, Effect = tr.Effect, Target = tr.Target, Value = tr.Value, Status = tr.Status,
                    Stacks = tr.Stacks, Duration = tr.Duration, Param = tr.Param, CooldownRounds = tr.CooldownRounds,
                    Stat = tr.Stat != null ? new StatMod(tr.Stat.Stat, Round(tr.Stat.Flat * f), Round(tr.Stat.Percent * f)) : null,
                };
                bool grantsSkill = tr.When == TriggerKind.OnRunStart && tr.Param != null && tr.Param != "skip_negative_events";
                if (ScalesValue(tr.Effect) && !grantsSkill) c.Value = Round(tr.Value * f);
                if (tr.Chance > 0 && tr.Chance < 100) c.Chance = Round(Math.Max(tr.Chance, Math.Min(t.SkillLevelChanceCap, tr.Chance * f)));
                if (tr.Effect == EffectKind.ApplyStatus) c.Stacks = tr.Stacks + extraStacks;
                if (tr.Duration > 0 && (tr.Effect == EffectKind.ApplyStatus || tr.Effect == EffectKind.BuffStat)) c.Duration = tr.Duration + extraRounds;
                s.Triggers.Add(c);
            }
            return s;
        }

        private static bool ScalesValue(EffectKind k)
        {
            switch (k)
            {
                case EffectKind.ExtraDice:
                case EffectKind.SkipNegativeEvent:
                case EffectKind.SlowAwakening: // the run clock (awakening) is a pacing pillar: it does not grow with skill levels
                    return false; // whole counts
                default:
                    return true;
            }
        }

        private static double Round(double v) => Math.Round(v, 2);

        private static string Key(RunTuning t) => t.SkillLevelEffectPct + "|" + t.SkillLevelChanceCap + "|" + t.SkillLevelStacksEvery + "|" + t.SkillLevelDurationEvery;

        private sealed class LevelCache
        {
            public string Key;
            public readonly Dictionary<int, SkillDef> Levels = new Dictionary<int, SkillDef>();
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SkillDef, LevelCache> Cache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<SkillDef, LevelCache>();
    }
}
