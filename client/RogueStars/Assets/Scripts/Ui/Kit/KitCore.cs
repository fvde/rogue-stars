using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// Sprite slots of the UI kit. Every kit piece looks decent procedurally and, when a sprite named
    /// <c>Resources/Art/Ui/Kit/&lt;piece&gt;</c> exists, uses it instead (9-sliced when the sprite has borders). Kit sprites are
    /// authored white/grey and tintable: code tints them with the piece's colour, so one sprite serves every colour.
    /// Piece names, sizes and borders: docs/design/ui-kit.md.
    /// </summary>
    public static class KitArt
    {
        public const string Folder = "Art/Ui/Kit/";
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>The kit sprite of <paramref name="piece"/>, or null while no art exists.</summary>
        public static Sprite Get(string piece)
        {
            if (string.IsNullOrEmpty(piece)) return null;
            if (Cache.TryGetValue(piece, out var s) && (s != null || !Application.isPlaying)) return s;
            s = Resources.Load<Sprite>(Folder + piece);
            Cache[piece] = s;
            return s;
        }

        public static void ClearCache() => Cache.Clear();

        /// <summary>
        /// Skin <paramref name="img"/> with the kit sprite <paramref name="piece"/> when it exists (true), else make it the
        /// procedural rounded rectangle of <paramref name="radius"/> (false).
        /// </summary>
        public static bool Skin(Image img, string piece, float radius)
        {
            var s = Get(piece);
            if (s == null) { UiShapes.Round(img, radius); return false; }
            img.sprite = s;
            img.type = s.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            img.fillCenter = true;
            img.pixelsPerUnitMultiplier = 1;
            return true;
        }
    }

    /// <summary>Vertical colour gradient multiplied onto a graphic (the soft top-to-bottom shading of kit surfaces).</summary>
    public sealed class KitGradient : BaseMeshEffect
    {
        public Color Top = Color.white;
        public Color Bottom = new Color(0.8f, 0.8f, 0.8f, 1);

        public void Set(Color top, Color bottom)
        {
            Top = top; Bottom = bottom;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var r = ((RectTransform)transform).rect;
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float t = r.height > 0.01f ? Mathf.InverseLerp(r.yMin, r.yMax, v.position.y) : 1f;
                var g = Color.Lerp(Bottom, Top, t);
                var c = (Color)v.color;
                v.color = new Color(c.r * g.r, c.g * g.g, c.b * g.b, c.a * g.a);
                vh.SetUIVertex(v, i);
            }
        }
    }

    /// <summary>
    /// Keeps a one-line label on one line: shrinks the font (down to <see cref="MinSize"/>) until the text fits its rect's
    /// width. Fixes labels that wrapped mid-word in narrow chips and tabs ("Diamond/s").
    /// </summary>
    public sealed class KitFit : UIBehaviour
    {
        public Text Text;
        public int MaxSize = 32, MinSize = 14;
        private int _lastKey = -1;

        protected override void OnRectTransformDimensionsChange() => Apply();
        protected override void OnEnable() => Apply();

        public void Apply()
        {
            if (Text == null) return;
            float w = ((RectTransform)Text.transform).rect.width;
            if (w <= 1) return;
            int key = Mathf.RoundToInt(w) * 1009 + (Text.text ?? "").GetHashCode();
            if (key == _lastKey) return;
            _lastKey = key;
            Text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var gen = Text.cachedTextGeneratorForLayout;
            for (int size = MaxSize; size >= MinSize; size--)
            {
                var settings = Text.GetGenerationSettings(Vector2.zero);
                settings.fontSize = size;
                settings.resizeTextForBestFit = false;
                settings.horizontalOverflow = HorizontalWrapMode.Overflow;
                float pw = gen.GetPreferredWidth(Text.text, settings) / Text.pixelsPerUnit;
                if (pw <= w || size == MinSize) { Text.fontSize = size; return; }
            }
        }

        /// <summary>Attach to <paramref name="text"/> (its current size is the maximum).</summary>
        public static KitFit On(Text text, int minSize = 14)
        {
            var fit = text.GetComponent<KitFit>();
            if (fit == null) fit = text.gameObject.AddComponent<KitFit>();
            fit.Text = text; fit.MaxSize = text.fontSize; fit.MinSize = Mathf.Min(minSize, text.fontSize);
            return fit;
        }
    }
}
