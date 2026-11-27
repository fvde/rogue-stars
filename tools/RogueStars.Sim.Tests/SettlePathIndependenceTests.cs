using System.Text.Json;
using System.Text.Json.Nodes;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using RogueStars.Sim;

namespace RogueStars.Sim.Tests;

/// <summary>
/// Settling is path-independent (docs/server.md → Local-first commands): settling a state at t1 and then at t2 gives exactly
/// the state of settling it once at t2 (same <see cref="StateHash"/>), for any number of intermediate settles. The server
/// returns settled states from /v1/session and /v1/state without saving them and the client settles locally, so both sides
/// reach the same state for a command at atMs however often each of them settled before.
/// </summary>
public class SettlePathIndependenceTests
{
    private const long Min = 60_000, Hour = 60 * Min, Day = 24 * Hour;
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) { IncludeFields = true };
    private static readonly JsonSerializerOptions Storage = new() { IncludeFields = true };

    private static GameState Clone(GameState s) => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(s, Storage), Storage)!;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RogueStars.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    /// <summary>A 60-day account as the server stored it (wire format).</summary>
    public static GameState State60() => JsonSerializer.Deserialize<GameState>(
        File.ReadAllText(Path.Combine(RepoRoot(), "tools", "RogueStars.Sim.Tests", "Fixtures", "state60.json")), Wire)!;

    /// <summary>When the 60-day state was saved (settled by /v1/state).</summary>
    private const long State60SavedMs = 1_795_776_642_741;

    private static GameEngine ServerEngine()
    {
        var c = new ContentDb();
        c.Shop.AdsDisabled = true; // the server's current mode (ServerContent)
        return new GameEngine(c) { Cloner = Clone };
    }

    /// <summary>The states checked: the 60-day account, a fresh player and simulator accounts at several ages.</summary>
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "state60" };
        yield return new object[] { "new" };
        yield return new object[] { "new-unlocked" };
        foreach (var p in new[] { "Casual", "Active", "Hardcore" })
            foreach (var d in new[] { 1, 3, 8, 20 })
                yield return new object[] { p + ":" + d };
    }

    private static (GameEngine E, GameState S, long T0) Load(string name)
    {
        if (name == "state60") { var s = State60(); return (ServerEngine(), s, State60SavedMs); }
        if (name is "new" or "new-unlocked")
        {
            var e = ServerEngine();
            if (name == "new-unlocked") e.RegisterNewPlayer(ctx => ctx.State.Flags.Add(Features.DevUnlockAllFlag)); // as the dev server
            return (e, e.NewPlayer("p", 1_800_000_000_000, 7), 1_800_000_000_000);
        }
        var parts = name.Split(':');
        var p = new SimPlayer(TimeProfile.ByName(parts[0]), SpendProfile.Dolphin, int.Parse(parts[1]) * 31 + 5);
        for (int d = 0; d < int.Parse(parts[1]); d++) p.PlayDay(d);
        var engine = new GameEngine(p.C) { Cloner = Clone };
        return (engine, p.S, Math.Max(p.Now, p.S.LastCommandMs)); // the bot settles between commands too
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SettlingInSteps_EqualsSettlingOnce(string name)
    {
        var (e, baseState, t0) = Load(name);
        var rnd = new Random(Seed(name) ^ 12345);
        var failures = new List<string>();
        long[] horizons = { 2 * Min, Hour, Day, 4 * Day, 21 * Day, 75 * Day };
        for (int i = 0; i < 120 && failures.Count < 3; i++)
        {
            long horizon = horizons[i % horizons.Length];
            long end = t0 + 1 + (long)(rnd.NextDouble() * horizon);
            int splits = 1 + rnd.Next(4);
            var times = new List<long>();
            for (int k = 0; k < splits; k++) times.Add(t0 + (long)(rnd.NextDouble() * (end - t0)));
            times.Sort();
            times.Add(end);

            var stepped = Clone(baseState);
            foreach (var t in times) e.Settle(stepped, t);
            var once = Clone(baseState);
            e.Settle(once, end);
            if (StateHash.Of(stepped) == StateHash.Of(once)) continue;
            failures.Add($"settles at +[{string.Join(", ", times.Select(t => t - t0))}] ms:\n  " + string.Join("\n  ", Diff(stepped, once)));
        }
        Assert.True(failures.Count == 0, name + ":\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The local-first flow: the server stores only command results; the client adopts a settled copy (session / state),
    /// settles it locally whenever it likes (refresh) and runs commands on it. After every command both states are identical.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Commands_AfterAnyLocalSettles_MatchTheServersState(string name)
    {
        var (e, baseState, t0) = Load(name);
        var rnd = new Random(Seed(name) ^ 999);
        var server = Clone(baseState);
        var client = Clone(baseState);
        e.Settle(client, t0 + Min); // /v1/session: settled, not saved
        long now = t0 + Min;
        GameCommand[] Commands() => new GameCommand[]
        {
            new CityCollect(), new MailClaimAll(), new QuestClaimAll(), new LoginClaim(), new CityPop { All = true },
            new SummonPassClaimAll { EventType = SummonEventType.Mount }, new SummonTaskClaimAll { EventType = SummonEventType.Hero },
            new LobbyPopupShown { PopupId = "p" },
        };
        int ok = 0;
        for (int i = 0; i < 24; i++)
        {
            long gap = rnd.Next(4) switch { 0 => (long)(rnd.NextDouble() * Min), 1 => (long)(rnd.NextDouble() * Hour), 2 => (long)(rnd.NextDouble() * Day), _ => (long)(rnd.NextDouble() * 5 * Day) };
            long at = now + 1 + gap;
            for (int k = rnd.Next(3); k > 0; k--) e.Settle(client, now + (long)(rnd.NextDouble() * (at - now))); // 30 s refreshes
            var cmds = Commands();
            int pick = rnd.Next(cmds.Length);
            var rc = e.Execute(ref client, cmds[pick], at);
            var rs = e.Execute(ref server, Commands()[pick], at);
            Assert.Equal(rs.Ok, rc.Ok);
            now = at; // time moves on (the client runs commands at or after its last settle)
            if (!rc.Ok) continue; // rejected locally: never sent (the client's copy stays settled, the server's untouched)
            ok++;
            Assert.True(StateHash.Of(server) == StateHash.Of(client),
                $"{name}: command {i} ({cmds[pick].Type}) at +{at - t0} ms:\n" + string.Join("\n", Diff(client, server)));
        }
        Assert.True(ok >= 4, name + ": too few accepted commands (" + ok + ")");
    }

    private static int Seed(string name)
    {
        int h = 17;
        foreach (char ch in name) h = h * 31 + ch;
        return h & 0x7fffffff;
    }

    private static List<string> Diff(GameState a, GameState b)
    {
        var d = new List<string>();
        Diff(JsonSerializer.SerializeToNode(a, Wire), JsonSerializer.SerializeToNode(b, Wire), "", d);
        return d;
    }

    private static void Diff(JsonNode? a, JsonNode? b, string path, List<string> outp)
    {
        if (outp.Count > 20) return;
        if (a is JsonObject oa && b is JsonObject ob)
        {
            foreach (var k in oa.Select(p => p.Key).Union(ob.Select(p => p.Key))) Diff(oa[k], ob[k], path + "." + k, outp);
            return;
        }
        if (a is JsonArray aa && b is JsonArray ab && aa.Count == ab.Count)
        {
            for (int i = 0; i < aa.Count; i++) Diff(aa[i], ab[i], path + "[" + i + "]", outp);
            return;
        }
        string sa = a?.ToJsonString() ?? "null", sb = b?.ToJsonString() ?? "null";
        if (sa != sb) outp.Add(path + ": " + Cut(sa) + " vs " + Cut(sb));
    }

    private static string Cut(string s) => s.Length > 240 ? s[..240] + "…" : s;
}
