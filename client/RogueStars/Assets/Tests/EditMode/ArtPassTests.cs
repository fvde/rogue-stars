using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.Editor;
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
    /// Art pass 1: enemy / alien sprites and their fallbacks, the biome mapping, the generated-art importer's selection rules,
    /// the layered board (an upright ground locked 1:1 to the board, decor props beside the path that never overlap tiles,
    /// edges or each other, deterministic per run and segment).
    /// </summary>
    public sealed class ArtPassTests
    {
        private AppFixture _f;
        private GameState S => _f.Service.State;
        private ContentDb C => _f.Service.Content;
        private static readonly Sprite Fake = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; RunFx.Instant = true; }

        [TearDown]
        public void TearDown()
        {
            RunScreen.InstantOutcomes = false; RunFx.Instant = false; BattlePlaybackScreen.Instant = false;
            ArtCatalog.Loader = null;
            ArtCatalog.FolderLoader = null;
            _f.Dispose();
        }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static List<Transform> AllNamed(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToList();

        private ActiveRun StartRun(LevelDef level)
        {
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            return S.Run.Active;
        }

        private RunScreen Open()
        {
            _f.App.Router.Push(new RunScreen());
            return (RunScreen)_f.App.Router.Current;
        }

        // ---- sprites ----

        [Test]
        public void EnemySprites_OwnThenFamilyThenBox()
        {
            var have = new HashSet<string> { "Art/Enemies/ember_imp", "Art/Aliens/alien_blorp" };
            ArtCatalog.Loader = p => have.Contains(p) ? Fake : null;

            Assert.That(ArtCatalog.Enemy("ember_imp"), Is.SameAs(Fake));
            Assert.That(ArtCatalog.EnemyOrFamily("ember_imp", C, out bool fam), Is.SameAs(Fake));
            Assert.That(fam, Is.False, "its own sprite");
            Assert.That(ArtCatalog.EnemyFamily("boss_magma_tyrant", C), Does.Contain("ember_imp"), "a boss's escorts are its family");
            Assert.That(ArtCatalog.EnemyOrFamily("boss_magma_tyrant", C, out fam), Is.SameAs(Fake));
            Assert.That(fam, Is.True, "the boss without art: its family's sprite");
            Assert.That(ArtCatalog.EnemyFamily("elite_frost_knight", C), Is.Not.Empty, "an elite's family: the enemies of its levels");
            Assert.That(ArtCatalog.EnemyFamily("elite_frost_knight", C), Does.Not.Contain("elite_frost_knight"));
            Assert.That(ArtCatalog.EnemyOrFamily("no_such_enemy", C, out fam), Is.Null, "no art anywhere: the lettered box");
            Assert.That(ArtCatalog.EnemyOrFamily("ember_imp", null, out _), Is.SameAs(Fake));

            // battle fighters: the boss is a scaled family sprite, a plain enemy its own sprite, an unknown enemy the box
            var boss = new BattleFighterInfo { Id = "boss_magma_tyrant", Role = CombatRole.Boss, Side = BattleSide.Enemy };
            Assert.That(BattlePlaybackScreen.FighterSprite(boss, C, out fam, out float size), Is.SameAs(Fake));
            Assert.That(fam, Is.True);
            Assert.That(size, Is.EqualTo(BattlePlaybackScreen.EnemySpriteSize * ArtCatalog.BossFamilyScale).Within(1e-3f));
            Assert.That(BattlePlaybackScreen.FighterSprite(new BattleFighterInfo { Id = "ember_imp", Role = CombatRole.Enemy, Side = BattleSide.Enemy }, C, out fam, out size), Is.SameAs(Fake));
            Assert.That(size, Is.EqualTo(BattlePlaybackScreen.EnemySpriteSize));
            Assert.That(BattlePlaybackScreen.FighterSprite(new BattleFighterInfo { Id = "x", Role = CombatRole.Enemy, Side = BattleSide.Enemy }, C, out _, out _), Is.Null);
            Assert.That(BattlePlaybackScreen.FighterSprite(new BattleFighterInfo { Id = "alien_blorp", Role = CombatRole.Alien, Side = BattleSide.Player }, C, out _, out _), Is.SameAs(Fake));
            Assert.That(BattlePlaybackScreen.FighterSprite(new BattleFighterInfo { Id = "alien_zib", Role = CombatRole.Alien, Side = BattleSide.Player }, C, out _, out _), Is.Null);

            // on the stage: sprites without the letter; the box keeps its letter; a hit still flashes and settles back
            BattlePlaybackScreen.Instant = true;
            var b = new BattleResult { Winner = BattleSide.Player, Rounds = 1 };
            b.Fighters.Add(new BattleFighterInfo { Index = 0, Name = "Worm", Role = CombatRole.Worm, Side = BattleSide.Player, Targetable = true, MaxHp = 1000, StartHp = 1000, EndHp = 1000, Alive = true });
            b.Fighters.Add(new BattleFighterInfo { Index = 1, Id = "alien_blorp", Name = "Blorp", Role = CombatRole.Alien, Side = BattleSide.Player, MaxHp = 100, StartHp = 100, EndHp = 100, Alive = true });
            b.Fighters.Add(new BattleFighterInfo { Index = 2, Id = "ember_imp", Name = "Imp", Role = CombatRole.Enemy, Side = BattleSide.Enemy, Targetable = true, MaxHp = 300, StartHp = 300, EndHp = 0 });
            b.Fighters.Add(new BattleFighterInfo { Index = 3, Id = "boss_magma_tyrant", Name = "Tyrant", Role = CombatRole.Boss, Side = BattleSide.Enemy, Targetable = true, MaxHp = 900, StartHp = 900, EndHp = 0 });
            b.Fighters.Add(new BattleFighterInfo { Index = 4, Id = "no_art", Name = "Thing", Role = CombatRole.Enemy, Side = BattleSide.Enemy, Targetable = true, MaxHp = 300, StartHp = 300, EndHp = 0 });
            b.Log.Add(new BattleLogEntry { Round = 1, Actor = 0, Target = 2, Action = BattleAction.Basic, Amount = 300, TargetHp = 0 });
            b.Log.Add(new BattleLogEntry { Round = 1, Actor = 0, Target = 2, Action = BattleAction.Death, TargetHp = 0 });
            var screen = new BattlePlaybackScreen(b);
            _f.App.Router.Push(screen);
            Image SpriteOf(int i) => FindNamed(FindNamed(screen.Root, "Fighter:" + i), "Sprite").GetComponent<Image>();
            Assert.That(SpriteOf(1).sprite, Is.SameAs(Fake), "the alien companion is its sprite");
            Assert.That(SpriteOf(2).sprite, Is.SameAs(Fake), "the enemy is its sprite, not the E box");
            Assert.That(SpriteOf(2).GetComponentInChildren<Text>(), Is.Null, "no role letter on a sprite");
            Assert.That(SpriteOf(3).sprite, Is.SameAs(Fake));
            Assert.That(FindNamed(FindNamed(screen.Root, "Fighter:3"), "RoleGlow"), Is.Not.Null, "the family sprite stands on the boss's glow");
            Assert.That(SpriteOf(3).rectTransform.sizeDelta.x, Is.GreaterThan(SpriteOf(2).rectTransform.sizeDelta.x), "scaled up");
            Assert.That(SpriteOf(4).GetComponentInChildren<Text>().text, Is.EqualTo("E"), "no art: the old box");
        }

        [Test]
        public void AlienSprites_OnTheAliensScreenAndTheSignalReveal()
        {
            ArtCatalog.Loader = p => p == "Art/Aliens/alien_blorp" ? Fake : null;
            S.Aliens.Owned["alien_blorp"] = new OwnedAlien { TypeId = "alien_blorp" };
            S.Aliens.Owned["alien_zib"] = new OwnedAlien { TypeId = "alien_zib" };
            _f.App.Router.Push(new AliensScreen());
            var screen = _f.App.Router.Current;
            var sprites = AllNamed(screen.Root, "AlienSprite");
            Assert.That(sprites.Count, Is.EqualTo(1), "only the alien with art shows a sprite");
            Assert.That(sprites[0].GetComponent<Image>().sprite, Is.SameAs(Fake));

            var signals = new SignalsScreen();
            _f.App.Router.Push(signals);
            var result = new GachaPullResult();
            result.Items.Add(new GachaPullItem { Kind = RewardKinds.Alien, Id = "alien_blorp", Amount = 1, Tier = Rarity.Common });
            result.Items.Add(new GachaPullItem { Kind = RewardKinds.Alien, Id = "alien_zib", Amount = 1, Tier = Rarity.Common });
            var modal = signals.Reveal(result);
            Assert.That(AllNamed(modal.Root.transform, "AlienSprite").Count, Is.EqualTo(1));
        }

        // ---- biomes ----

        [Test]
        public void BiomeMapping_EveryFamilyHasDistinctImportedArt()
        {
            ArtCatalog.Loader = null;
            var families = new[] { "ember", "glacier", "static", "void", "burrow", "dark", "standard" };
            var biomes = new HashSet<string>();
            foreach (var fam in families)
            {
                var b = ArtCatalog.BiomeFor(fam);
                Assert.That(b, Is.Not.SameAs(ArtCatalog.Default), fam + " is mapped");
                Assert.That(ArtCatalog.GroundOf(b).Sprite, Is.Not.Null, fam + " → " + b.Id + " has a ground image");
                biomes.Add(b.Id);
            }
            Assert.That(biomes.Count, Is.EqualTo(families.Length), "every family its own biome");
            Assert.That(ArtCatalog.BiomeFor("static").Id, Is.EqualTo("space_station"));
            Assert.That(ArtCatalog.BiomeFor("void").Id, Is.EqualTo("shipwreck"));
            Assert.That(ArtCatalog.BiomeFor("burrow").Id, Is.EqualTo("prehistoric"));
            Assert.That(ArtCatalog.BiomeFor("dark").Id, Is.EqualTo("ash_wastes"));
            // every level's tile pool is a mapped family
            foreach (var l in C.RunLevels.Values)
                if (ArtCatalog.FamilyOf(l) != null) Assert.That(ArtCatalog.BiomeFor(l), Is.Not.SameAs(ArtCatalog.Default), l.Id + " (" + l.TilePoolId + ")");
            // imported tile slabs: glacier (seabed) and static (neon city tiles on the station deck)
            Assert.That(ArtCatalog.TileSprite(ArtCatalog.BiomeFor("glacier"), TileCategory.Enemy), Is.Not.Null);
            Assert.That(ArtCatalog.TileSprite(ArtCatalog.BiomeFor("static"), TileCategory.Shop), Is.Not.Null);
            // a seamless ground wins over the full-frame background
            var have = new HashSet<string> { "Art/Boards/lava/background", "Art/Boards/lava/ground" };
            var bg = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 2, 2), Vector2.zero);
            ArtCatalog.Loader = p => p == "Art/Boards/lava/ground" ? Fake : have.Contains(p) ? bg : null;
            var g = ArtCatalog.GroundOf(ArtCatalog.BiomeFor("ember"));
            Assert.That(g.Sprite, Is.SameAs(Fake));
            Assert.That(g.Seamless, Is.True);
            ArtCatalog.Loader = p => p == "Art/Boards/lava/background" ? bg : null;
            g = ArtCatalog.GroundOf(ArtCatalog.BiomeFor("ember"));
            Assert.That(g.Sprite, Is.SameAs(bg));
            Assert.That(g.Seamless, Is.False);
            Assert.That(ArtCatalog.GroundOf(ArtCatalog.Default).Sprite, Is.Null);
        }

        [Test]
        public void ImportedSprites_AreInTheGame()
        {
            ArtCatalog.Loader = null;
            foreach (var id in new[] { "ember_imp", "glacier_golem", "spark_drone", "static_eel", "burrow_grub", "ice_crawler", "elite_lava_brute", "elite_rift_echo", "elite_storm_caller" })
                Assert.That(ArtCatalog.Enemy(id), Is.Not.Null, id);
            foreach (var id in new[] { "alien_blorp", "alien_clackclaw", "alien_frostmite", "alien_glimmerfly", "alien_gravitoad", "alien_pebblehorn", "alien_tidecaller", "alien_voidmaw" })
                Assert.That(ArtCatalog.Alien(id), Is.Not.Null, id);
            Assert.That(ArtCatalog.Enemy("null_shade"), Is.Null, "skipped: too featureless");
            Assert.That(ArtCatalog.Enemy("magma_slug"), Is.Null, "skipped: blurry");
        }

        // ---- importer ----

        private sealed class FakeTree
        {
            public readonly Dictionary<string, List<string>> Files = new Dictionary<string, List<string>>();
            public readonly Dictionary<string, List<string>> Dirs = new Dictionary<string, List<string>>();

            public void Add(string path)
            {
                var dir = Path.GetDirectoryName(path);
                if (!Files.TryGetValue(dir, out var l)) Files[dir] = l = new List<string>();
                l.Add(path);
                var parent = Path.GetDirectoryName(dir);
                if (!Dirs.TryGetValue(parent, out var d)) Dirs[parent] = d = new List<string>();
                if (!d.Contains(dir)) d.Add(dir);
            }

            public IEnumerable<string> D(string p) => Dirs.TryGetValue(p, out var l) ? l : new List<string>();
            public IEnumerable<string> F(string p) => Files.TryGetValue(p, out var l) ? l : new List<string>();
        }

        [Test]
        public void Importer_OnlySimpleVariants_PrefersC_SkipsRejected_StableNames()
        {
            const string A = "/art", B = "/boards";
            var t = new FakeTree();
            t.Add(A + "/aliens/alien_a/A_s1001.png");
            t.Add(A + "/aliens/alien_a/C_s1002.png");
            t.Add(A + "/aliens/alien_a/C_s1001.png");
            t.Add(A + "/aliens/alien_b/A_s1001.png");          // complicated only: not imported
            t.Add(A + "/aliens/review.png");
            t.Add(A + "/enemies/null_shade/C_s1301.png");       // rejected file
            t.Add(A + "/enemies/magma_slug/C_s1301.png");       // rejected file
            t.Add(A + "/enemies/magma_slug/S_s4201.png");       // a redo of a rejected id is imported
            t.Add(A + "/enemies/ember_imp/S_s4201.png");
            t.Add(A + "/enemies/ember_imp/C_s1301.png");
            t.Add(A + "/simple-redo/alien_c/S_s4201.png");      // simple redo: routed by the alien_ prefix
            t.Add(A + "/simple-redo/cinder_hound/S_s4201.png");
            t.Add(A + "/board-tile-sprites/ice/C_s3101.png");
            t.Add(A + "/board-tile-sprites/lava/A_s3101.png");
            t.Add(A + "/grounds/lava/G2_s4001.png");
            t.Add(A + "/decor/lava_2/C_s4101.png");
            t.Add(A + "/decor/lava_3/A_s4101.png");
            t.Add(A + "/decor/odd/C_s4101.png");
            t.Add(B + "/lava__BB__sdxl_ref__s3001.png");
            t.Add(B + "/candy_planet__BA__sdxl_ref__s3001.png");
            t.Add(B + "/index.html");

            var plan = ArtImportRules.Plan(A, B, t.D, t.F);
            var dest = plan.ToDictionary(i => i.Dest, i => i);
            string Src(string d) => dest.TryGetValue(d, out var i) ? i.Source : null;
            Assert.That(Src("Art/Aliens/alien_a.png"), Is.EqualTo(A + "/aliens/alien_a/C_s1001.png"), "C, lowest seed");
            Assert.That(dest.ContainsKey("Art/Aliens/alien_b.png"), Is.False, "A_ files are never imported");
            Assert.That(dest.ContainsKey("Art/Enemies/null_shade.png"), Is.False, "skip list");
            Assert.That(Src("Art/Enemies/magma_slug.png"), Is.EqualTo(A + "/enemies/magma_slug/S_s4201.png"));
            Assert.That(Src("Art/Enemies/ember_imp.png"), Is.EqualTo(A + "/enemies/ember_imp/C_s1301.png"), "C preferred over S");
            Assert.That(Src("Art/Aliens/alien_c.png"), Is.EqualTo(A + "/simple-redo/alien_c/S_s4201.png"));
            Assert.That(Src("Art/Enemies/cinder_hound.png"), Is.EqualTo(A + "/simple-redo/cinder_hound/S_s4201.png"));
            Assert.That(dest["Art/Boards/ice/tiles/slab.png"].Trim, Is.True, "tile slabs are trimmed");
            Assert.That(dest.ContainsKey("Art/Boards/lava/tiles/slab.png"), Is.False);
            Assert.That(Src("Art/Boards/lava/ground.png"), Is.EqualTo(A + "/grounds/lava/G2_s4001.png"));
            Assert.That(dest["Art/Boards/lava/ground.png"].Trim, Is.False, "grounds stay full-frame");
            Assert.That(Src("Art/Decor/lava/lava_2.png"), Is.EqualTo(A + "/decor/lava_2/C_s4101.png"));
            Assert.That(dest.ContainsKey("Art/Decor/lava/lava_3.png"), Is.False);
            Assert.That(plan.Any(i => i.Dest.Contains("odd")), Is.False, "decor folders are <biome>_<n>");
            Assert.That(Src("Art/Boards/lava/background.png"), Is.EqualTo(B + "/lava__BB__sdxl_ref__s3001.png"));
            Assert.That(dest["Art/Boards/lava/background.png"].KeepExisting, Is.True, "curated backgrounds are never replaced");
            Assert.That(dest.ContainsKey("Art/Boards/candy_planet/background.png"), Is.False, "only the BB boards");
            Assert.That(plan.Select(i => i.Dest), Is.Ordered, "stable order");
            Assert.That(ArtImportRules.Plan(A, B, t.D, t.F).Select(i => i.ToString()), Is.EqualTo(plan.Select(i => i.ToString())), "deterministic");

            // the preference is configurable
            ArtImportRules.SpriteVariants.Reverse();
            try
            {
                var p2 = ArtImportRules.Plan(A, B, t.D, t.F).ToDictionary(i => i.Dest, i => i.Source);
                Assert.That(p2["Art/Enemies/ember_imp.png"], Is.EqualTo(A + "/enemies/ember_imp/S_s4201.png"));
            }
            finally { ArtImportRules.SpriteVariants.Reverse(); }

            Assert.That(ArtImportRules.TryParse("C_s1301.png", out var v, out int seed), Is.True);
            Assert.That(v + seed, Is.EqualTo("C1301"));
            Assert.That(ArtImportRules.TryParse("review.png", out _, out _), Is.False);
            Assert.That(ArtImportRules.DecorBiome("toxic_swamp_3"), Is.EqualTo("toxic_swamp"));
        }

        // ---- ground ----

        [Test]
        public void Importer_SliceArt_OnlyTheOwnersPicks_ToTheArtCatalogKeys()
        {
            var picks = ArtImportRules.ReadPicks("{ \"slice-markers/enemy\": \"C_s1301.png\", \"slice-texture/tile_top_moss\" : \"T_s7.png\", \"slice-skills/fire_kindle\": \"C_s2.png\", \"nope/x\": \"a.png\" }");
            Assert.That(picks.Count, Is.EqualTo(4));
            var plan = ArtImportRules.PlanSlice("/assets", picks);
            Assert.That(plan.Count, Is.EqualTo(3), "unknown categories are ignored");
            var enemy = plan.Find(i => i.Id == "slice-markers/enemy");
            Assert.That(enemy.Dest, Is.EqualTo("Art/Slice/Markers/enemy.png"));
            Assert.That(enemy.Source.Replace('\\', '/'), Is.EqualTo("/assets/slice-markers/enemy/C_s1301.png"));
            Assert.That(enemy.Trim, Is.True);
            var tex = plan.Find(i => i.Id == "slice-texture/tile_top_moss");
            Assert.That(tex.Dest, Is.EqualTo("Art/Slice/Textures/tile_top_moss.png"));
            Assert.That(tex.Trim, Is.False, "full-frame textures keep their frame");
            Assert.That(plan.Find(i => i.Id == "slice-skills/fire_kindle").Dest, Is.EqualTo("Art/Skills/fire_kindle.png"));
            Assert.That(ArtImportRules.PlanSlice("/assets", null), Is.Empty);
            // the generic boss marker is retired (the board shows the run's boss)
            Assert.That(ArtImportRules.PlanSlice("/assets", ArtImportRules.ReadPicks("{ \"slice-markers/boss\": \"C_s1.png\" }")), Is.Empty);
        }

        // The board's ground and props are the world board's now (IsoBoardTests).
    }
}
