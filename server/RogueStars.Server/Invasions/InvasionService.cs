using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RogueStars.Server.Arena;
using RogueStars.Server.Commands;
using RogueStars.Server.Guilds;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Storage;

namespace RogueStars.Server.Invasions;

public static class InvasionOutcomes
{
    public const string Repelled = "repelled";
    public const string Overrun = "overrun";
}

/// <summary>One invasion instance (scheduled weekly or surprise), created by live ops via /internal/invasions.</summary>
public sealed class Invasion
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Content invasion definition id (rules LiveOps); defaults to Id.</summary>
    public string TypeId { get; set; } = "";
    public long StartsMs { get; set; }
    public long EndsMs { get; set; }
    /// <summary>Invader HP per group member; the group repels the invasion when total damage reaches HpPerMember × members.</summary>
    public long HpPerMember { get; set; } = 10_000;
    public long InvaderPower { get; set; } = 1_000;
    public bool Resolved { get; set; }
    public long ResolvedMs { get; set; }
}

public sealed class InvasionMember
{
    public string PlayerId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long Damage { get; set; }
    public int Attacks { get; set; }
    public long JoinedMs { get; set; }
    /// <summary>Outcome command applied to this member (resolution is resumable).</summary>
    public bool Applied { get; set; }
}

/// <summary>A group of ≈20 players fighting one invasion instance together.</summary>
public sealed class InvasionGroup : IMemberGroup
{
    public string Id { get; set; } = "";
    public string InvasionId { get; set; } = "";
    public List<InvasionMember> Members { get; set; } = new();
    public string? Outcome { get; set; }

    [JsonIgnore] public int MemberCount => Members.Count;
    public bool HasMember(string playerId) => Members.Any(m => m.PlayerId == playerId);
    public InvasionMember? Member(string playerId) => Members.FirstOrDefault(m => m.PlayerId == playerId);
    public long TotalDamage => Members.Sum(m => m.Damage);
    public long Hp(Invasion inv) => inv.HpPerMember * Math.Max(1, Members.Count);
}

public sealed class InvasionParticipant
{
    public string InvasionId { get; set; } = "";
    public string GroupId { get; set; } = "";
}

public sealed record CreateInvasionRequest(string? Id, string? Name, long StartsMs, long EndsMs, long? HpPerMember, long? InvaderPower, string? TypeId = null);
public sealed record AttackRequest(string? RequestId);

