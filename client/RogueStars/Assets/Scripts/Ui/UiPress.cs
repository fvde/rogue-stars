using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// Instant tap feedback for every button of the app, in one place (lives on the canvas root):
    /// <list type="bullet">
    /// <item>pointer down on a button (any interactable <see cref="Selectable"/> or a <see cref="HoldButton"/>) presses it at
    /// once (scale <see cref="PressScale"/>); released on pointer up, or as soon as the finger drags (a scroll is no tap);</item>
    /// <item>a tap that sends a command (the in-flight count rises in the frame of the tap) keeps the button pressed and
    /// shows it busy (dimmed, further taps ignored) until no command is in flight any more.</item>
    /// </list>
    /// Generic on purpose: it only reads the in-flight count, so a command that resolves at once (client prediction)
    /// never shows the busy look.
    /// </summary>
    public sealed class UiPress : MonoBehaviour
    {
        public const float PressScale = 0.94f;
        public const float BusyAlpha = 0.6f;

        /// <summary>Commands in flight now (GameSession.InFlight).</summary>
        public Func<int> InFlight;

        private struct Busy
        {
            public Transform Target;
            public CanvasGroup Group;
            public bool AddedGroup;
            public float Alpha;
            public bool Interactable;
            public Vector3 Rest;
        }

        private Transform _pressed;
        private Vector3 _rest;
        private Vector2 _downAt;
        private Transform _tapped;
        private Vector3 _tappedRest;
        private int _lastInFlight;
        private readonly List<Busy> _busy = new List<Busy>();
        private readonly List<RaycastResult> _hits = new List<RaycastResult>();

        /// <summary>The button pressed right now (tests).</summary>
        public Transform Pressed => _pressed;

        /// <summary>Is <paramref name="t"/> shown busy (its command in flight)?</summary>
        public bool IsBusy(Transform t) => t != null && _busy.Exists(b => b.Target == t);

        /// <summary>The pressable button that receives a press on <paramref name="hit"/> (null: nothing to press).</summary>
        public static Transform Target(GameObject hit)
        {
            if (hit == null) return null;
            var h = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
            if (h == null) h = ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit);
            if (h == null || h.GetComponent<InputField>() != null) return null;
            var sel = h.GetComponent<Selectable>();
            if (sel != null) return sel.IsInteractable() ? h.transform : null;
            return h.GetComponent<HoldButton>() != null ? h.transform : null;
        }

        /// <summary>Press <paramref name="t"/> (pointer down).</summary>
        public void Press(Transform t)
        {
            Release(false);
            if (t == null || IsBusy(t)) return;
            _pressed = t;
            _rest = t.localScale;
            t.localScale = _rest * PressScale;
        }

        /// <summary>Pointer up (<paramref name="tap"/>: released on the pressed button) or a drag ends the press.</summary>
        public void Release(bool tap)
        {
            if (_pressed == null) return;
            if (_pressed.localScale == _rest * PressScale) _pressed.localScale = _rest;
            if (tap) { _tapped = _pressed; _tappedRest = _rest; }
            _pressed = null;
        }

        /// <summary>End of frame: a tap that started a command shows busy; busy buttons recover once nothing is in flight.</summary>
        public void Settle()
        {
            int n = InFlight != null ? InFlight() : 0;
            if (n > _lastInFlight && _tapped != null) MarkBusy(_tapped, _tappedRest);
            _tapped = null;
            _lastInFlight = n;
            if (n <= 0 && _busy.Count > 0) ClearBusy();
        }

        private void MarkBusy(Transform t, Vector3 rest)
        {
            if (t == null || IsBusy(t)) return;
            var group = t.GetComponent<CanvasGroup>();
            bool added = group == null;
            if (added) group = t.gameObject.AddComponent<CanvasGroup>();
            _busy.Add(new Busy { Target = t, Group = group, AddedGroup = added, Alpha = group.alpha, Interactable = group.interactable, Rest = rest });
            group.alpha = BusyAlpha;
            group.interactable = false; // a second tap does nothing while the first is on its way
            t.localScale = rest * PressScale;
        }

        private void ClearBusy()
        {
            foreach (var b in _busy)
            {
                if (b.Target == null) continue; // rebuilt away meanwhile
                if (b.Group != null)
                {
                    b.Group.alpha = b.Alpha; b.Group.interactable = b.Interactable; // tells the selectables first
                    if (b.AddedGroup) DestroyComponent(b.Group);
                }
                if (b.Target.localScale == b.Rest * PressScale) b.Target.localScale = b.Rest;
            }
            _busy.Clear();
        }

        private static void DestroyComponent(Component c)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(c);
            else UnityEngine.Object.DestroyImmediate(c);
        }

        private void LateUpdate()
        {
            var es = EventSystem.current;
            if (es != null)
            {
                Vector2 pos = Input.mousePosition;
                if (Input.GetMouseButtonDown(0)) { _downAt = pos; Press(Hit(es, pos)); }
                else if (_pressed != null && Input.GetMouseButton(0) && (pos - _downAt).sqrMagnitude > (float)es.pixelDragThreshold * es.pixelDragThreshold)
                    Release(false);
                if (Input.GetMouseButtonUp(0) && _pressed != null) Release(true); // the tap's command (if any) started this frame
            }
            Settle();
        }

        private Transform Hit(EventSystem es, Vector2 pos)
        {
            _hits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = pos }, _hits);
            return _hits.Count > 0 ? Target(_hits[0].gameObject) : null;
        }
    }
}
