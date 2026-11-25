using UnityEngine;
using UnityEngine.UI;

namespace RogueStars.Client.UiKit
{
    /// <summary>
    /// The enabled / disabled look of a <see cref="Ui.Button"/> (G4). Disabled = a flat, desaturated face without the shine
    /// strip, a dimmed lip and the caption at ~60% opacity. Follows <see cref="Selectable.interactable"/> when it changes later.
    /// </summary>
    public sealed class UiButtonLook : MonoBehaviour
    {
        public const float DisabledTextAlpha = 0.6f;

        private Button _button;
        private Image _face, _lip, _shine;
        private Text _label;
        private Color _faceColor;
        private bool _hasLip;
        private bool _applied;
        private bool _shown;

        public Color FaceColor => _faceColor;

        public void Init(Button button, Image face, Image lip, Image shine, Text label, Color faceColor, bool hasLip)
        {
            _button = button; _face = face; _lip = lip; _shine = shine; _label = label; _faceColor = faceColor; _hasLip = hasLip;
            Apply();
        }

        public void SetFace(Color face)
        {
            _faceColor = face;
            Apply();
        }

        /// <summary>The flat, desaturated face of a disabled button.</summary>
        public static Color DisabledFace(Color face)
        {
            float g = face.r * 0.3f + face.g * 0.59f + face.b * 0.11f;
            var grey = new Color(g, g, g, 1f);
            var c = Color.Lerp(grey, Theme.Locked, 0.6f);
            c.a = face.a < 0.5f ? face.a : 1f;
            return c;
        }

        public void Apply()
        {
            if (_button == null) return;
            bool on = _button.interactable;
            _applied = true; _shown = on;
            var face = on ? _faceColor : DisabledFace(_faceColor);
            if (_face != null) _face.color = face;
            if (_lip != null) _lip.color = _hasLip ? Ui.LipColor(face, on) : new Color(0, 0, 0, 0);
            if (_shine != null) _shine.gameObject.SetActive(on);
            if (_label != null)
            {
                var ink = on ? Theme.TextOn(_faceColor) : Theme.Text;
                if (!on) ink.a = DisabledTextAlpha;
                _label.color = ink;
            }
        }

        private void LateUpdate()
        {
            if (_button != null && (!_applied || _shown != _button.interactable)) Apply();
        }
    }
}
