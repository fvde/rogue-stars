using UnityEngine;

namespace RogueStars.Client.UiKit
{
    /// <summary>A steady spin (pending spinners, G1 T51). Play mode only.</summary>
    public sealed class UiSpin : MonoBehaviour
    {
        public float DegreesPerSecond = -360f;

        private void Update() => transform.Rotate(0, 0, DegreesPerSecond * Time.unscaledDeltaTime);
    }
}