public sealed class InvasionService(IDocStore docs, PlayerService players, ServerHooks hooks, IBattleResolver resolver,
    GuildService guilds, Clock clock, IOptions<ServerOptions> options, ILogger<InvasionService> log)
{
    private InvasionOptions O => options.Value.Invasions;

    private static string ParticipantId(string invasionId, string playerId) => invasionId + "_" + playerId;

    public async Task<Invasion> CreateAsync(CreateInvasionRequest req, CancellationToken ct)
    {
        if (req.EndsMs <= req.StartsMs) throw ApiException.BadRequest("invalid_window");
        var id = string.IsNullOrWhiteSpace(req.Id) ? Ids.New(6) : req.Id.Trim();
        if (id.Contains('/') || id.Contains('_') || id.Length > 64) throw ApiException.BadRequest("invalid_id");
        var inv = new Invasion
        {
            Id = id, Name = req.Name ?? id, TypeId = req.TypeId?.Trim() ?? "", StartsMs = req.StartsMs, EndsMs = req.EndsMs,
            HpPerMember = req.HpPerMember ?? 10_000, InvaderPower = req.InvaderPower ?? 1_000,
        };
        if (!await docs.TryPutAsync(Collections.Invasions, id, inv, 0, ct)) throw ApiException.Conflict("invasion_exists");
        log.LogInformation("Invasion {InvasionId} created ({Starts}..{Ends})", id, inv.StartsMs, inv.EndsMs);
        return inv;
    }

    public async Task<Invasion> GetAsync(string id, CancellationToken ct) =>
        (await docs.GetAsync<Invasion>(Collections.Invasions, id, ct))?.Value ?? throw ApiException.NotFound("invasion_not_found");

    public async Task<IReadOnlyList<Invasion>> ActiveAsync(CancellationToken ct)
    {
        long now = clock.NowMs;
        return (await docs.ListAsync<Invasion>(Collections.Invasions, null, 500, ct))
            .Select(d => d.Value).Where(i => !i.Resolved && i.StartsMs <= now && now < i.EndsMs).ToList();
    }

    public async Task<InvasionGroup> JoinAsync(string playerId, string invasionId, CancellationToken ct)
    {
        var inv = await GetAsync(invasionId, ct);
        long now = clock.NowMs;
        if (inv.Resolved || now < inv.StartsMs || now >= inv.EndsMs) throw ApiException.Conflict("invasion_not_active");

        var existing = await docs.GetAsync<InvasionParticipant>(Collections.InvasionPlayers, ParticipantId(invasionId, playerId), ct);
        string groupId = existing?.Value.GroupId ?? "";
        if (existing == null)
        {
            var state = await players.SettledAsync(playerId, ct);
            groupId = await GroupAssigner.AssignAsync<InvasionGroup>(docs, Collections.InvasionCursors, invasionId, Collections.InvasionGroups,
                invasionId, O.GroupSize, id => new InvasionGroup { Id = id, InvasionId = invasionId },
                g => g.Members.Add(new InvasionMember { PlayerId = playerId, DisplayName = state.DisplayName ?? playerId, JoinedMs = now }),
                playerId, ct);
            await docs.TryPutAsync(Collections.InvasionPlayers, ParticipantId(invasionId, playerId),
                new InvasionParticipant { InvasionId = invasionId, GroupId = groupId }, 0, ct);
        }
        return (await docs.GetAsync<InvasionGroup>(Collections.InvasionGroups, groupId, ct))!.Value;
    }

    public async Task<object> AttackAsync(string playerId, string invasionId, AttackRequest req, CancellationToken ct)
    {
        var inv = await GetAsync(invasionId, ct);
        var group = await JoinAsync(playerId, invasionId, ct);
        var loadout = await players.LoadoutAsync(playerId, ct);
        long seed = Ids.NewSeed();
        var outcome = resolver.ResolveInvasionAttack(loadout, invasionId, inv.InvaderPower, seed);
        string attackId = req.RequestId ?? Ids.New();

        CommandResponse? hook = null;
        if (hooks.Has(hooks.Names.InvasionAttack))
        {
            hook = await hooks.TryExecuteAsync(playerId, hooks.Names.InvasionAttack,
                new
                {
                    OccurrenceId = invasionId, Damage = outcome.Damage, Seed = seed, AttackId = attackId,
                    InvasionId = string.IsNullOrEmpty(inv.TypeId) ? inv.Id : inv.TypeId, StartMs = inv.StartsMs, EndMs = inv.EndsMs,
                }, "inv-attack-" + attackId, ct);
            if (hook is { Ok: false }) throw new ApiException(StatusCodes.Status409Conflict, hook.Error ?? "attack_rejected");
            if (hook is { Replayed: true }) return new { ok = true, attackId, replayed = true, state = hook.State };
        }

        var updated = await docs.MutateAsync<InvasionGroup>(Collections.InvasionGroups, group.Id, g =>
        {
            var m = g?.Member(playerId);
            if (g == null || m == null) return null;
            m.Damage += outcome.Damage;
            m.Attacks++;
            return g;
        }, ct);
        await guilds.AddInvasionContributionAsync(playerId, outcome.Damage, ct);

        return new
        {
            ok = true,
            attackId,
            damage = outcome.Damage,
            seed,
            log = outcome.Log,
            groupDamage = updated!.TotalDamage,
            groupHp = updated.Hp(inv),
            state = hook?.State,
        };
    }

    public async Task<object> GroupStatusAsync(string playerId, string invasionId, CancellationToken ct)
    {
        var inv = await GetAsync(invasionId, ct);
        var p = await docs.GetAsync<InvasionParticipant>(Collections.InvasionPlayers, ParticipantId(invasionId, playerId), ct)
                ?? throw ApiException.NotFound("not_joined");
        var g = (await docs.GetAsync<InvasionGroup>(Collections.InvasionGroups, p.Value.GroupId, ct))!.Value;
        return new
        {
            ok = true,
            invasion = inv,
            groupId = g.Id,
            outcome = g.Outcome,
            groupDamage = g.TotalDamage,
            groupHp = g.Hp(inv),
            members = g.Members.OrderByDescending(m => m.Damage).Select(m => new { m.PlayerId, m.DisplayName, m.Damage, m.Attacks }),
        };
    }

    // ---- resolution job --------------------------------------------------------------------------------

    public sealed record ResolveReport(string InvasionId, int Groups, int Applied, int Pending, bool Resolved);

    /// <summary>Resolves every ended invasion: group outcome → outcome command per member, top contributors mailed. Resumable.</summary>
    public async Task<List<ResolveReport>> ResolveEndedAsync(CancellationToken ct)
    {
        long now = clock.NowMs;
        var reports = new List<ResolveReport>();
        foreach (var doc in await docs.ListAsync<Invasion>(Collections.Invasions, null, 500, ct))
        {
            var inv = doc.Value;
            if (inv.Resolved || inv.EndsMs > now) continue;
            reports.Add(await ResolveAsync(inv, ct));
        }
        return reports;
    }

    private async Task<ResolveReport> ResolveAsync(Invasion inv, CancellationToken ct)
    {
        string outcomeCommand = hooks.Names.InvasionOutcome;
        bool canApply = hooks.Has(outcomeCommand);
        if (!canApply) log.LogWarning("Invasion {InvasionId}: outcome command {Command} not registered; outcomes stay pending", inv.Id, outcomeCommand);

        var groupIds = await GroupAssigner.GroupIdsAsync(docs, Collections.InvasionCursors, inv.Id, inv.Id, ct);
        int applied = 0, pending = 0;
        foreach (var groupId in groupIds)
        {
            var group = await docs.MutateAsync<InvasionGroup>(Collections.InvasionGroups, groupId, g =>
            {
                if (g == null || g.Outcome != null) return null;
                g.Outcome = g.TotalDamage >= g.Hp(inv) ? InvasionOutcomes.Repelled : InvasionOutcomes.Overrun;
                return g;
            }, ct);
            if (group == null) continue;

            var ranked = group.Members.OrderByDescending(m => m.Damage).ThenBy(m => m.JoinedMs).ToList();
            for (int i = 0; i < ranked.Count; i++)
            {
                var member = ranked[i];
                if (member.Applied) continue;
                if (!canApply) { pending++; continue; }
                int rank = i + 1;
                try
                {
                    var r = await hooks.TryExecuteAsync(member.PlayerId, outcomeCommand, new
                    {
                        InvasionId = string.IsNullOrEmpty(inv.TypeId) ? inv.Id : inv.TypeId, OccurrenceId = inv.Id,
                        Repelled = group.Outcome == InvasionOutcomes.Repelled, ContributionRank = rank,
                        Outcome = group.Outcome, Damage = member.Damage, Rank = rank, GroupSize = ranked.Count,
                        GroupDamage = group.TotalDamage, GroupHp = group.Hp(inv),
                    }, $"inv-outcome-{inv.Id}", ct);
                    if (r is not { Ok: true })
                        log.LogWarning("Invasion {InvasionId}: outcome for {PlayerId} rejected: {Error}", inv.Id, member.PlayerId, r?.Error);

                    if (rank <= O.TopContributors && member.Damage > 0)
                    {
                        await hooks.SendMailAsync(member.PlayerId, new MailArgs
                        {
                            Title = "Invasion top contributor",
                            Body = $"Rank {rank} in your group against {inv.Name}.",
                            Source = "invasion_top",
                            Rewards = TopRewards().Select(x => x.ToReward()).ToList(),
                        }, $"inv-top-{inv.Id}-{member.PlayerId}", ct);
                    }
                }
                catch (ApiException ex)
                {
                    log.LogWarning("Invasion {InvasionId}: applying to {PlayerId} failed: {Error}", inv.Id, member.PlayerId, ex.Error);
                }

                await docs.MutateAsync<InvasionGroup>(Collections.InvasionGroups, groupId, g =>
                {
                    var m = g?.Member(member.PlayerId);
                    if (g == null || m == null || m.Applied) return null;
                    m.Applied = true;
                    return g;
                }, ct);
                applied++;
            }
        }

        bool resolved = pending == 0;
        if (resolved)
        {
            await docs.MutateAsync<Invasion>(Collections.Invasions, inv.Id, i =>
            {
                if (i == null || i.Resolved) return null;
                i.Resolved = true;
                i.ResolvedMs = clock.NowMs;
                return i;
            }, ct);
        }
        log.LogInformation("Invasion {InvasionId}: {Groups} groups, {Applied} applied, {Pending} pending", inv.Id, groupIds.Count, applied, pending);
        return new ResolveReport(inv.Id, groupIds.Count, applied, pending, resolved);
    }

    private List<RewardSpec> TopRewards() =>
        O.TopRewards.Count > 0 ? O.TopRewards : new List<RewardSpec> { RewardSpec.Cur(RogueStars.Rules.Core.Currencies.Diamonds, 100) };
}
