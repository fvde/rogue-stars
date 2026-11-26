using UnityEngine;

namespace RogueStars.Client.World.Anim
{
    /// <summary>
    /// A creature's clips as spine poses over time: idle (breathing sway), move (slither + hop squash-stretch; flyers hover),
    /// attack (rear up and lunge toward the target; slam and swoop styles), hit (recoil, flash), death (collapse and fade,
    /// held), celebrate (wiggle and hops). One-shot clips return to idle; a pose is a pure function of the clip's clock, so the
    /// same time gives the same pose. Plain C#: <see cref="Tick"/> allocates nothing.
    /// </summary>
    public sealed class CreatureMotion
    {
        /// <summary>Seconds of the clips at speed 1 (attack, hit, celebrate, death; move loops with <see cref="HopSeconds"/>).</summary>
        public const float AttackSeconds = 0.95f, HitSeconds = 0.6f, CelebrateSeconds = 1.5f, DeathSeconds = 1.0f, HopSeconds = 0.55f;
        private const float BlendSeconds = 0.12f;

        public readonly CreatureSpec Spec;
        public CreatureClip Clip { get; private set; } = CreatureClip.Idle;
        /// <summary>Screen direction of the target (+1 right, −1 left): where attack and recoil point.</summary>
        public float Aim = -1;
        /// <summary>Playback speed (battle ×1/×2/×3); multiplies the species speed.</summary>
        public float TimeScale = 1;
        /// <summary>Idle phase so a row of copies never sways in lockstep.</summary>
        public float PhaseOffset;
        /// <summary>The host moves the creature itself (a board hop with its own squash and arc): Move only slithers.</summary>
        public bool ExternalHop;
        /// <summary>Clips started so far per clip (tests, event mapping).</summary>
        public readonly int[] Played = new int[6];
        /// <summary>The clip's clock in seconds (already speed-scaled) and its length (0 = loops).</summary>
        public float Clock { get; private set; }
        public float Length { get; private set; }

        private float _t, _life;
        private bool _blending;
        private SpinePose _from, _last;

        public CreatureMotion(CreatureSpec spec) { Spec = spec ?? new CreatureSpec(); _last = SpinePose.Rest; }

        public bool IsOneShot(CreatureClip c) => c == CreatureClip.Attack || c == CreatureClip.Hit || c == CreatureClip.Celebrate || c == CreatureClip.Death;
        public bool Dead => Clip == CreatureClip.Death;
        /// <summary>A dead creature whose collapse is over: its pose never changes again, hosts stop ticking it.</summary>
        public bool Settled => Clip == CreatureClip.Death && Length > 0 && Clock > Length + 0.2f;
        public SpinePose Pose => _last;

        /// <summary>Length of a clip in seconds at the current speed (0 = loops).</summary>
        public float LengthOf(CreatureClip c)
        {
            float speed = Mathf.Max(0.05f, Spec.Speed * TimeScale);
            switch (c)
            {
                case CreatureClip.Attack: return AttackSeconds / speed;
                case CreatureClip.Hit: return HitSeconds / speed;
                case CreatureClip.Celebrate: return CelebrateSeconds / speed;
                case CreatureClip.Death: return DeathSeconds / speed;
                default: return 0;
            }
        }

        /// <summary>Starts a clip. <paramref name="seconds"/> &gt; 0 limits a looping clip (a single hop) before it returns to idle. A dead creature stays dead.</summary>
        public void Play(CreatureClip c, float seconds = 0)
        {
            if (Dead) return;
            _from = _last;
            _blending = true;
            Clip = c; _t = 0; _life = seconds;
            Length = seconds > 0 ? seconds : LengthOf(c);
            Played[(int)c]++;
        }

        /// <summary>Revives a dead creature (a Loop rewind) and returns to idle.</summary>
        public void Revive() { Clip = CreatureClip.Idle; _t = 0; _blending = false; _last = SpinePose.Rest; }

