using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    public sealed class MailScreen : UiScreen
    {
        public override string Title => "Mail";

        protected override void Build(RectTransform body)
        {
            var inbox = new List<MailItem>(State.Mail.Inbox);
            inbox.Sort((a, b) => b.SentMs.CompareTo(a.SentMs));
            var top = Ui.Row(body);
            Ui.Label(top, inbox.Count + " mails, " + MailSystem.UnreadCount(State) + " unread", Theme.FontNormal, Theme.TextDim);
            bool anyRewards = inbox.Exists(MailSystem.HasUnclaimed);
            Ui.Button(top, "Claim all", () => Run(new MailClaimAll()), Theme.Good, 260, Theme.ButtonHeight, anyRewards);
            if (inbox.Count == 0) Note(body, "Your mailbox is empty.");
            foreach (var mail in inbox)
            {
                var m = mail;
                var card = Card(body, m.Read ? Theme.Panel : Theme.PanelAlt);
                Ui.Label(card, (m.Read ? "" : "* ") + m.Title, Theme.FontNormal, m.Read ? Theme.TextDim : Theme.Text);
                if (m.Rewards.Count > 0) Ui.Label(card, Fmt.Rewards(m.Rewards, Content), Theme.FontSmall, m.Claimed ? Theme.TextDim : Theme.Good);
                if (m.ExpiresMs > 0) Live(Note(card, ""), () => "Expires in " + Fmt.Duration(m.ExpiresMs - Now));
                var row = Ui.Row(card, 80);
                Ui.Button(row, "Open", () => Open(m), Theme.Button, -1, 80);
                if (MailSystem.HasUnclaimed(m)) Ui.Button(row, "Claim", () => Run(new MailClaim { MailId = m.Id }), Theme.Good, -1, 80);
                else Ui.Button(row, "Delete", () => Run(new MailDelete { MailId = m.Id }), Theme.Bad, -1, 80);
            }
        }

        private void Open(MailItem m)
        {
            if (!m.Read) Run(new MailRead { MailId = m.Id });
            Modal(m.Title, b =>
            {
                Ui.Label(b, m.Body, Theme.FontNormal);
                if (m.Rewards.Count > 0) Ui.Label(b, "Rewards: " + Fmt.Rewards(m.Rewards, Content), Theme.FontSmall, Theme.Good);
            }, MailSystem.HasUnclaimed(m)
                ? new[] { new ModalButton("Claim", () => Run(new MailClaim { MailId = m.Id }), Theme.Good), new ModalButton("Close", null) }
                : new[] { new ModalButton("Close", null) });
        }
    }
}
