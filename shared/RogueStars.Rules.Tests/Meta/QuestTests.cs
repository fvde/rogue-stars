using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using static RogueStars.Rules.Tests.Meta.MetaTestKit;

namespace RogueStars.Rules.Tests.Meta;

public class QuestTests
{
    [Fact]
    public void DailyQuest_ProgressFromCounters_Claim_ResetsAtMidnightUtc()
    {
        var e = Engine(); var s = Player(e);
        long t = TestHelpers.T0;
        var q = e.Content.Quest("d_runs")!;
        s.Count("runs_completed", 2);
        Assert.Equal(2, QuestSystem.Progress(s, q));
        Fail(e, ref s, new QuestClaim { QuestId = "d_runs" }, t);
        s.Count("runs_completed");
        long dia = Bal(s, Currencies.Diamonds);
        Ok(e, ref s, new QuestClaim { QuestId = "d_runs" }, t);
        Assert.Equal(dia + 10, Bal(s, Currencies.Diamonds));
        Assert.Equal(20, s.Quests.DailyPoints);
        Fail(e, ref s, new QuestClaim { QuestId = "d_runs" }, t + H);

        // 23:59:59 same day: still claimed. 00:00 UTC: progress and claim reset.
        Fail(e, ref s, new QuestClaim { QuestId = "d_runs" }, Day0 + D - 1);
        e.Settle(s, Day0 + D);
        Assert.Equal(0, QuestSystem.Progress(s, q));
        Assert.Equal(0, s.Quests.DailyPoints);
        s.Count("runs_completed", 3);
        Ok(e, ref s, new QuestClaim { QuestId = "d_runs" }, Day0 + D + H);
    }

    [Fact]
    public void WeeklyQuest_ResetsMondayUtc_NotSunday()
    {
        var e = Engine(); var s = Player(e);
        var q = e.Content.Quest("w_runs")!;
        s.Count("runs_completed", 15);
        e.Settle(s, Monday - 1); // Sunday 23:59:59.999
        Assert.Equal(15, QuestSystem.Progress(s, q));
        e.Settle(s, Monday);
        Assert.Equal(0, QuestSystem.Progress(s, q));
        s.Count("runs_completed", 20);
        Ok(e, ref s, new QuestClaim { QuestId = "w_runs" }, Monday + H);
        Assert.Equal(100, s.Quests.WeeklyPoints);
        Assert.Equal(100 + 35 * 20, ShopSystem.SeasonXp(s, e.Content)); // quest season XP + counter XP (runs ×20)
    }

    [Fact]
    public void PointChests_NeedPoints_ClaimOncePerPeriod()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        s.Count("runs_completed", 3); s.Count("run_battles_won", 10);
        Fail(e, ref s, new QuestClaimChest { Track = PointTrack.Daily, Points = 40 }, t);
        Ok(e, ref s, new QuestClaimAll(), t);
        Assert.True(s.Quests.DailyPoints >= 40);
        Ok(e, ref s, new QuestClaimChest { Track = PointTrack.Daily, Points = 40 }, t);
        Fail(e, ref s, new QuestClaimChest { Track = PointTrack.Daily, Points = 40 }, t);
        Fail(e, ref s, new QuestClaimChest { Track = PointTrack.Daily, Points = 33 }, t);
        e.Settle(s, Day0 + D);
        Assert.Empty(s.Quests.ChestsClaimed);
    }

    [Fact]
    public void FeatureGatedQuests_HiddenUntilUnlocked()
    {
        var e = Engine(); var s = Player(e); long t = TestHelpers.T0;
        s.Count("arena_fights", 5);
        Assert.DoesNotContain(QuestSystem.Visible(s, e.Content, QuestKind.Daily, t), q => q.Id == "d_arena");
        Fail(e, ref s, new QuestClaim { QuestId = "d_arena" }, t);
        UnlockAll(s);
        Assert.Contains(QuestSystem.Visible(s, e.Content, QuestKind.Daily, t), q => q.Id == "d_arena");
    }

    [Fact]
    public void Achievements_UseAbsoluteCounters_NeverReset()
    {
        var e = Engine(); var s = Player(e);
        s.Count("runs_completed", 10);
        Ok(e, ref s, new QuestClaim { QuestId = "a_runs_10" }, TestHelpers.T0);
        e.Settle(s, Monday + 7 * D);
        Fail(e, ref s, new QuestClaim { QuestId = "a_runs_10" }, Monday + 7 * D);
        Assert.Equal(10, QuestSystem.Progress(s, e.Content.Quest("a_runs_50")!));
    }

    [Fact]
    public void Login_SevenDayCycle_NoStreakLoss()
    {
        var e = Engine(); var s = Player(e);
        var cycle = e.Content.Retention.LoginCycle;
        Assert.Equal(7, cycle.Count);
        var r = Ok(e, ref s, new LoginClaim(), TestHelpers.T0);
        Assert.Equal(1, (int)r.Payload!);
        Fail(e, ref s, new LoginClaim(), TestHelpers.T0 + H);
        // Skip three days: next claim continues with day 2, no reset.
        r = Ok(e, ref s, new LoginClaim(), Day0 + 4 * D);
        Assert.Equal(2, (int)r.Payload!);
        for (int d = 3; d <= 7; d++) Assert.Equal(d, (int)Ok(e, ref s, new LoginClaim(), Day0 + (2 + d) * D).Payload!);
        Assert.Equal(1, (int)Ok(e, ref s, new LoginClaim(), Day0 + 20 * D).Payload!); // wraps
        Assert.Equal(8, s.Counter("login_days"));
    }

    [Fact]
    public void NewPlayerEvent_DayUnlocks_AndWindow()
    {
        var e = Engine(); var s = Player(e);
        Ok(e, ref s, new NewPlayerClaim { Day = 1 }, TestHelpers.T0);
        Fail(e, ref s, new NewPlayerClaim { Day = 1 }, TestHelpers.T0);
        Fail(e, ref s, new NewPlayerClaim { Day = 2 }, TestHelpers.T0);
        Ok(e, ref s, new NewPlayerClaim { Day = 2 }, Day0 + D);
        Assert.DoesNotContain(QuestSystem.Visible(s, e.Content, QuestKind.NewPlayer, Day0 + D), q => q.Day > 2);
        s.Count("runs_completed", 3);
        Ok(e, ref s, new QuestClaim { QuestId = "np_runs_3" }, Day0 + D);
        Ok(e, ref s, new NewPlayerClaim { Day = 7 }, Day0 + 9 * D); // grace
        Assert.Equal("event over", Fail(e, ref s, new NewPlayerClaim { Day = 6 }, Day0 + 10 * D));
    }

    [Fact]
    public void ReturningPlayer_GetsGiftMail_WithCooldown()
    {
        var e = Engine(); var s = Player(e);
        Ok(e, ref s, new LoginClaim(), TestHelpers.T0);
        int before = s.Mail.Inbox.Count;
        var r = Ok(e, ref s, new LoginClaim(), TestHelpers.T0 + 8 * D);
        Assert.Contains(r.Events, ev => ev.Type == "returning_player");
        Assert.Equal(before + 1, s.Mail.Inbox.Count);
        Assert.Equal("returning_gift", s.Mail.Inbox.Last().TemplateId);
        // Away again shortly after: cooldown prevents another gift.
        Ok(e, ref s, new LoginClaim(), TestHelpers.T0 + 16 * D);
        Assert.Equal(1, s.Mail.Inbox.Count(m => m.TemplateId == "returning_gift"));
    }
}
