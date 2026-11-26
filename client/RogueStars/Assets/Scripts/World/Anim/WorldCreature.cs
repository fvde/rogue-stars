using System.Collections.Generic;
using UnityEngine;

namespace RogueStars.Client.World.Anim
{
    /// <summary>
    /// A creature drawn as a code-deformed mesh in the world: a child mesh under a <see cref="SpriteRenderer"/>'s object (the
    /// same transform, so board placement, flips, scale squash and ground contact keep working). The SpriteRenderer stays
    /// (sprite, sorting order, colour, flipX are read from it every frame) but is disabled while the mesh draws. With reduced
    /// animation on, or while the mesh is off screen, nothing is updated. The mesh comes from a pool (one topology for every
    /// creature: <see cref="SpineRig.VertexCount"/> vertices), so a board with many creatures allocates nothing per frame.
    /// </summary>
    public sealed class WorldCreature : MonoBehaviour
    {
        public const string MaterialPath = "Art/Anim/Materials/AnimSprite";
        private static readonly Stack<Mesh> Pool = new Stack<Mesh>();
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static Material _material;

        public static int PooledMeshes => Pool.Count;
        public static int TicksTotal { get; private set; }

        public SpriteRenderer Source { get; private set; }
        public CreatureMotion Motion { get; private set; }
        public SpineRig Rig { get; private set; }
        public MeshRenderer Renderer { get; private set; }
        /// <summary>Screen direction of the target (+1 right, −1 left).</summary>
        public float AimScreen = -1;
        public bool IsStatic { get; private set; }

        private Mesh _mesh;
        private MaterialPropertyBlock _block;
        private readonly Vector2[] _pos = new Vector2[SpineRig.VertexCount];
        private readonly Vector3[] _verts = new Vector3[SpineRig.VertexCount];
        private float _acc;
        private Sprite _sprite;

        public int VertexCount => _mesh != null ? _mesh.vertexCount : 0;
        public Vector3[] Vertices => _verts;

        /// <summary>Animates the creature drawn by <paramref name="sr"/> (null when it has no sprite). Idempotent.</summary>
        public static WorldCreature Attach(SpriteRenderer sr, bool externalHop = false)
        {
            if (sr == null || sr.sprite == null) return null;
            var ex = sr.GetComponentInChildren<WorldCreature>(true);
            if (ex != null && ex.Source == sr) return ex;
            var go = new GameObject("Spine") { layer = sr.gameObject.layer };
            go.transform.SetParent(sr.transform, false);
            var c = go.AddComponent<WorldCreature>();
            c.Bind(sr, externalHop);
            return c;
        }

        private void Bind(SpriteRenderer sr, bool externalHop)
        {
            Source = sr;
            gameObject.AddComponent<MeshFilter>();
            Renderer = gameObject.AddComponent<MeshRenderer>();
            if (_material == null) _material = Resources.Load<Material>(MaterialPath);
            Renderer.sharedMaterial = _material;
            Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Renderer.receiveShadows = false;
            _block = new MaterialPropertyBlock();
            _mesh = Pool.Count > 0 ? Pool.Pop() : NewMesh();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            Rebind();
            Motion.ExternalHop = externalHop;
            Motion.PhaseOffset = Mathf.Abs(Mathf.RoundToInt(sr.transform.position.x * 13 + sr.transform.position.y * 7)) % 97 * 0.173f;
            ApplyStatic(AnimSettings.Reduced);
            if (!IsStatic) Tick(0.0001f);
        }

        private static Mesh NewMesh()
        {
            var m = new Mesh { name = "Creature" };
            m.MarkDynamic();
            m.vertices = new Vector3[SpineRig.VertexCount];
            m.triangles = SpineRig.Triangles;
            return m;
        }

        private void Rebind()
        {
            _sprite = Source.sprite;
            Rig = SpineRig.For(_sprite);
            var spec = CreatureSpecs.For(_sprite.name, Rig.Aspect);
            var old = Motion;
            Motion = new CreatureMotion(spec);
            if (old != null) { Motion.PhaseOffset = old.PhaseOffset; Motion.TimeScale = old.TimeScale; Motion.ExternalHop = old.ExternalHop; }
            _mesh.SetUVs(0, Rig.Uv);
            var b = _sprite.bounds;
            _mesh.bounds = new Bounds(b.center, b.size * 3f);
            _block.SetTexture(MainTexId, _sprite.texture);
        }

        private void ApplyStatic(bool on)
        {
            IsStatic = on;
            if (Source != null) Source.enabled = on;
            if (Renderer != null) Renderer.enabled = !on;
        }

        public void Play(CreatureClip clip, float seconds = 0) { if (Motion != null) { Motion.Play(clip, seconds); _acc = 1; } }

        /// <summary>Keeps a looping clip going (Move while walking, Idle otherwise) without restarting it or cutting a one-shot.</summary>
        public void Loop(CreatureClip clip)
        {
            if (Motion == null || Motion.Clip == clip || Motion.IsOneShot(Motion.Clip)) return;
            Play(clip);
        }

        /// <summary>Follows the global reduced-animation switch (Update does it every frame; tests call it).</summary>
        public void SyncSettings() { if (Source != null && AnimSettings.Reduced != IsStatic) ApplyStatic(AnimSettings.Reduced); }

        private void Update()
        {
            if (Source == null) { enabled = false; return; }
            SyncSettings();
            if (IsStatic) return;
            if (Source.sprite != _sprite && Source.sprite != null) Rebind();
            if (!Renderer.isVisible && Time.frameCount > 2) return; // off screen: paused
            if (Motion.Settled) return;
            Tick(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
        }

        /// <summary>Advances the clock and rewrites the mesh (Update calls it; tests drive it directly).</summary>
        public void Tick(float dt)
        {
            if (Motion == null || Rig == null || _mesh == null) return;
            float flip = Source.flipX ? -1 : 1;
            Motion.Aim = AimScreen * flip * (transform.lossyScale.x < 0 ? -1 : 1);
            var pose = Motion.Tick(dt);
            Rig.Deform(pose, _pos);
            float ppu = _sprite.pixelsPerUnit;
            var piv = _sprite.pivot;
            float inv = 1f / ppu;
            for (int i = 0; i < _pos.Length; i++)
                _verts[i] = new Vector3(flip * (Rig.RootX - piv.x + _pos[i].x) * inv, (Rig.RootY - piv.y + _pos[i].y) * inv, 0);
            _mesh.SetVertices(_verts, 0, _verts.Length);
            Renderer.sortingOrder = Source.sortingOrder;
            Renderer.sortingLayerID = Source.sortingLayerID;
            var c = Source.color;
            c.a *= pose.Alpha <= 0 ? 1 : pose.Alpha;
            _block.SetColor(ColorId, c);
            _block.SetFloat(FlashId, pose.Flash);
            Renderer.SetPropertyBlock(_block);
            TicksTotal++;
        }

        /// <summary>Gives the mesh back to the pool (OnDestroy does it; edit-mode tests call it).</summary>
        public void Release()
        {
            if (_mesh != null) { Pool.Push(_mesh); _mesh = null; }
        }

        private void OnDestroy()
        {
            Release();
            if (Source != null && !Source.enabled && AnimSettings.Reduced == false) Source.enabled = true; // never leave a sprite hidden behind
        }
    }
}
