using System.Collections.Generic;
using RogueStars.Client.App;
using RogueStars.Client.UiKit;
using RogueStars.Client.World;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using UnityEngine;

namespace RogueStars.Client.Screens
{
    /// <summary>Inbox: "N messages, M unread", an unread dot, rewards as icon tiles, an envelope illustration when empty (MS2).</summary>
    public sealed class MailScreen : UiScreen
    {
        public override string Title => "Mail";

        /// <summary>"1 message", "3 messages, 1 unread".</summary>
        public static string CountLine(int count, int unread) =>
            count + (count == 1 ? " message" : " messages") + (unread > 0 ? ", " + unread + " unread" : "");

        protected override void Build(RectTransform body)
        {
            var inbox = new List<MailItem>(State.Mail.Inbox);
            inbox.Sort((a, b) => b.SentMs.CompareTo(a.SentMs));
            var top = Ui.Row(body);
            Ui.Label(top, CountLine(inbox.Count, MailSystem.UnreadCount(State)), Theme.FontNormal, Theme.TextDim, TextAnchor.MiddleLeft, "MailCount");
            bool anyRewards = inbox.Exists(MailSystem.HasUnclaimed);
            Ui.Button(top, "Claim all", () => Run(new MailClaimAll()), Theme.Good, 260, Theme.ButtonHeight, anyRewards);
            if (inbox.Count == 0) Empty(body);
            foreach (var mail in inbox)
            {
                var m = mail;
                var card = Card(body, m.Read ? Theme.Panel : Theme.PanelAlt);
                var head = Ui.Row(card, 48, 12);
                head.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childForceExpandWidth = false;
                if (!m.Read) Ui.Size(Ui.Dot(head, Theme.Accent, 22, "UnreadDot"), 22, 22, 0);
                Ui.Display(head, m.Title, Theme.FontNormal, m.Read ? Theme.TextDim : Theme.Text, TextAnchor.MiddleLeft, "MailTitle");
                if (m.Rewards.Count > 0) RewardIcons(card, m.Rewards, m.Claimed);
                if (m.ExpiresMs > 0) Live(Note(card, ""), () => "Expires in " + Fmt.Duration(m.ExpiresMs - Now));
                var row = Ui.Row(card, 80);
                Ui.Button(row, "Open", () => Open(m), Theme.Button, -1, 80);
                if (MailSystem.HasUnclaimed(m)) Ui.Button(row, "Claim", () => Run(new MailClaim { MailId = m.Id }), Theme.Good, -1, 80);
                else Ui.Button(row, "Delete", () => Run(new MailDelete { MailId = m.Id }), Theme.Bad, -1, 80);
            }
        }

        /// <summary>The rewards as icon tiles (usable ones; the rest waits in the reward stash).</summary>
        private void RewardIcons(Transform parent, List<Reward> rewards, bool claimed)
        {
            var usable = RewardGate.Usable(State, Content, rewards, Now);
            if (usable.Count == 0) return;
            var row = Ui.Row(parent, 110, 12, null, 0, "MailRewards");
            row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childForceExpandWidth = false;
            foreach (var r in usable) SummonKit.RewardTile(row, r, 100, null, claimed, claimed ? "claimed" : null);
        }

        private void Empty(Transform body)
        {
            var holder = Ui.Column(body, 12, 40, null, "EmptyMail");
            holder.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            var env = SummonKit.Img(holder, SummonArt.Icon("envelope"), "Envelope");
            Ui.Size(env, 360, 360, 0);
            env.color = new Color(1, 1, 1, 0.85f);
            Ui.Display(holder, "Your mailbox is empty.", Theme.FontNormal, Theme.TextDim, TextAnchor.MiddleCenter, "Note");
            Ui.Label(holder, "Gifts, rewards and news arrive here.", Theme.FontSmall, Theme.TextFaint, TextAnchor.MiddleCenter);
        }

        private void Open(MailItem m)
        {
            if (!m.Read) Run(new MailRead { MailId = m.Id });
            Modal(m.Title, b =>
            {
                Ui.Label(b, m.Body, Theme.FontNormal);
                if (m.Rewards.Count > 0) RewardIcons(b, m.Rewards, m.Claimed);
            }, MailSystem.HasUnclaimed(m)
                ? new[] { new ModalButton("Claim", () => Run(new MailClaim { MailId = m.Id }), Theme.Good), new ModalButton("Close", null) }
                : new[] { new ModalButton("Close", null) });
        }
    }
}
