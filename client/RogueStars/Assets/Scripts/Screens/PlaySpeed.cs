using System;
using RogueStars.Client.UiKit;
using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.Screens
{
    /// <summary>
    /// The play speed options shared by the battle playback and the run board: ONE button shows the current speed and a tap
    /// moves to the next one, ×0.5 → ×1 → ×2 → ×3 → ×0.5 (×3 only when allowed: the monthly pass in battle).
    /// </summary>
    public static class PlaySpeed
    {
        public static readonly float[] Options = { 0.5f, 1f, 2f, 3f };
        public const float Slowest = 0.5f, Default = 1f, Fastest = 3f;

        /// <summary>The speed after <paramref name="current"/>; ×3 is skipped while <paramref name="allowFastest"/> is false.</summary>
        public static float Next(float current, bool allowFastest)
        {
            int i = Array.FindIndex(Options, o => Mathf.Approximately(o, current));
            if (i < 0) i = Array.IndexOf(Options, Default);
            for (int step = 1; step <= Options.Length; step++)
            {
                float candidate = Options[(i + step) % Options.Length];
                if (candidate >= Fastest && !allowFastest) continue;
                return candidate;
            }
            return Default;
        }

        /// <summary>A stored speed made valid: one of the options, ×3 only when allowed.</summary>
        public static float Valid(float speed, bool allowFastest)
        {
            if (Array.FindIndex(Options, o => Mathf.Approximately(o, speed)) < 0) return Default;
            return speed >= Fastest && !allowFastest ? 2f : speed;
        }

        /// <summary>"x0.5", "x1", "x2", "x3".</summary>
        public static string Label(float speed) => speed < 1f ? "x" + speed.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "x" + Mathf.RoundToInt(speed);

        /// <summary>
        /// The speed button (name <c>SpeedChip</c>): the &gt;&gt; icon and the current speed (<c>SpeedText</c>), one tap = the next
        /// speed. Placed on the 1080-wide reference grid from the bottom (same look on the board and in battle).
        /// </summary>
        public static Text BuildChip(RectTransform parent, float s, float x, float y, float w, float h, Action onTap, float current)
        {
            var chip = Ui.Button(parent, "", () => onTap(), new Color32(0x3A, 0x2E, 0x58, 245), w * s, h * s, true, 20);
            chip.name = "SpeedChip";
            SliceKit.Place((RectTransform)chip.transform, s, x, y, w, h, true);
            UiShapes.Outline((RectTransform)chip.transform, SliceKit.PillEdge, 28 * s, 5);
            var icon = Ui.Panel(chip.transform, Color.white, "SpeedIcon");
            icon.sprite = World.HudArt.Icon("speed");
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.27f, 0.5f);
            icon.rectTransform.sizeDelta = new Vector2(60 * s, 60 * s);
            icon.rectTransform.anchoredPosition = Vector2.zero;
            var text = SliceKit.Chunky(chip.transform, Label(current), (int)(46 * s), Color.white, TextAnchor.MiddleCenter, "SpeedText");
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.68f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(120 * s, 60 * s);
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 20; text.resizeTextMaxSize = (int)(46 * s);
            return text;
        }
    }
}
