using System.Collections.Generic;
using UnityEngine;

namespace RogueStars.Client.World.Anim
{
    /// <summary>
    /// A creature's pose: lean / wave / curl in radians over the whole spine, stretch and squash factors, an offset in shares
    /// of the creature's height. "Forward" is <see cref="Dir"/>: +1 bends toward the right of the host's local space, −1 left.
    /// </summary>
    public struct SpinePose
    {
        /// <summary>Total bend spread evenly along the spine (+ = toward <see cref="Dir"/> = forward).</summary>
        public float Lean;
        /// <summary>Travelling wave: amplitude (radians), spatial frequency (waves per body) and phase (cycles).</summary>
        public float WaveAmp, WaveFreq, WavePhase;
        /// <summary>Extra bend of the head end (rear-up hook, lunge snap).</summary>
        public float HeadCurl;
        /// <summary>Length along the spine (breathing, rear-up) and width (landing squash).</summary>
        public float Stretch, Squash;
        /// <summary>Shift of the whole body in shares of its height (x forward-signed by <see cref="Dir"/> in the motion, y up).</summary>
        public Vector2 Offset;
        /// <summary>Local-space direction of "forward" (+1 right, −1 left).</summary>
        public float Dir;
        /// <summary>White flash 0..1 (world creatures; UI creatures follow their Image's colour) and opacity.</summary>
        public float Flash, Alpha;

        public static SpinePose Rest => new SpinePose { Stretch = 1, Squash = 1, WaveFreq = 1, Dir = -1, Alpha = 1 };

        public static SpinePose Lerp(in SpinePose a, in SpinePose b, float t) => new SpinePose
        {
            Lean = Mathf.Lerp(a.Lean, b.Lean, t), WaveAmp = Mathf.Lerp(a.WaveAmp, b.WaveAmp, t), WaveFreq = Mathf.Lerp(a.WaveFreq, b.WaveFreq, t),
            WavePhase = b.WavePhase, HeadCurl = Mathf.Lerp(a.HeadCurl, b.HeadCurl, t), Stretch = Mathf.Lerp(a.Stretch, b.Stretch, t),
            Squash = Mathf.Lerp(a.Squash, b.Squash, t), Offset = Vector2.Lerp(a.Offset, b.Offset, t), Dir = b.Dir,
            Flash = Mathf.Lerp(a.Flash, b.Flash, t), Alpha = Mathf.Lerp(a.Alpha, b.Alpha, t),
        };
    }

    /// <summary>
    /// The deformable mesh of one upright creature sprite: a (<see cref="Cols"/>+1)×(<see cref="Rows"/>+1) grid over the sprite's
    /// alpha bounds, bent along a vertical spine in code (every row sits on the spine at its height and turns with it, so the
    /// body bends smoothly without seams and without extra art). The spine is rooted at the bottom of the alpha bounds at the
    /// ground contact's x. Pure data + maths (no Unity objects besides the sprite it reads): UI and world hosts share it.
    /// Cost: <see cref="VertexCount"/> (102) vertices rewritten per frame, one draw call, no allocation in <see cref="Deform"/>.
    /// </summary>
    public sealed class SpineRig
    {
        public const int Cols = 5, Rows = 16;
        public const int VertexCount = (Cols + 1) * (Rows + 1);
        /// <summary>Integration steps of the spine per grid row.</summary>
        private const int SubSteps = 2;
        /// <summary>Transparent margin kept around the alpha bounds (pixels), so soft edges and outlines are not cut.</summary>
        private const float Margin = 3;

        /// <summary>Rest geometry in pixels relative to the root (x from <see cref="RootX"/>, y from the alpha bottom) and the matching UVs.</summary>
        public readonly Vector2[] Rest = new Vector2[VertexCount];
        public readonly Vector2[] Uv = new Vector2[VertexCount];
        /// <summary>Spine height (alpha bounds) in pixels and the root in the sprite rect's pixels (origin bottom-left).</summary>
        public float Height, RootX, RootY;
        /// <summary>Alpha bounds in the sprite rect's pixels.</summary>
        public float MinX, MaxX, MinY, MaxY;
        /// <summary>The sprite rect in pixels.</summary>
        public float RectW, RectH;
        public Sprite Sprite;
        /// <summary>Alpha-bounds aspect (height / width): decides the default style (<see cref="CreatureSpecs"/>).</summary>
        public float Aspect => Height / Mathf.Max(1f, MaxX - MinX);

        private readonly float[] _rowY = new float[Rows + 1];
        private static int[] _tris;
        private static readonly Dictionary<Sprite, SpineRig> Cache = new Dictionary<Sprite, SpineRig>();

        /// <summary>Triangles of the shared grid topology (every rig uses the same).</summary>
        public static int[] Triangles
        {
            get
            {
                if (_tris != null) return _tris;
                _tris = new int[Cols * Rows * 6];
                int t = 0;
                for (int r = 0; r < Rows; r++)
                    for (int c = 0; c < Cols; c++)
                    {
                        int i = r * (Cols + 1) + c;
                        _tris[t++] = i; _tris[t++] = i + Cols + 1; _tris[t++] = i + 1;
                        _tris[t++] = i + 1; _tris[t++] = i + Cols + 1; _tris[t++] = i + Cols + 2;
                    }
                return _tris;
            }
        }

