using System;
using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// The crash site on the home planet: from the Aliens unlock (level 6) the fireplace, the crashed saucer and the Launch Pod stand on
    /// the planet's top arc next to the worm, the squad (C4 walkers) wanders around them. They are only decoration and a shortcut:
    /// tapping them opens the City screen (camp mode before level 15, the full City after; owner 2026-11-26). The pod's seats live on
    /// that screen, not on the planet.
    /// </summary>
    public sealed partial class HubScreen
    {
        /// <summary>The crash site's shortcut button on the planet (null without a pod).</summary>
        public Button PodButton { get; private set; }

        /// <summary>The crash site shows once the Launch Pod stands (the Aliens unlock places it).</summary>
        public static bool CrashSiteVisible(GameState s) => CitySystem.IsBuilt(CitySystem.LaunchPod(s));

        /// <summary>Fireplace, saucer and pod on the planet's top arc (<paramref name="r"/> = the walkers' arc radius).</summary>
        private void BuildCrashSite(float cy, float r)
        {
            PodButton = null;
            if (!CrashSiteVisible(State)) return;
            float Surface(float x) { float dx = Mathf.Clamp(x - 540, -r, r); return cy - Mathf.Sqrt(Mathf.Max(0, r * r - dx * dx)); }
            void Stand(Image img, float x, float w, float h, float sink, float tilt = 0)
            {
                img.preserveAspect = true;
                At(img.rectTransform, x, Surface(x) - h / 2 + sink, w, h);
                if (tilt != 0) img.rectTransform.localRotation = Quaternion.Euler(0, 0, tilt);
            }
            var site = Ui.Rect(_frame, "CrashSite");
            Ui.Stretch(site);
            var saucer = Ui.Panel(site, Color.white, "CrashSite:Saucer");
            saucer.sprite = CityArt.Saucer(); saucer.raycastTarget = false;
            Stand(saucer, 540 - r * 0.8f, r * 0.62f, r * 0.5f, r * 0.12f, 14);
            var glow = Ui.Panel(site, new Color(1f, 0.62f, 0.25f, 0.45f), "CrashSite:FireGlow");
            glow.sprite = ScreenArt.Glow(); glow.raycastTarget = false;
            At(glow.rectTransform, 540 - r * 0.42f, Surface(540 - r * 0.42f) - r * 0.1f, r * 0.55f, r * 0.4f);
            var fire = Ui.Panel(site, Color.white, "CrashSite:Fire");
            fire.sprite = CityArt.Fire(); fire.raycastTarget = false;
            Stand(fire, 540 - r * 0.42f, r * 0.3f, r * 0.3f, r * 0.05f);
            if (Application.isPlaying) UiPulse.On(glow, 0.08f, 1.6f, 0.5f);

            var pod = CitySystem.LaunchPod(State);
            var def = Content.Blueprint(pod.BlueprintId);
            float px = 540 + r * 0.6f, pw = r * 0.5f;
            // the pod's art is the shortcut button to the City screen (a clear hit area, no chunky face)
            var hit = Ui.Panel(site, new Color(1, 1, 1, 0), "CrashSite:Pod");
            PodButton = hit.gameObject.AddComponent<Button>();
            PodButton.targetGraphic = hit;
            PodButton.transition = Selectable.Transition.None;
            PodButton.onClick.AddListener(OpenCity);
            var art = Ui.Panel(PodButton.transform, Color.white, "PodArt");
            art.sprite = CityArt.Building(def); art.preserveAspect = true; art.raycastTarget = false;
            Ui.Stretch(art.rectTransform);
            At((RectTransform)PodButton.transform, px, Surface(px) - pw / 2 + r * 0.06f, pw, pw);
            // the saucer and the fire open it too: one invisible button over the whole site
            var all = Ui.Panel(site, new Color(1, 1, 1, 0), "CrashSite:Area");
            var ab = all.gameObject.AddComponent<Button>();
            ab.targetGraphic = all; ab.transition = Selectable.Transition.None;
            ab.onClick.AddListener(OpenCity);
            At(all.rectTransform, 540 - r * 0.3f, Surface(540 - r * 0.3f) - r * 0.12f, r * 1.5f, r * 0.55f);
            all.transform.SetSiblingIndex(0);
        }

        /// <summary>The camp (level 6-14) or the City (from 15): the crash-site props and the Camp / City tab open the same screen.</summary>
        public void OpenCity()
        {
            if (CrashSiteVisible(State)) App.Open("city");
        }
    }
}
