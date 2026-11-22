using RogueStars.Server.Arena;
using RogueStars.Server.Commands;
using RogueStars.Server.Guilds;
using RogueStars.Server.Invasions;
using RogueStars.Server.Jobs;

namespace RogueStars.Server.Infrastructure;

public static class FeatureEndpoints
{
    public static void MapArena(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/arena");
        g.MapPost("/snapshot", async (HttpContext http, ArenaService arena, CancellationToken ct) =>
        {
            var loadout = await arena.UploadSnapshotAsync(http.PlayerId(), ct);
            return Results.Json(new { ok = true, power = loadout.Power, loadout }, StateJson.Wire);
        });
        g.MapPost("/fight", async (HttpContext http, FightRequest? req, ArenaService arena, CancellationToken ct) =>
            Results.Json(await arena.FightAsync(http.PlayerId(), req ?? new FightRequest(null, null), ct), StateJson.Wire));
        g.MapGet("/opponents", async (HttpContext http, ArenaService arena, CancellationToken ct) =>
            Results.Json(await arena.OpponentsAsync(http.PlayerId(), ct), StateJson.Wire));
        g.MapGet("/leaderboard", async (HttpContext http, ArenaService arena, CancellationToken ct) =>
            Results.Json(await arena.LeaderboardAsync(http.PlayerId(), ct), StateJson.Wire));
    }

    public static void MapGuilds(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/guilds");
        g.MapGet("", async (string? query, GuildService guilds, CancellationToken ct) =>
            Results.Ok(new { ok = true, guilds = await guilds.ListAsync(query, ct) }));
        g.MapPost("", async (HttpContext http, CreateGuildRequest req, GuildService guilds, CancellationToken ct) =>
            Results.Ok(new { ok = true, guild = await guilds.CreateAsync(http.PlayerId(), req, ct) }));
        g.MapGet("/mine", async (HttpContext http, GuildService guilds, Clock clock, CancellationToken ct) =>
        {
            var id = await guilds.GuildIdOfAsync(http.PlayerId(), ct);
            if (id == null) return Results.Ok(new { ok = true, guild = (Guild?)null });
            var guild = await guilds.GetAsync(id, ct);
            return Results.Ok(new { ok = true, guild, level = guild.Level, contributions = GuildService.ContributionSummary(guild, clock.WeekKey()) });
        });
        g.MapGet("/{guildId}", async (string guildId, GuildService guilds, CancellationToken ct) =>
        {
            var guild = await guilds.GetAsync(guildId, ct);
            return Results.Ok(new
            {
                ok = true,
                guild = new { guild.Id, guild.Name, guild.Description, guild.Level, guild.Members },
            });
        });
        g.MapGet("/{guildId}/contributions", async (string guildId, string? week, GuildService guilds, Clock clock, CancellationToken ct) =>
            Results.Ok(new { ok = true, contributions = GuildService.ContributionSummary(await guilds.GetAsync(guildId, ct), week ?? clock.WeekKey()) }));
        g.MapPost("/{guildId}/join", async (HttpContext http, string guildId, GuildService guilds, CancellationToken ct) =>
            Results.Ok(new { ok = true, guild = await guilds.JoinAsync(http.PlayerId(), guildId, ct) }));
        g.MapPost("/leave", async (HttpContext http, GuildService guilds, CancellationToken ct) =>
        {
            await guilds.LeaveAsync(http.PlayerId(), ct);
            return Results.Ok(new { ok = true });
        });
        g.MapPost("/kick", async (HttpContext http, MemberRequest req, GuildService guilds, CancellationToken ct) =>
            Results.Ok(new { ok = true, guild = await guilds.KickAsync(http.PlayerId(), req.PlayerId ?? "", ct) }));
        g.MapPost("/promote", async (HttpContext http, MemberRequest req, GuildService guilds, CancellationToken ct) =>
            Results.Ok(new { ok = true, guild = await guilds.SetRoleAsync(http.PlayerId(), req.PlayerId ?? "", req.Role ?? GuildRoles.Officer, ct) }));
        g.MapGet("/chat", async (HttpContext http, long? since, GuildService guilds, CancellationToken ct) =>
            Results.Ok(new { ok = true, messages = await guilds.ChatAsync(http.PlayerId(), since ?? 0, ct) }));
        g.MapPost("/chat", async (HttpContext http, ChatRequest req, GuildService guilds, CancellationToken ct) =>
            Results.Ok(new { ok = true, message = await guilds.PostChatAsync(http.PlayerId(), req.Text ?? "", ct) }));
        g.MapPost("/chat/{messageId}/report", async (HttpContext http, string messageId, ReportRequest? req, GuildService guilds, CancellationToken ct) =>
        {
            var report = await guilds.ReportAsync(http.PlayerId(), messageId, req?.Reason, ct);
            return Results.Ok(new { ok = true, reportId = report.Id });
        });
    }

    public static void MapInvasions(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/invasions");
        g.MapGet("", async (InvasionService inv, CancellationToken ct) => Results.Ok(new { ok = true, invasions = await inv.ActiveAsync(ct) }));
        g.MapPost("/{invasionId}/join", async (HttpContext http, string invasionId, InvasionService inv, CancellationToken ct) =>
        {
            var group = await inv.JoinAsync(http.PlayerId(), invasionId, ct);
            return Results.Ok(new { ok = true, groupId = group.Id, members = group.Members.Count });
        });
        g.MapPost("/{invasionId}/attack", async (HttpContext http, string invasionId, AttackRequest? req, InvasionService inv, CancellationToken ct) =>
            Results.Json(await inv.AttackAsync(http.PlayerId(), invasionId, req ?? new AttackRequest(null), ct), StateJson.Wire));
        g.MapGet("/{invasionId}/group", async (HttpContext http, string invasionId, InvasionService inv, CancellationToken ct) =>
            Results.Json(await inv.GroupStatusAsync(http.PlayerId(), invasionId, ct), StateJson.Wire));
    }

    public static void MapInternal(WebApplication app)
    {
        var g = app.MapGroup("/internal").AddEndpointFilter<JobAuthFilter>();
        g.MapPost("/jobs/{job}", async (string job, string? week, ServerHooks hooks, ArenaService arena, InvasionService invasions,
            ILoggerFactory lf, CancellationToken ct) =>
        {
            var log = lf.CreateLogger("Jobs");
            log.LogInformation("Job {Job} started", job);
            object result = job switch
            {
                // Daily resets themselves are lazy (rules settlers); the job delivers queued server mail.
                "daily-reset" => new { mailDelivered = await hooks.FlushOutboxesAsync(ct) },
                "weekly-arena" => await arena.RunWeeklyAsync(week, ct),
                "invasion-start" => await invasions.MaterialiseScheduledAsync(ct),
                "invasion-resolve" => await invasions.ResolveEndedAsync(ct),
                _ => throw ApiException.NotFound("unknown_job"),
            };
            log.LogInformation("Job {Job} finished", job);
            return Results.Json(new { ok = true, job, result }, StateJson.Wire);
        });
        g.MapPost("/invasions", async (CreateInvasionRequest req, InvasionService inv, CancellationToken ct) =>
            Results.Ok(new { ok = true, invasion = await inv.CreateAsync(req, ct) }));
    }
}
