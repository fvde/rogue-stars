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
    /// merge picker (copies, fodder points, Ore, best-fodder auto-select, confirmation before protected parts are
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
            Refresh(); // in place: the tiles that stay are kept
        }

        /// <summary>From a body slot to its parts: the inventory filtered to the slot (any rarity), scrolled into view.</summary>
        public void ShowSlot(PartSlot slot)
        {
            _slotFilter = GearGuide.FilterFor(slot);
            _rarityFilter = -1;
            _scrollToInventory = true;
            ShowInventoryView();
            Refresh();
        }

        /// <summary>Scroll to the inventory after the next build (Ore's "Go").</summary>
        public void FocusInventory() { _scrollToInventory = true; ShowInventoryView(); Refresh(); }

        /// <summary>The gear screens update in place: tiles, slots and sections are keyed parts.</summary>
        protected override bool InPlace => true;

        /// <summary>The screen switches to the view that holds the inventory (the Worm's Parts sub-tab).</summary>
        protected virtual void ShowInventoryView() { }

        // ---- inventory -----------------------------------------------------------------------------

        /// <summary>The inventory: header (count, Ore), slot tabs with counts, sort + rarity filter, the tile grid, quick merge.</summary>
        protected void BuildInventory(Transform body, string title)
        {
            int count = State.Parts.Items.Count;
            var head = Part(body, "inv:head", K(title, count), b =>
            {
                var row = Ui.Row(b, 72, 12, null, 0, "InventoryHead");
                Section(row, title + " · " + count);
                LiveCurrencyChip(row, Currencies.Ore, () => App.Bar.ShowCurrency(Currencies.Ore), "OreChip");
            });
            _inventoryAnchor = head;

            var labels = new List<string>();
            foreach (var f in GearGuide.SlotFilters)
            {
                int n = 0;
                foreach (var p in State.Parts.Items.Values) if (GearGuide.Matches(PartsSystem.Def(Content, p), f)) n++;
                labels.Add(GearGuide.SlotFilterLabel(f) + "\n<size=20>" + n + "</size>");
            }
            int slotIndex = Array.IndexOf(GearGuide.SlotFilters, _slotFilter);
            Part(body, "inv:filters", K(string.Join(",", labels), slotIndex), b =>
            {
                var tabs = Kit.TabChips(b, labels, slotIndex, i => SetFilter(GearGuide.SlotFilters[i], _rarityFilter, _sort), 92);
                for (int i = 0; i < tabs.Buttons.Count; i++) tabs.Buttons[i].name = "Filter:" + GearGuide.SlotFilters[i];
            });

            Part(body, "inv:tools", K(_sort, _rarityFilter), b =>
            {
                var tools = Ui.Row(b, 72, 10, null, 0, "InventoryTools");
                var sortLabels = new List<string>();
                foreach (var s in GearGuide.Sorts) sortLabels.Add(GearGuide.SortLabel(s));
                var sorts = Kit.TabChips(tools, sortLabels, Array.IndexOf(GearGuide.Sorts, _sort), i => SetFilter(_slotFilter, _rarityFilter, GearGuide.Sorts[i]), 72);
                for (int i = 0; i < sorts.Buttons.Count; i++) sorts.Buttons[i].name = "Sort:" + GearGuide.Sorts[i];
                Ui.Size(sorts.Buttons[0].transform.parent, -1, 72, 1);
                string rarityText = _rarityFilter < 0 ? "Any rarity" : RarityName(Content, _rarityFilter);
                var rb = Ui.Button(tools, rarityText, RarityPicker, _rarityFilter < 0 ? Theme.Button : Color.Lerp(RarityColor(_rarityFilter), Theme.Panel, 0.4f), 260, 72, true, 24);
                KitFit.On(rb.GetComponentInChildren<Text>(), 14);
                rb.name = "RarityFilter";
            });

            var list = GearGuide.Inventory(State, Content, _slotFilter, _rarityFilter, _sort);
            ShownParts.Clear();
            var grid = Part(body, "inv:grid", "", b =>
            {
                var g = Ui.Grid(b, new Vector2(TileWidth, TileHeight), 3, TileGap);
                g.GetComponent<GridLayoutGroup>().childAlignment = TextAnchor.UpperLeft; // a short last row starts at the left
            });
            foreach (var p in list)
            {
                ShownParts.Add(p.Uid);
                var part = p;
                Part(grid, "tile:" + p.Uid, TileKey(p), g => PartTile(g, part));
            }
            if (list.Count == 0)
            {
                string empty = count == 0 ? "No parts yet. Parts drop from runs and Parts pulls in the Shop."
                    : "No " + (_rarityFilter >= 0 ? RarityName(Content, _rarityFilter) + " " : "") + (_slotFilter == GearSlotFilter.All ? "" : GearGuide.SlotFilterLabel(_slotFilter) + " ") + "parts yet.";
                Part(body, "inv:empty", empty, b => Note(b, empty));
            }

            Part(body, "inv:foot", K(count, PartPlanner.Feedable(State, Content, Locks).Count, FooterExtrasKey()), b =>
            {
                var foot = Ui.Row(b, 80, 10, null, 0, "InventoryFoot");
                // parts v2: nothing merges; spare standard parts become Metal for the Mythic ladder
                int spare = PartPlanner.Feedable(State, Content, Locks).Count;
                Ui.Button(foot, "Feed spares\n<size=20>" + spare + " part" + (spare == 1 ? "" : "s") + " → Metal</size>", FeedSpares, Theme.Button, -1, 80, spare > 0, 26).name = "FeedSpares";
                InventoryFooterExtras(foot);
            });

            if (_scrollToInventory) ScrollToAfterBuild(head);
            _scrollToInventory = false;
        }

        /// <summary>Everything a part tile shows (its part key).</summary>
        private string TileKey(PartItem p)
        {
            var slot = GearGuide.EquippedSlot(State, p.Uid);
            return K(p.DefId, PartName(Content, p), p.Rarity, p.Level, PartsSystem.LevelCap(Content, p), slot, Locks.Contains(p.Uid), p.Awaken,
                slot.HasValue && GearGuide.CanLevelUp(State, Content, p));
        }

        /// <summary>What <see cref="InventoryFooterExtras"/> shows depends on (part key).</summary>
        protected virtual string FooterExtrasKey() => "";

        /// <summary>More buttons next to Feed spares (the Worm adds Runes).</summary>
        protected virtual void InventoryFooterExtras(RectTransform row) { }

        /// <summary>Tile grid: three columns of 296 + 2 × 14 = 916 ≤ the 930-unit body (978 − 2 × 24 padding).</summary>
        public const float TileWidth = 296, TileHeight = 262, TileGap = 14;

        /// <summary>One part tile: slot, name, rarity and level, EQUIPPED / slot, LOCKED, and the ▲ badge when it can level up now.</summary>
        private void PartTile(Transform grid, PartItem p)
        {
            var def = PartsSystem.Def(Content, p);
            var rc = RarityColor(p.Rarity);
            var slot = GearGuide.EquippedSlot(State, p.Uid);
            bool eq = slot.HasValue;
            // equipped: green outline; spare: the rarity colour
            Kit.RarityCard(grid, "Part:" + p.Uid, rc, () => Detail(p.Uid), out var c, 30, 16, 4, eq, null, eq ? 0.38f : 0.26f);
            var kindRow = Ui.Row(c, 30, 0, null, 0, "KindRow");
            Ui.Spacer(kindRow, 34).GetComponent<LayoutElement>().flexibleWidth = 0; // room for the rarity gem
            Ui.Label(kindRow, def.Kind.ToString().ToUpperInvariant() + (def.IsMythic ? " · " + (def.Synergy ?? "").ToUpperInvariant() : " · " + def.Role.ToString().ToUpperInvariant()), 20, Theme.TextDim, TextAnchor.MiddleLeft, "Kind").fontStyle = FontStyle.Bold;
            var name = Ui.Display(c, PartName(Content, p), 28, Color.Lerp(rc, Color.white, 0.6f), TextAnchor.UpperLeft, "Name");
            Ui.Size(name, -1, 66, 1);
            var lvRow = Ui.Row(c, 38, 8, null, 0, "Level");
            var lvb = Kit.LevelBadge(lvRow, "Lv " + p.Level + "/" + PartsSystem.LevelCap(Content, p), Color.white, 34);
            Ui.Label(lvRow, RarityName(Content, p.Rarity), 20, Theme.TextDim, TextAnchor.MiddleLeft, "RarityName");
            var chips = Ui.ChipRow(c, 38, 6);
            if (eq) Ui.Chip(chips, slot.Value == PartSlot.LeftArm ? "LEFT ARM" : slot.Value == PartSlot.RightArm ? "RIGHT ARM" : "EQUIPPED", Theme.Good, Theme.OnAccent, 18, 36, null, "Equipped");
            if (Locks.Contains(p.Uid)) Ui.Chip(chips, "LOCKED", null, Theme.Warn, 18, 36, null, "Locked");
            if (p.Awaken > 0) Ui.Chip(chips, "AWAKE", null, Theme.Premium, 18, 36);
            if (eq && GearGuide.CanLevelUp(State, Content, p)) Badge(c, "▲", "LevelUpBadge");
        }

        /// <summary>A round green badge in the top-right corner of a card (outside its layout).</summary>
        protected static void Badge(Transform card, string text, string name)
        {
            Kit.CornerBadge(card, text, Theme.Good, name);
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

        /// <summary>Feed every spare standard part (not equipped, locked or forged; never Mythics) for Metal, after a confirmation.</summary>
        protected void FeedSpares()
        {
            var spare = PartPlanner.Feedable(State, Content, Locks);
            if (spare.Count == 0) return;
            long metal = 0;
            foreach (var p in spare) metal += PartsSystem.MetalValue(Content, p);
            void Go() { var cmd = new PartFeed(); foreach (var p in spare) cmd.Uids.Add(p.Uid); Run(cmd); }
            Modal("Feed " + spare.Count + " spare part" + (spare.Count == 1 ? "" : "s") + "?", b => Note(b, "They become " + Fmt.Num(metal) + " Metal for the Mythic ladder (Common 1, Rare 2, Epic 6, Legendary 20); their level Ore and runes come back."),
                new ModalButton("Cancel", null), new ModalButton("Feed", Go, Theme.Warn)).Root.name = "Modal:FeedSpares";
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
            bool locked = Locks.Contains(uid) || p.Locked;
            var rc = RarityColor(p.Rarity);
            var modal = Modal(PartName(Content, p), outer => Ui.ScrollingColumn(outer, DetailMaxHeight, b =>
            {
                // who: rarity, slot, family, equipped
                var tags = Ui.ChipRow(b, 46, 8);
                Ui.Chip(tags, RarityName(Content, p.Rarity).ToUpperInvariant(), rc, Theme.TextOn(rc), 22, 44, null, "Rarity");
                Ui.Chip(tags, def.Kind.ToString().ToUpperInvariant() + (def.Kind == PartKind.Arm ? " · EITHER ARM" : ""), null, Theme.Text, 22, 44);
                Ui.Chip(tags, (def.IsMythic ? def.Synergy ?? "" : def.Role.ToString()).ToUpperInvariant(), null, Theme.TextDim, 22, 44);
                if (def.IsMythic && !string.IsNullOrEmpty(def.Identity)) Note(b, def.Identity);
                if (slot.HasValue) Ui.Chip(tags, "ON " + GearGuide.SlotName(slot.Value).ToUpperInvariant(), Theme.Good, Theme.OnAccent, 22, 44, null, "EquippedChip");
                if (p.Awaken > 0) Note(b, "Awakened (" + def.Name + ")");

                LevelCard(b, p);

                // stats: now → next level
                Ui.Caption(b, "Stats");
                var stats = PartsSystem.PartStats(State, Content, p);
                StatBlock next = null;
                if (GearGuide.NextLevelCost(Content, p, out _, out _))
                    next = PartsSystem.PartStats(State, Content, new PartItem { Uid = p.Uid, DefId = p.DefId, Rarity = p.Rarity, Level = p.Level + 1, ForgedSlots = p.ForgedSlots, Awaken = p.Awaken, Runes = p.Runes });
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
                        if (slot == a) continue; // W4: no Equip for the arm it is already on (the ON chip says so)
                        string verb = slot.HasValue ? "Swap to " : "Equip ";
                        Ui.Button(eqRow, verb + GearGuide.SlotName(a).ToLowerInvariant(), () => Run(new PartEquip { Uid = uid, Slot = a }, _ => Reopen(uid)),
                            Theme.Accent, -1, 80, true, 26).name = "Equip:" + a;
                    }
                }
                else if (!slot.HasValue) // W4: an equipped part shows Unequip only
                {
                    var target = (PartSlot)(int)def.Kind;
                    Ui.Button(eqRow, "Equip", () => Run(new PartEquip { Uid = uid, Slot = target }, _ => Reopen(uid)), Theme.Accent, -1, 80, true, 28).name = "Equip:" + target;
                }
                if (slot.HasValue)
                {
                    var s = slot.Value;
                    Ui.Button(eqRow, "Unequip", () => Run(new PartUnequip { Slot = s }, _ => Reopen(uid)), Theme.Button, 200, 80, true, 26).name = "Unequip";
                }
                Ui.Button(eqRow, locked ? "Unlock" : "Lock", () =>
                {
                    PartLocks.Set(State.PlayerId, uid, !locked);
                    Run(new PartLock { Uid = uid, Locked = !locked }, _ => { Reopen(uid); Refresh(); });
                }, locked ? Theme.Warn : Theme.Button, 190, 80, true, 26).name = "PartLock";

                var lines = PartPlanner.SkillLines(Content, p, p.Awaken > 0);
                if (lines.Count > 0) SkillList(b, lines, "Skills (" + Math.Min(lines.Count, PartsSystem.SkillCount(Content, p.Rarity)) + "/" + lines.Count + " active)");

                // the Mythic ladder (parts v2: only Mythics climb; copies + other Mythics of the slot, Metal, Ore) and the Replicator
                if (def.IsMythic)
                {
                    Ui.Caption(b, "Mythic ladder");
                    var plan = PartPlanner.AutoMerge(State, Content, p, Locks);
                    var mRow = Ui.Row(b, 84, 10);
                    string mergeText = plan.NextRarity < 0 ? "Mythic SS (final)" : "Climb → " + RarityName(Content, plan.NextRarity) + "\n<size=20>" + (plan.Valid ? "materials ready" + (plan.NeedsConfirmation ? " (uses protected parts)" : "") : Fmt.Humanize(plan.Error)) + "</size>";
                    Ui.Button(mRow, mergeText, () => OpenMergePicker(uid, false), plan.Valid ? Theme.Warn : Theme.Button, -1, 84, plan.NextRarity >= 0, 24).name = "OpenMerge";
                    Ui.Button(mRow, "Replicator\n<size=20>3 Mythics → copy</size>", () => OpenMergePicker(uid, true), Theme.Premium, 260, 84, true, 24).name = "OpenReplicator";
                    if (plan.NextRarity >= 0)
                        Note(b, "Next step: " + plan.CopiesNeeded + " cop" + (plan.CopiesNeeded == 1 ? "y" : "ies") + (plan.FodderNeeded > 0 ? " + " + plan.FodderNeeded + " other " + def.Kind + " Mythics" : "")
                            + ", " + Fmt.Num(plan.Metal) + " Metal (have " + Fmt.Num(State.Wallet.Get(Currencies.Metal)) + "), " + Fmt.Num(plan.Ore) + " Ore.");
                }
                else Note(b, "Standard parts never merge. A spare one becomes " + PartsSystem.MetalValue(Content, p) + " Metal for the Mythic ladder.");

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
                if (def.IsMythic && p.Rarity >= (int)cfg.AwakenMinRarity)
                {
                    string awText = p.Awaken > 0 ? "Awakened" : "Awaken\n<size=20>" + State.Wallet.Get(ProgressionCurrencies.PartAwakenCores) + "/" + cfg.AwakenCost + " cores</size>";
                    Ui.Button(row, awText, () => AwakenModal(uid), Theme.Warn, -1, 80, p.Awaken == 0, 24).name = "OpenAwaken";
                }
                if (!def.IsMythic)
                    Ui.Button(row, "Feed\n<size=20>+" + PartsSystem.MetalValue(Content, p) + " Metal</size>", () => Feed(uid), Theme.Bad, -1, 80, PartsSystem.IsFeedable(State, Content, p), 24).name = "Dismantle";
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
                var step = PartsSystem.NextClimb(Content, p);
                Ui.Label(card, "Level cap reached." + (step != null ? " Climb it to " + RarityName(Content, p.Rarity + 1) + " for a higher cap (" + cfg.RarityDef(p.Rarity + 1).LevelCap + ")."
                    : PartsSystem.Def(Content, p).IsMythic ? "" : " Standard parts keep their rarity's cap (" + cap + ")."), 22, Theme.TextDim, TextAnchor.MiddleLeft, "LevelCost");
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

        /// <summary>The part's skills: active ones with their effect, the others dimmed with the rarity / ladder step they need.</summary>
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

        private void Feed(string uid)
        {
            var p = PartsSystem.Get(State, uid);
            if (p == null) return;
            void Go() { var cmd = new PartFeed(); cmd.Uids.Add(uid); Run(cmd); Ui.Clear(App.ModalLayer); }
            if (p.Rarity < (int)PartRarity.Legendary && !Locks.Contains(uid)) { Go(); return; }
            Modal("Feed " + PartName(Content, p) + "?", b => Note(b, RarityName(Content, p.Rarity) + (Locks.Contains(uid) ? ", locked" : "") + ": becomes " + PartsSystem.MetalValue(Content, p)
                + " Metal for the Mythic ladder; its level Ore and runes come back."),
                new ModalButton("Cancel", null), new ModalButton("Feed", Go, Theme.Bad));
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
            string title = _pickReplicate ? "Replicator: " + PartsSystem.Def(Content, t).Name : "Climb " + PartName(Content, t) + (plan.NextRarity >= 0 ? " → " + RarityName(Content, plan.NextRarity) : "");
            Modal(title, b =>
            {
                var rc = Card(b, Theme.PanelAlt, 4);
                if (_pickReplicate) Note(rc, "Turns 3 spare Mythics (any slot, base step) into a copy of " + PartsSystem.Def(Content, t).Name + " for its ladder steps.");
                else KeyValue(rc, "Copies", plan.CopiesNeeded == 0 ? "none" : plan.Copies.Count + "/" + plan.CopiesNeeded,
                    plan.Copies.Count >= plan.CopiesNeeded ? Theme.Good : Theme.Bad);
                KeyValue(rc, _pickReplicate ? "Spare Mythics" : "Other Mythics", plan.FodderNeeded == 0 ? "none" : plan.FodderPoints + "/" + plan.FodderNeeded, plan.FodderPoints >= plan.FodderNeeded ? Theme.Good : Theme.Bad);
                if (!_pickReplicate) KeyValue(rc, "Metal", Fmt.Num(plan.Metal) + " (have " + Fmt.Num(State.Wallet.Get(Currencies.Metal)) + ")", State.Wallet.CanAfford(Currencies.Metal, plan.Metal) ? Theme.Text : Theme.Bad);
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
                    string reason = PartPlanner.ProtectReason(State, Content, c, Locks, _pickReplicate ? null : t);
                    bool copy = plan.Copies.Contains(c.Uid) || (!_pickReplicate && c.DefId == t.DefId && plan.CopiesNeeded > 0);
                    var row = Ui.Row(list, 74, 8, sel ? Color.Lerp(RarityColor(c.Rarity), Theme.Panel, 0.45f) : Theme.Panel, 8);
                    // W5: "Legendary · Lv 1 · 81 pts"; copies say so; protected parts carry a lock icon
                    if (reason != null)
                    {
                        var lockRt = Ui.Rect(row, "Protected");
                        Ui.Size(lockRt, 40, 40, 0);
                        var li = lockRt.gameObject.AddComponent<Image>();
                        li.sprite = RogueStars.Client.World.SummonArt.Icon("lock"); li.preserveAspect = true; li.raycastTarget = false;
                    }
                    Ui.Label(row, (sel ? "✓ " : "") + PartName(Content, c) + "\n<size=20>" + RarityName(Content, c.Rarity) + " · Lv " + c.Level + " · "
                                  + (copy ? "copy" : _pickReplicate ? "spare" : "other Mythic") + "</size>", 22, reason != null ? Theme.Warn : Theme.Text);
                    Ui.Button(row, sel ? "Remove" : "Add", () => TogglePick(cand.Uid), sel ? Theme.Button : Theme.Accent, 160, 60, true, 22).name = "Pick:" + c.Uid;
                    shown++;
                }
                if (shown == 0) Note(list, "No eligible Mythics (only base-step Mythics that are not equipped, awakened, locked or forged).");
                var tools = Ui.Row(b, 70, 8);
                Ui.Button(tools, "Auto-select best", AutoPick, Theme.Accent, -1, 70, true, 24).name = "PickAuto";
                Ui.Button(tools, "Clear", ClearPick, Theme.Button, 200, 70, true, 24);
            },
            new ModalButton("Cancel", () => { PickerPlan = null; Reopen(_pickTarget); }),
            new ModalButton(_pickReplicate ? "Replicate" : "Climb", ConfirmPick, plan.Valid ? Theme.Warn : Theme.Locked, true));
        }

        /// <summary>Eligible candidates for the picker: selected ones first, then by fodder value and level.</summary>
        private List<PartItem> PickerCandidates(PartItem t, MergePlan plan)
        {
            var list = new List<PartItem>();
            var kind = PartsSystem.Def(Content, t).Kind;
            foreach (var p in PartPlanner.Candidates(State, Content, t))
            {
                bool copyOk = !_pickReplicate && plan.CopiesNeeded > 0 && p.DefId == t.DefId;
                bool otherOk = plan.FodderNeeded > 0 && p.DefId != t.DefId && (_pickReplicate || PartsSystem.Def(Content, p).Kind == kind);
                if (copyOk || otherOk || _picked.Contains(p.Uid)) list.Add(p);
            }
            list.Sort((a, b) =>
            {
                bool sa = _picked.Contains(a.Uid), sb = _picked.Contains(b.Uid);
                if (sa != sb) return sa ? -1 : 1;
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
                    if (p != null) names.Add(PartName(Content, p) + " (" + PartPlanner.ProtectReason(State, Content, p, Locks, plan.Replicate ? null : PartsSystem.Get(State, _pickTarget)) + ")");
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
            else { var m = new PartClimb { TargetUid = target }; m.MaterialUids.AddRange(plan.Materials); cmd = m; }
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
            {
                Ui.Label(col, sk.Name, 22, sk.Unlocked ? Theme.Text : Theme.TextDim, TextAnchor.MiddleLeft, "SkillName");
                // W7: rarity as a coloured dot + name at the normal size
                var rr = Ui.Row(col, 32, 8, null, 0, "SkillRarity");
                rr.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                var rcol = RarityColor((int)sk.UnlockRarity);
                Ui.Size(Ui.Dot(rr, sk.Unlocked ? rcol : Theme.Shade(rcol, 0.6f), 18, "RarityDot"), 18, 18, 0);
                Ui.Label(rr, RarityName(Content, (int)sk.UnlockRarity) + (sk.Unlocked ? "" : " · locked"), 22, sk.Unlocked ? Color.Lerp(rcol, Color.white, 0.35f) : Theme.TextDim);
            }
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
            string v = stat == null ? "" : stat.Percent != 0 ? "+" + Fmt.Pct(stat.Percent) : "+" + Fmt.Whole(stat.Flat);
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

        /// <summary>Auto-selected ladder materials for the next step (null + reason when not possible). Used by tests and the detail.</summary>
        public List<string> FindMergeMaterials(PartItem target, out string why)
        {
            var plan = PartPlanner.AutoMerge(State, Content, target, Locks);
            why = plan.Error;
            return plan.Valid ? plan.Materials : null;
        }
    }
}
