using System;
using System.Numerics;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// 4 · Saucer Landing: drag the wobbling saucer (it follows your finger on a spring) onto the landing pad while solar
    /// wind gusts push it sideways. 3 landings; each scores softness (slow touchdown) + accuracy (centre of the pad),
    /// 0–100; off the pad scores 0. Score = sum of the 3 landings.
    /// </summary>
    public sealed class SaucerLanding : MinigameLogic
    {
        public const int Landings = 3;
        private const float K = 30, Damp = 11, Gravity = 110, PadY = 130, PadHalf = 95, TouchY = PadY + 46, StartY = 960;

        public override float TimeLimit => 28;
        public override int[] Thresholds => new[] { 90, 170, 240 };
        public override string Status => "Landing " + Math.Min(Landings, Landed + 1) + " / " + Landings + " · " + Score + " pts";
        public override Vector2 HintTarget => _p;
        public override Vector2 HintTo => new Vector2(PadX, TouchY + 80);

        public Vector2 P => _p;
        public float PadX { get; private set; }
        public int Landed { get; private set; }
        public float Wind { get; private set; }
        private Vector2 _p, _v, _finger;
        private bool _held;
        private float _pause, _gust, _gustLeft;
        private int _lastScore = -1;

        public SaucerLanding(int seed) : base(seed) { NewApproach(); }

        private void NewApproach()
        {
            PadX = Range(230, 670);
            _p = new Vector2(Range(200, 700), StartY);
            _v = Vector2.Zero;
            _held = false;
        }

        protected override void Update()
        {
            if (_pause > 0)
            {
                _pause -= Dt;
                if (_pause <= 0) { if (Landed >= Landings) End(); else NewApproach(); }
                return;
            }
            // solar wind: a slow swell plus gusts
            _gustLeft -= Dt;
            if (_gustLeft <= 0) { _gustLeft = Range(1.2f, 2.4f); _gust = Range(-260, 260); }
            Wind = 120 * (float)Math.Sin(Time * 0.9 + Seed % 7) + _gust;
            var a = new Vector2(Wind, -Gravity);
            if (_held) a += K * (_finger - _p) - Damp * _v;
            else a += -1.2f * _v; // drifting: light air drag
            _v += a * Dt;
            _p += _v * Dt;
            if (_p.X < 60) { _p.X = 60; _v.X = Math.Max(0, _v.X); }
            if (_p.X > 840) { _p.X = 840; _v.X = Math.Min(0, _v.X); }
            if (_p.Y > 1040) { _p.Y = 1040; _v.Y = Math.Min(0, _v.Y); }
            if (_p.Y <= TouchY) Touchdown();
        }

        /// <summary>Softness 0–50 (≤ 60 u/s = 50, 400 u/s = 0) + accuracy 0–50 (centre = 50, pad edge = 0); off the pad 0.</summary>
        public static int LandingScore(float offset, float speed)
        {
            if (Math.Abs(offset) > PadHalf) return 0;
            float soft = 50 * Clamp(1 - (speed - 60) / 340, 0, 1);
            float acc = 50 * Clamp(1 - Math.Abs(offset) / PadHalf, 0, 1);
            return (int)Math.Round(soft + acc);
        }

        private void Touchdown()
        {
            float offset = _p.X - PadX;
            int pts = LandingScore(offset, Math.Max(0, -_v.Y));
            _lastScore = pts;
            Score += pts;
            Landed++;
            _p.Y = TouchY;
            _pause = 1.1f;
            if (pts == 0) { Pop("CRASH", PadX, TouchY + 160, C(Red)); Shake(0.4f); }
            else Pop((pts >= 85 ? "PERFECT +" : pts >= 55 ? "SMOOTH +" : "BUMPY +") + pts, PadX, TouchY + 160, C(pts >= 55 ? Green : Gold));
        }

        public override void Down(Vector2 p) { _held = true; _finger = p; }
        public override void Move(Vector2 p) { if (_held) _finger = p; }
        public override void Up(Vector2 p) { _held = false; }

        public override void Bot(MinigameTier aim)
        {
            if (_pause > 0 || Over) return;
            // plan the landing quality that keeps the total in the aimed band
            Band(aim, out int low, out int high);
            int left = Landings - Landed;
            float target = left > 0 ? Math.Max(0, low - Score) / (float)left : 0;
            float room = high == int.MaxValue ? 1000 : high - Score - 12;
            // planned landings: (points ≈, offset from the pad centre, touchdown speed)
            float[] pts = { 0, 40, 70, 95 }, offs = { 150, 55, 30, 0 }, spds = { 200, 250, 150, 40 };
            int pick = -1;
            for (int i = 0; i < pts.Length; i++) if (pts[i] >= target && pts[i] <= room) { pick = i; break; }
            if (pick < 0) for (int i = pts.Length - 1; i >= 0; i--) if (pts[i] <= room) { pick = i; break; }
            if (pick < 0) pick = 0;
            float offset = offs[pick], speed = spds[pick];
            float tx = PadX + offset - Wind / K;
            // descend at `speed`: steady state of the spring with a target below the saucer
            if (_p.Y > TouchY + 220) speed = Math.Max(speed, 380); // come down fast, slow down near the pad
            float lead = (speed - Gravity / Damp) * Damp / K;
            float ty = _p.Y > TouchY + 250 ? _p.Y - lead * 0.6f : _p.Y - lead;
            if (Math.Abs(_p.X - (PadX + offset)) > 40 && _p.Y < TouchY + 200) ty = TouchY + 230; // line up first
            if (!_held) Down(new Vector2(tx, ty)); else Move(new Vector2(tx, ty));
        }

        protected override void Render()
        {
            // pad
            Box(450, 50, 900, 100, C(0x23284F));
            Box(PadX, PadY, PadHalf * 2, 26, C(Green, 0.9f), 13);
            Box(PadX, PadY, 30, 26, C(Gold), 8);
            Ring(PadX, PadY + 10, 70 + 20 * (float)Math.Sin(Time * 5), 4, C(Green, 0.6f));
            // wind streaks
            for (int i = 0; i < 9; i++)
            {
                float y = 250 + i * 85;
                float x = (float)(((Time * Wind * 1.6 + i * 237) % 1100 + 1100) % 1100) - 100;
                float len = 40 + Math.Abs(Wind) * 0.35f;
                Box(x, y, len, 5, C(Cyan, 0.15f + Math.Min(0.35f, Math.Abs(Wind) / 900)), 2);
            }
            Text(450, 1060, Wind > 40 ? "WIND →" : Wind < -40 ? "← WIND" : "calm", 30, C(Cyan, 0.7f));
            // the finger's spring
            if (_held && _pause <= 0) Line(_p, _finger, 4, C(Cream, 0.35f));
            // saucer
            float tilt = Clamp(-_v.X * 0.04f, -25, 25) + 4 * (float)Math.Sin(Time * 7);
            float rad = tilt * (float)Math.PI / 180;
            var up = new Vector2(-(float)Math.Sin(rad), (float)Math.Cos(rad));
            Ellipse(_p.X + up.X * 22, _p.Y + up.Y * 22, 90, 70, C(Cyan, 0.65f), tilt);
            Alien(_p.X + up.X * 26, _p.Y + up.Y * 26, 44, C(Green), 1, false, false);
            Ellipse(_p.X, _p.Y, 190, 50, C(0xB8C0E8), tilt);
            Ellipse(_p.X, _p.Y - 6, 150, 22, C(Steel), tilt);
            for (int i = -1; i <= 1; i++) Circle(_p.X + i * 50 * up.Y, _p.Y + i * 50 * -up.X - 4, 14, C((Frame / 10 + i) % 2 == 0 ? Gold : Orange));
            if (_pause > 0 && _lastScore == 0) for (int i = 0; i < 6; i++) Circle(_p.X + (i - 2.5f) * 30, _p.Y + 20 + (i % 3) * 18, 28, C(Orange, _pause));
        }
    }
}
