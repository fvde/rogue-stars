using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>The worm's current in-run numbers for the HUD (<see cref="RunSystem.WormStats"/>).</summary>
    [Serializable]
    public sealed class RunWormStats
    {
        public double Hp, MaxHp, Atk, Def, Speed, CritChance;
        /// <summary>The full final stat block (loadout + every run skill at its level + perks, buffs, curses, level modifiers).</summary>
        public StatBlock Stats = new StatBlock();
    }

    /// <summary>Run level and XP for the HUD (<see cref="RunSystem.LevelInfo"/>).</summary>
    [Serializable]
    public sealed class RunLevelInfo
    {
        public int Level, Xp, XpNeeded, PendingDrafts;
        /// <summary>Xp / XpNeeded (0..1).</summary>
        public double Progress;
    }

    // Read-only helpers for the client HUD (no state changes).
    public static partial class RunSystem
    {
        /// <summary>
        /// The worm's current in-run stats with every run modification applied: the loadout snapshot, every run skill at its
        /// level (passive packages), shop perks, stat tiles and event buffs / curses (RunStats), the level's modifiers. HP is the
        /// current run HP; MaxHp the current max (what the next battle starts with).
        /// </summary>
        public static RunWormStats WormStats(ContentDb content, ActiveRun run)
        {
            var r = new RunWormStats();
            if (run == null) return r;
            var block = BattleSimulator.FinalStats(WormSpec(content, run), content);
            r.Stats = block;
            r.MaxHp = Math.Max(1, block.Value(StatId.Hp));
            r.Hp = run.WormHp < 0 ? r.MaxHp : Math.Min(run.WormHp, r.MaxHp);
            r.Atk = block.Value(StatId.Atk);
            r.Def = block.Value(StatId.Def);
            r.Speed = block.Value(StatId.Speed);
            r.CritChance = block.Value(StatId.CritChance);
            return r;
        }

        /// <summary>Current run level, XP into it and XP needed for the next one (HUD "Lv n · XP x/y").</summary>
        public static RunLevelInfo LevelInfo(ContentDb content, ActiveRun run)
        {
            var info = new RunLevelInfo();
            if (run == null) return info;
            info.Level = run.Level;
            info.Xp = run.Xp;
            info.XpNeeded = XpNeeded(content, run.Level);
            info.PendingDrafts = run.PendingDrafts;
            info.Progress = info.XpNeeded > 0 ? Math.Min(1, (double)run.Xp / info.XpNeeded) : 0;
            return info;
        }

        /// <summary>
        /// Run coins at run start: the level's cheapest shop skill price + RunTuning.StartCoinsMargin (playtest: the first shop
        /// came with 0 coins). The first shop of a run always offers a skill at that cheapest price (FillShop).
        /// </summary>
        public static long StartCoins(ContentDb content, LevelDef level)
        {
            var table = level != null && level.ShopTableId != null && content.RunShopTables.TryGetValue(level.ShopTableId, out var t) ? t : content.RunShopTables["shop_standard"];
            return CheapestSkillPrice(table) + content.RunTuning.StartCoinsMargin;
        }

        private static long CheapestSkillPrice(ShopTableDef table)
        {
            long min = long.MaxValue;
            foreach (var kv in table.SkillPrices) if (kv.Value > 0 && kv.Value < min) min = kv.Value;
            return min == long.MaxValue ? 0 : min;
        }

        /// <summary>Level of a run skill (0 not owned, 1 owned, 2+ leveled): pass it to SkillText.Effects(content, def, level) for its numbers.</summary>
        public static int SkillLevel(ActiveRun run, string skillId) => run == null ? 0 : RunDraft.Level(run, skillId);
    }
}
