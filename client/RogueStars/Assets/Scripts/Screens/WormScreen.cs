using System;
using RogueStars.Client.World.Anim;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Worm tab on the owner-approved board "New · worm" (UI kit v2, board px scaled to the width): navy top bar with three
    /// currencies, the parchment stage with the worm, six slot tiles (head, left arm, legs | body, right arm, tail; tap → the
    /// part sheet, an empty slot → the spares that fit), the gold power plate with the stat-sheet button, HP / ATK / DEF bars,
    /// the feature band (Talents, Runes, Mounts, Laboratory), the Parts | Levels | Stats tabs, the dark inventory panel of the
    /// spare parts (<see cref="GearScreen"/>) or today's Levels / Stats views, and the board's nav. Updates in place.
    /// </summary>
    public sealed class WormScreen : GearScreen
    {
        public static readonly StatId[] ShownStats =
            { StatId.Hp, StatId.Atk, StatId.Def, StatId.Speed, StatId.CritChance, StatId.CritDamage, StatId.Dodge, StatId.ComboChance, StatId.CounterChance, StatId.LifeSteal };

        public override string Title => "Worm";

        /// <summary>G1 T26: an equipped part flies from the tapped button into its slot, the slot pulses.</summary>
        public override bool OnFeedback(GameEvent e)
        {
            if (e.Type != "part_equipped" || e.Args.Count < 1) return false;
            var slot = FindRect(Root, "Slot:" + e.Args[0]);
            if (slot == null) return false;
            var pressed = Ui.LastPressed;
            var from = pressed != null && pressed.gameObject.activeInHierarchy ? FeedbackFx.Centre((RectTransform)pressed.transform) : FeedbackFx.Centre(slot);
            FeedbackFx.FlyTo(App.Feedback.Layer, RogueStars.Client.World.SummonArt.Icon("star"), from, slot, 1, 96, () => FeedbackFx.Flash(slot, Theme.Gold));
            return true;
        }

        private static RectTransform FindRect(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true)) if (rt.name == name) return rt;
            return null;
        }
        public override string[] IntroFeatures => new[] { Features.Parts, Features.WormLevel };

        public enum WormTab { Parts, Levels, Stats }
        private WormTab _tab = WormTab.Parts;
        public WormTab CurrentTab => _tab;

        // ---- kit v2 chrome: own top bar (3 currencies), no back/title, the board's nav ---------------------------------

        /// <summary>Board heights (px at 1080): top bar, stage (parchment up to the tabs), nav.</summary>
        public const float TopBarDesign = 237, StageDesign = 1163, NavDesign = 237;

        public override float HeaderHeight => TopBarDesign;
        public override bool CoversResourceBar => true;
        public override bool ShowBack => false;
        protected override bool KitTopBar => true;
        protected override bool InventoryIncludesEquipped => false;
        protected override string ActiveMainTab => "worm";

        private string _topKey;

        public void SetTab(WormTab tab) { _tab = tab; Refresh(); }

        protected override void BuildHeaderExtras(RectTransform header) => _topKey = null;

        /// <summary>The three currencies of the top bar: Ore and Diamonds, plus Metal on Parts / Biomass on Levels.</summary>
        public string[] TopCurrencies => new[] { Currencies.Ore, _tab == WormTab.Levels ? Currencies.Biomass : Currencies.Metal, Currencies.Diamonds };

        private void TopBar()
        {
            var cur = TopCurrencies;
            var key = K(cur[0], State.Wallet.Get(cur[0]), cur[1], State.Wallet.Get(cur[1]), cur[2], State.Wallet.Get(cur[2]));
            var top = KitTopDesign;
            if (key == _topKey || top == null) return;
            _topKey = key;
            Ui.Clear(top);
            KitV2.Box(top, "topbar", 0, 0, 1080, TopBarDesign, KitV2.Navy, 1, 0, null, null, "Bar");
            KitV2.Box(top, "topbar_edge", 0, TopBarDesign - 8, 1080, 8, KitV2.Ink, 1, 0, null, null, "Edge");
            float[] x = { 377, 616, 852 }, w = { 219, 214, 207 };
            for (int i = 0; i < 3; i++)
            {
                string c = cur[i];
                KitV2.CurrencyPill(top, x[i], 128, w[i], KitV2.CurrencyIcon(c), Fmt.Num(State.Wallet.Get(c)), () => App.Bar.ShowCurrency(c), "Currency:" + c);
            }
        }

        private static readonly Dictionary<StatId, (string abbr, Color color)> StatLook = new Dictionary<StatId, (string, Color)>
        {
            { StatId.Hp, ("HP", Theme.Bad) }, { StatId.Atk, ("ATK", Theme.Accent) }, { StatId.Def, ("DEF", Theme.Blue) },
            { StatId.Speed, ("SPD", Theme.Good) }, { StatId.CritChance, ("CRT", Theme.Gold) }, { StatId.CritDamage, ("CDM", Theme.Hex(0xFF6B9A)) },
            { StatId.Dodge, ("DDG", Theme.Premium) }, { StatId.ComboChance, ("CMB", Theme.Worm) }, { StatId.CounterChance, ("CTR", Theme.Purple) },
            { StatId.LifeSteal, ("LS", Theme.Hex(0xB1E04E)) },
        };

        /// <summary>Three-letter badge text of a stat (HP, ATK, DEF ...).</summary>
        public static string StatAbbr(StatId st) => StatLook.TryGetValue(st, out var l) ? l.abbr : st.ToString().Substring(0, 3).ToUpperInvariant();

        private static (string key, string abbr, Color color, string value) StatEntry(StatId st, double v)
        {
            var look = StatLook.TryGetValue(st, out var l) ? l : (st.ToString().Substring(0, 3).ToUpperInvariant(), Theme.TextDim);
            return (st.ToString(), look.Item1, look.Item2, StatText(st, v));
        }

        protected override void Build(RectTransform body)
        {
            var vl = body.GetComponent<VerticalLayoutGroup>();
            if (vl != null && (vl.padding.left != 0 || vl.spacing != 0)) { vl.padding = new RectOffset(0, 0, 0, 0); vl.spacing = 0; }
            var loadout = Session.Engine.BuildLoadout(State, Now);
            if (_tab == WormTab.Levels && !Session.IsUnlocked(Features.WormLevel)) _tab = WormTab.Parts;
            TopBar();

            var stageWrap = Part(body, "stage", "", b =>
            {
                var d = KitV2.DesignBlock(b, "Stage", StageDesign, out _);
                var bg = d.gameObject.AddComponent<Image>();
                bg.raycastTarget = false;
                KitV2.Parchment(bg);
            });
            var stage = (RectTransform)stageWrap.Find("Design");
            Part(stage, "worm", K(State.Heroes?.Equipped), StageWorm);
            foreach (var slot in PartsSystem.AllSlots)
            {
                var sl = slot;
                var root = Part(stage, "slot:" + slot, SlotKey(slot), c => SlotTile(c, sl));
                var pos = SlotPos(slot);
                KitV2.At(root, pos.x, pos.y, PartTileView.SlotTile, PartTileView.SlotTile + PartTileView.StripHeight);
            }
            Part(stage, "power", K(loadout.Power), c => PowerPlate(c, loadout));
            var stats = new[] { StatId.Hp, StatId.Atk, StatId.Def };
            var sv = new string[3];
            for (int i = 0; i < 3; i++) sv[i] = StatText(stats[i], loadout.WormStats.Value(stats[i]));
            Part(stage, "stats", K(sv[0], sv[1], sv[2]), c => StatBars(c, stats, sv));
            var feats = FeatureEntries();
            var fkey = new System.Text.StringBuilder();
            foreach (var f in feats) fkey.Append(f.Id).Append(f.Unlocked).Append(f.Badge).Append(',');
            Part(stage, "features", fkey.ToString(), c => FeatureBand(c, feats));
            bool levelUnlocked = Session.IsUnlocked(Features.WormLevel);
            bool partsDot = PartsDot(), levelsDot = levelUnlocked && TalentActionable();
            Part(stage, "tabs", K(_tab, levelUnlocked, partsDot, levelsDot), c => Tabs(c, levelUnlocked, partsDot, levelsDot));

            switch (_tab)
            {
                case WormTab.Parts: BuildInventory(body, "Parts"); break;
                case WormTab.Levels:
                    Part(body, "levels", VersionKey(loadout.Power, State.Wallet.Get(Currencies.Biomass)), b => DarkPanel(b, "LevelsPanel", p => LevelsView(p, loadout)));
                    break;
                default:
                    var statsKey = new System.Text.StringBuilder();
                    foreach (var st in ShownStats) statsKey.Append(loadout.WormStats.Value(st)).Append(',');
                    Part(body, "stats", statsKey.ToString(), b => DarkPanel(b, "StatsPanel", p => StatsView(p, loadout)));
                    break;
            }
        }

        /// <summary>The Levels / Stats content on the dark panel under the tabs (canvas units, today's views).</summary>
        private void DarkPanel(RectTransform parent, string name, Action<RectTransform> fill)
        {
            var panel = Ui.Column(parent, 18, 24, KitV2.GridPanel, name);
            var le = panel.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 760;
            fill(panel);
        }

        // ---- stage: slots, worm, power, stats, features, tabs ---------------------------------------------------------

        /// <summary>Board position (px) of a body slot tile: head, left arm, legs on the left; body, right arm, tail on the right.</summary>
        public static Vector2 SlotPos(PartSlot slot)
        {
            switch (slot)
            {
                case PartSlot.Head: return new Vector2(37, 32);
                case PartSlot.LeftArm: return new Vector2(37, 280);
                case PartSlot.Legs: return new Vector2(37, 528);
                case PartSlot.Body: return new Vector2(872, 32);
                case PartSlot.RightArm: return new Vector2(872, 280);
                default: return new Vector2(872, 528);
            }
        }

        private PartTileView.Data SlotData(PartSlot slot)
        {
            State.Parts.Equipped.TryGetValue(slot, out var uid);
            var part = PartsSystem.Get(State, uid);
            var alert = GearGuide.SlotAlert(State, Content, slot, Locks) != PartAlert.None;
            if (part == null)
                return new PartTileView.Data
                {
                    Empty = true, Kind = slot == PartSlot.LeftArm || slot == PartSlot.RightArm ? PartKind.Arm : (PartKind)(int)slot,
                    Caption = slot == PartSlot.LeftArm ? "L. arm" : slot == PartSlot.RightArm ? "R. arm" : slot.ToString(), Alert = alert,
                };
            var d = PartTileView.From(State, Content, part, Locks, false);
            d.Alert = alert;
            d.LevelUp = GearGuide.CanLevelUp(State, Content, part);
            d.Equipped = false; // every slot tile is equipped: no check mark
            return d;
        }

        /// <summary>Everything a slot tile shows (its part key).</summary>
        private string SlotKey(PartSlot slot) => PartTileView.Key(SlotData(slot));

        private void SlotTile(RectTransform stage, PartSlot slot) =>
            PartTileView.Build(stage, SlotData(slot), PartTileView.SlotTile, () => ShowSlot(slot), "Slot:" + slot);

        private void StageWorm(RectTransform stage)
        {
            var img = KitV2.Icon(stage, HeaderSprite(State), 245, 16, 587, 667, "WormPortrait");
            UiCreature.Attach(img);
        }

        private void PowerPlate(RectTransform stage, CombatLoadout loadout)
        {
            KitV2.Box(stage, "power_plate", 395, 694, 373, 80, KitV2.Yellow.top, 34.7f, 6.7f, KitV2.Ink, KitV2.Yellow.bottom, "PowerPlate");
            var icon = KitV2.Circle(stage, "power_icon", 315, 683, 101, KitV2.PowerBadge, (KitV2.Ink, 6.7f));
            if (KitArt.Get("power_icon") == null)
                KitV2.Icon(icon.rectTransform, KitArt.Get("icon_power") ?? RogueStars.Client.World.HudArt.Icon("sword"), 18, 18, 65, 65, "PowerGlyph");
            KitV2.Text(stage, Fmt.Num(loadout.Power), 420, 694, 340, 80, 48, Color.white, TextAnchor.MiddleCenter, "Power");
            var list = KitV2.Box(stage, "list_button", 800, 699, 80, 69, KitV2.ListButton, 21.3f, 5.3f, KitV2.ListRim, null, "StatSheetButton").rectTransform;
            // the kit's list_button is the plain beige plate (no glyph baked): the three list lines stay code-drawn
            for (int i = 0; i < 3; i++) KitV2.Box(list, "list_line", 18, 17 + i * 13, 44, 8, KitV2.Ink, 4, 0, null, null, "Line");
            KitV2.Tap(list, ShowStatSheet);
        }

        private static readonly string[] StatIcons = { "icon_hp", "icon_atk", "icon_def" };
        private static readonly string[] StatHud = { "heart", "sword", "shield" };

        private void StatBars(RectTransform stage, StatId[] stats, string[] values)
        {
            float[] x = { 32, 378, 723 };
            for (int i = 0; i < 3; i++)
            {
                KitV2.Box(stage, "stat_bar", x[i], 811, 325, 80, KitV2.BarFill, 34.7f, 5.3f, KitV2.Ink, null, "StatBar:" + stats[i]);
                KitV2.Icon(stage, KitArt.Get(StatIcons[i]) ?? RogueStars.Client.World.HudArt.Icon(StatHud[i]), x[i] - 11, 812, 78, 78, "StatIcon:" + stats[i]);
                var t = KitV2.Text(stage, values[i], x[i] + 74, 820, 240, 60, 40, Color.white, TextAnchor.MiddleCenter, "StatValue:" + stats[i]);
                KitFit.On(t, 24);
            }
        }

        private sealed class Feature { public string Id, Label, Icon, FeatureId; public bool Unlocked, Badge; public Action Open; public Func<Sprite> Fallback; }

        private List<Feature> FeatureEntries() => new List<Feature>
        {
            new Feature { Id = "talents", Label = "Talents", Icon = "icon_talents", FeatureId = Features.WormLevel, Unlocked = Session.IsUnlocked(Features.WormLevel),
                Badge = Session.IsUnlocked(Features.WormLevel) && TalentActionable(), Open = () => SetTab(WormTab.Levels), Fallback = () => RogueStars.Client.World.HudArt.Icon("exp") },
            new Feature { Id = "runes", Label = "Runes", Icon = "icon_runes", FeatureId = Features.Runes, Unlocked = Session.IsUnlocked(Features.Runes),
                Open = () => App.Router.Push(new PartsScreen { Tab = 1 }), Fallback = () => ArtCatalog.SkillIcon("gen_guard") },
            new Feature { Id = "mounts", Label = "Mounts", Icon = "icon_mounts", FeatureId = Features.Mounts, Unlocked = Session.IsUnlocked(Features.Mounts),
                Open = () => App.Open("mounts"), Fallback = () => ArtCatalog.SliceMount(null) ?? RogueStars.Client.World.HudArt.Mount() },
            new Feature { Id = "laboratory", Label = "Laboratory", Icon = "icon_laboratory", FeatureId = Features.Laboratory, Unlocked = Session.IsUnlocked(Features.Laboratory),
                Open = () => App.Open("laboratory"), Fallback = () => RogueStars.Client.World.HudArt.Icon("book") },
        };

        private void FeatureBand(RectTransform stage, List<Feature> feats)
        {
            KitV2.Box(stage, "feature_band", 32, 907, 1016, 144, KitV2.Band, 37.3f, 0, null, null, "FeatureBand");
            float[] cx = { 160, 413, 667, 921 };
            for (int i = 0; i < feats.Count && i < cx.Length; i++)
            {
                var f = feats[i];
                var hit = KitV2.Place(stage, "Feature:" + f.Id, cx[i] - 110, 907, 220, 144);
                var icon = KitV2.Icon(hit, KitArt.Get(f.Icon) ?? f.Fallback(), 56, 11, 107, 107, "Icon");
                if (!f.Unlocked) { icon.color = new Color(0.35f, 0.33f, 0.4f, 0.75f); KitV2.Icon(hit, RogueStars.Client.World.SummonArt.Icon("lock"), 88, 40, 44, 44, "Lock"); }
                var l = KitV2.Text(hit, f.Label, 10, 99, 200, 42, 32, f.Unlocked ? Color.white : Theme.Hex(0xC8BCA0), TextAnchor.MiddleCenter, "Label");
                KitFit.On(l, 20);
                if (f.Badge) KitV2.BadgeRed(hit, 116, 7, "!", 64, "Badge");
                var ff = f;
                KitV2.Tap(hit, () => { if (ff.Unlocked) ff.Open(); else LockedFeature(ff.Label, ff.FeatureId); });
            }
        }

        private void LockedFeature(string label, string featureId)
        {
            var def = Content.Feature(featureId);
            Modal(label, b => Note(b, label + " unlocks later: " + Requirements.Describe(Content, def?.Requires ?? new RequirementSet()) + "."), new ModalButton("OK", null)).Root.name = "Modal:Locked";
        }

        private static readonly WormTab[] TabOrder = { WormTab.Parts, WormTab.Levels, WormTab.Stats };

        private void Tabs(RectTransform stage, bool levelUnlocked, bool partsDot, bool levelsDot)
        {
            for (int i = 0; i < TabOrder.Length; i++)
            {
                var t = TabOrder[i];
                float x = i * 361.5f;
                bool active = t == _tab;
                string label = t.ToString();
                var tab = active
                    ? KitV2.Box(stage, "tab_active", x, 1051, 357, 152, KitV2.Orange.top, 32, 6.7f, KitV2.Ink, KitV2.Orange.bottom, "Tab:" + label).rectTransform
                    : KitV2.Box(stage, "tab_inactive", x, 1067, 357, 136, KitV2.TabInactive, 32, 6.7f, KitV2.Ink, null, "Tab:" + label).rectTransform;
                bool locked = t == WormTab.Levels && !levelUnlocked;
                KitV2.Text(tab, label, 0, active ? 26 : 22, 357, active ? 59 : 53, active ? 50.7f : 45.3f, locked ? Theme.Hex(0xB0A8D0) : Color.white, TextAnchor.MiddleCenter, "Label");
                if (locked) KitV2.Icon(tab, RogueStars.Client.World.SummonArt.Icon("lock"), 286, 30, 44, 44, "Lock");
                bool dot = t == WormTab.Parts ? partsDot : t == WormTab.Levels && levelsDot;
                if (dot) KitV2.BadgeRed(tab, 281, -15, "!", 64, "Badge");
                var tt = t;
                KitV2.Tap(tab, () =>
                {
                    if (tt == WormTab.Levels && !Session.IsUnlocked(Features.WormLevel)) LockedFeature("Levels", Features.WormLevel);
                    else SetTab(tt);
                });
            }
        }

        /// <summary>The Parts tab's "!": a slot has an action (climb, better part, awaken) or an equipped part can level up.</summary>
        private bool PartsDot()
        {
            if (GearGuide.HasAffordableUpgrade(State, Content)) return true;
            foreach (var slot in PartsSystem.AllSlots) if (GearGuide.SlotAlert(State, Content, slot, Locks) != PartAlert.None) return true;
            return false;
        }

        /// <summary>The list button: every worm stat by group (Core always, the rest when not 0).</summary>
        public void ShowStatSheet()
        {
            var loadout = Session.Engine.BuildLoadout(State, Now);
            Modal("Worm stats", b => Ui.ScrollingColumn(b, 1300, col =>
            {
                foreach (var g in RunStatSheet.Groups)
                {
                    var lines = new List<StatId>();
                    foreach (var st in g.Stats) if (g.Title == "Core" || Math.Abs(loadout.WormStats.Value(st)) > 1e-9) lines.Add(st);
                    if (lines.Count == 0) continue;
                    Ui.Caption(col, g.Title);
                    foreach (var st in lines)
                    {
                        var row = Ui.Row(col, 44, 8, null, 0, "Stat:" + st);
                        Ui.Size(Ui.Label(row, Fmt.StatName(st), 26, Theme.TextDim), -1, -1, 1);
                        Ui.Label(row, StatText(st, loadout.WormStats.Value(st)), 28, Theme.Text, TextAnchor.MiddleRight);
                    }
                }
            }), new ModalButton("Close", null)).Root.name = "Modal:StatSheet";
        }

        /// <summary>Header portrait (W1): the equipped hero's own art when it has one, else the hero worm sprite.</summary>
        public static Sprite HeaderSprite(GameState state)
        {
            string hero = state?.Heroes?.Equipped;
            var own = string.IsNullOrEmpty(hero) ? null : ArtCatalog.Load(RogueStars.Client.World.SummonArt.Root + "/Heroes/" + hero);
            return own ?? RogueStars.Client.World.SummonArt.Featured(SummonEventType.Hero, hero ?? "");
        }

        public static string StatText(StatId st, double v) =>
            st == StatId.Hp || st == StatId.Atk || st == StatId.Def || st == StatId.Speed ? Fmt.Num(v) : Fmt.Pct(v);

        private bool TalentActionable()
        {
            var tier = WormSystem.CurrentTier(State, Content);
            if (tier == null || State.Worm.Level < tier.RequiredWormLevel) return false;
            foreach (var b in tier.Buttons)
            {
                int rank = WormSystem.Rank(State, b.Key);
                if (rank < b.MaxRank && State.Wallet.CanAfford(Currencies.Biomass, b.RankCost(rank))) return true;
            }
            return false;
        }

        // ---- Stats ------------------------------------------------------------------------------------------------------

        private void StatsView(RectTransform body, CombatLoadout loadout)
        {
            var card = Kit.Card(body, KitTone.Card, 20, 12, "StatsCard");
            for (int i = 0; i < ShownStats.Length; i += 2)
            {
                var row = Ui.Row(card, 110, 14, null, 0, "StatRow");
                for (int j = i; j < Mathf.Min(i + 2, ShownStats.Length); j++)
                {
                    var one = new List<(string key, string abbr, Color color, string value)> { StatEntry(ShownStats[j], loadout.WormStats.Value(ShownStats[j])) };
                    var cell = Ui.Column(row, 2, 0, null, "StatCell");
                    Ui.Size(cell, 440, 100, 1);
                    Kit.StatPills(cell, one);
                    Ui.Label(cell, Fmt.StatName(ShownStats[j]), 20, Theme.TextDim, TextAnchor.MiddleLeft, "StatName");
                }
            }
        }

        // ---- Levels: talent stat cards and the progression track --------------------------------------------------------

        private void LevelsView(RectTransform body, CombatLoadout loadout)
        {
            var cfg = Content.WormConfig;
            long cost = WormSystem.NextLevelCost(State, Content);
            var head = Kit.Card(body, KitTone.Raised, 22, 12, "LevelCard");
            var who = Ui.Row(head, 100, 18, null, 0, "Who");
            var portrait = Ui.Rect(who, "WormPortrait");
            Ui.Size(portrait, 120, 100, 0);
            var img = portrait.gameObject.AddComponent<Image>();
            img.sprite = HeaderSprite(State); img.preserveAspect = true; img.raycastTarget = false;
            UiCreature.Attach(img);
            var names = Ui.Column(who, 2, 0, null, "Names");
            names.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Size(names, -1, -1, 1);
            Ui.Display(names, "Worm level " + State.Worm.Level, Theme.FontTitle, Theme.Text);
            Ui.Label(names, "Power " + Fmt.Num(loadout.Power) + "  ·  max level " + cfg.MaxLevel, Theme.FontSmall, Theme.TextDim);
            if (State.Worm.Level >= cfg.MaxLevel || cost < 0) Note(head, "Max level");
            else
            {
                var row = Ui.Row(head, 90, 10);
                Kit.Button(row, "Level up\n<size=22>" + Fmt.Num(cost) + " Biomass</size>", () => Run(new WormLevelUp { Levels = 1 }), KitButtonColor.Positive, -1, 90, State.Wallet.CanAfford(Currencies.Biomass, cost), 30);
                Kit.Button(row, "+10", () => Run(new WormLevelUp { Levels = 10 }), KitButtonColor.Secondary, 160, 90);
            }

            var tier = WormSystem.CurrentTier(State, Content);
            if (tier != null) TalentCards(body, tier);
            else Note(body, "All talent tiers completed.");
            Track(body, tier);
        }

        /// <summary>The current tier's three upgrade buttons as stat cards (HP, ATK, DEF) and the tier completion button.</summary>
        private void TalentCards(RectTransform body, TalentTier tier)
        {
            Kit.Ribbon(body, "Talent tier " + (tier.Index + 1), new Color32(0x8A, 0x5A, 0x3C, 255), 76);
            bool levelOk = State.Worm.Level >= tier.RequiredWormLevel;
            if (!levelOk) Note(body, "Requires worm level " + tier.RequiredWormLevel);
            var row = Ui.Row(body, 330, 12, null, 0, "TalentCards");
            bool allCapped = true;
            foreach (var b in tier.Buttons)
            {
                var btn = b;
                int rank = WormSystem.Rank(State, btn.Key);
                bool maxed = rank >= btn.MaxRank;
                if (!maxed) allCapped = false;
                long c = btn.RankCost(rank);
                var st = btn.PerRank != null ? btn.PerRank.Stat : StatId.Hp;
                var look = StatLook.TryGetValue(st, out var l) ? l : ("", Theme.TextDim);
                var card = Kit.Card(row, KitTone.Raised, 12, 6, "Talent:" + btn.Key);
                card.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
                Ui.Size(card, -1, -1, 1);
                var rankPill = Kit.Pill(card, maxed ? "MAXED" : rank + " / " + btn.MaxRank, maxed ? Theme.Good : (Color?)null, maxed ? Theme.OnAccent : Theme.TextDim, "Rank");
                rankPill.GetComponent<LayoutElement>().flexibleWidth = 1;
                var discRow = Ui.Row(card, 80, 0, null, 0, "IconRow");
                discRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                discRow.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
                var disc = Ui.Dot(discRow, look.Item2, 76, "Icon");
                UiShapes.Outline(disc.rectTransform, Theme.Shade(look.Item2, 0.5f), 38, 4);
                var dt = Kit.Title(disc.transform, look.Item1, 24, Color.white, TextAnchor.MiddleCenter, "Abbr");
                Ui.Stretch(dt.rectTransform);
                double total = btn.PerRank == null ? 0 : (btn.PerRank.Percent != 0 ? btn.PerRank.Percent : btn.PerRank.Flat) * rank;
                var tt = Kit.Title(card, "+" + (btn.PerRank != null && btn.PerRank.Percent != 0 ? Fmt.Pct(total) : Fmt.Whole(total)), 34, Color.white, TextAnchor.MiddleCenter, "Total");
                Ui.Size(tt, -1, 44, 1);
                Ui.Label(card, btn.Key.ToUpperInvariant(), 22, Theme.TextDim, TextAnchor.MiddleCenter, "Key");
                Kit.Button(card, maxed ? "OK" : Fmt.Num(c) + "\nBIO", () => Run(new WormTalentUpgrade { Button = btn.Key, Ranks = 1 }),
                    maxed ? KitButtonColor.Secondary : KitButtonColor.Positive, -1, 76, levelOk && !maxed && State.Wallet.CanAfford(Currencies.Biomass, c), 22).name = "Upgrade:" + btn.Key;
            }
            Kit.Button(body, "Complete tier (" + Fmt.Num(tier.RankUpCost) + " Biomass)", () => Run(new WormTalentTierUp()), KitButtonColor.Primary, -1, 84,
                allCapped && levelOk && State.Wallet.CanAfford(Currencies.Biomass, tier.RankUpCost));
        }

        private static string PerkGlyph(TalentPerk p) =>
            p == null ? "" : p.Kind == TalentPerkKinds.StaminaCap ? "ST" : p.Kind == TalentPerkKinds.RunCoinsPercent ? "$" : p.Kind == TalentPerkKinds.DraftRerolls ? "RR" :
            p.Stat != null && StatLook.TryGetValue(p.Stat.Stat, out var l) ? l.abbr : "+";

        private static Color PerkColor(TalentPerk p) =>
            p == null ? Theme.Border : p.Kind == TalentPerkKinds.StaminaCap ? Theme.Warn : p.Kind == TalentPerkKinds.RunCoinsPercent ? Theme.Hex(0xE3C567) :
            p.Kind == TalentPerkKinds.DraftRerolls ? Theme.Purple : p.Stat != null && StatLook.TryGetValue(p.Stat.Stat, out var l) ? l.color : Theme.Blue;

        private sealed class TrackItem { public int Level; public string Kind, Title, Text, Glyph; public Color Color; public bool Done; }

        /// <summary>
        /// The vertical progression track: worm levels climb the gold spine (highest at the top), talent tiers hang off it as
        /// reward cards (their perk), every milestone level a banner (the milestone's % stats), a marker where the worm is now.
        /// Completed items carry a check.
        /// </summary>
        private void Track(RectTransform body, TalentTier current)
        {
            var cfg = Content.WormConfig;
            int cur = State.Worm.Level;
            int ci = current != null ? current.Index : cfg.TalentTiers.Count;
            int lo = ci > 0 ? cfg.TalentTiers[ci - 1].RequiredWormLevel : 0;
            int hi = ci + 3 < cfg.TalentTiers.Count ? cfg.TalentTiers[ci + 3].RequiredWormLevel : cfg.MaxLevel;
            var items = new List<TrackItem>();
            foreach (var t in cfg.TalentTiers)
                if (t.RequiredWormLevel >= lo && t.RequiredWormLevel <= hi)
                    items.Add(new TrackItem { Level = t.RequiredWormLevel, Kind = "tier", Title = "Tier " + (t.Index + 1), Text = t.Perk != null ? t.Perk.Description : "", Done = t.Index < ci,
                        Glyph = PerkGlyph(t.Perk), Color = PerkColor(t.Perk) });
            if (cfg.MilestoneEvery > 0)
                for (int l = cfg.MilestoneEvery; l <= hi; l += cfg.MilestoneEvery)
                {
                    if (l < lo) continue;
                    var parts = new List<string>();
                    foreach (var m in cfg.MilestoneStats) parts.Add("+" + Fmt.Pct(m.Percent) + " " + Fmt.StatName(m.Stat));
                    items.Add(new TrackItem { Level = l, Kind = "milestone", Title = "Level " + l, Text = string.Join(", ", parts), Done = cur >= l });
                }
            items.Add(new TrackItem { Level = cur, Kind = "now", Title = "You are here" });
            items.Sort((a, b) =>
            {
                int c = b.Level.CompareTo(a.Level);
                return c != 0 ? c : string.CompareOrdinal(b.Kind, a.Kind); // same level: milestone above tier above the marker
            });

            Kit.Ribbon(body, "Worm levels", new Color32(0x5A, 0x4A, 0x9C, 255), 76);
            var col = Ui.Column(body, 0, 0, null, "Track");
            int side = 0;
            foreach (var it in items)
            {
                var row = Ui.Row(col, 0, 0, null, 0, "TrackRow:" + it.Kind + ":" + it.Level);
                Ui.Size(row, -1, it.Kind == "now" ? 90 : it.Kind == "milestone" ? 170 : 230, 1);
                if (it.Kind == "milestone")
                {
                    row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                    var banner = Kit.Card(row, KitTone.Raised, 16, 4, "Milestone");
                    banner.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                    Ui.Size(banner, 820, 150, 0);
                    Kit.Title(banner, it.Title, 38, Theme.Gold, TextAnchor.MiddleCenter, "Title");
                    Ui.Label(banner, it.Text, 24, Theme.Text, TextAnchor.MiddleCenter, "Text");
                    if (it.Done) Kit.CornerBadge(banner, "OK", Theme.Good, "Done", 56);
                    continue;
                }
                bool left = it.Kind == "tier" && side++ % 2 == 0;
                var leftHalf = Ui.Rect(row, "Left"); Ui.Size(leftHalf, -1, -1, 1);
                var spine = Ui.Rect(row, "Spine"); Ui.Size(spine, 90, -1, 0);
                var rightHalf = Ui.Rect(row, "Right"); Ui.Size(rightHalf, -1, -1, 1);
                var bar = Ui.Panel(spine, it.Level > cur ? Theme.Shade(Theme.Gold, 0.55f) : Theme.Gold, "SpineBar");
                var brt = bar.rectTransform;
                brt.anchorMin = new Vector2(0.5f, 0); brt.anchorMax = new Vector2(0.5f, 1); brt.sizeDelta = new Vector2(18, 0);
                bool now = it.Kind == "now";
                var chip = Kit.LevelBadge(spine, "Lv " + (now ? cur : it.Level), now ? Theme.Good : Color.white, 40, "LevelChip");
                chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 0.5f);
                chip.pivot = new Vector2(0.5f, 0.5f);
                chip.anchoredPosition = Vector2.zero;
                chip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                var csf = chip.gameObject.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                if (it.Kind != "tier") continue;
                var half = left ? leftHalf : rightHalf;
                var card = Kit.Card(half, it.Done ? KitTone.Well : KitTone.Raised, 14, 4, "Reward:" + it.Title);
                Ui.Stretch(card, left ? 0 : 6, 10, left ? 6 : 0, 10);
                card.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                var node = Kit.HexNode(card, it.Color, it.Glyph, null, 86, false, it.Done, "PerkNode");
                Ui.Size(node, 86, 86, 0, 0);
                Kit.Title(card, it.Title, 30, it.Done ? Theme.TextDim : Theme.Gold, TextAnchor.MiddleCenter, "Title");
                Ui.Label(card, it.Text, 22, it.Done ? Theme.TextDim : Theme.Text, TextAnchor.MiddleCenter, "Perk");
                if (it.Done) Kit.CornerBadge(card, "OK", Theme.Good, "Done", 48);
            }
        }
    }
}
