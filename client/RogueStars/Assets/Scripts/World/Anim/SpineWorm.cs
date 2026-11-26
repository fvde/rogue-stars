using UnityEngine;

namespace RogueStars.Client.World.Anim
{
    /// <summary>
    /// Animation pilot, technique 1 (code-only curve bending): the hero sprite drawn as a grid mesh bent along a code-generated
    /// bone chain that runs up the sprite from its ground contact. Every grid row sits on the spine at its height and turns
    /// with the spine there, so the body bends smoothly (no seams, no new art). A pose is a handful of numbers
    /// (<see cref="SpinePose"/>); <see cref="WormMotion"/> turns states (idle, move, attack, hit, happy) into poses over time.
    /// Cost: one mesh of (<see cref="Cols"/>+1)×(<see cref="Rows"/>+1) vertices rewritten per frame, one draw call.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SpineWorm : MonoBehaviour
    {
        public const int Cols = 6, Rows = 18;
        /// <summary>Integration steps of the spine per grid row (the curve between two rows).</summary>
        private const int SubSteps = 3;

        private Mesh _mesh;
        private Vector3[] _rest, _verts;
        private float _bottom, _height;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        public SpinePose Pose = SpinePose.Rest;
        public MeshRenderer Renderer => _renderer;
        public int VertexCount => _verts != null ? _verts.Length : 0;

        /// <summary>Builds the grid from the sprite (its pivot = the ground contact stays at the local origin) and its material.</summary>
        public void Init(Sprite sprite, Material material, float height, int sortingOrder)
        {
            _renderer = GetComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.sortingOrder = sortingOrder;
            _block = new MaterialPropertyBlock();
            _block.SetTexture(MainTexId, sprite.texture);
            _renderer.SetPropertyBlock(_block);

            var b = sprite.bounds; // local units, pivot at 0
            float scale = height / b.size.y;
            var tr = sprite.textureRect;
            var tex = new Vector2(sprite.texture.width, sprite.texture.height);
            _rest = new Vector3[(Cols + 1) * (Rows + 1)];
            var uv = new Vector2[_rest.Length];
            var tris = new int[Cols * Rows * 6];
            for (int r = 0; r <= Rows; r++)
                for (int c = 0; c <= Cols; c++)
                {
                    float u = (float)c / Cols, v = (float)r / Rows;
                    int i = r * (Cols + 1) + c;
                    _rest[i] = new Vector3(Mathf.Lerp(b.min.x, b.max.x, u), Mathf.Lerp(b.min.y, b.max.y, v), 0) * scale;
                    uv[i] = new Vector2((tr.x + u * tr.width) / tex.x, (tr.y + v * tr.height) / tex.y);
                }
            int t = 0;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    int i = r * (Cols + 1) + c;
                    tris[t++] = i; tris[t++] = i + Cols + 1; tris[t++] = i + 1;
                    tris[t++] = i + 1; tris[t++] = i + Cols + 1; tris[t++] = i + Cols + 2;
                }
            _bottom = 0; // the contact row: the spine is rooted at the pivot
            _height = b.max.y * scale;
            _verts = (Vector3[])_rest.Clone();
            _mesh = new Mesh { name = "SpineWorm" };
            _mesh.MarkDynamic();
            _mesh.vertices = _verts;
            _mesh.uv = uv;
            _mesh.triangles = tris;
            _mesh.bounds = new Bounds(new Vector3(0, _height * 0.5f), new Vector3(b.size.x * scale * 3, _height * 3, 1));
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            Apply();
        }

        /// <summary>Bend angle density at spine share s (0 root .. 1 tip), radians per unit of spine.</summary>
        private float Curvature(float s)
        {
            var p = Pose;
            float root = Mathf.SmoothStep(0, 1, s / 0.25f); // the base stays planted
            float wave = p.WaveAmp * Mathf.Sin(2 * Mathf.PI * (p.WaveFreq * s - p.WavePhase));
            return (p.Lean + wave * root + p.HeadCurl * Mathf.SmoothStep(0.6f, 1, s)) / Mathf.Max(0.01f, _height);
        }

        /// <summary>Rewrites the mesh from <see cref="Pose"/> (call after changing it; <see cref="WormMotion"/> does).</summary>
        public void Apply()
        {
            if (_mesh == null) return;
            var p = Pose;
            float dir = p.Facing < 0 ? -1 : 1;
            // walk the spine row by row: position and heading at each row's height
            Vector2 pos = Vector2.zero;
            float angle = 0;
                        float yPrev = 0;
            for (int r = 0; r <= Rows; r++)
            {
                float yRest = _rest[r * (Cols + 1)].y; // rest height of this row (grid rows are uniform from min.y)
                if (r == 0) { yPrev = yRest; }
                else
                {
                    float y0 = Mathf.Max(yPrev, _bottom), y1 = Mathf.Max(yRest, _bottom);
                    float seg = (y1 - y0) * p.Stretch;
                    for (int k = 0; k < SubSteps && seg > 0; k++)
                    {
                        float s = Mathf.Clamp01((y0 + (k + 0.5f) / SubSteps * (y1 - y0) - _bottom) / _height);
                        angle += Curvature(s) * seg / SubSteps;
                        pos += new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * (seg / SubSteps);
                    }
                    yPrev = yRest;
                }
                // rows below the contact (the shadow padding) hang straight under the root
                Vector2 rowPos = yRest < 0 ? new Vector2(0, yRest * p.Stretch) : pos;
                float rowAngle = yRest < 0 ? 0 : angle;
                float cs = Mathf.Cos(rowAngle), sn = Mathf.Sin(rowAngle);
                float sh = Mathf.Clamp01(yRest / Mathf.Max(0.01f, _height));
                float widen = p.Squash * (1 - sh * 0.5f); // squash fattens the base more than the head
                for (int c = 0; c <= Cols; c++)
                {
                    int i = r * (Cols + 1) + c;
                    float x = _rest[i].x * widen;
                    var v = rowPos + new Vector2(x * cs, x * sn);
                    _verts[i] = new Vector3((v.x * dir) + p.Offset.x, v.y + p.Offset.y, 0);
                }
            }
            _mesh.vertices = _verts;
            _block.SetFloat(FlashId, p.Flash);
            _renderer.SetPropertyBlock(_block);
        }
    }

    /// <summary>The worm's pose: lean / wave / curl in radians over the whole spine, stretch and squash factors.</summary>
    public struct SpinePose
    {
        /// <summary>Total bend spread evenly along the spine (+ = toward the facing side = forward).</summary>
        public float Lean;
        /// <summary>Travelling wave: amplitude (radians), spatial frequency (waves per body) and phase (cycles).</summary>
        public float WaveAmp, WaveFreq, WavePhase;
        /// <summary>Extra bend of the head end (rear-up hook, lunge snap).</summary>
        public float HeadCurl;
        /// <summary>Length along the spine (breathing, rear-up) and width (landing squash).</summary>
        public float Stretch, Squash;
        public Vector2 Offset;
        /// <summary>+1 = the art's own facing (left), −1 mirrored.</summary>
        public float Facing;
        public float Flash;

        public static SpinePose Rest => new SpinePose { Stretch = 1, Squash = 1, WaveFreq = 1, Facing = 1 };
    }
}
