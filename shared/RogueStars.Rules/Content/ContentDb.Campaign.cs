using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Content
{
    /// <summary>
    /// Endless main campaign and Dark Side (Run workstream content). The main campaign is one linear sequence of levels, every
    /// level its own portal ("portal after portal") with its own biome; there are no boss levels (every run ends with its
    /// biome's boss). Main level n and Dark Side level i are pure functions of their number: the first
    /// <see cref="CampaignDef.MaterializedLevels"/> main levels / <see cref="CampaignDef.DarkSideLevels"/> Dark Side levels are
    /// materialized at start-up (Portals / RunLevels), every later one is generated on first access (<see cref="RunLevel"/>,
    /// <see cref="Portal"/>, <see cref="MainLevel"/>, <see cref="DarkSideLevel"/>) into a small bounded cache. Regenerating an
    /// evicted level yields identical content. Levels are grouped in chapters of <see cref="CampaignDef.ChapterLevels"/> for
    /// the reward / difficulty rhythm only. Reward formulas: docs/design/economy-model.md ("Endless campaign rewards").
    /// </summary>
    public sealed partial class ContentDb
    {
        public const string DarkSidePortalId = "dark_side";
        /// <summary>Main level n's id, which is also its portal's id: "main_&lt;n&gt;" ("main_1", "main_55", …).</summary>
        public const string MainPrefix = "main_";
        private const string DarkSideLevelPrefix = "dark_side_";
        private const string ChapterLootPrefix = "loot_p";
        /// <summary>Generated content is never built beyond this main level / Dark Side level / chapter (ids past it are unknown).</summary>
        public const int MaxGeneratedIndex = 1000000;

        // Enemy families (ember, glacier, static, void): tile pool, boss, enemy packs, elites, boss tiles (feed, relief, tribute).
        // Each campaign biome (CampaignDef.Biomes) fields one family.
        private static readonly string[] FamilyIds = { "ember", "glacier", "static", "void" };
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
            new[] { "elite_lava_brute", "elite_cinder_alpha", "elite_ash_matriarch" },
            new[] { "elite_frost_knight", "elite_glacier_warden", "elite_lurker_alpha" },
            new[] { "elite_storm_caller", "elite_volt_monarch", "elite_eel_tyrant" },
            new[] { "elite_void_reaver", "elite_rift_echo", "elite_null_wraith" },
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

        /// <summary>Id of main level n (1-based), also its portal's id: "main_&lt;n&gt;"; null out of range.</summary>
        public string MainLevelId(int n) => n < 1 || n > MaxGeneratedIndex ? null : MainPrefix + n;

        /// <summary>Main level number of a main level / portal id ("main_55" → 55), or −1.</summary>
        public static int MainLevelNumberOf(string id)
        {
            if (id == null || !id.StartsWith(MainPrefix, StringComparison.Ordinal)) return -1;
            return TryParseIndex(id, MainPrefix.Length, out int n) && n <= MaxGeneratedIndex ? n : -1;
        }

        /// <summary>
        /// The portal of main level <paramref name="index"/> + 1 (every main level is its own portal; 0-based like
        /// PortalDef.Order), materialized or generated; null out of range.
        /// </summary>
        public PortalDef MainPortal(int index) => index < 0 || index >= MaxGeneratedIndex ? null : Portal(MainPrefix + (index + 1));

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
        /// Currency reward scale of chapter <paramref name="chapter"/> (0-based, CampaignDef.ChapterOf): (c + 1) up to the endless
        /// anchor, then scale(anchor) × (M(c) / M(anchor))^α with M the threat multiplier at the chapter's first level.
        /// </summary>
        public double MainRewardScale(int chapter)
        {
            var c = Campaign;
            int anchor = Math.Max(1, c.EndlessFromChapter) - 1;
            if (chapter <= anchor) return c.RewardScale(chapter);
            double ratio = ChapterThreatMultiplier(chapter) / ChapterThreatMultiplier(anchor);
            return c.RewardScale(anchor) * Math.Pow(ratio, c.EndlessRewardExponent);
        }

        private double ChapterThreatMultiplier(int chapter) => ThreatMultiplierAt(Campaign.ThreatAt(chapter * Math.Max(1, Campaign.ChapterLevels) + 1));

        /// <summary>Rarity tiers added to run item drops of chapter <paramref name="chapter"/> (0 before the endless anchor).</summary>
        public int DropRarityShift(int chapter)
        {
            var c = Campaign;
            if (chapter < c.EndlessFromChapter) return 0;
            return Math.Min(c.MaxDropRarity, 1 + (chapter - c.EndlessFromChapter) / Math.Max(1, c.RarityShiftChapters));
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

        // ---- biome order -----------------------------------------------------------------------------------------

        /// <summary>
        /// Index (in CampaignDef.Biomes) of main level n's biome: a deterministic seeded shuffle, the same for every player.
        /// The levels run in blocks of B = Biomes.Count; each block uses every biome once, its last biome is a pure hash of the
        /// block number and the rest is a seeded permutation that never puts the same biome (nor, whenever possible, the same
        /// enemy family) on two consecutive levels, across block edges too. O(B) per level, no history. Level 1 is the first
        /// biome (the tutorial run's ember pool). Stable as long as the biome list and CampaignDef.BiomeOrderSeed stay.
        /// </summary>
        public int CampaignBiomeIndex(int n)
        {
            int b = Campaign.Biomes.Count;
            if (n < 1 || b == 0) return -1;
            if (b == 1) return 0;
            if (b == 2) return (n - 1) % 2; // two biomes can only alternate
            return BiomeBlock((n - 1) / b)[(n - 1) % b];
        }

        /// <summary>Main level n's biome (CampaignDef.Biomes), or null out of range.</summary>
        public CampaignBiomeDef CampaignBiome(int n)
        {
            int i = CampaignBiomeIndex(n);
            return i < 0 ? null : Campaign.Biomes[i];
        }

        /// <summary>Enemy family index (ember 0, glacier 1, static 2, void 3) of a family id; unknown → ember.</summary>
        public static int FamilyIndex(string family)
        {
            int i = Array.IndexOf(FamilyIds, family);
            return i < 0 ? 0 : i;
        }

        private static ulong Mix(ulong seed, ulong a, ulong b)
        {
            ulong z = seed ^ (a * 0x9E3779B97F4A7C15UL) ^ (b * 0xC2B2AE3D27D4EB4FUL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>The biome closing block k (a hash of k alone, so the next block can avoid it without any history).</summary>
        private int BlockLast(int k)
        {
            int b = Campaign.Biomes.Count;
            int last = (int)(Mix(Campaign.BiomeOrderSeed, (ulong)k, 0x1A57UL) % (ulong)b);
            if (k == 0 && last == 0) last = 1; // level 1 opens block 0 with biome 0
            return last;
        }

        private int[] BiomeBlock(int k)
        {
            int b = Campaign.Biomes.Count;
            int last = BlockLast(k), prev = k > 0 ? BlockLast(k - 1) : -1;
            var pref = new List<int>(b - 1);
            for (int i = 0; i < b; i++) if (i != last) pref.Add(i);
            for (int i = pref.Count - 1; i > 0; i--)
            {
                int j = (int)(Mix(Campaign.BiomeOrderSeed, (ulong)k, 0xB10C00UL + (ulong)i) % (ulong)(i + 1));
                int t = pref[i]; pref[i] = pref[j]; pref[j] = t;
            }
            var order = new int[b];
            order[b - 1] = last;
            int first = k == 0 ? 0 : -1;
            // Different families on consecutive levels when the biome list allows it, else just different biomes.
            if (!FillBlock(order, 0, pref, new bool[b], prev, first, true)) FillBlock(order, 0, pref, new bool[b], prev, first, false);
            return order;
        }

        private bool BiomesClash(int a, int c, bool byFamily) =>
            a == c || byFamily && Campaign.Biomes[a].Family == Campaign.Biomes[c].Family;

        /// <summary>Depth-first: the first arrangement in the seeded preference order that satisfies the neighbour rule.</summary>
        private bool FillBlock(int[] order, int pos, List<int> pref, bool[] used, int prev, int first, bool byFamily)
        {
            int b = order.Length;
            if (pos == b - 1) return !BiomesClash(order[pos - 1], order[pos], byFamily);
            foreach (int c in pref)
            {
                if (used[c] || pos == 0 && first >= 0 && c != first) continue;
                int before = pos == 0 ? prev : order[pos - 1];
                if (before >= 0 && BiomesClash(before, c, byFamily)) continue;
                used[c] = true;
                order[pos] = c;
                if (FillBlock(order, pos + 1, pref, used, prev, first, byFamily)) return true;
                used[c] = false;
            }
            return false;
        }

        // ---- generation ----------------------------------------------------------------------------------------

        private static string BossVariantId(int family, int variant) => FamilyBosses[family] + "_v" + variant;

        /// <summary>Boss of main level n: its biome's family boss, from the endless anchor on a rotating variant every N chapters.</summary>
        public string MainLevelBossId(int n)
        {
            var c = Campaign;
            int family = FamilyIndex(CampaignBiome(n)?.Family);
            int chapter = c.ChapterOf(n);
            if (chapter < c.EndlessFromChapter || c.BossVariants <= 0) return FamilyBosses[family];
            int era = (chapter - c.EndlessFromChapter) / Math.Max(1, c.BossVariantEveryChapters);
            return BossVariantId(family, era % Math.Min(c.BossVariants, BossTitles.Length) + 1);
        }

        /// <summary>Boss variants for the endless levels (finite: family × title), registered with the other bosses.</summary>
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

        /// <summary>
        /// Builds main level n and its portal (pure: stores nothing). Its name is its biome's ("55. Crystal Reef" in the UI);
        /// threat, rewards, stamina, board and dice follow the level number (its chapter and chapter step), the enemies its biome.
        /// </summary>
        private LevelDef CampaignBuildMainLevel(int n, out PortalDef portal)
        {
            var c = Campaign;
            int chapter = c.ChapterOf(n), step = c.ChapterStep(n);
            var biome = CampaignBiome(n);
            int family = FamilyIndex(biome.Family);
            string id = MainLevelId(n);
            portal = new PortalDef { Id = id, Name = biome.Name, Kind = PortalKind.Main, Order = n - 1 };
            double scale = MainRewardScale(chapter);
            double familyPct = c.FamilyStrengthPct[family % c.FamilyStrengthPct.Count];
            if (chapter >= c.LateFamilyFromChapter && c.LateFamilyStrengthPct.Count > 0)
                familyPct = ((1 + familyPct / 100.0) * (1 + c.LateFamilyStrengthPct[family % c.LateFamilyStrengthPct.Count] / 100.0) - 1) * 100;
            double threat = c.ThreatAt(n);
            int whole = (int)Math.Floor(threat + 1e-9);
            var l = RunNewLevel(id, biome.Name, id, 1, whole, FamilyPools[family], MainLevelBossId(n), ChapterLootPrefix + (chapter + 1),
                FamilyEnemies[family], FamilyElites[family]);
            l.Number = n;
            l.Biome = biome.Id;
            double basis = ThreatFractionMultiplier(threat) * (1 + familyPct / 100.0);
            double hpPct = (basis * (1 + c.EarlyHpPct(n) / 100.0) - 1) * 100, atkPct = (basis * (1 + c.EarlyAtkPct(n) / 100.0) - 1) * 100;
            if (Math.Abs(hpPct) > 0.01 || Math.Abs(atkPct) > 0.01)
                l.Modifiers = new LevelModifiers { EnemyHpPct = Math.Round(hpPct, 2), EnemyAtkPct = Math.Round(atkPct, 2) };
            l.StaminaCost = Math.Min(c.StaminaMax, c.StaminaBase + chapter);
            // A board segment; two dice walk ~twice as far per roll, so their segment is longer. The boss's awakening meter
            // ends the run (~23 rolls), MaxRolls is only the hard limit.
            l.Dice = step >= 6 ? 2 : 1;
            l.BoardLength = (24 + step + Math.Min(chapter, c.BoardGrowthChapters) * 2) * (l.Dice == 2 ? 3 : 2) / 2;
            l.MaxRolls = c.MaxRolls;
            l.CompletionRewards.Add(Reward.Cur(Currencies.Biomass, (long)Math.Round((120 + 30 * step) * scale)));
            l.CompletionRewards.Add(Reward.Cur(Currencies.Ore, (long)Math.Round((40 + 10 * step) * scale)));
            if (step % 5 == 0) l.CompletionRewards.Add(Reward.Cur(Currencies.PartTokens, 1));
            l.FirstClearRewards.AddRange(c.MilestoneRewards(n));
            l.FirstClearPart = true; // parts v2: a boss first clear gives one random Rare or Epic part
            CampaignIntroduceRunContent(l, n);
            portal.LevelIds.Add(l.Id);
            return l;
        }

        /// <summary>
        /// GDD §16 level unlocks: main level <paramref name="n"/> leaves out the run content that is introduced later (the
        /// signature pick, minibosses, forks, campfires; shops are there from the first run); level 1 is the tutorial run (its own small tile pool, a faster boss). Each element's
        /// feature unlocks when the level before its first board is cleared (ContentDb.Onboarding).
        /// </summary>
        private void CampaignIntroduceRunContent(LevelDef l, int n)
        {
            var c = Campaign;
            if (n == 1)
            {
                l.TilePoolId = c.TutorialPool;
                l.Modifiers.AwakeningPct = c.TutorialAwakeningPct;
            }
            if (n < c.SignatureChoiceFromLevel) l.SignatureChoice = false;
            if (n < c.MinibossesFromLevel) { l.FixedTiles.RemoveAll(ft => ft.TileId == "miniboss"); l.ClockMinibosses = false; }
            if (n < c.ForksFromLevel) l.BranchChance = 0;
            if (n < c.CampfiresFromLevel) l.FixedTiles.RemoveAll(ft => ft.TileId == "campfire");
        }

        /// <summary>Loot table of chapter <paramref name="chapter"/>: currencies × its reward scale, items shifted up in rarity past the anchor.</summary>
        private LootTableDef CampaignChapterLoot(int chapter)
        {
            var t = new LootTableDef { Id = ChapterLootPrefix + (chapter + 1) };
            long s = (long)Math.Round(MainRewardScale(chapter));
            int shift = DropRarityShift(chapter), max = Campaign.MaxDropRarity;
            int R(int rarity) => Math.Min(max, rarity + shift);
            // Biomass comes mainly from portal runs (completion + loot); the city farms are the secondary source.
            t.Entries.Add(new LootEntry(30, RewardKinds.Currency, Currencies.Biomass, 45 * s, 90 * s));
            t.Entries.Add(new LootEntry(20, RewardKinds.Currency, Currencies.Ore, 20 * s, 40 * s));
            t.Entries.Add(new LootEntry(14, RewardKinds.Currency, Currencies.Grain, 20 * s, 40 * s));
            t.Entries.Add(new LootEntry(8, RewardKinds.Currency, Currencies.Fuel, 10 * s, 20 * s));
            t.Entries.Add(new LootEntry(8, RewardKinds.Currency, Currencies.HeroXp, 15 * s, 30 * s));
            t.Entries.Add(new LootEntry(6, RewardKinds.Currency, Currencies.GearCores, 1 * s, 2 * s));
            // parts v2: runs and chests drop no parts (Parts pulls, boss first clears and Dark Side clears are the sources)
            t.Entries.Add(new LootEntry(3, RewardKinds.Rune, "*", 1, 1, R(0)));
            t.Entries.Add(new LootEntry(2, RewardKinds.Currency, Currencies.PartTokens, 1, 1));
            t.Entries.Add(new LootEntry(1, RewardKinds.Currency, Currencies.Diamonds, 5, 15));
            // Runs drop pull resources (rarely), never shards: shards only come from pulls (owner rule).
            t.Entries.Add(new LootEntry(0.85, RewardKinds.Currency, Currencies.BlueprintTokens, 1, 1));
            t.Entries.Add(new LootEntry(0.85, RewardKinds.Currency, Currencies.AlienTokens, 1, 1));
            return t;
        }

        /// <summary>Builds Dark Side level i (pure): procedural along the campaign curve, extreme modifiers, Data reward.</summary>
        private LevelDef CampaignBuildDarkSideLevel(int i)
        {
            double threat = DarkSideThreat(i);
            // Legacy levels used the rounded main level; same here (DarkSideMainLevel rounds).
            int whole = (int)Math.Floor(threat + 1e-9);
            var l = RunNewLevel(DarkSideLevelPrefix + i, "Dark Side " + i, DarkSidePortalId, i, whole, "pool_dark", "boss_void_maw", "loot_dark",
                new[] { "void_spawn", "null_shade", "tunnel_stalker" }, new[] { "elite_void_reaver", "elite_rift_echo", "elite_null_wraith" });
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
            // parts v2: a Dark Side clear has a 5% chance of a random Legendary part
            l.RareDrops.Add(new RareDrop(new Reward(RewardKinds.Part, "*", 1, (int)Rarity.Legendary), Campaign.DarkSideLegendaryPartPct));
            l.CompletionRewards.Add(Reward.Cur(Currencies.Diamonds, 8));
            l.CompletionRewards.Add(Reward.Cur(Currencies.Fuel, 300 * (1 + Math.Min(i, 12) / 4))); // was 1–4 Mount Cores (1 core = 300 Fuel)
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

        private void TrimCache()
        {
            if (_genLevels.Count < GeneratedCacheLevels) return;
            _genLevels.Clear();
            _genPortals.Clear();
            _genLoot.Clear();
        }

        /// <summary>Main level n and its portal from the cache, generated on a miss.</summary>
        private LevelDef GeneratedMain(int n, out PortalDef portal)
        {
            string id = MainLevelId(n);
            lock (_genLock)
            {
                if (_genLevels.TryGetValue(id, out var cached) && _genPortals.TryGetValue(id, out portal)) return cached;
                TrimCache();
                var l = CampaignBuildMainLevel(n, out portal);
                _genLevels[id] = l;
                _genPortals[id] = portal;
                return l;
            }
        }

        private PortalDef GeneratedPortal(string id)
        {
            int n = MainLevelNumberOf(id);
            if (n < 1) return null;
            GeneratedMain(n, out var portal);
            return portal;
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
            int n = MainLevelNumberOf(id);
            return n < 1 ? null : GeneratedMain(n, out _);
        }

        private LootTableDef GeneratedLootTable(string id)
        {
            if (!id.StartsWith(ChapterLootPrefix, StringComparison.Ordinal) || !TryParseIndex(id, ChapterLootPrefix.Length, out int p) || p > MaxGeneratedIndex) return null;
            lock (_genLock)
            {
                if (_genLoot.TryGetValue(id, out var cached)) return cached;
                var t = CampaignChapterLoot(p - 1);
                _genLoot[id] = t;
                return t;
            }
        }
    }
}
