using System;
using System.Numerics;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// 1 · Portal Slip: a portal ring pulses open and shut; tap while it is fully open (inside the glowing outline) to slip
    /// the worm through. 5 attempts, each one faster with a narrower window. Score = slips.
    /// </summary>
    public sealed class PortalSlip : MinigameLogic
    {
        public const int Attempts = 5;
        private static readonly float[] Periods = { 2.2f, 1.9f, 1.6f, 1.4f, 1.2f };
        /// <summary>Open fraction that still counts as "fully open": open ≥ 1 − window.</summary>
        private static readonly float[] Windows = { 0.10f, 0.085f, 0.07f, 0.06f, 0.05f };
        private const float MinD = 160, MaxD = 620, Pause = 1.0f, Cx = 450, Cy = 620;

        public override float TimeLimit => 25;
        public override int[] Thresholds => new[] { 2, 3, 5 };
        public override string Status => "Slips " + Score + " / " + Attempts;
        public override Vector2 HintTarget => new Vector2(Cx, Cy);

        public int Attempt { get; private set; }
        private float _phase;          // 0..1 per cycle, open = 0.5 − 0.5 cos(2π phase)
        private float _pause;          // after a tap: the slip / bounce animation
        private bool _lastHit;
        private float _waited;
        private readonly bool[] _results = new bool[Attempts];

        public PortalSlip(int seed) : base(seed) { _phase = Range(0, 0.15f); }

        public float Open => 0.5f - 0.5f * (float)Math.Cos(2 * Math.PI * _phase);
        private float Window => Windows[Math.Min(Attempt, Attempts - 1)];
        public bool InWindow => Open >= 1 - Window;

        protected override void Update()
        {
            if (_pause > 0)
            {
                _pause -= Dt;
                if (_pause <= 0 && Attempt >= Attempts) End();
                return;
            }
            _phase += Dt / Periods[Math.Min(Attempt, Attempts - 1)];
            _waited += Dt;
            if (_waited > 2.5f * Periods[Math.Min(Attempt, Attempts - 1)]) Resolve(false, "TOO SLOW"); // never waits forever
        }

        public override void Down(Vector2 p)
        {
            if (Over || _pause > 0 || Attempt >= Attempts) return;
            Resolve(InWindow, InWindow ? "SLIP!" : "MISSED");
        }

        private void Resolve(bool hit, string text)
        {
            _lastHit = hit;
            if (hit) Score++;
            else Shake();
            Pop(text, Cx, Cy + 380, hit ? C(Green) : C(Red), 64);
            _results[Attempt] = hit;
            Attempt++;
            _pause = Pause;
            _waited = 0;
            _phase = 0;
        }

        public override void Bot(MinigameTier aim)
        {
            if (_pause > 0 || Over) return;
            bool want = Tier < aim;
            if (want ? Open >= 1 - Window * 0.5f : Open < 0.25f) Down(HintTarget);
        }

        protected override void Render()
        {
            float open = _pause > 0 ? (_lastHit ? 1 : Open) : Open;
            float d = MinD + (MaxD - MinD) * open;
            bool ready = _pause <= 0 && InWindow;
            // the "fully open" outline: the target
            Ring(Cx, Cy, MaxD + 24, 6, C(ready ? Green : Cream, ready ? 0.95f : 0.35f));
            Ring(Cx, Cy, MaxD - (MaxD - MinD) * Window - 6, 3, C(Cream, 0.18f));
            // the portal: a swirl of rings
            Circle(Cx, Cy, d, C(0x2A1F6B, 0.85f));
            for (int i = 0; i < 4; i++)
            {
                float k = 1 - i * 0.2f;
                Ring(Cx, Cy, d * k, 10 - i * 2, C(i % 2 == 0 ? Purple : Cyan, 0.9f - i * 0.15f));
            }
            Circle(Cx, Cy, d * 0.25f, C(Ink, 0.25f + 0.3f * open));
            // the worm waiting below / slipping through
            float wy = 170, ws = 1.1f;
            if (_pause > 0 && _lastHit)
            {
                float k = 1 - _pause / Pause;
                wy = Lerp(170, Cy, k * 1.6f);
                ws = Lerp(1.1f, 0.2f, k * 1.4f);
            }
            if (ws > 0.21f) Worm(Cx, wy, ws);
            // attempts
            for (int i = 0; i < Attempts; i++)
            {
                float x = Cx + (i - 2) * 70;
                bool done = i < Attempt;
                Circle(x, 1040, 40, C(done ? (_results[i] ? Green : Red) : Steel, done ? 1 : 0.5f));
            }
        }
    }
}
