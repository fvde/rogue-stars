using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RogueStars.Rules.Core;
using RogueStars.Server.Commands;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Arena;

public sealed class ArenaSnapshot
{
    public string PlayerId { get; set; } = "";
    public CombatLoadout Loadout { get; set; } = new();
    public long UpdatedMs { get; set; }
}

/// <summary>Per-player arena record: current league and this week's group.</summary>
public sealed class ArenaPlayer
{
    public string PlayerId { get; set; } = "";
    public int League { get; set; }
    public string? Week { get; set; }
    public string? GroupId { get; set; }
    /// <summary>Last week whose rewards/promotion were applied (makes the weekly job idempotent).</summary>
    public string? SettledWeek { get; set; }
}

public sealed class ArenaMember
{
    public string PlayerId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long Points { get; set; }
    public long Power { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public long JoinedMs { get; set; }
}

/// <summary>A weekly league group of up to 150 players.</summary>
public sealed class ArenaGroup : IMemberGroup
{
    public string Id { get; set; } = "";
    public string Week { get; set; } = "";
    public int League { get; set; }
    public List<ArenaMember> Members { get; set; } = new();

    [JsonIgnore] public int MemberCount => Members.Count;
    public bool HasMember(string playerId) => Members.Any(m => m.PlayerId == playerId);
    public ArenaMember? Member(string playerId) => Members.FirstOrDefault(m => m.PlayerId == playerId);

    public List<ArenaMember> Ranked() => Members
        .OrderByDescending(m => m.Points).ThenByDescending(m => m.Wins).ThenBy(m => m.JoinedMs).ThenBy(m => m.PlayerId, StringComparer.Ordinal)
        .ToList();
}

public sealed class ArenaJobMarker
{
    public List<string> DoneGroups { get; set; } = new();
    public bool Complete { get; set; }
}

public static class OpponentPicker
{
    /// <summary>Random member among the <paramref name="window"/> closest in points (excluding the player).</summary>
    public static ArenaMember? Pick(IReadOnlyList<ArenaMember> members, string selfId, long selfPoints, int window, Rng rng)
    {
        var candidates = Candidates(members, selfId, selfPoints, window);
        return candidates.Count == 0 ? null : candidates[rng.Range(0, candidates.Count)];
    }

