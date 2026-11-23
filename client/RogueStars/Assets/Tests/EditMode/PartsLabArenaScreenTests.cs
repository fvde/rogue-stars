using System.Collections.Generic;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>Parts merge picker / Replicator / awakening preview, laboratory tree, arena shop and the new currency surfaces.</summary>
    public sealed class PartsLabArenaScreenTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp]
        public void SetUp()
        {
            PartLocks.Persist = false;
            PartLocks.ClearAll();
            _f = new AppFixture();
        }

        [TearDown]
        public void TearDown()
        {
            _f.Dispose();
            PartLocks.ClearAll();
            PartLocks.Persist = true;
        }

        private List<string> DefIds()
        {
            var ids = new List<string>(C.PartsConfig.Defs.Keys);
            ids.Sort(string.CompareOrdinal);
            return ids;
        }

        /// <summary>Grant parts and return the uids of the new ones.</summary>
        private List<string> Grant(string defId, PartRarity rarity, int count)
        {
            var before = new HashSet<string>(S.Parts.Items.Keys);
            _f.Service.DevGrant(new[] { new Reward(RewardKinds.Part, defId, count, (int)rarity) });
            var list = new List<string>();
            foreach (var uid in PartsSystem.SortedPartUids(S)) if (!before.Contains(uid)) list.Add(uid);
            return list;
        }

        private void GrantCurrency(string id, long amount) => _f.Service.DevGrant(new[] { Reward.Cur(id, amount) });

        private PartsScreen OpenParts()
        {
            _f.App.Open("parts");
            return (PartsScreen)_f.App.Router.Current;
        }

        private Button FindButton(Transform root, string name)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(true)) if (b.name == name) return b;
            return null;
        }

        // ---- merge picker ----------------------------------------------------------------------------

        [Test]
        public void PickFodder_CoversTheNeed_WithoutSuperfluousParts_AndPrefersTheCheapestSet()
        {
            var ids = DefIds();
            var pool = new List<PartItem>();
            foreach (var r in new[] { PartRarity.Epic, PartRarity.Epic, PartRarity.Epic, PartRarity.Legendary, PartRarity.Mythic })
                pool.Add(new PartItem { Uid = "x" + pool.Count, DefId = ids[1], Rarity = (int)r });
            long Sum(List<PartItem> l) { long s = 0; foreach (var p in l) s += PartsSystem.FodderValue(C, p); return s; }
            foreach (long need in new long[] { 27, 54, 81, 100, 162, 300, 324, 400 })
            {
                var pick = PartPlanner.PickFodder(C, pool, need);
                Assert.That(pick, Is.Not.Null, "need " + need);
                long sum = Sum(pick), min = long.MaxValue;
                foreach (var p in pick) min = System.Math.Min(min, PartsSystem.FodderValue(C, p));
                Assert.That(sum, Is.GreaterThanOrEqualTo(need), "covers " + need);
                Assert.That(sum - min, Is.LessThan(need), "no superfluous part for " + need);
            }
            Assert.That(Sum(PartPlanner.PickFodder(C, pool, 162)), Is.EqualTo(162), "Epic×3 + Legendary, not the Mythic");
            Assert.That(PartPlanner.PickFodder(C, pool, 10_000), Is.Null);
        }

        [Test]
        public void MergePicker_AutoSelectsCopyAndBestFodder_ForMythic_AndMerges()
        {
            var ids = DefIds();
            var target = Grant(ids[0], PartRarity.Legendary, 1)[0];
            var copy = Grant(ids[0], PartRarity.Legendary, 1)[0];
            var fodder = Grant(ids[1], PartRarity.Legendary, 3);
            var mythic = Grant(ids[2], PartRarity.Mythic, 1)[0];
            Grant(ids[3], PartRarity.Epic, 4); // below the minimum rarity: never used
            GrantCurrency(Currencies.Ore, 100_000);
            var screen = OpenParts();
            screen.OpenMergePicker(target, false);
            var plan = screen.PickerPlan;
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.Valid, Is.True, plan.Error);
            Assert.That(plan.NextRarity, Is.EqualTo((int)PartRarity.Mythic));
            Assert.That(plan.Copies, Is.EqualTo(new[] { copy }));
            Assert.That(plan.Fodder.Count, Is.EqualTo(2), "2 LE of fodder");
            Assert.That(plan.FodderPoints, Is.EqualTo(plan.FodderNeeded));
            Assert.That(plan.Materials, Does.Not.Contain(mythic), "Mythic parts are not auto-used while plain fodder exists");
            Assert.That(plan.NeedsConfirmation, Is.False);
            Assert.That(plan.Ore, Is.EqualTo(C.PartsConfig.Rarities[(int)PartRarity.Mythic].Merge.Ore));
            Assert.That(PartPlanner.Le(C, plan.FodderNeeded), Is.EqualTo("2 LE"));
            long ore = S.Wallet.Get(Currencies.Ore);
            screen.ConfirmPick();
            Assert.That(S.Parts.Items[target].Rarity, Is.EqualTo((int)PartRarity.Mythic));
            Assert.That(S.Parts.Items.ContainsKey(copy), Is.False);
            Assert.That(S.Wallet.Get(Currencies.Ore), Is.EqualTo(ore - plan.Ore));
            Assert.That(S.Parts.Items.ContainsKey(mythic), Is.True);
        }

        [Test]
        public void MergePicker_NeverUsesEquippedParts_AndAsksBeforeLockedOrMythicParts()
        {
            var ids = DefIds();
            var target = Grant(ids[0], PartRarity.Epic, 1)[0];
            var locked = Grant(ids[1], PartRarity.Epic, 2);
            PartLocks.Set(S.PlayerId, locked[0], true);
            PartLocks.Set(S.PlayerId, locked[1], true);
            // an equipped Epic of the right kind: rules refuse it, the planner never offers it
            string equipped = null;
            foreach (var id in ids)
            {
                if (C.PartsConfig.Defs[id].Kind == PartKind.Arm) continue;
                equipped = Grant(id, PartRarity.Epic, 1)[0];
                Assert.That(_f.Service.ExecuteNow(new PartEquip { Uid = equipped, Slot = (PartSlot)(int)C.PartsConfig.Defs[id].Kind }).Ok, Is.True);
                break;
            }
            var screen = OpenParts();
            screen.OpenMergePicker(target, false);
            var plan = screen.PickerPlan;
            Assert.That(plan.Valid, Is.True, plan.Error);
            Assert.That(plan.Materials, Does.Not.Contain(equipped));
            Assert.That(plan.NeedsConfirmation, Is.True, "only locked parts can cover the fodder");
            Assert.That(plan.Protected, Is.EquivalentTo(locked));
            int parts = S.Parts.Items.Count;
            screen.ConfirmPick();
            Assert.That(S.Parts.Items.Count, Is.EqualTo(parts), "nothing merged before the confirmation");
            Assert.That(_f.App.ModalLayer.GetChild(_f.App.ModalLayer.childCount - 1).name, Is.EqualTo("Modal:ConfirmProtected"));

            // with free fodder available the locked parts are left alone
            var free = Grant(ids[2], PartRarity.Epic, 2);
            screen.AutoPick();
            Assert.That(screen.PickerPlan.NeedsConfirmation, Is.False);
            Assert.That(screen.PickerPlan.Fodder, Is.EquivalentTo(free));
        }

        [Test]
        public void MergePicker_ManualSelection_IsCheckedByTheRules()
        {
            var ids = DefIds();
            var target = Grant(ids[0], PartRarity.Common, 1)[0];
            var commons = Grant(ids[1], PartRarity.Common, 3);
            var screen = OpenParts();
            screen.OpenMergePicker(target, false);
            Assert.That(screen.PickerPlan.Valid, Is.True, screen.PickerPlan.Error);
            Assert.That(screen.PickerPlan.Fodder.Count, Is.EqualTo(2));
            foreach (var uid in commons) if (!screen.PickerPlan.Fodder.Contains(uid)) screen.TogglePick(uid);
            Assert.That(screen.PickerPlan.Valid, Is.False, "three Commons for 2 points: superfluous");
            Assert.That(screen.PickerPlan.Error, Does.Contain("superfluous"));
            screen.ClearPick();
            Assert.That(screen.PickerPlan.Valid, Is.False);
            Assert.That(screen.PickerPlan.FodderPoints, Is.EqualTo(0));
        }

        [Test]
        public void Replicator_TurnsTenLegendaryEquivalentsIntoALegendaryCopy()
        {
            var ids = DefIds();
            var target = Grant(ids[0], PartRarity.Mythic, 1)[0];
            Grant(ids[0], PartRarity.Legendary, 1); // own def: never replicator fodder
            Grant(ids[1], PartRarity.Legendary, 6);
            Grant(ids[2], PartRarity.Legendary, 5);
            GrantCurrency(Currencies.Ore, C.PartsConfig.ReplicateOre);
            long ore = S.Wallet.Get(Currencies.Ore);
            var screen = OpenParts();
            screen.OpenMergePicker(target, true);
            var plan = screen.PickerPlan;
            Assert.That(plan.Replicate, Is.True);
            Assert.That(plan.Valid, Is.True, plan.Error);
            Assert.That(plan.FodderPoints, Is.EqualTo(C.PartsConfig.ReplicateFodderPoints));
            foreach (var uid in plan.Materials) Assert.That(S.Parts.Items[uid].DefId, Is.Not.EqualTo(ids[0]));
            int copies = 0;
            foreach (var p in S.Parts.Items.Values) if (p.DefId == ids[0] && p.Rarity == (int)PartRarity.Legendary) copies++;
            screen.ConfirmPick();
            int after = 0;
            foreach (var p in S.Parts.Items.Values) if (p.DefId == ids[0] && p.Rarity == (int)PartRarity.Legendary) after++;
            Assert.That(after, Is.EqualTo(copies + 1));
            Assert.That(S.Wallet.Get(Currencies.Ore), Is.EqualTo(ore - C.PartsConfig.ReplicateOre));
        }

        // ---- skills / awakening ------------------------------------------------------------------------

        [Test]
        public void PartSkills_FiveSkills_UnlockByRarity()
        {
            var ids = DefIds();
            foreach (var (rarity, unlocked) in new[] { (PartRarity.Common, 0), (PartRarity.Epic, 1), (PartRarity.Legendary, 2), (PartRarity.Mythic2, 3), (PartRarity.MythicS, 4), (PartRarity.MythicSS2, 5) })
            {
                var p = new PartItem { Uid = "t", DefId = ids[0], Rarity = (int)rarity };
                var lines = PartPlanner.SkillLines(C, p, false);
                Assert.That(lines.Count, Is.EqualTo(5));
                Assert.That(lines.FindAll(l => l.Unlocked).Count, Is.EqualTo(unlocked), rarity.ToString());
                Assert.That(lines.FindAll(l => l.Unlocked).ConvertAll(l => l.SkillId), Is.EqualTo(PartsSystem.PartSkills(C, p)));
            }
            var target = Grant(ids[0], PartRarity.MythicS, 1)[0];
            var screen = OpenParts();
            screen.Detail(target);
            int on = 0, off = 0;
            foreach (var t in _f.App.ModalLayer.GetComponentsInChildren<Text>(true)) { if (t.name == "Skill:on") on++; if (t.name == "Skill:off") off++; }
            Assert.That(on, Is.EqualTo(4));
            Assert.That(off, Is.EqualTo(1));
        }

        [Test]
        public void AwakenPreview_ShowsRedesignedNameStatsAndSkills_AndAwakensOnceForCores()
        {
            var ids = DefIds();
            var uid = Grant(ids[0], PartRarity.Mythic, 1)[0];
            var def = C.PartsConfig.Defs[ids[0]];
            var p = S.Parts.Items[uid];
            var pv = PartPlanner.Awaken(S, C, p);
            Assert.That(pv.NameBefore, Is.EqualTo(def.Name));
            Assert.That(pv.NameAfter, Is.EqualTo(def.AwakenedName));
            Assert.That(pv.NameAfter, Is.Not.EqualTo(pv.NameBefore));
            bool stronger = false;
            foreach (var kv in pv.StatsAfter.Flat) if (kv.Value > (pv.StatsBefore.Flat.TryGetValue(kv.Key, out var b) ? b : 0)) stronger = true;
            Assert.That(stronger, Is.True, "awakened base stats are higher");
            Assert.That(pv.SkillsBefore.ConvertAll(s => s.SkillId), Is.EqualTo(def.SkillIds));
            Assert.That(pv.SkillsAfter.ConvertAll(s => s.SkillId), Is.EqualTo(def.AwakenedSkillIds));
            Assert.That(pv.SkillsAfter.FindAll(s => s.Unlocked).Count, Is.EqualTo(3), "an awakened Mythic has three skills");
            Assert.That(pv.BlockReason, Does.Contain("Awaken Cores"));
            Assert.That(PartPlanner.Awaken(S, C, new PartItem { Uid = "e", DefId = ids[0], Rarity = (int)PartRarity.Legendary }).BlockReason, Does.Contain("Mythic"));

            GrantCurrency(ProgressionCurrencies.PartAwakenCores, C.PartsConfig.AwakenCost);
            var screen = OpenParts();
            screen.AwakenModal(uid);
            Text names = null;
            foreach (var t in _f.App.ModalLayer.GetComponentsInChildren<Text>(true)) if (t.name == "AwakenNames") names = t;
            Assert.That(names, Is.Not.Null);
            Assert.That(names.text, Does.Contain(def.Name).And.Contain(def.AwakenedName));
            Button awaken = null;
            foreach (var b in _f.App.ModalLayer.GetComponentsInChildren<Button>(true)) if (b.name.StartsWith("Button:Awaken (")) awaken = b;
            Assert.That(awaken, Is.Not.Null);
            awaken.onClick.Invoke();
            Assert.That(S.Parts.Items[uid].Awaken, Is.EqualTo(1));
            Assert.That(Fmt.PartName(C, S.Parts.Items[uid]), Is.EqualTo(def.AwakenedName));
            Assert.That(S.Wallet.Get(ProgressionCurrencies.PartAwakenCores), Is.EqualTo(0));
            Assert.That(PartPlanner.Awaken(S, C, S.Parts.Items[uid]).BlockReason, Is.EqualTo("already awakened"));
        }

        // ---- laboratory --------------------------------------------------------------------------------

        [Test]
        public void LabTree_EveryBranchLaysOutWithoutOverlaps()
        {
            var seen = new HashSet<string>();
            foreach (var branch in LabTreeLayout.Branches(C))
            {
                var layout = LabTreeLayout.Build(C, branch, LaboratoryScreen.TreeWidth);
                var rects = new List<KeyValuePair<string, Rect>>(layout.Nodes);
                foreach (var kv in rects)
                {
                    Assert.That(seen.Add(kv.Key), Is.True, kv.Key + " in one branch only");
                    Assert.That(kv.Value.xMin, Is.GreaterThanOrEqualTo(0), kv.Key);
                    Assert.That(kv.Value.xMax, Is.LessThanOrEqualTo(layout.Width + 0.01f), kv.Key);
                    Assert.That(kv.Value.yMax, Is.LessThanOrEqualTo(layout.Height + 0.01f), kv.Key);
                    Assert.That(kv.Value.width, Is.GreaterThan(150), "readable node width");
                }
                for (int i = 0; i < rects.Count; i++)
                    for (int j = i + 1; j < rects.Count; j++)
                        Assert.That(rects[i].Value.Overlaps(rects[j].Value), Is.False, branch + ": " + rects[i].Key + " overlaps " + rects[j].Key);
                foreach (var e in layout.Edges)
                    Assert.That(layout.Nodes[e.Key].yMax, Is.LessThan(layout.Nodes[e.Value].yMin), "edges run downwards: " + e.Key + " → " + e.Value);
            }
            Assert.That(seen.Count, Is.EqualTo(C.LabResearch.Count), "every node is placed");
        }

        [Test]
        public void Laboratory_ResearchFromTheNodeModal_UsesNextCostAndDuration_AndShowsTheSlot()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            GrantCurrency(Currencies.Data, 10_000);
            _f.App.Open("laboratory");
            var screen = (LaboratoryScreen)_f.App.Router.Current;
            Assert.That(screen.Layout, Is.Not.Null);
            Assert.That(screen.Layout.Nodes.Count, Is.GreaterThan(5));
            Assert.That(FindButton(screen.Root, "LabNode:lab_atk_1"), Is.Not.Null, "stat node drawn in the offense tab");
            Text annex = null;
            foreach (var t in screen.Root.GetComponentsInChildren<Text>(true)) if (t.name == "AnnexStatus") annex = t;
            Assert.That(annex.text, Does.Contain("Research Annex"));

            long cost = LaboratorySystem.NextDataCost(S, C);
            long duration = LaboratorySystem.NextDurationMs(S, C);
            long data = S.Wallet.Get(Currencies.Data);
            FindButton(screen.Root, "LabNode:lab_atk_1").onClick.Invoke();
            var research = FindButton(_f.App.ModalLayer, "Research");
            Assert.That(research.interactable, Is.True);
            research.onClick.Invoke();
            var e = LaboratorySystem.Entry(S, "lab_atk_1");
            Assert.That(e.ResearchingLevel, Is.EqualTo(1));
            Assert.That(S.Wallet.Get(Currencies.Data), Is.EqualTo(data - cost));
            Assert.That(TimerSystem.Get(S, e.TimerId).DurationMs, Is.EqualTo(duration));
            Assert.That(LaboratorySystem.NextDataCost(S, C), Is.GreaterThan(cost), "every research makes the next dearer");
            screen.Rebuild();
            bool slotCard = false;
            foreach (var rt in screen.Root.GetComponentsInChildren<RectTransform>(true)) if (rt.name == "ActiveResearch:lab_atk_1") slotCard = true;
            Assert.That(slotCard, Is.True);
            // the only slot is busy: other nodes are blocked
            RogueStars.Client.UiKit.Ui.Clear(_f.App.ModalLayer);
            FindButton(screen.Root, "LabNode:res_eagle_eye").onClick.Invoke();
            var second = FindButton(_f.App.ModalLayer, "Research");
            Assert.That(second == null || !second.interactable, Is.True, "laboratory busy");
            // finish with the daily free speed-up / time and collect
            _f.Clock.Advance(duration + 1000);
            _f.Service.Refresh(_ => { });
            Assert.That(_f.Service.ExecuteNow(new LabCollect { ResearchId = "lab_atk_1" }).Ok, Is.True);
            Assert.That(LaboratorySystem.Entry(S, "lab_atk_1").Level, Is.EqualTo(1));
        }

        [Test]
        public void Laboratory_SkillNodesToggle_StatNodesDoNot()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.App.Open("laboratory");
            var screen = (LaboratoryScreen)_f.App.Router.Current;
            string skillId = null;
            foreach (var d in C.LabResearch.Values) if (d.Kind == LabNodeKind.Skill && d.DefaultResearched && d.Branch == "offense") skillId = d.Id;
            Assert.That(skillId, Is.Not.Null, "a default skill node");
            screen.NodeModal(skillId);
            var toggle = FindButton(_f.App.ModalLayer, "Toggle");
            Assert.That(toggle, Is.Not.Null);
            bool before = LaboratorySystem.Entry(S, skillId).Enabled;
            toggle.onClick.Invoke();
            Assert.That(LaboratorySystem.Entry(S, skillId).Enabled, Is.EqualTo(!before));
            RogueStars.Client.UiKit.Ui.Clear(_f.App.ModalLayer);
            screen.NodeModal("lab_atk_1");
            Assert.That(FindButton(_f.App.ModalLayer, "Toggle"), Is.Null);
            Text prereq = null;
            RogueStars.Client.UiKit.Ui.Clear(_f.App.ModalLayer);
            screen.NodeModal("lab_boss_damage");
            foreach (var t in _f.App.ModalLayer.GetComponentsInChildren<Text>(true)) if (t.name == "Prereqs") prereq = t;
            Assert.That(prereq.text, Does.Contain("ALL"));
            RogueStars.Client.UiKit.Ui.Clear(_f.App.ModalLayer);
            screen.NodeModal("lab_crit_chance");
            foreach (var t in _f.App.ModalLayer.GetComponentsInChildren<Text>(true)) if (t.name == "Prereqs") prereq = t;
            Assert.That(prereq.text, Does.Contain("ANY"));
        }

        [Test]
        public void AnnexStatus_ExplainsTheRequirement()
        {
            Assert.That(LaboratoryScreen.AnnexStatus(S, C, out bool active), Does.Contain("not owned"));
            Assert.That(active, Is.False);
            _f.Service.DevGrant(new[] { new Reward(RewardKinds.Blueprint, "bp_research_annex", 1) });
            Assert.That(LaboratoryScreen.AnnexStatus(S, C, out active), Does.Contain("build it"));
            Assert.That(active, Is.False);
            _f.App.Open("city");
            bool banner = false;
            foreach (var rt in _f.App.Router.Current.Root.GetComponentsInChildren<RectTransform>(true)) if (rt.name == "StaffingBanner") banner = true;
            Assert.That(banner, Is.True, "city shows the Research Annex staffing hint");
        }

        // ---- arena shop / currencies / portals ---------------------------------------------------------

        [Test]
        public void ArenaShop_BuysAwakenCoresWithMedals_UpToTheWeeklyLimit()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            GrantCurrency(Currencies.ArenaMedals, 1000);
            _f.App.Open("arena");
            var screen = (ArenaScreen)_f.App.Router.Current;
            screen.ShowShop();
            var item = C.ShopItem("arena_awaken_core");
            Assert.That(item, Is.Not.Null);
            for (int i = 0; i < item.Limit; i++)
            {
                var buy = FindButton(screen.Root, "Buy:arena_awaken_core");
                Assert.That(buy.interactable, Is.True, "purchase " + (i + 1));
                buy.onClick.Invoke();
                screen.Rebuild();
            }
            Assert.That(S.Wallet.Get(ProgressionCurrencies.PartAwakenCores), Is.EqualTo(item.Limit));
            Assert.That(ArenaScreen.BoughtThisPeriod(S, C, item, _f.Service.NowMs), Is.EqualTo(item.Limit));
            var sold = FindButton(screen.Root, "Buy:arena_awaken_core");
            Assert.That(sold.interactable, Is.False, "weekly limit reached");
            Assert.That(sold.GetComponentInChildren<Text>().text, Is.EqualTo("Sold out"));
        }

        [Test]
        public void ResourceBar_ShowsDataMedalsAndAwakenCores_AndDetails()
        {
            GrantCurrency(Currencies.Data, 1234);
            GrantCurrency(Currencies.ArenaMedals, 56);
            GrantCurrency(ProgressionCurrencies.PartAwakenCores, 7);
            _f.App.Bar.Tick();
            var texts = new Dictionary<string, string>();
            foreach (var t in _f.Canvas.GetComponentsInChildren<Text>(true)) if (t.name.StartsWith("Chip:")) texts[t.name] = t.text;
            Assert.That(texts["Chip:" + Currencies.Data], Is.EqualTo("DATA " + Fmt.Num(S.Wallet.Get(Currencies.Data))));
            Assert.That(texts["Chip:" + Currencies.ArenaMedals], Is.EqualTo("MEDAL " + Fmt.Num(S.Wallet.Get(Currencies.ArenaMedals))));
            Assert.That(texts["Chip:" + ProgressionCurrencies.PartAwakenCores], Is.EqualTo("AWAKEN 7"));
            Assert.That(S.Wallet.Get(Currencies.Data), Is.GreaterThanOrEqualTo(1234));
            _f.App.Bar.ShowDetails();
            bool data = false;
            foreach (var t in _f.App.ModalLayer.GetComponentsInChildren<Text>(true)) if (t.text == "Data") data = true;
            Assert.That(data, Is.True);
            Assert.That(Fmt.CurrencyName(ProgressionCurrencies.PartAwakenCores), Is.EqualTo("Awaken Cores"));
            Assert.That(Fmt.CurrencyName(Currencies.ArenaMedals), Is.EqualTo("Arena Medals"));
        }

        [Test]
        public void Portals_ShowTheBiomassSurge_AndPageTheDarkSide()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.App.Open("portals");
            var screen = _f.App.Router.Current;
            Text surge = null;
            foreach (var t in screen.Root.GetComponentsInChildren<Text>(true)) if (t.name == "BiomassSurge") surge = t;
            Assert.That(surge, Is.Not.Null);
            Assert.That(surge.text, Does.Contain(C.RunTuning.DailyBiomassBonusRuns + "/" + C.RunTuning.DailyBiomassBonusRuns));
            FindButton(screen.Root, "Button:Dark Side").onClick.Invoke();
            int levels = 0;
            foreach (var b in screen.Root.GetComponentsInChildren<Button>(true)) if (b.name.StartsWith("Level:dark_side_")) levels++;
            Assert.That(levels, Is.EqualTo(PortalsScreen.DarkSidePageSize));
            Assert.That(FindButton(screen.Root, "Level:dark_side_1"), Is.Not.Null);
            FindButton(screen.Root, "DarkNext").onClick.Invoke();
            Assert.That(FindButton(screen.Root, "Level:dark_side_26"), Is.Not.Null);
            Assert.That(FindButton(screen.Root, "Level:dark_side_1"), Is.Null);
        }
    }
}
