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
        public SpriteRenderer Base, Top, Marker;
        public Color Accent;
        public Vector2 TopCentre;
        public string MarkerId;

        /// <summary>The landed look: the top takes the kind's colour, a beaten fight's marker is gone.</summary>
        public void ApplyLanded(bool cleared)
        {
            if (Top != null) Top.color = WorldBoard.LandedTint(Accent);
            if (Marker != null && cleared && WorldBoard.ClearsOnWin(MarkerId)) Marker.gameObject.SetActive(false);
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
        /// <summary>Width of the view in world units (1080 px of the reference screen).</summary>
        public const float ViewWidth = 10.8f;
        /// <summary>Depth window drawn around the worm (grid x + y behind / ahead).</summary>
        public const int Behind = 22, Ahead = 46;
        /// <summary>Worm height on the board (world units) and how far ahead "!" marks special tiles.</summary>
        public const float WormHeight = 1.35f;
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
                Target = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32) { name = "BoardRT", antiAliasing = 1 };
                Target.Create();
                Cam.targetTexture = Target;
            }
            Cam.aspect = (float)w / h;
            Cam.orthographicSize = ViewWidth / Cam.aspect / 2;
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
        public static Color LandedTint(Color accent) => Color.Lerp(Color.white, accent, 0.55f);

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
            var occupied = new HashSet<Vector2Int>();
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var t in visible)
            {
                occupied.Add(CellOf(t));
                var p = TopOf(t);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            if (visible.Count == 0) { minX = maxX = minY = maxY = 0; }

            // ground: a tiled meadow under everything
            var ground = NewSprite("Ground", IsoArt.Ground(), new Vector2((minX + maxX) / 2, (minY + maxY) / 2), -32000);
            ground.drawMode = SpriteDrawMode.Tiled;
            ground.size = new Vector2(maxX - minX + 30, maxY - minY + 30);

            // set-piece slots: a platform floor and the set piece
            var slotCells = new HashSet<Vector2Int>();
            for (int i = 0; i < run.SetPieces.Count; i++)
            {
                var s = run.SetPieces[i];
                int sd = s.X + s.Y;
                if (sd + s.W + s.H < cd - Behind || sd > cd + Ahead) continue;
                if (run.BossGateTile >= 0) continue;
                for (int x = s.X; x < s.X + s.W; x++)
                    for (int y = s.Y; y < s.Y + s.H; y++)
                    {
                        var cell = new Vector2Int(x, y);
                        slotCells.Add(cell);
                        NewSprite("Floor", IsoArt.Block(TileFace.Floor, s.Height), Iso.Cell(x, y, s.Height) - new Vector2(0, Iso.Slab), Iso.Order(x, y));
                    }
                string id = SetPieceFor(s, i);
                var sprite = IsoArt.SetPiece(id);
                var foot = Iso.Cell(s.X + (s.W - 1) / 2f, s.Y + (s.H - 1) / 2f, s.Height) - new Vector2(0, Iso.Slab);
                float fpW = (s.W + s.H) * Iso.StepX, fpH = (s.W + s.H) * Iso.StepY;
                var go = new GameObject("SetPiece:" + id) { layer = Layer };
                go.transform.SetParent(_content.transform, false);
                go.transform.localPosition = foot;
                if (IsoArt.IsFlat(id) && ArtCatalog.SliceSetPiece(id) == null)
                {
                    // a flat piece fills the circle inscribed in the slot (never over the path around it)
                    float d = Mathf.Min(s.W, s.H) * 1.414f * 1.04f;
                    Fit(go, sprite, d * Iso.StepX, d * Iso.StepY * 1.12f, Iso.Order(s.X, s.Y) + 4, false);
                }
                else
                    Fit(go, sprite, fpW * 0.78f, Mathf.Min(2.6f, 0.9f + (s.W + s.H) * 0.32f), Iso.Order(s.X, s.Y) + 4, true);
                SetPiecesDrawn.Add(id);
            }

            // scatter props on free cells along the path (and a few further out)
            foreach (var t in visible)
            {
                var c = CellOf(t);
                for (int dx = -4; dx <= 4; dx++)
                    for (int dy = -4; dy <= 4; dy++)
                    {
                        var cell = new Vector2Int(c.x + dx, c.y + dy);
                        if (occupied.Contains(cell) || slotCells.Contains(cell)) continue;
                        int ring = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        bool near = ring <= 1;
                        float chance = near ? 0.15f : ring == 2 ? 0.08f : 0.05f;
                        // only one tile decides a cell (the first in the window that sees it)
                        if (!occupied.Add(cell)) continue;
                        if (Hash(seed, cell.x, cell.y, 1) >= chance) continue;
                        string sid = IsoArt.ScatterIds[(int)(Hash(seed, cell.x, cell.y, 2) * IsoArt.ScatterIds.Length) % IsoArt.ScatterIds.Length];
                        var off = new Vector2(Hash(seed, cell.x, cell.y, 3) - 0.5f, Hash(seed, cell.x, cell.y, 4) - 0.5f) * 0.5f;
                        var pos = Iso.Cell(cell.x + off.x, cell.y + off.y, 0) - new Vector2(0, Iso.Slab + Iso.Wall);
                        var go = new GameObject("Scatter:" + sid) { layer = Layer };
                        go.transform.SetParent(_content.transform, false);
                        go.transform.localPosition = pos;
                        float size = near ? 0.55f : ring == 2 ? 0.75f : 0.95f;
                        Fit(go, IsoArt.Scatter(sid), size * 1.4f, size, Iso.Order(cell.x + off.x, cell.y + off.y) + 3, true, Hash(seed, cell.x, cell.y, 5) < 0.5f);
                        ScatterCount++;
                    }
            }

            // tiles
            int exclaims = 0;
            var ahead = new HashSet<int>(RunBoard.Ahead(run.Tiles, run.Position, ExclaimReach));
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
                view.Base = Child(go, "Block", IsoArt.Block(face, h), Vector2.zero, order);
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
                    view.Marker = Fit(mgo, IsoArt.Marker(marker), Iso.TileW * 0.95f, IsoArt.MarkerHeight(marker), order + 3, true);
                    if (look.Gate) mgo.transform.localScale = Vector3.one * 1.25f;
                }
                if (exclaimed.Contains(t.Id))
                {
                    float mh = marker != null ? IsoArt.MarkerHeight(marker) : 0.3f;
                    var ex = Child(go, "Exclaim", IsoArt.Exclaim(), new Vector2(0, mh + 0.25f), order + 9);
                    ex.transform.localScale = Vector3.one * 0.85f;
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
            var shadow = Child(tokenGo, "TokenShadow", UiShapes.Circle, Vector2.zero, 0);
            shadow.color = new Color(0, 0, 0, 0.35f);
            shadow.transform.localScale = new Vector3(1.1f, 0.45f, 1);
            TokenShadow = shadow.transform;
            var body = new GameObject("Body") { layer = Layer };
            body.transform.SetParent(tokenGo.transform, false);
            TokenBody = body.transform;
            var hero = ArtCatalog.Hero();
            var wormGo = new GameObject("Worm") { layer = Layer };
            wormGo.transform.SetParent(body.transform, false);
            _tokenSprite = hero != null ? Fit(wormGo, hero, 1.6f, WormHeight, 0, true) : Fit(wormGo, IsoArt.Marker("portal"), 1.2f, 0.9f, 0, true);
            if (Application.isPlaying) { var bob = wormGo.AddComponent<WorldBob>(); bob.Amplitude = 0.035f; bob.Period = 1.6f; }
            PlaceToken(TopOf(run.Tiles[Mathf.Clamp(tokenTile, 0, run.Tiles.Count - 1)]), CellOf(run.Tiles[Mathf.Clamp(tokenTile, 0, run.Tiles.Count - 1)]), Vector2.one);
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
