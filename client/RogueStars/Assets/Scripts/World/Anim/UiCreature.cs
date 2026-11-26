using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.World.Anim
{
    /// <summary>
    /// A creature drawn as a code-deformed mesh in the UI: a child of the sprite's <see cref="Image"/> (same rect, so click
    /// areas, flips by the parent's scale and the stage layout keep working). The Image stays in the hierarchy but does not
    /// draw; its sprite and colour (hit flashes, death tint) still drive the look. With reduced animation on, the Image
    /// draws as before and nothing updates. Ticks only while visible (active in the hierarchy, not clipped by a mask); idle-only
    /// creatures (cards) update at <see cref="IdleHz"/>.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UiCreature : MaskableGraphic
    {
        public const float IdleHz = 20;
        /// <summary>Ticks done so far (perf guard tests: a hidden or static creature adds none).</summary>
        public static int TicksTotal { get; private set; }

        private Image _source;
        private SpineRig _rig;
        private CreatureMotion _motion;
        private readonly Vector2[] _pos = new Vector2[SpineRig.VertexCount];
        private SpinePose _pose;
        private bool _static;
        private float _acc;

        /// <summary>The idle-only mode (aliens cards): only the breathing sway, throttled.</summary>
        public bool IdleOnly;
        /// <summary>Screen direction of the creature's target (+1 right, −1 left); attacks and recoil point there.</summary>
        public float AimScreen = -1;
        public CreatureMotion Motion => _motion;
        public SpineRig Rig => _rig;
        public Image Source => _source;
        public int VertexCount => SpineRig.VertexCount;
        /// <summary>The deformed vertices of the last tick (pixels relative to the root; tests).</summary>
        public Vector2[] Positions => _pos;
        public bool IsStatic => _static;
        public override Texture mainTexture => _rig?.Sprite != null ? _rig.Sprite.texture : base.mainTexture;

        /// <summary>
        /// Draws <paramref name="img"/>'s sprite as an animated creature. Returns null when there is nothing to animate
        /// (no sprite). With reduced animation on it still attaches (and draws static) so toggling it back works at once.
        /// </summary>
        public static UiCreature Attach(Image img, bool idleOnly = false)
        {
            if (img == null || img.sprite == null) return null;
            var existing = img.GetComponentInChildren<UiCreature>(true);
            if (existing != null && existing._source == img) { existing.IdleOnly = idleOnly; return existing; }
            var go = new GameObject("Spine", typeof(RectTransform)) { layer = img.gameObject.layer };
            var rt = (RectTransform)go.transform;
            rt.SetParent(img.rectTransform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var c = go.AddComponent<UiCreature>();
            c.IdleOnly = idleOnly;
            c.Bind(img);
            return c;
        }

        private void Bind(Image img)
        {
            _source = img;
            raycastTarget = false;
            Rebind();
            _motion.PhaseOffset = Mathf.Abs(Mathf.RoundToInt(img.rectTransform.position.x * 0.37f + img.rectTransform.position.y * 0.71f)) % 97 * 0.173f;
            ApplyStatic(AnimSettings.Reduced);
            if (!_static) Tick(0.0001f);
        }

        private void Rebind()
        {
            _rig = SpineRig.For(_source.sprite);
            var spec = CreatureSpecs.For(_source.sprite.name, _rig.Aspect);
            var old = _motion;
            _motion = new CreatureMotion(spec);
            if (old != null) { _motion.PhaseOffset = old.PhaseOffset; _motion.TimeScale = old.TimeScale; }
        }

        private void ApplyStatic(bool on)
        {
            _static = on;
            if (_source != null) _source.enabled = on; // the Image draws the plain sprite, or the mesh does
            SetVerticesDirty();
        }

        /// <summary>Starts a clip (idle-only creatures ignore everything but idle).</summary>
        public void Play(CreatureClip clip, float seconds = 0)
        {
            if (_motion == null || (IdleOnly && clip != CreatureClip.Idle)) return;
            _motion.Play(clip, seconds);
            _acc = 1; // tick at once
        }

        /// <summary>Keeps a looping clip going (Move while walking, Idle otherwise) without restarting it or cutting a one-shot.</summary>
        public void Loop(CreatureClip clip)
        {
            if (_motion == null || IdleOnly || _motion.Clip == clip || _motion.IsOneShot(_motion.Clip)) return;
            Play(clip);
        }

        public void SetTimeScale(float s) { if (_motion != null) _motion.TimeScale = s; }

        /// <summary>Follows the global reduced-animation switch (Update does it every frame; tests call it).</summary>
        public void SyncSettings() { if (_source != null && AnimSettings.Reduced != _static) ApplyStatic(AnimSettings.Reduced); }

        private float LocalSign() => transform.lossyScale.x < 0 ? -1 : 1;

        private void Update()
        {
            if (_source == null) { enabled = false; return; }
            SyncSettings();
            if (_static) return;
            if (!isActiveAndEnabled || canvasRenderer.cull) return;
            if (_motion.Settled) { if (_acc >= 0) { _acc = -1; SetVerticesDirty(); } return; } // the beaten stay down: one last rebuild for the tint, then no cost
            float dt = Time.unscaledDeltaTime;
            _acc += dt;
            // idle at 20 Hz (cards in a list), clips every frame
            bool calm = _motion.Clip == CreatureClip.Idle;
            if (calm && IdleOnly && _acc < 1f / IdleHz) return;
            float step = _acc; _acc = 0;
            if (_source.sprite != _rig.Sprite) Rebind();
            Tick(Mathf.Min(step, 0.1f));
        }

        /// <summary>Advances the clock and rewrites the mesh (Update calls it; tests drive it directly).</summary>
        public void Tick(float dt)
        {
            if (_motion == null || _rig == null) return;
            _motion.Aim = AimScreen * LocalSign();
            _pose = _motion.Tick(dt);
            _rig.Deform(_pose, _pos);
            TicksTotal++;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_rig == null || _source == null || _static) return;
            var r = rectTransform.rect;
            float sx = r.width / Mathf.Max(1, _rig.RectW), sy = r.height / Mathf.Max(1, _rig.RectH);
            if (_source.preserveAspect) sx = sy = Mathf.Min(sx, sy);
            float ox = r.xMin + (r.width - _rig.RectW * sx) * 0.5f + _rig.RootX * sx;
            float oy = r.yMin + (r.height - _rig.RectH * sy) * 0.5f + _rig.RootY * sy;
            Color c = color * _source.color;
            c.a *= _pose.Alpha <= 0 ? 1 : _pose.Alpha;
            Color32 col = c;
            for (int i = 0; i < SpineRig.VertexCount; i++)
                vh.AddVert(new Vector3(ox + _pos[i].x * sx, oy + _pos[i].y * sy, 0), col, _rig.Uv[i]);
            var tris = SpineRig.Triangles;
            for (int i = 0; i < tris.Length; i += 3) vh.AddTriangle(tris[i], tris[i + 1], tris[i + 2]);
        }
    }
}
