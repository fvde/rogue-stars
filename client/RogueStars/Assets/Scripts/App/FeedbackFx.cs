using System;
using RogueStars.Client.UiKit;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.App
{
    /// <summary>
    /// The shared feedback animations that replace toasts (G1): fly-to-target (rewards into the currency bar, items into
    /// slots, coins to a counter), the star-fill pop, a target pop and the button shake. Tweens run on
    /// <see cref="UiTweens"/> (synchronous outside play mode, so EditMode tests stay deterministic).
    /// </summary>
    public static class FeedbackFx
    {
        public const float FlyDuration = 0.55f;
        public const int MaxFlyIcons = 6;

        /// <summary>Flights started since load (tests).</summary>
        public static int FlightsStarted { get; private set; }
        /// <summary>The last flight's target name (tests).</summary>
        public static string LastFlightTarget { get; private set; }

        /// <summary>World centre of a rect.</summary>
        public static Vector3 Centre(RectTransform rt)
        {
            if (rt == null) return Vector3.zero;
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return (c[0] + c[2]) * 0.5f;
        }

        /// <summary>
        /// <paramref name="count"/> copies of <paramref name="icon"/> fly on an arc from <paramref name="fromWorld"/> to
        /// <paramref name="target"/> (staggered), then the target pops and <paramref name="arrived"/> runs once.
        /// </summary>
        public static void FlyTo(RectTransform layer, Sprite icon, Vector3 fromWorld, RectTransform target, int count = 4, float size = 64, Action arrived = null)
        {
            if (layer == null || target == null) { arrived?.Invoke(); return; }
            FlightsStarted++;
            LastFlightTarget = target.name;
            count = Mathf.Clamp(count, 1, MaxFlyIcons);
            Vector3 to = Centre(target);
            int landed = 0;
            for (int i = 0; i < count; i++)
            {
                var img = Ui.Panel(layer, Color.white, "FlyIcon");
                img.raycastTarget = false;
                if (icon != null) { img.sprite = icon; img.preserveAspect = true; }
                else UiShapes.Round(img, size / 2);
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(size, size);
                // a small spread at the start so the icons read as a burst
                float ang = i * 2.39996f;
                var spread = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0) * (i == 0 ? 0 : 40f * layer.lossyScale.x);
                Vector3 from = fromWorld + spread;
                rt.position = from;
                float delay = i * 0.06f;
                var go = img.gameObject;
                Vector3 ctrl = (from + to) * 0.5f + new Vector3(0, Vector3.Distance(from, to) * 0.35f, 0);
                UiTweens.Play(go, "fly", FlyDuration + delay, t =>
                {
                    if (rt == null) return;
                    float u = Mathf.Clamp01((t * (FlyDuration + delay) - delay) / FlyDuration);
                    float e = u * u * (3 - 2 * u);
                    Vector3 a = Vector3.Lerp(from, ctrl, e), b = Vector3.Lerp(ctrl, to, e);
                    rt.position = Vector3.Lerp(a, b, e);
                    rt.localScale = Vector3.one * Mathf.Lerp(1.15f, 0.6f, e);
                }, () =>
                {
                    Ui.DestroyGo(go);
                    landed++;
                    if (landed == count) { Pop(target); arrived?.Invoke(); }
                });
            }
        }

        /// <summary>A quick scale pop (a number ticking up, a slot receiving an item).</summary>
        public static void Pop(RectTransform target, float amount = 0.18f, float duration = 0.3f)
        {
            if (target == null) return;
            UiTweens.Play(target.gameObject, "pop", duration, t =>
            {
                if (target == null) return;
                target.localScale = Vector3.one * (1 + amount * Mathf.Sin(t * Mathf.PI));
            }, () => { if (target != null) target.localScale = Vector3.one; });
        }

        /// <summary>
        /// The star-fill pop (T12/T17/T23): the new star grows in from zero with a gold flash ring. Leaves the star filled.
        /// </summary>
        public static void StarPop(RectTransform star, Color? color = null)
        {
            if (star == null) return;
            var gold = color ?? Theme.Gold;
            var ring = Ui.Panel(star, new Color(gold.r, gold.g, gold.b, 0.8f), "StarFlash");
            ring.raycastTarget = false;
            UiShapes.Round(ring, 40);
            Ui.Stretch(ring.rectTransform, -16, -16, -16, -16);
            var rg = ring.gameObject;
            UiTweens.Play(star.gameObject, "star", 0.45f, t =>
            {
                if (star == null) return;
                float s = t < 0.6f ? Mathf.Lerp(0, 1.35f, t / 0.6f) : Mathf.Lerp(1.35f, 1f, (t - 0.6f) / 0.4f);
                star.localScale = Vector3.one * s;
                if (ring != null) { ring.color = new Color(gold.r, gold.g, gold.b, 0.8f * (1 - t)); ring.rectTransform.localScale = Vector3.one * (1 + t); }
            }, () => { if (star != null) star.localScale = Vector3.one; Ui.DestroyGo(rg); });
        }

        /// <summary>A short horizontal shake (a tapped button whose command failed, T57).</summary>
        public static void Shake(RectTransform target, float amplitude = 14f, float duration = 0.35f)
        {
            if (target == null) return;
            var start = target.anchoredPosition;
            UiTweens.Play(target.gameObject, "shake", duration, t =>
            {
                if (target == null) return;
                target.anchoredPosition = start + new Vector2(Mathf.Sin(t * Mathf.PI * 6) * amplitude * (1 - t), 0);
            }, () => { if (target != null) target.anchoredPosition = start; });
        }

        /// <summary>A coloured flash over a target that fades out (merge flash T27, talent row T31, building T06/T07).</summary>
        public static void Flash(RectTransform target, Color color, float duration = 0.5f)
        {
            if (target == null) return;
            var flash = Ui.Panel(target, new Color(color.r, color.g, color.b, 0.7f), "Flash");
            flash.raycastTarget = false;
            UiShapes.Round(flash, 24);
            Ui.Stretch(flash.rectTransform, -6, -6, -6, -6);
            var go = flash.gameObject;
            UiTweens.Play(go, "flash", duration, t =>
            {
                if (flash == null) return;
                flash.color = new Color(color.r, color.g, color.b, 0.7f * (1 - t));
            }, () => Ui.DestroyGo(go));
            Pop(target, 0.12f);
        }

        /// <summary>A glow sweep across a pill (stamina refilled, T03).</summary>
        public static void GlowSweep(RectTransform pill, Color color)
        {
            if (pill == null) return;
            var glow = Ui.Panel(pill, new Color(color.r, color.g, color.b, 0.55f), "GlowSweep");
            glow.raycastTarget = false;
            var rt = glow.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 0.5f);
            var go = glow.gameObject;
            UiTweens.Play(go, "sweep", 0.6f, t =>
            {
                if (rt == null) return;
                rt.anchorMax = new Vector2(Mathf.Clamp01(t * 1.2f), 1);
                glow.color = new Color(color.r, color.g, color.b, 0.55f * (1 - t * t));
            }, () => Ui.DestroyGo(go));
            Pop(pill, 0.1f);
        }
    }
}
