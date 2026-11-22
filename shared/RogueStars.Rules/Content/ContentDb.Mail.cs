using System;
using System.Collections.Generic;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Content
{
    /// <summary>Content-defined mail (welcome, returning-player gift...).</summary>
    [Serializable]
    public sealed class MailTemplateDef
    {
        public string Id;
        public string Title;
        public string Body;
        public List<Reward> Rewards = new List<Reward>();
        /// <summary>Lifetime after sending (0 = config default).</summary>
        public long ExpiresAfterMs;
    }

    [Serializable]
    public sealed class MailConfig
    {
        public long DefaultExpiryMs = 30 * MetaTime.Day;
        /// <summary>Inbox size; when full, the oldest read/claimed (else oldest) mail is dropped.</summary>
        public int MaxInbox = 100;
        /// <summary>Template sent to every new player (null = none).</summary>
        public string WelcomeTemplate = "welcome";
    }

    public sealed partial class ContentDb
    {
        public readonly Dictionary<string, MailTemplateDef> MailTemplates = new Dictionary<string, MailTemplateDef>();
        public readonly MailConfig Mail = new MailConfig();

        public MailTemplateDef MailTemplate(string id) => id != null && MailTemplates.TryGetValue(id, out var t) ? t : null;

        partial void BuildMail()
        {
            MailTemplates["welcome"] = new MailTemplateDef
            {
                Id = "welcome", Title = "Welcome, little worm!", Body = "The planet is empty, but not for long. Here is a little something to get you wriggling.",
                Rewards = { Reward.Cur(Currencies.Diamonds, 100), Reward.Cur(Currencies.Stamina, 30) },
            };
            MailTemplates["returning_gift"] = new MailTemplateDef
            {
                Id = "returning_gift", Title = "Welcome back!", Body = "The aliens missed you. They saved some supplies.",
                Rewards = { Reward.Cur(Currencies.Diamonds, 200), Reward.Cur(Currencies.Stamina, 60), Reward.Cur(Currencies.PartTokens, 5) },
                ExpiresAfterMs = 14 * MetaTime.Day,
            };
        }
    }
}
