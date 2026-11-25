using System;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Widgets shared by the summon event screens (hub, exchange, pass, packs, tasks, draw result): the event top bar with
    /// currency chips, reward tiles, icon buttons with count badges, banner titles. Built on <see cref="Ui"/>.
    /// </summary>
    public static class SummonKit
    {
        public static readonly Color Night = Theme.Hex(0x1A1230);
        /// <summary>Screen background behind the summon screens.</summary>
        public static readonly Color Deep = Theme.Hex(0x120C22);
        public static readonly Color Plum = Theme.Hex(0x2C2148);
        public static readonly Color PlumLight = Theme.Hex(0x463670);
        public static readonly Color Lime = Theme.Hex(0xB8E04A);
        public static readonly Color LimeInk = Theme.Hex(0x1E2A08);
        public static readonly Color Cream = Theme.Hex(0xFBF0D8);
        public static readonly Color CreamDeep = Theme.Hex(0xE8D2AE);
        public static readonly Color Brown = Theme.Hex(0x5A3A24);
        public static readonly Color BadgeRed = Theme.Hex(0xE8403E);

        /// <summary>Paint the screen background (the full-screen summon screens cover the resource bar).</summary>
        public static void Background(RectTransform root)
        {
            var bg = root != null ? root.GetComponent<Image>() : null;
            if (bg != null) bg.color = Deep;
        }

        /// <summary>Absolutely placed child: anchors at <paramref name="anchor"/>, pivot = anchor, offset and size.</summary>
        public static T Place<T>(T c, Vector2 anchor, Vector2 pos, Vector2 size) where T : Component
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return c;
        }

        public static Image Img(Transform parent, Sprite sprite, string name, bool preserve = true)
        {
            var img = Ui.Panel(parent, Color.white, name);
            img.sprite = sprite;
            img.preserveAspect = preserve;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A text with the display font and a dark outline (titles on art).</summary>
        public static Text Chunky(Transform parent, string text, int size, Color color, TextAnchor align, string name = "Label")
        {
            var t = Ui.Display(parent, text, size, color, align, name);
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.08f, 0.04f, 0.12f, 0.9f);
            o.effectDistance = new Vector2(2, -2);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        /// <summary>Top bar: back, title and currency chips (icon + amount). Returns the row.</summary>
        public static RectTransform TopBar(Transform parent, string title, Action onBack, params (string icon, long amount, string name)[] chips)
        {
            var bar = Ui.Row(parent, 112, 12, Night, 14, "TopBar");
            var hl = bar.GetComponent<HorizontalLayoutGroup>();
            hl.childForceExpandHeight = false; hl.childAlignment = TextAnchor.MiddleLeft;
            if (onBack != null) Ui.Button(bar, "<", onBack, PlumLight, 88, 84, true, Theme.FontTitle).name = "BackButton";
            var t = Chunky(bar, title, 46, Color.white, TextAnchor.MiddleLeft, "Title");
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 26; t.resizeTextMaxSize = 46;
            Ui.Size(t, -1, 84, 1);
            foreach (var (icon, amount, name) in chips) Chip(bar, icon, amount, name);
            return bar;
        }

        public static RectTransform Chip(Transform parent, string icon, long amount, string name)
        {
            var chip = Ui.Row(parent, 64, 4, Theme.Hex(0x0E0A1C), 0, "Chip:" + name);
            Ui.Size(chip, 186, 64, 0);
            var hl = chip.GetComponent<HorizontalLayoutGroup>();
            hl.childForceExpandWidth = false; hl.childAlignment = TextAnchor.MiddleLeft; hl.padding = new RectOffset(0, 16, 0, 0);
            var img = Img(chip, SummonArt.Icon(icon), "Icon");
            Ui.Size(img, 64, 64, 0);
            var t = Ui.Display(chip, Fmt.Num(amount), 34, Color.white, TextAnchor.MiddleRight, "Amount");
            Ui.Size(t, -1, 64, 1);
            return chip;
        }

        /// <summary>A reward tile: framed icon with the amount in the corner, optional caption under it. Returns the frame.</summary>
        public static RectTransform RewardTile(Transform parent, Reward r, float size, string caption = null, bool dim = false, string state = null, Color? frame = null)
        {
            var root = Ui.Rect(parent, "Reward:" + (r?.Kind ?? "") + ":" + (r?.Id ?? ""));
            Ui.Size(root, size, size + (caption != null ? 40 : 0), 0, 0);
            var box = Ui.RoundPanel(root, frame ?? Theme.Hex(0xF2A93C), size * 0.18f, "Frame");
            Place(box, new Vector2(0.5f, 1), Vector2.zero, new Vector2(size, size));
            var inner = Ui.RoundPanel(box.transform, Theme.Hex(0xC86E3A), size * 0.14f, "Inner");
            Ui.Stretch(inner.rectTransform, 7, 7, 7, 7);
            var icon = Img(inner.transform, SummonArt.RewardIcon(r), "Icon");
            Ui.Stretch(icon.rectTransform, size * 0.1f, size * 0.08f, size * 0.1f, size * 0.16f);
            string amount = SummonView.Amount(r);
            if (amount.Length > 0)
            {
                var a = Chunky(box.transform, amount, Mathf.RoundToInt(size * 0.24f), Color.white, TextAnchor.LowerCenter, "Amount");
                Ui.Stretch(a.rectTransform, 0, 0, 0, -4);
            }
            if (dim) { var d = Ui.RoundPanel(box.transform, new Color(0, 0, 0, 0.5f), size * 0.18f, "Dim"); Ui.Stretch(d.rectTransform); d.raycastTarget = false; }
            if (state == "claimed") Place(Img(box.transform, SummonArt.Icon("check"), "Claimed"), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.7f, size * 0.7f));
            else if (state == "locked") Place(Img(box.transform, SummonArt.Icon("lock"), "Locked"), new Vector2(1, 1), new Vector2(size * 0.12f, size * 0.12f), new Vector2(size * 0.4f, size * 0.4f));
            if (caption != null)
            {
                var c = Chunky(root, caption, 26, Color.white, TextAnchor.UpperCenter, "Caption");
                Place(c, new Vector2(0.5f, 0), Vector2.zero, new Vector2(size + 60, 40));
            }
            return root;
        }

        /// <summary>A red count badge in the top-right corner of <paramref name="target"/> (nothing for 0).</summary>
        public static void Badge(Transform target, int count, float size = 48)
        {
            if (count <= 0) return;
            var b = Ui.Panel(target, BadgeRed, "Badge");
            b.sprite = UiShapes.Circle;
            b.raycastTarget = false;
            Place(b, new Vector2(1, 1), new Vector2(6, 6), new Vector2(size, size));
            UiShapes.Outline(b.rectTransform, Color.white, size / 2, 3);
            var t = Ui.Display(b.transform, count > 99 ? "99" : count.ToString(), Mathf.RoundToInt(size * 0.62f), Color.white, TextAnchor.MiddleCenter, "Count");
            Ui.Stretch(t.rectTransform);
        }

        /// <summary>Side-column icon button: art, label under it, count badge.</summary>
        public static Button SideButton(Transform parent, string icon, string label, int badge, Action onClick, float size = 150)
        {
            var root = Ui.Rect(parent, "Side:" + label);
            Ui.Size(root, size, size + 34, 0, 0);
            var img = Ui.Panel(root, Color.white, "Icon");
            img.sprite = SummonArt.Icon(icon);
            img.preserveAspect = true;
            Place(img, new Vector2(0.5f, 1), Vector2.zero, new Vector2(size * 0.86f, size * 0.86f));
            var t = Chunky(root, label, 32, Color.white, TextAnchor.LowerCenter, "Label");
            Place(t, new Vector2(0.5f, 0), Vector2.zero, new Vector2(size + 40, 70));
            var btn = root.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            Badge(img.transform, badge);
            return btn;
        }

        /// <summary>A banner title over a sheet ("Exchange Shop", "Event Packs", "Tasks") with the art above it.</summary>
        public static RectTransform Banner(Transform parent, string title, string icon, float artSize = 200)
        {
            var root = Ui.Rect(parent, "Banner");
            Ui.Size(root, -1, artSize * 0.7f + 110, 1, 0);
            if (icon != null) Place(Img(root, SummonArt.Icon(icon), "Art"), new Vector2(0.5f, 1), Vector2.zero, new Vector2(artSize, artSize));
            var ribbon = Ui.RoundPanel(root, Theme.Hex(0xA77BFF), 24, "Ribbon");
            Place(ribbon, new Vector2(0.5f, 0), Vector2.zero, new Vector2(760, 110));
            UiShapes.Outline(ribbon.rectTransform, Theme.Hex(0x4A2E8A), 24, 6);
            var t = Chunky(ribbon.transform, title, 56, Color.white, TextAnchor.MiddleCenter, "Title");
            Ui.Stretch(t.rectTransform);
            return root;
        }

        /// <summary>A chunky lime action button (CLAIM, Draw) with an optional icon + count line under the caption.</summary>
        public static Button ActionButton(Transform parent, string caption, string icon, string count, Action onClick, Color face, float width, float height, bool interactable = true, string name = null)
        {
            var b = Ui.Button(parent, "", onClick, face, width, height, interactable, 30);
            b.name = name ?? "Button:" + caption;
            var faceRt = b.targetGraphic.rectTransform;
            var col = Ui.Column(faceRt, 0, 0, null, "Content");
            Ui.Stretch(col, 8, 4, 8, 4);
            var vl = col.GetComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter; vl.childForceExpandWidth = false; vl.childControlWidth = true;
            var ink = interactable ? Theme.TextOn(face) : new Color(Theme.Text.r, Theme.Text.g, Theme.Text.b, UiButtonLook.DisabledTextAlpha);
            if (!string.IsNullOrEmpty(caption))
            {
                var t = Ui.Display(col, caption, icon != null || count != null ? 40 : 38, ink, TextAnchor.MiddleCenter, "Caption");
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            if (icon != null || count != null)
            {
                var row = Ui.Row(col, 52, 8, null, 0, "Cost");
                var hl = row.GetComponent<HorizontalLayoutGroup>();
                hl.childAlignment = TextAnchor.MiddleCenter; hl.childForceExpandWidth = false;
                if (icon != null) Ui.Size(Img(row, SummonArt.Icon(icon), "Icon"), 52, 52, 0);
                if (count != null) { var c = Ui.Display(row, count, 38, ink, TextAnchor.MiddleLeft, "Count"); c.horizontalOverflow = HorizontalWrapMode.Overflow; Ui.Size(c, -1, 52, 0); }
            }
            return b;
        }

        /// <summary>The cream inner sheet of the packs / tasks panels (scrolling list).</summary>
        public static RectTransform CreamList(Transform parent, float maxHeight, Action<RectTransform> build)
        {
            var frame = Ui.RoundPanel(parent, Cream, 28, "CreamList");
            UiShapes.Outline(frame.rectTransform, CreamDeep, 28, 6);
            var col = Ui.Column(frame.transform, 0, 18, null, "Pad");
            Ui.Stretch(col);
            Ui.Size(frame, -1, maxHeight, 1, 0);
            Ui.ScrollingColumn(col, maxHeight - 36, build, 18, 800);
            return frame.rectTransform;
        }

        /// <summary>A brown-headed card on the cream sheet (pack / task row).</summary>
        public static RectTransform CreamCard(Transform parent, string title, out RectTransform body)
        {
            var card = Ui.Column(parent, 0, 0, CreamDeep, "Card:" + title);
            var head = Ui.Row(card, 64, 0, null, 0, "Head");
            var tt = Ui.Display(head, title, 36, Brown, TextAnchor.MiddleLeft, "Title");
            tt.horizontalOverflow = HorizontalWrapMode.Overflow;
            head.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(24, 24, 0, 0);
            body = Ui.Column(card, 10, 18, Theme.Hex(0xFFF7E6), "Body");
            return card;
        }
    }
}
