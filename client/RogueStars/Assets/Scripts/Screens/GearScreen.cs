using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Shared base of the gear screens (Worm tab and Parts), parts v2 on the kit v2 board ("New · worm"): the dark
    /// inventory panel (header: sort toggle, "Parts · N", the yellow Feed button; a 5-column grid of rarity tiles,
    /// <see cref="PartTileView"/>), the part sheet (rarity header, Mythic ladder, stats now / at cap, skills with the
    /// locked ones greyed and the awakened texts, level up, equip either arm, climb, awaken, lock, runes, Forge, feed),
    /// the empty-slot sheet, the Climb / Replicator picker (needs with owned counts, material tiles, the rules'
    /// ValidateClimb / ValidateReplicate decide), the Feed spares sheet (Metal preview = the rules' ValidateFeed) with
    /// its Replicator tab, the awakening preview and the rune list. Queries: <see cref="GearGuide"/>, material logic:
    /// <see cref="PartPlanner"/>. Commands are local-first: the sheet reopens on the new state at once.
    /// </summary>
    public abstract class GearScreen : UiScreen
    {
        private GearSort _sort = GearSort.Best;

        // climb / Replicator picker
        private string _pickTarget;
        private bool _pickReplicate;
        private readonly List<string> _picked = new List<string>();

        // feed sheet
        private List<string> _feedSel;
        private int _feedTab;

        // part sheet
        private bool _sheetAwakened;

        public GearSort Sort => _sort;
        /// <summary>Uids of the tiles of the last built inventory, in display order (tests).</summary>
        public readonly List<string> ShownParts = new List<string>();

        public static Color RarityColor(int partRarity) => Theme.PartRarityColor(partRarity);
        public static string RarityName(ContentDb c, int r) => c.PartsConfig.RarityDef(r).Name;
        public static string PartName(ContentDb c, PartItem p) => Fmt.PartName(c, p);

        protected HashSet<string> Locks => PartLocks.For(State.PlayerId);

        /// <summary>The gear screens update in place: tiles, slots and sections are keyed parts.</summary>
        protected override bool InPlace => true;

        /// <summary>The inventory also lists equipped parts (the Parts screen; the Worm shows them on its slots instead).</summary>
        protected virtual bool InventoryIncludesEquipped => true;

        public void SetSort(GearSort sort) { _sort = sort; Refresh(); }

        /// <summary>A body slot tapped: its part's sheet, or (empty) the sheet of the spares that fit it.</summary>
        public void ShowSlot(PartSlot slot)
        {
            State.Parts.Equipped.TryGetValue(slot, out var uid);
            if (PartsSystem.Get(State, uid) != null) Detail(uid);
            else SlotSheet(slot);
        }

        // ---- inventory panel ------------------------------------------------------------------------

        public const int GridColumns = 5;
        public const float GridX = 43, GridY = 173, GridStepX = 203, GridStepY = 243, PanelMinHeight = 763;

        /// <summary>The parts the inventory lists (sorted).</summary>
        public List<PartItem> InventoryParts()
        {
            var list = GearGuide.Inventory(State, Content, GearSlotFilter.All, -1, _sort);
            if (!InventoryIncludesEquipped) list.RemoveAll(p => PartsSystem.IsEquipped(State, p.Uid));
            return list;
        }

        /// <summary>The dark inventory panel (board px, scaled to the width): header and the 5-column tile grid.</summary>
        protected void BuildInventory(Transform body, string title)
        {
            var list = InventoryParts();
            int rows = Mathf.Max(1, (list.Count + GridColumns - 1) / GridColumns);
            float designH = Mathf.Max(PanelMinHeight, GridY + rows * GridStepY + 24);
            var wrap = Part(body, "inv:panel", "", b =>
            {
                var d = KitV2.DesignBlock(b, "InventoryPanel", designH, out _);
                var bg = d.gameObject.AddComponent<Image>();
                bg.raycastTarget = false;
                if (!KitV2.Skin(bg, "grid_panel")) bg.color = KitV2.GridPanel;
            });
            wrap.GetComponent<KitDesignFit>().SetDesignHeight(designH);
            var design = (RectTransform)wrap.Find("Design");

            int feedable = PartPlanner.Feedable(State, Content, Locks).Count;
            Part(design, "inv:head", K(title, list.Count, _sort, feedable), d =>
            {
                KitV2.Box(d, "grid_edge", 0, 0, 1080, 8, KitV2.Ink, 1, 0, null, null, "PanelEdge");
                var head = KitV2.Box(d, "grid_header", 0, 8, 1080, 133, KitV2.GridHeader, 1, 0, null, null, "InventoryHead");
                KitV2.Box(head.rectTransform, "grid_header_edge", 0, 128, 1080, 5.3f, KitV2.Ink, 1, 0, null, null, "HeadEdge");
                KitV2.SmallButton(d, "button_small_blue", 27, 29, 213, 85, KitV2.Blue, GearGuide.SortLabel(_sort), () => SetSort(GearGuide.NextSort(_sort)), "SortToggle");
                var t = KitV2.Text(d, title + " · " + list.Count, 250, 43, 600, 59, 53.3f, Color.white, TextAnchor.MiddleCenter, "InventoryTitle");
                KitFit.On(t, 28);
                KitV2.SmallButton(d, "button_small_yellow", 874, 29, 179, 85, KitV2.Yellow, "Feed", OpenFeed, "FeedSpares");
                if (feedable > 0) KitV2.BadgeRed(d, 1009, 7, feedable > 99 ? "99+" : feedable.ToString(), 64, "FeedBadge");
            });

            ShownParts.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var part = list[i];
                ShownParts.Add(part.Uid);
                var data = PartTileView.From(State, Content, part, Locks);
                var tile = Part(design, "tile:" + part.Uid, PartTileView.Key(data), g => PartTileView.Build(g, data, PartTileView.GridTile, () => Detail(part.Uid), "Part:" + part.Uid));
                KitV2.At(tile, GridX + (i % GridColumns) * GridStepX, GridY + (i / GridColumns) * GridStepY, PartTileView.GridTile, PartTileView.GridTile + PartTileView.StripHeight);
            }
            if (list.Count == 0)
            {
                string empty = State.Parts.Items.Count == 0 ? "No parts yet. Parts come from Parts pulls in the Shop and boss first clears."
                    : "Every part you own is equipped. Spares land here.";
                Part(design, "inv:empty", empty, d =>
                {
                    var t = KitV2.Text(d, empty, 80, 220, 920, 160, 38, Theme.Hex(0xD8C8B0), TextAnchor.MiddleCenter, "EmptyNote", false);
                    t.horizontalOverflow = HorizontalWrapMode.Wrap;
                });
            }
        }

        // ---- sheet helpers --------------------------------------------------------------------------

        private static readonly Color SheetText = Color.white, SheetDim = Theme.Hex(0xB8B0D8), SheetCard = Theme.Hex(0x3A3550);
        private static readonly Color BtnGreen = Theme.Hex(0x3FBF4A), BtnYellow = Theme.Hex(0xF7B52A), BtnPurple = Theme.Hex(0x7A5CE0),
            BtnRed = Theme.Hex(0xE0533A), BtnGrey = Theme.Hex(0x4A4466), BtnBlue = Theme.Hex(0x3C9EE6);

        /// <summary>A kit v2 sheet: navy box with a navy outline; build fills it (it closes itself on its buttons).</summary>
        protected UiModal KitSheet(string name, Action<RectTransform, UiModal> build) =>
            Ui.Sheet(App.ModalLayer, name, KitV2.Navy, KitV2.Ink, build, 960);

        private static RectTransform SheetSection(Transform parent, string title, string name)
        {
            var card = Ui.Card(parent, SheetCard, KitV2.Ink, 28, 22, 8, 3, name);
            if (title != null) Kit.Title(card, title, 30, Theme.Hex(0xFFE14D), TextAnchor.MiddleLeft, "SectionTitle");
            return card;
        }

        private static Text SheetLabel(Transform parent, string text, int size, Color color, string name = "Label", TextAnchor align = TextAnchor.MiddleLeft)
        {
            var t = Ui.Label(parent, text, size, color, align, name);
            t.supportRichText = true;
            return t;
        }

        private static Button SheetButton(Transform parent, string label, string sub, Color color, Action onClick, bool interactable, float width, string name, float height = 104)
        {
            var b = Ui.Button(parent, label + (sub != null ? "\n<size=22>" + sub + "</size>" : ""), onClick, color, width, height, interactable, 30);
            b.name = name;
            return b;
        }

        /// <summary>A part tile (no layout) inside a layout row: a holder of the tile's size.</summary>
        private RectTransform TileIn(Transform parent, PartTileView.Data d, float size, Action onClick, string name)
        {
            var holder = Ui.Rect(parent, name + "Holder");
            Ui.Size(holder, size, size + (d.Pips ? PartTileView.StripHeight : 0), 0, 0);
            var t = PartTileView.Build(holder, d, size, onClick, name);
            t.anchoredPosition = Vector2.zero;
            return t;
        }

        private static void CloseButton(RectTransform box, Action onClose)
        {
            var b = Ui.Button(box, "X", onClose, BtnGrey, 84, 84, true, 36);
            b.name = "SheetClose";
            var rt = (RectTransform)b.transform;
            var le = b.GetComponent<LayoutElement>() ?? b.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
            rt.sizeDelta = new Vector2(84, 84);
            rt.anchoredPosition = new Vector2(-22, -22);
        }

        /// <summary>A grid of part tiles (no pips) in a scrolling area; 5 columns of 150.</summary>
        private void TileGrid(Transform parent, List<PartItem> parts, Func<PartItem, bool> selected, Action<PartItem> onTap, string prefix, float maxHeight = 640, bool pips = false)
        {
            int rows = Mathf.Max(1, (parts.Count + 4) / 5);
            float cell = 150, gap = 18;
            float h = Mathf.Min(maxHeight, rows * (cell + gap + (pips ? PartTileView.StripHeight : 0)) + 24);
            var frame = Ui.Rect(parent, prefix + "Grid");
            Ui.Size(frame, -1, h, 1, 0);
            var content = Ui.Scroll(frame, out _, 0, 0);
            var grid = Ui.Grid(content, new Vector2(cell, cell + (pips ? PartTileView.StripHeight : 0)), 5, gap);
            var gl = grid.GetComponent<GridLayoutGroup>();
            gl.padding = new RectOffset(12, 0, 22, 8);
            gl.childAlignment = TextAnchor.UpperLeft;
            foreach (var p in parts)
            {
                var part = p;
                var d = PartTileView.From(State, Content, p, Locks, false);
                d.Pips = pips; d.Selected = selected != null && selected(p);
                PartTileView.Build(grid, d, cell, () => onTap(part), prefix + part.Uid);
            }
        }

        // ---- part sheet -----------------------------------------------------------------------------

        /// <summary>Tallest the sheet's scrolling body gets (it scrolls beyond).</summary>
        public const float DetailMaxHeight = 1180;

        /// <summary>The sheet of a part: header, ladder, stats, skills, runes and the actions for its state.</summary>
        public void Detail(string uid)
        {
            var p = PartsSystem.Get(State, uid);
            if (p == null) return;
            Ui.Clear(App.ModalLayer);
            var def = PartsSystem.Def(Content, p);
            var slot = GearGuide.EquippedSlot(State, uid);
            bool locked = Locks.Contains(uid) || p.Locked;
            int cap = PartsSystem.LevelCap(Content, p);
            var modal = KitSheet("PartDetail", (box, m) =>
            {
                var scroll = Ui.ScrollingColumn(box, DetailMaxHeight, b =>
                {
                    SheetHeader(b, p, def, slot, cap);
                    if (def.IsMythic) Ladder(b, p);
                    StatsSection(b, p, cap);
                    SkillsSection(b, p, def);
                    RunesSection(b, p);
                    if (!def.IsMythic)
                        SheetLabel(b, "Standard parts never merge or climb. A spare one feeds into " + PartsSystem.MetalValue(Content, p) + " Metal for the Mythic ladder.", 22, SheetDim, "StdNote");
                }, 18, 888);
                Actions(box, p, def, slot, locked, cap);
                CloseButton(box, () => { _sheetAwakened = false; m.Close(); });
            });
            modal.Root.name = "Modal:PartDetail";
        }

        private void SheetHeader(Transform b, PartItem p, PartDef def, PartSlot? slot, int cap)
        {
            var colors = KitV2.TileColors(p.Rarity);
            var band = Ui.Card(b, Color.white, KitV2.Ink, 32, 20, 0, 5, "SheetHeader");
            var g = band.gameObject.AddComponent<KitGradient>();
            g.Set(Color.Lerp(colors.top, KitV2.Navy, 0.35f), Color.Lerp(colors.bottom, KitV2.Navy, 0.55f));
            var row = Ui.Row(band, 230, 22, null, 0, "HeaderRow");
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            var d = PartTileView.From(State, Content, p, Locks, false);
            TileIn(row, d, 170, null, "SheetTile");
            var col = Ui.Column(row, 6, 0, null, "Who");
            Ui.Size(col, -1, -1, 1);
            var name = Kit.Title(col, PartName(Content, p), 44, Color.white, TextAnchor.MiddleLeft, "PartName");
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Size(name, -1, 56, 1);
            KitFit.On(name, 26);
            string kind = def.Kind == PartKind.Arm ? "Arm (either arm)" : def.Kind.ToString();
            string family = def.IsMythic ? (def.Synergy ?? "General") : def.Role.ToString();
            var line = Kit.Title(col, RarityName(Content, p.Rarity) + "  ·  " + family + "  ·  " + kind, 28, KitV2.RankColor(p.Rarity), TextAnchor.MiddleLeft, "RarityLine");
            Ui.Size(line, -1, 38, 1);
            KitFit.On(line, 18);
            var chips = Ui.ChipRow(col, 40, 8);
            if (slot.HasValue) Ui.Chip(chips, "ON " + GearGuide.SlotName(slot.Value).ToUpperInvariant(), Theme.Good, Theme.OnAccent, 20, 38, null, "EquippedChip");
            if (p.Awaken > 0) Ui.Chip(chips, "AWAKENED", Theme.Hex(0xFFC23D), KitV2.Ink, 20, 38, null, "AwakenedChip");
            if (p.Locked || Locks.Contains(p.Uid)) Ui.Chip(chips, "LOCKED", Theme.Hex(0x5B5578), Color.white, 20, 38, null, "LockedChip");
            var bar = Ui.Bar(col, cap > 0 ? p.Level / (float)cap : 1, Theme.Hex(0xFFC23D), "Lv " + p.Level + " / " + cap, 40);
            if (bar.Label != null) bar.Label.name = "LevelText";
            if (def.IsMythic && !string.IsNullOrEmpty(def.Identity)) SheetLabel(band, def.Identity, 22, Theme.Hex(0xF3E6C8), "Identity");
        }

        /// <summary>The Mythic ladder: M (70) → +1 → +2 → S (skill 4) → S+1 → S+2 → SS (skill 5, final); this part's step lit.</summary>
        private void Ladder(Transform b, PartItem p)
        {
            var sec = SheetSection(b, "Mythic ladder", "Ladder");
            var row = Ui.Row(sec, 150, 6, null, 0, "LadderSteps");
            var cfg = Content.PartsConfig;
            for (int r = (int)PartRarity.Mythic; r <= (int)cfg.TopRarity; r++)
            {
                bool done = p.Rarity > r, now = p.Rarity == r;
                bool skill = cfg.RarityDef(r).SkillCount > cfg.RarityDef(r - 1).SkillCount && r > (int)PartRarity.Mythic;
                var col = Ui.Column(row, 2, 0, null, "Step:" + PartArt.Rank(r));
                col.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
                Ui.Size(col, -1, 150, 1);
                var fill = now ? Theme.Hex(0xFFC23D) : done ? Theme.Hex(0xD93B3B) : Theme.Hex(0x2B2847);
                var node = Ui.Rect(col, "Node");
                Ui.Size(node, 84, 84, 0, 0);
                var ni = node.gameObject.AddComponent<Image>();
                ni.sprite = UiShapes.Hex; ni.color = fill; ni.preserveAspect = true; ni.raycastTarget = false;
                var nt = Kit.Title(node, PartArt.Rank(r), r == (int)PartRarity.Mythic || r == (int)PartRarity.MythicS || r == (int)PartRarity.MythicSS ? 34 : 26,
                    Color.white, TextAnchor.MiddleCenter, "Rank");
                Ui.Stretch(nt.rectTransform);
                SheetLabel(col, "Lv " + cfg.RarityDef(r).LevelCap, 20, now ? Theme.Hex(0xFFE14D) : SheetDim, "Cap", TextAnchor.MiddleCenter);
                SheetLabel(col, skill ? "+skill " + cfg.RarityDef(r).SkillCount : "", 18, done || now ? Theme.Hex(0x8FE39A) : Theme.Hex(0xC084FC), "Adds", TextAnchor.MiddleCenter);
            }
            var step = PartsSystem.NextClimb(Content, p);
            if (step == null) SheetLabel(sec, "Mythic SS: the top of the ladder (level cap 100, all five skills).", 22, SheetDim, "LadderNext");
            else
                SheetLabel(sec, "Next: " + RarityName(Content, p.Rarity + 1) + " (cap " + cfg.RarityDef(p.Rarity + 1).LevelCap + ") takes " + step.Copies + " cop" + (step.Copies == 1 ? "y" : "ies")
                    + (step.Others > 0 ? " + " + step.Others + " other " + PartsSystem.Def(Content, p).Kind + " Mythics" : "") + ", " + Fmt.Num(step.Metal) + " Metal, " + Fmt.Num(step.Ore) + " Ore.", 22, SheetDim, "LadderNext");
        }

        private void StatsSection(Transform b, PartItem p, int cap)
        {
            var sec = SheetSection(b, null, "Stats");
            var now = PartsSystem.PartStats(State, Content, p);
            var capItem = PartPlanner.CloneItem(p);
            capItem.Level = cap;
            var atCap = PartsSystem.PartStats(State, Content, capItem);
            var head = Ui.Row(sec, 44, 8, null, 0, "StatsHead");
            Ui.Size(Kit.Title(head, "Stats", 30, Theme.Hex(0xFFE14D), TextAnchor.MiddleLeft, "SectionTitle"), 330, -1, 0);
            Ui.Size(SheetLabel(head, "Now · Lv " + p.Level, 24, SheetDim, "NowHead"), -1, -1, 1);
            Ui.Size(SheetLabel(head, "At cap · Lv " + cap, 24, SheetDim, "CapHead"), -1, -1, 1);
            var keys = new List<StatId>(now.Flat.Keys);
            foreach (var k in atCap.Flat.Keys) if (!keys.Contains(k)) keys.Add(k);
            foreach (var k in keys)
            {
                now.Flat.TryGetValue(k, out double a); atCap.Flat.TryGetValue(k, out double c);
                StatLine(sec, Fmt.StatName(k), WormScreen.StatText(k, a), WormScreen.StatText(k, c), c > a + 1e-9);
            }
            var pkeys = new List<StatId>(now.Percent.Keys);
            foreach (var k in atCap.Percent.Keys) if (!pkeys.Contains(k)) pkeys.Add(k);
            foreach (var k in pkeys)
            {
                now.Percent.TryGetValue(k, out double a); atCap.Percent.TryGetValue(k, out double c);
                StatLine(sec, Fmt.StatName(k) + " %", "+" + Fmt.Pct(a), "+" + Fmt.Pct(c), c > a + 1e-9);
            }
        }

        private static void StatLine(Transform parent, string stat, string now, string cap, bool grows)
        {
            var row = Ui.Row(parent, 40, 8, null, 0, "StatRow");
            Ui.Size(SheetLabel(row, stat, 24, SheetDim, "Stat"), 330, -1, 0);
            Ui.Size(Kit.Title(row, now, 28, Color.white, TextAnchor.MiddleLeft, "Now"), -1, -1, 1);
            Ui.Size(Kit.Title(row, cap, 28, grows ? Theme.Hex(0x8FE39A) : Color.white, TextAnchor.MiddleLeft, "Cap"), -1, -1, 1);
        }

        /// <summary>Short "at S" text of the step a Mythic skill unlocks at.</summary>
        public static string UnlockShort(PartRarity r) => r == PartRarity.MythicSS ? "at SS" : r == PartRarity.MythicS ? "at S" : "at " + PartArt.Rank((int)r);

        private void SkillsSection(Transform b, PartItem p, PartDef def)
        {
            if (def.SkillIds.Count == 0) return;
            bool aw = p.Awaken > 0 || _sheetAwakened;
            var lines = PartPlanner.SkillLines(Content, p, aw);
            int active = Math.Min(lines.Count, PartsSystem.SkillCount(Content, p.Rarity));
            var sec = SheetSection(b, null, "Skills");
            var head = Ui.Row(sec, 56, 10, null, 0, "SkillsHead");
            Ui.Size(Kit.Title(head, (aw && p.Awaken == 0 ? "Awakened skills" : "Skills") + "  " + active + "/" + lines.Count + " active", 30, Theme.Hex(0xFFE14D), TextAnchor.MiddleLeft, "SectionTitle"), -1, -1, 1);
            if (def.IsMythic && p.Awaken == 0 && def.AwakenedSkillIds.Count > 0)
            {
                string uid = p.Uid;
                Ui.Button(head, _sheetAwakened ? "Now" : "Awakened", () => { _sheetAwakened = !_sheetAwakened; Detail(uid); }, BtnPurple, 220, 56, true, 24).name = "ToggleAwakened";
            }
            foreach (var sk in lines)
            {
                var card = Ui.Card(sec, sk.Unlocked ? Theme.Hex(0x2B2847) : Theme.Hex(0x302C45), sk.Unlocked ? Theme.Hex(0x6A4FD0) : Theme.Hex(0x45405E), 20, 16, 4, 3, sk.Unlocked ? "Skill:on" : "Skill:off");
                var top = Ui.Row(card, 40, 8, null, 0, "SkillTop");
                Ui.Size(Kit.Title(top, sk.Name, 28, sk.Unlocked ? Color.white : Theme.Hex(0x8C86A8), TextAnchor.MiddleLeft, "SkillName"), -1, -1, 1);
                if (!sk.Unlocked) Ui.Chip(top, UnlockShort(sk.UnlockRarity).ToUpperInvariant(), Theme.Hex(0x5B5578), Color.white, 20, 36, null, "SkillLock");
                SheetLabel(card, Fmt.SkillFull(Content.Skill(sk.SkillId)), 23, sk.Unlocked ? Theme.Hex(0xE8E4F8) : Theme.Hex(0x8C86A8), "SkillText");
            }
        }

        private void RunesSection(Transform b, PartItem p)
        {
            int runeSlots = PartsSystem.RuneSlotCount(Content, p);
            if (runeSlots <= 0) return;
            string uid = p.Uid;
            var sec = SheetSection(b, "Rune sockets", "Runes");
            var runeRow = Ui.Row(sec, 72, 8, null, 0, "RuneRow");
            bool unlocked = Session.IsUnlocked(Features.Runes);
            for (int i = 0; i < runeSlots; i++)
            {
                int ri = i;
                string runeUid = i < p.Runes.Count ? p.Runes[i] : "";
                var rune = !string.IsNullOrEmpty(runeUid) && State.Parts.Runes.TryGetValue(runeUid, out var r) ? r : null;
                Ui.Button(runeRow, rune == null ? "+ rune" : RuneShort(rune), () =>
                {
                    if (rune != null) Run(new RuneUnsocket { PartUid = uid, SlotIndex = ri }, _ => Reopen(uid));
                    else SocketModal(uid, ri);
                }, rune == null ? BtnGrey : Theme.RarityColor((Rarity)rune.Rarity), -1, 72, unlocked, 22).name = "Rune:" + i;
            }
            if (!unlocked) SheetLabel(sec, "Runes unlock later.", 22, SheetDim, "RunesLocked");
        }

        /// <summary>The sheet's action buttons for the part's state (below the scroll, always visible).</summary>
        private void Actions(Transform box, PartItem p, PartDef def, PartSlot? slot, bool locked, int cap)
        {
            string uid = p.Uid;
            var cfg = Content.PartsConfig;
            var acts = Ui.Column(box, 12, 0, null, "Actions");

            // level up: the next level's Ore (and Gear Cores), or the cap
            var lvRow = Ui.Row(acts, 104, 12, null, 0, "LevelRow");
            if (GearGuide.NextLevelCost(Content, p, out long ore, out long cores))
            {
                bool afford = GearGuide.CanLevelUp(State, Content, p);
                string price = Fmt.Num(ore) + " Ore" + (cores > 0 ? " + " + Fmt.Num(cores) + " Gear Cores" : "");
                SheetButton(lvRow, "Level up", price, afford ? BtnGreen : BtnGrey, () => Run(new PartLevelUp { Uid = uid }, _ => Reopen(uid)), afford, -1, "PartLevelUp");
                if (!afford) SheetButton(lvRow, "Ore " + Fmt.Num(State.Wallet.Get(Currencies.Ore)), "where to get", BtnBlue, () => App.Bar.ShowCurrency(Currencies.Ore), true, 260, "OreInfo");
            }
            else
            {
                var step = PartsSystem.NextClimb(Content, p);
                var t = SheetLabel(lvRow, "Level cap " + cap + " reached." + (step != null ? " Climb for a higher cap." : def.IsMythic ? "" : " Standard parts keep their rarity's cap."), 26, SheetDim, "LevelCapNote");
                Ui.Size(t, -1, -1, 1);
            }

            // equip (either arm), climb, awaken
            var row = Ui.Row(acts, 104, 12, null, 0, "EquipRow");
            if (def.Kind == PartKind.Arm)
            {
                foreach (var arm in new[] { PartSlot.LeftArm, PartSlot.RightArm })
                {
                    var a = arm;
                    if (slot == a) continue;
                    string label = slot.HasValue ? "Move to " + (a == PartSlot.LeftArm ? "left" : "right") : "Equip " + (a == PartSlot.LeftArm ? "left arm" : "right arm");
                    State.Parts.Equipped.TryGetValue(a, out var wornUid);
                    var worn = PartsSystem.Get(State, wornUid);
                    SheetButton(row, label, worn != null && !slot.HasValue ? "swaps " + PartArt.Rank(worn.Rarity) + " Lv." + worn.Level : null, BtnBlue,
                        () => Run(new PartEquip { Uid = uid, Slot = a }, _ => Reopen(uid)), true, -1, "Equip:" + a);
                }
            }
            else if (!slot.HasValue)
            {
                var target = (PartSlot)(int)def.Kind;
                State.Parts.Equipped.TryGetValue(target, out var wornUid);
                var worn = PartsSystem.Get(State, wornUid);
                SheetButton(row, "Equip", worn != null ? "swaps " + PartArt.Rank(worn.Rarity) + " Lv." + worn.Level : null, BtnBlue,
                    () => Run(new PartEquip { Uid = uid, Slot = target }, _ => Reopen(uid)), true, -1, "Equip:" + target);
            }
            if (slot.HasValue)
            {
                var s = slot.Value;
                SheetButton(row, "Unequip", null, BtnGrey, () => Run(new PartUnequip { Slot = s }, _ => Reopen(uid)), true, 220, "Unequip");
            }
            if (def.IsMythic)
            {
                var step = PartsSystem.NextClimb(Content, p);
                if (step != null)
                {
                    var plan = PartPlanner.AutoMerge(State, Content, p, Locks);
                    SheetButton(row, "Climb to " + PartArt.Rank(p.Rarity + 1), plan.Valid ? "ready" : "see needs", plan.Valid ? BtnRed : BtnGrey, () => OpenMergePicker(uid, false), true, -1, "OpenClimb");
                }
                if (GearGuide.CanAwaken(State, Content, p))
                    SheetButton(row, "Awaken", State.Wallet.Get(ProgressionCurrencies.PartAwakenCores) + "/" + cfg.AwakenCost + " cores", BtnPurple, () => AwakenModal(uid), true, -1, "OpenAwaken");
            }

            // lock, reset, Forge, feed
            var small = Ui.Row(acts, 80, 12, null, 0, "SmallRow");
            SheetButton(small, locked ? "Unlock" : "Lock", null, locked ? BtnYellow : BtnGrey, () =>
            {
                PartLocks.Set(State.PlayerId, uid, !locked);
                Run(new PartLock { Uid = uid, Locked = !locked }, _ => { Reopen(uid); Refresh(); });
            }, true, -1, "PartLock", 80);
            if (p.Level > 1)
                SheetButton(small, "Reset", "+" + Fmt.Num(p.SpentOre) + " Ore", BtnGrey, () => Run(new PartLevelDown { Uid = uid }, _ => Reopen(uid)), true, -1, "ResetLevel", 80);
            if (Session.IsUnlocked(Features.Forge) && p.Rarity >= (int)cfg.ForgeMinRarity)
            {
                bool forge = p.ForgedSlots < cfg.Forge.Count;
                SheetButton(small, "Forge", forge ? cfg.Forge[p.ForgedSlots].Diamonds + " Diamonds" : "done", BtnPurple, () => Run(new PartForgeUnlock { Uid = uid }, _ => Reopen(uid)), forge, -1, "Forge", 80);
            }
            if (!def.IsMythic && !slot.HasValue)
                SheetButton(small, "Feed", "+" + PartsSystem.MetalValue(Content, p) + " Metal", BtnYellow, () => Feed(uid), PartsSystem.IsFeedable(State, Content, p) && !Locks.Contains(uid), -1, "Feed", 80);
        }

        private void Feed(string uid)
        {
            var p = PartsSystem.Get(State, uid);
            if (p == null) return;
            void Go() { var cmd = new PartFeed(); cmd.Uids.Add(uid); Ui.Clear(App.ModalLayer); Run(cmd); }
            if (p.Rarity < (int)PartRarity.Legendary) { Go(); return; }
            Modal("Feed " + PartName(Content, p) + "?", b => Note(b, RarityName(Content, p.Rarity) + ": becomes " + PartsSystem.MetalValue(Content, p)
                + " Metal for the Mythic ladder; its level Ore and runes come back."),
                new ModalButton("Cancel", () => Reopen(uid)), new ModalButton("Feed", Go, BtnYellow));
        }

        /// <summary>Close the open sheets and reopen the part sheet on the updated state.</summary>
        protected void Reopen(string uid)
        {
            Ui.Clear(App.ModalLayer);
            if (PartsSystem.Get(State, uid) != null) Detail(uid);
        }

        // ---- empty slot sheet -----------------------------------------------------------------------

        /// <summary>An empty body slot: the spares that fit it (best first); tap one to equip it there.</summary>
        public void SlotSheet(PartSlot slot)
        {
            Ui.Clear(App.ModalLayer);
            var spares = GearGuide.SparesFor(State, Content, slot);
            KitSheet("SlotSheet", (box, m) =>
            {
                Kit.Title(box, GearGuide.SlotName(slot) + " slot", 44, Color.white, TextAnchor.MiddleCenter, "Title");
                if (spares.Count == 0) SheetLabel(box, "No spare part fits here yet. Parts come from Parts pulls in the Shop and boss first clears.", 26, SheetDim, "NoSpares", TextAnchor.MiddleCenter);
                else
                {
                    SheetLabel(box, "Tap a part to equip it.", 24, SheetDim, "Hint", TextAnchor.MiddleCenter);
                    TileGrid(box, spares, null, p =>
                    {
                        m.Close();
                        Run(new PartEquip { Uid = p.Uid, Slot = slot });
                    }, "Spare:", 700, true);
                }
                SheetButton(box, "Close", null, BtnGrey, m.Close, true, -1, "SlotClose", 90);
            }).Root.name = "Modal:SlotSheet";
        }

        // ---- climb / Replicator picker ----------------------------------------------------------------

        /// <summary>The current picker selection evaluated by the rules (null when no picker is open).</summary>
        public MergePlan PickerPlan { get; private set; }

        /// <summary>Open the Climb (or Replicator) picker for a Mythic, pre-filled with the auto-selected materials.</summary>
        public void OpenMergePicker(string uid, bool replicate)
        {
            _pickTarget = uid;
            _pickReplicate = replicate;
            AutoPick();
        }

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

        private void NeedRow(Transform parent, string what, string have, bool ok, string name)
        {
            var row = Ui.Row(parent, 46, 10, null, 0, name);
            Ui.Size(SheetLabel(row, what, 26, SheetDim, "What"), -1, -1, 1);
            Kit.Title(row, have, 30, ok ? Theme.Hex(0x8FE39A) : Theme.Hex(0xFF8F8F), TextAnchor.MiddleRight, "Have");
        }

        private void ShowPicker()
        {
            var t = PartsSystem.Get(State, _pickTarget);
            Ui.Clear(App.ModalLayer);
            if (t == null) { PickerPlan = null; return; }
            var plan = PartPlanner.Evaluate(State, Content, t, _picked, Locks, _pickReplicate);
            PickerPlan = plan;
            var def = PartsSystem.Def(Content, t);
            string title = _pickReplicate ? "Replicator: " + def.Name : "Climb " + PartName(Content, t) + (plan.NextRarity >= 0 ? " to " + RarityName(Content, plan.NextRarity) : "");
            var cands = PickerCandidates(t, plan);
            KitSheet("Picker", (box, m) =>
            {
                var head = Kit.Title(box, title, 40, Color.white, TextAnchor.MiddleCenter, "Title");
                head.horizontalOverflow = HorizontalWrapMode.Wrap;
                var needs = SheetSection(box, _pickReplicate ? "3 spare Mythics make 1 copy" : "This step needs", "Needs");
                int othersOwned = 0;
                foreach (var c in PartPlanner.Candidates(State, Content, t))
                    if (c.DefId != t.DefId && (_pickReplicate || PartsSystem.Def(Content, c).Kind == def.Kind)) othersOwned++;
                if (!_pickReplicate)
                {
                    NeedRow(needs, "Copies of " + def.Name + " (you have " + plan.CopiesOwned + ")", plan.Copies.Count + " / " + plan.CopiesNeeded, plan.Copies.Count == plan.CopiesNeeded, "NeedCopies");
                    if (plan.FodderNeeded > 0 || plan.Fodder.Count > 0)
                        NeedRow(needs, "Other " + def.Kind + " Mythics (you have " + othersOwned + ")", plan.FodderPoints + " / " + plan.FodderNeeded, plan.FodderPoints == plan.FodderNeeded, "NeedOthers");
                    long metal = State.Wallet.Get(Currencies.Metal);
                    NeedRow(needs, "Metal (you have " + Fmt.Num(metal) + ")", Fmt.Num(plan.Metal), metal >= plan.Metal, "NeedMetal");
                }
                else NeedRow(needs, "Spare Mythics, any slot (you have " + othersOwned + ")", plan.FodderPoints + " / " + plan.FodderNeeded, plan.FodderPoints == plan.FodderNeeded, "NeedSpares");
                long oreHave = State.Wallet.Get(Currencies.Ore);
                NeedRow(needs, "Ore (you have " + Fmt.Num(oreHave) + ")", Fmt.Num(plan.Ore), oreHave >= plan.Ore, "NeedOre");
                var status = SheetLabel(box, plan.Valid ? (plan.NeedsConfirmation ? "Ready: uses " + plan.Protected.Count + " protected part(s), you will be asked to confirm." : "Ready.") : Fmt.Humanize(plan.Error),
                    26, plan.Valid ? (plan.NeedsConfirmation ? Theme.Hex(0xFFE14D) : Theme.Hex(0x8FE39A)) : Theme.Hex(0xFF8F8F), "PickerStatus", TextAnchor.MiddleCenter);
                if (cands.Count == 0) SheetLabel(box, "No eligible Mythics: only base-step Mythics that are not equipped, awakened, locked or forged.", 24, SheetDim, "NoCandidates", TextAnchor.MiddleCenter);
                else TileGrid(box, cands, p => _picked.Contains(p.Uid), p => TogglePick(p.Uid), "Pick:", 520);
                var tools = Ui.Row(box, 90, 12, null, 0, "PickerTools");
                SheetButton(tools, "Auto-select", null, BtnBlue, AutoPick, true, -1, "PickAuto", 90);
                SheetButton(tools, "Clear", null, BtnGrey, ClearPick, true, 220, "PickClear", 90);
                var bottom = Ui.Row(box, 104, 12, null, 0, "PickerButtons");
                SheetButton(bottom, "Cancel", null, BtnGrey, () => { PickerPlan = null; Reopen(_pickTarget); }, true, 260, "PickCancel");
                SheetButton(bottom, _pickReplicate ? "Replicate" : "Climb", null, plan.Valid ? BtnRed : BtnGrey, ConfirmPick, plan.Valid, -1, "PickConfirm");
            }).Root.name = "Modal:Picker";
        }

        /// <summary>Eligible candidates: selected ones first, then copies / other Mythics by level.</summary>
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

        /// <summary>Climb / replicate the selection; protected materials (locked, copies of an equipped Mythic) need a confirmation.</summary>
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

        /// <summary>Auto-selected ladder materials for the next step (null + reason when not possible).</summary>
        public List<string> FindMergeMaterials(PartItem target, out string why)
        {
            var plan = PartPlanner.AutoMerge(State, Content, target, Locks);
            why = plan.Error;
            return plan.Valid ? plan.Materials : null;
        }

        // ---- Feed spares / Replicator sheet --------------------------------------------------------------

        /// <summary>The feed selection (uids) of the open Feed sheet.</summary>
        public IReadOnlyList<string> FeedSelection => _feedSel ?? (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>Metal the selection turns into (the rules' ValidateFeed; 0 for an empty selection).</summary>
        public long FeedPreviewMetal => _feedSel == null || _feedSel.Count == 0 ? 0 : PartsSystem.ValidateFeed(State, Content, _feedSel);

        /// <summary>Open the Feed sheet with every spare Common and Rare selected.</summary>
        public void OpenFeed()
        {
            _feedTab = 0;
            _feedSel = new List<string>();
            foreach (var p in PartPlanner.Feedable(State, Content, Locks)) if (p.Rarity <= (int)PartRarity.Rare) _feedSel.Add(p.Uid);
            ShowFeed();
        }

        /// <summary>The Replicator tab of the Feed sheet.</summary>
        public void OpenReplicator()
        {
            _feedTab = 1;
            _feedSel = _feedSel ?? new List<string>();
            ShowFeed();
        }

        public void ToggleFeed(string uid)
        {
            if (_feedSel == null) _feedSel = new List<string>();
            if (!_feedSel.Remove(uid)) _feedSel.Add(uid);
            ShowFeed();
        }

        /// <summary>Select (or, when all are selected, clear) every feedable spare of a rarity.</summary>
        public void ToggleFeedRarity(int rarity)
        {
            if (_feedSel == null) _feedSel = new List<string>();
            var of = PartPlanner.Feedable(State, Content, Locks).FindAll(p => p.Rarity == rarity);
            bool all = of.Count > 0 && of.TrueForAll(p => _feedSel.Contains(p.Uid));
            foreach (var p in of)
            {
                if (all) _feedSel.Remove(p.Uid);
                else if (!_feedSel.Contains(p.Uid)) _feedSel.Add(p.Uid);
            }
            ShowFeed();
        }

        public void ConfirmFeed()
        {
            if (_feedSel == null || _feedSel.Count == 0) return;
            var cmd = new PartFeed();
            cmd.Uids.AddRange(_feedSel);
            _feedSel = null;
            Ui.Clear(App.ModalLayer);
            Run(cmd);
        }

        private void ShowFeed()
        {
            Ui.Clear(App.ModalLayer);
            var feedable = PartPlanner.Feedable(State, Content, Locks);
            feedable.Sort((a, b) => a.Rarity != b.Rarity ? a.Rarity.CompareTo(b.Rarity) : string.CompareOrdinal(a.Uid, b.Uid));
            _feedSel.RemoveAll(u => !feedable.Exists(p => p.Uid == u));
            KitSheet("Feed", (box, m) =>
            {
                var tabs = Ui.Row(box, 96, 12, null, 0, "FeedTabs");
                SheetButton(tabs, "Feed spares", null, _feedTab == 0 ? BtnYellow : BtnGrey, () => { _feedTab = 0; ShowFeed(); }, true, -1, "FeedTab:feed", 96);
                SheetButton(tabs, "Replicator", null, _feedTab == 1 ? BtnPurple : BtnGrey, () => { _feedTab = 1; ShowFeed(); }, true, -1, "FeedTab:replicator", 96);
                if (_feedTab == 0) FeedTab(box, feedable);
                else ReplicatorTab(box);
                SheetButton(box, "Close", null, BtnGrey, () => { _feedSel = null; m.Close(); }, true, -1, "FeedClose", 90);
            }).Root.name = "Modal:Feed";
        }

        private void FeedTab(Transform box, List<PartItem> feedable)
        {
            SheetLabel(box, "Spare standard parts (not equipped, locked or forged; never Mythics) become Metal for the Mythic ladder: Common 1, Rare 2, Epic 6, Legendary 20. Their level Ore and runes come back.",
                23, SheetDim, "FeedRule");
            var chips = Ui.Row(box, 76, 10, null, 0, "FeedRarities");
            for (int r = 0; r <= (int)PartRarity.Legendary; r++)
            {
                int rr = r;
                int n = feedable.FindAll(p => p.Rarity == rr).Count;
                int sel = feedable.FindAll(p => p.Rarity == rr && _feedSel.Contains(p.Uid)).Count;
                var b = SheetButton(chips, PartArt.Rank(r) + " " + sel + "/" + n, null, sel > 0 ? Color.Lerp(KitV2.TileColors(r).bottom, KitV2.Navy, 0.2f) : BtnGrey,
                    () => ToggleFeedRarity(rr), n > 0, -1, "FeedRarity:" + r, 76);
            }
            if (feedable.Count == 0) SheetLabel(box, "No spare standard parts to feed.", 26, SheetDim, "NothingToFeed", TextAnchor.MiddleCenter);
            else TileGrid(box, feedable, p => _feedSel.Contains(p.Uid), p => ToggleFeed(p.Uid), "FeedPart:", 600);
            long metal = FeedPreviewMetal;
            var sum = Ui.Row(box, 70, 10, null, 0, "FeedSummary");
            Ui.Size(SheetLabel(sum, _feedSel.Count + " part" + (_feedSel.Count == 1 ? "" : "s") + " selected", 28, Color.white, "FeedCount"), -1, -1, 1);
            var icon = Ui.Rect(sum, "MetalIcon");
            Ui.Size(icon, 60, 60, 0, 0);
            var ii = icon.gameObject.AddComponent<Image>(); ii.sprite = SummonArt.CurrencyIcon(Currencies.Metal); ii.preserveAspect = true; ii.raycastTarget = false;
            Kit.Title(sum, "+" + Fmt.Num(metal) + " Metal", 40, Theme.Hex(0xFFE14D), TextAnchor.MiddleRight, "FeedMetal");
            SheetButton(box, "Feed " + _feedSel.Count, "+" + Fmt.Num(metal) + " Metal (have " + Fmt.Num(State.Wallet.Get(Currencies.Metal)) + ")", _feedSel.Count > 0 ? BtnYellow : BtnGrey, ConfirmFeed, _feedSel.Count > 0, -1, "FeedConfirm");
        }

        private void ReplicatorTab(Transform box)
        {
            var cfg = Content.PartsConfig;
            int spare = PartPlanner.Candidates(State, Content, null).Count;
            SheetLabel(box, cfg.ReplicateMythics + " spare Mythics (any slot, base step, not awakened) + " + Fmt.Num(cfg.ReplicateOre) + " Ore make 1 copy of a Mythic you own, for its ladder. You have "
                + spare + " spare Mythic" + (spare == 1 ? "" : "s") + ".", 23, SheetDim, "ReplicatorRule");
            // one tile per owned Mythic item (the equipped one first): tap → pick the 3 spares
            var reps = new List<PartItem>();
            var seen = new HashSet<string>();
            var all = new List<PartItem>(State.Parts.Items.Values);
            all.Sort((a, b) =>
            {
                int ea = PartsSystem.IsEquipped(State, a.Uid) ? 0 : 1, eb = PartsSystem.IsEquipped(State, b.Uid) ? 0 : 1;
                if (ea != eb) return ea.CompareTo(eb);
                return a.Rarity != b.Rarity ? b.Rarity.CompareTo(a.Rarity) : string.CompareOrdinal(a.Uid, b.Uid);
            });
            foreach (var p in all) if (PartsSystem.Def(Content, p).IsMythic && seen.Add(p.DefId)) reps.Add(p);
            if (reps.Count == 0) { SheetLabel(box, "You own no Mythic yet.", 26, SheetDim, "NoMythics", TextAnchor.MiddleCenter); return; }
            SheetLabel(box, "Which Mythic do you want a copy of?", 26, Color.white, "ReplicatorAsk", TextAnchor.MiddleCenter);
            TileGrid(box, reps, null, p => OpenMergePicker(p.Uid, true), "Replicate:", 600);
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
                Note(b, "Once per part. Level, ladder step, runes and Forge are kept; level-1 stats ×1.4 and all five skills become their awakened versions.");
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
            }, new ModalButton("Close", () => Reopen(partUid)));
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
    }
}
