using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Meta.MetaTestKit;

namespace RogueStars.Rules.Tests.Meta;

public class MailTests
{
    [Fact]
    public void WelcomeMail_ClaimOnce()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        var welcome = s.Mail.Inbox.Single();
        long dia = Bal(s, Currencies.Diamonds);
        Ok(e, ref s, new MailClaim { MailId = welcome.Id }, t);
        Assert.Equal(dia + 100, Bal(s, Currencies.Diamonds));
        Assert.Equal("nothing to claim", Fail(e, ref s, new MailClaim { MailId = welcome.Id }, t));
        Ok(e, ref s, new MailDelete { MailId = welcome.Id }, t);
        Assert.Empty(s.Mail.Inbox);
    }

    [Fact]
    public void Expiry_RemovesUnclaimedMail()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        MailSystem.Push(s, e.Content, t, "Short", "x", new[] { Reward.Cur(Currencies.Diamonds, 5) }, t + D);
        MailSystem.Push(s, e.Content, t, "Forever", "x", new[] { Reward.Cur(Currencies.Ore, 5) }, -1);
        Assert.Equal(3, s.Mail.Inbox.Count);
        var r = Ok(e, ref s, new MailRead { MailId = s.Mail.Inbox[2].Id }, t + D);
        Assert.Contains(r.Events, ev => ev.Type == "mail_expired");
        Assert.DoesNotContain(s.Mail.Inbox, m => m.Title == "Short");
        e.Settle(s, t + 31 * D); // welcome expires after 30 days, "Forever" stays
        Assert.DoesNotContain(s.Mail.Inbox, m => m.TemplateId == "welcome");
        Assert.Contains(s.Mail.Inbox, m => m.Title == "Forever");
    }

    [Fact]
    public void ClaimAll_AndServerPush()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        Assert.True(ServerOnly.Is(typeof(MailPush)));
        var push = new MailPush { Title = "Sorry", Body = "Downtime", ExpiresMs = 0 };
        push.Rewards.Add(Reward.Cur(Currencies.Diamonds, 50));
        Ok(e, ref s, push, t);
        Ok(e, ref s, new MailPush { TemplateId = "returning_gift" }, t);
        long dia = Bal(s, Currencies.Diamonds);
        var r = Ok(e, ref s, new MailClaimAll(), t);
        Assert.Equal(3, (int)r.Payload!);
        Assert.Equal(dia + 100 + 50 + 200, Bal(s, Currencies.Diamonds));
        Fail(e, ref s, new MailClaimAll(), t);
        Assert.Equal(0, MailSystem.UnreadCount(s));
    }

    [Fact]
    public void InboxCap_DropsOldestClaimedFirst()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        for (int i = 0; i < e.Content.Mail.MaxInbox + 5; i++) MailSystem.Push(s, e.Content, t, "m" + i, "", null);
        Assert.Equal(e.Content.Mail.MaxInbox, s.Mail.Inbox.Count);
    }
}

public class ArenaTests
{
    [Fact]
    public void PointsMath_EloStyle()
    {
        var c = ContentDb.Default;
        Assert.Equal(20, ArenaSystem.PointsDelta(c, 1000, 1000, true));
        Assert.Equal(-20, ArenaSystem.PointsDelta(c, 1000, 1000, false));
        Assert.True(ArenaSystem.PointsDelta(c, 1000, 1400, true) > 30);         // upset win pays more
        Assert.True(ArenaSystem.PointsDelta(c, 1000, 1400, false) > -10);       // losing to stronger costs little
        Assert.Equal(c.Arena.MinWinGain, ArenaSystem.PointsDelta(c, 2000, 1000, true)); // every win counts
        Assert.True(ArenaSystem.PointsDelta(c, 2000, 1000, false) <= -35);
    }

