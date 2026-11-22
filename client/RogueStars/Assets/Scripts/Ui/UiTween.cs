using UnityEngine;

namespace RogueStars.Client.UiKit
{
    /// <summary>Tiny code tween: scale pop and alpha fade on unscaled time. No-op outside play mode.</summary>
    public sealed class UiTween : MonoBehaviour
    {
        private float _t;
        private float _duration = 0.18f;
        private Vector3 _fromScale = Vector3.one, _toScale = Vector3.one;
        private float _fromAlpha = 1, _toAlpha = 1;
        private CanvasGroup _group;
        private bool _destroyAtEnd;

        public static void PopIn(Component target, float duration = 0.18f)
        {
            if (!Application.isPlaying || target == null) return;
            var tw = target.gameObject.AddComponent<UiTween>();
            tw._duration = duration;
            tw._fromScale = new Vector3(0.85f, 0.85f, 1); tw._toScale = Vector3.one;
            target.transform.localScale = tw._fromScale;
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
            float k = Mathf.Clamp01(_t / _duration);
            float e = 1 - (1 - k) * (1 - k); // ease-out quad
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
