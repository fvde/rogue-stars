using System.Collections.Generic;
using RogueStars.Client.UiKit;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// Draws a <see cref="MinigameLogic"/>'s shapes into an arena RectTransform (bottom-left origin) with a pool of uGUI
    /// nodes: node i = shape i, so the draw order is the list order. Rounded boxes and rings use the runtime
    /// <see cref="UiShapes"/> sprites, texts the display font. No allocations per frame once the pool is warm.
    /// </summary>
    public sealed class MinigameRenderer
    {
        private sealed class Node
        {
            public RectTransform Rt;
            public Image Img;
            public Text Txt;
            public ShapeKind Kind = (ShapeKind)(-1);
            public float Radius = -1, W = -1;
        }

        private readonly RectTransform _layer;
        private readonly List<Node> _nodes = new List<Node>();
        private readonly List<Shape> _shapes = new List<Shape>();

        public MinigameRenderer(RectTransform arena)
        {
            _layer = Ui.Rect(arena, "Shapes");
            _layer.anchorMin = _layer.anchorMax = Vector2.zero;
            _layer.pivot = Vector2.zero;
            _layer.sizeDelta = new Vector2(MinigameLogic.Width, MinigameLogic.Height);
            _layer.anchoredPosition = Vector2.zero;
        }

        /// <summary>Number of shapes drawn last frame (tests).</summary>
        public int Drawn { get; private set; }

        public void Render(MinigameLogic game)
        {
            game.Draw(_shapes);
            var shake = game.ShakeOffset;
            _layer.anchoredPosition = new Vector2(shake.X, shake.Y);
            for (int i = 0; i < _shapes.Count; i++) Apply(NodeAt(i), _shapes[i]);
            for (int i = _shapes.Count; i < _nodes.Count; i++) if (_nodes[i].Rt.gameObject.activeSelf) _nodes[i].Rt.gameObject.SetActive(false);
            Drawn = _shapes.Count;
        }

        private Node NodeAt(int i)
        {
            while (_nodes.Count <= i)
            {
                var rt = Ui.Rect(_layer, "S");
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                var img = rt.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                var t = Ui.Label(rt, "", 32, Color.white, TextAnchor.MiddleCenter, "T");
                t.font = Ui.DisplayFont;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                Ui.Stretch(t.rectTransform);
                _nodes.Add(new Node { Rt = rt, Img = img, Txt = t });
            }
            return _nodes[i];
        }

        // ---- smooth large shapes: 256-texel circle and ring textures (the UI kit's sprites are made for small radii) ----

        private const int Tex = 256;
        private static Sprite _bigCircle;
        private static readonly Dictionary<int, Sprite> Rings = new Dictionary<int, Sprite>();

        private static Sprite BigCircle => _bigCircle != null ? _bigCircle : (_bigCircle = MakeDisc(0));

        /// <summary>A ring whose line is <paramref name="ratio"/> of its diameter (quantised to 1/256).</summary>
        private static Sprite RingSprite(float ratio)
        {
            int t = Mathf.Clamp(Mathf.RoundToInt(ratio * Tex), 2, Tex / 2);
            if (Rings.TryGetValue(t, out var s) && s != null) return s;
            s = MakeDisc(t);
            Rings[t] = s;
            return s;
        }

        private static Sprite MakeDisc(int ring)
        {
            var tex = new Texture2D(Tex, Tex, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color32[Tex * Tex];
            float c = Tex / 2f, outer = Tex / 2f - 1, inner = outer - ring;
            for (int y = 0; y < Tex; y++)
                for (int x = 0; x < Tex; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    float a = Mathf.Clamp01(outer - d + 0.5f);
                    if (ring > 0) a = Mathf.Min(a, Mathf.Clamp01(d - inner + 0.5f));
                    px[y * Tex + x] = new Color32(255, 255, 255, (byte)(255 * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, Tex, Tex), new Vector2(0.5f, 0.5f), 100);
        }

        private static Color32 ToColor(uint c) => new Color32((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);

        private static void Apply(Node n, Shape s)
        {
            if (!n.Rt.gameObject.activeSelf) n.Rt.gameObject.SetActive(true);
            n.Rt.anchoredPosition = new Vector2(s.X, s.Y);
            n.Rt.localEulerAngles = new Vector3(0, 0, s.Rot);
            var color = ToColor(s.Color);
            if (s.Kind == ShapeKind.Text)
            {
                n.Rt.sizeDelta = new Vector2(10, 10);
                if (n.Kind != ShapeKind.Text) { n.Img.enabled = false; n.Txt.enabled = true; n.Kind = ShapeKind.Text; }
                if (n.Txt.text != s.Text) { n.Txt.text = s.Text; n.Txt.font = Ui.FontFor(s.Text); }
                n.Txt.fontSize = s.FontSize;
                n.Txt.color = color;
                return;
            }
            if (n.Kind == ShapeKind.Text || n.Kind < 0) { n.Img.enabled = true; n.Txt.enabled = false; n.Txt.text = ""; }
            n.Rt.sizeDelta = new Vector2(s.W, s.H);
            n.Img.color = color;
            switch (s.Kind)
            {
                case ShapeKind.Circle:
                    if (n.Kind != ShapeKind.Circle) { n.Img.sprite = s.W > 120 || s.H > 120 ? BigCircle : UiShapes.Circle; n.Img.type = Image.Type.Simple; n.Img.pixelsPerUnitMultiplier = 1; n.W = -1; }
                    else if ((s.W > 120 || s.H > 120) != (n.Img.sprite == BigCircle)) n.Img.sprite = s.W > 120 || s.H > 120 ? BigCircle : UiShapes.Circle;
                    break;
                case ShapeKind.Box:
                    if (n.Kind != ShapeKind.Box || Mathf.Abs(n.Radius - s.Radius) > 0.5f)
                    {
                        if (s.Radius < 1) { n.Img.sprite = null; n.Img.type = Image.Type.Simple; }
                        else UiShapes.Round(n.Img, Mathf.Min(s.Radius, Mathf.Min(s.W, s.H) / 2));
                        n.Radius = s.Radius;
                    }
                    break;
                case ShapeKind.Ring:
                    if (n.Kind != ShapeKind.Ring || Mathf.Abs(n.Radius - s.Radius) > 0.5f || Mathf.Abs(n.W - s.W) > 2)
                    {
                        n.Img.sprite = RingSprite(s.Radius / Mathf.Max(1, s.W));
                        n.Img.type = Image.Type.Simple;
                        n.Img.pixelsPerUnitMultiplier = 1;
                        n.Radius = s.Radius;
                        n.W = s.W;
                    }
                    break;
            }
            n.Kind = s.Kind;
        }
    }
}
