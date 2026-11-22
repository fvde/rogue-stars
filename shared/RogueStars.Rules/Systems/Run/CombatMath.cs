using System;
using RogueStars.Rules.Content;

namespace RogueStars.Rules.Systems
{
    /// <summary>Pure combat formulas (GDD §4). All percentages are percent points (25 = 25%).</summary>
    public static class CombatMath
    {
        /// <summary>softcap(x) = min(x, 100x/(x+100) + knee). Knee 10 for DR/dodge, 15 for crit chance.</summary>
        public static double SoftCap(double x, double knee)
        {
            if (x <= 0) return x;
            return Math.Min(x, 100.0 * x / (x + 100.0) + knee);
        }

        /// <summary>raw = max(A − DEF, floor%·A) with A = ATK·(1 + bonus%).</summary>
        public static double RawDamage(double atk, double bonusPct, double def, double floorPct)
        {
            double a = atk * (1 + bonusPct / 100.0);
            return Math.Max(a - def, a * floorPct / 100.0);
        }

        /// <summary>Multiplier from damage reduction: 1 − softcap(DR)/100, capped at MaxDamageReduction. Negative DR amplifies.</summary>
        public static double DamageReductionFactor(double dr, BattleTuning t)
        {
            double capped = Clamp(SoftCap(dr, t.DrKnee), -100, t.MaxDamageReduction);
            return 1 - capped / 100.0;
        }

        /// <summary>Crit probability (0..1) from crit chance with the +15 knee soft cap.</summary>
        public static double CritProbability(double critChance, BattleTuning t) => Clamp(SoftCap(critChance, t.CritKnee), 0, t.MaxCrit) / 100.0;

        /// <summary>Crit multiplier: 1 + CritDmg·(1 − critRes); both in percent points.</summary>
        public static double CritMultiplier(double critDamagePct, double critResistPct)
            => 1 + Math.Max(0, critDamagePct) / 100.0 * (1 - Clamp(critResistPct / 100.0, 0, 1));

        /// <summary>Dodge probability (0..1): softcap(dodge − dodgeIgnore) × decay^consecutiveDodges.</summary>
        public static double DodgeProbability(double dodge, double dodgeIgnore, int consecutiveDodges, BattleTuning t)
        {
            double d = Clamp(SoftCap(dodge - dodgeIgnore, t.DodgeKnee), 0, t.MaxDodge) / 100.0;
            return d * Math.Pow(t.DodgeDecay, consecutiveDodges);
        }

        /// <summary>
        /// Full hit damage before parry: raw (or A for true damage) × DR factor (not for true damage)
        /// × crit multiplier (if crit) × conditional multiplier × damage-taken multiplier.
        /// </summary>
        public static double HitDamage(double atk, double atkPct, double bonusPct, double def, double dr, bool trueDamage,
            bool crit, double critMult, double condMult, double takenMult, BattleTuning t)
        {
            double a = atk * atkPct / 100.0;
            double dmg = trueDamage ? a * (1 + bonusPct / 100.0) : RawDamage(a, bonusPct, def, t.DefFloorPct) * DamageReductionFactor(dr, t);
            if (crit) dmg *= critMult;
            dmg *= condMult * takenMult;
            return Math.Max(0, dmg);
        }

        public static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
    }
}
