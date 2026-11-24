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
    /// Shared base of the gear screens (Worm tab and Parts): the inventory of ALL owned parts (slot filter with counts,
    /// rarity filter, sort; tiles with slot, rarity, level, EQUIPPED and a ▲ level-up badge), the part detail (stats now →
    /// next level, Ore level up, equip, lock, skills by rarity, merge, Replicator, runes, Forge, awakening, dismantle), the
    /// merge picker (copies, fodder points in LE, Ore, best-fodder auto-select, confirmation before protected parts are
    /// used), the Replicator, the awakening preview and the rune list. Inventory queries: <see cref="GearGuide"/>; material
    /// logic: <see cref="PartPlanner"/>.
    /// </summary>
    public abstract class GearScreen : UiScreen
    {
        // inventory view
        private GearSlotFilter _slotFilter = GearSlotFilter.All;
        private int _rarityFilter = -1;
        private GearSort _sort = GearSort.Best;
        private RectTransform _inventoryAnchor;
        private bool _scrollToInventory;

        // merge picker state
        private string _pickTarget;
        private bool _pickReplicate;
        private readonly List<string> _picked = new List<string>();

        public GearSlotFilter SlotFilter => _slotFilter;
        public int RarityFilter => _rarityFilter;
        public GearSort Sort => _sort;
        /// <summary>Uids of the tiles of the last built inventory, in display order (tests).</summary>
        public readonly List<string> ShownParts = new List<string>();

        public static Color RarityColor(int partRarity) => Theme.PartRarityColor(partRarity);

        public static string RarityName(ContentDb c, int r) => c.PartsConfig.RarityDef(r).Name;

        public static string PartName(ContentDb c, PartItem p) => Fmt.PartName(c, p);

        protected HashSet<string> Locks => PartLocks.For(State.PlayerId);

        /// <summary>Filter the inventory (and keep the other filters).</summary>
        public void SetFilter(GearSlotFilter slot, int rarity, GearSort sort)
        {
            _slotFilter = slot; _rarityFilter = rarity; _sort = sort;
            Rebuild();
        }

        /// <summary>From a body slot to its parts: the inventory filtered to the slot (any rarity), scrolled into view.</summary>
        public void ShowSlot(PartSlot slot)
        {
            _slotFilter = GearGuide.FilterFor(slot);
            _rarityFilter = -1;
            _scrollToInventory = true;
            Rebuild();
        }

        /// <summary>Scroll to the inventory after the next build (Ore's "Go").</summary>
        public void FocusInventory() { _scrollToInventory = true; Rebuild(); }

        // ---- inventory -----------------------------------------------------------------------------

        /// <summary>The inventory: header (count, Ore), slot tabs with counts, sort + rarity filter, the tile grid, quick merge.</summary>
        protected void BuildInventory(Transform body, string title)
        {
            var head = Ui.Row(body, 72, 12, null, 0, "InventoryHead");
            _inventoryAnchor = head;
            Section(head, title + " · " + State.Parts.Items.Count);
            var ore = Ui.Button(head, "ORE " + Fmt.Num(State.Wallet.Get(Currencies.Ore)), () => App.Bar.ShowCurrency(Currencies.Ore), Theme.Button, 250, 64, true, 24);
            ore.name = "OreChip";

            var labels = new List<string>();
            foreach (var f in GearGuide.SlotFilters)
            {
                int n = 0;
                foreach (var p in State.Parts.Items.Values) if (GearGuide.Matches(PartsSystem.Def(Content, p), f)) n++;
                labels.Add(GearGuide.SlotFilterLabel(f) + "\n<size=20>" + n + "</size>");
            }
            var tabs = Ui.TabBar(body, labels, Array.IndexOf(GearGuide.SlotFilters, _slotFilter), i => SetFilter(GearGuide.SlotFilters[i], _rarityFilter, _sort), 92);
            for (int i = 0; i < tabs.Buttons.Count; i++) tabs.Buttons[i].name = "Filter:" + GearGuide.SlotFilters[i];

            var tools = Ui.Row(body, 72, 10, null, 0, "InventoryTools");
            var sortLabels = new List<string>();
            foreach (var s in GearGuide.Sorts) sortLabels.Add(GearGuide.SortLabel(s));
            var sorts = Ui.TabBar(tools, sortLabels, Array.IndexOf(GearGuide.Sorts, _sort), i => SetFilter(_slotFilter, _rarityFilter, GearGuide.Sorts[i]), 72);
            for (int i = 0; i < sorts.Buttons.Count; i++) sorts.Buttons[i].name = "Sort:" + GearGuide.Sorts[i];
            Ui.Size(sorts.Buttons[0].transform.parent, -1, 72, 1);
            string rarityText = _rarityFilter < 0 ? "Any rarity" : RarityName(Content, _rarityFilter);
            var rb = Ui.Button(tools, rarityText, RarityPicker, _rarityFilter < 0 ? Theme.Button : Color.Lerp(RarityColor(_rarityFilter), Theme.Panel, 0.4f), 260, 72, true, 24);
            rb.name = "RarityFilter";

            var list = GearGuide.Inventory(State, Content, _slotFilter, _rarityFilter, _sort);
            ShownParts.Clear();
            var grid = Ui.Grid(body, new Vector2(TileWidth, TileHeight), 3, TileGap);
            grid.GetComponent<GridLayoutGroup>().childAlignment = TextAnchor.UpperLeft; // a short last row starts at the left
            foreach (var p in list)
            {
                ShownParts.Add(p.Uid);
                PartTile(grid, p);
            }
            if (list.Count == 0)
                Note(body, State.Parts.Items.Count == 0 ? "No parts yet. Parts drop from runs and Part Signals."
                    : "No " + (_rarityFilter >= 0 ? RarityName(Content, _rarityFilter) + " " : "") + (_slotFilter == GearSlotFilter.All ? "" : GearGuide.SlotFilterLabel(_slotFilter) + " ") + "parts yet.");

            var foot = Ui.Row(body, 80, 10, null, 0, "InventoryFoot");
            Ui.Button(foot, "Quick merge\n<size=20>3→1 up to " + RarityName(Content, (int)Content.PartsConfig.QuickMergeMaxTarget) + "</size>", QuickMerge, Theme.Button, -1, 80, State.Parts.Items.Count >= 3, 26).name = "QuickMerge";
            InventoryFooterExtras(foot);

            if (_scrollToInventory) ScrollToAfterBuild(head);
            _scrollToInventory = false;
        }

        /// <summary>More buttons next to Quick merge (the Worm adds Runes).</summary>
        protected virtual void InventoryFooterExtras(RectTransform row) { }

        /// <summary>Tile grid: three columns of 296 + 2 × 14 = 916 ≤ the 930-unit body (978 − 2 × 24 padding).</summary>
        public const float TileWidth = 296, TileHeight = 232, TileGap = 14;

        /// <summary>One part tile: slot, name, rarity and level, EQUIPPED / slot, LOCKED, and the ▲ badge when it can level up now.</summary>
        private void PartTile(Transform grid, PartItem p)
        {
            var def = PartsSystem.Def(Content, p);
            var rc = RarityColor(p.Rarity);
            var slot = GearGuide.EquippedSlot(State, p.Uid);
            bool eq = slot.HasValue;
            // equipped: green outline; spare: the rarity colour
            Ui.CardButton(grid, "Part:" + p.Uid, Color.Lerp(Theme.Panel, rc, eq ? 0.34f : 0.2f), eq ? Theme.Good : Color.Lerp(rc, Theme.Panel, 0.2f), () => Detail(p.Uid), out var c, 30, 16, 4);
            Ui.Label(c, def.Kind.ToString().ToUpperInvariant() + (def.Kind == PartKind.Arm ? " · " + def.Style.ToString().ToUpperInvariant() : ""), 20, Theme.TextDim, TextAnchor.MiddleLeft, "Kind").fontStyle = FontStyle.Bold;
            var name = Ui.Display(c, PartName(Content, p), 28, Color.Lerp(rc, Color.white, 0.6f), TextAnchor.UpperLeft, "Name");
            Ui.Size(name, -1, 66, 1);
            Ui.Label(c, RarityName(Content, p.Rarity) + " · Lv " + p.Level + "/" + PartsSystem.LevelCap(Content, p), 22, Theme.Text, TextAnchor.MiddleLeft, "Level");
            var chips = Ui.ChipRow(c, 38, 6);
            if (eq) Ui.Chip(chips, slot.Value == PartSlot.LeftArm ? "LEFT ARM" : slot.Value == PartSlot.RightArm ? "RIGHT ARM" : "EQUIPPED", Theme.Good, Theme.OnAccent, 18, 36, null, "Equipped");
            if (Locks.Contains(p.Uid)) Ui.Chip(chips, "LOCKED", null, Theme.Warn, 18, 36, null, "Locked");
            if (p.Awaken > 0) Ui.Chip(chips, "AWAKE", null, Theme.Premium, 18, 36);
            if (eq && GearGuide.CanLevelUp(State, Content, p)) Badge(c, "▲", "LevelUpBadge");
        }

        /// <summary>A round green badge in the top-right corner of a card (outside its layout).</summary>
        protected static void Badge(Transform card, string text, string name)
        {
            var b = Ui.Dot(card, Theme.Good, 52, name);
            b.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = b.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
            rt.sizeDelta = new Vector2(52, 52);
            rt.anchoredPosition = new Vector2(-8, -8);
            var t = Ui.Label(b.transform, text, 26, Theme.OnAccent, TextAnchor.MiddleCenter, "BadgeText");
            t.fontStyle = FontStyle.Bold;
            Ui.Stretch(t.rectTransform);
        }

        private void RarityPicker()
        {
            Modal("Rarity", b =>
            {
                Ui.Button(b, "Any rarity", () => SetFilter(_slotFilter, -1, _sort), Theme.Button, -1, 76).name = "Rarity:any";
                foreach (int r in GearGuide.OwnedRarities(State))
                {
                    int rr = r;
                    Ui.Button(b, RarityName(Content, r), () => SetFilter(_slotFilter, rr, _sort), Color.Lerp(RarityColor(r), Theme.Panel, 0.4f), -1, 76).name = "Rarity:" + r;
                }
            }, new ModalButton("Close", null));
        }

        /// <summary>Quick merge (rules: 3→1 below Legendary, never equipped parts); asks first if locked parts could be used.</summary>
        protected void QuickMerge()
        {
            int locked = 0;
            foreach (var p in PartPlanner.Candidates(State, null))
                if (Locks.Contains(p.Uid) && p.Rarity < (int)Content.PartsConfig.QuickMergeMaxTarget) locked++;
            if (locked == 0) { Run(new PartQuickMerge()); return; }
            Modal("Quick merge", b => Note(b, "Quick merge may use " + locked + " locked part(s) below " + RarityName(Content, (int)Content.PartsConfig.QuickMergeMaxTarget) + " as fodder."),
                new ModalButton("Cancel", null), new ModalButton("Merge anyway", () => Run(new PartQuickMerge()), Theme.Warn));
        }

        // ---- part detail -------------------------------------------------------------------------

        /// <summary>Tallest the detail's scrolling body gets (it scrolls beyond).</summary>
        public const float DetailMaxHeight = 1240;

        public void Detail(string uid)
        {
            var p = PartsSystem.Get(State, uid);
            if (p == null) return;
            Ui.Clear(App.ModalLayer);
            var def = PartsSystem.Def(Content, p);
            var cfg = Content.PartsConfig;
            var slot = GearGuide.EquippedSlot(State, uid);
            bool locked = Locks.Contains(uid);
            var rc = RarityColor(p.Rarity);
            var modal = Modal(PartName(Content, p), outer => Ui.ScrollingColumn(outer, DetailMaxHeight, b =>
            {
                // who: rarity, slot, family, equipped
                var tags = Ui.ChipRow(b, 46, 8);
                Ui.Chip(tags, RarityName(Content, p.Rarity).ToUpperInvariant(), rc, Theme.TextOn(rc), 22, 44, null, "Rarity");
                Ui.Chip(tags, def.Kind.ToString().ToUpperInvariant() + (def.Kind == PartKind.Arm ? " · " + def.Style.ToString().ToUpperInvariant() : ""), null, Theme.Text, 22, 44);
                Ui.Chip(tags, SkillText.FamilyName(def.Family).ToUpperInvariant(), null, Theme.TextDim, 22, 44);
                if (slot.HasValue) Ui.Chip(tags, "ON " + GearGuide.SlotName(slot.Value).ToUpperInvariant(), Theme.Good, Theme.OnAccent, 22, 44, null, "EquippedChip");
                if (p.Awaken > 0) Note(b, "Awakened (" + def.Name + ")");

                LevelCard(b, p);

                // stats: now → next level
                Ui.Caption(b, "Stats");
                var stats = PartsSystem.PartStats(State, Content, p);
                StatBlock next = null;
                if (GearGuide.NextLevelCost(Content, p, out _, out _))
                    next = PartsSystem.PartStats(State, Content, new PartItem { Uid = p.Uid, DefId = p.DefId, Rarity = p.Rarity, Level = p.Level + 1, CapRaises = p.CapRaises, ForgedSlots = p.ForgedSlots, Awaken = p.Awaken, Runes = p.Runes });
                var sg = Ui.Column(b, 2, 0, null, "Stats");
                foreach (var kv in stats.Flat)
                {
                    double after = next != null && next.Flat.TryGetValue(kv.Key, out var a) ? a : kv.Value;
                    StatRow(sg, Fmt.StatName(kv.Key), WormScreen.StatText(kv.Key, kv.Value), after > kv.Value + 1e-9 ? WormScreen.StatText(kv.Key, after) : null, true);
                }
                foreach (var kv in stats.Percent) StatRow(sg, Fmt.StatName(kv.Key) + " %", "+" + Fmt.Pct(kv.Value), null, true);

                // equip / lock
                var eqRow = Ui.Row(b, 80, 10, null, 0, "EquipRow");
                if (def.Kind == PartKind.Arm)
                {
                    foreach (var arm in new[] { PartSlot.LeftArm, PartSlot.RightArm })
                    {
                        var a = arm;
                        bool here = slot == a;
                        Ui.Button(eqRow, here ? "On " + GearGuide.SlotName(a).ToLowerInvariant() : "Equip " + GearGuide.SlotName(a).ToLowerInvariant(), () => Run(new PartEquip { Uid = uid, Slot = a }, _ => Reopen(uid)),
                            Theme.Accent, -1, 80, !here, 26).name = "Equip:" + a;
                    }
                }
                else
                {
                    var target = (PartSlot)(int)def.Kind;
                    Ui.Button(eqRow, "Equip", () => Run(new PartEquip { Uid = uid, Slot = target }, _ => Reopen(uid)), Theme.Accent, -1, 80, !slot.HasValue, 28).name = "Equip:" + target;
                }
                if (slot.HasValue)
                {
                    var s = slot.Value;
                    Ui.Button(eqRow, "Unequip", () => Run(new PartUnequip { Slot = s }, _ => Reopen(uid)), Theme.Button, 200, 80, true, 26).name = "Unequip";
                }
                Ui.Button(eqRow, locked ? "Unlock" : "Lock", () => { PartLocks.Set(State.PlayerId, uid, !locked); Reopen(uid); Rebuild(); }, locked ? Theme.Warn : Theme.Button, 190, 80, true, 26).name = "PartLock";

                SkillList(b, PartPlanner.SkillLines(Content, p, p.Awaken > 0), "Skills (" + PartsSystem.SkillCount(Content, p.Rarity) + "/5 unlocked)");

                // merge / replicate
                Ui.Caption(b, "Upgrade rarity");
                var plan = PartPlanner.AutoMerge(State, Content, p, Locks);
                var mRow = Ui.Row(b, 84, 10);
                string mergeText = plan.NextRarity < 0 ? "Max rarity" : "Merge → " + RarityName(Content, plan.NextRarity) + "\n<size=20>" + (plan.Valid ? "materials ready" + (plan.NeedsConfirmation ? " (uses protected parts)" : "") : Fmt.Humanize(plan.Error)) + "</size>";
                Ui.Button(mRow, mergeText, () => OpenMergePicker(uid, false), plan.Valid ? Theme.Warn : Theme.Button, -1, 84, plan.NextRarity >= 0, 24).name = "OpenMerge";
                if (p.Rarity >= (int)cfg.ReplicateMinTargetRarity)
                    Ui.Button(mRow, "Replicator\n<size=20>Legendary copy</size>", () => OpenMergePicker(uid, true), Theme.Premium, 260, 84, true, 24).name = "OpenReplicator";

                // runes
                int runeSlots = PartsSystem.RuneSlotCount(Content, p);
                if (runeSlots > 0)
                {
                    Ui.Caption(b, "Rune sockets");
                    var runeRow = Ui.Row(b, 72, 8);
                    for (int i = 0; i < runeSlots; i++)
                    {
                        int ri = i;
                        string runeUid = i < p.Runes.Count ? p.Runes[i] : "";
                        var rune = !string.IsNullOrEmpty(runeUid) && State.Parts.Runes.TryGetValue(runeUid, out var r) ? r : null;
                        Ui.Button(runeRow, rune == null ? "+ rune" : RuneShort(rune), () =>
                        {
                            if (rune != null) Run(new RuneUnsocket { PartUid = uid, SlotIndex = ri }, _ => Reopen(uid));
                            else SocketModal(uid, ri);
                        }, rune == null ? Theme.Locked : Theme.RarityColor((Rarity)rune.Rarity), -1, 72, Session.IsUnlocked(Features.Runes), 22);
                    }
                }

                // advanced: Forge (once unlocked), awakening (Mythic+), dismantle
                var row = Ui.Row(b, 80, 10, null, 0, "AdvancedRow");
                if (Session.IsUnlocked(Features.Forge) && p.Rarity >= (int)cfg.ForgeMinRarity)
                {
                    bool forge = p.ForgedSlots < cfg.Forge.Count;
                    Ui.Button(row, "Forge\n<size=20>" + (forge ? cfg.Forge[p.ForgedSlots].Diamonds + " Diamonds" : "done") + "</size>", () => Run(new PartForgeUnlock { Uid = uid }, _ => Reopen(uid)), Theme.Premium, -1, 80, forge, 24);
                }
                if (p.Rarity >= (int)cfg.AwakenMinRarity)
                {
                    string awText = p.Awaken > 0 ? "Awakened" : "Awaken\n<size=20>" + State.Wallet.Get(ProgressionCurrencies.PartAwakenCores) + "/" + cfg.AwakenCost + " cores</size>";
                    Ui.Button(row, awText, () => AwakenModal(uid), Theme.Warn, -1, 80, p.Awaken == 0, 24).name = "OpenAwaken";
                }
                Ui.Button(row, "Dismantle\n<size=20>+" + Fmt.Num(cfg.RarityDef(p.Rarity).DismantleOre + p.SpentOre) + " Ore</size>", () => Dismantle(uid), Theme.Bad, -1, 80, !slot.HasValue && p.Awaken == 0, 24).name = "Dismantle";
            }), new ModalButton("Close", null));
            modal.Root.name = "Modal:PartDetail";
        }

        /// <summary>
        /// Level card of the detail: Lv n/cap, the next level's Ore (and Gear Core) price with the balance, a big LEVEL UP
        /// button (green with a "READY" chip when affordable), why not otherwise; at the cap: cap raise / merge hint; reset.
        /// </summary>
        private void LevelCard(Transform parent, PartItem p)
        {
            string uid = p.Uid;
            var cfg = Content.PartsConfig;
            int cap = PartsSystem.LevelCap(Content, p);
            bool below = GearGuide.NextLevelCost(Content, p, out long ore, out long cores);
            bool afford = GearGuide.CanLevelUp(State, Content, p);
            var card = Ui.Card(parent, Theme.PanelAlt, afford ? Theme.Good : Theme.Border, Theme.RadiusCard, 22, 10, 4, "LevelCard");
            var top = Ui.Row(card, 56, 10);
            Ui.Display(top, "Level " + p.Level + " / " + cap, Theme.FontLarge, Theme.Text, TextAnchor.MiddleLeft, "LevelText");
            if (afford) Ui.Chip(top, "READY", Theme.Good, Theme.OnAccent, 22, 44, null, "ReadyChip");
            if (below)
            {
                long haveOre = State.Wallet.Get(Currencies.Ore);
                string price = Fmt.Num(ore) + " Ore" + (cores > 0 ? " + " + Fmt.Num(cores) + " Gear Cores" : "");
                var row = Ui.Row(card, 96, 12);
                var up = Ui.Button(row, "LEVEL UP\n<size=22>" + price + "</size>", () => Run(new PartLevelUp { Uid = uid }, _ => Reopen(uid)), afford ? Theme.Good : Theme.Button, -1, 96, afford, 30);
                up.name = "PartLevelUp";
                var oreBtn = Ui.Button(row, "ORE " + Fmt.Num(haveOre) + "\n<size=18>what is Ore?</size>", () => App.Bar.ShowCurrency(Currencies.Ore), Theme.Button, 230, 96, true, 24);
                oreBtn.name = "OreInfo";
                string costText = afford ? "Costs " + price + " · you have " + Fmt.Num(haveOre) + " Ore."
                    : !State.Wallet.CanAfford(Currencies.Ore, ore) ? "Needs " + Fmt.Num(ore) + " Ore (you have " + Fmt.Num(haveOre) + "). Ore drops from runs and dismantled parts."
                    : "Needs " + Fmt.Num(cores) + " Gear Cores (you have " + Fmt.Num(State.Wallet.Get(Currencies.GearCores)) + ").";
                Ui.Label(card, costText, 22, afford ? Theme.Good : Theme.TextDim, TextAnchor.MiddleLeft, "LevelCost");
            }
            else
            {
                bool canCap = p.CapRaises < cfg.RarityDef(p.Rarity).MaxCapRaises && p.CapRaises < cfg.CapRaisePlates.Count;
                int nextRarity = p.Rarity + 1 < cfg.Rarities.Count ? p.Rarity + 1 : -1;
                Ui.Label(card, "Level cap reached." + (canCap ? " Raise the cap with Advancement Plates" : "")
                    + (nextRarity >= 0 ? (canCap ? " or merge" : " Merge") + " it to " + RarityName(Content, nextRarity) + " for a higher cap (" + cfg.RarityDef(nextRarity).BaseLevelCap + ")." : "."), 22, Theme.TextDim, TextAnchor.MiddleLeft, "LevelCost");
                if (canCap)
                    Ui.Button(card, "Raise cap · " + cfg.CapRaisePlates[p.CapRaises] + " plates", () => Run(new PartRaiseCap { Uid = uid }, _ => Reopen(uid)), Theme.Button, -1, 72,
                        State.Wallet.CanAfford(Currencies.AdvancementPlates, cfg.CapRaisePlates[p.CapRaises]), 24).name = "RaiseCap";
            }
            if (p.Level > 1)
                Ui.Button(card, "Reset to level 1 · refunds " + Fmt.Num(p.SpentOre) + " Ore", () => Run(new PartLevelDown { Uid = uid }, _ => Reopen(uid)), Theme.Button, -1, 60, true, 22).name = "ResetLevel";
        }

        private static void StatRow(Transform parent, string stat, string now, string next, bool _)
        {
            var row = Ui.Row(parent, 40, 8);
            Ui.Size(Ui.Label(row, stat, 24, Theme.TextDim), 300, -1, 0);
            Ui.Label(row, now, 24, Theme.Text);
            if (next != null) Ui.Label(row, "→ " + next + " next level", 22, Theme.Good);
        }

        /// <summary>The part's five skills: unlocked ones in the family colour with their effect, locked ones dimmed with the unlock rarity.</summary>
        private void SkillList(Transform parent, List<PartSkillLine> skills, string title)
        {
            Ui.Caption(parent, title);
            foreach (var sk in skills)
            {
                string head = (sk.Unlocked ? "● " : "○ ") + sk.Name + "  <size=18>" + (sk.Unlocked ? "unlocked at " : "unlocks at ") + RarityName(Content, (int)sk.UnlockRarity) + "</size>";
                var t = Ui.Label(parent, head + "\n<size=20>" + sk.Description + "</size>", 24, sk.Unlocked ? Theme.Text : Theme.TextDim);
                t.name = sk.Unlocked ? "Skill:on" : "Skill:off";
            }
        }

        private void Dismantle(string uid)
        {
            var p = PartsSystem.Get(State, uid);
            if (p == null) return;
            void Go() { var cmd = new PartDismantle(); cmd.Uids.Add(uid); Run(cmd); Ui.Clear(App.ModalLayer); }
            if (p.Rarity < (int)PartRarity.Legendary && !Locks.Contains(uid)) { Go(); return; }
            Modal("Dismantle " + PartName(Content, p) + "?", b => Note(b, RarityName(Content, p.Rarity) + (Locks.Contains(uid) ? ", locked" : "") + ": worth " + PartsSystem.FodderValue(Content, p)
                + " fodder points (" + PartPlanner.Le(Content, PartsSystem.FodderValue(Content, p)) + ") as merge material."),
                new ModalButton("Cancel", null), new ModalButton("Dismantle", Go, Theme.Bad));
        }

        /// <summary>Close the open modals and reopen the detail on the updated state.</summary>
        protected void Reopen(string uid)
        {
            Ui.Clear(App.ModalLayer);
            if (PartsSystem.Get(State, uid) != null) Detail(uid);
        }

        // ---- merge picker / Replicator -------------------------------------------------------------

        /// <summary>The current picker selection evaluated by the rules (null when no picker is open).</summary>
        public MergePlan PickerPlan { get; private set; }

        /// <summary>Open the merge (or Replicator) picker for a part, pre-filled with the auto-selected best fodder.</summary>
        public void OpenMergePicker(string uid, bool replicate)
        {
            _pickTarget = uid;
            _pickReplicate = replicate;
            AutoPick();
        }

        /// <summary>Replace the selection with the auto-selected materials.</summary>
        public void AutoPick()
        {
            var t = PartsSystem.Get(State, _pickTarget);
            if (t == null) return;
            var auto = _pickReplicate ? PartPlanner.AutoReplicate(State, Content, t, Locks) : PartPlanner.AutoMerge(State, Content, t, Locks);
            _picked.Clear();
            _picked.AddRange(auto.Materials);
            ShowPicker();
        }

        public void TogglePick(string uid)
        {
            if (!_picked.Remove(uid)) _picked.Add(uid);
            ShowPicker();
        }

        public void ClearPick() { _picked.Clear(); ShowPicker(); }

        private void ShowPicker()
        {
            var t = PartsSystem.Get(State, _pickTarget);
            Ui.Clear(App.ModalLayer);
            if (t == null) { PickerPlan = null; return; }
            var plan = PartPlanner.Evaluate(State, Content, t, _picked, Locks, _pickReplicate);
            PickerPlan = plan;
            string title = _pickReplicate ? "Replicator: " + PartsSystem.Def(Content, t).Name : "Merge " + PartName(Content, t) + (plan.NextRarity >= 0 ? " → " + RarityName(Content, plan.NextRarity) : "");
            Modal(title, b =>
            {
                var rc = Card(b, Theme.PanelAlt, 4);
                if (_pickReplicate) Note(rc, "Turns Legendary+ surplus of any other def into a Legendary copy of " + PartsSystem.Def(Content, t).Name + " (copy for its Mythic S / SS steps).");
                else KeyValue(rc, "Own-def copies", plan.CopiesNeeded == 0 ? "none" : plan.Copies.Count + "/" + plan.CopiesNeeded + " × " + RarityName(Content, (int)plan.CopyMinRarity) + "+ (owned " + plan.CopiesOwned + ")",
                    plan.Copies.Count >= plan.CopiesNeeded ? Theme.Good : Theme.Bad);
                KeyValue(rc, "Fodder", plan.FodderPoints + "/" + plan.FodderNeeded + " pts · " + PartPlanner.Le(Content, plan.FodderPoints) + "/" + PartPlanner.Le(Content, plan.FodderNeeded)
                                     + " · " + RarityName(Content, (int)plan.FodderMinRarity) + "+", plan.FodderPoints >= plan.FodderNeeded ? Theme.Good : Theme.Bad);
                KeyValue(rc, "Ore", plan.Ore <= 0 ? "none" : Fmt.Num(plan.Ore) + " (have " + Fmt.Num(State.Wallet.Get(Currencies.Ore)) + ")", State.Wallet.CanAfford(Currencies.Ore, plan.Ore) ? Theme.Text : Theme.Bad);
                var status = Ui.Label(b, plan.Valid ? (plan.NeedsConfirmation ? "Ready — uses " + plan.Protected.Count + " protected part(s), you will be asked to confirm." : "Ready.") : Fmt.Humanize(plan.Error),
                    Theme.FontSmall, plan.Valid ? (plan.NeedsConfirmation ? Theme.Warn : Theme.Good) : Theme.Bad);
                status.name = "PickerStatus";

                var cands = PickerCandidates(t, plan);
                var list = Ui.List(b, Mathf.Clamp(cands.Count * 82 + 24, 120, 640));
                int shown = 0;
                foreach (var c in cands)
                {
                    var cand = c;
                    bool sel = _picked.Contains(c.Uid);
                    string reason = PartPlanner.ProtectReason(State, Content, c, Locks);
                    bool copy = plan.Copies.Contains(c.Uid) || (!_pickReplicate && c.DefId == t.DefId && c.Rarity >= (int)plan.CopyMinRarity && plan.CopiesNeeded > 0);
                    string role = copy ? "copy" : "fodder";
                    var row = Ui.Row(list, 74, 8, sel ? Color.Lerp(RarityColor(c.Rarity), Theme.Panel, 0.45f) : Theme.Panel, 8);
                    Ui.Label(row, (sel ? "✓ " : "") + PartName(Content, c) + "  <size=20>" + RarityName(Content, c.Rarity) + " · Lv " + c.Level + "</size>\n<size=19>"
                                  + PartsSystem.FodderValue(Content, c) + " pts (" + PartPlanner.Le(Content, PartsSystem.FodderValue(Content, c)) + ") · " + role
                                  + (reason != null ? " · protected: " + reason : "") + "</size>", 22, reason != null ? Theme.Warn : Theme.Text);
                    Ui.Button(row, sel ? "Remove" : "Add", () => TogglePick(cand.Uid), sel ? Theme.Button : Theme.Accent, 160, 60, true, 22).name = "Pick:" + c.Uid;
                    shown++;
                }
                if (shown == 0) Note(list, "No eligible parts (equipped, awakened and forged parts are never materials).");
                var tools = Ui.Row(b, 70, 8);
                Ui.Button(tools, "Auto-select best", AutoPick, Theme.Accent, -1, 70, true, 24).name = "PickAuto";
                Ui.Button(tools, "Clear", ClearPick, Theme.Button, 200, 70, true, 24);
            },
            new ModalButton("Cancel", () => { PickerPlan = null; Reopen(_pickTarget); }),
            new ModalButton(_pickReplicate ? "Replicate" : "Merge", ConfirmPick, plan.Valid ? Theme.Warn : Theme.Locked, true));
        }

        /// <summary>Eligible candidates for the picker: selected ones first, then by fodder value and level.</summary>
        private List<PartItem> PickerCandidates(PartItem t, MergePlan plan)
        {
            var list = new List<PartItem>();
            foreach (var p in PartPlanner.Candidates(State, t))
            {
                bool copyOk = !_pickReplicate && plan.CopiesNeeded > 0 && p.DefId == t.DefId && p.Rarity >= (int)plan.CopyMinRarity;
                bool fodderOk = plan.FodderNeeded > 0 && p.Rarity >= (int)plan.FodderMinRarity && !(_pickReplicate && p.DefId == t.DefId);
                if (copyOk || fodderOk || _picked.Contains(p.Uid)) list.Add(p);
            }
            list.Sort((a, b) =>
            {
                bool sa = _picked.Contains(a.Uid), sb = _picked.Contains(b.Uid);
                if (sa != sb) return sa ? -1 : 1;
                long va = PartsSystem.FodderValue(Content, a), vb = PartsSystem.FodderValue(Content, b);
                if (va != vb) return va.CompareTo(vb);
                if (a.Level != b.Level) return a.Level.CompareTo(b.Level);
                return string.CompareOrdinal(a.Uid, b.Uid);
            });
            if (list.Count > 40) list.RemoveRange(40, list.Count - 40);
            return list;
        }

        /// <summary>Merge/replicate the selection; protected materials (locked, Mythic+, reserved copies) need a confirmation first.</summary>
        public void ConfirmPick()
        {
            var plan = PickerPlan;
            if (plan == null || !plan.Valid) return;
            if (plan.NeedsConfirmation)
            {
                var names = new List<string>();
                foreach (var uid in plan.Protected)
                {
                    var p = PartsSystem.Get(State, uid);
                    if (p != null) names.Add(PartName(Content, p) + " (" + PartPlanner.ProtectReason(State, Content, p, Locks) + ")");
                }
                Modal("Use protected parts?", b => Note(b, "This consumes: " + string.Join(", ", names) + "."),
                    new ModalButton("Cancel", null), new ModalButton("Use them", ExecutePick, Theme.Bad)).Root.name = "Modal:ConfirmProtected";
                return;
            }
            ExecutePick();
        }

        private void ExecutePick()
        {
            var plan = PickerPlan;
            string target = _pickTarget;
            if (plan == null || target == null) return;
            GameCommand cmd;
            if (plan.Replicate) { var r = new PartReplicate { TargetUid = target }; r.MaterialUids.AddRange(plan.Materials); cmd = r; }
            else { var m = new PartMerge { TargetUid = target }; m.MaterialUids.AddRange(plan.Materials); cmd = m; }
            foreach (var uid in plan.Materials) PartLocks.Set(State.PlayerId, uid, false);
            PickerPlan = null;
            _picked.Clear();
            Run(cmd, res => { if (res.Ok) Reopen(target); });
        }

        // ---- awakening ---------------------------------------------------------------------------

        /// <summary>Before/after preview of the one-time awakening (name, stats, the five skills swapped).</summary>
        public void AwakenModal(string uid)
        {
            var p = PartsSystem.Get(State, uid);
            if (p == null) return;
            var pv = PartPlanner.Awaken(State, Content, p);
            Ui.Clear(App.ModalLayer);
            Modal("Awaken " + pv.NameBefore, b =>
            {
                Ui.Label(b, pv.NameBefore + "  →  " + pv.NameAfter, Theme.FontLarge, Theme.Warn, TextAnchor.MiddleCenter).name = "AwakenNames";
                Note(b, "Once per part. Level, cap raises, runes, Forge and rarity are kept; all five skills are replaced by awakened redesigns.");
                var stats = Ui.Column(b, 2, 0, null, "AwakenStats");
                var keys = new List<StatId>();
                foreach (var k in pv.StatsAfter.Flat.Keys) if (!keys.Contains(k)) keys.Add(k);
                foreach (var k in pv.StatsBefore.Flat.Keys) if (!keys.Contains(k)) keys.Add(k);
                foreach (var k in keys)
                {
                    pv.StatsBefore.Flat.TryGetValue(k, out double before);
                    pv.StatsAfter.Flat.TryGetValue(k, out double after);
                    AwakenStatRow(stats, Fmt.StatName(k), WormScreen.StatText(k, before), WormScreen.StatText(k, after), after > before);
                }
                var pkeys = new List<StatId>();
                foreach (var k in pv.StatsAfter.Percent.Keys) if (!pkeys.Contains(k)) pkeys.Add(k);
                foreach (var k in pv.StatsBefore.Percent.Keys) if (!pkeys.Contains(k)) pkeys.Add(k);
                foreach (var k in pkeys)
                {
                    pv.StatsBefore.Percent.TryGetValue(k, out double before);
                    pv.StatsAfter.Percent.TryGetValue(k, out double after);
                    AwakenStatRow(stats, Fmt.StatName(k) + " %", "+" + Fmt.Pct(before), "+" + Fmt.Pct(after), after > before);
                }
                var cols = Ui.Row(b, -1, 12);
                SkillColumn(cols, "Now", pv.SkillsBefore);
                SkillColumn(cols, "Awakened", pv.SkillsAfter);
                Ui.Label(b, pv.BlockReason == null ? "Cost: " + pv.Cost + " Awaken Cores (have " + pv.Have + ")" : Fmt.Humanize(pv.BlockReason), Theme.FontSmall,
                    pv.BlockReason == null ? Theme.Good : Theme.Bad, TextAnchor.MiddleCenter).name = "AwakenCost";
            },
            new ModalButton("Cancel", () => Reopen(uid)),
            new ModalButton("Awaken (" + pv.Cost + ")", () => { if (pv.BlockReason == null) Run(new PartAwaken { Uid = uid }, _ => Reopen(uid)); else Reopen(uid); },
                pv.BlockReason == null ? Theme.Warn : Theme.Locked));
        }

        private static void AwakenStatRow(Transform parent, string stat, string before, string after, bool up)
        {
            var row = Ui.Row(parent, 34, 8);
            Ui.Size(Ui.Label(row, stat, 22, Theme.TextDim), 300, -1, 0);
            Ui.Label(row, before, 22, Theme.Text);
            Ui.Label(row, "→ " + after, 22, up ? Theme.Good : Theme.Text);
        }

        private void SkillColumn(Transform parent, string title, List<PartSkillLine> skills)
        {
            var col = Ui.Column(parent, 4, 8, Theme.PanelAlt, "SkillColumn");
            Ui.Size(col, -1, -1, 1);
            Ui.Label(col, title, Theme.FontSmall, Theme.Accent);
            foreach (var sk in skills)
                Ui.Label(col, (sk.Unlocked ? "● " : "○ ") + sk.Name + "\n<size=17>" + RarityName(Content, (int)sk.UnlockRarity) + (sk.Unlocked ? "" : " · locked") + "</size>", 20,
                    sk.Unlocked ? Theme.Text : Theme.TextDim);
        }

        private void SocketModal(string partUid, int slot)
        {
            Modal("Socket a rune", b =>
            {
                int n = 0;
                foreach (var r in SortedRunes())
                {
                    if (!string.IsNullOrEmpty(r.SocketedIn)) continue;
                    var rune = r;
                    n++;
                    Ui.Button(b, RuneShort(rune), () => Run(new RuneSocket { RuneUid = rune.Uid, PartUid = partUid, SlotIndex = slot }, _ => Reopen(partUid)),
                        Theme.RarityColor((Rarity)rune.Rarity), -1, 70);
                    if (n >= 8) break;
                }
                if (n == 0) Note(b, "No free runes.");
            }, new ModalButton("Close", null));
        }

        // ---- runes -------------------------------------------------------------------------------

        protected List<RuneItem> SortedRunes()
        {
            var list = new List<RuneItem>(State.Parts.Runes.Values);
            list.Sort((a, b) => a.Rarity != b.Rarity ? b.Rarity.CompareTo(a.Rarity) : string.CompareOrdinal(a.DefId + a.Uid, b.DefId + b.Uid));
            return list;
        }

        protected string RuneShort(RuneItem r)
        {
            var def = Content.PartsConfig.Runes.TryGetValue(r.DefId, out var d) ? d : null;
            var stat = PartsSystem.RuneStat(Content, r);
            string v = stat == null ? "" : stat.Percent != 0 ? "+" + Fmt.Pct(stat.Percent) : "+" + stat.Flat.ToString("0");
            return (def?.Name ?? r.DefId) + " " + v;
        }

        /// <summary>The rune inventory: lock, merge 3→1.</summary>
        protected void Runes(RectTransform body)
        {
            if (!Session.IsUnlocked(Features.Runes)) Note(body, "Runes unlock later (" + Requirements.Describe(Content, Content.Feature(Features.Runes)?.Requires ?? new RequirementSet()) + ").");
            var runes = SortedRunes();
            if (runes.Count == 0) Note(body, "No runes yet.");
            foreach (var r in runes)
            {
                var rune = r;
                var row = Ui.Row(body, 84, 8, Color.Lerp(Theme.RarityColor((Rarity)rune.Rarity), Theme.Panel, 0.75f), 10);
                Ui.Label(row, RuneShort(rune) + "\n<size=20>" + (Rarity)rune.Rarity + (string.IsNullOrEmpty(rune.SocketedIn) ? "" : " · socketed") + "</size>", Theme.FontSmall);
                Ui.Button(row, rune.Locked ? "Locked" : "Lock", () => Run(new RuneLock { RuneUid = rune.Uid, Locked = !rune.Locked }), rune.Locked ? Theme.Warn : Theme.Button, 150, 70, true, 24);
                var mates = new List<string>();
                foreach (var o in runes)
                    if (mates.Count < Content.PartsConfig.RuneMergeCount && o.DefId == rune.DefId && o.Rarity == rune.Rarity && !o.Locked && string.IsNullOrEmpty(o.SocketedIn)) mates.Add(o.Uid);
                bool can = mates.Count == Content.PartsConfig.RuneMergeCount && mates.Contains(rune.Uid) && rune.Rarity < (int)Content.PartsConfig.RuneMaxRarity;
                Ui.Button(row, "Merge 3", () => { var cmd = new RuneMerge(); cmd.RuneUids.AddRange(mates); Run(cmd); }, Theme.Accent, 170, 70, can, 24);
            }
        }

        /// <summary>Auto-selected merge materials for the next rarity (null + reason when not possible). Used by tests and the detail.</summary>
        public List<string> FindMergeMaterials(PartItem target, out string why)
        {
            var plan = PartPlanner.AutoMerge(State, Content, target, Locks);
            why = plan.Error;
            return plan.Valid ? plan.Materials : null;
        }
    }
}
