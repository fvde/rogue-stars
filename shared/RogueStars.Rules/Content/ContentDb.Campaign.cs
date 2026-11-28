using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

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

        // Boss variants of the endless levels: a title, and the feed / relief / tribute tile of which campaign boss the variant
        // brings (offsets in the campaign boss list: a variant borrows another boss's tiles). Escort and board hazard rotate too.
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

        // ---- biomes (docs/design/biomes: data.py BIOMES / WEATHER / UNLOCKS) -----------------------------------------------

        /// <summary>
        /// The 17 campaign biomes in unlock order with their rosters, biome reward and synergy weather, and the level-unlocked
        /// signatures. Live biomes are Ready (ground art in); the 9 planned ones wait for theirs (CampaignBiomeDef.Ready). Rosters
        /// list every def; ArtPending defs stay out of the live pools (BiomeRoster) and stand-ins fill the gap meanwhile.
        /// </summary>
        private void CampaignBuildBiomes()
        {
            var k = Campaign;
            k.Biomes.Clear();
            CampaignBiomeDef B(string id, string name, int from, bool ready, string resource, SkillFamily boost, SkillFamily weak, double weakPct,
                string[] enemies, string[] elites, string[] bosses, string[] standInEnemies = null, string[] standInElites = null, string standInBoss = null)
            {
                var b = new CampaignBiomeDef(id, name) { From = from, Ready = ready, Resource = resource, Boost = boost, Weak = weak, WeakPct = weakPct, StandInBoss = standInBoss };
                b.Enemies.AddRange(enemies); b.Elites.AddRange(elites); b.Bosses.AddRange(bosses);
                if (standInEnemies != null) b.StandInEnemies.AddRange(standInEnemies);
                if (standInElites != null) b.StandInElites.AddRange(standInElites);
                k.Biomes.Add(b);
                return b;
            }
            const SkillFamily Fire = SkillFamily.Fire, Ice = SkillFamily.Ice, Shock = SkillFamily.Lightning, Rad = SkillFamily.Poison,
                Bleed = SkillFamily.Bleed, Combo = SkillFamily.Combo, Counter = SkillFamily.Counter, Alien = SkillFamily.AlienSynergy,
                Time = SkillFamily.Time, General = SkillFamily.None;
            // start biomes: each boosts one start synergy
            B("lava", "Lava Fields", 1, true, Currencies.Ore, Fire, Ice, 50,
                new[] { "ember_imp", "magma_slug", "cinder_hound", "obsidian_crab" }, new[] { "elite_lava_brute", "elite_cinder_alpha" },
                new[] { "boss_magma_tyrant", "boss_elder_magma" });
            B("toxic_swamp", "Toxic Swamp", 1, true, Currencies.Fuel, Rad, Combo, 25,
                new[] { "void_spawn", "burrow_grub", "bog_toad", "mire_gator" }, new[] { "elite_burrow_horror", "elite_grub_mother" },
                new[] { "boss_gloop_matriarch" }, new[] { "tunnel_stalker" });
            B("shipwreck", "Shipwreck", 1, true, Currencies.GearCores, Bleed, Shock, 25,
                new[] { "tunnel_stalker", "null_shade", "rust_leech", "scrap_crab" }, new[] { "elite_void_reaver", "elite_null_wraith" },
                new[] { "boss_rust_queen", "boss_void_maw" }, new[] { "void_spawn" });
            B("gas_giant", "Gas Giant", 1, true, Currencies.Fuel, Combo, Counter, 25,
                new[] { "ash_wraith", "ember_imp", "gas_bloater", "flare_ray", "rig_raider" }, new[] { "elite_ash_matriarch", "elite_bloat_captain" },
                new[] { "boss_flare_medusa" }, new[] { "cinder_hound" }, new[] { "elite_cinder_alpha" }, "boss_elder_magma");
            // one new biome per signature unlock (the new biome boosts the new synergy)
            B("cloud_islands", "Cloud Isles", 10, true, Currencies.Grain, Ice, Rad, 25,
                new[] { "snow_lurker", "glacier_golem", "hail_sprite", "sky_ray" }, new[] { "elite_lurker_alpha", "elite_glacier_warden" },
                new[] { "boss_hailwing_roc" }, new[] { "frost_mite", "ice_crawler" }, null, "boss_frost_leviathan");
            B("space_station", "Space Station", 20, true, Currencies.Data, Shock, Alien, 25,
                new[] { "spark_drone", "volt_beetle", "security_bot", "lab_rat" }, new[] { "elite_storm_caller", "elite_volt_monarch" },
                new[] { "boss_storm_hive" }, new[] { "static_eel" });
            B("seabed", "Seabed", 35, true, Currencies.Biomass, Counter, Fire, 50,
                new[] { "frost_mite", "ice_crawler", "lantern_angler", "kelp_strangler" }, new[] { "elite_frost_knight", "elite_trench_crusher" },
                new[] { "boss_frost_leviathan" }, new[] { "glacier_golem" }, new[] { "elite_glacier_warden" });
            B("alien_jungle", "Alien Jungle", 60, true, Currencies.Grain, Alien, General, 25,
                new[] { "thunder_moth", "static_eel", "snap_orchid", "spark_monkey" }, new[] { "elite_eel_tyrant", "elite_canopy_stalker" },
                new[] { "boss_volt_orchid" }, new[] { "volt_beetle", "spark_drone" }, new[] { "elite_volt_monarch", "elite_storm_caller" }, "boss_storm_hive");
            B("clockwork_city", "Clockwork City", 90, false, Currencies.GearCores, Time, Bleed, 50,
                new[] { "tick_mite", "spark_drone", "cog_soldier", "steam_hound", "spring_hopper" }, new[] { "elite_clockwork_knight", "elite_rift_echo" },
                new[] { "boss_chrono_wyrm" });
            // planned biomes past the signatures (biomes only)
            B("desert_ruins", "Desert Ruins", 125, false, Currencies.Ore, General, Ice, 50,
                new[] { "cinder_hound", "ash_wraith", "sand_scarab", "dune_serpent", "wrapped_husk" }, new[] { "elite_cinder_alpha", "elite_obelisk_guardian" },
                new[] { "boss_scarab_sun_king" });
            B("ice", "Ice Shelf", 160, false, Currencies.Fuel, Ice, Combo, 25,
                new[] { "glacier_golem", "ice_crawler", "frost_mite", "tusk_seal", "shard_penguin" }, new[] { "elite_frost_knight", "elite_walrus_warlord" },
                new[] { "boss_tusk_colossus", "boss_frost_leviathan" });
            B("neon_city", "Neon City", 200, false, Currencies.Data, Shock, Time, 25,
                new[] { "spark_drone", "static_eel", "neon_ganger", "holo_hound" }, new[] { "elite_storm_caller", "elite_chrome_enforcer" },
                new[] { "boss_hollow_cantor" });
            B("mushroom_forest", "Mushroom Forest", 245, false, Currencies.Biomass, Rad, Shock, 25,
                new[] { "burrow_grub", "void_spawn", "puffball", "myco_beetle", "shroom_walker" }, new[] { "elite_grub_mother", "elite_mycelium_knight" },
                new[] { "boss_spore_monarch" });
            B("crystal_cave", "Crystal Cave", 295, false, Currencies.Ore, Counter, Bleed, 25,
                new[] { "tick_mite", "crystal_bat", "geode_crawler", "volt_beetle" }, new[] { "elite_rift_echo", "elite_shard_golem" },
                new[] { "boss_prism_tyrant" });
            B("coral_reef", "Coral Reef", 350, false, Currencies.Biomass, Bleed, Rad, 25,
                new[] { "ice_crawler", "puffer_mine", "spine_urchin", "reef_shark" }, new[] { "elite_glacier_warden", "elite_reef_guardian" },
                new[] { "boss_tide_hydra" });
            B("candy_planet", "Candy Planet", 420, false, Currencies.HeroXp, Combo, Counter, 25,
                new[] { "gummy_worm", "jelly_blob", "candy_wasp", "gingerbread_brute" }, new[] { "elite_licorice_lasher", "elite_gumball_golem" },
                new[] { "boss_bonbon_behemoth" });
            B("frozen_future", "Frozen Future", 500, false, Currencies.HeroXp, Time, Fire, 25,
                new[] { "tick_mite", "frost_mite", "stasis_drone", "chrome_sentry" }, new[] { "elite_rift_echo", "elite_cryo_captain" },
                new[] { "boss_stasis_warden" });

            k.SignatureUnlocks.Clear();
            k.SignatureUnlocks.Add(new SignatureUnlockDef(Ice, 10, Features.SignatureIce));
            k.SignatureUnlocks.Add(new SignatureUnlockDef(Shock, 20, Features.SignatureLightning));
            k.SignatureUnlocks.Add(new SignatureUnlockDef(Counter, 35, Features.SignatureCounter));
            k.SignatureUnlocks.Add(new SignatureUnlockDef(Alien, 60, Features.SignatureAlien));
            k.SignatureUnlocks.Add(new SignatureUnlockDef(Time, 90, Features.SignatureTime));
        }

        /// <summary>A biome's live roster: its own art-ready defs, topped up with art-ready stand-ins while its own art is pending.</summary>
        public sealed class BiomeRosterView
        {
            public List<string> Enemies = new List<string>(), Elites = new List<string>(), Bosses = new List<string>();
        }

        /// <summary>Whether an enemy def exists and its art is in (EnemyDef.ArtPending).</summary>
        public bool EnemyLive(string id) => id != null && Enemies.TryGetValue(id, out var e) && !e.ArtPending;

        /// <summary>
        /// The live roster of a biome: its own enemies / elites / bosses minus ArtPending ones, then stand-ins until it has
        /// CampaignDef.MinBiomeEnemies / MinBiomeElites (and a boss). Flip EnemyDef.ArtPending when a sprite lands and the
        /// biome's own def takes the stand-in's place.
        /// </summary>
        public BiomeRosterView BiomeRoster(CampaignBiomeDef b)
        {
            var r = new BiomeRosterView();
            if (b == null) return r;
            void Fill(List<string> into, List<string> own, List<string> standIns, int min)
            {
                foreach (var id in own) if (EnemyLive(id) && !into.Contains(id)) into.Add(id);
                foreach (var id in standIns) if (into.Count < min && EnemyLive(id) && !into.Contains(id)) into.Add(id);
            }
            Fill(r.Enemies, b.Enemies, b.StandInEnemies, Campaign.MinBiomeEnemies);
            Fill(r.Elites, b.Elites, b.StandInElites, Campaign.MinBiomeElites);
            foreach (var id in b.Bosses) if (EnemyLive(id)) r.Bosses.Add(id);
            if (r.Bosses.Count == 0 && b.StandInBoss != null) r.Bosses.Add(b.StandInBoss);
            return r;
        }

        /// <summary>A campaign biome by id (null if unknown).</summary>
        public CampaignBiomeDef CampaignBiomeById(string id)
        {
            if (id == null) return null;
            foreach (var b in Campaign.Biomes) if (b.Id == id) return b;
            return null;
        }

        /// <summary>
        /// Synergy weather of a level (main campaign levels: Normal and Overcharged; Dark Side, Burrow, invasions and the Arena have
        /// none): its biome boosts one synergy (+WeatherBoostPct %) and weakens another (−WeakPct %, the player only).
        /// </summary>
        public BattleWeather Weather(LevelDef level)
        {
            var b = CampaignBiomeById(level?.Biome);
            if (b == null || level.Number < Campaign.WeatherFromLevel) return null;
            return new BattleWeather { Boost = b.Boost, BoostPct = Campaign.WeatherBoostPct, Weak = b.Weak, WeakPct = b.WeakPct };
        }

        // ---- biome order -----------------------------------------------------------------------------------------

        /// <summary>
        /// The rotation: the ready biomes in unlock order (Ready list order) and the level each joins. The start biomes (From ≤ 1)
        /// are there from level 1; every later ready biome takes the next unlock slot (the From values of the non-start biomes in
        /// list order), so a planned biome without art leaves its slot to the next ready one. Starts strictly increase.
        /// </summary>
        private void Rotation(out List<int> biomes, out List<int> starts, out int starters)
        {
            var all = Campaign.Biomes;
            biomes = new List<int>(all.Count);
            starts = new List<int>(all.Count);
            var slots = new List<int>();
            for (int i = 0; i < all.Count; i++) if (all[i].From > 1) slots.Add(all[i].From);
            starters = 0;
            for (int i = 0; i < all.Count; i++)
                if (all[i].Ready && all[i].From <= 1) { biomes.Add(i); starters++; }
            if (starters == 0) for (int i = 0; i < all.Count && starters == 0; i++) if (all[i].Ready) { biomes.Add(i); starters = 1; }
            int slot = 0, last = 1;
            for (int i = 0; i < all.Count; i++)
            {
                if (!all[i].Ready || all[i].From <= 1 || biomes.Contains(i) || slot >= slots.Count) continue;
                int at = Math.Max(slots[slot++], last + 1);
                biomes.Add(i); starts.Add(at); last = at;
            }
        }

        /// <summary>
        /// Index (in CampaignDef.Biomes) of main level n's biome: a deterministic seeded shuffle, the same for every player.
        /// Epoch 0 (levels 1 … first unlock − 1) rotates the start biomes; every unlock slot opens an epoch whose first level is the new
        /// biome (its first visit) and whose later levels shuffle all biomes unlocked so far in blocks of the rotation size. Each block
        /// uses every biome once, its last biome is a hash of the epoch and block, the rest a seeded permutation that never puts the
        /// same biome on two consecutive levels, across block and epoch edges too. O(B) per level, no history. Level 1 is the first
        /// biome (Lava Fields, the tutorial run).
        /// </summary>
        public int CampaignBiomeIndex(int n)
        {
            if (n < 1) return -1;
            Rotation(out var rot, out var starts, out int s);
            if (rot.Count == 0) return -1;
            int e = EpochOf(starts, n);
            if (e > 0 && n == starts[e - 1]) return rot[s + e - 1];
            int size = s + e, first = e == 0 ? 1 : starts[e - 1] + 1;
            int pos = n - first;
            return rot[BiomeBlock(e, pos / size, size)[pos % size]];
        }

        /// <summary>Epoch of level n: 0 before the first unlock slot, else the number of slots reached.</summary>
        private static int EpochOf(List<int> starts, int n)
        {
            int e = 0;
            while (e < starts.Count && starts[e] <= n) e++;
            return e;
        }

        /// <summary>Main level n's biome (CampaignDef.Biomes), or null out of range.</summary>
        public CampaignBiomeDef CampaignBiome(int n)
        {
            int i = CampaignBiomeIndex(n);
            return i < 0 ? null : Campaign.Biomes[i];
        }

        /// <summary>Whether main level n is its biome's first visit (an unlock slot: the client's "New biome" banner).</summary>
        public bool IsBiomeDebut(int n)
        {
            if (n < 2) return false;
            Rotation(out _, out var starts, out _);
            return starts.Contains(n);
        }

        /// <summary>The level at which a biome joins the rotation (1 for a start biome), or −1 while it is not in it (no art).</summary>
        public int BiomeDebutLevel(string biomeId)
        {
            Rotation(out var rot, out var starts, out int s);
            for (int j = 0; j < rot.Count; j++)
                if (Campaign.Biomes[rot[j]].Id == biomeId) return j < s ? 1 : starts[j - s];
            return -1;
        }

        /// <summary>How many earlier main levels (before n) had n's biome: its 1st, 2nd … visit (boss variants alternate on it).</summary>
        public int BiomeVisitIndex(int n)
        {
            Rotation(out var rot, out var starts, out int s);
            int target = CampaignBiomeIndex(n);
            if (target < 0) return 0;
            int e = EpochOf(starts, n), count = 0;
            for (int j = 0; j <= e; j++)
            {
                int end = j == e ? n - 1 : starts[j] - 1;
                if (j > 0 && starts[j - 1] <= end && rot[s + j - 1] == target) count++;
                int size = s + j, local = -1;
                for (int q = 0; q < size; q++) if (rot[q] == target) local = q;
                if (local < 0) continue;
                int first = j == 0 ? 1 : starts[j - 1] + 1;
                if (end < first) continue;
                int len = end - first + 1, full = len / size, rem = len % size;
                count += full;
                if (rem > 0) { var blk = BiomeBlock(j, full, size); for (int q = 0; q < rem; q++) if (blk[q] == local) count++; }
            }
            return count;
        }

        private static ulong Mix(ulong seed, ulong a, ulong b)
        {
            ulong z = seed ^ (a * 0x9E3779B97F4A7C15UL) ^ (b * 0xC2B2AE3D27D4EB4FUL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>The local biome closing block k of epoch e (a hash of e and k alone, so the next block can avoid it without history).</summary>
        private int BlockLast(int e, int k, int size)
        {
            int last = (int)(Mix(Campaign.BiomeOrderSeed, (ulong)k, 0x1A57UL + ((ulong)e << 20)) % (ulong)size);
            if (e == 0 && k == 0 && last == 0 && size > 1) last = 1; // level 1 opens epoch 0 with biome 0
            return last;
        }

        /// <summary>Block k of epoch e: local indices 0 … size − 1 (positions in the rotation; the epoch's new biome is size − 1).</summary>
        private int[] BiomeBlock(int e, int k, int size)
        {
            if (size == 1) return new[] { 0 };
            int last = BlockLast(e, k, size), prev = k > 0 ? BlockLast(e, k - 1, size) : e > 0 ? size - 1 : -1;
            var pref = new List<int>(size - 1);
            for (int i = 0; i < size; i++) if (i != last) pref.Add(i);
            for (int i = pref.Count - 1; i > 0; i--)
            {
                int j = (int)(Mix(Campaign.BiomeOrderSeed, (ulong)k, 0xB10C00UL + (ulong)i + ((ulong)e << 20)) % (ulong)(i + 1));
                int t = pref[i]; pref[i] = pref[j]; pref[j] = t;
            }
            var order = new int[size];
            order[size - 1] = last;
            int first = e == 0 && k == 0 ? 0 : -1;
            if (!FillBlock(order, 0, pref, new bool[size], prev, first)) FillBlock(order, 0, pref, new bool[size], -1, first);
            return order;
        }

        /// <summary>Depth-first: the first arrangement in the seeded preference order that never repeats a biome on consecutive levels.</summary>
        private static bool FillBlock(int[] order, int pos, List<int> pref, bool[] used, int prev, int first)
        {
            int b = order.Length;
            if (pos == b - 1) return pos == 0 ? order[pos] != prev : order[pos - 1] != order[pos];
            foreach (int c in pref)
            {
                if (used[c] || pos == 0 && first >= 0 && c != first) continue;
                int before = pos == 0 ? prev : order[pos - 1];
                if (before >= 0 && before == c) continue;
                used[c] = true;
                order[pos] = c;
                if (FillBlock(order, pos + 1, pref, used, prev, first)) return true;
                used[c] = false;
            }
            return false;
        }

        // ---- generation ----------------------------------------------------------------------------------------

        /// <summary>Every boss a campaign biome may field (own bosses and stand-ins), in biome order: the endless variants' list.</summary>
        private List<string> CampaignBossIds()
        {
            var list = new List<string>();
            foreach (var b in Campaign.Biomes)
            {
                foreach (var id in b.Bosses) if (!list.Contains(id)) list.Add(id);
                if (b.StandInBoss != null && !list.Contains(b.StandInBoss)) list.Add(b.StandInBoss);
            }
            return list;
        }

        private static string BossVariantId(string bossId, int variant) => bossId + "_v" + variant;

        /// <summary>
        /// Boss of main level n: its biome's boss (biomes with two alternate per visit: 1st, 3rd … visit A, 2nd, 4th … B), from the
        /// endless anchor on a rotating variant every N chapters.
        /// </summary>
        public string MainLevelBossId(int n)
        {
            var c = Campaign;
            var roster = BiomeRoster(CampaignBiome(n));
            if (roster.Bosses.Count == 0) return null;
            string boss = roster.Bosses.Count == 1 ? roster.Bosses[0] : roster.Bosses[BiomeVisitIndex(n) % roster.Bosses.Count];
            int chapter = c.ChapterOf(n);
            if (chapter < c.EndlessFromChapter || c.BossVariants <= 0) return boss;
            int era = (chapter - c.EndlessFromChapter) / Math.Max(1, c.BossVariantEveryChapters);
            return BossVariantId(boss, era % Math.Min(c.BossVariants, BossTitles.Length) + 1);
        }

        /// <summary>Feed / relief / tribute tile names of each campaign boss (the same numbers for every boss).</summary>
        private static readonly Dictionary<string, string[]> BossTileNames = new Dictionary<string, string[]>
        {
            { "boss_magma_tyrant", new[] { "Magma Vent", "Cooling Pool", "Tyrant Shrine" } },
            { "boss_elder_magma", new[] { "Magma Heart", "Ash Basin", "Elder Shrine" } },
            { "boss_gloop_matriarch", new[] { "Gloop Pool", "Clean Spring", "Matriarch Tribute" } },
            { "boss_rust_queen", new[] { "Salvage Pile", "Scuttled Hull", "Queen's Tribute" } },
            { "boss_void_maw", new[] { "Void Rift", "Seal Stone", "Void Tribute" } },
            { "boss_flare_medusa", new[] { "Flare Vent", "Coolant Tank", "Medusa Tribute" } },
            { "boss_hailwing_roc", new[] { "Roc Perch", "Calm Cloud", "Roc Tribute" } },
            { "boss_frost_leviathan", new[] { "Frozen Heart", "Thaw Beacon", "Leviathan Tribute" } },
            { "boss_storm_hive", new[] { "Hive Node", "Grounding Rod", "Hive Tribute" } },
            { "boss_volt_orchid", new[] { "Pollen Bloom", "Weed Patch", "Orchid Tribute" } },
            { "boss_chrono_wyrm", new[] { "Clock Gear", "Stopped Clock", "Wyrm Tribute" } },
            { "boss_scarab_sun_king", new[] { "Sun Altar", "Shade Stone", "Sun-King Tribute" } },
            { "boss_tusk_colossus", new[] { "Ice Tusk", "Warm Vent", "Colossus Tribute" } },
            { "boss_hollow_cantor", new[] { "Echo Speaker", "Mute Switch", "Cantor Tribute" } },
            { "boss_spore_monarch", new[] { "Spore Pod", "Dry Moss", "Monarch Tribute" } },
            { "boss_prism_tyrant", new[] { "Prism Shard", "Dark Crystal", "Prism Tribute" } },
            { "boss_tide_hydra", new[] { "Hydra Spring", "Low Tide", "Hydra Tribute" } },
            { "boss_bonbon_behemoth", new[] { "Candy Pile", "Bitter Root", "Behemoth Tribute" } },
            { "boss_stasis_warden", new[] { "Stasis Field", "Thaw Switch", "Warden Tribute" } },
        };

        /// <summary>Feed / relief / tribute tile ids of a campaign boss ("bt_&lt;boss&gt;_feed" …; registered in RunBuildBosses).</summary>
        private static string[] BossTiles(string bossId)
        {
            string b = bossId.StartsWith("boss_", StringComparison.Ordinal) ? bossId.Substring(5) : bossId;
            return new[] { "bt_" + b + "_feed", "bt_" + b + "_relief", "bt_" + b + "_tribute" };
        }

        /// <summary>
        /// The campaign bosses' board kits (awakening actions, their own 3 boss tiles, an escort from their home biome) and the
        /// endless variants (finite: boss × title), which borrow another campaign boss's tiles.
        /// </summary>
        private void CampaignBuildBosses()
        {
            var bosses = CampaignBossIds();
            string Escort(string bossId, int v)
            {
                foreach (var b in Campaign.Biomes)
                    if (b.Bosses.Contains(bossId) || b.StandInBoss == bossId)
                    {
                        var r = BiomeRoster(b);
                        if (r.Enemies.Count > 0) return r.Enemies[v % r.Enemies.Count];
                    }
                return "ember_imp";
            }
            foreach (var id in bosses)
            {
                var names = BossTileNames.TryGetValue(id, out var n) ? n : new[] { "Boss Altar", "Calm Ground", "Boss Tribute" };
                var tiles = BossTiles(id);
                RunBossTile(tiles[0], names[0], 10, 4, 0);
                RunBossTile(tiles[1], names[1], -10, 0, 0);
                RunBossTile(tiles[2], names[2], 5, 6, 30);
                RunBoss(id, Enemies[id].Name, id, tiles, Escort(id, 0));
            }
            int variants = Math.Min(Campaign.BossVariants, BossTitles.Length);
            for (int f = 0; f < bosses.Count; f++)
                for (int v = 1; v <= variants; v++)
                {
                    var combo = BossTileCombos[(v - 1) % BossTileCombos.Length];
                    var tiles = new[]
                    {
                        BossTiles(bosses[(f + combo[0]) % bosses.Count])[0], BossTiles(bosses[(f + combo[1]) % bosses.Count])[1],
                        BossTiles(bosses[(f + combo[2]) % bosses.Count])[2],
                    };
                    RunBoss(BossVariantId(bosses[f], v), BossTitles[v - 1] + " " + Enemies[bosses[f]].Name, bosses[f], tiles,
                        Escort(bosses[f], v), BossHazards[v % BossHazards.Length]);
                }
        }

        /// <summary>
        /// Builds main level n and its portal (pure: stores nothing). Its name is its biome's ("55. Gas Giant" in the UI);
        /// threat, rewards, stamina, board and dice follow the level number (its chapter and chapter step), the enemies its biome.
        /// Difficulty comes only from the level scaling (no biome is harder or easier).
        /// </summary>
        private LevelDef CampaignBuildMainLevel(int n, out PortalDef portal)
        {
            var c = Campaign;
            int chapter = c.ChapterOf(n), step = c.ChapterStep(n);
            var biome = CampaignBiome(n);
            var roster = BiomeRoster(biome);
            string id = MainLevelId(n);
            portal = new PortalDef { Id = id, Name = biome.Name, Kind = PortalKind.Main, Order = n - 1 };
            double scale = MainRewardScale(chapter);
            double threat = c.ThreatAt(n);
            int whole = (int)Math.Floor(threat + 1e-9);
            var l = RunNewLevel(id, biome.Name, id, 1, whole, c.MainPool, MainLevelBossId(n), ChapterLootPrefix + (chapter + 1),
                roster.Enemies.ToArray(), roster.Elites.ToArray());
            l.Number = n;
            l.Biome = biome.Id;
            l.BiomeDebut = IsBiomeDebut(n);
            double basis = ThreatFractionMultiplier(threat);
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
            CampaignBiomeReward(l, biome, chapter);
            l.FirstClearRewards.AddRange(c.MilestoneRewards(n));
            l.FirstClearPart = true; // parts v2: a boss first clear gives one random Rare or Epic part
            CampaignIntroduceRunContent(l, n);
            portal.LevelIds.Add(l.Id);
            return l;
        }

        /// <summary>
        /// Biome reward (docs/design/biomes, "Biome reward (×2)"): the biome's resource pays ×BiomeRewardMult on a won run
        /// (RunSystem.Finish). A resource without a completion line (Grain, Fuel, Hero XP, Gear Cores: loot rolls only; Data: none
        /// on main levels) adds one guaranteed roll of it, so the ×2 is never a ×2 of nothing.
        /// </summary>
        private void CampaignBiomeReward(LevelDef l, CampaignBiomeDef biome, int chapter)
        {
            l.BiomeResource = biome.Resource;
            if (biome.Resource == null || l.CompletionRewards.Exists(r => r.Kind == RewardKinds.Currency && r.Id == biome.Resource)) return;
            if (biome.Resource == Currencies.Data)
            {
                long s = (long)Math.Round(MainRewardScale(chapter));
                l.BiomeRollMin = Campaign.BiomeDataRollMin * s;
                l.BiomeRollMax = Campaign.BiomeDataRollMax * s;
                return;
            }
            foreach (var e in CampaignChapterLoot(chapter).Entries)
                if (e.Kind == RewardKinds.Currency && e.Id == biome.Resource) { l.BiomeRollMin = e.Min; l.BiomeRollMax = e.Max; return; }
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
