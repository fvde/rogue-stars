using System;
using System.Collections.Generic;
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
    /// Look helpers of the vertical slice: the chunky outlined display text (white with a dark stroke), pill chips with an
    /// icon on the left, and placement in reference pixels (a 1080-px-wide portrait screen, scaled to the real width).
    /// </summary>
    public static class SliceKit
    {
        public static readonly Color Ink = new Color32(0x1E, 0x16, 0x24, 255);
        public static readonly Color PillDark = new Color32(0x24, 0x1E, 0x34, 235);
        public static readonly Color PillEdge = new Color32(0x0E, 0x0A, 0x14, 255);

        /// <summary>White chunky display text with a thick dark stroke and a drop (numbers, titles).</summary>
        public static Text Chunky(Transform parent, string text, int size, Color? color = null, TextAnchor align = TextAnchor.MiddleCenter, string name = "Label")
        {
            var t = Ui.Display(parent, text != null ? text.Replace("→", "»") : text, size, color ?? Color.white, align, name);
            t.font = Ui.FontFor(t.text);
            t.fontSize = size; // exact: chunky text is sized against the references (no Theme.Type scaling)
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            float d = Mathf.Max(2, size * 0.075f);
            var o1 = t.gameObject.AddComponent<Outline>();
            o1.effectColor = Ink; o1.effectDistance = new Vector2(d, -d);
            var o2 = t.gameObject.AddComponent<Outline>();
            o2.effectColor = Ink; o2.effectDistance = new Vector2(d * 0.7f, d * 0.7f);
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.9f); sh.effectDistance = new Vector2(0, -d * 1.6f);
            return t;
        }

        /// <summary>Places a rect by its centre and size in reference pixels (from the top or the bottom edge of the parent).</summary>
        public static RectTransform Place(RectTransform rt, float scale, float x, float y, float w, float h, bool fromBottom = false, float refHeight = 2400)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, fromBottom ? 0 : 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w * scale, h * scale);
            rt.anchoredPosition = fromBottom ? new Vector2(x * scale, (refHeight - y) * scale) : new Vector2(x * scale, -y * scale);
            return rt;
        }

        /// <summary>
        /// A full-width backdrop clipped to <paramref name="height"/> px from the top of <paramref name="parent"/>: a sprite covers
        /// the box at its own aspect (never stretched), <paramref name="zoom"/> enlarges it about the top centre and
        /// <paramref name="shiftUp"/> (px) slides it up, so the art's lower edge can be cropped away. No sprite: the drawn arena.
        /// </summary>
        public static Image Backdrop(RectTransform parent, Sprite sprite, float height, string name, float zoom = 1f, float shiftUp = 0f)
        {
            var box = Ui.Rect(parent, name);
            box.anchorMin = new Vector2(0, 1); box.anchorMax = new Vector2(1, 1); box.pivot = new Vector2(0.5f, 1);
            box.sizeDelta = new Vector2(0, height); box.anchoredPosition = Vector2.zero;
            box.gameObject.AddComponent<RectMask2D>();
            var img = Ui.Panel(box, Color.white, "Art");
            img.raycastTarget = false;
            var rt = img.rectTransform;
            float width = parent.rect.width;
            if (sprite == null || sprite.rect.height <= 0 || width <= 0)
            {
                img.sprite = sprite ?? HudArt.Arena();
                Ui.Stretch(rt);
                return img;
            }
            img.sprite = sprite;
            float k = Mathf.Max(width / sprite.rect.width, height / sprite.rect.height) * zoom;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(sprite.rect.width * k, sprite.rect.height * k);
            rt.anchoredPosition = new Vector2(0, shiftUp);
            return img;
        }

        /// <summary>An icon image (HudArt) at a reference position.</summary>
        public static Image Icon(RectTransform parent, string id, float scale, float x, float y, float size, bool fromBottom = false, string name = null)
        {
            var img = Ui.Panel(parent, Color.white, name ?? "Icon:" + id);
            img.sprite = HudArt.Icon(id);
            img.preserveAspect = true;
            img.raycastTarget = false;
            Place(img.rectTransform, scale, x, y, size, size, fromBottom);
            return img;
        }

        /// <summary>A dark rounded pill with an outline at a reference position.</summary>
        public static Image Pill(RectTransform parent, float scale, float x, float y, float w, float h, Color fill, string name, bool fromBottom = false)
        {
            var edge = Ui.RoundPanel(parent, PillEdge, h * scale / 2, name);
            edge.raycastTarget = false;
            Place(edge.rectTransform, scale, x, y, w, h, fromBottom);
            float t = Mathf.Max(4, 6 * scale);
            var face = Ui.RoundPanel(edge.transform, fill, h * scale / 2 - t, "Face");
            face.raycastTarget = false;
            Ui.Stretch(face.rectTransform, t, t, t, t);
            // inner bevel: a dark lip along the bottom, a soft gloss over the top half
            var lip = Ui.RoundPanel(face.transform, new Color(0, 0, 0, 0.22f), h * scale / 2 - t, "Lip");
            lip.raycastTarget = false;
            Ui.Stretch(lip.rectTransform);
            lip.rectTransform.anchorMax = new Vector2(1, 0.3f);
            var gloss = Ui.RoundPanel(face.transform, new Color(1, 1, 1, 0.12f), h * scale / 3, "Gloss");
            gloss.raycastTarget = false;
            gloss.rectTransform.anchorMin = new Vector2(0, 0.52f); gloss.rectTransform.anchorMax = new Vector2(1, 0.92f);
            gloss.rectTransform.offsetMin = new Vector2(h * scale * 0.3f, 0); gloss.rectTransform.offsetMax = new Vector2(-h * scale * 0.3f, 0);
            return edge;
        }
    }

    /// <summary>
    /// The run's HUD floating over the world board (vertical slice, reference 1): top-left the level with its EXP hexagon
    /// and XP fill, the HP pill (heart), ATK (sword) and DEF (shield) pills, the cog (pause) top-right, the coin chip under
    /// them; the boss rail down the left edge (boss skull on top, miniboss skull pips, dice icon and "n/N" at the bottom).
    /// Values: RunSystem.WormStats / LevelInfo through <see cref="RunHudStats"/>. Names kept for tests: TopStrip,
    /// PauseButton, HpBar, AtkChip, DefChip, RunLevel, LvNumber, XpFill, CoinsChip, BossClock (the rail), RollCounter,
    /// MinibossMarker:i, ClockFill.
    /// </summary>
    public sealed class SliceHud
    {
        public RectTransform Root;
        public Button Cog;
        /// <summary>The pause cog: reference size, a cool grey tint (the art is grey on a dark strip) and the dark hub disc over the art's white hole.</summary>
        public const float CogSize = 84, CogHubFraction = 0.46f;
        public static readonly Color CogTint = new Color32(0xD2, 0xD8, 0xEA, 255);
        public Image XpFill, HpFill;
        public Text Level, HpText, Atk, Def, Coins, Skills;
        public RectTransform CoinsChip, AtkPill, DefPill;
        public string XpText = "";
        private string _hpLine = "", _atkLine = "", _defLine = "", _skillsLine = "";
        public float Scale;

        /// <summary>Plain-text view (tests): "HP a / b | ATK n | DEF n | Lv n | XP x / y | SKILLS n".</summary>
        public string Summary => string.Join(" | ", Array.FindAll(new[] { _hpLine, _atkLine, _defLine, _level.Length > 0 ? "Lv " + _level : "", XpText, _skillsLine }, x => x.Length > 0));
        private string _level = "";

        /// <summary>Builds the top HUD over <paramref name="parent"/> (a full-screen rect of width <paramref name="width"/>).</summary>
        /// <summary>Reference px (1080 wide): the level pill's centre / width and the HP heart's centre / size (they must not overlap).</summary>
        public const float LevelPillX = 128, LevelPillW = 144, HeartX = 254, HeartSize = 76;

        public static SliceHud Build(RectTransform parent, float width, Action onCog, Action onLevel, bool coins)
        {
            var h = new SliceHud { Scale = width / 1080f };
            float s = h.Scale;
            var root = Ui.Rect(parent, "TopStrip");
            Ui.Stretch(root);
            h.Root = root;

            // level: EXP hexagon over a dark pill with the XP fill (playtest 0.1.325: the pill ends clear of the HP heart, the
            // level text shrinks to fit it instead of spilling under the heart)
            var lvl = SliceKit.Pill(root, s, LevelPillX, 208, LevelPillW, 64, SliceKit.PillDark, "RunLevel");
            var xpTrack = Ui.Rect(lvl.transform.Find("Face"), "XpTrack");
            Ui.Stretch(xpTrack);
            var xp = Ui.RoundPanel(xpTrack, new Color32(0x6E, 0xD2, 0xF6, 255), 22 * s, "XpFill");
            xp.raycastTarget = false;
            xp.type = Image.Type.Filled;
            xp.fillMethod = Image.FillMethod.Horizontal;
            xp.fillOrigin = 0;
            Ui.Stretch(xp.rectTransform);
            h.XpFill = xp;
            var lvText = SliceKit.Chunky(lvl.transform, "Lv. 1", (int)(42 * s), Color.white, TextAnchor.MiddleCenter, "LvNumber");
            Ui.Stretch(lvText.rectTransform, 40 * s, 0, 8 * s, 2);
            lvText.horizontalOverflow = HorizontalWrapMode.Wrap;
            lvText.resizeTextForBestFit = true; lvText.resizeTextMaxSize = lvText.fontSize; lvText.resizeTextMinSize = Mathf.Max(10, (int)(26 * s));
            h.Level = lvText;
            var lvBtn = lvl.gameObject.AddComponent<Button>();
            lvBtn.targetGraphic = lvl;
            if (onLevel != null) lvBtn.onClick.AddListener(() => onLevel());
            var expIcon = SliceKit.Icon(root, "exp", s, 56, 206, 74, false, "ExpIcon");
            var expCap = SliceKit.Chunky(expIcon.transform, "EXP", (int)(22 * s), Color.white, TextAnchor.MiddleCenter, "ExpCaption");
            Ui.Stretch(expCap.rectTransform);

            // HP pill: red fill with the heart
            var hp = SliceKit.Pill(root, s, 420, 208, 340, 64, new Color32(0x4A, 0x16, 0x22, 255), "HpBar");
            var hpFill = Ui.RoundPanel(hp.transform.Find("Face"), new Color32(0xEE, 0x3E, 0x4E, 255), 22 * s, "HpFill");
            hpFill.raycastTarget = false;
            hpFill.type = Image.Type.Filled;
            hpFill.fillMethod = Image.FillMethod.Horizontal;
            Ui.Stretch(hpFill.rectTransform);
            h.HpFill = hpFill;
            var shine = Ui.RoundPanel(hpFill.transform, new Color(1, 1, 1, 0.22f), 10 * s, "Shine");
            shine.raycastTarget = false;
            shine.rectTransform.anchorMin = new Vector2(0, 0.55f); shine.rectTransform.anchorMax = new Vector2(1, 0.9f);
            shine.rectTransform.offsetMin = new Vector2(14 * s, 0); shine.rectTransform.offsetMax = new Vector2(-14 * s, 0);
            h.HpText = SliceKit.Chunky(hp.transform, "", (int)(44 * s), Color.white, TextAnchor.MiddleCenter, "HpText");
            Ui.Stretch(h.HpText.rectTransform, 40 * s, 0, 6, 2);
            SliceKit.Icon(root, "heart", s, HeartX, 204, HeartSize, false, "HeartIcon");

            // ATK / DEF pills
            h.AtkPill = SliceKit.Pill(root, s, 712, 208, 152, 60, SliceKit.PillDark, "AtkPill").rectTransform;
            h.Atk = SliceKit.Chunky(h.AtkPill, "", (int)(40 * s), Color.white, TextAnchor.MiddleCenter, "AtkChip");
            Ui.Stretch(h.Atk.rectTransform, 24 * s, 0, 4, 2);
            SliceKit.Icon(root, "sword", s, 624, 204, 72, false, "SwordIcon");
            h.DefPill = SliceKit.Pill(root, s, 902, 208, 144, 60, SliceKit.PillDark, "DefPill").rectTransform;
            h.Def = SliceKit.Chunky(h.DefPill, "", (int)(40 * s), Color.white, TextAnchor.MiddleCenter, "DefChip");
            Ui.Stretch(h.Def.rectTransform, 24 * s, 0, 4, 2);
            SliceKit.Icon(root, "shield", s, 826, 204, 64, false, "ShieldIcon");

            // cog (pause)
            var cog = Ui.Button(root, "", onCog, new Color(0, 0, 0, 0), 70 * s, 70 * s, true, 20);
            cog.name = "PauseButton";
            SliceKit.Place((RectTransform)cog.transform, s, 1022, 204, CogSize, CogSize);
            var face = cog.transform.Find("Face");
            if (face != null)
            {
                var fi = face.GetComponent<Image>();
                if (fi != null)
                {
                    fi.sprite = HudArt.Icon("cog"); fi.type = Image.Type.Simple; fi.color = CogTint; fi.preserveAspect = true;
                    // the imported cog has an opaque white hole that read as a white blob at HUD size: a dark hub disc over it
                    var hub = Ui.Panel(face, SliceKit.PillDark, "CogHub");
                    hub.sprite = UiShapes.Circle; hub.raycastTarget = false;
                    var hr = hub.rectTransform;
                    hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0.495f);
                    hr.sizeDelta = new Vector2(CogSize * s * CogHubFraction, CogSize * s * CogHubFraction);
                    hr.anchoredPosition = Vector2.zero;
                }
            }
            foreach (Transform c in cog.transform) if (c.name != "Face") c.gameObject.SetActive(false);
            h.Cog = cog;

            if (coins)
            {
                var chip = SliceKit.Pill(root, s, 156, 296, 180, 66, SliceKit.PillDark, "CoinsChip");
                h.CoinsChip = chip.rectTransform;
                // #2: the count at the size of the other pills' numbers (HP 44, ATK / DEF 40)
                h.Coins = SliceKit.Chunky(chip.transform, "-", (int)(42 * s), Color.white, TextAnchor.MiddleCenter, "Coins");
                Ui.Stretch(h.Coins.rectTransform, 50 * s, 0, 4, 2);
                SliceKit.Icon(root, "coin", s, 62, 296, 70, false, "CoinIcon");
            }
            return h;
        }

        /// <summary>
        /// The shown values: HP fill and text, ATK / DEF numbers, level and XP fill. <paramref name="inRun"/> false (Q2: a
        /// battle with no run behind it — hero / mount kit fights, a finished run): no level badge and no XP bar at all.
        /// </summary>
        public void Show(RunHudStats st, int level, float xpFrac, int skills, bool known = true, bool inRun = true)
        {
            double hpFrac = st.MaxHp > 0 ? st.Hp / st.MaxHp : 1;
            HpFill.fillAmount = Mathf.Clamp01((float)hpFrac);
            HpText.text = Fmt.Num(Math.Round(st.Hp)) + " / " + Fmt.Num(Math.Round(st.MaxHp));
            _hpLine = "HP " + HpText.text;
            // never "–" (BT3): stats that are unknown hide their pills instead
            Atk.text = Fmt.Num(Math.Round(st.Atk));
            Def.text = Fmt.Num(Math.Round(st.Def));
            ShowPart("AtkPill", known); ShowPart("SwordIcon", known); ShowPart("DefPill", known); ShowPart("ShieldIcon", known);
            _atkLine = known ? "ATK " + Atk.text : "";
            _defLine = known ? "DEF " + Def.text : "";
            _skillsLine = "SKILLS " + skills;
            if (Skills != null) Skills.text = skills.ToString();
            bool hasLevel = inRun && known && level > 0;
            _level = hasLevel ? level.ToString() : "";
            Level.text = "Lv. " + _level;
            ShowPart("RunLevel", hasLevel); ShowPart("ExpIcon", hasLevel);
            XpFill.fillAmount = Mathf.Clamp01(xpFrac);
            XpText = hasLevel ? "XP " + st.Xp + " / " + st.XpNeed : "";
        }

        private void ShowPart(string name, bool on)
        {
            var t = Root != null ? Root.Find(name) : null;
            if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }

        // ---- the boss rail ----------------------------------------------------------------------------------------------

        /// <summary>
        /// The boss rail down the left edge (reference 1): the boss skull on top, a dark track filling up (blue → red) as the
        /// boss wakes, a skull pip per clock miniboss (bright once passed), the dice icon and "n/N" (rolls / the roll the
        /// boss is expected after) at the bottom. Named BossClock (tests).
        /// </summary>
        public static RectTransform BossRail(RectTransform parent, BossClockInfo info, float s)
        {
            var root = Ui.Rect(parent, "BossClock");
            Ui.Stretch(root);
            float top = 800, bottom = 1240, x = 74;
            var track = SliceKit.Pill(root, s, x, (top + bottom) / 2, 56, bottom - top + 40, new Color32(0x14, 0x12, 0x22, 245), "ClockTrack");
            var face = (RectTransform)track.transform.Find("Face");
            if (info.Fill > 0.001f)
            {
                // the fill: a gradient from calm blue at the bottom to the awakening's colour at its top
                var hot = Color.Lerp(new Color32(0x5C, 0xC8, 0xF4, 255), new Color32(0xF0, 0x4A, 0x52, 255), info.Fill);
                var holder = Ui.Rect(face, "ClockFillHolder");
                holder.anchorMin = Vector2.zero; holder.anchorMax = new Vector2(1, Mathf.Max(info.Fill, 0.08f));
                holder.offsetMin = holder.offsetMax = Vector2.zero;
                var fill = Ui.RoundPanel(holder, Color.white, 18 * s, "ClockFill");
                fill.raycastTarget = false;
                Ui.Stretch(fill.rectTransform);
                var grad = Ui.Panel(fill.transform, Color.white, "ClockGradient");
                grad.sprite = HudArt.Gradient(new Color32(0x3C, 0x9C, 0xE8, 255), hot);
                grad.raycastTarget = false;
                Ui.Stretch(grad.rectTransform, 3 * s, 3 * s, 3 * s, 3 * s);
                var shine = Ui.Panel(fill.transform, new Color(1, 1, 1, 0.25f), "Shine");
                shine.raycastTarget = false;
                shine.rectTransform.anchorMin = new Vector2(0.2f, 0.04f); shine.rectTransform.anchorMax = new Vector2(0.38f, 0.96f);
                shine.rectTransform.offsetMin = shine.rectTransform.offsetMax = Vector2.zero;
            }
            for (int i = 0; i < info.Markers.Count; i++)
            {
                var m = info.Markers[i];
                float y = bottom - (bottom - top) * m.At;
                // a framed pip: dark ring, then the skull
                var frame = Ui.Dot(root, SliceKit.PillEdge, 66 * s, "MinibossFrame:" + i);
                var fle = frame.GetComponent<LayoutElement>(); if (fle != null) fle.ignoreLayout = true;
                frame.raycastTarget = false;
                SliceKit.Place(frame.rectTransform, s, x, y, 66, 66);
                UiShapes.Outline(frame.rectTransform, m.Passed ? (Color)new Color32(0xF0, 0xCC, 0x6A, 255) : new Color32(0x6A, 0x64, 0x80, 255), 33 * s, 4);
                var pip = SliceKit.Icon(root, "skull", s, x, y - 2, 54, false, "MinibossMarker:" + i);
                pip.color = m.Passed ? Color.white : new Color(1, 1, 1, 0.5f);
            }
            SliceKit.Icon(root, "boss_rail_top", s, x, top - 36, 88, false, "BossSkull");
            SliceKit.Icon(root, "dice", s, x + 4, bottom + 40, 104, false, "RailDice");
            var roll = SliceKit.Chunky(root, ShortRoll(info.RollText), (int)(40 * s), Color.white, TextAnchor.MiddleCenter, "RollCounter");
            SliceKit.Place(roll.rectTransform, s, x + 2, bottom + 112, 160, 50);
            var boss = Ui.Label(root, "BOSS", 2, Color.clear, TextAnchor.MiddleCenter, "BossLabel"); // keeps "BOSS" readable for tests / screen readers
            boss.raycastTarget = false;
            SliceKit.Place(boss.rectTransform, s, x, top - 36, 10, 10);
            return root;
        }

        /// <summary>"Roll 12/30" → "12/30" (the rail shows only the numbers).</summary>
        public static string ShortRoll(string rollText) => string.IsNullOrEmpty(rollText) ? "" : rollText.StartsWith("Roll ") ? rollText.Substring(5) : rollText;
    }
}
