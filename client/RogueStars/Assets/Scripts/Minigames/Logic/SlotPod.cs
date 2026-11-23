using System;
using System.Numerics;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// 26 · Slot Pod: an alien slot pod with 3 reels. Tap SPIN, then tap the reels area to stop each reel yourself — the
    /// symbols scroll slowly enough to time it. 3 of a kind pays 12 pod coins, a pair 4. Between spins you may HOLD a reel
    /// (it keeps its symbol for the next spin) for 3 pod coins. 4 spins. Score = pod coins (in-game coins only).
    /// </summary>
    public sealed class SlotPod : MinigameLogic
    {
        public const int Spins = 4, Reels = 3, Symbols = 6, Triple = 12, Pair = 4, HoldCost = 3;
        private const float ReelSpeed = 6.5f, ReelW = 230, ReelH = 420, Cy = 640, SpinY = 120, HoldY = 330, SymbolH = 160;
        private static readonly float[] ReelX = { 200, 450, 700 };

        public override float TimeLimit => 30;
        public override int[] Thresholds => new[] { 6, 14, 24 };
        public override string Status => "Spin " + Math.Min(Spins, Spin + 1) + " / " + Spins + " · " + Score + " coins";
        public override Vector2 HintTarget => Spinning ? new Vector2(450, Cy) : new Vector2(450, SpinY);

        public int Spin { get; private set; }
        public bool Spinning { get; private set; }
        private readonly float[] _pos = new float[Reels];
        private readonly bool[] _stopped = new bool[Reels], _held = new bool[Reels];
        private readonly float[] _stopAt = new float[Reels];
        private float _idle, _resultAt = -9;
        private string _result = "";

        public SlotPod(int seed) : base(seed)
        {
            for (int r = 0; r < Reels; r++) { _pos[r] = Rng.Range(0, Symbols); _stopped[r] = true; _stopAt[r] = -9; }
        }

        /// <summary>The symbol a reel shows on the pay line.</summary>
        public int SymbolOf(int reel) => ((int)Math.Round(_pos[reel]) % Symbols + Symbols) % Symbols;
        /// <summary>The symbol under the line right now (while spinning).</summary>
        public int Showing(int reel) => ((int)Math.Floor(_pos[reel] + 0.5f) % Symbols + Symbols) % Symbols;
        private float Offset(int reel) => _pos[reel] - (float)Math.Floor(_pos[reel] + 0.5f);

        protected override void Update()
        {
            if (Spinning)
            {
                for (int r = 0; r < Reels; r++) if (!_stopped[r]) _pos[r] += ReelSpeed * Dt * (1 + r * 0.12f);
                return;
            }
            _idle += Dt;
            if (Spin >= Spins) { if (Time - _resultAt > 1.0f) End(); return; }
            if (_idle > 5f) StartSpin(); // no tap: spins by itself
        }

        private int NextReel { get { for (int r = 0; r < Reels; r++) if (!_stopped[r]) return r; return -1; } }

        public override void Down(Vector2 p)
        {
            if (Over) return;
            if (Spinning)
            {
                int r = NextReel;
                if (r < 0) return;
                _stopped[r] = true;
                _pos[r] = (float)Math.Round(_pos[r]);
                _stopAt[r] = Time;
                if (NextReel < 0) Settle();
                return;
            }
            if (Spin >= Spins) return;
            if (Spin > 0 && p.Y > HoldY - 50 && p.Y < HoldY + 50)
            {
                for (int r = 0; r < Reels; r++)
                    if (Math.Abs(p.X - ReelX[r]) < ReelW / 2) { ToggleHold(r); return; }
            }
            if (p.Y < SpinY + 70) StartSpin();
        }

        private void ToggleHold(int r)
        {
            if (_held[r]) { _held[r] = false; Score += HoldCost; return; }
            if (Score < HoldCost) { Pop("need " + HoldCost + " coins", ReelX[r], HoldY + 70, C(Red), 34); return; }
            int heldCount = 0;
            foreach (bool h in _held) if (h) heldCount++;
            if (heldCount >= Reels - 1) return;
            _held[r] = true;
            Score -= HoldCost;
        }

        private void StartSpin()
        {
            Spinning = true;
            _idle = 0;
            for (int r = 0; r < Reels; r++) _stopped[r] = _held[r];
            if (NextReel < 0) { for (int r = 0; r < Reels; r++) _stopped[r] = false; }
        }

        private void Settle()
        {
            Spinning = false;
            Spin++;
            _idle = 0;
            for (int r = 0; r < Reels; r++) _held[r] = false;
            int a = SymbolOf(0), b = SymbolOf(1), c = SymbolOf(2);
            int pay = a == b && b == c ? Triple : a == b || b == c || a == c ? Pair : 0;
            Score += pay;
            _result = pay == Triple ? "JACKPOT +" + pay : pay == Pair ? "PAIR +" + pay : "no match";
            _resultAt = Time;
            Pop(_result, 450, Cy + ReelH / 2 + 60, C(pay > 0 ? Gold : Cream, pay > 0 ? 1 : 0.7f), pay == Triple ? 64 : 48);
            if (pay == Triple) Shake(0.2f);
        }

        private float _botWait;

        public override void Bot(MinigameTier aim)
        {
            if (Over) return;
            _botWait += Dt;
            if (!Spinning)
            {
                if (Spin < Spins && _botWait > 0.4f) { _botWait = 0; Down(new Vector2(450, SpinY)); }
                return;
            }
            int r = NextReel;
            if (r < 0 || Math.Abs(Offset(r)) > 0.06f) return;
            // plan this spin's payout so the total stays in the aimed band
            Band(aim, out int low, out int high);
            int want = aim == MinigameTier.Gold ? Triple : Score >= low ? 0 : Score + Triple <= high ? Triple : Score + Pair <= high ? Pair : 0;
            int first = SymbolOf(0);
            bool ok;
            if (r == 0) ok = true;
            else if (want == Triple) ok = Showing(r) == first;
            else if (want == Pair) ok = r == 1 ? Showing(r) == first : Showing(r) != first && Showing(r) != SymbolOf(1);
            else ok = r == 1 ? Showing(r) != first : Showing(r) != first && Showing(r) != SymbolOf(1);
            if (ok) Down(new Vector2(450, Cy));
        }

        private void Symbol(float x, float y, int s, float scale, float alpha)
        {
            switch (s)
            {
                case 0: Alien(x, y, 120 * scale, C(Green, alpha)); break;
                case 1: Circle(x, y, 110 * scale, C(Pink, alpha)); Circle(x + 16 * scale, y + 12 * scale, 30 * scale, C(0xFFFFFF, alpha)); Circle(x + 18 * scale, y + 12 * scale, 14 * scale, C(0x1A0A12, alpha)); break; // worm head
                case 2: for (int i = 0; i < 5; i++) Box(x, y, 26 * scale, 120 * scale, C(Gold, alpha), 13 * scale, i * 36); break; // star
                case 3: Circle(x + 16 * scale, y + 16 * scale, 80 * scale, C(Cyan, alpha)); Circle(x - 20 * scale, y - 20 * scale, 50 * scale, C(Cyan, alpha * 0.5f)); Circle(x - 44 * scale, y - 44 * scale, 30 * scale, C(Cyan, alpha * 0.3f)); break; // comet
                case 4: Ring(x, y, 120 * scale, 16 * scale, C(Purple, alpha)); Ring(x, y, 70 * scale, 10 * scale, C(Cyan, alpha)); break; // portal
                default: Circle(x, y, 110 * scale, C(Gold, alpha)); Circle(x, y, 74 * scale, C(0xFFE08A, alpha)); Text(x, y, "C", (int)(52 * scale), C(0x7A4A00, alpha)); break; // coin
            }
        }

        protected override void Render()
        {
            // the pod
            Box(450, Cy, 860, ReelH + 120, C(0x2E3570), 70);
            Ring(450, Cy + ReelH / 2 + 90, 120, 10, C(Cyan, 0.6f));
            Circle(450, Cy + ReelH / 2 + 90, 60, C(Cyan, 0.3f + 0.3f * (float)Math.Sin(Time * 4)));
            for (int r = 0; r < Reels; r++)
            {
                float x = ReelX[r];
                Box(x, Cy, ReelW, ReelH, C(0x11142B), 30);
                float off = Offset(r);
                for (int k = -1; k <= 1; k++)
                {
                    int s = ((int)Math.Floor(_pos[r] + 0.5f) + k) % Symbols;
                    if (s < 0) s += Symbols;
                    float y = Cy - (k - off) * SymbolH;
                    float a = Math.Max(0, 1 - Math.Abs(k - off) * 0.6f);
                    if (Math.Abs(y - Cy) < ReelH / 2 - 30) Symbol(x, y, s, 0.9f, a);
                }
                bool stopFlash = Time - _stopAt[r] < 0.2f;
                if (stopFlash) Box(x, Cy, ReelW, ReelH, C(0xFFFFFF, 0.15f), 30);
                if (Spinning && r == NextReel) Ring(x, Cy, ReelW + 30, 6, C(Gold, 0.6f + 0.3f * (float)Math.Sin(Time * 10)));
                if (_held[r]) Box(x, Cy, ReelW + 10, ReelH + 10, C(Gold, 0.18f), 34);
                if (!Spinning && Spin > 0 && Spin < Spins)
                {
                    Box(x, HoldY, ReelW - 20, 70, C(_held[r] ? Gold : 0x23284F), 24);
                    Text(x, HoldY, _held[r] ? "HELD" : "HOLD " + HoldCost, 30, C(_held[r] ? 0x2A1300 : Cream));
                }
            }
            Box(450, Cy, 820, 8, C(Red, 0.6f), 4); // the pay line
            if (!Spinning && Spin < Spins)
            {
                Box(450, SpinY, 420, 120, C(Orange), 40);
                Text(450, SpinY, "SPIN", 56, C(0x2A1300));
            }
            else if (Spinning) Text(450, SpinY, "TAP TO STOP", 46, C(Gold));
            for (int i = 0; i < Spins; i++) Circle(450 + (i - 1.5f) * 50, 1060, 30, C(Gold, i < Spin ? 1 : 0.25f));
        }
    }
}
