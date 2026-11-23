using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// Rounded shapes generated at runtime (no art assets): an anti-aliased rounded-rect 9-slice sprite and ring
    /// (outline) sprites. One texture serves every radius: <see cref="Image.pixelsPerUnitMultiplier"/> scales the
    /// slice borders so the visible corner radius is exactly the requested one in canvas units.
    /// </summary>
    public static class UiShapes
    {
        /// <summary>Corner radius of the generated textures in texels (= slice border).</summary>
        public const int R = 32;
        private const int Size = 2 * R + 4;

        private static Sprite _fill;
        private static Sprite _circle;
        private static readonly Dictionary<int, Sprite> Rings = new Dictionary<int, Sprite>();

        /// <summary>Filled rounded rectangle (9-sliced).</summary>
        public static Sprite Fill => _fill != null ? _fill : (_fill = Make(0, "rs_round"));

        /// <summary>A filled anti-aliased circle (simple sprite; dots, coins, status pips).</summary>
        public static Sprite Circle
        {
            get
            {
                if (_circle != null) return _circle;
                const int n = 64;
                var tex = NewTexture(n, "rs_circle");
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Mathf.Sqrt((x + 0.5f - n / 2f) * (x + 0.5f - n / 2f) + (y + 0.5f - n / 2f) * (y + 0.5f - n / 2f));
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(n / 2f - d + 0.5f)));
                    }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                _circle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
                _circle.name = "rs_circle";
                return _circle;
            }
        }

        /// <summary>Rounded outline of <paramref name="thicknessTexels"/> texels at radius <see cref="R"/>.</summary>
        public static Sprite Ring(int thicknessTexels)
        {
            thicknessTexels = Mathf.Clamp(thicknessTexels, 1, R);
            if (Rings.TryGetValue(thicknessTexels, out var s) && s != null) return s;
            s = Make(thicknessTexels, "rs_ring" + thicknessTexels);
            Rings[thicknessTexels] = s;
            return s;
        }

        /// <summary>Make <paramref name="img"/> a rounded rectangle with the given corner radius (canvas units).</summary>
        public static Image Round(Image img, float radius)
        {
            if (img == null) return null;
            img.sprite = Fill;
            img.type = Image.Type.Sliced;
            img.fillCenter = true;
            img.pixelsPerUnitMultiplier = R / Mathf.Max(1f, radius);
            return img;
        }

        /// <summary>
        /// A rounded outline drawn over <paramref name="target"/> (a child that ignores layout and never takes raycasts).
        /// </summary>
        public static Image Outline(RectTransform target, Color color, float radius, float thickness = 4, string name = "Border")
        {
            var rt = Ui.Rect(target, name);
            Ui.Stretch(rt);
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            radius = Mathf.Max(1f, radius);
            float mult = R / radius;
            int texels = Mathf.Clamp(Mathf.RoundToInt(thickness * mult), 1, R);
            img.sprite = Ring(texels);
            img.type = Image.Type.Sliced;
            img.fillCenter = true;
            img.pixelsPerUnitMultiplier = mult;
            img.color = color;
            return img;
        }

        private static Texture2D NewTexture(int n, string name)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            return tex;
        }

        /// <summary>Coverage of a rounded square of half-size h and radius r centred in the texture, at texel (x, y).</summary>
        private static float Coverage(float x, float y, float half, float r)
        {
            float qx = Mathf.Abs(x) - (half - r);
            float qy = Mathf.Abs(y) - (half - r);
            float ox = Mathf.Max(qx, 0), oy = Mathf.Max(qy, 0);
            float dist = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
            return Mathf.Clamp01(0.5f - dist);
        }

        private static Sprite Make(int ring, string name)
        {
            var tex = NewTexture(Size, name);
            var px = new Color32[Size * Size];
            float c = Size / 2f;
            float half = Size / 2f - 1; // one texel of transparent margin for clean edges
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float fx = x + 0.5f - c, fy = y + 0.5f - c;
                    float a = Coverage(fx, fy, half, R - 1);
                    if (ring > 0) a = Mathf.Clamp01(a - Coverage(fx, fy, half - ring, Mathf.Max(0.5f, R - 1 - ring)));
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(255 * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var border = new Vector4(R + 1, R + 1, R + 1, R + 1);
            var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, border);
            sprite.name = name;
            return sprite;
        }
    }
}
