using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>
    /// Endless main campaign and Dark Side (Run workstream content). Main portal i (0-based) and Dark Side level i are pure
    /// functions of their index: the first <see cref="CampaignDef.Portals"/> portals / <see cref="CampaignDef.DarkSideLevels"/>
    /// Dark Side levels are materialized at start-up (Portals / RunLevels, so existing ids and lists keep working), every later
    /// one is generated on first access (<see cref="RunLevel"/>, <see cref="Portal"/>, <see cref="MainLevel"/>,
    /// <see cref="DarkSideLevel"/>) into a small bounded cache. Regenerating an evicted portal yields identical content.
    /// Reward formulas: docs/design/economy-model.md ("Endless campaign rewards").
    /// </summary>
    public sealed partial class ContentDb
    {
        public const string DarkSidePortalId = "dark_side";
        /// <summary>Ids of generated main portals beyond the named ones: "main_51", levels "main_51_1" … "main_51_10".</summary>
        public const string GeneratedMainPrefix = "main_";
        private const string DarkSideLevelPrefix = "dark_side_";
        private const string PortalLootPrefix = "loot_p";
        /// <summary>Generated content is never built beyond this portal / Dark Side index (ids past it are unknown).</summary>
        public const int MaxGeneratedIndex = 1000000;

        // The original 50 named portals (their ids are in saves: keep them).
        private static readonly string[] CampaignNamedPortals =
        {
            "Ember Wastes", "Glacier Rift", "Static Jungle", "Spore Hollows", "Rust Canyons", "Crystal Reef", "Gloom Marsh", "Sunscar Mesa",
            "Hive Spires", "Tidal Caverns", "Void Orchard", "Prism Barrens", "Thunder Steppes", "Bone Dunes", "Magnet Peaks", "Mirror Lakes",
            "Ash Cathedral", "Gravity Wells", "Neon Thicket", "Frost Forge", "Moonlit Mire", "Comet Scar", "Rift Gardens", "Iron Tundra",
            "Echo Chasm", "Star Nursery", "Nova Crater", "Eclipse Ridge", "Quantum Bog", "Abyssal Spire", "Molten Choir", "Shiver Coast",
            "Volt Labyrinth", "Hollow Moon", "Cinder Halo", "Rime Citadel", "Storm Cradle", "Null Expanse", "Solar Throne", "World Core",
            "Ember Deep", "Glass Tundra", "Spark Delta", "Umbral Gate", "Lava Garden", "Hail Basin", "Lightning Loom", "Starless Sea",
            "Crown of Cinders", "Heart of Frost",
        };

        private static readonly Dictionary<string, int> NamedPortalIndex = BuildNamedPortalIndex();

        private static Dictionary<string, int> BuildNamedPortalIndex()
        {
            var d = new Dictionary<string, int>();
            for (int i = 0; i < CampaignNamedPortals.Length; i++) d[CampaignNamedPortals[i].ToLowerInvariant().Replace(' ', '_')] = i;
            return d;
        }

        // Combinable name parts of generated portals: adjective × family biome noun (512 unique names per cycle).
        private static readonly string[] CampaignAdjectives =
        {
            "Shattered", "Silent", "Burning", "Drowned", "Forgotten", "Twisted", "Gilded", "Howling",
            "Sunken", "Restless", "Crimson", "Pale", "Iron", "Weeping", "Starlit", "Fractured",
        };
        private static readonly string[][] CampaignNouns =
        {
            new[] { "Cinderfields", "Magma Shelf", "Ash Plains", "Smoulder Vale", "Pyre Steppe", "Scorch Basin", "Flare Dunes", "Kiln Ridge" },
            new[] { "Rime Fjord", "Ice Shelf", "Frost Barrens", "Hoarfrost Vale", "Glacier Steps", "Snowveil Pass", "Permafrost Field", "Sleet Hollow" },
            new[] { "Arc Jungle", "Volt Marsh", "Storm Thicket", "Charge Canopy", "Spark Fen", "Ion Grove", "Thunder Weald", "Surge Delta" },
            new[] { "Null Rift", "Umbra Pits", "Hollow Deep", "Abyss Gate", "Shade Warren", "Gloom Expanse", "Void Sink", "Dusk Chasm" },
        };

        // Biome families (ember, glacier, static, void): tile pool, boss, enemy packs, elites, boss tiles (feed, relief, tribute).
        private static readonly string[] FamilyPools = { "pool_ember", "pool_glacier", "pool_static", "pool_void" };
        private static readonly string[] FamilyBosses = { "boss_magma_tyrant", "boss_frost_leviathan", "boss_storm_hive", "boss_void_maw" };
        private static readonly string[] FamilyBossNames = { "Magma Tyrant", "Frost Leviathan", "Storm Hive Queen", "Void Maw" };
        private static readonly string[][] FamilyEnemies =
        {
            new[] { "ember_imp", "magma_slug", "cinder_hound", "ash_wraith" },
            new[] { "frost_mite", "ice_crawler", "glacier_golem", "snow_lurker" },
            new[] { "volt_beetle", "static_eel", "spark_drone", "thunder_moth" },
            new[] { "void_spawn", "null_shade", "tunnel_stalker", "burrow_grub" },
        };
        private static readonly string[][] FamilyElites =
        {
            new[] { "elite_lava_brute" }, new[] { "elite_frost_knight" }, new[] { "elite_storm_caller" }, new[] { "elite_void_reaver", "elite_rift_echo" },
        };
        private static readonly string[][] FamilyBossTiles =
        {
            new[] { "bt_magma_vent", "bt_cooling_pool", "bt_tyrant_shrine" },
            new[] { "bt_frozen_heart", "bt_thaw_beacon", "bt_leviathan_tribute" },
            new[] { "bt_hive_node", "bt_grounding_rod", "bt_hive_tribute" },
            new[] { "bt_void_rift", "bt_seal_stone", "bt_void_tribute" },
        };
        // Boss variants: title, and which family's feed / relief / tribute tile the boss brings (offsets to its own family).
        private static readonly string[] BossTitles = { "Elder", "Hollow", "Radiant", "Ashen", "Ascended", "Feral" };
        private static readonly int[][] BossTileCombos =
        {
            new[] { 0, 1, 2 }, new[] { 1, 0, 3 }, new[] { 0, 2, 1 }, new[] { 3, 1, 0 }, new[] { 2, 3, 0 }, new[] { 1, 3, 1 },
        };
        private static readonly string[] BossHazards = { "hazard_spikes", "hazard_toll", "hazard_vortex" };

        // Bounded cache of generated content (thread-safe: the server shares one ContentDb between requests).
        private readonly object _genLock = new object();
        private readonly Dictionary<string, PortalDef> _genPortals = new Dictionary<string, PortalDef>();
        private readonly Dictionary<string, LevelDef> _genLevels = new Dictionary<string, LevelDef>();
        private readonly Dictionary<string, LootTableDef> _genLoot = new Dictionary<string, LootTableDef>();
        /// <summary>Generated levels kept in memory; the cache is dropped (and regenerated on demand) beyond this.</summary>
        public int GeneratedCacheLevels = 600;

        // ---- queries -------------------------------------------------------------------------------------------

        /// <summary>Id of main portal <paramref name="index"/> (0-based): the named portals, then "main_&lt;index+1&gt;".</summary>
        public static string MainPortalId(int index) =>
            index < CampaignNamedPortals.Length ? CampaignNamedPortals[index].ToLowerInvariant().Replace(' ', '_') : GeneratedMainPrefix + (index + 1);

        /// <summary>Main portal <paramref name="index"/> (0-based), materialized or generated; null for a negative index.</summary>
        public PortalDef MainPortal(int index) => index < 0 || index >= MaxGeneratedIndex ? null : Portal(MainPortalId(index));

        /// <summary>Id of global main level n (1-based).</summary>
        public string MainLevelId(int n)
        {
            if (n < 1) return null;
            int per = Math.Max(1, Campaign.LevelsPerPortal);
            return MainPortalId((n - 1) / per) + "_" + ((n - 1) % per + 1);
        }

        /// <summary>Global main level n (1-based): there is always a next one.</summary>
        public LevelDef MainLevel(int n) => n < 1 ? null : RunLevel(MainLevelId(n));

        /// <summary>Dark Side level i (1-based): there is always a next one.</summary>
        public LevelDef DarkSideLevel(int i) => i < 1 ? null : RunLevel(DarkSideLevelPrefix + i);

        /// <summary>Level <paramref name="index"/> (1-based) of a portal; endless portals generate levels past their LevelIds.</summary>
        public LevelDef PortalLevel(PortalDef portal, int index)
        {
            if (portal == null || index < 1) return null;
            if (index <= portal.LevelIds.Count) return RunLevel(portal.LevelIds[index - 1]);
            return portal.Kind == PortalKind.DarkSide ? DarkSideLevel(index) : null;
        }

        /// <summary>Whether a portal has a level after <paramref name="index"/>.</summary>
        public bool PortalHasLevel(PortalDef portal, int index) => portal != null && (portal.Endless || index <= portal.LevelIds.Count);

        /// <summary>Enemy multiplier at a fractional threat position (integer step × fraction bonus).</summary>
        public double ThreatMultiplierAt(double threat)
        {
            int whole = (int)Math.Floor(threat + 1e-9);
            return ThreatMultiplier(whole) * ThreatFractionMultiplier(threat);
        }

        /// <summary>
        /// Currency reward scale of main portal <paramref name="index"/>: (i + 1) up to the endless anchor, then
        /// scale(anchor) × (M(i) / M(anchor))^α with M the threat multiplier at the portal's first level.
        /// </summary>
        public double MainRewardScale(int index)
        {
            var c = Campaign;
            int anchor = Math.Max(1, c.EndlessFromPortal) - 1;
            if (index <= anchor) return c.RewardScale(index);
            double ratio = PortalThreatMultiplier(index) / PortalThreatMultiplier(anchor);
            return c.RewardScale(anchor) * Math.Pow(ratio, c.EndlessRewardExponent);
        }

        private double PortalThreatMultiplier(int index) => ThreatMultiplierAt(Campaign.ThreatAt(index * Math.Max(1, Campaign.LevelsPerPortal) + 1));

        /// <summary>Rarity tiers added to run item drops of main portal <paramref name="index"/> (0 before the endless anchor).</summary>
        public int DropRarityShift(int index)
        {
            var c = Campaign;
            if (index < c.EndlessFromPortal) return 0;
            return Math.Min(c.MaxDropRarity, 1 + (index - c.EndlessFromPortal) / Math.Max(1, c.RarityShiftPortals));
        }

        /// <summary>Data per clear of Dark Side level i (before the level's reward multiplier).</summary>
        public long DarkSideData(int i)
        {
            var c = Campaign;
            int anchor = Math.Max(1, c.DarkSideEndlessFrom);
            double Legacy(int k) => c.DarkSideDataBase * Math.Pow(c.DarkSideDataGrowth, k - 1);
            if (i <= anchor) return (long)Math.Round(Legacy(i));
            double ratio = ThreatMultiplierAt(DarkSideThreat(i)) / ThreatMultiplierAt(DarkSideThreat(anchor));
            return (long)Math.Round(Legacy(anchor) * Math.Pow(ratio, c.EndlessRewardExponent));
        }

        private double DarkSideThreat(int i) => Campaign.ThreatAt(Campaign.DarkSideMainLevel(i)) - Campaign.DarkSideThreatOffset;

        // ---- generation ----------------------------------------------------------------------------------------

        /// <summary>Display name of main portal <paramref name="index"/>: the named portals, then adjective + biome (+ cycle numeral).</summary>
        public static string CampaignPortalName(int index)
        {
            if (index < CampaignNamedPortals.Length) return CampaignNamedPortals[index];
            int family = index % 4;
            int k = index - CampaignNamedPortals.Length;
            int nouns = CampaignNouns[family].Length, adjs = CampaignAdjectives.Length;
            // k → (family by k mod 4, noun, adjective) is one-to-one within a cycle of 4 × nouns × adjectives portals.
            string name = CampaignAdjectives[k / (4 * nouns) % adjs] + " " + CampaignNouns[family][k / 4 % nouns];
            int cycle = k / (4 * nouns * adjs);
            return cycle == 0 ? name : name + " " + Numeral(cycle + 1);
        }

        private static string Numeral(int n)
        {
            if (n <= 0 || n > 3999) return n.ToString();
            var vals = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            var syms = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < vals.Length; i++) while (n >= vals[i]) { sb.Append(syms[i]); n -= vals[i]; }
            return sb.ToString();
        }

        private static string BossVariantId(int family, int variant) => FamilyBosses[family] + "_v" + variant;

        /// <summary>Boss of main portal <paramref name="index"/>: the family boss, then a rotating variant every N portals.</summary>
        public string MainPortalBossId(int index)
        {
            int family = index % 4;
            var c = Campaign;
            if (index < c.EndlessFromPortal || c.BossVariants <= 0) return FamilyBosses[family];
            int era = (index - c.EndlessFromPortal) / Math.Max(1, c.BossVariantEveryPortals);
            return BossVariantId(family, era % Math.Min(c.BossVariants, BossTitles.Length) + 1);
        }

        /// <summary>Boss variants for the endless portals (finite: family × title), registered with the other bosses.</summary>
        private void CampaignBuildBossVariants()
        {
            int variants = Math.Min(Campaign.BossVariants, BossTitles.Length);
            for (int f = 0; f < 4; f++)
                for (int v = 1; v <= variants; v++)
                {
                    var combo = BossTileCombos[(v - 1) % BossTileCombos.Length];
                    var tiles = new[]
                    {
                        FamilyBossTiles[(f + combo[0]) % 4][0], FamilyBossTiles[(f + combo[1]) % 4][1], FamilyBossTiles[(f + combo[2]) % 4][2],
                    };
                    RunBoss(BossVariantId(f, v), BossTitles[v - 1] + " " + FamilyBossNames[f], FamilyBosses[f], tiles,
                        FamilyEnemies[f][v % FamilyEnemies[f].Length], BossHazards[v % BossHazards.Length]);
                }
        }

        /// <summary>Builds main portal <paramref name="index"/> and its levels (pure: stores nothing).</summary>
        private PortalDef CampaignBuildMainPortal(int index, List<LevelDef> levels)
        {
            var c = Campaign;
            int family = index % 4;
            string id = MainPortalId(index), name = CampaignPortalName(index), bossId = MainPortalBossId(index);
            var portal = new PortalDef { Id = id, Name = name, Kind = PortalKind.Main, Order = index };
            double scale = MainRewardScale(index);
            double familyPct = c.FamilyStrengthPct[index % c.FamilyStrengthPct.Count];
            if (index >= c.LateFamilyFromPortal && c.LateFamilyStrengthPct.Count > 0)
                familyPct = ((1 + familyPct / 100.0) * (1 + c.LateFamilyStrengthPct[index % c.LateFamilyStrengthPct.Count] / 100.0) - 1) * 100;
            for (int i = 1; i <= c.LevelsPerPortal; i++)
            {
                int n = index * c.LevelsPerPortal + i;
                double threat = c.ThreatAt(n);
                int whole = (int)Math.Floor(threat + 1e-9);
                var l = RunNewLevel(id + "_" + i, name + " " + i, id, i, whole, FamilyPools[family], bossId, PortalLootPrefix + (index + 1),
                    FamilyEnemies[family], FamilyElites[family]);
                l.Number = n;
                double basis = ThreatFractionMultiplier(threat) * (1 + familyPct / 100.0);
                double hpPct = (basis * (1 + c.EarlyHpPct(n) / 100.0) - 1) * 100, atkPct = (basis * (1 + c.EarlyAtkPct(n) / 100.0) - 1) * 100;
                if (Math.Abs(hpPct) > 0.01 || Math.Abs(atkPct) > 0.01)
                    l.Modifiers = new LevelModifiers { EnemyHpPct = Math.Round(hpPct, 2), EnemyAtkPct = Math.Round(atkPct, 2) };
                l.StaminaCost = Math.Min(c.StaminaMax, c.StaminaBase + index);
                // A lap of the circuit board; two dice walk ~twice as far per roll, so their lap is longer. The boss's awakening
                // meter ends the run (~30 rolls), MaxRolls is only the hard limit.
                l.Dice = i >= 6 ? 2 : 1;
                l.BoardLength = (24 + i + Math.Min(index, c.BoardGrowthPortals) * 2) * (l.Dice == 2 ? 3 : 2) / 2;
                l.MaxRolls = c.MaxRolls;
                l.CompletionRewards.Add(Reward.Cur(Currencies.Biomass, (long)Math.Round((120 + 30 * i) * scale)));
                l.CompletionRewards.Add(Reward.Cur(Currencies.Ore, (long)Math.Round((40 + 10 * i) * scale)));
                if (i % 5 == 0) l.CompletionRewards.Add(Reward.Cur(Currencies.PartTokens, 1));
                l.FirstClearRewards.AddRange(c.MilestoneRewards(n));
                levels.Add(l);
                portal.LevelIds.Add(l.Id);
            }
            return portal;
        }

        /// <summary>Loot table of main portal <paramref name="index"/>: currencies × the portal's reward scale, items shifted up in rarity past the anchor.</summary>
        private LootTableDef CampaignPortalLoot(int index)
        {
            var t = new LootTableDef { Id = PortalLootPrefix + (index + 1) };
            long s = (long)Math.Round(MainRewardScale(index));
            int shift = DropRarityShift(index), max = Campaign.MaxDropRarity;
            int R(int rarity) => Math.Min(max, rarity + shift);
            // Biomass comes mainly from portal runs (completion + loot); the city farms are the secondary source.
            t.Entries.Add(new LootEntry(30, RewardKinds.Currency, Currencies.Biomass, 45 * s, 90 * s));
            t.Entries.Add(new LootEntry(20, RewardKinds.Currency, Currencies.Ore, 20 * s, 40 * s));
            t.Entries.Add(new LootEntry(14, RewardKinds.Currency, Currencies.Grain, 20 * s, 40 * s));
            t.Entries.Add(new LootEntry(8, RewardKinds.Currency, Currencies.Fuel, 10 * s, 20 * s));
            t.Entries.Add(new LootEntry(8, RewardKinds.Currency, Currencies.HeroXp, 15 * s, 30 * s));
            t.Entries.Add(new LootEntry(6, RewardKinds.Currency, Currencies.GearCores, 1 * s, 2 * s));
            t.Entries.Add(new LootEntry(5, RewardKinds.Part, "*", 1, 1, R(0)));
            t.Entries.Add(new LootEntry(2, RewardKinds.Part, "*", 1, 1, R(1)));
            t.Entries.Add(new LootEntry(3, RewardKinds.Rune, "*", 1, 1, R(0)));
            t.Entries.Add(new LootEntry(2, RewardKinds.Currency, Currencies.PartTokens, 1, 1));
            t.Entries.Add(new LootEntry(1, RewardKinds.Currency, Currencies.Diamonds, 5, 15));
            t.Entries.Add(new LootEntry(0.6, RewardKinds.Currency, Currencies.BlueprintTokens, 1, 1));
            t.Entries.Add(new LootEntry(0.6, RewardKinds.Currency, Currencies.AlienTokens, 1, 1));
            t.Entries.Add(new LootEntry(0.25, RewardKinds.Blueprint, "*", 1, 1, R(0)));
            t.Entries.Add(new LootEntry(0.25, RewardKinds.Alien, "*", 1, 1, R(0)));
            return t;
        }

        /// <summary>Builds Dark Side level i (pure): procedural along the campaign curve, extreme modifiers, Data reward.</summary>
        private LevelDef CampaignBuildDarkSideLevel(int i)
        {
            double threat = DarkSideThreat(i);
            // Legacy levels used the rounded main level; same here (DarkSideMainLevel rounds).
            int whole = (int)Math.Floor(threat + 1e-9);
            var l = RunNewLevel(DarkSideLevelPrefix + i, "Dark Side " + i, DarkSidePortalId, i, whole, "pool_dark", "boss_void_maw", "loot_dark",
                new[] { "void_spawn", "null_shade", "tunnel_stalker" }, new[] { "elite_void_reaver", "elite_rift_echo" });
            l.Number = i;
            l.StaminaCost = 12; l.ShopTableId = "shop_dark"; l.Dice = 2; l.MaxRolls = Campaign.MaxRolls; l.BoardLength = 51;
            l.RewardMultiplier = 2.5;
            double extreme = 40 + 5 * Math.Min(i, 10);
            // Threat fraction as extra enemy HP/ATK %, on top of the extreme modifier.
            double frac = (ThreatFractionMultiplier(threat) - 1) * 100;
            l.Modifiers = new LevelModifiers
            {
                EnemyAtkPct = Math.Round(extreme + frac, 2), EnemyHpPct = Math.Round(extreme + frac, 2), HealPct = -50, DangerPct = 50,
                Text = "Enemies +" + extreme + "% ATK/HP, healing -50%, danger +50%.",
            };
            l.CompletionRewards.Add(Reward.Cur(Currencies.Data, DarkSideData(i)));
            l.CompletionRewards.Add(Reward.Cur(Currencies.Diamonds, 8));
            l.CompletionRewards.Add(Reward.Cur(Currencies.MountCores, 1 + Math.Min(i, 12) / 4));
            return l;
        }

        // ---- on-demand cache -------------------------------------------------------------------------------------

        private static bool TryParseIndex(string s, int start, out int value)
        {
            value = 0;
            if (s.Length <= start || s.Length - start > 9) return false;
            for (int k = start; k < s.Length; k++)
            {
                char ch = s[k];
                if (ch < '0' || ch > '9') return false;
                value = value * 10 + (ch - '0');
            }
            return value >= 1 && (s[start] != '0');
        }

        /// <summary>Main portal index (0-based) of a portal id (named or generated), or −1.</summary>
        public static int MainPortalIndex(string portalId)
        {
            if (portalId == null) return -1;
            if (portalId.StartsWith(GeneratedMainPrefix, StringComparison.Ordinal))
                return TryParseIndex(portalId, GeneratedMainPrefix.Length, out int p) && p > CampaignNamedPortals.Length && p <= MaxGeneratedIndex ? p - 1 : -1;
            return NamedPortalIndex.TryGetValue(portalId, out int idx) ? idx : -1;
        }

        private void TrimCache()
        {
            if (_genLevels.Count < GeneratedCacheLevels) return;
            _genLevels.Clear();
            _genPortals.Clear();
            _genLoot.Clear();
        }

        private PortalDef GeneratedPortal(string id)
        {
            int index = MainPortalIndex(id);
            if (index < 0) return null;
            lock (_genLock)
            {
                if (_genPortals.TryGetValue(id, out var cached)) return cached;
                TrimCache();
                var levels = new List<LevelDef>();
                var portal = CampaignBuildMainPortal(index, levels);
                _genPortals[id] = portal;
                foreach (var l in levels) _genLevels[l.Id] = l;
                return portal;
            }
        }

        private LevelDef GeneratedLevel(string id)
        {
            if (id.StartsWith(DarkSideLevelPrefix, StringComparison.Ordinal))
            {
                if (!TryParseIndex(id, DarkSideLevelPrefix.Length, out int i) || i > MaxGeneratedIndex) return null;
                lock (_genLock)
                {
                    if (_genLevels.TryGetValue(id, out var cached)) return cached;
                    TrimCache();
                    var l = CampaignBuildDarkSideLevel(i);
                    _genLevels[id] = l;
                    return l;
                }
            }
            int cut = id.LastIndexOf('_');
            if (cut <= 0) return null;
            lock (_genLock)
                if (_genLevels.TryGetValue(id, out var cached)) return cached;
            var portal = GeneratedPortal(id.Substring(0, cut));
            if (portal == null || !TryParseIndex(id, cut + 1, out int index) || index > portal.LevelIds.Count || portal.LevelIds[index - 1] != id) return null;
            lock (_genLock)
            {
                if (_genLevels.TryGetValue(id, out var l)) return l;
            }
            // Evicted between the two locks: rebuild the portal once more (identical content).
            var levels = new List<LevelDef>();
            CampaignBuildMainPortal(MainPortalIndex(portal.Id), levels);
            return levels[index - 1];
        }

        private LootTableDef GeneratedLootTable(string id)
        {
            if (!id.StartsWith(PortalLootPrefix, StringComparison.Ordinal) || !TryParseIndex(id, PortalLootPrefix.Length, out int p) || p > MaxGeneratedIndex) return null;
            lock (_genLock)
            {
                if (_genLoot.TryGetValue(id, out var cached)) return cached;
                var t = CampaignPortalLoot(p - 1);
                _genLoot[id] = t;
                return t;
            }
        }
    }
}
