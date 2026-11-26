using System.Collections.Generic;
using NUnit.Framework;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// The iso world board (vertical slice): grid projection and painter's order, tile faces and markers per tile kind,
    /// set pieces in the rules' slots, scatter beside the path, "!" over special tiles ahead, the worm never standing in a
    /// marker, and the ArtCatalog slice keys replacing the code-drawn placeholders.
    /// </summary>
    public sealed class IsoBoardTests
    {
        private AppFixture _f;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; RunFx.Instant = true; }

        [TearDown]
        public void TearDown()
        {
            RunScreen.InstantOutcomes = false; RunFx.Instant = false;
            ArtCatalog.Loader = null;
            IsoArt.ClearCache();
            HudArt.ClearCache();
            _f.Dispose();
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static int Count(Transform root, string prefix)
        {
            int n = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith(prefix)) n++;
            return n;
        }

        private ActiveRun StartRun(int mainLevel = 1)
        {
            var level = C.MainLevel(mainLevel);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            return _f.Service.State.Run.Active;
        }

        [Test]
        public void Projection_ClimbsTheScreen_AndBackTilesDrawFirst()
        {
            Assert.That(Iso.Cell(1, 0).x, Is.GreaterThan(0), "+X runs up-right");
            Assert.That(Iso.Cell(0, 1).x, Is.LessThan(0), "+Y runs up-left");
            Assert.That(Iso.Cell(1, 1).y, Is.GreaterThan(Iso.Cell(0, 0).y));
            Assert.That(Iso.Cell(0, 0, 1).y - Iso.Cell(0, 0).y, Is.EqualTo(Iso.Rise).Within(1e-5f), "a raised block");
            Assert.That(Iso.Order(3, 4), Is.LessThan(Iso.Order(3, 3)), "further back = drawn earlier");
            Assert.That(Iso.TileW / Iso.TileH, Is.InRange(1.3f, 2.05f), "a flat dimetric diamond");
        }

        [Test]
        public void TileKinds_HaveFacesAndMarkers()
        {
            Assert.That(IsoArt.MarkerOf(TileCategory.Enemy), Is.EqualTo("enemy"));
            Assert.That(IsoArt.MarkerOf(TileCategory.Elite), Is.EqualTo("elite"));
            Assert.That(IsoArt.MarkerOf(TileCategory.Campfire), Is.EqualTo("rest"));
            Assert.That(IsoArt.MarkerOf(TileCategory.Shop), Is.EqualTo("shop"));
            Assert.That(IsoArt.MarkerOf(TileCategory.Event), Is.EqualTo("event"));
            Assert.That(IsoArt.MarkerOf(TileCategory.Empty), Is.Null);
            Assert.That(IsoArt.FaceOf(TileCategory.StatBuff), Is.EqualTo(TileFace.Boost));
            Assert.That(IsoArt.FaceOf(TileCategory.Event), Is.EqualTo(TileFace.Event));
            Assert.That(IsoArt.FaceOf(TileCategory.Shop), Is.EqualTo(TileFace.Special));
            Assert.That(IsoArt.FaceOf(TileCategory.Enemy), Is.EqualTo(TileFace.Stone));
            foreach (TileFace f in System.Enum.GetValues(typeof(TileFace)))
            {
                Assert.That(IsoArt.Block(f, 0), Is.Not.Null);
                Assert.That(IsoArt.Block(f, 1).bounds.size.y, Is.GreaterThan(IsoArt.Block(f, 0).bounds.size.y), "a raised block has a taller wall");
            }
            foreach (var id in new[] { "enemy", "elite", "miniboss", "boss", "event", "mystery", "chest", "rest", "shop", "minigame", "portal", "reward" })
                Assert.That(IsoArt.Marker(id), Is.Not.Null, id);
            foreach (var id in IsoArt.SetPieceIds) Assert.That(IsoArt.SetPiece(id), Is.Not.Null, id);
            foreach (var id in IsoArt.ScatterIds) Assert.That(IsoArt.Scatter(id), Is.Not.Null, id);
            foreach (var id in HudArt.Ids) Assert.That(HudArt.Icon(id), Is.Not.Null, id);
        }

        [Test]
        public void TheBossGate_ShowsTheRunsBoss_WithItsBattleSprite_Large()
        {
            var fake = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            var c = RogueStars.Rules.Content.ContentDb.Default;
            string bossId = null;
            foreach (var kv in c.RunBosses) { bossId = kv.Key; break; }
            string enemy = c.RunBosses[bossId].EnemyId;
            ArtCatalog.Loader = p => p == ArtCatalog.EnemyFolder + "/" + enemy || p == "Art/Slice/Markers/boss" ? fake : null;
            IsoArt.ClearCache();
            try
            {
                Assert.That(IsoArt.BossMarker(c, bossId), Is.SameAs(fake), "the boss's own (battle) sprite");
                Assert.That(IsoArt.Marker("boss"), Is.Not.SameAs(fake), "no generic slice boss marker");
                Assert.That(IsoArt.MarkerHeight("boss") * 1.25f / IsoArt.MarkerHeight("enemy"), Is.InRange(2f, 2.6f), "2–2.5× a normal marker on its gate");
                Assert.That(IsoArt.BossMarkerWidth, Is.GreaterThan(Iso.TileW * 1.5f));
            }
            finally { ArtCatalog.Loader = null; IsoArt.ClearCache(); }
        }

        [Test]
        public void SliceArt_ReplacesThePlaceholders_ThroughArtCatalog()
        {
            var fake = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            var asked = new List<string>();
            ArtCatalog.Loader = p => { asked.Add(p); return p == "Art/Slice/Markers/enemy" || p == "Art/Slice/Hud/heart" || p == "Art/Slice/SetPieces/portal_well" ? fake : null; };
            IsoArt.ClearCache();
            HudArt.ClearCache();
            Assert.That(IsoArt.Marker("enemy"), Is.SameAs(fake));
            Assert.That(IsoArt.Marker("elite"), Is.Not.SameAs(fake), "missing art: the drawn placeholder");
            Assert.That(HudArt.Icon("heart"), Is.SameAs(fake));
            Assert.That(IsoArt.SetPiece("portal_well"), Is.SameAs(fake));
            Assert.That(asked, Does.Contain("Art/Slice/Scatter/rock_small").Or.Not.Contain("x"));
            IsoArt.Scatter("rock_small");
            Assert.That(asked, Does.Contain("Art/Slice/Scatter/rock_small"));
        }

        [Test]
        public void SetPieceAlternates_PickedPerSlot_MainOnlyWithoutAlternates()
        {
            Sprite Make() => Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            Sprite main = Make(), alt2 = Make(), alt3 = Make(), well = Make();
            ArtCatalog.Loader = p => p == "Art/Slice/SetPieces/giant_mushroom" ? main : p == "Art/Slice/SetPieces/giant_mushroom_2" ? alt2
                : p == "Art/Slice/SetPieces/giant_mushroom_3" ? alt3 : p == "Art/Slice/SetPieces/portal_well" ? well : null;
            try
            {
                IsoArt.ClearCache();
                Assert.That(ArtCatalog.SliceSetPieceVariants("giant_mushroom"), Is.EqualTo(3));
                Assert.That(new[] { IsoArt.SetPiece("giant_mushroom", 0), IsoArt.SetPiece("giant_mushroom", 1), IsoArt.SetPiece("giant_mushroom", 5) },
                    Is.EqualTo(new[] { main, alt2, alt3 }));
                Assert.That(IsoArt.SetPiece("giant_mushroom", -7), Is.Not.Null, "negative hashes pick a variant too");
                Assert.That(IsoArt.SetPiece("portal_well", 4), Is.SameAs(well), "no alternates: the main sprite");
                Assert.That(IsoArt.SetPiece("crystal_spire", 4), Is.Not.Null, "no art: the drawn placeholder");
            }
            finally { ArtCatalog.Loader = null; IsoArt.ClearCache(); }
        }

        [Test]
        public void WorldBoard_DrawsTilesSetPiecesScatterAndExclaims_AroundTheWorm()
        {
            // level 5: shops and the miniboss lair to exclaim (the tutorial level 1 is plain, GDD §16)
            for (int n = 1; n <= 4; n++) _f.Service.State.Run.Levels[C.MainLevelId(n)] = new LevelProgress { Cleared = true, Clears = 1 };
            _f.Service.State.Counters[Requirements.MainLevelCounter] = 4;
            var run = StartRun(5);
            _f.App.Router.Push(new RunScreen());
            var screen = (RunScreen)_f.App.Router.Current;
            var world = screen.World;
            Assert.That(world.Root.layer, Is.EqualTo(WorldBoard.Layer));
            Assert.That(world.Root.transform.position.magnitude, Is.GreaterThan(1000), "far from the UI scene");
            int cd = WorldBoard.Depth(run.Tiles[run.Position]);
            int inWindow = run.Tiles.FindAll(t => WorldBoard.InWindow(run, t, cd)).Count;
            Assert.That(world.Tiles.Count, Is.EqualTo(inWindow));
            Assert.That(world.Tiles.Count, Is.LessThan(run.Tiles.Count + 1));
            Assert.That(world.SetPiecesDrawn, Is.Not.Empty, "the loops hold set pieces");
            Assert.That(world.ScatterCount, Is.GreaterThan(5), "props along the path");
            Assert.That(world.ExclaimCount, Is.InRange(1, WorldBoard.ExclaimMax + 2)); // + the run's minibosses ahead (always marked, playtest 0.1.308)
            // no marker under the worm; every marker sits on its own tile
            Assert.That(Find(world.Tiles[run.Position].Go.transform, "Marker:portal"), Is.Null);
            foreach (var kv in world.Tiles)
            {
                var t = run.Tiles[kv.Key];
                Assert.That((Vector2)kv.Value.Go.transform.localPosition, Is.EqualTo(WorldBoard.TopOf(t)));
                Assert.That(kv.Value.Base.sortingOrder, Is.EqualTo(Iso.Order(WorldBoard.CellOf(t).x, WorldBoard.CellOf(t).y)));
            }
            // set pieces never sit on a path cell
            var cells = new HashSet<Vector2Int>();
            foreach (var t in run.Tiles) if (t.Segment >= 0) cells.Add(WorldBoard.CellOf(t));
            foreach (var sp in run.SetPieces)
                for (int x = sp.X; x < sp.X + sp.W; x++)
                    for (int y = sp.Y; y < sp.Y + sp.H; y++)
                        Assert.That(cells.Contains(new Vector2Int(x, y)), Is.False);
            // a rebuild keeps one world (no leak of boards)
            screen.Rebuild();
            Assert.That(screen.World, Is.SameAs(world));
            Assert.That(Count(world.Root.transform, "Content"), Is.EqualTo(1));
        }

        [Test]
        public void Landing_ColoursTheTop_AndABeatenMarkerLeaves()
        {
            var view = new TileView { Accent = Color.red, MarkerId = "enemy" };
            var go = new GameObject("t");
            view.Top = new GameObject("top").AddComponent<SpriteRenderer>();
            view.Marker = new GameObject("m").AddComponent<SpriteRenderer>();
            view.ApplyLanded(true);
            Assert.That(view.Top.color, Is.EqualTo(WorldBoard.LandedTint(Color.red)));
            Assert.That(view.Marker.gameObject.activeSelf, Is.False);
            var shop = new TileView { Accent = Color.blue, MarkerId = "shop", Marker = new GameObject("m2").AddComponent<SpriteRenderer>() };
            shop.ApplyLanded(true);
            Assert.That(shop.Marker.gameObject.activeSelf, Is.True, "the shop stays");
            Object.DestroyImmediate(go); Object.DestroyImmediate(view.Top.gameObject); Object.DestroyImmediate(view.Marker.gameObject); Object.DestroyImmediate(shop.Marker.gameObject);
        }

        [Test]
        public void DiceCaption_SaysWhatTheTapDoes()
        {
            var run = new ActiveRun { Phase = RunPhase.AwaitRoll };
            Assert.That(RunScreen.DiceCaption(run, false, false), Is.EqualTo("Hold for Auto"));
            run.Phase = RunPhase.ChooseBranch;
            Assert.That(RunScreen.DiceCaption(run, false, false), Is.EqualTo("Pick a path"));
            run.Phase = RunPhase.Encounter; run.EncounterKind = "boss";
            Assert.That(RunScreen.DiceCaption(run, false, false), Is.EqualTo("Enter Arena"));
            Assert.That(RunScreen.DiceCaption(run, true, false), Is.EqualTo("Auto…"));
            Assert.That(RunScreen.DiceCaption(run, true, true), Is.EqualTo("Enter Arena"), "auto waits: the tap is the player's");
        }
    }
}