    public static List<ArenaMember> Candidates(IReadOnlyList<ArenaMember> members, string selfId, long selfPoints, int window) =>
        members.Where(m => m.PlayerId != selfId)
            .OrderBy(m => Math.Abs(m.Points - selfPoints)).ThenBy(m => m.PlayerId, StringComparer.Ordinal)
            .Take(Math.Max(1, window))
            .ToList();
}

public sealed record FightRequest(string? OpponentId, string? RequestId);

public sealed class ArenaService(IDocStore docs, PlayerService players, ServerHooks hooks, IBattleResolver resolver, Clock clock,
    IOptions<ServerOptions> options, ILogger<ArenaService> log)
{
    private ArenaOptions O => options.Value.Arena;

    public static string Bucket(string week, int league) => week + "_L" + league;

    public async Task<CombatLoadout> UploadSnapshotAsync(string playerId, CancellationToken ct)
    {
        var loadout = await players.LoadoutAsync(playerId, ct);
        var snap = new ArenaSnapshot { PlayerId = playerId, Loadout = loadout, UpdatedMs = clock.NowMs };
        await docs.MutateAsync<ArenaSnapshot>(Collections.ArenaSnapshots, playerId, _ => snap, ct);
        return loadout;
    }

    public async Task<ArenaPlayer?> CurrentEntryAsync(string playerId, CancellationToken ct)
    {
        var ap = await docs.GetAsync<ArenaPlayer>(Collections.ArenaPlayers, playerId, ct);
        return ap != null && ap.Value.Week == clock.WeekKey() ? ap.Value : null;
    }

    /// <summary>Assigns the player to a group of this week's league (first fight of the week).</summary>
    public async Task<ArenaPlayer> EnsureEntryAsync(string playerId, CombatLoadout loadout, CancellationToken ct)
    {
        string week = clock.WeekKey();
        var existing = await docs.GetAsync<ArenaPlayer>(Collections.ArenaPlayers, playerId, ct);
        if (existing != null && existing.Value.Week == week && existing.Value.GroupId != null) return existing.Value;

        int league = Math.Clamp(existing?.Value.League ?? 0, 0, O.MaxLeague);
        string bucket = Bucket(week, league);
        long now = clock.NowMs;
        string groupId = await GroupAssigner.AssignAsync<ArenaGroup>(docs, Collections.ArenaCursors, bucket, Collections.ArenaGroups, bucket,
            O.GroupSize, id => new ArenaGroup { Id = id, Week = week, League = league },
            g => g.Members.Add(new ArenaMember
            {
                PlayerId = playerId, DisplayName = loadout.DisplayName ?? playerId, Power = loadout.Power, JoinedMs = now,
            }),
            playerId, ct);

        var entry = await docs.MutateAsync<ArenaPlayer>(Collections.ArenaPlayers, playerId, ap =>
        {
            ap ??= new ArenaPlayer { PlayerId = playerId, League = league };
            ap.Week = week;
            ap.GroupId = groupId;
            ap.League = league;
            return ap;
        }, ct);
        log.LogInformation("Arena: {PlayerId} joined {GroupId}", playerId, groupId);
        return entry!;
    }

    public async Task<object> FightAsync(string playerId, FightRequest req, CancellationToken ct)
    {
        var loadout = await UploadSnapshotAsync(playerId, ct);
        var entry = await EnsureEntryAsync(playerId, loadout, ct);
        var group = (await docs.GetAsync<ArenaGroup>(Collections.ArenaGroups, entry.GroupId!, ct))?.Value
                    ?? throw ApiException.Conflict("group_missing");
        var me = group.Member(playerId) ?? throw ApiException.Conflict("group_missing");
        long seed = Ids.NewSeed();

        ArenaSnapshot defender;
        if (!string.IsNullOrEmpty(req.OpponentId))
        {
            if (req.OpponentId == playerId || !group.HasMember(req.OpponentId)) throw ApiException.BadRequest("invalid_opponent");
            defender = await SnapshotAsync(req.OpponentId, ct) ?? throw ApiException.NotFound("opponent_snapshot_missing");
        }
        else
        {
            defender = await PickDefenderAsync(group, me, loadout, Rng.FromSeed(seed ^ 0x5DEECE66DL), ct)
                       ?? throw ApiException.Conflict("no_opponents");
        }

        var outcome = resolver.ResolveDuel(loadout, defender.Loadout, seed);
        int delta = outcome.AttackerWon ? O.WinPoints : -O.LossPoints;
        string fightId = req.RequestId ?? Ids.New();

        CommandResponse? hook = null;
        if (hooks.Has(hooks.Names.ArenaFight))
        {
            hook = await hooks.TryExecuteAsync(playerId, hooks.Names.ArenaFight, new
            {
                OpponentId = defender.PlayerId, Won = outcome.AttackerWon, PointsDelta = delta, Seed = seed, Week = group.Week, FightId = fightId,
            }, "arena-fight-" + fightId, ct);
            if (hook is { Ok: false }) throw new ApiException(StatusCodes.Status409Conflict, hook.Error ?? "fight_rejected");
            if (hook is { Replayed: true }) return new { ok = true, fightId, replayed = true, payload = hook.Payload, state = hook.State };
        }

        long points = 0;
        await docs.MutateAsync<ArenaGroup>(Collections.ArenaGroups, group.Id, g =>
        {
            if (g == null) return null;
            var m = g.Member(playerId);
            if (m == null) return null;
            m.Points = Math.Max(0, m.Points + delta);
            m.Power = loadout.Power;
            m.DisplayName = loadout.DisplayName ?? m.DisplayName;
            if (outcome.AttackerWon) m.Wins++; else m.Losses++;
            points = m.Points;
            var d = g.Member(defender.PlayerId);
            if (d != null && outcome.AttackerWon) d.Points = Math.Max(0, d.Points - O.DefenderLossPoints);
            return g;
        }, ct);

        return new
        {
            ok = true,
            fightId,
            won = outcome.AttackerWon,
            pointsDelta = delta,
            points,
            seed,
            opponent = new { playerId = defender.PlayerId, displayName = defender.Loadout.DisplayName, power = defender.Loadout.Power },
            log = outcome.Log,
            state = hook?.State,
        };
    }

    private async Task<ArenaSnapshot?> PickDefenderAsync(ArenaGroup group, ArenaMember me, CombatLoadout loadout, Rng rng, CancellationToken ct)
    {
        var pick = OpponentPicker.Pick(group.Members, me.PlayerId, me.Points, O.OpponentWindow, rng);
        if (pick != null) return await SnapshotAsync(pick.PlayerId, ct);

        // Lonely group (start of the week): fall back to any stored snapshot close in power.
        var others = (await docs.ListAsync<ArenaSnapshot>(Collections.ArenaSnapshots, null, 100, ct))
            .Where(s => s.Id != me.PlayerId)
            .OrderBy(s => Math.Abs(s.Value.Loadout.Power - loadout.Power))
            .Take(Math.Max(1, O.OpponentWindow))
            .ToList();
        return others.Count == 0 ? null : others[rng.Range(0, others.Count)].Value;
    }

    private async Task<ArenaSnapshot?> SnapshotAsync(string playerId, CancellationToken ct) =>
        (await docs.GetAsync<ArenaSnapshot>(Collections.ArenaSnapshots, playerId, ct))?.Value;

    public async Task<object> OpponentsAsync(string playerId, CancellationToken ct)
    {
        var entry = await CurrentEntryAsync(playerId, ct);
        if (entry == null) return new { ok = true, joined = false, opponents = Array.Empty<object>() };
        var group = (await docs.GetAsync<ArenaGroup>(Collections.ArenaGroups, entry.GroupId!, ct))?.Value;
        var me = group?.Member(playerId);
        if (group == null || me == null) return new { ok = true, joined = false, opponents = Array.Empty<object>() };
        var list = OpponentPicker.Candidates(group.Members, playerId, me.Points, O.OpponentWindow)
            .Select(m => new { m.PlayerId, m.DisplayName, m.Points, m.Power });
        return new { ok = true, joined = true, opponents = list };
    }

    public async Task<object> LeaderboardAsync(string playerId, CancellationToken ct)
    {
        string week = clock.WeekKey();
        var entry = await CurrentEntryAsync(playerId, ct);
        if (entry == null)
        {
            var ap = await docs.GetAsync<ArenaPlayer>(Collections.ArenaPlayers, playerId, ct);
            return new { ok = true, week, joined = false, league = ap?.Value.League ?? 0, groupSize = O.GroupSize, entries = Array.Empty<object>() };
        }
        var group = (await docs.GetAsync<ArenaGroup>(Collections.ArenaGroups, entry.GroupId!, ct))?.Value ?? new ArenaGroup();
        var ranked = group.Ranked();
        return new
        {
            ok = true,
            week,
            joined = true,
            league = entry.League,
            groupId = group.Id,
            groupSize = O.GroupSize,
            myRank = ranked.FindIndex(m => m.PlayerId == playerId) + 1,
            entries = ranked.Select((m, i) => new { rank = i + 1, m.PlayerId, m.DisplayName, m.Points, m.Power, m.Wins, m.Losses }),
        };
    }

    // ---- weekly job ------------------------------------------------------------------------------------

    public sealed record WeeklyReport(string Week, int Groups, int Players, int Mailed, int Promoted, int Demoted);

    /// <summary>Distributes rank rewards (via mail) and applies promotion/demotion for a finished week. Idempotent.</summary>
    public async Task<WeeklyReport> RunWeeklyAsync(string? week, CancellationToken ct)
    {
        week ??= Clock.PreviousWeek(clock.Now.UtcDateTime);
        int groups = 0, playersCount = 0, mailed = 0, promoted = 0, demoted = 0;
        for (int league = 0; league <= O.MaxLeague; league++)
        {
            string bucket = Bucket(week, league);
            foreach (var groupId in await GroupAssigner.GroupIdsAsync(docs, Collections.ArenaCursors, bucket, bucket, ct))
            {
                var marker = await docs.GetAsync<ArenaJobMarker>(Collections.ArenaJobs, week, ct);
                if (marker != null && marker.Value.DoneGroups.Contains(groupId)) continue;
                var group = (await docs.GetAsync<ArenaGroup>(Collections.ArenaGroups, groupId, ct))?.Value;
                if (group == null) continue;
                groups++;

                var ranked = group.Ranked();
                for (int i = 0; i < ranked.Count; i++)
                {
                    int rank = i + 1;
                    var member = ranked[i];
                    playersCount++;
                    var tier = RankRewards().FirstOrDefault(r => rank <= r.MaxRank);
                    if (tier != null)
                    {
                        double scale = 1 + 0.5 * league;
                        var mail = new MailArgs
                        {
                            Title = "Arena week " + week,
                            Body = $"You finished rank {rank} in league {league}.",
                            Source = "arena_weekly",
                            Rewards = tier.Rewards.Select(r => r.ToReward(scale)).ToList(),
                        };
                        try
                        {
                            await hooks.SendMailAsync(member.PlayerId, mail, $"arena-{week}-{member.PlayerId}", ct);
                            mailed++;
                        }
                        catch (ApiException ex)
                        {
                            log.LogWarning("Arena reward mail for {PlayerId} failed: {Error}", member.PlayerId, ex.Error);
                        }
                    }

                    int next = league;
                    if (ranked.Count > O.PromoteTop && rank <= O.PromoteTop) next = Math.Min(O.MaxLeague, league + 1);
                    else if (ranked.Count > O.PromoteTop + O.DemoteBottom && rank > ranked.Count - O.DemoteBottom) next = Math.Max(0, league - 1);
                    if (next > league) promoted++;
                    if (next < league) demoted++;
                    await docs.MutateAsync<ArenaPlayer>(Collections.ArenaPlayers, member.PlayerId, ap =>
                    {
                        ap ??= new ArenaPlayer { PlayerId = member.PlayerId };
                        if (ap.SettledWeek == week) return null;
                        ap.SettledWeek = week;
                        // A player who already joined the new week keeps that week's assignment; the league applies next time.
                        ap.League = next;
                        return ap;
                    }, ct);
                }

                await docs.MutateAsync<ArenaJobMarker>(Collections.ArenaJobs, week, m =>
                {
                    m ??= new ArenaJobMarker();
                    if (!m.DoneGroups.Contains(groupId)) m.DoneGroups.Add(groupId);
                    return m;
                }, ct);
            }
        }
        await docs.MutateAsync<ArenaJobMarker>(Collections.ArenaJobs, week, m =>
        {
            m ??= new ArenaJobMarker();
            m.Complete = true;
            return m;
        }, ct);
        log.LogInformation("Arena week {Week} settled: {Groups} groups, {Players} players", week, groups, playersCount);
        return new WeeklyReport(week, groups, playersCount, mailed, promoted, demoted);
    }

    private List<RankReward> RankRewards() =>
        O.RankRewards.Count > 0 ? O.RankRewards.OrderBy(r => r.MaxRank).ToList() : ArenaOptions.DefaultRankRewards();
}
