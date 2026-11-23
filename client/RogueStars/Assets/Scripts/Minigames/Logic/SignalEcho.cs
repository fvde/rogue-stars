using System;
using System.Collections.Generic;
using System.Numerics;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// 15 · Signal Echo: four aliens blink a signal one after another; tap them back in the same order. Each round the
    /// signal grows by one (2, 3, … 7). A wrong alien or a long pause ends the game. Score = rounds echoed.
    /// </summary>
    public sealed class SignalEcho : MinigameLogic
    {
        public const int Rounds = 6, FirstLength = 2;
        private const float On = 0.34f, Off = 0.12f, TapTimeout = 4f, PadR = 150;
        private static readonly Vector2[] Pads = { new Vector2(260, 720), new Vector2(640, 720), new Vector2(260, 330), new Vector2(640, 330) };
        private static readonly int[] Colors = { Green, Pink, Cyan, Gold };

        public override float TimeLimit => 30;
        public override int[] Thresholds => new[] { 2, 4, 6 };
        public override string Status => "Round " + Math.Min(Rounds, Score + 1) + " / " + Rounds;
        public override Vector2 HintTarget => Pads[_sequence.Count > 0 ? _sequence[Math.Min(_input, _sequence.Count - 1)] : 0];

        private readonly List<int> _sequence = new List<int>();
        private bool _showing = true;
        private float _clock = -0.6f, _idle, _lit = -1, _litFor;
        private int _input;
        private bool _failed;

        public SignalEcho(int seed) : base(seed)
        {
            for (int i = 0; i < FirstLength; i++) _sequence.Add(Rng.Range(0, 4));
        }

        public bool Listening => !_showing && !Over && !_failed;
        public IReadOnlyList<int> Sequence => _sequence;
        public int InputIndex => _input;

        protected override void Update()
        {
            if (_litFor > 0) { _litFor -= Dt; if (_litFor <= 0) _lit = -1; }
            if (_failed) { _clock += Dt; if (_clock > 0.7f) End(); return; }
            if (_showing)
            {
                _clock += Dt;
                float step = On + Off;
                int i = _clock < 0 ? -1 : (int)(_clock / step);
                if (i >= _sequence.Count) { _showing = false; _input = 0; _idle = 0; _lit = -1; return; }
                _lit = i >= 0 && _clock - i * step < On ? _sequence[i] : -1;
                return;
            }
            _idle += Dt;
            if (_idle > TapTimeout) Fail();
        }

        public override void Down(Vector2 p)
        {
            if (!Listening) return;
            int pad = -1;
            for (int i = 0; i < Pads.Length; i++) if (Vector2.Distance(p, Pads[i]) <= PadR) pad = i;
            if (pad < 0) return;
            _lit = pad; _litFor = 0.2f; _idle = 0;
            if (pad != _sequence[_input]) { Fail(); return; }
            _input++;
            if (_input < _sequence.Count) return;
            Score++;
            Pop("ECHO!", 450, 1000, C(Green), 60);
            if (Score >= Rounds) { End(); return; }
            _sequence.Add(Rng.Range(0, 4));
            _showing = true;
            _clock = -0.7f;
        }

        private void Fail()
        {
            _failed = true;
            _clock = 0;
            Pop("SIGNAL LOST", 450, 1000, C(Red), 56);
            Shake(0.35f);
        }

        private float _botWait;

        public override void Bot(MinigameTier aim)
        {
            if (!Listening) { _botWait = 0; return; }
            _botWait += Dt;
            if (_botWait < 0.22f) return;
            _botWait = 0;
            int pad = Tier < aim ? _sequence[_input] : (_sequence[_input] + 1) % 4;
            Down(Pads[pad]);
        }

        protected override void Render()
        {
            for (int i = 0; i < Pads.Length; i++)
            {
                var p = Pads[i];
                bool lit = (int)_lit == i;
                Circle(p.X, p.Y, PadR * 2 + (lit ? 30 : 0), C(Colors[i], lit ? 0.35f : 0.1f));
                Alien(p.X, p.Y + (lit ? 16 : 0), lit ? 230 : 200, C(Colors[i], lit ? 1 : 0.55f), 1 + i % 2, true, true);
                if (lit) Ring(p.X, p.Y, PadR * 2 + 50, 8, C(Colors[i], 0.8f));
            }
            // progress pips of the current signal
            for (int i = 0; i < _sequence.Count; i++)
            {
                float x = 450 + (i - (_sequence.Count - 1) / 2f) * 60;
                bool done = !_showing && i < _input;
                Circle(x, 120, 34, done ? C(Colors[_sequence[i]]) : C(Steel, 0.6f));
            }
            Text(450, 1060, _failed ? "" : _showing ? "WATCH…" : "YOUR TURN", 44, C(_showing ? Cream : Green));
        }
    }
}
