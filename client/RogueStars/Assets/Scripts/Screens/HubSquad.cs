using System.Collections.Generic;
using RogueStars.Client.World.Anim;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// C4 (playtest): the squad (the Launch Pod's aliens) wanders around the worm on the home planet with the City's walker and
    /// social-event system (<see cref="WalkerSim"/>: hearts, high fives, scuffles, gossip, dances). Walkers move along the planet's
    /// top arc in reference px, inside the screen's safe area; the sim lives on the hub screen (it survives rebuilds), the view is
    /// rebuilt with it and pauses while the hub is hidden.
    /// </summary>
    public sealed class HubSquad : MonoBehaviour
    {
        public const float PairDistance = 80, AlienSize = 0.16f;

        public WalkerSim Sim;
        /// <summary>The planet: centre (ref px from the top-left) and radius; the worm's x.</summary>
        public Vector2 Centre;
        public float Radius, Scale = 1;
        private readonly List<KeyValuePair<Walker, RectTransform>> _views = new List<KeyValuePair<Walker, RectTransform>>();
        private readonly Dictionary<Walker, UiCreature> _anims = new Dictionary<Walker, UiCreature>();
        private RectTransform _layer;
        /// <summary>The tap target of each walker (tap: celebrate + hearts).</summary>
        public readonly Dictionary<Walker, HubCreatureTap> Taps = new Dictionary<Walker, HubCreatureTap>();

        /// <summary>Walkers shown (tests).</summary>
        public int Count => _views.Count;
        public int BubblesShown { get; private set; }

        /// <summary>
        /// Builds the squad's walkers on <paramref name="layer"/> (a rect anchored like the hub frame) around the worm. Returns null
        /// when the squad is empty.
        /// </summary>
        public static HubSquad Build(RectTransform layer, WalkerSim sim, IList<string> squad, ContentDb content, Vector2 centre, float radius, float scale)
        {
            if (squad == null || squad.Count == 0) return null;
            var host = Ui.Rect(layer, "HubSquad");
            Ui.Stretch(host);
            var hs = host.gameObject.AddComponent<HubSquad>();
            hs.Sim = sim;
            hs.Centre = centre;
            hs.Radius = radius;
            hs.Scale = scale;
            hs._layer = host;
            sim.PairDistance = PairDistance;
            // keep the walkers of the current squad (positions survive rebuilds), add the new ones, drop the leavers
            var keep = new HashSet<string>(squad);
            for (int i = sim.Walkers.Count - 1; i >= 0; i--) if (!keep.Contains(sim.Walkers[i].AlienId)) sim.Walkers.RemoveAt(i);
            float span = radius * 0.72f;
            var area = new Rect(Mathf.Max(70, centre.x - span), 0, Mathf.Min(1010, centre.x + span) - Mathf.Max(70, centre.x - span), 1);
            for (int i = 0; i < squad.Count; i++)
            {
                var w = sim.Find(squad[i]);
                if (w == null)
                {
                    w = sim.Add(new Walker { AlienId = squad[i], Mode = WalkerMode.Wander, Speed = 38 + 10 * sim.Random01(), Area = area });
                    w.Pos = new Vector2(Mathf.Lerp(area.xMin, area.xMax, (i + 0.5f) / squad.Count), 0);
                    w.Avoid = new Vector2(centre.x, 0);
                    w.AvoidRadius = radius * 0.2f;
                    sim.NextTarget(w);
                }
                w.Area = area;
                var def = content.AlienType(squad[i]);
                var img = SummonKit.Img(host, ArtCatalog.Alien(squad[i]) ?? HudArt.AlienCritter(def != null ? Theme.FamilyColor(def.Family) : Theme.Good), "Squad:" + squad[i]);
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0.5f, 0);
                float size = radius * 2 * AlienSize * scale;
                rt.sizeDelta = new Vector2(size, size);
                hs._views.Add(new KeyValuePair<Walker, RectTransform>(w, rt));
                var anim = UiCreature.Attach(img);
                if (anim != null) { hs._anims[w] = anim; hs.Taps[w] = HubCreatureTap.Add(img, anim, layer, scale, 0.7f, 0.85f); }
            }
            hs.Place();
            return hs;
        }

        private void OnEnable() { if (Sim != null) Sim.Social += OnSocial; }
        private void OnDisable() { if (Sim != null) Sim.Social -= OnSocial; }

        private void Start() { if (Sim != null && isActiveAndEnabled) { Sim.Social -= OnSocial; Sim.Social += OnSocial; } }

        private void Update()
        {
            if (Sim == null) return;
            Sim.Step(Mathf.Min(0.1f, Time.unscaledDeltaTime));
            Place();
        }

        /// <summary>The planet's surface height at x (ref px from the top), a little inside the rim.</summary>
        public float SurfaceY(float x)
        {
            float dx = Mathf.Clamp(x - Centre.x, -Radius, Radius);
            return Centre.y - Mathf.Sqrt(Mathf.Max(0, Radius * Radius - dx * dx));
        }

        private void Place()
        {
            foreach (var kv in _views)
            {
                var w = kv.Key;
                var rt = kv.Value;
                if (rt == null) continue;
                float x = Mathf.Clamp(w.Pos.x, 60, 1020);
                float y = SurfaceY(x) - w.Hop * 26;
                float bob = Application.isPlaying && w.State == WalkerState.Walking ? Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6 + w.Phase)) * 4 : 0;
                rt.anchoredPosition = new Vector2(x * Scale, -(y - bob) * Scale);
                rt.localScale = new Vector3(w.FacingLeft ? -1 : 1, 1, 1);
                if (_anims.TryGetValue(w, out var anim)) anim.Loop(w.State == WalkerState.Walking ? CreatureClip.Move : CreatureClip.Idle);
            }
        }

        private void OnSocial(Walker a, Walker b, int kind)
        {
            if (_layer == null) return;
            BubblesShown++;
            float x = (a.Pos.x + b.Pos.x) / 2;
            var img = SummonKit.Img(_layer, CityArt.SocialBubble(WalkerSim.SocialKinds[kind]), "SocialBubble");
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0);
            float size = 86 * Scale;
            rt.sizeDelta = new Vector2(size, size);
            float baseY = SurfaceY(x) - Radius * 2 * AlienSize - 6;
            rt.anchoredPosition = new Vector2(x * Scale, -baseY * Scale);
            var go = img.gameObject;
            UiTweens.Play(go, "bubble", Sim.StopTime, k =>
            {
                if (rt == null) return;
                rt.localScale = Vector3.one * (k < 0.15f ? Mathf.Lerp(0.3f, 1.1f, k / 0.15f) : 1);
                rt.anchoredPosition = new Vector2(x * Scale, -(baseY - 30 * k) * Scale);
                img.color = new Color(1, 1, 1, k > 0.7f ? 1 - (k - 0.7f) / 0.3f : 1);
            }, () => Ui.DestroyGo(go));
        }
    }
}
