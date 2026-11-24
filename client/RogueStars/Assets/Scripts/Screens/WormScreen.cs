using System;
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
    /// Worm tab = the gear screen: worm card (level, power, Biomass level up), the level-up callout when an equipped part can
    /// be levelled with Ore, the six body slots (tap → the inventory filtered to that slot), the inventory of every owned
    /// part (<see cref="GearScreen"/>), stats and the talent road.
    /// </summary>
    public sealed class WormScreen : GearScreen
    {
        public static readonly StatId[] ShownStats =
            { StatId.Hp, StatId.Atk, StatId.Def, StatId.Speed, StatId.CritChance, StatId.CritDamage, StatId.Dodge, StatId.ComboChance, StatId.CounterChance, StatId.LifeSteal };

        public override string Title => "Worm";
        public override string[] IntroFeatures => new[] { Features.Parts, Features.WormLevel };

        protected override void Build(RectTransform body)
        {
            var loadout = Session.Engine.BuildLoadout(State, Now);
            var cfg = Content.WormConfig;
            var head = Card(body, Theme.Header);
            var who = Ui.Row(head, 110, 22);
            var pill = Ui.RoundPanel(who, Theme.Worm, 40, "WormPill");
            Ui.Size(pill, 190, 80, 0);
            UiShapes.Outline(pill.rectTransform, Theme.WormLight, 40, 5);
            var eye = Ui.Dot(pill.transform, Theme.OnAccent, 14, "Eye");
            eye.rectTransform.anchorMin = eye.rectTransform.anchorMax = new Vector2(0.8f, 0.62f);
            eye.rectTransform.sizeDelta = new Vector2(14, 14);
            var names = Ui.Column(who, 0, 0, null, "Names");
            names.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Size(names, -1, -1, 1);
            Ui.Display(names, State.DisplayName ?? "Worm", Theme.FontTitle, Theme.Text);
            Ui.Label(names, "Worm level " + State.Worm.Level + "  ·  Power " + Fmt.Num(loadout.Power), Theme.FontNormal, Theme.TextDim);
            bool levelUnlocked = Session.IsUnlocked(Features.WormLevel);
            long cost = WormSystem.NextLevelCost(State, Content);
            // Levels and the talent road stay hidden until Worm Level unlocks (the hub teases it when it is next).
            if (levelUnlocked && (State.Worm.Level >= cfg.MaxLevel || cost < 0)) Note(head, "Max level");
            else if (levelUnlocked)
            {
                var row = Ui.Row(head, 90, 10);
                Ui.Button(row, "Level up\n<size=22>" + Fmt.Num(cost) + " Biomass</size>", () => Run(new WormLevelUp { Levels = 1 }), Theme.Good, -1, 90, State.Wallet.CanAfford(Currencies.Biomass, cost), 30);
                Ui.Button(row, "+10", () => Run(new WormLevelUp { Levels = 10 }), Theme.Button, 160, 90);
            }

            UpgradeCallout(body);
            if (AutoEquipCommands().Count > 0)
                Ui.Button(body, "Equip best parts", () => { foreach (var c in AutoEquipCommands()) Run(c); }, Theme.Good).name = "AutoEquip";

            var slotsHead = Ui.Row(body, 64, 12, null, 0, "SlotsHead");
            Section(slotsHead, "Equipped");
            Ui.Label(slotsHead, "tap a slot to see its parts", 22, Theme.TextDim, TextAnchor.MiddleRight);
            var grid = Ui.Grid(body, new Vector2(TileWidth, 200), 3, TileGap);
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot))) SlotCard(grid, slot);

            BuildInventory(body, "Inventory");

            Section(body, "Stats");
            var stats = Ui.Grid(body, new Vector2(450, 48), 2, 6);
            foreach (var st in ShownStats)
            {
                var t = Ui.Label(stats, Fmt.StatName(st) + ": " + StatText(st, loadout.WormStats.Value(st)), Theme.FontSmall);
                t.gameObject.name = "Stat:" + st;
            }
            if (levelUnlocked) Talents(body); // the talent road unlocks with worm levels (onboarding v2)
        }

        protected override void InventoryFooterExtras(RectTransform row)
        {
            if (Session.IsUnlocked(Features.Runes))
                Ui.Button(row, "Runes", () => App.Router.Push(new PartsScreen { Tab = 1 }), Theme.Button, 220, 80, true, 26).name = "OpenRunes";
        }

        /// <summary>
        /// "Level up ready": the first equipped part the wallet can level now (name, level, Ore price) with a direct LEVEL UP
        /// button and the count of the others; nothing when no equipped part is affordable.
        /// </summary>
        private void UpgradeCallout(Transform body)
        {
            var ready = GearGuide.UpgradableEquipped(State, Content);
            if (ready.Count == 0) return;
            var p = ready[0];
            GearGuide.NextLevelCost(Content, p, out long ore, out long cores);
            var card = Ui.Card(body, Color.Lerp(Theme.Panel, Theme.Good, 0.18f), Theme.Good, Theme.RadiusCard, 22, 8, 4, "UpgradeCallout");
            Ui.Display(card, "▲ Level up ready", Theme.FontLarge, Theme.Good, TextAnchor.MiddleLeft, "CalloutTitle");
            Ui.Label(card, "Ore levels up your parts. " + ready.Count + " equipped part" + (ready.Count == 1 ? " is" : "s are") + " ready.", 24, Theme.Text);
            var row = Ui.Row(card, 88, 10);
            string uid = p.Uid;
            Ui.Button(row, "LEVEL UP " + PartName(Content, p) + "\n<size=22>to Lv " + (p.Level + 1) + " · " + Fmt.Num(ore) + " Ore" + (cores > 0 ? " + " + Fmt.Num(cores) + " Gear Cores" : "") + "</size>",
                () => Run(new PartLevelUp { Uid = uid }), Theme.Good, -1, 88, true, 26).name = "QuickLevelUp";
            Ui.Button(row, "Details", () => Detail(uid), Theme.Button, 190, 88, true, 26).name = "CalloutDetails";
        }

        /// <summary>One body slot: name, the equipped part (rarity colour, level) or "Empty" with the spare count; ▲ when it can level up.</summary>
        private void SlotCard(Transform grid, PartSlot slot)
        {
            State.Parts.Equipped.TryGetValue(slot, out var uid);
            var part = PartsSystem.Get(State, uid);
            int spare = GearGuide.SpareFor(State, Content, slot);
            var rc = part == null ? Theme.Border : RarityColor(part.Rarity);
            bool selected = SlotFilter == GearGuide.FilterFor(slot) && SlotFilter != GearSlotFilter.All;
            var btn = Ui.CardButton(grid, "Slot:" + slot, part == null ? Theme.PanelAlt : Color.Lerp(Theme.Panel, rc, 0.3f), selected ? Theme.Accent : rc, () => ShowSlot(slot), out var c, 30, 16, 4);
            Ui.Label(c, GearGuide.SlotName(slot).ToUpperInvariant(), 22, selected ? Theme.Accent : Theme.TextDim, TextAnchor.MiddleLeft, "SlotName").fontStyle = FontStyle.Bold;
            if (part == null)
            {
                Ui.Display(c, "Empty", 28, Theme.TextFaint, TextAnchor.UpperLeft, "Name");
                Ui.Label(c, spare > 0 ? spare + " part" + (spare == 1 ? "" : "s") + " fit" + (spare == 1 ? "s" : "") : "no part yet", 22, spare > 0 ? Theme.Accent : Theme.TextFaint, TextAnchor.MiddleLeft, "Spare");
            }
            else
            {
                var name = Ui.Display(c, PartName(Content, part), 28, Color.Lerp(rc, Color.white, 0.6f), TextAnchor.UpperLeft, "Name");
                Ui.Size(name, -1, 66, 1);
                Ui.Label(c, RarityName(Content, part.Rarity) + " · Lv " + part.Level, 22, Theme.Text, TextAnchor.MiddleLeft, "Level");
                Ui.Label(c, spare > 0 ? "+" + spare + " spare" : "", 20, Theme.TextDim, TextAnchor.MiddleLeft, "Spare");
                if (GearGuide.CanLevelUp(State, Content, part)) Badge(c, "▲", "LevelUpBadge");
            }
        }

        /// <summary>Commands that fill empty slots with the best unequipped fitting part (rarity, then level).</summary>
        public List<PartEquip> AutoEquipCommands()
        {
            var cmds = new List<PartEquip>();
            var used = new HashSet<string>();
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
            {
                if (State.Parts.Equipped.ContainsKey(slot)) continue;
                PartItem best = null;
                foreach (var uid in PartsSystem.SortedPartUids(State))
                {
                    if (used.Contains(uid) || PartsSystem.IsEquipped(State, uid)) continue;
                    var p = PartsSystem.Get(State, uid);
                    if (!GearGuide.Fits(PartsSystem.Def(Content, p), slot)) continue;
                    if (best == null || p.Rarity > best.Rarity || (p.Rarity == best.Rarity && p.Level > best.Level)) best = p;
                }
                if (best == null) continue;
                used.Add(best.Uid);
                cmds.Add(new PartEquip { Uid = best.Uid, Slot = slot });
            }
            return cmds;
        }

        public static string StatText(StatId st, double v) =>
            st == StatId.Hp || st == StatId.Atk || st == StatId.Def || st == StatId.Speed ? Fmt.Num(v) : Fmt.Pct(v);

        private void Talents(RectTransform body)
        {
            Section(body, "Talent road");
            var tier = WormSystem.CurrentTier(State, Content);
            if (tier == null) { Note(body, "All talent tiers completed."); return; }
            var card = Card(body, Theme.PanelAlt);
            Ui.Label(card, "Tier " + (tier.Index + 1) + (tier.Perk != null ? " — perk: " + tier.Perk.Description : ""), Theme.FontNormal);
            bool levelOk = State.Worm.Level >= tier.RequiredWormLevel;
            if (!levelOk) Note(card, "Requires worm level " + tier.RequiredWormLevel);
            bool allCapped = true;
            foreach (var b in tier.Buttons)
            {
                var btn = b;
                int rank = WormSystem.Rank(State, btn.Key);
                if (rank < btn.MaxRank) allCapped = false;
                long c = btn.RankCost(rank);
                var row = Ui.Row(card, 80, 10);
                string per = btn.PerRank == null ? "" : " (+" + (btn.PerRank.Percent != 0 ? Fmt.Pct(btn.PerRank.Percent) : btn.PerRank.Flat.ToString("0")) + " " + btn.PerRank.Stat + ")";
                Ui.Label(row, btn.Key.ToUpperInvariant() + per + "  " + rank + "/" + btn.MaxRank, Theme.FontSmall);
                Ui.Button(row, rank >= btn.MaxRank ? "Max" : Fmt.Num(c) + " BIO", () => Run(new WormTalentUpgrade { Button = btn.Key, Ranks = 1 }), Theme.Good, 240, 70,
                    levelOk && rank < btn.MaxRank && State.Wallet.CanAfford(Currencies.Biomass, c), 26);
            }
            Ui.Button(card, "Complete tier (" + Fmt.Num(tier.RankUpCost) + " Biomass)", () => Run(new WormTalentTierUp()), Theme.Accent, -1, 80,
                allCapped && levelOk && State.Wallet.CanAfford(Currencies.Biomass, tier.RankUpCost));
        }
    }
}
