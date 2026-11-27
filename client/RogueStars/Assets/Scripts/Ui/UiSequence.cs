using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// Code-driven animation queue on unscaled time: each step runs update(t) for t in 0..1 over its duration, then
    /// its completion callback. Outside play mode (EditMode tests) every step completes immediately.
    /// </summary>
    public sealed class UiSequence : MonoBehaviour
    {
        private sealed class Step
        {
            public float Duration;
            public Action<float> Update;
            public Action Done;
            public string Label;
        }

        private readonly Queue<Step> _steps = new Queue<Step>();
        private Step _current;
        private float _t;

        /// <summary>Global speed multiplier applied to all sequences (battle speed toggles set their own).</summary>
        public float Speed = 1f;
        public bool Busy => _current != null || _steps.Count > 0;

        public static UiSequence On(GameObject go)
        {
            var s = go.GetComponent<UiSequence>();
            return s != null ? s : go.AddComponent<UiSequence>();
        }

        public UiSequence Then(float duration, Action<float> update, Action done = null)
        {
            var step = new Step { Duration = Mathf.Max(0.0001f, duration), Update = update, Done = done };
            if (!Application.isPlaying)
            {
                // Edit mode: complete synchronously (keeps tests deterministic).
                RunToEnd(step);
                return this;
            }
            _steps.Enqueue(step);
            return this;
        }

        public UiSequence Wait(float seconds, Action done = null) => Then(seconds, null, done);

        public UiSequence Call(Action action) => Then(0.0001f, null, action);

        /// <summary>A no-op step named <paramref name="label"/> (<see cref="FlushUntil"/> stops there).</summary>
        public UiSequence Mark(string label)
        {
            if (!Application.isPlaying) return this;
            _steps.Enqueue(new Step { Duration = 0.0001f, Label = label });
            return this;
        }

        /// <summary>
        /// Finish the running and queued steps at once up to the <see cref="Mark"/> <paramref name="label"/> (which stays
        /// queued and plays on as usual); nothing happens when no such mark is queued. True when it skipped anything.
        /// </summary>
        public bool FlushUntil(string label)
        {
            bool found = false;
            foreach (var s in _steps) if (s.Label == label) { found = true; break; }
            if (!found) return false;
            if (_current != null) { var c = _current; _current = null; RunToEnd(c); }
            while (_steps.Count > 0 && _steps.Peek().Label != label) RunToEnd(_steps.Dequeue());
            return true;
        }

        /// <summary>Finish every queued step immediately.</summary>
        public void Flush()
        {
            if (_current != null) { var c = _current; _current = null; RunToEnd(c); }
            while (_steps.Count > 0) RunToEnd(_steps.Dequeue());
        }

        public void Clear()
        {
            _current = null;
            _steps.Clear();
        }

        private static void RunToEnd(Step s)
        {
            s.Update?.Invoke(1f);
            s.Done?.Invoke();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime * Speed;
            while (dt > 0)
            {
                if (_current == null)
                {
                    if (_steps.Count == 0) return;
                    _current = _steps.Dequeue();
                    _t = 0;
                }
                float left = (1 - _t) * _current.Duration;
                float use = Mathf.Min(dt, left);
                _t += use / _current.Duration;
                dt -= use;
                var cur = _current;
                cur.Update?.Invoke(Mathf.Clamp01(_t));
                if (_t >= 0.9999f)
                {
                    _current = null;
                    cur.Done?.Invoke();
                    if (this == null) return; // destroyed by the callback
                }
                else return;
            }
        }
    }
}
