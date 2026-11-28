using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// The home backdrop's calm treatment (owner: "the home screen backgrounds are a bit too intense"), applied in code to every
    /// hub sky (day, night, event, invasion, older stage skies, the drawn fallback) and only to the sky layer — the planet,
    /// worm, squad and UI stay untouched:
    /// <list type="bullet">
    /// <item>a colour grade on the sky image (<c>RogueStars/UI/CalmBackdrop</c>): less saturation, less contrast around mid
    /// grey, a little darker, a slight navy tint (without the shader: a plain dimming multiply);</item>
    /// <item>a soft navy vignette over the frame: darker toward the edges, plus bands behind the top bar / season bar and
    /// behind the side rails, so the UI reads on any sky.</item>
    /// </list>
    /// One texture read per pixel for the grade, one small procedural texture for the vignette: cheap on mobile.
    /// </summary>
    public static class HubBackdrop
    {
        // ---- tunable strengths (1 / 0 = off) ---------------------------------------------------------------------------
        /// <summary>Saturation kept (1 = unchanged).</summary>
        public const float Saturation = 0.78f;
        /// <summary>Contrast (1 = unchanged) around <see cref="ContrastPivot"/>: a low pivot pulls the highlights down and keeps the darks dark (no grey wash on the night sky).</summary>
        public const float Contrast = 0.85f, ContrastPivot = 0.25f;
        /// <summary>Brightness multiply (1 = unchanged).</summary>
        public const float Brightness = 0.88f;
        /// <summary>Share of the navy grade colour mixed in (0 = none).</summary>
        public const float GradeAmount = 0.06f;
        /// <summary>Darkness of the radial vignette at the frame's corners (0 = none).</summary>
        public const float Vignette = 0.4f;
        /// <summary>Darkness of the bands behind the top edge and the side rails (0 = none).</summary>
        public const float RailShade = 0.18f;
        /// <summary>Width of a side band (share of the frame width) and height of the top band (share of the height).</summary>
        public const float RailBand = 0.2f, TopBand = 0.12f;
        /// <summary>The overlay never darkens more than this.</summary>
        public const float MaxShade = 0.85f;
        /// <summary>The navy of the grade and the vignette (#141030, the hub's night ink).</summary>
        public static readonly Color GradeColor = new Color32(0x14, 0x10, 0x30, 0xFF);
        /// <summary>The dimming multiply used when the grade shader is unavailable.</summary>
        public static readonly Color FallbackDim = new Color(0.78f, 0.78f, 0.82f, 1);

        public const string ShaderPath = "Art/Hub/Shaders/CalmBackdrop";

        /// <summary>The treatment is on (screenshots switch it off for the "before" shots).</summary>
        public static bool Enabled = true;

        private static Material _material;
        private static Sprite _vignette;

        /// <summary>The shared grade material (null when the shader is missing).</summary>
        public static Material Material
        {
            get
            {
                if (_material != null) return _material;
                var shader = Resources.Load<Shader>(ShaderPath);
                if (shader == null || !shader.isSupported) return null;
                _material = new Material(shader) { name = "rs_calm_backdrop", hideFlags = HideFlags.DontSave };
                _material.SetFloat("_Saturation", Saturation);
                _material.SetFloat("_Contrast", Contrast);
                _material.SetFloat("_Pivot", ContrastPivot);
                _material.SetFloat("_Brightness", Brightness);
                _material.SetColor("_Grade", GradeColor);
                _material.SetFloat("_GradeAmount", GradeAmount);
                return _material;
            }
        }

        /// <summary>Grade the sky image (the shader, else a dimming multiply).</summary>
        public static void Grade(Image sky)
        {
            var m = Material;
            if (m != null) { sky.material = m; sky.color = Color.white; }
            else sky.color = FallbackDim;
        }

        /// <summary>The vignette overlay stretched over <paramref name="frame"/> (no raycasts), named "SkyVignette".</summary>
        public static Image Overlay(RectTransform frame)
        {
            var img = Ui.Panel(frame, GradeColor, "SkyVignette");
            img.sprite = VignetteSprite;
            img.raycastTarget = false;
            Ui.Stretch(img.rectTransform);
            return img;
        }

        /// <summary>Darkness (0–1) of the overlay at (u, v) in the frame (v = 0 at the top), for a frame of height/width <paramref name="aspect"/>.</summary>
        public static float Shade(float u, float v, float aspect)
        {
            float dx = u - 0.5f, dy = (v - 0.5f) * aspect;
            float d = Mathf.Sqrt(dx * dx + dy * dy) / (0.5f * aspect); // 1 at the top and bottom centre
            float vig = Mathf.Pow(Mathf.Clamp01((d - 0.55f) / 0.45f), 1.5f) * Vignette;
            float side = Mathf.Clamp01(1 - Mathf.Min(u, 1 - u) / RailBand) * RailShade;
            float top = Mathf.Clamp01(1 - v / TopBand) * RailShade;
            return Mathf.Min(MaxShade, vig + side + top);
        }

        /// <summary>The procedural vignette (white, alpha = <see cref="Shade"/>; drawn for the board's 1080 × 1926 hub frame).</summary>
        public static Sprite VignetteSprite
        {
            get
            {
                if (_vignette != null) return _vignette;
                const int w = 72, h = 128;
                const float aspect = 1926f / 1080f;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                    { name = "rs_hub_vignette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float a = Shade((x + 0.5f) / w, 1 - (y + 0.5f) / h, aspect); // texture rows run bottom-up
                        px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                    }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                _vignette = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
                _vignette.name = "rs_hub_vignette";
                return _vignette;
            }
        }
    }
}
