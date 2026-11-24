using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using Xunit.Abstractions;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// The run board on an iso grid (vertical slice): every tile on its own grid cell, path neighbours are side neighbours,
/// right-angle turns, hairpins and fork loops leave set-piece slots that no tile ever takes, raised sections step by one,
/// the path stays inside the portrait band — also after many appended segments and when the boss arena opens.
/// </summary>
public class GridLayoutTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Levels() =>
        new[] { "ember_wastes_1", "ember_wastes_8", "glacier_rift_5", "static_jungle_9", "the_burrow_3", "dark_side_7" }.Select(l => new object[] { l });

    private static (int x, int y) Cell(RunTile t) => (RunBoard.CellX(t), RunBoard.CellY(t));

    /// <summary>A board with the start plus <paramref name="extra"/> appended segments.</summary>
    private static RunBoard.Board Grow(ContentDb c, LevelDef level, ulong seed, int extra)
    {
        var rng = new Rng(seed);
        var b = RunBoard.Generate(c, level, rng);
        for (int i = 0; i < extra; i++) RunBoard.AppendSegment(c, level, b, rng);
        return b;
    }

    /// <summary>Checks every grid invariant of a board; returns the board's (turns, hairpin back-steps, height steps).</summary>
    private static (int turns, int backs, int steps) AssertGrid(RunBoard.Board b, int band, string what)
    {
        var cells = new Dictionary<(int, int), int>();
        int turns = 0, backs = 0, steps = 0;
        foreach (var t in b.Tiles.Where(t => t.Segment >= 0))
        {
            Assert.Equal(Math.Round(t.PosX), t.PosX);
            Assert.Equal(Math.Round(t.PosY), t.PosY);
            Assert.InRange(t.Height, 0, 1);
            Assert.True(cells.TryAdd(Cell(t), t.Id), $"{what}: tiles {t.Id} and {cells.GetValueOrDefault(Cell(t))} share cell {Cell(t)}");
            int lat = RunBoard.Lateral(Cell(t).x, Cell(t).y);
            Assert.True(lat >= -band && lat <= band, $"{what}: tile {t.Id} ({t.TileId}, x {t.X} lane {t.Y}) outside the band: {lat}");
            foreach (var n in t.Next)
            {
                var a = Cell(t);
                var z = Cell(b.Tiles[n]);
                int dx = z.x - a.x, dy = z.y - a.y;
                Assert.True(Math.Abs(dx) + Math.Abs(dy) == 1, $"{what}: tiles {t.Id}→{n} are not grid neighbours ({a} → {z})");
                // the tile's Dir is the heading it was entered with (a join: from one of its arms)
                double want = dx == 1 ? 90 : dx == -1 ? -90 : dy == 1 ? 0 : 180;
                bool join = b.Tiles.Count(o => o.Next.Contains(n)) > 1;
                if (!join) Assert.Equal(want, Math.Abs(b.Tiles[n].Dir) == 180 ? 180 : b.Tiles[n].Dir);
                if (b.Tiles[n].Dir != t.Dir && t.Id != 0) turns++;
                if (dx < 0 || dy < 0) backs++;
                Assert.InRange(Math.Abs(b.Tiles[n].Height - t.Height), 0, 1);
                if (b.Tiles[n].Height != t.Height) steps++;
            }
        }
        // set-piece slots: free of tiles, no overlap with each other, and each is wrapped by path on several sides
        var slotCells = new HashSet<(int, int)>();
        foreach (var sp in b.SetPieces)
        {
            Assert.True(sp.W >= 1 && sp.H >= 1, $"{what}: empty slot");
            int touching = 0;
            for (int x = sp.X; x < sp.X + sp.W; x++)
                for (int y = sp.Y; y < sp.Y + sp.H; y++)
                {
                    Assert.False(cells.ContainsKey((x, y)), $"{what}: tile {cells.GetValueOrDefault((x, y))} on the {sp.Shape} slot at {(x, y)}");
                    Assert.True(slotCells.Add((x, y)), $"{what}: two set-piece slots share {(x, y)}");
                }
            for (int x = sp.X - 1; x <= sp.X + sp.W; x++)
                for (int y = sp.Y - 1; y <= sp.Y + sp.H; y++)
                    if (cells.ContainsKey((x, y))) touching++;
            Assert.True(touching >= sp.W + sp.H + 2, $"{what}: the {sp.Shape} slot at {sp.X},{sp.Y} is not wrapped by the path ({touching})");
        }
        return (turns, backs, steps);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void EveryTile_OnItsOwnCell_NeighboursAdjacent_SlotsFree(string levelId)
    {
        var c = ContentDb.Default;
        var level = c.RunLevel(levelId)!;
        int turns = 0, backs = 0, steps = 0, loops = 0, hairpins = 0;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var b = Grow(c, level, seed, 4);
            var r = AssertGrid(b, c.RunTuning.GridBand, $"{levelId} seed {seed}");
            turns += r.turns; backs += r.backs; steps += r.steps;
            loops += b.SetPieces.Count(s => s.Shape == "loop");
            hairpins += b.SetPieces.Count(s => s.Shape == "hairpin");
        }
        output.WriteLine($"{levelId}: turns {turns}, back-steps {backs}, height steps {steps}, loops {loops}, hairpins {hairpins}");
        Assert.True(turns > 100, "right-angle turns");
        Assert.True(backs > 10, "U-turns (hairpins) step back along an axis");
        Assert.True(steps > 20, "raised sections and step-downs");
        Assert.True(hairpins > 10, "hairpins wrap set-piece slots");
        if (levelId != "the_burrow_3") Assert.True(loops > 10, "fork loops wrap set-piece slots");
    }

    [Fact]
    public void ForkArms_RunAroundOneRectangle_TheJoinIsTheirCommonNeighbour()
    {
        var c = ContentDb.Default;
        var level = c.RunLevel("ember_wastes_8")!;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var b = Grow(c, level, seed, 2);
            foreach (var f in b.Tiles.Where(t => t.Next.Count > 1))
            {
                var ends = new List<int>();
                foreach (var s in f.Next)
                {
                    int cur = s;
                    while (b.Tiles[cur].Next.Count == 1 && b.Tiles.Count(t => t.Next.Contains(b.Tiles[cur].Next[0])) == 1) cur = b.Tiles[cur].Next[0];
                    ends.Add(b.Tiles[cur].Next[0]);
                }
                Assert.Equal(ends[0], ends[1]); // both arms rejoin at one tile
                var j = b.Tiles[ends[0]];
                Assert.True(RunBoard.CellX(j) > RunBoard.CellX(f) && RunBoard.CellY(j) > RunBoard.CellY(f), "the join is on the opposite corner");
            }
        }
    }

    [Fact]
    public void ManySegments_StayValid_AndKeepClimbing()
    {
        var c = ContentDb.Default;
        foreach (var levelId in new[] { "ember_wastes_3", "dark_side_7" })
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var b = Grow(c, c.RunLevel(levelId)!, seed, 14);
                AssertGrid(b, c.RunTuning.GridBand, $"{levelId} seed {seed}");
                // the path climbs: the open end is far up the screen
                var end = b.Tiles[b.EndTile];
                Assert.True(RunBoard.CellX(end) + RunBoard.CellY(end) > 14 * 20, "climbs");
            }
    }

    [Fact]
    public void ARealRun_GrowsItsBoard_OnTheGrid_AndTheArenaTakesAFreeCellNextToTheWorm()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        for (int i = 0; i < 12 && s.Run.Active != null && s.Run.Active.Phase == RunPhase.AwaitRoll; i++)
        {
            var r = RunTestKit.Exec(e, ref s, new RunRoll());
            Assert.True(r.Ok, r.Error);
            if (s.Run.Active == null || s.Run.Active.Phase != RunPhase.AwaitRoll) break;
        }
        var run = s.Run.Active!;
        var board = RunBoard.Wrap(run.Tiles, run.BoardEnd, run.BoardSegments, run.SetPieces);
        AssertGrid(board, e.Content.RunTuning.GridBand, "run");
        Assert.NotEmpty(run.SetPieces);

        var e2 = RunTestKit.Engine(RunTestKit.Strong());
        var s2 = RunTestKit.StartRun(e2);
        var run2 = s2.Run.Active!;
        run2.MinibossesSpawned = e2.Content.RunTuning.MinibossAtMeter.Count;
        run2.Awakening = 99.9;
        Assert.True(RunTestKit.Exec(e2, ref s2, new RunRoll()).Ok);
        run2 = s2.Run.Active!;
        var here = run2.Tiles[run2.Position];
        var arena = run2.Tiles[run2.BossGateTile];
        Assert.Equal(1, Math.Abs(RunBoard.CellX(arena) - RunBoard.CellX(here)) + Math.Abs(RunBoard.CellY(arena) - RunBoard.CellY(here)));
        Assert.DoesNotContain(run2.Trail, id => id != arena.Id && Cell(run2.Tiles[id]) == Cell(arena)); // never on the walked path
    }
}
