using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// The "hurray" banner: a glowing band across its parent with a big chunky word ("MYTHIC!", "LEGENDARY!") and an optional
    /// subtitle (the item's name). Used by the pull reveal (SG1) and meant for every celebration that replaces a toast.
    /// Slides open (x scale 0 → 1) in play mode; outside play mode it appears in its final state.
    /// </summary>
    public static class RevealBanner
    {
        public const string Name = "RevealBanner";

        /// <summary>
        /// Shows the banner in <paramref name="parent"/> (stretched across it, centred at <paramref name="anchorY"/> of its height).
        /// Returns the banner root; its texts are named "BannerText" and "BannerSub".
        /// </summary>
        public static RectTransform Show(Transform parent, string text, Color color, string subtitle = null, float anchorY = 0.82f, float height = 190)
        {
            var root = Ui.Rect(parent, Name);
            root.anchorMin = new Vector2(-0.05f, anchorY); root.anchorMax = new Vector2(1.05f, anchorY);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(0, height);
            root.anchoredPosition = Vector2.zero;
            var le = root.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;

            // soft outer glow and the band: rarity colour fading out to both sides
            var glow = Ui.Panel(root, new Color(color.r, color.g, color.b, 0.35f), "Glow");
            glow.sprite = Band(); glow.raycastTarget = false;
            Ui.Stretch(glow.rectTransform, 0, -26, 0, -26);
            var band = Ui.Panel(root, Color.Lerp(color, Color.white, 0.08f), "Band");
            band.sprite = Band(); band.raycastTarget = false;
            Ui.Stretch(band.rectTransform);
            var edge = Ui.Panel(root, new Color(1f, 0.92f, 0.6f, 0.85f), "Edge");
            edge.sprite = Band(); edge.raycastTarget = false;
            var ert = edge.rectTransform;
            ert.anchorMin = new Vector2(0, 1); ert.anchorMax = new Vector2(1, 1); ert.pivot = new Vector2(0.5f, 1);
            ert.sizeDelta = new Vector2(0, 6); ert.anchoredPosition = Vector2.zero;

            var t = Ui.Display(root, text, 96, Color.white, TextAnchor.MiddleCenter, "BannerText");
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = Theme.Shade(color, 0.35f); o.effectDistance = new Vector2(4, -4);
            var trt = t.rectTransform;
            trt.anchorMin = new Vector2(0, string.IsNullOrEmpty(subtitle) ? 0 : 0.3f); trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            if (!string.IsNullOrEmpty(subtitle))
            {
                var s = Ui.Display(root, subtitle, 34, Theme.Cream, TextAnchor.MiddleCenter, "BannerSub");
                var srt = s.rectTransform;
                srt.anchorMin = Vector2.zero; srt.anchorMax = new Vector2(1, 0.34f);
                srt.offsetMin = srt.offsetMax = Vector2.zero;
            }

            if (Application.isPlaying)
            {
                root.localScale = new Vector3(0, 1, 1);
                UiTweens.Play(root.gameObject, "banner", 0.35f, k =>
                {
                    if (root == null) return;
                    float e = 1f + 2.2f * Mathf.Pow(k - 1f, 3) + 1.2f * Mathf.Pow(k - 1f, 2); // back-out overshoot
                    root.localScale = new Vector3(Mathf.Max(0, e), 1, 1);
                }, () => { if (root != null) root.localScale = Vector3.one; });
            }
            return root;
        }

        private static Sprite _band;

        /// <summary>A horizontal band texture: transparent at both ends, opaque in the middle.</summary>
        private static Sprite Band()
        {
            if (_band != null) return _band;
            const int w = 64;
            var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "rs_band" };
            for (int x = 0; x < w; x++)
            {
                float d = Mathf.Abs(x - (w - 1) / 2f) / ((w - 1) / 2f);
                float a = Mathf.Clamp01((1f - d) / 0.3f);
                tex.SetPixel(x, 0, new Color(1, 1, 1, a));
            }
            tex.Apply();
            _band = Sprite.Create(tex, new Rect(0, 0, w, 1), new Vector2(0.5f, 0.5f), 100);
            _band.name = "rs_band";
            return _band;
        }
    }
}
