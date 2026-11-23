using System;
using System.Collections.Generic;
using System.Numerics;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// 13 · Asteroid Slice: rocks are hurled up through the frame; swipe through them to slice them (+1). Some carry a
    /// friendly alien hitching a ride — spare those (−3). 20 seconds. Score = rocks sliced.
    /// </summary>
    public sealed class AsteroidSlice : MinigameLogic
    {
        private const float Gravity = 620, Radius = 66, Interval = 0.6f, FriendlyShare = 0.25f;

        public override float TimeLimit => 20;
        public override int[] Thresholds => new[] { 6, 12, 18 };
        public override string Status => "Sliced " + Score;
        public override Vector2 HintTarget => new Vector2(250, 650);
        public override Vector2 HintTo => new Vector2(650, 560);

        public sealed class Rock { public Vector2 P, V; public bool Friendly, Sliced; public float SlicedAt, Spin; }
        public readonly List<Rock> Rocks = new List<Rock>();
        private float _spawn = 0.4f;
        private bool _down;
        private Vector2 _last;
        private readonly List<Vector2> _trail = new List<Vector2>();
        private readonly List<float> _trailT = new List<float>();

        public AsteroidSlice(int seed) : base(seed) { }

        protected override void Update()
        {
            _spawn -= Dt;
            if (_spawn <= 0)
            {
                _spawn = Interval * Range(0.7f, 1.3f);
                int n = Rng.NextDouble() < 0.3 ? 2 : 1;
                for (int i = 0; i < n; i++)
                {
                    float x = Range(140, 760);
                    float vy = Range(880, 1080);
                    float vx = (450 - x) * Range(0.2f, 0.6f) + Range(-60, 60);
                    Rocks.Add(new Rock { P = new Vector2(x, -70), V = new Vector2(vx, vy), Friendly = Rng.NextDouble() < FriendlyShare, Spin = Range(-200, 200) });
                }
            }
            foreach (var r in Rocks)
            {
                r.V.Y -= Gravity * Dt;
                r.P += r.V * Dt;
            }
            Rocks.RemoveAll(r => r.P.Y < -120 && r.V.Y < 0 || r.Sliced && Time - r.SlicedAt > 0.7f);
            for (int i = _trailT.Count - 1; i >= 0; i--) if (Time - _trailT[i] > 0.22f) { _trail.RemoveAt(i); _trailT.RemoveAt(i); }
        }

        public override void Down(Vector2 p) { _down = true; _last = p; AddTrail(p); }

        public override void Move(Vector2 p)
        {
            if (!_down) return;
            foreach (var r in Rocks)
            {
                if (r.Sliced || SegmentDistance(r.P, _last, p) > Radius) continue;
                r.Sliced = true; r.SlicedAt = Time;
                if (r.Friendly) { Score = Math.Max(0, Score - 3); Pop("−3 OUCH!", r.P.X, r.P.Y + 80, C(Red)); Shake(); }
                else { Score++; Pop("+1", r.P.X, r.P.Y + 80, C(Gold), 48); }
            }
            _last = p;
            AddTrail(p);
        }

        public override void Up(Vector2 p) { _down = false; }

        private void AddTrail(Vector2 p) { _trail.Add(p); _trailT.Add(Time); }

        public override void Bot(MinigameTier aim)
        {
            if (Tier >= aim) return;
            foreach (var r in Rocks)
            {
                if (r.Sliced || r.Friendly || r.P.Y < 80 || r.P.Y > 1040) continue;
                // a short horizontal swipe through its centre that misses every friendly rock
                var a = r.P - new Vector2(Radius + 20, 0);
                var b = r.P + new Vector2(Radius + 20, 0);
                bool safe = true;
                foreach (var f in Rocks) if (f.Friendly && !f.Sliced && SegmentDistance(f.P, a, b) <= Radius + 30) safe = false;
                if (!safe) continue;
                Down(a); Move(b); Up(b);
                return;
            }
        }

        protected override void Render()
        {
            foreach (var r in Rocks)
            {
                float rot = r.Spin * Time;
                if (r.Sliced)
                {
                    float k = (Time - r.SlicedAt) / 0.7f;
                    for (int s = -1; s <= 1; s += 2)
                        Ellipse(r.P.X + s * 60 * k, r.P.Y - 40 * k * k, Radius * 2, Radius, C(r.Friendly ? Red : 0x8A6A5C, 1 - k), rot + s * 20);
                    continue;
                }
                Circle(r.P.X, r.P.Y, Radius * 2, C(0x8A6A5C));
                Circle(r.P.X - 22, r.P.Y + 18, 34, C(0x6A4F44));
                Circle(r.P.X + 26, r.P.Y - 22, 26, C(0x6A4F44));
                if (r.Friendly)
                {
                    Alien(r.P.X, r.P.Y + Radius + 20, 70, C(Green), 2);
                    Ring(r.P.X, r.P.Y + 20, Radius * 2 + 70, 5, C(Green, 0.6f));
                }
            }
            for (int i = 1; i < _trail.Count; i++)
            {
                float k = 1 - (Time - _trailT[i]) / 0.22f;
                Line(_trail[i - 1], _trail[i], 6 + 18 * k, C(Cyan, 0.3f + 0.6f * k));
            }
        }
    }
}
