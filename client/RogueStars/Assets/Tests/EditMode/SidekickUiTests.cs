using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine.UI;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Sidekick + aliens pass: the battle playback shows sidekick turns, an alien's Guard softening a hit, Bodyguard's blocks; the
    /// aliens screen shows the ability of the star, the next star, act frequency, traits and genes "coming soon" (no levels, no
    /// stats); the breeding screen is Grain's sink; every species has an art key.
    /// </summary>
    public sealed class SidekickUiTests
    {
        private AppFixture _f;

        [SetUp] public void SetUp() => _f = new AppFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        /// <summary>A squad alien as the loadout builds it (aliens pass: Speed only, the ability of its star).</summary>
        public static CombatAlien Alien(ContentDb c, string id, int star = 1)
        {
            var def = c.AlienType(id);
            var a = new CombatAlien { AlienTypeId = id, Name = def.Name, Stars = star };
            a.Stats.Add(new StatMod(StatId.Speed, def.BaseSpeed));
            a.SkillIds.Add(AlienSystem.AbilityAt(c, id, star));
            return a;
        }

        public static BattleResult SidekickBattle(ContentDb c, ulong seed = 3, int rounds = 8, bool bodyguard = false)
        {
            BattleResult r = null;
            for (ulong s = seed; s < seed + 20; s++)
            {
                var lo = new CombatLoadout { DisplayName = "Worm", MountId = "mount_hive_carrier" };
                lo.WormStats.Add(new StatMod(StatId.Hp, 6000)).Add(new StatMod(StatId.Atk, 60)).Add(new StatMod(StatId.Def, 10)).Add(new StatMod(StatId.Speed, 100));
                lo.MountStats.Add(new StatMod(StatId.Atk, 40)).Add(new StatMod(StatId.Hp, 10));
                lo.MetaSkillIds.Add("mount_hive_carrier_w1");
                lo.Aliens.Add(Alien(c, "alien_pebblehorn", 5));
                lo.Aliens.Add(Alien(c, "alien_emberkit"));
                lo.Aliens.Add(Alien(c, "alien_mossling"));
                var setup = new BattleSetup { RoundCap = rounds };
                setup.Player.AddRange(BattleSimulator.BuildPlayerSide(lo, c, bodyguard ? new[] { "sig_alien", "alien_bodyguard" } : new[] { "sig_alien" }));
                setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "magma_slug", 1, 0, 3000));
                setup.Enemy.Add(BattleSimulator.BuildEnemy(c, "ash_wraith", 1, 0, 3000));
                r = BattleSimulator.Simulate(setup, c, new Rng(s));
                if (r.Log.Exists(e => e.Action == BattleAction.Guarded) && (!bodyguard || r.Log.Exists(e => e.Action == BattleAction.Block))) break;
            }
            return r;
        }

        [Test]
        public void Playback_ShowsSidekickTurns_GuardsAndBlocks()
        {
            var c = _f.Service.Content;
            var battle = SidekickBattle(c, bodyguard: true);
            Assert.That(battle.Log.Exists(e => e.Action == BattleAction.SidekickAct), Is.True);
            Assert.That(battle.Log.Exists(e => e.Action == BattleAction.Guarded), Is.True, "Pebblehorn's guard softened a hit");
            Assert.That(battle.Log.Exists(e => e.Action == BattleAction.Block), Is.True, "Bodyguard's block intercepted a hit");
            _f.App.Router.Reset(new HubScreen());
            var screen = new BattlePlaybackScreen(battle);
            _f.App.Router.Push(screen);
            // the NEXT pips include sidekicks (their act entries), before any of them has acted
            Assert.That(screen.NextActors.Exists(i => battle.Fighters[i].Role == CombatRole.Alien || battle.Fighters[i].Role == CombatRole.Summon), Is.True);
            int worm = battle.Fighters.FindIndex(f => f.Role == CombatRole.Worm);
            int guard = battle.Log.FindIndex(e => e.Action == BattleAction.Guard && e.Amount > 0);
            screen.PlaySteps(guard + 1);
            Assert.That(screen.GuardsOn(worm), Is.EqualTo(battle.Log[guard].Stacks), "the worm's guard chip");
            Assert.That(screen.GuardPctOn(worm), Is.EqualTo(35), "a 5★ Stone Guard: the next hit −35%");
            screen.PlayAll();
            Assert.That(screen.SidekickActs, Is.EqualTo(battle.Log.FindAll(e => e.Action == BattleAction.SidekickAct).Count));
            Assert.That(screen.Guardeds, Is.EqualTo(battle.Log.FindAll(e => e.Action == BattleAction.Guarded).Count));
            Assert.That(screen.Blocks, Is.EqualTo(battle.Log.FindAll(e => e.Action == BattleAction.Block).Count));
            Assert.That(screen.AbilityFloats, Does.Contain("Stone Guard"));
            Assert.That(screen.AbilityFloats, Does.Contain("Kindle"));
            Assert.That(screen.AbilityFloats, Does.Contain("Drone Zap"));
            // sidekick abilities play on the sidekicks, not on the worm's skill grid; Command (the worm's) does
            foreach (var f in BattleSkillFx.Fires(battle, c))
                Assert.That(f.SkillId.StartsWith("ask_") || f.SkillId.StartsWith("skab_"), Is.False, f.SkillId);
            Assert.That(screen.FireCount("sig_alien"), Is.GreaterThan(0));
        }

        [Test]
        public void AbilityNames_AreTheAbility()
        {
            var c = ContentDb.Default;
            Assert.That(BattlePlaybackScreen.AbilityName("ask_pebblehorn_3", "Pebblehorn", c), Is.EqualTo("Stone Guard"));
            Assert.That(BattlePlaybackScreen.AbilityName("ask_tidecaller_5", "Tidecaller", c), Is.EqualTo("Tidal Call"));
            Assert.That(BattlePlaybackScreen.AbilityName("ask_swarmheart_10", "Swarmheart", c), Is.EqualTo("Hatch"));
            Assert.That(BattlePlaybackScreen.AbilityName("skab_drone", "Drone", c), Is.EqualTo("Drone Zap"));
        }

        [Test]
        public void AlienCard_ShowsTheStarsAbility_NextStar_Frequency_Traits_GenesComingSoon()
        {
            var c = _f.Service.Content;
            var a = new OwnedAlien { TypeId = "alien_pebblehorn", Stars = 1 };
            Assert.That(AliensScreen.AbilityLine(c, a), Is.EqualTo("Stone Guard — Guard the worm: the next hit it takes deals 20% less."));
            Assert.That(AliensScreen.NextStarLine(c, a), Is.EqualTo("Next: 2★ — Guard the worm: the next hit it takes deals 23% less."));
            // a small range holds between tier stars: the next change is the next tier star
            var tq = new OwnedAlien { TypeId = "alien_thornqueen", Stars = 1 };
            Assert.That(AliensScreen.NextStarLine(c, tq), Does.StartWith("Next: 3★ (tier star) — ").And.Contains("2% of ATK"));
            Assert.That(AliensScreen.NextStarLine(c, new OwnedAlien { TypeId = "alien_thornqueen", Stars = 10 }), Is.Null);
            Assert.That(AliensScreen.ActsText(AlienSystem.ActsEvery(c, a)), Is.EqualTo("acts about every 3 turns"));
            Assert.That(AliensScreen.ActsText(100 / 50.0), Is.EqualTo("acts about every 2 turns"));
            Assert.That(AliensScreen.ActsText(100 / 40.0), Is.EqualTo("acts about every 2.5 turns"));
            Assert.That(AliensScreen.ActsText(0.6), Is.EqualTo("acts every turn"));
            a.Traits.Add(ContentDb.TraitBulwark);
            Assert.That(AliensScreen.TraitsLine(c, a), Is.EqualTo("Traits: Bulwark (Its guards and shields +20%)"));
            Assert.That(AliensScreen.SynergyLine(c.AlienType("alien_nebulisk")), Is.EqualTo("Bleed · Fire · Radiation · Combo"));
            Assert.That(AliensScreen.SquadHint(c, new System.Collections.Generic.List<string> { "alien_shiverpup", "alien_frostmite" }), Does.Contain("Freeze resets Chill"));
            Assert.That(AliensScreen.SquadHint(c, new System.Collections.Generic.List<string> { "alien_shiverpup", "alien_blorp" }), Is.Null);

            // the screen: no levels, no Lv-up button, genes "coming soon", the ability of the star
            _f.Service.State.Aliens.Owned["alien_pebblehorn"] = new OwnedAlien { TypeId = "alien_pebblehorn", Stars = 3, Level = 40 };
            _f.App.Router.Push(new AliensScreen());
            var texts = _f.App.Router.Current.Root.GetComponentsInChildren<Text>(true).Select(t => t.text).ToList();
            Assert.That(texts.Any(t => t.StartsWith("Lv ") || t.StartsWith("Lv up")), Is.False, string.Join(" | ", texts));
            Assert.That(texts, Does.Contain(AliensScreen.GenesLine));
            Assert.That(texts.Any(t => t.Contains("deals 25% less")), Is.True, "3★ Stone Guard");
            Assert.That(texts.Any(t => t.StartsWith("Breed")), Is.True, "a Breed shortcut (Grain's sink)");
        }

        [Test]
        public void BreedingScreen_IsGrainsSink_WithOddsAndReleaseShards()
        {
            var c = _f.Service.Content;
            Assert.That(BreedingScreen.OddsText(c, "alien_blorp", "alien_solarch"), Is.EqualTo("50% Blorp (1 shard if released) · 50% Solarch (2 shards if released)"));
            var child = new PendingChild { TypeId = "alien_solarch", ParentA = "alien_blorp", ParentB = "alien_solarch" };
            Assert.That(BreedingScreen.ReleaseText(c, child), Is.EqualTo("+2 Solarch shards and 900 Grain back"));
            _f.Service.State.Aliens.Owned["alien_blorp"] = new OwnedAlien { TypeId = "alien_blorp" };
            _f.Service.State.Aliens.Owned["alien_zib"] = new OwnedAlien { TypeId = "alien_zib" };
            _f.App.Router.Push(new BreedingScreen("alien_blorp"));
            var texts = _f.App.Router.Current.Root.GetComponentsInChildren<Text>(true).Select(t => t.text).ToList();
            Assert.That(texts, Does.Contain(BreedingScreen.Intro));
            Assert.That(texts.Any(t => t.Contains("(power)")), Is.False, "no gene stats");
        }

        [Test]
        public void EverySpecies_HasAnArtKey_AndTheRefundToastReads()
        {
            var c = ContentDb.Default;
            Assert.That(ArtCatalog.AlienKeys.OrderBy(k => k), Is.EqualTo(c.AlienTypes.Keys.Select(ArtCatalog.AlienKey).OrderBy(k => k)));
            var e = new GameEvent { Type = "alien_levels_refunded" };
            e.Args.Add("1234");
            Assert.That(EventText.Describe(e, c), Is.EqualTo("Alien levels are gone: +1234 Grain back for breeding"));
        }
    }
}
