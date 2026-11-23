using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    /// <summary>
    /// Exact, player-facing text of what a skill does, generated from its stat package and triggers (numbers included),
    /// so the UI never depends on hand-written descriptions staying in sync with the data.
    /// </summary>
    public static class SkillText
    {
        /// <summary>One line per effect, e.g. "+8% Crit Chance", "On rage attack: apply Bleed ×2 to the enemy".</summary>
        public static List<string> Effects(SkillDef s)
        {
            var lines = new List<string>();
            if (s == null) return lines;
            foreach (var m in s.Stats) { var t = Stat(m); if (t != null) lines.Add(t); }
            foreach (var t in s.Triggers) { var line = Trigger(t); if (line != null) lines.Add(line); }
            return lines;
        }

        /// <summary>Effect lines of a run skill at a level (its leveled numbers, SkillLeveling); level ≤ 1 = the base skill.</summary>
        public static List<string> Effects(ContentDb content, SkillDef s, int level) => Effects(SkillLeveling.At(content, s, level));

        /// <summary>All effect lines of a run skill at a level in one paragraph.</summary>
        public static string Full(ContentDb content, SkillDef s, int level) => Full(SkillLeveling.At(content, s, level));

        /// <summary>"Spiked Hide" / "Spiked Hide Lv 3".</summary>
        public static string Title(SkillDef s, int level) => s == null ? "" : s.Name + (level > 1 ? " Lv " + level : "");

        /// <summary>The level-up rule in one line, with the exact numbers from RunTuning (e.g. "Level up: +25% to all its numbers …").</summary>
        public static string LevelUpRule(ContentDb content)
        {
            var t = (content ?? ContentDb.Default).RunTuning;
            var sb = new StringBuilder("Level up: +").Append(Num(t.SkillLevelEffectPct)).Append("% to all its numbers (chances up to ")
                .Append(Num(t.SkillLevelChanceCap)).Append("%)");
            if (t.SkillLevelStacksEvery > 0) sb.Append(", +1 stack / round every ").Append(t.SkillLevelStacksEvery).Append(" levels");
            return sb.ToString();
        }

        /// <summary>All effect lines joined into one paragraph ("" when the skill has no effects).</summary>
        public static string Full(SkillDef s)
        {
            var lines = Effects(s);
            var sb = new StringBuilder();
            foreach (var l in lines) { if (sb.Length > 0) sb.Append(' '); sb.Append(l).Append('.'); }
            return sb.ToString();
        }

        /// <summary>Stats stored in percent points (flat 25 = 25%); the rest (HP, ATK, DEF, Speed) are plain numbers.</summary>
        public static bool IsPointStat(StatId s) => s != StatId.Hp && s != StatId.Atk && s != StatId.Def && s != StatId.Speed;

        public static string StatName(StatId s) => s switch
        {
            StatId.Hp => "HP",
            StatId.Atk => "ATK",
            StatId.Def => "DEF",
            StatId.CritChance => "crit chance",
            StatId.CritDamage => "crit damage",
            StatId.CritResist => "crit resist",
            StatId.DodgeIgnore => "dodge ignore",
            StatId.ComboChance => "combo chance",
            StatId.CounterChance => "counter chance",
            StatId.ParryChance => "parry chance",
            StatId.ParryReduction => "parry damage reduction",
            StatId.DamageReduction => "damage reduction",
            StatId.LifeSteal => "life steal",
            StatId.Regen => "regeneration",
            StatId.RageGain => "rage gain",
            StatId.ShieldPower => "shield power",
            StatId.HealPower => "healing",
            StatId.StatusChance => "status chance",
            StatId.StatusResist => "status resist",
            StatId.BossDamage => "boss damage",
            StatId.FireDamage => "fire damage",
            StatId.IceDamage => "ice damage",
            StatId.LightningDamage => "lightning damage",
            StatId.PoisonDamage => "poison damage",
            StatId.BleedDamage => "bleed damage",
            StatId.AlienPower => "alien power",
            StatId.MountPower => "mount power",
            StatId.LootLuck => "loot luck",
            StatId.XpGain => "run XP",
            _ => Words(s.ToString()),
        };

        public static string Stat(StatMod m)
        {
            if (m == null) return null;
            var parts = new List<string>();
            if (m.Flat != 0) parts.Add(Signed(m.Flat) + (IsPointStat(m.Stat) ? "%" : ""));
            if (m.Percent != 0) parts.Add(Signed(m.Percent) + "%");
            return parts.Count == 0 ? null : string.Join(" and ", parts) + " " + StatName(m.Stat);
        }

        private static string Trigger(SkillTrigger t)
        {
            if (t.When == TriggerKind.Passive && t.Param != null && t.Param.StartsWith("cond:", StringComparison.Ordinal))
                return Signed(t.Value) + "% damage " + Condition(t.Param.Substring(5));
            string effect = Effect(t);
            if (effect == null) return null;
            string body = (t.Chance > 0 && t.Chance < 100 ? Num(t.Chance) + "% chance to " : "") + effect;
            string when = When(t.When);
            var sb = new StringBuilder(when != null ? when + ": " + body : Cap(body));
            if (t.CooldownRounds > 0) sb.Append(" (every ").Append(t.CooldownRounds).Append(t.CooldownRounds == 1 ? " round)" : " rounds)");
            return sb.ToString();
        }

        private static string Cap(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;

        private static string Effect(SkillTrigger t)
        {
            string target = Target(t.Target);
            string dur = t.Duration > 0 ? " for " + t.Duration + (t.Duration == 1 ? " round" : " rounds") : "";
            switch (t.Effect)
            {
                case EffectKind.Damage: return "deal " + Num(t.Value) + "% ATK damage to " + target;
                case EffectKind.TrueDamage: return "deal " + Num(t.Value) + "% ATK true damage (ignores defense) to " + target;
                case EffectKind.ApplyStatus:
                    return "apply " + t.Status + (t.Stacks > 1 ? " ×" + t.Stacks : "") + dur + " to " + target;
                case EffectKind.Heal: return "heal " + (t.Target == TargetKind.Self ? "" : target + " for ") + Num(t.Value) + "% of max HP";
                case EffectKind.Shield: return "gain a shield of " + Num(t.Value) + "% of max HP" + (t.Target == TargetKind.Self ? "" : " for " + target);
                case EffectKind.GainRage: return "gain " + Num(t.Value) + " rage";
                case EffectKind.BuffStat:
                    {
                        var stat = Stat(t.Stat);
                        if (stat == null) return null;
                        return (t.Target == TargetKind.Self ? "" : target + " get ") + stat + (t.Duration > 0 ? dur : " for the rest of the battle");
                    }
                case EffectKind.RunStatGrowth:
                    {
                        var stat = Stat(t.Stat);
                        return stat == null ? null : stat + " for the rest of the run";
                    }
                case EffectKind.GainRunCoins: return "gain " + Num(t.Value) + " coins";
                case EffectKind.ExtraDice: return "roll " + Num(t.Value) + " extra " + (t.Value == 1 ? "die" : "dice") + " next roll";
                case EffectKind.HealOnTile: return "heal " + Num(t.Value) + "% of max HP";
                case EffectKind.SlowAwakening: return "slow the boss's awakening by " + Num(t.Value);
                case EffectKind.ExtraLoot: return "+" + Num(t.Value) + "% loot";
                case EffectKind.Summon: return "summon " + Words(t.Param ?? "an ally");
                case EffectKind.SkipNegativeEvent: return "skip " + Num(t.Value) + " bad event " + (t.Value == 1 ? "outcome" : "outcomes") + " per run";
                default: return Words(t.Effect.ToString());
            }
        }

        private static string When(TriggerKind k) => k switch
        {
            TriggerKind.Passive => null,
            TriggerKind.OnBattleStart => "Battle start",
            TriggerKind.OnRoundStart => "Each round",
            TriggerKind.OnRoundEnd => "End of each round",
            TriggerKind.OnTurnStart => "Each turn",
            TriggerKind.OnAttack => "On attack",
            TriggerKind.OnHit => "On hit",
            TriggerKind.OnCrit => "On crit",
            TriggerKind.OnCombo => "On combo",
            TriggerKind.OnCounter => "On counter",
            TriggerKind.OnDodge => "On dodge",
            TriggerKind.OnParry => "On parry",
            TriggerKind.OnDamaged => "When hit",
            TriggerKind.OnKill => "On kill",
            TriggerKind.OnLowHp => "Below 30% HP",
            TriggerKind.OnRageAttack => "On rage attack",
            TriggerKind.OnStatusApplied => "When you apply a status",
            TriggerKind.OnRunStart => "Run start",
            TriggerKind.OnRoll => "Each roll",
            TriggerKind.OnTileLand => "Each tile you land on",
            TriggerKind.OnTilePass => "Each tile you pass",
            TriggerKind.OnLevelUp => "Each run level up",
            TriggerKind.OnBossAwaken => "When the boss awakens",
            TriggerKind.OnBattleWon => "After each won battle",
            _ => Words(k.ToString()),
        };

        private static string Target(TargetKind t) => t switch
        {
            TargetKind.Self => "yourself",
            TargetKind.Enemy => "the enemy",
            TargetKind.AllEnemies => "all enemies",
            TargetKind.RandomEnemy => "a random enemy",
            TargetKind.LowestHpEnemy => "the weakest enemy",
            TargetKind.Worm => "the worm",
            TargetKind.AllAllies => "all allies",
            _ => Words(t.ToString()),
        };

        private static string Condition(string c) => c switch
        {
            "vs_boss" => "against bosses",
            "vs_elite" => "against elites and bosses",
            "vs_high_hp" => "against enemies above 70% HP",
            "vs_low_hp" => "against enemies below 40% HP",
            "vs_shielded" => "against shielded enemies",
            "basic" => "with basic attacks",
            "combo" => "with combo attacks",
            "counter" => "with counterattacks",
            "rage" => "with rage attacks",
            "skill" => "with skill attacks",
            _ when c.StartsWith("vs_status:", StringComparison.Ordinal) => "against enemies with " + c.Substring(10),
            _ => "when " + Words(c),
        };

        private static string Signed(double v) => (v > 0 ? "+" : "") + Num(v);
        private static string Num(double v) => v.ToString(Math.Abs(v - Math.Round(v)) < 1e-9 ? "0" : "0.#", CultureInfo.InvariantCulture);

        /// <summary>"OnRageAttack" / "low_hp" → "on rage attack" / "low hp".</summary>
        public static string Words(string id)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < id.Length; i++)
            {
                char ch = id[i];
                if (ch == '_' || ch == '-') { sb.Append(' '); continue; }
                if (char.IsUpper(ch) && i > 0 && id[i - 1] != '_') sb.Append(' ');
                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }
    }
}
