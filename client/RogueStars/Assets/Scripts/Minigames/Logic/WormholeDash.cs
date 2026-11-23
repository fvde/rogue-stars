using System;
using System.Collections.Generic;
using System.Numerics;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// 6 · Wormhole Dash: the worm races down a wormhole with two lanes; tap anywhere to switch lanes. Grab coins (+1), dodge
    /// asteroids (−3). 22 seconds, it keeps speeding up. Score = coins.
    /// </summary>
    public sealed class WormholeDash : MinigameLogic
    {
        private static readonly float[] Lanes = { 290, 610 };
        private const float WormY = 210, HitR = 80, RowGap = 330;

        public override float TimeLimit => 22;
        public override int[] Thresholds => new[] { 8, 16, 24 };
        public override string Status => "Coins " + Score;
        public override Vector2 HintTarget => new Vector2(Lanes[1 - Lane], WormY);

        public sealed class Thing { public int Lane; public float Y; public bool Rock, Done; }
        public readonly List<Thing> Things = new List<Thing>();
        public int Lane { get; private set; }
        private float _x = Lanes[0], _scroll, _nextRow = Height + 80, _hit;

        public WormholeDash(int seed) : base(seed) { }

        public float SpeedNow => 520 + 22 * Time;

        protected override void Update()
        {
            float v = SpeedNow;
            _scroll += v * Dt;
            _nextRow -= v * Dt;
            if (_nextRow <= Height + 60)
            {
                _nextRow += RowGap * Range(0.85f, 1.2f);
                int kind = Rng.Range(0, 6);
                int l = Rng.Range(0, 2);
                switch (kind)
                {
                    case 0: case 1: Things.Add(new Thing { Lane = l, Y = Height + 60, Rock = true }); Things.Add(new Thing { Lane = 1 - l, Y = Height + 60 }); break;
                    case 2: Things.Add(new Thing { Lane = l, Y = Height + 60, Rock = true }); break;
                    default:
                        Things.Add(new Thing { Lane = l, Y = Height + 60 });
                        Things.Add(new Thing { Lane = l, Y = Height + 60 + RowGap * 0.33f });
                        break;
                }
            }
            float target = Lanes[Lane];
            _x += Math.Sign(target - _x) * Math.Min(Math.Abs(target - _x), 2600 * Dt);
            if (_hit > 0) _hit -= Dt;
            foreach (var t in Things)
            {
                float before = t.Y;
                t.Y -= v * Dt;
                if (t.Done || !(before >= WormY && t.Y < WormY)) continue;
                if (Math.Abs(Lanes[t.Lane] - _x) > HitR) continue;
                t.Done = true;
                if (t.Rock) { Score = Math.Max(0, Score - 3); Pop("−3", _x, WormY + 120, C(Red)); Shake(); _hit = 0.4f; }
                else { Score++; Pop("+1", _x, WormY + 120, C(Gold), 48); }
            }
            Things.RemoveAll(t => t.Y < -80 || (t.Done && !t.Rock));
        }

        public override void Down(Vector2 p) => Lane = 1 - Lane;

        public override void Bot(MinigameTier aim)
        {
            Band(aim, out int low, out int high);
            // value of each lane for the next things to cross (within reach of one switch)
            float[] value = new float[2];
            float horizon = SpeedNow * 0.55f;
            foreach (var t in Things)
            {
                if (t.Done || t.Y < WormY - 10 || t.Y > WormY + horizon) continue;
                float w = 1 - (t.Y - WormY) / (horizon * 2);
                bool top = Score + 1 > high;
                if (t.Rock) value[t.Lane] += (Score > high && Score - 3 >= low ? 3 : Score - 3 >= low ? (top ? -1 : -3) : -6) * w;
                else value[t.Lane] += (Score < low || (!top && Tier < aim) ? 1 : top ? -4 : -0.5f) * w;
            }
            int best = value[1 - Lane] > value[Lane] + 0.01f ? 1 - Lane : Lane;
            if (best != Lane) Down(HintTarget);
        }

        protected override void Render()
        {
            // the tunnel: rings rushing towards the viewer
            for (int i = 0; i < 7; i++)
            {
                float k = (float)(((_scroll / 600.0) + i / 7.0) % 1.0);
                Ring(450, 640, 200 + k * 1100, 6 + k * 10, C(i % 2 == 0 ? Purple : Blue, 0.15f + 0.35f * k));
            }
            foreach (float lx in Lanes) Box(lx, 550, 220, 1100, C(Ink, 0.05f), 40);
            foreach (var t in Things)
            {
                if (t.Done) continue;
                float x = Lanes[t.Lane];
                if (t.Rock)
                {
                    Circle(x, t.Y, 130, C(0x8A6A5C));
                    Circle(x - 25, t.Y + 20, 34, C(0x6A4F44));
                    Circle(x + 30, t.Y - 18, 24, C(0x6A4F44));
                }
                else
                {
                    Circle(x, t.Y, 70, C(Gold));
                    Circle(x, t.Y, 46, C(0xFFE08A));
                    Text(x, t.Y, "C", 34, C(0x7A4A00));
                }
            }
            Worm(_x, WormY, 1.0f, false, _hit > 0 ? 2 : 0);
            if (_hit > 0) Ring(_x, WormY, 170, 8, C(Red, _hit / 0.4f));
        }
    }
}
