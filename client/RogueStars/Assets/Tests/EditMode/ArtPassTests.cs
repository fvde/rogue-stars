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
        public void Ground_CopiesAreUpright_FullFrameCrossfades_SeamlessMeetsEdgeToEdge()
        {
            var uv = new Rect(0, 0, 1, 1);
            var quads = GroundLayer.BuildQuads(uv, 1000, 1700, 340, 4, 1);
            foreach (var q in quads)
            {
                Assert.That(q.UvTop, Is.GreaterThan(q.UvBottom), "upright: never flipped vertically");
                Assert.That(q.UvRight, Is.GreaterThan(q.UvLeft), "never mirrored");
                Assert.That(q.Bottom, Is.GreaterThan(q.Top));
            }
            var fades = quads.Where(q => q.AlphaTop < 1).ToList();
            Assert.That(fades.Count, Is.EqualTo(3), "a crossfade band between every two copies");
            foreach (var q in fades)
            {
                Assert.That(q.AlphaTop, Is.EqualTo(0));
                Assert.That(q.AlphaBottom, Is.EqualTo(1));
                Assert.That(q.Bottom - q.Top, Is.EqualTo(340).Within(1e-3f));
                Assert.That(q.UvTop, Is.EqualTo(1), "the band is the top of the lower copy fading in over the copy above");
            }
            // the copy below starts one pitch lower, so its band lies over the bottom of the copy above
            Assert.That(quads.Where(q => q.AlphaTop < 1).Select(q => q.Top), Is.EqualTo(new[] { 1360f, 2720f, 4080f }));
            var seamless = GroundLayer.BuildQuads(uv, 1000, 1700, 0, 3, 1);
            Assert.That(seamless.All(q => q.AlphaTop == 1 && q.AlphaBottom == 1), Is.True);
            Assert.That(seamless.Select(q => q.Top), Is.EqualTo(new[] { 0f, 1700f, 3400f }), "edge to edge");

            Assert.That(GroundLayer.Phase(0, 500), Is.EqualTo(500));
            Assert.That(GroundLayer.Phase(120, 500), Is.EqualTo(380).Within(1e-3f), "moves exactly with the camera");
            Assert.That(GroundLayer.Phase(1120, 500), Is.EqualTo(380).Within(1e-2f), "wraps by whole pitches");
            for (float c = -900; c < 5000; c += 37) Assert.That(GroundLayer.Phase(c, 500), Is.GreaterThan(0).And.LessThanOrEqualTo(500));
        }

        [Test]
        public void Board_GroundAndDecorMoveExactlyWithTheTiles()
        {
            ArtCatalog.Loader = null;
            StartRun(C.MainLevel(1));
            var screen = Open();
            var ground = FindNamed(screen.Root, "BoardGround");
            Assert.That(ground, Is.Not.Null);
            foreach (var tr in ground.GetComponentsInChildren<Transform>(true))
                Assert.That(tr.localScale.y, Is.GreaterThan(0), "no flipped ground copy: " + tr.name);
            Assert.That(screen.GroundView.Quads().All(q => q.UvTop > q.UvBottom), Is.True);
            Assert.That(screen.GroundView.Band, Is.GreaterThan(0), "the full-frame lava background crossfades");

            var decor = FindNamed(screen.Root, "Decor") ?? FindNamed(screen.Root, "Decor:Landmark");
            Assert.That(decor, Is.Not.Null, "placeholder decor beside the path");
            Assert.That(decor.parent.name, Is.EqualTo("Board"), "decor lies on the board");
            var tile = FindNamed(screen.Root, "Tile:1");
            var drag = FindNamed(screen.Root, "BoardHolder").GetComponent<BoardDrag>();
            float pitch = screen.GroundView.Pitch;
            float Mod(float x) { x %= pitch; return x < 0 ? x + pitch : x; }
            float offset = Mod(screen.GroundY - screen.BoardY);
            for (int i = 0; i < 40; i++)
            {
                float b0 = screen.BoardY, g0 = screen.GroundY, d0 = decor.position.y, t0 = tile.position.y;
                drag.OnDragY(i % 7 == 6 ? -91 : 37);
                float db = screen.BoardY - b0, dg = screen.GroundY - g0;
                Assert.That(db, Is.Not.EqualTo(0));
                bool wrapped = Mathf.Abs(dg - db) > 1;
                if (!wrapped) Assert.That(dg, Is.EqualTo(db).Within(1e-2f), "the ground moves 1:1 with the board, frame " + i);
                else Assert.That(Mathf.Abs(Mathf.Abs(dg - db) - pitch), Is.LessThan(0.05f), "only a whole-pitch wrap");
                Assert.That(Mod(screen.GroundY - screen.BoardY), Is.EqualTo(offset).Within(0.05f).Or.EqualTo(offset + pitch).Within(0.05f).Or.EqualTo(offset - pitch).Within(0.05f),
                    "the ground keeps its place under the tiles");
                Assert.That(decor.position.y - d0, Is.EqualTo(tile.position.y - t0).Within(1e-3f), "decor moves with the tiles");
            }
        }

        // ---- decor ----

        private static RunTile T(int id, double x, double y, double dir, params int[] next)
        {
            var t = new RunTile { Id = id, TileId = "t", PosX = x, PosY = y, Dir = dir, Segment = id < 12 ? 0 : 1, X = id };
            t.Next.AddRange(next);
            return t;
        }

        /// <summary>A hand-laid board: a trunk that forks at tile 4 into two arms that meet again, a second segment, the arena.</summary>
        private static List<RunTile> Board()
        {
            var tiles = new List<RunTile>
            {
                T(0, 0, 0, 0, 1), T(1, 0, 1, 0, 2), T(2, 0.4, 2, 20, 3), T(3, 0.8, 3, 10, 4), T(4, 0.8, 4, 0, 5, 8),
                T(5, -0.6, 5, -40, 6), T(6, -1.2, 6, 0, 7), T(7, -0.6, 7, 40, 11),
                T(8, 2.0, 5, 40, 9), T(9, 2.2, 6, 0, 10), T(10, 1.6, 7, -40, 11),
                T(11, 0.6, 8, 0, 12),
                T(12, 0.6, 9, 0, 13), T(13, 0, 10, -30, 14), T(14, -0.6, 11, 0, 15), T(15, -0.6, 12, 0, 16), T(16, 0, 13.4, 0),
            };
            return tiles;
        }

        private static void AssertClear(List<RunTile> tiles, List<DecorProp> props, int gate)
        {
            var byId = tiles.ToDictionary(t => t.Id);
            foreach (var p in props)
            {
                float r = BoardDecor.Radius(p.Size);
                foreach (var t in tiles)
                {
                    var rect = RunScreen.TileRect(t, t.Id == gate);
                    Assert.That(BoardDecor.CircleRectGap(p.Pos, rect), Is.GreaterThanOrEqualTo(r), "prop at " + p.Pos + " overlaps tile " + t.Id);
                    foreach (int n in t.Next)
                        if (byId.TryGetValue(n, out var nt))
                            Assert.That(BoardDecor.SegmentDistance(p.Pos, RunScreen.TileCenter(t, 0), RunScreen.TileCenter(nt, 0)), Is.GreaterThanOrEqualTo(r + BoardDecor.EdgeHalf),
                                "prop at " + p.Pos + " on the path " + t.Id + "→" + n);
                }
                foreach (var q in props)
                    if (!q.Equals(p)) Assert.That((q.Pos - p.Pos).magnitude, Is.GreaterThanOrEqualTo(r + BoardDecor.Radius(q.Size)), "props overlap");
                Assert.That(p.Pos.x, Is.GreaterThan(0).And.LessThan(RunScreen.BoardWidth), "on the board");
            }
        }

        [Test]
        public void Decor_NeverOverlapsTilesPathOrEachOther_Deterministic_LandmarksAtForksAndArena()
        {
            var tiles = Board();
            int any = 0, landmarks = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                var props = BoardDecor.Place(tiles, seed, 16);
                AssertClear(tiles, props, 16);
                any += props.Count;
                foreach (var p in props.Where(p => p.Landmark))
                {
                    landmarks++;
                    float near = Math.Min((p.Pos - RunScreen.TileCenter(tiles[4], 0)).magnitude, (p.Pos - RunScreen.TileCenter(tiles[16], 0)).magnitude);
                    Assert.That(near, Is.LessThan(360), "landmarks stand beside the fork or the arena");
                    Assert.That(p.Size, Is.GreaterThanOrEqualTo(BoardDecor.LandmarkMin));
                }
                Assert.That(props.Where(p => !p.Landmark).All(p => p.Size <= BoardDecor.PropMax), Is.True);
                var again = BoardDecor.Place(tiles, seed, 16);
                Assert.That(again, Is.EqualTo(props), "deterministic per seed");
            }
            Assert.That(any, Is.GreaterThan(40 * 2), "props are placed");
            Assert.That(landmarks, Is.GreaterThanOrEqualTo(40), "most boards get a landmark at the fork or the arena");
            Assert.That(BoardDecor.Place(tiles, 1, 16), Is.Not.EqualTo(BoardDecor.Place(tiles, 2, 16)), "another run, other props");
            // segment by segment: adding a segment ahead never moves the props of the segments behind it
            var seg0 = tiles.Where(t => t.Segment == 0).ToList();
            var only0 = BoardDecor.Place(seg0, 7);
            var both = BoardDecor.Place(tiles, 7).Where(p => p.Segment == 0).ToList();
            foreach (var p in both) Assert.That(only0, Does.Contain(p));
            // not cluttered: at most ~one prop per tile on average
            Assert.That(any / 40.0, Is.LessThanOrEqualTo(tiles.Count * 1.2));
            // hand-built boards (no 2D layout) get no decor
            var snake = new List<RunTile> { new RunTile { Id = 0, Segment = -1 }, new RunTile { Id = 1, Segment = -1 } };
            Assert.That(BoardDecor.Place(snake, 1), Is.Empty);
        }

        [Test]
        public void Decor_OnRealRuns_IsClearAndUsesPropArtWhenThere()
        {
            // three different runs of level 1 (each restart forks a new run stream); boards grown by a few rolls
            var seen = new HashSet<string>();
            for (int k = 0; k < 3; k++)
            {
                if (S.Run.Active != null) Assert.That(_f.Service.ExecuteNow(new RunAbandon()).Ok, Is.True);
                var run = StartRun(C.MainLevel(1));
                for (int i = 0; i < 6 && S.Run.Active != null; i++) _f.Service.ExecuteNow(new RunAutoStep { Farming = true, FarmShops = true });
                run = S.Run.Active ?? run;
                seen.Add(run.RunId);
                var props = BoardDecor.Place(run.Tiles, BoardDecor.Seed(run.RunId), run.BossGateTile);
                Assert.That(props, Is.Not.Empty, run.RunId);
                AssertClear(run.Tiles.Where(RunScreen.HasLayout).ToList(), props, run.BossGateTile);
                Assert.That(BoardDecor.Seed(run.RunId), Is.EqualTo(BoardDecor.Seed(new string(run.RunId.ToCharArray()))));
            }
            Assert.That(seen.Count, Is.EqualTo(3));
            if (S.Run.Active != null) _f.Service.ExecuteNow(new RunAbandon());
            StartRun(C.MainLevel(1));
            // with generated props the board draws them (no placeholder rocks), sprites cycle through the biome's set
            ArtCatalog.FolderLoader = p => new[] { Fake };
            var screen = Open();
            var prop = FindNamed(screen.Root, "Prop");
            Assert.That(prop, Is.Not.Null);
            Assert.That(prop.GetComponent<Image>().sprite, Is.SameAs(Fake));
            Assert.That(FindNamed(screen.Root, "Rock"), Is.Null);
            Assert.That(screen.Decor, Is.Not.Empty);
        }
    }
}
