using System.Collections.Generic;
using NUnit.Framework;
using RogueStars.Client.World;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Tests
{
    /// <summary>
    /// The run board's path: every arrow points from its tile to the next tile in walking order (corners: the new heading),
    /// whatever heading the tile was entered with; walls only on the plinth's exposed edges; one elevation; pits between
    /// runs filled.
    /// </summary>
    public sealed class BoardPathTests
    {
        /// <summary>A path from headings (0 +Y, 1 +X, 2 −Y, 3 −X), ids in walking order, laid out like the rules' grid.</summary>
        private static List<RunTile> Walk(string headings, int height = 0)
        {
            var tiles = new List<RunTile>();
            int x = 0, y = 0;
            for (int i = 0; i <= headings.Length; i++)
            {
                int inH = i > 0 ? headings[i - 1] - '0' : 1;
                if (i > 0) { var st = Iso.Step(inH); x += st.x; y += st.y; }
                var t = new RunTile { Id = i, PosX = x, PosY = y, Segment = 0, Height = i % 3 == 0 ? height : 0, Dir = inH == 1 ? 90 : inH == 2 ? 180 : inH == 3 ? -90 : 0 };
                if (i > 0) tiles[i - 1].Next.Add(i);
                tiles.Add(t);
            }
            return tiles;
        }

        /// <summary>The screen direction of a heading (board world units).</summary>
        private static Vector2 Screen(int heading) { var s = Iso.Step(heading); return Iso.Cell(s.x, s.y) - Iso.Cell(0, 0); }

        [TestCase("1111")]
        [TestCase("0000")]
        [TestCase("2222")]
        [TestCase("3333")]
        public void StraightRuns_PointAlongTheRun(string headings)
        {
            var tiles = Walk(headings);
            for (int i = 0; i < tiles.Count - 1; i++)
                Assert.That(WorldBoard.PathHeading(tiles[i], tiles), Is.EqualTo(headings[i] - '0'), "tile " + i);
        }

        [Test]
        public void EveryTurn_TheCornerPointsToTheNextTile_NotTheWayItWasEntered()
        {
            // all eight turns: out of every heading to the left and to the right
            foreach (var turn in new[] { "01", "03", "10", "12", "21", "23", "30", "32" })
            {
                var tiles = Walk(turn[0] + "" + turn[0] + turn[1] + turn[1]);
                var corner = tiles[2];
                Assert.That(RunBoard.Heading(corner), Is.EqualTo(turn[0] - '0'), "entered along the first heading");
                Assert.That(WorldBoard.PathHeading(corner, tiles), Is.EqualTo(turn[1] - '0'), "turn " + turn + ": the corner points on");
                Assert.That(WorldBoard.PathHeading(tiles[1], tiles), Is.EqualTo(turn[0] - '0'), "turn " + turn + ": before the corner");
                Assert.That(WorldBoard.PathHeading(tiles[3], tiles), Is.EqualTo(turn[1] - '0'), "turn " + turn + ": after it");
            }
        }

        [Test]
        public void EveryArrow_PointsAtTheNextTile_OnScreen()
        {
            var tiles = Walk("11100033300011121100011100333230001121100");
            for (int i = 0; i < tiles.Count - 1; i++)
            {
                var dir = Screen(WorldBoard.PathHeading(tiles[i], tiles)).normalized;
                var toNext = (WorldBoard.TopOf(tiles[i + 1]) - WorldBoard.TopOf(tiles[i])).normalized;
                Assert.That(Vector2.Dot(dir, toNext), Is.GreaterThan(0.999f), "tile " + i);
            }
        }

        [Test]
        public void PathEnd_KeepsTheWayItWasEntered_AndAForkPointsDownItsFirstArm()
        {
            var tiles = Walk("1100");
            Assert.That(WorldBoard.PathHeading(tiles[4], tiles), Is.EqualTo(0), "the last tile: as entered");
            // a fork at tile 1: arm A (first Next) turns +Y, arm B goes on +X
            var fork = Walk("11");
            fork.Add(new RunTile { Id = 3, PosX = 1, PosY = 1, Segment = 0, Dir = 0 });
            fork[1].Next.Clear();
            fork[1].Next.Add(3);
            fork[1].Next.Add(2);
            Assert.That(WorldBoard.PathHeading(fork[1], fork), Is.EqualTo(0));
        }

        [Test]
        public void HandBuiltBoards_PointAlongTheirStaircase()
        {
            var tiles = new List<RunTile>();
            for (int i = 0; i < 6; i++) tiles.Add(new RunTile { Id = i, X = i, Y = 0, Segment = -1 });
            for (int i = 0; i < 5; i++) tiles[i].Next.Add(i + 1);
            for (int i = 0; i < 5; i++)
            {
                var d = WorldBoard.CellOf(tiles[i + 1]) - WorldBoard.CellOf(tiles[i]);
                Assert.That(Iso.Step(WorldBoard.PathHeading(tiles[i], tiles)), Is.EqualTo(d), "tile " + i);
            }
        }

        [Test]
        public void Chevrons_AreDrawnPerHeading_CursesToo()
        {
            for (int h = 0; h < 4; h++)
            {
                Assert.That(IsoArt.Top(TileFace.Boost, h).name, Does.EndWith("_" + h));
                Assert.That(IsoArt.Top(TileFace.Curse, h).name, Does.EndWith("_" + h), "a curse points forward like a boost");
            }
            Assert.That(IsoArt.ChevronDir(1).x, Is.GreaterThan(0), "+X: up-right");
            Assert.That(IsoArt.ChevronDir(1).y, Is.GreaterThan(0));
            Assert.That(IsoArt.ChevronDir(0).x, Is.LessThan(0), "+Y: up-left");
            Assert.That(IsoArt.ChevronDir(2).x, Is.GreaterThan(0), "−Y: down-right");
            Assert.That(IsoArt.ChevronDir(2).y, Is.LessThan(0));
            Assert.That(IsoArt.ChevronDir(3).x, Is.LessThan(0), "−X: down-left");
        }

        [Test]
        public void Walls_OnlyOnExposedEdges()
        {
            // a corner: (0,0) → (1,0) → (1,1)
            var cells = new HashSet<Vector2Int> { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1) };
            int Mask(int x, int y) => IsoArt.PlinthMask((a, b) => cells.Contains(new Vector2Int(a, b)), x, y);
            // (1,0): the corner; its front-left neighbour (0,0) covers the left wall, the front-right is open
            Assert.That(IsoArt.ExposedWalls(Mask(1, 0)), Is.EqualTo(IsoArt.WallRight));
            // (0,0): nothing in front: both walls
            Assert.That(IsoArt.ExposedWalls(Mask(0, 0)), Is.EqualTo(IsoArt.WallLeft | IsoArt.WallRight));
            // (1,1): the front-right neighbour (1,0) covers the right wall
            Assert.That(IsoArt.ExposedWalls(Mask(1, 1)), Is.EqualTo(IsoArt.WallLeft));
            // the other turn: (0,0) → (0,1) → (1,1)
            cells = new HashSet<Vector2Int> { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
            Assert.That(IsoArt.ExposedWalls(Mask(0, 1)), Is.EqualTo(IsoArt.WallLeft), "the corner: (0,0) in front-right");
            Assert.That(IsoArt.ExposedWalls(Mask(1, 1)), Is.EqualTo(IsoArt.WallRight), "(0,1) in front-left");
            // back neighbours never matter
            Assert.That(IsoArt.ExposedWalls(IsoArt.NbBackLeft | IsoArt.NbBackRight), Is.EqualTo(IsoArt.WallLeft | IsoArt.WallRight));
            Assert.That(IsoArt.ExposedWalls(IsoArt.NbFrontLeft | IsoArt.NbFrontRight), Is.EqualTo(0));
        }

        [Test]
        public void BlockSprites_DropCoveredWalls()
        {
            // a block whose front edges both join the next cells draws no wall: its lowest solid row is its plinth's front
            // corner, far above the wall foot of an open block
            var open = IsoArt.Block(TileFace.Stone, 0, 0);
            var covered = IsoArt.Block(TileFace.Stone, 0, IsoArt.NbFrontLeft | IsoArt.NbFrontRight);
            float wallPx = Iso.Wall * Iso.Ppu;
            Assert.That(SpriteContact.Of(covered).Bottom - SpriteContact.Of(open).Bottom, Is.GreaterThan(wallPx * 0.8f), "no wall under a joined block");
            var half = IsoArt.Block(TileFace.Stone, 0, IsoArt.NbFrontLeft);
            Assert.That(SpriteContact.Of(half).Bottom, Is.EqualTo(SpriteContact.Of(open).Bottom).Within(6f), "one open wall still reaches the ground");
        }

        [Test]
        public void OneElevation_RaisedSectionsDrawFlat()
        {
            var tiles = Walk("1111", 1);
            Assert.That(tiles[3].Height, Is.EqualTo(1), "the rules still raise it");
            foreach (var t in tiles)
                Assert.That(WorldBoard.TopOf(t).y, Is.EqualTo(Iso.Cell((float)t.PosX, (float)t.PosY).y).Within(1e-5f), "tile " + t.Id + " on the one plinth level");
        }
    }
}
