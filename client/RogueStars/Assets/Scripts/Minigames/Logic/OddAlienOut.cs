using System;
using System.Numerics;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// 25 · Odd Alien Out: a grid of near-identical aliens, one of them different (colour, eyes, antenna, mouth or tilt);
    /// tap it fast. 5 rounds with bigger grids and subtler differences; a wrong tap costs time. Points per round by speed
    /// (5 under 1.2 s … 2 under 5 s, 0 when it runs out). Score = points.
    /// </summary>
    public sealed class OddAlienOut : MinigameLogic
    {
        public const int Rounds = 5;
        private static readonly int[] Sizes = { 3, 4, 4, 5, 5 };
        private const float RoundTime = 5f, Pause = 0.5f, Penalty = 1.2f, Area = 820, Cy = 540;
        private static readonly int[] Bodies = { Green, Cyan, Pink, Gold, Purple };

        public override float TimeLimit => 30;
        public override int[] Thresholds => new[] { 8, 14, 20 };
        public override string Status => "Round " + Math.Min(Rounds, Round + 1) + " / " + Rounds + " · " + Score + " pts";
        public override Vector2 HintTarget => Cell(_odd);

        public int Round { get; private set; }
        private int _odd, _feature, _body;
        private float _elapsed, _pause, _wrongFlash;
        private int _wrongCell = -1;

        public OddAlienOut(int seed) : base(seed) { NewRound(); }

        public int OddCell => _odd;
        private int N => Sizes[Math.Min(Round, Rounds - 1)];

        public Vector2 Cell(int i)
        {
            int n = N;
            float step = Area / n;
            return new Vector2(450 + (i % n - (n - 1) / 2f) * step, Cy + ((n - 1) / 2f - i / n) * step);
        }

        private void NewRound()
        {
            _odd = Rng.Range(0, N * N);
            _feature = Rng.Range(0, 5);
            _body = Rng.Range(0, Bodies.Length);
            _elapsed = 0;
            _wrongCell = -1;
        }

        public static int Points(float seconds) => seconds < 1.2f ? 5 : seconds < 2.4f ? 4 : seconds < 3.6f ? 3 : seconds < RoundTime ? 2 : 0;

        protected override void Update()
        {
            if (_wrongFlash > 0) _wrongFlash -= Dt;
            if (_pause > 0)
            {
                _pause -= Dt;
                if (_pause <= 0) { Round++; if (Round >= Rounds) End(); else NewRound(); }
                return;
            }
            _elapsed += Dt;
            if (_elapsed >= RoundTime) { Pop("TOO SLOW", 450, 1020, C(Red)); _pause = Pause; }
        }

        public override void Down(Vector2 p)
        {
            if (_pause > 0 || Over) return;
            float half = Area / N / 2;
            for (int i = 0; i < N * N; i++)
            {
                var c = Cell(i);
                if (Math.Abs(p.X - c.X) > half || Math.Abs(p.Y - c.Y) > half) continue;
                if (i == _odd)
                {
                    int pts = Points(_elapsed);
                    Score += pts;
                    Pop("+" + pts, c.X, c.Y + 60, C(pts >= 4 ? Green : Gold));
                    _pause = Pause;
                }
                else { _elapsed += Penalty; _wrongCell = i; _wrongFlash = 0.35f; Shake(0.2f); }
                return;
            }
        }

        public override void Bot(MinigameTier aim)
        {
            if (_pause > 0 || Over) return;
            if (Tier < aim && _elapsed >= 0.3f) Down(Cell(_odd));
        }

        protected override void Render()
        {
            int n = N;
            float size = Area / n * 0.78f;
            // difference gets subtler: colour shift shrinks with the round
            float shift = Lerp(0.45f, 0.18f, Round / (float)(Rounds - 1));
            for (int i = 0; i < n * n; i++)
            {
                var c = Cell(i);
                bool odd = i == _odd && _pause <= 0;
                uint body = C(Bodies[_body]);
                int eyes = 1; bool antenna = true, mouth = true; float rot = 0;
                if (odd)
                    switch (_feature)
                    {
                        case 0: body = C(Bodies[(_body + 1) % Bodies.Length], 1 - shift); break;
                        case 1: eyes = 2; break;
                        case 2: antenna = false; break;
                        case 3: mouth = false; break;
                        default: rot = 28; break;
                    }
                if (i == _odd && _pause > 0) Ring(c.X, c.Y, size * 1.15f, 8, C(Green));
                Box(c.X, c.Y, size * 1.12f, size * 1.12f, C(i == _wrongCell && _wrongFlash > 0 ? Red : 0x1B2045, 0.9f), size * 0.2f);
                Alien(c.X, c.Y - size * 0.06f, size * 0.7f, body, eyes, antenna, mouth, rot);
            }
            // round timer
            float left = _pause > 0 ? 0 : Math.Max(0, 1 - _elapsed / RoundTime);
            Box(450 - 400 * (1 - left), 1030, 800 * left, 22, C(left > 0.5f ? Green : left > 0.25f ? Gold : Red), 11);
        }
    }
}
