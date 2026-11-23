using System;
using System.Collections.Generic;
using System.Numerics;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    public enum ShapeKind { Circle, Box, Ring, Text }

    /// <summary>One thing to draw in the arena (bottom-left origin, y up, canvas units). Rot in degrees, Color RGBA.</summary>
    public struct Shape
    {
        public ShapeKind Kind;
        public float X, Y, W, H, Rot;
        public uint Color;
        /// <summary>Box: corner radius. Ring: line thickness.</summary>
        public float Radius;
        public string Text;
        public int FontSize;
    }

    /// <summary>
    /// A run mini-game's rules and look, independent of Unity: a fixed 60 Hz step, pointer input in arena coordinates,
    /// a score, the tier thresholds (<see cref="Thresholds"/>: the one place per game), a list of shapes to draw and a
    /// scripted player (<see cref="Bot"/>: plays well while below the aimed tier and holds back once there; tests, demo).
    /// Deterministic from the seed the rules picked (RunMinigameStart).
    /// </summary>
    public abstract class MinigameLogic
    {
        public const float Dt = 1f / 60f;
        public const float Width = 900, Height = 1100;

        public readonly int Seed;
        protected readonly Rng Rng;
        public float Time { get; private set; }
        public bool Over { get; private set; }
        public int Score { get; protected set; }
        /// <summary>Step counter (deterministic animation clock).</summary>
        public int Frame { get; private set; }

        protected MinigameLogic(int seed)
        {
            Seed = seed;
            Rng = Rng.FromSeed(seed);
            var bg = Rng.FromSeed(seed ^ 0x5EED);
            for (int i = 0; i < 34; i++) _stars.Add(new Vector3((float)bg.NextDouble() * Width, (float)bg.NextDouble() * Height, (float)bg.NextDouble()));
        }

        /// <summary>Hard end (seconds); every game ends by itself well before 30 s of play.</summary>
        public abstract float TimeLimit { get; }
        /// <summary>Score needed for Bronze, Silver, Gold.</summary>
        public abstract int[] Thresholds { get; }
        /// <summary>HUD line, e.g. "Slips 3 / 5".</summary>
        public abstract string Status { get; }
        /// <summary>The big touch target (hint pointer, highlighted).</summary>
        public abstract Vector2 HintTarget { get; }
        /// <summary>Where a drag / swipe hint moves to (equal to <see cref="HintTarget"/> for taps).</summary>
        public virtual Vector2 HintTo => HintTarget;

        public MinigameTier Tier => TierFor(Score);

        public MinigameTier TierFor(int score)
        {
            var t = Thresholds;
            return score >= t[2] ? MinigameTier.Gold : score >= t[1] ? MinigameTier.Silver : score >= t[0] ? MinigameTier.Bronze : MinigameTier.Miss;
        }

        /// <summary>Lowest and highest score of a tier (Gold: no upper bound).</summary>
        public void Band(MinigameTier tier, out int low, out int high)
        {
            var t = Thresholds;
            low = tier == MinigameTier.Miss ? 0 : t[(int)tier - 1];
            high = tier == MinigameTier.Gold ? int.MaxValue : t[(int)tier] - 1;
        }

        /// <summary>Advance one fixed step (no-op once over).</summary>
        public void Step()
        {
            if (Over) return;
            Time += Dt;
            Frame++;
            Update();
            for (int i = _pops.Count - 1; i >= 0; i--) if (Time - _pops[i].Born > 0.9f) _pops.RemoveAt(i);
            if (_shake > 0) _shake = Math.Max(0, _shake - Dt);
            if (!Over && Time >= TimeLimit) End();
        }

        protected abstract void Update();

        /// <summary>Ends the game now (also at the time limit).</summary>
        protected void End()
        {
            if (Over) return;
            Over = true;
            OnEnd();
        }

        protected virtual void OnEnd() { }

        public virtual void Down(Vector2 p) { }
        public virtual void Move(Vector2 p) { }
        public virtual void Up(Vector2 p) { }

        /// <summary>A tap = down + up at one point.</summary>
        public void Tap(Vector2 p) { Down(p); Up(p); }

        /// <summary>Scripted player for one step: plays well while the score is below <paramref name="aim"/>, holds back once there.</summary>
        public abstract void Bot(MinigameTier aim);

        // ---- drawing --------------------------------------------------------------------------

        private List<Shape> _out;
        private readonly List<Vector3> _stars = new List<Vector3>();

        /// <summary>The shapes of the current frame, back to front.</summary>
        public void Draw(List<Shape> shapes)
        {
            _out = shapes;
            shapes.Clear();
            foreach (var s in _stars)
            {
                float tw = 0.35f + 0.35f * (float)Math.Sin(Time * 2 + s.Z * 12);
                Circle(s.X, s.Y, 4 + 5 * s.Z, C(0xC9CFF5, tw));
            }
            Render();
            foreach (var p in _pops)
            {
                float k = (Time - p.Born) / 0.9f;
                Text(p.X, p.Y + 90 * k, p.Text, p.Size, Alpha(p.Color, 1 - k * k));
            }
            _out = null;
        }

        protected abstract void Render();

        /// <summary>Arena shake offset (juice on hits), read by the view.</summary>
        public Vector2 ShakeOffset => _shake <= 0 ? Vector2.Zero : new Vector2((float)Math.Sin(Frame * 2.7) * 14 * _shake / 0.3f, (float)Math.Cos(Frame * 3.1) * 10 * _shake / 0.3f);
        private float _shake;
        protected void Shake(float seconds = 0.3f) => _shake = Math.Max(_shake, seconds);

        private struct PopText { public string Text; public float X, Y, Born; public uint Color; public int Size; }
        private readonly List<PopText> _pops = new List<PopText>();
        /// <summary>A floating text that rises and fades ("+1", "PERFECT").</summary>
        protected void Pop(string text, float x, float y, uint color, int size = 56) =>
            _pops.Add(new PopText { Text = text, X = x, Y = y, Born = Time, Color = color, Size = size });

        protected void Circle(float x, float y, float d, uint color) => Add(ShapeKind.Circle, x, y, d, d, 0, color, 0, null, 0);
        protected void Ellipse(float x, float y, float w, float h, uint color, float rot = 0) => Add(ShapeKind.Circle, x, y, w, h, rot, color, 0, null, 0);
        protected void Box(float x, float y, float w, float h, uint color, float radius = 0, float rot = 0) => Add(ShapeKind.Box, x, y, w, h, rot, color, radius, null, 0);
        protected void Ring(float x, float y, float d, float thickness, uint color) => Add(ShapeKind.Ring, x, y, d, d, 0, color, thickness, null, 0);
        protected void Text(float x, float y, string text, int size, uint color) => Add(ShapeKind.Text, x, y, 0, 0, 0, color, 0, text, size);

        /// <summary>A line from a to b as a thin rotated box.</summary>
        protected void Line(Vector2 a, Vector2 b, float width, uint color)
        {
            var d = b - a;
            float len = d.Length();
            if (len < 0.5f) return;
            float rot = (float)(Math.Atan2(d.Y, d.X) * 180 / Math.PI);
            Box((a.X + b.X) / 2, (a.Y + b.Y) / 2, len, width, color, width / 2, rot);
        }

        private void Add(ShapeKind kind, float x, float y, float w, float h, float rot, uint color, float radius, string text, int size) =>
            _out?.Add(new Shape { Kind = kind, X = x, Y = y, W = w, H = h, Rot = rot, Color = color, Radius = radius, Text = text, FontSize = size });

        /// <summary>The worm (pink segments, head at x,y facing right or left), the hero of every game.</summary>
        protected void Worm(float x, float y, float scale = 1, bool faceLeft = false, float wiggle = 0)
        {
            float dir = faceLeft ? 1 : -1;
            for (int i = 3; i >= 1; i--)
            {
                float sx = x + dir * i * 34 * scale;
                float sy = y + (float)Math.Sin(Time * 8 + i * 1.2 + wiggle) * 6 * scale;
                Circle(sx, sy, (58 - i * 6) * scale, i % 2 == 0 ? C(0xE8789F) : C(Pink));
            }
            Circle(x, y, 66 * scale, C(Pink));
            Circle(x - dir * 12 * scale, y + 10 * scale, 20 * scale, C(0xFFFFFF));
            Circle(x - dir * 15 * scale, y + 10 * scale, 10 * scale, C(0x1A0A12));
        }

        /// <summary>A little green alien (body, eyes, antenna).</summary>
        protected void Alien(float x, float y, float d, uint body, int eyes = 1, bool antenna = true, bool mouth = true, float rot = 0)
        {
            if (antenna)
            {
                Line(new Vector2(x, y + d * 0.35f), new Vector2(x + d * 0.12f, y + d * 0.68f), d * 0.06f, body);
                Circle(x + d * 0.12f, y + d * 0.7f, d * 0.16f, C(Gold));
            }
            Ellipse(x, y, d, d * 0.9f, body, rot);
            if (eyes == 1) { Circle(x, y + d * 0.1f, d * 0.4f, C(0xFFFFFF)); Circle(x, y + d * 0.1f, d * 0.18f, C(0x10202A)); }
            else
                for (int i = -1; i <= 1; i += 2) { Circle(x + i * d * 0.2f, y + d * 0.1f, d * 0.28f, C(0xFFFFFF)); Circle(x + i * d * 0.2f, y + d * 0.1f, d * 0.13f, C(0x10202A)); }
            if (mouth) Box(x, y - d * 0.22f, d * 0.3f, d * 0.07f, C(0x10202A), d * 0.035f);
        }

        // ---- colours (space palette on the game's warm theme) ---------------------------------

        public const int Pink = 0xF28BB6, Cyan = 0x7FE3FF, Gold = 0xFFC35C, Green = 0x7BE08A, Red = 0xE5484D, Purple = 0xA77BFF,
            Orange = 0xFF8A1F, Blue = 0x3E7BFA, Cream = 0xFFE0B8, Ink = 0xEEF1FF, Night = 0x0E1124, Steel = 0x5A639A;

        public static uint C(int rgb, float a = 1) => ((uint)rgb << 8) | (uint)Math.Max(0, Math.Min(255, (int)(a * 255)));
        public static uint Alpha(uint color, float a) => (color & 0xFFFFFF00u) | (uint)Math.Max(0, Math.Min(255, (int)((color & 0xFF) * a)));

        protected static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        protected static float Lerp(float a, float b, float t) => a + (b - a) * Clamp(t, 0, 1);
        protected float Range(float lo, float hi) => lo + (float)Rng.NextDouble() * (hi - lo);

        /// <summary>Distance from p to the segment a–b.</summary>
        protected static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float l2 = ab.LengthSquared();
            float t = l2 <= 1e-6f ? 0 : Clamp(Vector2.Dot(p - a, ab) / l2, 0, 1);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
