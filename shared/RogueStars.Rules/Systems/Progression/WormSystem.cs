using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Worm level (Biomass), base stat growth, talent road with tier perks, and the worm-stat loadout
    /// contribution. Also owns the Power score formula (<see cref="ComputePower"/>).
    /// </summary>
    public static class WormSystem
    {
        public static void Register(GameEngine e)
        {
            e.Register<WormLevelUp>(HandleLevelUp);
            e.Register<WormTalentUpgrade>(HandleTalentUpgrade);
            e.Register<WormTalentTierUp>(HandleTierUp);
            e.RegisterLoadoutContributor(Contribute);
            EconomySystem.RegisterRegenBonusProvider(e, (s, c, now, currency) =>
                currency == Currencies.Stamina ? ((long)PerkTotal(s, c, TalentPerkKinds.StaminaCap), 0.0) : (0L, 0.0));
        }

        // ---- queries ---------------------------------------------------------------------------

        /// <summary>Talent tier the player is currently working on, or null when the road is complete.</summary>
        public static TalentTier CurrentTier(GameState s, ContentDb c) =>
            s.Worm.TalentTiersDone < c.WormConfig.TalentTiers.Count ? c.WormConfig.TalentTiers[s.Worm.TalentTiersDone] : null;

        public static int Rank(GameState s, string key) => s.Worm.TalentRanks.TryGetValue(key, out var r) ? r : 0;

        /// <summary>Biomass needed for the next worm level, or -1 at max level.</summary>
        public static long NextLevelCost(GameState s, ContentDb c) =>
            s.Worm.Level < c.WormConfig.MaxLevel ? c.WormConfig.LevelCosts[s.Worm.Level] : -1;

        /// <summary>Sum of a talent perk kind over completed tiers (stamina cap, draft rerolls, run coin %).</summary>
        public static double PerkTotal(GameState s, ContentDb c, string kind)
        {
            double v = 0;
            var tiers = c.WormConfig.TalentTiers;
            for (int i = 0; i < s.Worm.TalentTiersDone && i < tiers.Count; i++)
                if (tiers[i].Perk != null && tiers[i].Perk.Kind == kind) v += tiers[i].Perk.Value;
            return v;
        }

        /// <summary>Worm stats from level + talents + talent stat perks (no parts/hero/mount).</summary>
        public static StatBlock WormBaseStats(GameState s, ContentDb c)
        {
            var w = c.WormConfig;
            var b = new StatBlock().AddAll(w.BaseStats);
            int lv = s.Worm.Level;
            b.AddBlock(ProgressionUtil.Scaled(w.StatsPerLevel, lv - 1));
            int milestones = w.MilestoneEvery > 0 ? lv / w.MilestoneEvery : 0;
            b.AddBlock(ProgressionUtil.Scaled(w.MilestoneStats, milestones));

            var tiers = w.TalentTiers;
            for (int i = 0; i < tiers.Count && i <= s.Worm.TalentTiersDone; i++)
            {
                var tier = tiers[i];
                bool done = i < s.Worm.TalentTiersDone;
                foreach (var btn in tier.Buttons)
                {
                    int ranks = done ? btn.MaxRank : Rank(s, btn.Key);
                    if (ranks > 0) b.Add(new StatMod(btn.PerRank.Stat, btn.PerRank.Flat * ranks, btn.PerRank.Percent * ranks));
                }
                if (done && tier.Perk != null && tier.Perk.Kind == TalentPerkKinds.Stat) b.Add(tier.Perk.Stat);
            }
            return b;
        }

        /// <summary>
        /// Power score of a loadout: weighted worm stats + skills + mount and alien stats. Applied as the final
        /// loadout step (registered last in SystemsRegistry), so every contributor (City aliens/boons too) counts.
        /// </summary>
        public static long ComputePower(CombatLoadout l, ContentDb c)
        {
            var w = c.WormConfig;
            double p = 0;
            foreach (var kv in w.PowerWeights) p += l.WormStats.Value(kv.Key) * kv.Value;
            p += l.MetaSkillIds.Count * w.PowerPerSkill;
            p += (l.MountStats.Value(StatId.Atk) * 2 + l.MountStats.Value(StatId.Hp) * 0.2) * w.PowerPerMountStat;
            foreach (var a in l.Aliens)
                if (a != null && a.Stats != null) p += (a.Stats.Value(StatId.Atk) * 2 + a.Stats.Value(StatId.Hp) * 0.2) * w.PowerPerAlienStat;
            return (long)Math.Round(p);
        }

        private static void Contribute(GameState s, ContentDb c, long now, CombatLoadout l)
        {
            l.WormStats.AddBlock(WormBaseStats(s, c));
            l.DraftRerolls += (int)PerkTotal(s, c, TalentPerkKinds.DraftRerolls);
        }

        // ---- handlers --------------------------------------------------------------------------

        private static CommandResult HandleLevelUp(WormLevelUp cmd, CommandContext ctx)
        {
            var s = ctx.State; var w = ctx.Content.WormConfig;
            GameEngine.Require(cmd.Levels >= 1, "levels must be >= 1");
            GameEngine.Require(s.Worm.Level + cmd.Levels <= w.MaxLevel, "max level reached");
            long cost = 0;
            for (int l = s.Worm.Level; l < s.Worm.Level + cmd.Levels; l++) cost += w.LevelCosts[l];
            ProgressionUtil.Spend(s, Currencies.Biomass, cost);
            s.Worm.Level += cmd.Levels;
            s.Count("worm_levels", cmd.Levels);
            ProgressionUtil.CountMax(s, "worm_level_max", s.Worm.Level);
            ctx.Emit("worm_level", s.Worm.Level.ToString());
            return CommandResult.Success(s.Worm.Level);
        }

        private static CommandResult HandleTalentUpgrade(WormTalentUpgrade cmd, CommandContext ctx)
        {
            var s = ctx.State; var c = ctx.Content;
            var tier = CurrentTier(s, c);
            GameEngine.Require(tier != null, "talent road complete");
            GameEngine.Require(s.Worm.Level >= tier.RequiredWormLevel, "worm level too low");
            GameEngine.Require(cmd.Ranks >= 1, "ranks must be >= 1");
            TalentButton btn = null;
            foreach (var b in tier.Buttons) if (b.Key == cmd.Button) btn = b;
            GameEngine.Require(btn != null, "unknown talent button");
            int rank = Rank(s, btn.Key);
            GameEngine.Require(rank + cmd.Ranks <= btn.MaxRank, "talent rank cap reached");
            long cost = 0;
            for (int r = rank; r < rank + cmd.Ranks; r++) cost += btn.RankCost(r);
            ProgressionUtil.Spend(s, Currencies.Biomass, cost);
            s.Worm.TalentRanks[btn.Key] = rank + cmd.Ranks;
            s.Count("worm_talent_ranks", cmd.Ranks);
            return CommandResult.Success();
        }

        private static CommandResult HandleTierUp(WormTalentTierUp cmd, CommandContext ctx)
        {
            var s = ctx.State; var c = ctx.Content;
            var tier = CurrentTier(s, c);
            GameEngine.Require(tier != null, "talent road complete");
            foreach (var b in tier.Buttons) GameEngine.Require(Rank(s, b.Key) >= b.MaxRank, "all talents of the tier must be maxed");
            ProgressionUtil.Spend(s, Currencies.Biomass, tier.RankUpCost);
            s.Worm.TalentTiersDone++;
            s.Worm.TalentRanks.Clear();
            s.Count("worm_talent_tiers");
            ctx.Emit("worm_talent_tier", s.Worm.TalentTiersDone.ToString(), tier.Perk != null ? tier.Perk.Kind : "");
            return CommandResult.Success(tier.Perk);
        }
    }
}
