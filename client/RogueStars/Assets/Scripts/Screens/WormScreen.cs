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
    /// <summary>Worm: level (Biomass), stats and power, talent road and the six body slots.</summary>
    public sealed class WormScreen : UiScreen
    {
        public static readonly StatId[] ShownStats =
            { StatId.Hp, StatId.Atk, StatId.Def, StatId.Speed, StatId.CritChance, StatId.CritDamage, StatId.Dodge, StatId.ComboChance, StatId.CounterChance, StatId.LifeSteal };

        public override string Title => "Worm";

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

            var slotsHead = Ui.Row(body, Theme.RowHeight, 12);
            Section(slotsHead, "Body slots");
            if (Session.IsUnlocked(Features.Parts)) Ui.Button(slotsHead, "All parts", () => App.Open("parts"), Theme.Accent, 260).name = "OpenParts";
            if (AutoEquipCommands().Count > 0)
                Ui.Button(body, "Equip best parts", () => { foreach (var c in AutoEquipCommands()) Run(c); }, Theme.Good).name = "AutoEquip";
            var grid = Ui.Grid(body, new Vector2(296, 170), 3, 14);
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
            {
                var sl = slot;
                State.Parts.Equipped.TryGetValue(sl, out var uid);
                var part = PartsSystem.Get(State, uid);
                var rarity = part == null ? Theme.Border : PartsScreen.RarityColor(part.Rarity);
                string label = Fmt.Humanize(sl.ToString()).ToUpperInvariant() + "\n<size=22>" + (part == null ? "empty" : PartsScreen.PartName(Content, part) + " · Lv " + part.Level) + "</size>";
                var b = Ui.Button(grid, label, () => EquipModal(sl), part == null ? new Color(0, 0, 0, 0) : Theme.Tint(rarity, 0.25f), -1, 170, true, 28);
                b.name = "Slot:" + sl;
                b.GetComponentInChildren<Text>().color = part == null ? Theme.TextFaint : Color.Lerp(rarity, Color.white, 0.55f);
                UiShapes.Outline((RectTransform)b.transform, rarity, Theme.RadiusButton, part == null ? 3 : 5);
            }

            Section(body, "Stats");
            var stats = Ui.Grid(body, new Vector2(450, 48), 2, 6);
            foreach (var st in ShownStats)
            {
                var t = Ui.Label(stats, Fmt.Humanize(st.ToString()) + ": " + StatText(st, loadout.WormStats.Value(st)), Theme.FontSmall);
                t.gameObject.name = "Stat:" + st;
            }
            if (levelUnlocked) Talents(body); // the talent road unlocks with worm levels (onboarding v2)
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
                    var def = PartsSystem.Def(Content, p);
                    bool fits = def.Kind == PartKind.Arm ? (slot == PartSlot.LeftArm || slot == PartSlot.RightArm) : (int)def.Kind == (int)slot;
                    if (!fits) continue;
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

        private void EquipModal(PartSlot slot)
        {
            Modal("Equip " + Fmt.Humanize(slot.ToString()), b =>
            {
                if (State.Parts.Equipped.ContainsKey(slot))
                    Ui.Button(b, "Unequip", () => Run(new PartUnequip { Slot = slot }), Theme.Bad, -1, 76);
                int n = 0;
                foreach (var uid in PartsSystem.SortedPartUids(State))
                {
                    var p = PartsSystem.Get(State, uid);
                    var def = PartsSystem.Def(Content, p);
                    bool fits = def.Kind == PartKind.Arm ? (slot == PartSlot.LeftArm || slot == PartSlot.RightArm) : (int)def.Kind == (int)slot;
                    if (!fits || PartsSystem.IsEquipped(State, uid)) continue;
                    n++;
                    string id = uid;
                    Ui.Button(b, PartsScreen.PartName(Content, p) + " Lv " + p.Level, () => Run(new PartEquip { Uid = id, Slot = slot }),
                        Color.Lerp(PartsScreen.RarityColor(p.Rarity), Theme.Panel, 0.5f), -1, 76);
                    if (n >= 8) break;
                }
                if (n == 0) Note(b, "No spare part fits this slot.");
            }, new ModalButton("Close", null));
        }
    }
}
