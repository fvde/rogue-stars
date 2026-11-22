using System;
using System.Collections.Generic;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        public OnboardingState Onboarding = new OnboardingState();
    }
}

namespace RogueStars.Rules.Systems
{
    [Serializable]
    public sealed class OnboardingState
    {
        /// <summary>Feature id → unlock time (ms). Unlocks are sticky.</summary>
        public Dictionary<string, long> Unlocked = new Dictionary<string, long>();
        /// <summary>Unlocks whose celebration the player has opened (and whose gift was granted).</summary>
        public HashSet<string> Celebrated = new HashSet<string>();
        /// <summary>Completed tutorial steps (also mirrored as "tut:&lt;id&gt;" flags).</summary>
        public HashSet<string> TutorialDone = new HashSet<string>();
        public long PopupDay = -1;
        public int PopupsShownToday;
    }
}
