using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RogueStars.Client.UiKit
{
    /// <summary>Distinguishes a tap from a press-and-hold (auto mode on the Roll button).</summary>
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public float HoldSeconds = 0.5f;
        public Action OnTap;
        public Action OnHold;

        private bool _down;
        private bool _fired;
        private float _t;

        public void OnPointerDown(PointerEventData e) { _down = true; _fired = false; _t = 0; }

        public void OnPointerUp(PointerEventData e)
        {
            if (_down && !_fired) OnTap?.Invoke();
            _down = false;
        }

        public void OnPointerExit(PointerEventData e) { if (_fired) _down = false; }

        private void Update()
        {
            if (!_down || _fired) return;
            _t += Time.unscaledDeltaTime;
            if (_t >= HoldSeconds) { _fired = true; OnHold?.Invoke(); }
        }
    }
}
