using System;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>Mark a tutorial step done (sets flag "tut:&lt;Step&gt;", grants step rewards once).</summary>
    [Serializable]
    public sealed class TutorialAdvance : GameCommand { public string Step; }

    /// <summary>Player opened the unlock celebration of a feature: grants its one-time unlock gift. Never forced.</summary>
    [Serializable]
    public sealed class FeatureCelebrate : GameCommand { public string FeatureId; }

    /// <summary>Client reports a lobby popup was shown; fails when the daily popup cap is reached.</summary>
    [Serializable]
    public sealed class LobbyPopupShown : GameCommand { public string PopupId; }
}
