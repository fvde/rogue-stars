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
    /// <c>first_hit_taken</c> (the first hit the owner takes this round), <c>combo_strikes:N</c> (exactly N combo strikes this turn),
    /// <c>rage_crit</c> (the owner's latest rage attack landed as a crit; for OnRageAttack triggers: lab Frost Nova).</item>
    /// <item>Hero &amp; mount kits: <c>worm_rage:N</c> (the owner's side's worm has N+ rage), <c>crit_kill</c> (the killing hit was a crit),
    /// <c>extra_hits:N</c> (N+ extra hits this turn), <c>siphon_full</c> (the worm's siphon shield is full).</item>
    /// </list>
    /// Parsed once per string and cached; unknown terms are false (content validation rejects them).
    /// </summary>
    public static class BattleConditions
    {
        public enum Kind
        {
            Has, Lacks, Max, Unblocked, HpBelow, Boss, NotBoss, Statuses, SelfHpBelow, SelfShielded, Sidekicks, OneEnemy,
            FirstTurn, FirstHit, FirstHitTaken, ComboStrikes, RageCrit, Unknown, WormRage, CritKill, ExtraHits, SiphonFull,
            // parts v2
            AttackerSlower, AmountPct, CritIn, RageIn, NovaKill, NovaRage, WithinHeat, RageWithin, TurnEvery, NoCritTurn, Forced,
            ComboMin, HpAbove, BasicHit, HitCrit, InExtraTurn,
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
                case "rage_crit": t.Kind = Kind.RageCrit; break;
                case "worm_rage": t.Kind = Kind.WormRage; t.N = Num(1, 100); break;
                case "crit_kill": t.Kind = Kind.CritKill; break;
                case "extra_hits": t.Kind = Kind.ExtraHits; t.N = Math.Max(1, Num(1, 1)); break;
                case "siphon_full": t.Kind = Kind.SiphonFull; break;
                // parts v2
                case "attacker_slower": t.Kind = Kind.AttackerSlower; break;
                case "amount_pct": t.Kind = Kind.AmountPct; t.N = Num(1, 0); break;
                case "crit_in": t.Kind = Kind.CritIn; break;
                case "rage_in": t.Kind = Kind.RageIn; break;
                case "nova_kill": t.Kind = Kind.NovaKill; break;
                case "nova_rage":
                case "in_rage": t.Kind = Kind.NovaRage; break;
                case "within_heat": t.Kind = Kind.WithinHeat; t.N = Num(1, 0); break;
                case "rage_within": t.Kind = Kind.RageWithin; t.N = Num(1, 0); break;
                case "turn_every": t.Kind = Kind.TurnEvery; t.N = Math.Max(1, Num(1, 1)); break;
                case "no_crit_turn": t.Kind = Kind.NoCritTurn; break;
                case "forced": t.Kind = Kind.Forced; break;
                case "combo_min": t.Kind = Kind.ComboMin; t.N = Math.Max(1, Num(1, 1)); break;
                case "hp_above": t.Kind = Kind.HpAbove; t.N = Num(1, 0); break;
                case "basic_hit": t.Kind = Kind.BasicHit; break;
                case "hit_crit": t.Kind = Kind.HitCrit; break;
                case "extra_turn": t.Kind = Kind.InExtraTurn; break;
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
                case Kind.RageCrit: return "your rage attack crits";
                case Kind.WormRage: return "the worm has " + Num(t.N) + "+ rage";
                case Kind.CritKill: return "a critical kill";
                case Kind.ExtraHits: return Num(t.N) + "+ extra hits this turn";
                case Kind.SiphonFull: return "the siphon shield is full";
                case Kind.AttackerSlower: return "the attacker is slower than you";
                case Kind.AmountPct: return "the hit takes over " + Num(t.N) + "% of your max HP";
                case Kind.CritIn: return "the hit was a crit";
                case Kind.RageIn: return "the hit was a rage attack";
                case Kind.NovaKill: return "a Supernova kill";
                case Kind.NovaRage: return "a Supernova your rage attack set off";
                case Kind.WithinHeat: return "it is within " + Num(t.N) + " Heat of Supernova";
                case Kind.RageWithin: return "you are within " + Num(t.N) + " rage of full";
                case Kind.TurnEvery: return "every " + Num(t.N) + "th turn";
                case Kind.NoCritTurn: return "a turn without a crit";
                case Kind.Forced: return "a forced miss";
                case Kind.ComboMin: return Num(t.N) + "+ combo strikes this turn";
                case Kind.HpAbove: return "it is above " + Num(t.N) + "% HP";
                case Kind.BasicHit: return "a basic attack";
                case Kind.HitCrit: return "the hit crit";
                case Kind.InExtraTurn: return "an extra turn";
                default: return t.Source;
            }
        }

        private static string Num(double v) => NumFmt.OneDecimal(v);
    }
}
