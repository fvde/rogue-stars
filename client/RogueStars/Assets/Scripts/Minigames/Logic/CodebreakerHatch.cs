using System;
using System.Collections.Generic;
using System.Numerics;
using RogueStars.Rules.Systems;

namespace RogueStars.Client.Minigames
{
    /// <summary>
    /// 21 · Codebreaker Hatch: crack the vault's 4-colour code (6 colours, no repeats) in 6 tries. Tap colours to fill the
    /// 4 slots (tap a slot to clear it); a full row is tried at once. Gold pegs = right colour, right place; white pegs =
    /// right colour, wrong place. Score = 7 − tries used (0 if not cracked).
    /// </summary>
    public sealed class CodebreakerHatch : MinigameLogic
    {
        public const int Slots = 4, Tries = 6;
        public static readonly int[] Colors = { Red, Gold, Green, Cyan, Purple, Pink };
        private const float ButtonY = 95, RowTop = 880, RowH = 118;

        public override float TimeLimit => 30;
        public override int[] Thresholds => new[] { 1, 2, 3 };
        public override string Status => Cracked ? "Cracked!" : "Try " + Math.Min(Tries, History.Count + 1) + " / " + Tries;
        public override Vector2 HintTarget => ButtonCenter(0);

        public readonly int[] Code = new int[Slots];
        public readonly List<int[]> History = new List<int[]>();
        public readonly List<int> Hits = new List<int>(), Near = new List<int>();
        public readonly List<int> Current = new List<int>();
        public bool Cracked { get; private set; }
        private float _endIn = -1;

        public CodebreakerHatch(int seed) : base(seed)
        {
            var pool = new List<int> { 0, 1, 2, 3, 4, 5 };
            for (int i = 0; i < Slots; i++) { int k = Rng.Range(0, pool.Count); Code[i] = pool[k]; pool.RemoveAt(k); }
        }

        public static Vector2 ButtonCenter(int i) => new Vector2(450 + (i - 2.5f) * 140, ButtonY);
        private static Vector2 SlotCenter(int row, int s) => new Vector2(200 + s * 118, RowTop - row * RowH);

        protected override void Update()
        {
            if (_endIn > 0) { _endIn -= Dt; if (_endIn <= 0) End(); }
        }

        public override void Down(Vector2 p)
        {
            if (Over || _endIn > 0) return;
            for (int i = 0; i < Colors.Length; i++)
                if (Vector2.Distance(p, ButtonCenter(i)) <= 64) { Pick(i); return; }
            int row = History.Count;
            for (int s = 0; s < Current.Count; s++)
                if (Vector2.Distance(p, SlotCenter(row, s)) <= 52) { Current.RemoveAt(s); return; }
        }

        private void Pick(int color)
        {
            if (Current.Contains(color) || Current.Count >= Slots) return;
            Current.Add(color);
            if (Current.Count == Slots) Submit();
        }

        public static void Grade(int[] code, int[] guess, out int hits, out int near)
        {
            hits = 0; near = 0;
            for (int i = 0; i < code.Length; i++)
            {
                if (guess[i] == code[i]) hits++;
                else if (Array.IndexOf(code, guess[i]) >= 0) near++;
            }
        }

        private void Submit()
        {
            var guess = Current.ToArray();
            Current.Clear();
            Grade(Code, guess, out int hits, out int near);
            History.Add(guess); Hits.Add(hits); Near.Add(near);
            if (hits == Slots)
            {
                Cracked = true;
                Score = Tries + 1 - History.Count;
                Pop("HATCH OPEN!", 450, 560, C(Gold), 64);
                _endIn = 1.0f;
            }
            else if (History.Count >= Tries) { Pop("LOCKED", 450, 560, C(Red), 64); Shake(0.4f); _endIn = 1.0f; }
            else if (hits + near == 0) Pop("cold", 820, SlotCenter(History.Count - 1, 0).Y, C(Cyan, 0.8f), 36);
        }

        private float _botWait;

        /// <summary>Bot: knows the code; tries wrong codes until the try that keeps it in the aimed tier, then cracks it.</summary>
        public override void Bot(MinigameTier aim)
        {
            if (Over || _endIn > 0) return;
            _botWait += Dt;
            if (_botWait < 0.16f) return;
            _botWait = 0;
            Band(aim, out int low, out int high);
            // cracked on try k scores 7 − k: Gold cracks on try 3, the others on the try that scores exactly their minimum
            int crackAt = aim == MinigameTier.Miss ? int.MaxValue : aim == MinigameTier.Gold ? 3 : Tries + 1 - low;
            bool crack = History.Count + 1 >= crackAt;
            int slot = Current.Count;
            int color = crack ? Code[slot] : Code[(slot + 1) % Slots]; // a rotation of the code: never right
            if (Current.Contains(color)) color = Array.Find(new[] { 0, 1, 2, 3, 4, 5 }, c => !Current.Contains(c));
            Down(ButtonCenter(color));
        }

        protected override void Render()
        {
            // the hatch
            Circle(450, 1018, 140, C(Steel));
            Ring(450, 1018, 140, 10, C(Cracked ? Gold : Cream, 0.8f));
            Box(450, 1018, Cracked ? 20 : 100, 18, C(Cracked ? Gold : 0x23284F), 9, Cracked ? 90 : (float)(Time * 40 % 360));
            for (int r = 0; r < Tries; r++)
            {
                bool active = r == History.Count && !Cracked && _endIn <= 0;
                Box(450, RowTop - r * RowH, 840, RowH - 14, C(active ? 0x2E3570 : 0x1B2045, active ? 1 : 0.8f), 30);
                for (int s = 0; s < Slots; s++)
                {
                    var c = SlotCenter(r, s);
                    int col = r < History.Count ? History[r][s] : active && s < Current.Count ? Current[s] : -1;
                    if (col >= 0) Circle(c.X, c.Y, 86, C(Colors[col]));
                    else Ring(c.X, c.Y, 80, 4, C(Cream, active ? 0.5f : 0.15f));
                }
                if (r < History.Count)
                    for (int k = 0; k < Slots; k++)
                    {
                        float x = 720 + (k % 2) * 40, y = RowTop - r * RowH + (k < 2 ? 18 : -18);
                        if (k < Hits[r]) Circle(x, y, 30, C(Gold));
                        else if (k < Hits[r] + Near[r]) Circle(x, y, 30, C(0xFFFFFF));
                        else Ring(x, y, 26, 3, C(Cream, 0.3f));
                    }
            }
            for (int i = 0; i < Colors.Length; i++)
            {
                var b = ButtonCenter(i);
                bool used = Current.Contains(i);
                Circle(b.X, b.Y, 116, C(Colors[i], used ? 0.3f : 1));
                Ring(b.X, b.Y, 124, 5, C(Ink, used ? 0.1f : 0.5f));
            }
        }
    }
}
