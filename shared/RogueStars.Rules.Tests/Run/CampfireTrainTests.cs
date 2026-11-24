using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>Campfire Train (owner rule): the player picks which owned skill improves; auto mode picks deterministically.</summary>
public class CampfireTrainTests
{
    private static (GameEngine e, GameState s) AtCampfire(params string[] skills)
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        run.Skills.Clear();
        run.SkillLevels.Clear();
        foreach (var id in skills) run.Skills.Add(id);
        run.Phase = RunPhase.Event;
        run.EventId = "campfire";
        run.EventTile = -1;
        return (e, s);
    }

    /// <summary>Plain draftable Common run skills (they level up, no upgrade chain).</summary>
    private static List<string> Commons(ContentDb c, int n)
    {
        var list = new List<string>();
        foreach (var id in RunDraft.SortedSkillIds(c))
        {
            var s = c.Skills[id];
            if (list.Count >= n) break;
            if (s.Source != SkillSource.Run || s.Signature || !s.Draftable || s.Rarity != Rarity.Common || RunDraft.HasUpgrade(c, id) || !SkillLeveling.Grows(c, s)) continue;
            list.Add(id);
        }
        return list;
    }

    private static int TrainIndex(ContentDb c) => c.RunEvents["campfire"].Options.FindIndex(o => RunSystem.ImprovesSkill(o));

    [Fact]
    public void Campfire_HasRestAndTrain()
    {
        var ev = ContentDb.Default.RunEvents["campfire"];
        Assert.Equal("Rest", ev.Options[0].Text);
        Assert.Equal("Train", ev.Options[1].Text);
        Assert.Equal(1, TrainIndex(ContentDb.Default));
        Assert.False(RunSystem.ImprovesSkill(ev.Options[0]));
    }

    [Fact]
    public void Train_UpgradesTheChosenSkill()
    {
        var c = ContentDb.Default;
        var two = Commons(c, 2);
        var (e, s) = AtCampfire(two[0], two[1]);
        Assert.Equal(two, RunSystem.TrainChoices(c, s.Run.Active!));
        var r = RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = TrainIndex(c), SkillId = two[1] });
        Assert.True(r.Ok, r.Error);
        Assert.Equal(1, RunDraft.Level(s.Run.Active!, two[0]));
        Assert.Equal(2, RunDraft.Level(s.Run.Active!, two[1]));
    }

    [Fact]
    public void Train_RefusesSkillsThatCannotImprove()
    {
        var c = ContentDb.Default;
        var two = Commons(c, 2);
        var (e, s) = AtCampfire(two[0]);
        // not owned
        Assert.False(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = TrainIndex(c), SkillId = two[1] }).Ok);
        // maxed
        s.Run.Active!.SkillLevels[two[0]] = c.RunTuning.MaxSkillLevelOf(Rarity.Common);
        Assert.DoesNotContain(two[0], RunSystem.TrainChoices(c, s.Run.Active));
        Assert.False(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = TrainIndex(c), SkillId = two[0] }).Ok);
        // a skill on an option that improves nothing (Rest)
        s.Run.Active.SkillLevels.Remove(two[0]);
        Assert.False(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = 0, SkillId = two[0] }).Ok);
        Assert.Equal(RunPhase.Event, s.Run.Active!.Phase); // nothing applied
    }

    [Fact]
    public void Train_NeverListsSignatures()
    {
        var c = ContentDb.Default;
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        foreach (var id in run.Skills)
            if (c.Skill(id)?.Signature == true)
            {
                Assert.Null(RunSystem.TrainTarget(c, run, id));
                Assert.DoesNotContain(id, RunSystem.TrainChoices(c, run));
            }
    }

    [Fact]
    public void Train_AutoPicksTheLowestLevel_Deterministically()
    {
        var c = ContentDb.Default;
        var two = Commons(c, 2);
        var (e, s) = AtCampfire(two[0], two[1]);
        s.Run.Active!.SkillLevels[two[0]] = 2;
        Assert.Equal(two[1], RunSystem.TrainAutoPick(c, s.Run.Active));
        var r = RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = TrainIndex(c) }); // no skill: the default
        Assert.True(r.Ok, r.Error);
        Assert.Equal(2, RunDraft.Level(s.Run.Active!, two[1]));
        Assert.Equal(2, RunDraft.Level(s.Run.Active!, two[0]));
        // ties: the earliest gained
        var (_, s2) = AtCampfire(two[1], two[0]);
        Assert.Equal(two[1], RunSystem.TrainAutoPick(c, s2.Run.Active!));
    }

    [Fact]
    public void Train_WithNothingToImprove_GivesCoins()
    {
        var c = ContentDb.Default;
        var (e, s) = AtCampfire();
        long coins = s.Run.Active!.Coins;
        Assert.Empty(RunSystem.TrainChoices(c, s.Run.Active));
        Assert.True(RunTestKit.Exec(e, ref s, new RunChooseEventOption { Index = TrainIndex(c) }).Ok);
        Assert.Equal(coins + c.RunTuning.DraftEmptyCoins, s.Run.Active!.Coins);
    }
}
