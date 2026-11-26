using System.Collections.Generic;
using UnityEngine;

namespace RogueStars.Client.World.Anim
{
    public enum CreatureClip { Idle, Move, Attack, Hit, Death, Celebrate }

    /// <summary>How a creature attacks: rear up and lunge (tall, bendy), slam down (wide, heavy), swoop (flyers).</summary>
    public enum CreatureAttack { Lunge, Slam, Swoop }

    /// <summary>Per-species tuning of the code animation: every number is a multiplier on the clip's default.</summary>
    public sealed class CreatureSpec
    {
        /// <summary>Overall motion amplitude and clip speed.</summary>
        public float Amp = 1, Speed = 1;
        /// <summary>Bend (waves, lean, curl) and squash/stretch share.</summary>
        public float Bend = 1, Squash = 1;
        public CreatureAttack Attack = CreatureAttack.Lunge;
        /// <summary>Flyers hover (a vertical bob) instead of hopping and never squash on landing.</summary>
        public bool Hover;

        public CreatureSpec Clone() => (CreatureSpec)MemberwiseClone();
    }

    /// <summary>
    /// The species table: a small override list by sprite name (art key: <c>alien_blorp</c>, <c>spark_drone</c>, …) over defaults
    /// derived from the alpha bounds' aspect ratio (tall = bendy, wide = squashy and heavy). Unknown species just get the defaults.
    /// </summary>
    public static class CreatureSpecs
    {
        /// <summary>Aspect (height / width) above which a creature counts as tall, below which as wide.</summary>
        public const float TallAspect = 1.25f, WideAspect = 0.85f;

        public static readonly Dictionary<string, CreatureSpec> Overrides = new Dictionary<string, CreatureSpec>
        {
            { "hero_worm", new CreatureSpec { Bend = 1.25f, Squash = 1.1f } },
            { "alien_glimmerfly", new CreatureSpec { Hover = true, Attack = CreatureAttack.Swoop, Speed = 1.2f, Bend = 0.6f, Squash = 0.5f } },
            { "spark_drone", new CreatureSpec { Hover = true, Attack = CreatureAttack.Swoop, Speed = 1.3f, Bend = 0.5f, Squash = 0.4f } },
            { "elite_rift_echo", new CreatureSpec { Hover = true, Attack = CreatureAttack.Swoop, Bend = 0.8f, Squash = 0.5f } },
            { "elite_storm_caller", new CreatureSpec { Hover = true, Attack = CreatureAttack.Swoop, Amp = 0.9f, Bend = 0.8f } },
            { "alien_voidmaw", new CreatureSpec { Attack = CreatureAttack.Slam, Amp = 1.15f } },
            { "alien_clackclaw", new CreatureSpec { Speed = 1.25f, Bend = 0.7f } },
            { "alien_gravitoad", new CreatureSpec { Attack = CreatureAttack.Slam, Squash = 1.3f } },
            { "glacier_golem", new CreatureSpec { Attack = CreatureAttack.Slam, Amp = 0.6f, Speed = 0.7f, Bend = 0.4f } },
            { "elite_lava_brute", new CreatureSpec { Attack = CreatureAttack.Slam, Amp = 0.75f, Speed = 0.8f, Bend = 0.5f } },
            { "ember_imp", new CreatureSpec { Speed = 1.3f, Bend = 0.9f } },
            { "static_eel", new CreatureSpec { Bend = 1.5f, Squash = 0.7f } },
            { "burrow_grub", new CreatureSpec { Bend = 1.4f } },
        };

        /// <summary>The spec of a species: its override, else defaults by aspect ratio (<paramref name="aspect"/> = alpha height / width).</summary>
        public static CreatureSpec For(string key, float aspect)
        {
            if (key != null && Overrides.TryGetValue(key, out var o)) return o;
            if (aspect > TallAspect) return new CreatureSpec { Bend = 1.2f, Squash = 0.85f };
            if (aspect < WideAspect) return new CreatureSpec { Bend = 0.55f, Squash = 1.35f, Attack = CreatureAttack.Slam, Amp = 0.9f };
            return new CreatureSpec();
        }
    }
}
