using System.Linq;
using NUnit.Framework;
using RogueStars.Client.Screens;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>Hero and mount kits on screen: the hero / mount cards show the kit per star (and the next change), the battle
    /// shows mount shots, hero abilities and the kit HUD ("Starfall 2/4", "Scan Pulse in 2").</summary>
    public sealed class KitScreenTests
    {
        private AppFixture _f;
        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;

        private static Text Text(Transform root, string name) => root.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == name);

        [Test]
        public void HeroesScreen_ShowsTheKitAtItsStars_AndTheNextChange()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            S.Heroes.Owned["hero_star_knight"] = new HeroEntry { Stars = 5 };
            S.Heroes.Equipped = "hero_star_knight";
            _f.Service.Refresh(_ => { });
            var screen = new HeroesScreen();
            _f.App.Router.Push(screen);
            var card = screen.Root.GetComponentsInChildren<Transform>(true).First(t => t.name == "Hero:hero_star_knight");
            Assert.That(Text(card, "Kit:Trait:1★").text, Does.Contain("Starguard"));
            Assert.That(Text(card, "Kit:Clause:5★").text, Does.Contain("Starlight"));
            Assert.That(Text(card, "Kit:Clause:7★"), Is.Null);
            Assert.That(Text(card, "KitNext:Clause").text, Does.Contain("Next 7★").And.Contain("Guiding Star"));
            // a hero not owned shows its 1★ kit and the 3★ number to come
            var chef = screen.Root.GetComponentsInChildren<Transform>(true).First(t => t.name == "Hero:hero_chef");
            Assert.That(Text(chef, "Kit:Ability:1★").text, Does.Contain("Feast"));
            Assert.That(Text(chef, "KitNext:Number").text, Does.Contain("every 4th → 3rd round"));
        }

        [Test]
        public void MountsScreen_ShowsPassiveAndActive_ReloadAndTheNextStep()
        {
            _f.Service.DevSetFlag(Features.DevUnlockAllFlag, true);
            S.Mounts.Owned["mount_star_lancer"] = new MountEntry { Stars = 5, Awaken = 3, Level = 11 };
            _f.Service.Refresh(_ => { });
            var screen = new MountsScreen();
            _f.App.Router.Push(screen);
            var card = screen.Root.GetComponentsInChildren<Transform>(true).First(t => t.name == "Mount:mount_star_lancer");
            Assert.That(Text(card, "Kit:Passive:A3").text, Does.Contain("+30%")); // Lance Sight A3 (kit tuning)
            Assert.That(Text(card, "Kit:Active:A3").text, Does.Contain("Every 3 rounds: 140% ATK"));
            Assert.That(Text(card, "KitNext:Active").text, Does.Contain("Next A5"));
            Assert.That(card.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("Reload 3") && t.text.Contains("+30% dmg")), Is.True); // 2★ 4★ A1 A2 + 10 levels
        }

        [Test]
        public void Playback_ShowsMountShots_HeroAbilities_AndTheKitHud()
        {
            var c = C;
            var lo = new CombatLoadout { DisplayName = "Worm", MountId = "mount_scout_saucer" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 6000)).Add(new StatMod(StatId.Atk, 100)).Add(new StatMod(StatId.Speed, 100));
            lo.MetaSkillIds.AddRange(HeroSystem.TraitSkills(c, "hero_star_knight", 1));
            lo.MetaSkillIds.AddRange(MountSystem.WeaponSkills(c, "mount_scout_saucer", 1, 0));
            var setup = new BattleSetup { RoundCap = 6 };
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, new[] { "sig_fire" }));
            for (int i = 0; i < 3; i++) setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "cinder_hound", 2, 0, 2000));
            var battle = BattleSimulator.Simulate(setup, c, new Rng(3UL));
            Assert.That(battle.Log.Exists(e => e.Action == BattleAction.Ability), Is.True);
            var screen = new BattlePlaybackScreen(battle);
            _f.App.Router.Push(screen);
            screen.PlayAll();
            Assert.That(screen.Volleys, Is.EqualTo(battle.Log.Count(e => e.Action == BattleAction.Volley)));
            Assert.That(screen.Abilities, Is.GreaterThan(0));
            Assert.That(screen.KitHud, Has.Some.EqualTo("Starfall 2/4"));
            Assert.That(screen.KitHud, Has.Some.EqualTo("Starfall!"));
            Assert.That(screen.KitHud, Has.Some.StartsWith("Scan Pulse in "));
            Assert.That(screen.AbilityFloats, Has.Some.EqualTo("SCAN PULSE"));
        }
    }
}
