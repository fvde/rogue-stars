using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Client.World.Anim;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// Tapping the hub's worm or a wandering squad alien: it plays its celebrate clip (wiggle + hops) with a small heart / sparkle
    /// burst. A short cooldown (<see cref="Cooldown"/>) keeps it from being spammed; no sound. The tap target is a transparent
    /// button over the creature's body only (narrower than its sprite), so it never covers the crash-site props / pod or the
    /// hub's other buttons.
    /// </summary>
    public sealed class HubCreatureTap : MonoBehaviour
    {
        public const float Cooldown = 1.5f;

        public UiCreature Creature;
        /// <summary>Where the burst parents (the hub frame) and the scale of its reference px.</summary>
        public RectTransform Layer;
        public float Scale = 1;
        /// <summary>Taps that played (tests).</summary>
        public int Plays { get; private set; }
        private float _next = float.NegativeInfinity;

        /// <summary>Adds a transparent tap button as a child of <paramref name="host"/> (the creature's image) covering its middle <paramref name="widthFraction"/>.</summary>
        public static HubCreatureTap Add(Image host, UiCreature creature, RectTransform layer, float scale, float widthFraction = 0.6f, float heightFraction = 0.8f)
        {
            var hit = Ui.Panel(host.rectTransform, new Color(1, 1, 1, 0), "TapTarget");
            var rt = hit.rectTransform;
            rt.anchorMin = new Vector2(0.5f - widthFraction / 2, 0);
            rt.anchorMax = new Vector2(0.5f + widthFraction / 2, heightFraction);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var tap = hit.gameObject.AddComponent<HubCreatureTap>();
            tap.Creature = creature; tap.Layer = layer; tap.Scale = scale;
            var b = hit.gameObject.AddComponent<Button>();
            b.targetGraphic = hit; b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => tap.Tap(Time.unscaledTime));
            return tap;
        }

        /// <summary>A tap at time <paramref name="now"/>: true when it played (not within the cooldown of the last one).</summary>
        public bool Tap(float now)
        {
            if (now < _next) return false;
            _next = now + Cooldown;
            Plays++;
            if (Creature != null) Creature.Play(CreatureClip.Celebrate);
            Hearts();
            return true;
        }

        private void Hearts()
        {
            if (Layer == null || !Application.isPlaying) return;
            // the creature's top centre, in the layer's top-left px
            var corners = new Vector3[4];
            ((RectTransform)transform.parent).GetWorldCorners(corners);
            var top = (corners[1] + corners[2]) / 2;
            var local = (Vector2)Layer.InverseTransformPoint(top);
            var origin = new Vector2(local.x - Layer.rect.xMin, local.y - Layer.rect.yMax); // anchored (0,1)
            for (int i = 0; i < 3; i++)
            {
                var img = SummonKit.Img(Layer, CityArt.SocialBubble("heart"), "TapHeart");
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0.5f, 0);
                float size = 120 * Scale;
                rt.sizeDelta = new Vector2(size, size);
                float dx = (i - 1) * 55 * Scale, delay = i * 0.12f, dur = 1.0f + delay;
                var go = img.gameObject;
                img.color = new Color(1, 1, 1, 0);
                UiTweens.Play(go, "tapheart", dur, k =>
                {
                    float s = Mathf.Clamp01((k * dur - delay) / 1.0f);
                    if (rt == null) return;
                    rt.anchoredPosition = origin + new Vector2(dx * s, 110 * Scale * s);
                    rt.localScale = Vector3.one * (s < 0.15f ? Mathf.Lerp(0.3f, 1.1f, s / 0.15f) : 1);
                    img.color = new Color(1, 1, 1, s <= 0 ? 0 : s > 0.7f ? 1 - (s - 0.7f) / 0.3f : 1);
                }, () => Ui.DestroyGo(go));
            }
            RunFx.Burst(Layer, origin, null, new Color(1f, 0.85f, 0.35f), 6, 16 * Scale);
        }
    }
}
