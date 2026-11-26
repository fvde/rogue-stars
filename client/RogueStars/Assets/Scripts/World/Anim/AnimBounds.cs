using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace RogueStars.Client.World.Anim
{
    /// <summary>
    /// The alpha bounds of creature sprites (pixels of the sprite rect, origin bottom-left): where the spine starts and how tall
    /// it is. Imported art is not CPU-readable (memory), so the bounds come from a table (<c>Resources/Art/anim_bounds.json</c>,
    /// written by the editor tool <c>Tools/Art/Build Anim Bounds</c> and re-written when creature art changes); a readable
    /// texture (code-drawn sprites) is measured directly; anything else gets its whole rect.
    /// </summary>
    public static class AnimBounds
    {
        public const string TablePath = "Art/anim_bounds";
        /// <summary>A pixel counts as part of the creature above this alpha.</summary>
        public const byte Threshold = 16;

        private static Dictionary<string, int[]> _table;
        private static readonly Dictionary<Sprite, RectInt> Cache = new Dictionary<Sprite, RectInt>();

        public static void ClearCache() { Cache.Clear(); _table = null; }

        /// <summary>Entries of the table (tests).</summary>
        public static IReadOnlyDictionary<string, int[]> Table { get { Load(); return _table; } }

        private static void Load()
        {
            if (_table != null) return;
            _table = new Dictionary<string, int[]>();
            var ta = Resources.Load<TextAsset>(TablePath);
            if (ta == null) return;
            try { _table = JsonConvert.DeserializeObject<Dictionary<string, int[]>>(ta.text) ?? _table; }
            catch (JsonException) { /* a broken table: fall back to rects */ }
        }

        /// <summary>The alpha bounds of a sprite in its rect's pixels.</summary>
        public static RectInt Of(Sprite s)
        {
            if (s == null) return default;
            if (Cache.TryGetValue(s, out var r)) return r;
            var tr = s.rect;
            int w = (int)tr.width, h = (int)tr.height;
            Load();
            if (_table.TryGetValue(s.name, out var e) && e.Length >= 6 && e[4] == w && e[5] == h) r = new RectInt(e[0], e[1], e[2] - e[0] + 1, e[3] - e[1] + 1);
            else if (s.texture != null && s.texture.isReadable) r = Measure(s);
            else r = new RectInt(0, 0, w, h);
            Cache[s] = r;
            return r;
        }

        /// <summary>The bounds of a sprite's readable pixels above <see cref="Threshold"/> alpha (whole rect when empty).</summary>
        public static RectInt Measure(Sprite s)
        {
            var tr = s.rect;
            int x0 = (int)tr.x, y0 = (int)tr.y, w = (int)tr.width, h = (int)tr.height;
            var px = s.texture.GetPixels32();
            int tw = s.texture.width;
            return Bounds(i => px[i].a, x0, y0, w, h, tw);
        }

        /// <summary>Bounds over a Color32 image (row-major, bottom row first) - the editor tool and tests use it.</summary>
        public static RectInt Bounds(Color32[] px, int w, int h) => Bounds(i => px[i].a, 0, 0, w, h, w);

        private static RectInt Bounds(System.Func<int, byte> alpha, int x0, int y0, int w, int h, int stride)
        {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (alpha((y0 + y) * stride + x0 + x) > Threshold)
                    {
                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
            if (maxX < 0) return new RectInt(0, 0, w, h);
            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
    }
}
