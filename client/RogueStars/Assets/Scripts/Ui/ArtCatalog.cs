using System;
using System.Collections.Generic;
using RogueStars.Rules.Content;
using UnityEngine;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// The look of one board biome: the ground plane behind the path, the folder of per-kind tile sprites and the palette of
    /// the procedurally drawn slabs (used whenever a tile sprite is missing).
    /// </summary>
    public sealed class BiomeArt
    {
        public string Id;
        /// <summary>Resources path of the full-frame, top-down, vertical ground image (null = plain procedural board).</summary>
        public string Background;
        /// <summary>Resources folder of tile sprites: <c>&lt;folder&gt;/&lt;kind&gt;</c> (see <see cref="ArtCatalog.TileKey"/>) or <c>&lt;folder&gt;/slab</c>.</summary>
        public string Tiles;
        /// <summary>Procedural slab: top face, bevel side (the darker "lip" under the face) and outline.</summary>
        public Color SlabFace, SlabSide, SlabEdge;
        /// <summary>Multiplier on the ground image so the slabs and their labels stay readable over it (1 = untouched).</summary>
        public float GroundDim = 0.78f;
    }

    /// <summary>
    /// ONE place that maps biomes (portal families) to art: board background, tile sprites, the hero sprite. Every lookup
    /// falls back to the procedural look when an asset is missing, so new generated art is added by dropping the file under
    /// <c>Assets/Art/Resources/Art/…</c> (imported as a sprite by <c>ArtImportSettings</c>) and, for a new biome, one line in
    /// <see cref="Biomes"/> / <see cref="FamilyBiome"/>.
    /// </summary>
    public static class ArtCatalog
    {
        /// <summary>The hero worm (transparent, square): board token and battle sprite.</summary>
        public const string HeroWorm = "Art/Hero/hero_worm";

        /// <summary>The look of boards without a mapped biome: the old dark cards as chunky slabs, no ground image.</summary>
        public static readonly BiomeArt Default = new BiomeArt
        {
            Id = "default", Background = null, Tiles = null,
            SlabFace = Theme.Panel, SlabSide = Theme.Hex(0x140B0D), SlabEdge = Theme.Hex(0x0E0708), GroundDim = 1,
        };

        /// <summary>Known biomes (add a line per new ground / tile set).</summary>
        public static readonly List<BiomeArt> Biomes = new List<BiomeArt>
        {
            new BiomeArt
            {
                Id = "seabed", Background = "Art/Boards/seabed/background", Tiles = "Art/Boards/seabed/tiles",
                SlabFace = Theme.Hex(0xE9DFC6), SlabSide = Theme.Hex(0x9C8A66), SlabEdge = Theme.Hex(0x2A1C14), GroundDim = 0.8f,
            },
            new BiomeArt
            {
                Id = "lava", Background = "Art/Boards/lava/background", Tiles = "Art/Boards/lava/tiles",
                SlabFace = Theme.Hex(0x4A4048), SlabSide = Theme.Hex(0x221B22), SlabEdge = Theme.Hex(0x120C10), GroundDim = 0.82f,
            },
        };

        /// <summary>Portal family (the level's tile pool without "pool_": ember, glacier, static, void, burrow…) → biome id.</summary>
        public static readonly Dictionary<string, string> FamilyBiome = new Dictionary<string, string>
        {
            { "ember", "lava" },
            { "glacier", "seabed" },
        };

        private static Func<string, Sprite> _loader = path => Resources.Load<Sprite>(path);
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>How a Resources path becomes a sprite (tests swap it; the cache is cleared on every change).</summary>
        public static Func<string, Sprite> Loader
        {
            get => _loader;
            set { _loader = value ?? (path => Resources.Load<Sprite>(path)); Cache.Clear(); }
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

        /// <summary>A level's portal family: its tile pool without the "pool_" prefix (null for hand-made levels without one).</summary>
        public static string FamilyOf(LevelDef level)
        {
            var pool = level?.TilePoolId;
            if (string.IsNullOrEmpty(pool)) return null;
            return pool.StartsWith("pool_") ? pool.Substring(5) : pool;
        }

        /// <summary>The biome of a portal family, else <see cref="Default"/>.</summary>
        public static BiomeArt BiomeFor(string family)
        {
            if (family != null && FamilyBiome.TryGetValue(family, out var id))
                foreach (var b in Biomes) if (b.Id == id) return b;
            return Default;
        }

        public static BiomeArt BiomeFor(LevelDef level) => BiomeFor(FamilyOf(level));

        /// <summary>The ground image of a biome, or null (draw the plain procedural board).</summary>
        public static Sprite Background(BiomeArt biome) => biome == null ? null : Load(biome.Background);

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

        /// <summary>The hero worm sprite, or null (the drawn pill worm).</summary>
        public static Sprite Hero() => Load(HeroWorm);
    }
}
