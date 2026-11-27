using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Screens
{
    /// <summary>One line of the skills book's Stats tab: "Crit chance · 42%" (+ an optional dim note such as the soft-capped value).</summary>
    public sealed class StatSheetRow
    {
        public StatId Stat;
        public string Label, Value, Note;
    }

    /// <summary>A titled block of the Stats tab (Core, Offense, ...).</summary>
    public sealed class StatSheetGroup
    {
        public string Title;
        public List<StatSheetRow> Rows = new List<StatSheetRow>();
    }

    /// <summary>
    /// The skills book's Stats tab (playtest): every battle stat of the worm as it stands right now in this run, from the same
    /// block the battle starts from — <see cref="RunSystem.WormStats"/> (BattleSimulator.FinalStats of RunSystem.WormSpec: the
    /// loadout of parts, hero, mount and city boons, every run skill at its level with its passive package, perks, stat tiles,
    /// event buffs / curses and level modifiers). Grouped; Core always shows, every other stat only when it is not 0. Pure.
    /// </summary>
    public static class RunStatSheet
    {
        /// <summary>The groups in display order and the stats each lists (every StatId but the removed parry pair, once).</summary>
        public static readonly (string Title, StatId[] Stats)[] Groups =
        {
            ("Core", new[] { StatId.Hp, StatId.Atk, StatId.Def, StatId.Speed }),
            ("Offense", new[]
            {
                StatId.CritChance, StatId.CritDamage, StatId.ComboChance, StatId.CounterChance, StatId.DodgeIgnore, StatId.BossDamage,
                StatId.RageGain, StatId.ArcDamage, StatId.EchoDamage, StatId.SupernovaDamage,
            }),
            ("Defense", new[]
            {
                StatId.Dodge, StatId.DamageReduction, StatId.ShieldPower, StatId.CritResist, StatId.StatusResist, StatId.SupernovaGuard,
                StatId.DamageTaken,
            }),
            ("Sustain", new[] { StatId.Regen, StatId.LifeSteal, StatId.HealPower, StatId.HealReceived }),
            ("Status & Synergy", new[]
            {
                StatId.StatusChance, StatId.FireDamage, StatId.IceDamage, StatId.LightningDamage, StatId.PoisonDamage, StatId.BleedDamage,
                StatId.AlienPower, StatId.SidekickHaste, StatId.MountPower,
            }),
            ("Run", new[] { StatId.XpGain, StatId.LootLuck }),
        };

        /// <summary>Stats the sheet never lists: parry was removed (FinalStats folds leftover parry chance into counter chance).</summary>
        public static readonly StatId[] Hidden = { StatId.ParryChance, StatId.ParryReduction };

        /// <summary>The Stats tab of this run (empty groups left out).</summary>
        public static List<StatSheetGroup> Build(ContentDb c, ActiveRun run)
        {
            var list = new List<StatSheetGroup>();
            if (run == null) return list;
            var w = RunSystem.WormStats(c, run);
            var t = c.Combat;
            foreach (var (title, stats) in Groups)
            {
                var g = new StatSheetGroup { Title = title };
                foreach (var id in stats)
                {
                    double v = id == StatId.Hp ? w.MaxHp : w.Stats.Value(id);
                    bool core = id == StatId.Hp || id == StatId.Atk || id == StatId.Def || id == StatId.Speed;
                    if (!core && Math.Abs(v) < 0.05) continue;
                    var row = new StatSheetRow { Stat = id, Label = Label(id), Value = Value(id, v) };
                    if (id == StatId.Hp) row.Value = Fmt.Num(Math.Max(0, Math.Round(w.Hp))) + " / " + Fmt.Num(w.MaxHp);
                    else if (id == StatId.CritChance) row.Note = Capped(v, CombatMath.CritProbability(v, t) * 100);
                    else if (id == StatId.Dodge) row.Note = Capped(v, CombatMath.DodgeProbability(v, 0, 0, t) * 100);
                    else if (id == StatId.DamageReduction) row.Note = Capped(v, (1 - CombatMath.DamageReductionFactor(v, t)) * 100);
                    else if (id == StatId.Regen) row.Note = "of max HP per turn";
                    else if (id == StatId.LifeSteal) row.Note = "of damage dealt";
                    g.Rows.Add(row);
                }
                if (g.Rows.Count > 0) list.Add(g);
            }
            return list;
        }

        /// <summary>"Crit chance", "Max HP", "Radiation damage": the rules' stat names, capitalised.</summary>
        public static string Label(StatId id)
        {
            if (id == StatId.Hp) return "HP";
            if (id == StatId.Speed) return "Speed";
            string n = SkillText.StatName(id);
            return string.IsNullOrEmpty(n) ? id.ToString() : char.ToUpperInvariant(n[0]) + n.Substring(1);
        }

        /// <summary>HP / ATK / DEF / Speed as whole numbers, every point stat in percent (costs signed: "+20%" damage taken).</summary>
        public static string Value(StatId id, double v)
        {
            if (!StatMod.IsPointStat(id)) return Fmt.Num(Math.Round(v));
            string s = Fmt.Pct(v);
            return (id == StatId.DamageTaken || id == StatId.HealReceived) && v > 0 ? "+" + s : s;
        }

        /// <summary>The note of a soft-capped stat: what the battle actually uses, when that differs from the stat.</summary>
        private static string Capped(double raw, double effective) =>
            Math.Abs(raw - effective) >= 0.5 ? Fmt.Pct(effective) + " after the cap" : null;
    }
}
