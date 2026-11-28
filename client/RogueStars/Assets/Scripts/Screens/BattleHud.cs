using System;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// The battle's top HUD on the owner-approved board "New · battle" (UI kit v2, board px in the 1080-wide design container
    /// of <see cref="BattlePlaybackScreen"/>). Row 1: the run level pill (EXP icon, "Lv. n", a thin XP line), the wide HP bar
    /// ("12K / 12K", heart), the ATK and DEF pills, the settings button. Row 2: the run coins pill left, "Round n" centred (never
    /// the battle's length), the skip button ("»", closes the playback) right. Parts (tests): TopStrip, RunLevel, ExpIcon,
    /// LvNumber, XpFill, HpBar, HpFill, HpText, HeartIcon, AtkPill, SwordIcon, AtkChip, DefPill, ShieldIcon, DefChip,
    /// SettingsButton, CoinsChip, CoinIcon, Coins, Round, SkipButton.
    /// </summary>
    public sealed class BattleHud
    {
        public RectTransform Root;
        public Image XpFill, HpFill;
        public Text Level, HpText, Atk, Def, Coins, Round;
        public RectTransform CoinsChip, SkipButton, SettingsButton;
        public string XpText = "";
        private string _hpLine = "", _atkLine = "", _defLine = "", _skillsLine = "", _level = "";

        /// <summary>Plain-text view (tests): "HP a / b | ATK n | DEF n | Lv n | XP x / y | SKILLS n".</summary>
        public string Summary => string.Join(" | ", Array.FindAll(new[] { _hpLine, _atkLine, _defLine, _level.Length > 0 ? "Lv " + _level : "", XpText, _skillsLine }, x => x.Length > 0));

        /// <summary>Board rows: the stat row's top and the coins / round row's top (board px).</summary>
        public const float Row1 = 139, Row2 = 240, RowH = 80;

        public static BattleHud Build(RectTransform design, bool coins, Action onSettings, Action onSkip, Action onHudTap)
        {
            var h = new BattleHud();
            var root = KitV2.Place(design, "TopStrip", 0, 0, KitV2.DesignWidth, Row2 + RowH + 10);
            h.Root = root;

            // the run level: EXP icon over the navy pill, a thin XP line along its foot
            var lvl = KitV2.HudPill(root, 21, Row1, 160, RowH, "RunLevel").rectTransform;
            var xpTrack = KitV2.Place(lvl, "XpTrack", 66, RowH - 17, 80, 7);
            var xi = xpTrack.gameObject.AddComponent<Image>(); xi.raycastTarget = false; UiShapes.Round(xi, 3.5f); xi.color = KitV2.BarFill;
            var xp = KitV2.Place(xpTrack, "XpFill", 0, 0, 80, 7).gameObject.AddComponent<Image>();
            xp.raycastTarget = false; xp.sprite = UiShapes.Fill; xp.type = Image.Type.Filled; xp.fillMethod = Image.FillMethod.Horizontal;
            xp.color = Theme.Hex(0x6ED2F6);
            h.XpFill = xp;
            h.Level = KitV2.Text(lvl, "Lv. 1", 62, 14, 92, 46, 37.3f, Color.white, TextAnchor.MiddleCenter, "LvNumber");
            KitFit.On(h.Level, 20);
            KitV2.Icon(root, HudArt.Icon("exp"), 16, Row1 + 7, 64, 65, "ExpIcon");
            KitV2.Tap(lvl, onHudTap);

            // the HP bar: navy frame, red fill, the heart over its left end, "hp / max"
            var hp = KitV2.Place(root, "HpBar", 194, Row1, 436, RowH);
            var hpFill = KitV2.Bar(hp, "hp_red", KitV2.HpRed, 34.7f, 0, 0, 24);
            var track = (RectTransform)hpFill.parent;
            track.offsetMin = new Vector2(54, 16); track.offsetMax = new Vector2(-13, -16);
            hpFill.name = "HpFill";
            h.HpFill = hpFill.GetComponent<Image>();
            h.HpText = KitV2.Text(hp, "", 54, 18, 369, 44, 34.7f, Color.white, TextAnchor.MiddleCenter, "HpText");
            KitFit.On(h.HpText, 20);
            KitV2.Icon(root, KitArt.Get("icon_hp") ?? HudArt.Icon("heart"), 178, Row1 - 6, 80, 72, "HeartIcon");
            KitV2.Tap(hp, onHudTap);

            // ATK / DEF pills
            var atk = KitV2.HudPill(root, 643, Row1, 165, RowH, "AtkPill").rectTransform;
            h.Atk = KitV2.Text(atk, "", 60, 18, 98, 44, 34.7f, Color.white, TextAnchor.MiddleCenter, "AtkChip");
            KitFit.On(h.Atk, 18);
            KitV2.Icon(root, KitArt.Get("icon_atk") ?? HudArt.Icon("sword"), 638, Row1 - 3, 64, 64, "SwordIcon");
            var def = KitV2.HudPill(root, 822, Row1, 143, RowH, "DefPill").rectTransform;
            h.Def = KitV2.Text(def, "", 58, 18, 78, 44, 34.7f, Color.white, TextAnchor.MiddleCenter, "DefChip");
            KitFit.On(h.Def, 18);
            KitV2.Icon(root, KitArt.Get("icon_def") ?? HudArt.Icon("shield"), 817, Row1 - 3, 59, 64, "ShieldIcon");

            h.SettingsButton = (RectTransform)KitV2.SettingsButton(root, 979, Row1, 80, HudArt.Icon("cog"), onSettings).transform;

            // row 2: run coins left, the round centred, skip right
            if (coins)
            {
                var chip = KitV2.HudPill(root, 21, Row2, 176, RowH, "CoinsChip", 0.8f).rectTransform;
                h.CoinsChip = chip;
                h.Coins = KitV2.Text(chip, "0", 72, 12, 96, 56, 42.7f, Color.white, TextAnchor.MiddleCenter, "Coins");
                KitFit.On(h.Coins, 20);
                KitV2.Icon(root, KitArt.Get("icon_run_coin") ?? HudArt.Icon("coin"), 11, Row2 - 5, 80, 80, "CoinIcon");
            }
            h.Round = KitV2.Text(root, BattlePlaybackScreen.RoundLabel(1), 230, Row2 + 11, 620, 59, 50.7f, Color.white, TextAnchor.MiddleCenter, "Round");
            var skip = KitV2.HudPill(root, 925, Row2, 134, RowH, "SkipButton", 0.8f).rectTransform;
            KitV2.Icon(skip, HudArt.Icon("speed"), 35, 14, 64, 52, "SkipIcon");
            KitV2.Tap(skip, onSkip);
            h.SkipButton = skip;
            return h;
        }

        /// <summary>
        /// The shown values: HP fill and text, ATK / DEF numbers, level and XP. <paramref name="inRun"/> false (a battle with no
        /// run behind it: hero / mount kit fights, a finished run): no level pill. Unknown stats hide their pills (never "–").
        /// </summary>
        public void Show(RunHudStats st, int level, float xpFrac, int skills, bool known = true, bool inRun = true)
        {
            double hpFrac = st.MaxHp > 0 ? st.Hp / st.MaxHp : 1;
            float f = Mathf.Clamp01((float)hpFrac);
            HpFill.rectTransform.anchorMax = new Vector2(f, 1);
            HpFill.rectTransform.offsetMax = Vector2.zero;
            HpFill.gameObject.SetActive(f > 0.001f);
            HpText.text = Fmt.Num(Math.Round(st.Hp)) + " / " + Fmt.Num(Math.Round(st.MaxHp));
            _hpLine = "HP " + HpText.text;
            Atk.text = Fmt.Num(Math.Round(st.Atk));
            Def.text = Fmt.Num(Math.Round(st.Def));
            ShowPart("AtkPill", known); ShowPart("SwordIcon", known); ShowPart("DefPill", known); ShowPart("ShieldIcon", known);
            _atkLine = known ? "ATK " + Atk.text : "";
            _defLine = known ? "DEF " + Def.text : "";
            _skillsLine = "SKILLS " + skills;
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
    }
}
