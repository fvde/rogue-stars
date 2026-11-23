using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// Keyed one-shot tweens on unscaled time: one running tween per (object, channel); starting a new one on the same
    /// channel completes the old one first (update(1) + done), so fast playback never piles animations up. Outside play
    /// mode every tween completes synchronously (EditMode tests stay deterministic).
    /// </summary>
    public sealed class UiTweens : MonoBehaviour
    {
        private sealed class Tween
        {
            public float Duration, T;
            public Action<float> Update;
            public Action Done;
        }

        private readonly Dictionary<string, Tween> _running = new Dictionary<string, Tween>();
        private readonly List<string> _keys = new List<string>();

        /// <summary>Run <paramref name="update"/>(t) for t in 0..1 over <paramref name="duration"/> seconds on <paramref name="go"/>'s channel.</summary>
        public static void Play(GameObject go, string channel, float duration, Action<float> update, Action done = null)
        {
            if (go == null) return;
            if (!Application.isPlaying || duration <= 0.0001f)
            {
                var existing = go.GetComponent<UiTweens>();
                if (existing != null) existing.Finish(channel);
                update?.Invoke(1);
                done?.Invoke();
                return;
            }
            var host = go.GetComponent<UiTweens>();
            if (host == null) host = go.AddComponent<UiTweens>();
            host.Finish(channel);
            host._running[channel] = new Tween { Duration = duration, Update = update, Done = done };
            update?.Invoke(0);
        }

        /// <summary>Complete every running tween of <paramref name="go"/> now.</summary>
        public static void FinishAll(GameObject go)
        {
            var host = go != null ? go.GetComponent<UiTweens>() : null;
            if (host == null) return;
            host._keys.Clear();
            host._keys.AddRange(host._running.Keys);
            foreach (var k in host._keys) host.Finish(k);
        }

        private void Finish(string channel)
        {
            if (!_running.TryGetValue(channel, out var tw)) return;
            _running.Remove(channel);
            tw.Update?.Invoke(1);
            tw.Done?.Invoke();
        }

        private void Update()
        {
            if (_running.Count == 0) return;
            _keys.Clear();
            _keys.AddRange(_running.Keys);
            float dt = Time.unscaledDeltaTime;
            foreach (var k in _keys)
            {
                if (!_running.TryGetValue(k, out var tw)) continue;
                tw.T += dt / Mathf.Max(0.0001f, tw.Duration);
                if (tw.T >= 1) { Finish(k); if (this == null) return; }
                else tw.Update?.Invoke(tw.T);
            }
        }
    }

    /// <summary>
    /// Gentle idle motion: a vertical bob (the worm token on the board). Moves the object's own anchored position around
    /// the position it had when enabled, so a parent can be moved freely. Play mode only (no Update in edit mode).
    /// </summary>
    public sealed class UiBob : MonoBehaviour
    {
        public float Amplitude = 8f;
        public float Period = 1.6f;
        private RectTransform _rt;
        private Vector2 _base;
        private float _t;

        public static UiBob On(RectTransform rt, float amplitude = 8f, float period = 1.6f)
        {
            var b = rt.gameObject.AddComponent<UiBob>();
            b.Amplitude = amplitude; b.Period = period;
            return b;
        }

        private void OnEnable() { _rt = (RectTransform)transform; _base = _rt.anchoredPosition; _t = 0; }
        private void OnDisable() { if (_rt != null) _rt.anchoredPosition = _base; }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            _rt.anchoredPosition = _base + new Vector2(0, Amplitude * Mathf.Sin(_t * 2 * Mathf.PI / Mathf.Max(0.1f, Period)));
        }
    }

    /// <summary>Breathing glow: scale and alpha oscillate (the next level node, ROLL when ready). Play mode only.</summary>
    public sealed class UiPulse : MonoBehaviour
    {
        public float Scale = 0.08f;
        public float Period = 1.4f;
        public float MinAlpha = 0.45f;
        private Graphic _g;
        private float _alpha;
        private float _t;

        public static UiPulse On(Component c, float scale = 0.08f, float period = 1.4f, float minAlpha = 0.45f)
        {
            var p = c.gameObject.AddComponent<UiPulse>();
            p.Scale = scale; p.Period = period; p.MinAlpha = minAlpha;
            return p;
        }

        private void OnEnable() { _g = GetComponent<Graphic>(); _alpha = _g != null ? _g.color.a : 1; _t = 0; }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            float k = 0.5f + 0.5f * Mathf.Sin(_t * 2 * Mathf.PI / Mathf.Max(0.1f, Period));
            transform.localScale = Vector3.one * (1 + Scale * k);
            if (_g != null)
            {
                var c = _g.color;
                c.a = _alpha * Mathf.Lerp(MinAlpha, 1, k);
                _g.color = c;
            }
        }
    }
}
