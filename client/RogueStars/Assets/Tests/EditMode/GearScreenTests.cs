using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// The gear experience of the owner's playtest (main level 2, Parts just unlocked): the Worm tab lists every part with
    /// equipped markers, filters/sorts, slot → filter, part detail with the Ore level up, the level-up badges and the
    /// currency explainers (what each currency is for and where it is spent, from the rules' reward-use mapping).
    /// </summary>
    public sealed class GearScreenTests
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

        /// <summary>Main level 2 reached for real: the onboarding settler unlocks Parts, the moment is claimed.</summary>
        private void ReachLevel2()
        {
            S.Counters[Requirements.MainLevelCounter] = 2;
            _f.Service.DevSetFlag("gear_test", false); // settles
            foreach (var id in OnboardingSystem.PendingCelebrations(S, C).ToArray())
                Assert.That(_f.Service.ExecuteNow(new FeatureCelebrate { FeatureId = id }).Ok, Is.True, id);
            Assert.That(Features.IsUnlocked(S, C, Features.Parts), Is.True);
        }

        private WormScreen OpenWorm()
        {
            _f.App.Open("worm");
            return (WormScreen)_f.App.Router.Current;
        }

        private void EquipBest()
        {
            // the player equips by hand (no auto-pick, W3): the first fitting part per slot
            foreach (PartSlot slot in System.Enum.GetValues(typeof(PartSlot)))
            {
                if (S.Parts.Equipped.ContainsKey(slot)) continue;
                foreach (var uid in PartsSystem.SortedPartUids(S))
                {
                    if (PartsSystem.IsEquipped(S, uid) || !GearGuide.Fits(PartsSystem.Def(C, PartsSystem.Get(S, uid)), slot)) continue;
                    Assert.That(_f.Service.ExecuteNow(new PartEquip { Uid = uid, Slot = slot }).Ok, Is.True);
                    break;
                }
            }
        }

        private List<string> Grant(string defId, PartRarity rarity, int count)
        {
            var before = new HashSet<string>(S.Parts.Items.Keys);
            _f.Service.DevGrant(new[] { new Reward(RewardKinds.Part, defId, count, (int)rarity) });
            return S.Parts.Items.Keys.Where(k => !before.Contains(k)).OrderBy(k => k).ToList();
        }

        private static Button Named(Transform root, string name) =>
            root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        private static Transform Child(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        private void SpendAllOre()
        {
            S.Wallet.Balances[Currencies.Ore] = 0;
            _f.Service.Refresh(_ => { });
        }

        // ---- inventory -----------------------------------------------------------------------------

        [Test]
        public void Level2_OreLevelsUpParts_FromDayOne()
        {
            ReachLevel2();
            long ore = S.Wallet.Get(Currencies.Ore);
            Assert.That(S.Parts.Items.Count, Is.GreaterThanOrEqualTo(3), "starter parts");
            foreach (var p in S.Parts.Items.Values)
            {
                Assert.That(GearGuide.NextLevelCost(C, p, out long cost, out long cores), Is.True, "a level-1 part can level up");
                Assert.That(cores, Is.EqualTo(0), "no Gear Cores below level 10");
                Assert.That(ore, Is.GreaterThanOrEqualTo(cost), "the Parts unlock's Ore pays a level");
            }
            Assert.That(HubLayout.VisibleCurrencies(S, C), Does.Contain(Currencies.Ore));
        }

        [Test]
        public void WormTab_SlotsShowTheEquipped_GridListsTheSpares()
        {
            ReachLevel2();
            EquipBest();
            var ids = C.PartsConfig.Defs.Keys.OrderBy(k => k).ToList();
            for (int i = 0; i < 9; i++) Grant(ids[i], (PartRarity)(i % 4), 1);
            var worm = OpenWorm();
            var spares = S.Parts.Items.Keys.Where(u => !PartsSystem.IsEquipped(S, u)).ToList();
            Assert.That(worm.ShownParts, Is.EquivalentTo(spares), "the grid lists the spares; the slots show what is worn");
            foreach (var uid in spares) Assert.That(Named(worm.Root, "Part:" + uid), Is.Not.Null, uid);
            foreach (var kv in S.Parts.Equipped)
            {
                Assert.That(Named(worm.Root, "Part:" + kv.Value), Is.Null, "equipped parts are on their slot, not in the grid");
                var slot = Named(worm.Root, "Slot:" + kv.Key);
                Assert.That(slot, Is.Not.Null, kv.Key.ToString());
                var p = S.Parts.Items[kv.Value];
                Assert.That(Child(slot.transform, "Level").GetComponent<Text>().text, Is.EqualTo("Lv." + p.Level));
                if (p.Rarity >= (int)PartRarity.Mythic)
                    Assert.That(Child(slot.transform, "Rank").GetComponent<Text>().text, Does.StartWith(RogueStars.Client.World.PartArt.Rank(p.Rarity).Substring(0, 1)));
                else Assert.That(Child(slot.transform, "Rank"), Is.Null, "no rank letter below Mythic");
            }
            // By Rarity (Best): rarity descending
            var r = worm.ShownParts.Select(u => S.Parts.Items[u].Rarity).ToList();
            Assert.That(r, Is.Ordered.Descending);
        }

        [Test]
        public void Inventory_SortToggle_CyclesRarityLevelSlot()
        {
            ReachLevel2();
            var ids = C.PartsConfig.Defs.Keys.OrderBy(k => k).ToList();
            foreach (var id in ids) Grant(id, (PartRarity)(ids.IndexOf(id) % 3), 1);
            var worm = OpenWorm();
            Assert.That(worm.Sort, Is.EqualTo(GearSort.Best));
            Assert.That(Child(Named(worm.Root, "SortToggle").transform, "Label").GetComponent<Text>().text, Is.EqualTo("By Rarity"));
            _f.Service.ExecuteNow(new PartLevelUp { Uid = worm.ShownParts.Last() });
            Named(worm.Root, "SortToggle").onClick.Invoke();
            Assert.That(worm.Sort, Is.EqualTo(GearSort.Level));
            Assert.That(S.Parts.Items[worm.ShownParts[0]].Level, Is.EqualTo(2), "By Level: highest level first");
            Named(worm.Root, "SortToggle").onClick.Invoke();
            Assert.That(worm.Sort, Is.EqualTo(GearSort.Slot));
            var kinds = worm.ShownParts.Select(u => (int)PartsSystem.Def(C, S.Parts.Items[u]).Kind).ToList();
            Assert.That(kinds, Is.Ordered, "By Slot: head … arms");
            Named(worm.Root, "SortToggle").onClick.Invoke();
            Assert.That(worm.Sort, Is.EqualTo(GearSort.Best));
        }

        [Test]
        public void BodySlot_OpensThePartSheet_EmptySlotOffersItsSpares()
        {
            ReachLevel2();
            var worm = OpenWorm();
            // nothing worn yet: the head slot lists the spare heads, tapping one equips it
            Named(worm.Root, "Slot:Head").onClick.Invoke();
            Assert.That(Child(_f.App.ModalLayer, "Modal:SlotSheet"), Is.Not.Null);
            var spare = GearGuide.SparesFor(S, C, PartSlot.Head);
            Assert.That(spare, Is.Not.Empty);
            Named(_f.App.ModalLayer, "Spare:" + spare[0].Uid).onClick.Invoke();
            Assert.That(S.Parts.Equipped[PartSlot.Head], Is.EqualTo(spare[0].Uid));
            Ui.Clear(_f.App.ModalLayer);
            // a worn slot opens its part's sheet
            Named(worm.Root, "Slot:Head").onClick.Invoke();
            Assert.That(Child(_f.App.ModalLayer, "Modal:PartDetail"), Is.Not.Null);
            Assert.That(Child(_f.App.ModalLayer, "PartName").GetComponent<Text>().text, Is.EqualTo(Fmt.PartName(C, spare[0])));
        }

        // ---- level up with Ore -------------------------------------------------------------------

        [Test]
        public void PartDetail_ShowsOreCost_AndLevelsUp()
        {
            ReachLevel2();
            EquipBest();
            var worm = OpenWorm();
            var uid = S.Parts.Equipped[PartSlot.Head];
            Named(worm.Root, "Slot:Head").onClick.Invoke();
            var modal = _f.App.ModalLayer;
            Assert.That(Child(modal, "Modal:PartDetail"), Is.Not.Null);
            long cost = C.PartsConfig.LevelOreCosts[1];
            var up = Named(modal, "PartLevelUp");
            Assert.That(up.GetComponentInChildren<Text>().text, Does.Contain(Fmt.Num(cost) + " Ore"));
            Assert.That(Child(modal, "Skill:on"), Is.Null, "parts v2: a Common part has no skills"); // Epic+ list theirs (PartsLabArenaScreenTests)
            Assert.That(up.interactable, Is.True);
            long ore = S.Wallet.Get(Currencies.Ore);
            up.onClick.Invoke();
            Assert.That(S.Parts.Items[uid].Level, Is.EqualTo(2));
            Assert.That(S.Wallet.Get(Currencies.Ore), Is.EqualTo(ore - cost));
            Assert.That(Child(_f.App.ModalLayer, "Modal:PartDetail"), Is.Not.Null, "the sheet reopens on the new level");
            Assert.That(Child(_f.App.ModalLayer, "LevelText").GetComponent<Text>().text, Does.Contain("Lv 2"));

            // without Ore: disabled; the Ore button explains Ore
            SpendAllOre();
            worm.Detail(uid);
            Assert.That(Named(_f.App.ModalLayer, "PartLevelUp").interactable, Is.False);
            Named(_f.App.ModalLayer, "OreInfo").onClick.Invoke();
            Assert.That(Child(_f.App.ModalLayer, "Modal:Currency:" + Currencies.Ore), Is.Not.Null);
        }

        [Test]
        public void AffordableUpgrade_BadgesTheWormTab_AndTheSlot_NoCallout()
        {
            ReachLevel2();
            Assert.That(GearGuide.HasAffordableUpgrade(S, C), Is.False, "nothing equipped yet");
            EquipBest();
            Assert.That(GearGuide.HasAffordableUpgrade(S, C), Is.True);
            var hub = new HubScreen();
            _f.App.Router.Reset(hub);
            Assert.That(hub.TabBadge("worm"), Is.EqualTo(HubScreen.UpgradeBadge));
            var tab = Child(hub.Root, "Tab:worm");
            Assert.That(Child(tab, "BadgeText").GetComponent<Text>().text, Is.EqualTo(HubScreen.UpgradeBadge));

            var worm = OpenWorm();
            Assert.That(Child(Named(worm.Root, "Slot:Head").transform, "LevelUpBadge"), Is.Not.Null);
            Assert.That(Child(worm.Root, "UpgradeCallout"), Is.Null, "the level-up callout was removed (W3)");
            Assert.That(Child(worm.Root, "Button:Equip best parts"), Is.Null, "no auto-equip (W3)");

            SpendAllOre();
            Assert.That(GearGuide.HasAffordableUpgrade(S, C), Is.False);
            worm.Rebuild();
            Assert.That(Child(Named(worm.Root, "Slot:Head").transform, "LevelUpBadge"), Is.Null);
            _f.App.Router.Reset(hub = new HubScreen());
            Assert.That(hub.TabBadge("worm"), Is.Not.EqualTo(HubScreen.UpgradeBadge));
        }

        // ---- currency explainers -----------------------------------------------------------------

        [Test]
        public void OreChip_ExplainsOre_AndGoesToTheWorm()
        {
            ReachLevel2();
            EquipBest();
            _f.App.Router.Reset(new HubScreen());
            _f.App.Bar.Rebuild();
            var chip = Named(_f.App.Root, "ChipBg:" + Currencies.Ore);
            Assert.That(chip, Is.Not.Null, "the Ore chip is tappable");
            chip.onClick.Invoke();
            var modal = Child(_f.App.ModalLayer, "Modal:Currency:" + Currencies.Ore);
            Assert.That(modal, Is.Not.Null);
            var texts = modal.GetComponentsInChildren<Text>(true).Select(t => t.text).ToList();
            Assert.That(texts, Does.Contain(C.CurrencyUse(Currencies.Ore).Use));
            Assert.That(texts.Any(t => t.StartsWith("You can level up")), Is.True, "what Ore can do right now");
            var go = Named(modal, "Button:Go to Worm");
            Assert.That(go, Is.Not.Null);
            go.onClick.Invoke();
            Assert.That(_f.App.Router.Current, Is.InstanceOf<WormScreen>());
            // already on the Worm: no "Go to Worm" (W6)
            _f.App.Bar.ShowCurrency(Currencies.Ore);
            Assert.That(Named(_f.App.ModalLayer, "Button:Go to Worm"), Is.Null);
            Assert.That(Named(_f.App.ModalLayer, "Button:Close"), Is.Not.Null);
        }

        [Test]
        public void EveryCurrency_SaysWhatItIsFor_AndLinksToWhereItIsSpent()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            var visible = HubLayout.VisibleCurrencies(S, C);
            Assert.That(visible.Count, Is.GreaterThan(10));
            foreach (var id in visible)
            {
                var use = C.CurrencyUse(id);
                Assert.That(use, Is.Not.Null, id);
                var info = CurrencyGuide.For(S, C, id);
                Assert.That(info.Use, Is.EqualTo(use.Use), id);
                Assert.That(info.ScreenId, Is.Not.Null, id + " has a place to spend it");
                var entry = ScreenCatalog.Find(info.ScreenId);
                Assert.That(entry, Is.Not.Null, id);

                _f.App.Router.Reset(new HubScreen());
                var modal = _f.App.Bar.ShowCurrency(id);
                Assert.That(modal.Root.GetComponentsInChildren<Text>(true).Select(t => t.text), Does.Contain(use.Use), id);
                Named(modal.Root.transform, "Button:" + info.GoLabel).onClick.Invoke();
                Assert.That(_f.App.Router.Current.GetType(), Is.EqualTo(entry.Create().GetType()), id + " → " + info.ScreenId);
            }
            // where the uses point (the reward-use mapping's first unlocked use)
            Assert.That(CurrencyGuide.SpendScreen(S, C, Currencies.Ore), Is.EqualTo("worm"));
            Assert.That(CurrencyGuide.SpendScreen(S, C, Currencies.Stamina), Is.EqualTo("portals"));
            Assert.That(CurrencyGuide.SpendScreen(S, C, Currencies.Diamonds), Is.EqualTo("shop"));
            Assert.That(CurrencyGuide.SpendScreen(S, C, Currencies.Data), Is.EqualTo("laboratory"));
            Assert.That(CurrencyGuide.SpendScreen(S, C, Currencies.ArenaMedals), Is.EqualTo("arena"));

            // the details list: every row says what the currency is for and opens its explainer
            _f.App.Router.Reset(new HubScreen());
            _f.App.Bar.ShowDetails();
            foreach (var id in visible)
            {
                var row = Child(_f.App.ModalLayer, "Currency:" + id);
                Assert.That(row, Is.Not.Null, id);
                Assert.That(row.GetComponentsInChildren<Text>(true).Select(t => t.text), Does.Contain(C.CurrencyUse(id).Use), id);
            }
            Child(_f.App.ModalLayer, "Currency:" + Currencies.Data).GetComponent<Button>().onClick.Invoke();
            Assert.That(Child(_f.App.ModalLayer, "Modal:Currency:" + Currencies.Data), Is.Not.Null);
        }

        [Test]
        public void NewAccount_DiamondsGoToTheShop_FromTheStart()
        {
            // the Shop's offers need no unlock (owner, 2026-11-27): Diamonds lead there on day 1
            Assert.That(CurrencyGuide.SpendScreen(S, C, Currencies.Diamonds), Is.EqualTo("shop"));
            Assert.That(CurrencyGuide.For(S, C, Currencies.Stamina).GoLabel, Is.EqualTo("Go to Portals"));
        }
    }
}
