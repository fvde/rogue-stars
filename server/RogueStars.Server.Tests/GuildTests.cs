namespace RogueStars.Server.Tests;

public class GuildTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    private static string Role(System.Text.Json.JsonElement guild, string playerId) =>
        guild.GetProperty("members").EnumerateArray().First(m => m.GetProperty("playerId").GetString() == playerId).GetProperty("role").GetString()!;

    [Fact]
    public async Task Create_Join_Promote_Kick_Leave_Succession()
    {
        var leader = await _app.PlayerAsync("leader");
        var officer = await _app.PlayerAsync("officer");
        var member = await _app.PlayerAsync("member");

        var (s, created) = await leader.PostAsync("/v1/guilds", new { name = "Star Eaters", description = "hi" });
        Assert.Equal(200, s);
        string id = created.GetProperty("guild").GetProperty("id").GetString()!;

        Assert.Equal(409, (await officer.PostAsync("/v1/guilds", new { name = "star  EATERS" })).Status); // name taken
        Assert.Equal(409, (await leader.PostAsync("/v1/guilds", new { name = "Second" })).Status);         // already in guild

        var (_, list) = await member.GetAsync("/v1/guilds?query=star");
        Assert.Equal(id, list.GetProperty("guilds")[0].GetProperty("id").GetString());

        Assert.Equal(200, (await officer.PostAsync($"/v1/guilds/{id}/join")).Status);
        Assert.Equal(200, (await member.PostAsync($"/v1/guilds/{id}/join")).Status);
        Assert.Equal(409, (await member.PostAsync($"/v1/guilds/{id}/join")).Status);

        // Only the leader may promote.
        Assert.Equal(403, (await member.PostAsync("/v1/guilds/promote", new { playerId = officer.PlayerId, role = "officer" })).Status);
        var (_, promoted) = await leader.PostAsync("/v1/guilds/promote", new { playerId = officer.PlayerId, role = "officer" });
        Assert.Equal("officer", Role(promoted.GetProperty("guild"), officer.PlayerId));

        // Members cannot kick; officers can kick members but not the leader.
        Assert.Equal(403, (await member.PostAsync("/v1/guilds/kick", new { playerId = officer.PlayerId })).Status);
        Assert.Equal(403, (await officer.PostAsync("/v1/guilds/kick", new { playerId = leader.PlayerId })).Status);
        Assert.Equal(200, (await officer.PostAsync("/v1/guilds/kick", new { playerId = member.PlayerId })).Status);
        var (_, mine) = await member.GetAsync("/v1/guilds/mine");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, mine.GetProperty("guild").ValueKind);

        // Leader leaves: the officer inherits leadership.
        Assert.Equal(200, (await leader.PostAsync("/v1/guilds/leave")).Status);
        var (_, after) = await officer.GetAsync("/v1/guilds/mine");
        Assert.Equal("leader", Role(after.GetProperty("guild"), officer.PlayerId));

        // Last member leaves: guild disbanded and the name is free again.
        Assert.Equal(200, (await officer.PostAsync("/v1/guilds/leave")).Status);
        Assert.Equal(404, (await member.GetAsync($"/v1/guilds/{id}")).Status);
        Assert.Equal(200, (await member.PostAsync("/v1/guilds", new { name = "Star Eaters" })).Status);
    }

    [Fact]
    public async Task LeaderTransfer_ViaPromoteToLeader()
    {
        var a = await _app.PlayerAsync("a");
        var b = await _app.PlayerAsync("b");
        var (_, g) = await a.PostAsync("/v1/guilds", new { name = "Transfer" });
        string id = g.GetProperty("guild").GetProperty("id").GetString()!;
        await b.PostAsync($"/v1/guilds/{id}/join");
        var (_, res) = await a.PostAsync("/v1/guilds/promote", new { playerId = b.PlayerId, role = "leader" });
        Assert.Equal("leader", Role(res.GetProperty("guild"), b.PlayerId));
        Assert.Equal("officer", Role(res.GetProperty("guild"), a.PlayerId));
    }

    [Fact]
    public async Task Chat_PostRead_RateLimit_Filter_Report()
    {
        _app.Settings["RogueStars:Guilds:BlockedWords:0"] = "zorg";
        var a = await _app.PlayerAsync("a");
        var b = await _app.PlayerAsync("b");
        var (_, g) = await a.PostAsync("/v1/guilds", new { name = "Chatters" });
        await b.PostAsync($"/v1/guilds/{g.GetProperty("guild").GetProperty("id").GetString()}/join");

        var (s, posted) = await a.PostAsync("/v1/guilds/chat", new { text = "you ZORG!" });
        Assert.Equal(200, s);
        Assert.Equal("you ****!", posted.GetProperty("message").GetProperty("text").GetString());

        // Minimum interval between messages.
        Assert.Equal(429, (await a.PostAsync("/v1/guilds/chat", new { text = "too fast" })).Status);

        // At most 5 per 30 s window.
        for (int i = 0; i < 4; i++)
        {
            _app.Time.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(200, (await a.PostAsync("/v1/guilds/chat", new { text = "msg " + i })).Status);
        }
        _app.Time.Advance(TimeSpan.FromSeconds(2));
        var limited = await a.PostAsync("/v1/guilds/chat", new { text = "sixth" });
        Assert.Equal(429, limited.Status);
        Assert.Equal("rate_limited", limited.Body.GetProperty("error").GetString());
        Assert.Equal(200, (await b.PostAsync("/v1/guilds/chat", new { text = "others unaffected" })).Status);
        _app.Time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(200, (await a.PostAsync("/v1/guilds/chat", new { text = "window passed" })).Status);

        var (_, chat) = await b.GetAsync("/v1/guilds/chat");
        var messages = chat.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(7, messages.Count);
        string firstId = messages[0].GetProperty("id").GetString()!;

        var (rs, report) = await b.PostAsync($"/v1/guilds/chat/{firstId}/report", new { reason = "rude" });
        Assert.Equal(200, rs);
        Assert.False(string.IsNullOrEmpty(report.GetProperty("reportId").GetString()));
        Assert.Equal(404, (await b.PostAsync("/v1/guilds/chat/nope/report", new { reason = "x" })).Status);

        var outsider = await _app.PlayerAsync("outsider");
        Assert.Equal(400, (await outsider.PostAsync("/v1/guilds/chat", new { text = "hi" })).Status);
    }
}
