using System;
using System.Collections.Generic;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Core
{
    public sealed partial class GameState
    {
        public MailState Mail = new MailState();
    }
}

namespace RogueStars.Rules.Systems
{
    [Serializable]
    public sealed class MailState
    {
        public List<MailItem> Inbox = new List<MailItem>();
        public long NextId = 1;
    }

    [Serializable]
    public sealed class MailItem
    {
        public string Id;
        public string TemplateId;
        public string Title;
        public string Body;
        public List<Core.Reward> Rewards = new List<Core.Reward>();
        public long SentMs;
        /// <summary>0 = never expires.</summary>
        public long ExpiresMs;
        public bool Read;
        public bool Claimed;
    }
}
