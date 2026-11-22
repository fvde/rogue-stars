using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// Code-built uGUI toolkit. Everything is a RectTransform under a layout group; widgets size themselves
    /// with LayoutElements. Placeholder visuals only (flat coloured rects + the built-in legacy font).
    /// </summary>
    public static class Ui
    {
        private static Font _font;
        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        // ---- primitives -------------------------------------------------------------------------

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            var rt = (RectTransform)go.transform;
            if (parent != null) rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Anchor to all four edges with the given insets.</summary>
        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Set layout sizes (negative = leave unset).</summary>
        public static LayoutElement Size(Component c, float width = -1, float height = -1, float flexWidth = -1, float flexHeight = -1)
        {
            if (!c.TryGetComponent<LayoutElement>(out var le)) le = c.gameObject.AddComponent<LayoutElement>();
            if (width >= 0) { le.preferredWidth = width; le.minWidth = width; }
            if (height >= 0) { le.preferredHeight = height; le.minHeight = height; }
            if (flexWidth >= 0) le.flexibleWidth = flexWidth;
            if (flexHeight >= 0) le.flexibleHeight = flexHeight;
            return le;
        }

        public static Image Panel(Transform parent, Color color, string name = "Panel")
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        /// <summary>Vertical stack (children fill the width, use their preferred height).</summary>
        public static RectTransform Column(Transform parent, float spacing = 12, int padding = 0, Color? background = null, string name = "Column")
        {
            var rt = Rect(parent, name);
            if (background.HasValue) rt.gameObject.AddComponent<Image>().color = background.Value;
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return rt;
        }

        /// <summary>Horizontal row of a fixed height; children with flexibleWidth share the rest.</summary>
        public static RectTransform Row(Transform parent, float height = Theme.RowHeight, float spacing = 12, Color? background = null, int padding = 0, string name = "Row")
        {
            var rt = Rect(parent, name);
            if (background.HasValue) rt.gameObject.AddComponent<Image>().color = background.Value;
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(padding, padding, padding / 2, padding / 2);
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            if (height > 0) Size(rt, height: height);
            return rt;
        }

        /// <summary>Flexible empty space inside a row/column.</summary>
        public static RectTransform Spacer(Transform parent, float size = -1)
        {
            var rt = Rect(parent, "Spacer");
            if (size >= 0) Size(rt, size, size); else Size(rt, flexWidth: 1);
            return rt;
        }

        public static Text Label(Transform parent, string text, int size = Theme.FontNormal, Color? color = null, TextAnchor align = TextAnchor.MiddleLeft, string name = "Label")
        {
            var rt = Rect(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.color = color ?? Theme.Text;
            t.alignment = align;
            t.text = text ?? "";
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            Size(t, flexWidth: 1);
            return t;
        }

        public static Button Button(Transform parent, string text, Action onClick, Color? color = null, float width = -1, float height = Theme.ButtonHeight, bool interactable = true, int fontSize = Theme.FontNormal)
        {
            var img = Panel(parent, color ?? Theme.Button, "Button:" + text);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.8f);
            btn.colors = colors;
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var label = Label(img.transform, text, fontSize, interactable ? Theme.Text : Theme.TextDim, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 8, 4, 8, 4);
            Size(btn, width, height, width < 0 ? 1 : 0);
            return btn;
        }

        public static InputField Input(Transform parent, string value, string placeholder, float height = Theme.ButtonHeight, Action<string> onEndEdit = null)
        {
            var img = Panel(parent, Theme.PanelAlt, "Input");
            Size(img, height: height, flexWidth: 1);
            var input = img.gameObject.AddComponent<InputField>();
            var text = Label(img.transform, "", Theme.FontNormal);
            text.supportRichText = false;
            Stretch(text.rectTransform, 16, 4, 16, 4);
            var ph = Label(img.transform, placeholder, Theme.FontNormal, Theme.TextDim);
            ph.fontStyle = FontStyle.Italic;
            Stretch(ph.rectTransform, 16, 4, 16, 4);
            input.textComponent = text;
            input.placeholder = ph;
            input.targetGraphic = img;
            input.text = value ?? "";
            if (onEndEdit != null) input.onEndEdit.AddListener(v => onEndEdit(v));
            return input;
        }

        // ---- composites -------------------------------------------------------------------------

        /// <summary>Full-size vertical scroll view; returns the content (a Column that grows with its children).</summary>
        public static RectTransform Scroll(Transform parent, out ScrollRect scrollRect, float spacing = 14, int padding = 20)
        {
            var root = Stretch(Rect(parent, "Scroll"));
            scrollRect = root.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 40;
            var viewport = Stretch(Rect(root, "Viewport"));
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0); // raycast target for drags
            var content = Column(viewport, spacing, padding, null, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            return content;
        }

        /// <summary>A fixed-height scrolling list inside a layout (e.g. chat); returns the list content.</summary>
        public static RectTransform List(Transform parent, float height, Color? background = null)
        {
            var frame = Panel(parent, background ?? Theme.PanelAlt, "List");
            Size(frame, height: height);
            return Scroll(frame.transform, out _, 8, 10);
        }

        /// <summary>Fixed-column grid; preferred height follows the child count.</summary>
        public static RectTransform Grid(Transform parent, Vector2 cellSize, int columns, float spacing = 12)
        {
            var rt = Rect(parent, "Grid");
            var g = rt.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = cellSize;
            g.spacing = new Vector2(spacing, spacing);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = columns;
            g.childAlignment = TextAnchor.UpperCenter;
            return rt;
        }

        public static UiBar Bar(Transform parent, float value, Color color, string label = null, float height = 36)
        {
            var bg = Panel(parent, Theme.Locked, "Bar");
            Size(bg, height: height, flexWidth: 1);
            var fill = Panel(bg.transform, color, "Fill");
            fill.raycastTarget = false;
            var frt = fill.rectTransform;
            frt.anchorMin = Vector2.zero; frt.pivot = new Vector2(0, 0.5f);
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            Text text = null;
            if (label != null)
            {
                text = Label(bg.transform, label, Theme.FontSmall, Theme.Text, TextAnchor.MiddleCenter);
                Stretch(text.rectTransform);
            }
            var bar = new UiBar(fill.rectTransform, text);
            bar.Set(value, label);
            return bar;
        }

        public static UiTabBar TabBar(Transform parent, IList<string> labels, int selected, Action<int> onSelect, float height = 84)
        {
            var row = Row(parent, height, 6);
            var bar = new UiTabBar();
            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                var b = Button(row, labels[i], () => { bar.Select(index); onSelect?.Invoke(index); }, Theme.Button, -1, height, true, Theme.FontSmall);
                bar.Buttons.Add(b);
            }
            bar.Select(selected);
            return bar;
        }

        /// <summary>Modal dialog over <paramref name="layer"/>; any button closes it unless KeepOpen.</summary>
        public static UiModal Modal(Transform layer, string title, Action<RectTransform> buildBody, params ModalButton[] buttons)
        {
            var dim = Panel(layer, Theme.Dim, "Modal:" + title);
            Stretch(dim.rectTransform);
            var modal = new UiModal(dim.gameObject);
            var box = Column(dim.transform, 16, 28, Theme.Panel, "Box");
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(940, 0);
            box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Label(box, title, Theme.FontLarge, Theme.Text, TextAnchor.MiddleCenter, "Title");
            var body = Column(box, 12, 0, null, "Body");
            buildBody?.Invoke(body);
            modal.Body = body;
            if (buttons == null || buttons.Length == 0) buttons = new[] { new ModalButton("Close", null) };
            var row = Row(box, Theme.ButtonHeight, 12);
            foreach (var mb in buttons)
            {
                var b = mb;
                Button(row, b.Label, () =>
                {
                    if (!b.KeepOpen) modal.Close();
                    b.Action?.Invoke();
                }, b.Color ?? Theme.Button);
            }
            UiTween.PopIn(box);
            return modal;
        }

        // ---- housekeeping -----------------------------------------------------------------------

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) DestroyGo(t.GetChild(i).gameObject);
        }

        /// <summary>Destroy that also works in edit mode (tests).</summary>
        public static void DestroyGo(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) { go.SetActive(false); go.transform.SetParent(null, false); Object.Destroy(go); }
            else Object.DestroyImmediate(go);
        }
    }

    public sealed class ModalButton
    {
        public string Label;
        public Action Action;
        public Color? Color;
        public bool KeepOpen;
        public ModalButton(string label, Action action, Color? color = null, bool keepOpen = false) { Label = label; Action = action; Color = color; KeepOpen = keepOpen; }
    }

    public sealed class UiModal
    {
        public readonly GameObject Root;
        public RectTransform Body;
        public bool IsOpen => Root != null;
        public event Action Closed;
        public UiModal(GameObject root) { Root = root; }
        public void Close()
        {
            if (Root == null) return;
            Ui.DestroyGo(Root);
            Closed?.Invoke();
        }
    }

    public sealed class UiBar
    {
        private readonly RectTransform _fill;
        private readonly Text _label;
        public float Value { get; private set; }
        public Text Label => _label;
        public UiBar(RectTransform fill, Text label) { _fill = fill; _label = label; }
        public void Set(float value, string label = null)
        {
            Value = Mathf.Clamp01(float.IsNaN(value) ? 0 : value);
            _fill.anchorMax = new Vector2(Value, 1);
            _fill.offsetMax = Vector2.zero;
            if (_label != null && label != null) _label.text = label;
        }
    }

    public sealed class UiTabBar
    {
        public readonly List<Button> Buttons = new List<Button>();
        public int Selected { get; private set; }
        public void Select(int index)
        {
            Selected = index;
            for (int i = 0; i < Buttons.Count; i++)
                if (Buttons[i] != null) ((Image)Buttons[i].targetGraphic).color = i == index ? Theme.Accent : Theme.Button;
        }
    }
}
