using UnityEngine;

namespace RogueStars.Client.World.Anim
{
    public enum WormState { Idle, Move, Attack, Hit, Happy }

    /// <summary>
    /// The worm's states as spine poses over time (technique 1). Idle breathes and sways; Move slithers and hops
    /// (<see cref="HopTo"/> moves the transform on an arc with take-off stretch and landing squash); Attack rears up and
    /// lunges forward; Hit recoils with a white flash and a damped wobble; Happy wiggles fast with little bounces. One-shot
    /// states fall back to Idle. Pure function of the state clock, so the same time gives the same pose.
    /// </summary>
    [RequireComponent(typeof(SpineWorm))]
    public sealed class WormMotion : MonoBehaviour
    {
        public const float AttackSeconds = 1.1f, HitSeconds = 0.7f, HappySeconds = 1.4f;

        private SpineWorm _worm;
        private float _t;
        private Vector3 _hopFrom, _hopTo;
        private float _hopT = -1, _hopDur;
        public WormState State { get; private set; } = WormState.Idle;
        public float Facing = 1;

        private void Awake() => _worm = GetComponent<SpineWorm>();

        public void Play(WormState s) { State = s; _t = 0; }

        /// <summary>A board hop to <paramref name="to"/> (parent space) over <paramref name="seconds"/>, slithering on the way.</summary>
        public void HopTo(Vector3 to, float seconds)
        {
            _hopFrom = transform.localPosition; _hopTo = to; _hopDur = seconds; _hopT = 0;
            if (Mathf.Abs(to.x - _hopFrom.x) > 0.01f) Facing = to.x < _hopFrom.x ? 1 : -1;
            Play(WormState.Move);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;
            var p = SpinePose.Rest;
            p.Facing = Facing;
            float breathe = Mathf.Sin(_t * 2 * Mathf.PI / 1.8f);
            switch (State)
            {
                case WormState.Idle:
                    p.Stretch = 1 + 0.035f * breathe;
                    p.Squash = 1 - 0.025f * breathe;
                    p.WaveAmp = 0.12f; p.WaveFreq = 0.6f; p.WavePhase = _t * 0.35f;
                    p.Lean = 0.05f * Mathf.Sin(_t * 1.1f);
                    break;
                case WormState.Move:
                {
                    p.WaveAmp = 0.45f; p.WaveFreq = 1.1f; p.WavePhase = _t * 2.2f;
                    p.Lean = 0.18f; // leaning into the travel direction
                    if (_hopT >= 0)
                    {
                        _hopT += dt / Mathf.Max(0.01f, _hopDur);
                        float h = Mathf.Clamp01(_hopT);
                        var pos = Vector3.Lerp(_hopFrom, _hopTo, Smooth(h));
                        pos.y += Mathf.Sin(h * Mathf.PI) * 0.55f;
                        transform.localPosition = pos;
                        // take-off stretch, airborne, landing squash
                        float stretch = h < 0.2f ? Mathf.Lerp(0.85f, 1.12f, h / 0.2f) : h < 0.85f ? 1.12f - (h - 0.2f) * 0.2f : Mathf.Lerp(1.0f, 0.82f, (h - 0.85f) / 0.15f);
                        p.Stretch = stretch; p.Squash = 1 / Mathf.Sqrt(stretch);
                        if (_hopT >= 1) { _hopT = -1; transform.localPosition = _hopTo; Play(WormState.Idle); _landT = 0; }
                    }
                    break;
                }
                case WormState.Attack:
                {
                    float a = _t / AttackSeconds;
                    if (a < 0.38f)
                    { // rear up: lean back, head hooks forward, body stretches tall
                        float k = Smooth(a / 0.38f);
                        p.Lean = -0.8f * k; p.HeadCurl = 0.7f * k; p.Stretch = 1 + 0.2f * k; p.Squash = 1 - 0.1f * k;
                        p.WaveAmp = 0.08f * k; p.WavePhase = _t * 6;
                    }
                    else if (a < 0.52f)
                    { // lunge: whip forward and travel
                        float k = Smooth((a - 0.38f) / 0.14f);
                        p.Lean = Mathf.Lerp(-0.8f, 1.15f, k); p.HeadCurl = Mathf.Lerp(0.7f, 0.35f, k);
                        p.Stretch = Mathf.Lerp(1.2f, 1.1f, k); p.Squash = 0.95f;
                        p.Offset = new Vector2(-0.45f * k * Facing, 0);
                    }
                    else
                    { // settle back with a little overshoot
                        float k = Smooth(Mathf.Clamp01((a - 0.52f) / 0.48f));
                        float wob = Mathf.Sin(k * Mathf.PI * 2) * (1 - k) * 0.15f;
                        p.Lean = Mathf.Lerp(1.15f, 0, k) + wob; p.HeadCurl = Mathf.Lerp(0.35f, 0, k);
                        p.Stretch = Mathf.Lerp(1.1f, 1, k); p.Squash = Mathf.Lerp(0.95f, 1, k);
                        p.Offset = new Vector2(-0.45f * (1 - k) * Facing, 0);
                    }
                    if (a >= 1) Play(WormState.Idle);
                    break;
                }
                case WormState.Hit:
                {
                    float a = _t / HitSeconds;
                    float decay = Mathf.Exp(-a * 5);
                    p.Lean = -0.6f * decay * Mathf.Cos(a * 14);
                    p.Squash = 1 + 0.12f * decay; p.Stretch = 1 - 0.1f * decay;
                    p.Offset = new Vector2(0.22f * decay * Facing, 0);
                    p.Flash = Mathf.Clamp01(1 - a * 4) * 0.85f;
                    if (a >= 1) Play(WormState.Idle);
                    break;
                }
                case WormState.Happy:
                {
                    float a = _t / HappySeconds;
                    float env = Mathf.Sin(Mathf.Clamp01(a) * Mathf.PI);
                    p.WaveAmp = 0.8f * env; p.WaveFreq = 1.4f; p.WavePhase = _t * 5.5f;
                    float bounce = Mathf.Abs(Mathf.Sin(_t * Mathf.PI * 3.2f));
                    p.Offset = new Vector2(0, bounce * 0.28f * env);
                    p.Stretch = 1 + 0.1f * bounce * env; p.Squash = 1 - 0.06f * bounce * env;
                    if (a >= 1) Play(WormState.Idle);
                    break;
                }
            }
            // landing squash that rings out after a hop
            if (_landT >= 0 && State == WormState.Idle)
            {
                _landT += dt;
                float d = Mathf.Exp(-_landT * 9);
                p.Squash *= 1 + 0.18f * d * Mathf.Cos(_landT * 22);
                p.Stretch *= 1 - 0.15f * d * Mathf.Cos(_landT * 22);
                if (_landT > 0.6f) _landT = -1;
            }
            _worm.Pose = p;
            _worm.Apply();
        }

        private float _landT = -1;

        private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }
    }
}
