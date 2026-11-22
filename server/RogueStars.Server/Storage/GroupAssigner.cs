using RogueStars.Server.Infrastructure;

namespace RogueStars.Server.Storage;

/// <summary>A group document with a bounded member list (arena league group, invasion group).</summary>
public interface IMemberGroup
{
    int MemberCount { get; }
    bool HasMember(string playerId);
}

/// <summary>Points at the group currently being filled for one bucket (week+league, invasion).</summary>
public sealed class GroupCursor
{
    public int Current { get; set; }
}

/// <summary>
/// Fills groups sequentially up to a capacity using two compare-and-set documents: a cursor naming the open group,
/// and the group itself (whose member count is the source of truth, so a group never exceeds the capacity).
/// </summary>
public static class GroupAssigner
{
    public static string GroupId(string prefix, int index) => prefix + "_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static async Task<string> AssignAsync<TGroup>(IDocStore store, string cursorCollection, string cursorId,
        string groupCollection, string groupPrefix, int capacity, Func<string, TGroup> create, Action<TGroup> addMember,
        string playerId, CancellationToken ct) where TGroup : class, IMemberGroup
    {
        for (int attempt = 0; attempt < 64; attempt++)
        {
            var cursorDoc = await store.GetAsync<GroupCursor>(cursorCollection, cursorId, ct);
            int index = cursorDoc?.Value.Current ?? 0;
            string groupId = GroupId(groupPrefix, index);
            bool member = false, full = false;
            await store.MutateAsync<TGroup>(groupCollection, groupId, g =>
            {
                member = false;
                full = false;
                g ??= create(groupId);
                if (g.HasMember(playerId)) { member = true; return null; }
                if (g.MemberCount >= capacity) { full = true; return null; }
                addMember(g);
                member = true;
                return g;
            }, ct);
            if (member)
            {
                if (cursorDoc == null) await store.TryPutAsync(cursorCollection, cursorId, new GroupCursor { Current = index }, 0, ct);
                return groupId;
            }
            if (full)
                await store.TryPutAsync(cursorCollection, cursorId, new GroupCursor { Current = index + 1 }, cursorDoc?.Version ?? 0, ct);
        }
        throw ApiException.Conflict("contention");
    }

    /// <summary>All group ids created so far for a bucket.</summary>
    public static async Task<List<string>> GroupIdsAsync(IDocStore store, string cursorCollection, string cursorId, string groupPrefix,
        CancellationToken ct)
    {
        var cursor = await store.GetAsync<GroupCursor>(cursorCollection, cursorId, ct);
        if (cursor == null) return new();
        return Enumerable.Range(0, cursor.Value.Current + 1).Select(i => GroupId(groupPrefix, i)).ToList();
    }
}