        public static void ClearCache() => Cache.Clear();

        /// <summary>The rig of a sprite (cached; null for a missing sprite).</summary>
        public static SpineRig For(Sprite sprite)
        {
            if (sprite == null) return null;
            if (Cache.TryGetValue(sprite, out var rig) && rig != null) return rig;
            rig = Build(sprite, AnimBounds.Of(sprite));
            Cache[sprite] = rig;
            return rig;
        }

        /// <summary>Builds a rig from explicit alpha bounds (pixels of the sprite rect).</summary>
        public static SpineRig Build(Sprite sprite, RectInt alpha)
        {
            var rig = new SpineRig { Sprite = sprite };
            var tr = sprite.rect;
            rig.RectW = tr.width; rig.RectH = tr.height;
            float x0 = Mathf.Max(0, alpha.xMin - Margin), x1 = Mathf.Min(tr.width, alpha.xMax + Margin);
            float y0 = Mathf.Max(0, alpha.yMin - Margin), y1 = Mathf.Min(tr.height, alpha.yMax + Margin);
            rig.MinX = alpha.xMin; rig.MaxX = alpha.xMax; rig.MinY = alpha.yMin; rig.MaxY = alpha.yMax;
            rig.Height = Mathf.Max(1, alpha.yMax - alpha.yMin);
            // root: the ground contact's x when the art has a pivot there (standing art), else the middle of the lower alpha part
            var c = SpriteContact.Of(sprite);
            rig.RootX = c.Valid && c.X > alpha.xMin && c.X < alpha.xMax ? c.X : (alpha.xMin + alpha.xMax) * 0.5f;
            rig.RootY = alpha.yMin;
            var tex = sprite.texture;
            float tw = tex.width, th = tex.height;
            for (int r = 0; r <= Rows; r++)
            {
                float v = (float)r / Rows;
                float py = Mathf.Lerp(y0, y1, v);
                rig.RowY(r, py - rig.RootY);
                for (int col = 0; col <= Cols; col++)
                {
                    float u = (float)col / Cols;
                    float px = Mathf.Lerp(x0, x1, u);
                    int i = r * (Cols + 1) + col;
                    rig.Rest[i] = new Vector2(px - rig.RootX, py - rig.RootY);
                    rig.Uv[i] = new Vector2((tr.x + px) / tw, (tr.y + py) / th);
                }
            }
            return rig;
        }

        private void RowY(int r, float y) => _rowY[r] = y;

        /// <summary>Bend angle density at spine share s (0 root .. 1 tip), radians per unit of spine share.</summary>
        private static float Curvature(in SpinePose p, float s)
        {
            float root = Mathf.SmoothStep(0, 1, s / 0.25f); // the base stays planted
            float wave = p.WaveAmp * Mathf.Sin(2 * Mathf.PI * (p.WaveFreq * s - p.WavePhase));
            return p.Lean + wave * root + p.HeadCurl * Mathf.SmoothStep(0.6f, 1, s);
        }

        /// <summary>
        /// Writes the deformed vertices into <paramref name="output"/> (≥ <see cref="VertexCount"/>): pixels relative to the root,
        /// x right, y up. No allocation.
        /// </summary>
        public void Deform(in SpinePose p, Vector2[] output)
        {
            float h = Mathf.Max(1, Height);
            float dir = p.Dir < 0 ? -1 : 1;
            float px = 0, py = 0, angle = 0, yPrev = _rowY[0];
            float offX = p.Offset.x * h, offY = p.Offset.y * h;
            for (int r = 0; r <= Rows; r++)
            {
                float yRest = _rowY[r];
                if (r > 0 && yRest > 0)
                {
                    float y0 = Mathf.Max(yPrev, 0), y1 = yRest;
                    float seg = (y1 - y0) * p.Stretch;
                    float step = seg / SubSteps;
                    for (int k = 0; k < SubSteps && seg > 0; k++)
                    {
                        float s = Mathf.Clamp01((y0 + (k + 0.5f) / SubSteps * (y1 - y0)) / h);
                        angle += Curvature(p, s) * dir / h * step;
                        px += Mathf.Sin(angle) * step;
                        py += Mathf.Cos(angle) * step;
                    }
                }
                yPrev = yRest;
                // rows below the root (the margin under the feet) hang straight under it
                bool below = yRest <= 0;
                float rowX = below ? 0 : px, rowY = below ? yRest * p.Stretch : py, a = below ? 0 : angle;
                float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                float sh = Mathf.Clamp01(yRest / h);
                float widen = 1 + (p.Squash - 1) * (1 - sh * 0.5f); // squash fattens the base more than the head
                int row = r * (Cols + 1);
                for (int c = 0; c <= Cols; c++)
                {
                    float x = Rest[row + c].x * widen;
                    output[row + c] = new Vector2(rowX + x * cs + offX, rowY - x * sn + offY);
                }
            }
        }
    }
}
