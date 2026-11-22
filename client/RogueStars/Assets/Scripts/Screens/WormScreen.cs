using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

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
            Ui.Label(head, "Level " + State.Worm.Level + "  ·  Power " + Fmt.Num(loadout.Power), Theme.FontLarge, Theme.Warn);
            bool levelUnlocked = Session.IsUnlocked(Features.WormLevel);
            long cost = WormSystem.NextLevelCost(State, Content);
            if (!levelUnlocked) Note(head, "Worm levels unlock later (" + Requirements.Describe(Content, Content.Feature(Features.WormLevel)?.Requires ?? new RequirementSet()) + ").");
            else if (State.Worm.Level >= cfg.MaxLevel || cost < 0) Note(head, "Max level");
            else
            {
                var row = Ui.Row(head, 90, 10);
                Ui.Button(row, "Level up\n<size=22>" + Fmt.Num(cost) + " Biomass</size>", () => Run(new WormLevelUp { Levels = 1 }), Theme.Good, -1, 90, State.Wallet.CanAfford(Currencies.Biomass, cost), 30);
                Ui.Button(row, "+10", () => Run(new WormLevelUp { Levels = 10 }), Theme.Button, 160, 90);
            }

            Section(body, "Body slots");
            if (AutoEquipCommands().Count > 0)
                Ui.Button(body, "Equip best parts", () => { foreach (var c in AutoEquipCommands()) Run(c); }, Theme.Good).name = "AutoEquip";
            var grid = Ui.Grid(body, new Vector2(330, 150), 3, 12);
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
            {
                var sl = slot;
                State.Parts.Equipped.TryGetValue(sl, out var uid);
                var part = PartsSystem.Get(State, uid);
                string label = Fmt.Humanize(sl.ToString()) + "\n<size=22>" + (part == null ? "empty" : PartsScreen.PartName(Content, part) + " Lv " + part.Level) + "</size>";
                var color = part == null ? Theme.Locked : Color.Lerp(PartsScreen.RarityColor(part.Rarity), Theme.Panel, 0.5f);
                Ui.Button(grid, label, () => EquipModal(sl), color, -1, 150, true, 30).name = "Slot:" + sl;
            }

            Section(body, "Stats");
            var stats = Ui.Grid(body, new Vector2(500, 48), 2, 6);
            foreach (var st in ShownStats)
            {
                var t = Ui.Label(stats, Fmt.Humanize(st.ToString()) + ": " + StatText(st, loadout.WormStats.Value(st)), Theme.FontSmall);
                t.gameObject.name = "Stat:" + st;
            }
            Talents(body);
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
