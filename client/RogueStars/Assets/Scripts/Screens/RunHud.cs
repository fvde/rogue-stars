using System;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// The run's top status strip (owner-approved design "A · Top status bar"), shared by the run board and the battle
    /// playback: a square left button (pause / skip), the HP bar ("HP 734 / 1,080") over one line "ATK n · DEF n · SKILLS n",
    /// the run level as an XP ring ("LV 7"), and the coins chip. The run screen adds the boss clock under it
    /// (<see cref="BossClock"/>).
    /// </summary>
    public sealed class RunHudStrip
    {
        /// <summary>Height of the stats row (without the boss clock).</summary>
        public const float StatsH = 120;
        /// <summary>Height of the boss clock row.</summary>
        public const float ClockH = 54;
        public const int PadX = 20, PadTop = 12, PadBottom = 10;
        public static readonly Color StripBg = Theme.Hex(0x140E16);
        public static readonly Color AtkColor = Theme.Hex(0xFF9B5C), DefColor = Theme.Hex(0x7FC8FF), SkillsColor = Theme.Hex(0xD5B8FF);
        public static readonly Color RingColor = Theme.Hex(0x7BE08A), ClockGold = Theme.Hex(0xFFD36B), BossPink = Theme.Hex(0xFF9FA2);

        public RectTransform Root;
        public Button LeftButton;
        public UiBar Hp;
        public Text Atk, Def, Skills, Extra, Level, Coins;
        public Image XpFill;
        public RectTransform CoinsChip;
        /// <summary>"XP 3 / 8" of the shown level (the ring has no text; tapping it shows this).</summary>
        public string XpText = "";

        /// <summary>Total height with or without the boss clock row.</summary>
        public static float Height(bool clock) => PadTop + PadBottom + StatsH + (clock ? ClockH + 6 : 0);

        public static RunHudStrip Build(RectTransform parent, string leftLabel, string leftName, Action onLeft, Action onLevel, bool coins)
        {
            var s = new RunHudStrip();
            var root = Ui.Column(parent, 6, 0, null, "TopStrip");
            root.gameObject.AddComponent<Image>().color = StripBg;
            root.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(PadX, PadX, PadTop, PadBottom);
            s.Root = root;

            var row = Ui.Row(root, StatsH, 14, null, 0, "HudStats");
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;

            // left: pause (run) / skip (battle) — a square outlined button
            var left = Ui.Button(row, leftLabel, onLeft, new Color(1, 1, 1, 0.06f), 92, 92, true, 34);
            left.name = leftName;
            left.GetComponentInChildren<Text>().color = Theme.Text;
            UiShapes.Outline((RectTransform)left.transform, new Color(1, 1, 1, 0.2f), 24, 3);
            s.LeftButton = left;

            // middle: HP bar over "ATK · DEF · SKILLS"
            var mid = Ui.Column(row, 6, 0, null, "HpStats");
            Ui.Size(mid, -1, 92, 1);
            mid.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            s.Hp = Ui.Bar(mid, 1, Theme.Bad, "", 40);
            s.Hp.Fill.parent.name = "HpBar";
            s.Hp.Fill.parent.GetComponent<Image>().color = Theme.Hex(0x3A1C22);
            s.Hp.Label.alignment = TextAnchor.MiddleLeft;
            s.Hp.Label.rectTransform.offsetMin = new Vector2(18, 0);
            s.Hp.Label.fontSize = 26;
            var stats = Ui.Row(mid, 38, 18, null, 0, "StatLine");
            var sl = stats.GetComponent<HorizontalLayoutGroup>();
            sl.childForceExpandHeight = true; sl.childControlWidth = true;
            s.Atk = StatText(stats, "AtkChip");
            s.Def = StatText(stats, "DefChip");
            s.Skills = StatText(stats, "SkillCount");
            s.Extra = StatText(stats, "HudExtra");
            Ui.Spacer(stats);

            // run level: XP progress as a ring with "LV n"
            var ring = Ui.Rect(row, "RunLevel");
            Ui.Size(ring, 108, 108, 0);
            var track = Ui.Panel(ring, new Color(1, 1, 1, 0.12f), "RingTrack");
            track.sprite = UiShapes.CircleRing;
            Ui.Stretch(track.rectTransform);
            var fill = Ui.Panel(ring, RingColor, "XpRing");
            fill.sprite = UiShapes.CircleRing;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.raycastTarget = false;
            Ui.Stretch(fill.rectTransform);
            s.XpFill = fill;
            var lvCap = Ui.Display(ring, "LV", 22, Theme.Hex(0xA9E9B2), TextAnchor.UpperCenter, "LvCaption");
            Ui.Stretch(lvCap.rectTransform, 0, 22, 0, 0);
            var lv = Ui.Display(ring, "1", 40, Theme.Text, TextAnchor.LowerCenter, "LvNumber");
            Ui.Stretch(lv.rectTransform, 0, 0, 0, 18);
            s.Level = lv;
            var ringBtn = ring.gameObject.AddComponent<Button>();
            ringBtn.targetGraphic = track;
            if (onLevel != null) ringBtn.onClick.AddListener(() => onLevel());

            if (coins)
            {
                var chip = Ui.Chip(row, "-", new Color(0.08f, 0.055f, 0.086f, 0.9f), Theme.Text, 28, 64, new Color(1, 1, 1, 0.16f), "CoinsChip");
                var dot = Ui.Dot(chip, Theme.Hex(0xFFC23D), 28);
                dot.transform.SetAsFirstSibling();
                chip.GetComponent<HorizontalLayoutGroup>().spacing = 10;
                s.Coins = chip.GetComponentInChildren<Text>();
                s.CoinsChip = chip;
            }
            return s;
        }

        private static Text StatText(RectTransform row, string name)
        {
            var t = Ui.Display(row, "", 28, Theme.Hex(0xE8DCCB), TextAnchor.MiddleLeft, name);
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Size(t, -1, -1, 0);
            return t;
        }

        /// <summary>"ATK <color>142</color>": a stat caption with its number in the stat colour.</summary>
        public static string StatLine(string caption, string value, Color c) => caption + " <color=#" + ColorUtility.ToHtmlStringRGB(c) + ">" + value + "</color>";

        /// <summary>The shown values: HP bar, the stat line, level ring (XP fraction) and its XP text.</summary>
        public void Show(RunHudStats s, int level, float xpFrac, int skills, bool known = true)
        {
            double hpFrac = s.MaxHp > 0 ? s.Hp / s.MaxHp : 1;
            Hp.Set((float)hpFrac, "HP " + Fmt.Num(Math.Round(s.Hp)) + " / " + Fmt.Num(Math.Round(s.MaxHp)));
            Hp.Fill.GetComponent<Image>().color = hpFrac > 0.5 ? Theme.Hex(0xFF6B5E) : hpFrac > 0.25 ? Theme.Warn : Theme.Bad;
            Atk.text = StatLine("ATK", known ? Fmt.Num(Math.Round(s.Atk)) : "–", AtkColor);
            Def.text = StatLine("DEF", known ? Fmt.Num(Math.Round(s.Def)) : "–", DefColor);
            Skills.text = StatLine("SKILLS", skills.ToString(), SkillsColor);
            Level.text = level > 0 ? level.ToString() : "–";
            XpFill.fillAmount = Mathf.Clamp01(xpFrac);
            XpText = "XP " + s.Xp + " / " + s.XpNeed;
        }

        /// <summary>Plain-text view of the strip (tests): "HP a / b | ATK n | DEF n | Lv n | XP x / y | SKILLS n".</summary>
        public string Summary => string.Join(" | ", new[] { Hp?.Label?.text, Plain(Atk), Plain(Def), "Lv " + Level?.text, XpText, Plain(Skills) });

        public static string Plain(Text t) => t == null ? null : System.Text.RegularExpressions.Regex.Replace(t.text, "<[^>]+>", "");

        // ---- boss clock -------------------------------------------------------------------------

        /// <summary>
        /// The boss clock row: "Roll n/N" (gold), the awakening bar (purple → red as the boss wakes) with the miniboss markers
        /// (passed = bright gold), "BOSS" on the right.
        /// </summary>
        public static RectTransform BossClock(RectTransform parent, BossClockInfo info)
        {
            var row = Ui.Row(parent, ClockH, 16, null, 0, "BossClock");
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            var roll = Ui.Display(row, info.RollText, 32, ClockGold, TextAnchor.MiddleLeft, "RollCounter");
            roll.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Size(roll, 190, 44, 0);
            var track = Ui.Rect(row, "ClockTrack");
            Ui.Size(track, -1, 26, 1);
            var bg = Ui.RoundPanel(track, new Color(1, 1, 1, 0.12f), 13, "Track");
            bg.raycastTarget = false;
            Ui.Stretch(bg.rectTransform);
            if (info.Fill > 0.001f)
            {
                var fill = Ui.RoundPanel(track, Color.Lerp(Theme.Purple, Theme.Bad, info.Fill), 13, "ClockFill");
                fill.raycastTarget = false;
                var frt = fill.rectTransform;
                frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(Mathf.Max(info.Fill, 0.04f), 1);
                frt.offsetMin = frt.offsetMax = Vector2.zero;
            }
            for (int i = 0; i < info.Markers.Count; i++)
            {
                var m = info.Markers[i];
                var mk = Ui.RoundPanel(track, m.Passed ? ClockGold : new Color(ClockGold.r, ClockGold.g, ClockGold.b, 0.45f), 5, "MinibossMarker:" + i);
                mk.raycastTarget = false;
                var rt = mk.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(m.At, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(10, 42);
            }
            var boss = Ui.Display(row, "BOSS", 30, BossPink, TextAnchor.MiddleRight, "BossLabel");
            boss.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Size(boss, 90, 44, 0);
            return row;
        }
    }
}
