using System.Text.Json;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Server.Tests;

/// <summary>
/// GDD §16 level unlocks on the authoritative server: a locked feature's command is rejected, clearing the tutorial level 1
/// through the API records the first unlock the same day (no calendar gate), and new saves start on the current onboarding
/// version.
/// </summary>
public class LevelUnlockServerTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();
    private ContentDb Content => _app.Service<GameEngine>().Content;

    [Fact]
    public async Task TutorialClear_UnlocksParts_TheSameDay_LockedCommandsAreRejected()
    {
        _app.DevUnlockAll = false;
        var p = await _app.PlayerAsync("levels");
        var state = await p.StateAsync();
        Assert.Equal(Content.Onboarding.Version, state.GetProperty("onboarding").GetProperty("version").GetInt32());

        var pull = await p.CommandAsync("GachaPull", new { pullType = (int)GachaType.Part, count = 1, payment = (int)GachaPayment.Diamonds }, "pull");
        Assert.False(pull.Body.GetProperty("ok").GetBoolean());
        Assert.Equal("feature locked", pull.Body.GetProperty("error").GetString());

        // the tutorial run, played through the command endpoint
        for (int attempt = 0; attempt < 3 && await p.CounterAsync(Requirements.MainLevelCounter) < 1; attempt++)
        {
            var start = await p.CommandAsync("RunStart", new { levelId = Content.MainLevel(1).Id, mode = (int)RunMode.Normal }, "run" + attempt);
            Assert.True(start.Body.GetProperty("ok").GetBoolean(), start.Body.GetProperty("error").ToString());
            for (int i = 0; i < 600; i++)
            {
                var st = await p.StateAsync();
                if (st.GetProperty("run").GetProperty("active").ValueKind == JsonValueKind.Null) break;
                var step = await p.CommandAsync("RunAutoStep", new { farming = true, farmShops = true }, "step" + attempt + "_" + i);
                Assert.True(step.Body.GetProperty("ok").GetBoolean(), step.Body.GetProperty("error").ToString());
            }
        }
        Assert.Equal(1, await p.CounterAsync(Requirements.MainLevelCounter));
        var unlocked = (await p.StateAsync()).GetProperty("onboarding").GetProperty("unlocked");
        Assert.True(unlocked.TryGetProperty(Features.Parts, out _), "Parts unlock with the first clear, the same day");
        Assert.False(unlocked.TryGetProperty(Features.RunShop, out _), "Shops wait for level 2");
        var celebrate = await p.CommandAsync("FeatureCelebrate", new { featureId = Features.Parts }, "celebrate");
        Assert.True(celebrate.Body.GetProperty("ok").GetBoolean(), celebrate.Body.GetProperty("error").ToString());
        Assert.False((await p.CommandAsync("FeatureCelebrate", new { featureId = Features.RunShop }, "early")).Body.GetProperty("ok").GetBoolean());
    }
}
