using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World.Anim;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.World
{
    /// <summary>Drives a <see cref="CityWorld"/> every frame while its root is active (the screen hides it when covered).</summary>
    public sealed class CityWorldDriver : MonoBehaviour
    {
        public CityWorld World;
        private void Update() { if (World != null && !World.Disposed) World.Tick(Mathf.Min(0.1f, Time.unscaledDeltaTime)); }
    }

    /// <summary>
    /// The City diorama (City design §8): built like <see cref="WorldBoard"/> — SpriteRenderers on the iso grid (<see cref="Iso.Cell"/>,
    /// painter's order <see cref="Iso.Order"/>), its own layer far from the scene origin, an orthographic camera rendering into a
    /// RenderTexture shown under the uGUI HUD. Map cell (x, y) of the rules' 20×20 grid sits at iso (H−1−y, W−1−x): the map's top
    /// row is the back-left edge, its first column the back-right edge. The land, streets and contact shadows are ONE baked
    /// texture (<see cref="CityTerrain"/>); buildings, the plaza's fire and saucer, features, lamps, walkers and FX are pooled
    /// sprites within <see cref="SpriteBudget"/>. Walkers and FX stop while the screen is hidden (its root goes inactive).
    /// </summary>
    public sealed class CityWorld
    {
        public const int Layer = 29;
        /// <summary>Zoom stops (view width in world units): overview and edit (one step in); pinches stay within Min..Max.</summary>
        public const float OverviewWidth = 12.5f, EditWidth = 9f, MinWidth = 6f, MaxWidth = 24f;
        public const int SpriteBudget = 180;
        public const int MaxLamps = 10, MaxFx = 36;
        public const float WalkerHeight = 0.74f;

        private sealed class BuildingView
        {
            public string Uid;
            public GameObject Go;
            public SpriteRenderer Body, Scaffold, Glow;
            /// <summary>The building's shader look (sway, glow pulse, smoke...), applied once it is built.</summary>
            public PropFxRef Fx;
            public Sprite Sprite;
            public Vector2 Foot;
            public float Height;
            public int Order;
            public RectInt Rect;
            public bool Active, Built;
            public float Flash;
            public float Bounce;
        }

        private sealed class WalkerView
        {
            public GameObject Go;
            public SpriteRenderer Body, Carry, Hat;
            public string Alien, Goods;
            public Vector2 LastScreen;
            public bool Left;
            /// <summary>The walker's code animation: Move while walking, Idle otherwise, Celebrate on a cheer.</summary>
            public WorldCreature Anim;
            public WalkerState LastState = WalkerState.Waiting;
        }

        private sealed class Fx
        {
            public SpriteRenderer Sr;
            public Vector2 P, V;
            public float T, Life, S0, S1, Gravity, Delay, Spin;
            public Color C;
        }

        private static int _instances;
        public readonly GameObject Root;
        public readonly Camera Cam;
        public RenderTexture Target { get; private set; }
        public float Width { get; private set; } = OverviewWidth;
        public bool Disposed { get; private set; }
        public readonly WalkerSim Sim = new WalkerSim(17);
        public readonly CityRoutes Routes = new CityRoutes();
        /// <summary>World rect of the land and its patches (the camera's pan bounds).</summary>
        public Rect LandBounds { get; private set; }
        public int TerrainBakes { get; private set; }
        public float Darkness { get; private set; }

        private readonly Transform _content;
        private readonly int _gw, _gh;
        private readonly ContentDb _c;
        private SpriteRenderer _terrain, _grid, _night, _vignette, _fire, _fireGlow, _saucer, _drill, _ghostBody;
        private Texture2D _terrainTex, _gridTex;
        private string _terrainKey;
        private float _gridAlpha = 0.15f;
        private readonly Dictionary<string, BuildingView> _buildings = new Dictionary<string, BuildingView>();
        private readonly List<SpriteRenderer> _features = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _lamps = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _lampGlows = new List<SpriteRenderer>();
        private readonly Dictionary<int, WalkerView> _walkers = new Dictionary<int, WalkerView>();
        private readonly Stack<WalkerView> _walkerPool = new Stack<WalkerView>();
        private readonly List<Fx> _fx = new List<Fx>();
        private readonly Stack<SpriteRenderer> _fxPool = new Stack<SpriteRenderer>();
        private readonly List<SpriteRenderer> _marks = new List<SpriteRenderer>();
        private int _marksUsed;
        private readonly Dictionary<string, BuildingView> _last = new Dictionary<string, BuildingView>();
        private bool _firstWalkerSync = true;
        private float _sparkClock, _dustClock, _fireClock;
        private Vector2 _drillFrom, _drillTo;
        private float _drillT = 1;
        private bool _drillHome;
        private readonly List<int> _flashCells = new List<int>();
        private float _flashLeft;
        private bool _edit;
        /// <summary>Alien generations seen (a bred child walks out of the pen with an eggshell hat).</summary>
        private readonly Dictionary<string, int> _generation = new Dictionary<string, int>();
        private Vector2 _lastFire;

        public CityWorld(ContentDb content)
        {
            _c = content;
            _gw = content.CityTuning.GridWidth;
            _gh = content.CityTuning.GridHeight;
            int n = _instances++;
            Root = new GameObject("CityWorld") { layer = Layer };
            Root.transform.position = new Vector3(-9000 - (n % 8) * 3000, -9000, 0);
            Root.AddComponent<CityWorldDriver>().World = this;
            var camGo = new GameObject("CityCamera") { layer = Layer };
            camGo.transform.SetParent(Root.transform, false);
            camGo.transform.localPosition = new Vector3(0, 0, -50);
            Cam = camGo.AddComponent<Camera>();
            Cam.orthographic = true;
            Cam.cullingMask = 1 << Layer;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color32(0x24, 0x1C, 0x3E, 255);
            Cam.nearClipPlane = 0.1f;
            Cam.farClipPlane = 200;
            Cam.depth = -49;
            Cam.allowHDR = false;
            Cam.allowMSAA = false;
            Cam.enabled = Application.isPlaying && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            _night = CamChild("Night", 32000, CityArt.Square());
            _night.color = new Color(0.05f, 0.07f, 0.26f, 0);
            _vignette = CamChild("Vignette", 32700, IsoArt.Vignette());
            var holder = new GameObject("Content") { layer = Layer };
            holder.transform.SetParent(Root.transform, false);
            _content = holder.transform;
            _terrain = Sr("Terrain", null, Vector2.zero, -30000);
            _grid = Sr("Grid", null, Vector2.zero, -29990);
            Sim.Delivered += OnDelivered;
            Sim.Social += OnSocial;
            Sim.PairDistance = 0.5f;
        }

        private SpriteRenderer CamChild(string name, int order, Sprite sprite)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(Cam.transform, false);
            go.transform.localPosition = new Vector3(0, 0, 10);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        // ---- projection ----------------------------------------------------------------------------------------------------

        /// <summary>World (board-local) position of map point (x, y) on the ground.</summary>
        public Vector2 Pos(float x, float y) => Iso.Cell((_gh - 1) - y, (_gw - 1) - x);

        /// <summary>Painter's order of something standing on map point (x, y).</summary>
        public int Order(float x, float y) => Iso.Order((_gh - 1) - y, (_gw - 1) - x);

        /// <summary>The map point under a world (board-local) position.</summary>
        public Vector2 ToGrid(Vector2 local)
        {
            float a = local.x / Iso.StepX, b = local.y / Iso.StepY;
            float gx = (a + b) / 2, gy = (b - a) / 2;
            return new Vector2((_gw - 1) - gy, (_gh - 1) - gx);
        }

        public static Vector2Int CellOf(Vector2 grid) => new Vector2Int(Mathf.FloorToInt(grid.x + 0.5f), Mathf.FloorToInt(grid.y + 0.5f));

        /// <summary>World position of a footprint's centre.</summary>
        public Vector2 FootprintCentre(int x, int y, int w, int h) => Pos(x + (w - 1) / 2f, y + (h - 1) / 2f);

        // ---- camera ---------------------------------------------------------------------------------------------------------

        public RenderTexture EnsureTarget(int w, int h)
        {
            float fit = Mathf.Min(1, Mathf.Min(2048f / Mathf.Max(1, w), 4096f / Mathf.Max(1, h)));
            w = Mathf.Max(64, Mathf.RoundToInt(w * fit));
            h = Mathf.Max(64, Mathf.RoundToInt(h * fit));
            if (Target == null || Target.width != w || Target.height != h)
            {
                if (Target != null) { Cam.targetTexture = null; Target.Release(); UnityEngine.Object.DestroyImmediate(Target); }
                Target = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32) { name = "CityRT", antiAliasing = 1 };
                Target.Create();
                Cam.targetTexture = Target;
            }
            Cam.aspect = (float)w / h;
            ApplyWidth();
            return Target;
        }

        private void ApplyWidth()
        {
            Cam.orthographicSize = Width / Cam.aspect / 2;
            float vh = ViewHeight;
            foreach (var sr in new[] { _vignette, _night })
            {
                if (sr == null || sr.sprite == null) continue;
                var b = sr.sprite.bounds.size;
                sr.transform.localScale = new Vector3(Width * 1.02f / b.x, vh * 1.02f / b.y, 1);
            }
        }

        public float ViewHeight => Cam.orthographicSize * 2;
        public Vector2 CameraCentre => Cam.transform.localPosition;

        public void SetView(Vector2 centre, float width)
        {
            Cam.transform.localPosition = new Vector3(centre.x, centre.y, -50);
            width = Mathf.Clamp(width, MinWidth, MaxWidth);
            if (Mathf.Abs(width - Width) < 1e-5f) return;
            Width = width;
            ApplyWidth();
        }

        /// <summary>A centre clamped to the land's bounds.</summary>
        public Vector2 ClampCentre(Vector2 c)
        {
            var b = LandBounds;
            if (b.width <= 0) return c;
            return new Vector2(Mathf.Clamp(c.x, b.xMin, b.xMax), Mathf.Clamp(c.y, b.yMin, b.yMax));
        }

        /// <summary>A world point in an overlay of <paramref name="size"/> anchored like the view (top-left origin, y down).</summary>
        public Vector2 ToOverlay(Vector2 local, Vector2 size)
        {
            var c = CameraCentre;
            float hw = Width / 2, hh = ViewHeight / 2;
            float vx = (local.x - c.x + hw) / Width, vy = (local.y - c.y + hh) / ViewHeight;
            return new Vector2(vx * size.x, -(1 - vy) * size.y);
        }

        /// <summary>The world point under an overlay point (the <see cref="ToOverlay"/> convention: top-left origin, y negative downward).</summary>
        public Vector2 FromOverlay(Vector2 px, Vector2 size)
        {
            var c = CameraCentre;
            float vx = px.x / Mathf.Max(1, size.x), vy = 1 + px.y / Mathf.Max(1, size.y);
            return new Vector2(c.x - Width / 2 + vx * Width, c.y - ViewHeight / 2 + vy * ViewHeight);
        }

        // ---- sync with the state --------------------------------------------------------------------------------------------

        /// <summary>Brings the diorama in line with the state (re-bakes the land only when the layout changed).</summary>
        public void Sync(GameState s, ContentDb c, long nowMs, bool edit)
        {
            _edit = edit;
            Routes.Ensure(s, c);
            var g = Routes.Grid;
            BakeIfChanged(s, c, g);
            _gridAlpha = edit ? 0.4f : 0.15f;
            if (_grid != null) _grid.color = new Color(1, 1, 1, _gridAlpha);
            SyncPlaza(c);
            SyncFeatures(s, c, g);
            SyncBuildings(s, c, g);
            SyncLamps(s, c);
            SyncDrill(s, c);
            SyncWalkers(s, c);
            SetTime(nowMs);
            UpdateWalkers();
        }

        /// <summary>Cell kinds for the painter.</summary>
        public static CityTerrain.CellKind[] Kinds(GameState s, ContentDb c, CityGrid g)
        {
            int w = g.W, h = g.H;
            var kinds = new CityTerrain.CellKind[w * h];
            var patchOf = new Dictionary<int, CityPatchDef>();
            foreach (var p in c.CityPatches) foreach (var cell in p.Cells) patchOf[cell] = p;
            for (int i = 0; i < kinds.Length; i++)
            {
                if (g.Land[i])
                {
                    kinds[i] = g.Plaza[i] ? CityTerrain.CellKind.Plaza : g.Street[i] ? CityTerrain.CellKind.Street : CityTerrain.CellKind.Land;
                    continue;
                }
                if (patchOf.TryGetValue(i, out var patch))
                    kinds[i] = CitySystem.DigBlockReason(s, c, patch.Id) == null ? CityTerrain.CellKind.RubbleOpen : CityTerrain.CellKind.Rubble;
            }
            foreach (var id in s.City.DugPatches)
            {
                var p = c.CityPatch(id);
                if (p == null) continue;
                foreach (var f in p.Features) if (f.Kind == "spring") kinds[f.Y * w + f.X] = CityTerrain.CellKind.Spring;
            }
            return kinds;
        }

        private void BakeIfChanged(GameState s, ContentDb c, CityGrid g)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(string.Join(",", s.City.DugPatches)).Append('|').Append(s.City.DigPatchId).Append('|').Append(s.City.FreeDigs).Append('|');
            var streets = new List<int>(s.City.Streets);
            streets.Sort();
            foreach (var st in streets) sb.Append(st).Append(',');
            sb.Append('|');
            foreach (var it in g.Items) sb.Append(it.X).Append(':').Append(it.Y).Append(':').Append(it.W).Append(':').Append(it.H).Append(';');
            // diggable patches change with the wallet only through the block reason (touching land / a dig running)
            string key = sb.ToString();
            if (key == _terrainKey) return;
            _terrainKey = key;
            TerrainBakes++;
            var kinds = Kinds(s, c, g);
            var shadows = new List<RectInt>();
            foreach (var it in g.Items) shadows.Add(new RectInt(it.X, it.Y, it.W, it.H));
            var bounds = CityTerrain.Bounds(kinds, g.W, g.H, Pos);
            LandBounds = Rect.MinMaxRect(bounds.xMin + 1.5f, bounds.yMin + 1f, bounds.xMax - 1.5f, bounds.yMax - 1f);
            var cv = CityTerrain.Paint(kinds, g.W, g.H, Pos, shadows, bounds);
            _terrainTex = cv.ToTexture("CityTerrain", _terrainTex);
            _terrain.sprite = Sprite.Create(_terrainTex, new Rect(0, 0, cv.W, cv.H), Vector2.zero, CityTerrain.Ppu, 0, SpriteMeshType.FullRect);
            _terrain.transform.localPosition = bounds.min;
            var gv = CityTerrain.Grid(kinds, g.W, g.H, Pos, bounds);
            _gridTex = gv.ToTexture("CityGrid", _gridTex);
            _grid.sprite = Sprite.Create(_gridTex, new Rect(0, 0, gv.W, gv.H), Vector2.zero, CityTerrain.GridPpu, 0, SpriteMeshType.FullRect);
            _grid.transform.localPosition = bounds.min;
        }

        private void SyncPlaza(ContentDb c)
        {
            var t = c.CityTuning;
            var fire = Pos(t.FireX, t.FireY);
            _lastFire = fire;
            if (_fire == null)
            {
                _fireGlow = Sr("FireGlow", CityArt.Glow(), fire + new Vector2(0, 0.35f), 32110);
                _fireGlow.transform.localScale = Vector3.one * 2.6f;
                _fire = Standing("Fire", CityArt.Fire(), fire, 1.25f, 1.3f, Order(t.FireX, t.FireY) + 5);
                _saucer = Standing("Saucer", CityArt.Saucer(), Pos(t.PlazaX + 0.1f, t.PlazaY + 0.1f), 2.0f, 1.3f, Order(t.PlazaX, t.PlazaY) + 4);
                PropFx.Apply(_fire, PropFx.LookOfSprite(_fire.sprite));
                PropFx.Apply(_saucer, PropFx.LookOfSprite(_saucer.sprite));
            }
        }

        private void SyncFeatures(GameState s, ContentDb c, CityGrid g)
        {
            foreach (var f in _features) Destroy(f.transform.parent.gameObject);
            _features.Clear();
            foreach (var p in c.CityPatches)
                foreach (var f in p.Features)
                {
                    var sprite = CityArt.Feature(f.Kind);
                    if (sprite == null) continue;
                    bool dug = s.City.DugPatches.Contains(p.Id);
                    var sr = Standing("Feature:" + f.Kind, sprite, Pos(f.X, f.Y), 1.3f, f.Kind == "crystal" ? 1.5f : 1.2f, Order(f.X, f.Y) + 5);
                    if (!dug) sr.color = new Color(0.62f, 0.58f, 0.7f, 1);
                    PropFx.Apply(sr, PropFx.LookOfSprite(sprite));
                    _features.Add(sr);
                }
        }

        private void SyncBuildings(GameState s, ContentDb c, CityGrid g)
        {
            var seen = new HashSet<string>();
            foreach (var it in g.Items)
            {
                var b = it.Building;
                if (b == null) continue;
                seen.Add(it.Uid);
                if (!_buildings.TryGetValue(it.Uid, out var v))
                {
                    v = new BuildingView { Uid = it.Uid };
                    v.Go = new GameObject("Building:" + it.Uid) { layer = Layer };
                    v.Go.transform.SetParent(_content, false);
                    _buildings[it.Uid] = v;
                }
                var foot = FootprintCentre(it.X, it.Y, it.W, it.H);
                int order = Order(it.X + it.W - 1, it.Y + it.H - 1) + 5;
                var sprite = CityArt.Building(it.Def);
                float height = CityArt.BuildingHeight(it.W, it.H); // the stand-in's height and the scaffold's
                float fpW = (it.W + it.H) * Iso.StepX;
                bool refit = v.Sprite != sprite || v.Order != order || v.Foot != foot || v.Body == null || v.Rect.width != it.W || v.Rect.height != it.H;
                bool spriteChanged = v.Sprite != sprite;
                if (refit)
                {
                    v.Go.transform.localPosition = foot;
                    if (v.Body == null)
                    {
                        var bodyGo = new GameObject("Body") { layer = Layer };
                        bodyGo.transform.SetParent(v.Go.transform, false);
                        v.Body = bodyGo.AddComponent<SpriteRenderer>();
                    }
                    v.Height = FitBuilding(v.Body, sprite, CityArt.HasOwnArt(it.Def), it.W, it.H);
                    v.Body.sortingOrder = order;
                    v.Sprite = sprite;
                    v.Foot = foot;
                    v.Order = order;
                }
                v.Rect = new RectInt(it.X, it.Y, it.W, it.H);
                v.Built = b.Level >= 1;
                // a finished building moves (per blueprint: sway, glow pulse, smoke); a site under construction stays still
                if (v.Built && (v.Fx == null || spriteChanged)) v.Fx = PropFx.Apply(v.Body, PropFx.LookOfBuilding(it.Def?.Id));
                v.Active = CitySystem.IsActive(s, c, b);
                bool timer = b.TimerId != null;
                if (timer && v.Scaffold == null)
                {
                    int size = Mathf.Clamp(Mathf.Max(it.W, it.H), 1, 3);
                    v.Scaffold = new GameObject("Scaffold") { layer = Layer }.AddComponent<SpriteRenderer>();
                    v.Scaffold.gameObject.layer = Layer;
                    v.Scaffold.transform.SetParent(v.Go.transform, false);
                    v.Scaffold.sprite = CityArt.Scaffold(size);
                    var sb = v.Scaffold.sprite.bounds.size;
                    float k = Mathf.Min(fpW * 0.95f / sb.x, height * 1.05f / sb.y);
                    v.Scaffold.transform.localScale = new Vector3(k, k, 1);
                }
                if (v.Scaffold != null)
                {
                    v.Scaffold.gameObject.SetActive(timer);
                    v.Scaffold.sortingOrder = order + 1;
                }
                if (v.Flash <= 0) v.Body.color = Tint(v);
                if (v.Glow == null)
                {
                    v.Glow = new GameObject("Glow") { layer = Layer }.AddComponent<SpriteRenderer>();
                    v.Glow.gameObject.layer = Layer;
                    v.Glow.transform.SetParent(v.Go.transform, false);
                    v.Glow.sprite = CityArt.Glow();
                    v.Glow.sortingOrder = 32100;
                }
                v.Glow.transform.localPosition = new Vector2(0, Mathf.Min(v.Height, height * 1.6f) * 0.35f);
                v.Glow.transform.localScale = Vector3.one * (0.9f + 0.25f * (it.W + it.H));
                _last[it.Uid] = v;
            }
            var gone = new List<string>();
            foreach (var kv in _buildings) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var uid in gone)
            {
                Destroy(_buildings[uid].Go);
                _buildings.Remove(uid);
            }
        }

        private Color Tint(BuildingView v)
        {
            if (!v.Built) return new Color(1, 1, 1, 0.45f);
            if (!v.Active) return new Color(0.7f, 0.7f, 0.76f, 1);
            return Color.white;
        }

        private void SyncLamps(GameState s, ContentDb c)
        {
            var cells = LampCells(s, c);
            while (_lamps.Count < cells.Count)
            {
                var lamp = Sr("Lamp", CityArt.Lamp(false), Vector2.zero, 0);
                var glow = Sr("LampGlow", CityArt.Glow(), Vector2.zero, 32105);
                glow.transform.localScale = Vector3.one * 0.9f;
                _lamps.Add(lamp);
                _lampGlows.Add(glow);
            }
            for (int i = 0; i < _lamps.Count; i++)
            {
                bool on = i < cells.Count;
                _lamps[i].gameObject.SetActive(on);
                if (!on) { _lampGlows[i].gameObject.SetActive(false); continue; }
                int cell = cells[i];
                float x = cell % _gw + 0.38f, y = cell / _gw - 0.38f;
                var p = Pos(x, y);
                _lamps[i].transform.localPosition = p;
                float sh = 0.75f / Mathf.Max(0.01f, _lamps[i].sprite.bounds.size.y);
                _lamps[i].transform.localScale = new Vector3(sh, sh, 1);
                _lamps[i].sortingOrder = Order(x, y) + 6;
                _lampGlows[i].transform.localPosition = p + new Vector2(0, 0.62f);
            }
        }

        /// <summary>Street cells that get a lamp: every few tiles of the network, at most <see cref="MaxLamps"/>.</summary>
        public static List<int> LampCells(GameState s, ContentDb c)
        {
            var list = new List<int>();
            var sorted = new List<int>(s.City.Streets);
            sorted.Sort();
            int w = c.CityTuning.GridWidth;
            foreach (var cell in sorted)
            {
                int x = cell % w, y = cell / w;
                if ((x * 3 + y * 5) % 7 != 0) continue;
                list.Add(cell);
                if (list.Count >= MaxLamps) break;
            }
            return list;
        }

        private void SyncDrill(GameState s, ContentDb c)
        {
            var patch = c.CityPatch(s.City.DigPatchId);
            if (patch == null)
            {
                if (_drill != null && _drillT >= 1) _drill.transform.parent.gameObject.SetActive(false);
                return;
            }
            var at = PatchCentre(patch);
            if (_drill == null) _drill = Standing("DrillBot", CityArt.DrillBot(), at, 1.0f, 0.9f, 0);
            var holder = _drill.transform.parent;
            holder.gameObject.SetActive(true);
            if (_drillTo != at && _drillT >= 1) { _drillTo = at; _drillFrom = at; holder.localPosition = at; }
        }

        /// <summary>Dig day: the drill bot drives from the plaza to the patch.</summary>
        public void DriveDrill(CityPatchDef patch)
        {
            if (patch == null) return;
            var at = PatchCentre(patch);
            if (_drill == null) _drill = Standing("DrillBot", CityArt.DrillBot(), _lastFire, 1.0f, 0.9f, 0);
            _drill.transform.parent.gameObject.SetActive(true);
            _drillFrom = _lastFire;
            _drillTo = at;
            _drillT = Application.isPlaying ? 0 : 1;
            _drill.transform.parent.localPosition = Application.isPlaying ? _drillFrom : _drillTo;
        }

        public Vector2 PatchCentre(CityPatchDef p)
        {
            float sx = 0, sy = 0;
            foreach (var cell in p.Cells) { sx += cell % _gw; sy += cell / _gw; }
            int n = Mathf.Max(1, p.Cells.Count);
            // the cell closest to the average (patches can be bent)
            float ax = sx / n, ay = sy / n;
            int best = p.Cells.Count > 0 ? p.Cells[0] : 0;
            float bd = float.MaxValue;
            foreach (var cell in p.Cells)
            {
                float d = (cell % _gw - ax) * (cell % _gw - ax) + (cell / _gw - ay) * (cell / _gw - ay);
                if (d < bd) { bd = d; best = cell; }
            }
            return Pos(best % _gw, best / _gw);
        }

        // ---- walkers --------------------------------------------------------------------------------------------------------

        /// <summary>Plaza ring around the fireplace (idle aliens wander / sit here).</summary>
        private List<Vector2> Ring(ContentDb c, float r)
        {
            var list = new List<Vector2>();
            float fx = c.CityTuning.FireX, fy = c.CityTuning.FireY;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4;
                list.Add(new Vector2(fx + Mathf.Cos(a) * r, fy + Mathf.Sin(a) * r));
            }
            return list;
        }

        /// <summary>
        /// One walker per working alien (commuting building ⇄ fireplace with its goods; the Launch Pod's residents come and go) and
        /// the idle ones on the plaza (by the fire at night); at most <see cref="WalkerSim.MaxWalkers"/>. A newly assigned alien
        /// starts at the fire and walks into its building (T18); a bred child wears half an eggshell for one lap of the plaza.
        /// </summary>
        public void SyncWalkers(GameState s, ContentDb c)
        {
            var wanted = new List<KeyValuePair<OwnedAlien, string>>();
            var ids = new List<string>(s.Aliens.Owned.Keys);
            ids.Sort(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var a = s.Aliens.Owned[id];
                string home = null;
                if (!string.IsNullOrEmpty(a.WorkBuildingId))
                {
                    var b = CitySystem.Building(s, a.WorkBuildingId);
                    if (b != null && b.Placed && b.Level >= 1 && Routes.ToFire(b.Uid) != null) home = b.Uid;
                }
                wanted.Add(new KeyValuePair<OwnedAlien, string>(a, home));
                if (wanted.Count >= WalkerSim.MaxWalkers) break;
            }
            var keep = new HashSet<string>();
            foreach (var kv in wanted)
            {
                var a = kv.Key;
                string home = kv.Value;
                keep.Add(a.TypeId);
                var w = Sim.Find(a.TypeId);
                bool newborn = _generation.TryGetValue(a.TypeId, out var gen) && a.Generation > gen;
                _generation[a.TypeId] = a.Generation;
                if (w != null && w.Home == home && w.Mode != WalkerMode.Sit && !newborn)
                {
                    if (home != null) w.Path = RouteFor(home); // the layout may have changed
                    continue;
                }
                if (w != null && !newborn && home == null && w.Home == null) continue;
                if (w != null) Sim.Remove(w);
                w = new Walker { AlienId = a.TypeId, Home = home };
                if (home != null)
                {
                    var def = c.Blueprint(CitySystem.Building(s, home).BlueprintId);
                    w.Mode = WalkerMode.Commute;
                    w.Goods = def?.ProduceCurrency ?? def?.Pull?.TokenCurrency;
                    w.Path = RouteFor(home);
                    float len = WalkerSim.Length(w.Path);
                    w.Speed = 1.0f + Sim.Random01() * 0.3f;
                    w.Lane = (Sim.Random01() - 0.5f) * 0.3f;
                    if (_firstWalkerSync)
                    {
                        w.S = Sim.Random01() * len;
                        w.Dir = Sim.Random01() < 0.5f ? 1 : -1;
                        w.Carrying = w.Dir > 0 && w.Goods != null;
                    }
                    else { w.S = len; w.Dir = -1; } // T18: walks from the fire into its building
                    w.Pos = WalkerSim.At(w.Path, w.S);
                }
                else
                {
                    w.Mode = WalkerMode.Wander;
                    w.Speed = 0.6f + Sim.Random01() * 0.2f;
                    w.Area = new Rect(c.CityTuning.PlazaX - 0.35f, c.CityTuning.PlazaY - 0.35f, c.CityTuning.PlazaSize - 0.3f, c.CityTuning.PlazaSize - 0.3f);
                    w.Avoid = new Vector2(c.CityTuning.FireX, c.CityTuning.FireY);
                    w.AvoidRadius = 0.75f;
                    w.Pos = w.Avoid + new Vector2(Sim.Random01() - 0.5f, Sim.Random01() - 0.5f).normalized * 1.0f;
                    Sim.NextTarget(w);
                }
                if (newborn)
                {
                    // out of the Breeding Pen with half an eggshell for a hat, one lap of the plaza
                    w.Hat = true;
                    w.Mode = WalkerMode.Wander;
                    w.Spots = Ring(c, 1.05f);
                    w.SpotIndex = 0;
                    w.Laps = 0;
                    w.Speed = 0.9f;
                    w.Pos = PenDoor(s, c) ?? w.Spots[0];
                    w.Target = w.Spots[0];
                }
                Sim.Add(w);
            }
            for (int i = Sim.Walkers.Count - 1; i >= 0; i--)
                if (!keep.Contains(Sim.Walkers[i].AlienId)) Sim.Walkers.RemoveAt(i);
            _firstWalkerSync = false;
            ApplyNight(c);
        }

        private Vector2? PenDoor(GameState s, ContentDb c)
        {
            foreach (var b in s.City.Buildings)
                if (b.Placed && b.BlueprintId == "bp_breeding_pen")
                {
                    int door = Routes.Door(b.Uid);
                    if (door >= 0) return new Vector2(door % _gw, door / _gw);
                }
            return null;
        }

        private List<Vector2> RouteFor(string uid)
        {
            var cells = Routes.ToFire(uid);
            var list = new List<Vector2>();
            if (cells == null) return list;
            foreach (var cell in cells) list.Add(new Vector2(cell % _gw, cell / _gw));
            return list;
        }

        /// <summary>At night idle aliens sit around the fire; by day they wander the plaza again.</summary>
        private void ApplyNight(ContentDb c)
        {
            bool night = Darkness > 0.5f;
            var ring = Ring(c, 0.95f);
            int i = 0;
            foreach (var w in Sim.Walkers)
            {
                if (w.Home != null || w.Hat) continue;
                if (night && w.Mode != WalkerMode.Sit)
                {
                    w.Mode = WalkerMode.Sit;
                    w.State = WalkerState.Sitting;
                    w.Pos = ring[(i * 3) % ring.Count];
                    w.FacingLeft = w.Pos.x > c.CityTuning.FireX;
                }
                else if (!night && w.Mode == WalkerMode.Sit)
                {
                    w.Mode = WalkerMode.Wander;
                    w.State = WalkerState.Walking;
                    Sim.NextTarget(w);
                }
                i++;
            }
        }

        private WalkerView ViewFor(Walker w)
        {
            if (_walkers.TryGetValue(w.Id, out var v)) return v;
            v = _walkerPool.Count > 0 ? _walkerPool.Pop() : null;
            if (v == null)
            {
                v = new WalkerView { Go = new GameObject("Walker") { layer = Layer } };
                v.Go.transform.SetParent(_content, false);
                v.Body = new GameObject("Body") { layer = Layer }.AddComponent<SpriteRenderer>();
                v.Body.transform.SetParent(v.Go.transform, false);
                v.Carry = new GameObject("Carry") { layer = Layer }.AddComponent<SpriteRenderer>();
                v.Carry.transform.SetParent(v.Go.transform, false);
                v.Hat = new GameObject("Hat") { layer = Layer }.AddComponent<SpriteRenderer>();
                v.Hat.transform.SetParent(v.Go.transform, false);
                v.Hat.sprite = CityArt.Eggshell();
            }
            v.Go.SetActive(true);
            if (v.Alien != w.AlienId)
            {
                v.Alien = w.AlienId;
                var def = _c.AlienType(w.AlienId);
                var sprite = ArtCatalog.Alien(w.AlienId) ?? HudArt.AlienCritter(def != null ? Theme.FamilyColor(def.Family) : Theme.Good);
                v.Body.sprite = sprite;
                float k = WalkerHeight / Mathf.Max(0.01f, sprite.bounds.size.y);
                v.Body.transform.localScale = new Vector3(k, k, 1);
                v.Body.transform.localPosition = new Vector3(-sprite.bounds.center.x * k, -sprite.bounds.min.y * k, 0);
                v.Anim = WorldCreature.Attach(v.Body, true);
            }
            _walkers[w.Id] = v;
            return v;
        }

        private readonly HashSet<int> _alive = new HashSet<int>();
        private readonly List<int> _dead = new List<int>();

        /// <summary>Places the walker sprites (no allocations per frame: the sets are reused).</summary>
        private void UpdateWalkers()
        {
            var alive = _alive;
            alive.Clear();
            foreach (var w in Sim.Walkers)
            {
                alive.Add(w.Id);
                var v = ViewFor(w);
                if (!w.Visible) { v.Go.SetActive(false); continue; }
                v.Go.SetActive(true);
                var p = w.Pos;
                // a lane offset across the walking direction keeps two walkers on one street apart
                var world = Pos(p.x + w.Lane, p.y - w.Lane) + new Vector2(0, 0.02f);
                float bob = Application.isPlaying ? Mathf.Abs(Mathf.Sin(Time.unscaledTime * 7 + w.Phase)) * 0.04f : 0;
                float lift = w.Hop * 0.22f + (w.State == WalkerState.Walking ? bob : 0);
                v.Go.transform.localPosition = world + new Vector2(0, lift);
                int order = Order(p.x, p.y) + 7;
                v.Body.sortingOrder = order;
                // face by screen direction while moving; social / cheer face the partner (sim FacingLeft = owner x)
                if (w.State == WalkerState.Walking)
                {
                    float dx = world.x - v.LastScreen.x;
                    if (Mathf.Abs(dx) > 0.002f) v.Left = dx < 0;
                }
                else if (w.State == WalkerState.Sitting) v.Left = world.x > _lastFire.x;
                v.LastScreen = world;
                v.Body.flipX = v.Left;
                if (v.Anim != null)
                {
                    if (w.State == WalkerState.Cheer && v.LastState != WalkerState.Cheer) v.Anim.Play(CreatureClip.Celebrate);
                    else v.Anim.Loop(w.State == WalkerState.Walking ? CreatureClip.Move : CreatureClip.Idle);
                    v.LastState = w.State;
                }
                bool carry = w.Carrying && w.Goods != null && w.State != WalkerState.Inside;
                v.Carry.gameObject.SetActive(carry);
                if (carry && v.Goods != w.Goods)
                {
                    v.Goods = w.Goods;
                    v.Carry.sprite = CityArt.Goods(w.Goods);
                    float ck = 0.3f / Mathf.Max(0.01f, v.Carry.sprite.bounds.size.y);
                    v.Carry.transform.localScale = new Vector3(ck, ck, 1);
                    v.Carry.transform.localPosition = new Vector3(0, WalkerHeight + 0.14f, 0);
                }
                if (carry) v.Carry.sortingOrder = order + 1;
                v.Hat.gameObject.SetActive(w.Hat);
                if (w.Hat)
                {
                    float hk = 0.3f / Mathf.Max(0.01f, v.Hat.sprite.bounds.size.x);
                    v.Hat.transform.localScale = new Vector3(hk, hk, 1);
                    v.Hat.transform.localPosition = new Vector3(0, WalkerHeight * 0.86f, 0);
                    v.Hat.sortingOrder = order + 1;
                    if (w.Laps >= 1)
                    {
                        w.Hat = false;
                        w.Spots = null;
                        if (w.Home != null)
                        {
                            w.Mode = WalkerMode.Commute;
                            w.Path = RouteFor(w.Home);
                            w.S = WalkerSim.Length(w.Path);
                            w.Dir = -1;
                        }
                        else { w.Mode = WalkerMode.Wander; Sim.NextTarget(w); }
                    }
                }
            }
            var dead = _dead;
            dead.Clear();
            foreach (var kv in _walkers) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var id in dead)
            {
                var v = _walkers[id];
                v.Go.SetActive(false);
                _walkerPool.Push(v);
                _walkers.Remove(id);
            }
        }

        private void OnDelivered(Walker w)
        {
            // the goods pop at the fire: a small burst in the resource's colour
            var at = _lastFire + new Vector2(0, 0.5f);
            Spawn("ring", at, Vector2.zero, 0.45f, 0.2f, 0.6f, Theme.CurrencyColor(w.Goods), 31000);
            Delivered?.Invoke(w.Goods);
        }

        /// <summary>A walker delivered goods at the fire (the screen ticks the bar pill).</summary>
        public event Action<string> Delivered;

        private void OnSocial(Walker a, Walker b, int kind)
        {
            var mid = (a.Pos + b.Pos) / 2;
            var at = Pos(mid.x, mid.y) + new Vector2(0, WalkerHeight + 0.15f);
            var fx = Spawn("bubble:" + WalkerSim.SocialKinds[kind], at, new Vector2(0, 0.12f), Sim.StopTime, 0.2f, 0.55f, Color.white, 32200);
            if (fx != null) fx.Gravity = 0;
            SocialShown++;
        }

        /// <summary>Social bubbles shown (tests / showcase).</summary>
        public int SocialShown { get; private set; }

        // ---- ticking ----------------------------------------------------------------------------------------------------------

        public void SetTime(long nowMs)
        {
            float d = CityDayNight.Darkness(nowMs);
            bool flip = (d > 0.5f) != (Darkness > 0.5f);
            Darkness = d;
            if (_night != null) _night.color = new Color(0.04f, 0.06f, 0.22f, 0.62f * d);
            bool lit = d > 0.3f;
            for (int i = 0; i < _lamps.Count; i++)
            {
                if (!_lamps[i].gameObject.activeSelf) continue;
                _lamps[i].sprite = CityArt.Lamp(lit);
                _lampGlows[i].gameObject.SetActive(lit);
                _lampGlows[i].color = new Color(1, 1, 1, 0.75f * d);
            }
            foreach (var v in _buildings.Values)
                if (v.Glow != null)
                {
                    bool on = lit && v.Built && v.Active;
                    v.Glow.gameObject.SetActive(on);
                    v.Glow.color = new Color(1, 1, 1, 0.6f * d);
                }
            if (_fireGlow != null) _fireGlow.color = new Color(1, 1, 1, 0.35f + 0.55f * d);
            if (flip) ApplyNight(_c);
        }

        public void Tick(float dt)
        {
            if (Disposed) return;
            Sim.Step(dt);
            UpdateWalkers();
            _fireClock += dt;
            if (_fire != null) _fire.transform.parent.localScale = new Vector3(1, 1 + 0.04f * Mathf.Sin(_fireClock * 11), 1);
            if (_fireGlow != null) _fireGlow.transform.localScale = Vector3.one * (2.6f + 0.15f * Mathf.Sin(_fireClock * 7.3f));
            // the drill bot drives to its patch, then drills (dust puffs)
            if (_drill != null && _drill.gameObject.activeInHierarchy)
            {
                var holder = _drill.transform.parent;
                if (_drillT < 1)
                {
                    _drillT = Mathf.Min(1, _drillT + dt / 2.2f);
                    holder.localPosition = Vector2.Lerp(_drillFrom, _drillTo, Mathf.SmoothStep(0, 1, _drillT));
                    if (_drillT >= 1 && _drillHome) { _drillHome = false; holder.gameObject.SetActive(false); }
                }
                else holder.localPosition = _drillTo + new Vector2(Mathf.Sin(_fireClock * 30) * 0.02f, 0);
                var g = ToGrid(holder.localPosition);
                _drill.sortingOrder = Order(g.x, g.y) + 8;
                _dustClock += dt;
                if (_drillT >= 1 && _dustClock > 0.45f)
                {
                    _dustClock = 0;
                    Spawn("dust", _drillTo + new Vector2(0.35f, 0.1f), new Vector2(UnityEngine.Random.Range(-0.2f, 0.4f), 0.5f), 0.9f, 0.25f, 0.6f, Color.white, 31000);
                }
            }
            // construction: sparks from the scaffolds
            _sparkClock += dt;
            if (_sparkClock > 0.6f)
            {
                _sparkClock = 0;
                foreach (var v in _buildings.Values)
                    if (v.Scaffold != null && v.Scaffold.gameObject.activeSelf)
                        Spawn("spark", v.Foot + new Vector2(UnityEngine.Random.Range(-0.4f, 0.4f), v.Height * UnityEngine.Random.Range(0.3f, 0.8f)),
                            new Vector2(UnityEngine.Random.Range(-0.6f, 0.6f), 0.8f), 0.4f, 0.16f, 0.05f, new Color(1f, 0.85f, 0.35f), 31000).Gravity = 3;
            }
            // building flashes (refused erase) and bounces (just built)
            foreach (var v in _buildings.Values)
            {
                if (v.Flash > 0)
                {
                    v.Flash -= dt;
                    float k = Mathf.Abs(Mathf.Sin(v.Flash * 12));
                    v.Body.color = v.Flash > 0 ? Color.Lerp(Tint(v), new Color(1, 0.3f, 0.3f, 1), k) : Tint(v);
                }
                if (v.Bounce > 0)
                {
                    v.Bounce = Mathf.Max(0, v.Bounce - dt);
                    float t = 1 - v.Bounce / 0.5f;
                    float sy = 1 + 0.18f * Mathf.Sin(t * Mathf.PI) * (1 - t);
                    v.Go.transform.localScale = new Vector3(2 - sy, sy, 1);
                }
                else v.Go.transform.localScale = Vector3.one;
            }
            if (_flashLeft > 0)
            {
                _flashLeft -= dt;
                if (_flashLeft <= 0) { _flashCells.Clear(); RedrawMarks(); }
            }
            UpdateFx(dt);
        }

        // ---- marks: ghost tiles, street previews, the dig frame, red flashes ------------------------------------------------------

        public struct Mark
        {
            public int X, Y;
            public Color Color;
            public bool Ring;
        }

        private readonly List<Mark> _markList = new List<Mark>();

        /// <summary>Tiles drawn flat on the ground (the ghost's footprint, a street stroke, the dig frame).</summary>
        public void SetMarks(IList<Mark> marks)
        {
            _markList.Clear();
            if (marks != null) _markList.AddRange(marks);
            RedrawMarks();
        }

        private void RedrawMarks()
        {
            _marksUsed = 0;
            foreach (var m in _markList) PlaceMark(m.X, m.Y, m.Color, m.Ring);
            foreach (var cell in _flashCells) PlaceMark(cell % _gw, cell / _gw, new Color(1f, 0.25f, 0.25f, 0.8f), false);
            for (int i = _marksUsed; i < _marks.Count; i++) _marks[i].gameObject.SetActive(false);
        }

        private void PlaceMark(int x, int y, Color color, bool ring)
        {
            if (_marksUsed >= _marks.Count) _marks.Add(Sr("Mark", CityArt.Tile(), Vector2.zero, -29980));
            var sr = _marks[_marksUsed++];
            sr.gameObject.SetActive(true);
            sr.sprite = ring ? CityArt.TileRing() : CityArt.Tile();
            sr.color = color;
            sr.transform.localPosition = Pos(x, y);
            sr.transform.localScale = new Vector3(ring ? 1 : 0.94f, ring ? 1 : 0.94f, 1);
            sr.sortingOrder = ring ? -29970 : -29980;
        }

        /// <summary>Flashes cells and buildings red (a refused erase: what it would cut off).</summary>
        public void Flash(IEnumerable<int> cells, IEnumerable<string> uids)
        {
            _flashCells.Clear();
            if (cells != null) _flashCells.AddRange(cells);
            _flashLeft = 0.9f;
            if (uids != null) foreach (var u in uids) if (_buildings.TryGetValue(u, out var v)) v.Flash = 0.9f;
            RedrawMarks();
        }

        /// <summary>The ghost building over its footprint (null hides it); dims the original while moving it.</summary>
        public void SetGhost(BlueprintDef def, int x, int y, bool rotated, bool ok, string movingUid)
        {
            foreach (var v in _buildings.Values) if (v.Flash <= 0) v.Body.color = v.Uid == movingUid ? new Color(1, 1, 1, 0.25f) : Tint(v);
            if (def == null)
            {
                if (_ghostBody != null) _ghostBody.transform.parent.gameObject.SetActive(false);
                return;
            }
            CityLayout.Footprint(def, rotated, out int w, out int h);
            var foot = FootprintCentre(x, y, w, h);
            if (_ghostBody == null) _ghostBody = Standing("Ghost", CityArt.Building(def), foot, 1, 1, 0);
            var holder = _ghostBody.transform.parent;
            holder.gameObject.SetActive(true);
            holder.localPosition = foot;
            FitBuilding(_ghostBody, CityArt.Building(def), CityArt.HasOwnArt(def), w, h);
            _ghostBody.sortingOrder = 31500;
            _ghostBody.color = ok ? new Color(1, 1, 1, 0.82f) : new Color(1, 0.7f, 0.7f, 0.7f);
        }

        // ---- moments ------------------------------------------------------------------------------------------------------------

        /// <summary>T06: the scaffold drops away with a dust poof and confetti, the building bounces in.</summary>
        public void Built(string uid)
        {
            if (!_buildings.TryGetValue(uid, out var v)) return;
            if (v.Scaffold != null)
            {
                var sc = v.Scaffold;
                var fx = Spawn("sprite", (Vector2)v.Go.transform.localPosition, new Vector2(0, -0.6f), 0.5f, 1, 1, Color.white, v.Order + 2);
                if (fx != null) { fx.Sr.sprite = sc.sprite; fx.Sr.transform.localScale = sc.transform.localScale; fx.S0 = fx.S1 = sc.transform.localScale.x; fx.Gravity = 4; }
                sc.gameObject.SetActive(false);
            }
            for (int i = 0; i < 6; i++)
                Spawn("poof", v.Foot + new Vector2(UnityEngine.Random.Range(-0.6f, 0.6f), UnityEngine.Random.Range(0, 0.3f)), new Vector2(UnityEngine.Random.Range(-0.5f, 0.5f), 0.4f), 0.7f, 0.3f, 0.8f, new Color(1, 1, 1, 0.9f), 31000);
            Confetti(v.Foot + new Vector2(0, v.Height * 0.8f), 14);
            v.Bounce = 0.5f;
        }

        /// <summary>T07 / level-up party: confetti over the building, nearby walkers hop and cheer; a star-up adds a gold ring.</summary>
        public void Party(string uid, bool starUp = false)
        {
            if (!_buildings.TryGetValue(uid, out var v)) return;
            Confetti(v.Foot + new Vector2(0, v.Height * 0.9f), 18);
            if (starUp) Spawn("ring", v.Foot + new Vector2(0, v.Height * 0.5f), Vector2.zero, 0.9f, 0.4f, 2.6f, Theme.Gold, 31010);
            var g = ToGrid(v.Foot);
            Sim.Cheer(g, 4.5f);
        }

        /// <summary>T09: the stored building shrinks away (the screen flies its card into the tray).</summary>
        public Vector2? Stored(string uid)
        {
            if (!_last.TryGetValue(uid, out var v) || v.Sprite == null) return null;
            var fx = Spawn("sprite", v.Foot, new Vector2(0, 0.3f), 0.45f, v.Body.transform.localScale.x, 0.05f, Color.white, 31000);
            if (fx != null) fx.Sr.sprite = v.Sprite;
            return v.Foot + new Vector2(0, v.Height * 0.5f);
        }

        /// <summary>T10 / dig day: the new patch's rubble crumbles tile by tile from the edge that touched the land (0.05 s each).</summary>
        public void Crumble(CityPatchDef patch, GameState s, ContentDb c)
        {
            if (patch == null) return;
            var order = CrumbleOrder(patch, s, c);
            for (int i = 0; i < order.Count; i++)
            {
                int cell = order[i];
                var at = Pos(cell % _gw, cell / _gw);
                var tile = Spawn("tile", at, Vector2.zero, 0.35f, 0.94f, 0.4f, new Color32(0x8C, 0x80, 0x98, 255), -29975);
                if (tile != null) { tile.Delay = i * 0.05f; tile.Gravity = 0; }
                for (int k = 0; k < 2; k++)
                {
                    var rock = Spawn("rock", at, new Vector2(UnityEngine.Random.Range(-0.8f, 0.8f), UnityEngine.Random.Range(0.6f, 1.3f)), 0.6f, 0.22f, 0.12f, Color.white, 31000);
                    if (rock != null) { rock.Delay = i * 0.05f; rock.Gravity = 4; rock.Spin = UnityEngine.Random.Range(-360, 360); }
                }
            }
            if (_drill != null && Application.isPlaying) { _drillFrom = _drillTo; _drillTo = _lastFire; _drillT = 0; _drillHome = true; }
        }

        /// <summary>Patch cells ordered by distance from the land (the edge that touched the land crumbles first).</summary>
        public static List<int> CrumbleOrder(CityPatchDef patch, GameState s, ContentDb c)
        {
            int w = c.CityTuning.GridWidth;
            var dist = new Dictionary<int, int>();
            var q = new Queue<int>();
            foreach (var cell in patch.Cells)
            {
                int x = cell % w, y = cell / w;
                bool touches = false;
                foreach (var n in new[] { cell + 1, cell - 1, cell + w, cell - w })
                    if (n >= 0 && n < w * c.CityTuning.GridHeight && !patch.Cells.Contains(n) && Math.Abs(n % w - x) + Math.Abs(n / w - y) == 1 && CityLayout.IsLand(s, c, n)) touches = true;
                if (touches) { dist[cell] = 0; q.Enqueue(cell); }
            }
            if (q.Count == 0 && patch.Cells.Count > 0) { dist[patch.Cells[0]] = 0; q.Enqueue(patch.Cells[0]); }
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                foreach (var n in new[] { cur + 1, cur - 1, cur + w, cur - w })
                {
                    if (!patch.Cells.Contains(n) || dist.ContainsKey(n)) continue;
                    if (Math.Abs(n % w - cur % w) + Math.Abs(n / w - cur / w) != 1) continue;
                    dist[n] = dist[cur] + 1;
                    q.Enqueue(n);
                }
            }
            var list = new List<int>(patch.Cells);
            list.Sort((a, b) => (dist.TryGetValue(a, out var da) ? da : 99).CompareTo(dist.TryGetValue(b, out var db) ? db : 99));
            return list;
        }

        public void Confetti(Vector2 at, int n)
        {
            var colors = new[] { Theme.Gold, Theme.Good, Theme.Purple, Theme.Premium, Theme.Worm, Theme.Accent };
            for (int i = 0; i < n; i++)
            {
                var fx = Spawn("confetti", at, new Vector2(UnityEngine.Random.Range(-1.4f, 1.4f), UnityEngine.Random.Range(1.2f, 2.6f)), 1.1f, 0.14f, 0.12f, colors[i % colors.Length], 31020);
                if (fx != null) { fx.Gravity = 3.5f; fx.Spin = UnityEngine.Random.Range(-540, 540); }
            }
        }

        /// <summary>A short poof at a building (placed, moved).</summary>
        public void Poof(string uid)
        {
            if (!_buildings.TryGetValue(uid, out var v)) return;
            for (int i = 0; i < 4; i++)
                Spawn("poof", v.Foot + new Vector2(UnityEngine.Random.Range(-0.5f, 0.5f), 0.1f), new Vector2(UnityEngine.Random.Range(-0.4f, 0.4f), 0.3f), 0.5f, 0.25f, 0.6f, new Color(1, 1, 1, 0.85f), 31000);
        }

        // ---- FX pool --------------------------------------------------------------------------------------------------------------

        private Fx Spawn(string kind, Vector2 at, Vector2 vel, float life, float s0, float s1, Color color, int order)
        {
            if (_fx.Count >= MaxFx) { Release(_fx[0]); _fx.RemoveAt(0); }
            var sr = _fxPool.Count > 0 ? _fxPool.Pop() : Sr("Fx", null, Vector2.zero, 0);
            sr.gameObject.SetActive(true);
            sr.sprite = kind == "sprite" ? null : kind == "tile" ? CityArt.Tile() : kind.StartsWith("bubble:", StringComparison.Ordinal)
                ? CityArt.SocialBubble(kind.Substring(7)) : CityArt.Fx(kind);
            sr.sortingOrder = order;
            sr.color = color;
            sr.transform.localPosition = at;
            sr.transform.localRotation = Quaternion.identity;
            float scale = s0;
            sr.transform.localScale = new Vector3(scale, scale, 1);
            var fx = new Fx { Sr = sr, P = at, V = vel, Life = life, S0 = s0, S1 = s1, C = color, Gravity = 1.2f };
            _fx.Add(fx);
            if (!Application.isPlaying) { } // edit mode: they stay until the next tick (tests count them)
            return fx;
        }

        private void Release(Fx fx)
        {
            if (fx.Sr == null) return;
            fx.Sr.gameObject.SetActive(false);
            _fxPool.Push(fx.Sr);
        }

        private void UpdateFx(float dt)
        {
            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                var f = _fx[i];
                if (f.Delay > 0)
                {
                    f.Delay -= dt;
                    continue;
                }
                f.T += dt;
                if (f.T >= f.Life) { Release(f); _fx.RemoveAt(i); continue; }
                float k = f.T / f.Life;
                f.V.y -= f.Gravity * dt;
                f.P += f.V * dt;
                f.Sr.transform.localPosition = f.P;
                float pop = k < 0.15f ? Mathf.Lerp(0.6f, 1.1f, k / 0.15f) : 1;
                float s = Mathf.Lerp(f.S0, f.S1, k) * pop;
                f.Sr.transform.localScale = new Vector3(s, s, 1);
                if (f.Spin != 0) f.Sr.transform.localRotation = Quaternion.Euler(0, 0, f.Spin * f.T);
                f.Sr.color = new Color(f.C.r, f.C.g, f.C.b, f.C.a * (k > 0.6f ? 1 - (k - 0.6f) / 0.4f : 1));
            }
        }

        /// <summary>FX alive (tests).</summary>
        public int FxCount => _fx.Count;

        // ---- queries ---------------------------------------------------------------------------------------------------------------

        /// <summary>Where a building's pins sit (world): over its top.</summary>
        public Vector2? PinAnchor(string uid)
        {
            if (!_buildings.TryGetValue(uid, out var v)) return null;
            return v.Foot + new Vector2(0, v.Height * 0.95f);
        }

        /// <summary>The building's body sprite renderer (tests, showcase), or null.</summary>
        public SpriteRenderer BodyOf(string uid) => _buildings.TryGetValue(uid, out var v) ? v.Body : null;

        /// <summary>How high the building stands (world units, ground to top), or 0.</summary>
        public float HeightOf(string uid) => _buildings.TryGetValue(uid, out var v) ? v.Height : 0;

        public Vector2? FootOf(string uid) => _buildings.TryGetValue(uid, out var v) ? v.Foot : (Vector2?)null;

        public bool HasBuilding(string uid) => _buildings.ContainsKey(uid);
        public int BuildingCount => _buildings.Count;
        public bool HasScaffold(string uid) => _buildings.TryGetValue(uid, out var v) && v.Scaffold != null && v.Scaffold.gameObject.activeSelf;
        public bool IsDim(string uid) => _buildings.TryGetValue(uid, out var v) && v.Built && !v.Active;
        public bool DrillVisible => _drill != null && _drill.gameObject.activeInHierarchy;
        public int LampsLit { get { int n = 0; foreach (var g in _lampGlows) if (g.gameObject.activeSelf) n++; return n; } }
        public int WalkersVisible { get { int n = 0; foreach (var v in _walkers.Values) if (v.Go.activeSelf) n++; return n; } }
        public int MarkCount => _marksUsed;

        /// <summary>Active sprite renderers of the diorama (the ≤ <see cref="SpriteBudget"/> budget).</summary>
        public int ActiveSprites
        {
            get
            {
                int n = 0;
                foreach (var sr in Root.GetComponentsInChildren<SpriteRenderer>(false)) if (sr.enabled && sr.sprite != null) n++;
                return n;
            }
        }

        // ---- sprite helpers --------------------------------------------------------------------------------------------------------

        private SpriteRenderer Sr(string name, Sprite sprite, Vector2 pos, int order)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(_content, false);
            go.transform.localPosition = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>A standing sprite on a holder at <paramref name="foot"/> (its footprint centre on the holder's origin).</summary>
        private SpriteRenderer Standing(string name, Sprite sprite, Vector2 foot, float maxW, float maxH, int order)
        {
            var holder = new GameObject(name) { layer = Layer };
            holder.transform.SetParent(_content, false);
            holder.transform.localPosition = foot;
            var go = new GameObject("Sprite") { layer = Layer };
            go.transform.SetParent(holder.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            FitStanding(sr, sprite, maxW, maxH);
            return sr;
        }

        /// <summary>
        /// Seats a building on its w × h footprint (the parent's origin = the footprint centre) and returns how high it stands
        /// (world units, ground to top). Own building art: the trimmed sprite is as wide as its foundation plate, so the plate
        /// spans <see cref="CityArt.PlateFill"/> of the footprint's width (an iso w × h footprint's bounding box is (w + h) steps
        /// wide whatever the rotation), capped at <see cref="CityArt.BuildingMaxHeight"/>; the plate's centre (the import pivot)
        /// sits on the footprint centre. Stand-ins: fitted into the footprint width × <see cref="CityArt.BuildingHeight"/>.
        /// </summary>
        public static float FitBuilding(SpriteRenderer sr, Sprite sprite, bool own, int w, int h)
        {
            float fpW = (w + h) * Iso.StepX;
            if (!own || sprite == null)
            {
                FitStanding(sr, sprite, fpW * 0.92f, CityArt.BuildingHeight(w, h));
                return CityArt.BuildingHeight(w, h);
            }
            var b = sprite.bounds;
            float k = Mathf.Min(fpW * CityArt.PlateFill / Mathf.Max(0.01f, b.size.x), CityArt.BuildingMaxHeight(w, h) / Mathf.Max(0.01f, b.size.y));
            FitStanding(sr, sprite, b.size.x * k, b.size.y * k);
            var c = SpriteContact.Of(sprite);
            return (sprite.rect.height - c.Y) / sprite.pixelsPerUnit * k;
        }

        /// <summary>Fits a sprite into maxW × maxH with its ground contact (<see cref="SpriteContact"/>) on the parent's origin.</summary>
        public static void FitStanding(SpriteRenderer sr, Sprite sprite, float maxW, float maxH)
        {
            sr.sprite = sprite;
            if (sprite == null) return;
            var b = sprite.bounds;
            float k = Mathf.Min(maxW / Mathf.Max(0.01f, b.size.x), maxH / Mathf.Max(0.01f, b.size.y));
            sr.transform.localScale = new Vector3(k, k, 1);
            var c = SpriteContact.Of(sprite);
            float ppu = sprite.pixelsPerUnit;
            float cx = (c.X - sprite.pivot.x) / ppu, cy = (c.Y - sprite.pivot.y) / ppu;
            sr.transform.localPosition = new Vector3(-cx * k, -cy * k, 0);
        }

        private static void Destroy(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(go);
            else UnityEngine.Object.DestroyImmediate(go);
        }

        public void SetActive(bool on) { if (Root != null) Root.SetActive(on); }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            if (Cam != null) Cam.targetTexture = null;
            if (Target != null) { Target.Release(); UnityEngine.Object.DestroyImmediate(Target); Target = null; }
            if (_terrainTex != null) UnityEngine.Object.DestroyImmediate(_terrainTex);
            if (_gridTex != null) UnityEngine.Object.DestroyImmediate(_gridTex);
            if (Root != null) Destroy(Root);
        }
    }
}
