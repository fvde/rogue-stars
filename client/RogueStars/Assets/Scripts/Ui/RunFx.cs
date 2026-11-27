using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// Board-game motion for the run screen: tumbling dice, the token's hop (arc + squash/stretch), a smooth follow
    /// camera, the landing pop and floating feedback popups. The maths is pure and tested; the visuals run on
    /// <see cref="UiSequence"/> / <see cref="UiTweens"/>, which complete synchronously outside play mode, and every effect
    /// has an instant path (<see cref="Instant"/>) so EditMode / PlayMode tests stay fast and deterministic.
    /// </summary>
    public static class RunFx
    {
        /// <summary>Skip mode for tests: dice land at once, hops jump, popups appear and settle in the same frame.</summary>
        public static bool Instant;

        /// <summary>True when effects must complete synchronously (skip flag or edit mode).</summary>
        public static bool Skip => Instant || !Application.isPlaying;

        // ---- dice ---------------------------------------------------------------------------------

        /// <summary>
        /// Board timings at speed ×1 (the speed chip divides them: ×2, ×3). Mirrored by the run-time model
        /// (RogueStars.Sim RunClock: DiceSec / AutoDiceSec, HopSec / AutoHopSec) — keep them in step.
        /// </summary>
        public const float DiceRollSeconds = 1.6f, AutoDiceRollSeconds = 0.7f, HopStepSeconds = 0.38f, AutoHopStepSeconds = 0.23f;
        /// <summary>The landing pop of a tile (seconds at ×1).</summary>
        public const float LandPopSeconds = 0.7f;

        /// <summary>Length of the dice roll: ~1.6 s by hand, shorter in auto mode, 0 in skip mode.</summary>
        public static float DiceSeconds(bool auto) => Skip ? 0 : auto ? AutoDiceRollSeconds : DiceRollSeconds;

        /// <summary>Share of the roll the dice tumble before they settle on their value.</summary>
        public const float DiceSettleAt = 0.78f;

        /// <summary>
        /// Face shown by die <paramref name="die"/> at roll time <paramref name="t"/> (0..1): changing faces while it tumbles
        /// (slower and slower), never the same face twice in a row, the rolled value from <see cref="DiceSettleAt"/> on (pure).
        /// </summary>
        public static int DiceFaceAt(float t, int final, int die)
        {
            final = Mathf.Clamp(final, 1, 6);
            if (t >= DiceSettleAt) return final;
            // face changes at a rate that slows down: index = how many changes happened by t
            float u = t / DiceSettleAt;
            int changes = Mathf.FloorToInt(14 * (1 - (1 - u) * (1 - u)));
            int face = 1 + (int)(Hash(changes * 7 + die * 31 + final) % 6);
            // the face right before settling is never the final one (it visibly "lands")
            if (face == final) face = face % 6 + 1;
            return face;
        }

        private static uint Hash(int x)
        {
            uint h = (uint)x * 2654435761u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            return h;
        }

        /// <summary>Pip positions of a die face (-1..1 square, y up; pure).</summary>
        public static List<Vector2> Pips(int value)
        {
            var p = new List<Vector2>();
            const float a = 0.55f;
            switch (Mathf.Clamp(value, 1, 6))
            {
                case 1: p.Add(Vector2.zero); break;
                case 2: p.Add(new Vector2(-a, a)); p.Add(new Vector2(a, -a)); break;
                case 3: p.Add(new Vector2(-a, a)); p.Add(Vector2.zero); p.Add(new Vector2(a, -a)); break;
                case 4: p.Add(new Vector2(-a, a)); p.Add(new Vector2(a, a)); p.Add(new Vector2(-a, -a)); p.Add(new Vector2(a, -a)); break;
                case 5: p.Add(new Vector2(-a, a)); p.Add(new Vector2(a, a)); p.Add(Vector2.zero); p.Add(new Vector2(-a, -a)); p.Add(new Vector2(a, -a)); break;
                default:
                    p.Add(new Vector2(-a, a)); p.Add(new Vector2(a, a)); p.Add(new Vector2(-a, 0)); p.Add(new Vector2(a, 0));
                    p.Add(new Vector2(-a, -a)); p.Add(new Vector2(a, -a));
                    break;
            }
            return p;
        }

        /// <summary>Tumble pose of a die at roll time t: rotation (deg), bounce height and squash (pure).</summary>
        public static void DicePose(float t, int die, out float angle, out float height, out Vector2 scale)
        {
            float dir = die % 2 == 0 ? 1 : -1;
            if (t >= DiceSettleAt)
            {
                // settle: a small overshoot back to straight, then a pop
                float s = (t - DiceSettleAt) / (1 - DiceSettleAt);
                angle = dir * 12 * (1 - s) * Mathf.Cos(s * Mathf.PI * 2);
                height = 0;
                float pop = Mathf.Sin(Mathf.Clamp01(s * 1.6f) * Mathf.PI) * 0.16f;
                scale = new Vector2(1 + pop, 1 + pop);
                return;
            }
            float u = t / DiceSettleAt;
            angle = dir * 720 * (1 - (1 - u) * (1 - u)) + die * 23;
            // three bounces, each lower
            float b = u * 3;
            int n = Mathf.Min(2, Mathf.FloorToInt(b));
            float f = b - n;
            height = Mathf.Sin(f * Mathf.PI) * 90 / (1 + n * 1.6f);
            float impact = f < 0.12f ? 1 - f / 0.12f : 0;
            scale = new Vector2(1 + 0.18f * impact, 1 - 0.18f * impact);
        }

        // ---- token hop ----------------------------------------------------------------------------

        /// <summary>Seconds per tile hop (faster in auto mode).</summary>
        public static float HopSeconds(bool auto) => Skip ? 0 : auto ? AutoHopStepSeconds : HopStepSeconds;

        /// <summary>Height of the hop arc in board units.</summary>
        public const float HopArc = 54;

        /// <summary>Smoothstep easing (slow take-off and landing).</summary>
        public static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }

        /// <summary>
        /// Token pose during one hop at time t (0..1): eased position along the step plus an arc, and a squash/stretch scale
        /// (squash at take-off and landing, stretched in the air; pure).
        /// </summary>
        public static Vector2 HopPosition(Vector2 from, Vector2 to, float t) =>
            Vector2.Lerp(from, to, Ease(t)) + new Vector2(0, HopArc * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI));

        public static Vector2 HopScale(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < 0.15f) { float k = Mathf.Sin(t / 0.15f * Mathf.PI); return new Vector2(1 + 0.16f * k, 1 - 0.16f * k); }       // crouch
            if (t > 0.85f) { float k = Mathf.Sin((t - 0.85f) / 0.15f * Mathf.PI); return new Vector2(1 + 0.2f * k, 1 - 0.2f * k); } // land
            float air = Mathf.Sin((t - 0.15f) / 0.7f * Mathf.PI);
            return new Vector2(1 - 0.1f * air, 1 + 0.12f * air);                                                                 // stretch
        }

        // ---- camera -------------------------------------------------------------------------------

        /// <summary>Camera follow sharpness (1/s): higher = tighter.</summary>
        public const float CameraSharpness = 4f;
        /// <summary>Fastest the camera ever moves (board units per second): a far target is reached smoothly, never snapped.</summary>
        public const float CameraMaxSpeed = 1500f;

        /// <summary>
        /// One frame of the follow camera: exponential ease towards the target (frame-rate independent), its speed capped
        /// at <see cref="CameraMaxSpeed"/>, so the change per frame is at most CameraMaxSpeed·dt (pure).
        /// </summary>
        public static float CameraStep(float current, float target, float dt)
        {
            if (float.IsNaN(current)) return target;
            if (dt <= 0) return current;
            float want = (target - current) * (1 - Mathf.Exp(-CameraSharpness * dt));
            float max = CameraMaxSpeed * dt;
            return current + Mathf.Clamp(want, -max, max);
        }


        // ---- landing / popups ---------------------------------------------------------------------

        /// <summary>Landing pop of a tile: scale over t (overshoot, then settle; pure).</summary>
        public static float LandPopScale(float t)
        {
            t = Mathf.Clamp01(t);
            return 1 + 0.22f * Mathf.Sin(t * Mathf.PI) * (1 - t * 0.6f);
        }

        /// <summary>Seconds a landing popup floats before it is gone.</summary>
        public const float PopupSeconds = 1.25f;

        /// <summary>
        /// A floating text (e.g. "+5% ATK") that rises and fades over <paramref name="parent"/> at <paramref name="pos"/>.
        /// Skip mode: it is placed at its end position and stays (tests read it; the next rebuild clears it).
        /// </summary>
        public static RectTransform FloatText(RectTransform parent, Vector2 pos, string text, Color color, int size = 34, float delay = 0, string name = "Popup")
        {
            var rt = Ui.Rect(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(420, size + 20);
            rt.anchoredPosition = pos;
            var bg = UiShapes.Round(rt.gameObject.AddComponent<Image>(), (size + 20) / 2f);
            bg.color = new Color(0.06f, 0.03f, 0.04f, 0.78f);
            bg.raycastTarget = false;
            var fit = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            fit.padding = new RectOffset(22, 22, 4, 4);
            fit.childAlignment = TextAnchor.MiddleCenter;
            fit.childControlWidth = true; fit.childControlHeight = true;
            fit.childForceExpandWidth = false;
            rt.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var t = Ui.Display(rt, text, size, color, TextAnchor.MiddleCenter, "Text");
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.7f);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            if (Skip) { rt.anchoredPosition = pos + new Vector2(0, 70); return rt; }
            group.alpha = 0;
            float total = PopupSeconds + delay;
            UiTweens.Play(rt.gameObject, "float", total, k =>
            {
                float s = Mathf.Clamp01((k * total - delay) / PopupSeconds);
                if (s <= 0) { group.alpha = 0; return; }
                float rise = 1 - (1 - s) * (1 - s);
                rt.anchoredPosition = pos + new Vector2(0, 110 * rise);
                float pop = s < 0.18f ? Mathf.Lerp(0.6f, 1.15f, s / 0.18f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((s - 0.18f) / 0.2f));
                rt.localScale = Vector3.one * pop;
                group.alpha = s < 0.7f ? 1 : 1 - (s - 0.7f) / 0.3f;
            }, () => { if (rt != null) UnityEngine.Object.Destroy(rt.gameObject); });
            return rt;
        }

        /// <summary>How long a toast stays readable (then it fades in ToastFadeSeconds).</summary>
        public const float ToastSeconds = 1.9f, ToastFadeSeconds = 0.35f;

        /// <summary>
        /// A one-line toast (top-left anchored <paramref name="pos"/> = its centre): a dark pill with the text that pops in,
        /// holds for <see cref="ToastSeconds"/> and fades. In skip mode it stays (tests and screenshots read it).
        /// </summary>
        public static RectTransform Toast(RectTransform parent, Vector2 pos, string text, float maxWidth = 900)
        {
            var rt = Ui.Rect(parent, "Toast");
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            var bg = UiShapes.Round(rt.gameObject.AddComponent<Image>(), 44);
            bg.color = new Color(0.07f, 0.05f, 0.12f, 0.9f);
            bg.raycastTarget = false;
            UiShapes.Outline(rt, new Color(1, 1, 1, 0.55f), 44, 4);
            var fit = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            fit.padding = new RectOffset(40, 40, 18, 18);
            fit.childAlignment = TextAnchor.MiddleCenter;
            fit.childControlWidth = true; fit.childControlHeight = true;
            fit.childForceExpandWidth = false;
            var csf = rt.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var t = Ui.Display(rt, text, 40, Color.white, TextAnchor.MiddleCenter, "Text");
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = Mathf.Min(maxWidth - 80, Mathf.Max(200, t.preferredWidth));
            t.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.6f);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            if (Skip) return rt;
            float total = ToastSeconds + ToastFadeSeconds;
            UiTweens.Play(rt.gameObject, "toast", total, k =>
            {
                float at = k * total;
                rt.localScale = Vector3.one * (at < 0.15f ? Mathf.Lerp(0.85f, 1f, at / 0.15f) : 1f);
                group.alpha = at < 0.15f ? at / 0.15f : at < ToastSeconds ? 1 : 1 - (at - ToastSeconds) / ToastFadeSeconds;
            }, () => { if (rt != null) UnityEngine.Object.Destroy(rt.gameObject); });
            return rt;
        }

        /// <summary>
        /// Particles from <paramref name="from"/>: coins flying out and arcing towards <paramref name="to"/> (supply), or
        /// sparks rising (heal, buff). Cosmetic only; nothing is created in skip mode.
        /// </summary>
        public static void Burst(RectTransform parent, Vector2 from, Vector2? to, Color color, int count, float size = 22, float delay = 0)
        {
            if (Skip || parent == null) return;
            for (int i = 0; i < count; i++)
            {
                var dot = Ui.Dot(parent, color, size, "Particle");
                var rt = dot.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = from;
                var le = dot.GetComponent<LayoutElement>(); if (le != null) le.ignoreLayout = true;
                float ang = (i / (float)Mathf.Max(1, count)) * Mathf.PI * 2 + 0.4f;
                var spread = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (50 + 18 * (i % 3));
                float d = delay + i * 0.035f;
                float dur = 0.75f + d;
                var g = dot.gameObject.AddComponent<CanvasGroup>();
                g.blocksRaycasts = false;
                g.alpha = 0;
                UiTweens.Play(dot.gameObject, "burst", dur, k =>
                {
                    float s = Mathf.Clamp01((k * dur - d) / 0.75f);
                    if (s <= 0) return;
                    g.alpha = s < 0.8f ? 1 : 1 - (s - 0.8f) / 0.2f;
                    var out1 = from + spread * Mathf.Sin(Mathf.Min(1, s * 2.2f) * Mathf.PI * 0.5f);
                    if (to.HasValue)
                    {
                        float f = Mathf.Clamp01((s - 0.3f) / 0.7f);
                        rt.anchoredPosition = Vector2.Lerp(out1, to.Value, f * f) + new Vector2(0, 60 * Mathf.Sin(f * Mathf.PI));
                    }
                    else rt.anchoredPosition = out1 + new Vector2(0, 120 * s);
                    rt.localScale = Vector3.one * (1 - 0.4f * s);
                }, () => { if (rt != null) UnityEngine.Object.Destroy(rt.gameObject); });
            }
        }

        /// <summary>An expanding ring (landing shockwave) in <paramref name="color"/> around <paramref name="pos"/>.</summary>
        public static RectTransform Ring(RectTransform parent, Vector2 pos, Vector2 size, Color color, float delay = 0)
        {
            if (Skip || parent == null) return null;
            var rt = Ui.Rect(parent, "LandRing");
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = UiShapes.Outline(rt, color, Mathf.Min(size.x, size.y) / 2.2f, 8, "Ring");
            var g = rt.gameObject.AddComponent<CanvasGroup>();
            g.blocksRaycasts = false;
            g.alpha = 0;
            float dur = 0.55f + delay;
            UiTweens.Play(rt.gameObject, "ring", dur, k =>
            {
                float s = Mathf.Clamp01((k * dur - delay) / 0.55f);
                if (s <= 0) return;
                rt.localScale = Vector3.one * (1 + 0.8f * (1 - (1 - s) * (1 - s)));
                g.alpha = 1 - s;
            }, () => { if (rt != null) UnityEngine.Object.Destroy(rt.gameObject); });
            return rt;
        }
    }

    /// <summary>One or more dice drawn in code: a cream rounded face with dark pips; <see cref="Set"/> changes the face.</summary>
    public sealed class DiceView
    {
        public readonly RectTransform Root;
        public readonly List<RectTransform> Dice = new List<RectTransform>();
        private readonly List<int> _faces = new List<int>();
        private readonly float _size;
        private readonly Color? _pipColor;

        /// <summary>
        /// <paramref name="faceColor"/> / <paramref name="pipColor"/>: a printed look (the dice on the roll button), else
        /// cream dice with dark pips.
        /// </summary>
        public DiceView(RectTransform parent, int count, float size, string name = "DiceRoll", Color? faceColor = null, Color? pipColor = null)
        {
            _size = size;
            _pipColor = pipColor;
            Root = Ui.Rect(parent, name);
            var h = Root.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = size * 0.3f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = false; h.childControlHeight = false;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            for (int i = 0; i < Mathf.Max(1, count); i++)
            {
                var holder = Ui.Rect(Root, "Die" + i);
                holder.sizeDelta = new Vector2(size, size);
                var face = Ui.RoundPanel(holder, faceColor ?? Theme.Cream, size * 0.2f, "Face");
                face.raycastTarget = false;
                Ui.Stretch(face.rectTransform);
                UiShapes.Outline(face.rectTransform, faceColor.HasValue ? new Color(0.55f, 0.12f, 0.1f, 0.55f) : Theme.Shade(Theme.Cream, 0.62f), size * 0.2f, Mathf.Max(3, size * 0.05f));
                Dice.Add(face.rectTransform);
                _faces.Add(0);
                Set(i, 1);
            }
        }

        /// <summary>The face value die <paramref name="i"/> shows.</summary>
        public int Face(int i) => i >= 0 && i < _faces.Count ? _faces[i] : 0;

        public void Set(int i, int value)
        {
            if (i < 0 || i >= Dice.Count || _faces[i] == value) return;
            _faces[i] = value;
            var face = Dice[i];
            for (int c = face.childCount - 1; c >= 0; c--)
                if (face.GetChild(c).name == "Pip") UnityEngine.Object.DestroyImmediate(face.GetChild(c).gameObject);
            float pip = _size * 0.19f;
            foreach (var p in RunFx.Pips(value))
            {
                var dot = Ui.Dot(face, _pipColor ?? (value == 1 ? Theme.Bad : Theme.OnAccent), pip, "Pip");
                var rt = dot.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(pip, pip);
                rt.anchoredPosition = p * (_size * 0.3f);
            }
        }

        /// <summary>Pose of the whole roll at time t (faces, tumble, bounce) for the rolled values.</summary>
        public void Pose(float t, IList<int> values)
        {
            for (int i = 0; i < Dice.Count; i++)
            {
                int v = i < values.Count ? values[i] : 1;
                Set(i, RunFx.DiceFaceAt(t, v, i));
                RunFx.DicePose(t, i, out float angle, out float height, out Vector2 scale);
                var holder = (RectTransform)Dice[i].parent;
                Dice[i].localRotation = Quaternion.Euler(0, 0, angle);
                Dice[i].offsetMin = new Vector2(0, height); Dice[i].offsetMax = new Vector2(0, height);
                Dice[i].localScale = new Vector3(scale.x, scale.y, 1);
                holder.localScale = Vector3.one;
            }
        }
    }

    /// <summary>
    /// What the board's gesture surfaces share (the board view and the tap-target overlay over it): the pointers down and
    /// the view callback. <see cref="OnMove"/>(from, to, zoom): the world point under holder point <c>from</c> goes under
    /// <c>to</c> (px from the holder's top-left), the view <c>zoom</c> times closer.
    /// </summary>
    public sealed class BoardGestureState
    {
        public RectTransform Space;
        public Action<Vector2, Vector2, float> OnMove;
        public readonly Dictionary<int, Vector2> Pointers = new Dictionary<int, Vector2>();
        /// <summary>Wheel zoom per notch.</summary>
        public const float WheelStep = 1.12f;
        /// <summary>Fingers closer than this (px) do not zoom (noise).</summary>
        public const float MinPinch = 24f;

        /// <summary>A screen point in the space's px from its top-left (y down).</summary>
        public Vector2 Local(Vector2 screen, Camera cam)
        {
            if (Space == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(Space, screen, cam, out var local)) return screen;
            var r = Space.rect;
            return new Vector2(local.x - r.xMin, r.yMax - local.y);
        }

        /// <summary>
        /// A pointer moved from <paramref name="prev"/> to <paramref name="cur"/>: one finger pans; with a second finger
        /// down it is a pinch (zoom about the fingers' midpoint, which also pans).
        /// </summary>
        public void Moved(int id, Vector2 prev, Vector2 cur)
        {
            Pointers[id] = cur;
            Vector2? other = null;
            foreach (var kv in Pointers) if (kv.Key != id) { other = kv.Value; break; }
            if (other.HasValue)
            {
                var o = other.Value;
                float d0 = Vector2.Distance(prev, o), d1 = Vector2.Distance(cur, o);
                float zoom = d0 > MinPinch && d1 > MinPinch ? d1 / d0 : 1;
                OnMove?.Invoke((prev + o) / 2, (cur + o) / 2, zoom);
            }
            else OnMove?.Invoke(prev, cur, 1);
        }
    }

    /// <summary>
    /// The board's gestures (on the board view and on the tap-target overlay, sharing one <see cref="BoardGestureState"/>):
    /// one finger (or the mouse) drags the view, two fingers pinch-zoom, the mouse wheel zooms at the cursor. A drag that
    /// starts on a fork tap target pans and cancels its click (the event system ends the press when a drag begins), a tap
    /// still picks.
    /// </summary>
    public sealed class BoardGestures : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        public BoardGestureState State;

        public void OnPointerDown(PointerEventData e) { if (State != null) State.Pointers[e.pointerId] = State.Local(e.position, e.pressEventCamera); }
        public void OnPointerUp(PointerEventData e) { State?.Pointers.Remove(e.pointerId); }

        public void OnBeginDrag(PointerEventData e)
        {
            if (State == null) return;
            if (!State.Pointers.ContainsKey(e.pointerId)) State.Pointers[e.pointerId] = State.Local(e.position - e.delta, e.pressEventCamera);
        }

        public void OnDrag(PointerEventData e)
        {
            if (State == null) return;
            var cur = State.Local(e.position, e.pressEventCamera);
            var prev = State.Pointers.TryGetValue(e.pointerId, out var p) ? p : State.Local(e.position - e.delta, e.pressEventCamera);
            State.Moved(e.pointerId, prev, cur);
        }

        public void OnEndDrag(PointerEventData e) { State?.Pointers.Remove(e.pointerId); }

        public void OnScroll(PointerEventData e)
        {
            if (State == null || Mathf.Abs(e.scrollDelta.y) < 0.01f) return;
            var at = State.Local(e.position, e.enterEventCamera);
            State.OnMove?.Invoke(at, at, Mathf.Pow(BoardGestureState.WheelStep, e.scrollDelta.y));
        }

        private void OnDisable() { State?.Pointers.Clear(); }
    }
}
