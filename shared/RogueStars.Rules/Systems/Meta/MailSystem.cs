using System.Collections.Generic;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;

namespace RogueStars.Rules.Systems
{
    /// <summary>
    /// Mailbox (GDD §15): inbox with rewards and expiry; claim / claim-all; expired mail is removed with
    /// its unclaimed rewards. Server pushes mail via <see cref="Push"/> (static) or the MailPush command.
    /// Events: "mail"(mailId), "mail_expired"(mailId).
    /// </summary>
    public static class MailSystem
    {
        public static void Register(GameEngine e)
        {
            e.RegisterSettler(Settle);
            e.RegisterNewPlayer(ctx =>
            {
                var t = ctx.Content.MailTemplate(ctx.Content.Mail.WelcomeTemplate);
                if (t != null) PushTemplate(ctx.State, ctx.Content, ctx.NowMs, t.Id);
            });
            e.Register<MailRead>(HandleRead);
            e.Register<MailClaim>(HandleClaim);
            e.Register<MailClaimAll>(HandleClaimAll);
            e.Register<MailDelete>(HandleDelete);
            e.Register<MailPush>(HandlePush);
        }

        /// <summary>
        /// Put a mail into the player's inbox (server helper; also used by other Meta systems).
        /// <paramref name="expiresMs"/> 0 = now + config default; negative = never expires. Returns the mail id.
        /// </summary>
        public static string Push(GameState state, ContentDb content, long nowMs, string title, string body, IEnumerable<Reward> rewards, long expiresMs = 0, string templateId = null)
        {
            var mail = state.Mail;
            var item = new MailItem
            {
                Id = "m" + mail.NextId++, TemplateId = templateId, Title = title, Body = body, SentMs = nowMs,
                ExpiresMs = expiresMs < 0 ? 0 : expiresMs == 0 ? nowMs + content.Mail.DefaultExpiryMs : expiresMs,
            };
            if (rewards != null) foreach (var r in rewards) item.Rewards.Add(new Reward(r.Kind, r.Id, r.Amount, r.Rarity));
            while (mail.Inbox.Count >= content.Mail.MaxInbox && mail.Inbox.Count > 0)
            {
                int drop = mail.Inbox.FindIndex(m => m.Claimed || (m.Read && m.Rewards.Count == 0));
                mail.Inbox.RemoveAt(drop >= 0 ? drop : 0);
            }
            mail.Inbox.Add(item);
            return item.Id;
        }

        /// <summary>Push a content mail template.</summary>
        public static string PushTemplate(GameState state, ContentDb content, long nowMs, string templateId)
        {
            var t = content.MailTemplate(templateId);
            if (t == null) throw new RuleException("unknown mail template");
            long exp = t.ExpiresAfterMs > 0 ? nowMs + t.ExpiresAfterMs : 0;
            return Push(state, content, nowMs, t.Title, t.Body, t.Rewards, exp, t.Id);
        }

        public static bool HasUnclaimed(MailItem m) => !m.Claimed && m.Rewards.Count > 0;

        public static int UnreadCount(GameState s)
        {
            int n = 0;
            foreach (var m in s.Mail.Inbox) if (!m.Read || HasUnclaimed(m)) n++;
            return n;
        }

        private static void Settle(CommandContext ctx)
        {
            var inbox = ctx.State.Mail.Inbox;
            for (int i = inbox.Count - 1; i >= 0; i--)
            {
                var m = inbox[i];
                if (m.ExpiresMs != 0 && ctx.NowMs >= m.ExpiresMs)
                {
                    inbox.RemoveAt(i);
                    ctx.Emit("mail_expired", m.Id);
                }
            }
        }

        private static MailItem Find(GameState s, string id)
        {
            foreach (var m in s.Mail.Inbox) if (m.Id == id) return m;
            return null;
        }

        private static void Claim(CommandContext ctx, MailItem m)
        {
            m.Claimed = true;
            m.Read = true;
            ctx.State.Count("mail_claimed");
            ctx.GrantAll(m.Rewards);
        }

        private static CommandResult HandleRead(MailRead cmd, CommandContext ctx)
        {
            var m = Find(ctx.State, cmd.MailId);
            GameEngine.Require(m != null, "mail not found");
            m.Read = true;
            return CommandResult.Success();
        }

        private static CommandResult HandleClaim(MailClaim cmd, CommandContext ctx)
        {
            var m = Find(ctx.State, cmd.MailId);
            GameEngine.Require(m != null, "mail not found");
            GameEngine.Require(HasUnclaimed(m), "nothing to claim");
            Claim(ctx, m);
            return CommandResult.Success();
        }

        private static CommandResult HandleClaimAll(MailClaimAll cmd, CommandContext ctx)
        {
            int n = 0;
            foreach (var m in ctx.State.Mail.Inbox) if (HasUnclaimed(m)) { Claim(ctx, m); n++; }
            GameEngine.Require(n > 0, "nothing to claim");
            return CommandResult.Success(n);
        }

        private static CommandResult HandleDelete(MailDelete cmd, CommandContext ctx)
        {
            var m = Find(ctx.State, cmd.MailId);
            GameEngine.Require(m != null, "mail not found");
            GameEngine.Require(!HasUnclaimed(m), "claim rewards first");
            ctx.State.Mail.Inbox.Remove(m);
            return CommandResult.Success();
        }

        private static CommandResult HandlePush(MailPush cmd, CommandContext ctx)
        {
            string id = cmd.TemplateId != null
                ? PushTemplate(ctx.State, ctx.Content, ctx.NowMs, cmd.TemplateId)
                : Push(ctx.State, ctx.Content, ctx.NowMs, cmd.Title, cmd.Body, cmd.Rewards, cmd.ExpiresMs);
            ctx.Emit("mail", id);
            return CommandResult.Success(id);
        }
    }
}
