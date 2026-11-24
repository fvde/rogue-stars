using System;
using System.Collections.Generic;
using RogueStars.Client.Screens;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Content;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.World
{
    /// <summary>Gentle idle bob of a world object (the "!" markers, the worm): play mode only.</summary>
    public sealed class WorldBob : MonoBehaviour
    {
        public float Amplitude = 0.06f, Period = 1.4f, Phase;
        private Vector3 _base;
        private bool _has;

        private void OnEnable() { _base = transform.localPosition; _has = true; }

        private void Update()
        {
            if (!_has) return;
            transform.localPosition = _base + new Vector3(0, Amplitude * Mathf.Sin((Time.unscaledTime + Phase) * Mathf.PI * 2 / Period), 0);
        }
    }

    /// <summary>Destroys a world board with the UI object it belongs to (the run screen's root).</summary>
    public sealed class WorldLifetime : MonoBehaviour
    {
        public Action OnGone;
        private void OnDestroy() => OnGone?.Invoke();
    }

    /// <summary>What one tile of the world board is drawn with (landing tints its top and clears its marker).</summary>
    public sealed class TileView
    {
        public int Id;
        public GameObject Go;
        public SpriteRenderer Base, Top, Marker, MarkerShadow;
        public Color Accent;
        public Vector2 TopCentre;
        public string MarkerId;

        /// <summary>The landed look: the top takes the kind's colour, a beaten fight's marker is gone.</summary>
        public void ApplyLanded(bool cleared)
        {
            if (Top != null) Top.color = WorldBoard.LandedTint(Accent);
            if (Marker != null && cleared && WorldBoard.ClearsOnWin(MarkerId))
            {
                Marker.gameObject.SetActive(false);
                if (MarkerShadow != null) MarkerShadow.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// The run board as a real 2D world (vertical slice): SpriteRenderers on an iso grid seen by an orthographic camera,
    /// painter's-order sorting, rendered into a RenderTexture shown by a RawImage under the uGUI HUD. The rules' grid layout
    /// (RunTile.PosX/PosY/Height, RunSetPiece slots) places tile blocks with their markers standing on them, set pieces in
    /// the loops, scatter props along the edges, "!" over special tiles ahead and the worm. The world lives far from the
    /// scene origin on its own layer (<see cref="Layer"/>), so no other camera sees it.
    /// </summary>
    public sealed class WorldBoard
    {
        /// <summary>Render layer of every board object (only the board camera draws it).</summary>
        public const int Layer = 30;
        /// <summary>Width of the view in world units (1080 px of the reference screen: ~5.4 tiles across, 1 unit = 120 px).</summary>
        public const float ViewWidth = 9.0f;
        /// <summary>Depth window drawn around the worm (grid x + y behind / ahead).</summary>
        public const int Behind = 22, Ahead = 46;
        /// <summary>Worm height on the board (world units) and how far ahead "!" marks special tiles.</summary>
        public const float WormHeight = 1.8f;
        public const int ExclaimReach = 40, ExclaimMax = 3;

        private static int _instances;
        public readonly GameObject Root;
        public readonly Camera Cam;
        public RenderTexture Target { get; private set; }
        private GameObject _content;
        private readonly Vector3 _origin;
        private readonly Dictionary<int, TileView> _tiles = new Dictionary<int, TileView>();
        public IReadOnlyDictionary<int, TileView> Tiles => _tiles;
        public Transform Token { get; private set; }
        public Transform TokenBody { get; private set; }
        public Transform TokenShadow { get; private set; }
        private SpriteRenderer _tokenSprite;
        private SpriteRenderer _vignette;
        /// <summary>Set pieces / scatter props placed by the last build (tests).</summary>
        public readonly List<string> SetPiecesDrawn = new List<string>();
        public int ScatterCount { get; private set; }
        public int ExclaimCount { get; private set; }
        public bool Disposed { get; private set; }

        public WorldBoard(string name = "WorldBoard")
        {
            int n = _instances++;
            _origin = new Vector3(5000 + (n % 8) * 3000, -5000, 0);
            Root = new GameObject(name) { layer = Layer };
            Root.transform.position = _origin;
            var camGo = new GameObject("BoardCamera") { layer = Layer };
            camGo.transform.SetParent(Root.transform, false);
            camGo.transform.localPosition = new Vector3(0, 0, -50);
            Cam = camGo.AddComponent<Camera>();
            Cam.orthographic = true;
            Cam.cullingMask = 1 << Layer;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color32(0x5E, 0xB0, 0x92, 255);
            Cam.nearClipPlane = 0.1f;
            Cam.farClipPlane = 200;
            Cam.depth = -50;
            Cam.allowHDR = false;
            Cam.allowMSAA = false;
            Cam.orthographicSize = ViewWidth;
            // a slight vignette over the whole view (frames the diorama; sized in EnsureTarget)
            var vig = new GameObject("Vignette") { layer = Layer };
            vig.transform.SetParent(camGo.transform, false);
            vig.transform.localPosition = new Vector3(0, 0, 10);
            _vignette = vig.AddComponent<SpriteRenderer>();
            _vignette.sprite = IsoArt.Vignette();
            _vignette.sortingOrder = 32700;
            // renders only in play mode on a real graphics device (edit-mode tests and -nographics runs draw nothing)
            Cam.enabled = Application.isPlaying && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
        }

        /// <summary>The render target at <paramref name="w"/> × <paramref name="h"/> pixels (recreated when the size changes).</summary>
        public RenderTexture EnsureTarget(int w, int h)
        {
            w = Mathf.Clamp(w, 64, 2048);
            h = Mathf.Clamp(h, 64, 4096);
            if (Target == null || Target.width != w || Target.height != h)
            {
                if (Target != null) { Cam.targetTexture = null; Target.Release(); UnityEngine.Object.DestroyImmediate(Target); }
                Target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "BoardRT", antiAliasing = 1 };
                Target.Create();
                Cam.targetTexture = Target;
            }
            Cam.aspect = (float)w / h;
            Cam.orthographicSize = ViewWidth / Cam.aspect / 2;
            if (_vignette != null && _vignette.sprite != null)
            {
                var vb = _vignette.sprite.bounds.size;
                _vignette.transform.localScale = new Vector3(ViewWidth / vb.x, ViewHeight / vb.y, 1);
            }
            return Target;
        }

        /// <summary>Visible height of the view in world units.</summary>
        public float ViewHeight => Cam.orthographicSize * 2;

        /// <summary>Moves the camera (board-local world position of the view centre).</summary>
        public void SetCamera(Vector2 centre) => Cam.transform.localPosition = new Vector3(centre.x, centre.y, -50);
        public Vector2 CameraCentre => Cam.transform.localPosition;

        /// <summary>
        /// A board-local world point in an overlay rect anchored like the board (top-left origin, y down — the uGUI pins,
        /// popups and tap targets).
        /// </summary>
        public Vector2 ToOverlay(Vector2 local, Vector2 overlaySize)
        {
            var c = CameraCentre;
            float hw = ViewWidth / 2, hh = ViewHeight / 2;
            float vx = (local.x - c.x + hw) / ViewWidth, vy = (local.y - c.y + hh) / ViewHeight;
            return new Vector2(vx * overlaySize.x, -(1 - vy) * overlaySize.y);
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            if (Cam != null) Cam.targetTexture = null;
            if (Target != null) { Target.Release(); UnityEngine.Object.DestroyImmediate(Target); Target = null; }
            if (Root != null) Destroy(Root);
        }

        private static void Destroy(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(go);
            else UnityEngine.Object.DestroyImmediate(go);
        }

        // ---- look rules (pure) ---------------------------------------------------------------------------------------

        /// <summary>The landed tile's top tint (multiplied over the face colour).</summary>
        public static Color LandedTint(Color accent)
        {
            // a soft, desaturated wash: landed tiles read as visited without turning the path into a patchwork
            float g = accent.r * 0.3f + accent.g * 0.59f + accent.b * 0.11f;
            return Color.Lerp(Color.white, Color.Lerp(accent, new Color(g, g, g), 0.45f), 0.24f);
        }

        /// <summary>Markers that leave the board once their tile is resolved (a beaten enemy, a taken pickup / chest).</summary>
        public static bool ClearsOnWin(string markerId) =>
            markerId == "enemy" || markerId == "elite" || markerId == "miniboss" || markerId == "reward" || markerId == "chest" || markerId == "mystery";

        /// <summary>Markers that get a floating "!" while ahead of the worm.</summary>
        public static bool Notable(string markerId) =>
            markerId == "elite" || markerId == "miniboss" || markerId == "chest" || markerId == "rest" || markerId == "shop" || markerId == "minigame" || markerId == "event";

        /// <summary>Grid depth (x + y) of a tile.</summary>
        public static int Depth(RunTile t) => RunBoard.CellX(t) + RunBoard.CellY(t);

        /// <summary>
        /// Tiles drawn around the worm: a depth window from <see cref="Behind"/> behind to <see cref="Ahead"/> ahead; once
        /// the boss arena opened only the walked path and the arena. Hand-built boards (no layout) draw every tile.
        /// </summary>
        public static bool InWindow(ActiveRun run, RunTile t, int centreDepth)
        {
            if (run.BossGateTile >= 0 && t.Id != run.BossGateTile && !run.Trail.Contains(t.Id)) return false;
            if (t.Segment < 0) return true;
            int d = Depth(t);
            return d >= centreDepth - Behind && d <= centreDepth + Ahead;
        }

        /// <summary>
        /// Grid cell of a tile: the rules' layout, else (hand-built boards) a staircase along the column index.
        /// </summary>
        public static Vector2Int CellOf(RunTile t)
        {
            if (t.Segment >= 0) return new Vector2Int(RunBoard.CellX(t), RunBoard.CellY(t));
            // a staircase (+X, +Y, +X…); a second lane two cells to the side
            return new Vector2Int((t.X + 1) / 2 + t.Y * 2, t.X / 2);
        }

        /// <summary>Top-face centre of a tile in board-local world units.</summary>
        public static Vector2 TopOf(RunTile t)
        {
            var c = CellOf(t);
            return Iso.Cell(c.x, c.y, t.Segment >= 0 ? t.Height : 0);
        }

        /// <summary>Deterministic 0..1 per cell (scatter props).</summary>
        public static float Hash(int seed, int x, int y, int salt)
        {
            unchecked
            {
                uint h = 2166136261u ^ (uint)seed;
                h = (h ^ (uint)x) * 16777619; h = (h ^ (uint)y) * 16777619; h = (h ^ (uint)salt) * 16777619;
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>The set piece a slot shows: loops get the big ones (the portal well first), hairpins the standing ones.</summary>
        public static string SetPieceFor(RunSetPiece s, int index)
        {
            if (s.Shape == "loop" && s.W >= 2 && s.H >= 2)
                return index % 3 == 0 ? "portal_well" : index % 3 == 1 ? "crystal_spire" : "giant_mushroom";
            var standing = new[] { "giant_mushroom", "crashed_saucer", "alien_tree", "crystal_spire" };
            return standing[Math.Abs(s.X * 7 + s.Y * 13) % standing.Length];
        }

        // ---- build ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the board around the worm: tiles in the window (the landing tile of the current move keeps its unlanded
        /// look until <see cref="TileView.ApplyLanded"/>), set pieces, scatter, "!" markers, the token at
        /// <paramref name="tokenTile"/>, fork highlights (<paramref name="branchPaths"/>: option index per tile).
        /// </summary>
        public void Build(ActiveRun run, ContentDb content, int landingId, int tokenTile, IDictionary<int, int> branchPaths, int seed)
        {
            if (_content != null) Destroy(_content);
            _content = new GameObject("Content") { layer = Layer };
            _content.transform.SetParent(Root.transform, false);
            _tiles.Clear();
            SetPiecesDrawn.Clear();
            ScatterCount = 0;
            ExclaimCount = 0;
            Token = TokenBody = TokenShadow = null;

            var centre = run.Tiles[Mathf.Clamp(tokenTile, 0, run.Tiles.Count - 1)];
            int cd = Depth(centre);
            var visible = new List<RunTile>();
            foreach (var t in run.Tiles) if (InWindow(run, t, cd)) visible.Add(t);

            // the plinth: path cells and set-piece floors (cell → height); its walls run on where neighbours share a height
            var plinth = new Dictionary<Vector2Int, int>();
            foreach (var t in visible) plinth[CellOf(t)] = t.Segment >= 0 ? t.Height : 0;
            var slots = new List<KeyValuePair<int, RunSetPiece>>();
            for (int i = 0; i < run.SetPieces.Count; i++)
            {
                var s = run.SetPieces[i];
                int sd = s.X + s.Y;
                if (sd + s.W + s.H < cd - Behind || sd > cd + Ahead) continue;
                if (run.BossGateTile >= 0) continue;
                slots.Add(new KeyValuePair<int, RunSetPiece>(i, s));
                for (int x = s.X; x < s.X + s.W; x++)
                    for (int y = s.Y; y < s.Y + s.H; y++)
                        plinth[new Vector2Int(x, y)] = s.Height;
            }
            int Mask(int x, int y, int h) => IsoArt.PlinthMask((a, b) => plinth.TryGetValue(new Vector2Int(a, b), out int hh) && hh == h, x, y);

            // the island the diorama stands on (ground, cliff edge) over the dark surround; its distance field places scatter
            var field = Surround(plinth.Keys, seed);

            // set-piece slots: floors flush with the plinth and the set piece filling the slot
            foreach (var kv in slots)
            {
                var s = kv.Value;
                for (int x = s.X; x < s.X + s.W; x++)
                    for (int y = s.Y; y < s.Y + s.H; y++)
                        NewSprite("Floor", IsoArt.Block(TileFace.Floor, s.Height, Mask(x, y, s.Height)), Iso.Cell(x, y, s.Height) - new Vector2(0, Iso.Slab), Iso.Order(x, y));
                string id = SetPieceFor(s, kv.Key);
                var sprite = IsoArt.SetPiece(id);
                var foot = Iso.Cell(s.X + (s.W - 1) / 2f, s.Y + (s.H - 1) / 2f, s.Height) - new Vector2(0, Iso.Slab);
                float fpW = (s.W + s.H) * Iso.StepX;
                var go = new GameObject("SetPiece:" + id) { layer = Layer };
                go.transform.SetParent(_content.transform, false);
                go.transform.localPosition = foot;
                if (IsoArt.IsFlat(id) && ArtCatalog.SliceSetPiece(id) == null)
                {
                    // a flat piece fills the circle inscribed in the slot (its rim meets the path around it)
                    float d = Mathf.Min(s.W, s.H) * 1.414f * 1.1f;
                    Fit(go, sprite, d * Iso.StepX, d * Iso.StepY * 1.12f, Iso.Order(s.X, s.Y) + 4, false);
                }
                else
                {
                    Shadow(go, fpW * 0.62f, Iso.Order(s.X, s.Y) + 3);
                    Fit(go, sprite, fpW * 0.9f, Mathf.Min(3.0f, 1.1f + (s.W + s.H) * 0.4f), Iso.Order(s.X, s.Y) + 4, true);
                }
                SetPiecesDrawn.Add(id);
            }

            // scatter on the island around the plinth: dense ground cover along its edges, mid props further out, a few large
            // background pieces at the island's rim (never in front of the path)
            foreach (var cell in field.Cells())
            {
                float d = field.Dist(cell.x, cell.y);
                if (d < 0.5f || d > 4.2f || field.Alpha(cell.x, cell.y) < (d >= 2.5f ? 0.5f : 0.95f)) continue;
                float roll = Hash(seed, cell.x, cell.y, 1), pick = Hash(seed, cell.x, cell.y, 2);
                string sid;
                if (d < 1.5f) { if (roll >= 0.36f) continue; sid = pick < 0.82f ? Pick(SmallScatter, Hash(seed, cell.x, cell.y, 6)) : Pick(MidScatter, Hash(seed, cell.x, cell.y, 6)); }
                else if (d < 2.5f) { if (roll >= 0.16f) continue; sid = pick < 0.5f ? Pick(SmallScatter, Hash(seed, cell.x, cell.y, 6)) : Pick(MidScatter, Hash(seed, cell.x, cell.y, 6)); }
                else
                {
                    if (roll >= 0.13f) continue;
                    sid = pick < 0.6f && !PathBehind(plinth, cell) ? Pick(LargeScatter, Hash(seed, cell.x, cell.y, 6)) : Pick(MidScatter, Hash(seed, cell.x, cell.y, 6));
                }
                var off = new Vector2(Hash(seed, cell.x, cell.y, 3) - 0.5f, Hash(seed, cell.x, cell.y, 4) - 0.5f) * 0.45f;
                var pos = Iso.Cell(cell.x + off.x, cell.y + off.y, 0) - new Vector2(0, Iso.Slab + Iso.Wall);
                var go = new GameObject("Scatter:" + sid) { layer = Layer };
                go.transform.SetParent(_content.transform, false);
                go.transform.localPosition = pos;
                float size = IsoArt.ScatterHeight(sid) * (0.85f + 0.3f * Hash(seed, cell.x, cell.y, 7));
                int order = Iso.Order(cell.x + off.x, cell.y + off.y) + 3;
                var sr = Fit(go, IsoArt.Scatter(sid), size * (IsoArt.IsLargeScatter(sid) ? 0.9f : 1.7f), size, order, true, Hash(seed, cell.x, cell.y, 5) < 0.5f);
                if (sr.sprite != null) Shadow(go, Mathf.Min(1.4f, sr.bounds.size.x * 0.9f), order - 1);
                ScatterCount++;
            }

            // tiles
            int exclaims = 0;
            var exclaimOrder = RunBoard.Ahead(run.Tiles, run.Position, ExclaimReach);
            var exclaimed = new HashSet<int>();
            foreach (var id in exclaimOrder)
            {
                if (exclaims >= ExclaimMax) break;
                var t = run.Tiles[id];
                var cat = content.BoardTiles.TryGetValue(t.TileId ?? "", out var td) ? td.Category : TileCategory.Empty;
                if (t.TimesResolved > 0 || !Notable(IsoArt.MarkerOf(cat))) continue;
                exclaimed.Add(id);
                exclaims++;
            }
            foreach (var t in visible)
            {
                var look = RunScreen.LookOf(run, t, content);
                var cat = content.BoardTiles.TryGetValue(t.TileId ?? "", out var td) ? td.Category : TileCategory.Empty;
                var face = IsoArt.FaceOf(cat);
                var cell = CellOf(t);
                int h = t.Segment >= 0 ? t.Height : 0;
                int order = Iso.Order(cell.x, cell.y);
                var top = TopOf(t);
                var go = new GameObject("Tile:" + t.Id) { layer = Layer };
                go.transform.SetParent(_content.transform, false);
                go.transform.localPosition = top;
                var view = new TileView { Id = t.Id, Go = go, Accent = look.Accent, TopCentre = top };
                view.Base = Child(go, "Block", IsoArt.Block(face, h, Mask(cell.x, cell.y, h)), Vector2.zero, order);
                view.Top = Child(go, "Top", IsoArt.Top(face, t.Segment >= 0 ? RunBoard.Heading(t) : 0), Vector2.zero, order + 1);
                bool landed = look.Landed && t.Id != landingId;
                if (landed) view.Top.color = LandedTint(look.Accent);
                if (branchPaths != null && branchPaths.TryGetValue(t.Id, out int opt))
                {
                    var hl = Child(go, "BranchPath", IsoArt.Top(TileFace.Special, 0), Vector2.zero, order + 2);
                    var bc = RunScreen.BranchColor(opt);
                    hl.color = new Color(bc.r, bc.g, bc.b, look.Option ? 0.85f : 0.55f);
                    hl.transform.localScale = Vector3.one * 0.82f;
                }
                string marker = IsoArt.MarkerOf(cat);
                view.MarkerId = marker;
                if (marker != null && t.Id != tokenTile && !(landed && look.Cleared && ClearsOnWin(marker)))
                {
                    var mgo = new GameObject("Marker:" + marker) { layer = Layer };
                    mgo.transform.SetParent(go.transform, false);
                    mgo.transform.localPosition = new Vector2(0, -0.02f);
                    view.Marker = Fit(mgo, IsoArt.Marker(marker), Iso.TileW * 1.05f, IsoArt.MarkerHeight(marker), order + 3, true);
                    view.MarkerShadow = Shadow(mgo, Mathf.Min(Iso.TileW * 0.7f, view.Marker.bounds.size.x * 0.85f), order + 2);
                    if (look.Gate) mgo.transform.localScale = Vector3.one * 1.25f;
                }
                if (exclaimed.Contains(t.Id))
                {
                    float mh = marker != null ? IsoArt.MarkerHeight(marker) : 0.3f;
                    var ex = Child(go, "Exclaim", IsoArt.Exclaim(), new Vector2(0, mh + 0.25f), order + 9);
                    ex.transform.localScale = Vector3.one * 0.95f;
                    var bob = ex.gameObject.AddComponent<WorldBob>();
                    bob.Amplitude = 0.08f; bob.Phase = t.Id * 0.37f;
                    ExclaimCount++;
                }
                _tiles[t.Id] = view;
            }

            // the worm
            var tokenGo = new GameObject("Token") { layer = Layer };
            tokenGo.transform.SetParent(_content.transform, false);
            Token = tokenGo.transform;
            var shadow = Child(tokenGo, "TokenShadow", IsoArt.SoftShadow(), Vector2.zero, 0);
            shadow.transform.localScale = new Vector3(1.05f, 1.05f, 1);
            TokenShadow = shadow.transform;
            var body = new GameObject("Body") { layer = Layer };
            body.transform.SetParent(tokenGo.transform, false);
            TokenBody = body.transform;
            var hero = ArtCatalog.Hero();
            var wormGo = new GameObject("Worm") { layer = Layer };
            wormGo.transform.SetParent(body.transform, false);
            _tokenSprite = hero != null ? Fit(wormGo, hero, 2.4f, WormHeight, 0, true) : Fit(wormGo, IsoArt.Marker("portal"), 1.6f, 1.2f, 0, true);
            if (Application.isPlaying) { var bob = wormGo.AddComponent<WorldBob>(); bob.Amplitude = 0.035f; bob.Period = 1.6f; }
            PlaceToken(TopOf(run.Tiles[Mathf.Clamp(tokenTile, 0, run.Tiles.Count - 1)]), CellOf(run.Tiles[Mathf.Clamp(tokenTile, 0, run.Tiles.Count - 1)]), Vector2.one);
        }

        // ---- the island and its surround ---------------------------------------------------------------------------------

        /// <summary>Ground-cover scatter (along the plinth), mid props, and large background pieces (the island's rim).</summary>
        public static readonly string[] SmallScatter = { "fern", "flowers", "pebble_glow", "mushrooms", "rock_small", "fern" };
        public static readonly string[] MidScatter = { "bush", "rock_pile", "barrel", "bones", "crystal_small", "rock_small" };
        public static readonly string[] LargeScatter = { "stump", "antenna" };
        /// <summary>Island radius in grid cells around the plinth (before the organic wobble).</summary>
        public const float IslandRadius = 2.6f;

        private static string Pick(string[] ids, float h) => ids[Mathf.Clamp((int)(h * ids.Length), 0, ids.Length - 1)];

        /// <summary>Whether a tall prop on <paramref name="c"/> would stand in front of the path (plinth cells behind it on screen).</summary>
        private static bool PathBehind(Dictionary<Vector2Int, int> plinth, Vector2Int c)
        {
            for (int k = 1; k <= 3; k++)
                for (int s = -1; s <= 1; s++)
                    if (plinth.ContainsKey(new Vector2Int(c.x + k + (s > 0 ? 1 : 0), c.y + k + (s < 0 ? 1 : 0)))) return true;
            return false;
        }

        /// <summary>
        /// A chamfer distance field (grid cells) from the plinth over its bounding box (+6), and the island's alpha: ground
        /// within <see cref="IslandRadius"/> of the plinth, wobbled by noise into an organic outline.
        /// </summary>
        public sealed class IslandField
        {
            private readonly int _x0, _y0, _w, _h, _seed;
            private readonly float[] _d;

            public IslandField(ICollection<Vector2Int> cells, int seed)
            {
                _seed = seed;
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
                foreach (var c in cells) { x0 = Math.Min(x0, c.x); y0 = Math.Min(y0, c.y); x1 = Math.Max(x1, c.x); y1 = Math.Max(y1, c.y); }
                if (cells.Count == 0) { x0 = y0 = 0; x1 = y1 = 0; }
                _x0 = x0 - 6; _y0 = y0 - 6; _w = x1 - x0 + 13; _h = y1 - y0 + 13;
                _d = new float[_w * _h];
                for (int i = 0; i < _d.Length; i++) _d[i] = 99;
                foreach (var c in cells) _d[(c.y - _y0) * _w + (c.x - _x0)] = 0;
                const float D = 1.414f;
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                    {
                        float v = _d[y * _w + x];
                        if (x > 0) v = Math.Min(v, _d[y * _w + x - 1] + 1);
                        if (y > 0) v = Math.Min(v, _d[(y - 1) * _w + x] + 1);
                        if (x > 0 && y > 0) v = Math.Min(v, _d[(y - 1) * _w + x - 1] + D);
                        if (x < _w - 1 && y > 0) v = Math.Min(v, _d[(y - 1) * _w + x + 1] + D);
                        _d[y * _w + x] = v;
                    }
                for (int y = _h - 1; y >= 0; y--)
                    for (int x = _w - 1; x >= 0; x--)
                    {
                        float v = _d[y * _w + x];
                        if (x < _w - 1) v = Math.Min(v, _d[y * _w + x + 1] + 1);
                        if (y < _h - 1) v = Math.Min(v, _d[(y + 1) * _w + x] + 1);
                        if (x < _w - 1 && y < _h - 1) v = Math.Min(v, _d[(y + 1) * _w + x + 1] + D);
                        if (x > 0 && y < _h - 1) v = Math.Min(v, _d[(y + 1) * _w + x - 1] + D);
                        _d[y * _w + x] = v;
                    }
            }

            public int X0 => _x0;
            public int Y0 => _y0;
            public int X1 => _x0 + _w - 1;
            public int Y1 => _y0 + _h - 1;

            public IEnumerable<Vector2Int> Cells()
            {
                for (int y = _y0; y < _y0 + _h; y++)
                    for (int x = _x0; x < _x0 + _w; x++)
                        yield return new Vector2Int(x, y);
            }

            /// <summary>Distance (cells) from the plinth at a grid cell (99 outside the field).</summary>
            public float Dist(int x, int y)
            {
                x -= _x0; y -= _y0;
                return x < 0 || y < 0 || x >= _w || y >= _h ? 99 : _d[y * _w + x];
            }

            /// <summary>Bilinear distance at a fractional grid point.</summary>
            public float Dist(float gx, float gy)
            {
                int ix = Mathf.FloorToInt(gx), iy = Mathf.FloorToInt(gy);
                float fx = gx - ix, fy = gy - iy;
                float a = Dist(ix, iy), b = Dist(ix + 1, iy), c = Dist(ix, iy + 1), d = Dist(ix + 1, iy + 1);
                return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
            }

            /// <summary>Island alpha (0..1) at a fractional ground point in grid coordinates.</summary>
            public float Alpha(float gx, float gy)
            {
                float r = IslandRadius + 1.3f * (Mathf.PerlinNoise(gx * 0.27f + (_seed & 255) * 0.37f, gy * 0.27f + 11.3f) - 0.5f) * 2
                          + 0.35f * (Mathf.PerlinNoise(gx * 0.9f + 4.1f, gy * 0.9f + (_seed & 127) * 0.21f) - 0.5f);
                return Mathf.Clamp01((r - Dist(gx, gy)) / 0.16f + 0.5f);
            }
        }

        /// <summary>
        /// The island under the diorama and the dark surround framing it: the tiled backdrop (deep water / rock), a soft drop
        /// shadow and a two-band cliff below the island's front edges, and the tiled ground clipped to the island by a sprite
        /// mask. Returns the distance field (scatter).
        /// </summary>
        private IslandField Surround(ICollection<Vector2Int> cells, int seed)
        {
            var field = new IslandField(cells, seed);
            float drop = Iso.Slab + Iso.Wall;
            // the ground-plane rect of the field's grid box
            float xMin = (field.X0 - field.Y1) * Iso.StepX, xMax = (field.X1 - field.Y0) * Iso.StepX;
            float yMin = (field.X0 + field.Y0) * Iso.StepY - drop, yMax = (field.X1 + field.Y1) * Iso.StepY - drop;
            var rect = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
            var backdrop = NewSprite("Backdrop", IsoArt.Backdrop(), rect.center, -32000);
            backdrop.drawMode = SpriteDrawMode.Tiled;
            backdrop.size = rect.size + new Vector2(40, 40);
            var mask = IsoArt.IslandMask(rect, field.Alpha);
            var shadow = NewSprite("IslandShadow", mask, rect.min + new Vector2(0.18f, -1.05f), -31998);
            shadow.color = new Color(0.02f, 0.06f, 0.1f, 0.45f);
            var cliffLow = NewSprite("CliffLow", mask, rect.min + new Vector2(0, -0.85f), -31997);
            cliffLow.color = new Color32(0x25, 0x4A, 0x52, 255);
            var cliff = NewSprite("Cliff", mask, rect.min + new Vector2(0, -0.42f), -31996);
            cliff.color = new Color32(0x3E, 0x72, 0x6E, 255);
            var ground = NewSprite("Ground", IsoArt.Ground(), rect.center, -31990);
            ground.drawMode = SpriteDrawMode.Tiled;
            ground.size = rect.size;
            ground.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            var maskGo = new GameObject("IslandMask") { layer = Layer };
            maskGo.transform.SetParent(_content.transform, false);
            maskGo.transform.localPosition = rect.min;
            var sm = maskGo.AddComponent<SpriteMask>();
            sm.sprite = mask;
            sm.alphaCutoff = 0.5f;
            sm.isCustomRangeActive = true;
            sm.backSortingOrder = -31991;
            sm.frontSortingOrder = -31990;
            // a lit rim along the island's edge (the mask, nudged up, under the ground)
            var rim = NewSprite("IslandRim", mask, rect.min + new Vector2(0, -0.06f), -31995);
            rim.color = new Color32(0x9C, 0xCC, 0xA4, 255);
            return field;
        }

        /// <summary>A soft contact shadow under a prop (child of <paramref name="holder"/>), <paramref name="width"/> world units wide.</summary>
        private static SpriteRenderer Shadow(GameObject holder, float width, int order)
        {
            var sr = Child(holder, "Shadow", IsoArt.SoftShadow(), new Vector2(0.04f, 0.02f), order);
            float k = width / Mathf.Max(0.01f, IsoArt.SoftShadow().bounds.size.x);
            sr.transform.localScale = new Vector3(k, k, 1);
            return sr;
        }

        /// <summary>Moves the worm (board-local top-face point, fractional grid cell for its draw order, squash scale).</summary>
        public void PlaceToken(Vector2 at, Vector2 cell, Vector2 scale)
        {
            if (Token == null) return;
            Token.localPosition = at + new Vector2(0, 0.02f);
            if (TokenBody != null) TokenBody.localScale = new Vector3(scale.x, scale.y, 1);
            int order = Iso.Order(cell.x, cell.y) + 6;
            if (_tokenSprite != null) _tokenSprite.sortingOrder = order;
            var sh = TokenShadow != null ? TokenShadow.GetComponent<SpriteRenderer>() : null;
            if (sh != null) sh.sortingOrder = order - 1;
        }

        /// <summary>Lifts the worm's body above its shadow (hop arc) without moving the shadow.</summary>
        public void LiftToken(float lift)
        {
            if (TokenBody != null) TokenBody.localPosition = new Vector3(0, lift, 0);
        }

        // ---- sprite helpers ------------------------------------------------------------------------------------------

        private SpriteRenderer NewSprite(string name, Sprite sprite, Vector2 pos, int order)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(_content.transform, false);
            go.transform.localPosition = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        private static SpriteRenderer Child(GameObject parent, string name, Sprite sprite, Vector2 pos, int order)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>
        /// A sprite fitted into <paramref name="maxW"/> × <paramref name="maxH"/> world units on <paramref name="holder"/>;
        /// <paramref name="standing"/>: its lowest opaque row sits on the holder's origin (whatever the sprite's pivot), else
        /// centred on it.
        /// </summary>
        private static SpriteRenderer Fit(GameObject holder, Sprite sprite, float maxW, float maxH, int order, bool standing, bool flip = false)
        {
            var go = new GameObject("Sprite") { layer = Layer };
            go.transform.SetParent(holder.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            sr.flipX = flip;
            if (sprite == null) return sr;
            var b = sprite.bounds;
            float k = Mathf.Min(maxW / Mathf.Max(0.01f, b.size.x), maxH / Mathf.Max(0.01f, b.size.y));
            go.transform.localScale = new Vector3(k, k, 1);
            go.transform.localPosition = standing ? new Vector3(-b.center.x * k, -b.min.y * k, 0) : new Vector3(-b.center.x * k, -b.center.y * k, 0);
            return sr;
        }
    }
}
