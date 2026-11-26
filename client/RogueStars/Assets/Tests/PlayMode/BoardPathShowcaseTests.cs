using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RogueStars.Client.App;
using RogueStars.Client.Services;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RogueStars.Client.PlayTests
{
    /// <summary>
    /// The run board's path, straight from the board camera: a hand-laid meander with every turn (both turn directions out
    /// of all four headings) and chevron tiles on its corners, then seeded boards of several levels at a few places along
    /// their main path (corners, hairpins, forks). Explicit; run WITHOUT -nographics (ROGUESTARS_SHOTS_DIR = output folder).
    /// </summary>
    [Explicit("renders the board path screenshots; run with -testFilter BoardPathShowcaseTests")]
    public sealed class BoardPathShowcaseTests
    {
        private const int W = 1080, H = 1600;
        private const long T0 = 1_772_452_800_000L;
        /// <summary>The meander's headings (0 +Y, 1 +X, 2 −Y, 3 −X): all eight turns, every heading, runs that touch.</summary>
        public const string Meander = "11100033300011121100011100333230001121100";
        private ClientApp _app;
        private LocalGameService _svc;
        private string _dir, _shots;
        private Texture2D _tex;
        private readonly List<WorldBoard> _boards = new List<WorldBoard>();

        [UnityTest, Timeout(900000)]
        public IEnumerator Showcase()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = AppBootstrap.Current;
            _dir = Path.Combine(Application.temporaryCachePath, "bps_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _svc = new LocalGameService(_dir, "local_bps", "Wiggles", new ManualClock(T0), 11);
            _app.Start(_svc);
            string env = Environment.GetEnvironmentVariable("ROGUESTARS_SHOTS_DIR");
            _shots = !string.IsNullOrEmpty(env) ? env : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "board-path"));
            Directory.CreateDirectory(_shots);
            _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            yield return null;
            var c = _svc.Content;

            // 1. the meander: every turn, chevrons (boost / curse) on every corner and along the runs
            var run = new ActiveRun { RunId = "showcase", Tiles = MeanderTiles(c) };
            var board = NewBoard();
            board.Build(run, c, -1, 0, null, 7);
            var b = board.PathBounds;
            float width = Mathf.Max(b.width + 3.4f, (b.height + 4.2f) * W / H);
            yield return Shot(board, b.center + new Vector2(0, -0.5f), width, "path_meander_all_turns");
            // the same at the game's zoom, around the corners
            yield return Shot(board, WorldBoard.TopOf(run.Tiles[9]), WorldBoard.ViewWidth, "path_meander_zoom_a");
            yield return Shot(board, WorldBoard.TopOf(run.Tiles[22]), WorldBoard.ViewWidth, "path_meander_zoom_b");
            yield return Shot(board, WorldBoard.TopOf(run.Tiles[33]), WorldBoard.ViewWidth, "path_meander_zoom_c");

            // 2. seeded boards of several levels (biomes) at a few places along the main path
            _svc.State.Counters[Requirements.MainLevelCounter] = 30;
            for (int i = 1; i <= 30 && c.MainLevel(i) != null; i++)
                _svc.State.Run.Levels[c.MainLevelId(i)] = new LevelProgress { Cleared = true, Clears = 1, Feats = new List<bool> { true, true, false } };
            foreach (int n in new[] { 2, 6, 11, 17 })
            {
                var level = c.MainLevel(n);
                if (level == null) continue;
                if (_svc.State.Run.Active != null) _svc.ExecuteNow(new RunAbandon());
                var start = _svc.ExecuteNow(new RunStart { PortalId = level.PortalId, LevelId = level.Id, Mode = RunMode.Normal });
                Assert.That(start.Ok, Is.True, start.Error);
                run = _svc.State.Run.Active;
                var path = MainPath(run);
                foreach (int at in new[] { 3, 16, 30 })
                {
                    if (at >= path.Count) continue;
                    board = NewBoard();
                    board.Build(run, c, -1, path[at], null, 11 + n);
                    // framed on the stretch from a few tiles behind the worm to a dozen ahead
                    var mid = Vector2.zero;
                    int from = Math.Max(0, at - 3), to = Math.Min(path.Count - 1, at + 12);
                    for (int k = from; k <= to; k++) mid += WorldBoard.TopOf(run.Tiles[path[k]]);
                    yield return Shot(board, mid / (to - from + 1), 12f, "path_level" + n + "_at" + at);
                }
            }
        }

        /// <summary>The meander as board tiles: one segment, ids in walking order, chevron faces on the corners.</summary>
        public static List<RunTile> MeanderTiles(ContentDb c)
        {
            string Of(TileCategory cat) => c.BoardTiles.Values.Where(d => d.Category == cat).Select(d => d.Id).OrderBy(id => id).FirstOrDefault();
            string buff = Of(TileCategory.StatBuff), curse = Of(TileCategory.StatDebuff), enemy = Of(TileCategory.Enemy), ev = Of(TileCategory.Event);
            var tiles = new List<RunTile>();
            int x = 0, y = 0;
            for (int i = 0; i <= Meander.Length; i++)
            {
                if (i > 0)
                {
                    var st = Iso.Step(Meander[i - 1] - '0');
                    x += st.x; y += st.y;
                }
                bool corner = i > 0 && i < Meander.Length && Meander[i] != Meander[i - 1];
                string id = corner ? (i % 2 == 0 ? buff : curse) : i % 5 == 1 ? buff : i % 5 == 3 ? curse : i % 7 == 4 ? enemy : i % 11 == 6 ? ev : null;
                var t = new RunTile { Id = i, TileId = id, PosX = x, PosY = y, Segment = 0, Dir = i > 0 ? DirDeg(Meander[i - 1] - '0') : 90 };
                if (i > 0) tiles[i - 1].Next.Add(i);
                tiles.Add(t);
            }
            return tiles;
        }

        private static double DirDeg(int heading) => heading == 1 ? 90 : heading == 2 ? 180 : heading == 3 ? -90 : 0;

        private WorldBoard NewBoard()
        {
            foreach (var old in _boards) old.Dispose();
            _boards.Clear();
            var board = new WorldBoard("PathShowcase");
            _boards.Add(board);
            return board;
        }

        private IEnumerator Shot(WorldBoard board, Vector2 centre, float width, string name)
        {
            var rt = board.EnsureTarget(W, H);
            board.SetView(centre, width);
            yield return null;
            board.Cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(_shots, name + ".png"), _tex.EncodeToPNG());
        }

        private static List<int> MainPath(ActiveRun run)
        {
            var list = new List<int>();
            int id = run.Position;
            for (int guard = 0; guard < 400 && id >= 0; guard++)
            {
                list.Add(id);
                var t = run.Tiles[id];
                if (t.Next.Count == 0) break;
                id = t.Next[0];
            }
            return list;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var b in _boards) b.Dispose();
            _boards.Clear();
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            try { if (_dir != null) Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
