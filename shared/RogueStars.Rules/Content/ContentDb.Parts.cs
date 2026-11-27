using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>What kind of body part a definition is. Arm parts fit either arm slot (arms are interchangeable).</summary>
    public enum PartKind { Head, Body, Tail, Legs, Arm }

    /// <summary>The six equipment slots of the worm.</summary>
    public enum PartSlot { Head, Body, Tail, Legs, LeftArm, RightArm }

    /// <summary>Standard part roles (parts v2): every standard rarity has one item per slot and role. Mythics have none.</summary>
    public enum PartRole { None, Guard, Strike, Swift, Keen, Mend, Fury }

    /// <summary>
    /// Parts v2 (docs/design/parts.md, docs/design/parts-v2/): Common, Rare, Epic and Legendary are four separate item sets that
    /// never merge; Mythic items climb their own ladder Mythic → +1 → +2 → S → S+1 → S+2 → SS (SS is final). An item's
    /// rarity is its def's rarity, a Mythic's plus its ladder step. Saves store the int.
    /// </summary>
    public enum PartRarity
    {
        Common, Rare, Epic, Legendary,
        Mythic, Mythic1, Mythic2,
        MythicS, MythicS1, MythicS2,
        MythicSS,
    }

    [Serializable]
    public sealed class PartDef
    {
        public string Id;
        public string Name;
        public PartKind Kind;
        /// <summary>The item set the def belongs to (Common … Legendary, or Mythic for the 30 build enablers).</summary>
        public PartRarity Rarity;
        /// <summary>Standard role (Guard, Strike, Swift, Keen, Mend, Fury); None for a Mythic.</summary>
        public PartRole Role;
        /// <summary>Mythic: its synergy family (None for the General Mythics and the standard parts, Crit for Keen parts).</summary>
        public SkillFamily Family;
        /// <summary>Mythic: the synergy name shown ("Fire", "Radiation", "General" …) and its build identity.</summary>
        public string Synergy, Identity;
        /// <summary>Stats at level 1 (slot core × rarity multiplier + role stat; a Mythic's synergy stat).</summary>
        public List<StatMod> BaseStats = new List<StatMod>();
        /// <summary>Stats per level above 1: flat stats +15% of level 1; percentage stats linear to 3× at the part's max level.</summary>
        public List<StatMod> StatsPerLevel = new List<StatMod>();
        /// <summary>Skill ids (Source = Part): Epic 1, Legendary 2, Mythic 5 (active: 3 at Mythic, the 4th at S, the 5th at SS).</summary>
        public List<string> SkillIds = new List<string>();

        /// <summary>Awakened version (Mythic+, once): new name, ×1.4 stats, each skill replaced by its awakened version.</summary>
        public string AwakenedName;
        public List<StatMod> AwakenedBaseStats = new List<StatMod>();
        public List<StatMod> AwakenedStatsPerLevel = new List<StatMod>();
        public List<string> AwakenedSkillIds = new List<string>();

        public bool IsMythic => Rarity >= PartRarity.Mythic;
    }

    /// <summary>One step of the Mythic ladder: copies of the same Mythic + other Mythics of the same slot (base step, not awakened), Metal and Ore.</summary>
    [Serializable]
    public sealed class PartClimbStep
    {
        public int Copies;
        public int Others;
        public long Metal;
        public long Ore;
    }

    [Serializable]
    public sealed class PartRarityDef
    {
        public PartRarity Rarity;
        public string Name;
        /// <summary>Level cap at this rarity / Mythic step (Common 10 … Legendary 50; Mythic 70 … SS 100).</summary>
        public int LevelCap;
        /// <summary>Rune slots granted by rarity alone (the Forge adds more, up to PartsConfig.MaxRuneSlots).</summary>
        public int RuneSlots;
        /// <summary>Skills active at this rarity (Epic 1, Legendary 2, Mythic 3, S 4, SS 5).</summary>
        public int SkillCount;
        /// <summary>Metal a spare part of this rarity is worth when fed (Common 1, Rare 2, Epic 6, Legendary 20; Mythics never).</summary>
        public long Metal;
        /// <summary>Mythic ladder: the cost to reach this step from the one below (null below Mythic +1).</summary>
        public PartClimbStep Climb;
    }

    [Serializable]
    public sealed class ForgeStep
    {
        public long Diamonds;
        public long GearCores;
        /// <summary>Percent added to the part's base stats only (cumulative over steps).</summary>
        public double BonusPercent;
    }

    [Serializable]
    public sealed class RuneDef
    {
        public string Id;
        public string Name;
        public StatId Stat;
        /// <summary>True: value is a stat percent; false: flat.</summary>
        public bool IsPercent;
        /// <summary>Value at Common rarity; multiplied by PartsConfig.RuneRarityMultipliers.</summary>
        public double BaseValue;
    }

    [Serializable]
    public sealed class PartsConfig
    {
        public Dictionary<string, PartDef> Defs = new Dictionary<string, PartDef>();
        /// <summary>Indexed by (int)PartRarity.</summary>
        public List<PartRarityDef> Rarities = new List<PartRarityDef>();
        /// <summary>LevelOreCosts[L] / LevelGearCoreCosts[L]: cost from level L to L+1 (index 0 unused).</summary>
        public List<long> LevelOreCosts = new List<long>();
        public List<long> LevelGearCoreCosts = new List<long>();
        public int MaxRuneSlots = 5;
        public PartRarity ForgeMinRarity = PartRarity.Epic;
        public List<ForgeStep> Forge = new List<ForgeStep>();
        /// <summary>Awakening: Mythic or higher, once per part, for AwakenCost Awaken Cores; ×AwakenStatMult level-1 stats.</summary>
        public PartRarity AwakenMinRarity = PartRarity.Mythic;
        public long AwakenCost = 50;
        public double AwakenStatMult = 1.4;
        /// <summary>Flat stats grow by this share of their level-1 value per level (rounded to whole numbers).</summary>
        public double FlatGrowth = 0.15;
        /// <summary>Percentage stats grow linearly from their level-1 value to this multiple at the part's max level.</summary>
        public double PercentMaxMult = 3;
        /// <summary>Replicator: ReplicateMythics spare Mythics (any slot, base step) + ReplicateOre → 1 copy of a Mythic the player owns.</summary>
        public int ReplicateMythics = 3;
        public long ReplicateOre = 5000;
        /// <summary>Starter set: one Common per slot (new players' kit; old saves are reset to it, parts v2 has no migration).</summary>
        public List<string> StarterParts = new List<string>();

        public Dictionary<string, RuneDef> Runes = new Dictionary<string, RuneDef>();
        /// <summary>Indexed by (int)Rarity (Common..Mythic).</summary>
        public List<double> RuneRarityMultipliers = new List<double>();
        public Rarity RuneMaxRarity = Rarity.Mythic;
        public int RuneMergeCount = 3;

        public PartRarityDef RarityDef(int r) => Rarities[Math.Max(0, Math.Min(Rarities.Count - 1, r))];
        public int MaxLevel => LevelOreCosts.Count;
        /// <summary>The top of the Mythic ladder (Mythic SS).</summary>
        public PartRarity TopRarity => (PartRarity)(Rarities.Count - 1);
    }

    public sealed partial class ContentDb
    {
        /// <summary>Parts v2: 120 standard items, 30 Mythics, the Mythic ladder, Metal, runes, Forge and awakening (Progression).</summary>
        public readonly PartsConfig PartsConfig = new PartsConfig();

        /// <summary>Slot core stats at Common level 1 (× the rarity multiplier), data.py CORE.</summary>
        private static readonly Dictionary<PartKind, (StatId, double)[]> PartCore = new Dictionary<PartKind, (StatId, double)[]>
        {
            [PartKind.Head] = new[] { (StatId.Hp, 50.0) },
            [PartKind.Body] = new[] { (StatId.Hp, 100.0), (StatId.Def, 6.0) },
            [PartKind.Tail] = new[] { (StatId.Hp, 40.0), (StatId.Atk, 5.0) },
            [PartKind.Legs] = new[] { (StatId.Def, 8.0), (StatId.Speed, 3.0) },
            [PartKind.Arm] = new[] { (StatId.Atk, 15.0) },
        };

        /// <summary>Role stat at Common level 1 (× the rarity multiplier); point stats are percentage stats.</summary>
        private static readonly Dictionary<PartRole, (StatId, double)> PartRoleStat = new Dictionary<PartRole, (StatId, double)>
        {
            [PartRole.Guard] = (StatId.Def, 6), [PartRole.Strike] = (StatId.Atk, 5), [PartRole.Swift] = (StatId.Speed, 4),
            [PartRole.Keen] = (StatId.CritChance, 3), [PartRole.Mend] = (StatId.LifeSteal, 2), [PartRole.Fury] = (StatId.RageGain, 6),
        };

        public static readonly PartRole[] PartRoles = { PartRole.Guard, PartRole.Strike, PartRole.Swift, PartRole.Keen, PartRole.Mend, PartRole.Fury };
        public static readonly PartKind[] PartKinds = { PartKind.Head, PartKind.Body, PartKind.Tail, PartKind.Legs, PartKind.Arm };
        private static readonly string[] PartTier = { "Scrap", "Alloy", "Plasma", "Quantum" };
        private static readonly double[] PartMult = { 1.0, 1.7, 2.4, 3.6, 5.6 };
        private static readonly string[] PartPrefix = { "c", "r", "e", "l" };

        /// <summary>Standard item id: rarity prefix (c / r / e / l), slot, role ("e_head_guard").</summary>
        public static string StdPartId(PartRarity r, PartKind k, PartRole role) =>
            PartPrefix[(int)r] + "_" + k.ToString().ToLowerInvariant() + "_" + role.ToString().ToLowerInvariant();

        /// <summary>The page's rounding: below 20 whole numbers, from 20 multiples of 5 (build.py rnd, banker's rounding like Python).</summary>
        public static double PartRound(double x) => x < 20 ? Math.Floor(x + 0.5) : Math.Round(x / 5.0) * 5;

        partial void BuildParts()
        {
            var p = PartsConfig;

            // ---- rarities and the Mythic ladder (docs/design/parts.md) ----------------------------------
            // name, level cap, rune slots (today's mapping), skills active, Metal when fed
            AddPartRarity(PartRarity.Common, "Common", 10, 0, 0, 1, null);
            AddPartRarity(PartRarity.Rare, "Rare", 20, 1, 0, 2, null);
            AddPartRarity(PartRarity.Epic, "Epic", 30, 1, 1, 6, null);
            AddPartRarity(PartRarity.Legendary, "Legendary", 50, 2, 2, 20, null);
            AddPartRarity(PartRarity.Mythic, "Mythic", 70, 3, 3, 0, null);
            // copies, other Mythics of the same slot, Metal, Ore (pacing.py LADDER_REQ, Ore = today's Mythic-ladder Ore)
            AddPartRarity(PartRarity.Mythic1, "Mythic+1", 75, 3, 3, 0, Climb(1, 0, 100, 2200));
            AddPartRarity(PartRarity.Mythic2, "Mythic+2", 80, 3, 3, 0, Climb(1, 0, 150, 3400));
            AddPartRarity(PartRarity.MythicS, "Mythic S", 85, 4, 4, 0, Climb(1, 0, 200, 5100));
            AddPartRarity(PartRarity.MythicS1, "Mythic S+1", 90, 4, 4, 0, Climb(2, 2, 300, 7600));
            AddPartRarity(PartRarity.MythicS2, "Mythic S+2", 95, 4, 4, 0, Climb(2, 2, 450, 11400));
            AddPartRarity(PartRarity.MythicSS, "Mythic SS", 100, 5, 5, 0, Climb(5, 5, 600, 17100));

            // ---- levels: today's curves (Ore 30·L^1.4, Gear Cores 2·(L−9)^1.25 from level 10) up to the top cap ------
            int maxLevel = p.Rarities[p.Rarities.Count - 1].LevelCap;
            p.LevelOreCosts.Add(0); p.LevelGearCoreCosts.Add(0);
            for (int l = 1; l < maxLevel; l++)
            {
                p.LevelOreCosts.Add((long)Math.Round(30 * Math.Pow(l, 1.4)));
                p.LevelGearCoreCosts.Add(l < 10 ? 0 : (long)Math.Round(2 * Math.Pow(l - 9, 1.25)));
            }

            // ---- forge, awakening, replicator ----------------------------------------------------------
            p.MaxRuneSlots = 5;
            p.ForgeMinRarity = PartRarity.Epic;
            p.Forge.Add(new ForgeStep { Diamonds = 300, GearCores = 400, BonusPercent = 10 });
            p.Forge.Add(new ForgeStep { Diamonds = 900, GearCores = 1200, BonusPercent = 15 });
            p.AwakenMinRarity = PartRarity.Mythic;
            p.AwakenCost = 50;
            p.AwakenStatMult = 1.4;
            p.ReplicateMythics = 3;
            p.ReplicateOre = 5000;

            // ---- runes -----------------------------------------------------------------------------
            p.RuneRarityMultipliers.AddRange(new double[] { 1, 2.2, 4, 6.5, 10 });
            p.RuneMergeCount = 3;
            AddRuneDef("rune_vigor", "Vigor Rune", StatId.Hp, true, 2);
            AddRuneDef("rune_fury", "Fury Rune", StatId.Atk, true, 2);
            AddRuneDef("rune_bulwark", "Bulwark Rune", StatId.Def, true, 2.5);
            AddRuneDef("rune_precision", "Precision Rune", StatId.CritChance, false, 1);
            AddRuneDef("rune_ruin", "Ruin Rune", StatId.CritDamage, false, 4);
            AddRuneDef("rune_haste", "Haste Rune", StatId.Speed, false, 3);
            AddRuneDef("rune_echo", "Echo Rune", StatId.ComboChance, false, 1);
            AddRuneDef("rune_riposte", "Riposte Rune", StatId.CounterChance, false, 1);
            AddRuneDef("rune_mist", "Mist Rune", StatId.Dodge, false, 0.8);
            AddRuneDef("rune_leech", "Leech Rune", StatId.LifeSteal, false, 0.8);
            AddRuneDef("rune_ember", "Ember Rune", StatId.FireDamage, false, 2);
            AddRuneDef("rune_volt", "Volt Rune", StatId.LightningDamage, false, 2);

            // ---- 120 standard items: 4 rarities × 5 slots × 6 roles ----------------------------------
            for (int r = 0; r < 4; r++)
                foreach (var kind in PartKinds)
                    foreach (var role in PartRoles)
                        AddStdPart((PartRarity)r, kind, role);

            // ---- 30 Mythics (data.py M) ------------------------------------------------------------------
            foreach (var m in PartMythicData) AddMythicPart(m);

            // Starter set: one Common per slot (both arms), auto-equipped on an old save's reset.
            p.StarterParts.AddRange(new[]
            {
                StdPartId(PartRarity.Common, PartKind.Head, PartRole.Keen), StdPartId(PartRarity.Common, PartKind.Body, PartRole.Guard),
                StdPartId(PartRarity.Common, PartKind.Tail, PartRole.Strike), StdPartId(PartRarity.Common, PartKind.Legs, PartRole.Swift),
                StdPartId(PartRarity.Common, PartKind.Arm, PartRole.Strike), StdPartId(PartRarity.Common, PartKind.Arm, PartRole.Fury),
            });

            BuildPartSkills();
        }

        private static PartClimbStep Climb(int copies, int others, long metal, long ore) =>
            new PartClimbStep { Copies = copies, Others = others, Metal = metal, Ore = ore };

        private void AddPartRarity(PartRarity r, string name, int cap, int runeSlots, int skills, long metal, PartClimbStep climb)
        {
            if ((int)r != PartsConfig.Rarities.Count) throw new InvalidOperationException("rarities must be added in order");
            PartsConfig.Rarities.Add(new PartRarityDef { Rarity = r, Name = name, LevelCap = cap, RuneSlots = runeSlots, SkillCount = skills, Metal = metal, Climb = climb });
        }

        private void AddRuneDef(string id, string name, StatId stat, bool percent, double value) =>
            PartsConfig.Runes[id] = new RuneDef { Id = id, Name = name, Stat = stat, IsPercent = percent, BaseValue = value };

        /// <summary>Weapon noun of an arm by role (cosmetic only: arms are interchangeable, no attack styles).</summary>
        private static string PartNoun(PartKind k, PartRole role)
        {
            switch (k)
            {
                case PartKind.Head: return "Helm";
                case PartKind.Body: return "Shell";
                case PartKind.Tail: return "Tail";
                case PartKind.Legs: return "Legs";
            }
            switch (role)
            {
                case PartRole.Guard: return "Hammer";
                case PartRole.Strike: return "Cannon";
                case PartRole.Swift: return "Whip";
                case PartRole.Keen: return "Claw";
                case PartRole.Mend: return "Drill";
                default: return "Blaster";
            }
        }

        /// <summary>
        /// A stat line: level-1 value <paramref name="b"/>; flat stats add FlatGrowth × b per level (whole numbers), percentage
        /// stats (point stats, HP %) grow linearly to PercentMaxMult × b at <paramref name="cap"/>.
        /// </summary>
        private void AddPartStat(List<StatMod> baseStats, List<StatMod> perLevel, StatId stat, double b, bool percentOfStat, int cap)
        {
            var cfg = PartsConfig;
            bool pctStat = percentOfStat || StatMod.IsPointStat(stat);
            baseStats.Add(percentOfStat ? new StatMod(stat, 0, b) : new StatMod(stat, b));
            double step = pctStat ? b * (cfg.PercentMaxMult - 1) / Math.Max(1, cap - 1) : PartRound(b * cfg.FlatGrowth);
            perLevel.Add(percentOfStat ? new StatMod(stat, 0, step) : new StatMod(stat, step));
        }

        private void AddStdPart(PartRarity r, PartKind kind, PartRole role)
        {
            var cfg = PartsConfig;
            double mult = PartMult[(int)r];
            int cap = cfg.Rarities[(int)r].LevelCap;
            var d = new PartDef
            {
                Id = StdPartId(r, kind, role), Name = PartTier[(int)r] + " " + role + " " + PartNoun(kind, role), Kind = kind, Rarity = r, Role = role,
                Family = role == PartRole.Keen ? SkillFamily.Crit : SkillFamily.None,
            };
            // slot core + role stat (+ the arm's second role stat), merged per stat like data.py stats_for, then × the rarity multiplier
            var raw = new List<(StatId, double)>();
            void Add(StatId s, double v)
            {
                for (int i = 0; i < raw.Count; i++) if (raw[i].Item1 == s) { raw[i] = (s, raw[i].Item2 + v); return; }
                raw.Add((s, v));
            }
            foreach (var (s, v) in PartCore[kind]) Add(s, v);
            var rs = PartRoleStat[role]; Add(rs.Item1, rs.Item2);
            if (kind == PartKind.Arm && role == PartRole.Guard) Add(StatId.CounterChance, 3);
            if (kind == PartKind.Arm && role == PartRole.Swift) Add(StatId.ComboChance, 3);
            foreach (var (s, v) in raw) AddPartStat(d.BaseStats, d.StatsPerLevel, s, PartRound(v * mult), false, cap);
            int n = r == PartRarity.Epic ? 1 : r == PartRarity.Legendary ? 2 : 0;
            for (int i = 0; i < n; i++) d.SkillIds.Add(d.Id + "_sk" + (i + 1));
            PartsConfig.Defs[d.Id] = d;
        }

        /// <summary>A Mythic's synergy stat ("+10% fire damage" …): the stat and whether it is a % of the stat (HP) or points.</summary>
        private static (StatId stat, double value, bool percentOfStat) MythicStat(string text)
        {
            var parts = text.TrimStart('+').Split(new[] { '%' }, 2);
            double v = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            switch (parts[1].Trim())
            {
                case "fire damage": return (StatId.FireDamage, v, false);
                case "ice damage": return (StatId.IceDamage, v, false);
                case "Arc damage": return (StatId.ArcDamage, v, false);
                case "radiation damage": return (StatId.PoisonDamage, v, false);
                case "bleed damage": return (StatId.BleedDamage, v, false);
                case "combo chance": return (StatId.ComboChance, v, false);
                case "counter chance": return (StatId.CounterChance, v, false);
                case "alien power": return (StatId.AlienPower, v, false);
                case "Echo damage": return (StatId.EchoDamage, v, false);
                case "crit chance": return (StatId.CritChance, v, false);
                case "HP": return (StatId.Hp, v, true);
                case "rage gain": return (StatId.RageGain, v, false);
                default: throw new InvalidOperationException("unknown Mythic stat " + text);
            }
        }

        internal static SkillFamily SynergyFamily(string syn)
        {
            switch (syn)
            {
                case "Fire": return SkillFamily.Fire;
                case "Ice": return SkillFamily.Ice;
                case "Lightning": return SkillFamily.Lightning;
                case "Radiation": return SkillFamily.Poison;
                case "Bleed": return SkillFamily.Bleed;
                case "Combo": return SkillFamily.Combo;
                case "Counter": return SkillFamily.Counter;
                case "Alien": return SkillFamily.AlienSynergy;
                case "Time": return SkillFamily.Time;
                default: return SkillFamily.None;
            }
        }

        private void AddMythicPart(MythicData m)
        {
            var cfg = PartsConfig;
            var kind = (PartKind)Enum.Parse(typeof(PartKind), m.Slot);
            int cap = cfg.Rarities[cfg.Rarities.Count - 1].LevelCap; // percentage stats reach 3× at level 100 (SS, final)
            var d = new PartDef
            {
                Id = m.Id, Name = m.Name, AwakenedName = m.AwakenedName, Kind = kind, Rarity = PartRarity.Mythic, Role = PartRole.None,
                Family = SynergyFamily(m.Synergy), Synergy = m.Synergy, Identity = m.Identity,
            };
            var syn = MythicStat(m.Stat);
            foreach (var aw in new[] { false, true })
            {
                var bs = aw ? d.AwakenedBaseStats : d.BaseStats;
                var pl = aw ? d.AwakenedStatsPerLevel : d.StatsPerLevel;
                double am = aw ? cfg.AwakenStatMult : 1;
                foreach (var (s, v) in PartCore[kind]) AddPartStat(bs, pl, s, PartRound(PartRound(v * PartMult[4]) * am), false, cap);
                AddPartStat(bs, pl, syn.stat, PartRound(syn.value * am), syn.percentOfStat, cap);
            }
            for (int i = 0; i < 5; i++) { d.SkillIds.Add(d.Id + "_sk" + (i + 1)); d.AwakenedSkillIds.Add(d.Id + "_aw" + (i + 1)); }
            PartsConfig.Defs[d.Id] = d;
        }
    }
}
