using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Server.Tests;

/// <summary>Campfire Train (owner rule): the server validates the chosen skill through the rules.</summary>
public class CampfireTrainServerTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();
    private ContentDb Content => _app.Service<GameEngine>().Content;

    private List<string> Commons(int n)
    {
        var list = new List<string>();
        foreach (var id in RunDraft.SortedSkillIds(Content))
        {
            var s = Content.Skills[id];
            if (list.Count >= n) break;
            if (s.Source != SkillSource.Run || s.Signature || !s.Draftable || s.Rarity != Rarity.Common || RunDraft.HasUpgrade(Content, id) || !SkillLeveling.Grows(Content, s)) continue;
            list.Add(id);
        }
        return list;
    }

    [Fact]
    public async Task Train_AcceptsAnOwnedUpgradableSkill_AndRefusesOthers()
    {
        var p = await _app.PlayerAsync("train");
        var start = await p.CommandAsync("RunStart", new { levelId = Content.MainLevel(1).Id, mode = (int)RunMode.Normal }, "run");
        Assert.True(start.Body.GetProperty("ok").GetBoolean(), start.Body.GetProperty("error").ToString());
        var two = Commons(2);
        Assert.True((await p.CommandAsync("TestCampfire", new { skillIds = new[] { two[0] } }, "fire")).Body.GetProperty("ok").GetBoolean());

        var bad = await p.CommandAsync("RunChooseEventOption", new { index = 1, skillId = two[1] }, "bad");
        Assert.False(bad.Body.GetProperty("ok").GetBoolean());
        Assert.Equal("skill cannot be improved", bad.Body.GetProperty("error").GetString());

        var ok = await p.CommandAsync("RunChooseEventOption", new { index = 1, skillId = two[0] }, "ok");
        Assert.True(ok.Body.GetProperty("ok").GetBoolean(), ok.Body.GetProperty("error").ToString());
        var levels = (await p.StateAsync()).GetProperty("run").GetProperty("active").GetProperty("skillLevels");
        Assert.Equal(2, levels.GetProperty(two[0]).GetInt32());
    }
}
