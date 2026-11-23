using System;
using System.Collections.Generic;
using System.Numerics;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// 7 · Shield Parry: coloured alien shots fly at the worm; tap the shield of the same colour just as a shot reaches the
    /// glowing ring. Too early, too late or the wrong colour and it hits; a press with nothing to parry locks the shields
    /// for a moment. 18 shots, faster and faster. Score = parries.
    /// </summary>
    public sealed class ShieldParry : MinigameLogic
    {
        public const int Shots = 18;
        private const float Cx = 450, Cy = 560, RingR = 170, Window = 0.17f, Lockout = 0.4f, ButtonY = 110;
        public static readonly int[] Colors = { Red, Cyan, Gold };
        private static readonly float[] ButtonX = { 170, 450, 730 };

        public override float TimeLimit => 26;
        public override int[] Thresholds => new[] { 6, 11, 15 };
        public override string Status => "Parried " + Score + " / " + Shots;
        public override Vector2 HintTarget => new Vector2(ButtonX[Next?.Color ?? 1], ButtonY);

        public sealed class Shot { public int Color; public Vector2 From; public float Launch, Impact; public bool Done, Parried; }
        public readonly List<Shot> Queue = new List<Shot>();
        private float _lock, _flash;
        private int _flashColor = -1, _resolved;

        public ShieldParry(int seed) : base(seed)
        {
            float t = 1.6f;
            for (int i = 0; i < Shots; i++)
            {
                float travel = Lerp(1.5f, 0.95f, i / (float)Shots);
                float ang = Range(-70, 70) * (float)Math.PI / 180;
                var from = new Vector2(Cx + 640 * (float)Math.Sin(ang), Cy + 640 * (float)Math.Cos(ang));
                Queue.Add(new Shot { Color = Rng.Range(0, 3), From = from, Launch = t, Impact = t + travel });
                t += Lerp(1.35f, 0.75f, i / (float)Shots);
            }
        }

        /// <summary>The next shot still in flight (or about to launch).</summary>
        public Shot Next { get { foreach (var s in Queue) if (!s.Done) return s; return null; } }

        public Vector2 PositionOf(Shot s)
        {
            float k = (Time - s.Launch) / (s.Impact - s.Launch);
            var dir = Vector2.Normalize(new Vector2(Cx, Cy) - s.From);
            var ringPoint = new Vector2(Cx, Cy) - dir * RingR;
            return s.From + (ringPoint - s.From) * k;
        }

        protected override void Update()
        {
            if (_lock > 0) _lock -= Dt;
            if (_flash > 0) _flash -= Dt;
            foreach (var s in Queue)
            {
                if (s.Done || Time <= s.Impact + Window) continue;
                s.Done = true; // too late: it hits
                _resolved++;
                Pop("OUCH", Cx, Cy + 260, C(Red), 52);
                Shake(0.25f);
            }
            if (_resolved >= Shots) End();
        }

        public override void Down(Vector2 p)
        {
            if (Over || _lock > 0 || p.Y > ButtonY + 110) return;
            int b = p.X < 310 ? 0 : p.X < 590 ? 1 : 2;
            _flash = 0.18f; _flashColor = b;
            var s = Next;
            if (s == null || Math.Abs(Time - s.Impact) > Window) { _lock = Lockout; return; } // nothing to parry: shields lock
            s.Done = true;
            _resolved++;
            if (s.Color == b) { s.Parried = true; Score++; Pop("PARRY!", Cx, Cy + 260, C(Colors[b]), 60); }
            else { Pop("WRONG", Cx, Cy + 260, C(Red), 52); Shake(0.25f); }
        }

        public override void Bot(MinigameTier aim)
        {
            var s = Next;
            if (s == null || Tier >= aim) return;
            if (Math.Abs(Time - s.Impact) <= 0.04f) Down(new Vector2(ButtonX[s.Color], ButtonY));
        }

        protected override void Render()
        {
            // the worm in its bubble and the parry ring
            var n = Next;
            bool near = n != null && Math.Abs(Time - n.Impact) <= Window;
            Ring(Cx, Cy, RingR * 2, near ? 10 : 6, C(near ? Colors[n.Color] : Cream, near ? 0.95f : 0.3f));
            Circle(Cx, Cy, 250, C(Cyan, 0.12f));
            Worm(Cx + 30, Cy, 1.2f);
            foreach (var s in Queue)
            {
                if (s.Done || Time < s.Launch) continue;
                var p = PositionOf(s);
                var dir = Vector2.Normalize(new Vector2(Cx, Cy) - s.From);
                for (int i = 3; i >= 1; i--) Circle(p.X - dir.X * i * 26, p.Y - dir.Y * i * 26, 50 - i * 9, C(Colors[s.Color], 0.5f - i * 0.12f));
                Circle(p.X, p.Y, 64, C(Colors[s.Color]));
                Circle(p.X, p.Y, 28, C(0xFFFFFF, 0.8f));
            }
            for (int b = 0; b < 3; b++)
            {
                bool hl = n != null && n.Color == b && Time > n.Launch;
                float scale = _flash > 0 && _flashColor == b ? 0.9f : 1;
                Box(ButtonX[b], ButtonY, 250 * scale, 150 * scale, C(Colors[b], _lock > 0 ? 0.35f : 1), 40);
                if (hl) Ring(ButtonX[b], ButtonY, 190, 6, C(Ink, 0.5f + 0.4f * (float)Math.Sin(Time * 10)));
                // shield glyph
                Ellipse(ButtonX[b], ButtonY + 4, 70 * scale, 84 * scale, C(0xFFFFFF, 0.55f));
            }
        }
    }
}
