using System;
using System.Collections.Generic;
using System.Numerics;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// 2 · Comet Catch: comets streak down across the sky; drag the worm along the ground so its mouth meets the icy
    /// comets (+1) and dodges the fiery ones (−2). 20 seconds, comets get faster. Score = comets eaten.
    /// </summary>
    public sealed class CometCatch : MinigameLogic
    {
        private const float MouthY = 150, CatchR = 74, Speed = 2000, Interval = 0.55f, FieryShare = 0.28f;

        public override float TimeLimit => 20;
        public override int[] Thresholds => new[] { 5, 10, 15 };
        public override string Status => "Comets " + Score;
        public override Vector2 HintTarget => new Vector2(_x, MouthY);
        public override Vector2 HintTo => new Vector2(_x > 450 ? _x - 260 : _x + 260, MouthY);

        public sealed class Comet { public Vector2 P, V; public bool Fiery, Done; }
        public readonly List<Comet> Comets = new List<Comet>();
        private float _x = 450, _target = 450, _spawn = 0.5f, _flash;

        public CometCatch(int seed) : base(seed) { }

        public float WormX => _x;

        protected override void Update()
        {
            _spawn -= Dt;
            if (_spawn <= 0)
            {
                _spawn = Interval * Range(0.8f, 1.15f);
                float x = Range(90, 810);
                float speed = 420 + 16 * Time;
                float vx = Range(-150, 150);
                // keep the crossing point on screen
                float t = (Height + 40 - MouthY) / speed;
                float land = Clamp(x + vx * t, 80, 820);
                Comets.Add(new Comet { P = new Vector2(x, Height + 40), V = new Vector2((land - x) / t, -speed), Fiery = Rng.NextDouble() < FieryShare });
            }
            float dx = _target - _x;
            _x += Math.Sign(dx) * Math.Min(Math.Abs(dx), Speed * Dt);
            if (_flash > 0) _flash -= Dt;
            foreach (var c in Comets)
            {
                if (c.Done) continue;
                var before = c.P;
                c.P += c.V * Dt;
                if (before.Y >= MouthY && c.P.Y < MouthY)
                {
                    float cx = before.X + (c.P.X - before.X) * (before.Y - MouthY) / (before.Y - c.P.Y);
                    if (Math.Abs(cx - _x) <= CatchR)
                    {
                        c.Done = true;
                        if (c.Fiery) { Score = Math.Max(0, Score - 2); Pop("−2", _x, MouthY + 90, C(Red)); Shake(); _flash = 0.3f; }
                        else { Score++; Pop("+1", _x, MouthY + 90, C(Cyan)); }
                    }
                }
                if (c.P.Y < -60) c.Done = true;
            }
            Comets.RemoveAll(c => c.Done);
        }

        public override void Down(Vector2 p) => _target = Clamp(p.X, 70, 830);
        public override void Move(Vector2 p) => _target = Clamp(p.X, 70, 830);

        /// <summary>Where a comet crosses the mouth line and when (seconds from now).</summary>
        private static float CrossX(Comet c, out float t)
        {
            t = (c.P.Y - MouthY) / -c.V.Y;
            return c.P.X + c.V.X * t;
        }

        public override void Bot(MinigameTier aim)
        {
            Band(aim, out int low, out int high);
            bool want = Score < low || (Score < high && Tier < aim);
            float best = float.MaxValue, goal = _x;
            if (want)
            {
                foreach (var c in Comets)
                {
                    if (c.Done || c.Fiery || c.P.Y < MouthY) continue;
                    float x = CrossX(c, out float t);
                    if (Math.Abs(x - _x) / Speed > t + 0.02f) continue; // out of reach
                    bool risky = false;
                    foreach (var f in Comets)
                        if (!f.Done && f.Fiery && f.P.Y >= MouthY) { float fx = CrossX(f, out float ft); if (Math.Abs(ft - t) < 0.35f && Math.Abs(fx - x) < CatchR * 2.2f) risky = true; }
                    if (risky) continue;
                    if (t < best) { best = t; goal = x; }
                }
            }
            if (!want || best == float.MaxValue)
            {
                // hold back: stand where no comet will cross soon
                float bestGap = -1;
                for (float x = 80; x <= 820; x += 20)
                {
                    float gap = float.MaxValue;
                    foreach (var c in Comets)
                    {
                        if (c.Done || c.P.Y < MouthY) continue;
                        float cx = CrossX(c, out float t);
                        if (t > 1.2f) continue;
                        if (!want || c.Fiery) gap = Math.Min(gap, Math.Abs(cx - x));
                    }
                    gap -= Math.Abs(x - _x) * 0.05f;
                    if (gap > bestGap) { bestGap = gap; goal = x; }
                }
            }
            Move(new Vector2(goal, MouthY));
        }

        protected override void Render()
        {
            Box(450, 50, 900, 100, C(0x23284F), 0);
            Box(450, 100, 900, 6, C(Steel, 0.6f));
            foreach (var c in Comets)
            {
                if (c.Done) continue;
                var dir = Vector2.Normalize(c.V);
                for (int i = 4; i >= 1; i--)
                {
                    var t = c.P - dir * (i * 22);
                    Circle(t.X, t.Y, 54 - i * 8, c.Fiery ? C(i % 2 == 0 ? Orange : Red, 0.6f - i * 0.1f) : C(Cyan, 0.5f - i * 0.09f));
                }
                Circle(c.P.X, c.P.Y, 56, c.Fiery ? C(Orange) : C(0xD6F6FF));
                Circle(c.P.X, c.P.Y, 30, c.Fiery ? C(0xFFE08A) : C(Cyan));
            }
            // landing shadows of the next comets (where to stand)
            foreach (var c in Comets)
            {
                if (c.Done || c.P.Y < MouthY) continue;
                float x = CrossX(c, out float t);
                if (t < 1.2f) Ellipse(x, MouthY - 46, 70, 16, c.Fiery ? C(Red, 0.5f) : C(Cyan, 0.45f));
            }
            Ring(_x, MouthY, CatchR * 2, 4, C(Cream, 0.25f));
            Worm(_x, MouthY, 1.15f, _target < _x, _flash > 0 ? 3 : 0);
            Ellipse(_x + (_target < _x ? -22 : 22), MouthY - 14, 26, _flash > 0 ? 8 : 18, C(0x4A1028));
        }
    }
}
