using System.Collections.Generic;
using System.Linq;
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
    /// <summary>
    /// Rewards follow use (GDD §13) in the client: the currency chips and details list exactly the currencies the rules'
    /// reward-use mapping says the player can use, and reward displays (run result, level details, calendars, shop, unlock
    /// moment) never show a currency or item without a use.
    /// </summary>
    public sealed class RewardVisibilityTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private void Unlock(params string[] ids)
        {
            foreach (var id in ids) S.Onboarding.Unlocked[id] = _f.Service.NowMs;
        }

        private static List<string> Texts(Component root) => root.GetComponentsInChildren<Text>(true).Select(t => t.text).ToList();

        /// <summary>Texts of every tab of the current screen (tab buttons found by their labels).</summary>
        private List<string> AllTabTexts(params string[] labels)
        {
            var all = new List<string>();
            foreach (var label in labels)
            {
                var b = _f.App.Router.Current.Root.GetComponentsInChildren<Button>(true).FirstOrDefault(x => x.GetComponentInChildren<Text>()?.text == label);
                Assert.That(b, Is.Not.Null, "tab " + label);
                b.onClick.Invoke();
                all.AddRange(Texts(_f.App.Router.Current.Root));
            }
            return all;
        }

        private void ClearLevel1()
        {
            var level = C.MainLevel(1);
            for (int attempt = 0; attempt < 3 && S.Counter(Requirements.MainLevelCounter) < 1; attempt++)
            {
                Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
                for (int i = 0; i < 500 && S.Run.Active != null; i++) _f.Service.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = true });
            }
            Assert.That(S.Counter(Requirements.MainLevelCounter), Is.EqualTo(1), "level 1 cleared");
        }

        [Test]
        public void CurrencyOrder_CoversEveryMappedCurrency()
        {
            foreach (var u in C.RewardUses)
            {
                if (u.Currency == null || u.Currency.EndsWith("*")) continue; // item kinds, per-entity shards
                Assert.That(HubLayout.CurrencyOrder, Does.Contain(u.Currency), u.Currency + " has no place in the currency list");
            }
        }

        [Test]
        public void VisibleCurrencies_FollowTheRulesMapping()
        {
            Assert.That(HubLayout.VisibleCurrencies(S, C), Is.EqualTo(new[] { Currencies.Stamina, Currencies.Diamonds }), "brand-new account");
            Unlock(Features.Parts);
            Assert.That(HubLayout.VisibleCurrencies(S, C), Is.EqualTo(new[] { Currencies.Stamina, Currencies.Diamonds, Currencies.Ore, Currencies.PartTokens }), "Part Signals come with Parts");
            S.Counters["part_rarity_max"] = (int)PartRarity.Uncommon; // a part that can pass level 10: Gear Cores have a use
            Assert.That(HubLayout.VisibleCurrencies(S, C), Does.Contain(Currencies.GearCores));
            Unlock(Features.Laboratory, Features.Mounts);
            var visible = HubLayout.VisibleCurrencies(S, C);
            Assert.That(visible, Does.Contain(Currencies.Data).And.Contain(Currencies.Fuel).And.Contain(Currencies.MountTickets));
            Assert.That(visible, Does.Not.Contain(Currencies.Biomass).And.Not.Contain(Currencies.Grain).And.Not.Contain(Currencies.HeroXp));
            foreach (var id in HubLayout.CurrencyOrder)
                Assert.That(visible.Contains(id), Is.EqualTo(RewardGate.CurrencyHasUse(S, C, id)), id);
            S.Flags.Add(Features.DevUnlockAllFlag);
            Assert.That(HubLayout.VisibleCurrencies(S, C), Is.EqualTo(HubLayout.CurrencyOrder));
        }

        [Test]
        public void ResourceBar_AndDetails_ListOnlyUsableCurrencies()
        {
            ClearLevel1(); // Parts unlock: Ore arrives with it; Biomass from the run waits in the stash
            _f.App.Bar.Rebuild();
            var chips = _f.Canvas.GetComponentsInChildren<Text>(true).Where(t => t.name.StartsWith("Chip:")).Select(t => t.name.Substring(5)).ToList();
            Assert.That(chips, Does.Contain(Currencies.Ore).And.Not.Contain(Currencies.Biomass).And.Not.Contain(Currencies.Grain));
            _f.App.Bar.ShowDetails();
            var rows = _f.App.ModalLayer.GetComponentsInChildren<RectTransform>(true).Where(t => t.name.StartsWith("Currency:")).Select(t => t.name.Substring(9)).ToList();
            Assert.That(rows, Is.EqualTo(HubLayout.VisibleCurrencies(S, C)));
            Assert.That(rows, Does.Not.Contain(Currencies.Biomass));
            foreach (var kv in S.Wallet.Balances.Where(kv => kv.Value > 0)) Assert.That(rows, Does.Contain(kv.Key), "every balance is listed");
        }

        [Test]
        public void RunResult_FirstRun_ShowsNoBiomass_UnlockMomentShowsTheStashedParts()
        {
            ClearLevel1();
            Assert.That(S.Wallet.Get(Currencies.Biomass), Is.EqualTo(0));
            Assert.That(RewardGate.Stashed(S, Currencies.Biomass), Is.GreaterThan(0));
            _f.App.Router.Push(new RunResultScreen());
            var texts = Texts(_f.App.Router.Current.Root);
            Assert.That(texts.Any(t => t.Contains("Biomass") && t.StartsWith("+")), Is.False, "no Biomass tile: " + string.Join(" | ", texts));
            Assert.That(texts.Any(t => t.Contains("Ore") && t.StartsWith("+")), Is.False, "Ore landed before Parts: it arrives with the unlock moment");

            _f.App.Router.Reset(new HubScreen());
            var hub = (HubScreen)_f.App.Router.Current;
            Assert.That(hub.ShowPendingUnlock(), Is.True);
            var moment = (UnlockScreen)_f.App.Router.Current;
            moment.ShowPage(moment.PageCount - 1); // the reward is on the last intro panel
            var chips = moment.Root.GetComponentsInChildren<Text>(true).Where(t => t.transform.parent != null && t.transform.parent.name == "Reward").Select(t => t.text).ToList();
            Assert.That(chips, Does.Contain("+20 Diamonds"));
            // the starter parts (3) and any part the first run dropped were kept for the Parts unlock
            Assert.That(chips.Any(t => t.EndsWith(" Parts") && int.TryParse(t.Split(' ')[0], out var n) && n >= 3), Is.True, "the starter parts were kept for the Parts unlock: " + string.Join(" | ", chips));
            Assert.That(chips.Any(t => t.StartsWith("+") && t.EndsWith(" Ore")), Is.True, "the Ore of the old starting wallet and the first run");
        }

        [Test]
        public void RewardPreviews_LeaveOutLockedRewards()
        {
            var calendar = C.Retention.LoginCycle.SelectMany(d => d).ToList();
            Assert.That(Fmt.Rewards(calendar, C, S), Does.Not.Match(@"\d Crates?\b").And.Not.Contain("Gear Cores").And.Contain("Diamonds"));
            var chest = C.MainLevel(10).FirstClearRewards;
            Assert.That(Fmt.Rewards(chest, C, S), Does.Contain("Diamonds").And.Not.Match(@"\d (Crate|Signal|Schematic)s?\b"));
            Assert.That(Fmt.Rewards(new[] { Reward.Cur(Currencies.Fuel, 10) }, C, S), Is.EqualTo("—"));
            Unlock(Features.PartPulls);
            Assert.That(Fmt.Rewards(chest, C, S), Does.Match(@"\d Crates?\b").And.Not.Match(@"\d Signals?\b"));

            // Level details: completion rewards of level 1 (Biomass, Ore) for a day-1 account show neither.
            _f.App.Open("portals");
            var root = _f.App.Router.Current.Root;
            root.GetComponentsInChildren<Button>(true).First(b => b.name == "Level:" + C.MainLevelId(10)).onClick.Invoke();
            _f.App.Router.Current.Root.GetComponentsInChildren<Button>(true).First(b => b.name == "LevelDetails").onClick.Invoke();
            var texts = Texts(_f.App.ModalLayer);
            Assert.That(texts.Any(t => t.Contains("Biomass") || System.Text.RegularExpressions.Regex.IsMatch(t, @"\d Signals?\b")), Is.False, string.Join(" | ", texts));
        }

        [Test]
        public void Quests_LoginCalendar_HidesLockedCurrencies()
        {
            Unlock(Features.Parts);
            _f.App.Open("quests");
            var all = AllTabTexts("Login", "Newbie");
            Assert.That(all.Any(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"\d (Schematic|Signal)s?\b") || t.Contains("Data")), Is.False, string.Join(" | ", all.Where(t => t.Contains("Day"))));
        }

        [Test]
        public void Shop_HidesOffersAndItemsForLockedFeatures()
        {
            Unlock(Features.Parts, Features.PartPulls, Features.Shop);
            _f.App.Open("shop");
            var all = AllTabTexts("Offers", "Diamonds", "Items", "Ads");
            _f.App.Open("passes"); // the Passes page (Shop → "Passes"): daily gift, passes, Forever products, season pass, funds
            Assert.That(_f.App.Router.Current, Is.InstanceOf<PassesScreen>());
            all.AddRange(Texts(_f.App.Router.Current.Root));
            Assert.That(all.Any(t => t.Contains("Fuel") || t.Contains("Hero XP") || t.Contains("Grain") || t.Contains("Arena Tickets")), Is.False,
                string.Join(" | ", all.Where(t => t.Contains("Fuel") || t.Contains("Hero XP") || t.Contains("Grain") || t.Contains("Arena Tickets"))));
            Assert.That(all.Any(t => t.Contains("Diamonds")), Is.True, "Diamond packs stay");
        }
    }
}
