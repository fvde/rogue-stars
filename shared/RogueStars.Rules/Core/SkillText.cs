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
        public static List<string> Effects(SkillDef s) => Texts(Lines(s, null));

        /// <summary>One line of a skill's text; <see cref="Cost"/> lines are its downsides (shown in the warning colour).</summary>
        public sealed class SkillLine
        {
            public string Text;
            public bool Cost;
            public SkillLine(string text, bool cost) { Text = text; Cost = cost; }
        }

        /// <summary>The skill's lines at a level with their cost flags (UI: costs in the warning colour).</summary>
        public static List<SkillLine> RichLines(ContentDb content, SkillDef s, int level) => Lines(SkillLeveling.At(content, s, level), null);

        /// <summary>True if this text line of the skill is one of its costs (costs never scale, so it reads the same at every level).</summary>
        public static bool IsCostLine(SkillDef s, string line)
        {
            if (s == null || line == null) return false;
            foreach (var l in Lines(s, null)) if (l.Cost && l.Text == line) return true;
            return false;
        }

        private static List<string> Texts(List<SkillLine> lines)
        {
            var res = new List<string>(lines.Count);
            foreach (var l in lines) res.Add(l.Text);
            return res;
        }

        /// <summary>Rendering options for an upgrade: per trigger, show numbers the other level needs (chance, ×stacks, duration).</summary>
        private sealed class Show
        {
            public bool Chance, Stacks, Duration;
            public int DefaultDuration;
        }

        private static List<SkillLine> Lines(SkillDef s, Show[] show)
        {
            var lines = new List<SkillLine>();
            if (s == null) return lines;
            if (!string.IsNullOrEmpty(s.Text))
            {
                foreach (var raw in s.Text.Split('|'))
                {
                    if (raw.Length == 0) continue;
                    bool cost = raw[0] == '!';
                    lines.Add(new SkillLine(Fill(s, cost ? raw.Substring(1) : raw).TrimEnd('.', ' '), cost));
                }
                return lines;
            }
            foreach (var m in s.Stats) { var t = Stat(m); if (t != null) lines.Add(new SkillLine(t, SkillLeveling.IsCost(m))); }
            for (int i = 0; i < s.Triggers.Count; i++)
            {
                var tr = s.Triggers[i];
                var line = Trigger(tr, show != null && i < show.Length ? show[i] : null);
                if (line != null) lines.Add(new SkillLine(line, IsCostTrigger(tr)));
            }
            return lines;
        }

        /// <summary>Downsides among triggers: self damage, rules that take something away.</summary>
        private static bool IsCostTrigger(SkillTrigger t)
        {
            if (t.Effect == EffectKind.LoseHp) return true;
            if (t.Effect != EffectKind.Rule || t.Param == null) return false;
            switch (RuleName(t.Param))
            {
                case "keeps_shields": case "combo_falloff": case "combo_cap": case "proc_cap": case "no_turn_attack": case "symbiote":
                case "lose_turn_every": case "decay":
                    return true;
                default: return false;
            }
        }

        private static readonly Regex Placeholder = new Regex(@"\{([vcsdbp])(\d+)\}", RegexOptions.CultureInvariant);

        /// <summary>Fills a card template's placeholders with the (leveled) skill's numbers.</summary>
        private static string Fill(SkillDef s, string template) => Placeholder.Replace(template, m =>
        {
            int i = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            char k = m.Groups[1].Value[0];
            if (k == 'p')
            {
                if (i >= s.Stats.Count) return m.Value;
                var st = s.Stats[i];
                return Num(st.Flat != 0 ? st.Flat : st.Percent);
            }
            if (i >= s.Triggers.Count) return m.Value;
            var t = s.Triggers[i];
            switch (k)
            {
                case 'v': return Num(t.Value);
                case 'c': return Num(Math.Min(100, t.Chance));
                case 's': return Num(Math.Max(1, t.Stacks));
                case 'd': return Num(t.Duration);
                case 'b': return t.Stat == null ? "0" : Num(t.Stat.Flat != 0 ? t.Stat.Flat : t.Stat.Percent);
                default: return m.Value;
            }
        });

        /// <summary>Words of a card text (owner: card texts short, about 20 words).</summary>
        public static int WordCount(string text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;

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
            var la = Texts(Lines(a, show));
            var lb = Texts(Lines(b, show));
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

        /// <summary>Marks around a cost in a marked text (the client shows it in the warning colour; <see cref="StripMarks"/> for plain).</summary>
        public const char CostStart = '\u0002', CostEnd = '\u0003';

        /// <summary><see cref="Full(ContentDb, SkillDef, int)"/> with its cost lines between <see cref="CostStart"/> / <see cref="CostEnd"/>.</summary>
        public static string FullMarked(ContentDb content, SkillDef s, int level)
        {
            var sb = new StringBuilder();
            foreach (var l in RichLines(content, s, level))
            {
                if (sb.Length > 0) sb.Append(' ');
                if (l.Cost) sb.Append(CostStart).Append(l.Text).Append('.').Append(CostEnd); else sb.Append(l.Text).Append('.');
            }
            return sb.ToString();
        }

        /// <summary><see cref="UpgradeFull"/> with its cost lines marked (costs never scale, so they read the same).</summary>
        public static string UpgradeFullMarked(ContentDb content, SkillDef s, int from, int to)
        {
            var sb = new StringBuilder();
            foreach (var l in Upgrade(content, s, from, to))
            {
                if (sb.Length > 0) sb.Append(' ');
                if (IsCostLine(s, l)) sb.Append(CostStart).Append(l).Append('.').Append(CostEnd); else sb.Append(l).Append('.');
            }
            return sb.ToString();
        }

        /// <summary>A marked text without its cost marks.</summary>
        public static string StripMarks(string text) => text == null ? null : text.Replace(CostStart.ToString(), "").Replace(CostEnd.ToString(), "");

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

        /// <summary>
        /// Synergy weather name of a synergy (biome chips "Heat +25%", "Chill −50%"): the damage it scales. None = General (skills
        /// without a synergy tag).
        /// </summary>
        public static string WeatherName(SkillFamily f) => f switch
        {
            SkillFamily.Fire => "Heat",
            SkillFamily.Ice => "Chill",
            SkillFamily.Lightning => "Shock",
            SkillFamily.Poison => "Radiation",
            SkillFamily.AlienSynergy => "Alien",
            SkillFamily.None => "General",
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
            StatId.ParryChance => "counter chance", // parry was removed: leftover parry counts as counter chance
            StatId.ParryReduction => "damage reduction",
            StatId.ArcDamage => "Arc damage",
            StatId.EchoDamage => "Echo damage",
            StatId.DamageTaken => "damage taken",
            StatId.HealReceived => "healing received",
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
            StatId.SidekickHaste => "sidekick speed",
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
            {
                string c = t.Param.Substring(5);
                string line = c.StartsWith("per_stack:", StringComparison.Ordinal)
                    ? Signed(t.Value) + "% damage per " + StatusLabel(c.Substring(10)) + " stack on the target"
                    : c == "status_count" ? Signed(t.Value) + "% damage per different status on the target"
                    : c == "if" ? Signed(t.Value) + "% damage"
                    : Signed(t.Value) + "% damage " + Condition(c);
                if (!string.IsNullOrWhiteSpace(t.If)) line += " when " + BattleConditions.Describe(t.If);
                return line;
            }
            if (t.Effect == EffectKind.Rule) return Rule(t);
            string effect = Effect(t, show);
            if (effect == null) return null;
            if (t.When == TriggerKind.OnAct) effect = effect.Replace("% ATK", "% power"); // a sidekick's ATK is its ability power
            bool chance = t.Chance > 0 && (t.Chance < 100 || (show != null && show.Chance));
            string body = (chance ? Num(Math.Min(100, t.Chance)) + "% chance to " : "") + effect;
            string when = t.EveryN > 0 ? EveryText(t) : When(t);
            if (t.Param != null && t.Param.StartsWith("tile:", StringComparison.Ordinal)
                && (t.When == TriggerKind.OnTileLand || t.When == TriggerKind.OnTilePass))
                when = (t.When == TriggerKind.OnTileLand ? "Landing on a " : "Passing a ") + Words(t.Param.Substring(5));
            if (!string.IsNullOrWhiteSpace(t.If)) when = (when ?? "While") + " (" + BattleConditions.Describe(t.If) + ")";
            var sb = new StringBuilder(when != null ? when + ": " + body : Cap(body));
            if (t.CooldownRounds > 0) sb.Append(" (every ").Append(t.CooldownRounds).Append(t.CooldownRounds == 1 ? " round)" : " rounds)");
            if (t.MaxPerBattle == 1) sb.Append(" (once per battle)");
            else if (t.MaxPerBattle > 1) sb.Append(" (").Append(t.MaxPerBattle).Append(" times per battle)");
            return sb.ToString();
        }

        private static string Ordinal(int n)
        {
            int h = n % 100;
            string suf = h == 11 || h == 12 || h == 13 ? "th" : (n % 10) == 1 ? "st" : (n % 10) == 2 ? "nd" : (n % 10) == 3 ? "rd" : "th";
            return n + suf;
        }

        /// <summary>"Every 10th Radiation stack you apply", "Every 4th combo strike" (EveryN triggers).</summary>
        private static string EveryText(SkillTrigger t)
        {
            string n = Ordinal(t.EveryN);
            switch (t.When)
            {
                case TriggerKind.OnStatusApplied:
                    return "Every " + n + " " + (string.IsNullOrEmpty(t.Param) ? "status" : StatusLabel(BattleSim.ParamHead(t.Param))) + " stack you apply";
                case TriggerKind.OnArcJump: return "Every " + n + " Arc jump";
                case TriggerKind.OnCombo: return "Every " + n + " combo strike";
                case TriggerKind.OnHit: return "Every " + n + " hit";
                case TriggerKind.OnCrit: return "Every " + n + " crit";
                case TriggerKind.OnRoundStart: case TriggerKind.OnRoundEnd: return "Every " + n + " round";
                case TriggerKind.OnTurnStart: case TriggerKind.OnTurnEnd: return "Every " + n + " turn";
                case TriggerKind.OnDamaged: return "Every " + n + " hit you take";
                case TriggerKind.OnCounter: return "Every " + n + " counter";
                default: return (When(t) ?? "Every time") + " (every " + n + " time)";
            }
        }

        private static string RuleName(string p)
        {
            int i = p.IndexOf(':');
            return i < 0 ? p : p.Substring(0, i);
        }

        private static string RuleArg(string p)
        {
            int i = p.IndexOf(':');
            return i < 0 ? "" : p.Substring(i + 1);
        }

        /// <summary>A passive rule (R6) in words: "Supernova needs 12 Heat", "Chill no longer cools Heat" …</summary>
        private static string Rule(SkillTrigger t)
        {
            if (string.IsNullOrEmpty(t.Param)) return null;
            string name = RuleName(t.Param), arg = RuleArg(t.Param), st = StatusName(t.Status), v = Num(t.Value);
            switch (name)
            {
                case "max": return st + " can stack " + v + " higher";
                case "burst_at": return "Supernova needs " + arg + " Heat";
                case "no_recoil": return "Supernovas never hurt you";
                case "keeps_shields": return st + " no longer ignores shields";
                case "dot_crit": return st + " ticks can crit";
                case "dot": return t.Value == 100 ? st + " ticks deal double" : st + " ticks deal " + Signed(t.Value) + "%";
                case "consume_heal": return t.Value == 100 ? "consuming " + st + " heals twice as much" : "consuming " + st + " heals " + Signed(t.Value) + "%";
                case "no_expire": return arg.Length > 0 && arg != "1" ? st + " no longer wears off at " + arg + "+ stacks" : st + " no longer expires";
                case "no_cool": return "Chill no longer cools Heat";
                case "shutdown": return "Chilled enemies can't crit, combo or counter";
                case "double_vs_chill": return "Shock on chilled or frozen enemies counts double";
                case "arc_seek_chill": return "Arcs jump to chilled or frozen enemies first";
                case "arc_loop": return "Last enemy standing: your Arc jumps strike it, " + Signed(t.Value) + "% damage";
                case "basic_aoe": return "Your basic attack hits every enemy at " + v + "% damage";
                case "combo_no_decay": return "Combo chance no longer drops within a turn";
                case "combo_falloff": return "each combo strike deals " + v + "% less than the last";
                case "combo_cap": return "combo chance at most " + arg + "%";
                case "proc_cap": return "no on-hit effects after the " + Ordinal(int.TryParse(arg, out var pc) ? pc : 6) + " strike of a turn";
                case "combo_as_counter": return "Your combo chance also counts as counter chance";
                case "counter_combo": return "Counterattacks can combo";
                case "no_turn_attack": return "you no longer attack on your turn";
                case "counter_all": return "Counter every hit";
                case "hivemind": return "Each alien is tuned to one of your signatures: its ability also triggers it once";
                case "symbiote": return "your aliens stop acting alone";
                case "overkill": return "Damage beyond an enemy's HP carries over to the next enemy";
                case "last_light": return "Below " + arg + "% HP: every hit crits, but you can't heal";
                case "echo_strongest": return "Echo repeats your strongest attack this battle";
                case "extra_turn": return "Take an extra turn each round";
                case "lose_turn_every": return "every " + Ordinal(int.TryParse(arg, out var lt) ? lt : 4) + " round you lose your turn";
                case "dot_ticks": return "Status damage ticks " + (arg == "2" ? "twice" : arg + " times") + " per round";
                case "decay": return "permanent stacks decay by " + arg + " each round";
                case "plague": return "Every " + v + " stacks of a status you inflict: later enemies of this run begin with 1 more stack of it";
                default: return Words(name);
            }
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
                        return "Arc to " + Math.Max(1, t.Stacks) + " other " + (t.Stacks > 1 ? "enemies" : "enemy") + " for " + Num(t.Value) + "% ATK damage";
                    return "deal " + Num(t.Value) + "% ATK damage to " + target;
                case EffectKind.TrueDamage: return "deal " + Num(t.Value) + "% ATK true damage (ignores defense) to " + target;
                case EffectKind.ApplyStatus:
                {
                    string per = BattleSim.HasToken(t.Param, "per_status") ? " per different status it has"
                        : BattleSim.HasToken(t.Param, "per_unit") ? " per stack" : "";
                    if (t.Status == StatusId.Exposed) return "Expose " + target + " (+" + Num(Math.Max(1, t.Stacks)) + "% damage taken)";
                    return "apply " + StatusName(t.Status) + (t.Stacks > 1 || per.Length > 0 || (show != null && show.Stacks) ? " ×" + t.Stacks : "") + per + dur + " to " + target;
                }
                case EffectKind.Heal:
                    return "heal " + (t.Target == TargetKind.Self ? "" : target + " for ") + Num(t.Value) + "% of max HP"
                        + (t.Param == "overheal_shield" ? " (overheal becomes a shield)" : "");
                case EffectKind.Shield:
                    if (BattleSim.HasToken(t.Param, "of_amount")) return "gain a shield of " + Num(t.Value) + "% of the damage";
                    return "gain a shield of " + Num(t.Value) + "% of max HP" + (t.Target == TargetKind.Self ? "" : " for " + target);
                case EffectKind.GainRage: return (t.Target == TargetKind.Self ? "gain " : target + " gain ") + Num(t.Value) + " rage";
                case EffectKind.BuffStat:
                    {
                        var stat = Stat(t.Stat);
                        if (stat == null) return null;
                        string get = t.Target == TargetKind.Worm || t.Target == TargetKind.Enemy || t.Target == TargetKind.RandomEnemy || t.Target == TargetKind.LowestHpEnemy ? " gets " : " get ";
                        string how = BattleSim.HasToken(t.Param, "until_turn") ? " until your next turn"
                            : BattleSim.HasToken(t.Param, "next_turn") ? " for your next turn"
                            : BattleSim.HasToken(t.Param, "reset_no_combo") ? " this battle (a turn without a combo resets it)"
                            : t.Duration > 0 ? dur : " for the rest of the battle";
                        return (t.Target == TargetKind.Self ? "" : target + get) + stat + how;
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
                case EffectKind.Summon:
                {
                    var sk = t.Param != null ? ContentDb.Default.Sidekick(t.Param) : null;
                    if (sk == null) return "summon " + Words(t.Param ?? "an ally");
                    int n = Math.Max(1, t.Stacks);
                    return "launch " + (n > 1 ? Num(n) + " " + sk.Name + " sidekicks" : "a " + sk.Name + " sidekick") + " (it zaps for " + Num(t.Value) + "% of the worm's ATK)";
                }
                case EffectKind.Guard: return "guard " + target + ": the next hit it takes deals " + Num(t.Value) + "% less";
                case EffectKind.Block:
                {
                    int n = Math.Max(1, t.Stacks);
                    return "intercept the next " + (n > 1 ? Num(n) + " hits" : "hit") + " on " + target;
                }
                case EffectKind.RemoveStatus:
                    return "remove " + (t.Stacks > 0 ? Num(t.Stacks) + " " : "all ") + StatusName(t.Status) + " from " + target;
                case EffectKind.ConsumeStatus:
                {
                    var c = ConsumeSpec.Of(t);
                    var parts = new List<string>();
                    if (c.Dmg > 0) parts.Add(Num(c.Dmg) + "% ATK damage per stack" + (c.Heal > 0 ? ", heal " + Num(c.Heal) + "% of it" : ""));
                    if (c.Buff) parts.Add(Signed(c.BuffValue) + "% " + StatName(c.BuffStat) + " per stack for the battle" + (c.Cap > 0 ? " (max +" + Num(c.Cap) + "%)" : ""));
                    if (c.RefundMax > 0) parts.Add("at max stacks, refund " + Num(c.RefundMax) + " rage");
                    return "consume " + (c.Take > 0 ? Num(c.Take) + " of " : "") + (t.Target == TargetKind.AllEnemies ? "all enemies'" : "the enemy's") + " "
                        + StatusName(t.Status) + ": " + string.Join("; ", parts);
                }
                case EffectKind.BlockStatus:
                    return (t.Target == TargetKind.Self ? "you can't" : "it can't") + " gain " + StatusName(t.Status) + " for " + Math.Max(1, t.Duration) + (t.Duration == 1 ? " round" : " rounds");
                case EffectKind.ForceSupernova: return "its Heat goes Supernova now" + (t.Value != 0 ? ", " + Signed(t.Value) + "% damage" : "");
                case EffectKind.MultiplyStatus:
                {
                    string cap = t.Param != null && t.Param.StartsWith("cap:", StringComparison.Ordinal) ? " (at most +" + t.Param.Substring(4) + ")" : "";
                    return (t.Value == 200 ? "double " : "multiply ") + (t.Target == TargetKind.AllEnemies ? "every enemy's " : "the enemy's ") + StatusName(t.Status) + (t.Value == 200 ? "" : " ×" + Num(t.Value / 100.0)) + cap;
                }
                case EffectKind.TransferStatus: return Num(t.Value) + "% of its " + StatusName(t.Status) + " spreads to each other enemy";
                case EffectKind.TickStatus: return "its " + StatusName(t.Status) + " ticks now" + (t.Value != 100 ? " at " + Num(t.Value) + "%" : "");
                case EffectKind.Execute: return t.Value > 0 ? "kill a non-boss below " + Num(t.Value) + "% HP outright" : "kill it outright (not bosses)";
                case EffectKind.Reflect: return "reflect " + Num(t.Value) + "% of it to " + target;
                case EffectKind.CheatDeath: return "survive at 1 HP" + (BattleSim.HasToken(t.Param, "counter_window") ? "; counter every hit until your next turn" : "");
                case EffectKind.FreeRageAttack: return "unleash a free rage attack";
                case EffectKind.Charge: return "your next counter deals " + Signed(t.Value) + "% (stacks)";
                case EffectKind.LoseHp: return "lose " + Num(t.Value) + "% HP";
                case EffectKind.StackAll: return "it gains " + Num(Math.Max(1, t.Stacks)) + " stack of each of its statuses";
                case EffectKind.Sacrifice: return "an alien takes the hit and leaves the battle; heal " + Num(t.Value) + "%";
                case EffectKind.Echo: return "repeat your last attack" + (t.Stacks > 1 ? " ×" + t.Stacks : "");
                case EffectKind.Rewind: return "undo the damage of your last " + (t.Stacks > 1 ? Num(t.Stacks) + " hits" : "hit") + " taken";
                case EffectKind.Loop: return "rewind yourself and all enemies 2 rounds";
                case EffectKind.StartRage: return "start that battle with " + Num(t.Value) + " rage";
                case EffectKind.Strike: return t.Param == "counter" ? "counterattack" : "strike again";
                case EffectKind.AllyAction:
                    if (t.Param == "all") return "every alien acts";
                    return t.Stacks > 1 ? Num(t.Stacks) + " extra sidekick actions" : "one of your sidekicks takes an extra action";
                case EffectKind.SkipNegativeEvent: return "skip " + Num(t.Value) + " bad event " + (t.Value == 1 ? "outcome" : "outcomes") + " per run";
                default: return Words(t.Effect.ToString());
            }
        }

        private static string When(SkillTrigger t)
        {
            switch (t.When)
            {
                case TriggerKind.OnStackThreshold:
                    return BattleSim.ParseThreshold(t.Param, out var st, out var n) ? "When an enemy reaches " + n + " " + StatusName(st) : "At a stack threshold";
                case TriggerKind.OnStatusConsumed:
                    return string.IsNullOrEmpty(t.Param) ? "When you consume a status" : "Whenever " + StatusLabel(BattleSim.ParamHead(t.Param)) + " is consumed";
                case TriggerKind.OnStatusApplied:
                    return string.IsNullOrEmpty(t.Param) ? When(t.When) : "When you apply " + StatusLabel(BattleSim.ParamHead(t.Param));
                default: return When(t.When);
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
            TriggerKind.OnAct => null,
            TriggerKind.OnSidekickAct => "When an alien acts",
            TriggerKind.OnEnemyDeath => "When an enemy dies",
            TriggerKind.OnArcJump => "Each Arc jump",
            TriggerKind.OnShieldHit => "When your shield absorbs a hit",
            TriggerKind.OnFatal => "When a hit would kill you",
            TriggerKind.OnBlock => "When an alien blocks or guards a hit",
            TriggerKind.OnTurnEnd => "End of your turn",
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
            TargetKind.AllOtherEnemies => "every other enemy",
            TargetKind.OtherEnemies => "another enemy",
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
            "arc" => "with Arc jumps",
            _ when c.StartsWith("vs_status:", StringComparison.Ordinal) => "against enemies with " + StatusLabel(c.Substring(10)),
            _ => "when " + Words(c),
        };

        private static string StatusLabel(string id) => Enum.TryParse(id, out StatusId st) ? StatusName(st) : id;

        private static string Signed(double v) => (v > 0 ? "+" : "") + Num(v);
        private static string Num(double v) => NumFmt.OneDecimal(v);

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
