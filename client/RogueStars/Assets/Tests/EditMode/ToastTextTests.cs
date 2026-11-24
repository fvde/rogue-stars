using System.Collections.Generic;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Rules.Core;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Owner playtest 0.1.200 ("debug style messages pop up randomly, e.g. when purchasing skills"): toasts only for what
    /// the screen does not show already; never a humanized event id.
    /// </summary>
    public sealed class ToastTextTests
    {
        private AppFixture _f;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private static GameEvent Ev(string type, params string[] args) => new GameEvent { Type = type, Args = new List<string>(args) };

        [Test]
        public void RunSteps_SeenOnTheBoardOrASheet_AreNotToasted()
        {
            var c = _f.Service.Content;
            foreach (var type in new[]
            {
                "run_skill_gained", "run_skill_level", "run_level_up", "run_shop_buy", "run_draft_pick", "run_completed", "run_failed",
                "run_revived", "run_rewards_doubled", "run_miniboss_coming", "run_tile_outcome", "run_signature", "run_milestone",
                "run_rare_drop", "run_outcome", "run_roll", "guild_role",
            })
                Assert.That(EventText.Describe(Ev(type, "gen_power", "2", "3"), c), Is.Null, type);
        }

        [Test]
        public void InformativeEvents_StayShortAndReadable()
        {
            var c = _f.Service.Content;
            Assert.That(EventText.Describe(Ev("run_encounter", "miniboss"), c), Is.EqualTo("A miniboss attacks!"));
            Assert.That(EventText.Describe(Ev("run_encounter", "boss"), c), Is.EqualTo("The boss arena opens!"));
            Assert.That(EventText.Describe(Ev("guild_joined", "g1", "member"), c), Is.EqualTo("Welcome to the guild!"));
            Assert.That(EventText.Describe(Ev("reward", RewardKinds.Currency, Currencies.Diamonds, "30"), c), Does.StartWith("+30"));
        }

        [Test]
        public void EveryEventTheRulesEmit_HasPlayerText_OrNone()
        {
            // a humanized id ("Run tile outcome") is the debug-style popup the owner saw: every known type is mapped
            var c = _f.Service.Content;
            foreach (var type in new[] { "run_tile_outcome", "run_signature", "run_signature_awakened", "run_milestone", "run_rare_drop", "guild_joined", "guild_role" })
            {
                var text = EventText.Describe(Ev(type, "x", "1", "1"), c);
                Assert.That(text == null || text != Fmt.Humanize(type), Is.True, type + " → " + text);
            }
        }

        [Test]
        public void RunEnd_RewardsShowOnTheResultScreen_NotAsToasts()
        {
            var end = new List<GameEvent> { Ev("reward", RewardKinds.Currency, Currencies.Diamonds, "10"), Ev("feature_unlocked", "parts"), Ev("run_completed", "r", "l") };
            var shown = EventText.ForToasts(end);
            Assert.That(shown.Exists(e => e.Type == "reward"), Is.False, "the result strip shows them");
            Assert.That(shown.Exists(e => e.Type == "feature_unlocked"), Is.True, "unlocks still toast");
            var claim = new List<GameEvent> { Ev("reward", RewardKinds.Currency, Currencies.Diamonds, "10") };
            Assert.That(EventText.ForToasts(claim).Count, Is.EqualTo(1), "rewards outside a run's end still toast");
        }

        [Test]
        public void BuyingASkillInTheRunShop_ShowsNoToast()
        {
            var level = _f.Service.Content.MainLevel(1);
            Assert.That(_f.Service.ExecuteNow(new RogueStars.Rules.Systems.RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RogueStars.Rules.Systems.RunMode.Normal }).Ok, Is.True);
            var run = _f.Service.State.Run.Active;
            run.Coins = 100000;
            run.Phase = RogueStars.Rules.Systems.RunPhase.Shop;
            run.Shop.Clear();
            run.Shop.Add(new RogueStars.Rules.Systems.ShopOffer { Kind = RogueStars.Rules.Systems.ShopOfferKind.Skill, SkillId = "gen_power", Price = 10 });
            _f.App.Update(ToastCenter.Lifetime + 1);
            Assert.That(_f.App.Toasts.Count, Is.EqualTo(0));
            bool ok = false;
            _f.App.Session.Run(new RogueStars.Rules.Systems.RunShopBuy { Index = 0 }, r => ok = r.Ok);
            Assert.That(ok, Is.True, _f.App.Session.LastResult?.Error);
            Assert.That(_f.App.Toasts.Count, Is.EqualTo(0), "the card turns Sold and the coins drop: no toast (" + _f.App.Toasts.LastText + ")");
        }
    }
}
