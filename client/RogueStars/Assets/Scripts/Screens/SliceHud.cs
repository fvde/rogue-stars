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
            var t = Ui.Display(parent, text, size, color ?? Color.white, align, name);
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
            var face = Ui.RoundPanel(edge.transform, fill, h * scale / 2 - 3, "Face");
            face.raycastTarget = false;
            Ui.Stretch(face.rectTransform, 4, 4, 4, 4);
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
        public Image XpFill, HpFill;
        public Text Level, HpText, Atk, Def, Coins, Skills;
        public RectTransform CoinsChip, AtkPill, DefPill;
        public string XpText = "";
        private string _hpLine = "", _atkLine = "", _defLine = "", _skillsLine = "";
        public float Scale;

        /// <summary>Plain-text view (tests): "HP a / b | ATK n | DEF n | Lv n | XP x / y | SKILLS n".</summary>
        public string Summary => string.Join(" | ", new[] { _hpLine, _atkLine, _defLine, "Lv " + _level, XpText, _skillsLine });
        private string _level = "";

        /// <summary>Builds the top HUD over <paramref name="parent"/> (a full-screen rect of width <paramref name="width"/>).</summary>
        public static SliceHud Build(RectTransform parent, float width, Action onCog, Action onLevel, bool coins)
        {
            var h = new SliceHud { Scale = width / 1080f };
            float s = h.Scale;
            var root = Ui.Rect(parent, "TopStrip");
            Ui.Stretch(root);
            h.Root = root;

            // level: EXP hexagon over a dark pill with the XP fill
            var lvl = SliceKit.Pill(root, s, 138, 208, 150, 58, SliceKit.PillDark, "RunLevel");
            var xpTrack = Ui.Rect(lvl.transform.Find("Face"), "XpTrack");
            Ui.Stretch(xpTrack);
            var xp = Ui.RoundPanel(xpTrack, new Color32(0x6E, 0xD2, 0xF6, 255), 22 * s, "XpFill");
            xp.raycastTarget = false;
            xp.type = Image.Type.Filled;
            xp.fillMethod = Image.FillMethod.Horizontal;
            xp.fillOrigin = 0;
            Ui.Stretch(xp.rectTransform);
            h.XpFill = xp;
            var lvText = SliceKit.Chunky(lvl.transform, "Lv. 1", (int)(40 * s), Color.white, TextAnchor.MiddleLeft, "LvNumber");
            Ui.Stretch(lvText.rectTransform, 46 * s, 0, 6, 2);
            h.Level = lvText;
            var lvBtn = lvl.gameObject.AddComponent<Button>();
            lvBtn.targetGraphic = lvl;
            if (onLevel != null) lvBtn.onClick.AddListener(() => onLevel());
            var expIcon = SliceKit.Icon(root, "exp", s, 56, 206, 74, false, "ExpIcon");
            var expCap = SliceKit.Chunky(expIcon.transform, "EXP", (int)(22 * s), Color.white, TextAnchor.MiddleCenter, "ExpCaption");
            Ui.Stretch(expCap.rectTransform);

            // HP pill: red fill with the heart
            var hp = SliceKit.Pill(root, s, 418, 208, 336, 58, new Color32(0x4A, 0x16, 0x22, 255), "HpBar");
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
            h.HpText = SliceKit.Chunky(hp.transform, "", (int)(38 * s), Color.white, TextAnchor.MiddleCenter, "HpText");
            Ui.Stretch(h.HpText.rectTransform, 40 * s, 0, 6, 2);
            SliceKit.Icon(root, "heart", s, 250, 204, 78, false, "HeartIcon");

            // ATK / DEF pills
            h.AtkPill = SliceKit.Pill(root, s, 710, 208, 148, 54, SliceKit.PillDark, "AtkPill").rectTransform;
            h.Atk = SliceKit.Chunky(h.AtkPill, "", (int)(34 * s), Color.white, TextAnchor.MiddleCenter, "AtkChip");
            Ui.Stretch(h.Atk.rectTransform, 24 * s, 0, 4, 2);
            SliceKit.Icon(root, "sword", s, 624, 204, 72, false, "SwordIcon");
            h.DefPill = SliceKit.Pill(root, s, 902, 208, 140, 54, SliceKit.PillDark, "DefPill").rectTransform;
            h.Def = SliceKit.Chunky(h.DefPill, "", (int)(34 * s), Color.white, TextAnchor.MiddleCenter, "DefChip");
            Ui.Stretch(h.Def.rectTransform, 24 * s, 0, 4, 2);
            SliceKit.Icon(root, "shield", s, 826, 204, 64, false, "ShieldIcon");

            // cog (pause)
            var cog = Ui.Button(root, "", onCog, new Color(0, 0, 0, 0), 70 * s, 70 * s, true, 20);
            cog.name = "PauseButton";
            SliceKit.Place((RectTransform)cog.transform, s, 1022, 204, 72, 72);
            var face = cog.transform.Find("Face");
            if (face != null) { var fi = face.GetComponent<Image>(); if (fi != null) { fi.sprite = HudArt.Icon("cog"); fi.type = Image.Type.Simple; fi.color = Color.white; fi.preserveAspect = true; } }
            foreach (Transform c in cog.transform) if (c.name != "Face") c.gameObject.SetActive(false);
            h.Cog = cog;

            if (coins)
            {
                var chip = SliceKit.Pill(root, s, 154, 296, 176, 60, SliceKit.PillDark, "CoinsChip");
                h.CoinsChip = chip.rectTransform;
                h.Coins = SliceKit.Chunky(chip.transform, "-", (int)(42 * s), Color.white, TextAnchor.MiddleCenter, "Coins");
                Ui.Stretch(h.Coins.rectTransform, 50 * s, 0, 4, 2);
                SliceKit.Icon(root, "coin", s, 62, 296, 70, false, "CoinIcon");
            }
            return h;
        }

        /// <summary>The shown values: HP fill and text, ATK / DEF numbers, level and XP fill.</summary>
        public void Show(RunHudStats st, int level, float xpFrac, int skills, bool known = true)
        {
            double hpFrac = st.MaxHp > 0 ? st.Hp / st.MaxHp : 1;
            HpFill.fillAmount = Mathf.Clamp01((float)hpFrac);
            HpText.text = Fmt.Num(Math.Round(st.Hp)) + " / " + Fmt.Num(Math.Round(st.MaxHp));
            _hpLine = "HP " + HpText.text;
            Atk.text = known ? Fmt.Num(Math.Round(st.Atk)) : "–";
            Def.text = known ? Fmt.Num(Math.Round(st.Def)) : "–";
            _atkLine = "ATK " + Atk.text;
            _defLine = "DEF " + Def.text;
            _skillsLine = "SKILLS " + skills;
            if (Skills != null) Skills.text = skills.ToString();
            _level = level > 0 ? level.ToString() : "–";
            Level.text = "Lv. " + _level;
            XpFill.fillAmount = Mathf.Clamp01(xpFrac);
            XpText = "XP " + st.Xp + " / " + st.XpNeed;
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
            var track = SliceKit.Pill(root, s, x, (top + bottom) / 2, 40, bottom - top + 30, new Color32(0x14, 0x12, 0x22, 240), "ClockTrack");
            var face = (RectTransform)track.transform.Find("Face");
            if (info.Fill > 0.001f)
            {
                var fill = Ui.RoundPanel(face, Color.Lerp(new Color32(0x5C, 0xC8, 0xF4, 255), new Color32(0xF0, 0x4A, 0x52, 255), info.Fill), 14 * s, "ClockFill");
                fill.raycastTarget = false;
                var frt = fill.rectTransform;
                frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(1, Mathf.Max(info.Fill, 0.06f));
                frt.offsetMin = frt.offsetMax = Vector2.zero;
            }
            for (int i = 0; i < info.Markers.Count; i++)
            {
                var m = info.Markers[i];
                float y = bottom - (bottom - top) * m.At;
                var pip = SliceKit.Icon(root, "skull", s, x, y, 58, false, "MinibossMarker:" + i);
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