        /// <summary>Advances the clock by <paramref name="dt"/> real seconds and returns the new pose.</summary>
        public SpinePose Tick(float dt)
        {
            float speed = Mathf.Max(0.05f, Spec.Speed * TimeScale);
            _t += dt * speed;
            // a clip's length is in speed-scaled seconds; the clock below runs in unscaled clip time
            float amp = Spec.Amp, bend = Spec.Bend, squash = Spec.Squash;
            var p = SpinePose.Rest;
            p.Dir = Aim;
            Clock = _t / speed;
            switch (Clip)
            {
                case CreatureClip.Idle: Idle(ref p, amp, bend, squash); break;
                case CreatureClip.Move: Move(ref p, amp, bend, squash); break;
                case CreatureClip.Attack: Attack(ref p, amp, bend, squash); break;
                case CreatureClip.Hit: Hit(ref p, amp, bend, squash); break;
                case CreatureClip.Death: Death(ref p, amp, bend, squash); break;
                case CreatureClip.Celebrate: Celebrate(ref p, amp, bend, squash); break;
            }
            // a one-shot (or a limited loop) ends: back to idle, blended
            bool over = IsOneShot(Clip) ? Clip != CreatureClip.Death && Clock >= Length : _life > 0 && Clock >= _life;
            if (over) { Play(CreatureClip.Idle); }
            if (_blending && Clip != CreatureClip.Death)
            {
                float k = Mathf.Clamp01(_t / (BlendSeconds * speed));
                if (k >= 1) _blending = false; else p = SpinePose.Lerp(_from, p, k);
            }
            _last = p;
            return p;
        }

        private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }

        private void Idle(ref SpinePose p, float amp, float bend, float squash)
        {
            float t = _t + PhaseOffset;
            float breathe = Mathf.Sin(t * 2 * Mathf.PI / 1.8f);
            p.Stretch = 1 + 0.035f * amp * breathe;
            p.Squash = 1 - 0.025f * amp * squash * breathe;
            p.WaveAmp = 0.12f * amp * bend; p.WaveFreq = 0.6f; p.WavePhase = t * 0.35f;
            p.Lean = 0.05f * amp * bend * Mathf.Sin(t * 1.1f);
            if (Spec.Hover) p.Offset.y = 0.05f * amp * Mathf.Sin(t * 2.4f);
        }

        private void Move(ref SpinePose p, float amp, float bend, float squash)
        {
            float t = _t + PhaseOffset;
            p.WaveAmp = 0.45f * amp * bend; p.WaveFreq = 1.1f; p.WavePhase = t * 2.2f;
            p.Lean = 0.18f * amp * bend;
            if (ExternalHop) return;
            float cyc = (t / HopSeconds) % 1f;
            if (Spec.Hover)
            {
                p.Offset.y = 0.07f * amp * Mathf.Sin(t * 7f);
                p.Lean = 0.16f * amp;
                p.WaveAmp *= 0.4f;
                return;
            }
            // hop: take-off stretch, airborne, landing squash
            float h = Mathf.Sin(cyc * Mathf.PI);
            float stretch = cyc < 0.2f ? Mathf.Lerp(0.88f, 1.1f, cyc / 0.2f) : cyc < 0.8f ? 1.1f - (cyc - 0.2f) * 0.2f : Mathf.Lerp(0.98f, 0.85f, (cyc - 0.8f) / 0.2f);
            stretch = 1 + (stretch - 1) * amp * squash;
            p.Stretch = stretch; p.Squash = 1 / Mathf.Sqrt(Mathf.Max(0.5f, stretch));
            p.Offset.y = h * 0.09f * amp;
        }

