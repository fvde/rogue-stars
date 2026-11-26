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
    /// Worm tab = the gear screen, composed from the UI kit: sub-tab chips (Parts, Levels, Stats), the worm on its stage with
    /// the six body slots left and right (tap → the inventory filtered to that slot), the stat bar, the inventory of every
    /// owned part (<see cref="GearScreen"/>), the Levels view (worm level, the talent tier's stat cards and the progression
    /// track with its real rewards) and the main tab bar along the bottom.
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

        private enum WormTab { Parts, Levels, Stats }
        private WormTab _tab = WormTab.Parts;

        protected override string ActiveMainTab => "worm";

        protected override void ShowInventoryView() => _tab = WormTab.Parts;

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
            var loadout = Session.Engine.BuildLoadout(State, Now);
            bool levelUnlocked = Session.IsUnlocked(Features.WormLevel);
            // Levels and the talent road stay hidden until Worm Level unlocks (the hub teases it when it is next).
            var tabs = new List<WormTab> { WormTab.Parts };
            if (levelUnlocked) tabs.Add(WormTab.Levels);
            tabs.Add(WormTab.Stats);
            if (!tabs.Contains(_tab)) _tab = WormTab.Parts;
            var labels = new List<string>();
            var dots = new List<bool>();
            foreach (var t in tabs)
            {
                labels.Add(t == WormTab.Parts ? "Parts" : t == WormTab.Levels ? "Levels" : "Stats");
                dots.Add(t == WormTab.Parts ? GearGuide.HasAffordableUpgrade(State, Content) : t == WormTab.Levels && TalentActionable());
            }
            Kit.TabChips(body, labels, tabs.IndexOf(_tab), i => { _tab = tabs[i]; Rebuild(); }, 84, dots);

            switch (_tab)
            {
                case WormTab.Parts: PartsView(body, loadout); break;
                case WormTab.Levels: LevelsView(body, loadout); break;
                default: StatsView(body, loadout); break;
            }
        }

        // ---- Parts: paperdoll, stat bar, inventory ---------------------------------------------------------------------

        private const float SlotW = 200, SlotH = 212, SlotGap = 12;
        private static readonly PartSlot[] LeftSlots = { PartSlot.Head, PartSlot.LeftArm, PartSlot.Legs };
        private static readonly PartSlot[] RightSlots = { PartSlot.Body, PartSlot.RightArm, PartSlot.Tail };

        private void PartsView(RectTransform body, CombatLoadout loadout)
        {
            float stageH = 3 * SlotH + 2 * SlotGap;
            var paper = Ui.Row(body, stageH, 14, null, 0, "Paperdoll");
            SlotColumn(paper, LeftSlots);
            Stage(paper, loadout, stageH);
            SlotColumn(paper, RightSlots);
            var stats = new List<(string key, string abbr, Color color, string value)>();
            foreach (var st in new[] { StatId.Hp, StatId.Atk, StatId.Def }) stats.Add(StatEntry(st, loadout.WormStats.Value(st)));
            Kit.StatPills(body, stats, "StatBar");
            BuildInventory(body, "Inventory");
        }

        private void SlotColumn(Transform parent, PartSlot[] slots)
        {
            var col = Ui.Column(parent, SlotGap, 0, null, "SlotColumn");
            Ui.Size(col, SlotW, -1, 0);
            foreach (var slot in slots) SlotCard(col, slot);
        }

        private static void Float(RectTransform rt, float anchorY, float y)
        {
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, anchorY);
            rt.pivot = new Vector2(0.5f, anchorY);
            rt.anchoredPosition = new Vector2(0, y);
            var fit = rt.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>The worm on its stage: portrait, name, worm level and power.</summary>
        private void Stage(Transform parent, CombatLoadout loadout, float height)
        {
            var stage = Kit.Surface(parent, Theme.Hex(0x4A2E44), 40, Theme.Border, "panel_stage", "Stage");
            Ui.Size(stage, -1, height, 1);
            var glow = Ui.Dot(stage.transform, new Color(1f, 0.75f, 0.9f, 0.16f), 380, "Glow");
            var grt = glow.rectTransform;
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.52f); grt.sizeDelta = new Vector2(380, 380); grt.anchoredPosition = Vector2.zero;
            glow.GetComponent<LayoutElement>().ignoreLayout = true;
            var portrait = Ui.Rect(stage.transform, "WormPortrait");
            Ui.Stretch(portrait, 8, 80, 8, 110);
            var img = portrait.gameObject.AddComponent<Image>();
            img.sprite = HeaderSprite(State);
            img.preserveAspect = true; img.raycastTarget = false;
            UiCreature.Attach(img);
            var name = Kit.Title(stage.transform, State.DisplayName ?? "Worm", 34, Theme.Cream, TextAnchor.UpperCenter, "WormName");
            Ui.Stretch(name.rectTransform, 8, 16, 8, 0);
            KitFit.On(name, 18);
            Float(Kit.LevelBadge(stage.transform, "Power " + Fmt.Num(loadout.Power), Theme.Gold, 48, "Power"), 0, 14);
            Float(Kit.LevelBadge(stage.transform, "Lv " + State.Worm.Level, Color.white, 44, "WormLevel"), 0, 72);
        }

        protected override void InventoryFooterExtras(RectTransform row)
        {
            if (Session.IsUnlocked(Features.Runes))
                Ui.Button(row, "Runes", () => App.Router.Push(new PartsScreen { Tab = 1 }), Theme.Button, 220, 80, true, 26).name = "OpenRunes";
        }

        /// <summary>One body slot on the paperdoll: rarity card with the equipped part (name, level), or an empty socket with the spare count; ▲ when it can level up.</summary>
        private void SlotCard(Transform col, PartSlot slot)
        {
            State.Parts.Equipped.TryGetValue(slot, out var uid);
            var part = PartsSystem.Get(State, uid);
            int spare = GearGuide.SpareFor(State, Content, slot);
            var rc = part == null ? Theme.Border : RarityColor(part.Rarity);
            bool selected = SlotFilter == GearGuide.FilterFor(slot) && SlotFilter != GearSlotFilter.All;
            var btn = Kit.RarityCard(col, "Slot:" + slot, rc, () => ShowSlot(slot), out var c, 30, 14, 3, false, selected ? Theme.Accent : (Color?)null, part == null ? 0.08f : 0.36f);
            Ui.Size(btn, -1, SlotH, 0);
            var head = Ui.Row(c, 28, 0, null, 0, "SlotHead");
            Ui.Spacer(head, 32).GetComponent<LayoutElement>().flexibleWidth = 0;
            var slotName = Ui.Label(head, GearGuide.SlotName(slot).ToUpperInvariant(), 20, selected ? Theme.Accent : Theme.TextDim, TextAnchor.MiddleLeft, "SlotName");
            slotName.fontStyle = FontStyle.Bold;
            KitFit.On(slotName, 12);
            Ui.Spacer(head, part != null ? 50 : 0).GetComponent<LayoutElement>().flexibleWidth = 0; // room for the level-up badge
            if (part == null)
            {
                Ui.Display(c, "Empty", 28, Theme.TextFaint, TextAnchor.MiddleCenter, "Name");
                Ui.Label(c, spare > 0 ? spare + " part" + (spare == 1 ? "" : "s") + " fit" + (spare == 1 ? "s" : "") : "no part yet", 20, spare > 0 ? Theme.Accent : Theme.TextFaint, TextAnchor.MiddleCenter, "Spare");
            }
            else
            {
                var name = Ui.Display(c, PartName(Content, part), 26, Color.Lerp(rc, Color.white, 0.6f), TextAnchor.UpperLeft, "Name");
                Ui.Size(name, -1, 62, 1);
                var lvRow = Ui.Row(c, 36, 4, null, 0, "Level");
                Kit.LevelBadge(lvRow, "Lv " + part.Level, Color.white, 34);
                Ui.Label(c, spare > 0 ? "+" + spare + " spare" : "", 20, Theme.TextDim, TextAnchor.MiddleLeft, "Spare");
                if (GearGuide.CanLevelUp(State, Content, part)) Badge(c, "▲", "LevelUpBadge");
            }
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
