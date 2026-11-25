using System.Collections.Generic;
using NUnit.Framework;
using RogueStars.Client.Editor;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEditor;
using UnityEngine;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// Owner playtest 0.1.224 ("several background assets are floating above the ground"): markers, set pieces and scatter
    /// stand with their footprint centre on their spot (not the front edge of the base, not the transparent padding), each
    /// imported standing sprite carries that contact as its pivot, and every contact shadow is centred on the footprint.
    /// </summary>
    public sealed class GroundContactTests
    {
        private AppFixture _f;
        private ContentDb C => _f.Service.Content;

        [SetUp] public void SetUp() { _f = new AppFixture(); RunScreen.InstantOutcomes = true; RunFx.Instant = true; }

        [TearDown]
        public void TearDown()
        {
            RunScreen.InstantOutcomes = false; RunFx.Instant = false;
            _f.Dispose();
        }

        private static Color32[] Image(int w, int h, System.Func<int, int, bool> solid)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = solid(x, y) ? new Color32(200, 100, 50, 255) : new Color32(0, 0, 0, 0);
            return px;
        }

        [Test]
        public void Find_IgnoresPaddingAndSpecks_FootprintCentreAboveTheFrontEdge()
        {
            // a 40 px wide base from row 12 up to row 70 in a 100 × 100 image (12 rows of padding), plus a stray speck in row 3
            var px = Image(100, 100, (x, y) => y >= 12 && y <= 70 && x >= 30 && x < 70 || x == 50 && y == 3);
            var c = SpriteContact.Find(px, 100, 100);
            Assert.That(c.Valid, Is.True);
            Assert.That(c.Bottom, Is.EqualTo(12), "the padding and the speck are not the ground");
            Assert.That(c.Width, Is.EqualTo(40));
            Assert.That(c.X, Is.EqualTo(50));
            Assert.That(c.Y, Is.EqualTo(12 + 40 * SpriteContact.DepthShare).Within(0.01f));
            // a faint glow under the object does not count either
            var glow = Image(100, 100, (x, y) => y >= 20 && y <= 60 && x >= 40 && x < 60);
            for (int x = 0; x < 100; x++) glow[5 * 100 + x] = new Color32(255, 255, 255, 60);
            Assert.That(SpriteContact.Find(glow, 100, 100).Bottom, Is.EqualTo(20));
            Assert.That(SpriteContact.Find(Image(8, 8, (x, y) => false), 8, 8).Valid, Is.False);
        }

        [Test]
        public void EveryImportedStandingSprite_HasItsGroundContactAsPivot()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art/Resources/Art/Slice" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!ArtImportSettings.IsStanding(path)) continue;
                var c = ArtImportSettings.ContactOfFile(path, out int w, out int h);
                Assert.That(c.Valid, Is.True, path);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                Assert.That(sprite, Is.Not.Null, path);
                var want = SpriteContact.Pivot01(c, w, h);
                var have = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
                Assert.That(Vector2.Distance(have, want), Is.LessThan(0.01f), path + ": pivot " + have + " should be the contact " + want);
                // the runtime reads the same contact back from the pivot
                var back = SpriteContact.Of(sprite);
                Assert.That(back.X / sprite.rect.width, Is.EqualTo(want.x).Within(0.01f), path);
                Assert.That(back.Y / sprite.rect.height, Is.EqualTo(want.y).Within(0.01f), path);
                n++;
            }
            Assert.That(n, Is.GreaterThan(20), "markers and set pieces are imported");
        }

        [Test]
        public void Board_PropsStandOnTheirSpot_ShadowsCentredUnderThem()
        {
            for (int i = 1; i <= 4; i++) _f.Service.State.Run.Levels[C.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1 };
            _f.Service.State.Counters[Requirements.MainLevelCounter] = 4;
            var level = C.MainLevel(5);
            Assert.That(_f.Service.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal }).Ok, Is.True);
            _f.App.Router.Push(new RunScreen());
            var world = ((RunScreen)_f.App.Router.Current).World;
            int checkedProps = 0;
            foreach (var t in world.Root.GetComponentsInChildren<Transform>(true))
            {
                bool marker = t.name.StartsWith("Marker:"), piece = t.name.StartsWith("SetPiece:"), scatter = t.name.StartsWith("Scatter:");
                if (!marker && !piece && !scatter) continue;
                var spriteT = t.Find("Sprite");
                var shadowT = t.Find("Shadow");
                if (spriteT == null || shadowT == null) continue; // flat pieces have no shadow
                var sr = spriteT.GetComponent<SpriteRenderer>();
                if (sr.sprite == null) continue;
                // the footprint centre is on the holder's origin: the tile's top, the slot's floor, the ground spot
                Assert.That(WorldBoard.FootOf(sr).magnitude, Is.LessThan(0.005f), t.name + " stands on its spot");
                Assert.That(((Vector2)shadowT.localPosition).magnitude, Is.LessThan(0.005f), t.name + ": the shadow is centred under it");
                // the base's front edge lies inside the shadow (it touches): below the origin by less than the shadow's half depth
                var c = SpriteContact.Of(sr.sprite);
                float front = (c.Y - c.Bottom) / sr.sprite.pixelsPerUnit * spriteT.localScale.y;
                float shadowHalf = shadowT.GetComponent<SpriteRenderer>().sprite.bounds.size.y * shadowT.localScale.y / 2;
                Assert.That(front, Is.LessThanOrEqualTo(shadowHalf + 0.01f), t.name + ": the base sits inside its contact shadow");
                Assert.That(shadowT.GetComponent<SpriteRenderer>().sortingOrder, Is.LessThan(sr.sortingOrder), t.name + ": shadow under the prop");
                checkedProps++;
            }
            Assert.That(checkedProps, Is.GreaterThan(10));
        }
    }
}
