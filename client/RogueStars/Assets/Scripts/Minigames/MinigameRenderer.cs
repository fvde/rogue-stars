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
                    if (n.Kind != ShapeKind.Circle) { n.Img.sprite = UiShapes.Circle; n.Img.type = Image.Type.Simple; n.Img.pixelsPerUnitMultiplier = 1; }
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
                        float radius = Mathf.Max(1, s.W / 2);
                        float mult = UiShapes.R / radius;
                        n.Img.sprite = UiShapes.Ring(Mathf.Clamp(Mathf.RoundToInt(s.Radius * mult), 1, UiShapes.R));
                        n.Img.type = Image.Type.Sliced;
                        n.Img.pixelsPerUnitMultiplier = mult;
                        n.Radius = s.Radius;
                        n.W = s.W;
                    }
                    break;
            }
            n.Kind = s.Kind;
        }
    }
}
