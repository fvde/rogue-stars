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
    /// <summary>Parts ladder picker / Replicator / awakening preview, laboratory tree, arena shop and the new currency surfaces.</summary>
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

        /// <summary>Research needs a running Laboratory building (City): a built Laboratory in the state, staffed by a Zib.</summary>
        private void LabReady()
        {
            _f.Service.DevGrant(new[] { new Reward(RewardKinds.Blueprint, ContentDb.LaboratoryId, 1), new Reward(RewardKinds.Alien, "alien_zib", 1) });
            var b = new CityBuilding { Uid = ContentDb.LaboratoryId + "#1", BlueprintId = ContentDb.LaboratoryId, Placed = true, Level = 1, X = 11, Y = 8 };
            S.City.Buildings.Add(b);
            AlienSystem.Owned(S, "alien_zib").WorkBuildingId = b.Uid;
            CityLayout.Invalidate(S);
            _f.Service.Refresh(_ => { });
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

        // ---- Mythic ladder picker (parts v2) -----------------------------------------------------------

        private const string Blaster = "myth_firebrand_blaster", Cannon = "myth_shatter_cannon", Whip = "myth_chainlash_whip", Crown = "myth_cinder_crown";

        [Test]
        public void ClimbPicker_AutoSelectsCopiesAndOtherMythicsOfTheSlot_AndClimbs()
        {
            var target = Grant(Blaster, PartRarity.MythicS, 1)[0];
            var copies = Grant(Blaster, PartRarity.Mythic, 2);
            var others = new List<string> { Grant(Cannon, PartRarity.Mythic, 1)[0], Grant(Whip, PartRarity.Mythic, 1)[0] };
            var head = Grant(Crown, PartRarity.Mythic, 1)[0]; // another slot: never an other Mythic of an arm
            GrantCurrency(Currencies.Ore, 100_000);
            GrantCurrency(Currencies.Metal, 1000);
            var screen = OpenParts();
            screen.OpenMergePicker(target, false);
            var plan = screen.PickerPlan;
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.Valid, Is.True, plan.Error);
            Assert.That(plan.NextRarity, Is.EqualTo((int)PartRarity.MythicS1));
            Assert.That(plan.Copies, Is.EquivalentTo(copies));
            Assert.That(plan.Fodder, Is.EquivalentTo(others));
            Assert.That(plan.Materials, Does.Not.Contain(head));
            Assert.That(plan.Metal, Is.EqualTo(300));
            Assert.That(plan.Ore, Is.EqualTo(C.PartsConfig.Rarities[(int)PartRarity.MythicS1].Climb.Ore));
            Assert.That(plan.NeedsConfirmation, Is.False);
            long ore = S.Wallet.Get(Currencies.Ore);
            screen.ConfirmPick();
            Assert.That(S.Parts.Items[target].Rarity, Is.EqualTo((int)PartRarity.MythicS1));
            Assert.That(S.Wallet.Get(Currencies.Ore), Is.EqualTo(ore - plan.Ore));
            Assert.That(S.Wallet.Get(Currencies.Metal), Is.EqualTo(700));
            Assert.That(S.Parts.Items.ContainsKey(head), Is.True);
        }

        [Test]
        public void ClimbPicker_NeverUsesEquippedMythics_AndAsksBeforeLockedOnes()
        {
            var target = Grant(Blaster, PartRarity.MythicS, 1)[0];
            Grant(Blaster, PartRarity.Mythic, 2);
            var locked = new List<string> { Grant(Cannon, PartRarity.Mythic, 1)[0], Grant(Cannon, PartRarity.Mythic, 1)[0] };
            PartLocks.Set(S.PlayerId, locked[0], true);
            PartLocks.Set(S.PlayerId, locked[1], true);
            var equipped = Grant(Whip, PartRarity.Mythic, 1)[0];
            Assert.That(_f.Service.ExecuteNow(new PartEquip { Uid = equipped, Slot = PartSlot.LeftArm }).Ok, Is.True);
            GrantCurrency(Currencies.Ore, 100_000);
            GrantCurrency(Currencies.Metal, 1000);
            var screen = OpenParts();
            screen.OpenMergePicker(target, false);
            var plan = screen.PickerPlan;
            Assert.That(plan.Valid, Is.True, plan.Error);
            Assert.That(plan.Materials, Does.Not.Contain(equipped));
            Assert.That(plan.NeedsConfirmation, Is.True, "only locked Mythics can be the other Mythics");
            Assert.That(plan.Protected, Is.EquivalentTo(locked));
            int parts = S.Parts.Items.Count;
            screen.ConfirmPick();
            Assert.That(S.Parts.Items.Count, Is.EqualTo(parts), "nothing climbed before the confirmation");
            Assert.That(_f.App.ModalLayer.GetChild(_f.App.ModalLayer.childCount - 1).name, Is.EqualTo("Modal:ConfirmProtected"));

            // with free other Mythics available the locked ones are left alone
            // (not the equipped Whip's def: those are its own copies, protected)
            var free = new List<string> { Grant("myth_arcwright_drill", PartRarity.Mythic, 1)[0], Grant("myth_bloodletter_claw", PartRarity.Mythic, 1)[0] };
            screen.AutoPick();
            Assert.That(screen.PickerPlan.NeedsConfirmation, Is.False);
            Assert.That(screen.PickerPlan.Fodder, Is.EquivalentTo(free));
        }

        [Test]
        public void ClimbPicker_ManualSelection_IsCheckedByTheRules()
        {
            var target = Grant(Blaster, PartRarity.Mythic, 1)[0];
            var copy = Grant(Blaster, PartRarity.Mythic, 1)[0];
            var other = Grant(Cannon, PartRarity.Mythic, 1)[0];
            GrantCurrency(Currencies.Ore, 100_000);
            GrantCurrency(Currencies.Metal, 1000);
            var screen = OpenParts();
            screen.OpenMergePicker(target, false);
            Assert.That(screen.PickerPlan.Valid, Is.True, screen.PickerPlan.Error);
            Assert.That(screen.PickerPlan.Copies, Is.EqualTo(new[] { copy }));
            screen.TogglePick(other);
            Assert.That(screen.PickerPlan.Valid, Is.False, "Mythic → +1 takes one copy and no other Mythics");
            Assert.That(screen.PickerPlan.Error, Does.Contain("other Mythics"));
            screen.ClearPick();
            Assert.That(screen.PickerPlan.Valid, Is.False);
            Assert.That(screen.PickerPlan.FodderPoints, Is.EqualTo(0));
        }

        [Test]
        public void Replicator_TurnsThreeSpareMythicsIntoACopy()
        {
            var target = Grant(Blaster, PartRarity.MythicS, 1)[0];
            Grant(Crown, PartRarity.Mythic, 1);
            Grant("myth_rimeguard_plate", PartRarity.Mythic, 1);
            Grant("myth_storm_coil", PartRarity.Mythic, 1);
            GrantCurrency(Currencies.Ore, C.PartsConfig.ReplicateOre);
            long ore = S.Wallet.Get(Currencies.Ore);
            var screen = OpenParts();
            screen.OpenMergePicker(target, true);
            var plan = screen.PickerPlan;
            Assert.That(plan.Replicate, Is.True);
            Assert.That(plan.Valid, Is.True, plan.Error);
            Assert.That(plan.Materials.Count, Is.EqualTo(3));
            foreach (var uid in plan.Materials) Assert.That(S.Parts.Items[uid].DefId, Is.Not.EqualTo(Blaster));
            screen.ConfirmPick();
            int copies = 0;
            foreach (var p in S.Parts.Items.Values) if (p.DefId == Blaster && p.Rarity == (int)PartRarity.Mythic) copies++;
            Assert.That(copies, Is.EqualTo(1));
            Assert.That(S.Wallet.Get(Currencies.Ore), Is.EqualTo(ore - C.PartsConfig.ReplicateOre));
        }

        // ---- skills / awakening ------------------------------------------------------------------------

        [Test]
        public void PartSkills_MythicFive_ActiveByLadderStep_StandardByRarity()
        {
            foreach (var (rarity, unlocked) in new[] { (PartRarity.Mythic, 3), (PartRarity.Mythic2, 3), (PartRarity.MythicS, 4), (PartRarity.MythicSS, 5) })
            {
                var p = new PartItem { Uid = "t", DefId = Blaster, Rarity = (int)rarity };
                var lines = PartPlanner.SkillLines(C, p, false);
                Assert.That(lines.Count, Is.EqualTo(5));
                Assert.That(lines.FindAll(l => l.Unlocked).Count, Is.EqualTo(unlocked), rarity.ToString());
                Assert.That(lines.FindAll(l => l.Unlocked).ConvertAll(l => l.SkillId), Is.EqualTo(PartsSystem.PartSkills(C, p)));
            }
            Assert.That(PartPlanner.SkillLines(C, new PartItem { DefId = "c_head_guard", Rarity = 0 }, false), Is.Empty);
            Assert.That(PartPlanner.SkillLines(C, new PartItem { DefId = "l_head_guard", Rarity = (int)PartRarity.Legendary }, false).FindAll(l => l.Unlocked).Count, Is.EqualTo(2));
            var target = Grant(Blaster, PartRarity.MythicS, 1)[0];
            var screen = OpenParts();
            screen.Detail(target);
            int on = 0, off = 0;
            foreach (var t in _f.App.ModalLayer.GetComponentsInChildren<Transform>(true)) { if (t.name == "Skill:on") on++; if (t.name == "Skill:off") off++; }
            Assert.That(on, Is.EqualTo(4));
            Assert.That(off, Is.EqualTo(1));
        }

        [Test]
        public void AwakenPreview_ShowsRedesignedNameStatsAndSkills_AndAwakensOnceForCores()
        {
            var uid = Grant(Blaster, PartRarity.Mythic, 1)[0];
            var def = C.PartsConfig.Defs[Blaster];
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
            Assert.That(PartPlanner.Awaken(S, C, new PartItem { Uid = "e", DefId = "l_arm_fury", Rarity = (int)PartRarity.Legendary }).BlockReason, Does.Contain("Mythic"));

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
        public void LabTree_WholeTree_LaysOutByDependency_WithoutOverlaps()
        {
            LabReady();
            var layout = LabTreeLayout.Build(C);
            var rects = new List<KeyValuePair<string, Rect>>(layout.Nodes);
            Assert.That(rects.Count, Is.EqualTo(C.LabResearch.Count), "every node is placed once");
            foreach (var kv in rects)
            {
                Assert.That(kv.Value.xMin, Is.GreaterThanOrEqualTo(0), kv.Key);
                Assert.That(kv.Value.xMax, Is.LessThanOrEqualTo(layout.Width + 0.01f), kv.Key);
                Assert.That(kv.Value.yMax, Is.LessThanOrEqualTo(layout.Height + 0.01f), kv.Key);
                Assert.That(kv.Value.width, Is.GreaterThan(150), "readable node width");
                var lane = layout.Lanes[C.LabNode(kv.Key).Branch];
                Assert.That(kv.Value.xMin >= lane.xMin && kv.Value.xMax <= lane.xMax, Is.True, kv.Key + " sits in its branch lane");
            }
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                    Assert.That(rects[i].Value.Overlaps(rects[j].Value), Is.False, rects[i].Key + " overlaps " + rects[j].Key);
            int edges = 0;
            foreach (var d in C.LabResearch.Values) edges += d.Requires.Count;
            Assert.That(layout.Edges.Count, Is.EqualTo(edges), "every prerequisite is an edge, across branches too");
            foreach (var e in layout.Edges)
            {
                Assert.That(layout.Nodes[e.Key].yMax, Is.LessThan(layout.Nodes[e.Value].yMin), "edges run downwards: " + e.Key + " → " + e.Value);
                // the line's segments are axis-aligned and never cut through another node
                var pts = layout.Route(e.Key, e.Value);
                for (int k = 0; k + 1 < pts.Count; k++)
                {
                    var a = pts[k]; var b = pts[k + 1];
                    Assert.That(Mathf.Approximately(a.x, b.x) || Mathf.Approximately(a.y, b.y), Is.True, "axis-aligned " + e.Key + ">" + e.Value);
                    var seg = Rect.MinMaxRect(Mathf.Min(a.x, b.x) + 0.5f, Mathf.Min(a.y, b.y) + 0.5f, Mathf.Max(a.x, b.x) - 0.5f, Mathf.Max(a.y, b.y) - 0.5f);
                    if (seg.width < 0) seg.width = 0;
                    if (seg.height < 0) seg.height = 0;
                    foreach (var kv in rects)
                    {
                        if (kv.Key == e.Key || kv.Key == e.Value) continue;
                        var inner = Rect.MinMaxRect(kv.Value.xMin + 1, kv.Value.yMin + 1, kv.Value.xMax - 1, kv.Value.yMax - 1);
                        bool hits = seg.xMax >= inner.xMin && seg.xMin <= inner.xMax && seg.yMax >= inner.yMin && seg.yMin <= inner.yMax;
                        Assert.That(hits, Is.False, e.Key + ">" + e.Value + " crosses " + kv.Key);
                    }
                }
            }
        }

        [Test]
        public void Laboratory_Tree_DrawsEveryNodeOnce_AndALineForEveryDependency_PannableBothWays()
        {
            LabReady();
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.App.Open("laboratory");
            var screen = (LaboratoryScreen)_f.App.Router.Current;
            var names = new Dictionary<string, int>();
            foreach (var rt in screen.Root.GetComponentsInChildren<RectTransform>(true))
                names[rt.name] = (names.TryGetValue(rt.name, out int n) ? n : 0) + 1;
            foreach (var d in C.LabResearch.Values)
            {
                Assert.That(names.TryGetValue("LabNode:" + d.Id, out int count) ? count : 0, Is.EqualTo(1), d.Id + " drawn once");
                foreach (var r in d.Requires) Assert.That(names.ContainsKey("Edge:" + r.ResearchId + ">" + d.Id), Is.True, "line " + r.ResearchId + " → " + d.Id);
            }
            Assert.That(screen.TreeScroll.horizontal && screen.TreeScroll.vertical, Is.True, "the tree pans both ways");
            Assert.That(screen.Layout.Width, Is.GreaterThan(1000), "wider than the screen: panning needed");
            // no text lists dependencies on the tree, no raw ids anywhere on the screen
            foreach (var t in screen.Root.GetComponentsInChildren<Text>(true))
            {
                Assert.That(t.text, Does.Not.Contain("Requires"), t.name);
                Assert.That(t.text, Does.Not.Contain("ANY ").And.Not.Contain("ALL "), t.name);
                foreach (StatId st in System.Enum.GetValues(typeof(StatId)))
                {
                    string raw = st.ToString();
                    if (raw.Length > 3 && raw != Fmt.StatName(st)) Assert.That(t.text, Does.Not.Contain(raw), t.name + ": " + t.text);
                }
            }
        }

        [Test]
        public void Laboratory_NodeDetail_UsesStatNames_AndARequirementRowPerPrerequisite()
        {
            LabReady();
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.App.Open("laboratory");
            var screen = (LaboratoryScreen)_f.App.Router.Current;
            screen.NodeModal("lab_boss_damage");
            Text next = null; int reqs = 0; Text reason = null;
            foreach (var t in _f.App.ModalLayer.GetComponentsInChildren<Text>(true))
            {
                if (t.name == "EffectNext") next = t;
                if (t.name == "Requirement") reqs++;
                if (t.name == "DisabledReason") reason = t;
            }
            Assert.That(next.text, Does.Contain("Boss damage +2%"));
            Assert.That(next.text, Does.Not.Contain("BossDamage"));
            Assert.That(reqs, Is.EqualTo(C.LabNode("lab_boss_damage").Requires.Count));
            Assert.That(reason, Is.Not.Null, "a locked node says why Research is disabled");
            Assert.That(FindButton(_f.App.ModalLayer, "Research").interactable, Is.False);
        }

        [Test]
        public void Laboratory_NodeStates_LockedAvailableResearchingResearched()
        {
            LabReady();
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            GrantCurrency(Currencies.Data, 10_000);
            Assert.That(LaboratoryScreen.NodeState(S, C, C.LabNode("lab_atk_1")), Is.EqualTo(LabNodeState.Available));
            Assert.That(LaboratoryScreen.NodeState(S, C, C.LabNode("lab_boss_damage")), Is.EqualTo(LabNodeState.Locked));
            string researched = null;
            foreach (var d in C.LabResearch.Values) if (d.DefaultResearched && d.Levels.Count > 1) researched = d.Id;
            Assert.That(LaboratoryScreen.NodeState(S, C, C.LabNode(researched)), Is.EqualTo(LabNodeState.Researched));
            Assert.That(_f.Service.ExecuteNow(new LabResearch { ResearchId = "lab_atk_1" }).Ok, Is.True);
            Assert.That(LaboratoryScreen.NodeState(S, C, C.LabNode("lab_atk_1")), Is.EqualTo(LabNodeState.Researching));
            _f.App.Open("laboratory");
            var screen = (LaboratoryScreen)_f.App.Router.Current;
            bool Has(string node, string child)
            {
                var n = FindButton(screen.Root, "LabNode:" + node);
                foreach (var rt in n.GetComponentsInChildren<RectTransform>(true)) if (rt.name == child) return true;
                return false;
            }
            Assert.That(Has("lab_atk_1", "State:Researching") && Has("lab_atk_1", "ResearchingRing") && Has("lab_atk_1", "ResearchBar"), Is.True);
            Assert.That(Has("lab_boss_damage", "State:Locked") && Has("lab_boss_damage", "LockIcon"), Is.True);
            Assert.That(Has(researched, "State:Researched") && Has(researched, "CheckBadge"), Is.True);
            Assert.That(Has("lab_hp_1", "State:Available") && Has("lab_hp_1", "AvailableRing"), Is.True);
            // collecting pops the node (synchronous outside play mode) and leaves it at normal scale
            screen.PlayResearchDone("lab_atk_1");
            Assert.That(FindButton(screen.Root, "LabNode:lab_atk_1").transform.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void Laboratory_ResearchFromTheNodeModal_UsesNextCostAndDuration_AndShowsTheSlot()
        {
            LabReady();
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            GrantCurrency(Currencies.Data, 10_000);
            _f.App.Open("laboratory");
            var screen = (LaboratoryScreen)_f.App.Router.Current;
            Assert.That(screen.Layout, Is.Not.Null);
            Assert.That(FindButton(screen.Root, "LabNode:pkg_combo"), Is.Null, "no packages lane: signatures unlock by level");
            screen.ShowBranch("offense");
            Assert.That(screen.Layout.Nodes.Count, Is.GreaterThan(5));
            Assert.That(FindButton(screen.Root, "LabNode:lab_atk_1"), Is.Not.Null, "stat node drawn in the offense lane");
            Text lab = null;
            foreach (var t in screen.Root.GetComponentsInChildren<Text>(true)) if (t.name == "LabBuilding") lab = t;
            Assert.That(lab.text, Does.Contain("research slot"));

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
        public void Laboratory_SynergySkillNodes_NeedTheirSignatureUnlock()
        {
            LabReady();
            _f.App.Open("laboratory");
            var screen = (LaboratoryScreen)_f.App.Router.Current;
            screen.NodeModal("res_frost_nova");
            bool needs = false;
            foreach (var t in _f.App.ModalLayer.GetComponentsInChildren<Text>(true))
                if (t.name == "Requirement" && t.text.Contains(C.Feature(Features.SignatureIce).Name)) needs = true;
            Assert.That(needs, Is.True, "Frost Nova waits for the Ice signature's level unlock");
        }

        [Test]
        public void Laboratory_SkillNodesToggle_StatNodesDoNot()
        {
            LabReady();
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
        public void LabBuildingStatus_ExplainsWhatResearchNeeds()
        {
            Assert.That(LaboratoryScreen.BuildingStatus(S, C, out bool active), Does.Contain("not owned"));
            Assert.That(active, Is.False);
            _f.Service.DevGrant(new[] { new Reward(RewardKinds.Blueprint, ContentDb.LaboratoryId, 1) });
            Assert.That(LaboratoryScreen.BuildingStatus(S, C, out active), Does.Contain("build it"));
            LabReady();
            Assert.That(LaboratoryScreen.BuildingStatus(S, C, out active), Does.Contain("research slot"));
            Assert.That(active, Is.True);
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
        public void ResourceBar_ShowsUsefulCurrencies_AndDetailsListMedalsAndAwakenCores()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true); // chips show once their features are unlocked
            GrantCurrency(Currencies.Data, 1234);
            GrantCurrency(Currencies.ArenaMedals, 56);
            GrantCurrency(ProgressionCurrencies.PartAwakenCores, 7);
            _f.App.Bar.Rebuild();
            var texts = new Dictionary<string, string>();
            foreach (var t in _f.Canvas.GetComponentsInChildren<Text>(true)) if (t.name.StartsWith("Chip:")) texts[t.name] = t.text;
            Assert.That(texts.Count, Is.LessThanOrEqualTo(ResourceBar.MaxChips), "at most four chips, the rest in the wallet");
            Assert.That(texts.ContainsKey("Chip:" + Currencies.Stamina), Is.True, "the hub shows stamina");
            Assert.That(texts.ContainsKey("Chip:" + Currencies.Data), Is.False, "Data belongs to the Laboratory's bar");
            _f.App.Open("laboratory");
            _f.App.Bar.Tick(); // another screen: the bar rebuilds with its currencies
            texts.Clear();
            foreach (var t in _f.Canvas.GetComponentsInChildren<Text>(true)) if (t.name.StartsWith("Chip:")) texts[t.name] = t.text;
            Assert.That(texts["Chip:" + Currencies.Data], Is.EqualTo(Fmt.Num(S.Wallet.Get(Currencies.Data))), "icon + number only");
            Assert.That(ResourceBar.ChipsFor(S, C, "HubScreen"), Does.Not.Contain(Currencies.Fuel), "zero / irrelevant balances are hidden");
            var visible = HubLayout.VisibleCurrencies(S, C);
            Assert.That(visible, Does.Contain(Currencies.ArenaMedals).And.Contain(ProgressionCurrencies.PartAwakenCores));
            Assert.That(S.Wallet.Get(Currencies.Data), Is.GreaterThanOrEqualTo(1234));
            _f.App.Bar.ShowDetails();
            var names = new HashSet<string>();
            foreach (var t in _f.App.ModalLayer.GetComponentsInChildren<Text>(true)) names.Add(t.text);
            Assert.That(names, Does.Contain("Data").And.Contain("Arena Medals").And.Contain("Awaken Cores"));
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
