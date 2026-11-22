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
    /// <summary>
    /// Parts inventory with slot filters, part detail (stats, level/cap, equip, merge ladder, runes, Forge, S/SS
    /// awakening, dismantle), quick-merge, and the rune inventory (lock, merge 3→1).
    /// </summary>
    public sealed class PartsScreen : UiScreen
    {
        private static readonly string[] Filters = { "All", "Head", "Body", "Tail", "Legs", "Arm", "Runes" };
        private int _filter;

        public override string Title => "Parts";

        public static Color RarityColor(int partRarity)
        {
            var r = partRarity >= (int)PartRarity.Mythic ? Rarity.Mythic
                : partRarity >= (int)PartRarity.Legendary ? Rarity.Legendary
                : partRarity >= (int)PartRarity.Epic ? Rarity.Epic
                : partRarity >= (int)PartRarity.Rare ? Rarity.Rare : Rarity.Common;
            return Theme.RarityColor(r);
        }

        public static string RarityName(ContentDb c, int r) => c.PartsConfig.RarityDef(r).Name;

        public static string PartName(ContentDb c, PartItem p)
        {
            var def = c.PartsConfig.Defs.TryGetValue(p.DefId, out var d) ? d : null;
            return (def?.Name ?? p.DefId) + (p.Awaken == 1 ? " [S]" : p.Awaken >= 2 ? " [SS]" : "");
        }

        protected override void Build(RectTransform body)
        {
            Ui.TabBar(body, Filters, _filter, i => { _filter = i; Rebuild(); }, 76);
            if (_filter == 6) { Runes(body); return; }
            var top = Ui.Row(body, 80);
            Ui.Label(top, State.Parts.Items.Count + " parts · Ore " + Fmt.Num(State.Wallet.Get(Currencies.Ore)) + " · Cores " + Fmt.Num(State.Wallet.Get(Currencies.GearCores)), Theme.FontSmall, Theme.TextDim);
            Ui.Button(top, "Quick merge", () => Run(new PartQuickMerge()), Theme.Accent, 260, 76);
            var grid = Ui.Grid(body, new Vector2(330, 170), 3, 12);
            int n = 0;
            foreach (var uid in PartsSystem.SortedPartUids(State))
            {
                var p = PartsSystem.Get(State, uid);
                var def = PartsSystem.Def(Content, p);
                if (_filter > 0 && (int)def.Kind != _filter - 1) continue;
                n++;
                var part = p;
                bool eq = PartsSystem.IsEquipped(State, uid);
                string label = PartName(Content, p) + "\n<size=22>" + RarityName(Content, p.Rarity) + " · Lv " + p.Level + (eq ? " · equipped" : "") + "</size>";
                Ui.Button(grid, label, () => Detail(part.Uid), Color.Lerp(RarityColor(p.Rarity), Theme.Panel, eq ? 0.35f : 0.6f), -1, 170, true, 26).name = "Part:" + uid;
            }
            if (n == 0) Note(body, "No parts here yet. Parts drop from runs and Part Signals.");
        }

        // ---- part detail -------------------------------------------------------------------------

        private void Detail(string uid)
        {
            var p = PartsSystem.Get(State, uid);
            if (p == null) return;
            var def = PartsSystem.Def(Content, p);
            var cfg = Content.PartsConfig;
            Modal(PartName(Content, p), b =>
            {
                Ui.Label(b, RarityName(Content, p.Rarity) + " " + def.Kind + (def.Kind == PartKind.Arm ? " · " + def.Style : "") + " · " + def.Family, Theme.FontSmall, RarityColor(p.Rarity));
                var stats = PartsSystem.PartStats(State, Content, p);
                var parts = new List<string>();
                foreach (var kv in stats.Flat) parts.Add(kv.Key + " " + WormScreen.StatText(kv.Key, kv.Value));
                foreach (var kv in stats.Percent) parts.Add(kv.Key + " +" + Fmt.Pct(kv.Value));
                Note(b, string.Join(" · ", parts));
                foreach (var sid in PartsSystem.PartSkills(Content, p)) Ui.Label(b, "Skill: " + (Content.Skill(sid)?.Name ?? sid), Theme.FontSmall, Theme.FamilyColor(def.Family));

                // level / cap
                int cap = PartsSystem.LevelCap(Content, p);
                var lvRow = Ui.Row(b, 76, 8);
                bool canLevel = p.Level < cap && p.Level < cfg.LevelOreCosts.Count;
                string lvCost = canLevel ? Fmt.Num(cfg.LevelOreCosts[p.Level]) + " ore + " + Fmt.Num(cfg.LevelGearCoreCosts[p.Level]) + " cores" : "cap " + cap;
                Ui.Button(lvRow, "Lv " + p.Level + "/" + cap + " ▲\n<size=20>" + lvCost + "</size>", () => Run(new PartLevelUp { Uid = uid }, _ => Reopen(uid)), Theme.Good, -1, 76, canLevel, 24);
                bool canCap = p.CapRaises < cfg.RarityDef(p.Rarity).MaxCapRaises && p.CapRaises < cfg.CapRaisePlates.Count;
                Ui.Button(lvRow, "Raise cap\n<size=20>" + (canCap ? cfg.CapRaisePlates[p.CapRaises] + " plates" : "max") + "</size>", () => Run(new PartRaiseCap { Uid = uid }, _ => Reopen(uid)), Theme.Button, -1, 76, canCap, 24);
                Ui.Button(lvRow, "Reset Lv", () => Run(new PartLevelDown { Uid = uid }, _ => Reopen(uid)), Theme.Button, 170, 76, p.Level > 1, 24);

                // equip
                var eqRow = Ui.Row(b, 70, 8);
                if (def.Kind == PartKind.Arm)
                {
                    Ui.Button(eqRow, "Equip left", () => Run(new PartEquip { Uid = uid, Slot = PartSlot.LeftArm }), Theme.Accent, -1, 70);
                    Ui.Button(eqRow, "Equip right", () => Run(new PartEquip { Uid = uid, Slot = PartSlot.RightArm }), Theme.Accent, -1, 70);
                }
                else Ui.Button(eqRow, "Equip", () => Run(new PartEquip { Uid = uid, Slot = (PartSlot)(int)def.Kind }), Theme.Accent, -1, 70, !PartsSystem.IsEquipped(State, uid));

                // merge ladder
                var mats = FindMergeMaterials(p, out string why);
                string next = p.Rarity + 1 < cfg.Rarities.Count ? RarityName(Content, p.Rarity + 1) : "max";
                Ui.Button(b, "Merge → " + next + (mats != null ? " (uses " + mats.Count + ")" : "\n<size=20>" + why + "</size>"),
                    () => { var cmd = new PartMerge { TargetUid = uid }; cmd.MaterialUids.AddRange(mats); Run(cmd, _ => Reopen(uid)); }, Theme.Warn, -1, 76, mats != null, 26);

                // runes
                int slots = PartsSystem.RuneSlotCount(Content, p);
                if (slots > 0)
                {
                    var runeRow = Ui.Row(b, 70, 8);
                    for (int i = 0; i < slots; i++)
                    {
                        int slot = i;
                        string runeUid = i < p.Runes.Count ? p.Runes[i] : "";
                        var rune = !string.IsNullOrEmpty(runeUid) && State.Parts.Runes.TryGetValue(runeUid, out var r) ? r : null;
                        Ui.Button(runeRow, rune == null ? "+ rune" : RuneShort(rune), () =>
                        {
                            if (rune != null) Run(new RuneUnsocket { PartUid = uid, SlotIndex = slot }, _ => Reopen(uid));
                            else SocketModal(uid, slot);
                        }, rune == null ? Theme.Locked : Theme.RarityColor((Rarity)rune.Rarity), -1, 70, Session.IsUnlocked(Features.Runes), 22);
                    }
                }

                // forge / awaken / dismantle
                var row = Ui.Row(b, 76, 8);
                bool forge = Session.IsUnlocked(Features.Forge) && p.Rarity >= (int)cfg.ForgeMinRarity && p.ForgedSlots < cfg.Forge.Count;
                string forgeCost = p.ForgedSlots < cfg.Forge.Count ? cfg.Forge[p.ForgedSlots].Diamonds + " dia" : "done";
                Ui.Button(row, "Forge\n<size=20>" + forgeCost + "</size>", () => Run(new PartForgeUnlock { Uid = uid }, _ => Reopen(uid)), Theme.Premium, -1, 76, forge, 24);
                var minAw = p.Awaken == 0 ? cfg.AwakenMinRarity : cfg.SSMinRarity;
                bool awaken = p.Awaken < 2 && p.Awaken < cfg.AwakenCores.Count && p.Rarity >= (int)minAw;
                Ui.Button(row, (p.Awaken == 0 ? "Awaken S" : "Awaken SS") + "\n<size=20>" + (p.Awaken < cfg.AwakenCores.Count ? cfg.AwakenCores[p.Awaken] + " cores" : "max") + "</size>",
                    () => Run(new PartAwaken { Uid = uid }, _ => Reopen(uid)), Theme.Warn, -1, 76, awaken, 24);
                Ui.Button(row, "Dismantle\n<size=20>+" + cfg.RarityDef(p.Rarity).DismantleOre + " ore</size>", () => { var cmd = new PartDismantle(); cmd.Uids.Add(uid); Run(cmd); }, Theme.Bad, -1, 76,
                    !PartsSystem.IsEquipped(State, uid) && p.Awaken == 0, 24);
            }, new ModalButton("Close", null));
        }

        /// <summary>Close the open detail and reopen it on the updated state.</summary>
        private void Reopen(string uid)
        {
            Ui.Clear(App.ModalLayer);
            if (PartsSystem.Get(State, uid) != null) Detail(uid);
        }

        /// <summary>Pick merge materials for the next rarity (same-def copies first, then same-kind fodder, lowest level first).</summary>
        public List<string> FindMergeMaterials(PartItem target, out string why)
        {
            why = null;
            var cfg = Content.PartsConfig;
            if (target.Rarity + 1 >= cfg.Rarities.Count) { why = "max rarity"; return null; }
            var recipe = cfg.Rarities[target.Rarity + 1].Merge;
            if (recipe == null) { why = "no recipe"; return null; }
            var def = PartsSystem.Def(Content, target);
            var pool = new List<PartItem>();
            foreach (var p in State.Parts.Items.Values)
                if (p.Uid != target.Uid && !PartsSystem.IsEquipped(State, p.Uid) && p.Awaken == 0 && p.ForgedSlots == 0) pool.Add(p);
            pool.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : string.CompareOrdinal(a.Uid, b.Uid));
            var picked = new List<string>();
            int same = 0, fodder = 0;
            foreach (var p in pool)
                if (same < recipe.SameDefCount && p.DefId == target.DefId && p.Rarity == (int)recipe.SameDefRarity) { picked.Add(p.Uid); same++; }
            foreach (var p in pool)
                if (fodder < recipe.FodderCount && !picked.Contains(p.Uid) && PartsSystem.Def(Content, p).Kind == def.Kind && p.Rarity == (int)recipe.FodderRarity) { picked.Add(p.Uid); fodder++; }
            if (same < recipe.SameDefCount || fodder < recipe.FodderCount)
            {
                why = "needs " + (recipe.SameDefCount > 0 ? recipe.SameDefCount + "× same " + RarityName(Content, (int)recipe.SameDefRarity) : "")
                      + (recipe.FodderCount > 0 ? (recipe.SameDefCount > 0 ? " + " : "") + recipe.FodderCount + "× " + RarityName(Content, (int)recipe.FodderRarity) + " " + def.Kind : "");
                return null;
            }
            try { PartsSystem.ValidateMerge(State, Content, target.Uid, picked); }
            catch (RuleException ex) { why = ex.Message; return null; }
            return picked;
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

        private List<RuneItem> SortedRunes()
        {
            var list = new List<RuneItem>(State.Parts.Runes.Values);
            list.Sort((a, b) => a.Rarity != b.Rarity ? b.Rarity.CompareTo(a.Rarity) : string.CompareOrdinal(a.DefId + a.Uid, b.DefId + b.Uid));
            return list;
        }

        private string RuneShort(RuneItem r)
        {
            var def = Content.PartsConfig.Runes.TryGetValue(r.DefId, out var d) ? d : null;
            var stat = PartsSystem.RuneStat(Content, r);
            string v = stat == null ? "" : stat.Percent != 0 ? "+" + Fmt.Pct(stat.Percent) : "+" + stat.Flat.ToString("0");
            return (def?.Name ?? r.DefId) + " " + v;
        }

        private void Runes(RectTransform body)
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
