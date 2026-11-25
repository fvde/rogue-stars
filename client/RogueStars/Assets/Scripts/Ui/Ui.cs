using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// Code-built uGUI toolkit. Everything is a RectTransform under a layout group; widgets size themselves
    /// with LayoutElements. Look: rounded cards and pills (<see cref="UiShapes"/>), chunky buttons with a darker bottom
    /// lip, the rounded display font (Fredoka, <see cref="DisplayFont"/>) for titles and numbers, the built-in font for body text.
    /// </summary>
    public static class Ui
    {
        private static Font _font;
        private static Font _body;
        private static bool _bodyTried;
        private static Font _display;
        private static bool _displayTried;

        /// <summary>The built-in system font: only the fallback for glyphs Fredoka lacks (★ ☆ ✓ →).</summary>
        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        /// <summary>Rounded body font (Fredoka Regular, SIL OFL) — the default for all Ui text (G6); falls back to <see cref="Font"/>.</summary>
        public static Font BodyFont
        {
            get
            {
                if (_body != null) return _body;
                if (!_bodyTried) { _bodyTried = true; _body = Resources.Load<Font>("Fonts/Fredoka-Regular"); }
                return _body != null ? _body : Font;
            }
        }

        /// <summary>The body font when it has every glyph of <paramref name="text"/>, else the system font.</summary>
        public static Font BodyFontFor(string text)
        {
            var b = BodyFont;
            if (b == Font || string.IsNullOrEmpty(text)) return b;
            foreach (char c in text)
                if (c > 0x7E && !char.IsWhiteSpace(c) && !DisplayHas(c)) return Font;
            return b;
        }

        /// <summary>Rounded display font for titles, buttons and numbers (Fredoka Bold, SIL OFL; falls back to the body font).</summary>
        public static Font DisplayFont
        {
            get
            {
                if (_display != null) return _display;
                if (!_displayTried) { _displayTried = true; _display = Resources.Load<Font>("Fonts/Fredoka-Bold"); }
                return _display != null ? _display : Font;
            }
        }

        /// <summary>The display font when it has every glyph of <paramref name="text"/> (★, ✓, → are not in Fredoka), else the body font.</summary>
        public static Font FontFor(string text)
        {
            var d = DisplayFont;
            if (d == Font || string.IsNullOrEmpty(text)) return d;
            foreach (char c in text)
                if (c > 0x7E && !char.IsWhiteSpace(c) && !DisplayHas(c)) return Font;
            return d;
        }

        /// <summary>
        /// Non-ASCII code point ranges of Fredoka-Bold.ttf (from its cmap). Font.HasCharacter is not used: dynamic fonts
        /// report OS fallback glyphs that a device may not have.
        /// </summary>
        private static readonly int[] DisplayRanges =
        {
            0x00A0, 0x00AC, 0x00AE, 0x00FF, 0x0131, 0x0131, 0x0141, 0x0142, 0x0152, 0x0153, 0x0160, 0x0161, 0x0178, 0x0178,
            0x017D, 0x017E, 0x0192, 0x0192, 0x02C6, 0x02C7, 0x02D8, 0x02DD, 0x2013, 0x2014, 0x2018, 0x201A, 0x201C, 0x201E,
            0x2020, 0x2022, 0x2026, 0x2026, 0x2030, 0x2030, 0x2039, 0x203A, 0x2044, 0x2044, 0x20AC, 0x20AC, 0x2122, 0x2122,
            0x2212, 0x2212,
        };

        private static bool DisplayHas(char c)
        {
            for (int i = 0; i < DisplayRanges.Length; i += 2)
                if (c >= DisplayRanges[i] && c <= DisplayRanges[i + 1]) return true;
            return false;
        }

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

        /// <summary>Flat rectangle (board pieces, overlays, bar fills).</summary>
        public static Image Panel(Transform parent, Color color, string name = "Panel")
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        /// <summary>Rounded rectangle with the given corner radius.</summary>
        public static Image RoundPanel(Transform parent, Color color, float radius = Theme.RadiusCard, string name = "Panel") =>
            UiShapes.Round(Panel(parent, color, name), radius);

        /// <summary>A filled circle (dots, pips, avatars).</summary>
        public static Image Dot(Transform parent, Color color, float size, string name = "Dot")
        {
            var img = Panel(parent, color, name);
            img.sprite = UiShapes.Circle;
            img.raycastTarget = false;
            Size(img, size, size, 0, 0);
            return img;
        }

        /// <summary>Vertical stack (children fill the width, use their preferred height). A background makes it a rounded card.</summary>
        public static RectTransform Column(Transform parent, float spacing = 12, int padding = 0, Color? background = null, string name = "Column")
        {
            var rt = Rect(parent, name);
            if (background.HasValue) UiShapes.Round(rt.gameObject.AddComponent<Image>(), padding > 0 ? Theme.RadiusCard : Theme.RadiusSmall).color = background.Value;
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return rt;
        }

        /// <summary>Horizontal row of a fixed height; children with flexibleWidth share the rest. A background is rounded.</summary>
        public static RectTransform Row(Transform parent, float height = Theme.RowHeight, float spacing = 12, Color? background = null, int padding = 0, string name = "Row")
        {
            var rt = Rect(parent, name);
            if (background.HasValue) UiShapes.Round(rt.gameObject.AddComponent<Image>(), height > 0 ? Mathf.Min(Theme.RadiusButton, height / 2) : Theme.RadiusButton).color = background.Value;
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(padding, padding, padding / 2, padding / 2);
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            if (height > 0) Size(rt, height: height);
            return rt;
        }

        /// <summary>
        /// A rounded card: padded vertical stack on <paramref name="fill"/>, optionally outlined with <paramref name="border"/>.
        /// </summary>
        public static RectTransform Card(Transform parent, Color fill, Color? border = null, float radius = Theme.RadiusCard, int padding = 28, float spacing = 12, float borderWidth = 4, string name = "Card")
        {
            var rt = Rect(parent, name);
            UiShapes.Round(rt.gameObject.AddComponent<Image>(), radius).color = fill;
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            if (border.HasValue) UiShapes.Outline(rt, border.Value, radius, borderWidth);
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
            t.font = BodyFontFor(text);
            t.fontSize = Theme.Type(size);
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

        /// <summary>A label in the rounded display font (titles, big numbers, button captions).</summary>
        public static Text Display(Transform parent, string text, int size = Theme.FontLarge, Color? color = null, TextAnchor align = TextAnchor.MiddleLeft, string name = "Label")
        {
            var t = Label(parent, text, size, color, align, name);
            t.font = FontFor(text);
            t.lineSpacing = 0.9f;
            return t;
        }

        /// <summary>Small caps label above a section ("LOOT SECURED").</summary>
        public static Text Caption(Transform parent, string text, Color? color = null)
        {
            var t = Label(parent, (text ?? "").ToUpperInvariant(), 24, color ?? Theme.TextDim, TextAnchor.MiddleLeft, "Caption");
            t.fontStyle = FontStyle.Bold;
            return t;
        }

        /// <summary>
        /// A pill chip sized to its text (status, risk/reward, priority). <paramref name="fill"/> null = outlined only.
        /// </summary>
        public static RectTransform Chip(Transform parent, string text, Color? fill, Color textColor, int fontSize = 24, float height = 48, Color? border = null, string name = "Chip")
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            UiShapes.Round(img, height / 2);
            img.color = fill ?? new Color(0, 0, 0, 0);
            img.raycastTarget = false;
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            int pad = Mathf.RoundToInt(height * 0.4f);
            h.padding = new RectOffset(pad, pad, 0, 0);
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            var t = Label(rt, text, fontSize, textColor, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Size(t, flexWidth: 0);
            Size(rt, -1, height, 0, 0);
            if (border.HasValue || !fill.HasValue) UiShapes.Outline(rt, border ?? textColor, height / 2, 3);
            return rt;
        }

        /// <summary>A row that holds chips left-aligned without stretching them.</summary>
        public static RectTransform ChipRow(Transform parent, float height = 48, float spacing = 10)
        {
            var row = Row(parent, height, spacing);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            return row;
        }

        /// <summary>
        /// Chunky button: rounded face (<see cref="Button.targetGraphic"/>) over a darker lip, a soft top shine, caption in
        /// the display font (dark on bright faces). Named "Button:" + text.
        /// </summary>
        public static Button Button(Transform parent, string text, Action onClick, Color? color = null, float width = -1, float height = Theme.ButtonHeight, bool interactable = true, int fontSize = Theme.FontNormal)
        {
            var faceColor = color ?? Theme.Button;
            var root = Rect(parent, "Button:" + text);
            bool chunky = height >= 56;
            float lip = chunky ? Mathf.Min(Theme.ButtonLip, height * 0.1f) : 0;
            float radius = Mathf.Min(Theme.RadiusButton, height / 2);
            var lipImg = UiShapes.Round(root.gameObject.AddComponent<Image>(), radius);
            var face = UiShapes.Round(Stretch(Rect(root, "Face"), 0, 0, 0, lip).gameObject.AddComponent<Image>(), radius);
            Image shine = null;
            if (chunky && faceColor.a > 0.5f && Theme.TextOn(faceColor) == Theme.OnAccent)
            {
                // soft highlight on bright faces (the wireframes' top-to-bottom gradient); hidden while disabled (G4)
                shine = UiShapes.Round(Panel(face.transform, new Color(1, 1, 1, 0.13f), "Shine"), Mathf.Max(4, radius - 8));
                shine.raycastTarget = false;
                var srt = shine.rectTransform;
                srt.anchorMin = new Vector2(0, 0.55f); srt.anchorMax = Vector2.one;
                srt.offsetMin = new Vector2(10, 0); srt.offsetMax = new Vector2(-10, -5);
            }
            var btn = root.gameObject.AddComponent<Button>();
            btn.targetGraphic = face;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.disabledColor = Color.white; // the disabled look is the flat face colour itself (UiButtonLook)
            colors.fadeDuration = 0;
            btn.colors = colors;
            btn.onClick.AddListener(() => LastPressed = btn);
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var label = Label(face.transform, text, fontSize, Theme.TextOn(faceColor), TextAnchor.MiddleCenter);
            label.font = FontFor(text);
            label.lineSpacing = 0.9f;
            Stretch(label.rectTransform, 10, 2, 10, 2);
            Size(btn, width, height, width < 0 ? 1 : 0);
            btn.interactable = interactable;
            var look = root.gameObject.AddComponent<UiButtonLook>();
            look.Init(btn, face, lipImg, shine, label, faceColor, lip > 0);
            return btn;
        }

        /// <summary>The last <see cref="Button"/> the player pressed (anchor of the error bubble, G1 T57); null when destroyed.</summary>
        public static UnityEngine.UI.Button LastPressed;

        /// <summary>
        /// A chunky button with a one-line reason under it while disabled (G4: "Needs 100 shards"). The reason label is named
        /// "DisabledReason"; enabled buttons get no reason.
        /// </summary>
        public static Button ButtonWithReason(Transform parent, string text, Action onClick, Color? color, float width, float height, bool interactable, string reason, int fontSize = Theme.FontNormal)
        {
            if (interactable || string.IsNullOrEmpty(reason)) return Button(parent, text, onClick, color, width, height, interactable, fontSize);
            // a fixed-height row grows to fit the reason under the button (never over the content above)
            var rowLayout = parent.GetComponent<HorizontalLayoutGroup>();
            var rowSize = parent.GetComponent<LayoutElement>();
            if (rowLayout != null && rowSize != null && rowSize.preferredHeight > 0 && rowSize.preferredHeight < height + 34)
            {
                rowSize.preferredHeight = height + 34;
                rowLayout.childForceExpandHeight = false;
                rowLayout.childAlignment = rowLayout.childAlignment == TextAnchor.MiddleCenter || rowLayout.childAlignment == TextAnchor.UpperCenter ? TextAnchor.UpperCenter
                    : rowLayout.childAlignment == TextAnchor.MiddleRight || rowLayout.childAlignment == TextAnchor.UpperRight ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            }
            var col = Column(parent, 4, 0, null, "ButtonWithReason");
            col.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            Size(col, width, -1, width < 0 ? 1 : 0);
            var b = Button(col, text, onClick, color, width, height, false, fontSize);
            DisabledReason(col, reason);
            return b;
        }

        /// <summary>The one-line reason under a disabled button (G4).</summary>
        public static Text DisabledReason(Transform parent, string reason)
        {
            var t = Label(parent, reason, 20, Theme.TextDim, TextAnchor.MiddleCenter, "DisabledReason");
            Size(t, -1, 30, 1);
            return t;
        }

        /// <summary>Recolour a button made by <see cref="Button"/> (face, lip, caption contrast).</summary>
        public static void SetButtonColor(Button b, Color face)
        {
            if (b == null) return;
            var look = b.GetComponent<UiButtonLook>();
            if (look != null) { look.SetFace(face); return; }
            if (b.targetGraphic != null) b.targetGraphic.color = face;
            var lip = b.GetComponent<Image>();
            if (lip != null && lip != b.targetGraphic && lip.color.a > 0) lip.color = LipColor(face, b.interactable);
            var label = b.GetComponentInChildren<Text>(true);
            if (label != null && b.interactable) label.color = Theme.TextOn(face);
        }

        internal static Color LipColor(Color face, bool interactable)
        {
            if (face.a < 0.5f) return new Color(0, 0, 0, 0);
            var c = face == Theme.Accent ? Theme.AccentLip : Theme.Shade(face, 0.55f);
            return interactable ? c : Theme.Shade(c, 0.6f);
        }

        public static InputField Input(Transform parent, string value, string placeholder, float height = Theme.ButtonHeight, Action<string> onEndEdit = null)
        {
            var img = RoundPanel(parent, Theme.PanelAlt, Theme.RadiusButton, "Input");
            UiShapes.Outline(img.rectTransform, Theme.Border, Theme.RadiusButton, 3);
            Size(img, height: height, flexWidth: 1);
            var input = img.gameObject.AddComponent<InputField>();
            var text = Label(img.transform, "", Theme.FontNormal);
            text.supportRichText = false;
            Stretch(text.rectTransform, 24, 4, 24, 4);
            var ph = Label(img.transform, placeholder, Theme.FontNormal, Theme.TextFaint);
            ph.fontStyle = FontStyle.Italic;
            Stretch(ph.rectTransform, 24, 4, 24, 4);
            input.textComponent = text;
            input.placeholder = ph;
            input.targetGraphic = img;
            input.text = value ?? "";
            if (onEndEdit != null) input.onEndEdit.AddListener(v => onEndEdit(v));
            return input;
        }

        // ---- composites -------------------------------------------------------------------------

        /// <summary>Full-size vertical scroll view; returns the content (a Column that grows with its children).</summary>
        public static RectTransform Scroll(Transform parent, out ScrollRect scrollRect, float spacing = 18, int padding = 24)
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
            var frame = RoundPanel(parent, background ?? Theme.PanelAlt, Theme.RadiusButton, "List");
            Size(frame, height: height);
            return Scroll(frame.transform, out _, 8, 14);
        }

        /// <summary>
        /// A column inside a layout that scrolls once its content is taller than <paramref name="maxHeight"/> (tall modal
        /// bodies: the part detail): <paramref name="build"/> fills it, then the frame takes the content's height, capped.
        /// Returns the content column.
        /// </summary>
        public static RectTransform ScrollingColumn(Transform parent, float maxHeight, Action<RectTransform> build, float spacing = 14, float width = ModalInnerWidth)
        {
            var frame = Rect(parent, "ScrollFrame");
            frame.sizeDelta = new Vector2(width, maxHeight); // text wraps at the real width while measuring (the layout sets it later)
            var le = Size(frame, height: maxHeight);
            var content = Scroll(frame, out var sr, spacing, 0);
            build?.Invoke(content);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            le.preferredHeight = Mathf.Min(maxHeight, LayoutUtility.GetPreferredHeight(content));
            sr.verticalNormalizedPosition = 1;
            return content;
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

        /// <summary>Rounded progress bar (track + fill); the label sits centred on it.</summary>
        public static UiBar Bar(Transform parent, float value, Color color, string label = null, float height = 36)
        {
            float r = height / 2;
            var bg = RoundPanel(parent, Theme.Track, r, "Bar");
            Size(bg, height: height, flexWidth: 1);
            var fill = RoundPanel(bg.transform, color, r, "Fill");
            fill.raycastTarget = false;
            var frt = fill.rectTransform;
            frt.anchorMin = Vector2.zero; frt.pivot = new Vector2(0, 0.5f);
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            Text text = null;
            if (label != null)
            {
                text = Label(bg.transform, label, height >= 34 ? Theme.FontSmall : 20, Theme.Text, TextAnchor.MiddleCenter);
                text.fontStyle = FontStyle.Bold;
                text.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.6f);
                Stretch(text.rectTransform);
            }
            var bar = new UiBar(fill.rectTransform, text);
            bar.Set(value, label);
            return bar;
        }

        /// <summary>Segmented tabs on a dark pill; the selected tab is the accent colour.</summary>
        public static UiTabBar TabBar(Transform parent, IList<string> labels, int selected, Action<int> onSelect, float height = 84)
        {
            var row = Row(parent, height, 6, Theme.PanelAlt, 8);
            var bar = new UiTabBar();
            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                var b = Button(row, labels[i], () => { bar.Select(index); onSelect?.Invoke(index); }, Theme.PanelAlt, -1, height - 8, true, Theme.FontSmall);
                bar.Buttons.Add(b);
            }
            bar.Select(selected);
            return bar;
        }

        /// <summary>Width of a <see cref="Modal"/>'s body (940 box − 2 × 36 padding).</summary>
        public const float ModalInnerWidth = 868;

        /// <summary>Modal dialog over <paramref name="layer"/>; any button closes it unless KeepOpen.</summary>
        public static UiModal Modal(Transform layer, string title, Action<RectTransform> buildBody, params ModalButton[] buttons)
        {
            var dim = Panel(layer, Theme.Dim, "Modal:" + title);
            Stretch(dim.rectTransform);
            var modal = new UiModal(dim.gameObject);
            var box = Card(dim.transform, Theme.Panel, Theme.Border, 48, 36, 18, 4, "Box");
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(940, 0);
            box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Display(box, title, Theme.FontTitle, Theme.Cream, TextAnchor.MiddleCenter, "Title");
            var body = Column(box, 14, 0, null, "Body");
            buildBody?.Invoke(body);
            modal.Body = body;
            if (buttons == null || buttons.Length == 0) buttons = new[] { new ModalButton("Close", null) };
            var row = Row(box, Theme.ButtonHeight, 14);
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

        /// <summary>
        /// Modal with a fully custom box (event, draft, shop): <paramref name="build"/> fills the rounded box. Nothing is
        /// closed automatically; actions call <see cref="UiModal.Close"/> themselves.
        /// </summary>
        public static UiModal Sheet(Transform layer, string name, Color fill, Color? border, Action<RectTransform, UiModal> build, float width = 960)
        {
            var dim = Panel(layer, Theme.Dim, "Modal:" + name);
            Stretch(dim.rectTransform);
            var modal = new UiModal(dim.gameObject);
            var box = Card(dim.transform, fill, border, 56, 36, 20, 5, "Box");
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(width, 0);
            box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            modal.Body = box;
            build?.Invoke(box, modal);
            UiTween.PopIn(box);
            return modal;
        }

        /// <summary>
        /// A tappable card (event option, draft card): rounded <paramref name="fill"/> with an outline; returns the button and
        /// the card's content column. Named <paramref name="name"/> (e.g. "Button:" + caption, as the tests look buttons up).
        /// </summary>
        public static Button CardButton(Transform parent, string name, Color fill, Color border, Action onClick, out RectTransform content, float radius = 40, int padding = 26, float spacing = 10)
        {
            content = Card(parent, fill, border, radius, padding, spacing, 5, name);
            var btn = content.gameObject.AddComponent<Button>();
            btn.targetGraphic = content.GetComponent<Image>();
            var colors = btn.colors;
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.9f);
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
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
        public RectTransform Fill => _fill;
        public UiBar(RectTransform fill, Text label) { _fill = fill; _label = label; }
        public void Set(float value, string label = null)
        {
            Value = Mathf.Clamp01(float.IsNaN(value) ? 0 : value);
            _fill.anchorMax = new Vector2(Value, 1);
            _fill.offsetMax = Vector2.zero;
            _fill.gameObject.SetActive(Value > 0.001f);
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
                if (Buttons[i] != null) Ui.SetButtonColor(Buttons[i], i == index ? Theme.Accent : Theme.PanelAlt);
        }
    }
}
