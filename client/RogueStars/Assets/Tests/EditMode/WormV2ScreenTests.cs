using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Parts v2 phase 2: the Worm screen on the board "New · worm" (slots → equipped parts, either-arm swap, the part sheet's
    /// buttons per state, the Climb picker checked by ValidateClimb, the Feed sheet's Metal preview = MetalValue, the
    /// Replicator's exactly three Mythics, red "!" alerts, the board layout).
    /// </summary>
    public sealed class WormV2ScreenTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        private const string Blaster = "myth_firebrand_blaster", Cannon = "myth_shatter_cannon", Whip = "myth_chainlash_whip", Crown = "myth_cinder_crown";

        [SetUp]
        public void SetUp()
        {
            PartLocks.Persist = false;
            PartLocks.ClearAll();
            _f = new AppFixture();
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
        }

        [TearDown]
        public void TearDown()
        {
            _f.Dispose();
            PartLocks.ClearAll();
            PartLocks.Persist = true;
        }

        private List<string> Grant(string defId, PartRarity rarity, int count)
        {
            var before = new HashSet<string>(S.Parts.Items.Keys);
            _f.Service.DevGrant(new[] { new Reward(RewardKinds.Part, defId, count, (int)rarity) });
            return S.Parts.Items.Keys.Where(k => !before.Contains(k)).OrderBy(k => k).ToList();
        }

        private void GrantCurrency(string id, long amount) => _f.Service.DevGrant(new[] { Reward.Cur(id, amount) });

        private WormScreen OpenWorm()
        {
            _f.App.Open("worm");
            return (WormScreen)_f.App.Router.Current;
        }

        private static Button Named(Transform root, string name) => root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);
        private static Transform Child(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        private Transform Modal => _f.App.ModalLayer;

        private void Equip(string uid, PartSlot slot) => Assert.That(_f.Service.ExecuteNow(new PartEquip { Uid = uid, Slot = slot }).Ok, Is.True);

        // ---- layout ---------------------------------------------------------------------------------

        [Test]
        public void Layout_SixSlotsMapToTheEquippedParts_AtTheBoardPositions()
        {
            var head = Grant("e_head_guard", PartRarity.Epic, 1)[0];
            var left = Grant(Blaster, PartRarity.MythicS, 1)[0];
            var right = Grant("l_arm_fury", PartRarity.Legendary, 1)[0];
            Equip(head, PartSlot.Head); Equip(left, PartSlot.LeftArm); Equip(right, PartSlot.RightArm);
            var worm = OpenWorm();
            var expected = new Dictionary<PartSlot, string> { { PartSlot.Head, "E" }, { PartSlot.LeftArm, "S" }, { PartSlot.RightArm, "L" } };
            foreach (var slot in PartsSystem.AllSlots)
            {
                var tile = Named(worm.Root, "Slot:" + slot);
                Assert.That(tile, Is.Not.Null, slot.ToString());
                var rt = (RectTransform)tile.transform;
                var pos = WormScreen.SlotPos(slot);
                Assert.That(rt.anchoredPosition, Is.EqualTo(new Vector2(pos.x, -pos.y)), slot + " at its board position");
                bool onLeft = slot == PartSlot.Head || slot == PartSlot.LeftArm || slot == PartSlot.Legs;
                Assert.That(pos.x < 540, Is.EqualTo(onLeft), "head, left arm, legs left; body, right arm, tail right");
                if (expected.TryGetValue(slot, out var rank))
                    Assert.That(Child(tile.transform, "Rank").GetComponent<Text>().text, Is.EqualTo(rank), slot.ToString());
                else if (!S.Parts.Equipped.ContainsKey(slot))
                    Assert.That(Child(tile.transform, "Caption"), Is.Not.Null, slot + ": empty socket");
            }
            Assert.That(Child(Named(worm.Root, "Slot:LeftArm").transform, "Level").GetComponent<Text>().text, Is.EqualTo("Lv.1"));
            // board pieces: top bar with three currencies, power plate, three stat bars, four features, three tabs, the panel header
            foreach (var c in worm.TopCurrencies) Assert.That(Child(worm.Root, "Currency:" + c), Is.Not.Null, c);
            Assert.That(worm.TopCurrencies, Is.EqualTo(new[] { Currencies.Ore, Currencies.Metal, Currencies.Diamonds }));
            Assert.That(Child(worm.Root, "Power").GetComponent<Text>().text, Is.EqualTo(Fmt.Num(_f.App.Session.Engine.BuildLoadout(S, _f.App.Session.NowMs).Power)));
            foreach (var st in new[] { StatId.Hp, StatId.Atk, StatId.Def }) Assert.That(Child(worm.Root, "StatBar:" + st), Is.Not.Null);
            foreach (var f in new[] { "talents", "runes", "mounts", "laboratory" }) Assert.That(Named(worm.Root, "Feature:" + f), Is.Not.Null, f);
            foreach (var t in new[] { "Parts", "Levels", "Stats" }) Assert.That(Named(worm.Root, "Tab:" + t), Is.Not.Null, t);
            Assert.That(Named(worm.Root, "SortToggle"), Is.Not.Null);
            Assert.That(Named(worm.Root, "FeedSpares"), Is.Not.Null);
            Assert.That(Named(worm.Root, "Tab:worm"), Is.Not.Null, "the board nav");
            Assert.That(Child(worm.Root, "BackButton"), Is.Null, "a main tab: no back button");
        }

        [Test]
        public void Layout_ArmSwap_FromEitherArm()
        {
            var a = Grant("l_arm_fury", PartRarity.Legendary, 1)[0];
            var b = Grant("r_arm_guard", PartRarity.Rare, 1)[0];
            var worm = OpenWorm();
            worm.Detail(a);
            Assert.That(Named(Modal, "Equip:LeftArm"), Is.Not.Null, "an arm part offers both arms");
            Assert.That(Named(Modal, "Equip:RightArm"), Is.Not.Null);
            Named(Modal, "Equip:RightArm").onClick.Invoke();
            Assert.That(S.Parts.Equipped[PartSlot.RightArm], Is.EqualTo(a));
            // on the right arm: move to the left, no Equip for the arm it wears
            Assert.That(Named(Modal, "Equip:RightArm"), Is.Null);
            Named(Modal, "Equip:LeftArm").onClick.Invoke();
            Assert.That(S.Parts.Equipped[PartSlot.LeftArm], Is.EqualTo(a));
            Assert.That(S.Parts.Equipped.ContainsKey(PartSlot.RightArm) && S.Parts.Equipped[PartSlot.RightArm] == a, Is.False);
            worm.Detail(b);
            Named(Modal, "Equip:RightArm").onClick.Invoke();
            Assert.That(S.Parts.Equipped[PartSlot.RightArm], Is.EqualTo(b));
            Ui.Clear(Modal);
            worm.Refresh();
            Assert.That(Child(Named(worm.Root, "Slot:LeftArm").transform, "Rank").GetComponent<Text>().text, Is.EqualTo("L"));
            Assert.That(Child(Named(worm.Root, "Slot:RightArm").transform, "Rank").GetComponent<Text>().text, Is.EqualTo("R"));
            Assert.That(worm.ShownParts, Does.Not.Contain(a).And.Not.Contain(b));
        }

        // ---- part sheet buttons per state -----------------------------------------------------------------

        [Test]
        public void Sheet_CommonSpare_EquipLevelLockFeed_NoClimbNoAwaken()
        {
            var uid = Grant("c_head_keen", PartRarity.Common, 1)[0];
            var worm = OpenWorm();
            worm.Detail(uid);
            Assert.That(Child(Modal, "Modal:PartDetail"), Is.Not.Null);
            Assert.That(Named(Modal, "Equip:Head"), Is.Not.Null);
            Assert.That(Named(Modal, "Unequip"), Is.Null);
            Assert.That(Named(Modal, "PartLevelUp"), Is.Not.Null);
            Assert.That(Named(Modal, "PartLock"), Is.Not.Null);
            Assert.That(Named(Modal, "Feed"), Is.Not.Null);
            Assert.That(Named(Modal, "OpenClimb"), Is.Null);
            Assert.That(Named(Modal, "OpenAwaken"), Is.Null);
            Assert.That(Child(Modal, "Ladder"), Is.Null, "no ladder on a standard part");
            Assert.That(Child(Modal, "LevelText").GetComponent<Text>().text, Is.EqualTo("Lv 1 / 10"));
            Assert.That(Child(Modal, "CapHead").GetComponent<Text>().text, Does.Contain("Lv 10"), "stats now and at cap");
            // lock: the rules' lock and the sheet's state
            Named(Modal, "PartLock").onClick.Invoke();
            Assert.That(S.Parts.Items[uid].Locked, Is.True);
            Assert.That(Named(Modal, "PartLock").GetComponentInChildren<Text>().text, Does.StartWith("Unlock"));
            Assert.That(Named(Modal, "Feed").interactable, Is.False, "a locked part is never fed");
        }

        [Test]
        public void Sheet_EquippedLegendary_UnequipAndSkills_NoClimb()
        {
            var uid = Grant("l_head_guard", PartRarity.Legendary, 1)[0];
            Equip(uid, PartSlot.Head);
            var worm = OpenWorm();
            worm.ShowSlot(PartSlot.Head);
            Assert.That(Named(Modal, "Unequip"), Is.Not.Null);
            Assert.That(Named(Modal, "Equip:Head"), Is.Null);
            Assert.That(Named(Modal, "Feed"), Is.Null, "never feed what is worn");
            Assert.That(Named(Modal, "OpenClimb"), Is.Null, "standard parts never climb");
            Assert.That(Modal.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Skill:on"), Is.EqualTo(2), "Legendary: two skills");
            Assert.That(Child(Modal, "EquippedChip"), Is.Not.Null);
            Named(Modal, "Unequip").onClick.Invoke();
            Assert.That(S.Parts.Equipped.ContainsKey(PartSlot.Head), Is.False);
        }

        [Test]
        public void Sheet_MythicS_Ladder_LockedFifthSkillAtSS_ClimbAndAwaken()
        {
            var uid = Grant(Blaster, PartRarity.MythicS, 1)[0];
            var worm = OpenWorm();
            worm.Detail(uid);
            Assert.That(Child(Modal, "Ladder"), Is.Not.Null);
            foreach (var r in new[] { "M", "M+1", "M+2", "S", "S+1", "S+2", "SS" }) Assert.That(Child(Modal, "Step:" + r), Is.Not.Null, r);
            Assert.That(Child(Child(Modal, "Step:S"), "Cap").GetComponent<Text>().text, Is.EqualTo("Lv 85"));
            Assert.That(Child(Child(Modal, "Step:SS"), "Adds").GetComponent<Text>().text, Is.EqualTo("+skill 5"));
            var off = Modal.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Skill:off").ToList();
            Assert.That(Modal.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Skill:on"), Is.EqualTo(4));
            Assert.That(off.Count, Is.EqualTo(1));
            Assert.That(Child(off[0], "SkillLock").GetComponentInChildren<Text>().text, Is.EqualTo("AT SS"));
            var skill5 = C.Skill(C.PartsConfig.Defs[Blaster].SkillIds[4]);
            Assert.That(Child(off[0], "SkillText").GetComponent<Text>().text, Is.EqualTo(Fmt.SkillFull(skill5)), "the approved text");
            Assert.That(Named(Modal, "OpenClimb"), Is.Not.Null);
            Assert.That(Named(Modal, "OpenAwaken"), Is.Null, "no cores: awaken only when eligible");
            // the awakened texts preview
            Named(Modal, "ToggleAwakened").onClick.Invoke();
            var aw = C.Skill(C.PartsConfig.Defs[Blaster].AwakenedSkillIds[0]);
            Assert.That(Modal.GetComponentsInChildren<Text>(true).Any(t => t.name == "SkillText" && t.text == Fmt.SkillFull(aw)), Is.True);
            GrantCurrency(ProgressionCurrencies.PartAwakenCores, C.PartsConfig.AwakenCost);
            worm.Detail(uid);
            Assert.That(Named(Modal, "OpenAwaken"), Is.Not.Null);
            Named(Modal, "OpenAwaken").onClick.Invoke();
            Assert.That(Modal.GetComponentsInChildren<Text>(true).Any(t => t.name == "AwakenNames"), Is.True);
        }

        // ---- climb picker -------------------------------------------------------------------------------

        [Test]
        public void ClimbPicker_ShowsTheStepsNeeds_AndValidatesWithTheRules()
        {
            var target = Grant(Blaster, PartRarity.MythicS, 1)[0];
            var copies = Grant(Blaster, PartRarity.Mythic, 2);
            var others = new List<string> { Grant(Cannon, PartRarity.Mythic, 1)[0], Grant(Whip, PartRarity.Mythic, 1)[0] };
            GrantCurrency(Currencies.Ore, 100_000);
            GrantCurrency(Currencies.Metal, 1000);
            var worm = OpenWorm();
            worm.Detail(target);
            Named(Modal, "OpenClimb").onClick.Invoke();
            Assert.That(Child(Modal, "Modal:Picker"), Is.Not.Null);
            var step = C.PartsConfig.Rarities[(int)PartRarity.MythicS1].Climb;
            Assert.That(Child(Child(Modal, "NeedCopies"), "Have").GetComponent<Text>().text, Is.EqualTo("2 / " + step.Copies));
            Assert.That(Child(Child(Modal, "NeedOthers"), "Have").GetComponent<Text>().text, Is.EqualTo("2 / " + step.Others));
            Assert.That(Child(Child(Modal, "NeedMetal"), "Have").GetComponent<Text>().text, Is.EqualTo(Fmt.Num(step.Metal)));
            Assert.That(Child(Child(Modal, "NeedCopies"), "What").GetComponent<Text>().text, Does.Contain("you have 2"));
            foreach (var uid in copies.Concat(others)) Assert.That(Named(Modal, "Pick:" + uid), Is.Not.Null, "material tiles");
            Assert.That(Named(Modal, "PickConfirm").interactable, Is.True);

            // a hand-made selection is exactly what the rules accept
            Named(Modal, "Pick:" + others[0]).onClick.Invoke(); // deselect one other Mythic
            var plan = worm.PickerPlan;
            Assert.That(plan.Valid, Is.False);
            Assert.Throws<RuleException>(() => PartsSystem.ValidateClimb(S, C, target, plan.Materials));
            Assert.That(Named(Modal, "PickConfirm").interactable, Is.False);
            Assert.That(Child(Modal, "PickerStatus").GetComponent<Text>().text, Does.Contain("other Mythics"));
            Named(Modal, "PickAuto").onClick.Invoke();
            Assert.That(worm.PickerPlan.Valid, Is.True, worm.PickerPlan.Error);
            Assert.DoesNotThrow(() => PartsSystem.ValidateClimb(S, C, target, worm.PickerPlan.Materials));
            // not enough Metal: the rules say no, so does the picker
            S.Wallet.Balances[Currencies.Metal] = 10;
            worm.AutoPick();
            Assert.That(worm.PickerPlan.Valid, Is.False);
            Assert.That(worm.PickerPlan.Error, Does.Contain("metal"));
            GrantCurrency(Currencies.Metal, 1000);
            worm.AutoPick();
            Named(Modal, "PickConfirm").onClick.Invoke();
            Assert.That(S.Parts.Items[target].Rarity, Is.EqualTo((int)PartRarity.MythicS1));
            Assert.That(Child(Modal, "Modal:PartDetail"), Is.Not.Null, "back on the climbed part's sheet");
        }

        // ---- feed spares ----------------------------------------------------------------------------------

        [Test]
        public void FeedSheet_PreviewEqualsMetalValue_AndFeeds()
        {
            var commons = Grant("c_tail_mend", PartRarity.Common, 3);
            var rare = Grant("r_legs_keen", PartRarity.Rare, 1)[0];
            var epic = Grant("e_body_fury", PartRarity.Epic, 1)[0];
            var leg = Grant("l_arm_strike", PartRarity.Legendary, 1)[0];
            var locked = Grant("c_head_mend", PartRarity.Common, 1)[0];
            Assert.That(_f.Service.ExecuteNow(new PartLock { Uid = locked, Locked = true }).Ok, Is.True);
            var mythic = Grant(Crown, PartRarity.Mythic, 1)[0];
            var worn = Grant("e_head_swift", PartRarity.Epic, 1)[0];
            Equip(worn, PartSlot.Head);
            var worm = OpenWorm();
            Named(worm.Root, "FeedSpares").onClick.Invoke();
            Assert.That(Child(Modal, "Modal:Feed"), Is.Not.Null);
            // default: every spare Common and Rare; locked, worn and Mythic parts are never offered
            var expected = PartPlanner.Feedable(S, C, null).Where(p => p.Rarity <= (int)PartRarity.Rare).Select(p => p.Uid).ToList();
            Assert.That(expected, Does.Contain(rare).And.Contains(commons[0]));
            Assert.That(worm.FeedSelection, Is.EquivalentTo(expected));
            foreach (var never in new[] { locked, mythic, worn }) Assert.That(Named(Modal, "FeedPart:" + never), Is.Null, never);
            long Sum() => worm.FeedSelection.Sum(u => PartsSystem.MetalValue(C, S.Parts.Items[u]));
            Assert.That(worm.FeedPreviewMetal, Is.EqualTo(Sum()));
            Assert.That(worm.FeedPreviewMetal, Is.EqualTo(expected.Count(u => S.Parts.Items[u].Rarity == 0) + 2));
            Named(Modal, "FeedPart:" + leg).onClick.Invoke();
            Named(Modal, "FeedRarity:2").onClick.Invoke(); // all Epics
            Assert.That(worm.FeedSelection, Does.Contain(leg).And.Contain(epic));
            Assert.That(worm.FeedPreviewMetal, Is.EqualTo(Sum()));
            Assert.That(worm.FeedPreviewMetal, Is.EqualTo(expected.Count(u => S.Parts.Items[u].Rarity == 0) + 2 + 6 + 20));
            Assert.That(Child(Modal, "FeedMetal").GetComponent<Text>().text, Is.EqualTo("+" + Fmt.Num(worm.FeedPreviewMetal) + " Metal"));
            long metal = S.Wallet.Get(Currencies.Metal);
            long preview = worm.FeedPreviewMetal;
            Named(Modal, "FeedConfirm").onClick.Invoke();
            Assert.That(S.Wallet.Get(Currencies.Metal), Is.EqualTo(metal + preview));
            Assert.That(S.Parts.Items.ContainsKey(leg), Is.False);
            Assert.That(S.Parts.Items.ContainsKey(locked) && S.Parts.Items.ContainsKey(mythic) && S.Parts.Items.ContainsKey(worn), Is.True);
        }

        // ---- Replicator --------------------------------------------------------------------------------

        [Test]
        public void Replicator_NeedsExactlyThreeSpareMythics()
        {
            var target = Grant(Blaster, PartRarity.MythicS, 1)[0];
            var spares = new List<string> { Grant(Crown, PartRarity.Mythic, 1)[0], Grant("myth_rimeguard_plate", PartRarity.Mythic, 1)[0], Grant("myth_storm_coil", PartRarity.Mythic, 1)[0], Grant(Cannon, PartRarity.Mythic, 1)[0] };
            GrantCurrency(Currencies.Ore, C.PartsConfig.ReplicateOre);
            var worm = OpenWorm();
            Named(worm.Root, "FeedSpares").onClick.Invoke();
            Named(Modal, "FeedTab:replicator").onClick.Invoke();
            Assert.That(Named(Modal, "Replicate:" + target), Is.Not.Null, "one tile per owned Mythic");
            Named(Modal, "Replicate:" + target).onClick.Invoke();
            Assert.That(Child(Modal, "Modal:Picker"), Is.Not.Null);
            Assert.That(worm.PickerPlan.Replicate, Is.True);
            Assert.That(worm.PickerPlan.Materials.Count, Is.EqualTo(3), "auto-selects three");
            Assert.That(worm.PickerPlan.Valid, Is.True, worm.PickerPlan.Error);
            var extra = spares.First(u => !worm.PickerPlan.Materials.Contains(u));
            worm.TogglePick(extra); // four
            Assert.That(worm.PickerPlan.Valid, Is.False, "four is not three");
            Assert.That(Named(Modal, "PickConfirm").interactable, Is.False);
            worm.TogglePick(extra);
            worm.TogglePick(worm.PickerPlan.Materials[0]); // two
            Assert.That(worm.PickerPlan.Valid, Is.False, "two is not three");
            worm.AutoPick();
            Assert.That(worm.PickerPlan.Valid, Is.True);
            int before = S.Parts.Items.Values.Count(p => p.DefId == Blaster);
            Named(Modal, "PickConfirm").onClick.Invoke();
            Assert.That(S.Parts.Items.Values.Count(p => p.DefId == Blaster), Is.EqualTo(before + 1));
        }

        // ---- alerts ---------------------------------------------------------------------------------------

        [Test]
        public void Alerts_ClimbReady_BetterPart_Awaken()
        {
            var worn = Grant("r_head_guard", PartRarity.Rare, 1)[0];
            Equip(worn, PartSlot.Head);
            var worm = OpenWorm();
            Assert.That(Child(Named(worm.Root, "Slot:Head").transform, "Alert"), Is.Null, "nothing to do");
            var better = Grant("e_head_strike", PartRarity.Epic, 1)[0];
            worm.Refresh();
            Assert.That(GearGuide.SlotAlert(S, C, PartSlot.Head, null), Is.EqualTo(PartAlert.Better));
            Assert.That(Child(Named(worm.Root, "Slot:Head").transform, "Alert"), Is.Not.Null, "a better spare head");
            Assert.That(Child(Named(worm.Root, "Part:" + better).transform, "Alert"), Is.Not.Null);
            var myth = Grant(Blaster, PartRarity.Mythic, 1)[0];
            Equip(myth, PartSlot.LeftArm);
            Assert.That(GearGuide.Alert(S, C, S.Parts.Items[myth], null), Is.EqualTo(PartAlert.None));
            Grant(Blaster, PartRarity.Mythic, 1);
            GrantCurrency(Currencies.Metal, 100);
            GrantCurrency(Currencies.Ore, 10_000);
            Assert.That(GearGuide.Alert(S, C, S.Parts.Items[myth], null), Is.EqualTo(PartAlert.Climb));
            worm.Refresh();
            Assert.That(Child(Named(worm.Root, "Slot:LeftArm").transform, "Alert"), Is.Not.Null);
            S.Wallet.Balances[Currencies.Metal] = 0;
            GrantCurrency(ProgressionCurrencies.PartAwakenCores, C.PartsConfig.AwakenCost);
            Assert.That(GearGuide.Alert(S, C, S.Parts.Items[myth], null), Is.EqualTo(PartAlert.Awaken));
        }

        [Test]
        public void Tiles_RankLetters_AndIconKeys()
        {
            Assert.That(Enumerable.Range(0, 11).Select(PartArt.Rank), Is.EqualTo(new[] { "C", "R", "E", "L", "M", "M+1", "M+2", "S", "S+1", "S+2", "SS" }));
            Assert.That(PartArt.IconKey(C.PartsConfig.Defs["e_head_guard"]), Is.EqualTo("e_head_guard"), "one icon per part");
            Assert.That(PartArt.IconKey(C.PartsConfig.Defs[Blaster]), Is.EqualTo(Blaster));
            foreach (PartKind k in System.Enum.GetValues(typeof(PartKind))) Assert.That(PartArt.SlotGlyph(k), Is.Not.Null, k.ToString());
            var uid = Grant("e_head_guard", PartRarity.Epic, 2)[0];
            var worm = OpenWorm();
            var tile = Named(worm.Root, "Part:" + uid).transform;
            Assert.That(Child(tile, "Copies").GetComponent<Text>().text, Is.EqualTo("1"), "the copy-count diamond");
            Assert.That(Child(tile, "Glyph"), Is.Not.Null, "no icon art yet: the slot glyph");
            Assert.That(Child(tile, "Pips"), Is.Not.Null);
        }

        [Test]
        public void Tabs_And_Features_Navigate()
        {
            var worm = OpenWorm();
            Named(worm.Root, "Tab:Stats").onClick.Invoke();
            Assert.That(worm.CurrentTab, Is.EqualTo(WormScreen.WormTab.Stats));
            Assert.That(Child(worm.Root, "StatsPanel"), Is.Not.Null);
            Named(worm.Root, "Feature:talents").onClick.Invoke();
            Assert.That(worm.CurrentTab, Is.EqualTo(WormScreen.WormTab.Levels));
            Assert.That(Child(worm.Root, "LevelsPanel"), Is.Not.Null);
            Assert.That(worm.TopCurrencies[1], Is.EqualTo(Currencies.Biomass));
            Named(worm.Root, "StatSheetButton").onClick.Invoke();
            Assert.That(Child(Modal, "Modal:StatSheet"), Is.Not.Null);
            Ui.Clear(Modal);
            Named(worm.Root, "Feature:laboratory").onClick.Invoke();
            Assert.That(_f.App.Router.Current, Is.InstanceOf<LaboratoryScreen>());
        }
    }
}
