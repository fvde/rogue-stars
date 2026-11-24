using NUnit.Framework;
using RogueStars.Client.Screens;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>Sidekick pass: the battle playback shows sidekick turns, guards and blocks; the alien screen shows ability, power and frequency.</summary>
    public sealed class SidekickUiTests
    {
        private AppFixture _f;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        private static CombatAlien Alien(ContentDb c, string id)
        {
            var def = c.AlienType(id);
            var a = new CombatAlien { AlienTypeId = id, Name = def.Name };
            a.Stats.Add(new StatMod(StatId.Hp, def.BaseHp)).Add(new StatMod(StatId.Atk, def.BaseAtk)).Add(new StatMod(StatId.Def, def.BaseDef))
                .Add(new StatMod(StatId.Speed, def.BaseSpeed));
            a.SkillIds.Add(def.ArchetypeSkills[0].SkillId);
            return a;
        }

        public static BattleResult SidekickBattle(ContentDb c, ulong seed = 3, int rounds = 8)
        {
            var lo = new CombatLoadout { DisplayName = "Worm", MountId = "mount_hive_carrier" };
            lo.WormStats.Add(new StatMod(StatId.Hp, 6000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Def, 10)).Add(new StatMod(StatId.Speed, 100));
            lo.MountStats.Add(new StatMod(StatId.Atk, 40)).Add(new StatMod(StatId.Hp, 10));
            lo.MetaSkillIds.Add("mount_hive_carrier_w1");
            lo.Aliens.Add(Alien(c, "alien_pebblehorn"));
            lo.Aliens.Add(Alien(c, "alien_emberkit"));
            lo.Aliens.Add(Alien(c, "alien_mossling"));
            var setup = new BattleSetup { RoundCap = rounds };
            setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, new[] { "sig_alien" }));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "magma_slug", 1, 0, 3000));
            setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "ash_wraith", 1, 0, 3000));
            return BattleSimulator.Simulate(setup, c, new Rng(seed));
        }

        [Test]
        public void Playback_ShowsSidekickTurns_GuardsAndBlocks()
        {
            var c = _f.Service.Content;
            var battle = SidekickBattle(c);
            Assert.That(battle.Log.Exists(e => e.Action == BattleAction.SidekickAct), Is.True);
            Assert.That(battle.Log.Exists(e => e.Action == BattleAction.Block), Is.True, "a guard intercepted a hit");
            _f.App.Router.Reset(new HubScreen());
            var screen = new BattlePlaybackScreen(battle);
            _f.App.Router.Push(screen);
            // the NEXT pips include sidekicks (their act entries), before any of them has acted
            Assert.That(screen.NextActors.Exists(i => battle.Fighters[i].Role == CombatRole.Alien || battle.Fighters[i].Role == CombatRole.Summon), Is.True);
            int worm = battle.Fighters.FindIndex(f => f.Role == CombatRole.Worm);
            int guard = battle.Log.FindIndex(e => e.Action == BattleAction.Guard);
            screen.PlaySteps(guard + 1);
            Assert.That(screen.GuardsOn(worm), Is.EqualTo(battle.Log[guard].Stacks), "the worm's guard chip");
            screen.PlayAll();
            Assert.That(screen.SidekickActs, Is.EqualTo(battle.Log.FindAll(e => e.Action == BattleAction.SidekickAct).Count));
            Assert.That(screen.Blocks, Is.EqualTo(battle.Log.FindAll(e => e.Action == BattleAction.Block).Count));
            Assert.That(screen.AbilityFloats, Does.Contain("Block"));
            Assert.That(screen.AbilityFloats, Does.Contain("Kindle"));
            Assert.That(screen.AbilityFloats, Does.Contain("Drone Zap"));
            // sidekick abilities play on the sidekicks, not on the worm's skill grid; Command (the worm's) does
            foreach (var f in BattleSkillFx.Fires(battle, c))
                Assert.That(f.SkillId.StartsWith("ask_") || f.SkillId.StartsWith("skab_"), Is.False, f.SkillId);
            Assert.That(screen.FireCount("sig_alien"), Is.GreaterThan(0));
        }

        [Test]
        public void AbilityNames_DropTheSpeciesAndTier()
        {
            var c = ContentDb.Default;
            Assert.That(BattlePlaybackScreen.AbilityName("ask_pebblehorn_3", "Pebblehorn", c), Is.EqualTo("Block"));
            Assert.That(BattlePlaybackScreen.AbilityName("ask_tidecaller_5", "Tidecaller", c), Is.EqualTo("Soothing Spores"));
            Assert.That(BattlePlaybackScreen.AbilityName("skab_drone", "Drone", c), Is.EqualTo("Drone Zap"));
        }

        [Test]
        public void AlienScreen_ShowsAbility_PowerAndFrequency_NotHpDef()
        {
            var c = _f.Service.Content;
            var a = new OwnedAlien { TypeId = "alien_pebblehorn", Level = 1, Stars = 1 };
            Assert.That(AliensScreen.AbilityLine(c, a), Is.EqualTo("Ability: Block — Intercept the next hit on the worm."));
            Assert.That(AliensScreen.PowerLine(c, a), Does.StartWith("Power "));
            Assert.That(AliensScreen.PowerLine(c, a), Does.EndWith("acts about every 3 turns"));
            Assert.That(AliensScreen.ActsText(100 / 50.0), Is.EqualTo("acts about every 2 turns"));
            Assert.That(AliensScreen.ActsText(100 / 40.0), Is.EqualTo("acts about every 2.5 turns"));
            Assert.That(AliensScreen.ActsText(0.6), Is.EqualTo("acts every turn"));
        }
    }
}
