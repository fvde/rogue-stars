using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    [Serializable]
    public sealed class MountDef
    {
        public string Id;
        public string Name;
        public SkillFamily Family;
        public SkillFamily SupportFamily;
        /// <summary>Mount combat stats at 1★ level 1 (go to CombatLoadout.MountStats).</summary>
        public List<StatMod> BaseStats = new List<StatMod>();
        public List<StatMod> StatsPerLevel = new List<StatMod>();
        /// <summary>Weapon skills by star / awakening; later steps replace earlier ones.</summary>
        public List<SkillStep> Weapons = new List<SkillStep>();
    }

    [Serializable]
    public sealed class MountConfig
    {
        public Dictionary<string, MountDef> Defs = new Dictionary<string, MountDef>();
        public int MaxStars = 5;
        public int MaxAwaken = 5;
        /// <summary>Mount shards per duplicate copy.</summary>
        public long DupeShards = 10;
        /// <summary>StarShardCosts[s] = mount shards to reach star s (2..5).</summary>
        public List<long> StarShardCosts = new List<long>();
        /// <summary>AwakenShardCosts[a] / AwakenCoreCosts[a] = cost to reach awakening a (1..5).</summary>
        public List<long> AwakenShardCosts = new List<long>();
        public List<long> AwakenCoreCosts = new List<long>();
        public List<double> StarStatPercent = new List<double>();
        public List<double> AwakenStatPercent = new List<double>();
        public int MaxLevelBase = 20;
        public int MaxLevelPerStep = 10;
        /// <summary>LevelFuelCosts[L] = Fuel from level L to L+1.</summary>
        public List<long> LevelFuelCosts = new List<long>();
        /// <summary>Permanent worm stats per star+awakening of every owned mount (collection bonus).</summary>
        public List<StatMod> CollectionPerStep = new List<StatMod>();

        public int MaxLevel(int stars, int awaken) => Math.Min(LevelFuelCosts.Count, MaxLevelBase + MaxLevelPerStep * (stars + awaken));
    }

    public sealed partial class ContentDb
    {
        /// <summary>Mounts (spaceships), stars, awakening, weapon skills (Progression).</summary>
        public readonly MountConfig MountConfig = new MountConfig();

        partial void BuildMounts()
        {
            var m = MountConfig;
            m.MaxStars = 5; m.MaxAwaken = 5; m.DupeShards = 10;
            m.StarShardCosts.AddRange(new long[] { 0, 0, 20, 50, 100, 180 });
            m.AwakenShardCosts.AddRange(new long[] { 0, 100, 150, 200, 260, 330 });
            m.AwakenCoreCosts.AddRange(new long[] { 0, 100, 200, 400, 700, 1100 });
            m.StarStatPercent.AddRange(new double[] { 0, 0, 15, 35, 60, 90 });
            m.AwakenStatPercent.AddRange(new double[] { 0, 15, 32, 52, 75, 100 });
            m.MaxLevelBase = 20; m.MaxLevelPerStep = 10;
            m.LevelFuelCosts.Add(0);
            for (int l = 1; l < m.MaxLevelBase + m.MaxLevelPerStep * (m.MaxStars + m.MaxAwaken); l++) m.LevelFuelCosts.Add((long)Math.Round(50 * Math.Pow(l, 1.5)));
            m.CollectionPerStep.Add(new StatMod(StatId.MountPower, 1));
            m.CollectionPerStep.Add(new StatMod(StatId.Hp, 0, 0.2));

            AddMountDef("mount_scout_saucer", "Scout Saucer", SkillFamily.Lightning, SkillFamily.Combo, "Zap Turret");
            AddMountDef("mount_magma_barge", "Magma Barge", SkillFamily.Fire, SkillFamily.Counter, "Lava Mortar");
            AddMountDef("mount_frost_frigate", "Frost Frigate", SkillFamily.Ice, SkillFamily.Counter, "Cryo Beam");
            AddMountDef("mount_toxic_hauler", "Toxic Hauler", SkillFamily.Poison, SkillFamily.Combo, "Sludge Sprayer");
            AddMountDef("mount_star_lancer", "Star Lancer", SkillFamily.Crit, SkillFamily.Bleed, "Lance Cannon");
            AddMountDef("mount_hive_carrier", "Hive Carrier", SkillFamily.AlienSynergy, SkillFamily.Crit, "Spore Cannon"); // was "Drone Bay": the name promised drones the weapon never had
        }

        /// <summary>
        /// Weapon plan: W I at 1★, W II at 2★, W III at 4★, support system at 5★, support II at awakening 3,
        /// W IV (ultimate) at awakening 5.
        /// </summary>
        private void AddMountDef(string id, string name, SkillFamily family, SkillFamily support, string weapon)
        {
            var d = new MountDef { Id = id, Name = name, Family = family, SupportFamily = support };
            d.BaseStats.Add(new StatMod(StatId.Atk, 30));
            d.BaseStats.Add(new StatMod(StatId.Hp, 300));
            d.BaseStats.Add(new StatMod(StatId.DamageReduction, 5));
            d.StatsPerLevel.Add(new StatMod(StatId.Atk, 4));
            d.StatsPerLevel.Add(new StatMod(StatId.Hp, 30));

            string w1 = id + "_w1", w2 = id + "_w2", w3 = id + "_w3", w4 = id + "_w4", s1 = id + "_s1", s2 = id + "_s2";
            AddSkill(ProgFamilySkill(w1, weapon, family, Rarity.Rare, SkillSource.Mount, 1.0, null));
            AddSkill(ProgFamilySkill(w2, weapon + " II", family, Rarity.Epic, SkillSource.Mount, 1.5, w1));
            AddSkill(ProgFamilySkill(w3, weapon + " III", family, Rarity.Legendary, SkillSource.Mount, 2.1, w2));
            AddSkill(ProgFamilySkill(s1, name + " Support System", support, Rarity.Legendary, SkillSource.Mount, 1.2, null));
            AddSkill(ProgFamilySkill(s2, name + " Support System II", support, Rarity.Mythic, SkillSource.Mount, 2.2, s1));
            AddSkill(ProgFamilySkill(w4, weapon + " Omega", family, Rarity.Mythic, SkillSource.Mount, 3.2, w3));

            d.Weapons.Add(new SkillStep(1, 0, w1));
            d.Weapons.Add(new SkillStep(2, 0, w2, w1));
            d.Weapons.Add(new SkillStep(4, 0, w3, w2));
            d.Weapons.Add(new SkillStep(5, 0, s1));
            d.Weapons.Add(new SkillStep(5, 3, s2, s1));
            d.Weapons.Add(new SkillStep(5, 5, w4, w3));
            MountConfig.Defs[id] = d;
        }
    }
}
