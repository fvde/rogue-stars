using UnityEngine;

namespace RogueStars.Client.World.Anim
{
    /// <summary>
    /// The global switch of the code animation: with "reduced animation" on (Settings, per device, PlayerPrefs) creatures and
    /// props draw static (their plain sprites) and nothing is updated per frame. Hosts check <see cref="Reduced"/> when they
    /// attach and every frame, so toggling takes effect at once.
    /// </summary>
    public static class AnimSettings
    {
        public const string Key = "rs.reducedAnim";
        private static int _cached = -1;
        private static bool? _override;

        /// <summary>Tests / dev tools force a value (null = the saved setting).</summary>
        public static bool? Override { get => _override; set { _override = value; Version++; Changed?.Invoke(); } }

        public static bool Reduced
        {
            get
            {
                if (_override.HasValue) return _override.Value;
                if (_cached < 0) _cached = PlayerPrefs.GetInt(Key, 0);
                return _cached == 1;
            }
            set
            {
                _cached = value ? 1 : 0;
                PlayerPrefs.SetInt(Key, _cached);
                PlayerPrefs.Save();
                Version++;
                Changed?.Invoke();
            }
        }

        /// <summary>Raised when the setting changes (props re-apply their materials).</summary>
        public static event System.Action Changed;

        /// <summary>Bumped when the setting changes (props re-apply their materials).</summary>
        public static int Version { get; private set; }
    }
}