    [Fact]
    public void League_PromotionDemotion_In150Groups()
    {
        var c = ContentDb.Default;
        Assert.Equal(150, c.Arena.GroupSize);
        int mid = 2;
        Assert.Equal(LeagueOutcome.Promote, ArenaSystem.Outcome(c, mid, 30, 150));
        Assert.Equal(LeagueOutcome.Stay, ArenaSystem.Outcome(c, mid, 31, 150));
        Assert.Equal(LeagueOutcome.Stay, ArenaSystem.Outcome(c, mid, 120, 150));
        Assert.Equal(LeagueOutcome.Demote, ArenaSystem.Outcome(c, mid, 121, 150));
        Assert.Equal(LeagueOutcome.Stay, ArenaSystem.Outcome(c, 0, 150, 150));            // bottom league: no demotion
        Assert.Equal(LeagueOutcome.Stay, ArenaSystem.Outcome(c, c.Arena.Leagues.Count - 1, 1, 150)); // top league: no promotion

        var players = Enumerable.Range(0, 150).Select(i => new ArenaStanding { PlayerId = "p" + i.ToString("D3"), Points = 1000 + (i % 75) }).ToList();
        var ranked = ArenaSystem.ResolveGroup(c, mid, players);
        Assert.Equal(30, ranked.Count(x => x.Outcome == LeagueOutcome.Promote));
        Assert.Equal(30, ranked.Count(x => x.Outcome == LeagueOutcome.Demote));
        Assert.Equal("p074", ranked[0].PlayerId); // highest points, tie broken by id
        Assert.Throws<ArgumentException>(() => ArenaSystem.ResolveGroup(c, mid, players.Concat(new[] { new ArenaStanding { PlayerId = "x" } }).ToList()));

        var groups = ArenaSystem.SplitIntoGroups(c, Enumerable.Range(0, 301).Select(i => "p" + i).ToList());
        Assert.Equal(3, groups.Count);
        Assert.All(groups, g => Assert.InRange(g.Count, 100, 150));
    }

    [Fact]
    public void FightFlow_TicketsRegen_ServerResult_WeekEnd()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        Assert.Equal(5, Bal(s, Currencies.ArenaTickets));
        Assert.Equal("feature locked", Fail(e, ref s, new ArenaStartFight { OpponentId = "p2" }, t));
        s.Counters[Requirements.MainLevelCounter] = 16;
        Fail(e, ref s, new ArenaFightResult { Won = true, OpponentPoints = 1000 }, t);
        for (int i = 0; i < 5; i++) Ok(e, ref s, new ArenaStartFight { OpponentId = "p2" }, t);
        Assert.Equal("no arena tickets", Fail(e, ref s, new ArenaStartFight { OpponentId = "p2" }, t));
        Ok(e, ref s, new ArenaFightResult { Won = true, OpponentPoints = 1000 }, t);
        Assert.Equal(1020, s.Arena.Points);
        Assert.Equal(1, s.Counter("arena_wins"));
        e.Settle(s, t + 4 * H);
        Assert.Equal(2, Bal(s, Currencies.ArenaTickets)); // 1 per 2 h

        long dia = Bal(s, Currencies.Diamonds);
        var r = Ok(e, ref s, new ArenaWeekEnd { Rank = 1, GroupSize = 150 }, t);
        Assert.Equal(LeagueOutcome.Promote, r.Payload);
        Assert.Equal(1, s.Arena.League);
        Assert.Equal(dia + 200, Bal(s, Currencies.Diamonds));
        Assert.Equal(e.Content.Arena.StartPoints, s.Arena.Points);
        Fail(e, ref s, new ArenaWeekEnd { Rank = 151, GroupSize = 151 }, t);
        Ok(e, ref s, new ArenaWeekEnd { Rank = 150, GroupSize = 150 }, t);
        Assert.Equal(0, s.Arena.League);
    }

    [Fact]
    public void FightResult_PayloadCarriesRulesPoints_AndPointsResetInANewWeek()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        s.Counters[Requirements.MainLevelCounter] = 16;
        Ok(e, ref s, new ArenaStartFight { OpponentId = "p2" }, t);
        var won = Assert.IsType<ArenaFightOutcome>(Ok(e, ref s, new ArenaFightResult { Won = true, OpponentPoints = 1000 }, t).Payload);
        Assert.Equal(20, won.Delta);
        Assert.Equal(1020, won.Points);
        Assert.Equal(s.Arena.Points, won.Points);
        Ok(e, ref s, new ArenaStartFight { OpponentId = "p2" }, t);
        var lost = Assert.IsType<ArenaFightOutcome>(Ok(e, ref s, new ArenaFightResult { Won = false, OpponentPoints = 1020 }, t).Payload);
        Assert.True(lost.Delta < 0);
        Assert.Equal(1020 + lost.Delta, lost.Points);

        // Next reset-week: the first fight starts from StartPoints again (best points are kept).
        long nextWeek = GameClock.WeekStartMs(e.Content, GameClock.WeekIndex(e.Content, t) + 1) + H;
        Ok(e, ref s, new ArenaStartFight { OpponentId = "p2" }, nextWeek);
        Assert.Equal(e.Content.Arena.StartPoints, s.Arena.Points);
        Assert.Equal(0, s.Arena.WeekWins);
        var again = Assert.IsType<ArenaFightOutcome>(Ok(e, ref s, new ArenaFightResult { Won = true, OpponentPoints = 1000 }, nextWeek).Payload);
        Assert.Equal(1020, again.Points);
        Assert.Equal(1020, again.BestPoints);
    }

    [Fact]
    public void Snapshot_ContainsLoadout()
    {
        var e = Engine();
        double baseAtk = e.BuildLoadout(Player(e), TestHelpers.T0).WormStats.Value(StatId.Atk);
        e.RegisterLoadoutContributor((st, c, now, l) => { l.HeroId = "hero_star_knight"; l.Power = 4242; l.WormStats.Add(new StatMod(StatId.Atk, 10)); });
        var s = Player(e);
        s.DisplayName = "Wormy";
        s.Arena.Points = 1234;
        var snap = ArenaSystem.CreateSnapshot(e, s, TestHelpers.T0);
        Assert.Equal("p1", snap.PlayerId);
        Assert.Equal("Wormy", snap.Loadout.DisplayName);
        Assert.Equal("hero_star_knight", snap.Loadout.HeroId);
        Assert.Equal(4242, snap.Power);
        Assert.Equal(1234, snap.Points);
        Assert.Equal(baseAtk + 10, snap.Loadout.WormStats.Value(StatId.Atk));
        Assert.Equal(snap.Loadout.HeroId, TestHelpers.Clone(snap).Loadout.HeroId);
    }
}

