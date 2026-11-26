using UnityEngine;

namespace RogueStars.Client.World.Anim
{
    /// <summary>The animation level the player picks (Settings, per device).</summary>
    public enum AnimLevel { Auto = 0, Full = 1, Reduced = 2 }

    /// <summary>What <see cref="AnimSettings.Resolve"/> looks at (fake values in tests).</summary>
    public readonly struct DeviceSpecs
    {
        public readonly int MemoryMb, Processors, GraphicsMemoryMb;
        public DeviceSpecs(int memoryMb, int processors, int graphicsMemoryMb) { MemoryMb = memoryMb; Processors = processors; GraphicsMemoryMb = graphicsMemoryMb; }
        public static DeviceSpecs Current => new DeviceSpecs(SystemInfo.systemMemorySize, SystemInfo.processorCount, SystemInfo.graphicsMemorySize);
    }

    /// <summary>
    /// The global switch of the code animation: Auto (default: Reduced on a weak device, Full otherwise), Full or Reduced
    /// (Settings, per device, PlayerPrefs). With Reduced creatures and props draw static (their plain sprites) and nothing is
    /// updated per frame. Hosts check <see cref="Reduced"/> when they attach and every frame, so changing it takes effect at once.
    /// </summary>
    public static class AnimSettings
    {
        /// <summary>
        /// Master switch (owner decision 2026-11-26: animation deferred completely). False = <see cref="Reduced"/> is always true:
        /// creatures and props draw as plain static sprites, nothing ticks, hub creature taps are inert, the Settings control is hidden.
        /// The machinery stays in place; tests switch it on (it is a static field, not a const, only so tests can override it).
        /// </summary>
        public static bool Enabled = false;

        public const string Key = "rs.animLevel";
        /// <summary>Below this much RAM (MB; a "4 GB" phone reports ~3.6 GB) the device counts as weak.</summary>
        public const int LowMemoryMb = 3500;
        /// <summary>Fewer cores than this counts as weak.</summary>
        public const int LowProcessors = 4;
        /// <summary>A GPU with less memory than this (when it reports any) counts as weak.</summary>
        public const int LowGraphicsMb = 1000;

        private static int _cached = -1;
        private static bool? _override;
        private static DeviceSpecs? _device;

        /// <summary>Tests / dev tools force a value (null = the saved setting).</summary>
        public static bool? Override { get => _override; set { _override = value; Bump(); } }

        /// <summary>Tests fake the device (null = this device).</summary>
        public static DeviceSpecs? Device { get => _device; set { _device = value; Bump(); } }

        /// <summary>True when the specs are weak: low RAM, very few cores, or a tiny GPU memory.</summary>
        public static bool IsWeak(DeviceSpecs d) =>
            (d.MemoryMb > 0 && d.MemoryMb < LowMemoryMb) || (d.Processors > 0 && d.Processors < LowProcessors) || (d.GraphicsMemoryMb > 0 && d.GraphicsMemoryMb < LowGraphicsMb);

        /// <summary>Auto picks Reduced on a weak device, Full otherwise; Full / Reduced force it.</summary>
        public static bool Resolve(AnimLevel level, DeviceSpecs d) => level == AnimLevel.Reduced || (level == AnimLevel.Auto && IsWeak(d));

        public static AnimLevel Level
        {
            get
            {
                if (_cached < 0) _cached = Mathf.Clamp(PlayerPrefs.GetInt(Key, 0), 0, 2);
                return (AnimLevel)_cached;
            }
            set
            {
                _cached = (int)value;
                PlayerPrefs.SetInt(Key, _cached);
                PlayerPrefs.Save();
                Bump();
            }
        }

        /// <summary>The next level in the Settings cycle: Auto, Full, Reduced.</summary>
        public static AnimLevel Next(AnimLevel l) => (AnimLevel)(((int)l + 1) % 3);

        /// <summary>What the level resolves to on this device right now.</summary>
        public static bool Reduced => !Enabled || (_override ?? Resolve(Level, _device ?? DeviceSpecs.Current));

        /// <summary>"Auto (Full)", "Full", "Reduced".</summary>
        public static string Label(AnimLevel l, bool resolvedReduced) => l == AnimLevel.Auto ? "Auto (" + (resolvedReduced ? "Reduced" : "Full") + ")" : l.ToString();

        /// <summary>Forgets the cached level (tests after deleting the saved key).</summary>
        public static void ResetCache() { _cached = -1; Bump(); }

        private static void Bump() { Version++; Changed?.Invoke(); }

        /// <summary>Raised when the setting changes (props re-apply their materials).</summary>
        public static event System.Action Changed;

        /// <summary>Bumped when the setting changes (props re-apply their materials).</summary>
        public static int Version { get; private set; }
    }
}
