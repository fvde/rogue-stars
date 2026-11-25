using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueStars.Client.World
{
    public enum WalkerMode { Commute, Wander, Sit }
    public enum WalkerState { Walking, Inside, AtFire, Waiting, Social, Cheer, Sitting }

    /// <summary>One alien walking in the city (or around the worm on the hub). Positions are in the owner's space (grid cells / ref px).</summary>
    public sealed class Walker
    {
        public int Id;
        public string AlienId;
        /// <summary>Commute: the building uid it works in.</summary>
        public string Home;
        /// <summary>Commute: the currency it carries to the fire (null = nothing).</summary>
        public string Goods;
        public bool Hat;
        public WalkerMode Mode;
        public WalkerState State;
        /// <summary>Commute route, door → the fireplace (grid points); S = distance walked along it, Dir +1 = toward the fire.</summary>
        public List<Vector2> Path;
        public float S;
        public int Dir = 1;
        public float Speed = 1.1f;
        public float Timer;
        public Vector2 Pos;
        public Vector2 Target;
        /// <summary>Wander: the area it picks targets in (ignored when <see cref="Spots"/> is set).</summary>
        public Rect Area;
        /// <summary>Wander: a point targets keep <see cref="AvoidRadius"/> away from (the fireplace).</summary>
        public Vector2 Avoid;
        public float AvoidRadius;
        /// <summary>Wander: points to walk between (a lap of the plaza for a newborn).</summary>
        public List<Vector2> Spots;
        public int SpotIndex;
        public int Laps;
        public bool FacingLeft;
        /// <summary>True while carrying goods toward the fire.</summary>
        public bool Carrying;
        /// <summary>Hop height 0..1 (cheer, social dance).</summary>
        public float Hop;
        public float Phase;
        /// <summary>Lane offset (so walkers on one street don't stack).</summary>
        public float Lane;
        public bool Visible => State != WalkerState.Inside;
    }

    /// <summary>
    /// The walkers and their social events (City design §7–8), client-only and cosmetic: a commuter walks building → fireplace with its
    /// goods, pops them at the fire and walks back; idle aliens wander (or sit by the fire at night). When two walkers come within
    /// <see cref="PairDistance"/> there is a 1-in-3 chance of a social event (both stop <see cref="StopTime"/> s); the same pair waits
    /// <see cref="PairCooldown"/> s and only one event plays every <see cref="GlobalGap"/> s; the pair check runs every
    /// <see cref="CheckEvery"/> s. Used by the City diorama and by the squad around the worm on the hub (C4). Pure; tested.
    /// </summary>
    public sealed class WalkerSim
    {
        public static readonly string[] SocialKinds = { "heart", "highfive", "scuffle", "gossip", "dance" };
        public const int MaxWalkers = 24;

        public float PairDistance = 0.5f;
        public float Chance = 1f / 3f;
        public float PairCooldown = 60f, GlobalGap = 6f, StopTime = 1.5f, CheckEvery = 0.25f;
        /// <summary>A failed roll waits this long before the same pair rolls again (one roll per meeting).</summary>
        public float RetryAfterMiss = 4f;
        public float InsideTime = 2.2f, FireTime = 0.7f;

        public readonly List<Walker> Walkers = new List<Walker>();
        public float Clock { get; private set; }
        public int EventsPlayed { get; private set; }
        public float LastEventAt { get; private set; } = -999;
        private float _nextCheck;
        private readonly Dictionary<long, float> _pairUntil = new Dictionary<long, float>();
        private readonly System.Random _rng;
        private int _nextId = 1;

        /// <summary>A roll in [0, 1) (tests replace it).</summary>
        public Func<double> Roll;
        /// <summary>Two walkers start a social event (kind index into <see cref="SocialKinds"/>).</summary>
        public event Action<Walker, Walker, int> Social;
        /// <summary>A commuter reached the fire with its goods.</summary>
        public event Action<Walker> Delivered;

        public WalkerSim(int seed = 1)
        {
            _rng = new System.Random(seed);
            Roll = () => _rng.NextDouble();
        }

        public float Random01() => (float)_rng.NextDouble();

        public Walker Add(Walker w)
        {
            w.Id = _nextId++;
            w.Phase = Random01() * 6.28f;
            Walkers.Add(w);
            return w;
        }

        public void Remove(Walker w) => Walkers.Remove(w);

        public Walker Find(string alienId)
        {
            foreach (var w in Walkers) if (w.AlienId == alienId) return w;
            return null;
        }

        // ---- moving -----------------------------------------------------------------------------------

        public void Step(float dt)
        {
            if (dt <= 0) return;
            Clock += dt;
            for (int i = 0; i < Walkers.Count; i++) Advance(Walkers[i], dt);
            if (Clock >= _nextCheck)
            {
                _nextCheck = Clock + CheckEvery;
                CheckPairs();
            }
        }

        private void Advance(Walker w, float dt)
        {
            w.Hop = w.State == WalkerState.Cheer ? Mathf.Abs(Mathf.Sin(w.Timer * Mathf.PI * 2 / 0.6f))
                : w.State == WalkerState.Social && w.Timer > 0 ? 0.25f * Mathf.Abs(Mathf.Sin(w.Timer * 9)) : 0;
            switch (w.State)
            {
                case WalkerState.Sitting:
                    return;
                case WalkerState.Inside:
                case WalkerState.AtFire:
                case WalkerState.Waiting:
                case WalkerState.Social:
                case WalkerState.Cheer:
                    w.Timer -= dt;
                    if (w.Timer > 0) return;
                    Resume(w);
                    return;
            }
            if (w.Mode == WalkerMode.Commute) Commute(w, dt);
            else if (w.Mode == WalkerMode.Wander) Wander(w, dt);
        }

        private void Resume(Walker w)
        {
            switch (w.State)
            {
                case WalkerState.Inside:
                    w.State = WalkerState.Walking;
                    w.Dir = 1;
                    w.Carrying = w.Goods != null;
                    break;
                case WalkerState.AtFire:
                    w.State = WalkerState.Walking;
                    w.Dir = -1;
                    break;
                case WalkerState.Waiting:
                    w.State = WalkerState.Walking;
                    NextTarget(w);
                    break;
                default:
                    w.State = w.Mode == WalkerMode.Sit ? WalkerState.Sitting : WalkerState.Walking;
                    break;
            }
        }

        private void Commute(Walker w, float dt)
        {
            if (w.Path == null || w.Path.Count == 0) { w.State = WalkerState.Inside; w.Timer = InsideTime; return; }
            float len = Length(w.Path);
            w.S = Mathf.Clamp(w.S + w.Dir * w.Speed * dt, 0, len);
            var before = w.Pos;
            w.Pos = At(w.Path, w.S);
            Face(w, before, w.Pos);
            if (w.Dir > 0 && w.S >= len - 1e-4f)
            {
                w.State = WalkerState.AtFire;
                w.Timer = FireTime;
                if (w.Carrying) { w.Carrying = false; Delivered?.Invoke(w); }
            }
            else if (w.Dir < 0 && w.S <= 1e-4f)
            {
                w.State = WalkerState.Inside;
                w.Timer = InsideTime + Random01() * 1.5f;
            }
        }

        private void Wander(Walker w, float dt)
        {
            var d = w.Target - w.Pos;
            float step = w.Speed * dt;
            if (d.magnitude <= step)
            {
                var before = w.Pos;
                w.Pos = w.Target;
                Face(w, before, w.Pos);
                if (w.Spots != null && w.Spots.Count > 0)
                {
                    w.SpotIndex++;
                    if (w.SpotIndex >= w.Spots.Count) { w.SpotIndex = 0; w.Laps++; }
                    w.Target = w.Spots[w.SpotIndex];
                    return;
                }
                w.State = WalkerState.Waiting;
                w.Timer = 0.8f + Random01() * 2.2f;
                return;
            }
            var prev = w.Pos;
            w.Pos += d.normalized * step;
            Face(w, prev, w.Pos);
        }

        /// <summary>A new random wander target inside the walker's area.</summary>
        public void NextTarget(Walker w)
        {
            if (w.Spots != null && w.Spots.Count > 0) { w.Target = w.Spots[w.SpotIndex % w.Spots.Count]; return; }
            w.Target = new Vector2(w.Area.xMin + Random01() * w.Area.width, w.Area.yMin + Random01() * w.Area.height);
            if (w.AvoidRadius > 0)
            {
                var d = w.Target - w.Avoid;
                if (d.magnitude < w.AvoidRadius) w.Target = w.Avoid + (d.sqrMagnitude < 1e-6f ? Vector2.right : d.normalized) * w.AvoidRadius;
            }
        }

        private static void Face(Walker w, Vector2 from, Vector2 to)
        {
            // the owner's x axis (the city view flips by the screen direction itself)
            float dx = to.x - from.x;
            if (Mathf.Abs(dx) > 1e-4f) w.FacingLeft = dx < 0;
        }

        public static float Length(List<Vector2> path)
        {
            float l = 0;
            for (int i = 1; i < path.Count; i++) l += Vector2.Distance(path[i - 1], path[i]);
            return l;
        }

        public static Vector2 At(List<Vector2> path, float s)
        {
            if (path.Count == 1) return path[0];
            for (int i = 1; i < path.Count; i++)
            {
                float seg = Vector2.Distance(path[i - 1], path[i]);
                if (s <= seg || i == path.Count - 1) return Vector2.Lerp(path[i - 1], path[i], seg <= 0 ? 1 : Mathf.Clamp01(s / seg));
                s -= seg;
            }
            return path[path.Count - 1];
        }

        // ---- social events --------------------------------------------------------------------------------

        private static long Key(Walker a, Walker b) => a.Id < b.Id ? ((long)a.Id << 32) | (uint)b.Id : ((long)b.Id << 32) | (uint)a.Id;

        private static bool Free(Walker w) => w.State == WalkerState.Walking || w.State == WalkerState.Waiting;

        /// <summary>Same-pair cooldown left (tests).</summary>
        public float PairWait(Walker a, Walker b) => _pairUntil.TryGetValue(Key(a, b), out var until) ? Mathf.Max(0, until - Clock) : 0;

        /// <summary>The pair check: at most one event starts per check, and none within <see cref="GlobalGap"/> of the last one.</summary>
        public void CheckPairs()
        {
            if (Clock - LastEventAt < GlobalGap) return;
            for (int i = 0; i < Walkers.Count; i++)
            {
                var a = Walkers[i];
                if (!Free(a) || !a.Visible) continue;
                for (int j = i + 1; j < Walkers.Count; j++)
                {
                    var b = Walkers[j];
                    if (!Free(b) || !b.Visible) continue;
                    if (Vector2.Distance(a.Pos, b.Pos) > PairDistance) continue;
                    long key = Key(a, b);
                    if (_pairUntil.TryGetValue(key, out var until) && until > Clock) continue;
                    if (Roll() >= Chance) { _pairUntil[key] = Clock + RetryAfterMiss; continue; }
                    Start(a, b, key);
                    return;
                }
            }
        }

        private void Start(Walker a, Walker b, long key)
        {
            int kind = (int)(Random01() * SocialKinds.Length) % SocialKinds.Length;
            _pairUntil[key] = Clock + PairCooldown;
            LastEventAt = Clock;
            EventsPlayed++;
            foreach (var w in new[] { a, b })
            {
                w.State = WalkerState.Social;
                w.Timer = StopTime;
            }
            a.FacingLeft = b.Pos.x < a.Pos.x;
            b.FacingLeft = a.Pos.x < b.Pos.x;
            Social?.Invoke(a, b, kind);
        }

        /// <summary>Level-up party: walkers within <paramref name="radius"/> of <paramref name="at"/> turn to it, hop twice and cheer.</summary>
        public int Cheer(Vector2 at, float radius)
        {
            int n = 0;
            foreach (var w in Walkers)
            {
                if (!w.Visible || w.State == WalkerState.Social) continue;
                if (Vector2.Distance(w.Pos, at) > radius) continue;
                w.State = WalkerState.Cheer;
                w.Timer = 1.2f;
                w.FacingLeft = at.x < w.Pos.x;
                n++;
            }
            return n;
        }
    }
}
