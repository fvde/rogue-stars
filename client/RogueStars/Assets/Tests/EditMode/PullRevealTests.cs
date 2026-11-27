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
    /// <summary>SG1: the shared pull reveal (summons, Signals, Shard Reactor, chests).</summary>
    public sealed class PullRevealTests
    {
        private AppFixture _f;

        [SetUp] public void SetUp() { _f = new AppFixture(); PullReveal.AnimateInEditMode = false; SummonEventScreen.SkipAnimation = false; }
        [TearDown] public void TearDown() { PullReveal.AnimateInEditMode = false; SummonEventScreen.SkipAnimation = false; _f.Dispose(); }

        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        private void UnlockAll()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.Service.Refresh(_ => { });
        }

        private static RevealItem Item(Rarity r, string id) => new RevealItem { Kind = RewardKinds.Part, Id = id, Name = id, Tier = r };

        private static List<RevealItem> Mixed() => new List<RevealItem>
        {
            Item(Rarity.Mythic, "m"), Item(Rarity.Common, "c1"), Item(Rarity.Legendary, "l"), Item(Rarity.Epic, "e"), Item(Rarity.Common, "c2"), Item(Rarity.Rare, "r"),
        };

        [Test]
        public void FlipOrder_LowRaritiesFirst_HeldLegendaryThenMythicLast()
        {
            var items = Mixed();
            var order = PullReveal.FlipOrder(items).Select(i => items[i].Id).ToList();
            Assert.That(order, Is.EqualTo(new[] { "c1", "c2", "r", "e", "l", "m" }));
            Assert.That(PullReveal.Held(Rarity.Legendary) && PullReveal.Held(Rarity.Mythic) && !PullReveal.Held(Rarity.Epic), Is.True);
            Assert.That(PullReveal.CrackStrength(Rarity.Mythic), Is.GreaterThan(PullReveal.CrackStrength(Rarity.Legendary)));
            Assert.That(PullReveal.CrackStrength(Rarity.Epic), Is.EqualTo(0));
            Assert.That(PullReveal.BannerText(Rarity.Mythic), Is.EqualTo("MYTHIC!"));
            Assert.That(PullReveal.BannerText(Rarity.Legendary), Is.EqualTo("LEGENDARY!"));
            Assert.That(PullReveal.BannerText(Rarity.Epic), Is.Null);
            Assert.That(PullReveal.BestItem(items).Id, Is.EqualTo("m"));
        }

        [Test]
        public void Animation_ChargesCracksDealsAndHolds_ThenTheBannerAfterTheBestCard()
        {
            PullReveal.AnimateInEditMode = true;
            var items = Mixed();
            var modal = PullReveal.Show(_f.App.ModalLayer, items, new RevealOptions { Title = "Test" });
            var player = modal.Root.GetComponent<PullRevealPlayer>();
            Assert.That(player.Finished, Is.False);
            Assert.That(player.Phase, Is.EqualTo("charge"));
            Assert.That(player.Cards.All(c => !c.gameObject.activeSelf), Is.True, "cards wait for the portal");
            Assert.That(modal.Root.transform.Find("Stage/Portal").gameObject.activeSelf, Is.True);

            bool sawCrack = false, sawHold = false, heldFaceDownWhileOthersOpen = false;
            for (int k = 0; k < 400 && !player.Finished; k++)
            {
                player.Advance(0.05f);
                sawCrack |= player.Phase == "crack";
                if (player.Phase == "hold")
                {
                    sawHold = true;
                    var legendary = player.Cards[2];
                    if (!player.FlipLog.Contains(2) && player.FlipLog.Contains(3)) heldFaceDownWhileOthersOpen |= legendary.Find("Back").gameObject.activeSelf;
                }
                if (player.FlipLog.Count < items.Count) Assert.That(player.BannerShown, Is.False, "no banner before the best card");
            }
            Assert.That(player.Finished, Is.True);
            Assert.That(sawCrack && sawHold && heldFaceDownWhileOthersOpen, Is.True);
            Assert.That(player.FlipLog.Select(i => items[i].Id), Is.EqualTo(new[] { "c1", "c2", "r", "e", "l", "m" }));
            Assert.That(player.BannerShown, Is.True);
            var banner = modal.Root.transform.Find(RevealBanner.Name);
            Assert.That(banner.GetComponentsInChildren<Text>(true).First(t => t.name == "BannerText").text, Is.EqualTo("MYTHIC!"));
        }

        [Test]
        public void Skip_JumpsToTheEndState_BannerStillShows()
        {
            PullReveal.AnimateInEditMode = true;
            var items = Mixed();
            var modal = PullReveal.Show(_f.App.ModalLayer, items, new RevealOptions { Title = "Test" });
            var player = modal.Root.GetComponent<PullRevealPlayer>();
            player.Advance(0.3f);
            Assert.That(player.Finished, Is.False);
            modal.Root.GetComponent<Button>().onClick.Invoke(); // tap anywhere
            Assert.That(player.Finished, Is.True);
            foreach (var c in player.Cards)
            {
                Assert.That(c.gameObject.activeSelf, Is.True);
                Assert.That(c.Find("Front").gameObject.activeSelf, Is.True);
                Assert.That(c.Find("Back").gameObject.activeSelf, Is.False);
            }
            Assert.That(modal.Root.transform.Find("Stage/Portal").gameObject.activeSelf, Is.False);
            Assert.That(player.BannerShown, Is.True, "a skip never hides a Mythic");
            Assert.That(modal.Root.transform.Find("Buttons"), Is.Not.Null);
        }

        [Test]
        public void EditMode_OpensInTheEndState_CommonsHaveNoBanner()
        {
            var items = new List<RevealItem> { Item(Rarity.Common, "a"), Item(Rarity.Rare, "b") };
            var modal = PullReveal.Show(_f.App.ModalLayer, items, new RevealOptions());
            var player = modal.Root.GetComponent<PullRevealPlayer>();
            Assert.That(player.Finished, Is.True);
            Assert.That(player.BannerShown, Is.False);
            Assert.That(modal.Root.transform.Find(RevealBanner.Name), Is.Null);
        }

        [Test]
        public void SignalCards_NameOnce_NewRibbon_ShardBadge_AndPullAgainButtons()
        {
            UnlockAll();
            _f.App.Open("signals");
            var screen = (ShopScreen)_f.App.Router.Current;
            var part = C.PartsConfig.Defs.Values.First();
            var result = new GachaPullResult { Type = GachaType.Alien };
            result.Items.Add(new GachaPullItem { Kind = RewardKinds.Alien, Id = "alien_blorp", Amount = 1, Tier = Rarity.Common, Shards = 50, Note = "new" });
            result.Items.Add(new GachaPullItem { Kind = RewardKinds.Alien, Id = "alien_nebulisk", Amount = 1, Tier = Rarity.Mythic, Shards = 50, Note = "shards" });
            result.Items.Add(new GachaPullItem { Kind = RewardKinds.Part, Id = part.Id, Amount = 1, Rarity = 0, Tier = Rarity.Common });
            var modal = screen.Reveal(result, GachaType.Alien, null);
            var cards = modal.Body.Cast<Transform>().Where(t => t.name.StartsWith("Card:")).ToList();
            Assert.That(cards.Count, Is.EqualTo(3));

            var blorp = cards[0];
            Assert.That(blorp.Find("Front/NewRibbon"), Is.Not.Null, "NEW is a ribbon");
            Assert.That(cards[1].Find("Front/NewRibbon"), Is.Null);
            Assert.That(cards[1].GetComponentsInChildren<Text>(true).Any(t => t.name == "BadgeText" && t.text == "+50 shards"), Is.True);

            // the name once, without the rarity word
            var partName = cards[2].GetComponentsInChildren<Text>(true).First(t => t.name == "Name").text;
            Assert.That(partName, Is.EqualTo(part.Name));
            var all = cards[2].GetComponentsInChildren<Text>(true).Select(t => t.text).ToList();
            Assert.That(all.Count(t => t.Contains(part.Name)), Is.EqualTo(1));
            Assert.That(all.Any(t => t.Contains("Common")), Is.False);
            Assert.That(cards[2].Find("Front/ArtBox/Glyph"), Is.Not.Null, "parts have no art yet: a rarity glyph");

            Assert.That(modal.Root.GetComponentsInChildren<Button>(true).Any(b => b.name == "Again:1"), Is.True);
            Assert.That(modal.Root.GetComponentsInChildren<Button>(true).Any(b => b.name == "Again:10"), Is.True);
            Assert.That(modal.Root.GetComponentsInChildren<Text>(true).Any(t => t.name == "BannerText" && t.text == "MYTHIC!"), Is.True);
        }

        [Test]
        public void SummonReveal_UsesTheSharedReveal_WithDrawAgain()
        {
            UnlockAll();
            var screen = new SummonEventScreen();
            _f.App.Router.Push(screen);
            var result = new SummonPullResult { Type = SummonEventType.Hero, Luck = 3, LuckMax = 100 };
            result.Items.Add(new SummonPullItem { Kind = RewardKinds.Hero, Id = "hero_chef", Amount = 1, Tier = Rarity.Legendary, Featured = true, Note = "new", Shards = 50 });
            var modal = screen.Reveal(result);
            Assert.That(modal.Root.GetComponent<PullRevealPlayer>(), Is.Not.Null);
            Assert.That(modal.Root.transform.Find("Cards").GetChild(0).Find("Front/NewRibbon"), Is.Not.Null);
            Assert.That(modal.Root.GetComponentsInChildren<Text>(true).Any(t => t.name == "BannerText" && t.text == "LEGENDARY!"), Is.True);
            Assert.That(modal.Root.GetComponentsInChildren<Button>(true).Count(b => b.name.StartsWith("Again:")), Is.EqualTo(2));
        }

        [Test]
        public void SkipAnimationSetting_OpensTheEndState()
        {
            PullReveal.AnimateInEditMode = true;
            var modal = PullReveal.Show(_f.App.ModalLayer, Mixed(), new RevealOptions { Skip = true });
            Assert.That(modal.Root.GetComponent<PullRevealPlayer>().Finished, Is.True);
        }

        [Test]
        public void ReactorOutput_AndChestRewards_UseTheReveal()
        {
            UnlockAll();
            var reactor = new ReactorScreen();
            _f.App.Router.Push(reactor);
            var m = reactor.RevealOutput(ReactorKind.Alien, "alien_nebulisk", 50, true);
            var card = m.Body.GetChild(0);
            Assert.That(card.name, Is.EqualTo("Card:" + RewardKinds.Alien + ":alien_nebulisk"));
            Assert.That(card.Find("Front/NewRibbon"), Is.Not.Null);
            m.Close();

            var items = PullReveal.FromRewards(new[] { Reward.Cur(Currencies.Ore, 2000) }, C);
            Assert.That(items[0].Amount, Is.EqualTo("×2K"));
            Assert.That(items[0].Name, Is.EqualTo("Ore"));
            Assert.That(items[0].Tier, Is.EqualTo(Rarity.Common));
        }

        // ---- Q4: run results and mail claims reveal only Legendary / Mythic items ----------------------------------------

        private Reward PartReward(PartRarity r) => new Reward(RewardKinds.Part, C.PartsConfig.Defs.Values.First().Id, 1, (int)r);

        private bool RevealOpen(string name) => _f.App.ModalLayer.GetComponentsInChildren<PullRevealPlayer>(true).Any(p => p.name == "Modal:" + name);

        [Test]
        public void Earns_OnlyWithALegendaryOrMythicItem()
        {
            Assert.That(PullReveal.Earns(new[] { Reward.Cur(Currencies.Diamonds, 999999), PartReward(PartRarity.Epic) }, C), Is.False);
            Assert.That(PullReveal.Earns(new[] { Reward.Cur(Currencies.Ore, 10), PartReward(PartRarity.Legendary) }, C), Is.True);
            Assert.That(PullReveal.Earns(new[] { PartReward(PartRarity.Mythic) }, C), Is.True);
            Assert.That(PullReveal.Earns(new[] { new Reward(RewardKinds.Alien, "alien_nebulisk", 1) }, C), Is.True, "a Mythic alien");
            Assert.That(PullReveal.Earns(null, C), Is.False);
        }

        [Test]
        public void RunResult_WithALegendaryItem_PlaysTheReveal_Once()
        {
            UnlockAll();
            S.Run.LastSummary = new RunSummary { RunId = "x", LevelId = C.MainLevel(1).Id, Won = true, Rolls = 30 };
            S.Run.LastSummary.Granted.Add(Reward.Cur(Currencies.Diamonds, 10));
            S.Run.LastSummary.Granted.Add(PartReward(PartRarity.Legendary));
            var screen = new RunResultScreen();
            _f.App.Router.Push(screen);
            Assert.That(screen.Reveal, Is.Not.Null, "Legendary loot: the pull reveal");
            Assert.That(RevealOpen("RunReveal"), Is.True);
            var cards = screen.Reveal.Body.Cast<Transform>().Where(t => t.name.StartsWith("Card:")).ToList();
            Assert.That(cards.Count, Is.EqualTo(2), "every reward of the run on a card");
            Assert.That(screen.Root.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Reward"), Is.EqualTo(2), "the strip stays under it");
            screen.Reveal.Close();
            screen.Rebuild();
            Assert.That(RevealOpen("RunReveal"), Is.False, "a rebuild does not replay it");
        }

        [Test]
        public void RunResult_WithoutALegendaryItem_KeepsTheRewardStrip_NoReveal()
        {
            UnlockAll();
            S.Run.LastSummary = new RunSummary { RunId = "x", LevelId = C.MainLevel(1).Id, Won = true, Rolls = 30 };
            S.Run.LastSummary.Granted.Add(Reward.Cur(Currencies.Diamonds, 10));
            S.Run.LastSummary.Granted.Add(PartReward(PartRarity.Epic));
            var screen = new RunResultScreen();
            _f.App.Router.Push(screen);
            Assert.That(screen.Reveal, Is.Null);
            Assert.That(_f.App.ModalLayer.GetComponentsInChildren<PullRevealPlayer>(true), Is.Empty);
            Assert.That(screen.Root.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Reward"), Is.EqualTo(2));
        }

        [Test]
        public void MailClaim_WithAMythicItem_PlaysTheReveal_OtherClaimsDoNot()
        {
            UnlockAll();
            long now = _f.Service.NowMs;
            string plain = MailSystem.Push(S, C, now, "Gift", "Diamonds", new[] { Reward.Cur(Currencies.Diamonds, 50) });
            string rich = MailSystem.Push(S, C, now, "Treasure", "A Mythic part", new[] { Reward.Cur(Currencies.Ore, 100), PartReward(PartRarity.Mythic) });
            _f.App.Open("mail");
            var mail = (MailScreen)_f.App.Router.Current;
            long dia = S.Wallet.Get(Currencies.Diamonds);
            mail.Claim(S.Mail.Inbox.First(m => m.Id == plain));
            Assert.That(S.Wallet.Get(Currencies.Diamonds), Is.EqualTo(dia + 50));
            Assert.That(RevealOpen("MailReveal"), Is.False, "Diamonds only: the usual flyout, no reveal");
            mail.Claim(S.Mail.Inbox.First(m => m.Id == rich));
            Assert.That(S.Mail.Inbox.First(m => m.Id == rich).Claimed, Is.True);
            Assert.That(RevealOpen("MailReveal"), Is.True, "a Mythic part: the pull reveal");
        }

        [Test]
        public void MailClaimAll_RevealsOnlyWhenOneMailHoldsALegendary()
        {
            UnlockAll();
            long now = _f.Service.NowMs;
            MailSystem.Push(S, C, now, "A", "", new[] { Reward.Cur(Currencies.Diamonds, 5) });
            MailSystem.Push(S, C, now, "B", "", new[] { PartReward(PartRarity.Rare) });
            _f.App.Open("mail");
            var mail = (MailScreen)_f.App.Router.Current;
            mail.ClaimAll();
            Assert.That(RevealOpen("MailReveal"), Is.False);
            MailSystem.Push(S, C, now, "C", "", new[] { Reward.Cur(Currencies.Diamonds, 5) });
            MailSystem.Push(S, C, now, "D", "", new[] { PartReward(PartRarity.Legendary) });
            mail.ClaimAll();
            Assert.That(RevealOpen("MailReveal"), Is.True);
        }
    }
}
