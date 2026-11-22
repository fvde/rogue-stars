using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    [Serializable]
    public sealed class MailRead : GameCommand { public string MailId; }

    [Serializable]
    public sealed class MailClaim : GameCommand { public string MailId; }

    /// <summary>Claim every unexpired mail with unclaimed rewards.</summary>
    [Serializable]
    public sealed class MailClaimAll : GameCommand { }

    /// <summary>Delete a mail (only without unclaimed rewards).</summary>
    [Serializable]
    public sealed class MailDelete : GameCommand { public string MailId; }

    /// <summary>Server/admin pushes a mail (compensation, guild gift, event rewards). Use TemplateId or Title/Body/Rewards.</summary>
    [Serializable, ServerOnly]
    public sealed class MailPush : GameCommand
    {
        public string TemplateId;
        public string Title;
        public string Body;
        public List<Reward> Rewards = new List<Reward>();
        /// <summary>Absolute expiry (0 = template/config default).</summary>
        public long ExpiresMs;
    }
}
