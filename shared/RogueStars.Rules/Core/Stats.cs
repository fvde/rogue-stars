using System;
using System.Collections.Generic;

namespace RogueStars.Rules.Core
{
    public enum StatId
    {
        Hp, Atk, Def, Speed,
        CritChance, CritDamage, CritResist,
        Dodge, DodgeIgnore,
        ComboChance, CounterChance, ParryChance, ParryReduction,
        DamageReduction, LifeSteal, Regen, RageGain,
        ShieldPower, HealPower, StatusChance, StatusResist,
        BossDamage, FireDamage, IceDamage, LightningDamage, PoisonDamage, BleedDamage,
        AlienPower, MountPower, LootLuck, XpGain,
    }

    /// <summary>A flat + percent modification of one stat. Percent is additive within a block (10 = +10%).</summary>
    [Serializable]
    public sealed class StatMod
    {
        public StatId Stat;
        public double Flat;
        public double Percent;

        public StatMod() { }
        public StatMod(StatId stat, double flat, double percent = 0) { Stat = stat; Flat = flat; Percent = percent; }
    }

    /// <summary>
    /// Final stat values. Combine with: value = (sum of flats) * (1 + sum of percents / 100).
    /// Chance-type stats (crit, dodge, ...) are expressed in percent points (25 = 25%).
    /// </summary>
    [Serializable]
    public sealed class StatBlock
    {
        public Dictionary<StatId, double> Flat = new Dictionary<StatId, double>();
        public Dictionary<StatId, double> Percent = new Dictionary<StatId, double>();

        public StatBlock Add(StatMod mod)
        {
            if (mod == null) return this;
            if (mod.Flat != 0) Flat[mod.Stat] = Get(Flat, mod.Stat) + mod.Flat;
            if (mod.Percent != 0) Percent[mod.Stat] = Get(Percent, mod.Stat) + mod.Percent;
            return this;
        }

        public StatBlock AddAll(IEnumerable<StatMod> mods)
        {
            if (mods != null) foreach (var m in mods) Add(m);
            return this;
        }

        public StatBlock AddBlock(StatBlock other)
        {
            if (other == null) return this;
            foreach (var kv in other.Flat) Flat[kv.Key] = Get(Flat, kv.Key) + kv.Value;
            foreach (var kv in other.Percent) Percent[kv.Key] = Get(Percent, kv.Key) + kv.Value;
            return this;
        }

        public double Value(StatId stat) => Get(Flat, stat) * (1 + Get(Percent, stat) / 100.0);

        public StatBlock Clone() => new StatBlock().AddBlock(this);

        private static double Get(Dictionary<StatId, double> d, StatId s) => d.TryGetValue(s, out var v) ? v : 0;
    }
}
