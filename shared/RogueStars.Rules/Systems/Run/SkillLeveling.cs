using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Run skill levels (GDD §3.3 "Skill levels"): a skill drafted, bought or forged again levels up (up to its tier's cap,
    /// RunTuning.MaxSkillLevelOf) and its OWN numbers grow along RunTuning.SkillLevelLadder (15 → 25 → 40 → 65 …): a Common
    /// skill's level L has its Lv1 numbers × ladder[L−1] / ladder[0] (×1, ×5/3, ×8/3: +15% → +25% → +40%), a Legendary's
    /// × ladder[L] / ladder[1] (×1, ×8/5), a Mythic's × ladder[L+1] / ladder[2]. Every trigger grows exactly one number:
    /// <list type="bullet">
    /// <item>passive stats (flat and %), conditional damage bonuses, the stat of BuffStat / RunStatGrowth triggers and the value
    /// of value effects (damage, heal, shield, rage, coins, heal-on-tile, awakening slow);</item>
    /// <item>count effects (status, extra dice, skipped bad events) grow their chance up to RunTuning.SkillLevelChanceCap (100%);
    /// what would go past it grows the count instead: status stacks (multi-stack statuses), status duration (single-stack
    /// statuses such as Freeze / Stun / Charm), dice / events.</item>
    /// </list>
    /// Numbers are rounded to readable steps (whole numbers from 10, halves below; counts and coins whole) and every level
    /// changes at least one number of every trigger, so each level reads differently (SkillText.Upgrade shows "+15% → +25%").
    /// Skill grants (OnRunStart "skill:" params) never scale. A skill without any growing number never levels (<see cref="Grows"/>).
    /// </summary>
    public static class SkillLeveling
    {
        /// <summary>The skill at a level (the def itself for level ≤ 1; a cached scaled copy otherwise, never mutate it).</summary>
        public static SkillDef At(ContentDb content, SkillDef def, int level)
        {
            if (def == null || level <= 1) return def;
            content = content ?? ContentDb.Default;
            var key = Key(content.RunTuning);
            var cache = Cache.GetOrCreateValue(def);
            lock (cache)
            {
                if (cache.Key != key) { cache.Key = key; cache.Levels.Clear(); }
                if (cache.Levels.TryGetValue(level, out var hit)) return hit;
            }
            var prev = At(content, def, level - 1);
            var made = Scale(content, def, prev, level);
            lock (cache) { cache.Levels[level] = made; }
            return made;
        }

        /// <summary>The skill at a level by id (null for unknown ids).</summary>
        public static SkillDef At(ContentDb content, string skillId, int level) => At(content, content?.Skill(skillId), level);

        /// <summary>True if leveling changes the skill (it has a number that grows); false: it is never offered as a level-up.</summary>
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
            return false;
        }

        /// <summary>
        /// Factor on a skill's Lv1 numbers at <paramref name="level"/> for a skill of run tier <paramref name="tier"/>
        /// (Common: 1, 5/3, 8/3; Legendary: 1, 8/5; Mythic: 1, 13/8).
        /// </summary>
        public static double Factor(ContentDb content, Rarity tier, int level)
        {
            var t = (content ?? ContentDb.Default).RunTuning;
            var rt = RunTuning.RunTier(tier);
            int offset = rt == Rarity.Mythic ? 2 : rt == Rarity.Legendary ? 1 : 0;
            return Rung(t, offset + Math.Max(1, level) - 1) / Rung(t, offset);
        }

        /// <summary>Rung i of the ladder (0-based); rungs past the table continue as the sum of the two below.</summary>
        private static double Rung(RunTuning t, int i)
        {
            var l = t.SkillLevelLadder;
            if (l == null || l.Length == 0) return 1;
            if (i < l.Length) return l[i];
            double a = l.Length >= 2 ? l[l.Length - 2] : l[0], b = l[l.Length - 1];
            for (int k = l.Length; k <= i; k++) { double c = a + b; a = b; b = c; }
            return b;
        }

        /// <summary>
        /// A signature's awakening (level 2 / 3): its one effect grows, nothing else. Counts (status stacks, Arc's jumps, Command's
        /// aliens) × level; a proc chance below 100% × 1, 1.5, 2 (Riposte, Flurry: 30% → 45% → 60%); otherwise the value × level.
        /// </summary>
        private static SkillDef Awaken(SkillDef d, int level)
        {
            var s = new SkillDef
            {
                Id = d.Id, Name = d.Name, Description = d.Description, Family = d.Family, Rarity = d.Rarity, Source = d.Source,
                Draftable = d.Draftable, Signature = true,
            };
            s.Stats.AddRange(d.Stats);
            foreach (var tr in d.Triggers)
            {
                var c = new SkillTrigger
                {
                    When = tr.When, Chance = tr.Chance, Effect = tr.Effect, Target = tr.Target, Value = tr.Value, Status = tr.Status,
                    Stacks = tr.Stacks, Duration = tr.Duration, Param = tr.Param, CooldownRounds = tr.CooldownRounds, Stat = tr.Stat,
                };
                if (tr.Effect == EffectKind.ApplyStatus || tr.Effect == EffectKind.AllyAction || tr.Target == TargetKind.OtherEnemies)
                    c.Stacks = Math.Max(1, tr.Stacks) * level;
                else if (tr.Chance > 0 && tr.Chance < 100) c.Chance = Math.Min(100, tr.Chance * (1 + 0.5 * (level - 1)));
                else c.Value = tr.Value * level;
                s.Triggers.Add(c);
            }
            return s;
        }

        private static SkillDef Scale(ContentDb content, SkillDef d, SkillDef prev, int level)
        {
            if (d.Signature) return Awaken(d, level);
            var t = content.RunTuning;
            double f = Factor(content, d.Rarity, level);
            double cap = t.SkillLevelChanceCap > 0 ? t.SkillLevelChanceCap : 100;
            var s = new SkillDef
            {
                Id = d.Id, Name = d.Name, Description = d.Description, Family = d.Family, Rarity = d.Rarity, Source = d.Source,
                UpgradesFrom = d.UpgradesFrom, RequiresFamilyCount = d.RequiresFamilyCount, Draftable = d.Draftable, Signature = d.Signature,
            };
            s.Requires.AddRange(d.Requires);
            for (int i = 0; i < d.Stats.Count; i++) s.Stats.Add(Grow(d.Stats[i], prev.Stats[i], f));
            for (int i = 0; i < d.Triggers.Count; i++)
            {
                SkillTrigger tr = d.Triggers[i], p = prev.Triggers[i];
                var c = new SkillTrigger
                {
                    When = tr.When, Chance = tr.Chance, Effect = tr.Effect, Target = tr.Target, Value = tr.Value, Status = tr.Status,
                    Stacks = tr.Stacks, Duration = tr.Duration, Param = tr.Param, CooldownRounds = tr.CooldownRounds,
                    Stat = tr.Stat != null ? Grow(tr.Stat, p.Stat, f) : null,
                };
                bool grantsSkill = tr.When == TriggerKind.OnRunStart && tr.Param != null && tr.Param != "skip_negative_events";
                if (!grantsSkill)
                {
                    if (IsCountEffect(tr.Effect)) GrowCount(content, tr, p, c, f, cap);
                    else if (tr.Effect == EffectKind.GainRunCoins) c.Value = Whole(tr.Value * f, p.Value);
                    else if (tr.Stat == null && tr.Value != 0) c.Value = Nice(tr.Value * f, p.Value);
                }
                s.Triggers.Add(c);
            }
            return s;
        }

        /// <summary>Count effects grow their chance, then (past the cap) their count; value effects grow their value.</summary>
        public static bool IsCountEffect(EffectKind k) =>
            k == EffectKind.ApplyStatus || k == EffectKind.ExtraDice || k == EffectKind.SkipNegativeEvent;

        private static void GrowCount(ContentDb content, SkillTrigger b, SkillTrigger p, SkillTrigger c, double f, double cap)
        {
            double baseChance = b.Chance > 0 ? Math.Min(b.Chance, cap) : cap;
            double want = baseChance * f;
            if (want <= cap + 1e-9)
            {
                c.Chance = Math.Min(cap, Whole(want, p.Chance));
                return;
            }
            c.Chance = cap;
            bool prevCapped = p.Chance >= cap - 1e-9;
            if (b.Effect == EffectKind.ApplyStatus)
            {
                content.Combat.Statuses.TryGetValue(b.Status, out var sd);
                if (sd != null && sd.MaxStacks <= 1)
                {
                    // single-stack statuses (Freeze, Stun, Charm) last longer instead
                    int baseDur = b.Duration > 0 ? b.Duration : Math.Max(1, sd.DefaultDuration);
                    int prevDur = p.Duration > 0 ? p.Duration : baseDur;
                    int dur = Math.Max(baseDur, (int)Math.Round(baseDur * want / cap, MidpointRounding.AwayFromZero));
                    if (prevCapped && dur <= prevDur) dur = prevDur + 1;
                    c.Duration = dur == baseDur && b.Duration <= 0 ? 0 : dur; // the default duration stays implicit
                    return;
                }
                int st = Math.Max(b.Stacks, (int)Math.Round(Math.Max(1, b.Stacks) * want / cap, MidpointRounding.AwayFromZero));
                if (prevCapped && st <= p.Stacks) st = p.Stacks + 1;
                c.Stacks = st;
                return;
            }
            double n = Math.Max(b.Value, Math.Round(Math.Max(1, b.Value) * want / cap, MidpointRounding.AwayFromZero));
            if (prevCapped && n <= p.Value) n = p.Value + 1;
            c.Value = n;
        }

        private static StatMod Grow(StatMod b, StatMod p, double f) =>
            new StatMod(b.Stat, b.Flat != 0 ? Nice(b.Flat * f, p.Flat) : 0, b.Percent != 0 ? Nice(b.Percent * f, p.Percent) : 0);

        /// <summary>Readable rounding (whole numbers from 10, halves below) that always moves past the previous level's number.</summary>
        private static double Nice(double v, double prev)
        {
            double a = Math.Abs(v);
            double step = a >= 10 ? 1 : 0.5;
            double r = Math.Round(a / step, MidpointRounding.AwayFromZero) * step;
            double pa = Math.Abs(prev);
            if (r <= pa + 1e-9) r = pa + (pa >= 10 ? 1 : 0.5);
            return v < 0 ? -r : r;
        }

        private static double Whole(double v, double prev)
        {
            double r = Math.Round(Math.Abs(v), MidpointRounding.AwayFromZero);
            if (r <= Math.Abs(prev) + 1e-9) r = Math.Abs(prev) + 1;
            return v < 0 ? -r : r;
        }

        private static string Key(RunTuning t) => string.Join(",", t.SkillLevelLadder ?? new double[0]) + "|" + t.SkillLevelChanceCap;

        private sealed class LevelCache
        {
            public string Key;
            public readonly Dictionary<int, SkillDef> Levels = new Dictionary<int, SkillDef>();
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SkillDef, LevelCache> Cache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<SkillDef, LevelCache>();
    }
}
