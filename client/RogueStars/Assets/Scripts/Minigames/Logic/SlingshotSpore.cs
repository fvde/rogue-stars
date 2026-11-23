using System;
using System.Collections.Generic;
using System.Numerics;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// 9 · Slingshot Spore: pull the spore back from the slingshot and let go to fling it at the aliens on floating
    /// platforms (a dotted arc shows the start of the flight). 8 spores. Score = aliens hit.
    /// </summary>
    public sealed class SlingshotSpore : MinigameLogic
    {
        public const int Spores = 8;
        private static readonly Vector2 Anchor = new Vector2(190, 300);
        private const float Power = 6.2f, MaxPull = 230, Gravity = 900, HitR = 62, GrabR = 150, FlightMax = 2.4f;

        public override float TimeLimit => 30;
        public override int[] Thresholds => new[] { 2, 4, 6 };
        public override string Status => "Spores " + (Spores - Shot) + " · Hits " + Score;
        public override Vector2 HintTarget => Anchor;
        public override Vector2 HintTo => Anchor + new Vector2(-150, -120);

        public sealed class Target { public Vector2 Home; public float Phase, Amp; public bool Hit; public float HitAt; }
        public readonly List<Target> Targets = new List<Target>();
        public int Shot { get; private set; }
        private bool _pulling, _flying;
        private Vector2 _pull, _spore, _vel;
        private float _flight, _cooldown;
        // bot
        private int _botStep;
        private Vector2 _botPull;

        public SlingshotSpore(int seed) : base(seed)
        {
            for (int i = 0; i < 3; i++) Targets.Add(NewTarget(i));
        }

        private Target NewTarget(int slot)
        {
            float x = 560 + slot * 110 + Range(-30, 30);
            float y = 380 + ((slot * 2 + Rng.Range(0, 3)) % 5) * 130 + Range(-20, 20);
            return new Target { Home = new Vector2(x, y), Phase = Range(0, 6.28f), Amp = Range(20, 50) };
        }

        public Vector2 PositionOf(Target t, float time) => t.Home + new Vector2(0, t.Amp * (float)Math.Sin(time * 1.3f + t.Phase));

        protected override void Update()
        {
            if (_cooldown > 0) { _cooldown -= Dt; if (_cooldown <= 0 && Shot >= Spores) End(); }
            for (int i = 0; i < Targets.Count; i++)
                if (Targets[i].Hit && Time - Targets[i].HitAt > 0.6f) Targets[i] = NewTarget(i);
            if (!_flying) return;
            _flight += Dt;
            _vel.Y -= Gravity * Dt;
            _spore += _vel * Dt;
            foreach (var t in Targets)
            {
                if (t.Hit || Vector2.Distance(_spore, PositionOf(t, Time)) > HitR) continue;
                t.Hit = true; t.HitAt = Time;
                Score++;
                Pop("SPLAT!", _spore.X, _spore.Y + 60, C(Green));
                _flying = false;
                break;
            }
            if (_flying && (_spore.Y < -60 || _spore.X > Width + 60 || _spore.X < -60 || _flight > FlightMax)) { _flying = false; Pop("miss", Math.Min(_spore.X, 820), Math.Max(_spore.Y, 120), C(Cream, 0.8f), 40); }
            if (!_flying) _cooldown = Math.Max(_cooldown, 0.45f);
        }

        private bool Ready => !_flying && _cooldown <= 0 && Shot < Spores && !Over;

        public override void Down(Vector2 p)
        {
            if (!Ready || Vector2.Distance(p, Anchor) > GrabR) return;
            _pulling = true;
            _pull = Anchor;
        }

        public override void Move(Vector2 p)
        {
            if (!_pulling) return;
            var d = p - Anchor;
            if (d.Length() > MaxPull) d = Vector2.Normalize(d) * MaxPull;
            _pull = Anchor + d;
        }

        public override void Up(Vector2 p)
        {
            if (!_pulling) return;
            _pulling = false;
            if (Vector2.Distance(_pull, Anchor) < 25) return; // a tap: no shot
            _vel = (Anchor - _pull) * Power;
            _spore = Anchor;
            _flying = true;
            _flight = 0;
            Shot++;
        }

        /// <summary>The pull point that lands a spore on <paramref name="t"/> (false if out of range).</summary>
        private bool Solve(Target t, out Vector2 pull)
        {
            for (float T = 0.55f; T <= 1.6f; T += 0.05f)
            {
                var goal = PositionOf(t, Time + 3 * Dt + T); // released in 3 steps
                var v = (goal - Anchor - new Vector2(0, -0.5f * Gravity * T * T)) / T;
                var d = -v / Power;
                if (d.Length() <= MaxPull) { pull = Anchor + d; return true; }
            }
            pull = Anchor;
            return false;
        }

        public override void Bot(MinigameTier aim)
        {
            if (_botStep > 0)
            {
                _botStep++;
                if (_botStep == 2) Move(Vector2.Lerp(Anchor, _botPull, 0.5f));
                else if (_botStep == 3) Move(_botPull);
                else { Up(_botPull); _botStep = 0; }
                return;
            }
            if (!Ready) return;
            if (Tier < aim)
            {
                Target best = null; Vector2 pull = Anchor;
                foreach (var t in Targets) if (!t.Hit && Solve(t, out var pp)) { best = t; pull = pp; break; }
                if (best == null) return;
                _botPull = pull;
            }
            else _botPull = Anchor + new Vector2(60, 100); // flings it down into the ground
            Down(Anchor);
            _botStep = 1;
        }

        protected override void Render()
        {
            Box(450, 60, 900, 120, C(0x23284F));
            foreach (var t in Targets)
            {
                var p = PositionOf(t, Time);
                Box(p.X, p.Y - 58, 150, 26, C(Steel), 13);
                Ellipse(p.X, p.Y - 80, 90, 24, C(Cyan, 0.25f + 0.15f * (float)Math.Sin(Time * 4 + t.Phase)));
                if (!t.Hit) Alien(p.X, p.Y, 86, C(Green), 1 + (int)(t.Phase * 10) % 2);
                else { float k = (Time - t.HitAt) / 0.6f; for (int i = 0; i < 5; i++) Circle(p.X + (i - 2) * 30 * (1 + k), p.Y + (i % 2) * 30 * k, 40 * (1 - k), C(Green, 1 - k)); }
            }
            // slingshot
            Box(Anchor.X - 50, Anchor.Y - 110, 22, 180, C(0x8A6A5C), 8, 12);
            Box(Anchor.X + 50, Anchor.Y - 110, 22, 180, C(0x8A6A5C), 8, -12);
            var sp = _pulling ? _pull : _flying ? _spore : Anchor;
            if (!_flying)
            {
                Line(new Vector2(Anchor.X - 50, Anchor.Y - 25), sp, 10, C(Pink, 0.8f));
                Line(new Vector2(Anchor.X + 50, Anchor.Y - 25), sp, 10, C(Pink, 0.8f));
            }
            if (_pulling)
            {
                var v = (Anchor - _pull) * Power;
                for (int i = 1; i <= 7; i++)
                {
                    float t = i * 0.07f;
                    var q = Anchor + v * t + new Vector2(0, -0.5f * Gravity * t * t);
                    Circle(q.X, q.Y, 16, C(Cream, 0.7f - i * 0.08f));
                }
            }
            if (Ready || _pulling || _flying)
            {
                Circle(sp.X, sp.Y, 64, C(0xC9A2FF));
                Circle(sp.X - 12, sp.Y + 12, 20, C(0xFFFFFF, 0.6f));
                for (int i = 0; i < 6; i++) Circle(sp.X + 34 * (float)Math.Cos(i + Time * 3), sp.Y + 34 * (float)Math.Sin(i + Time * 3), 12, C(Purple));
            }
            if (Ready && !_pulling) Ring(Anchor.X, Anchor.Y, 150 + 16 * (float)Math.Sin(Time * 5), 5, C(Cream, 0.5f));
            for (int i = 0; i < Spores; i++) Circle(60 + i * 44, 1050, 30, C(Purple, i < Spores - Shot ? 1 : 0.2f));
        }
    }
}
