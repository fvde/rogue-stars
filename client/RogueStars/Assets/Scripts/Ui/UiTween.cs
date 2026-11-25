using UnityEngine;

namespace RogueStars.Client.UiKit
{
    /// <summary>Tiny code tween: scale pop, card flips and alpha fade on unscaled time. No-op outside play mode.</summary>
    public sealed class UiTween : MonoBehaviour
    {
        private float _t;
        private float _delay;
        private float _duration = 0.18f;
        private Vector3 _fromScale = Vector3.one, _toScale = Vector3.one;
        private float _fromAlpha = 1, _toAlpha = 1;
        private CanvasGroup _group;
        private bool _destroyAtEnd;
        private bool _easeIn;

        public static void PopIn(Component target, float duration = 0.18f)
        {
            if (!Application.isPlaying || target == null) return;
            var tw = target.gameObject.AddComponent<UiTween>();
            tw._duration = duration;
            tw._fromScale = new Vector3(0.85f, 0.85f, 1); tw._toScale = Vector3.one;
            target.transform.localScale = tw._fromScale;
        }

        /// <summary>The second half of a card flip: edge-on (x scale 0) → face up, after <paramref name="delay"/> seconds.</summary>
        public static void FlipIn(Component target, float delay, float duration = 0.14f)
        {
            if (!Application.isPlaying || target == null) return;
            var tw = target.gameObject.AddComponent<UiTween>();
            tw._delay = delay;
            tw._duration = duration;
            tw._fromScale = new Vector3(0, 1, 1); tw._toScale = Vector3.one;
            target.transform.localScale = tw._fromScale;
        }

        /// <summary>The first half of a card flip: face up → edge-on, then destroyed (edit mode: destroyed at once).</summary>
        public static void FlipOut(Component target, float duration = 0.12f, float delay = 0)
        {
            if (target == null) return;
            if (!Application.isPlaying) { Ui.DestroyGo(target.gameObject); return; }
            var tw = target.gameObject.AddComponent<UiTween>();
            tw._delay = delay;
            tw._duration = duration;
            tw._fromScale = target.transform.localScale; tw._toScale = new Vector3(0, tw._fromScale.y, 1);
            tw._destroyAtEnd = true;
            tw._easeIn = true;
        }

        public static void FadeOut(Component target, float duration = 0.35f, bool destroy = true)
        {
            if (target == null) return;
            if (!Application.isPlaying) { if (destroy) Ui.DestroyGo(target.gameObject); return; }
            var tw = target.gameObject.AddComponent<UiTween>();
            tw._duration = duration;
            tw._group = target.GetComponent<CanvasGroup>();
            if (tw._group == null) tw._group = target.gameObject.AddComponent<CanvasGroup>();
            tw._fromAlpha = tw._group.alpha; tw._toAlpha = 0;
            tw._fromScale = tw._toScale = target.transform.localScale;
            tw._destroyAtEnd = destroy;
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            if (_t < _delay) return;
            float k = Mathf.Clamp01((_t - _delay) / _duration);
            float e = _easeIn ? k * k : 1 - (1 - k) * (1 - k); // ease-in quad (flip out) / ease-out quad
            transform.localScale = Vector3.LerpUnclamped(_fromScale, _toScale, e);
            if (_group != null) _group.alpha = Mathf.Lerp(_fromAlpha, _toAlpha, e);
            if (k >= 1)
            {
                if (_destroyAtEnd) Destroy(gameObject);
                else Destroy(this);
            }
        }
    }
}
