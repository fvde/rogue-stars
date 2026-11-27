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
    /// Pull system screens (docs/design/pull-system.md): the Signals' visible pity counters ("Mythic x/100", "Legendary x/40") and the
    /// 10-pull rule, the summon Luck out of 100, and the Shard Reactor screen (slots, pick source and target, cost, start, collect).
    /// </summary>
    public sealed class PullSystemScreenTests
    {
        private AppFixture _f;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        private void UnlockAll()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            _f.Service.Refresh(_ => { });
        }

        private static Text FindText(Transform root, string name) => root.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == name);
        private static Button Find(Transform root, string name) => root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        [Test]
        public void Signals_ShowTheVisiblePityCounters_InRarityColours_AndTheTenPullRule()
        {
            UnlockAll();
            S.Gacha.For(GachaType.Part).SinceMythic = 37;
            S.Gacha.For(GachaType.Part).SinceLegendary = 12;
            var screen = new ShopScreen(GachaType.Part); // the Shop's Parts section
            _f.App.Router.Push(screen);
            var mythic = FindText(screen.Root.transform, "PityMythic");
            var legendary = FindText(screen.Root.transform, "PityLegendary");
            Assert.That(mythic.text, Is.EqualTo("Mythic 37/100"));
            Assert.That(legendary.text, Is.EqualTo("Legendary 12/40"));
            Assert.That(mythic.color, Is.EqualTo(Theme.RarityColor(Rarity.Mythic)));
            Assert.That(legendary.color, Is.EqualTo(Theme.RarityColor(Rarity.Legendary)));
            var rule = FindText(screen.Root.transform, "PityRule").text;
            StringAssert.Contains("at least one Epic", rule);
            StringAssert.Contains("never changes", rule);

            // A pull moves the counters on the rebuilt screen.
            S.Wallet.Add(Currencies.Diamonds, 10_000);
            var r = _f.Service.ExecuteNow(new GachaPull { PullType = GachaType.Part, Count = 1, Payment = GachaPayment.Diamonds });
            Assert.That(r.Ok, Is.True, r.Error);
            var res = (GachaPullResult)r.Payload;
            screen.Rebuild();
            Assert.That(FindText(screen.Root.transform, "PityMythic").text, Is.EqualTo("Mythic " + res.Pity.Mythic + "/100"));
            Assert.That(res.Pity.Mythic, Is.EqualTo(res.Items[0].Tier == Rarity.Mythic ? 0 : 38));
        }

        [Test]
        public void SignalReveal_ShowsBatchesOf50()
        {
            UnlockAll();
            _f.App.Open("signals"); // the former pull screen's id: an alias of the Shop
            var screen = (ShopScreen)_f.App.Router.Current;
            var result = new GachaPullResult { Type = GachaType.Alien };
            result.Items.Add(new GachaPullItem { Kind = RewardKinds.Alien, Id = "alien_blorp", Amount = 1, Tier = Rarity.Common, Shards = 50, Note = "new" });
            result.Items.Add(new GachaPullItem { Kind = RewardKinds.Alien, Id = "alien_nebulisk", Amount = 1, Tier = Rarity.Mythic, Shards = 50, Note = "shards" });
            var modal = screen.Reveal(result);
            var texts = modal.Root.GetComponentsInChildren<Text>(true).Select(t => t.text).ToList();
            Assert.That(texts.Any(t => t.Contains("NEW")), Is.True);
            Assert.That(texts.Any(t => t.Contains("+50 shards")), Is.True);
        }

        [Test]
        public void SummonLuck_IsOutOf100()
        {
            UnlockAll();
            var screen = new SummonEventScreen();
            _f.App.Router.Push(screen);
            Assert.That(FindText(screen.Root.transform, "Luck").text, Does.Contain("/100"));
            StringAssert.Contains("1.2%", FindText(screen.Root.transform, "LuckRule").text);
        }

        /// <summary>A built, staffed Shard Reactor with spare Blorp shards.</summary>
        private void ReactorReady(int workers = 1)
        {
            UnlockAll();
            _f.Service.DevGrant(new[] { new Reward(RewardKinds.Blueprint, ContentDb.ShardReactorId, 1), new Reward(RewardKinds.Alien, "alien_blorp", 1),
                new Reward(RewardKinds.Alien, "alien_mossling", 1), Reward.Cur(Currencies.Grain, 50_000), Reward.Cur(Currencies.Diamonds, 5_000) });
            var b = new CityBuilding { Uid = ContentDb.ShardReactorId + "#1", BlueprintId = ContentDb.ShardReactorId, Placed = true, Level = 1, X = 11, Y = 8 };
            S.City.Buildings.Add(b);
            if (workers > 0) AlienSystem.Owned(S, "alien_mossling").WorkBuildingId = b.Uid;
            S.Wallet.Add(Currencies.AlienShards("alien_blorp"), 6_000);
            _f.Service.Refresh(_ => { });
        }

        [Test]
        public void Reactor_NotOwned_ExplainsWhereItComesFrom()
        {
            UnlockAll();
            var screen = new ReactorScreen();
            _f.App.Router.Push(screen);
            Assert.That(FindText(screen.Root.transform, "NotOwned"), Is.Not.Null);
        }

        [Test]
        public void Reactor_NoWorker_NoJobSlot()
        {
            ReactorReady(workers: 0);
            var screen = new ReactorScreen();
            _f.App.Router.Push(screen);
            Assert.That(FindText(screen.Root.transform, "JobCount").text, Is.EqualTo("Jobs 0/0"));
            Assert.That(Find(screen.Root.transform, "ManageAliens"), Is.Not.Null);
        }

        [Test]
        public void Reactor_PickSourceAndTarget_ShowsTheCost_StartsAJob_AndCollects()
        {
            ReactorReady();
            var screen = new ReactorScreen();
            _f.App.Router.Push(screen);
            Assert.That(FindText(screen.Root.transform, "JobCount").text, Is.EqualTo("Jobs 0/1"));
            Find(screen.Root.transform, "Source:alien_blorp").onClick.Invoke();
            // targets: aliens of a higher rarity only (Blorp is Common)
            var targets = screen.Root.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Target:")).Select(b => b.name.Substring(7)).ToList();
            Assert.That(targets, Is.Not.Empty);
            Assert.That(targets.All(id => C.AlienType(id).Rarity > Rarity.Common), Is.True);
            Assert.That(targets, Does.Contain("alien_nebulisk"));
            Find(screen.Root.transform, "Target:alien_nebulisk").onClick.Invoke();
            var quote = screen.Root.GetComponentsInChildren<Transform>(true).First(t => t.name == "Quote");
            var lines = quote.GetComponentsInChildren<Text>(true).Select(t => t.text).ToList();
            Assert.That(lines.Any(t => t.Contains("2560") || t.Contains("2,560")), Is.True, string.Join(" | ", lines));
            Assert.That(lines.Any(t => t.Contains("Grain")), Is.True);
            Assert.That(lines.Any(t => t.Contains("10 Nebulisk shards")), Is.True);
            long grain = S.Wallet.Get(Currencies.Grain);
            Find(screen.Root.transform, "Start").onClick.Invoke();
            Assert.That(S.Reactor.Jobs.Count, Is.EqualTo(1));
            Assert.That(S.Wallet.Get(Currencies.Grain), Is.EqualTo(grain - C.ShardReactor.AlienFuelPerUnit[(int)Rarity.Mythic]));
            screen.Rebuild();
            Assert.That(FindText(screen.Root.transform, "JobCount").text, Is.EqualTo("Jobs 1/1"));
            var job = screen.Root.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith("Job:"));
            Assert.That(Find(job, "Collect").interactable, Is.False);
            // finish the timer with Diamonds, then collect
            var r = _f.Service.ExecuteNow(new TimerSpeedUp { TimerId = S.Reactor.Jobs[0].TimerId, Method = SpeedUpMethod.Diamonds });
            Assert.That(r.Ok, Is.True, r.Error);
            screen.Rebuild();
            job = screen.Root.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith("Job:"));
            Find(job, "Collect").onClick.Invoke();
            Assert.That(S.Wallet.Get(Currencies.AlienShards("alien_nebulisk")), Is.EqualTo(10));
            Assert.That(S.Reactor.Jobs, Is.Empty);
        }

        [Test]
        public void City_ReactorBuilding_OpensItsScreen()
        {
            ReactorReady();
            var city = new CityScreen();
            _f.App.Router.Push(city);
            city.Select(ContentDb.ShardReactorId + "#1");
            Find(city.Root.transform, "OpenReactor").onClick.Invoke();
            Assert.That(_f.App.Router.Current, Is.InstanceOf<ReactorScreen>());
        }
    }
}
