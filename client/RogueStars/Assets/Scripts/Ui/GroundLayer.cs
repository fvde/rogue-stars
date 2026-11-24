using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// The board's ground plane as one uGUI graphic: copies of the biome's ground sprite stacked down the rect, ALWAYS upright
    /// (never mirrored). A seamless ground texture meets edge to edge; a full-frame background overlaps the copy above by a
    /// soft band in which the lower copy fades in (a crossfade, so no hard seam). The pattern repeats every <see cref="Pitch"/>,
    /// so the run screen scrolls it 1:1 with the board by its phase (<see cref="Phase"/>).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class GroundLayer : MaskableGraphic
    {
        /// <summary>Share of a full-frame copy's height that crossfades into the next copy.</summary>
        public const float FullFrameBand = 0.2f;

        /// <summary>One drawn piece: a rect (top-left origin, y down, layer units), its sprite UVs and alpha at its top / bottom.</summary>
        public struct Quad
        {
            public float Top, Bottom, Left, Right;
            public float UvTop, UvBottom, UvLeft, UvRight;
            public float AlphaTop, AlphaBottom;
        }

        private Sprite _sprite;
        private float _tileW, _tileH, _band;
        private int _rows, _cols;

        public Sprite Sprite => _sprite;
        /// <summary>Height of one copy (layer units).</summary>
        public float TileHeight => _tileH;
        /// <summary>Crossfade band between copies (0 for a seamless texture).</summary>
        public float Band => _band;
        /// <summary>Vertical repeat of the pattern: one copy minus the band it shares with the next.</summary>
        public float Pitch => _tileH - _band;
        public int Rows => _rows;
        public int Columns => _cols;

        public override Texture mainTexture => _sprite != null ? _sprite.texture : s_WhiteTexture;

        /// <summary>Sets the sprite and the copy size; <paramref name="seamless"/> = no crossfade band.</summary>
        public void Setup(Sprite sprite, float tileW, float tileH, bool seamless, int rows, int cols)
        {
            _sprite = sprite;
            _tileW = tileW;
            _tileH = tileH;
            _band = seamless ? 0 : Mathf.Round(tileH * FullFrameBand);
            _rows = Mathf.Max(1, rows);
            _cols = Mathf.Max(1, cols);
            raycastTarget = false;
            SetAllDirty();
        }

        /// <summary>The pieces drawn (pure; tests): per copy an opaque body and, below the first row, a band fading in from the top.</summary>
        public static List<Quad> BuildQuads(Rect uv, float tileW, float tileH, float band, int rows, int cols)
        {
            var list = new List<Quad>();
            float pitch = tileH - band;
            float bandUv = tileH > 0 ? band / tileH * uv.height : 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    float top = r * pitch, left = c * tileW;
                    bool fade = r > 0 && band > 0;
                    if (fade)
                        list.Add(new Quad
                        {
                            Top = top, Bottom = top + band, Left = left, Right = left + tileW,
                            UvTop = uv.yMax, UvBottom = uv.yMax - bandUv, UvLeft = uv.xMin, UvRight = uv.xMax, AlphaTop = 0, AlphaBottom = 1,
                        });
                    list.Add(new Quad
                    {
                        Top = fade ? top + band : top, Bottom = top + tileH, Left = left, Right = left + tileW,
                        UvTop = fade ? uv.yMax - bandUv : uv.yMax, UvBottom = uv.yMin, UvLeft = uv.xMin, UvRight = uv.xMax, AlphaTop = 1, AlphaBottom = 1,
                    });
                }
            return list;
        }

        /// <summary>The sprite's rect in its texture's UV space.</summary>
        public static Rect SpriteUv(Sprite s)
        {
            if (s == null || s.texture == null) return new Rect(0, 0, 1, 1);
            var tr = s.textureRect;
            float w = s.texture.width, h = s.texture.height;
            return new Rect(tr.x / w, tr.y / h, tr.width / w, tr.height / h);
        }

        /// <summary>The pieces of this layer as set up (tests).</summary>
        public List<Quad> Quads() => BuildQuads(SpriteUv(_sprite), _tileW, _tileH, _band, _rows, _cols);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_sprite == null || _tileH <= 0) return;
            var r = rectTransform.rect;
            float x0 = r.xMin + (r.width - _cols * _tileW) / 2, y0 = r.yMax;
            var col = color;
            foreach (var q in Quads())
            {
                int i = vh.currentVertCount;
                var ct = new Color(col.r, col.g, col.b, col.a * q.AlphaTop);
                var cb = new Color(col.r, col.g, col.b, col.a * q.AlphaBottom);
                vh.AddVert(new Vector3(x0 + q.Left, y0 - q.Bottom), cb, new Vector2(q.UvLeft, q.UvBottom));
                vh.AddVert(new Vector3(x0 + q.Left, y0 - q.Top), ct, new Vector2(q.UvLeft, q.UvTop));
                vh.AddVert(new Vector3(x0 + q.Right, y0 - q.Top), ct, new Vector2(q.UvRight, q.UvTop));
                vh.AddVert(new Vector3(x0 + q.Right, y0 - q.Bottom), cb, new Vector2(q.UvRight, q.UvBottom));
                vh.AddTriangle(i, i + 1, i + 2);
                vh.AddTriangle(i + 2, i + 3, i);
            }
        }

        /// <summary>
        /// Where the layer's top sits (anchored y, layer pinned to the viewport's top) for a camera rise: it moves exactly with
        /// the board (1:1, no parallax) and wraps by whole pitches, so the drawn ground never changes under the tiles.
        /// Always in (0, pitch].
        /// </summary>
        public static float Phase(float cam, float pitch)
        {
            if (pitch <= 0 || float.IsNaN(cam)) return 0;
            float m = cam % pitch;
            if (m < 0) m += pitch;
            return pitch - m;
        }
    }
}
