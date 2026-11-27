using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using UnityEngine;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// The look of one board biome: the ground plane behind the path, the folder of per-kind tile sprites, the decor props
    /// scattered beside the path and the palette of the procedurally drawn slabs (used whenever a tile sprite is missing).
    /// </summary>
    public sealed class BiomeArt
    {
        public string Id;
        /// <summary>Resources path of the full-frame, top-down, vertical ground image (null = plain procedural board).</summary>
        public string Background;
        /// <summary>
        /// Resources path of a seamless (tileable) ground texture, preferred over <see cref="Background"/> when present: its
        /// copies meet edge to edge. A full-frame background instead crossfades a soft band between upright copies.
        /// </summary>
        public string Ground;
        /// <summary>Resources folder of tile sprites: <c>&lt;folder&gt;/&lt;kind&gt;</c> (see <see cref="ArtCatalog.TileKey"/>) or <c>&lt;folder&gt;/slab</c>.</summary>
        public string Tiles;
        /// <summary>Resources folder of decor props scattered beside the path (<c>Art/Decor/&lt;biome&gt;/*.png</c>, any number).</summary>
        public string Decor;
        /// <summary>Procedural slab: top face, bevel side (the darker "lip" under the face) and outline.</summary>
        public Color SlabFace, SlabSide, SlabEdge;
        /// <summary>Multiplier on the ground image so the slabs and their labels stay readable over it (1 = untouched).</summary>
        public float GroundDim = 0.78f;
        /// <summary>Colour of the drawn placeholder rocks that stand in for decor props while the biome has no prop art.</summary>
        public Color Rock = new Color(0.42f, 0.38f, 0.36f, 1);
    }

    /// <summary>The ground drawn behind the path: its sprite and whether it tiles seamlessly (else crossfaded upright copies).</summary>
    public struct GroundArt
    {
        public Sprite Sprite;
        public bool Seamless;
    }

    /// <summary>
    /// ONE place that maps the game to art: biomes (portal families) → board ground, tile sprites and decor props; enemy and
    /// alien sprites by def id; the hero sprite. Every lookup falls back (family sprite → procedural look) when an asset is
    /// missing, so new generated art is added by dropping the file under <c>Assets/Art/Resources/Art/…</c> (or running
    /// <c>Tools/Art/Import Generated Art</c>) and, for a new biome, one line in <see cref="Biomes"/> / <see cref="FamilyBiome"/>.
    /// </summary>
    public static class ArtCatalog
    {
        /// <summary>
        /// The hero worm's body (transparent, square, legless, walking left): board token, battle, home planet, rest scene.
        /// Imported as standing art (ArtImportSettings: its pivot is the ground contact). Icons use <see cref="WormIcon"/>.
        /// </summary>
        public const string HeroWorm = "Art/Hero/hero_worm";
        /// <summary>Enemy sprites: <c>Art/Enemies/&lt;enemy def id&gt;</c> (transparent, square).</summary>
        public const string EnemyFolder = "Art/Enemies";
        /// <summary>Alien sprites: <c>Art/Aliens/&lt;alien type id&gt;</c> (transparent, square).</summary>
        public const string AlienFolder = "Art/Aliens";
        /// <summary>Battle-scoped sidekicks (drones) by sidekick id (sk_drone…); without art HudArt.Drone is drawn.</summary>
        public const string SidekickFolder = "Art/Sidekicks";
        /// <summary>A boss / elite drawn with a regular enemy's sprite of its family is drawn this much bigger.</summary>
        public const float BossFamilyScale = 1.3f, EliteFamilyScale = 1.15f;

        /// <summary>The look of boards without a mapped biome: the old dark cards as chunky slabs, no ground image.</summary>
        public static readonly BiomeArt Default = new BiomeArt
        {
            Id = "default", Background = null, Tiles = null,
            SlabFace = Theme.Panel, SlabSide = Theme.Hex(0x140B0D), SlabEdge = Theme.Hex(0x0E0708), GroundDim = 1,
        };

        /// <summary>
        /// Known biomes (add a line per new ground / tile set). Ground images and tile sprites were generated for different
        /// biomes, so a biome may borrow the tile set of another one whose slabs suit its ground (<c>tileSet</c>).
        /// </summary>
        public static readonly List<BiomeArt> Biomes = new List<BiomeArt>
        {
            Make("seabed", "seabed", 0xE9DFC6, 0x9C8A66, 0x2A1C14, 0.8f, 0x8C8170),
            Make("lava", null, 0x4A4048, 0x221B22, 0x120C10, 0.82f, 0x3E3438),
            Make("space_station", "neon_city", 0x3C4452, 0x1E232C, 0x0C0F14, 0.8f, 0x5C6474),
            Make("shipwreck", "crystal_cave", 0x5A4A44, 0x2E2420, 0x140E0C, 0.8f, 0x6A5648),
            Make("prehistoric", "desert_ruins", 0xD8C08A, 0x8C7448, 0x2A2014, 0.84f, 0x77705C),
            Make("ash_wastes", null, 0x4C4646, 0x262222, 0x100C0C, 0.8f, 0x58524F),
            Make("alien_jungle", "mushroom_forest", 0x6A8A4C, 0x3A5028, 0x16200E, 0.84f, 0x5E6652),
            Make("toxic_swamp", null, 0x4E5A3A, 0x28301C, 0x10140A, 0.8f, 0x505842),
            Make("gas_giant", null, 0x4A4A5E, 0x24243A, 0x0E0E18, 0.8f, 0x5C5C72),
        };

        /// <summary>A biome line: its own ground / decor folders, the tile set of <paramref name="tileSet"/> (null = its own tile folder).</summary>
        public static BiomeArt Make(string id, string tileSet, int face, int side, int edge, float dim, int rock) => new BiomeArt
        {
            Id = id, Background = "Art/Boards/" + id + "/background", Ground = "Art/Boards/" + id + "/ground",
            Tiles = "Art/Boards/" + (tileSet ?? id) + "/tiles", Decor = "Art/Decor/" + id,
            SlabFace = Theme.Hex(face), SlabSide = Theme.Hex(side), SlabEdge = Theme.Hex(edge), GroundDim = dim, Rock = Theme.Hex(rock),
        };

        /// <summary>
        /// Portal family (the level's tile pool without "pool_": ember, glacier, static, void, burrow, dark, standard) → biome
        /// id, by theme: ember → lava, glacier → seabed, static (storms, sparks) → the space station deck, void → the wreck
        /// field, burrow → the prehistoric clearing, the Dark Side → the ash wastes, the standard pool → the alien jungle.
        /// </summary>
        public static readonly Dictionary<string, string> FamilyBiome = new Dictionary<string, string>
        {
            { "ember", "lava" },
            { "glacier", "seabed" },
            { "static", "space_station" },
            { "void", "shipwreck" },
            { "burrow", "prehistoric" },
            { "dark", "ash_wastes" },
            { "standard", "alien_jungle" },
        };

        private static Func<string, Sprite> _loader = DefaultLoader;
        private static Func<string, Sprite[]> _folderLoader = DefaultFolderLoader;
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, List<Sprite>> FolderCache = new Dictionary<string, List<Sprite>>();

        private static Sprite DefaultLoader(string path) => Resources.Load<Sprite>(path);
        private static Sprite[] DefaultFolderLoader(string path) => Resources.LoadAll<Sprite>(path);

        /// <summary>How a Resources path becomes a sprite (tests swap it; the caches are cleared on every change).</summary>
        public static Func<string, Sprite> Loader
        {
            get => _loader;
            set { _loader = value ?? DefaultLoader; Cache.Clear(); FolderCache.Clear(); }
        }

        /// <summary>How a Resources folder becomes its sprites (decor props; tests swap it; caches cleared on every change).</summary>
        public static Func<string, Sprite[]> FolderLoader
        {
            get => _folderLoader;
            set { _folderLoader = value ?? DefaultFolderLoader; Cache.Clear(); FolderCache.Clear(); }
        }

        /// <summary>A sprite by Resources path, or null when the asset is missing (cached, misses too).</summary>
        public static Sprite Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Cache.TryGetValue(path, out var s)) return s;
            s = _loader(path);
            Cache[path] = s;
            return s;
        }

        /// <summary>Every sprite in a Resources folder, sorted by name (cached; empty when the folder is missing).</summary>
        public static IReadOnlyList<Sprite> LoadFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return Array.Empty<Sprite>();
            if (FolderCache.TryGetValue(path, out var list)) return list;
            list = new List<Sprite>();
            var found = _folderLoader(path);
            if (found != null) foreach (var s in found) if (s != null) list.Add(s);
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            FolderCache[path] = list;
            return list;
        }

        /// <summary>
        /// A level's portal family: its tile pool without the "pool_" prefix and without a variant suffix
        /// ("pool_ember_tutorial" → "ember"; null for hand-made levels without one).
        /// </summary>
        public static string FamilyOf(LevelDef level)
        {
            var pool = level?.TilePoolId;
            if (string.IsNullOrEmpty(pool)) return null;
            var family = pool.StartsWith("pool_") ? pool.Substring(5) : pool;
            int cut = family.IndexOf('_');
            return cut > 0 ? family.Substring(0, cut) : family;
        }

        /// <summary>Feature intro art (unlock screens, "?" replays): <c>Art/Intro/&lt;key&gt;</c>; IntroArt draws a fallback.</summary>
        public const string IntroFolder = "Art/Intro";
        public static Sprite Intro(string key) => string.IsNullOrEmpty(key) ? null : Load(IntroFolder + "/" + key);

        /// <summary>The biome of a portal family, else <see cref="Default"/>.</summary>
        public static BiomeArt BiomeFor(string family)
        {
            if (family != null && FamilyBiome.TryGetValue(family, out var id))
                foreach (var b in Biomes) if (b.Id == id) return b;
            return Default;
        }

        public static BiomeArt BiomeFor(LevelDef level) => BiomeFor(FamilyOf(level));

        /// <summary>The full-frame ground image of a biome, or null.</summary>
        public static Sprite Background(BiomeArt biome) => biome == null ? null : Load(biome.Background);

        /// <summary>
        /// The ground drawn behind the path: the biome's seamless ground texture when there is one, else its full-frame
        /// background (crossfaded copies), else no sprite (the plain procedural board).
        /// </summary>
        public static GroundArt GroundOf(BiomeArt biome)
        {
            if (biome == null) return default;
            var g = Load(biome.Ground);
            if (g != null) return new GroundArt { Sprite = g, Seamless = true };
            return new GroundArt { Sprite = Background(biome), Seamless = false };
        }

        /// <summary>File name (without extension) of a tile kind's sprite in a biome's tile folder: "enemy", "shop", "boss_gate"…</summary>
        public static string TileKey(TileCategory c)
        {
            switch (c)
            {
                case TileCategory.StatBuff: return "stat_buff";
                case TileCategory.StatDebuff: return "stat_debuff";
                case TileCategory.BossTile: return "boss_tile";
                case TileCategory.BossGate: return "boss_gate";
                default: return c.ToString().ToLowerInvariant();
            }
        }

        /// <summary>
        /// The slab sprite of a tile kind in a biome: <c>tiles/&lt;kind&gt;</c>, else the biome's generic <c>tiles/slab</c>, else null
        /// (the slab is drawn procedurally in the biome's palette). The tile kind's colour marker and label are drawn on top either way.
        /// </summary>
        public static Sprite TileSprite(BiomeArt biome, TileCategory c)
        {
            if (biome == null || string.IsNullOrEmpty(biome.Tiles)) return null;
            return Load(biome.Tiles + "/" + TileKey(c)) ?? Load(biome.Tiles + "/slab");
        }

        /// <summary>The decor props of a biome (sorted by name; empty = none generated yet: placeholder rocks or nothing).</summary>
        public static IReadOnlyList<Sprite> DecorProps(BiomeArt biome) => biome == null ? Array.Empty<Sprite>() : LoadFolder(biome.Decor);

        /// <summary>The hero worm sprite, or null (the drawn pill worm).</summary>
        public static Sprite Hero() => Load(HeroWorm);

        // ---- vertical slice (iso board, HUD, draft cards, battle) -------------------------------------------------------
        // Generated art of the tools/art/packages/slice-*.json packages, imported by ArtImporter from the owner's picks
        // (tools/art/slice-picks.json) under these Resources keys. Every caller falls back to a code-drawn placeholder
        // (World/IsoArt, Screens/SliceKit) while a key has no file.

        /// <summary>Resources root of the slice art.</summary>
        public const string SliceRoot = "Art/Slice";
        /// <summary>Tile markers standing on tiles: <c>Art/Slice/Markers/&lt;id&gt;</c> (enemy, elite, miniboss, omen, event, mystery, chest, rest, shop, minigame, portal, reward; the boss gate shows the run's boss sprite, IsoArt.BossMarker).</summary>
        public const string SliceMarkers = SliceRoot + "/Markers";
        /// <summary>Set pieces inside loops: <c>Art/Slice/SetPieces/&lt;id&gt;</c> (crashed_saucer, portal_well, crystal_spire, giant_mushroom, ruined_arch, burrow_mound, time_clock, alien_tree).</summary>
        public const string SliceSetPieces = SliceRoot + "/SetPieces";
        /// <summary>Scatter props along the path: <c>Art/Slice/Scatter/&lt;id&gt;</c> (rock_small, rock_pile, bush, flowers, fern, pebble_glow, stump, crystal_small, mushrooms, barrel, antenna, bones).</summary>
        public const string SliceScatterFolder = SliceRoot + "/Scatter";
        /// <summary>HUD icons: <c>Art/Slice/Hud/&lt;id&gt;</c> (dice, pedestal, heart, sword, shield, coin, exp, cog, skull, book, speed, reroll, boss_rail_top).</summary>
        public const string SliceHudFolder = SliceRoot + "/Hud";
        /// <summary>Full-frame textures: <c>Art/Slice/Textures/&lt;id&gt;</c> (tile_top_moss, tile_side_rock; ground_meadow, backdrop_deep optional).</summary>
        public const string SliceTextures = SliceRoot + "/Textures";
        /// <summary>Battle arenas (3/4 top-down, full-frame): <c>Art/Slice/Battle/&lt;id&gt;</c> (battle_meadow, battle_meadow_ruins).</summary>
        public const string SliceBattleFolder = SliceRoot + "/Battle";
        /// <summary>Battle mounts the worm rides by mount id: <c>Art/Slice/Mounts/&lt;mount id&gt;</c> (else <c>Art/Slice/Mounts/mount</c>).</summary>
        public const string SliceMounts = SliceRoot + "/Mounts";
        /// <summary>Skill icons by run skill id: <c>Art/Skills/&lt;skill id&gt;</c>.</summary>
        public const string SkillFolder = "Art/Skills";

        public static Sprite SliceMarker(string id) => string.IsNullOrEmpty(id) ? null : Load(SliceMarkers + "/" + id);
        public static Sprite SliceSetPiece(string id) => string.IsNullOrEmpty(id) ? null : Load(SliceSetPieces + "/" + id);

        /// <summary>
        /// Variant <paramref name="variant"/> of a set piece: 0 = the main sprite <c>&lt;id&gt;</c>, k = the alternate
        /// <c>&lt;id&gt;_&lt;k + 1&gt;</c> (<c>_2</c>, <c>_3</c> …); null when that file is missing.
        /// </summary>
        public static Sprite SliceSetPiece(string id, int variant) =>
            variant <= 0 ? SliceSetPiece(id) : string.IsNullOrEmpty(id) ? null : Load(SliceSetPieces + "/" + id + "_" + (variant + 1));

        /// <summary>How many sprites a set piece has: the main one plus its consecutive alternates (0 when it has no art).</summary>
        public static int SliceSetPieceVariants(string id)
        {
            int n = 0;
            while (n < 16 && SliceSetPiece(id, n) != null) n++;
            return n;
        }
        public static Sprite SliceScatter(string id) => string.IsNullOrEmpty(id) ? null : Load(SliceScatterFolder + "/" + id);
        public static Sprite SliceHud(string id) => string.IsNullOrEmpty(id) ? null : Load(SliceHudFolder + "/" + id);
        public static Sprite SliceTexture(string id) => string.IsNullOrEmpty(id) ? null : Load(SliceTextures + "/" + id);
        public static Sprite SliceBattle(string id) => string.IsNullOrEmpty(id) ? null : Load(SliceBattleFolder + "/" + id);
        /// <summary>Id of the shared side-view battle background (<c>Art/Slice/Battle/battle_meadow</c>), used for every biome for now.</summary>
        public const string BattleDefault = "battle_meadow";
        /// <summary>The side-view battle background of a biome: <c>battle_&lt;biome&gt;</c> when imported, else the shared default; null = the drawn arena.</summary>
        public static Sprite BattleBackground(string biomeId) =>
            (string.IsNullOrEmpty(biomeId) ? null : SliceBattle("battle_" + biomeId)) ?? SliceBattle(BattleDefault);
        /// <summary>The battle mount's art (by mount id, else the generic one), or null (the drawn hover saucer).</summary>
        public static Sprite SliceMount(string mountId) => (string.IsNullOrEmpty(mountId) ? null : Load(SliceMounts + "/" + mountId)) ?? Load(SliceMounts + "/mount");
        public static Sprite SkillIcon(string skillId) => string.IsNullOrEmpty(skillId) ? null : Load(SkillFolder + "/" + skillId);

        // ---- screens pass (rest, events, chest, shop, home, run result) ------------------------------------------------
        // Art queue packages run-characters, run-props, run-icons, hub-planet, hub-scene, hub-icons and the result package.
        // Every key falls back to a code-drawn placeholder (World/ScreenArt) while it has no file.

        /// <summary>Run NPCs (busts): <c>Art/Run/Characters/&lt;id&gt;</c> (shopkeeper, cursed_vendor, black_marketeer, camp_cook).</summary>
        public const string RunCharacters = "Art/Run/Characters";
        /// <summary>Run scene props: <c>Art/Run/Props/&lt;id&gt;</c> (chest_common, chest_mythic, chest_signature, abandoned_cart, campfire_big, event_shrine, event_wreck).</summary>
        public const string RunProps = "Art/Run/Props";
        /// <summary>Run reward / option icons: <c>Art/Run/Icons/&lt;id&gt;</c> (coins_small, coins_big, heal, max_hp, skill_book, upgrade, reroll, curse, blessing, sale_tag).</summary>
        public const string RunIcons = "Art/Run/Icons";
        /// <summary>The home planet by growth stage: <c>Art/Hub/Planet/planet_stage1</c> … <c>planet_stage5</c>.</summary>
        public const string HubPlanet = "Art/Hub/Planet";
        /// <summary>
        /// Home backdrops: <c>Art/Hub/Scene/&lt;id&gt;</c> — <c>sky_day</c>, <c>sky_night</c>, <c>sky_event</c>, <c>sky_invasion</c>
        /// (full-frame space, HubScreen.SkyState picks one); the older <c>sky</c> / <c>sky_stage1…5</c> are fallbacks.
        /// </summary>
        public const string HubScene = "Art/Hub/Scene";
        /// <summary>Home icons: <c>Art/Hub/Icons/&lt;id&gt;</c> (tab_worm, tab_city, tab_planet, tab_signals, tab_arena, shop, offer, pass, mail, more, quests, observatory, invasion, event, map, chapter_rewards, patrol, challenges, settings, profile_level = the level badge star, play_button = the START rocket; imported 2026-11-25: tab_worm, shop, pass, event, patrol (quick_harvest), chapter_rewards (quick_chest), profile_level, play_button).</summary>
        public const string HubIcons = "Art/Hub/Icons";
        /// <summary>Run result art: <c>Art/Result/&lt;id&gt;</c> (crown = victory crest, crest_broken = defeat crest, banner = the hanging pennant).</summary>
        public const string ResultArt = "Art/Result";

        /// <summary>
        /// Passes page ticket illustrations: <c>Art/Shop/Passes/&lt;product id&gt;</c> (monthly_pass, perm_no_ads,
        /// perm_daily_diamonds, gift_diamond_week, gift_token_week): one flat ticket object per pass, transparent, about
        /// 3:2 landscape. Without art the page draws a tilted kit plate with the pass's icon (PassesScreen.Ticket).
        /// </summary>
        public const string PassTickets = "Art/Shop/Passes";

        public static Sprite PassTicket(string productId) => string.IsNullOrEmpty(productId) ? null : Load(PassTickets + "/" + productId);

        public static Sprite RunCharacter(string id) => string.IsNullOrEmpty(id) ? null : Load(RunCharacters + "/" + id);
        public static Sprite RunProp(string id) => string.IsNullOrEmpty(id) ? null : Load(RunProps + "/" + id);
        /// <summary>
        /// A run reward / option icon. Max HP has no icon of its own (owner, 2026-11-25): it shows the HUD heart
        /// (<c>Art/Slice/Hud/heart</c>) unless a <c>max_hp</c> file is ever imported.
        /// </summary>
        public static Sprite RunIcon(string id) => string.IsNullOrEmpty(id) ? null
            : Load(RunIcons + "/" + id) ?? (id == MaxHpIcon ? SliceHud("heart") : null);
        public const string MaxHpIcon = "max_hp";
        public static Sprite Planet(int stage) => Load(HubPlanet + "/planet_stage" + Mathf.Clamp(stage, 1, 4)); // owner: the planet stays at the 4 approved stages
        public const string SkyDay = "day", SkyNight = "night", SkyEvent = "event", SkyInvasion = "invasion";
        public static readonly string[] SkyStates = { SkyDay, SkyNight, SkyEvent, SkyInvasion };

        /// <summary>Iso island ground chunks: <c>Art/Slice/Ground/&lt;id&gt;</c> (ground_corner, ground_large, ground_long, ground_small; alternates <c>&lt;id&gt;_2</c>). Imported for board decoration, not drawn yet.</summary>
        public const string SliceGroundFolder = SliceRoot + "/Ground";
        public static readonly string[] SliceGroundIds = { "ground_corner", "ground_large", "ground_long", "ground_small" };
        public static Sprite SliceGround(string id) => string.IsNullOrEmpty(id) ? null : Load(SliceGroundFolder + "/" + id);

        /// <summary>
        /// The worm as an icon (the Worm tab, the profile avatar): <c>Art/Hub/Icons/tab_worm</c>, else the hero body sprite.
        /// The body (<see cref="Hero"/>) is the legless walking worm drawn on the board, in battle and on the home planet.
        /// </summary>
        public static Sprite WormIcon() => HubIcon("tab_worm") ?? Hero();

        public static Sprite HubSceneArt(string id) => string.IsNullOrEmpty(id) ? null : Load(HubScene + "/" + id);
        public static Sprite HubIcon(string id) => string.IsNullOrEmpty(id) ? null : Load(HubIcons + "/" + id);
        public static Sprite Result(string id) => string.IsNullOrEmpty(id) ? null : Load(ResultArt + "/" + id);

        /// <summary>An alien's sprite by alien type id (alien_blorp…), or null (the drawn critter in its synergy's colour, HudArt.AlienCritter).</summary>
        public static Sprite Alien(string alienTypeId) => string.IsNullOrEmpty(alienTypeId) ? null : Load(AlienKey(alienTypeId));

        /// <summary>The art key (Resources path) of an alien species' sprite: <c>Art/Aliens/&lt;id&gt;</c>.</summary>
        public static string AlienKey(string alienTypeId) => AlienFolder + "/" + alienTypeId;

        /// <summary>
        /// Every alien species' art key (aliens pass: 20 species; the art generator's list). All 20 have art since the 2026-11-26 import (aliens3); a missing file
        /// still draws the critter of the synergy's colour.
        /// </summary>
        public static readonly string[] AlienKeys =
        {
            "Art/Aliens/alien_blorp", "Art/Aliens/alien_mossling", "Art/Aliens/alien_pebblehorn", "Art/Aliens/alien_zib", "Art/Aliens/alien_glimmerfly",
            "Art/Aliens/alien_shiverpup", "Art/Aliens/alien_emberkit", "Art/Aliens/alien_frostmite", "Art/Aliens/alien_sporeling", "Art/Aliens/alien_clackclaw",
            "Art/Aliens/alien_whirrbit", "Art/Aliens/alien_arcwhisk", "Art/Aliens/alien_voidmaw", "Art/Aliens/alien_thornqueen", "Art/Aliens/alien_gravitoad",
            "Art/Aliens/alien_chronoslug", "Art/Aliens/alien_solarch", "Art/Aliens/alien_tidecaller", "Art/Aliens/alien_nebulisk", "Art/Aliens/alien_swarmheart",
        };

        /// <summary>Battle status icons: <c>Art/Status/&lt;key&gt;</c> (<see cref="StatusKey"/>; plus <c>guard</c> for the guard chip).</summary>
        public const string StatusFolder = "Art/Status";

        /// <summary>The art key of a status: its lower-case name; Poison is drawn as Radiation and Burn as Heat (one look each, as StatusArt draws them).</summary>
        public static string StatusKey(RogueStars.Rules.Core.StatusId s)
        {
            switch (s)
            {
                case RogueStars.Rules.Core.StatusId.None: return null;
                case RogueStars.Rules.Core.StatusId.Poison: return "radiation";
                case RogueStars.Rules.Core.StatusId.Burn: return "heat";
                default: return s.ToString().ToLowerInvariant();
            }
        }

        /// <summary>A status icon by key (bleed, heat, radiation, guard…), or null (the drawn glyph, World.StatusArt).</summary>
        public static Sprite StatusIcon(string key) => string.IsNullOrEmpty(key) ? null : Load(StatusFolder + "/" + key);

        /// <summary>A summoned sidekick's sprite (sk_drone…), or null (the drawn drone, HudArt.Drone).</summary>
        public static Sprite Sidekick(string sidekickId) => string.IsNullOrEmpty(sidekickId) ? null : Load(SidekickFolder + "/" + sidekickId);

        /// <summary>An enemy's own sprite by enemy def id (ember_imp…), or null.</summary>
        public static Sprite Enemy(string enemyId) => string.IsNullOrEmpty(enemyId) ? null : Load(EnemyFolder + "/" + enemyId);

        /// <summary>
        /// An enemy's sprite: its own, else (bosses, elites, summons without art) the first sprite of its family
        /// (<see cref="EnemyFamily"/>) with <paramref name="family"/> = true (drawn scaled up), else null (the old lettered box).
        /// </summary>
        public static Sprite EnemyOrFamily(string enemyId, ContentDb content, out bool family)
        {
            family = false;
            var own = Enemy(enemyId);
            if (own != null || content == null || string.IsNullOrEmpty(enemyId)) return own;
            foreach (var id in EnemyFamily(enemyId, content))
            {
                var s = Enemy(id);
                if (s != null) { family = true; return s; }
            }
            return null;
        }

        /// <summary>
        /// The regular enemies an enemy belongs with (content-derived, deterministic): a boss's escorts, then the enemy and
        /// elite pools of the levels it rules; an elite's (or any enemy's) level pools. The enemy itself is left out.
        /// </summary>
        public static List<string> EnemyFamily(string enemyId, ContentDb content)
        {
            var list = new List<string>();
            if (content == null || string.IsNullOrEmpty(enemyId)) return list;
            void Add(string id) { if (!string.IsNullOrEmpty(id) && id != enemyId && !list.Contains(id)) list.Add(id); }
            var bosses = new List<BossDef>(content.RunBosses.Values);
            bosses.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            var bossIds = new HashSet<string>();
            foreach (var b in bosses)
                if (b.EnemyId == enemyId) { bossIds.Add(b.Id); foreach (var e in b.Escorts) Add(e); }
            var levels = new List<LevelDef>(content.RunLevels.Values);
            levels.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (var l in levels)
            {
                bool rules = l.BossId != null && bossIds.Contains(l.BossId);
                if (!rules && !l.ElitePool.Contains(enemyId) && !l.EnemyPool.Contains(enemyId)) continue;
                foreach (var e in l.EnemyPool) Add(e);
                foreach (var e in l.ElitePool) Add(e);
            }
            return list;
        }
    }
}
