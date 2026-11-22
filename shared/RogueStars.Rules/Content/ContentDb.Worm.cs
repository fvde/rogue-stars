using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>Kinds of talent-road tier perks.</summary>
    public static class TalentPerkKinds
    {
        public const string StaminaCap = "stamina_cap";          // Value = extra max stamina
        public const string DraftRerolls = "draft_rerolls";      // Value = extra draft rerolls per run
        public const string RunCoinsPercent = "run_coins_percent";// Value = % more run coins
        public const string Stat = "stat";                       // Stat = permanent worm stat
    }

    [Serializable]
    public sealed class TalentPerk
    {
        public string Kind;
        public double Value;
        public StatMod Stat;
        public string Description;
    }

    /// <summary>One upgrade button inside a talent tier (HP, ATK or DEF).</summary>
    [Serializable]
    public sealed class TalentButton
    {
        /// <summary>"hp", "atk" or "def".</summary>
        public string Key;
        public StatMod PerRank;
        public int MaxRank;
        /// <summary>Biomass cost of rank r (0-based) = CostBase * (1 + r * CostGrowth).</summary>
        public long CostBase;
        public double CostGrowth = 0.15;

        public long RankCost(int rank) => (long)Math.Round(CostBase * (1 + rank * CostGrowth));
    }

    [Serializable]
    public sealed class TalentTier
    {
        public int Index;
        public int RequiredWormLevel;
        public List<TalentButton> Buttons = new List<TalentButton>();
        /// <summary>Biomass cost of completing this tier (rank-up) once every button is capped.</summary>
        public long RankUpCost;
        public TalentPerk Perk;
    }

    [Serializable]
    public sealed class WormConfig
    {
        public int MaxLevel;
        /// <summary>LevelCosts[L] = Biomass to go from level L to L+1 (index 0 unused).</summary>
        public List<long> LevelCosts = new List<long>();
        public List<StatMod> BaseStats = new List<StatMod>();
        public List<StatMod> StatsPerLevel = new List<StatMod>();
        /// <summary>Every MilestoneEvery levels the worm gains MilestoneStats (percent bonuses).</summary>
        public int MilestoneEvery = 25;
        public List<StatMod> MilestoneStats = new List<StatMod>();
        public List<TalentTier> TalentTiers = new List<TalentTier>();
        /// <summary>Power score weights per stat (value * weight).</summary>
        public Dictionary<StatId, double> PowerWeights = new Dictionary<StatId, double>();
        public double PowerPerSkill = 120;
        public double PowerPerMountStat = 0.5;
        public double PowerPerAlienStat = 0.5;
    }

    public sealed partial class ContentDb
    {
        /// <summary>Worm level curve, base stats, talent road and power weights (Progression).</summary>
        public readonly WormConfig WormConfig = new WormConfig();

        partial void BuildWorm()
        {
            var w = WormConfig;
            w.MaxLevel = 500;
            w.LevelCosts.Add(0);
            for (int l = 1; l < w.MaxLevel; l++)
                w.LevelCosts.Add((long)Math.Round(80 * Math.Pow(l, 1.5) * Math.Pow(1.01, l)));

            w.BaseStats.Add(new StatMod(StatId.Hp, 500));
            w.BaseStats.Add(new StatMod(StatId.Atk, 50));
            w.BaseStats.Add(new StatMod(StatId.Def, 20));
            w.BaseStats.Add(new StatMod(StatId.Speed, 100));
            w.BaseStats.Add(new StatMod(StatId.CritDamage, 50));
            w.StatsPerLevel.Add(new StatMod(StatId.Hp, 25));
            w.StatsPerLevel.Add(new StatMod(StatId.Atk, 5));
            w.StatsPerLevel.Add(new StatMod(StatId.Def, 2.5));
            w.MilestoneEvery = 25;
            w.MilestoneStats.Add(new StatMod(StatId.Hp, 0, 3));
            w.MilestoneStats.Add(new StatMod(StatId.Atk, 0, 3));
            w.MilestoneStats.Add(new StatMod(StatId.Def, 0, 3));

            // Talent road: 60 tiers, ~one tier per worm level band, perks cycle through the five perk types.
            for (int t = 0; t < 60; t++)
            {
                double scale = 1 + t * 0.35;
                int maxRank = Math.Min(15, 5 + t / 5);
                long cost = (long)Math.Round(150 * Math.Pow(1 + t, 1.7));
                var tier = new TalentTier { Index = t, RequiredWormLevel = 3 + t * 8, RankUpCost = cost * 5 };
                tier.Buttons.Add(new TalentButton { Key = "hp", PerRank = new StatMod(StatId.Hp, Math.Round(15 * scale)), MaxRank = maxRank, CostBase = cost });
                tier.Buttons.Add(new TalentButton { Key = "atk", PerRank = new StatMod(StatId.Atk, Math.Round(3 * scale, 1)), MaxRank = maxRank, CostBase = cost });
                tier.Buttons.Add(new TalentButton { Key = "def", PerRank = new StatMod(StatId.Def, Math.Round(1.5 * scale, 1)), MaxRank = maxRank, CostBase = cost });
                switch (t % 5)
                {
                    case 0: tier.Perk = new TalentPerk { Kind = TalentPerkKinds.StaminaCap, Value = 5, Description = "+5 max Stamina" }; break;
                    case 1: tier.Perk = new TalentPerk { Kind = TalentPerkKinds.RunCoinsPercent, Value = 4, Description = "+4% run coins" }; break;
                    case 2: tier.Perk = new TalentPerk { Kind = TalentPerkKinds.Stat, Stat = new StatMod(StatId.Speed, 3), Description = "+3 Speed" }; break;
                    case 3: tier.Perk = new TalentPerk { Kind = TalentPerkKinds.Stat, Stat = new StatMod(StatId.CritChance, 0.5), Description = "+0.5% crit chance" }; break;
                    default:
                        tier.Perk = t % 10 == 4
                            ? new TalentPerk { Kind = TalentPerkKinds.DraftRerolls, Value = 1, Description = "+1 draft reroll per run" }
                            : new TalentPerk { Kind = TalentPerkKinds.Stat, Stat = new StatMod(StatId.Atk, 0, 2), Description = "+2% ATK" };
                        break;
                }
                w.TalentTiers.Add(tier);
            }

            var pw = w.PowerWeights;
            pw[StatId.Hp] = 0.2; pw[StatId.Atk] = 2; pw[StatId.Def] = 1.5; pw[StatId.Speed] = 0.5;
            pw[StatId.CritChance] = 20; pw[StatId.CritDamage] = 3; pw[StatId.Dodge] = 20; pw[StatId.ComboChance] = 15;
            pw[StatId.CounterChance] = 15; pw[StatId.DamageReduction] = 25; pw[StatId.LifeSteal] = 15;
        }
    }
}
