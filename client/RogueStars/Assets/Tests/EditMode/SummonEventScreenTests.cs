using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.Services;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>Summon event screens (hub, draw result, packs, tasks, exchange, pass) and their hub entry.</summary>
    public sealed class SummonEventScreenTests
    {
        private AppFixture _f;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() { SummonEventScreen.SkipAnimation = false; _f.Dispose(); }

        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;
        private long Now => _f.Service.NowMs;

        private void UnlockAll()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.Service.Refresh(_ => { });
        }

        private T Open<T>(T screen) where T : UiScreen
        {
            _f.App.Router.Push(screen);
            return screen;
        }

        private Button Find(Transform root, string name) =>
            root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        private Text FindText(Transform root, string name) =>
            root.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == name);

        [Test]
        public void HubEntry_AppearsWithHeroes_InTheLeftColumn_NotUnderMore()
        {
            Assert.That(HubLayout.LeftColumn(S, C).Any(e => e.Id == "summon"), Is.False);
            UnlockAll();
            Assert.That(HubLayout.LeftColumn(S, C).Any(e => e.Id == "summon" && e.ScreenId == "summon"), Is.True);
            Assert.That(HubLayout.MoreScreens(S, C).Any(e => e.Id == "summon"), Is.False);
            Assert.That(HubLayout.Reachable(S, C, Now), Does.Contain("summon"));
            Assert.That(ScreenCatalog.Find("summon"), Is.Not.Null);
        }

        [Test]
        public void Hub_ShowsFeaturedLuckMilestonesSideColumnAndDraws()
        {
            UnlockAll();
            var screen = Open(new SummonEventScreen());
            var ev = SummonEventSystem.CurrentEvent(S, C, SummonEventType.Hero, Now);
            var root = screen.Root;
            Assert.That(FindText(root, "Title").text, Is.EqualTo(SummonView.FeaturedName(C, SummonEventType.Hero, ev.FeaturedId)));
            Assert.That(FindText(root, "Luck").text, Does.Contain("/" + SummonEventSystem.LuckMax(C, SummonEventType.Hero)));
            Assert.That(FindText(root, "LuckRule").text, Does.Contain("Luck is kept until the next hero summon event"));
            StringAssert.StartsWith("Time left", FindText(root, "TimeLeft").text);
            foreach (var side in new[] { "Exchange", "Packs", "Pass", "Tasks" }) Assert.That(Find(root, "Side:" + side), Is.Not.Null, side);
            int milestones = root.GetComponentsInChildren<RectTransform>(true).Count(t => t.name.StartsWith("Milestone:"));
            Assert.That(milestones, Is.EqualTo(C.SummonType(SummonEventType.Hero).Milestones.Count));
            Assert.That(Find(root, "Draw:1"), Is.Not.Null);
            Assert.That(Find(root, "Draw:10"), Is.Not.Null);
            // Both types once mounts are unlocked: the tabs switch.
            Assert.That(Find(root, "Tab:Mount"), Is.Not.Null);
            Find(root, "Tab:Mount").onClick.Invoke();
            Assert.That(screen.Type, Is.EqualTo(SummonEventType.Mount));
            Assert.That(FindText(root, "LuckRule").text, Does.Contain("mount"));
        }

        [Test]
        public void Draws_PayTicketsFirst_ThenDiamonds_AndRevealEveryResult()
        {
            UnlockAll();
            var def = C.SummonType(SummonEventType.Hero);
            _f.Service.DevGrant(new[] { Reward.Cur(def.TicketCurrency, 10) });
            var screen = Open(new SummonEventScreen());
            Assert.That(FindText(Find(screen.Root, "Draw:10").transform, "Count").text, Is.EqualTo("10 tickets"), "the draw shows its cost (SM2), not have/need");
            SummonEventScreen.SkipAnimation = true;
            Find(screen.Root, "Draw:10").onClick.Invoke();
            Assert.That(S.Wallet.Get(def.TicketCurrency), Is.EqualTo(0));
            Assert.That(SummonEventSystem.Progress(S, C, SummonEventType.Hero, Now).Pulls, Is.EqualTo(10));
            var modal = _f.App.ModalLayer.Find("Modal:SummonResult");
            Assert.That(modal, Is.Not.Null);
            Assert.That(modal.GetComponentsInChildren<RectTransform>(true).Count(t => t.name.StartsWith("Card:")), Is.EqualTo(10));
            Assert.That(FindText(modal, "LuckAfter").text, Does.Contain("Luck"));
            // Out of tickets: the draw shows its Diamond price.
            screen.Rebuild();
            Assert.That(FindText(Find(screen.Root, "Draw:1").transform, "Count").text, Is.EqualTo(Fmt.Num(def.DiamondCost1)));
        }

        [Test]
        public void Reveal_MarksBatches_NewAndShards()
        {
            UnlockAll();
            var screen = Open(new SummonEventScreen());
            var result = new SummonPullResult { Type = SummonEventType.Hero, Luck = 0, LuckMax = 100, LuckTriggered = true };
            result.Items.Add(new SummonPullItem { Kind = RewardKinds.Hero, Id = "hero_chef", Amount = 1, Tier = Rarity.Mythic, Featured = true, Note = "shards", Shards = 50, Stars = 4 });
            result.Items.Add(new SummonPullItem { Kind = RewardKinds.Currency, Id = Currencies.HeroXp, Amount = 300, Tier = Rarity.Common });
            var modal = screen.Reveal(result);
            var badges = modal.Root.GetComponentsInChildren<Text>(true).Where(t => t.name == "BadgeText").Select(t => t.text).ToList();
            Assert.That(badges, Does.Contain("+50 SHARDS"));
            Assert.That(modal.Root.GetComponentsInChildren<Text>(true).Any(t => t.name == "Amount" && t.text == "×50"), Is.True);
            Assert.That(FindText(modal.Root.transform, "LuckAfter").text, Does.Contain("guaranteed"));
            Assert.That(SummonView.CopyBadge(new SummonPullItem { Kind = RewardKinds.Hero, Note = "new" }, C), Is.EqualTo("NEW"));
            StringAssert.Contains("Star Sigils", SummonView.CopyBadge(new SummonPullItem { Kind = RewardKinds.Hero, Note = "overflow" }, C));
        }

        [Test]
        public void Packs_AdTicketIsFreeWhileAdsAreDisabled_PaidPacksAreComingSoonOutsideTheOfferedSet()
        {
            _f.Service.AdsDisabled = true;
            UnlockAll();
            var def = C.SummonType(SummonEventType.Hero);
            _f.App.Session.ProductSet = StoreCatalog.SetInitial; // the summon products are not in the first Play set
            var screen = Open(new SummonEventScreen());
            var sheet = screen.PacksSheet();
            foreach (var id in def.PackProductIds) Assert.That(Find(sheet.Root.transform, "ComingSoon:" + id), Is.Not.Null, id);
            var ad = Find(sheet.Root.transform, "Pack:" + def.AdPlacementId);
            Assert.That(FindText(ad.transform, "Caption").text, Is.EqualTo("Free"));
            ad.onClick.Invoke();
            Assert.That(S.Wallet.Get(def.TicketCurrency), Is.EqualTo(1));
            // Offline with every product offered: price buttons (dev receipts).
            Ui.Clear(_f.App.ModalLayer);
            _f.App.Session.ProductSet = StoreCatalog.SetAll;
            sheet = screen.PacksSheet();
            Assert.That(Find(sheet.Root.transform, "Pack:" + def.PackProductIds[0]), Is.Not.Null);
        }

        [Test]
        public void Tasks_ClaimReachedTargets_GoOpensTheGame()
        {
            UnlockAll();
            var def = C.SummonType(SummonEventType.Hero);
            var screen = Open(new SummonEventScreen());
            S.Counters["run_enemies_defeated"] = S.Counter("run_enemies_defeated") + 200; // 50 and 150 reached
            _f.Service.Refresh(_ => { });
            Assert.That(SummonView.TaskBadge(S, C, SummonEventType.Hero, Now), Is.EqualTo(2));
            var sheet = screen.TasksSheet();
            Find(sheet.Root.transform, "Claim:enemies").onClick.Invoke();
            Assert.That(S.Wallet.Get(def.TicketCurrency), Is.EqualTo(2));
            var tasks = _f.App.ModalLayer.Find("Modal:Tasks");
            Assert.That(Find(tasks, "Go:levels"), Is.Not.Null);
            Assert.That(SummonView.GoScreen(def.Tasks.First(t => t.Id == "levels")), Is.EqualTo("portals"));
            Assert.That(SummonView.GoScreen(def.Tasks.First(t => t.Id == "alien_pulls")), Is.EqualTo("signals"));
        }

        [Test]
        public void Exchange_BuysWithTokens_ShowsEventLimits()
        {
            UnlockAll();
            var def = C.SummonType(SummonEventType.Mount);
            _f.Service.DevGrant(new[] { Reward.Cur(def.TokenCurrency, 1000) });
            var screen = Open(new SummonExchangeScreen(SummonEventType.Mount));
            var item = def.Exchange.First(x => x.Id == "mount_shards");
            Assert.That(FindText(screen.Root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "Item:" + item.Id), "Limit").text,
                Is.EqualTo("Event limit 0/" + item.EventLimit));
            Find(screen.Root, "Buy:" + item.Id).onClick.Invoke();
            Assert.That(S.Wallet.Get(def.TokenCurrency), Is.EqualTo(1000 - item.Price));
            string featured = SummonEventSystem.CurrentEvent(S, C, SummonEventType.Mount, Now).FeaturedId;
            Assert.That(S.Wallet.Get(Currencies.MountShards(featured)), Is.EqualTo(item.Reward.Amount));
        }

        [Test]
        public void Pass_ClaimAllTakesTheUnlockedFreeDay_PremiumIsComingSoonWhenNotOffered()
        {
            UnlockAll();
            _f.App.Session.ProductSet = StoreCatalog.SetInitial;
            var def = C.SummonType(SummonEventType.Hero);
            var screen = Open(new SummonPassScreen(SummonEventType.Hero));
            Assert.That(Find(screen.Root, "ComingSoon:" + def.PassProductId), Is.Not.Null);
            int days = SummonEventSystem.PassDaysUnlocked(S, C, SummonEventType.Hero, Now);
            Assert.That(days, Is.GreaterThanOrEqualTo(1));
            Find(screen.Root, "ClaimAll").onClick.Invoke();
            long expected = def.Pass.Take(days).Sum(d => d.Free[0].Amount);
            Assert.That(S.Wallet.Get(def.TicketCurrency), Is.EqualTo(expected));
        }

        [Test]
        public void Mounts_UnlockWithShards_AndLinkToTheEvents()
        {
            UnlockAll();
            const string id = "mount_frost_frigate";
            _f.Service.DevGrant(new[] { Reward.Cur(Currencies.MountShards(id), C.ShardStars.UnlockShards) });
            var screen = Open(new MountsScreen());
            Find(screen.Root, "Unlock:" + id).onClick.Invoke();
            Assert.That(MountSystem.Get(S, id), Is.Not.Null);
            Find(screen.Root, "OpenSummon").onClick.Invoke();
            Assert.That(_f.App.Router.Current, Is.InstanceOf<SummonEventScreen>());
            Assert.That(((SummonEventScreen)_f.App.Router.Current).Type, Is.EqualTo(SummonEventType.Mount));
        }

        [Test]
        public void Art_EveryKeyHasACodeDrawnFallback()
        {
            foreach (var id in SummonArt.IconIds) Assert.That(SummonArt.Icon(id), Is.Not.Null, id);
            Assert.That(SummonArt.Backdrop(SummonEventType.Hero), Is.Not.Null);
            Assert.That(SummonArt.Backdrop(SummonEventType.Mount), Is.Not.Null);
            foreach (var h in C.HeroConfig.Defs.Keys) Assert.That(SummonArt.Featured(SummonEventType.Hero, h), Is.Not.Null, h);
            foreach (var m in C.MountConfig.Defs.Keys) Assert.That(SummonArt.Featured(SummonEventType.Mount, m), Is.Not.Null, m);
        }
    }
}
