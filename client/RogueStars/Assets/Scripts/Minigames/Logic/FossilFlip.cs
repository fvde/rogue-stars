using System;
using System.Collections.Generic;
using System.Numerics;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// 16 · Fossil Flip: 16 cards from a time rift hide 8 pairs of prehistoric fossils (shown face up for a moment at the
    /// start). Flip two at a time; matching pairs stay open. Clear pairs before the rift closes (28 s). Score = pairs.
    /// </summary>
    public sealed class FossilFlip : MinigameLogic
    {
        public const int Pairs = 8, Cols = 4;
        private const float Peek = 1.4f, CardW = 180, CardH = 210, Gap = 24, MismatchShow = 0.55f;
        private static readonly int[] Colors = { Cream, Gold, Green, Cyan, Pink, Purple, Orange, 0xB8C0E8 };

        public override float TimeLimit => 28;
        public override int[] Thresholds => new[] { 3, 5, 7 };
        public override string Status => "Pairs " + Score + " / " + Pairs;
        public override Vector2 HintTarget => CardCenter(0);

        public readonly int[] Cards = new int[Pairs * 2];
        public readonly bool[] Matched = new bool[Pairs * 2];
        private int _a = -1, _b = -1;
        private float _hideAt = -1;
        private readonly float[] _flipAt = new float[Pairs * 2];

        public FossilFlip(int seed) : base(seed)
        {
            for (int i = 0; i < Cards.Length; i++) Cards[i] = i / 2;
            for (int i = Cards.Length - 1; i > 0; i--) { int j = Rng.Range(0, i + 1); int t = Cards[i]; Cards[i] = Cards[j]; Cards[j] = t; }
            for (int i = 0; i < _flipAt.Length; i++) _flipAt[i] = -9;
        }

        public static Vector2 CardCenter(int i)
        {
            int r = i / Cols, c = i % Cols;
            return new Vector2(450 + (c - 1.5f) * (CardW + Gap), 900 - r * (CardH + Gap));
        }

        private bool Peeking => Time < Peek;
        public bool FaceUp(int i) => Peeking || Matched[i] || i == _a || i == _b;

        protected override void Update()
        {
            if (_hideAt > 0 && Time >= _hideAt) { _flipAt[_a] = _flipAt[_b] = Time; _a = _b = -1; _hideAt = -1; }
        }

        public override void Down(Vector2 p)
        {
            if (Over || Peeking || _hideAt > 0) return;
            for (int i = 0; i < Cards.Length; i++)
            {
                var c = CardCenter(i);
                if (Math.Abs(p.X - c.X) > CardW / 2 || Math.Abs(p.Y - c.Y) > CardH / 2) continue;
                Flip(i);
                return;
            }
        }

        private void Flip(int i)
        {
            if (Matched[i] || i == _a) return;
            _flipAt[i] = Time;
            if (_a < 0) { _a = i; return; }
            _b = i;
            if (Cards[_a] == Cards[_b])
            {
                Matched[_a] = Matched[_b] = true;
                Score++;
                var c = CardCenter(_b);
                Pop("MATCH!", c.X, c.Y + 80, C(Green), 48);
                _a = _b = -1;
                if (Score >= Pairs) End();
            }
            else _hideAt = Time + MismatchShow;
        }

        private float _botWait;

        public override void Bot(MinigameTier aim)
        {
            if (Over || Peeking || _hideAt > 0) return;
            _botWait += Dt;
            if (_botWait < 0.3f) return;
            _botWait = 0;
            bool want = Tier < aim;
            if (_a < 0)
            {
                for (int i = 0; i < Cards.Length; i++) if (!Matched[i]) { Down(CardCenter(i)); return; }
                return;
            }
            for (int i = 0; i < Cards.Length; i++)
                if (!Matched[i] && i != _a && (Cards[i] == Cards[_a]) == want) { Down(CardCenter(i)); return; }
        }

        private void Fossil(float x, float y, int kind, float s)
        {
            uint col = C(Colors[kind]);
            uint dark = C(0x3A2A28);
            switch (kind)
            {
                case 0: // ammonite
                    Ring(x, y, 110 * s, 14 * s, col); Ring(x + 8 * s, y, 66 * s, 10 * s, col); Circle(x + 14 * s, y, 22 * s, col); break;
                case 1: // bone
                    Box(x, y, 100 * s, 26 * s, col, 13 * s, 30);
                    foreach (int k in new[] { -1, 1 }) { Circle(x + k * 44 * s, y + k * 26 * s + 12 * s, 34 * s, col); Circle(x + k * 52 * s, y + k * 26 * s - 10 * s, 34 * s, col); }
                    break;
                case 2: // fern
                    Box(x, y, 10 * s, 120 * s, col, 5 * s);
                    for (int k = -2; k <= 2; k++) { Ellipse(x - 26 * s, y + k * 22 * s, 46 * s, 16 * s, col, 25); Ellipse(x + 26 * s, y + k * 22 * s, 46 * s, 16 * s, col, -25); }
                    break;
                case 3: // tooth
                    Box(x, y + 18 * s, 70 * s, 50 * s, col, 20 * s); Box(x - 18 * s, y - 24 * s, 22 * s, 56 * s, col, 10 * s, 12); Box(x + 18 * s, y - 24 * s, 22 * s, 56 * s, col, 10 * s, -12); break;
                case 4: // egg
                    Ellipse(x, y, 84 * s, 112 * s, col); Box(x - 8 * s, y + 10 * s, 40 * s, 6 * s, dark, 3 * s, 35); Box(x + 10 * s, y - 8 * s, 34 * s, 6 * s, dark, 3 * s, -30); break;
                case 5: // trilobite
                    Ellipse(x, y, 90 * s, 120 * s, col);
                    for (int k = -2; k <= 2; k++) Box(x, y + k * 20 * s, 80 * s, 5 * s, dark, 2 * s);
                    Box(x, y, 12 * s, 110 * s, dark, 5 * s); break;
                case 6: // skull
                    Circle(x, y + 8 * s, 100 * s, col); Box(x, y - 40 * s, 60 * s, 30 * s, col, 10 * s);
                    Circle(x - 22 * s, y + 10 * s, 26 * s, dark); Circle(x + 22 * s, y + 10 * s, 26 * s, dark); break;
                default: // claw
                    for (int k = -1; k <= 1; k++) Box(x + k * 28 * s, y, 22 * s, 100 * s, col, 11 * s, k * 18);
                    break;
            }
        }

        protected override void Render()
        {
            // the rift: closes as time runs out
            float left = Math.Max(0, 1 - Time / TimeLimit);
            Ellipse(450, 1045, 820 * left + 40, 50, C(Purple, 0.6f));
            Ellipse(450, 1045, 760 * left + 20, 28, C(Cyan, 0.7f));
            for (int i = 0; i < Cards.Length; i++)
            {
                var c = CardCenter(i);
                float since = Time - _flipAt[i];
                float flip = since < 0.18f ? Math.Abs(1 - 2 * since / 0.18f) : 1; // squash through the flip
                bool up = FaceUp(i);
                Box(c.X, c.Y, CardW * flip, CardH, C(up ? (Matched[i] ? 0x1F4A33 : 0x2E3570) : 0x4A3A8F), 26);
                if (up) { if (flip > 0.4f) Fossil(c.X, c.Y, Cards[i], flip); }
                else { Ring(c.X, c.Y, 90 * flip, 6, C(Cyan, 0.45f)); Circle(c.X, c.Y, 22 * flip, C(Cyan, 0.6f)); }
                if (Matched[i]) Ring(c.X, c.Y, 170, 5, C(Green, 0.5f));
            }
            if (Peeking) Text(450, 70, "REMEMBER THEM!", 46, C(Gold));
        }
    }
}
