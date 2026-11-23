using System;
using System.Collections.Generic;
using System.Numerics;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// 14 · Jelly Stack: an alien jelly block swings above the tower; tap to drop it. The part that hangs over the edge
    /// squishes off, so the blocks get narrower. A block that misses the tower costs one of 3 lives. Score = tower height.
    /// </summary>
    public sealed class JellyStack : MinigameLogic
    {
        public const int Lives = 3;
        private const float BlockH = 70, BaseY = 120, SwingY = 980, FallSpeed = 1900, StartW = 320;
        private static readonly int[] Jelly = { Green, Pink, Cyan, Gold, Purple, Orange };

        public override float TimeLimit => 26;
        public override int[] Thresholds => new[] { 4, 8, 12 };
        public override string Status => "Height " + Score + " · Lives " + (Lives - Misses);
        public override Vector2 HintTarget => new Vector2(450, 600);

        public sealed class Block { public float X, W; public int Color; public float Landed; }
        public readonly List<Block> Tower = new List<Block>();
        public int Misses { get; private set; }
        private float _x = 200, _dir = 1, _w = StartW, _dropY, _cut, _cutX, _cutAt = -9;
        private bool _dropping;
        private int _color;

        public JellyStack(int seed) : base(seed)
        {
            Tower.Add(new Block { X = 450, W = StartW, Color = -1, Landed = -9 });
            _x = Range(160, 740);
            _color = Rng.Range(0, Jelly.Length);
        }

        private float TopY => BaseY + Tower.Count * BlockH;
        private float Camera => Math.Max(0, TopY - 640);
        public float SwingX => _x;
        public Block Top => Tower[Tower.Count - 1];

        protected override void Update()
        {
            if (_dropping)
            {
                _dropY -= FallSpeed * Dt;
                float land = TopY + BlockH / 2 - Camera;
                if (_dropY <= land) Land();
                return;
            }
            float speed = 330 + 22 * Tower.Count;
            _x += _dir * speed * Dt;
            if (_x > 900 - _w / 2) { _x = 900 - _w / 2; _dir = -1; }
            if (_x < _w / 2) { _x = _w / 2; _dir = 1; }
        }

        private void Land()
        {
            _dropping = false;
            var top = Top;
            float left = Math.Max(_x - _w / 2, top.X - top.W / 2), right = Math.Min(_x + _w / 2, top.X + top.W / 2);
            if (right - left < 8)
            {
                Misses++;
                Pop("SPLAT", _x, TopY - Camera + 60, C(Red));
                Shake(0.3f);
                _cutX = _x; _cut = _w; _cutAt = Time;
                if (Misses >= Lives) End();
                Next();
                return;
            }
            float cut = _w - (right - left);
            if (cut > 1) { _cutX = _x < top.X ? left - cut / 2 : right + cut / 2; _cut = cut; _cutAt = Time; }
            Tower.Add(new Block { X = (left + right) / 2, W = right - left, Color = _color, Landed = Time });
            Score = Tower.Count - 1;
            Pop(cut <= 6 ? "PERFECT" : "+1", (left + right) / 2, TopY - Camera + 40, C(cut <= 6 ? Gold : Green), cut <= 6 ? 54 : 44);
            if (cut <= 6) { Top.W = Math.Min(StartW, Top.W + 8); } // perfect drops grow the block back a little
            _w = Top.W;
            Next();
        }

        private void Next()
        {
            _color = (_color + 1 + Rng.Range(0, Jelly.Length - 1)) % Jelly.Length;
            _x = Rng.NextDouble() < 0.5 ? _w / 2 + 10 : 890 - _w / 2;
            _dir = _x < 450 ? 1 : -1;
        }

        public override void Down(Vector2 p)
        {
            if (_dropping || Over) return;
            _dropping = true;
            _dropY = SwingY;
        }

        public override void Bot(MinigameTier aim)
        {
            if (_dropping || Over) return;
            float d = Math.Abs(_x - Top.X);
            if (Tier < aim ? d <= 5 : d >= _w + 10) Down(HintTarget);
        }

        protected override void Render()
        {
            float cam = Camera;
            Box(450, BaseY - 40 - cam, 520, 90, C(Steel), 20);
            for (int i = 1; i < Tower.Count; i++)
            {
                var b = Tower[i];
                float age = Time - b.Landed;
                float squish = age < 0.35f ? (float)Math.Sin(age / 0.35f * Math.PI) * 0.18f : 0;
                float sway = (float)Math.Sin(Time * 2.2 + i * 0.5) * Math.Min(10, i * 0.8f);
                float y = BaseY + (i - 0.5f) * BlockH - cam;
                Box(b.X + sway, y - squish * BlockH * 0.4f, b.W * (1 + squish), BlockH * (1 - squish), C(Jelly[b.Color], 0.92f), 22);
                Box(b.X + sway - b.W * 0.25f, y + BlockH * 0.2f, b.W * 0.3f, 10, C(0xFFFFFF, 0.35f), 5);
            }
            if (Time - _cutAt < 0.6f)
            {
                float k = (Time - _cutAt) / 0.6f;
                Box(_cutX, TopY - cam - 300 * k * k, _cut, BlockH * (1 - 0.4f * k), C(Red, 0.6f * (1 - k)), 20);
            }
            if (!Over)
            {
                float y = _dropping ? _dropY : SwingY;
                if (!_dropping) { Line(new Vector2(_x, y + BlockH / 2), new Vector2(_x, 1100), 4, C(Cream, 0.4f)); Box(_x, TopY - cam + 6, _w, 8, C(Cream, 0.18f), 4); }
                Box(_x, y, _w, BlockH, C(Jelly[_color]), 22);
                Circle(_x - 14, y + 6, 14, C(0x10202A));
                Circle(_x + 14, y + 6, 14, C(0x10202A));
            }
            for (int i = 0; i < Lives; i++) Circle(820 + i * 30 - 30, 1050, 24, C(Red, i < Lives - Misses ? 1 : 0.2f));
            Text(80, 1050, Score + "", 52, C(Gold));
        }
    }
}
