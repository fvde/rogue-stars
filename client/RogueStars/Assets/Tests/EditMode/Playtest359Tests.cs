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
    /// Playtest 0.1.359 (client side): the battle bars stand centred under their fighter's art (the worm sat left of its HP
    /// bar), elites without art of their own are drawn with a biome sprite, and the result screen shows the doubled totals
    /// of every listed reward after "double rewards".
    /// </summary>
    public sealed class Playtest359Tests
    {
        private AppFixture _f;
        private ContentDb C => _f.Service.Content;
        private static readonly Sprite Fake = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

        [SetUp] public void SetUp() => _f = new AppFixture();

        [TearDown]
        public void TearDown()
        {
            BattlePlaybackScreen.Instant = false;
            ArtCatalog.Loader = null;
            _f.Dispose();
        }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static float CenterX(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return (c[0].x + c[2].x) / 2;
        }

        [Test]
        public void Battle_EveryFightersBars_AreCentredUnderItsArt()
        {
            ArtCatalog.Loader = p => p == "Art/Hero/hero_worm" || p.StartsWith("Art/Enemies/") ? Fake : null;
            var b = new BattleResult { Winner = BattleSide.Player, Rounds = 1 };
            b.Fighters.Add(new BattleFighterInfo { Index = 0, Name = "Worm", Role = CombatRole.Worm, Side = BattleSide.Player, Targetable = true, MaxHp = 1000, StartHp = 1000, EndHp = 1000, Alive = true });
            b.Fighters.Add(new BattleFighterInfo { Index = 1, Id = "cinder_hound", Name = "Hound", Role = CombatRole.Enemy, Side = BattleSide.Enemy, Targetable = true, MaxHp = 300, StartHp = 300, EndHp = 0 });
            b.Fighters.Add(new BattleFighterInfo { Index = 2, Id = "elite_lava_brute", Name = "Brute", Role = CombatRole.Elite, Side = BattleSide.Enemy, Targetable = true, MaxHp = 900, StartHp = 900, EndHp = 0 });
            b.Log.Add(new BattleLogEntry { Round = 1, Actor = 0, Target = 1, Action = BattleAction.Basic, Amount = 300, TargetHp = 0 });
            var screen = new BattlePlaybackScreen(b);
            _f.App.Router.Push(screen);
            Canvas.ForceUpdateCanvases();
            for (int i = 0; i < 3; i++)
            {
                var fighter = FindNamed(screen.Root, "Fighter:" + i);
                var sprite = (RectTransform)FindNamed(fighter, "Sprite");
                float tol = Mathf.Abs(sprite.rect.width * sprite.lossyScale.x) * 0.02f + 0.001f;
                foreach (var bar in new[] { "HpHolder", "ShieldHolder", "RageHolder" })
                    Assert.That(CenterX((RectTransform)FindNamed(fighter, bar)), Is.EqualTo(CenterX(sprite)).Within(tol), "fighter " + i + " " + bar);
            }
        }

        [Test]
        public void ElitesWithoutArt_AreDrawnWithTheirBiomeSprite()
        {
            // (elite_cinder_alpha has its own art since art import 5; elite_lurker_alpha still borrows snow_lurker's)
            ArtCatalog.Loader = p => p == "Art/Enemies/snow_lurker" ? Fake : null;
            var s = ArtCatalog.EnemyOrFamily("elite_lurker_alpha", C, out bool family);
            Assert.That(s, Is.SameAs(Fake));
            Assert.That(family, Is.True, "drawn at the elite scale");
            foreach (var l in new[] { C.MainLevel(8), C.MainLevel(18), C.MainLevel(28), C.MainLevel(38) })
                foreach (var id in l.ElitePool)
                {
                    var def = C.Enemies[id];
                    Assert.That(def.ArtId == null || C.Enemies.ContainsKey(def.ArtId), Is.True, id);
                }
        }

        [Test]
        public void RunResult_DoubleRewards_ShowsEveryListedRewardDoubled()
        {
            _f.Service.DevGrantStarterKit();
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            var level = C.MainLevel(1);
            for (int attempt = 0; attempt < 6 && !(_f.Service.State.Run.LastSummary?.Won ?? false); attempt++)
            {
                Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
                for (int i = 0; i < 3000 && _f.Service.State.Run.Active != null; i++)
                {
                    Assert.That(_f.Service.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = true }).Ok, Is.True);
                    while (_f.App.Router.Current is BattlePlaybackScreen) _f.App.Router.Pop();
                }
            }
            var sum = _f.Service.State.Run.LastSummary;
            Assert.That(sum.Won, Is.True);
            Assert.That(sum.Rewards.Any(l => l.Source == RunRewardLine.Feat), Is.True, "a first clear pays its feat Diamonds");
            Assert.That(sum.Rewards.All(l => l.Doublable), Is.True, "every listed reward is doublable");
            var before = RunResultScreen.Aggregate(sum.Granted).Where(r => r.Kind == RewardKinds.Currency).ToDictionary(r => r.Id, r => r.Amount);

            _f.App.Router.Reset(new HubScreen());
            _f.App.Router.Push(new RunResultScreen());
            Button btn = null;
            foreach (var x in _f.App.Router.Current.Root.GetComponentsInChildren<Button>(true)) if (x.name == "DoubleRewards") btn = x;
            Assert.That(btn, Is.Not.Null);
            btn.onClick.Invoke();
            sum = _f.Service.State.Run.LastSummary;
            Assert.That(sum.Doubled, Is.True);
            var after = RunResultScreen.Aggregate(sum.Granted).Where(r => r.Kind == RewardKinds.Currency).ToDictionary(r => r.Id, r => r.Amount);
            foreach (var kv in before) Assert.That(after[kv.Key], Is.EqualTo(kv.Value * 2), kv.Key);

            _f.App.Router.Current.Rebuild();
            var amounts = _f.App.Router.Current.Root.GetComponentsInChildren<Text>(true).Where(t => t.name == "Amount").Select(t => t.text).ToList();
            foreach (var r in RunResultScreen.Aggregate(sum.Granted).Where(r => r.Kind == RewardKinds.Currency))
                Assert.That(amounts, Does.Contain("x" + Fmt.Num(r.Amount)), r.Id);
        }

        [Test]
        public void RunResult_SixRewards_AllShowAsTiles_NoMoreLine()
        {
            var sum = new RunSummary { RunId = "x", LevelId = C.MainLevel(1).Id, Won = true, Rolls = 31, Level = 9 };
            foreach (var id in new[] { Currencies.Biomass, Currencies.Ore, Currencies.Diamonds, Currencies.AlienTokens, Currencies.HeroXp, Currencies.GearCores })
                sum.Granted.Add(Reward.Cur(id, 1250));
            _f.Service.State.Run.LastSummary = sum;
            _f.App.Router.Push(new RunResultScreen());
            var root = _f.App.Router.Current.Root;
            Assert.That(root.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Reward"), Is.EqualTo(6));
            Assert.That(FindNamed(root, "MoreRewards"), Is.Null, "every reward on the strip");
        }
    }
}
