using System;
using System.Collections.Generic;
using System.Globalization;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Trigger gates (skill redesign R1): <see cref="SkillTrigger.If"/> and conditional damage bonuses with <c>cond:if</c>.
    /// A condition is one or more terms joined by '&amp;'; each term is <c>name[:arg[:arg]]</c>. "ctx" is the trigger's context
    /// fighter (the hit enemy for hits, the attacker for when-hit triggers, the dead enemy for OnEnemyDeath).
    /// <list type="bullet">
    /// <item><c>has:Status[:N]</c> ctx has at least N (1) stacks; <c>lacks:Status</c>; <c>max:Status</c> ctx's stacks are at the cap
    /// (the cap its applier's rules give); <c>unblocked:Status</c> ctx can gain the status now (no re-apply cooldown / block);</item>
    /// <item><c>hp_below:P</c> ctx below P % HP; <c>boss</c> / <c>not_boss</c>; <c>statuses:N</c> ctx has N+ different statuses;</item>
    /// <item><c>self_hp_below:P</c>, <c>self_shielded</c>, <c>sidekicks</c> (the owner's side has a living sidekick),
    /// <c>one_enemy</c> (one enemy left);</item>
    /// <item><c>first_turn</c> (the owner's first turn), <c>first_hit</c> (the owner's first landed hit of the battle),
    /// <c>first_hit_taken</c> (the first hit the owner takes this round), <c>combo_strikes:N</c> (exactly N combo strikes this turn).</item>
    /// </list>
    /// Parsed once per string and cached; unknown terms are false (content validation rejects them).
    /// </summary>
    public static class BattleConditions
    {
        public enum Kind
        {
            Has, Lacks, Max, Unblocked, HpBelow, Boss, NotBoss, Statuses, SelfHpBelow, SelfShielded, Sidekicks, OneEnemy,
            FirstTurn, FirstHit, FirstHitTaken, ComboStrikes, Unknown,
        }

        public sealed class Term
        {
            public Kind Kind;
            public StatusId Status;
            public double N;
            public string Source;
        }

        private static readonly Dictionary<string, Term[]> Cache = new Dictionary<string, Term[]>();

        /// <summary>The parsed terms of a condition (empty for null / blank).</summary>
        public static Term[] Parse(string cond)
        {
            if (string.IsNullOrWhiteSpace(cond)) return Array.Empty<Term>();
            lock (Cache)
            {
                if (Cache.TryGetValue(cond, out var hit)) return hit;
            }
            var parts = cond.Split('&');
            var list = new List<Term>(parts.Length);
            foreach (var raw in parts)
            {
                var p = raw.Trim();
                if (p.Length == 0) continue;
                list.Add(ParseTerm(p));
            }
            var arr = list.ToArray();
            lock (Cache) { Cache[cond] = arr; }
            return arr;
        }

        private static Term ParseTerm(string p)
        {
            var a = p.Split(':');
            var t = new Term { Source = p, Kind = Kind.Unknown };
            string name = a[0];
            double Num(int i, double def) =>
                a.Length > i && double.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
            bool St(int i) => a.Length > i && Enum.TryParse(a[i], out t.Status) && t.Status != StatusId.None;
            switch (name)
            {
                case "has": if (St(1)) { t.Kind = Kind.Has; t.N = Math.Max(1, Num(2, 1)); } break;
                case "lacks": if (St(1)) t.Kind = Kind.Lacks; break;
                case "max": if (St(1)) t.Kind = Kind.Max; break;
                case "unblocked": if (St(1)) t.Kind = Kind.Unblocked; break;
                case "hp_below": t.Kind = Kind.HpBelow; t.N = Num(1, 0); break;
                case "boss": t.Kind = Kind.Boss; break;
                case "not_boss": t.Kind = Kind.NotBoss; break;
                case "statuses": t.Kind = Kind.Statuses; t.N = Math.Max(1, Num(1, 1)); break;
                case "self_hp_below": t.Kind = Kind.SelfHpBelow; t.N = Num(1, 0); break;
                case "self_shielded": t.Kind = Kind.SelfShielded; break;
                case "sidekicks": t.Kind = Kind.Sidekicks; break;
                case "one_enemy": t.Kind = Kind.OneEnemy; break;
                case "first_turn": t.Kind = Kind.FirstTurn; break;
                case "first_hit": t.Kind = Kind.FirstHit; break;
                case "first_hit_taken": t.Kind = Kind.FirstHitTaken; break;
                case "combo_strikes": t.Kind = Kind.ComboStrikes; t.N = Math.Max(1, Num(1, 1)); break;
            }
            return t;
        }

        /// <summary>True if every term of the condition is known (content validation).</summary>
        public static bool IsValid(string cond)
        {
            foreach (var t in Parse(cond)) if (t.Kind == Kind.Unknown) return false;
            return true;
        }

        /// <summary>Player-facing wording of a condition ("heated enemy with 6+ Heat" …), used by SkillText.</summary>
        public static string Describe(string cond)
        {
            var terms = Parse(cond);
            var parts = new List<string>();
            foreach (var t in terms) parts.Add(DescribeTerm(t));
            return string.Join(", ", parts);
        }

        private static string DescribeTerm(Term t)
        {
            string st = SkillText.StatusName(t.Status);
            switch (t.Kind)
            {
                case Kind.Has: return t.N > 1 ? "it has " + Num(t.N) + "+ " + st : "it has " + st;
                case Kind.Lacks: return "it has no " + st;
                case Kind.Max: return "it is at max " + st;
                case Kind.Unblocked: return "it can gain " + st;
                case Kind.HpBelow: return "it is below " + Num(t.N) + "% HP";
                case Kind.Boss: return "a boss";
                case Kind.NotBoss: return "not a boss";
                case Kind.Statuses: return "it has " + Num(t.N) + "+ different statuses";
                case Kind.SelfHpBelow: return "you are below " + Num(t.N) + "% HP";
                case Kind.SelfShielded: return "you are shielded";
                case Kind.Sidekicks: return "you have a sidekick";
                case Kind.OneEnemy: return "one enemy left";
                case Kind.FirstTurn: return "your first turn";
                case Kind.FirstHit: return "your first hit";
                case Kind.FirstHitTaken: return "the first hit you take each round";
                case Kind.ComboStrikes: return Num(t.N) + " combo strikes this turn";
                default: return t.Source;
            }
        }

        private static string Num(double v) => v.ToString(Math.Abs(v - Math.Round(v)) < 1e-9 ? "0" : "0.#", CultureInfo.InvariantCulture);
    }
}