public class GuildTests
{
    [Fact]
    public void Membership_ServerOnly_Roles_Leave()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        Assert.True(ServerOnly.Is(typeof(GuildSetMembership)));
        Assert.Equal("feature locked", Fail(e, ref s, new GuildSetMembership { GuildId = "g1", Role = GuildRoles.Member }, t));
        UnlockAll(s);
        Fail(e, ref s, new GuildSetMembership { GuildId = "g1", Role = "emperor" }, t);
        Ok(e, ref s, new GuildSetMembership { GuildId = "g1", Role = GuildRoles.Member }, t);
        Ok(e, ref s, new GuildSetMembership { GuildId = "g1", Role = GuildRoles.Officer }, t);
        Assert.Equal(GuildRoles.Officer, s.Guild.Role);
        Ok(e, ref s, new GuildSetMembership { GuildId = null }, t);
        Assert.False(GuildSystem.InGuild(s));
        Assert.Equal("rejoin cooldown", Fail(e, ref s, new GuildSetMembership { GuildId = "g2", Role = GuildRoles.Member }, t + H));
        Ok(e, ref s, new GuildSetMembership { GuildId = "g2", Role = GuildRoles.Leader }, t + 13 * H);
    }

    [Fact]
    public void GuildShop_MarksAndWeeklyLimit()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        UnlockAll(s);
        Assert.Equal("not in a guild", Fail(e, ref s, new GuildShopBuy { ItemId = "guild_ore" }, t));
        Ok(e, ref s, new GuildSetMembership { GuildId = "g1", Role = GuildRoles.Member }, t);
        Assert.Equal("not enough currency", Fail(e, ref s, new GuildShopBuy { ItemId = "guild_ore" }, t));
        s.Wallet.Add(Currencies.GuildMarks, 1000);
        for (int i = 0; i < 5; i++) Ok(e, ref s, new GuildShopBuy { ItemId = "guild_ore" }, t);
        Assert.Equal("item limit reached", Fail(e, ref s, new GuildShopBuy { ItemId = "guild_ore" }, t));
        Assert.Equal(500, Bal(s, Currencies.GuildMarks));
        Ok(e, ref s, new GuildShopBuy { ItemId = "guild_ore" }, Monday);
        Fail(e, ref s, new GuildShopBuy { ItemId = "daily_fuel" }, Monday);
    }

    [Fact]
    public void InvasionContribution_CountsWeekly_GrantsMarks()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        UnlockAll(s);
        Ok(e, ref s, new GuildSetMembership { GuildId = "g1", Role = GuildRoles.Member }, t);
        var ctx = new CommandContext { State = s, Content = e.Content, Engine = e, NowMs = t };
        GuildSystem.AddInvasionContribution(ctx, 500);
        Assert.Equal(500, GuildSystem.WeeklyContribution(s, e.Content, t));
        Assert.Equal(5, Bal(s, Currencies.GuildMarks));
        e.Settle(s, Monday);
        Assert.Equal(0, GuildSystem.WeeklyContribution(s, e.Content, Monday));
        Assert.Equal(500, s.Guild.TotalInvasionContribution);
        Assert.Equal(3, GuildSystem.GuildLevel(e.Content, 3_500));
    }
}
