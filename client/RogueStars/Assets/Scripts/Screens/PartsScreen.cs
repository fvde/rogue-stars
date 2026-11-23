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
    /// Parts inventory (14-step ladder Common … Mythic SS+2) with slot filters and quick merge; part detail (stats, five
    /// skills locked/unlocked by rarity, level/cap, equip, lock, runes, Forge, dismantle); the merge picker (copies,
    /// fodder points in LE, Ore, best-fodder auto-select, confirmation before protected parts are used); the Replicator
    /// (Legendary copy of a Mythic+ part's def); awakening (Mythic+, once, Awaken Cores) with a before/after preview; and
    /// the rune inventory (lock, merge 3→1). Material logic: <see cref="PartPlanner"/>.
    /// </summary>
    public sealed class PartsScreen : UiScreen
    {
        private static readonly string[] Filters = { "All", "Head", "Body", "Tail", "Legs", "Arm", "Runes" };
        private int _filter;

        // merge picker state
        private string _pickTarget;
        private bool _pickReplicate;
        private readonly List<string> _picked = new List<string>();

        public override string Title => "Parts";

        public static Color RarityColor(int partRarity) => Theme.PartRarityColor(partRarity);

        public static string RarityName(ContentDb c, int r) => c.PartsConfig.RarityDef(r).Name;

        public static string PartName(ContentDb c, PartItem p) => Fmt.PartName(c, p);

        private HashSet<string> Locks => PartLocks.For(State.PlayerId);

        protected override void Build(RectTransform body)
        {
            Ui.TabBar(body, Filters, _filter, i => { _filter = i; Rebuild(); }, 76);
            if (_filter == 6) { Runes(body); return; }
            var top = Ui.Row(body, 80);
            Ui.Label(top, State.Parts.Items.Count + " parts · Ore " + Fmt.Num(State.Wallet.Get(Currencies.Ore)) + " · Cores " + Fmt.Num(State.Wallet.Get(Currencies.GearCores))
                          + "\n<size=22>Awaken Cores " + State.Wallet.Get(ProgressionCurrencies.PartAwakenCores) + "/" + Content.PartsConfig.AwakenCost + "</size>", Theme.FontSmall, Theme.TextDim);
            Ui.Button(top, "Quick merge\n<size=20>3→1 up to " + RarityName(Content, (int)Content.PartsConfig.QuickMergeMaxTarget) + "</size>", QuickMerge, Theme.Accent, 300, 76, true, 26);
            var grid = Ui.Grid(body, new Vector2(330, 170), 3, 12);
            int n = 0;
            foreach (var p in SortedParts())
            {
                var def = PartsSystem.Def(Content, p);
                if (_filter > 0 && (int)def.Kind != _filter - 1) continue;
                n++;
                var part = p;
                bool eq = PartsSystem.IsEquipped(State, p.Uid);
                var flags = new List<string>();
                if (eq) flags.Add("equipped");
                if (Locks.Contains(p.Uid)) flags.Add("locked");
                if (p.Awaken > 0) flags.Add("awakened");
                string label = PartName(Content, p) + "\n<size=22>" + RarityName(Content, p.Rarity) + " · Lv " + p.Level + "</size>"
                               + (flags.Count > 0 ? "\n<size=20>" + string.Join(" · ", flags) + "</size>" : "");
                Ui.Button(grid, label, () => Detail(part.Uid), Color.Lerp(RarityColor(p.Rarity), Theme.Panel, eq ? 0.35f : 0.6f), -1, 170, true, 26).name = "Part:" + p.Uid;
            }
            if (n == 0) Note(body, "No parts here yet. Parts drop from runs and Part Signals.");
        }

        /// <summary>Equipped first, then rarity and level (highest first), then uid.</summary>
        private List<PartItem> SortedParts()
        {
            var list = new List<PartItem>(State.Parts.Items.Values);
            list.Sort((a, b) =>
            {
                int ea = PartsSystem.IsEquipped(State, a.Uid) ? 0 : 1, eb = PartsSystem.IsEquipped(State, b.Uid) ? 0 : 1;
                if (ea != eb) return ea.CompareTo(eb);
                if (a.Rarity != b.Rarity) return b.Rarity.CompareTo(a.Rarity);
                if (a.Level != b.Level) return b.Level.CompareTo(a.Level);
                return string.CompareOrdinal(a.Uid, b.Uid);
            });
            return list;
        }

        /// <summary>Quick merge (rules: 3→1 below Legendary, never equipped parts); asks first if locked parts could be used.</summary>
        private void QuickMerge()
        {
            int locked = 0;
            foreach (var p in PartPlanner.Candidates(State, null))
                if (Locks.Contains(p.Uid) && p.Rarity < (int)Content.PartsConfig.QuickMergeMaxTarget) locked++;
            if (locked == 0) { Run(new PartQuickMerge()); return; }
            Modal("Quick merge", b => Note(b, "Quick merge may use " + locked + " locked part(s) below " + RarityName(Content, (int)Content.PartsConfig.QuickMergeMaxTarget) + " as fodder."),
                new ModalButton("Cancel", null), new ModalButton("Merge anyway", () => Run(new PartQuickMerge()), Theme.Warn));
        }

        // ---- part detail -------------------------------------------------------------------------

        public void Detail(string uid)
        {
            var p = PartsSystem.Get(State, uid);
            if (p == null) return;
            var def = PartsSystem.Def(Content, p);
            var cfg = Content.PartsConfig;
            bool equipped = PartsSystem.IsEquipped(State, uid);
            bool locked = Locks.Contains(uid);
            Modal(PartName(Content, p), b =>
            {
                Ui.Label(b, RarityName(Content, p.Rarity) + " " + def.Kind + (def.Kind == PartKind.Arm ? " · " + def.Style : "") + " · " + def.Family
                            + (p.Awaken > 0 ? " · awakened (" + def.Name + ")" : ""), Theme.FontSmall, RarityColor(p.Rarity));
                Note(b, StatsText(PartsSystem.PartStats(State, Content, p)));
                SkillList(b, PartPlanner.SkillLines(Content, p, p.Awaken > 0), "Skills (" + PartsSystem.SkillCount(Content, p.Rarity) + "/5)");

                // level / cap
                int cap = PartsSystem.LevelCap(Content, p);
                var lvRow = Ui.Row(b, 76, 8);
                bool canLevel = p.Level < cap && p.Level < cfg.LevelOreCosts.Count;
                string lvCost = canLevel ? Fmt.Num(cfg.LevelOreCosts[p.Level]) + " ore + " + Fmt.Num(cfg.LevelGearCoreCosts[p.Level]) + " cores" : "cap " + cap;
                Ui.Button(lvRow, "Lv " + p.Level + "/" + cap + " ▲\n<size=20>" + lvCost + "</size>", () => Run(new PartLevelUp { Uid = uid }, _ => Reopen(uid)), Theme.Good, -1, 76, canLevel, 24);
                bool canCap = p.CapRaises < cfg.RarityDef(p.Rarity).MaxCapRaises && p.CapRaises < cfg.CapRaisePlates.Count;
                Ui.Button(lvRow, "Raise cap\n<size=20>" + (canCap ? cfg.CapRaisePlates[p.CapRaises] + " plates" : "max") + "</size>", () => Run(new PartRaiseCap { Uid = uid }, _ => Reopen(uid)), Theme.Button, -1, 76, canCap, 24);
                Ui.Button(lvRow, "Reset Lv", () => Run(new PartLevelDown { Uid = uid }, _ => Reopen(uid)), Theme.Button, 170, 76, p.Level > 1, 24);

                // equip / lock
                var eqRow = Ui.Row(b, 70, 8);
                if (def.Kind == PartKind.Arm)
                {
                    Ui.Button(eqRow, "Equip left", () => Run(new PartEquip { Uid = uid, Slot = PartSlot.LeftArm }, _ => Reopen(uid)), Theme.Accent, -1, 70, true, 26);
                    Ui.Button(eqRow, "Equip right", () => Run(new PartEquip { Uid = uid, Slot = PartSlot.RightArm }, _ => Reopen(uid)), Theme.Accent, -1, 70, true, 26);
                }
                else Ui.Button(eqRow, equipped ? "Equipped" : "Equip", () => Run(new PartEquip { Uid = uid, Slot = (PartSlot)(int)def.Kind }, _ => Reopen(uid)), Theme.Accent, -1, 70, !equipped, 26);
                Ui.Button(eqRow, locked ? "Unlock" : "Lock", () => { PartLocks.Set(State.PlayerId, uid, !locked); Reopen(uid); Rebuild(); }, locked ? Theme.Warn : Theme.Button, 200, 70, true, 26).name = "PartLock";

                // merge / replicate
                var plan = PartPlanner.AutoMerge(State, Content, p, Locks);
                var mRow = Ui.Row(b, 80, 8);
                string mergeText = plan.NextRarity < 0 ? "Max rarity" : "Merge → " + RarityName(Content, plan.NextRarity) + "\n<size=20>" + (plan.Valid ? "ready" + (plan.NeedsConfirmation ? " (uses protected parts)" : "") : plan.Error) + "</size>";
                Ui.Button(mRow, mergeText, () => OpenMergePicker(uid, false), plan.Valid ? Theme.Warn : Theme.Button, -1, 80, plan.NextRarity >= 0, 24).name = "OpenMerge";
                if (p.Rarity >= (int)cfg.ReplicateMinTargetRarity)
                    Ui.Button(mRow, "Replicator\n<size=20>Legendary copy</size>", () => OpenMergePicker(uid, true), Theme.Premium, 260, 80, true, 24).name = "OpenReplicator";

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
                string awText = p.Awaken > 0 ? "Awakened" : p.Rarity < (int)cfg.AwakenMinRarity ? "Awaken\n<size=20>" + RarityName(Content, (int)cfg.AwakenMinRarity) + "+</size>"
                    : "Awaken\n<size=20>" + State.Wallet.Get(ProgressionCurrencies.PartAwakenCores) + "/" + cfg.AwakenCost + " cores</size>";
                Ui.Button(row, awText, () => AwakenModal(uid), Theme.Warn, -1, 76, p.Awaken == 0 && p.Rarity >= (int)cfg.AwakenMinRarity, 24).name = "OpenAwaken";
                Ui.Button(row, "Dismantle\n<size=20>+" + Fmt.Num(cfg.RarityDef(p.Rarity).DismantleOre) + " ore</size>", () => Dismantle(uid), Theme.Bad, -1, 76, !equipped && p.Awaken == 0, 24);
            }, new ModalButton("Close", null));
        }

        private static string StatsText(StatBlock stats)
        {
            var parts = new List<string>();
            foreach (var kv in stats.Flat) parts.Add(kv.Key + " " + WormScreen.StatText(kv.Key, kv.Value));
            foreach (var kv in stats.Percent) parts.Add(kv.Key + " +" + Fmt.Pct(kv.Value));
            return string.Join(" · ", parts);
        }

        /// <summary>The part's five skills: unlocked ones in the family colour with their effect, locked ones dimmed with the unlock rarity.</summary>
        private void SkillList(Transform parent, List<PartSkillLine> skills, string title)
        {
            Ui.Label(parent, title, Theme.FontSmall, Theme.Text);
            foreach (var sk in skills)
            {
                string head = (sk.Unlocked ? "● " : "○ ") + sk.Name + "  <size=18>" + RarityName(Content, (int)sk.UnlockRarity) + (sk.Unlocked ? "" : " · locked") + "</size>";
                var t = Ui.Label(parent, head + "\n<size=19>" + sk.Description + "</size>", 22, sk.Unlocked ? Theme.Text : Theme.TextDim);
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
        private void Reopen(string uid)
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
                    StatRow(stats, k.ToString(), WormScreen.StatText(k, before), WormScreen.StatText(k, after), after > before);
                }
                var pkeys = new List<StatId>();
                foreach (var k in pv.StatsAfter.Percent.Keys) if (!pkeys.Contains(k)) pkeys.Add(k);
                foreach (var k in pv.StatsBefore.Percent.Keys) if (!pkeys.Contains(k)) pkeys.Add(k);
                foreach (var k in pkeys)
                {
                    pv.StatsBefore.Percent.TryGetValue(k, out double before);
                    pv.StatsAfter.Percent.TryGetValue(k, out double after);
                    StatRow(stats, k + " %", "+" + Fmt.Pct(before), "+" + Fmt.Pct(after), after > before);
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

        private static void StatRow(Transform parent, string stat, string before, string after, bool up)
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

        /// <summary>Auto-selected merge materials for the next rarity (null + reason when not possible). Used by tests and the detail.</summary>
        public List<string> FindMergeMaterials(PartItem target, out string why)
        {
            var plan = PartPlanner.AutoMerge(State, Content, target, Locks);
            why = plan.Error;
            return plan.Valid ? plan.Materials : null;
        }
    }
}
