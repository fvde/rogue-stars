using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
        public static List<string> Effects(SkillDef s) => Lines(s, null);

        /// <summary>Rendering options for an upgrade: per trigger, show numbers the other level needs (chance, ×stacks, duration).</summary>
        private sealed class Show
        {
            public bool Chance, Stacks, Duration;
            public int DefaultDuration;
        }

        private static List<string> Lines(SkillDef s, Show[] show)
        {
            var lines = new List<string>();
            if (s == null) return lines;
            foreach (var m in s.Stats) { var t = Stat(m); if (t != null) lines.Add(t); }
            for (int i = 0; i < s.Triggers.Count; i++)
            {
                var line = Trigger(s.Triggers[i], show != null && i < show.Length ? show[i] : null);
                if (line != null) lines.Add(line);
            }
            return lines;
        }

        /// <summary>
        /// The effect lines of an upgrade from level <paramref name="from"/> to <paramref name="to"/> with the changed numbers
        /// inline, e.g. "+15% → +25% crit chance", "On hit: 30% → 50% chance to apply Burn to the enemy".
        /// </summary>
        public static List<string> Upgrade(ContentDb content, SkillDef s, int from, int to)
        {
            content = content ?? ContentDb.Default;
            var a = SkillLeveling.At(content, s, Math.Max(1, from));
            var b = SkillLeveling.At(content, s, Math.Max(1, to));
            if (a == null || b == null) return Effects(b ?? a);
            var show = new Show[a.Triggers.Count];
            for (int i = 0; i < a.Triggers.Count && i < b.Triggers.Count; i++)
            {
                SkillTrigger x = a.Triggers[i], y = b.Triggers[i];
                var o = new Show
                {
                    Chance = (x.Chance > 0 && x.Chance < 100) || (y.Chance > 0 && y.Chance < 100),
                    Stacks = x.Stacks != y.Stacks,
                    Duration = x.Duration != y.Duration,
                };
                if (o.Duration && content.Combat.Statuses.TryGetValue(x.Status, out var sd)) o.DefaultDuration = sd.DefaultDuration;
                show[i] = o;
            }
            var la = Lines(a, show);
            var lb = Lines(b, show);
            var res = new List<string>();
            for (int i = 0; i < Math.Max(la.Count, lb.Count); i++)
            {
                if (i >= la.Count) { res.Add(lb[i]); continue; }
                if (i >= lb.Count) { res.Add(la[i]); continue; }
                res.Add(Diff(la[i], lb[i]));
            }
            return res;
        }

        /// <summary>All upgrade lines (<see cref="Upgrade"/>) in one paragraph.</summary>
        public static string UpgradeFull(ContentDb content, SkillDef s, int from, int to) => Join(Upgrade(content, s, from, to));

        private static readonly Regex NumberToken =
            new Regex(@"[+\-×]?\d+(?:\.\d+)?%?(?: rounds?\b| extra di(?:e|ce)\b| bad event outcomes?\b)?", RegexOptions.CultureInvariant);

        /// <summary>"+15% crit chance" + "+25% crit chance" → "+15% → +25% crit chance" (whole lines when the wording differs).</summary>
        public static string Diff(string a, string b)
        {
            if (a == b) return a;
            var ma = NumberToken.Matches(a);
            var mb = NumberToken.Matches(b);
            if (ma.Count == mb.Count && ma.Count > 0 && NumberToken.Replace(a, "#") == NumberToken.Replace(b, "#"))
            {
                var sb = new StringBuilder();
                int pos = 0;
                for (int i = 0; i < ma.Count; i++)
                {
                    sb.Append(a, pos, ma[i].Index - pos);
                    sb.Append(ma[i].Value);
                    if (ma[i].Value != mb[i].Value) sb.Append(Arrow).Append(mb[i].Value);
                    pos = ma[i].Index + ma[i].Length;
                }
                sb.Append(a, pos, a.Length - pos);
                return sb.ToString();
            }
            return a + Arrow + b;
        }

        /// <summary>The arrow between an old and a new number.</summary>
        public const string Arrow = " → ";

        /// <summary>Effect lines of a run skill at a level (its leveled numbers, SkillLeveling); level ≤ 1 = the base skill.</summary>
        public static List<string> Effects(ContentDb content, SkillDef s, int level) => Effects(SkillLeveling.At(content, s, level));

        /// <summary>All effect lines of a run skill at a level in one paragraph.</summary>
        public static string Full(ContentDb content, SkillDef s, int level) => Full(SkillLeveling.At(content, s, level));

        /// <summary>"Spiked Hide Lv 2/3" (Common: 3 levels, Legendary: 2), a Mythic just its name (it never levels).</summary>
        public static string Title(ContentDb content, SkillDef s, int level)
        {
            if (s == null) return "";
            if (s.Signature) return s.Name + (level > 1 ? " (Awakened " + (level - 1) + ")" : "");
            int max = (content ?? ContentDb.Default).RunTuning.MaxSkillLevelOf(s.Rarity);
            return max <= 1 ? s.Name : s.Name + " Lv " + Math.Max(1, level) + "/" + max;
        }

        /// <summary>"Spiked Hide" / "Spiked Hide Lv 3".</summary>
        public static string Title(SkillDef s, int level) => s == null ? "" : s.Name + (level > 1 ? " Lv " + level : "");

        /// <summary>The level-up rule in one line, from RunTuning's ladder ("Level up: its numbers climb +15% → +25% → +40% …").</summary>
        public static string LevelUpRule(ContentDb content)
        {
            content = content ?? ContentDb.Default;
            var t = content.RunTuning;
            var sb = new StringBuilder("Level up: its numbers climb ");
            Ladder(sb, content, Rarity.Common, 15);
            if (t.MaxSkillLevelOf(Rarity.Legendary) > 1)
            {
                sb.Append(" (Legendary ");
                Ladder(sb, content, Rarity.Legendary, 15 * SkillLeveling.Factor(content, Rarity.Common, 2));
                sb.Append(')');
            }
            sb.Append("; chances stop at ").Append(Num(t.SkillLevelChanceCap)).Append("%, then stacks or duration grow");
            return sb.ToString();
        }

        private static void Ladder(StringBuilder sb, ContentDb content, Rarity tier, double first)
        {
            for (int lv = 1; lv <= content.RunTuning.MaxSkillLevelOf(tier); lv++)
                sb.Append(lv > 1 ? Arrow : "").Append('+').Append(Num(Math.Round(first * SkillLeveling.Factor(content, tier, lv)))).Append('%');
        }

        /// <summary>All effect lines joined into one paragraph ("" when the skill has no effects).</summary>
        public static string Full(SkillDef s) => Join(Effects(s));

        private static string Join(List<string> lines)
        {
            var sb = new StringBuilder();
            foreach (var l in lines) { if (sb.Length > 0) sb.Append(' '); sb.Append(l).Append('.'); }
            return sb.ToString();
        }

        /// <summary>Player-facing synergy name ("Radiation" for the internal Poison id, "Alien" for AlienSynergy).</summary>
        public static string FamilyName(SkillFamily f) => f switch
        {
            SkillFamily.Poison => "Radiation",
            SkillFamily.AlienSynergy => "Alien",
            SkillFamily.None => "Generic",
            _ => f.ToString(),
        };

        /// <summary>Player-facing status name ("Radiation" for the internal Poison id; the legacy Burn reads as Heat).</summary>
        public static string StatusName(StatusId s) => s switch
        {
            StatusId.Poison => "Radiation",
            StatusId.Burn => "Heat",
            _ => s.ToString(),
        };

        /// <summary>
        /// A signature's awakening lines: "Awakened (3 Fire skills): On hit: apply Heat ×2 to the enemy." for each awakening level
        /// (RunTuning.SignatureAwakenAt). Empty for other skills.
        /// </summary>
        public static List<string> Awakenings(ContentDb content, SkillDef s)
        {
            content = content ?? ContentDb.Default;
            var lines = new List<string>();
            if (s == null || !s.Signature) return lines;
            var at = content.RunTuning.SignatureAwakenAt;
            for (int i = 0; i < at.Length; i++)
                lines.Add("Awakened (" + at[i] + " " + FamilyName(s.Family) + " skills): " + Full(SkillLeveling.At(content, s, i + 2)));
            return lines;
        }

        /// <summary>Stats stored in percent points (flat 25 = 25%); the rest (HP, ATK, DEF, Speed) are plain numbers.</summary>
        public static bool IsPointStat(StatId s) => StatMod.IsPointStat(s);

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
            StatId.PoisonDamage => "radiation damage",
            StatId.SupernovaDamage => "Supernova damage",
            StatId.SupernovaGuard => "Supernova self-damage reduction",
            StatId.BleedDamage => "bleed damage",
            StatId.AlienPower => "alien power",
            StatId.MountPower => "mount power",
            StatId.LootLuck => "bonus loot chance",
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

        private static string Trigger(SkillTrigger t, Show show = null)
        {
            if (t.When == TriggerKind.Passive && t.Param != null && t.Param.StartsWith("cond:", StringComparison.Ordinal))
                return Signed(t.Value) + "% damage " + Condition(t.Param.Substring(5));
            string effect = Effect(t, show);
            if (effect == null) return null;
            bool chance = t.Chance > 0 && (t.Chance < 100 || (show != null && show.Chance));
            string body = (chance ? Num(Math.Min(100, t.Chance)) + "% chance to " : "") + effect;
            string when = When(t.When);
            if (t.Param != null && t.Param.StartsWith("tile:", StringComparison.Ordinal)
                && (t.When == TriggerKind.OnTileLand || t.When == TriggerKind.OnTilePass))
                when = (t.When == TriggerKind.OnTileLand ? "Landing on a " : "Passing a ") + Words(t.Param.Substring(5));
            var sb = new StringBuilder(when != null ? when + ": " + body : Cap(body));
            if (t.CooldownRounds > 0) sb.Append(" (every ").Append(t.CooldownRounds).Append(t.CooldownRounds == 1 ? " round)" : " rounds)");
            return sb.ToString();
        }

        private static string Cap(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;

        private static string Effect(SkillTrigger t, Show show = null)
        {
            string target = Target(t.Target);
            int d = t.Duration > 0 ? t.Duration : show != null && show.Duration ? show.DefaultDuration : 0;
            string dur = d > 0 ? " for " + d + (d == 1 ? " round" : " rounds") : "";
            switch (t.Effect)
            {
                case EffectKind.Damage:
                    if (t.Target == TargetKind.OtherEnemies)
                        return "jump to " + Math.Max(1, t.Stacks) + " other " + (t.Stacks > 1 ? "enemies" : "enemy") + " for " + Num(t.Value) + "% ATK damage";
                    return "deal " + Num(t.Value) + "% ATK damage to " + target;
                case EffectKind.TrueDamage: return "deal " + Num(t.Value) + "% ATK true damage (ignores defense) to " + target;
                case EffectKind.ApplyStatus:
                    return "apply " + StatusName(t.Status) + (t.Stacks > 1 || (show != null && show.Stacks) ? " ×" + t.Stacks : "") + dur + " to " + target;
                case EffectKind.Heal:
                    return "heal " + (t.Target == TargetKind.Self ? "" : target + " for ") + Num(t.Value) + "% of max HP"
                        + (t.Param == "overheal_shield" ? " (overheal becomes a shield)" : "");
                case EffectKind.Shield: return "gain a shield of " + Num(t.Value) + "% of max HP" + (t.Target == TargetKind.Self ? "" : " for " + target);
                case EffectKind.GainRage: return (t.Target == TargetKind.Self ? "gain " : target + " gain ") + Num(t.Value) + " rage";
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
                case EffectKind.SlowAwakening:
                    return t.When == TriggerKind.OnRunStart ? "the boss awakens " + Num(t.Value) + "% slower" : "drain " + Num(t.Value) + " from the boss's awakening";
                case EffectKind.Summon: return "summon " + Words(t.Param ?? "an ally");
                case EffectKind.RemoveStatus:
                    return "remove " + (t.Stacks > 0 ? Num(t.Stacks) + " " : "all ") + StatusName(t.Status) + " from " + target;
                case EffectKind.ConsumeStatus:
                {
                    double heal = BattleSim.ConsumeHealPct(t);
                    return "consume " + (t.Target == TargetKind.AllEnemies ? "all enemies'" : "the enemy's") + " " + StatusName(t.Status) + ": "
                        + Num(t.Value) + "% ATK damage per stack" + (heal > 0 ? ", heal " + Num(heal) + "% of it" : "");
                }
                case EffectKind.Strike: return t.Param == "counter" ? "counterattack" : "strike again";
                case EffectKind.AllyAction:
                    return t.Stacks > 1 ? Num(t.Stacks) + " of your aliens attack again" : "one of your aliens attacks again";
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
            TriggerKind.OnSupernova => "On Supernova",
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
            _ when c.StartsWith("vs_status:", StringComparison.Ordinal) => "against enemies with " + StatusLabel(c.Substring(10)),
            _ => "when " + Words(c),
        };

        private static string StatusLabel(string id) => Enum.TryParse(id, out StatusId st) ? StatusName(st) : id;

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
