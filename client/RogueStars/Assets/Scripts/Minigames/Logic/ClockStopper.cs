using System;
using System.Numerics;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// 5 · Clock Stopper: a broken time-clock spins its hand fast (faster each time, changing direction); tap to stop it on
    /// the glowing target hour. 6 targets; within 6° = PERFECT 3, 15° = GOOD 2, 30° = OK 1. Score = points.
    /// </summary>
    public sealed class ClockStopper : MinigameLogic
    {
        public const int Targets = 6;
        private const float Cx = 450, Cy = 560, R = 330, Pause = 0.8f, Timeout = 4.0f;
        private static readonly float[] Speeds = { 210, 240, 270, 300, 320, 340 };

        public override float TimeLimit => 30;
        public override int[] Thresholds => new[] { 6, 11, 15 };
        public override string Status => "Time " + Math.Min(Targets, Index + 1) + " / " + Targets + " · " + Score + " pts";
        public override Vector2 HintTarget => new Vector2(Cx, Cy);

        public int Index { get; private set; }
        /// <summary>Hand angle in degrees, clockwise from 12 o'clock.</summary>
        public float Hand { get; private set; }
        public float TargetAngle { get; private set; }
        private readonly int[] _hours = new int[Targets];
        private float _pause, _waited;
        private int _dir = 1;

        public ClockStopper(int seed) : base(seed)
        {
            int last = -1;
            for (int i = 0; i < Targets; i++)
            {
                int h;
                do h = Rng.Range(1, 13); while (h == last);
                _hours[i] = last = h;
            }
            Hand = Range(0, 360);
            Begin();
        }

        private void Begin()
        {
            if (Index >= Targets) return;
            TargetAngle = (_hours[Index] % 12) * 30;
            _dir = Index % 2 == 0 ? 1 : -1;
            _waited = 0;
        }

        public float Error => Math.Abs(((Hand - TargetAngle) % 360 + 540) % 360 - 180);

        public static int Points(float error) => error <= 6 ? 3 : error <= 15 ? 2 : error <= 30 ? 1 : 0;

        protected override void Update()
        {
            if (_pause > 0)
            {
                _pause -= Dt;
                if (_pause <= 0) { if (Index >= Targets) End(); else Begin(); }
                return;
            }
            Hand = ((Hand + _dir * Speeds[Index] * Dt) % 360 + 360) % 360;
            _waited += Dt;
            if (_waited >= Timeout) Stop();
        }

        public override void Down(Vector2 p)
        {
            if (_pause > 0 || Over || Index >= Targets) return;
            Stop();
        }

        private void Stop()
        {
            int pts = _waited >= Timeout ? 0 : Points(Error);
            Score += pts;
            Pop(pts == 3 ? "PERFECT" : pts == 2 ? "GOOD" : pts == 1 ? "OK" : "MISS", Cx, Cy + R + 70, C(pts >= 2 ? Green : pts == 1 ? Gold : Red), 60);
            if (pts == 0) Shake(0.25f);
            Index++;
            _pause = Pause;
        }

        public override void Bot(MinigameTier aim)
        {
            if (_pause > 0 || Over) return;
            bool want = Tier < aim;
            if (want ? Error <= 3 : Error >= 80 && Error <= 120) Down(HintTarget);
        }

        private static Vector2 At(float deg, float r) =>
            new Vector2(Cx + r * (float)Math.Sin(deg * Math.PI / 180), Cy + r * (float)Math.Cos(deg * Math.PI / 180));

        protected override void Render()
        {
            Circle(Cx, Cy, R * 2 + 40, C(0x2A1F6B));
            Ring(Cx, Cy, R * 2 + 40, 14, C(Gold, 0.85f));
            Circle(Cx, Cy, R * 2 - 10, C(0x161A3A));
            // the target hour: a glowing wedge
            if (Index < Targets)
            {
                for (int i = -3; i <= 3; i++)
                {
                    var p = At(TargetAngle + i * 5, R - 60);
                    Circle(p.X, p.Y, 46 - Math.Abs(i) * 8, C(Green, 0.5f + 0.2f * (float)Math.Sin(Time * 6)));
                }
            }
            for (int h = 1; h <= 12; h++)
            {
                var p = At(h * 30, R - 60);
                bool target = Index < Targets && h % 12 == _hours[Index] % 12;
                Text(p.X, p.Y, h.ToString(), target ? 56 : 44, C(target ? 0x10202A : Cream));
                var t = At(h * 30, R - 4);
                Circle(t.X, t.Y, 12, C(Cream, 0.6f));
            }
            // the hand (time-travel needle) with a ghost trail
            for (int i = 3; i >= 1; i--) Line(new Vector2(Cx, Cy), At(Hand - _dir * i * 7, R - 30), 18 - i * 3, C(Cyan, 0.12f * (4 - i)));
            Line(new Vector2(Cx, Cy), At(Hand, R - 30), 18, C(_pause > 0 ? (Points(Error) > 0 ? Green : Red) : Cyan));
            Circle(Cx, Cy, 60, C(Gold));
            Circle(Cx, Cy, 26, C(Orange));
            // next targets
            for (int i = 0; i < Targets; i++)
            {
                float x = 450 + (i - 2.5f) * 120;
                bool cur = i == Index;
                Box(x, 1040, 96, 64, C(i < Index ? Steel : cur ? Green : 0x23284F, i < Index ? 0.5f : 1), 20);
                Text(x, 1040, _hours[i] + ":00", 30, C(cur ? 0x10202A : Ink));
            }
        }
    }
}
