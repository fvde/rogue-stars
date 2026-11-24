using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    [Serializable]
    public sealed class HeroDef
    {
        public string Id;
        public string Name;
        public SkillFamily Family;
        public SkillFamily SecondaryFamily;
        /// <summary>Worm stats at 1★ level 1 (scaled by HeroConfig.StarStatPercent).</summary>
        public List<StatMod> BaseStats = new List<StatMod>();
        public List<StatMod> StatsPerLevel = new List<StatMod>();
        /// <summary>Trait skills by star (Star field); a step can replace an earlier trait.</summary>
        public List<SkillStep> Traits = new List<SkillStep>();
    }

    [Serializable]
    public sealed class HeroConfig
    {
        public Dictionary<string, HeroDef> Defs = new Dictionary<string, HeroDef>();
        public int MaxStars = 10;
        /// <summary>StarShardCosts[s] = shards to reach star s (index 1 = unlock; index 0 unused).</summary>
        public List<long> StarShardCosts = new List<long>();
        /// <summary>StarStatPercent[s] = % bonus to the hero's base stats at star s.</summary>
        public List<double> StarStatPercent = new List<double>();
        public int MaxLevelBase = 10;
        public int MaxLevelPerStar = 10;
        /// <summary>LevelXpCosts[L] = Hero XP from level L to L+1 (index 0 unused).</summary>
        public List<long> LevelXpCosts = new List<long>();
        /// <summary>Permanent worm stats per star of every unlocked hero (collection bonus).</summary>
        public List<StatMod> CollectionPerStar = new List<StatMod>();

        public int MaxLevel(int stars) => Math.Min(LevelXpCosts.Count, MaxLevelBase + MaxLevelPerStar * stars);
    }

    public sealed partial class ContentDb
    {
        /// <summary>Hero personas, star costs, trait skills (Progression).</summary>
        public readonly HeroConfig HeroConfig = new HeroConfig();

        partial void BuildHeroes()
        {
            var h = HeroConfig;
            h.MaxStars = 10;
            h.StarShardCosts.AddRange(new long[] { 0, 40, 30, 60, 100, 160, 250, 380, 550, 800, 1100 });
            h.StarStatPercent.AddRange(new double[] { 0, 0, 10, 22, 36, 52, 70, 92, 118, 148, 185 });
            h.MaxLevelBase = 10; h.MaxLevelPerStar = 10;
            h.LevelXpCosts.Add(0);
            for (int l = 1; l < h.MaxLevelBase + h.MaxLevelPerStar * h.MaxStars; l++) h.LevelXpCosts.Add((long)Math.Round(40 * Math.Pow(l, 1.6)));
            h.CollectionPerStar.Add(new StatMod(StatId.Hp, 0, 0.3));
            h.CollectionPerStar.Add(new StatMod(StatId.Atk, 0, 0.3));

            AddHeroDef("hero_star_knight", "Star Knight Worm", SkillFamily.Counter, SkillFamily.Crit, "Starlight Riposte", "Knight's Oath");
            AddHeroDef("hero_chef", "Chef Worm", SkillFamily.Fire, SkillFamily.Poison, "Spicy Ladle", "Five-Course Feast");
            AddHeroDef("hero_void_pirate", "Void Pirate Worm", SkillFamily.Crit, SkillFamily.Bleed, "Plunder Shot", "Black Flag");
            AddHeroDef("hero_glacier_monk", "Glacier Monk Worm", SkillFamily.Ice, SkillFamily.Counter, "Still Water", "Avalanche Mantra");
            AddHeroDef("hero_sparkplug", "Sparkplug Worm", SkillFamily.Lightning, SkillFamily.Combo, "Static Wiggle", "Overclock");
            AddHeroDef("hero_alchemist", "Toxic Alchemist Worm", SkillFamily.Poison, SkillFamily.Fire, "Bubbling Flask", "Grand Reaction");
            AddHeroDef("hero_beast_tamer", "Beast Tamer Worm", SkillFamily.AlienSynergy, SkillFamily.Combo, "Pack Call", "Alpha Bond");
            AddHeroDef("hero_noodle_ninja", "Shadow Noodle Worm", SkillFamily.Combo, SkillFamily.Bleed, "Noodle Flurry", "Thousand Cuts");
        }

        /// <summary>
        /// Trait plan for every hero: A I at 1★, A II at 3★, B I at 5★ (new trait), A III at 7★, B II at 10★
        /// (ultimate: much stronger plus an opening buff). Each step is a real power jump.
        /// </summary>
        private void AddHeroDef(string id, string name, SkillFamily main, SkillFamily second, string traitA, string traitB)
        {
            var d = new HeroDef { Id = id, Name = name, Family = main, SecondaryFamily = second };
            d.BaseStats.Add(new StatMod(StatId.Hp, 100, 5));
            d.BaseStats.Add(new StatMod(StatId.Atk, 10, 5));
            d.StatsPerLevel.Add(new StatMod(StatId.Hp, 10));
            d.StatsPerLevel.Add(new StatMod(StatId.Atk, 2));

            string a1 = id + "_a1", a2 = id + "_a2", a3 = id + "_a3", b1 = id + "_b1", b2 = id + "_b2";
            AddSkill(ProgFamilySkill(a1, traitA, main, Rarity.Rare, SkillSource.Hero, 1.0, null));
            AddSkill(ProgFamilySkill(a2, traitA + " II", main, Rarity.Epic, SkillSource.Hero, 1.6, a1));
            AddSkill(ProgFamilySkill(b1, traitB, second, Rarity.Epic, SkillSource.Hero, 1.2, null));
            AddSkill(ProgFamilySkill(a3, traitA + " III", main, Rarity.Legendary, SkillSource.Hero, 2.4, a2));
            var ult = ProgFamilySkill(b2, traitB + " (Awakened)", second, Rarity.Mythic, SkillSource.Hero, 2.8, b1);
            ult.Triggers.Add(new SkillTrigger { When = TriggerKind.OnBattleStart, Effect = EffectKind.BuffStat, Target = TargetKind.Self, Stat = new StatMod(StatId.Atk, 0, 25), Duration = 3 });
            ult.Description += " Opens every battle with +25% ATK for 3 rounds.";
            AddSkill(ult);

            d.Traits.Add(new SkillStep(1, 0, a1));
            d.Traits.Add(new SkillStep(3, 0, a2, a1));
            d.Traits.Add(new SkillStep(5, 0, b1));
            d.Traits.Add(new SkillStep(7, 0, a3, a2));
            d.Traits.Add(new SkillStep(10, 0, b2, b1));
            HeroConfig.Defs[id] = d;
        }
    }
}