        private void Attack(ref SpinePose p, float amp, float bend, float squash)
        {
            float a = Length > 0 ? Mathf.Clamp01(Clock / Length) : 0;
            float d = Aim < 0 ? -1 : 1;
            switch (Spec.Attack)
            {
                case CreatureAttack.Slam:
                    if (a < 0.4f)
                    { // rise: stretch tall, lean back
                        float k = Smooth(a / 0.4f);
                        p.Lean = -0.3f * amp * bend * k; p.Stretch = 1 + 0.22f * amp * k; p.Squash = 1 - 0.1f * amp * squash * k;
                    }
                    else if (a < 0.55f)
                    { // slam down toward the target
                        float k = Smooth((a - 0.4f) / 0.15f);
                        p.Lean = Mathf.Lerp(-0.3f, 0.6f, k) * amp * bend; p.Stretch = Mathf.Lerp(1.22f, 0.8f, k); p.Squash = Mathf.Lerp(0.9f, 1.3f, k * squash);
                        p.Offset = new Vector2(0.14f * amp * k * d, 0);
                    }
                    else
                    {
                        float k = Smooth((a - 0.55f) / 0.45f);
                        float wob = Mathf.Sin(k * Mathf.PI * 2) * (1 - k) * 0.08f;
                        p.Lean = Mathf.Lerp(0.6f, 0, k) * amp * bend + wob; p.Stretch = Mathf.Lerp(0.8f, 1, k); p.Squash = Mathf.Lerp(1.3f, 1, k);
                        p.Offset = new Vector2(0.14f * amp * (1 - k) * d, 0);
                    }
                    break;
                case CreatureAttack.Swoop:
                    if (a < 0.35f)
                    { // draw back and up
                        float k = Smooth(a / 0.35f);
                        p.Lean = -0.35f * amp * k; p.Offset = new Vector2(-0.08f * amp * k * d, 0.18f * amp * k); p.Stretch = 1 + 0.08f * k;
                    }
                    else if (a < 0.55f)
                    { // dive through the target
                        float k = Smooth((a - 0.35f) / 0.2f);
                        p.Lean = Mathf.Lerp(-0.35f, 0.9f, k) * amp; p.Offset = new Vector2(Mathf.Lerp(-0.08f, 0.35f, k) * amp * d, Mathf.Lerp(0.18f, -0.05f, k) * amp);
                        p.Stretch = 1.1f;
                    }
                    else
                    {
                        float k = Smooth((a - 0.55f) / 0.45f);
                        p.Lean = Mathf.Lerp(0.9f, 0, k) * amp; p.Offset = new Vector2(0.35f * amp * (1 - k) * d, Mathf.Lerp(-0.05f, 0, k) * amp); p.Stretch = Mathf.Lerp(1.1f, 1, k);
                    }
                    break;
                default: // Lunge: rear up, whip forward, settle
                    if (a < 0.38f)
                    {
                        float k = Smooth(a / 0.38f);
                        p.Lean = -0.8f * amp * bend * k; p.HeadCurl = 0.7f * amp * bend * k; p.Stretch = 1 + 0.2f * amp * k; p.Squash = 1 - 0.1f * amp * squash * k;
                        p.WaveAmp = 0.08f * k; p.WavePhase = _t * 6;
                    }
                    else if (a < 0.52f)
                    {
                        float k = Smooth((a - 0.38f) / 0.14f);
                        p.Lean = Mathf.Lerp(-0.8f, 1.15f, k) * amp * bend; p.HeadCurl = Mathf.Lerp(0.7f, 0.35f, k) * amp * bend;
                        p.Stretch = Mathf.Lerp(1.2f, 1.1f, k); p.Squash = 0.95f;
                        p.Offset = new Vector2(0.12f * k * d, 0);
                    }
                    else
                    {
                        float k = Smooth((a - 0.52f) / 0.48f);
                        float wob = Mathf.Sin(k * Mathf.PI * 2) * (1 - k) * 0.15f;
                        p.Lean = Mathf.Lerp(1.15f, 0, k) * amp * bend + wob; p.HeadCurl = Mathf.Lerp(0.35f, 0, k) * amp * bend;
                        p.Stretch = Mathf.Lerp(1.1f, 1, k); p.Squash = Mathf.Lerp(0.95f, 1, k);
                        p.Offset = new Vector2(0.12f * (1 - k) * d, 0);
                    }
                    break;
            }
        }

        private void Hit(ref SpinePose p, float amp, float bend, float squash)
        {
            float a = Length > 0 ? Mathf.Clamp01(Clock / Length) : 0;
            float decay = Mathf.Exp(-a * 5);
            p.Lean = -0.6f * decay * Mathf.Cos(a * 14) * amp * bend;
            p.Squash = 1 + 0.12f * decay * amp * squash; p.Stretch = 1 - 0.1f * decay * amp;
            // recoil away from the target
            p.Offset = new Vector2(-0.07f * decay * amp * (Aim < 0 ? -1 : 1), 0);
            p.Flash = Mathf.Clamp01(1 - a * 4) * 0.85f;
        }

        private void Death(ref SpinePose p, float amp, float bend, float squash)
        {
            float a = Length > 0 ? Smooth(Clock / Length) : 1;
            p.Stretch = Mathf.Lerp(1, 0.32f, a);
            p.Squash = Mathf.Lerp(1, 1.4f, a);
            p.Lean = -0.5f * a * bend * (Aim < 0 ? 1 : 1) + Mathf.Sin(a * Mathf.PI * 3) * (1 - a) * 0.25f;
            p.Offset = new Vector2(-0.1f * a * (Aim < 0 ? -1 : 1), 0);
            p.Alpha = Mathf.Lerp(1, 0.35f, a);
        }

        private void Celebrate(ref SpinePose p, float amp, float bend, float squash)
        {
            float a = Length > 0 ? Mathf.Clamp01(Clock / Length) : 0;
            float env = Mathf.Sin(a * Mathf.PI);
            p.WaveAmp = 0.8f * env * amp * bend; p.WaveFreq = 1.4f; p.WavePhase = _t * 5.5f;
            float bounce = Mathf.Abs(Mathf.Sin(_t * Mathf.PI * 3.2f));
            p.Offset.y = bounce * 0.28f * env * amp;
            p.Stretch = 1 + 0.1f * bounce * env * amp * squash; p.Squash = 1 - 0.06f * bounce * env * amp * squash;
        }
    }
}
